using BenchmarkDotNet.Running;
using LilySharp.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RenderPipelineBenchmark).Assembly).Run(args);
