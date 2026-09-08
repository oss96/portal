---
name: verify
description: Verify Portal - which test suites exist, what each covers, and how to run them. Use before claiming a Portal change works, when asked to test the app, or when deciding whether a change needs a UI check.
---

# Verifying Portal

Portal has no CI. Everything below runs on the development machine and needs no
window, so there is nothing to schedule on the guest VM.

## 1. Unit tests — always

```
cargo test
```

Nine tests, all offline. They cover release-version parsing and comparison,
the release-host allowlist, the update-selection rules (newer only, drafts and
prereleases skipped, missing or foreign asset rejected) and the `.old` / `.new`
sibling paths.

## 2. Network tests — when the updater or the release flow changed

```
cargo test -- --ignored
```

Three tests against the live Forgejo instance: the release feed still has the
shape the client expects, a check driven through the app's own multi-threaded
runtime reports back the way the UI reads it, and a real download of the
published `portal.exe` plus the rename swap, in a scratch directory. They need
`git.ossalali.com` to be reachable.

## 3. Screenshot harness — when anything on screen changed

```
cargo run --features shots
```

Renders 15 scenes offscreen through `egui_kittest` and `egui-wgpu` on a CPU
adapter and writes them to `target/shots/` with an `index.json`. No window is
opened and no input is sent anywhere, so it is deterministic and needs no GPU.
**Open the PNGs and look at them** — the harness proves the frame rendered, not
that it is right.

Scenes: the connect view (dark and light), a local pane plain and with rows
selected, a remote pane with the permission columns (dark and light), the
Updates section in all seven states, and the transfer rows in all four
statuses. Add a scene by adding a `Scene` to `scenes()` in `src/app/shots.rs`.

## What none of this covers

The browser view holds a live `russh` handle and an `SftpSession`, so it cannot
be built from a fixture: the two-pane layout as a whole, the toolbar (including
the update chip), the drag-and-drop between panes, the dialogs, and real mouse
input are not exercised. Neither is window creation or the relaunch after an
update. Reaching those would need either an SFTP trait seam so the panes can be
driven from a fake, or a guest suite in `Forgejo-VM-MCP` that launches
`portal.exe`. Neither exists yet — say so rather than implying they passed.
