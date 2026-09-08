//! Deterministic screenshot harness — `cargo run --features shots`.
//!
//! Nothing here opens a window and nothing sends input to one. `egui_kittest`
//! runs the egui pass in-process and rasterises it through `egui-wgpu` into a
//! texture it reads back, so a capture is exactly the frame egui produced and
//! never a desktop grab another window can cover. Its default adapter selector
//! prefers a CPU rasteriser, so this runs on a VM or a runner with no GPU.
//!
//! Every screen is driven by a fixture instead of by clicking, so no SSH host
//! is contacted and two runs produce the same pixels. Only the parts of the UI
//! that do not need a live SFTP session can be captured: the connect view, a
//! file pane, the updates section and the transfer rows. The browser view owns
//! a `russh` handle, so it cannot be built without a server.
//!
//! Add a screen by adding a `Scene` to `scenes()`.

use super::{
    AppSettings, ConnectState, PaneId, PaneState, RowView, SavedSession, WindowState,
    render_file_list, render_pane_header, render_transfer_row, render_update_section,
};
use crate::fs::FileEntry;
use crate::transfer::{TaskStatus, TransferDirection};
use crate::update::{self, ReleaseInfo, UpdateStatus};
use eframe::egui;
use egui::{Theme, Vec2};
use std::collections::HashSet;
use std::path::Path;

/// Fixed so a scene is the same pixels on any machine, and matched to the
/// developer display so what is reviewed is what the user sees.
const PIXELS_PER_POINT: f32 = 1.5;

/// egui settles layout over a few passes; panels and tables need more than one.
const STEPS: usize = 4;

const PANE: Vec2 = Vec2::new(560.0, 420.0);
const CONNECT: Vec2 = Vec2::new(560.0, 520.0);
const SECTION: Vec2 = Vec2::new(460.0, 220.0);
const ROWS: Vec2 = Vec2::new(460.0, 380.0);

#[derive(Clone)]
enum Kind {
    Connect,
    /// A file pane, as the side panels compose it: header then table.
    Pane {
        title: &'static str,
        pane_id: PaneId,
        show_meta: bool,
    },
    /// The Updates block of the settings window, in one particular state.
    Updates(UpdateStatus),
    Transfers,
}

struct Scene {
    name: &'static str,
    size: Vec2,
    theme: Theme,
    kind: Kind,
}

// ---- fixture data ----

fn local_entries() -> Vec<FileEntry> {
    let file = |name: &str, size: u64| FileEntry {
        name: name.to_string(),
        is_dir: false,
        size,
        permissions: None,
        user: None,
        group: None,
        uid: None,
        gid: None,
    };
    let dir = |name: &str| FileEntry {
        name: name.to_string(),
        is_dir: true,
        size: 0,
        permissions: None,
        user: None,
        group: None,
        uid: None,
        gid: None,
    };
    vec![
        FileEntry::parent(),
        dir("Backups"),
        dir("Screenshots"),
        file("portal.exe", 21_758_464),
        file("notes.md", 4_812),
        file("session.log", 183_402),
        file("archive.tar.gz", 964_242_119),
    ]
}

fn remote_entries() -> Vec<FileEntry> {
    let entry = |name: &str, is_dir: bool, size: u64, mode: u32| FileEntry {
        name: name.to_string(),
        is_dir,
        size,
        permissions: Some(mode),
        user: Some("oss".to_string()),
        group: Some("users".to_string()),
        uid: Some(1000),
        gid: Some(100),
    };
    vec![
        FileEntry::parent(),
        entry("docker", true, 0, 0o755),
        entry("media", true, 0, 0o775),
        entry("compose.yaml", false, 2_048, 0o644),
        entry("backup.sql", false, 48_221_984, 0o600),
        entry("deploy.sh", false, 1_204, 0o750),
    ]
}

fn pane(path: &str, entries: Vec<FileEntry>, selected: &[usize]) -> PaneState {
    PaneState {
        path: path.to_string(),
        path_input: path.to_string(),
        entries,
        selected: selected.iter().copied().collect::<HashSet<usize>>(),
        last_clicked: selected.last().copied(),
        search_query: String::new(),
    }
}

fn connect_state() -> ConnectState {
    ConnectState {
        host: "matrix.ossalali.com".to_string(),
        user: "root".to_string(),
        port: "22".to_string(),
        error: None,
        saved_sessions: vec![
            SavedSession {
                host: "matrix.ossalali.com".to_string(),
                user: "root".to_string(),
                port: 22,
            },
            SavedSession {
                host: "192.168.2.3".to_string(),
                user: "oss".to_string(),
                port: 22,
            },
        ],
        // Never connect from the harness.
        try_auto_connect: false,
    }
}

