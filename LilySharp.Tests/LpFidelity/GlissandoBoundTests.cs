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
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// A glissando's bounds are the bound HEADS' own ink edges — left the start head's
/// right edge, right the target head's left edge, or the target column's ACCIDENTAL
/// ink when it prints one (the line stops before the accidental) — each end then
/// pulled 0.5 inward ALONG the line. A chord glissando is a fan, one line per member
/// pair in written order, and both lines of a pair share their bound X's.
/// </summary>
/// <remarks>
/// The book is lilypond-src/input/regression/glissando-accidental.ly ("Glissandi stop
/// before hitting accidentals. Chord glissandi stop at the same horizontal position
/// and have the same slope"), twinned at audit/lp-regression/lys. Every literal below
/// is LilyPond 2.26.0's own drawn line from that twin (scratch/lpreg/gliss-acc-lp.svg).
/// LILYPOND-REF: scm/define-grobs.scm:1695-1702 Glissando bound-details — right attach-dir LEFT + end-on-accidental, left attach-dir RIGHT
/// LILYPOND-REF: lily/line-spanner.cc:177-202 calc_bound_info — end-on-accidental rereads x from the AccidentalPlacement extent
/// LILYPOND-REF: lily/line-spanner.cc:599 Line_spanner::print — padding is consumed along dz.direction ()
/// </remarks>
[Trait("Category", "Unit")]
public class GlissandoBoundTests
{
    private const string Src = """
        octave absolute
        time 4/4

        part v { clef treble }

        section Main {
          v {
            a1@glissando | cis'1@glissando | as1 |
            <f, a,>1@glissando | <f' a'>1@glissando |
            <fis, a,>1@glissando | <fis' a'>1@glissando |
            <fis, ais,>1@glissando | <fis' ais'>1@glissando |
            <f, ais,>1@glissando | <f' ais'>1 |
          }
        }

        form main { ~Main }

        score main { staff v }
        """;

    [Fact]
    public void GlissandoLines_AnchorAtHeadInkAndStopBeforeAccidentals()
    {
        var page = RenderedGeometry.Render(Src);
        double middle = Assert.Single(page.StaffRefpoints());

        // The staff frame's X origin: where the staff lines start (the page carries
        // a margin the LP staff-relative literals below do not).
        double staffLeft = page.Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && Math.Abs(l.X2 - l.X1) > 20)
            .Min(l => Math.Min(l.X1, l.X2));

        // The glissando lines: slanted, at staff-line thickness. Staff and ledger
        // lines are horizontal, stems vertical and thicker.
        var lines = page.Lines
            .Where(l => Math.Abs(l.StrokeWidth - 0.1) < 1e-9
                        && Math.Abs(l.Y1 - l.Y2) > 1e-6 && Math.Abs(l.X1 - l.X2) > 1e-6)
            .Select(l => l.X1 <= l.X2
                ? (X1: l.X1 - staffLeft, Y1: l.Y1, X2: l.X2 - staffLeft, Y2: l.Y2)
                : (X1: l.X2 - staffLeft, Y1: l.Y2, X2: l.X1 - staffLeft, Y2: l.Y1))
            .OrderBy(l => l.X1).ThenBy(l => l.Y1)
            .ToList();

        // 2 note glissandi + 7 chord glissandi x 2 member lines. The chord starts
        // were dropped without a word until 2026-08-07 (the detector read notes only).
        Assert.Equal(16, lines.Count);

        // a1 -> cis'1: starts at the whole head's right ink edge and STOPS BEFORE
        // the target's sharp — the right bound anchors at the accidental ink's left
        // edge — each end pulled 0.5 inward along the line. LP: (11.037, +0.4) ->
        // (14.945, -0.4) about the staff middle (Y-down device frame).
        var first = lines[0];
        Assert.Equal(11.037, first.X1, 0.01);
        Assert.Equal(middle + 0.4, first.Y1, 0.01);
        Assert.Equal(14.945, first.X2, 0.01);
        Assert.Equal(middle - 0.4, first.Y2, 0.01);

        // cis'1 -> as1: the same stop before the target's flat. LP: (19.337, -0.401)
        // -> (23.287, +0.401). The right end carries 0.012 of the FLAT's own ink-left
        // difference (accidental-placement regime, not a glissando term), hence the
        // wider tolerance on that one coordinate.
        var second = lines[1];
        Assert.Equal(19.337, second.X1, 0.01);
        Assert.Equal(middle - 0.401, second.Y1, 0.01);
        Assert.Equal(23.287, second.X2, 0.02);
        Assert.Equal(middle + 0.401, second.Y2, 0.01);

        // <f, a,> -> <f' a'>: a fan of two lines, one per member pair. LP:
        // 35.478 -> 41.004 with member Y's one staff space apart at both ends.
        var pair = lines.Where(l => Math.Abs(l.X1 - 35.478) < 0.05).ToList();
        Assert.Equal(2, pair.Count);
        Assert.Equal(41.004, pair[0].X2, 0.01);

        // Every chord pair: the two lines share their bound X's exactly (the claim's
        // "stop at the same horizontal position") and are parallel (same slope) —
        // these relations hold regardless of column spacing.
        foreach (var group in lines.Skip(2).GroupBy(l => Math.Round(l.X1, 3)))
        {
            var two = group.ToList();
            Assert.Equal(2, two.Count);
            Assert.Equal(two[0].X2, two[1].X2, 6);
            double slope0 = (two[0].Y2 - two[0].Y1) / (two[0].X2 - two[0].X1);
            double slope1 = (two[1].Y2 - two[1].Y1) / (two[1].X2 - two[1].X1);
            Assert.Equal(slope0, slope1, 3);
        }
    }
}
