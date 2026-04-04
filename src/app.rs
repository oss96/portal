use crate::{fs, ssh, transfer};
use eframe::egui;
use russh::client;
use russh_sftp::client::SftpSession;
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use transfer::TransferProgress;

// ── Persistent Config ──────────────────────────────────────────────────

fn config_dir() -> PathBuf {
    dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("portal")
}

// ── Saved Sessions ─────────────────────────────────────────────────────

#[derive(Serialize, Deserialize, Clone, PartialEq)]
struct SavedSession {
    host: String,
    user: String,
    port: u16,
}

fn load_sessions() -> Vec<SavedSession> {
    let path = config_dir().join("sessions.json");
    std::fs::read_to_string(&path)
        .ok()
        .and_then(|d| serde_json::from_str(&d).ok())
        .unwrap_or_default()
}

fn save_session(host: &str, user: &str, port: u16) {
    let mut sessions = load_sessions();
    let new = SavedSession {
        host: host.to_string(),
        user: user.to_string(),
        port,
    };
    sessions.retain(|s| s != &new);
    sessions.insert(0, new);
    sessions.truncate(10);

    let dir = config_dir();
    let _ = std::fs::create_dir_all(&dir);
    let _ = std::fs::write(
        dir.join("sessions.json"),
        serde_json::to_string_pretty(&sessions).unwrap_or_default(),
    );
}

fn persist_sessions(sessions: &[SavedSession]) {
    let dir = config_dir();
    let _ = std::fs::create_dir_all(&dir);
    let _ = std::fs::write(
        dir.join("sessions.json"),
        serde_json::to_string_pretty(sessions).unwrap_or_default(),
    );
}

// ── Settings ───────────────────────────────────────────────────────────

#[derive(Serialize, Deserialize, Clone)]
struct AppSettings {
    default_local_path: String,
    default_remote_path: String,
    #[serde(default = "default_host_path")]
    default_host_path: String,
    #[serde(default)]
    auto_connect: bool,
}

fn default_host_path() -> String {
    "/".to_string()
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            default_local_path: dirs::home_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .to_string_lossy()
                .to_string(),
            default_remote_path: "/".to_string(),
            default_host_path: "/".to_string(),
            auto_connect: false,
        }
    }
}

fn load_settings() -> AppSettings {
    let path = config_dir().join("settings.json");
    std::fs::read_to_string(&path)
        .ok()
        .and_then(|d| serde_json::from_str(&d).ok())
        .unwrap_or_default()
}

fn save_settings(settings: &AppSettings) {
    let dir = config_dir();
    let _ = std::fs::create_dir_all(&dir);
    let _ = std::fs::write(
        dir.join("settings.json"),
        serde_json::to_string_pretty(settings).unwrap_or_default(),
    );
}

// ── Drag & Drop Payload ────────────────────────────────────────────────

#[derive(Clone, Copy, PartialEq)]
enum PaneId {
    Local,
    Remote,
    Host,
}

#[derive(Clone)]
struct DragPayload {
    source: PaneId,
    entries: Vec<fs::FileEntry>,
    src_path: String,
}

enum DeleteTarget {
    Remote,
    Host,
}

// ── Transfer State ─────────────────────────────────────────────────────

enum TransferState {
    Idle,
    InProgress {
        progress: Arc<Mutex<TransferProgress>>,
        handle: tokio::task::JoinHandle<Result<usize, String>>,
        cancel: Arc<AtomicBool>,
    },
    Done {
        message: String,
        is_error: bool,
        since: std::time::Instant,
    },
}

// ── App ────────────────────────────────────────────────────────────────

pub struct PortalApp {
    runtime: tokio::runtime::Runtime,
    view: View,
    first_frame: bool,
    settings: AppSettings,
}

enum View {
    Connect(ConnectState),
    Browser(BrowserState),
}

// ── Connect View State ─────────────────────────────────────────────────

struct ConnectState {
    host: String,
    user: String,
    port: String,
    error: Option<String>,
    saved_sessions: Vec<SavedSession>,
    try_auto_connect: bool,
}

impl ConnectState {
    fn new(auto_connect: bool) -> Self {
        let saved = load_sessions();
        let (host, user, port) = saved
            .first()
            .map(|s| (s.host.clone(), s.user.clone(), s.port.to_string()))
            .unwrap_or_else(|| (String::new(), String::new(), "22".to_string()));
        let should_auto = auto_connect && !saved.is_empty();
        Self {
            host,
            user,
            port,
            error: None,
            saved_sessions: saved,
            try_auto_connect: should_auto,
        }
    }
}

impl Default for ConnectState {
    fn default() -> Self {
        Self::new(false)
    }
}

impl ConnectState {
    fn with_prefill(host: &str, user: &str, port: u16) -> Self {
        Self {
            host: host.to_string(),
            user: user.to_string(),
            port: port.to_string(),
            error: None,
            saved_sessions: load_sessions(),
            try_auto_connect: false,
        }
    }
}

// ── Browser View State ─────────────────────────────────────────────────

struct BrowserState {
    handle: client::Handle<ssh::Handler>,
    sftp: Arc<SftpSession>,
    local: PaneState,
    remote: PaneState,
    host: PaneState,
    show_host: bool,
    status: String,
    connection_label: String,
    transfer_state: TransferState,
    show_settings: bool,
    settings_draft: AppSettings,
    confirm_delete: Option<(DeleteTarget, Vec<fs::FileEntry>)>,
    new_folder: Option<(PaneId, String)>,
    merge_folders: Option<(PaneId, Vec<fs::FileEntry>, String, bool)>,
}

struct PaneState {
    path: String,
    entries: Vec<fs::FileEntry>,
    selected: HashSet<usize>,
    last_clicked: Option<usize>,
}

// ── Construction ───────────────────────────────────────────────────────

