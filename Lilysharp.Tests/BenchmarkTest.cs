using System.Diagnostics;
using Lilysharp.Core.Syntax;
using Lilysharp.Core.Svg;
using Xunit;
using Xunit.Abstractions;

namespace Lilysharp.Tests;

public class BenchmarkTest
{
    private readonly ITestOutputHelper _output;
    public BenchmarkTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public void BenchmarkFurElise()
    {
        var source = File.ReadAllText(@"../../../../samples/fur-elise.lys");
        
        // Warmup
        for (int i = 0; i < 3; i++) {
            var tree = SyntaxTree.Parse(source);
            var svg = new SvgExporter().Export(tree);
        }
        
        // Benchmark
        var times = new List<double>();
        for (int i = 0; i < 10; i++) {
            var sw = Stopwatch.StartNew();
            var tree = SyntaxTree.Parse(source);
            var svg = new SvgExporter().Export(tree);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        
        _output.WriteLine($"Parse + SVG Export (fur-elise.lys, 10 runs):");
        _output.WriteLine($"  Min: {times.Min():F2} ms");
        _output.WriteLine($"  Max: {times.Max():F2} ms");
        _output.WriteLine($"  Avg: {times.Average():F2} ms");
    }
}