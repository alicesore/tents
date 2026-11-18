# Tents: Detailed Milestones

Expands §5 of `PROJECT_PLAN.md` into ordered, checkable steps. Each step should be small enough for one PR. "Exit" lines are the gate for closing the milestone. Every milestone also has to hold the performance budgets (idle CPU ~0%, no timers or polling).

---

## M0: Foundations

**A. Repo and build skeleton**
1. Create `Tents.sln` with `src/Tents.Core`, `src/Tents.Shell`, `src/Tents.App` (WPF), `tests/Tents.Tests` (xUnit), `bench/Tents.Benchmarks` (BenchmarkDotNet). Target `net10.0` / `net10.0-windows`.
2. Add `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`, analyzers on, `Deterministic=true`.
3. Add `Directory.Packages.props` (central package management) and `global.json` pinning the 10.0.x SDK.
4. Add `.editorconfig`, `.gitattributes` and a Windows-appropriate `.gitignore`.
5. Enforce project references: Core references nothing; Shell references Core; App references both. Add a test that fails if Core gains a Win32 or WPF reference.
6. Add CsWin32 to `Tents.Shell` with `NativeMethods.txt` and a smoke call (e.g. `GetSystemMetrics`).

**B. CI and project hygiene**
7. GitHub Actions on `windows-latest`: restore, build (Release), test, upload test results. Trigger on push and PR.
8. Add a format check (`dotnet format --verify-no-changes`) and a build-warnings gate.
9. Add `LICENSE` (MIT), `CONTRIBUTING.md` (build steps, style, clean-room rules from §3), issue and PR templates, and the labels from §9.
10. Create the GitHub milestones M0–M6 and the spike issues.

**C. Spikes** (each ends with a short write-up in `docs/spikes/`, a throwaway prototype branch and a decision)
11. **Spike 1, desktop layer.** Prototype both options: (a) re-parent under Progman/WorkerW, (b) bottom-most tool window plus Show Desktop handling. Test: Win+D, Win+Tab, virtual desktop switch, Explorer kill/restart (`TaskbarCreated`), lock/unlock, display change, Win10 22H2 vs Win11. Pick one and list the failure modes.
12. **Spike 2, desktop items.** Prototype (a) hide native icon layer and draw our own, (b) reposition native icons. Check: no double icons, rename/delete/new-file flows, behavior after Explorer restart, behavior if Tents crashes (native icons must come back).
13. **Spike 3, performance baseline.** Minimal WPF window on .NET 10: measure idle CPU (WPR/`dotnet-counters`), working set and private bytes, and startup time. Try Workstation non-concurrent GC, ReadyToRun and trimming where applicable. Compare against a bare Win32 window.
14. Decide the WPF-vs-Win32/Direct2D fallback from spike 3. Record the final budgets in `PROJECT_PLAN.md`.

**Exit:** CI green, three spike write-ups merged, approach chosen, budgets recorded.

---

## M1: MVP (core tents)

Decisions from the M0 spikes that shape M1 ([spike 1](docs/spikes/01-desktop-layer.md), [spike 2](docs/spikes/02-desktop-items.md), [spike 3](docs/spikes/03-performance-baseline.md)):
- **Hosting:** each tent is a top-level tool window owned by the shell's desktop window and pinned to the bottom of the z-order. No re-parenting under Progman/WorkerW.
- **Desktop items:** the native icon layer is switched off (`FWF_NOICONS`) and Tents draws every desktop item. There are no loose icons in M1: every item belongs to a tent, and items the user has not assigned live in a default tent.
- **Rendering:** WPF with software rendering, behind one switch, until the interactive measurement (step 12) confirms or replaces it.
- **The tent UI thread never blocks.** Because the tent is owned by a window on Explorer's thread, a hung tent thread also blocks mouse input to the desktop. Shell enumeration, icon extraction and file I/O run on other threads.

