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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class DotSpacingTests
{
    /// <summary>
    /// AN AUGMENTATION DOT'S SPACING BOX IS ITS EXTRA-SPACING-HEIGHT, and that is what decides
    /// whether the dot reaches a neighbouring column AT ALL. A dotted note's box stands on its
    /// HEAD's row (the pure Y extent — the dot column's shift is not pure) and is grown 0.5 each
    /// way; a neighbour whose own band meets that reach takes a rod THROUGH the dot instead of
    /// the plain head-to-head one, and a neighbour outside it is untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A REGIME BOUNDARY WITH POINTS ON BOTH SIDES AND ACROSS IT (RULES §5.0): the dotted
    /// <c>d'2.</c> is fixed on staff position 2 and the other voice's eighths walk down a row at
    /// a time. The dot binds at positions 0 and −1 and is clear of the neighbour from −2 down,
    /// so the last three rows are controls in the same book — the step there is the ordinary
    /// spring, which no reading of the dot can change.
    /// </para>
    /// <para>
    /// MEASURED (2.26.0, scratch/p394/probe, these very books twinned by <c>lysc ly</c>; the
    /// column X out of rods.ly's RODCOL, which also carries the rods): the paper column's right
    /// skyline stands at 2.4774 over a band 1.61 tall — 0.45 of ink, the Dots grob's 0.5 of
    /// extra-spacing-height each way, and the PaperColumn's own 0.08 of padding each way —
    /// CENTRED ON THE HEAD's row, while RODDOTS reports the drawn dot a row higher. The rods are
    /// 2.6774 (neighbour on 0), 2.6074 (on −1, where the bands overlap by 0.01 and the distance
    /// is taken on the skyline's ramp) and 1.6042 (on −2, clear: head to head). The steps below
    /// are max(spring, rod), so the first two are the rod and the rest the 2.5042 spring.
    /// </para>
    /// <para>
    /// ⚠️ THE POISON IS THE PREVIOUS SPELLING, not a disabled line: boxing the dot 0.45 tall on
    /// the row it is DRAWN on (no extra-spacing-height) reads 2.5042 for all five rows — the two
    /// binding rows go red and the three controls stay green, which is what makes them controls.
    /// Session 392 leg 4 built exactly that box, saw pages move, and could not name the path; it
    /// read a pair from the already-saturated class, where both spellings agree, and concluded
    /// the change guarded nothing.
    /// </para>
    /// <para>
    /// ⚠️ Those are LilyPond's names for the pitches. Lily#'s absolute octave is one lower
    /// (<c>c'</c> is LilyPond's c''), so the twin spells each an apostrophe higher.
    /// </para>
    /// LILYPOND-REF: lily/separation-item.cc:163-183 Separation_item::boxes — the box is the
    ///   element's pure Y extent widened by its extra-spacing-height;
    /// LILYPOND-REF: scm/define-grobs.scm:1277 Dots extra-spacing-height (-0.5 . 0.5).
    /// </remarks>
    [Theory]
    [InlineData("b", 2.6774)]   // position  0 — the dot's band reaches it
    [InlineData("a", 2.6074)]   // position -1 — 0.01 of overlap, taken on the ramp
    [InlineData("g", 2.5042)]   // position -2 — clear of the band: the spring
    [InlineData("f", 2.5042)]   // position -3 — control
    [InlineData("e", 2.5042)]   // position -4 — control
    public void ADottedColumnReachesItsNeighbourThroughTheDotsExtraSpacingHeight(
        string lower, double lilyPond)
    {
        var src = $$"""
            octave absolute
            time 4/4
            part up
            section Main {
              up {
                voice
                { d'2. r4 | }
                { {{lower}}8 {{lower}} {{lower}} {{lower}} {{lower}} {{lower}} {{lower}} {{lower}} | }
              }
            }
            form main { ~Main }
            score main "x" { staff ~up }
            """;
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(src);
        var multi = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .CollectMultiStaff(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!);
        var layout = new LayoutEngine(new LayoutOptions()).Layout(multi);
        var bar = layout.Systems.SelectMany(s => s.Measures).Single(m => m.MeasureIndex == 0);

        // The first column pair of the bar: the dotted half shares column 0 with the first
        // eighth, and the step to the second eighth is the quantity the dot's box can reach.
        double step = bar.GetXForTiming(Fraction.Eighth) - bar.GetXForTiming(Fraction.Zero);

        Assert.Equal(lilyPond, step, 3);
    }
}