impl PortalApp {
    pub fn connected(
        runtime: tokio::runtime::Runtime,
        handle: client::Handle<ssh::Handler>,
        sftp: SftpSession,
        user: &str,
        host: &str,
    ) -> anyhow::Result<Self> {
        save_session(host, user, 22);
        let settings = load_settings();
        let sftp = Arc::new(sftp);

        let local_path = settings.default_local_path.clone();
        let local_entries = fs::list_local(&PathBuf::from(&local_path)).unwrap_or_default();
        let remote_path = settings.default_remote_path.clone();
        let remote_entries = runtime
            .block_on(fs::list_remote(&sftp, &remote_path))
            .unwrap_or_default();

        let host_path = settings.default_host_path.clone();
        let host_entries = runtime
            .block_on(fs::list_remote(&sftp, &host_path))
            .unwrap_or_default();

        Ok(Self {
            runtime,
            view: View::Browser(BrowserState {
                handle,
                sftp,
                local: PaneState {
                    path: local_path,
                    entries: local_entries,
                    selected: HashSet::new(),
                    last_clicked: None,
                },
                remote: PaneState {
                    path: remote_path,
                    entries: remote_entries,
                    selected: HashSet::new(),
                    last_clicked: None,
                },
                host: PaneState {
                    path: host_path,
                    entries: host_entries,
                    selected: HashSet::new(),
                    last_clicked: None,
                },
                show_host: false,
                status: "Ready".to_string(),
                connection_label: format!("{}@{}", user, host),
                transfer_state: TransferState::Idle,
                show_settings: false,
                settings_draft: settings.clone(),
                confirm_delete: None,
                new_folder: None,
                merge_folders: None,
            }),
            first_frame: true,
            settings,
        })
    }

    pub fn with_connect_dialog(runtime: tokio::runtime::Runtime) -> Self {
        let settings = load_settings();
        Self {
            runtime,
            view: View::Connect(ConnectState::new(settings.auto_connect)),
            first_frame: true,
            settings,
        }
    }

    pub fn with_prefilled_connect(
        runtime: tokio::runtime::Runtime,
        host: &str,
        user: &str,
        port: u16,
        error: String,
    ) -> Self {
        let mut state = ConnectState::with_prefill(host, user, port);
        state.error = Some(error);
        Self {
            runtime,
            view: View::Connect(state),
            first_frame: true,
            settings: load_settings(),
        }
    }
}

// ── eframe::App Implementation ─────────────────────────────────────────

impl eframe::App for PortalApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        if self.first_frame {
            self.first_frame = false;
            if let Some(cmd) = egui::ViewportCommand::center_on_screen(ctx) {
                ctx.send_viewport_cmd(cmd);
            }
        }

        match &mut self.view {
            View::Connect(state) => {
                if let Some(browser) =
                    show_connect_view(ctx, state, &self.runtime, &self.settings)
                {
                    self.view = View::Browser(browser);
                }
            }
            View::Browser(state) => {
                poll_transfer(state, &self.runtime);
                show_browser_view(ctx, state, &self.runtime);

                // Apply settings if saved
                if !state.show_settings {
                    if state.settings_draft.default_local_path != self.settings.default_local_path
                        || state.settings_draft.default_remote_path
                            != self.settings.default_remote_path
                    {
                        self.settings = state.settings_draft.clone();
                    }
                }

                // Auto-persist host pane path when it changes
                if state.host.path != self.settings.default_host_path {
                    self.settings.default_host_path = state.host.path.clone();
                    state.settings_draft.default_host_path = state.host.path.clone();
                    save_settings(&self.settings);
                }
            }
        }
    }
}

// ── Transfer Polling ───────────────────────────────────────────────────

fn poll_transfer(state: &mut BrowserState, runtime: &tokio::runtime::Runtime) {
    let current = std::mem::replace(&mut state.transfer_state, TransferState::Idle);

    match current {
        TransferState::InProgress { handle, progress, cancel } => {
            if handle.is_finished() {
                let result = runtime.block_on(handle);
                match result {
                    Ok(Ok(count)) => {
                        let p = progress.lock().unwrap();
                        let elapsed = p
                            .started_at
                            .map(|t| t.elapsed().as_secs_f64())
                            .unwrap_or(0.0);
                        let speed = if elapsed > 0.01 {
                            format_size((p.bytes_done as f64 / elapsed) as u64)
                        } else {
                            "---".to_string()
                        };
                        let total_str = format_size(p.bytes_done);
                        drop(p);

                        if let Ok(entries) = fs::list_local(&PathBuf::from(&state.local.path)) {
                            state.local.entries = entries;
                        }
                        if let Ok(entries) =
                            runtime.block_on(fs::list_remote(&state.sftp, &state.remote.path))
                        {
                            state.remote.entries = entries;
                        }
                        if state.show_host {
                            if let Ok(entries) = runtime.block_on(fs::list_remote(&state.sftp, &state.host.path)) {
                                state.host.entries = entries;
                            }
                        }
                        state.local.selected.clear();
                        state.remote.selected.clear();
                        state.host.selected.clear();

                        state.transfer_state = TransferState::Done {
                            message: format!(
                                "Transferred {} item(s) \u{2014} {} at {}/s",
                                count, total_str, speed
                            ),
                            is_error: false,
                            since: std::time::Instant::now(),
                        };
                    }
                    Ok(Err(e)) => {
                        state.transfer_state = TransferState::Done {
                            message: format!("Error: {}", e),
                            is_error: true,
                            since: std::time::Instant::now(),
                        };
                    }
                    Err(e) => {
                        let (msg, is_err) = if e.is_cancelled() {
                            ("Transfer cancelled".to_string(), false)
                        } else {
                            (format!("Failed: {}", e), true)
                        };
                        state.transfer_state = TransferState::Done {
                            message: msg,
                            is_error: is_err,
                            since: std::time::Instant::now(),
                        };
                    }
                }
            } else {
                state.transfer_state = TransferState::InProgress { handle, progress, cancel };
            }
        }
        TransferState::Done {
            message,
            is_error,
            since,
        } => {
            if since.elapsed().as_secs() < 8 {
                state.transfer_state = TransferState::Done {
                    message,
                    is_error,
                    since,
                };
            }
        }
        TransferState::Idle => {}
    }
}

// ── Connect View ───────────────────────────────────────────────────────

