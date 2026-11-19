# Rendering mode (M1 step 12)

**Decision: keep software rendering (`RenderOptions.ProcessRenderMode = SoftwareOnly`) as the default at every scale, with `--hardware-rendering` to opt out, and keep `AllowsTransparency` off.** The budgets in `PROJECT_PLAN.md` §7 stand; software rendering is not ruled out, so nothing there is revisited.

**The cost: resizing a tent at 200% scaling.** That is the one place where software rendering is clearly slower: about 28 ms per step against 13 ms in hardware rendering (see [Results at 200%](#results-at-200-192-dpi) and [Why software rendering stays at 200%](#why-software-rendering-stays-at-200)).

## Method

`bench/Tents.RenderMeasure`, run by `measure-rendering.ps1 -Repetitions 3`: four configurations (software or hardware rendering, each with and without `AllowsTransparency`), three runs each, medians below. Every run creates one tent with **200 synthetic items** (empty files of 10 types in a temporary folder, resolved through the shell for real file-type icons) and a second **live tent** that gains an item and scrolls to it every step. Both use the app's own `TentWindow`, `ItemGridView`, `IconCache` and `OwnedPinHost`. Each scenario is 120 steps driven from code, and after each step the tool waits until WPF has rendered it.

- **drag**: move the tent 4 × 2 px per step.
- **resize**: grow it 3 × 2 px per step, ending 360 × 240 px larger.
- **scroll**: scroll through the whole 200-item extent.
- **hover**: move the hover highlight over the visible cells.
- **live**: the live tent gains an item and scrolls to the end, every step. This is the early signal for [M4](../MILESTONES.md#m4-live-tents).
- **startup**: tent creation until its first `ContentRendered`.
- **memory**: private bytes and working set after all scenarios.

Measured 2026-11-18 on Windows 11 Pro 26H2, Ryzen 7 5700X3D, GeForce RTX 5070 Ti, .NET 10.0.15. The primary monitor is 2560 × 1440. It was measured once at 100% (96 DPI) and once at 200% (192 DPI), with the scale set in Settings and the same command run again. Tent sizes are in DIPs, so at 200% the measured tent is four times the pixels: 840 × 1200 px at the start, growing to 1560 × 1680 px.

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

## Results at 200% (192 DPI)

| Scenario | Software | Software, transparent | Hardware | Hardware, transparent |
|---|---|---|---|---|
| Startup, first frame | 376 ms | 388 ms | 440 ms | 513 ms |
| Drag, CPU per step | 0.65 ms | 0.78 ms | 0.91 ms | 0.65 ms |
| Resize, CPU per step | 40.5 ms | 47.7 ms | 12.6 ms | 25.5 ms |
| Resize, wall time per step | 27.7 ms | 73.4 ms | 12.8 ms | 51.1 ms |
| Resize, frames | 252 | 694 | 282 | 644 |
| Scroll, CPU per step | 4.17 ms | 3.12 ms | 2.47 ms | 2.47 ms |
| Hover, CPU per step | 1.43 ms | 1.43 ms | 0.78 ms | 1.30 ms |
| Live, CPU per step | 1.30 ms | 1.43 ms | 0.78 ms | 1.43 ms |
| Private bytes after scenarios | 112 MB | 113 MB | 259 MB | 237 MB |
| Working set after scenarios | 172 MB | 179 MB | 195 MB | 203 MB |

Resize CPU over 120 steps ranged from 4813 to 4906 ms in software rendering and from 1328 to 1625 ms in hardware rendering. Every other scenario stayed within one or two timer ticks across runs, except software scroll, which ranged from 328 to 531 ms.

## Reading

- **Drag, scroll, hover and live cost the same in both modes,** within the timer resolution. Every step finishes in under 3 ms.
- **Resize is the only place where software rendering costs more:** about 26% more CPU (14.5 vs 11.5 ms per step). Wall time is the same, about 12 ms per step, which leaves room for 60 frames per second. Resizing a tent is short and user-driven, so this is acceptable.
- **Hardware rendering doubles private bytes** (218 vs 106 MB) and starts slower. Together with spike 3's idle numbers (hardware 113 MB private bytes and 2.7 s startup, both over budget), software stays the default.
- **`AllowsTransparency` makes resize 1.6 to 2.5 times more expensive** in both modes and doubles the frames. Drag, scroll, hover and live do not change. Tents stays opaque. Any future translucency should come from DWM (for example system backdrop materials), not from layered windows.
- **Live content:** a tent that gains an item and scrolls every step costs about 1 ms of CPU per update in software rendering (1.3 ms at 200%). That is no obstacle for M4's refresh rates.
- **At 200%, software rendering scales with pixels but hardware rendering does not.** Software resize CPU almost triples, from 14.5 to 40.5 ms per step, and wall time goes from 12 to 28 ms per step, which is about 36 updates per second. Hardware resize barely changes (11.5 to 12.6 ms). Scroll CPU goes from 2.5 to 4.2 ms per step. Drag, hover and live stay at 1.5 ms or less.
- **Memory hardly depends on scale in software rendering** (106 to 112 MB). In hardware rendering it grows from 218 to 259 MB.

## Why software rendering stays at 200%

Before the 200% pass, this write-up set the threshold for a second look: resize above about 16 ms of wall time per step at 200%. Software rendering crosses it, at 28 ms. Looking again:

- **It costs only while resizing.** Resizing is short and user-driven. It still runs at about 36 updates per second, on a tent of 1560 × 1680 px. Moving, scrolling, hovering and live updates stay well under one frame at 200%.
- **Hardware rendering costs all the time.** It fails the memory and startup budgets while idle (spike 3: 113 MB private bytes and 2.7 s startup), and here it uses more than twice the private memory at every scale. For an app whose first rule is staying light in the background, that is worse than slower resizes.
- **A per-monitor renderer is possible but unmeasured.** WPF can set the render mode per window ([`HwndTarget.RenderMode`](https://learn.microsoft.com/dotnet/api/system.windows.interop.hwndtarget.rendermode)), so only tents on high-DPI monitors could use hardware rendering. Nothing here shows how much memory a process keeps once one window has used hardware rendering. That would need its own measurement before it could be considered.

**To review:** if 200% resizing feels slow in the manual checks, the next step is to make software resizing cheaper, not to switch renderer. For example, the item grid could skip redrawing cells that did not move during a resize. `--hardware-rendering` stays as the opt-out.

## Caveats

- **Private bytes after the scenarios (106 MB, 112 MB at 200%) are above the 80 MB idle budget,** but this is not the budget's measurement. It is taken right after the scenarios, with 320 resolved items, a tent grown to about 780 × 840 DIPs, no settle time and no collection. The budget is measured 30 s after startup with nothing changing. Step 22's idle check is the real test, and it should watch whether a resize leaves memory behind.
- Steps are driven from code (`SetWindowPos`, the view's scroll and hover), not by real mouse input. The system's own drag handling and input latency are not in these numbers.
- Frames counted are WPF render passes, not frames DWM presented.
- Only one machine, with a fast GPU. Hardware rendering may look better or worse on integrated graphics. Software rendering does not depend on the GPU.

## Not measured

- Windows 10 22H2.
- 150% and other in-between scales, and a tent that spans two monitors with different DPI.
- Integrated graphics.