**A. Core model (`Tents.Core`, fully unit-tested)**
1. Types: `Tent` (id, title, bounds, rolled-up flag, item ids), `Layout` (with exactly one default tent), `MonitorConfigKey`.
2. Operations: create, move, resize, rename, delete, roll up and expand, assign and remove items. Pure and deterministic. Items that are not assigned, and items of a deleted tent, go to the default tent.
3. JSON persistence with a versioned schema: `System.Text.Json` source generators, atomic write (temp file then replace), debounced saver, corrupt-file recovery (keep `.bak`).
4. Benchmarks for load and save with 10 tents / 200 items and with a stress size.

**B. Shell layer (`Tents.Shell`)**
5. Desktop item list from the native desktop view: acquire the desktop's `IFolderView2` (and re-acquire it after `TaskbarCreated`), list items with `IFolderView::Items`. Stable item identity (parsing name). Do not enumerate `FOLDERID_Desktop` directly: it returns items the desktop does not show (22 against 4 in spike 2).
6. Icon retrieval via `IShellItemImageFactory` at exact pixel size, with correct premultiplied-alpha conversion of the returned bitmap. Frozen-bitmap cache shared across tents, keyed by item and size.
7. `FileSystemWatcher` on the Desktop and Public Desktop folders with event coalescing (one update per burst). The native view is the reference for which items exist but lags the file system by about 1.3 s.
8. Native icon layer: switch `FWF_NOICONS` on and off through `IFolderView2::SetCurrentFolderFlags`, and keep a state file that records "Tents turned the icons off", checked at the next start. After switching back on, the view lists 0 items for a moment; do not rely on the list immediately. Notice when the user turns Show desktop icons back on while Tents runs, and treat it as Pause.
9. `ShellExecuteEx` open, and `IContextMenu` native context menu (including the "Show more options" behavior on Win11).
10. OLE drop target and drag source with shell data objects. Verify drag to and from Explorer and other apps.