fn show_connect_view(
    ctx: &egui::Context,
    state: &mut ConnectState,
    runtime: &tokio::runtime::Runtime,
    settings: &AppSettings,
) -> Option<BrowserState> {
    let mut result = None;

    egui::CentralPanel::default().show(ctx, |ui| {
        ui.vertical_centered(|ui| {
            ui.add_space(40.0);
            ui.heading("Portal");
            ui.label("SSH File Manager");
            ui.add_space(20.0);

            if !state.saved_sessions.is_empty() {
                ui.label(egui::RichText::new("Recent sessions").strong());
                ui.add_space(4.0);

                let mut picked: Option<SavedSession> = None;
                let mut remove_idx: Option<usize> = None;

                for (idx, session) in state.saved_sessions.iter().enumerate() {
                    ui.horizontal(|ui| {
                        let label =
                            format!("{}@{}:{}", session.user, session.host, session.port);
                        if ui.button(&label).clicked() {
                            picked = Some(session.clone());
                        }
                        if ui
                            .small_button("\u{2716}")
                            .on_hover_text("Remove")
                            .clicked()
                        {
                            remove_idx = Some(idx);
                        }
                    });
                }

                if let Some(idx) = remove_idx {
                    state.saved_sessions.remove(idx);
                    persist_sessions(&state.saved_sessions);
                }
                if let Some(s) = picked {
                    state.host = s.host;
                    state.user = s.user;
                    state.port = s.port.to_string();
                }

                ui.add_space(12.0);
                ui.separator();
                ui.add_space(8.0);
            }

            ui.allocate_ui(egui::vec2(300.0, 120.0), |ui| {
                egui::Grid::new("connect_form")
                    .num_columns(2)
                    .spacing([10.0, 8.0])
                    .show(ui, |ui| {
                        ui.label("Host:");
                        ui.add(
                            egui::TextEdit::singleline(&mut state.host)
                                .desired_width(200.0)
                                .hint_text("192.168.1.1"),
                        );
                        ui.end_row();
                        ui.label("User:");
                        ui.add(
                            egui::TextEdit::singleline(&mut state.user)
                                .desired_width(200.0)
                                .hint_text("root"),
                        );
                        ui.end_row();
                        ui.label("Port:");
                        ui.add(
                            egui::TextEdit::singleline(&mut state.port).desired_width(200.0),
                        );
                        ui.end_row();
                    });
            });

            ui.add_space(15.0);

            let can_connect =
                !state.host.is_empty() && !state.user.is_empty() && !state.port.is_empty();
            let connect_btn = ui.add_enabled(can_connect, egui::Button::new("  Connect  "));
            let enter_pressed = ui.input(|i| i.key_pressed(egui::Key::Enter));

            // Auto-connect on first frame if enabled
            let auto = std::mem::take(&mut state.try_auto_connect);

            if (connect_btn.clicked() || enter_pressed || auto) && can_connect {
                let port: u16 = state.port.parse().unwrap_or(22);
                state.error = None;

                match runtime.block_on(ssh::connect(&state.host, port, &state.user)) {
                    Ok((handle, sftp)) => {
                        save_session(&state.host, &state.user, port);
                        let sftp = Arc::new(sftp);

                        let local_path = settings.default_local_path.clone();
                        let local_entries =
                            fs::list_local(&PathBuf::from(&local_path)).unwrap_or_default();
                        let remote_path = settings.default_remote_path.clone();
                        let remote_entries = runtime
                            .block_on(fs::list_remote(&sftp, &remote_path))
                            .unwrap_or_default();

                        let host_path = settings.default_host_path.clone();
                        let host_entries = runtime
                            .block_on(fs::list_remote(&sftp, &host_path))
                            .unwrap_or_default();

                        result = Some(BrowserState {
                            handle,
                            sftp,
                            local: PaneState {
                                path: local_path,
                                entries: local_entries,
                                selected: HashSet::new(),
                    last_clicked: None,
                            },
                            remote: PaneState {
                                path: remote_path,
                                entries: remote_entries,
                                selected: HashSet::new(),
                    last_clicked: None,
                            },
                            host: PaneState {
                                path: host_path,
                                entries: host_entries,
                                selected: HashSet::new(),
                    last_clicked: None,
                            },
                            show_host: false,
                            status: "Connected".to_string(),
                            connection_label: format!("{}@{}", state.user, state.host),
                            transfer_state: TransferState::Idle,
                            show_settings: false,
                            settings_draft: settings.clone(),
                            confirm_delete: None,
                new_folder: None,
                merge_folders: None,
                        });
                    }
                    Err(e) => {
                        state.error = Some(format!("{}", e));
                    }
                }
            }

            if let Some(err) = &state.error {
                ui.add_space(10.0);
                ui.colored_label(egui::Color32::RED, err);
            }
        });
    });

    result
}

// ── Browser View ───────────────────────────────────────────────────────

