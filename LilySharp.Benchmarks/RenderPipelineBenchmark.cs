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

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Benchmarks;

/// <summary>
/// Benchmarks for the full SVG rendering pipeline and individual stages.
/// Run: dotnet run -c Release -- --filter '*RenderPipeline*'
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class RenderPipelineBenchmark
{
    private string _grammarTourSource = null!;
    private string _featureTourSource = null!;
    private string _choraleSource = null!;

    // Pre-parsed trees for stage-specific benchmarks (run on the largest fixture)
    private SyntaxTree _grammarTourTree = null!;
    private Score _grammarTourScore = null!;
    private MultiStaffScore _grammarTourMulti = null!;
    private ScoreLayout _grammarTourMultiLayout = null!;

    // The old samples/music/*.lys pieces (fur-elise/minuet/happy-birthday) were
    // removed; benchmark against the largest live test fixtures instead.
    private static string FindFixturesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures directory");
    }

    [GlobalSetup]
    public void Setup()
    {
        var fixturesDir = FindFixturesDir();
        _grammarTourSource = File.ReadAllText(Path.Combine(fixturesDir, "showcase", "grammar-tour.lys"));
        _featureTourSource = File.ReadAllText(Path.Combine(fixturesDir, "test", "feature-tour.lys"));
        _choraleSource = File.ReadAllText(Path.Combine(fixturesDir, "showcase", "08-chorale.lys"));

        // Pre-compute for stage-specific benchmarks
        _grammarTourTree = SyntaxTree.Parse(_grammarTourSource);
        var collector = new MeasureCollector();
        _grammarTourScore = collector.Collect(_grammarTourTree, null);
        var layoutEngine = new LayoutEngine();
        _grammarTourMulti = MultiStaffScore.FromScore(_grammarTourScore);
        _grammarTourMultiLayout = layoutEngine.Layout(_grammarTourMulti);
    }

    // === Full Pipeline Benchmarks ===

    [Benchmark(Description = "Full pipeline: grammar-tour")]
    public string FullPipeline_GrammarTour()
    {
        var tree = SyntaxTree.Parse(_grammarTourSource);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }

    [Benchmark(Description = "Full pipeline: feature-tour")]
    public string FullPipeline_FeatureTour()
    {
        var tree = SyntaxTree.Parse(_featureTourSource);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }

    [Benchmark(Description = "Full pipeline: chorale")]
    public string FullPipeline_Chorale()
    {
        var tree = SyntaxTree.Parse(_choraleSource);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }

    // === Individual Stage Benchmarks (grammar-tour) ===

    [Benchmark(Description = "Stage 1: Parse")]
    public SyntaxTree Stage_Parse()
    {
        return SyntaxTree.Parse(_grammarTourSource);
    }

    [Benchmark(Description = "Stage 2: Collect")]
    public Score Stage_Collect()
    {
        var collector = new MeasureCollector();
        return collector.Collect(_grammarTourTree, null);
    }

    [Benchmark(Description = "Stage 3: Layout")]
    public ScoreLayout Stage_Layout()
    {
        var layoutEngine = new LayoutEngine();
        return layoutEngine.Layout(_grammarTourMulti);
    }

    [Benchmark(Description = "Stage 4: Render SVG")]
    public string Stage_RenderSvg()
    {
        using var doc = new SvgDocumentContext(new SvgDocumentOptions { EmbedFont = false });
        SharedRenderer.RenderTo(_grammarTourMulti, _grammarTourMultiLayout, doc);
        doc.Dispose();
        return doc.ToSvg();
    }
}
