# Spike 2: desktop items

Finished in [#6](https://github.com/alicesore/tents/pull/6). Milestone step: `MILESTONES.md` M0 step 12.
Prototype (throwaway): `spikes/02-desktop-items/` and `spikes/common/` on branch `spike/02-desktop-items`.

## Question

How can desktop items be shown inside tents without also appearing as native desktop icons?

## Recommendation

**Hide the native icon layer and draw every desktop item ourselves (candidate 1).** Do not reposition native icons (candidate 2).

- **Hide mechanism:** turn off "Show desktop icons" through the documented `IFolderView2::SetCurrentFolderFlags(FWF_NOICONS, FWF_NOICONS)` on the desktop's shell view. It is the same switch as the desktop's own View > Show desktop icons, it survives an Explorer restart with no duplicate icons, and the user can undo it with one familiar click.
- **Crash safety, three layers:** restore in-process on exit and on unhandled exceptions; keep a small state file that says "Tents turned the icons off"; and have something outside the process restore when the process dies without cleaning up (a watchdog that blocks on the process handle, and a check of the state file on the next start).
- **Item list:** take the set of desktop items from the native view (`IFolderView`), not from the desktop namespace root. The native view keeps listing and tracking its items while hidden.

**Confidence: medium-high on Windows 11 26H2** that hiding, drawing and restoring are reliable; every step was observed, including three kinds of crash. **Windows 10 22H2 is untested.**

The cost is scope, not risk: with the native layer off, Tents has to show *every* desktop item, including the ones the user has not put in a tent. See [What this means for M1](#what-this-means-for-m1).

**Candidate 2 is rejected with high confidence** for the configuration tested (Align icons to grid on, which is the Windows default): Explorer does not leave an icon off-screen, so a native icon cannot be hidden by moving it.

## Test environment

| | |
|---|---|
| OS | Windows 11 Pro, version 26H2, build 26300.9457 |
| Runtime | .NET 10.0.15, WPF, CsWin32 0.3.371 for the COM interfaces |
| Desktop | 4 items in the native view: 3 files in the user's Desktop folder and 1 virtual (non-file) item. View mode: icons, 48 px. Auto arrange off, Align icons to grid on, grid spacing 75x99 px. |
| Displays | 2560x1440 primary, 1440x2560 portrait to its right and 516 px higher. The desktop icon list spans the bounding box of both (4000x2560). |

All item tests used scratch files named `tents-spike-*.txt`. The user's own files and icons were not modified or moved. Icon visibility, the arrange flags and all icon positions were saved before the experiments and verified identical afterwards.

**Windows 10 22H2: untested.**

## What was built

`ItemsTentSpike` is one WPF tent, hosted the way spike 1 recommends, that draws shell items itself (icons from `IShellItemImageFactory`, changes from `FileSystemWatcher`, bursts coalesced into one queued refresh, no timers). It can additionally:

- hide the native icon layer in one of two ways (candidate 1), or
- move the native icons of the items it draws to a given position (candidate 2).

Whatever it changes, it records first and puts back on exit, on an unhandled exception, and through a watchdog process if it is killed. `desktopitems` is a read-only command-line tool for the desktop view plus `backup` and `restore`. `run-items-checks.ps1` runs the checklist.

## Results

### Candidate 1: hide the native icon layer, draw every icon ourselves

Two ways of hiding were tried.

| Check | (1a) `ShowWindow(SW_HIDE)` on the icon list window | (1b) `FWF_NOICONS` via `IFolderView2` |
|---|---|---|
| Native icons hidden | Pass | Pass |
| What it changes | Only the window's visibility. `FWF_NOICONS` stays off, registry `HideIcons` stays 0. | The view flag, and the registry value `HideIcons` becomes 1 immediately. The icon list window is hidden as a result. |
| No duplicate icons while running | Pass | Pass |
| Native view still lists its items while hidden (`IFolderView::ItemCount`, positions) | Pass | Pass |
| Desktop right-click menu still opens | Pass | Pass |
| New file on the Desktop appears in the tent | Pass, 18 ms after the file system event | Pass, 18 ms |
| Rename is reflected | Pass, 14 ms | Pass, 14 ms |
| Delete is reflected | Pass, 14 ms | Pass, 14 ms |
| 20 files created in a burst (scratch folder) | Pass: 3 refreshes instead of 20, final state correct | same code |
| Clean exit: native icons back | Pass | Pass (flag, registry value and window all restored) |
| Unhandled exception: native icons back | Pass (restored by the unhandled-exception handler) | Pass |
| Hard kill with watchdog: native icons back | Pass, about 190 ms after the kill | Pass, about 190 ms |
| Hard kill without watchdog | **Icons stay hidden.** Restored from the state file by hand. | **Icons stay hidden, and stay hidden across restarts.** Restored from the state file by hand. |
| After an Explorer kill and restart | **Native icons come back by themselves**: the new shell's icon list was visible when `TaskbarCreated` arrived, about 1 s after the kill. The prototype hid it again then. Duplicates are on screen in between. | **Stays hidden**: the new shell came up with `FWF_NOICONS` set and the list hidden. No duplicates. |
| User recovery without Tents | Toggle View > Show desktop icons off and on (simulated through the flag: the list window became visible again). Or restart Explorer. | Tick View > Show desktop icons. |
| Windows 10 22H2 | untested | untested |

Observations that matter for the design:

- **The desktop namespace root is not the desktop.** Enumerating `FOLDERID_Desktop` through `IShellItem`/`IEnumShellItems` returned 22 items where the native desktop shows 4. The 18 extra were 8 virtual (non-file) items and 10 file system items that the desktop view does not display. The native view's own list (`IFolderView::Items`) is exactly what the desktop shows, including the one virtual item on this desktop, and it keeps working while the icons are hidden.
- **The native view lags the file system.** `FileSystemWatcher` reported a new file within milliseconds; the native view listed it 1.3 s later (measured 1.27 s and 1.34 s). For candidate 1 this lag is invisible to the user, because the tent draws from its own notification.
- After restoring `FWF_NOICONS`, the view listed 0 items for a moment before repopulating. Restore code must not assume the item list is available immediately after the flag flips.
- Turning Show desktop icons off and on made Explorer re-place icons that were stacked on one grid cell. The user's own icons, each on its own cell, did not move.

### Candidate 2: reposition the native icons

Tested with `IFolderView::SelectAndPositionItems(SVSI_POSITIONITEM)`, the documented way to move a desktop icon.

| Check | Result |
|---|---|
| Move an icon to an on-screen position | Works, **snapped to the desktop grid**: a request for (1650,1250) ended at (1663,1211), and (1700,1300) at (1663,1310). |
| Park an icon off-screen at (-3000,-3000) to hide it | **Fail.** The position reads back as (-3000,-3000) immediately after the call, but within 3 s every parked icon was at (13,23), the first grid cell of the icon list. Explorer pulls the icon back into the view. |
| No duplicate icons | **Fail** in general: (13,23) is the top-left cell of the desktop. On this machine that cell happens to lie in the dead area above the primary monitor, so nothing was visible, but that is an accident of this monitor layout. |
| Several parked icons | They all stack on the same cell. |
| New file on the Desktop | **Fail (flash).** Explorer first shows the new icon at its own default slot. It could only be moved once the native view had the item, 1.3 s after the file was created; until then it is visible natively as well as in the tent. |
| Rename | The native icon keeps its position. The prototype's record of where the icon originally was is keyed by name, so the renamed icon was not put back on exit. |
| Delete | Works. |
| After an Explorer kill and restart | The moved position persisted: the new shell showed the scratch icon where the prototype had put it. |
| Clean exit: icons back where Explorer had them | Pass for icons that were not renamed. |
| Unhandled exception: icon put back | Pass |
| Hard kill with watchdog: icon put back | Pass |
| With Align icons to grid switched off | untested (it is a user setting; the spike was limited to moving icons) |
| Windows 10 22H2 | untested |

Why it is rejected:

1. A native icon cannot be hidden by moving it, at least with Align icons to grid on. That removes the "park the native icon, draw our own" design.
2. The other design, leaving the native icons visible and arranging them *inside* a tent, cannot do rollup (there is no way to hide the icons of a collapsed tent), cannot scroll or clip, forces every tent onto the desktop grid, and puts the tent's background either over the icons or in the fragile child-window layer that spike 1 rejected.
3. New items flash at Explorer's default position for over a second.
4. Explorer persists the positions. Anything left behind by a crash is permanent until restored, and the user has no simple way to undo it.

## Failure modes of the recommended approach

1. **Tents is killed while the icons are off.** With `FWF_NOICONS` the desktop stays empty, also after a restart, until something turns the icons back on. Observed. Mitigations, all tested in the prototype: restore in the unhandled-exception handler (not only in `finally`, which is not guaranteed to run for an unhandled exception); a watchdog process blocked on the main process handle; a state file that the next start checks. The user-level recovery is View > Show desktop icons.
2. **Tents is uninstalled or disabled while the icons are off.** Same state. The uninstaller, Pause and Exit must all restore. `MILESTONES.md` M1 steps 17 and 19 already require this.
3. **The user turns Show desktop icons back on while Tents is running.** Tents needs to notice and treat it as "pause". Later measured in M1.8 (#43) on Windows 11 build 26300: the switch changes what `HideIcons` reads as, but raises no registry change notification, not even on the whole `HKCU\Software` tree, so a registry watch cannot be the trigger. M1 rechecks at start, resume, unlock and Explorer restart instead; a live trigger (the icon list window becoming visible) is deferred.
4. **Tents now owns the whole desktop experience.** Selection, drag and drop, rename, context menus, keyboard navigation, cut/copy/paste and accessibility for desktop items are ours. None of that was prototyped here; the prototype only draws icons and names.
5. **Icon rendering.** The prototype converts the `HBITMAP` from `IShellItemImageFactory` without handling its alpha channel. Correct premultiplied-alpha conversion is M1 work.
6. **The view object is per shell instance.** After an Explorer restart the `IFolderView2` pointer is dead; the prototype re-acquires it on `TaskbarCreated`.

## Untested

| Item | Why | How to verify |
|---|---|---|
| **Windows 10 22H2, every row** | Not available here | On Windows 10 22H2: build `spikes/`, then run `run-items-checks.ps1 -Scenario hide-noicons -IUnderstandThisChangesMyDesktop`, the same with `-Scenario hide-listview` and `-Scenario park`, and once more with `-ExplorerRestart`. Read the script header first. |
| User toggles Show desktop icons while Tents runs | Needs a person at the desktop menu | Start the prototype with `--hide=noicons`, tick View > Show desktop icons, observe. |
| Sign out and sign in, reboot, with icons off | Would end the test session | Start with `--hide=noicons`, sign out and in. Icons should still be off; then exit the prototype and confirm they return. |
| Candidate 2 with Align icons to grid off | Changing that setting was outside what the spike was allowed to do | Untick View > Align icons to grid, run `-Scenario park`. |
| Desktops with many items, or with OneDrive-synced Desktop folders | Only 4 to 8 items were present | Manual. |
| Display changes while hidden | Not tested | Manual. |
| A second kind of shell change notification (`SHChangeNotifyRegister`) | The plan specifies `FileSystemWatcher`, which worked | Only needed if virtual items must be tracked live. |

## How to re-run

```powershell
dotnet build spikes\common\SpikeCtl -c Release
dotnet build spikes\02-desktop-items\DesktopItemsSpike -c Release
dotnet build spikes\02-desktop-items\ItemsTentSpike -c Release
cd spikes\02-desktop-items
.\DesktopItemsSpike\bin\Release\net10.0-windows\desktopitems.exe info      # read-only
.\run-items-checks.ps1 -Scenario hide-noicons -IUnderstandThisChangesMyDesktop
.\run-items-checks.ps1 -Scenario park -IUnderstandThisChangesMyDesktop
```

The script saves the desktop state first and restores it at the end. If icons ever stay hidden: right-click the desktop > View > Show desktop icons (toggle it off and on if it is already ticked), or restart Explorer from Task Manager.

## What this means for M1

- **No loose icons in M1.** With the native layer off, every desktop item has to be drawn by Tents. The smallest design that satisfies that: every item belongs to a tent, and items the user has not assigned live in a default tent. A full-desktop surface for free-floating icons is a much larger piece of work and should be a later, separate decision.
- **Pause and Exit turn the native icons back on** and show the plain desktop. That makes Pause a safe, complete off switch.
- **A desktop-items component in `Tents.Shell`** owns: acquiring `IFolderView2` for the desktop (and re-acquiring it after `TaskbarCreated`), the item list from the native view, `FWF_NOICONS` on and off, and the state file.
- **Change tracking** stays on `FileSystemWatcher` for speed, as planned. The native view is the reference for which items exist, including virtual ones, but it is about 1.3 s behind.
- **A watchdog or equivalent** is part of M1 step 19 (crash safety), because an unhandled-exception handler cannot cover a kill.
- The item position Explorer would have used (`IFolderView::GetItemPosition`) is still available while hidden, which is useful for placing a new item sensibly and for restoring a familiar layout when Tents is paused.

## Sources

Public documentation and public articles only. No other product's binaries, resources or source code were inspected. Windows behavior was observed through documented APIs on a stock installation.

- Raymond Chen, [Manipulating the positions of desktop icons](https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933) (the route to the desktop's `IFolderView`, `GetItemPosition`, `SelectAndPositionItems`, and switching off auto-arrange and snap-to-grid before positioning)
- Microsoft Learn, [IShellWindows::FindWindowSW](https://learn.microsoft.com/en-us/windows/win32/api/exdisp/nf-exdisp-ishellwindows-findwindowsw)
- Microsoft Learn, [IFolderView::Items](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-items), [IFolderView::GetItemPosition](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getitemposition), [IFolderView::SelectAndPositionItems](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-selectandpositionitems)
- Microsoft Learn, [IFolderView2::SetCurrentFolderFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setcurrentfolderflags) and [FOLDERFLAGS](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags) (`FWF_NOICONS`: "The view should not display icons"; `FWF_AUTOARRANGE`; `FWF_SNAPTOGRID` is listed as not supported, although the desktop view reports and uses it)
- Microsoft Learn, [SHGetKnownFolderItem](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetknownfolderitem) and [IShellItemImageFactory](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellitemimagefactory)
- Microsoft Learn, [FileSystemWatcher](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher) and [AppDomain.UnhandledException](https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.unhandledexception)
- Microsoft Learn, [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) (used to observe when the native icon list changed)
- Microsoft Learn, [SHChangeNotifyRegister](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister) (not used; listed as the alternative notification source)
