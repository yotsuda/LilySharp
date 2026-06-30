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
///   WidthPreserving — a leading newline (pure trivia): no measure's content or natural
///                     width changes. Post-F3/B this takes the WHOLE-LAYOUT REUSE path —
///                     LayoutEngine.Layout is skipped outright (not just the break DP),
///                     and the renderer re-derives data-pos from the live score. Only
///                     collect + render remain. This is the B payoff over S4b/S5-3.
///   WidthChanging   — inserting a note: a measure's spring vector changes, so reuse is
///                     declined and the full layout (break DP + system layout) runs. The
///                     gap between the two is the reuse payoff on a content-unchanged edit.
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

    // grammar-tour is MULTI-staff (the S5-3a per-system cache does not engage);
    // grammar-2026-06-09 is the largest SINGLE-staff fixture (cache engages).
    private const string MultiFixture = "showcase/grammar-tour";
    private const string SingleFixture = "showcase/grammar-2026-06-09";

    private SyntaxTree _tree = null!;
    private TextChange _widthPreservingEdit;
    private TextChange _widthChangingEdit;
    private IncrementalCompiler _session = null!;

    private SyntaxTree _singleTree = null!;
    private TextChange _singleWidthPreservingEdit;
    private TextChange _singleWidthChangingEdit;
    private IncrementalCompiler _singleSession = null!;

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
        var fixtures = FindFixturesDir();

        var source = File.ReadAllText(
            Path.Combine(fixtures, MultiFixture.Replace('/', Path.DirectorySeparatorChar) + ".lys")).Replace("\r\n", "\n");
        _tree = SyntaxTree.Parse(source);
        // Leading newline: pure trivia, so no token (and no measure) changes —
        // the line-break gate is unchanged and Edit takes the skip path.
        _widthPreservingEdit = new TextChange(new TextSpan(0, 0), "\n");
        // A note insertion near the middle: changes a measure's natural width, so
        // the break DP runs fully.
        _widthChangingEdit = new TextChange(new TextSpan(source.Length / 2, 0), " c4");

        var single = File.ReadAllText(
            Path.Combine(fixtures, SingleFixture.Replace('/', Path.DirectorySeparatorChar) + ".lys")).Replace("\r\n", "\n");
        _singleTree = SyntaxTree.Parse(single);
        _singleWidthPreservingEdit = new TextChange(new TextSpan(0, 0), "\n");
        _singleWidthChangingEdit = new TextChange(new TextSpan(single.Length / 2, 0), " c4");

        // Sanity: the width-preserving edits MUST take the whole-layout reuse path,
        // otherwise this benchmark silently measures the wrong thing. Fail fast if not.
        VerifyReuses(_tree, _widthPreservingEdit, nameof(Multi_WidthPreserving));
        VerifyReuses(_singleTree, _singleWidthPreservingEdit, nameof(Single_WidthPreserving));
    }

    private static void VerifyReuses(SyntaxTree tree, TextChange edit, string label)
    {
        var probe = new IncrementalCompiler(tree, Options);
        probe.Render();
        probe.Edit(edit);
        if (!probe.LastEditReusedLayout)
            throw new InvalidOperationException(
                $"{label}: expected whole-layout reuse to fire, but it did not.");
    }

    [IterationSetup]
    public void WarmSession()
    {
        _session = new IncrementalCompiler(_tree, Options);
        _session.Render();
        _singleSession = new IncrementalCompiler(_singleTree, Options);
        _singleSession.Render();
    }

    [Benchmark(Description = "multi-staff edit: width-preserving (systems reused)")]
    public string Multi_WidthPreserving() => _session.Edit(_widthPreservingEdit);

    [Benchmark(Description = "multi-staff edit: width-changing")]
    public string Multi_WidthChanging() => _session.Edit(_widthChangingEdit);

    [Benchmark(Description = "single-staff edit: width-preserving (systems reused)")]
    public string Single_WidthPreserving() => _singleSession.Edit(_singleWidthPreservingEdit);

    [Benchmark(Description = "single-staff edit: width-changing")]
    public string Single_WidthChanging() => _singleSession.Edit(_singleWidthChangingEdit);
}
