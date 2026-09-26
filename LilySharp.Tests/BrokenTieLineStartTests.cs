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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The piece of a tie broken at a line break begins at the right edge of the line-start
/// column's staff extent — past the bar line the system opens with, when it opens with one
/// (lily/tie-formatting-problem.cc:262-270 set_minimum_height, <c>staff_extent[-dir]</c>).
/// LilyPond 2.26.0 on Lab sessions/p649 brk (no bar: 8.853400) and brk2 (`.|:`: 11.793400,
/// from the staff's left end): the repeat bar moves the piece 2.94 right. Until session 649
/// Lily# began it at the first measure's X under the bar (ABC.lys section B3).
/// </summary>
[Trait("Category", "Unit")]
public sealed class BrokenTieLineStartTests
{
    private static string Book(string form) => $$"""
        octave absolute
        clef bass
        key e major
        time 4/4
        part melody
        section A { melody { e,2 b,,2~ | break } }
        section B { melody { b,,2 e,2 | } }
        form main { {{form}} }
        score main { staff melody }
        """;

    private static double ContinuationStartX(string form)
    {
        var tree = SyntaxTree.Parse(Book(form));
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        // The piece on the second line is the bow nearest the left margin.
        return doc.Page.Beziers.Min(b => b.P0.X);
    }

    [Fact]
    public void APieceOnALineThatOpensWithARepeatBar_BeginsPastTheBar()
        => Assert.Equal(11.793400 - 8.853400,
            ContinuationStartX("~A |: ~B :|") - ContinuationStartX("~A ~B"), 6);
}
