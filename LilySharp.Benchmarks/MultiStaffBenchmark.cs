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