fn available() -> ReleaseInfo {
    ReleaseInfo {
        version: "0.5.0".to_string(),
        page_url: "https://git.ossalali.com/oss/Portal/releases/tag/0.5.0".to_string(),
        download_url: "https://git.ossalali.com/oss/Portal/releases/download/0.5.0/portal.exe"
            .to_string(),
        size: 21_758_464,
    }
}

fn transfer_rows(ui: &mut egui::Ui) {
    let rows = [
        RowView {
            direction: TransferDirection::Download,
            status: TaskStatus::Active,
            name: "archive.tar.gz",
            subfile: Some("archive.tar.gz"),
            bytes_done: 412_090_368,
            bytes_total: 964_242_119,
            elapsed: 12.4,
            error: None,
            show_x_button: true,
        },
        RowView {
            direction: TransferDirection::Upload,
            status: TaskStatus::Queued,
            name: "portal.exe",
            subfile: None,
            bytes_done: 0,
            bytes_total: 21_758_464,
            elapsed: 0.0,
            error: None,
            show_x_button: true,
        },
        RowView {
            direction: TransferDirection::Download,
            status: TaskStatus::Done,
            name: "compose.yaml",
            subfile: None,
            bytes_done: 2_048,
            bytes_total: 2_048,
            elapsed: 0.3,
            error: None,
            show_x_button: true,
        },
        RowView {
            direction: TransferDirection::Upload,
            status: TaskStatus::Error,
            name: "backup.sql",
            subfile: None,
            bytes_done: 1_048_576,
            bytes_total: 48_221_984,
            elapsed: 2.1,
            error: Some("permission denied"),
            show_x_button: true,
        },
    ];
    for row in &rows {
        render_transfer_row(ui, row);
        ui.separator();
    }
}

// ---- the scenes ----

fn scenes() -> Vec<Scene> {
    vec![
        Scene {
            name: "01-connect",
            size: CONNECT,
            theme: Theme::Dark,
            kind: Kind::Connect,
        },
        Scene {
            name: "02-connect-light",
            size: CONNECT,
            theme: Theme::Light,
            kind: Kind::Connect,
        },
        Scene {
            name: "03-local-pane",
            size: PANE,
            theme: Theme::Dark,
            kind: Kind::Pane {
                title: "Local",
                pane_id: PaneId::Local,
                show_meta: false,
            },
        },
        // The selection fix: whole rows highlight and their checkbox glyph fills.
        Scene {
            name: "04-local-pane-selected",
            size: PANE,
            theme: Theme::Dark,
            kind: Kind::Pane {
                title: "Local",
                pane_id: PaneId::Local,
                show_meta: false,
            },
        },
        Scene {
            name: "05-remote-pane-permissions",
            size: PANE,
            theme: Theme::Dark,
            kind: Kind::Pane {
                title: "Remote",
                pane_id: PaneId::Remote,
                show_meta: true,
            },
        },
        Scene {
            name: "06-remote-pane-light",
            size: PANE,
            theme: Theme::Light,
            kind: Kind::Pane {
                title: "Remote",
                pane_id: PaneId::Remote,
                show_meta: true,
            },
        },
        Scene {
            name: "07-updates-idle",
            size: SECTION,
            theme: Theme::Dark,
            kind: Kind::Updates(UpdateStatus::Idle),
        },
        Scene {
            name: "08-updates-checking",
            size: SECTION,
            theme: Theme::Dark,
            kind: Kind::Updates(UpdateStatus::Checking),
        },
        Scene {
            name: "09-updates-up-to-date",
            size: SECTION,
            theme: Theme::Dark,
            kind: Kind::Updates(UpdateStatus::UpToDate),
        },
        Scene {
            name: "10-updates-available",
            size: SECTION,
            theme: Theme::Dark,
            kind: Kind::Updates(UpdateStatus::Available(available())),
        },
        Scene {
            name: "11-updates-downloading",
            size: SECTION,
            theme: Theme::Dark,
            kind: Kind::Updates(UpdateStatus::Downloading {
                info: available(),
                received: 9_181_030,
                total: 21_758_464,
            }),
        },
        Scene {
            name: "12-updates-ready",
            size: SECTION,
            theme: Theme::Dark,
            kind: Kind::Updates(UpdateStatus::ReadyToRestart {
                version: "0.5.0".to_string(),
            }),
        },
        Scene {
            name: "13-updates-error",
            size: SECTION,
            theme: Theme::Dark,
            kind: Kind::Updates(UpdateStatus::Error(
                "Update check failed: request failed: dns error".to_string(),
            )),
        },
        Scene {
            name: "14-updates-available-light",
            size: SECTION,
            theme: Theme::Light,
            kind: Kind::Updates(UpdateStatus::Available(available())),
        },
        Scene {
            name: "15-transfers",
            size: ROWS,
            theme: Theme::Dark,
            kind: Kind::Transfers,
        },
    ]
}

