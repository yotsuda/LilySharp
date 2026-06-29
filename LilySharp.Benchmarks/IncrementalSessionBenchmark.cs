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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Benchmarks;

/// <summary>
/// F0/§19.7 — warm-session single-edit latency for the F3 IncrementalCompiler.
///
/// Unlike <see cref="IncrementalEditBenchmark"/> (which times a cold render of an
/// edited tree), this measures ONE <see cref="IncrementalCompiler.Edit"/> on a
/// session whose caches are already warm — the real LSP shape, and the only place
/// the S4b line-break DP-skip can show. The session is mutated by Edit, so it is
/// rebuilt+warmed in [IterationSetup] and exactly one edit is timed per iteration
/// (InvocationCount=1, UnrollFactor=1).
///
/// Two edits bracket the behaviour:
///   WidthPreserving — a leading newline (pure trivia): no measure's natural width
///                     changes, so the gate is unchanged and the break DP is SKIPPED.
///   WidthChanging   — inserting a note: a measure's spring vector changes, so the
///                     break DP runs fully. The gap between the two is S4b's payoff;
///                     what remains in the cheap case is layout+render — the S5 target.
///
/// Run: dotnet run -c Release -- --filter '*IncrementalSession*'
/// </summary>
[Config(typeof(Config))]
public class IncrementalSessionBenchmark
{
    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default
                .WithRuntime(CoreRuntime.Core90)
                .WithInvocationCount(1)
                .WithUnrollFactor(1));
            AddDiagnoser(MemoryDiagnoser.Default);
        }
    }

    private const string Fixture = "showcase/grammar-tour";

    private SyntaxTree _tree = null!;
    private TextChange _widthPreservingEdit;
    private TextChange _widthChangingEdit;
    private IncrementalCompiler _session = null!;

    private static readonly SvgRenderOptions Options = new() { EmbedFont = false };

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

    [GlobalSetup]
    public void Setup()
    {
        var path = Path.Combine(FindFixturesDir(), Fixture.Replace('/', Path.DirectorySeparatorChar) + ".lys");
        var source = File.ReadAllText(path).Replace("\r\n", "\n");
        _tree = SyntaxTree.Parse(source);

        // Leading newline: pure trivia, so no token (and no measure) changes —
        // the line-break gate is unchanged and Edit takes the skip path.
        _widthPreservingEdit = new TextChange(new TextSpan(0, 0), "\n");
        // A note insertion near the middle: changes a measure's natural width, so
        // the break DP runs fully.
        _widthChangingEdit = new TextChange(new TextSpan(source.Length / 2, 0), " c4");
    }

    [IterationSetup]
    public void WarmSession()
    {
        _session = new IncrementalCompiler(_tree, Options);
        _session.Render();
    }

    [Benchmark(Description = "Warm session edit: width-preserving (DP skipped)")]
    public string Session_WidthPreserving() => _session.Edit(_widthPreservingEdit);

    [Benchmark(Description = "Warm session edit: width-changing (full rebreak)")]
    public string Session_WidthChanging() => _session.Edit(_widthChangingEdit);
}
