//! In-app updater backed by the releases of the Portal repository on Forgejo.
//!
//! The flow is deliberately small: ask Forgejo for the latest release, compare
//! its tag with the version compiled into this binary, download `portal.exe`
//! next to the running executable, swap the two files by renaming, and
//! relaunch. Windows lets a running executable be renamed but not overwritten,
//! which is what makes the swap possible without a separate installer.

use anyhow::{Context, Result, bail};
use serde::Deserialize;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use tokio::io::AsyncWriteExt;

/// Only this host may serve release metadata and binaries.
const RELEASE_HOST: &str = "git.ossalali.com";
const LATEST_RELEASE_API: &str = "https://git.ossalali.com/api/v1/repos/oss/Portal/releases/latest";
const ASSET_NAME: &str = "portal.exe";
/// Applies to the whole metadata request, which is small.
const API_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(30);
/// A download gets no overall deadline; it is cut off only when it stalls.
const STALL_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(60);

pub const CURRENT_VERSION: &str = env!("CARGO_PKG_VERSION");

/// A stable release that is newer than the running binary.
#[derive(Clone, Debug, PartialEq)]
pub struct ReleaseInfo {
    pub version: String,
    pub page_url: String,
    pub download_url: String,
    pub size: u64,
}

#[derive(Clone, Debug)]
pub enum UpdateStatus {
    Idle,
    Checking,
    UpToDate,
    Available(ReleaseInfo),
    Downloading {
        info: ReleaseInfo,
        received: u64,
        total: u64,
    },
    /// The new binary is already in place on disk; a restart runs it.
    ReadyToRestart {
        version: String,
    },
    Error(String),
}

pub type SharedStatus = Arc<Mutex<UpdateStatus>>;

/// Everything the UI needs to drive updates. Lives on the app for the whole run.
pub struct Updater {
    pub status: SharedStatus,
    /// Captured before any rename so a relaunch always targets the real path.
    exe_path: Option<PathBuf>,
    pub restart_requested: bool,
}

impl Updater {
    pub fn new() -> Self {
        let exe_path = std::env::current_exe().ok();
        if let Some(exe) = &exe_path {
            cleanup_previous(exe);
        }
        Self {
            status: Arc::new(Mutex::new(UpdateStatus::Idle)),
            exe_path,
            restart_requested: false,
        }
    }

    pub fn status(&self) -> UpdateStatus {
        self.status
            .lock()
            .map(|s| s.clone())
            .unwrap_or(UpdateStatus::Idle)
    }

    pub fn is_busy(&self) -> bool {
        matches!(
            self.status(),
            UpdateStatus::Checking | UpdateStatus::Downloading { .. }
        )
    }

    /// Query Forgejo in the background; the result lands in `status`.
    pub fn check(&self, runtime: &tokio::runtime::Runtime, ctx: &eframe::egui::Context) {
        if self.is_busy() {
            return;
        }
        set_status(&self.status, UpdateStatus::Checking);
        let status = self.status.clone();
        let ctx = ctx.clone();
        runtime.spawn(async move {
            let outcome = match fetch_latest().await {
                Ok(Some(info)) => UpdateStatus::Available(info),
                Ok(None) => UpdateStatus::UpToDate,
                Err(e) => UpdateStatus::Error(format!("Update check failed: {e:#}")),
            };
            set_status(&status, outcome);
            ctx.request_repaint();
        });
    }

    /// Download the release and swap it into place; the result lands in `status`.
    pub fn download_and_install(
        &self,
        runtime: &tokio::runtime::Runtime,
        ctx: &eframe::egui::Context,
        info: ReleaseInfo,
    ) {
        if self.is_busy() {
            return;
        }
        let Some(exe_path) = self.exe_path.clone() else {
            set_status(
                &self.status,
                UpdateStatus::Error("Cannot locate the running executable".to_string()),
            );
            return;
        };
        set_status(
            &self.status,
            UpdateStatus::Downloading {
                info: info.clone(),
                received: 0,
                total: info.size,
            },
        );
        let status = self.status.clone();
        let ctx = ctx.clone();
        runtime.spawn(async move {
            let progress_status = status.clone();
            let progress_ctx = ctx.clone();
            let progress_info = info.clone();
            let on_progress = move |received: u64| {
                set_status(
                    &progress_status,
                    UpdateStatus::Downloading {
                        info: progress_info.clone(),
                        received,
                        total: progress_info.size,
                    },
                );
                progress_ctx.request_repaint();
            };
            let outcome = match download(&info, &exe_path, on_progress).await {
                Ok(new_file) => match install(&exe_path, &new_file) {
                    Ok(()) => UpdateStatus::ReadyToRestart {
                        version: info.version.clone(),
                    },
                    Err(e) => UpdateStatus::Error(format!("Install failed: {e:#}")),
                },
                Err(e) => UpdateStatus::Error(format!("Download failed: {e:#}")),
            };
            set_status(&status, outcome);
            ctx.request_repaint();
        });
    }

