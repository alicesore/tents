using BenchmarkDotNet.Running;

// Entry point for all benchmark suites. Run with: dotnet run -c Release --project bench/Tents.Benchmarks -- --filter "*"
// No suites exist yet; hot paths in Tents.Core get benchmarks as they are written (M1+).
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
