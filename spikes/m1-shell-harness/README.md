# M1 shell harness (throwaway)

A small window that drives the production `Tents.Shell` code, so M1 steps 9 (open, context menu) and 10 (drag and drop) can be checked by hand before the real tent window exists (step 11 onwards). It is not part of `Tents.sln`, and CI does not build it.

It lists the items of the desktop's native view in an ordinary window. It changes nothing by itself. Only a command you pick in a shell menu (Delete, Rename and so on) or a drop you make acts on files, exactly as it would on the desktop.

```powershell
dotnet run --project spikes\m1-shell-harness\ShellHarness -c Release
```

The manual checks, with what to click and what to expect, are listed in the PR of each step.
