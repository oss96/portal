# Portal

A dual-pane SSH file manager with a native GUI, built in Rust.

Portal connects to remote hosts via SSH and provides a side-by-side file browser for transferring files and folders between your local machine and the remote server.

## Features

- **Dual-pane file browser** - Local files on the left, remote files on the right
- **SCP transfers** - Uses the SCP protocol for fast streaming file transfers (files and folders)
- **Live progress** - Progress bar, file counter, bytes transferred, and transfer speed (MB/s)
- **Drag and drop** - Drag files between panes to upload or download
- **Session management** - Saved sessions with auto-connect option
- **Settings** - Configurable default local and remote paths
- **SSH key authentication** - Auto-discovers ed25519, RSA, and ECDSA keys from `~/.ssh/`
- **SSH config support** - Reads `~/.ssh/config` for host aliases, ports, and usernames
- **Cancel transfers** - Cancel in-progress transfers instantly

## Usage

```bash
# Launch with connection dialog
portal

# Connect directly
portal user@host

# Specify port
portal user@host -p 2222
```

## Keyboard / Mouse

| Action | How |
|---|---|
| Navigate directories | Double-click a folder |
| Go to parent | Double-click `..` |
| Select files | Click to toggle, or use checkboxes |
| Upload | Select local files, click **Upload** (or drag to remote pane) |
| Download | Select remote files, click **Download** (or drag to local pane) |

## Building

Requires Rust 1.85+ (edition 2024).

```bash
cargo build --release
```

The binary is at `target/release/portal.exe`.

## Configuration

Settings and session data are stored in `%APPDATA%/portal/`:

- `sessions.json` - Saved SSH connections
- `settings.json` - Default paths and auto-connect preference

## Architecture

| File | Purpose |
|---|---|
| `src/main.rs` | CLI parsing, font setup, eframe launch |
| `src/app.rs` | GUI application (egui) - connect dialog, file browser, settings |
| `src/ssh.rs` | SSH connection, key auth, SFTP session |
| `src/fs.rs` | File entry type, local and remote directory listing |
| `src/transfer.rs` | SCP protocol implementation for file/folder transfers |

## Dependencies

- **russh** - Pure Rust SSH client (no C dependencies)
- **russh-sftp** - SFTP for directory browsing
- **eframe/egui** - Native GUI framework
- **tokio** - Async runtime
- **clap** - CLI argument parsing

## License

MIT