    pub fn set_error(&self, message: String) {
        set_status(&self.status, UpdateStatus::Error(message));
    }

    /// Start the (already swapped) executable again. Called on the way out.
    pub fn relaunch(&self) -> Result<()> {
        let exe = self.exe_path.as_ref().context("executable path unknown")?;
        std::process::Command::new(exe)
            .args(std::env::args_os().skip(1))
            .spawn()
            .with_context(|| format!("failed to start {}", exe.display()))?;
        Ok(())
    }
}

fn set_status(status: &SharedStatus, value: UpdateStatus) {
    if let Ok(mut guard) = status.lock() {
        *guard = value;
    }
}

// ── Version handling ───────────────────────────────────────────────────

fn parse_version(text: &str) -> Option<(u64, u64, u64)> {
    let text = text.strip_prefix('v').unwrap_or(text);
    let mut parts = text.split('.');
    let major = parts.next()?.parse().ok()?;
    let minor = parts.next()?.parse().ok()?;
    let patch = parts.next()?.parse().ok()?;
    if parts.next().is_some() {
        return None;
    }
    Some((major, minor, patch))
}

pub fn is_newer(candidate: &str, current: &str) -> bool {
    match (parse_version(candidate), parse_version(current)) {
        (Some(c), Some(cur)) => c > cur,
        _ => false,
    }
}

// ── Forgejo client ─────────────────────────────────────────────────────

#[derive(Deserialize)]
struct Release {
    tag_name: String,
    html_url: String,
    #[serde(default)]
    draft: bool,
    #[serde(default)]
    prerelease: bool,
    #[serde(default)]
    assets: Vec<Asset>,
}

#[derive(Deserialize)]
struct Asset {
    name: String,
    browser_download_url: String,
    size: u64,
}

fn client_builder() -> reqwest::ClientBuilder {
    reqwest::Client::builder()
        .user_agent(format!("Portal/{CURRENT_VERSION}"))
        .connect_timeout(API_TIMEOUT)
}

fn http_client() -> Result<reqwest::Client> {
    client_builder()
        .timeout(API_TIMEOUT)
        .build()
        .context("failed to build HTTP client")
}

/// A client for the binary itself. A total timeout would abort a large download
/// on a slow line, so only a stalled transfer ends it.
fn download_client() -> Result<reqwest::Client> {
    client_builder()
        .read_timeout(STALL_TIMEOUT)
        .build()
        .context("failed to build HTTP client")
}

fn is_release_host(url: &str) -> bool {
    match reqwest::Url::parse(url) {
        Ok(u) => u.scheme() == "https" && u.host_str() == Some(RELEASE_HOST),
        Err(_) => false,
    }
}

/// The latest stable release if it is newer than this binary, otherwise `None`.
async fn fetch_latest() -> Result<Option<ReleaseInfo>> {
    let client = http_client()?;
    let response = client
        .get(LATEST_RELEASE_API)
        .send()
        .await
        .context("request failed")?;
    if response.status() == reqwest::StatusCode::NOT_FOUND {
        // Nothing published yet, which is not an error.
        return Ok(None);
    }
    let release: Release = response
        .error_for_status()
        .context("Forgejo rejected the request")?
        .json()
        .await
        .context("unexpected release feed")?;
    select_update(&release, CURRENT_VERSION)
}

/// Decide whether a release should be offered. Kept free of I/O so the rules
/// can be tested on their own.
fn select_update(release: &Release, current: &str) -> Result<Option<ReleaseInfo>> {
    if release.draft || release.prerelease {
        return Ok(None);
    }
    let version = release
        .tag_name
        .strip_prefix('v')
        .unwrap_or(&release.tag_name)
        .to_string();
    if parse_version(&version).is_none() {
        bail!("release tag '{}' is not X.Y.Z", release.tag_name);
    }
    if !is_newer(&version, current) {
        return Ok(None);
    }
    let asset = release
        .assets
        .iter()
        .find(|a| a.name == ASSET_NAME)
        .with_context(|| format!("release {version} has no {ASSET_NAME} asset"))?;
    if asset.size == 0 {
        bail!("release {version} reports an empty {ASSET_NAME}");
    }
    if !is_release_host(&asset.browser_download_url) || !is_release_host(&release.html_url) {
        bail!("release {version} points outside {RELEASE_HOST}");
    }
    Ok(Some(ReleaseInfo {
        version,
        page_url: release.html_url.clone(),
        download_url: asset.browser_download_url.clone(),
        size: asset.size,
    }))
}

// ── Download and swap ──────────────────────────────────────────────────

