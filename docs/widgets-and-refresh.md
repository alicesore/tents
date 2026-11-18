# Live tents: content providers and the refresh policy

Status: accepted direction, nothing built yet. It sets the design for M4 (Live tents), builds on M3, and feeds the budgets in `PROJECT_PLAN.md` §7. The milestone steps are in `MILESTONES.md`.

## Goal

Tents starts as a container for desktop icons. The longer-term goal is a desktop you arrange the way you like: tents that hold live, interactive content (a folder, a calendar, a weather forecast, notes, an inbox), grouped into layouts you can switch between, with a look you control (color, corner radius, spacing, transparency). Everything stays lightweight: a tent that has nothing to show costs nothing.

This document answers one question: how does the performance focus (no timers, no polling, near-zero idle CPU, small memory) fit with live content? It adds one measurement to M1 step 12 and otherwise leaves the M1 and M2 plans alone.

## What the current constraints mean for live content

| Constraint | Source | Effect on live content |
|---|---|---|
| No timers, no polling, idle CPU effectively 0 | README, MILESTONES, spike 3 budgets | Weather, calendar and mail need refreshing. A fixed "0 timers" budget cannot hold once they exist. |
| Software rendering by default | M1 step 12, spike 3 | Fine for mostly static content. Scrolling lists, animation and blur cost CPU on every frame. Interactive cost is not yet measured. |
| Memory budgets (80 MB private bytes) | spike 3 | Set for 10 tents and 200 icons. Network, authentication and parsing code are not in those numbers. |
| The tent UI thread never blocks | M1 hosting decision | Any network, parsing or disk work for live content runs on other threads. |

The model is not a constraint. `Tents.Core` is pure and has no OS or UI dependency, and M3 already plans a tent "source". Live content extends that idea.

## Content providers

A tent's content comes from a **provider**. Desktop items (M1) and a folder path (M3) are the first two providers. Live content is further providers.

Rules:

1. **The core model stays pure.** `Tents.Core` holds only a provider kind (a stable string id), a version, and the provider's settings as plain data. It never references provider code.
2. **Providers live outside Core.** A provider implementation sits in `Tents.Shell` or its own project and is registered by kind id at startup.
3. **Unknown kinds survive a round trip.** If a layout file names a provider this build does not know, the tent is kept as is, shown as a placeholder, and written back unchanged. A newer layout opened by an older build must not lose data. This extends the recovery rules in [layout-file.md](layout-file.md).
4. **A provider declares its needs up front** (below), so the host can enforce the policy rather than trusting each provider.
5. **A provider is created lazily and disposed when its tent is removed or hidden.** A tent that is hidden by quick-hide or on an inactive layout holds no timers and no network work.

### What a provider declares

| Declaration | Values | Used for |
|---|---|---|
| Update source | `none`, `events`, `timed` | Decides what the host may schedule. |
| Minimum interval | duration, only for `timed` | Floor for refresh. The user can lengthen it, never shorten it below the floor. |
| Needs network | yes / no | Skipped on metered or offline state. |
| Rendering cost class | `static`, `scrolling`, `animated` | Input to the rendering mode decision. |
| Memory class | `small`, `large` | Input to the per-provider budget. |

## Refresh policy

The "no timers" rule becomes: **no timer exists unless a visible tent needs one, and then exactly one shared scheduler owns it.**

1. **Events first.** Prefer a file system watcher, a shell notification, a system event or a push channel. Providers with `events` cost nothing while nothing happens. This is how desktop items and folders already work.
2. **One scheduler.** Providers never create their own timers. They ask the host scheduler for a refresh with an interval. The host keeps at most one OS timer and sets it to the earliest due refresh.
3. **Coalesce.** Refreshes that are due within a window of each other run together, so the process wakes once rather than once per tent.
4. **Only while needed.** The scheduler does not tick while no timed tent is visible. Quick-hide, a different active layout, a locked session and display sleep all pause it. It resumes with one refresh, not a burst of missed ones.
5. **Back off on failure.** A failed refresh doubles the next interval up to a cap, and a tent that is offline waits for a network-change event instead of retrying.
6. **Work is off the UI thread.** The provider fetches and parses on a worker, and hands the UI thread one finished, immutable snapshot to draw.
7. **Rendering is event-driven.** A tent redraws when it receives a new snapshot or the user interacts. No render loop, no animation timers unless the user is interacting.
8. **The idle promise stays honest.** The README wording changes from "no CPU while idle" to "no CPU while idle unless a visible live tent is refreshing", and the release notes report both numbers.

M7's auto-update check is the same kind of documented exception and should use the same scheduler.