fn show_browser_view(
    ctx: &egui::Context,
    state: &mut BrowserState,
    runtime: &tokio::runtime::Runtime,
) {
    ctx.send_viewport_cmd(egui::ViewportCommand::Title(format!(
        "Portal \u{2014} {}",
        state.connection_label
    )));

    let is_transferring = matches!(state.transfer_state, TransferState::InProgress { .. });
    if is_transferring {
        ctx.request_repaint();
    }

    // Settings window (floating)
    if state.show_settings {
        show_settings_window(ctx, state);
    }

    // Delete confirmation dialog
    if state.confirm_delete.is_some() {
        show_delete_confirm(ctx, state, runtime);
    }

    // New folder dialog
    if state.new_folder.is_some() {
        show_new_folder_dialog(ctx, state, runtime);
    }

    // Merge folders dialog
    if state.merge_folders.is_some() {
        show_merge_dialog(ctx, state, runtime);
    }

    // Bottom panel
    egui::TopBottomPanel::bottom("bottom_panel")
        .min_height(28.0)
        .show(ctx, |ui| {
            ui.horizontal(|ui| {
                if ui
                    .add_enabled(!is_transferring, egui::Button::new(" \u{2B06} Upload "))
                    .clicked()
                {
                    start_copy(state, runtime, true);
                }
                if ui
                    .add_enabled(!is_transferring, egui::Button::new(" \u{2B07} Download "))
                    .clicked()
                {
                    start_copy(state, runtime, false);
                }
                // Determine which remote pane has a selection (host takes priority)
                let has_remote_sel = !state.remote.selected.is_empty();
                let has_host_sel = !state.host.selected.is_empty();
                let active_remote_pane = if has_host_sel {
                    Some((DeleteTarget::Host, PaneId::Host))
                } else if has_remote_sel {
                    Some((DeleteTarget::Remote, PaneId::Remote))
                } else {
                    None
                };

                // Delete: operates on whichever remote pane has a selection
                if ui
                    .add_enabled(
                        !is_transferring && active_remote_pane.is_some(),
                        egui::Button::new(" \u{1F5D1} Delete "),
                    )
                    .clicked()
                {
                    let (target, pane_id) = active_remote_pane.unwrap();
                    let pane = match pane_id {
                        PaneId::Host => &state.host,
                        _ => &state.remote,
                    };
                    let entries: Vec<fs::FileEntry> = pane
                        .selected
                        .iter()
                        .filter_map(|&i| pane.entries.get(i).cloned())
                        .collect();
                    if !entries.is_empty() {
                        state.confirm_delete = Some((target, entries));
                    }
                }

                // New Folder: defaults to Remote, or Host if host has selection
                if ui.button(" \u{1F4C1}\u{207A} New Folder ").clicked() {
                    let pane_id = if has_host_sel {
                        PaneId::Host
                    } else {
                        PaneId::Remote
                    };
                    state.new_folder = Some((pane_id, String::new()));
                }

                // Merge: operates on whichever remote pane has directory selections
                {
                    let dir_count = |pane: &PaneState| {
                        pane.selected
                            .iter()
                            .filter(|&&i| pane.entries.get(i).is_some_and(|e| e.is_dir && e.name != ".."))
                            .count()
                    };
                    let merge_pane = if dir_count(&state.host) >= 2 {
                        Some(PaneId::Host)
                    } else if dir_count(&state.remote) >= 2 {
                        Some(PaneId::Remote)
                    } else {
                        None
                    };
                    if ui
                        .add_enabled(merge_pane.is_some(), egui::Button::new(" \u{1F500} Merge "))
                        .on_hover_text("Merge contents of selected folders into a new folder")
                        .clicked()
                    {
                        let pane_id = merge_pane.unwrap();
                        let pane = match pane_id {
                            PaneId::Host => &state.host,
                            _ => &state.remote,
                        };
                        let folders: Vec<fs::FileEntry> = pane
                            .selected
                            .iter()
                            .filter_map(|&i| pane.entries.get(i).cloned())
                            .filter(|e| e.is_dir && e.name != "..")
                            .collect();
                        state.merge_folders = Some((pane_id, folders, String::new(), true));
                    }
                }

                // Copy/Move: only when Host pane is visible (needs two remote panes)
                if state.show_host {
                    ui.separator();
                    let has_any = has_remote_sel || has_host_sel;

                    if ui
                        .add_enabled(has_any, egui::Button::new(" \u{1F4CB} Copy "))
                        .on_hover_text("Copy selected files to the other pane's directory")
                        .clicked()
                    {
                        let (src_pane, dst_pane) = if has_host_sel {
                            (&state.host, &state.remote)
                        } else {
                            (&state.remote, &state.host)
                        };
                        let entries: Vec<fs::FileEntry> = src_pane
                            .selected
                            .iter()
                            .filter_map(|&i| src_pane.entries.get(i).cloned())
                            .collect();
                        let src = src_pane.path.clone();
                        let dst = dst_pane.path.clone();
                        match runtime.block_on(fs::copy_remote(
                            &state.handle, &src, &entries, &dst,
                        )) {
                            Ok(n) => state.status = format!("Copied {} item(s)", n),
                            Err(e) => state.status = format!("Copy error: {}", e),
                        }
                        refresh_remote_pane(&state.sftp, runtime, &mut state.remote);
                        refresh_remote_pane(&state.sftp, runtime, &mut state.host);
                    }

                    if ui
                        .add_enabled(has_any, egui::Button::new(" \u{2702} Move "))
                        .on_hover_text("Move selected files to the other pane's directory")
                        .clicked()
                    {
                        let (src_pane, dst_pane) = if has_host_sel {
                            (&state.host, &state.remote)
                        } else {
                            (&state.remote, &state.host)
                        };
                        let entries: Vec<fs::FileEntry> = src_pane
                            .selected
                            .iter()
                            .filter_map(|&i| src_pane.entries.get(i).cloned())
                            .collect();
                        let src = src_pane.path.clone();
                        let dst = dst_pane.path.clone();
                        match runtime.block_on(fs::move_remote(
                            &state.handle, &src, &entries, &dst,
                        )) {
                            Ok(n) => state.status = format!("Moved {} item(s)", n),
                            Err(e) => state.status = format!("Move error: {}", e),
                        }
                        refresh_remote_pane(&state.sftp, runtime, &mut state.remote);
                        refresh_remote_pane(&state.sftp, runtime, &mut state.host);
                    }
                }

                if is_transferring {
                    if ui
                        .button(egui::RichText::new("\u{2716} Cancel").color(egui::Color32::RED))
                        .clicked()
                    {
                        if let TransferState::InProgress { cancel, handle, .. } =
                            &state.transfer_state
                        {
                            cancel.store(true, Ordering::Relaxed);
                            handle.abort(); // immediately kill the task
                        }
                    }
                }

                ui.separator();

                // Status / progress display
                match &state.transfer_state {
                    TransferState::Idle => {
                        ui.label(&state.status);
                    }
                    TransferState::InProgress { progress, .. } => {
                        let p = progress.lock().unwrap().clone();

                        ui.spinner();

                        // Progress bar: use bytes when known, else file count
                        let fraction = if p.bytes_total > 0 {
                            p.bytes_done as f32 / p.bytes_total as f32
                        } else if p.files_total > 0 {
                            p.files_done as f32 / p.files_total as f32
                        } else {
                            0.0
                        };
                        ui.add(
                            egui::ProgressBar::new(fraction)
                                .desired_width(120.0)
                                .show_percentage(),
                        );

                        // Speed
                        let elapsed = p
                            .started_at
                            .map(|t| t.elapsed().as_secs_f64())
                            .unwrap_or(0.0);
                        let speed_str = if elapsed > 0.5 && p.bytes_done > 0 {
                            let bps = p.bytes_done as f64 / elapsed;
                            format!("{}/s", format_size(bps as u64))
                        } else {
                            "...".to_string()
                        };

                        let text = format!(
                            "[{}/{}] {}  {} - {}",
                            p.files_done,
                            p.files_total,
                            p.current_file,
                            format_size(p.bytes_done),
                            speed_str,
                        );
                        ui.label(egui::RichText::new(text).color(egui::Color32::YELLOW));
                    }
                    TransferState::Done {
                        message, is_error, ..
                    } => {
                        let color = if *is_error {
                            egui::Color32::RED
                        } else {
                            egui::Color32::GREEN
                        };
                        ui.label(egui::RichText::new(message).color(color));
                    }
                }

                // Right-aligned buttons
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if ui.button("\u{2699} Settings").clicked() {
                        state.show_settings = !state.show_settings;
                    }
                    let host_label = if state.show_host {
                        "\u{1F4C2} Host"
                    } else {
                        "\u{1F4C1} Host"
                    };
                    if ui.button(host_label).clicked() {
                        state.show_host = !state.show_host;
                    }
                });
            });
        });

    // Left panel: local files
    let local_width = if state.show_host {
        ctx.screen_rect().width() / 3.0 - 10.0
    } else {
        ctx.screen_rect().width() / 2.0 - 10.0
    };
    let local_response = egui::SidePanel::left("local_panel")
        .default_width(local_width)
        .resizable(true)
        .show(ctx, |ui| {
            render_pane_header(ui, "Local", &state.local.path);
            render_file_list(ui, &mut state.local, PaneId::Local)
        });

    if let Some(payload) = local_response.response.dnd_release_payload::<DragPayload>() {
        match payload.source {
            PaneId::Remote | PaneId::Host if !is_transferring => {
                // Download from remote/host to local via SCP
                start_copy_entries(state, runtime, false, &payload.entries, &payload.src_path);
            }
            _ => {}
        }
    }
    if let Some(action) = local_response.inner {
        handle_local_action(state, action);
    }

    // Right panel: host files (only when toggled on)
    if state.show_host {
        let host_response = egui::SidePanel::right("host_panel")
            .default_width(ctx.screen_rect().width() / 3.0 - 10.0)
            .resizable(true)
            .show(ctx, |ui| {
                render_pane_header(ui, "Host", &state.host.path);
                render_file_list(ui, &mut state.host, PaneId::Host)
            });

        if let Some(payload) = host_response.response.dnd_release_payload::<DragPayload>() {
            match payload.source {
                PaneId::Local if !is_transferring => {
                    // Upload from local to host's current dir via SCP
                    start_copy_entries(state, runtime, true, &payload.entries, &payload.src_path);
                }
                PaneId::Remote => {
                    // Remote copy within the remote host
                    let src = payload.src_path.clone();
                    let dst = state.host.path.clone();
                    match runtime.block_on(fs::copy_remote(
                        &state.handle, &src, &payload.entries, &dst,
                    )) {
                        Ok(n) => state.status = format!("Copied {} item(s)", n),
                        Err(e) => state.status = format!("Copy error: {}", e),
                    }
                    refresh_remote_pane(&state.sftp, runtime, &mut state.remote);
                    refresh_remote_pane(&state.sftp, runtime, &mut state.host);
                }
                _ => {}
            }
        }
        if let Some(action) = host_response.inner {
            handle_host_action(state, runtime, action);
        }
    }

    // Central panel: remote files
    let remote_response = egui::CentralPanel::default().show(ctx, |ui| {
        render_pane_header(ui, "Remote", &state.remote.path);
        render_file_list(ui, &mut state.remote, PaneId::Remote)
    });

    if let Some(payload) = remote_response.response.dnd_release_payload::<DragPayload>() {
        if payload.source == PaneId::Local && !is_transferring {
            start_copy_entries(state, runtime, true, &payload.entries, &payload.src_path);
        }
    }
    if let Some(action) = remote_response.inner {
        handle_remote_action(state, runtime, action);
    }
}