fn sibling(exe: &Path, suffix: &str) -> PathBuf {
    let mut name = exe
        .file_name()
        .map(|n| n.to_os_string())
        .unwrap_or_else(|| "portal.exe".into());
    name.push(suffix);
    exe.with_file_name(name)
}

/// Stream the asset to `<exe>.new`, reporting bytes received, and verify its size.
async fn download(info: &ReleaseInfo, exe: &Path, on_progress: impl Fn(u64)) -> Result<PathBuf> {
    let target = sibling(exe, ".new");
    let client = download_client()?;
    let mut response = client
        .get(&info.download_url)
        .send()
        .await
        .context("request failed")?
        .error_for_status()
        .context("Forgejo rejected the download")?;

    let mut file = tokio::fs::File::create(&target)
        .await
        .with_context(|| format!("cannot create {}", target.display()))?;
    let mut received: u64 = 0;
    let mut last_reported: u64 = 0;
    // Report in steps of about 1% so the UI is not flooded with repaints.
    let report_step = (info.size / 100).max(1);
    while let Some(chunk) = response.chunk().await.context("connection lost")? {
        file.write_all(&chunk).await.context("write failed")?;
        received += chunk.len() as u64;
        if received > info.size {
            drop(file);
            let _ = tokio::fs::remove_file(&target).await;
            bail!("download is larger than the announced {} bytes", info.size);
        }
        if received - last_reported >= report_step {
            last_reported = received;
            on_progress(received);
        }
    }
    file.flush().await.context("flush failed")?;
    drop(file);

    if received != info.size {
        let _ = tokio::fs::remove_file(&target).await;
        bail!(
            "download is incomplete: {} of {} bytes",
            received,
            info.size
        );
    }
    if !starts_with_pe_header(&target).await {
        let _ = tokio::fs::remove_file(&target).await;
        bail!("the downloaded file is not a Windows executable");
    }
    on_progress(received);
    Ok(target)
}

/// Every Windows executable starts with `MZ`; an error page saved to disk does not.
async fn starts_with_pe_header(path: &Path) -> bool {
    match tokio::fs::read(path).await {
        Ok(bytes) => bytes.starts_with(b"MZ"),
        Err(_) => false,
    }
}

/// Move the running executable aside and put the new one in its place.
fn install(exe: &Path, new_file: &Path) -> Result<()> {
    let old = sibling(exe, ".old");
    let _ = std::fs::remove_file(&old);
    std::fs::rename(exe, &old).with_context(|| format!("cannot move {} aside", exe.display()))?;
    if let Err(e) = std::fs::rename(new_file, exe) {
        // Put the running binary back so the install stays where it was.
        let _ = std::fs::rename(&old, exe);
        return Err(e).with_context(|| format!("cannot place {}", exe.display()));
    }
    Ok(())
}

