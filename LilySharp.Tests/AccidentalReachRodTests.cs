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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An accidental's column raises a rod past its neighbour, to the note before it that its ink
/// still reaches — LilyPond's set_column_rods walks back, not only to the adjacent column.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, LilySharp-Lab sessions/p580/reach3, one bar squeezed to its rods):
/// in `f,16 ges,, a,16` under five flats the f's column carries a rod of 2.750200 to the a,
/// whose natural clears the low g-flat between them (1.604200 to it) but meets the f.
/// ⚠️ OPEN RESIDUAL −0.0117 (Lily# 2.738472), the same sloped-shoulder residual as
/// AccidentalSpacingPaddingTests.
/// </remarks>
public class AccidentalReachRodTests
{
    [Fact]
    public void ANaturalClearOfItsNeighbour_IsRoddedToTheNoteItReaches()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            key des major
            time 4/4
            part bassline { clef bass }
            section S { bassline { f,16 ges,, a,16 c,16 r4 r2 | } }
            form main { S }
            score main { staff bassline }
            """);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var measures = MultiStaffLayouter.CollectAllMeasuresAtIndex(multi, 0);
        var timings = MultiStaffLayouter.CollectAllTimingsForMeasure(multi, 0);
        var rods = new List<(int Left, int Right, double Distance)>();

        MeasureLayouter.AddAccidentalReachRods(multi.TextMetrics, measures, timings,
            MultiStaffLayouter.CollectStavesOfMeasuresAtIndex(multi, 0), rods);

        // Column 0 (the f) to column 2 (the a): springs 1 and 2.
        var fToA = Assert.Single(rods, r => r.Left == 1 && r.Right == 3);
        Assert.InRange(fToA.Distance, 2.750200 - 0.015, 2.750200 + 0.015);
    }
}
