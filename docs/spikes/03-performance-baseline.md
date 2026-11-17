# Spike 3: performance baseline

Issue: [#4](https://github.com/alicesore/tents/issues/4). Milestone steps: `MILESTONES.md` M0 steps 13 and 14.
Prototype (throwaway): `spikes/03-perf-baseline/` and `spikes/common/` on branch `spike/03-perf-baseline`.

## Question

What are the idle CPU, RAM and startup costs of a minimal WPF tent on .NET 10? Can they meet the performance budget, or is the Win32/Direct2D fallback needed?

## Recommendation

**WPF is viable. The Win32/Direct2D fallback is not needed for M1.** Two conditions come with that:

1. **Idle is not the problem.** A WPF tent that does nothing has 2 to 3 thread wakeups per 60 s, CPU time below what Windows can resolve (one 15.6 ms tick in 60 s), no timers, no thread-pool work and no garbage collections. That holds for every WPF variant, including the typical setup of 10 tents with 200 items. The plan's "idle CPU effectively 0%" is met by WPF itself; whether Tents meets it depends on Tents' own code.
2. **Hardware rendering is the problem, at startup and in memory.** With WPF's default (GPU) rendering, the typical setup takes **2.7 s** from process start to the last tent's first frame, and each extra window adds about 240 ms. With `RenderMode.SoftwareOnly` the same setup takes **0.55 s** and uses about 30% less working set (88 MB instead of 124 MB) and 55% less private bytes (50 MB instead of 113 MB). **Start M1 with software rendering behind one switch**, and measure interactive cost (drag, resize, scroll, hover) both ways before M1 exits. See [Rendering mode](#rendering-mode).

Startup and memory are where WPF is expensive. A bare Win32 window on the same runtime starts in 34 ms with a 19 MB working set and 5 MB private bytes. Most of WPF's cost is fixed: about 440 ms and about 70 MB private bytes before the first tent shows anything. That is the price of WPF. It is within the budgets proposed [below](#proposed-final-budgets), but those budgets will not get much lower while Tents uses WPF.

**Build settings:** keep Workstation non-concurrent GC and ReadyToRun as the plan says. Neither made a measurable difference here (see [Effects](#effects-of-each-option)), but they cost nothing and the reasons for them (no background GC thread, less JIT at startup once Tents has real code) still apply. Do not turn off tiered compilation. Framework-dependent is fine; self-contained bought nothing.

**Confidence: medium-high** for the idle results. They were the same in all 17 measured rows and they agree with the runtime's own counters. **Medium** for the startup and memory numbers: warm starts only, one machine with a fast CPU and a discrete GPU, and a test app that is much simpler than Tents. **Medium-low** for software rendering as the M1 default, because its interactive cost was not measured. **Windows 10 22H2 is untested.**

## Test environment

| | |
|---|---|
| OS | Windows 11 Pro, version 26H2, build 26300.9457 |
| CPU, RAM | AMD Ryzen 7 5700X3D (8 cores, 16 logical CPUs), 32 GB |
| GPU | NVIDIA GeForce RTX 5070 Ti, driver 32.0.16.2114. A second, virtual display adapter is also installed. |
| Displays | 2560x1440 primary, 1440x2560 portrait secondary. Test windows were on the primary at 96 DPI. |
| Power plan | Ultra Performance |
| Runtime | .NET 10.0.15 (Microsoft.NETCore.App and Microsoft.WindowsDesktop.App), SDK 10.0.404, win-x64 |
| Tools | `dotnet-counters` 10.0.750501 |

**Windows 10 22H2: untested.** Nothing in this document is a Windows 10 result.

## What was built

- **`WpfTent`**: a minimal WPF tent hosted the way spike 1 recommends (tool window owned by the shell's desktop window, pinned to the bottom). It does nothing after startup: no timers, no polling, no logging, no hooks. A hidden controller window handles `TaskbarCreated` and `WM_CLOSE`. Options: `--tents=N`, `--items=M` (48 px image and label per item, images from a shared cache of 20 frozen bitmaps), `--software` (`RenderOptions.ProcessRenderMode = SoftwareOnly`), `--transparent` (`AllowsTransparency = true`), `--trim` (one `SetProcessWorkingSetSize(-1, -1)` after the last tent's first frame).
- **`Win32Tent`**: the comparison point. Bare Win32 windows painted with GDI, hosted the same way, same options except the two WPF-only ones. Written with blittable P/Invoke so it can also be compiled with NativeAOT.
- **`publish-variants.ps1`** publishes every build variant into `artifacts\<variant>\` (git-ignored).
- **`measure.ps1`** measures every row and writes `results.csv`, `results.md`, `per-thread.log` and the `dotnet-counters` CSVs.
- **`wpr-idle.ps1`** records an ETW trace of an idle variant. It needs elevation and was **not run**; see [Not measured](#not-measured).

"Typical" means `--tents=10 --items=20`: 10 tents of 420x400 px with 200 items in total, the setup named in `PROJECT_PLAN.md` §7.

## Method

Per row of the table (`measure.ps1`):

- **Startup:** 1 warm-up launch that is discarded, then 7 launches. The app writes the time from its own process creation to the first rendered frame of its **last** tent (WPF `ContentRendered`; Win32 the first `WM_PAINT`) to a marker file. The table shows the median and the range. All launches are **warm** (files in the OS cache). Cold start after sign-in was not measured.
- **Idle:** launch, wait 30 s to settle, take sample A, wait 60 s, take sample B, then close the app.
  - **CPU ms**: the difference in process CPU time (kernel + user) between A and B. Windows accounts process CPU time in clock ticks of 15.625 ms on this machine (the `dotnet-counters` CPU values also come in multiples of 0.015625 s). **0 means less than one tick in 60 s**, not exactly zero.
  - **Wakeups**: from the raw per-thread context-switch counter (`Win32_PerfRawData_PerfProc_Thread`), summed per thread: the difference for threads present in both samples, the whole count for threads created in between. A sleeping thread does not switch, so this counts thread wakeups. Threads that exited between A and B cannot be counted and are listed separately; the total is a lower bound by at least one per exited thread. (Run 1 subtracted the two sums instead, which went negative when threads exited; those numbers were discarded.)
  - **Working set, private working set, private bytes** at sample B. Working set is what is resident, including shared pages of DLLs. Private working set is the resident part that belongs to the process alone (what Task Manager's Memory column shows). Private bytes is committed private memory, resident or not (`PROCESS_MEMORY_COUNTERS_EX.PrivateUsage`).
- **dotnet-counters:** for three rows, 12 s of `System.Runtime` counters were collected **after** sample B, so they do not affect the idle numbers.

The machine was not used during the run, with one exception: the screen was unlocked once, at 00:50:35, while the "working set trimmed once" row was running. That row was measured again afterwards on an idle machine (`-OutDir artifacts\results-rerun`), and the table shows the re-run. All other rows were collected in one uninterrupted run from 00:34 to 01:05 on 2026-11-17.

### How to re-run

```powershell
dotnet build spikes\common\SpikeCtl -c Release     # used by measure.ps1 to close the test app cleanly
cd spikes\03-perf-baseline
.\publish-variants.ps1                              # about 2 minutes; wpf-trimmed and win32-aot fail, see below
dotnet tool install -g dotnet-counters              # only for the counters rows
.\measure.ps1                                       # about 32 minutes; do not touch the machine
.\measure.ps1 -Only 'TYPICAL*' -OutDir artifacts\results-typical   # a subset, into another folder
```

Close heavy applications first. The script opens and closes small windows at the top left of the primary monitor; mouse or keyboard use during a row affects that row. A re-run into the same `-OutDir` overwrites `results.csv`.

## Results

All 17 measurable rows. Framework-dependent unless stated. "nc" = non-concurrent GC, "R2R" = ReadyToRun.

| Variant | Startup ms, median (min-max) | Idle CPU ms / 60 s | Wakeups / 60 s | Threads (woke / total, exited) | Working set MB | Private WS MB | Private bytes MB |
|---|---|---|---|---|---|---|---|
| wpf default | 481 (478-485) | 0 | 2 | 2 / 10, 3 | 97.7 | 30.0 | 71.6 |
| wpf non-concurrent GC | 475 (472-480) | 0 | 3 | 2 / 10, 4 | 97.7 | 29.9 | 69.8 |
| wpf ReadyToRun | 479 (475-493) | 0 | 2 | 2 / 10, 3 | 97.7 | 30.0 | 72.0 |
| wpf nc + R2R | 480 (474-488) | 0 | 2 | 2 / 10, 3 | 97.4 | 29.7 | 71.1 |
| wpf nc + R2R, TieredCompilation off | 496 (491-500) | 0 | 3 | 2 / 10, 3 | 97.7 | 30.0 | 68.8 |
| wpf nc + R2R, TieredPGO off | 475 (471-483) | 0 | 2 | 2 / 10, 3 | 99.0 | 31.4 | 72.1 |
| wpf nc + R2R, self-contained | 472 (467-488) | 0 | 2 | 2 / 10, 3 | 99.0 | 31.4 | 70.3 |
| wpf nc + R2R, software rendering | 405 (403-412) | 0 | 2 | 2 / 8, 3 | 74.8 | 17.7 | 38.3 |
| wpf nc + R2R, AllowsTransparency | 522 (518-534) | 0 | 3 | 2 / 9, 3 | 100.4 | 31.7 | 60.7 |
| wpf nc + R2R, working set trimmed once (re-run) | 579 (574-590) | 0 | 2 | 2 / 10, 3 | 11.9 | 4.2 | 69.9 |
| win32 on CoreCLR (nc + R2R) | 34 (33-35) | 0 | 0 | 0 / 4, 1 | 19.1 | 2.4 | 4.9 |
| win32 on CoreCLR, working set trimmed once | 33 (32-35) | 0 | 0 | 0 / 4, 0 | 2.7 | 0.8 | 5.0 |
| **TYPICAL** wpf default | 2681 (2675-2701) | 0 | 2 | 2 / 10, 3 | 123.5 | 49.1 | 114.1 |
| **TYPICAL** wpf nc + R2R | 2659 (2644-2688) | 0 | 3 | 2 / 10, 3 | 123.5 | 49.2 | 113.1 |
| **TYPICAL** wpf nc + R2R, software rendering | 546 (543-547) | 0 | 2 | 2 / 8, 3 | 87.6 | 29.8 | 50.4 |
| **TYPICAL** wpf nc + R2R, trimmed once | 2685 (2669-2694) | 0 | 2 | 2 / 10, 3 | 40.2 | 16.6 | 110.7 |
| **TYPICAL** win32 on CoreCLR | 76 (74-83) | 0 | 0 | 0 / 4, 1 | 19.2 | 2.5 | 5.0 |
| wpf trimmed (`PublishTrimmed`) | not measured: the SDK refuses to build it, see below | | | | | | |
| win32 NativeAOT, single and TYPICAL | not measured: the native link step fails on this machine, see below | | | | | | |

The first run, which was stopped after 6 rows, gave startup medians within 1 to 17 ms of these (480 to 513 ms). Its one outlier ("wpf non-concurrent GC", 1062 ms CPU and 5770 switches in 60 s) did not repeat: the re-measurement gave 0 ms and 3 wakeups. Its cause is unknown; user input over the test window during that row is the most likely explanation, but that was not verified.

### Effects of each option

| Option | Effect measured here | Verdict |
|---|---|---|
| **Non-concurrent GC** (`ConcurrentGarbageCollection=false`) | None on startup, idle or memory. No garbage collection happened at all during the idle window (`dotnet-counters`: 0 collections, no GC heap recorded yet), so there was no background GC to save. | Keep. It removes the background GC thread and its work once Tents has a real heap; it cost nothing here. [Docs](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector) |
| **ReadyToRun** (`PublishReadyToRun=true`) | None (479 vs 481 ms). WPF and the runtime already ship precompiled; the test app has almost no code of its own to compile. | Keep. Tents' own code will be much larger than the test app's; re-measure once it exists. [Docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run) |
| **TieredCompilation off** | 16 ms slower startup, no idle or memory gain. | Do not use. [Docs](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation) |
| **TieredPGO off** | No difference. | Leave at the default. |
| **Self-contained** | No measurable difference (472 vs 480 ms, within run-to-run spread). | Not worth the larger download on its own merits. M6 can decide based on installation, not performance. |
| **Software rendering** | Single tent: 75 ms faster, 23 MB less working set, 33 MB less private bytes, 2 fewer threads. Typical setup: **2.1 s faster**, 36 MB less working set, 63 MB less private bytes. Idle unchanged. | Adopt for M1 behind a switch, pending the interactive measurement. See [Rendering mode](#rendering-mode). |
| **AllowsTransparency** | 42 ms slower startup, working set unchanged, private bytes 10 MB lower, one fewer thread. | Avoid unless a feature needs per-pixel transparency. A tent with a solid background does not. |
| **Working set trimmed once** | Working set 97.7 to 11.9 MB, private WS 30.0 to 4.2 MB. Private bytes unchanged (69.9 MB): nothing is freed, the pages are only taken out of RAM until they are touched again. After 60 s idle they were still out, which confirms that idle WPF touches almost nothing. Typical setup: 123.5 to 40.2 MB. Startup with trimming was 100 ms slower on the single tent but not on the typical setup; the trim runs after the startup time is written, so this was not expected and was not investigated. | **Do not use it to meet a budget.** It moves cost to the next interaction (page faults when the user hovers or drags) and mainly changes what Task Manager shows. Budgets below are set on untrimmed numbers. [`SetProcessWorkingSetSize`](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-setprocessworkingsetsize) |
| **Trimming** (`PublishTrimmed`) | Not measurable: the SDK refuses with NETSDK1168, "WPF is not supported or recommended with trimming enabled". | Not available for WPF. [Docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities) |
| **NativeAOT** (Win32 test app only; WPF does not support it) | Not measured: the native link step (`link.exe`) failed because the Windows SDK libraries are not installed on this machine. Nothing was installed. | Only relevant to the Win32/Direct2D fallback, which is not needed. |

### Rendering mode

The typical setup is where the two modes differ:

| Typical setup (10 tents, 200 items) | Hardware (default) | Software (`SoftwareOnly`) |
|---|---|---|
| Startup to the last tent's first frame | 2659 ms | 546 ms |
| Startup cost per extra tent, compared with one tent | about 240 ms | about 16 ms |
| Working set | 123.5 MB | 87.6 MB |
| Private working set | 49.2 MB | 29.8 MB |
| Private bytes | 113.1 MB | 50.4 MB |
| Idle wakeups / CPU | 3 / 0 | 2 / 0 |

Why hardware rendering costs about 240 ms per window here was not investigated. The cost appears for each window, and the windows are created one after another on the UI thread, so it adds up. It may depend on the GPU, the driver or the virtual display adapter on this machine. Tents shows mostly static content, which suits software rendering, but **its cost while the user interacts (dragging a tent, resizing, scrolling a large tent, hover effects) was not measured.** Software rendering uses the CPU for every frame that does render, and a 420x400 px tent is small, but a large tent at 200% DPI is 4 times the pixels.

`RenderOptions.ProcessRenderMode` applies to the whole process ([docs](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.processrendermode)), so it is one switch in `Tents.App` startup.

### dotnet-counters observations

Collected for `wpf default`, `wpf nc + R2R` and `TYPICAL wpf nc + R2R`, 12 s each, after the idle window ([dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)):

- `dotnet.timer.count` **0**, thread-pool work items 0, thread-pool queue 0, lock contentions 0.
- **No garbage collection has happened** since startup: GC collections 0 per second, GC pause time 0, and every `last_collection` heap value is still 0. The idle app has no reason to collect.
- **Attaching the tool changes what it measures.** While `dotnet-counters` was attached, each app allocated 24 to 41 KB per second, JIT-compiled about 9 methods per second (133 in the first second), and its working set grew from 97.7 to 107 MB (typical: 123.5 to 134 MB). That is the diagnostics session itself (EventPipe), not the app. `dotnet-counters` is good for checking timers, GC and the thread pool, but **not for wakeup or memory numbers**. Use per-thread counters or WPR for those.

## Not measured

| Item | Why | How to measure it |
|---|---|---|
| **Windows 10 22H2, every row** | Not available here | On Windows 10 22H2: run the [re-run steps](#how-to-re-run). Check especially that idle wakeups stay at 2 to 3 per 60 s, and the hardware-vs-software startup difference on the typical setup. |
| **WPR / ETW trace** | WPR needs an elevated prompt; the spike ran without elevation ("Failed to enable the policy to profile system performance", 0xc5585011) | From an elevated PowerShell: `.\wpr-idle.ps1 -Variant wpf-nonconc-r2r -AppArgs '--tents=10','--items=20'`, then open the `.etl` in WPA, "CPU Usage (Precise)", filter to `WpfTent.exe`, and read Count and "Ready Thread Stack" per thread. This would show *what* the 2 to 3 wakeups are. ([WPR command line](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/wpr-command-line-options)) |
| **Which threads wake, and why** | Needs the WPR trace above | Same. 2 threads woke in every WPF row; 3 short-lived threads exited in every WPF row during the window (startup helpers finishing). |
| **Trimming** | NETSDK1168: the SDK does not support trimming WPF apps | Not possible for WPF. |
| **NativeAOT** | `link.exe` fails without the Windows SDK libraries; installing them was out of scope for this spike | Install the "Desktop development with C++" workload or the Windows SDK, then `.\publish-variants.ps1 -Only win32-aot` and `.\measure.ps1 -Only '*NativeAOT*' -OutDir artifacts\results-aot`. Only relevant if the fallback is ever reconsidered. |
| **Cold start** (first launch after sign-in or reboot) | Needs a reboot per sample | Add the app to `HKCU\...\Run` with `--marker=<path>` and reboot a few times. Expect it to be slower than the warm numbers. |
| **Interactive cost** (drag, resize, scroll, hover) in each rendering mode | Out of scope for an idle baseline; needs a person or input injection | M1, once the tent can be dragged and scrolled: CPU ms and frame count for a scripted drag, both render modes, at 100% and 200% DPI. |
| **Real icons** | The test uses 20 synthetic 48x48 bitmaps shared by all items | 200 distinct icons at 48 px are about 1.8 MB of pixels; at 96 px (200% DPI) about 7.4 MB. Re-measure with real shell icons in M1. |
| **Mixed DPI, second monitor, slower machines, integrated GPUs** | One machine, primary monitor only | Re-run on the machines in the release checklist. |

## Proposed final budgets

Set from the measured baseline with headroom for Tents' own code. All memory numbers are untrimmed, measured 30 s after startup with nothing changing, for the **typical setup** (10 tents, 200 items), on a machine like the one above. Recorded in `PROJECT_PLAN.md` §7.

| Budget | Value | Measured baseline |
|---|---|---|
| Idle CPU | **Below one scheduler tick (16 ms) of CPU time per 60 s** of idle | below 1 tick in every row |
| Idle wakeups | **At most 5 per 60 s**, whole process | 2 to 3 (WPF), 0 (Win32) |
| Timers | **0** (`dotnet.timer.count`), and no thread-pool work while idle | 0 |
| Garbage collections while idle | **0** | 0 |
| Warm startup to all tents drawn | **at most 1.0 s** | 0.55 s software, 2.7 s hardware (hardware fails this budget) |
| Private bytes | **at most 80 MB** | 50 MB software, 113 MB hardware |
| Private working set | **at most 45 MB** | 30 MB software, 49 MB hardware |
| Working set | **at most 120 MB** | 88 MB software, 124 MB hardware |

With hardware rendering the typical setup already fails the startup budget and the memory budgets, which is why software rendering is the M1 starting point. If the interactive measurement rules out software rendering, these budgets have to be revisited rather than quietly relaxed.

## Sources

Public documentation only. No other product's binaries, resources or source code were inspected. All numbers were observed on the machine described above.

- Microsoft Learn, [Runtime configuration options for garbage collection](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector) (Workstation vs Server, concurrent/background GC, `ConcurrentGarbageCollection`)
- Microsoft Learn, [ReadyToRun compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- Microsoft Learn, [Runtime configuration options for compilation](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation) (`TieredCompilation`, `TieredPGO`)
- Microsoft Learn, [Known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities) (WPF is not supported with trimming)
- Microsoft Learn, [dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)
- Microsoft Learn, [WPR command-line options](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/wpr-command-line-options)
- Microsoft Learn, [PROCESS_MEMORY_COUNTERS_EX](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex) (`PrivateUsage`, working set)
- Microsoft Learn, [SetProcessWorkingSetSize](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-setprocessworkingsetsize) (passing -1, -1 removes as many pages as possible from the working set)
- Microsoft Learn, [RenderOptions.ProcessRenderMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.processrendermode)
