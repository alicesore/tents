# Rendering mode (M1 step 12)

**Decision: keep software rendering (`RenderOptions.ProcessRenderMode = SoftwareOnly`) as the default, with `--hardware-rendering` to opt out, and keep `AllowsTransparency` off.** The budgets in `PROJECT_PLAN.md` §7 stand; software rendering is not ruled out, so nothing there is revisited. The 200% pass is still to do (see [Not measured yet](#not-measured-yet)).

## Method

`bench/Tents.RenderMeasure`, run by `measure-rendering.ps1 -Repetitions 3`: four configurations (software or hardware rendering, each with and without `AllowsTransparency`), three runs each, medians below. Every run creates one tent with **200 synthetic items** (empty files of 10 types in a temporary folder, resolved through the shell for real file-type icons) and a second **live tent** that gains an item and scrolls to it every step. Both use the app's own `TentWindow`, `ItemGridView`, `IconCache` and `OwnedPinHost`. Each scenario is 120 steps driven from code, and after each step the tool waits until WPF has rendered it.

- **drag**: move the tent 4 × 2 px per step.
- **resize**: grow it 3 × 2 px per step, ending 360 × 240 px larger.
- **scroll**: scroll through the whole 200-item extent.
- **hover**: move the hover highlight over the visible cells.
- **live**: the live tent gains an item and scrolls to the end, every step. This is the early signal for [M4](../MILESTONES.md#m4-live-tents).
- **startup**: tent creation until its first `ContentRendered`.
- **memory**: private bytes and working set after all scenarios.

Measured 2026-11-18 on Windows 11 Pro 26H2, Ryzen 7 5700X3D, GeForce RTX 5070 Ti, .NET 10.0.15. The primary monitor was 2560 × 1440 at 100% (96 DPI).

## Results at 100% (96 DPI)

CPU is the process's CPU time per step. Windows reports it in 15.6 ms ticks, so totals under about 200 ms (0.8 ms per step here) are only good to one or two ticks. Frames are WPF render passes (`CompositionTarget.Rendering`).

| Scenario | Software | Software, transparent | Hardware | Hardware, transparent |
|---|---|---|---|---|
| Startup, first frame | 376 ms | 372 ms | 436 ms | 500 ms |
| Drag, CPU per step | 0.78 ms | 0.78 ms | 0.78 ms | 0.52 ms |
| Resize, CPU per step | 14.5 ms | 26.0 ms | 11.5 ms | 18.8 ms |
| Resize, wall time per step | 12.2 ms | 36.4 ms | 11.7 ms | 27.0 ms |
| Resize, frames | 266 | 544 | 264 | 477 |
| Scroll, CPU per step | 2.47 ms | 2.47 ms | 2.34 ms | 2.21 ms |
| Hover, CPU per step | 0.78 ms | 1.17 ms | 1.43 ms | 0.65 ms |
| Live, CPU per step | 1.04 ms | 0.91 ms | 0.91 ms | 0.78 ms |
| Private bytes after scenarios | 106 MB | 109 MB | 218 MB | 218 MB |
| Working set after scenarios | 166 MB | 168 MB | 187 MB | 193 MB |

Run-to-run spread was within one or two timer ticks for every scenario except resize. Resize CPU ranged 1656 to 1953 ms (software) and 1125 to 1391 ms (hardware) over 120 steps.

## Reading

- **Drag, scroll, hover and live cost the same in both modes,** within the timer resolution. Every step finishes in under 3 ms.
- **Resize is the only place where software rendering costs more:** about 26% more CPU (14.5 vs 11.5 ms per step). Wall time is the same, about 12 ms per step, which leaves room for 60 frames per second. Resizing a tent is short and user-driven, so this is acceptable.
- **Hardware rendering doubles private bytes** (218 vs 106 MB) and starts slower. Together with spike 3's idle numbers (hardware 113 MB private bytes and 2.7 s startup, both over budget), software stays the default.
- **`AllowsTransparency` makes resize 1.6 to 2.5 times more expensive** in both modes and doubles the frames. Drag, scroll, hover and live do not change. Tents stays opaque. Any future translucency should come from DWM (for example system backdrop materials), not from layered windows.
- **Live content:** a tent that gains an item and scrolls every step costs about 1 ms of CPU per update in software rendering. That is no obstacle for M4's refresh rates.

## Caveats

- **Private bytes after the scenarios (106 MB) are above the 80 MB idle budget,** but this is not the budget's measurement. It is taken right after the scenarios, with 320 resolved items, a tent grown to about 780 × 840 px, no settle time and no collection. The budget is measured 30 s after startup with nothing changing. Step 22's idle check is the real test, and it should watch whether a resize leaves memory behind.
- Steps are driven from code (`SetWindowPos`, the view's scroll and hover), not by real mouse input. The system's own drag handling and input latency are not in these numbers.
- Frames counted are WPF render passes, not frames DWM presented.
- Only one machine, with a fast GPU. Hardware rendering may look better or worse on integrated graphics. Software rendering does not depend on the GPU.

## Not measured yet

- **200% scaling.** This needs the display scale changed by hand (Settings > Display > Scale), then `measure-rendering.ps1 -Repetitions 3` again. Software rendering draws four times the pixels at 200%, so resize is the scenario to watch. If resize goes over about 16 ms of wall time per step at 200%, reconsider the default for high-DPI monitors only.
- Windows 10 22H2.
