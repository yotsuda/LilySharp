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
/// Laissez-vibrer and repeat ties are placed by the tie scorer, as LilyPond places them
/// (HANDOFF R9(f), session 573).
/// </summary>
/// <remarks>
/// LilyPond 2.26.0 on audit/lp-geometry/probes/semi-tie-scoring.ly (these books exported by
/// <c>lysc ly</c>), each LaissezVibrerTie / RepeatTie's control-points dumped: [0].y is
/// <see cref="RenderedGeometry.BowAttachmentAboveStaffMiddle"/>, [3].x − [0].x
/// <see cref="RenderedGeometry.BowSpan"/>, [1].y − [0].y
/// <see cref="RenderedGeometry.BowControlLift"/>. Before the port Lily# stood every one of these
/// 0.4 off the head centre with a width of 1.1 and its own bow shape.
/// LILYPOND-REF: lily/semi-tie-column.cc:51-86 calc_positioning_done;
/// LILYPOND-REF: lily/tie-formatting-problem.cc:386-442 from_semi_ties.
/// </remarks>
[Trait("Category", "Unit")]
public class SemiTieScoringTests
{
    private static string Book(string music) => $$"""
        octave absolute
        time 4/4
        part melody { clef treble }
        section A { melody { {{music}} } }
        form main { ~A }
        score main { staff melody }
        """;

    [Fact]
    public void LaissezVibrerTies_AreScoredLikeTies()
    {
        var g = RenderedGeometry.Render(Book(
            "c'2@laissezVibrer r2 | d'2@laissezVibrer r2 | f'2@laissezVibrer r2 | c'2.@laissezVibrer r4 |"));

        // Clears its head: position 2 + 0.20, attached at the head's CENTRE.
        Assert.Equal(1.200000, g.BowAttachmentAboveStaffMiddle(0), 6);
        Assert.Equal(1.788700, g.BowSpan(0), 6);
        Assert.Equal(0.478835, g.BowControlLift(0), 6);
        // A short tie in a space, centred vertically: 3 (-0.16).
        Assert.Equal(1.341174, g.BowAttachmentAboveStaffMiddle(1), 6);
        Assert.Equal(1.100000, g.BowSpan(1), 6);
        Assert.Equal(0.332393, g.BowControlLift(1), 6);
        // On a line: no head-edge hug (the open side has no head extent), 5 (0.00).
        Assert.Equal(2.500000, g.BowAttachmentAboveStaffMiddle(2), 6);
        // The dot is in the host outline, and so in the open end's reach.
        Assert.Equal(2.688700, g.BowSpan(3), 6);
        Assert.Equal(0.606508, g.BowControlLift(3), 6);
    }

    [Fact]
    public void RepeatTies_ReadTheAccidentalsInTheirHostOutline()
    {
        var g = RenderedGeometry.Render(Book(
            "fis''2@repeatTie r2 | bes'2@repeatTie r2 | <c'' ees''>2@repeatTie r2 |"));

        Assert.Equal(6.000000, g.BowAttachmentAboveStaffMiddle(0), 6);
        Assert.Equal(1.100000, g.BowSpan(0), 6);
        Assert.Equal(4.000000, g.BowAttachmentAboveStaffMiddle(1), 6);
        // The chord's lower tie: down, and long — it leaves from the flat's left.
        Assert.Equal(3.500000, g.BowAttachmentAboveStaffMiddle(2), 6);
        Assert.Equal(2.370000, g.BowSpan(2), 6);
        Assert.Equal(-0.567872, g.BowControlLift(2), 6);
    }
}
