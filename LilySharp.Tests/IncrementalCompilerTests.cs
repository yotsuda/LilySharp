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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// F3 engine slice S4b: the incremental compiler must produce SVG byte-identical
/// to a full recompile on every edit (the S1 incremental==full invariant, now
/// with a real cutoff), AND it must actually skip the line-break DP when — and
/// only when — the edit leaves the line-break gate unchanged.
/// </summary>
[Trait("Category", "Visual")]
public class IncrementalCompilerTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private const string Base = """
        time 4/4
        key c major
        part melody { clef treble }
        phrase p { c4 d e f | g4 a b c | d4 e f g | a4 b c d | }
        section Main { melody { $p } }
        structure { Main }
        score "x" { staff melody }
        """;

    private static string Full(string text) =>
        SvgGenerator.Generate(SyntaxTree.Parse(text), Opt).Replace("\r\n", "\n");

    private static string Norm(string svg) => svg.Replace("\r\n", "\n");

    private static TextChange Replace(string text, string find, string replacement)
    {
        int at = text.IndexOf(find, System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"snippet not found: {find}");
        return new TextChange(new TextSpan(at, find.Length), replacement);
    }

    [Fact]
    public void FirstRender_EqualsFullGenerate()
    {
        var session = new IncrementalCompiler(SyntaxTree.Parse(Base), Opt);
        Assert.Equal(Full(Base), Norm(session.Render()));
    }

    [Fact]
    public void WidthPreservingEdit_SkipsLineBreak_AndMatchesFull()
    {
        var tree = SyntaxTree.Parse(Base);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render(); // warm the cache

        // Adding an articulation collects into a separate list, not the measure
        // items, so the gate is unchanged -> the break DP is skipped.
        var change = Replace(Base, "c4 d e f", "c4@staccato d e f");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditSkippedLineBreak);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void WidthChangingEdit_RecomputesBreaks_AndMatchesFull()
    {
        var tree = SyntaxTree.Parse(Base);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        // Re-rhythming a bar changes its natural width -> the gate changes -> the
        // breaks are recomputed. Output still matches a full recompile.
        var change = Replace(Base, "c4 d e f", "c2 d4 e4");
        var incremental = Norm(session.Edit(change));

        Assert.False(session.LastEditSkippedLineBreak);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void ChainedEdits_AlwaysMatchFull_WithExpectedSkips()
    {
        var session = new IncrementalCompiler(SyntaxTree.Parse(Base), Opt);
        session.Render();

        // (find, replace, expectSkip) — alternating gate-preserving (skip) and
        // gate-changing (recompute, incl. a structural measure insertion) edits.
        var steps = new (string Find, string Replace, bool ExpectSkip)[]
        {
            ("c4 d e f", "c4@staccato d e f", true),   // +articulation -> skip
            ("g4 a b c", "g2 a4 b4", false),           // re-rhythm        -> recompute
            ("d4 e f g", "d4@accent e f g", true),     // +articulation    -> skip
            ("a4 b c d", "a4 b c d | r1", false),      // insert a measure -> recompute
        };

        foreach (var (find, replace, expectSkip) in steps)
        {
            string current = session.Tree.Text;
            var change = Replace(current, find, replace);
            int at = current.IndexOf(find, System.StringComparison.Ordinal);
            string editedText = current[..at] + replace + current[(at + find.Length)..];

            var incremental = Norm(session.Edit(change));
            Assert.Equal(Full(editedText), incremental);
            Assert.Equal(expectSkip, session.LastEditSkippedLineBreak);
        }
    }
}
