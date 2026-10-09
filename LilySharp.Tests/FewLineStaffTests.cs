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
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A notation staff of fewer than five lines (session 871): its profile is its LINES, not the
/// five-line band (MultiStaffLayouter.LineSpanOf), and its bar lines are LilyPond's widened
/// extent (MultiStaffLayouter.BarHalfExtentOf) — drawn so, and seeded so (SkylineBuilder.SeedBarlines).
/// The spacing numbers are LilyPond 2.26.0's, measured on these books' twins (VerticalAxisGroup
/// offsets): until session 871 Lily# stood the staves 11.095 apart in both.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FewLineStaffTests
{
    private static IReadOnlyList<BoxSymbol> Symbols(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return BoxesGenerator.GenerateDocument(tree, RenderSpecParser.FindFirst(tree))
            .Pages.SelectMany(p => p.Symbols).ToList();
    }

    private static double[] LineCentres(IReadOnlyList<BoxSymbol> symbols)
        => symbols.Where(s => s.Kind == "staffLine").Select(s => Math.Round((s.Box[1] + s.Box[3]) / 2, 3))
            .Distinct().OrderBy(y => y).ToArray();

    private static string Book(int lines) =>
        "time 4/4\npart rh { clef percussion }\npart m { clef treble }\n"
        + "section A { rh { c1 | c1 | } m { c'''1 | c1 | } }\n"
        + $"score {{ staff rh as lines {lines}  staff m }}\n";

    [Theory]
    [InlineData(1, 9.590)]    // LilyPond: VerticalAxisGroup -1.0 and -10.590
    [InlineData(3, 10.095)]   // LilyPond: -1.05 and -11.145
    public void AStaffOfFewerLines_IsSpacedByItsLines_AsLilyPondSpacesIt(int lines, double lilyPond)
    {
        var y = LineCentres(Symbols(Book(lines)));
        double upperMiddle = y[(lines - 1) / 2];
        double lowerMiddle = y[lines + 2];   // the five-line staff's middle line
        Assert.Equal(lilyPond, lowerMiddle - upperMiddle, 3);
    }

    /// <remarks>
    /// The extents are about the staff's MIDDLE (staff position 0) in Lily#'s own line frame
    /// (EngravingDefaults.StaffLinePositions — two lines at ±2, four at −2…+4, LILYSHARP-OWN),
    /// so <paramref name="topPosition"/> names the top line's position to find the middle from.
    /// </remarks>
    [Theory]
    [InlineData(1, 0, -1.0, 1.0)]
    [InlineData(2, 2, -1.0, 1.0)]
    [InlineData(3, 2, -1.0, 1.0)]
    [InlineData(4, 4, -1.0, 2.0)]
    [InlineData(5, 4, -2.0, 2.0)]
    public void TheBarLine_IsLilyPondsWidenedExtent(int lines, int topPosition, double low, double high)
    {
        // scm/output-lib.scm staff-symbol::calc-widened-extent: under two spaces long, the
        // lines' extent widens by a space each way; scm/bar-line.scm takes half a line off.
        var symbols = Symbols("time 4/4\npart rh { clef percussion }\nsection A { rh { c1 | c1 | } }\n"
            + $"score {{ staff rh as lines {lines} }}\n");
        double middle = LineCentres(symbols)[0] + topPosition * 0.5;   // device y grows DOWN
        var bar = symbols.First(s => s.Kind == "barLine");
        Assert.Equal(middle - high, bar.Box[1], 3);
        Assert.Equal(middle - low, bar.Box[3], 3);
    }
}