## Budgets

Budgets become per provider class, measured on the typical setup plus the provider.

| Setup | Idle CPU | Wakeups per 60 s | Timers | Private bytes |
|---|---|---|---|---|
| Desktop items and folders only (M1 to M3) | below one tick per 60 s | at most 5 | 0 | at most 80 MB (unchanged) |
| Plus live providers, between refreshes | below one tick per 60 s | at most 5 | at most 1 (the shared scheduler) | 80 MB plus a per-provider allowance, to be measured |
| During a refresh | no budget on CPU time, but under 1 s on the worker | n/a | n/a | no growth that survives a collection |

The per-provider allowance is not set here. It is measured when the first live provider exists, the same way spike 3 measured the baseline. If a provider needs more than its allowance, it ships as opt-in.

## Rendering

- Keep software rendering as the M1 default. Spike 3 shows software rendering with transparency meets every budget for static content.
- The interactive measurement in M1 step 12 should include one scrolling, frequently updating tent, not only an icon grid. That is the cheapest early signal for whether software rendering survives live content.
- Rich content (HTML bodies, embedded web views) is expected to break the memory budget, because a web view host adds a separate process. Treat it as an opt-in, large provider, or use a light custom renderer. Measure before deciding.
- Blur and acrylic backdrops (M2 step 2) stay optional and measured.

## First provider: notes

Notes is the first live provider, ahead of anything that needs the network. It needs only file system events, so it tests the provider lifecycle, editing and saving before the scheduler has anything to schedule.

- **Markdown files are the source of truth.** A note is a plain UTF-8 `.md` file on disk. The layout file stores only where the note is. Tents does not add front matter or any other marker to the file, and it preserves the file's existing encoding and line endings. Other editors and sync tools keep working with the same files.
- **Files stay put.** The project rule applies: Tents never moves, renames or deletes a note unless the user asks. Deleting a notes tent deletes the tent, not the file.
- **Events only.** Update source is `events`: a coalesced file watcher on the note's folder. No timer.
- **Never overwrite silently.** An external change while the user has unsaved edits keeps the user's text and offers the choice.
- **Saving reuses the existing pieces.** Atomic write and the debounced saver (one-shot delay) from M1 step 3.
- **Typing is a hosting problem, not a model problem.** Tents never take focus (`WS_EX_NOACTIVATE`). A notes tent needs the keyboard, so how editing gets input is decided in spike 4 before any editor is built.
- **A folder of notes** (one tent over a directory of `.md` files) is a natural follow-up on the M3 folder provider, not part of the first version.

## Appearance and layouts

These are already on the roadmap and need no new performance rules:

- Per-tent color, opacity, corner radius and spacing, plus follow the Windows accent color (M2 appearance). Changes are applied on a settings event, never on a timer.
- Switching between named layouts (M2 snapshots, M6 pages). Switching releases the providers of the layout that is no longer shown, which is what makes rule 4 of the refresh policy cheap.

## Proposed changes to the plan

| Where | Change |
|---|---|
| M3 step 1 | Model the tent source as a provider kind with versioned settings, not a closed enum of two cases. |
| M3 step 1 | Add the round-trip rule for unknown provider kinds to the layout file spec and its tests. |
| M1 step 12 | Include one scrolling, frequently updating tent in the interactive measurement. |
| `PROJECT_PLAN.md` §7 | Split the budgets by provider class as in the table above. |
| New M4 | Live tents milestone: spike, shared scheduler, provider declarations, notes first, then weather, per-provider allowance measured. Rules, pages and distribution move to M5 to M7. |
| New M8 | Account-backed tents (calendar and mail), after v1.0.0. |
| M7 step 4 | Reuse the shared scheduler for the update check. |

## Open questions

- Decided: notes first, then weather, then (M8) calendar and mail.
- Where do notes live by default (a Tents folder under `%APPDATA%`, or somewhere the user already keeps files), and can each tent point anywhere? Decide in the M4 notes PR and record it in `docs/notes.md`.
- Which Markdown renderer: a library, or a small renderer for a limited subset? Decide with a startup and memory measurement.
- Are providers built in, or loadable? Loadable providers raise trust and crash-isolation questions (a crash in a tent must not take the desktop's mouse input down). Built in is the safer start.
- Sign-in for mail and calendar: where tokens are stored, and which libraries are acceptable under the "new dependency needs a written reason and a measurement" rule.
- Metered connections and battery saver: should refresh pause by default?
- What is the real per-provider memory allowance on a Windows 10 22H2 machine? Spike 3 left Windows 10 untested.