/// Remove the leftovers of a previous update. Best effort; the old binary may
/// still be locked if the previous instance has not fully exited yet.
fn cleanup_previous(exe: &Path) {
    let _ = std::fs::remove_file(sibling(exe, ".old"));
    let _ = std::fs::remove_file(sibling(exe, ".new"));
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_three_part_versions() {
        assert_eq!(parse_version("0.4.2"), Some((0, 4, 2)));
        assert_eq!(parse_version("v1.2.3"), Some((1, 2, 3)));
        assert_eq!(parse_version("1.2"), None);
        assert_eq!(parse_version("1.2.3.4"), None);
        assert_eq!(parse_version("latest"), None);
    }

    #[test]
    fn compares_versions_numerically() {
        assert!(is_newer("0.10.0", "0.9.9"));
        assert!(is_newer("1.0.0", "0.99.99"));
        assert!(!is_newer("0.4.2", "0.4.2"));
        assert!(!is_newer("0.4.1", "0.4.2"));
        assert!(!is_newer("nightly", "0.4.2"));
    }

    #[test]
    fn only_the_release_host_is_trusted() {
        assert!(is_release_host(
            "https://git.ossalali.com/oss/Portal/releases/download/0.4.2/portal.exe"
        ));
        assert!(!is_release_host("http://git.ossalali.com/x"));
        assert!(!is_release_host("https://example.com/portal.exe"));
        assert!(!is_release_host("not a url"));
    }

    fn release_json(version: &str, asset_url: &str) -> Release {
        serde_json::from_str(&format!(
            r#"{{
                "tag_name": "{version}",
                "html_url": "https://git.ossalali.com/oss/Portal/releases/tag/{version}",
                "draft": false,
                "prerelease": false,
                "assets": [{{
                    "name": "portal.exe",
                    "browser_download_url": "{asset_url}",
                    "size": 20388352
                }}]
            }}"#
        ))
        .expect("fixture parses")
    }

    #[test]
    fn offers_a_newer_release_with_its_asset() {
        let release = release_json(
            "0.5.0",
            "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe",
        );
        let info = select_update(&release, "0.4.2")
            .expect("selection succeeds")
            .expect("an update is offered");
        assert_eq!(info.version, "0.5.0");
        assert_eq!(info.size, 20_388_352);
    }

    #[test]
    fn ignores_releases_that_are_not_newer() {
        let release = release_json(
            "0.4.2",
            "https://git.ossalali.com/oss/Portal/releases/download/0.4.2/portal.exe",
        );
        assert!(select_update(&release, "0.4.2").unwrap().is_none());
        assert!(select_update(&release, "0.9.0").unwrap().is_none());
    }

    #[test]
    fn skips_drafts_and_prereleases() {
        let mut release = release_json(
            "0.5.0",
            "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe",
        );
        release.draft = true;
        assert!(select_update(&release, "0.4.2").unwrap().is_none());
        release.draft = false;
        release.prerelease = true;
        assert!(select_update(&release, "0.4.2").unwrap().is_none());
    }

    #[test]
    fn rejects_an_asset_hosted_elsewhere() {
        let release = release_json("0.5.0", "https://example.com/portal.exe");
        assert!(select_update(&release, "0.4.2").is_err());
    }

    #[test]
    fn rejects_a_release_without_the_binary() {
        let mut release = release_json(
            "0.5.0",
            "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe",
        );
        release.assets.clear();
        assert!(select_update(&release, "0.4.2").is_err());
    }

    /// Confirms the live feed still has the shape the client expects.
    #[tokio::test]
    #[ignore = "requires network access to git.ossalali.com"]
    async fn reads_the_live_release_feed() {
        let client = http_client().unwrap();
        let release: Release = client
            .get(LATEST_RELEASE_API)
            .send()
            .await
            .expect("request reaches Forgejo")
            .error_for_status()
            .expect("Forgejo serves the release")
            .json()
            .await
            .expect("the feed has the expected shape");
        let info = select_update(&release, "0.0.1")
            .expect("selection succeeds")
            .expect("the published release is offered against 0.0.1");
        assert!(info.download_url.ends_with("/portal.exe"));
        assert!(info.size > 0);
    }

    /// The app spawns checks on a multi-threaded runtime and reads the outcome
    /// from the UI thread; this covers that round trip as the app performs it.
    #[test]
    #[ignore = "requires network access to git.ossalali.com"]
    fn reports_a_check_back_to_the_ui_thread() {
        let runtime = tokio::runtime::Runtime::new().unwrap();
        let ctx = eframe::egui::Context::default();
        let updater = Updater::new();
        updater.check(&runtime, &ctx);

        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(30);
        loop {
            match updater.status() {
                UpdateStatus::Checking => {
                    assert!(
                        std::time::Instant::now() < deadline,
                        "the check never reported back"
                    );
                    std::thread::sleep(std::time::Duration::from_millis(50));
                }
                UpdateStatus::UpToDate | UpdateStatus::Available(_) => break,
                other => panic!("unexpected status: {other:?}"),
            }
        }
    }

    /// Exercises the real download and the rename swap in a scratch directory.
    #[tokio::test]
    #[ignore = "requires network access to git.ossalali.com"]
    async fn downloads_and_swaps_the_live_binary() {
        let client = http_client().unwrap();
        let release: Release = client
            .get(LATEST_RELEASE_API)
            .send()
            .await
            .expect("request reaches Forgejo")
            .error_for_status()
            .expect("Forgejo serves the release")
            .json()
            .await
            .expect("the feed has the expected shape");
        let info = select_update(&release, "0.0.1").unwrap().unwrap();

        let dir = std::env::temp_dir().join(format!("portal-update-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let exe = dir.join("portal.exe");
        std::fs::write(&exe, b"MZ standing in for the running binary").unwrap();

        let downloaded = download(&info, &exe, |_| {}).await.expect("download");
        assert_eq!(downloaded, dir.join("portal.exe.new"));
        assert_eq!(std::fs::metadata(&downloaded).unwrap().len(), info.size);

        install(&exe, &downloaded).expect("swap");
        assert_eq!(std::fs::metadata(&exe).unwrap().len(), info.size);
        assert_eq!(
            std::fs::read(dir.join("portal.exe.old")).unwrap(),
            b"MZ standing in for the running binary"
        );
        assert!(!downloaded.exists());

        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn sibling_keeps_the_directory_and_full_name() {
        let exe = Path::new(r"C:\Tools\portal.exe");
        assert_eq!(
            sibling(exe, ".old"),
            PathBuf::from(r"C:\Tools\portal.exe.old")
        );
        assert_eq!(
            sibling(exe, ".new"),
            PathBuf::from(r"C:\Tools\portal.exe.new")
        );
    }
}
