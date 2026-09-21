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

using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The boxes of a tie's bound column that are NOT that tie's own note heads — the
/// augmentation dots, the untied members of the chord, and the accidentals.
/// </summary>
/// <remarks>
/// <para>
/// <c>set_column_chord_outline</c> walks EVERY grob of a bound column into the skyline the
/// tie reads its attachment off, and four of those boxes belong to something other than the
/// tied heads: the DOTS (:123-139, left bound only), the FLAG (:181-190, left bound and a
/// normal stem only), the chord's UNTIED heads (:210-224), and the ACCIDENTALS (:226-236,
/// right bound only). Lily# builds all four
/// (<see cref="Core.Svg.Layout.ElementCoordinator"/>'s BuildTieColumn) and reads all four
/// (<see cref="Core.Svg.Layout.TieChordOutline"/>.Build).
/// </para>
/// <para>
/// ⚠️ THESE EXIST BECAUSE NOTHING OBSERVED ANY OF THEM. Session 451 emptied each of the four
/// lists in turn and all 8,774 tests stayed green — the suite knew the tied heads and the
/// stem and nothing else about the column. The pure-unit
/// <see cref="TieChordOutlineTests"/> asserts the outline at a Y of its own choosing from a
/// hand-built <c>TieColumnParts</c>, which cannot see whether the COLLECTOR ever puts a dot,
/// a neighbour head or an accidental into one; these three books carry that half, one book
/// per box, each chosen so the tie actually meets it.
/// </para>
/// <para>
/// ⚠️ THE FLAG HAS NO BOOK, AND THAT IS A FINDING RATHER THAN A GAP. Lily# builds the flag
/// box for a SINGLE NOTE only, and a single note's tie is always on the far side of the head
/// from its own flag, so the box can never be in the way. Measured (session 452): emptying
/// the flag list moves 0 of 41 probe books, while making the same box enormous moves 19 — so
/// it IS built and IS read, and only its real geometry keeps it silent. LilyPond's own flag
/// box speaks on a flagged CHORD whose stem tip is near the tied top head, which is the case
/// Lily# excludes. docs/HANDOFF.md carries the ticket.
/// </para>
/// <para>
/// The numbers are LilyPond 2.26.0's own, measured on
/// audit/lp-geometry/probes/tie-outline-boxes.ly, which is these three books exported by
/// <c>lysc ly</c>. <c>w</c> is <c>control-points[3].x - control-points[0].x</c> and
/// <c>y</c> is <c>control-points[0].y</c> — the same two quantities the tie.width.* and
/// tie.y.* ledger points read, and the ones <see cref="RenderedGeometry"/> reports as
/// <c>BowSpan</c> and <c>BowAttachmentAboveStaffMiddle</c>.
/// </para>
/// <code>
/// PROBE TVDOT WIDTH pos=-6 dir=-1 w=4.006155 y=-4.000000
/// PROBE TVDOT WIDTH pos=-4 dir=-1 w=1.801955 y=-2.750000
/// PROBE TVDOT WIDTH pos=-2 dir=1  w=3.219055 y=0.225000
/// PROBE TVACC WIDTH pos=-6 dir=-1 w=3.875445 y=-3.750000
/// PROBE TVACC WIDTH pos=-2 dir=1  w=1.124045 y=-0.671696
/// PROBE TVOTH WIDTH pos=-6 dir=-1 w=5.187845 y=-4.500000
/// </code>
/// LILYPOND-REF: lily/tie-formatting-problem.cc:96-287 set_column_chord_outline,
///   :72-87 get_attachment.
/// </remarks>
[Trait("Category", "Unit")]
public class TieOutlineBoxTests
{
    /// <summary>The one-staff, one-system book every tie width probe here is written in.</summary>
    private static string Book(string music, string name) => $$"""
        octave absolute
        time 4/4
        key c major

        part melody { clef treble }

        section Main {
          melody { {{music}} }
        }

        form main { ~Main }

        score main "{{name}}" {
          staff melody
        }
        """;

