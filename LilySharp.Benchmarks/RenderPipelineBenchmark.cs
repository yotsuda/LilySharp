using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
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
    private string _furEliseSource = null!;
    private string _minuetSource = null!;
    private string _happyBirthdaySource = null!;

    // Pre-parsed trees for stage-specific benchmarks
    private SyntaxTree _furEliseTree = null!;
    private Score _furEliseScore = null!;
    private ScoreLayout _furEliseLayout = null!;

    private static string FindSamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find samples/ directory");
    }

    [GlobalSetup]
    public void Setup()
    {
        var samplesDir = FindSamplesDir();
        _furEliseSource = File.ReadAllText(Path.Combine(samplesDir, "music", "fur-elise.lys"));
        _minuetSource = File.ReadAllText(Path.Combine(samplesDir, "music", "minuet.lys"));
        _happyBirthdaySource = File.ReadAllText(Path.Combine(samplesDir, "music", "happy-birthday.lys"));

        // Pre-compute for stage-specific benchmarks
        _furEliseTree = SyntaxTree.Parse(_furEliseSource);
        var collector = new MeasureCollector();
        _furEliseScore = collector.Collect(_furEliseTree, null);
        var layoutEngine = new LayoutEngine();
        _furEliseLayout = layoutEngine.Layout(_furEliseScore);
    }

    // === Full Pipeline Benchmarks ===

    [Benchmark(Description = "Full pipeline: Fur Elise")]
    public string FullPipeline_FurElise()
    {
        var tree = SyntaxTree.Parse(_furEliseSource);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }

    [Benchmark(Description = "Full pipeline: Minuet")]
    public string FullPipeline_Minuet()
    {
        var tree = SyntaxTree.Parse(_minuetSource);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }

    [Benchmark(Description = "Full pipeline: Happy Birthday")]
    public string FullPipeline_HappyBirthday()
    {
        var tree = SyntaxTree.Parse(_happyBirthdaySource);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }

    // === Individual Stage Benchmarks (Fur Elise) ===

    [Benchmark(Description = "Stage 1: Parse")]
    public SyntaxTree Stage_Parse()
    {
        return SyntaxTree.Parse(_furEliseSource);
    }

    [Benchmark(Description = "Stage 2: Collect")]
    public Score Stage_Collect()
    {
        var collector = new MeasureCollector();
        return collector.Collect(_furEliseTree, null);
    }

    [Benchmark(Description = "Stage 3: Layout")]
    public ScoreLayout Stage_Layout()
    {
        var layoutEngine = new LayoutEngine();
        return layoutEngine.Layout(_furEliseScore);
    }

    [Benchmark(Description = "Stage 4: Render SVG")]
    public string Stage_RenderSvg()
    {
        var renderer = new SvgRenderer(renderOptions: new SvgRenderOptions { EmbedFont = false });
        return renderer.Render(_furEliseScore, _furEliseLayout);
    }
}