**C. App**
11. Desktop host (spike 1, `owned-pin`): tent windows with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, owner set at creation to `GetShellWindow()`, every z-order change rewritten to `HWND_BOTTOM` in `WM_WINDOWPOSCHANGING`, `MA_NOACTIVATE`. Behind an interface so `bottom-react2` can be used if Windows 10 needs it. One hidden controller window receives `TaskbarCreated` (idempotent: re-own the tent if it still exists, recreate it if not), `WM_DISPLAYCHANGE` and session notifications.
12. Rendering mode: `RenderOptions.ProcessRenderMode = SoftwareOnly`, set once at startup behind one setting. Measure the interactive cost (drag, resize, scroll a 200-item tent, hover) with software and hardware rendering, with and without `AllowsTransparency`, at 100% and 200% DPI. Decide the default from that; if software rendering is ruled out, revisit the budgets in `PROJECT_PLAN.md` §7 explicitly. Include one scrolling tent that receives frequent updates in the measurement, not only icon grids: it is the earliest signal for whether software rendering survives live content ([M4](#m4-live-tents)).
13. Per-monitor v2 DPI manifest. Correct sizing on mixed DPI.
14. Move and resize by dragging (title bar and edges), with snap to a grid (optional, off by default).
15. Create a tent from the desktop context menu or tray, plus delete with confirm, and title editing in place.
16. Item view: virtualized, lazy icon loading. Item selection, double-click to open, right-click native menu, drag in and out.
17. Rollup: click title to collapse or expand. State persists.
18. Layout restore on startup, including items that were deleted while Tents was not running.
19. Tray icon (Settings placeholder, Pause, Exit). Pause and Exit turn the native icons back on and release every hook, so the plain desktop is back.
20. Start with Windows: `HKCU\...\Run` toggle.
21. Crash safety, three layers: restore the native icons in the unhandled-exception handler (not only in `finally`); a watchdog process that waits on the main process handle and restores them if Tents is killed; the state file from step 8, checked at the next start.

**D. Quality and release**
22. Idle check script, based on `spikes/03-perf-baseline/measure.ps1`: launch, settle, sample CPU, per-thread wakeups, working set, private working set and private bytes, and fail the release checklist if over the budgets in `PROJECT_PLAN.md` §7. Confirm the wakeups with WPR from an elevated prompt.
23. Manual test matrix: Win10 22H2 and Win11, Explorer restart, sleep/resume, monitor hot-plug, DPI change, high-contrast, Narrator pass. Include every Windows 10 item listed as untested in the three spike write-ups.
24. Publish a portable zip (framework-dependent, ReadyToRun, Workstation non-concurrent GC) to GitHub Releases, with idle numbers in the notes. Tag `v0.1.0`.

**Exit:** works on Win10 22H2 and Win11, survives an Explorer restart, meets the budgets in `PROJECT_PLAN.md` §7.

---

## M2: Polish

1. **Quick-hide:** double-click empty desktop to toggle all tents. Needs a low-cost way to detect the desktop click (decide in the spike 1 architecture, no polling). Add a fade-free instant hide.
2. **Appearance:** per-tent color and opacity, a global light/dark follow (`SystemEvents` / registry notification, no polling), and acrylic or blur backdrop (measure cost, make it optional).
3. **Multi-monitor:** layout keyed by monitor configuration. Detect topology changes, switch layouts, and restore when a config returns. Handle off-screen tents after unplug.
4. **Mixed-DPI:** verify moving a tent across monitors rescales icons crisply.
5. **Snapshots:** save, name, restore and delete layout snapshots. Confirm before restore, and keep an automatic "before restore" backup.
6. **Settings UI:** startup, theme, quick-hide, snapshots, reset. Lazy-create the window and release it after close so it costs nothing while idle.
7. Accessibility: keyboard navigation inside tents, UI Automation names, high-contrast theme.
8. Re-run the idle and load check. Release `v0.2.0`.

## M3: Folder-portal tents

Longer-term direction for live content, and why the source should be open-ended: [widgets-and-refresh.md](docs/widgets-and-refresh.md).

1. Model: a tent has a source, stored as a provider kind (stable string id) plus versioned settings as plain data. Desktop items and a folder path are the first two kinds. Do not use a closed enum, so [M4](#m4-live-tents) can add kinds without a model change. Core never references provider code.
   - A layout file that names an unknown kind must round-trip: keep the tent, show a placeholder, write it back unchanged. Add this rule to `docs/layout-file.md` and cover it in `LayoutSerializerTests` and `LayoutFileStoreTests`.
2. Shell: enumerate any folder, with a watcher per portal (shared watcher pool, coalesced).
3. UI: choose a folder when creating a tent, with a breadcrumb or "open folder" action, and a subfolder navigate-in/back-out behavior.
4. Handle unavailable paths (network drives offline, removed drives) without polling. Show a placeholder and retry on a shell notification.
5. Large folders: virtualization and cap on icon load concurrency. Benchmarks and memory check with 5,000 items.
6. Release `v0.3.0`.

## M4: Live tents

Design: [docs/widgets-and-refresh.md](docs/widgets-and-refresh.md). Depends on M3 (provider kind in the model, folder provider as the template). M5 and M6 do not depend on it; the M7 update check reuses its scheduler.

Goal: tents can show live, interactive content without giving up the idle promise. No timer exists unless a visible tent needs one, and then one shared scheduler owns it. The first provider is **notes**, stored as plain Markdown files: it needs no network, only file system events, and it exercises the provider lifecycle, editing and the "your files stay put" rule before any timed refresh exists.

**A. Spike**
1. **Spike 4, live-content cost and keyboard input.** Throwaway prototype, two questions:
   - *Cost.* One tent refreshed on a timer, one scrolling tent that gets frequent updates. Measure wakeups, CPU, working set and private bytes with the `spikes/03-perf-baseline/measure.ps1` method, in software and hardware rendering, at 100% and 200% DPI. Decide the scheduler design, the default rendering mode for live tents and how the per-provider allowance is measured.
   - *Typing into a tent.* Tents are `WS_EX_NOACTIVATE` and never take focus (M1 step 11), but a notes tent needs the keyboard. Prototype the options (activate the tent only while editing, or edit in a separate short-lived window placed over the tent) and check the bottom-of-z-order pin, the owner relationship from spike 1, Explorer restart during an edit, IME composition, and that focus returns to the previous window on exit. Pick one and list its failure modes.
   - Write up in `docs/spikes/`.

**B. Core (`Tents.Core`, fully unit-tested)**
2. Scheduling logic as pure code driven by `TimeProvider` (see `ManualTimeProvider` in the tests): due-time computation, coalescing of refreshes due close together, interval floor, exponential backoff with a cap, one wake-up time for the host to arm. No OS timer in Core.
3. Provider declaration types: update source (`none`, `events`, `timed`), minimum interval, needs network, rendering cost class, memory class. Decide whether they live in Core or in a new `Tents.Providers` project; either way `CoreBoundaryTests` must still pass and Core must not reference provider code.
4. Layout and settings: per-provider settings with a version and a migration hook, using the existing `ILayoutMigration` path.

**C. Host**
5. Provider registry and lifecycle: register by kind id at startup, create lazily, dispose when the tent is deleted, hidden or on an inactive layout. A hidden tent holds no timer and no network work.
6. Shared scheduler host: at most one OS timer, armed to the earliest due refresh. Pause on quick-hide, inactive layout, session lock and display sleep. Resume with one refresh, not a burst of missed ones. Tests for each pause and resume condition.
7. Worker hand-off: a provider fetches and parses off the UI thread and gives the UI thread one immutable snapshot. Draw only when a snapshot arrives or the user interacts. No render loop and no animation timer outside interaction. This keeps the M1 rule that the tent UI thread never blocks.
8. Offline and metered handling: no retry loop while offline, wait for a network-change event. Decide whether refresh pauses on metered connections or battery saver (open question in the design doc).
9. Unknown-kind placeholder tent, using the round-trip rule from M3 step 1.

**D. First providers** (each a separate PR, each measured before it merges)
10. **Notes provider** (kind `notes`): no network, update source `events`. A tent shows and edits one note.
    - **Markdown files are the source of truth.** Each note is a plain UTF-8 `.md` file on disk. The layout file stores only the note's location, never its text. Tents adds nothing to the file (no front matter); the tent title is the tent's own title. Existing encoding (with or without BOM) and line endings are preserved on save.
    - **Files stay put.** Tents never moves, renames or deletes a note file unless the user asks. Deleting a notes tent removes the tent, not the file. Default notes folder and per-tent override: decide in the PR and write it down in `docs/notes.md`.
    - **Rendering:** a rendered Markdown view by default, with a plain-text edit mode. Decide the renderer in the PR: a Markdown library against a small renderer for a limited subset (headings, emphasis, lists, links, code, checkboxes). Written reason and a startup and memory measurement are required (new dependency rule); prefer the BCL if the subset is enough.
    - **Saving:** the same atomic write as the layout file, driven by the debounced saver (one-shot delay, no idle timer). Nothing is written if the text did not change.
    - **External edits:** a coalesced `FileSystemWatcher` on the note's folder. If the file changes on disk while the tent is not being edited, reload it. If it changes while the user has unsaved edits, keep the user's text and offer the choice; never overwrite silently. A missing file shows a placeholder, and the next edit creates it.
    - **Large notes:** lazy and virtualized rendering. Measure a 1 MB note against the budgets.
    - **Input:** uses the approach chosen in spike 4.
    - **Tests:** save and load round trip, BOM and line-ending preservation, external change, change during an edit, missing file, a file locked by another program, and a note path on a drive that is gone.
    - A tent over a *folder* of `.md` files is a possible follow-up that builds on the M3 folder provider. Not in this milestone.
11. **Weather**: one small request, coarse `timed` refresh. The first provider that uses the scheduler. Pick the data source and record the reason (new dependency rule).
12. Appearance settings that live tents need (corner radius, spacing, follow Windows accent color), applied from a settings event, never a timer. Extends M2 appearance.

**E. Quality and release**
13. Extend the idle check script with provider classes: desktop items only, plus each live provider between refreshes, plus during a refresh. Budgets per class as in the design doc; fail the release checklist if over them.
14. Measure the per-provider memory allowance from steps 10 and 11 and record it in `PROJECT_PLAN.md` §7, together with the split budgets. A provider over its allowance ships as opt-in.
15. Update the README wording from "no CPU while idle" to "no CPU while idle unless a visible live tent is refreshing", and report both numbers in the release notes.
16. Manual matrix: Explorer restart (including during a notes edit), sleep and resume, lock and unlock, network loss and return, layout switch, quick-hide, with a live tent present. Include the Windows 10 22H2 rows still marked untested.
17. Release `v0.4.0`.

**Exit:** a live tent runs within its measured allowance, an idle desktop with only desktop-item tents is unchanged from M3, and no timer exists while every live tent is hidden.

## M5: Rules and auto-sort

1. Rules engine in `Tents.Core` (pure and testable): conditions (extension or type group, name glob or regex, date created/modified, size), actions (move item into tent X), ordered evaluation, first-match or all-match setting.
2. Rule storage in the layout file (versioned migration).
3. Apply rules to new items from the watcher only (event-driven). A manual "apply to existing items" action with a preview.
4. Rule editor UI with a live match preview. Never moves files on disk. Only tent membership changes.
5. Safe regex (match timeouts) and a benchmark for evaluating rules on a large desktop.
6. Release `v0.5.0`.

## M6: Desktop pages and extras

1. Pages model: layout holds N pages, each with tents. Add, rename, reorder and delete pages.
2. Switching UI: tray menu, hotkey (global hotkey registration, configurable), and optional edge or gesture trigger. No polling.
3. Search and filter inside a tent: type-to-filter, with the filter cleared on defocus.
4. Icon tinting per tent. Tint applied once at cache time (cached tinted bitmaps), not at render time.
5. Release `v0.6.0`.

## M7: Distribution

1. Decide MSIX vs WiX (signing, per-user install, startup-task story, uninstall cleanup of the Run key and `%APPDATA%\Tents` choice).
2. Build the installer in CI. Code signing (decide certificate source).
3. Publish a winget manifest (`alicesore.Tents`) and automate the PR on release.
4. Auto-update: a check on launch or on a long timer (documented exception to "no timers"; M4 provides the shared scheduler, so use it instead of creating a timer), signature and hash verification, a user toggle.
5. Docs: README rewrite with screenshots, user guide, troubleshooting.
6. Release `v1.0.0` after a full manual test pass and a final performance report.

---

## M8: Account-backed tents (calendar and mail)

Depends on M4 (the provider and scheduler foundations) and is planned after the `v1.0.0` release in M7; release it as `v1.1.0`. These providers are separate because they bring sign-in, token storage and large dependencies, and they are the ones most likely to break the memory budget.

1. **Decision record:** which providers, in what order (calendar before mail is expected, since mail needs rich content). Which services, and the minimum scope of access each needs.
2. **Sign-in and token storage:** where tokens live (Windows credential storage, never the layout file), how sign-in works without a long-lived background process, and how tokens are removed on sign-out and uninstall. Every library needs a written reason and a startup and memory measurement.
3. **Push versus poll:** use a push or change notification channel where the service offers one, otherwise a `timed` provider with a long floor interval.
4. **Calendar provider:** read-only first. Cache the last snapshot so a restart draws immediately without a network call.
5. **Mail provider, list view:** subjects and senders only, plain text. No message bodies.
6. **Rich content decision:** if message bodies are wanted, evaluate a light custom renderer against an embedded web view. A web view adds a separate process, so it ships as an opt-in, `large` memory class provider with its own allowance. Measure before deciding.
7. **Privacy:** nothing leaves the machine except calls to the chosen service, no telemetry, and a clear "what is stored and where" page in the docs.
8. Measure, set the allowances, update the idle check and the budgets, and run the manual matrix with an account connected.

**Exit:** each provider is inside its recorded allowance, a signed-out or offline tent shows a clear placeholder and costs nothing, and the privacy page matches the behavior.

---

## Ongoing (every milestone)
- Performance check before each release. A regression counts as a bug.
- A new dependency needs a written reason and a startup and memory measurement.
- Clean-room: link sources for any design decision that relies on outside material.
