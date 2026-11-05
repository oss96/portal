# Portal design

The interactive prototype is `docs/design/portal-prototype.html`. The app follows its layout B, "Server first". The prototype's Tweaks panel still shows layouts A (twin panes) and C (workspace) for comparison.

## Layout

```
┌ command bar ───────────────────────────────────────────────────────────────┐
│ New folder | Upload/Download  Rename  Permissions  Delete  Refresh        │
│                               selection summary | Hidden files  Details   │
├ sidebar ─────┬ [This PC pane  ┬ server pane ───────────┬ details ───────────┤
│ <server>     │  or rail]      │ ← → ↑  Tower  / › mnt › │ icon, name, kind   │
│  Home, Root, │                │ name  modified  size    │ location, modified │
│  shares, pins│                │ permissions  owner      │ size / Calculate   │
│ Storage      │                │                         │ owner, permissions │
│ Servers      │                │                         │ actions            │
│ This PC      │                │ footer: counts          │ Edit on the server │
├──────────────┴────────────────┴─────────────────────────┴────────────────────┤
│ ● Connected to Tower as root  tower · 14 ms  Disconnect   Transfers  Settings │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Sidebar:** 232 px. **Details pane:** 296 px.
- **Two panes:** the This PC pane shares the middle with the server pane. While it's closed, a 44 px rail (`PaneRail` in `Themes/Styles.xaml`) holds its place at the left edge of the file area. Click the rail, or drag server files over it, to open the pane. The ‹ button at the far left of the This PC pane folds it back into the rail. Ctrl+K also has Show and Hide This PC.
- **Narrow windows:** when both panes are open and the window is narrower than 1440 px, the details pane steps aside so the file lists keep room for their columns.
- **Transfers:** a flyout above the status bar, not a permanent panel. The status bar always shows the count, time left and overall progress.
- **Dialogs:** in-window over a dimmed backdrop. Dialogs that need an answer (conflict, host key) ignore clicks on the backdrop.

## Tokens

Portal uses WPF's built-in Fluent theme (`ThemeMode`) and its resource keys, so light, dark and the Windows accent follow the system.

| Role | Resource |
|---|---|
| Window | Mica / `SolidBackgroundFillColorBaseBrush` |
| Panes | `LayerFillColorDefaultBrush` |
| Sidebar | `LayerFillColorAltBrush` |
| Cards, server tiles | `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush` |
| Hover | `SubtleFillColorTertiaryBrush` |
| Selection | `SelectionFillBrush`: the accent at 24 % alpha, plus a 3 × 16 px accent pill |
| Text | `TextFillColorPrimary`, `Secondary` (captions), `Tertiary` (hints) |
| Status | `SystemFillColorSuccess`, `Caution`, `Critical` and their `Background` variants |
| Folders | `#E0A93B` |

- **Type:**
  - Segoe UI Variable, at 12 (captions, metadata), 14 (body), 20 (dialog and pane titles) and 28 (screen titles).
  - Cascadia Mono 12 for paths, modes and fingerprints.
- **Icons:** Segoe Fluent Icons glyphs, listed in `Glyphs.cs`.
- **Spacing:** 4, 8, 12, 16, 24, 32.
- **Corners:** 4 px for controls, 8 px for cards and overlays.
- **Rows:** 32 px.

## Interaction rules

- **Selection:**
  - Click selects one row, Ctrl toggles, Shift extends, Ctrl+A selects everything.
  - Pressing an already-selected row keeps the multi-selection, so it can be dragged.
  - Clicking empty space clears the selection.
- **Keyboard:** every command has a key; the list is in README.md. Focus shows as a stroke around the row.
- **Mouse back and forward:** buttons 4 and 5 go back and forward in the pane under the pointer.
- **Commands act on the active pane:** a 2 px accent bar marks it when both panes are open. Upload and Download swap with the active side.
- **Destructive actions name the target:**
  - Deleting on a server says "for good" and "can't be undone", and uses a red confirm button.
  - Deleting on This PC moves to the Recycle Bin.
- **Conflicts:**
  - The dialog compares both copies and offers Replace (Merge for folders), Keep both with the exact new name, or Skip.
  - "Do the same for the next N" covers the rest.
  - "Stop the transfer" queues nothing.
- **Every state has a screen:**
  - Loading: a thin bar after 150 ms.
  - Empty folder: what to drag here.
  - No filter match: a Clear filter button.
  - Load error: the reason, Try again and Go up.
  - Lost connection: a banner with Reconnect.
- **Feedback:**
  - Toasts for results, with one action each (Open folder, Show transfers, Retry).
  - Errors stay until closed; everything else fades after 5 to 8 seconds.
- **Errors are written for people:** they say what happened and what to try, for example "Couldn't find "tower". Check the address, or make sure this PC is on the same network or connected to Tailscale.", never an exception name.

## Copy

- Sentence case everywhere.
- Name the server by its saved name ("Tower") and this computer "This PC".
- Use "folder", not "directory".
- Talk about "permissions" and "owner" in the UI. The details pane spells the mode out: "Owner can read, write, run".

## Changes

Newest first.

- **2026-11-05: This PC toggle moved to where the pane opens.** The toggle sat at the far right of the command bar, but the pane opens at the far left of the file area. It's now a collapsed rail in the pane's place, with a fold button in the pane's header. Proposal: `design/proposals/2026-11-05-this-pc-toggle.html`, option C (collapsed rail), chosen by the owner over A (pane-edge button) and B (sidebar switch).