// ── Settings Window ────────────────────────────────────────────────────

fn show_settings_window(ctx: &egui::Context, state: &mut BrowserState) {
    let mut open = state.show_settings;

    egui::Window::new("\u{2699} Settings")
        .open(&mut open)
        .resizable(false)
        .default_width(400.0)
        .anchor(egui::Align2::CENTER_CENTER, [0.0, 0.0])
        .show(ctx, |ui| {
            ui.add_space(8.0);

            egui::Grid::new("settings_grid")
                .num_columns(2)
                .spacing([10.0, 10.0])
                .show(ui, |ui| {
                    ui.label("Default local path:");
                    ui.add(
                        egui::TextEdit::singleline(&mut state.settings_draft.default_local_path)
                            .desired_width(280.0),
                    );
                    ui.end_row();

                    ui.label("Default remote path:");
                    ui.add(
                        egui::TextEdit::singleline(&mut state.settings_draft.default_remote_path)
                            .desired_width(280.0),
                    );
                    ui.end_row();

                    ui.label("Default host path:");
                    ui.add(
                        egui::TextEdit::singleline(&mut state.settings_draft.default_host_path)
                            .desired_width(280.0),
                    );
                    ui.end_row();

                    ui.label("Auto-connect:");
                    ui.checkbox(
                        &mut state.settings_draft.auto_connect,
                        "Connect to last session on launch",
                    );
                    ui.end_row();
                });

            ui.add_space(12.0);

            ui.horizontal(|ui| {
                if ui.button("  Save  ").clicked() {
                    save_settings(&state.settings_draft);
                    state.status = "Settings saved".to_string();
                    state.show_settings = false;
                }
                if ui.button("  Reset to defaults  ").clicked() {
                    state.settings_draft = AppSettings::default();
                }
            });
        });

    state.show_settings = open;
}

fn show_delete_confirm(
    ctx: &egui::Context,
    state: &mut BrowserState,
    runtime: &tokio::runtime::Runtime,
) {
    let (target, entries) = state.confirm_delete.as_ref().unwrap();
    let count = entries.len();
    let target_label = match target {
        DeleteTarget::Remote => "remote host",
        DeleteTarget::Host => "local host",
    };

    let mut action = None; // None = keep open, Some(true) = delete, Some(false) = cancel

    egui::Window::new("Confirm Delete")
        .collapsible(false)
        .resizable(false)
        .anchor(egui::Align2::CENTER_CENTER, [0.0, 0.0])
        .show(ctx, |ui| {
            ui.label(format!(
                "Delete {} item(s) from the {}?",
                count, target_label
            ));
            ui.add_space(4.0);

            egui::ScrollArea::vertical()
                .max_height(200.0)
                .show(ui, |ui| {
                    for entry in entries {
                        let icon = if entry.is_dir { "\u{1F4C1}" } else { "\u{1F4C4}" };
                        ui.label(format!("{} {}", icon, entry.name));
                    }
                });

            ui.add_space(8.0);
            ui.horizontal(|ui| {
                if ui
                    .button(egui::RichText::new("Delete").color(egui::Color32::RED))
                    .clicked()
                {
                    action = Some(true);
                }
                if ui.button("Cancel").clicked() {
                    action = Some(false);
                }
            });
        });

    match action {
        Some(true) => {
            let (target, entries) = state.confirm_delete.take().unwrap();
            match target {
                DeleteTarget::Remote => {
                    match runtime.block_on(fs::delete_remote(&state.sftp, &state.remote.path, &entries)) {
                        Ok(n) => state.status = format!("Deleted {} item(s)", n),
                        Err(e) => state.status = format!("Delete error: {}", e),
                    }
                    if let Ok(entries) = runtime.block_on(fs::list_remote(&state.sftp, &state.remote.path)) {
                        state.remote.entries = entries;
                        state.remote.selected.clear();
                    }
                }
                DeleteTarget::Host => {
                    match runtime.block_on(fs::delete_remote(&state.sftp, &state.host.path, &entries)) {
                        Ok(n) => state.status = format!("Deleted {} item(s)", n),
                        Err(e) => state.status = format!("Delete error: {}", e),
                    }
                    if let Ok(entries) = runtime.block_on(fs::list_remote(&state.sftp, &state.host.path)) {
                        state.host.entries = entries;
                        state.host.selected.clear();
                    }
                }
            }
        }
        Some(false) => {
            state.confirm_delete = None;
        }
        None => {}
    }
}

