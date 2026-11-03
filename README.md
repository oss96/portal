# Portal

A Windows app for managing the files on your servers over SSH.

Portal puts the server first. A sidebar holds your saved servers, the server's shares and pinned folders, and how full its disks are. The main pane lists the server's files with their permissions and owner. A details pane explains the selection in plain words. This PC opens next to the server when you want to move files across.

## Features

- **Connect**:
  - Saved servers.
  - Hosts from `~/.ssh/config`.
  - Sign-in with your SSH keys (asks for a passphrase when a key has one), a chosen key file, or a password that is never stored.
  - The first connection to a server shows its host-key fingerprint and saves it to `known_hosts`. A changed key blocks the connection.
- **Browse**:
  - Breadcrumbs you can click or type into (Ctrl+L).
  - Back, forward and up.
  - A filter per pane (Ctrl+F).
  - Sortable columns.
  - Hidden files on or off (Ctrl+H).
  - Explorer-style selection with Ctrl, Shift and Ctrl+A.
- **Manage on the server**:
  - New folder.
  - Rename inline (F2).
  - Delete, with a confirmation that names the server and says it is permanent.
  - Permissions and owner as a checkbox grid or chmod value, optionally for everything inside.
  - Copy, cut and paste within the server.
  - Folder sizes and disk usage, measured on the server.
  - Search by name (Ctrl+K).
  - Open terminal here.
- **Transfer**:
  - Drag between panes or from Explorer, Upload/Download, or copy and paste between panes.
  - Existing files ask Replace, Keep both or Skip, with an option to apply the answer to the rest.
  - Data goes to a hidden `.portal-part` file that is renamed when complete, so a cut-off transfer never leaves a half-written file under the real name.
  - Modification times are kept. A replaced file keeps its mode.
  - A failed item doesn't stop the others. Retry and resume continue from the partial file.
  - Pause, cancel, and a configurable number of parallel transfers.
- **Edit on the server**: opens a server file in its usual Windows app and uploads it again every time it is saved.
- **Command search**: Ctrl+K runs any command, or finds files under the current server folder.
- **Updates**: Portal checks its own Forgejo releases and installs a new `portal.exe` in place.

## Usage

```
portal                     open the connect screen (or reconnect to the last server)
portal user@host           connect directly
portal user@host -p 2222   connect on another port
```

## Keyboard

| Action | Keys |
|---|---|
| Open a folder or file | Enter, double-click |
| Up one level | Backspace, Alt+Up |
| Back, forward | Alt+Left, Alt+Right |
| Rename | F2 |
| Delete | Del |
| New folder | Ctrl+Shift+N |
| Upload or download the selection | Ctrl+Enter |
| Copy, cut, paste | Ctrl+C, Ctrl+X, Ctrl+V |
| Type a path | Ctrl+L |
| Filter this folder | Ctrl+F |
| Hidden files | Ctrl+H |
| Refresh | F5 |
| Search commands and files | Ctrl+K |
| Settings | Ctrl+, |

## How Portal talks to a server

Each connection opens three SSH channels to the same server:

- **SFTP for browsing:** listings, rename, new folder.
- **SFTP for transfers:** kept separate so a big upload doesn't slow down browsing.
- **Shell commands:** for work that should happen on the server: `rm -rf`, `chmod`/`chown -R`, `cp -a`, `mv`, `du`, `df`, `find`.

Every path in those commands is POSIX-quoted. SMB, WebDAV, NFS and a custom agent were considered and rejected, because none of them can change permissions and owners or reach root-owned paths without a new service on the server.

## Building

Requires the .NET 10 SDK.

```
dotnet build Portal.slnx
dotnet run --project app/Portal
dotnet publish app/Portal/Portal.csproj -c Release -r win-x64 -o publish   # single-file publish/portal.exe
```

## Testing

```
dotnet test Portal.slnx                                  # unit tests, offline
PORTAL_SSH_TESTS=1 dotnet test Portal.slnx               # plus a real OpenSSH server in Docker
PORTAL_LIVE_TESTS=1 dotnet test Portal.slnx              # plus the live release feed
dotnet run --project app/Portal.Shots [-- --light]       # render every screen offscreen to target/shots-wpf
```

The screenshot harness renders the real views over an in-memory server through `RenderTargetBitmap`, so it opens no window. The live-window check runs on the Windows CI VM; see `.claude/skills/verify/SKILL.md`.

## Releasing

There is no CI. `.claude/skills/release/release.sh X.Y.Z`:

- bumps `<Version>` in `Directory.Build.props`;
- runs the tests and publishes `portal.exe`;
- tags `X.Y.Z` and pushes;
- uploads the binary to a new Forgejo release.

Run it with `--dry-run` first.

## Updating

- **Check:** On launch Portal asks Forgejo for the latest release. A newer tag shows an update chip in the status bar and under Settings → Updates.
- **Install:** Portal downloads `portal.exe.new` next to the running exe, renames the running exe to `portal.exe.old`, and moves the new file into place. Restarting runs it. Leftovers are deleted on the next launch.
- **Checks on the download:** only `https://git.ossalali.com` is accepted as a source, the size must match the release, and the file must be a Windows executable. Drafts and prereleases are ignored.

## Configuration

Stored in `%APPDATA%\portal\`:

- `servers.json`: saved servers and their pinned folders.
- `preferences.json`: theme, parallel transfers, conflict policy, reconnect and update settings, pane toggles.
- `window.json`: window size and position.

The first launch imports `sessions.json` and `settings.json` from the old Portal.

## Layout

| Path | What |
|---|---|
| `app/Portal.Core` | File systems (local, SSH), the connector, known_hosts and SSH config parsing, the transfer engine, settings, the updater |
| `app/Portal` | The WPF app (Fluent theme, MVVM) |
| `app/Portal.Tests` | Unit and integration tests |
| `app/Portal.Shots` | The offscreen screenshot harness |
| `docs/design` | The interactive design prototype |
| `DESIGN.md` | Design system and interaction rules |

## License

MIT
