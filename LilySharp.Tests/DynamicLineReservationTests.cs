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

using System;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The staves are spaced off a dynamic where its hairpin's line puts it, not where it would
/// stand alone (evaluation 5, piano r-07). The right hand's decrescendo, pushed down by its beam,
/// takes its closing pp down with it; until 2026-11-22 the room reserved the pp at its own
/// higher place, and the drawn pp stood in the left hand's fermata (gap 5.63 — LilyPond opens it
/// to about 8.1).
/// </summary>
[Trait("Category", "Unit")]
public class DynamicLineReservationTests
{
    private static string Book(string rhMarks) =>
        "octave absolute\npart rh { instrument piano-right }\npart lh { instrument piano-left }\n" +
        "section A {\n  rh { g'8" + rhMarks + " c'' e'' g'' bes'' g'' e'' c'' | e''2.@pp@fermata r4 | }\n" +
        "  lh { <c, g,>1 | <c,, c,>2.@fermata r4 | }\n}\nform { ~A }\n" +
        "score { grandStaff { staff rh  staff lh } }\n";

    private static double Gap(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        var system = layout.AllSystems.First();
        return Math.Abs(LayoutUtilities.FindStaffYInSystem(system, 0) - LayoutUtilities.FindStaffYInSystem(system, 1))
            - 4.0;
    }

    [Fact]
    public void ThePpOnTheHairpinsLine_OpensTheGapToTheLeftHand()
    {
        double alone = Gap(Book("@mf"));           // no hairpin: the pp stands on its own
        double onTheLine = Gap(Book("@mf@decresc")); // the pp rides the beam-lowered line
        Assert.True(onTheLine > alone + 1.0, $"on the line {onTheLine:0.00}, alone {alone:0.00}");
    }
}
