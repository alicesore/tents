# Contributing to Tents

Thanks for helping out. Tents is a small, performance-focused project, so a few rules matter more than usual. Please read the clean-room rules and the performance rules before you open a PR.

## Clean-room rules

Tents' functionality is inspired by Stardock Fences. Tents is a clean-room implementation, and all contributors must follow these rules:

1. **No reverse engineering.** Don't decompile, disassemble, debug or otherwise inspect another product's binaries, resources or data files.
2. **Use public information only.** Designs may be based only on public documentation and on behavior that an ordinary user can see.
3. **Record your sources.** When a design decision relies on outside reference material, link the source in the issue or PR.
4. **Make everything original.** Names, icons, artwork, UI text and documentation must all be original to Tents.
5. **Don't contribute tainted work.** If you have seen another product's source code or internals, don't contribute to the parts it relates to.

## Performance rules

Being incredibly light in the background is one of the most important things about Tents. A feature that breaks the performance budgets is not finished. In short:

- **No timers and no polling.** Work is triggered only by file system events, shell notifications and window messages.
- **Nothing renders while idle.** No running animations, and no layout passes unless something changed.
- **Minimal dependencies.** Every new NuGet package needs a written reason, and its startup and memory cost must be checked.
- **Debounce I/O.** Layout saves and file-change handling are coalesced.
- PRs that touch hot paths include benchmark or profiling results.

## Building

Requirements: Windows 10 22H2 or Windows 11, and the .NET SDK 10.0.x (see `global.json`). Visual Studio or VS Code with C# Dev Kit both work.

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet format --verify-no-changes
```

CI runs the same steps on every push and pull request, and your PR needs to be green.

### Solution layout

| Project | Purpose |
|---|---|
| `src/Tents.Core` | Tent and layout models, rules, persistence. No UI or Win32 dependencies. |
| `src/Tents.Shell` | Win32 and shell interop (CsWin32). References Core. |
| `src/Tents.App` | WPF app. References Core and Shell. |
| `tests/Tents.Tests` | xUnit tests. |
| `bench/Tents.Benchmarks` | BenchmarkDotNet suites for hot paths in Core. |

A test fails if `Tents.Core` gains a UI, Win32 or Shell reference, so keep platform code in `Tents.Shell` or `Tents.App`.

### Running benchmarks

```powershell
dotnet run -c Release --project bench/Tents.Benchmarks -- --filter "*"
```

## Code style

- The `.editorconfig` in the repo root is the source of truth, and `dotnet format --verify-no-changes` must pass.
- Nullable reference types are on, and **warnings are errors**. Fix the warning rather than suppressing it. If a suppression is truly needed, add a comment that explains why.
- Use file-scoped namespaces, braces on all blocks, and `_camelCase` for private fields.
- Keep `Tents.Core` pure and deterministic so it is easy to unit test.
- Tents never moves, renames or deletes the user's files unless the user explicitly asks it to.

## Pull requests

- Keep PRs small and focused: one logical change.
- Add or update tests for behavior changes.
- Fill in the PR template, including the performance and clean-room checklist.
- Link any outside sources your design relies on (clean-room rule 3).

## Planned work and issues

Planned work is tracked in the [Tents roadmap](https://github.com/users/alicesore/projects/1) project: one item for each step in [MILESTONES.md](MILESTONES.md), with its status. A PR for a step names it (for example "Step: M1.5"), and the item is set to Done when the PR is merged. Steps don't get their own issues.

Issues are for things that aren't in the plan yet: bug reports, feature requests, performance problems and proposed spikes. Use the issue templates: bug, feature, spike (a time-boxed investigation) and performance. Labels: `bug`, `feature`, `performance`, `spike`, `good first issue`, `help wanted`.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