fn show_new_folder_dialog(
    ctx: &egui::Context,
    state: &mut BrowserState,
    runtime: &tokio::runtime::Runtime,
) {
    let mut action = None; // None = keep open, Some(true) = create, Some(false) = cancel

    let (pane_id, _) = state.new_folder.as_ref().unwrap();
    let pane_label = match pane_id {
        PaneId::Remote => "Remote",
        PaneId::Host => "Host",
        PaneId::Local => "Local",
    };

    egui::Window::new("New Folder")
        .collapsible(false)
        .resizable(false)
        .anchor(egui::Align2::CENTER_CENTER, [0.0, 0.0])
        .show(ctx, |ui| {
            ui.label(format!("Create folder in {} pane:", pane_label));
            ui.add_space(4.0);

            let (_, name) = state.new_folder.as_mut().unwrap();
            let te = ui.add(
                egui::TextEdit::singleline(name)
                    .desired_width(250.0)
                    .hint_text("folder name"),
            );
            // Auto-focus the text field
            te.request_focus();

            if ui.input(|i| i.key_pressed(egui::Key::Enter)) && !name.trim().is_empty() {
                action = Some(true);
            }

            ui.add_space(8.0);
            ui.horizontal(|ui| {
                if ui
                    .add_enabled(
                        !state.new_folder.as_ref().unwrap().1.trim().is_empty(),
                        egui::Button::new("Create"),
                    )
                    .clicked()
                {
                    action = Some(true);
                }
                if ui.button("Cancel").clicked() {
                    action = Some(false);
                }
            });
        });

    match action {
        Some(true) => {
            let (pane_id, name) = state.new_folder.take().unwrap();
            let name = name.trim().to_string();
            let pane = match pane_id {
                PaneId::Remote => &mut state.remote,
                PaneId::Host => &mut state.host,
                PaneId::Local => &mut state.local,
            };
            let new_dir = format!("{}/{}", pane.path.trim_end_matches('/'), name);
            match runtime.block_on(state.sftp.create_dir(&new_dir)) {
                Ok(()) => {
                    state.status = format!("Created folder: {}", name);
                    // Refresh the pane
                    if let Ok(entries) =
                        runtime.block_on(fs::list_remote(&state.sftp, &pane.path))
                    {
                        pane.entries = entries;
                        pane.selected.clear();
                    }
                }
                Err(e) => state.status = format!("Error creating folder: {}", e),
            }
        }
        Some(false) => {
            state.new_folder = None;
        }
        None => {}
    }
}

fn show_merge_dialog(
    ctx: &egui::Context,
    state: &mut BrowserState,
    runtime: &tokio::runtime::Runtime,
) {
    let (_, folders, _, _) = state.merge_folders.as_ref().unwrap();
    let folder_count = folders.len();

    let mut action = None;

    egui::Window::new("Merge Folders")
        .collapsible(false)
        .resizable(false)
        .anchor(egui::Align2::CENTER_CENTER, [0.0, 0.0])
        .show(ctx, |ui| {
            ui.label(format!(
                "Merge contents of {} folder(s) into a new folder:",
                folder_count
            ));
            ui.add_space(4.0);

            egui::ScrollArea::vertical()
                .max_height(120.0)
                .show(ui, |ui| {
                    let (_, folders, _, _) = state.merge_folders.as_ref().unwrap();
                    for f in folders {
                        ui.label(format!("\u{1F4C1} {}", f.name));
                    }
                });

            ui.add_space(8.0);
            let (_, _, name, delete_originals) = state.merge_folders.as_mut().unwrap();
            let te = ui.add(
                egui::TextEdit::singleline(name)
                    .desired_width(250.0)
                    .hint_text("new folder name"),
            );
            te.request_focus();

            ui.add_space(4.0);
            ui.checkbox(delete_originals, "Delete original folders after merge");

            if ui.input(|i| i.key_pressed(egui::Key::Enter)) && !name.trim().is_empty() {
                action = Some(true);
            }

            ui.add_space(8.0);
            ui.horizontal(|ui| {
                if ui
                    .add_enabled(
                        !state.merge_folders.as_ref().unwrap().2.trim().is_empty(),
                        egui::Button::new("Merge"),
                    )
                    .clicked()
                {
                    action = Some(true);
                }
                if ui.button("Cancel").clicked() {
                    action = Some(false);
                }
            });
        });

    match action {
        Some(true) => {
            let (pane_id, folders, name, delete_originals) = state.merge_folders.take().unwrap();
            let name = name.trim().to_string();
            let base_path = match pane_id {
                PaneId::Host => state.host.path.clone(),
                _ => state.remote.path.clone(),
            };
            let new_dir = format!("{}/{}", base_path.trim_end_matches('/'), name);

            // Create the target folder, then merge contents
            match runtime.block_on(state.sftp.create_dir(&new_dir)) {
                Ok(()) => {
                    match runtime.block_on(fs::merge_folders_remote(
                        &state.handle,
                        &base_path,
                        &folders,
                        &new_dir,
                        delete_originals,
                    )) {
                        Ok(n) => {
                            if delete_originals {
                                state.status = format!(
                                    "Merged {} folder(s) into {} (originals deleted)",
                                    n, name
                                );
                            } else {
                                state.status =
                                    format!("Merged {} folder(s) into {}", n, name);
                            }
                        }
                        Err(e) => state.status = format!("Merge error: {}", e),
                    }
                }
                Err(e) => state.status = format!("Error creating folder: {}", e),
            }
            // Refresh the pane
            let pane = match pane_id {
                PaneId::Host => &mut state.host,
                _ => &mut state.remote,
            };
            if let Ok(entries) = runtime.block_on(fs::list_remote(&state.sftp, &base_path)) {
                pane.entries = entries;
                pane.selected.clear();
            }
        }
        Some(false) => {
            state.merge_folders = None;
        }
        None => {}
    }
}

// ── Pane Rendering ─────────────────────────────────────────────────────

