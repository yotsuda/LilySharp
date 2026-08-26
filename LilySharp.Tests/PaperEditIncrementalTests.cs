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
/// A <c>paper { }</c> edit through the incremental session must render byte-identical
/// to a full recompile — the incremental==full invariant for the SECOND edit whose
/// changed input lives outside every reuse key (the first is <c>fonts { }</c>,
/// FontEditIncrementalTests).
/// </summary>
/// <remarks>
/// ⚠️ WHY THIS NET EXISTS (2026-08-26): the page dimensions are an input to line
/// breaking, justification and paging, and they were in NONE of the session's reuse
/// keys — not the per-measure content key, not the global tuple, and not the break
/// gate, which caches prefix widths and the spring vector, neither of which reads the
/// page width. MEASURED before the guard: a <c>paperWidth 150mm</c> edit on a warm
/// 30-bar session fired BOTH the gate skip and whole-layout reuse and rendered
/// 2,376,499 bytes against the full recompile's 2,372,674 — the old width's layout
/// served as a hit, and the preview showed A4 line breaks on 150 mm paper until the
/// next content edit. The guard is in <c>IncrementalCompiler.Compile</c> (the paper
/// joins the font plan in the session state that resets every cache); this net is
/// what proves it holds.
/// </remarks>
public class PaperEditIncrementalTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Book(string paper)
    {
        var bars = string.Join(" |\n    ", Enumerable.Repeat("c'4 d'4 e'4 f'4", 30));
        return paper + "part m { clef treble }\nsection S {\n  m {\n    " + bars
            + " |\n  }\n}\nform main { S }\nscore main \"pp\" { staff m }\n";
    }

    [Fact]
    public void PaperEdit_RendersIdenticalToFullRecompile_BothWays()
    {
        var a4 = Book("");
        var narrow = Book("paper {\n  paperWidth 150mm\n}\n");

        var compiler = new IncrementalCompiler(SyntaxTree.Parse(a4), Opt);
        compiler.Render();

        // Warm → narrow paper → back to A4: each step must equal a full recompile
        // of its own text (the second leg also proves the guard triggers again on
        // the way back rather than serving the narrow layout).
        foreach (var (text, label) in new[] { (narrow, "to 150mm"), (a4, "back to A4") })
        {
            var incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            var full = SvgGenerator.Generate(SyntaxTree.Parse(text), Opt);
            Assert.True(full == incremental,
                $"{label}: incremental != full (reusedLayout={compiler.LastEditReusedLayout}, "
                + $"skippedBreak={compiler.LastEditSkippedLineBreak})");
            Assert.False(compiler.LastEditReusedLayout,
                $"{label}: the whole cached layout was reused across a paper change");
            Assert.False(compiler.LastEditSkippedLineBreak,
                $"{label}: the break DP was skipped across a paper change");
        }

        // And the guard must not overfire: a keystroke that leaves the SAME paper
        // block in place keeps its reuse (two collects of one block must compare
        // equal by value, not by instance).
        var compiler2 = new IncrementalCompiler(SyntaxTree.Parse(narrow), Opt);
        compiler2.Render();
        var edited = narrow.Replace("e'4 f'4 |\n  }", "e'4 g'4 |\n  }");
        Assert.NotEqual(narrow, edited);
        var inc2 = compiler2.RenderIncremental(SyntaxTree.Parse(edited));
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(edited), Opt), inc2);
        var inc3 = compiler2.RenderIncremental(SyntaxTree.Parse(narrow));
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(narrow), Opt), inc3);
        Assert.True(compiler2.LastEditSkippedLineBreak,
            "an unchanged paper block shed the caches — the guard compares by instance, not value?");
    }
}
