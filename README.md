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

Requires Rust 1.95+ (edition 2024; egui 0.36 sets the floor).

```bash
cargo build --release
```

The binary is at `target/release/portal.exe`.

## Testing

```bash
cargo test                    # offline unit tests
cargo test -- --ignored       # tests that talk to the live release feed
cargo run --features shots    # render the UI offscreen to target/shots/
```

The screenshot harness (`src/app/shots.rs`) runs the egui pass in-process and
rasterises it on a CPU adapter, so it opens no window and needs no GPU. The
browser view needs a live SFTP session, so only the connect view, a file pane,
the updates section and the transfer rows are captured.

## Releasing

There is no CI. A release is built on the development machine and published to
the Forgejo repository by `.claude/skills/release/release.sh`, which bumps the
version in `Cargo.toml`, builds `portal.exe`, tags `X.Y.Z`, pushes, and uploads
the binary to a new release. Run it with `--dry-run` first to see the plan.

## Updating

Portal updates itself from the releases of its own repository. On launch it asks
Forgejo for the latest release and, when the tag is newer than the running build,
offers it in the status bar and under **Settings → Updates**. Installing downloads
`portal.exe` next to the running executable, renames the old binary to
`portal.exe.old`, and puts the new one in its place; restarting runs it. The
`.old` file is deleted on the next launch, once it is no longer in use.

Only `https://git.ossalali.com` is accepted as a source, the download must match
the size the release advertises, and it must be a Windows executable. Drafts and
prereleases are ignored. The launch check can be turned off in Settings.

Portal has to be able to write to its own directory for this to work, so an
install under `C:\Program Files` needs elevation.

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