// ---- the driver ----

struct Fixture {
    kind: Kind,
    connect: ConnectState,
    pane: PaneState,
    active: PaneId,
    settings: AppSettings,
    window: WindowState,
    updater: update::Updater,
    runtime: tokio::runtime::Runtime,
}

impl Fixture {
    fn new(scene: &Scene) -> Result<Self, String> {
        let selected: &[usize] = if scene.name == "04-local-pane-selected" {
            &[1, 3, 4]
        } else {
            &[]
        };
        let pane = match &scene.kind {
            Kind::Pane {
                pane_id: PaneId::Remote,
                ..
            } => pane("/srv", remote_entries(), selected),
            _ => pane(r"C:\Users\Oss\Projects", local_entries(), selected),
        };
        let updater = update::Updater::new();
        if let Kind::Updates(status) = &scene.kind {
            *updater
                .status
                .lock()
                .map_err(|_| "the update status lock is poisoned".to_string())? = status.clone();
        }
        Ok(Self {
            kind: scene.kind.clone(),
            connect: connect_state(),
            pane,
            active: PaneId::Local,
            settings: AppSettings::default(),
            window: WindowState::default(),
            updater,
            runtime: tokio::runtime::Runtime::new().map_err(|e| e.to_string())?,
        })
    }
}

fn draw(ui: &mut egui::Ui, fx: &mut Fixture) {
    let kind = fx.kind.clone();
    match kind {
        Kind::Connect => {
            // Returns a browser state only on a successful connect, which the
            // fixture never triggers.
            let _ = super::show_connect_view(
                ui,
                &mut fx.connect,
                &fx.runtime,
                &fx.settings,
                &fx.window,
            );
        }
        Kind::Pane {
            title,
            pane_id,
            show_meta,
        } => {
            egui::CentralPanel::default().show(ui, |ui| {
                let mut request_focus = false;
                let _ = render_pane_header(
                    ui,
                    title,
                    &mut fx.pane,
                    pane_id == PaneId::Local,
                    pane_id,
                    fx.active,
                    &mut request_focus,
                );
                let _ = render_file_list(ui, &mut fx.pane, pane_id, &mut fx.active, show_meta);
            });
        }
        Kind::Updates(_) => {
            egui::CentralPanel::default().show(ui, |ui| {
                ui.add_space(6.0);
                render_update_section(ui, &fx.runtime, &mut fx.updater);
            });
        }
        Kind::Transfers => {
            egui::CentralPanel::default().show(ui, transfer_rows);
        }
    }
}

/// Renders every scene offscreen and writes the PNGs plus an index.
pub fn capture(out_dir: &Path) -> Result<(), String> {
    std::fs::create_dir_all(out_dir).map_err(|e| format!("{}: {e}", out_dir.display()))?;

    let scenes = scenes();
    let mut manifest = String::from("{\n  \"scenes\": [\n");

    for (i, scene) in scenes.iter().enumerate() {
        let fixture = Fixture::new(scene)?;
        let mut harness = egui_kittest::Harness::builder()
            .with_size(scene.size)
            .with_pixels_per_point(PIXELS_PER_POINT)
            .wgpu()
            .build_ui_state(draw, fixture);

        // The real app's fonts and interaction style, so a capture is not a
        // different look from the shipped one.
        crate::setup_fonts(&harness.ctx);
        crate::setup_style(&harness.ctx);
        let theme = scene.theme;
        harness.ctx.options_mut(|o| {
            o.theme_preference = match theme {
                Theme::Dark => egui::ThemePreference::Dark,
                Theme::Light => egui::ThemePreference::Light,
            }
        });

        harness.run_steps(STEPS);
        let image = harness.render()?;
        let path = out_dir.join(format!("{}.png", scene.name));
        image
            .save_with_format(&path, image::ImageFormat::Png)
            .map_err(|e| format!("{}: {e}", path.display()))?;

        manifest.push_str(&format!(
            "    {{ \"name\": \"{}\", \"width\": {}, \"height\": {} }}{}\n",
            scene.name,
            image.width(),
            image.height(),
            if i + 1 == scenes.len() { "" } else { "," }
        ));
        println!(
            "  {:>2}/{}  {}  {}x{}",
            i + 1,
            scenes.len(),
            scene.name,
            image.width(),
            image.height()
        );
    }

    manifest.push_str("  ]\n}\n");
    let index = out_dir.join("index.json");
    std::fs::write(&index, manifest).map_err(|e| format!("{}: {e}", index.display()))?;

    println!(
        "\n{} screenshots written to {}",
        scenes.len(),
        out_dir.display()
    );
    Ok(())
}
