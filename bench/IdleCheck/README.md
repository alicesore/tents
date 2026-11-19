# Idle check

`idle-check.ps1` is the release gate for the idle budgets in `PROJECT_PLAN.md` section 7 (M1 step 22). It runs Tents with the typical setup (10 tents, 200 items), measures it the way spike 3 did, and exits with code 1 if any value is over budget or was not measured.

```powershell
.\bench\IdleCheck\idle-check.ps1                    # publishes Tents (Release, ReadyToRun) and measures it
.\bench\IdleCheck\idle-check.ps1 -Exe path\Tents.exe # measures a given build, for example the release zip's
```

Windows PowerShell 5.1 is enough. No elevation, nothing installed. `dotnet-counters` (a .NET global tool) is needed for the GC, timer and work-item numbers; without it those rows say "not measured" and the check fails unless `-AllowUnmeasured` is given. It takes about 4 minutes; leave the machine alone meanwhile.

## What it touches

- **Not the desktop.** Tents runs with `--desktop-folder=<temp>\items`: it shows 200 synthetic items (20 folders, 20 shortcuts to programs in System32, 160 empty files) from a new temporary folder instead of the desktop's items. In that mode it never reads or changes the native desktop icons, starts no watchdog, and its crash handler leaves the icons alone.
- **Not the real layout or log.** `--layout-file` and `--log-file` point into the same temporary folder. The folder is deleted at the end; the logs are copied to the results folder first.
- **On screen:** while Tents runs, 10 tents are shown behind every other window on the primary monitor and its tray icon appears. Tents is launched 1 + `-StartupRuns` times for the startup numbers and once more for the idle and resize numbers, and closed each time with `WM_CLOSE` to its controller window.
- **Single instance:** if Tents is already running, the script stops before doing anything. Exit Tents from the tray first.

## What it measures

| Case | How | Budget |
|---|---|---|
| Warm startup | median of `-StartupRuns` launches after one warm-up; Tents logs the time from process creation until every tent has drawn its first frame | at most 1000 ms |
| Idle CPU | process CPU time over the idle window (`-IdleSeconds`, after `-SettleSeconds`), scaled to 60 s | below 16 ms per 60 s |
| Idle wakeups | context switches summed per thread over the same window, scaled to 60 s; threads that exit inside the window are reported | at most 5 per 60 s |
| Memory | working set, private working set, private bytes at the end of the window; no trimming | 130 / 45 / 80 MB |
| GCs, timers, work items | `dotnet-counters` (System.Runtime) for `-CounterSeconds` after the idle window: `dotnet.gc.collections`, `dotnet.timer.count`, `dotnet.thread_pool.work_item.count` | 0 / 0 / 0 |
| After resize | one tent grown in `-ResizeSteps` steps with `SetWindowPos` (as in M1 step 12) and shrunk back the same way, then the idle window and counters again: a resize must leave nothing behind | as above |

The counters run after the idle window because attaching wakes the process. Results go to `bench/IdleCheck/artifacts/results-<time>/`: `idle-check.md` (the table), `idle-check.json` (details) and the Tents logs.

Also reported, not against a budget:

- **Settle timeline:** wakeups and CPU every 5 s from launch to the idle window, with the busiest thread, to see when Tents went quiet.
- **Waking threads:** each by its start address (module + offset), for example `combase.dll+0x...` for COM's own thread.
- **Commit and working set breakdown** at the end of the idle window: committed private, image and mapped memory; working set inside loaded modules and outside them (private and shared), with the largest modules.
- **Grown tent:** memory with the resized tent at its large size, 10 s after the resize. A bigger tent has bigger render buffers, so this is reported apart.

`idle-check.json` names the loaded modules, which can include shell extensions of software installed on the machine; check it before sharing.

**CPU is tick-sampled.** Windows charges process CPU time per scheduler tick, so a thread that wakes for very short slices can show 0 ms. The wakeup count catches those; WPR measures CPU exactly.

## Confirming the wakeups with WPR

The per-thread context-switch counters are the same numbers WPR reports, but WPR also shows which thread woke and why. Windows Performance Recorder needs an elevated prompt, and the script never elevates by itself. To record, open PowerShell with **Run as administrator** and run:

```powershell
.\bench\IdleCheck\idle-check.ps1 -WprTrace
```

It records the first idle window with `wpr -start CPU -filemode` into `idle.etl` in the results folder (the process id is in `idle-check.json`). Open it in Windows Performance Analyzer, add **CPU Usage (Precise)**, filter to that process, and read **Count** (context switches, that is wakeups) and **CPU Usage (ms)** per thread. The **Ready Thread Stack** column shows what woke each thread. The trace is system-wide: it holds the names of other running processes, so check it before sharing it.
