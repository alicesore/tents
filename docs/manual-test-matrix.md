# Manual test matrix (M1)

This is the M1 step 23 checklist: what a person has to check by hand before `v0.1.0`, on **Windows 11** and **Windows 10 22H2**. It collects the manual checks from the M1 PRs (#45 to #56, #58), the Windows 10 items the three spikes left untested (`docs/spikes/`), and the rows `MILESTONES.md` names: Explorer restart, sleep and resume, monitor hot-plug, DPI change, high contrast and Narrator.

A check counts as passed only when the person who ran it says so. Nothing here was passed by a script or by Claude.

## How to record a result

Each check has one column per Windows version. Write one of:

- `not run`: the starting value.
- `pass (YYYY-MM-DD)`: it behaved as described.
- `fail (YYYY-MM-DD): what happened`: then open an issue and link it.
- `n/a`: it cannot apply on that machine (for example no second monitor). Say why.

Write down the Windows build (`winver`), the display scale of each monitor, and the Tents commit or release you tested.

## Reported so far

| Date | Windows | Build tested | What the user reported |
|---|---|---|---|
| 2026-11-18 | Windows 11 | `main` after #58 | Ran the app; "everything looks good"; resizing at 200% felt fast ("snappy and fast"). No list of individual checks was reported, so every check below is still `not run`. |

## Before you start

- **What changes your desktop.** A plain start of Tents turns the native desktop icons off about a second after the tents have filled with your items, and back on at Pause and Exit (since #54). Start with `--keep-native-icons` to leave them on. Sections marked **changes the desktop** need the icons off.
- **Undo, whatever happens:** Exit from the tray; or tray > Show Windows desktop icons; or right-click the desktop > View > Show desktop icons. A killed Tents is covered by its watchdog (a second, small `Tents.exe`) and, if both are killed, by the next start of Tents.
- **Where to look:** the log is `%LOCALAPPDATA%\Tents\tents.log` (counts only, no file names); the watchdog log is `watchdog.log` next to it; a crash leaves `crash.txt` there.
- **Keep your real layout out of it** if you want: add `--layout-file=%TEMP%\tents-try\layout.json`.
- Run Tents from a release zip (`Tents.exe`) or with `dotnet run --project src\Tents.App -c Release -- <options>`.

## 1. Hosting behind windows (#45)

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 1.1 | With other windows open, the tents are behind all of them. Clicking a tent does not raise it or take focus from the active window. | not run | not run |
| 1.2 | Win+D and the Show desktop button: the tents stay visible. Restore the windows: the tents are behind them again. | not run | not run |
| 1.3 | Win+M: the tents stay. | not run | not run |
| 1.4 | Win+Tab: the tents are not shown as window cards (look with your own eyes; spike 1 only counted pixels). | not run | not run |
| 1.5 | New virtual desktop (Ctrl+Win+D): the tents are there too. Close it with Ctrl+Win+F4. | not run | not run |
| 1.6 | Win+L, then sign in: the tents are still there; the log has `SessionLocked.` and `SessionUnlocked.` | not run | not run |
| 1.7 | Start Tents a second time while it runs: the second one exits; still one set of tents. | not run | not run |
| 1.8 | A full-screen application or game: the tents do not show over it, and are back behind windows when it closes. | not run | not run |

## 2. Explorer restart

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 2.1 | Restart Windows Explorer from Task Manager: the tents are back behind the windows within a few seconds; the log has a `TaskbarCreated:` line saying they were re-attached or recreated. Then repeat 1.2. (#45) | not run | not run |
| 2.2 | After the restart the items are all still in the tents (the log may say it ignored an empty read). (#47) | not run | not run |
| 2.3 | After the restart the tray icon is back. (#52) | not run | not run |
| 2.4 | **Changes the desktop.** With the native icons off, restart Explorer: the native icons stay off (or come back and go off again within a second), and nothing is doubled for long. | not run | not run |
| 2.5 | End Explorer in Task Manager, wait a minute, start `explorer.exe` from Task Manager (File > Run new task): the tents re-attach. (spike 1, untested) | not run | not run |

## 3. Sleep, resume, sessions

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 3.1 | Sleep and wake the machine: the tents are still there; the log has `Resumed.` (#45) | not run | not run |
| 3.2 | **Changes the desktop.** Right-click the desktop > View > Show desktop icons while Tents runs, then lock and unlock (Win+L): Tents pauses by itself and the tray checkbox is ticked. (#52) | not run | not run |
| 3.3 | Sign out and back in with Tents running: the layout survives. (#51) | not run | not run |
| 3.4 | Fast user switching to another account and back: the tents are still there. (spike 1, untested) | not run | not run |
| 3.5 | Remote Desktop into the machine with Tents running, and back to the console: the tents are still there and behind windows. (spike 1, untested) | not run | not run |

## 4. Monitors and DPI

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 4.1 | Task Manager > Details > add the column DPI awareness: `Tents.exe` shows Per-Monitor (v2). (#46) | not run | not run |
| 4.2 | At 100% a new "Desktop" tent is 380 × 460 px; at 150% it is 570 × 690 and its text is sharp. (#46) | not run | not run |
| 4.3 | **DPI change:** change the primary monitor's scale in Settings > Display while Tents runs: the tents keep their apparent size and the text stays sharp. (#46, spike 1 untested) | not run | not run |
| 4.4 | Change the resolution while Tents runs: the tents are still visible, in place, behind windows; the log has `Display changed:`. (spike 1 untested) | not run | not run |
| 4.5 | Two monitors at different scales: drag a tent from one to the other; it keeps its apparent size and its text stays sharp. A tent can live on the secondary monitor. (#48, spike 1 untested) | not run | not run |
| 4.6 | A tent that spans two monitors with different scales draws correctly. (`docs/rendering-mode.md`, not measured) | not run | not run |
| 4.7 | **Monitor hot-plug:** with a tent on the second monitor, unplug it while Tents runs: note what happens (M1 keeps tents where they are until the next start; record whether the tent is unreachable). Plug it back in. | not run | not run |
| 4.8 | Put a tent on the second monitor, exit, unplug it, start: the tent is on the primary monitor. Plug it back in: the tent stays where it now is (per-configuration layouts are M2). (#51) | not run | not run |
| 4.9 | **Changes the desktop.** Change the scale or resolution while the native icons are off: the icons stay off, the tents stay correct, and Exit brings the icons back. (spike 2, untested) | not run | not run |
| 4.10 | Resizing a tent at 150% feels smooth (software rendering; 100% and 200% were measured in step 12, 150% was not). | not run | not run |
| 4.11 | Windows 10 also broadcasts `TaskbarCreated` when the primary display's DPI changes (spike 1, observation 6): after 4.3 the tents are still there once, not duplicated, and the log has one `TaskbarCreated:` line per change. | n/a (Windows 10 behavior) | not run |

## 5. Items in a tent (#47)

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 5.1 | Every desktop item appears, with sharp icons (thumbnails for images), names cut with "…" after two lines. | not run | not run |
| 5.2 | Hover highlights; click selects; Ctrl+click and Shift+click select several; clicking empty space clears. | not run | not run |
| 5.3 | Double-click a file: it opens as from the desktop. Double-click a virtual item (Recycle Bin, if shown): it opens. | not run | not run |
| 5.4 | Right-click an item: the full shell menu; Shift+right-click adds extended verbs; Properties opens. Right-click with several selected: a multi-item menu. | not run | not run |
| 5.5 | Create a text file on the desktop from Explorer: it appears in a tent within a fraction of a second. Rename it in Explorer: the new name shows in the same place. Delete it: it disappears. | not run | not run |
| 5.6 | Drag an item from a tent to an Explorer folder: copied or moved as from the desktop (Shift moves, Ctrl copies). | not run | not run |
| 5.7 | Drag a file from an Explorer folder onto a tent's empty space: it is copied or moved to the desktop and shows in that tent. | not run | not run |
| 5.8 | Drag an item onto a folder item in a tent: it goes into that folder. | not run | not run |
| 5.9 | Drag an item to another place in the same tent: it moves there; no file changes. | not run | not run |
| 5.10 | With more items than fit, the scroll bar appears and the wheel scrolls. | not run | not run |
| 5.11 | Task Manager, with the tents idle: CPU stays at 0. | not run | not run |
| 5.12 | A desktop with many items (100 or more), and a Desktop folder synced by OneDrive: every item shows once, and changes arrive. (spike 2, untested) | not run | not run |

## 6. Move, resize, roll up (#48, #49)

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 6.1 | Drag the title bar: the tent follows the pointer smoothly and stays behind other windows; it does not take focus. | not run | not run |
| 6.2 | Drag each edge and corner: the opposite edge stays put; the tent cannot be made smaller than about 120 × 80. | not run | not run |
| 6.3 | Hover the edges: resize cursors show; over items and the title bar the normal cursor. | not run | not run |
| 6.4 | A short click on the title bar does not move the tent. | not run | not run |
| 6.5 | Start with `--grid=40`: moves and resizes jump in 40-pixel steps. | not run | not run |
| 6.6 | Click a tent's title: it collapses to its title bar, which shows the item count. Click again: it expands to exactly the size it had. | not run | not run |
| 6.7 | Drag the title bar of a rolled-up tent: it moves and stays rolled up. | not run | not run |
| 6.8 | A rolled-up tent can be made wider or narrower, not taller; after expanding, its height is unchanged. | not run | not run |
| 6.9 | Drop an item onto a rolled-up tent's title bar: it joins that tent (the count goes up). | not run | not run |

## 7. New, rename, delete tents (#50)

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 7.1 | Right-click a title bar, and empty space in a tent: New tent, Rename tent, Delete tent... (greyed out on "Desktop"), Exit Tents. | not run | not run |
| 7.2 | New tent: it appears down and to the right, title selected for editing. Type a name, Enter: the title changes and the previously active window is active again. | not run | not run |
| 7.3 | Rename, then Esc: the old title stays. Rename, then click another window: the typed title is kept. | not run | not run |
| 7.4 | While editing, other windows still cover the tent normally. | not run | not run |
| 7.5 | Drag items into a new tent, then Delete tent...: the question names the tent and item count; No keeps it; Yes removes it, its items go to "Desktop", no file is touched. | not run | not run |
| 7.6 | Create a tent near the bottom-right corner: the next new tent goes to the top-left of the work area, not off-screen. | not run | not run |

## 8. Layout restore (#51)

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 8.1 | Create a tent, move and resize it, roll one up, rename one, drag items into it. Exit. Start again: everything is where it was. | not run | not run |
| 8.2 | With Tents not running, delete a scratch file that was in a tent. Start: it is gone from the tent; other items unchanged. | not run | not run |
| 8.3 | With Tents not running, create a scratch file on the desktop. Start: it is in the default tent. | not run | not run |
| 8.4 | Kill Tents in Task Manager right after a change (within half a second): the next start shows the layout from before at worst, never a broken one. | not run | not run |

## 9. Tray, Pause, Exit, native icons (#52, #54) — changes the desktop

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 9.1 | Plain start: the tents fill with your items; about a second later the native icons disappear; a second, small `Tents.exe` (the watchdog) runs. Nothing is doubled. | not run | not run |
| 9.2 | Hover the tray icon: "Tents". Right-click it: New tent, Show Windows desktop icons (unticked), Settings (greyed out), Start with Windows, Exit Tents. Left-click shows the same menu. | not run | not run |
| 9.3 | New tent from the tray: a tent appears next to "Desktop", ready to rename. | not run | not run |
| 9.4 | Tick Show Windows desktop icons: the native icons come back first, then the tents disappear; tooltip "Tents (paused)"; New tent greyed out. Untick: tents back where they were, then native icons gone. | not run | not run |
| 9.5 | Exit Tents from the tray: tents and tray icon disappear, the native icons are back, both `Tents.exe` end. | not run | not run |
| 9.6 | Start with `--keep-native-icons`: the native icons stay; only one `Tents.exe` runs; Pause only hides the tents. | not run | not run |

## 10. Crash safety (#53) — changes the desktop

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 10.1 | End the **main** `Tents.exe` (the larger one) in Task Manager: within about a second the native icons are back, the watchdog has ended, and `watchdog.log` says so. | not run | not run |
| 10.2 | End both processes ("End process tree"): the icons stay off. Start Tents again: they come back at once. (Or right-click desktop > View > Show desktop icons.) | not run | not run |
| 10.3 | Exit from the tray: icons back, the watchdog ends by itself, no `crash.txt`. | not run | not run |
| 10.4 | Sign out and in, and reboot, with the icons off: they are off after signing in until Tents starts or exits, and on after Exit. (spike 2, untested) | not run | not run |

## 11. Start with Windows (#55)

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 11.1 | Tray > Start with Windows: ticked; Task Manager > Startup apps lists "Tents". Untick: the entry is gone. | not run | not run |
| 11.2 | With it ticked, sign out and in (or restart): Tents starts, the tray icon appears, the tents show the items, the native icons go off once the items are in. | not run | not run |
| 11.3 | Disable Tents in Task Manager > Startup apps: Windows no longer starts it, but the tray item stays ticked (that switch is not read). | not run | not run |

## 12. High contrast

M1 draws tents in its own fixed dark colors and does not read the system's contrast colors yet, so this records what a contrast theme user sees today. Failures here become issues for M2's theme work.

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 12.1 | Turn on a contrast theme (Settings > Accessibility > Contrast themes) while Tents runs: tent titles, item names and the selection are still readable; note the colors. | not run | not run |
| 12.2 | Start Tents with a contrast theme already on: same as 12.1. | not run | not run |
| 12.3 | The tray menu and the tent menus follow the contrast theme. | not run | not run |

## 13. Narrator

M1 has no UI Automation support for the drawn item grid (no automation peers), so Narrator is expected to read little inside a tent. Record what it reads; gaps become issues.

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 13.1 | Turn on Narrator (Ctrl+Win+Enter). Move the mouse over a tent title and over items: note what is read. | not run | not run |
| 13.2 | The tray icon and its menu are read ("Tents", each menu item and its checked state). | not run | not run |
| 13.3 | The tent title-bar menu and the Delete tent question are read. | not run | not run |
| 13.4 | Tents never takes keyboard focus from the active window while Narrator runs (tents are not activated by clicks). | not run | not run |

## 14. Windows 10 22H2: spike re-runs

The three spikes ran only on Windows 11 26H2 and marked every Windows 10 row untested. These re-runs need a Windows 10 22H2 machine; the scripts are in `spikes/` and print what they do in their header. Read it first: some change the desktop or end Explorer.

| # | Check | Win10 22H2 |
|---|---|---|
| 14.1 | Spike 1 (desktop layer), every row: build `spikes/`, run `spikectl tree` and record the window tree, then `run-checks.ps1 -Mode owned-pin`, `run-checks.ps1 -Mode bottom-react2`, `hang-check.ps1 -Mode owned-pin` and `explorer-restart-check.ps1 -IUnderstandThisKillsExplorer`, with `-Rect` over empty desktop. | not run |
| 14.2 | Spike 1: which window Show Desktop raises. Run `owned-pin` with `--owner=iconhost` and `--owner=progman`, with a static and with a slideshow wallpaper; check whether the icon view's host window changes while Tents runs. | not run |
| 14.3 | Spike 2 (desktop items), every row: `run-items-checks.ps1 -Scenario hide-noicons -IUnderstandThisChangesMyDesktop`, the same with `-Scenario hide-listview` and `-Scenario park`, and once more with `-ExplorerRestart`. (`park` is the rejected candidate 2; run it for completeness.) | not run |
| 14.4 | Spike 3 (performance baseline): the re-run steps in `docs/spikes/03-performance-baseline.md`. Check that idle wakeups stay at 2 to 3 per 60 s, and the hardware-vs-software startup difference on the typical setup. | not run |
| 14.5 | The idle check on the app: `bench/IdleCheck/idle-check.ps1` passes (`bench/IdleCheck/README.md`). | not run |
| 14.6 | Every section above, in the Win10 22H2 column. | not run |

## 15. Performance confirmation

| # | Check | Win11 | Win10 22H2 |
|---|---|---|---|
| 15.1 | `bench/IdleCheck/idle-check.ps1` passes on the release build. (The script's own results are in #59 and the release notes; this row is for a person running it on their machine.) | not run | not run |
| 15.2 | From an elevated PowerShell, `idle-check.ps1 -WprTrace`: in WPA, the Tents process shows at most 5 context switches per 60 s in the idle window, and what woke each thread. | not run | not run |
| 15.3 | On a machine with integrated graphics: resizing a tent feels smooth, and the idle check passes. (`docs/rendering-mode.md`, not measured) | not run | not run |
