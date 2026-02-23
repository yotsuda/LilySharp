// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

public class BenchmarkTest
{
    private readonly ITestOutputHelper _output;
    public BenchmarkTest(ITestOutputHelper output) => _output = output;

    private static string RenderSvg(SyntaxTree tree)
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        var renderer = new SvgRenderer();
        return renderer.Render(score, layout);
    }

    [Fact(Skip = "Benchmark test - run manually")]
    [Trait("Category", "Benchmark")]
    public void BenchmarkFurElise()
    {
        var source = File.ReadAllText(@"../../../../samples/music/fur-elise.lys");

        // Warmup
        for (int i = 0; i < 3; i++) {
            var tree = SyntaxTree.Parse(source);
            var svg = RenderSvg(tree);
        }

        // Benchmark
        var times = new List<double>();
        for (int i = 0; i < 10; i++) {
            var sw = Stopwatch.StartNew();
            var tree = SyntaxTree.Parse(source);
            var svg = RenderSvg(tree);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        _output.WriteLine($"Parse + SVG Export (fur-elise.lys, 10 runs):");
        _output.WriteLine($"  Min: {times.Min():F2} ms");
        _output.WriteLine($"  Max: {times.Max():F2} ms");
        _output.WriteLine($"  Avg: {times.Average():F2} ms");
    }
}