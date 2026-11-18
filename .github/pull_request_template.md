## What and why

<!-- Describe the change. Name the MILESTONES.md step it implements (for example M1.5), or link the issue it fixes. Delete the line you don't need. -->

Step: M
Closes #

## How I tested it

<!-- Tests added or run, and any manual checks. -->

## Checklist

- [ ] `dotnet build -c Release`, `dotnet test` and `dotnet format --verify-no-changes` pass locally with no warnings
- [ ] Tests added or updated for behavior changes

### Performance

- [ ] No timers, polling or wakeups while idle (work is event-driven)
- [ ] Nothing renders or lays out while idle
- [ ] No new NuGet package, or its reason and startup/memory cost are described below
- [ ] If this touches a hot path, benchmark or profiling results are attached below
- [ ] I/O is debounced or coalesced where it could burst

### Clean-room

- [ ] I did not decompile, disassemble, debug or inspect any other product's binaries, resources or data files
- [ ] I have not seen other products' source code or internals for the parts this change relates to
- [ ] Names, icons, artwork, UI text and docs are original to Tents
- [ ] Any outside reference material this design relies on is linked below

## Performance results / sources / package justification

<!-- Benchmarks, profiling output, links to public documentation, or NuGet package justification. -->
