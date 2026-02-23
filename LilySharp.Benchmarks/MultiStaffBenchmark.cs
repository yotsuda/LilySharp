using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Benchmarks;

/// <summary>
/// Benchmarks for multi-staff (grand staff) rendering.
/// Run: dotnet run -c Release -- --filter '*MultiStaff*'
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class MultiStaffBenchmark
{
    private string _pianoSource = null!;
    private string _advancedSource = null!;

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
        _pianoSource = File.ReadAllText(Path.Combine(samplesDir, "showcase", "03-piano.lys"));
        _advancedSource = File.ReadAllText(Path.Combine(samplesDir, "showcase", "04-advanced.lys"));
    }

    [Benchmark(Description = "Multi-staff: Piano (grand staff)")]
    public string Piano_GrandStaff()
    {
        var tree = SyntaxTree.Parse(_pianoSource);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }

    [Benchmark(Description = "Multi-staff: Advanced showcase")]
    public string Advanced_Showcase()
    {
        var tree = SyntaxTree.Parse(_advancedSource);
        var options = new SvgRenderOptions { EmbedFont = false };
        return SvgGenerator.Generate(tree, options);
    }
}
