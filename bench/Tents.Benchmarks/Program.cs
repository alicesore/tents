using BenchmarkDotNet.Running;

// Entry point for all benchmark suites. Run with: dotnet run -c Release --project bench/Tents.Benchmarks -- --filter "*"
// Suites: LayoutPersistenceBenchmarks (layout file load and save, M1 step 4).
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