fn render_pane_header(ui: &mut egui::Ui, title: &str, path: &str) {
    ui.horizontal(|ui| {
        ui.strong(title);
        ui.separator();
        ui.label(path);
    });
    ui.separator();
}

enum PaneAction {
    EnterDir(String),
    GoParent,
}

fn render_file_list(
    ui: &mut egui::Ui,
    pane: &mut PaneState,
    pane_id: PaneId,
) -> Option<PaneAction> {
    let mut action: Option<PaneAction> = None;

    egui::ScrollArea::vertical().show(ui, |ui| {
        ui.style_mut().spacing.item_spacing.y = 1.0;

        for (i, entry) in pane.entries.iter().enumerate() {
            let is_parent = i == 0 && entry.name == "..";
            let is_selected = pane.selected.contains(&i);

            let row_response = {
                let available_width = ui.available_width();
                let desired_size = egui::vec2(available_width, 22.0);
                let (rect, response) =
                    ui.allocate_exact_size(desired_size, egui::Sense::click_and_drag());

                if response.hovered() {
                    ui.painter()
                        .rect_filled(rect, 2.0, ui.visuals().widgets.hovered.bg_fill);
                }
                if is_selected {
                    ui.painter().rect_filled(
                        rect,
                        2.0,
                        egui::Color32::from_rgba_premultiplied(100, 149, 237, 40),
                    );
                }
                if response.hovered() {
                    ui.ctx().set_cursor_icon(egui::CursorIcon::PointingHand);
                }

                let text_rect = rect.shrink2(egui::vec2(4.0, 0.0));
                let size_reserved: f32 = 80.0;

                if !is_parent {
                    let check = if is_selected { "\u{2611}" } else { "\u{2610}" };
                    ui.painter().text(
                        text_rect.left_center(),
                        egui::Align2::LEFT_CENTER,
                        check,
                        egui::FontId::proportional(14.0),
                        ui.visuals().text_color(),
                    );
                }

                let name_offset = if is_parent { 0.0 } else { 20.0 };
                let icon = if entry.is_dir {
                    "\u{1F4C1} "
                } else {
                    "\u{1F4C4} "
                };
                let name_color = if entry.is_dir {
                    egui::Color32::from_rgb(100, 149, 237)
                } else {
                    ui.visuals().text_color()
                };

                let name_clip = egui::Rect::from_min_max(
                    egui::pos2(text_rect.left() + name_offset, rect.top()),
                    egui::pos2(text_rect.right() - size_reserved, rect.bottom()),
                );
                ui.painter_at(name_clip).text(
                    egui::pos2(text_rect.left() + name_offset, text_rect.center().y),
                    egui::Align2::LEFT_CENTER,
                    format!("{}{}", icon, entry.name),
                    egui::FontId::proportional(14.0),
                    name_color,
                );

                let size_text = if entry.is_dir {
                    "<DIR>".to_string()
                } else {
                    format_size(entry.size)
                };
                ui.painter().text(
                    text_rect.right_center(),
                    egui::Align2::RIGHT_CENTER,
                    size_text,
                    egui::FontId::proportional(13.0),
                    ui.visuals().weak_text_color(),
                );

                if !is_parent {
                    let drag_entries = if is_selected {
                        pane.selected
                            .iter()
                            .filter_map(|&idx| pane.entries.get(idx).cloned())
                            .collect()
                    } else {
                        vec![entry.clone()]
                    };
                    response.dnd_set_drag_payload(DragPayload {
                        source: pane_id,
                        entries: drag_entries,
                        src_path: pane.path.clone(),
                    });
                }

                response
            };

            if entry.is_dir && row_response.double_clicked() {
                action = Some(if is_parent {
                    PaneAction::GoParent
                } else {
                    PaneAction::EnterDir(entry.name.clone())
                });
            } else if row_response.clicked() && !is_parent {
                let modifiers = ui.input(|i| i.modifiers);
                if modifiers.shift && pane.last_clicked.is_some() {
                    // Shift+click: select range from last_clicked to current
                    let anchor = pane.last_clicked.unwrap();
                    let lo = anchor.min(i);
                    let hi = anchor.max(i);
                    if !modifiers.ctrl && !modifiers.command {
                        pane.selected.clear();
                    }
                    for idx in lo..=hi {
                        if idx != 0 || pane.entries.first().is_none_or(|e| e.name != "..") {
                            pane.selected.insert(idx);
                        }
                    }
                } else if modifiers.ctrl || modifiers.command {
                    // Ctrl+click: toggle single item
                    if is_selected {
                        pane.selected.remove(&i);
                    } else {
                        pane.selected.insert(i);
                    }
                    pane.last_clicked = Some(i);
                } else {
                    // Plain click: select only this item
                    pane.selected.clear();
                    pane.selected.insert(i);
                    pane.last_clicked = Some(i);
                }
            }
        }
    });

    action
}

// ── Navigation ─────────────────────────────────────────────────────────

fn navigate_local_pane(pane: &mut PaneState, action: PaneAction, status: &mut String) {
    let new_path = match action {
        PaneAction::EnterDir(name) => PathBuf::from(&pane.path)
            .join(&name)
            .to_string_lossy()
            .to_string(),
        PaneAction::GoParent => PathBuf::from(&pane.path)
            .parent()
            .map(|p| p.to_string_lossy().to_string())
            .unwrap_or_else(|| pane.path.clone()),
    };
    match fs::list_local(&PathBuf::from(&new_path)) {
        Ok(entries) => {
            pane.path = new_path;
            pane.entries = entries;
            pane.selected.clear();
        }
        Err(e) => *status = format!("Error: {}", e),
    }
}

fn handle_local_action(state: &mut BrowserState, action: PaneAction) {
    navigate_local_pane(&mut state.local, action, &mut state.status);
}

fn handle_host_action(
    state: &mut BrowserState,
    runtime: &tokio::runtime::Runtime,
    action: PaneAction,
) {
    navigate_remote_pane(&state.sftp, runtime, &mut state.host, action, &mut state.status);
}

fn refresh_remote_pane(
    sftp: &Arc<SftpSession>,
    runtime: &tokio::runtime::Runtime,
    pane: &mut PaneState,
) {
    if let Ok(entries) = runtime.block_on(fs::list_remote(sftp, &pane.path)) {
        pane.entries = entries;
        pane.selected.clear();
    }
}

