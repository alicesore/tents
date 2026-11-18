# Spikes (throwaway)

Everything under `spikes/` is **throwaway prototype code** written to answer the M0 spike questions.

- It is not part of `Tents.sln` and is not built, tested or format-checked by CI.
- It does not follow the production rules (analyzers, warnings as errors, CsWin32, project boundaries).
- Nothing here may be referenced from `src/`. Production code is written fresh in M1 from the findings in `docs/spikes/`.
- Clean-room rules still apply: the code uses only documented Windows APIs, publicly described techniques (linked in each write-up) and behavior observed on a stock Windows install.

`m1-shell-harness/` is different: a throwaway window that references the production `Tents.Shell` (the allowed direction), for the manual checks of M1 steps 9 and 10.

These prototypes run against the real desktop. Read the write-up for a spike before running its prototype.
