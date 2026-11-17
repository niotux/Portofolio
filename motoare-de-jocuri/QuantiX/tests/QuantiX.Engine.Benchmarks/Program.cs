using BenchmarkDotNet.Running;
using QuantiX.Engine.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RefBenchmarks).Assembly).Run(args);