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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The incremental==full net for books WITH grob overrides (2026-08-26 review,
/// finding 3-2, first stage). An override used to disqualify the session from
/// every reuse wholesale — one <c>override</c> line meant the cold pipeline on
/// every keystroke. Whole-VECTOR reuse (springs, whole-layout) is now allowed
/// when the override/revert collections compare value-equal between edits, on
/// the totality argument: all inputs unchanged ⇒ the same layout. Per-SYSTEM
/// reuse stays override-free-only (a per-measure key cannot localize a global
/// spacing change), which this net does not touch.
/// </summary>
public class OverrideEditIncrementalTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Book(string overrideLine)
    {
        var bars = string.Join(" |\n    ", Enumerable.Repeat("c'4 d'4 e'4 f'4", 30));
        return overrideLine + "part m { clef treble }\nsection S {\n  m {\n    " + bars
            + " |\n  }\n}\nform main { S }\nscore main \"ov\" { staff m }\n";
    }

    [Fact]
    public void StandingOverride_ContentUnchangedEdit_ReusesTheWholeLayout()
    {
        var text = Book("override NoteHead.color = red\n");
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(text), Opt);
        compiler.Render();

        // A trivia keystroke under a STANDING override: content keys equal, override
        // collections value-equal — the whole-layout reuse this stage recovers.
        var edited = text.Insert(text.IndexOf("e'4 f'4", System.StringComparison.Ordinal), " ");
        var incremental = compiler.RenderIncremental(SyntaxTree.Parse(edited));
        var full = SvgGenerator.Generate(SyntaxTree.Parse(edited), Opt);
        Assert.True(full == incremental, "trivia edit under an override: incremental != full");
        Assert.True(compiler.LastEditReusedLayout,
            "a content-unchanged edit under a standing override did not reuse the layout — "
            + "the recovery this net pins is dead");
    }

    [Fact]
    public void OverrideValueEdit_DeclinesReuse_AndMatchesFull()
    {
        var red = Book("override NoteHead.color = red\n");
        var blue = Book("override NoteHead.color = blue\n");
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(red), Opt);
        compiler.Render();

        // Changing the override VALUE must decline every whole-vector reuse — the
        // collections compare unequal — and still render exactly like a full
        // recompile, both ways.
        foreach (var (text, label) in new[] { (blue, "red→blue"), (red, "blue→red") })
        {
            var incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            var full = SvgGenerator.Generate(SyntaxTree.Parse(text), Opt);
            Assert.True(full == incremental, $"{label}: incremental != full");
            Assert.False(compiler.LastEditReusedLayout,
                $"{label}: the layout was reused across an override change");
        }
    }

    [Fact]
    public void StandingOverride_ContentEdit_MatchesFull()
    {
        var text = Book("override NoteHead.color = red\n");
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(text), Opt);
        compiler.Render();

        // A pitch edit under a standing override: content moved, so no whole-layout
        // reuse — but the per-measure spring memo may now serve unchanged measures
        // (overridesUnchanged holds), and the result must equal a full recompile.
        int at = text.LastIndexOf("e'4 f'4", System.StringComparison.Ordinal);
        var edited = text.Remove(at + 4, 3).Insert(at + 4, "g'4");
        Assert.NotEqual(text, edited);
        var incremental = compiler.RenderIncremental(SyntaxTree.Parse(edited));
        var full = SvgGenerator.Generate(SyntaxTree.Parse(edited), Opt);
        Assert.True(full == incremental, "content edit under an override: incremental != full");
        Assert.False(compiler.LastEditReusedLayout);
    }
}
