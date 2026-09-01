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
using LilySharp.Core.Svg;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A TAB slur takes its direction from the STRING, and the two directions are mirror
/// images — the half of LilyPond's tab slur that no number in the ledger can state on its
/// own, because a residual recorded on one side would sit there just as quietly if the
/// other side had never been drawn.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/slur.cc:47-70 calc_direction — DOWN unless some encompassed
///   non-rest column's stem points DOWN, in which case UP. On a TabStaff the stem direction
///   is the string's, not the notated pitch's, so a bass run on the LOW strings bows DOWN
///   where the notation staff above it bows UP.
/// </para>
/// <para>
/// ⚠️ WHY IT IS A TEST AND NOT A SNAPSHOT. Until 2026-09-01 Lily# drew every tab slur as an
/// arch ABOVE the digits with <c>curveUp: true</c> hard-coded
/// (<c>ElementCoordinator.BuildTabSlurLayout</c>), so bar 2 of this book is precisely the
/// picture the old code could not produce. A snapshot pins whatever was blessed; this says
/// which of the two answers is right, and the mirror assertion says the 0.525 of
/// <c>slur::move-closer-to-tab-note-heads</c> is applied with the direction's SIGN rather
/// than always downward — a bug a one-sided book would show as a plausible small residual.
/// </para>
/// <para>
/// MEASURED against LilyPond 2.26.0 (audit/lp-geometry/probes/tab-slur.ly), in the tab
/// staff's own spaces above its middle: up <c>P0 1.043326 C1 2.074551</c>, down
/// <c>P0 −1.043326 C1 −2.074551</c> — exact negatives, because bar 2 is bar 1 with its
/// strings reflected. The five ledger points <c>slur.tab.*</c> hold those numbers; this
/// holds the SHAPE of the relation between them, which survives the fret digits being
/// resized (and they ARE resized: that is the whole of those residuals).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabSlurDirectionTests
{
    /// <summary>
    /// The book of <c>test/tab-slur-pinned</c>: bar 1 on the two HIGH strings (positions
    /// 1, 3, 3, 1 — stems down, slur up), bar 2 its exact REFLECTION on the two LOW ones
    /// (−1, −3, −3, −1 — stems up, slur down). Every note names its string, so nothing here
    /// depends on the fret allocator; every note is fret 5, so nothing depends on the digit
    /// widths either, and the two bars are the same book twice about the middle.
    /// </summary>
    private const string PinnedTabSlurs = """
        octave absolute
        time 4/4
        key c major

        part bl { clef bass tuning bass }

        section A {
          bl { g,4\2( c\1 c\1 g,\2) | d,4\3( a,,\4 a,,\4 d,\3) | }
        }

        form main { A }

        score main { tab bl as numbers }
        """;

    [Fact]
    public void ATabSlurTakesItsDirectionFromTheString_AndTheTwoAreMirrorImages()
    {
        var g = RenderedGeometry.Render(PinnedTabSlurs);

        // Bar 1: high strings, so the bow is ABOVE its attachment (control point higher).
        double upP0 = g.TabBowPointAboveStaffMiddle(0, 0);
        double upC1 = g.TabBowPointAboveStaffMiddle(0, 1);
        Assert.True(upC1 > upP0,
            $"bar 1 sits on the two HIGH strings, whose tab stems point DOWN, so LilyPond's "
            + $"rule bows the slur UP — but the control point ({upC1:F6}) is not above the "
            + $"attachment ({upP0:F6}).");

        // Bar 2: low strings, so the bow is BELOW. This is the assertion the pre-2026-09-01
        // code could not pass at all.
        double downP0 = g.TabBowPointAboveStaffMiddle(1, 0);
        double downC1 = g.TabBowPointAboveStaffMiddle(1, 1);
        Assert.True(downC1 < downP0,
            $"bar 2 sits on the two LOW strings, whose tab stems point UP, so the slur bows "
            + $"DOWN — but the control point ({downC1:F6}) is not below the attachment "
            + $"({downP0:F6}). A tab slur that always bows up fails here.");

        // …and the two are exact mirrors, which is what says the tablature.scm translation
        // carries the direction's SIGN rather than always pulling downward. The two bars
        // differ only by reflecting the strings — positions (1,3,3,1) → (−1,−3,−3,−1) — and
        // LilyPond answers the exact negative (measured: 1.043326 / −1.043326). Nine places
        // rather than an epsilon, because the arithmetic on the two sides is the same
        // arithmetic, not merely a similar one.
        Assert.Equal(-downP0, upP0, 9);
        Assert.Equal(-downC1, upC1, 9);

        // The bow's RISE, which is the scorer's own output with the translation cancelled:
        // the same magnitude in both directions.
        Assert.Equal(upC1 - upP0, -(downC1 - downP0), 9);
    }

    /// <summary>
    /// A four-line staff's lines are at the ODD positions. The five-line predicate cannot be
    /// reused for a tab, and reusing it is silent: it would call every tab string a gap and
    /// every gap a string, so <c>move_away_from_staffline</c> and <c>avoid_staff_line</c>
    /// would nudge the curve off the wrong things.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-symbol.cc line_positions — with no explicit
    /// <c>line-positions</c> an N-line staff sits at <c>N−1, N−3, …, −(N−1)</c>.
    /// MEASURED on 2.26.0 (scratch/p318/tabslur-dump.ly): a four-string bass tab reports
    /// <c>line-count 4</c> and its TabNoteHeads take <c>staff-position</c> 3, 1, −1, −3.
    /// </remarks>
    [Fact]
    public void AFourLineStaffsLinesAreTheOddPositions_AndFiveLinesAgreesWithTheFiveLineRule()
    {
        foreach (int pos in new[] { -3, -1, 1, 3 })
            Assert.True(EngravingDefaults.OnStaffLine(pos, 4), $"tab line {pos} is a line");
        foreach (int pos in new[] { -5, -4, -2, 0, 2, 4, 5 })
            Assert.False(EngravingDefaults.OnStaffLine(pos, 4), $"{pos} is not a tab line");

        // A six-string tab is the EVEN positions, like the five-line staff but two wider.
        foreach (int pos in new[] { -5, -3, -1, 1, 3, 5 })
            Assert.True(EngravingDefaults.OnStaffLine(pos, 6), $"six-string line {pos}");
        Assert.False(EngravingDefaults.OnStaffLine(7, 6));

        // …and at five it is the same predicate the notation side has always used, for every
        // position either can be asked about. This is what makes threading the line count
        // through SlurScoringProblem a no-op on a notation staff.
        for (int pos = -8; pos <= 8; pos++)
            Assert.Equal(EngravingDefaults.OnStaffLine(pos), EngravingDefaults.OnStaffLine(pos, 5));
    }
}
