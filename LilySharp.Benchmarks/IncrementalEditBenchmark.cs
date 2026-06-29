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
}