    /// <summary>
    /// THE AUGMENTATION DOTS ARE IN THE OUTLINE: the middle tie of a dotted triad leaves the
    /// DOT COLUMN's right edge, not the head's, and so comes out 0.765 narrower than it would
    /// if the column held only heads.
    /// </summary>
    /// <remarks>
    /// The triad is what makes the dots legible. On a single dotted note the tie is thrown
    /// clear of its own head and never crosses the dot row; between two members of a chord it
    /// has nowhere else to go, and the dot of the head below is exactly where it runs.
    /// MEASURED (session 452): dropping the dots leaves pos −4 at w=2.566955 — a full dot
    /// column too wide — and moves the outer two ties' chosen POSITIONS as well, because the
    /// scorer reads the same outline at every candidate Y.
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:123-139 set_column_chord_outline.
    /// </remarks>
    [Fact]
    public void TheDotsAreInTheOutline_AndTheMiddleTieOfADottedTriadLeavesThem()
    {
        var g = RenderedGeometry.Render(Book("<c e g>4.~ <c e g>8 <c e g>2 |", "TVDOT"));

        // Bows are indexed bottom to top within a chord: positions -6, -4, -2.
        Assert.Equal(4.006155, g.BowSpan(0), 6);
        Assert.Equal(1.801955, g.BowSpan(1), 6);
        Assert.Equal(3.219055, g.BowSpan(2), 6);

        Assert.Equal(-4.000000, g.BowAttachmentAboveStaffMiddle(0), 6);
        Assert.Equal(-2.750000, g.BowAttachmentAboveStaffMiddle(1), 6);
        Assert.Equal(0.225000, g.BowAttachmentAboveStaffMiddle(2), 6);
    }

    /// <summary>
    /// THE ACCIDENTALS ARE IN THE OUTLINE: a tie arriving at a chord that carries an UNTIED
    /// flatted member stops short of that flat, and the upper tie comes out 1.928 narrower
    /// than the same tie arriving at a chord without one.
    /// </summary>
    /// <remarks>
    /// The accidentals enter the RIGHT bound only, and this is why: they stand between the
    /// arriving tie and the head it is arriving at, and no other bound can meet them. The
    /// <c>aes</c> is deliberately an UNTIED member — a book whose accidental sits on a tied
    /// head would also depend on Lily# matching ties by staff position rather than by pitch
    /// (Svg/Collector/TieDetector.cs), which is a separate question.
    /// MEASURED (session 452): dropping the accidentals leaves pos −2 at w=3.051745.
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:226-236 set_column_chord_outline.
    /// </remarks>
    [Fact]
    public void TheAccidentalsAreInTheOutline_AndAnArrivingTieStopsShortOfThem()
    {
        var g = RenderedGeometry.Render(Book("<c g>2~ <c g aes>2 |", "TVACC"));

        Assert.Equal(3.875445, g.BowSpan(0), 6);
        Assert.Equal(1.124045, g.BowSpan(1), 6);

        Assert.Equal(-3.750000, g.BowAttachmentAboveStaffMiddle(0), 6);
        Assert.Equal(-0.671696, g.BowAttachmentAboveStaffMiddle(1), 6);
    }

    /// <summary>
    /// THE CHORD'S UNTIED HEADS ARE IN THE OUTLINE: a tie arriving at a chord whose other
    /// member is NOT tied has to clear that member, and it pays for it in the POSITION it
    /// settles on — half a staff space lower — while its width does not move at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE WIDTH CANNOT SEE THIS ONE. The untied <c>b,</c> stands a step below the tied
    /// <c>c</c>, so it is not at the tie's own Y and does not change what the outline reads
    /// there; what it changes is the SCORE of every candidate the search walks, and that
    /// shows in <c>control-points[0].y</c> alone. A book asserted on width would be green
    /// with the head box thrown away — which is the shape session 451's poison came back in.
    /// MEASURED (session 452): dropping the untied heads leaves the tie at y=-3.750000.
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:210-224 set_column_chord_outline;
    ///   :915-1001 generate_configuration / find_best_variation for the search that reads it.
    /// </remarks>
    [Fact]
    public void TheUntiedChordMembersAreInTheOutline_AndTheArrivingTieClearsThem()
    {
        var g = RenderedGeometry.Render(Book("c2~ <b, c>2 |", "TVOTH"));

        Assert.Equal(5.187845, g.BowSpan(0), 6);
        Assert.Equal(-4.500000, g.BowAttachmentAboveStaffMiddle(0), 6);
    }
}
