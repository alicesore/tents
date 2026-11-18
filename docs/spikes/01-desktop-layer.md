# Spike 1: desktop layer

Finished in [#5](https://github.com/alicesore/tents/pull/5). Milestone step: `MILESTONES.md` M0 step 11.
Prototype (throwaway): `spikes/01-desktop-layer/` and `spikes/common/` on branch `spike/01-desktop-layer`.

## Question

What is the most reliable way to keep tents pinned to the desktop, behind normal windows?

## Recommendation

**Use a top-level tool window that is owned by the shell's desktop window and pinned to the bottom of the z-order** (`owned-pin` in the prototype). Do not re-parent tents under Progman/WorkerW.

- Each tent is a top-level window with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, whose **owner** is the shell's desktop window (`GetShellWindow()`).
- In `WM_WINDOWPOSCHANGING`, every z-order change is rewritten to `HWND_BOTTOM`. For an owned window, "bottom" resolves to "directly above its owner", so the tent always sits directly above the desktop and below everything else.
- `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE`.
- A separate hidden top-level window listens for `TaskbarCreated` and sets the owner again (the desktop window is a new window after an Explorer restart). If the tent window no longer exists, it is recreated.

This needs no hooks, no timers and no polling. The window manager keeps the tent attached to the desktop.

**Confidence: high on Windows 11 26H2 (the build tested here), medium overall.** Windows 10 22H2 is untested, and so are display changes and mixed DPI (see [Untested](#untested)). The fallback if Windows 10 behaves differently is `bottom-react2` (below), which passed the same checklist here. M1 should put the hosting behind one small interface so the strategy can be switched per OS.

## Test environment

| | |
|---|---|
| OS | Windows 11 Pro, version 26H2, build 26300.9457 |
| Runtime | .NET 10.0.15 (SDK 10.0.404), WPF |
| Displays | 2560x1440 primary at (0,0), 1440x2560 portrait at (2560,-516). Tents were tested on the primary only, at 96 DPI. |
| Explorer | `SeparateProcess=0`, `AutoRestartShell=1`, 4 desktop icons |

**Windows 10 22H2: untested.** Nothing in this document is a Windows 10 result.

### Desktop window tree observed on this build

Read with documented APIs only (`GetShellWindow`, `GetWindow`, `GetWindowLongPtr`); run `spikectl tree` to reproduce.

```
Progman "Program Manager"   top-level  ex = WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP
├─ SHELLDLL_DefView          child      ex = WS_EX_LAYERED          (z-order: top)
│  └─ SysListView32 "FolderView"        (the desktop icons)
└─ WorkerW                   child      visible, disabled            (z-order: below the icon view)
```

The WorkerW child existed on a freshly restarted shell without the prototype sending anything to Progman. This is not the layout described for Windows 8/10 in the public write-up of the WorkerW technique ([CodeProject](#sources)), where the WorkerW is a separate top-level window. Any approach that depends on this tree is therefore version-specific.

## What was tried

One WPF window (and, for comparison, one bare Win32 window) hosted seven ways. A script drove the checklist and read the result from window state, the top-level z-order and a count of the tent's marker-colored pixels on screen. No screenshots are stored.

| Mode | What it is | Issue candidate |
|---|---|---|
| `child-progman` | `SetParent` under Progman, above the icon view | 1 |
| `child-defview` | `SetParent` under `SHELLDLL_DefView`, above the icon list | 1 |
| `child-workerw` | `SetParent` under the wallpaper WorkerW, behind the icons | 1 |
| `bottom` | Top-level tool window pinned to `HWND_BOTTOM`, no Show Desktop handling | 2 (baseline) |
| `bottom-react2` | As `bottom`, plus: while the desktop is the foreground window, lift the tent above the desktop window if the desktop has been raised over it; push it back to the bottom when the foreground changes. Uses `SetWinEventHook`. | 2 |
| `owned-bottom` | Top-level tool window owned by Progman, sent to `HWND_BOTTOM` once | variant of 2 |
| `owned-pin` | Owned by Progman **and** pinned to `HWND_BOTTOM` in `WM_WINDOWPOSCHANGING` | variant of 2 |

Two more modes were tried and dropped: `owned` (owned, never sent to the bottom: starts in front of other windows) and `bottom-react` (re-inserts the tent above the desktop window on every z-order event: started in front of normal windows and ended a run with windows out of order).

## Results

All results are from Windows 11 26H2. "Pass" means observed by the script. Each cell for the two finalists is backed by several runs, see [Repeatability](#repeatability).

| Check | `child-progman` (WPF) | `child-defview` (WPF) | `child-workerw` | `bottom` | `bottom-react2` | `owned-bottom` | **`owned-pin`** |
|---|---|---|---|---|---|---|---|
| Renders at all | **Fail**: invisible | Pass | **Fail**: invisible (WPF) | Pass | Pass | Pass | Pass |
| Behind normal windows at start | n/a | Pass | n/a | Pass | Pass | Pass | Pass |
| Show Desktop button (`Shell.ToggleDesktop`): tent visible | Fail | Pass | Fail | **Fail**: desktop raised over it | Pass | Pass | Pass |
| Win+D (real key input): tent visible | Fail | Pass | Fail | **Fail** | Pass | Pass | Pass |
| Behind normal windows again after un-showing | n/a | Pass | n/a | Pass | Pass | **Fail**: stays in front of all windows | Pass |
| Minimize All (Win+M): tent stays | Fail | Pass | Fail | Pass | Pass | Pass | Pass |
| Click reaches tent, tent is not activated | Pass (input only) | Pass | **Fail**: clicks go to the icon list | n/a | Pass | Pass | Pass |
| Click does not raise tent over a normal window | n/a | Pass | n/a | n/a | Pass | Pass | Pass |
| Win+Tab (Task View): tent unchanged, not listed as a window | n/a | Pass | n/a | Pass | Pass | Pass | Pass (see note 1) |
| New virtual desktop: tent visible there too | not run | not run | not run | Pass | Pass | Pass | Pass (see note 2) |
| Explorer kill and restart | not run | Pass, but the tent window is **destroyed** and must be recreated | not run | not run | Pass, window survives | not run | Pass, window survives, owner re-set on `TaskbarCreated` |
| Lock and unlock (Win+L) | not run | Pass | not run | not run | Pass | not run | Pass |
| Display change | untested | untested | untested | untested | untested | untested | untested |
| Tent UI thread hung for 8 s: desktop still takes mouse input | not run | **No**: desktop input blocked | not run | Yes | Yes (same window relationship as `bottom`) | not run | **No**: desktop input blocked |
| Prototype hard-killed: Explorer unaffected, no window left behind | not run | Pass | not run | not run | not run | not run | Pass |
| Extra cost while idle | none | none | none | none | 1 WinEvent hook always, 1 more while the desktop is foreground | none | **none** |
| Windows 10 22H2 | untested | untested | untested | untested | untested | untested | untested |

Notes:

1. During Task View the only marker pixels on screen were a 33x23 px area inside the desktop strip, which is the tent shown as part of the desktop preview (the tent is 420x300). No larger thumbnail appeared. This is inferred from pixel bounds, not from looking at the screen.
2. `IVirtualDesktopManager::GetWindowDesktopId` returned an empty GUID for the tent and `IsWindowOnCurrentVirtualDesktop` returned true on both desktops: the tent is not assigned to a virtual desktop and shows on all of them, like the desktop itself. The check created a second desktop with Ctrl+Win+D and closed it with Ctrl+Win+F4.

### Candidate 1 in detail: re-parenting under Progman/WorkerW

| Variant | WPF window | Bare Win32 window |
|---|---|---|
| Child of Progman, ordinary window | invisible, but hit-testing and clicks work | invisible, clicks work |
| Child of Progman, `WS_EX_LAYERED` | invisible (`AllowsTransparency=True`) | **works** (`SetLayeredWindowAttributes`) |
| Child of `SHELLDLL_DefView`, ordinary window | **works** | invisible |
| Child of `SHELLDLL_DefView`, `WS_EX_LAYERED` | not run | **works** |
| Child of wallpaper WorkerW, ordinary window | invisible, no input | invisible, no input |
| Child of wallpaper WorkerW, `WS_EX_LAYERED` | not run | visible, **no input** (behind the icon view) |

Progman has `WS_EX_NOREDIRECTIONBITMAP` on this build, which the documentation describes as a window that does not render to a redirection surface. That is consistent with ordinary child windows of Progman painting nothing. Whether a given child renders depends on the parent, on the layered style and on the UI framework, and the combination that works for WPF (child of `SHELLDLL_DefView`) is a different one from the one that works for Win32.

Reasons not to use it, in order of weight:

1. It depends on an undocumented window tree that differs between Windows versions (see above). Every variant needs per-version probing.
2. Killing Explorer destroys the tent windows with it (observed: the child received `WM_NCDESTROY` 25 ms after the kill). The app must rebuild every window on `TaskbarCreated`.
3. The hung-thread problem is the same as for the owned window (observed), so it buys nothing there.
4. `SetParent` documents a forced DPI-awareness reset of the child's process when parent and child are in different processes with different DPI modes, and Raymond Chen describes cross-process parent/child trees as technically legal but very hard to manage.
5. A re-parented child stops receiving broadcasts such as `TaskbarCreated` and `WM_DISPLAYCHANGE`, which go to top-level windows only.

### Candidate 2 in detail: bottom-most tool window

- **Plain bottom pinning fails Show Desktop**, exactly as Raymond Chen's description predicts: Show Desktop minimizes what it can and then raises the desktop window over everything that is left. Observed z-order after Win+D: desktop window at index 2, tent at index 3, 0% of the tent visible. Note that `HWND_BOTTOM` puts the tent directly *above* Progman in normal use, not below it.
- **Ownership fixes it without any code reacting to it.** When Progman is raised, the window manager moves its owned windows with it. The log shows a single z-order message to the tent per Show Desktop, which the pin turns into `HWND_BOTTOM`; the tent ends directly above Progman. Across all 15 `owned-pin` checklist logs (the 14 runs below plus the excluded early run), the tent was directly above Progman in **250 of 250** z-order samples taken at foreground changes.
- **Ownership without the pin is not enough**: after un-showing the desktop, `owned-bottom` stayed in front of all 7 application windows.
- **`bottom-react2` also works** and has no owner, so a hang cannot reach the desktop. Its cost is a permanent out-of-context `EVENT_SYSTEM_FOREGROUND` hook (the process wakes on every foreground change anywhere) and a second `EVENT_OBJECT_REORDER` hook while the desktop is the foreground window (the prototype keeps both installed for logging). It reacts *after* the desktop has been raised, so there is a window of time in which the desktop covers the tents; whether that is visible as a flicker was not measured.

### Repeatability

The script was run repeatedly with real application windows open (7 to 10 of them).

| Mode | Runs | All checks passed | Notes |
|---|---|---|---|
| `owned-pin` (WPF) | 12 | 10 | Both failing runs had a non-minimizable popup window from another application over the test area: Show Desktop does not minimize such windows, so the pixel check failed. The z-order was correct in both (tent directly above Progman). All 4 runs made after moving the test area clear of that popup passed. |
| `owned-pin` (WPF, `AllowsTransparency=True`) | 1 | 1 | |
| `owned-pin` (bare Win32) | 1 | 1 | |
| `bottom-react2` (WPF) | 11 | 9 | One run failed the Minimize All pixel check with the same popup signature. In one run the Show Desktop state ended after the click on the tent and four Win+D presses did not re-enter it; cause not established. |

One early `owned-pin` run and one early `bottom-react` run are excluded: the harness toggled Show Desktop without checking the resulting state and had drifted out of phase. The harness now verifies the state after every toggle (`spikectl show-desktop`).

## Failure modes of the recommended approach

1. **A hung tent UI thread blocks mouse input to the desktop.** Measured: with the UI thread asleep for 8 s and a click queued for the tent, a right-click on empty desktop did not open the desktop menu until the hang ended. With an unowned window it opened normally. This is the input-queue attachment that comes with a cross-thread owner relationship. Consequence for M1: nothing may block the tent UI thread. Shell enumeration, icon extraction and file I/O run on other threads.
2. **The owner is a window we do not control.** It is replaced on every Explorer restart. Observed here: the tent window survived the kill, `TaskbarCreated` arrived 1.5 s later (to the controller window and to the tent itself), `GetShellWindow()` already returned the new Progman, and re-setting the owner restored correct behavior. The documentation says an owned window is destroyed with its owner, so M1 must handle both outcomes (re-own if the window exists, recreate if not). The prototype does.
3. **Changing the owner after creation.** The documentation says ownership cannot be transferred after creation; in practice `SetWindowLongPtr(GWLP_HWNDPARENT)` did change it (verified with `GetWindow(GW_OWNER)`), and the Win32 prototype sets it at creation instead. M1 should set the owner at creation (`WindowInteropHelper.Owner` before the window is shown) and treat recreation as the safe path after an Explorer restart.
4. **While Explorer is not running there is no desktop to own to.** The tents stayed on screen during the 0.6 s gap in the test. A long gap (Explorer ended and not restarted) was not tested.
5. **Other applications' non-minimizable windows stay above the desktop during Show Desktop** and can cover tents. This is the same for native desktop icons.
6. **`TaskbarCreated` is not only an Explorer-restart signal.** The documentation says Windows 10 also broadcasts it when the DPI of the primary display changes, so the handler must be idempotent.
7. **An owned window is hidden when its owner is minimized** (documented). Progman was never minimized in any test, but nothing guarantees it.

## Untested

| Item | Why | How to verify |
|---|---|---|
| **Windows 10 22H2, every row** | Not available here | On a Windows 10 22H2 machine: build `spikes/`, run `spikectl tree` and record the window tree, then `run-checks.ps1 -Mode owned-pin`, `run-checks.ps1 -Mode bottom-react2`, `hang-check.ps1 -Mode owned-pin` and `explorer-restart-check.ps1 -IUnderstandThisKillsExplorer`. Pick `-Rect` over empty desktop. |
| Windows 10: which window Show Desktop raises | On layouts where the icon view is hosted by a top-level WorkerW, the owner must be that WorkerW. The prototype owns to the icon view's host window by default (`--owner=iconhost`) and can be forced to Progman (`--owner=progman`). | Run `owned-pin` both ways, with a static wallpaper and with a slideshow wallpaper. Check whether the host window changes while Tents is running. |
| Display change (resolution, scale, monitor added or removed) | Needs a settings change, which the spike was not allowed to make | With `owned-pin` running (`run-checks.ps1 -Mode owned-pin -KeepRunning`), change resolution and scale and unplug a monitor. The log records `WM_DISPLAYCHANGE` and `WM_DPICHANGED`. Check the tent is still visible, in place and behind windows. |
| Mixed DPI and tents on the secondary monitor | Both monitors appear to be at 100%; all tests used the primary | Run with `-Rect` on the second monitor at a different scale. |
| Explorer ended and not restarted for a long time | Starting Explorer from the test session would give the new shell the session's environment | End Explorer in Task Manager, wait a minute, run `explorer.exe` from Task Manager, check the tent re-attaches. |
| Sleep and resume, fast user switching, Remote Desktop | Not on the issue's checklist | Manual. |
| Full-screen applications and games | Not on the checklist | Manual. |
| Looking at Task View with your own eyes | The script only counts marker pixels | Press Win+Tab with a tent visible and confirm it is not a window card. |
| Detecting a double-click on empty desktop (M2 quick-hide) | Out of scope for this spike | Needs its own small spike: the click lands in Explorer's icon list, not in our process. |

## How to re-run

```powershell
dotnet build spikes\common\SpikeCtl -c Release
dotnet build spikes\01-desktop-layer\DesktopLayerSpike -c Release
dotnet build spikes\01-desktop-layer\Win32Tent -c Release
cd spikes\01-desktop-layer
.\run-checks.ps1 -Mode owned-pin -Rect 1900,600,420,300      # x,y,w,h in pixels, over empty desktop
.\hang-check.ps1 -Mode owned-pin -Rect 1900,600,420,300
.\explorer-restart-check.ps1 -IUnderstandThisKillsExplorer    # kills explorer.exe; read the script header first
```

The scripts drive the real desktop: windows minimize and restore, Task View opens, and a temporary virtual desktop is created and removed. They record which windows were shown and un-minimize those at the end if a toggle went out of phase.

## What this means for M1

- `Tents.Shell` gets a desktop-host component with one job: own a window to the shell's desktop window, pin it, and re-attach on `TaskbarCreated`. Keep it behind an interface so `bottom-react2` can be swapped in if Windows 10 needs it.
- One hidden top-level controller window per process receives `TaskbarCreated`, `WM_DISPLAYCHANGE` and session notifications.
- The tent UI thread never blocks. This is now a correctness rule, not only a performance rule.
- WPF tents can use `AllowsTransparency=True` with this hosting (passed), so the choice between a layered and an ordinary window is a rendering-cost question for spike 3, not a hosting question.

## Sources

Public documentation and public articles only. No other product's binaries, resources or source code were inspected. Windows behavior was observed through documented APIs on a stock installation.

- Raymond Chen, [What is the difference between Minimize All and Show Desktop?](https://devblogs.microsoft.com/oldnewthing/20040527-00/?p=39153) (Show Desktop raises the desktop to the top of the window stack; topmost windows still cover it)
- Raymond Chen, [Is it legal to have a cross-process parent/child or owner/owned window relationship?](https://devblogs.microsoft.com/oldnewthing/20130412-00/?p=4683) (legal; cross-thread relationships attach the input queues)
- Microsoft Learn, [Window Features: Owned Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) (an owned window is always above its owner, is destroyed with it, is hidden when it is minimized)
- Microsoft Learn, [The Taskbar: Taskbar Creation Notification](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar) (`TaskbarCreated` is broadcast to all top-level windows)
- Microsoft Learn, [SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent) (style requirements; cross-process DPI-awareness behavior)
- Microsoft Learn, [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos) and [WM_WINDOWPOSCHANGING](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-windowposchanging)
- Microsoft Learn, [Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) (`WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `WS_EX_LAYERED` on child windows from Windows 8, `WS_EX_NOREDIRECTIONBITMAP`)
- Microsoft Learn, [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) (out-of-context hooks need a message loop on the calling thread)
- Microsoft Learn, [GetShellWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getshellwindow)
- Microsoft Learn, [Shell.ToggleDesktop](https://learn.microsoft.com/en-us/windows/win32/shell/shell-toggledesktop) (same effect as the Show Desktop button)
- Microsoft Learn, [IVirtualDesktopManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)
- Microsoft Learn, [WTSRegisterSessionNotification](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification) and [WM_DISPLAYCHANGE](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange)
- Gerald Degeneve, [Draw Behind Desktop Icons in Windows 8+](https://www.codeproject.com/Articles/856020/Draw-Behind-Desktop-Icons-in-Windows-plus), CodeProject (the publicly described WorkerW technique; used only as the description of the older window layout, and the page could not be re-fetched while writing this, so details are from search-result summaries of it)
