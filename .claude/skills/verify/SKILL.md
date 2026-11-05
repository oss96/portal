---
name: verify
description: Verify Portal - which test suites exist, what each covers, and how to run them. Use before claiming a Portal change works, when asked to test the app, or when deciding whether a change needs a UI check.
---

# Verifying Portal

Portal has no CI. The first three checks run on the development machine and open no window. The live-window check runs on the Windows CI VM.

## 1. Unit tests: always

```
dotnet test Portal.slnx
```

About 150 offline tests:

- **Formatting and paths:** permission formatting, remote paths, parsing of `df`, `find` and passwd output.
- **SSH setup:** SSH config resolution, known_hosts matching (hashed entries, wildcards, revoked keys), shell quoting.
- **Transfer engine:** over an in-memory server. Conflicts, keep-both names, apply-to-rest, a failed item next to a good one, retry resuming from the partial file.
- **Settings:** including the import of the old Portal's files.
- **Updater:** version rules, host allowlist, release selection, download checks and the swap.

## 2. Real SSH server: when anything in Portal.Core talks to a server

```
PORTAL_SSH_TESTS=1 dotnet test Portal.slnx
```

Starts two `lscr.io/linuxserver/openssh-server` containers in Docker with a freshly generated key on random localhost ports, one of them limited to SFTP (`ForceCommand internal-sftp`), and removes them afterwards. Docker must be running.

Covers:

- **Host keys:** first-connect trust and remembering the key, a declined key, a changed key.
- **Sign-in:** password, and a wrong password.
- **Files:** upload (mtime kept), folder upload and download, replace keeping the mode, rename, copy and move on the server.
- **Server commands:** `du`, `df`, search (busybox fallback) and delete.
- **Permissions:** recursive changes touch only the bits that changed.
- **Copy and move on the server:** conflicts, Keep both names, "x - Copy" in the same folder, replace by policy.
- **SFTP-only account:** connect, upload and replace, chmod, folder size and delete without a shell.
- **Host keys again:** a key of a type not on record counts as a changed identity.

## 3. Screens: when anything on screen changed

```
dotnet run --project app/Portal.Shots
dotnet run --project app/Portal.Shots -- --light
```

Renders 14 scenes per theme to `target/shots-wpf/` through `RenderTargetBitmap`, over an in-memory server: connect, connect progress and error, host-key dialog, browser, This PC next to the server, permissions, delete, conflict, settings, transfers with a failed item, empty folder, filter with no match, command search, toasts with a lost connection. No window opens.

**Open the PNGs and look at them.** The harness proves the views render, not that they are right.

Add a scene in `app/Portal.Shots/Program.cs`. Render each theme in its own process: switching `ThemeMode` at runtime does not re-theme offscreen visuals.

## 4. Live window: on the Windows CI VM, never here

Suite id `portal-ui` runs `.claude/skills/verify/suites/test-ui.ps1` in the guest through the `forgejo-vm` MCP server. Queue it the way the `vm-queue` skill says.

It runs checks 1 and 3, then starts the real `portal.exe` with a private profile (`PORTAL_CONFIG_DIR`, `PORTAL_SSH_DIR`). It drives the connect form through UI Automation patterns only, captures the window into `target/ui-check/`, and closes the window through its WindowPattern.

It needs the .NET 10 SDK in the guest, and the route added to Forgejo-VM-MCP (`run-suite-portal-ui.ps1` plus its allowlist entry).

## What none of this covers

- **Behaviour against real servers:** transfer speed against Tower, Unraid's shfs, root-owned paths, chown to other users, and fail2ban on Hetzner are not exercised.
- **Live-window interactions:** drag and drop with Explorer, and the browser screen in a real window.
- **Updates:** the relaunch after an update.

Say so rather than implying they passed.