fn navigate_remote_pane(
    sftp: &Arc<SftpSession>,
    runtime: &tokio::runtime::Runtime,
    pane: &mut PaneState,
    action: PaneAction,
    status: &mut String,
) {
    let new_path = match action {
        PaneAction::EnterDir(name) => {
            format!("{}/{}", pane.path.trim_end_matches('/'), name)
        }
        PaneAction::GoParent => {
            let p = pane.path.trim_end_matches('/');
            match p.rfind('/') {
                Some(0) | None => "/".to_string(),
                Some(i) => p[..i].to_string(),
            }
        }
    };
    match runtime.block_on(fs::list_remote(sftp, &new_path)) {
        Ok(entries) => {
            pane.path = new_path;
            pane.entries = entries;
            pane.selected.clear();
        }
        Err(e) => *status = format!("Error: {}", e),
    }
}

fn handle_remote_action(
    state: &mut BrowserState,
    runtime: &tokio::runtime::Runtime,
    action: PaneAction,
) {
    navigate_remote_pane(&state.sftp, runtime, &mut state.remote, action, &mut state.status);
}

// ── File Transfer ──────────────────────────────────────────────────────

fn start_copy(state: &mut BrowserState, runtime: &tokio::runtime::Runtime, upload: bool) {
    let (indices, src_path) = if upload {
        (
            state.local.selected.iter().copied().collect::<Vec<_>>(),
            state.local.path.clone(),
        )
    } else {
        (
            state.remote.selected.iter().copied().collect::<Vec<_>>(),
            state.remote.path.clone(),
        )
    };

    if indices.is_empty() {
        state.status = "Select files first".to_string();
        return;
    }

    let pane = if upload { &state.local } else { &state.remote };
    let entries: Vec<fs::FileEntry> = indices
        .iter()
        .filter_map(|&i| pane.entries.get(i).cloned())
        .collect();

    start_copy_entries(state, runtime, upload, &entries, &src_path);
}

fn start_copy_entries(
    state: &mut BrowserState,
    runtime: &tokio::runtime::Runtime,
    upload: bool,
    entries: &[fs::FileEntry],
    src_path: &str,
) {
    if entries.is_empty() {
        return;
    }

    let sftp = Arc::clone(&state.sftp);
    let items = entries.to_vec();
    let total = items.len();
    let src = src_path.to_string();
    let dst = if upload {
        state.remote.path.clone()
    } else {
        state.local.path.clone()
    };

    let progress = Arc::new(Mutex::new(TransferProgress {
        current_file: "Calculating size...".to_string(),
        files_done: 0,
        files_total: total,
        bytes_done: 0,
        bytes_total: 0,
        started_at: Some(std::time::Instant::now()),
    }));

    let cancel = Arc::new(AtomicBool::new(false));

    // Open SCP channels on the main thread (one per item)
    // Each channel gets exec'd with the appropriate scp command
    let mut channels = Vec::new();
    for entry in &items {
        let channel_result = if upload {
            let remote_target = format!("{}/{}", dst.trim_end_matches('/'), entry.name);
            // For upload: scp -r -t <remote_dir>
            let cmd = if entry.is_dir {
                format!("scp -r -t {}", shell_escape(&dst))
            } else {
                format!("scp -t {}", shell_escape(&remote_target))
            };
            runtime.block_on(open_scp_channel(&state.handle, &cmd))
        } else {
            let remote_path = format!("{}/{}", src.trim_end_matches('/'), entry.name);
            let cmd = if entry.is_dir {
                format!("scp -r -f {}", shell_escape(&remote_path))
            } else {
                format!("scp -f {}", shell_escape(&remote_path))
            };
            runtime.block_on(open_scp_channel(&state.handle, &cmd))
        };

        match channel_result {
            Ok(stream) => channels.push(stream),
            Err(e) => {
                state.status = format!("Failed to open SCP channel: {}", e);
                return;
            }
        }
    }

    let progress_clone = Arc::clone(&progress);
    let cancel_clone = Arc::clone(&cancel);

    let handle = runtime.spawn(async move {
        // Compute total bytes for progress bar
        let mut total_bytes = 0u64;
        for entry in &items {
            if cancel_clone.load(Ordering::Relaxed) {
                return Err("Cancelled".to_string());
            }
            if upload {
                let local_path = PathBuf::from(&src).join(&entry.name);
                total_bytes += transfer::local_total_bytes(&local_path);
            } else {
                total_bytes +=
                    transfer::remote_total_bytes(&sftp, &format!("{}/{}", src.trim_end_matches('/'), entry.name), entry.is_dir).await;
            }
        }
        {
            let mut p = progress_clone.lock().unwrap();
            p.bytes_total = total_bytes;
            p.current_file = String::new();
            p.started_at = Some(std::time::Instant::now());
        }

        let mut total_files = 0usize;

        for (entry, mut stream) in items.iter().zip(channels) {
            if cancel_clone.load(Ordering::Relaxed) {
                return Err("Cancelled".to_string());
            }

            {
                let mut p = progress_clone.lock().unwrap();
                p.current_file = entry.name.clone();
            }

            let result = if upload {
                let local_path = PathBuf::from(&src).join(&entry.name);
                transfer::scp_upload(&mut stream, &local_path, &progress_clone, &cancel_clone).await
            } else {
                let local_base = PathBuf::from(&dst);
                transfer::scp_download(&mut stream, &local_base, &progress_clone, &cancel_clone).await
            };

            match result {
                Ok(count) => total_files += count,
                Err(e) => return Err(e.to_string()),
            }
        }

        Ok(total_files)
    });

    state.transfer_state = TransferState::InProgress {
        progress,
        handle,
        cancel,
    };
}

async fn open_scp_channel(
    handle: &client::Handle<ssh::Handler>,
    command: &str,
) -> anyhow::Result<russh::ChannelStream<russh::client::Msg>> {
    let channel = handle.channel_open_session().await?;
    channel.exec(true, command).await?;
    Ok(channel.into_stream())
}

fn shell_escape(s: &str) -> String {
    // Simple shell escaping: wrap in single quotes, escape existing single quotes
    format!("'{}'", s.replace('\'', "'\\''"))
}

// ── Helpers ────────────────────────────────────────────────────────────

fn format_size(bytes: u64) -> String {
    if bytes < 1024 {
        format!("{} B", bytes)
    } else if bytes < 1024 * 1024 {
        format!("{:.1} KB", bytes as f64 / 1024.0)
    } else if bytes < 1024 * 1024 * 1024 {
        format!("{:.1} MB", bytes as f64 / (1024.0 * 1024.0))
    } else {
        format!("{:.1} GB", bytes as f64 / (1024.0 * 1024.0 * 1024.0))
    }
}
