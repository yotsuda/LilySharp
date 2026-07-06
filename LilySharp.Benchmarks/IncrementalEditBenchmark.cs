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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Benchmarks;

/// <summary>
/// F0 — edit-latency baseline for the incremental-compilation work (F3).
///
/// The proposal's thesis: today the parser is incremental but everything
/// downstream (collect → layout → render) re-runs fully on every edit, so a
/// one-note edit on a large score costs almost the same as a cold compile.
/// These benchmarks pin that number down BEFORE any F3 stage lands, so a later
/// natural-width early-cutoff (S4) or query-DAG (S5) can be proven, not claimed.
///
/// Compare:
///   Cold_ParseAndRender          — full compile from source text
///   Edit_IncrementalReparse_*    — pre-parsed tree, WithChange(1 edit) + full render
///   Edit_ColdReparse_*           — Parse(editedText) + full render
/// The near-equality of the three today IS the motivation: incremental parsing
/// saves little because render dominates. When F3 lands, only Edit_* should drop.
///
/// Source is the largest test fixture (showcase/grammar-tour); the dead
/// samples/music pieces the old RenderPipelineBenchmark points at no longer
/// exist, so this walks up to LilySharp.Tests/Fixtures like the snapshot tests.
///
/// Run: dotnet run -c Release -- --filter '*IncrementalEdit*'
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class IncrementalEditBenchmark
{
    private const string Fixture = "showcase/grammar-tour";

    private string _source = null!;
    private string _editedSource = null!;
    private SyntaxTree _tree = null!;
    private TextChange _edit;

    // Pre-computed stage inputs for the edited tree, so each stage can be timed
    // in isolation and we see WHERE one edit's time goes (collect vs layout vs
    // render) — the case for S5's per-system memoization.
    private SyntaxTree _editedTree = null!;
    private RenderSpec? _editedSpec;
    private MultiStaffScore _editedScore = null!;
    private ScoreLayout _editedLayout = null!;

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
        _source = File.ReadAllText(path).Replace("\r\n", "\n");
        _tree = SyntaxTree.Parse(_source);

        // A representative single edit: insert a note token near the middle of
        // the document (insertion = zero-length span), the shape of a user
        // typing one note. Robust to file contents.
        int at = _source.Length / 2;
        _edit = new TextChange(new TextSpan(at, 0), " c4");
        _editedSource = _source.Insert(at, " c4");

        // Stage inputs: the edited tree carried through the exact render path
        // (CollectScore → Layout → RenderToSvg, as IncrementalCompiler does).
        _editedTree = _tree.WithChange(_edit);
        _editedSpec = RenderSpecParser.FindFirst(_editedTree);
        _editedScore = SvgGenerator.CollectScore(_editedTree, _editedSpec);
        _editedLayout = new LayoutEngine().Layout(_editedScore);
    }

    [Benchmark(Baseline = true, Description = "Cold: parse + render")]
    public string Cold_ParseAndRender()
    {
        var tree = SyntaxTree.Parse(_source);
        return SvgGenerator.Generate(tree, Options);
    }

    [Benchmark(Description = "Edit: incremental reparse + full render")]
    public string Edit_IncrementalReparse_FullRender()
    {
        var tree = _tree.WithChange(_edit);
        return SvgGenerator.Generate(tree, Options);
    }

    [Benchmark(Description = "Edit: cold reparse + full render")]
    public string Edit_ColdReparse_FullRender()
    {
        var tree = SyntaxTree.Parse(_editedSource);
        return SvgGenerator.Generate(tree, Options);
    }

    // === Per-stage breakdown of ONE edit ===
    // These isolate where the edit cost lives. The render path is
    // reparse → collect → layout → render; comparing these four pins which stage
    // S5 must memoize (the proposal's claim is layout+render dominate).

    [Benchmark(Description = "Edit stage 0: incremental reparse only")]
    public SyntaxTree Edit_Stage0_Reparse() => _tree.WithChange(_edit);

    [Benchmark(Description = "Edit stage 1: collect (semantics)")]
    public MultiStaffScore Edit_Stage1_Collect() =>
        SvgGenerator.CollectScore(_editedTree, _editedSpec);

    [Benchmark(Description = "Edit stage 2: layout")]
    public object Edit_Stage2_Layout() =>
        new LayoutEngine().Layout(_editedScore);

    [Benchmark(Description = "Edit stage 3: render SVG")]
    public string Edit_Stage3_RenderSvg() =>
        SvgGenerator.RenderToSvg(_editedScore, _editedLayout, Options);
}

