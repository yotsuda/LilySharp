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
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Benchmarks;

/// <summary>
/// F3/S5 probe: cost of the per-edit MeasureContentKey.Compute(MultiStaffScore) — the
/// benchmark-identified multi-staff bottleneck (runs every edit; ~1.6ms / ~3MB on
/// grammar-tour per S5-3b). Confirms the absolute cost before attacking it incrementally.
///
/// Run: dotnet run -c Release -- --filter '*ContentKey*'
/// </summary>
[Config(typeof(Config))]
public class ContentKeyBenchmark
{
    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithRuntime(CoreRuntime.Core90));
            AddDiagnoser(MemoryDiagnoser.Default);
        }
    }

    private MultiStaffScore _multi = null!;
    private MultiStaffScore _single = null!;

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
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/ directory");
    }

    private static MultiStaffScore Collect(string rel)
    {
        var path = Path.Combine(FindFixturesDir(), rel.Replace('/', Path.DirectorySeparatorChar) + ".lys");
        var tree = SyntaxTree.Parse(File.ReadAllText(path).Replace("\r\n", "\n"));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [GlobalSetup]
    public void Setup()
    {
        _multi = Collect("showcase/grammar-tour");
        _single = Collect("showcase/grammar-2026-06-09");
    }

    [Benchmark(Description = "Compute(MultiStaffScore) — grammar-tour (multi)")]
    public int Multi() => MeasureContentKey.Compute(_multi).Length;

    [Benchmark(Description = "Compute(MultiStaffScore) — grammar-2026-06-09 (single)")]
    public int Single() => MeasureContentKey.Compute(_single).Length;
}
