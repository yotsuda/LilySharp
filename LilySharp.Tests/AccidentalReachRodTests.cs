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
using LilySharp.Core.Semantics;
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

        MeasureLayouter.AddColumnReachRods(multi.TextMetrics, measures, timings,
            MultiStaffLayouter.CollectStavesOfMeasuresAtIndex(multi, 0), rods);

        // Column 0 (the f) to column 2 (the a): springs 1 and 2.
        var fToA = Assert.Single(rods, r => r.Left == 1 && r.Right == 3);
        Assert.InRange(fToA.Distance, 2.750200 - 0.015, 2.750200 + 0.015);
    }

    /// <summary>
    /// An accidental is held off ANOTHER voice of its staff two columns back — the column
    /// between belongs to a different staff, so no adjacent pair ever compared the two.
    /// </summary>
    /// <remarks>
    /// 03-piano-nocturne (Lab probe, 2026-09-26): voice 1's fis8 on 3/8 and voice 2's
    /// <c>&lt;ais d&gt;2</c> on 1/2, with the left hand's sixteenths putting a column at 7/16
    /// that the right hand does not share. The flag stood on the ♯; LilyPond, whose separation
    /// skyline is the staff's, keeps them apart. Counter-case: the same bar with no left hand,
    /// where the two columns ARE adjacent and the cross-voice pass prices them — no reach rod.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAccidental_IsRoddedToAnotherVoiceOfItsStaff_PastAColumnItDoesNotShare(bool leftHand)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            key e major
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section S {
              rh { voice { gis''4. fis''8 e''4 dis'' } { <b' e''>2 <ais' d''>2 } | }
              lh { {{(leftHand ? "e,16 b, e b e,16 b, e b e,16 b, e b e,16 b, e b" : "e,1")}} | }
            }
            form main { S }
            score main { grandStaff { staff rh  staff lh } }
            """);
        Assert.False(tree.HasErrors);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var measures = MultiStaffLayouter.CollectAllMeasuresAtIndex(multi, 0);
        var timings = MultiStaffLayouter.CollectAllTimingsForMeasure(multi, 0);
        var rods = new List<(int Left, int Right, double Distance)>();

        MeasureLayouter.AddColumnReachRods(multi.TextMetrics, measures, timings,
            MultiStaffLayouter.CollectStavesOfMeasuresAtIndex(multi, 0), rods);

        int fis = timings.IndexOf(new Fraction(3, 8)), chord = timings.IndexOf(new Fraction(1, 2));
        var reach = rods.Where(r => r.Left == fis + 1 && r.Right == chord + 1).ToList();
        if (leftHand)
        {
            Assert.Equal(fis + 2, chord);                 // a left-hand column stands between
            Assert.True(reach.Count > 0 && reach.Max(r => r.Distance) > 1.0,
                "the fis8 must hold the ♯ off across the left hand's column");
        }
        else
        {
            Assert.Equal(fis + 1, chord);                 // adjacent: the cross-voice pass's pair
            Assert.Empty(reach);
        }
    }

    /// <summary>
    /// Every column is walked, not only an accidental's: two plain heads of one staff with
    /// another staff's column between them carry the rod LilyPond's set_column_rods raises
    /// over any pair whose ink can reach. The owner's piano-sonatina (Lab corpora/dogfood/big,
    /// 2026-10-03): <c>tuplet 3/2 { e'8 f' g' }</c> over Alberti eighths — the f' on 1/12 and
    /// the g' on 1/6 with the left hand's 1/8 between them — drew the g' 1.10 after the f' on
    /// an eight-bar line, the heads touching; LilyPond 2.26.0 holds 2.17 there (its line is
    /// less crammed) and never less than the rod. Counter-case: the same bar with no left
    /// hand, where the two columns ARE adjacent and the pair pass prices them.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APlainHead_IsRoddedToItsOwnVoicesNote_PastAColumnItDoesNotShare(bool leftHand)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section S {
              rh { tuplet 3/2 { e'8 f' g' } c'4~ c'2 | }
              lh { {{(leftHand ? "c8 g e g c g e g" : "c1")}} | }
            }
            form main { S }
            score main { grandStaff { staff rh  staff lh } }
            """);
        Assert.False(tree.HasErrors);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var measures = MultiStaffLayouter.CollectAllMeasuresAtIndex(multi, 0);
        var timings = MultiStaffLayouter.CollectAllTimingsForMeasure(multi, 0);
        var rods = new List<(int Left, int Right, double Distance)>();

        MeasureLayouter.AddColumnReachRods(multi.TextMetrics, measures, timings,
            MultiStaffLayouter.CollectStavesOfMeasuresAtIndex(multi, 0), rods);

        // Three eighths in the time of two: the f' on 1/12, the g' on 1/6, the left hand's
        // 1/8 between them. By value, so a tuplet onset's spelling cannot miss.
        int f = timings.FindIndex(t => System.Math.Abs(t.ToDouble() - 1.0 / 12) < 1e-9);
        int g = timings.FindIndex(t => System.Math.Abs(t.ToDouble() - 1.0 / 6) < 1e-9);
        Assert.True(f >= 0 && g >= 0, string.Join(" ", timings.Select(t => t.ToString())));
        var reach = rods.Where(r => r.Left == f + 1 && r.Right == g + 1).ToList();
        if (leftHand)
        {
            Assert.Equal(f + 2, g);                       // the left hand's 1/4 stands between
            // The rod: the spanner's 0.1 over the f' column's right skyline against the g''s
            // left — the head and its up-stem, 1.5 — the same 1.604200 LilyPond's dump carries
            // for two eighths (SpacingRules.SeparationRodDistance's measurement).
            Assert.InRange(Assert.Single(reach).Distance, 1.55, 1.65);
        }
        else
        {
            Assert.Equal(f + 1, g);                       // adjacent: the pair pass's rod
            Assert.Empty(reach);
        }
    }
}
