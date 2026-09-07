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

using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The tuplet bracket that FOLLOWS an UP-STEM, SLOPED beam sits where LilyPond puts it — in
/// absolute position, not only in slope.
/// </summary>
/// <remarks>
/// <para>
/// The ledger pair <c>staff.staff.tuplet-bracket-follow-beam{,-rest}</c> measures this arm
/// with DOWN stems, where the stem stands 0.065 from its column anchor, and
/// <c>TupletBracketRestBoundTests</c> pins a down-stem book's dy only. An UP stem stands
/// 1.2392 from its anchor, so a beam-face read in the wrong frame is nineteen times larger
/// here — and until 2026-09-07 it was (BeamLayout.OuterEdgeStaffSpaceAtX interpolated in the
/// column-anchor frame, and the follow arm "corrected" its tips by slope × attach). The
/// sweep of 920 books moved 48 when the frame was put right, most of them tuplet numbers
/// riding sloped up-stem beams by 0.1–0.3 ss; these two books are the LilyPond oracle for
/// that regime (scratch/p346/up3-probe.ly, upr-probe.ly — `lysc ly` twins with a grob dump).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TupletBracketFollowsBeamTests
{
    // A manual beam over five eighths, all below the middle line (stems UP), descending to
    // the a, so the beam SLOPES; the tuplet is its middle three, so the beam is longer than
    // the tuplet and the bracket is DRAWN.
    private const string NoteBound = """
        octave absolute
        part melody {
          section A { c8[ tuplet 3/2 { e8 d c } a,8] c4 r4 | }
        }
        form main { A }
        score main { staff melody }
        """;

    // The same arm with the tuplet's FIRST column a REST the beam runs over.
    private const string RestBound = """
        octave absolute
        part melody {
          section A { e8[ tuplet 3/2 { r8 d c ] } c8 c4 r4 | }
        }
        form main { A }
        score main { staff melody }
        """;

    /// <summary>
    /// LP 2.26.0 (scratch/p346/up3-probe.ly): Beam <c>positions=(1.0 . 0.81)</c>, bracket
    /// <c>positions=(2.2794417238237257 . 2.191726958403722)</c>, <c>beam=SET</c>.
    /// </summary>
    [Fact]
    public void AnUpStemSlopedBeam_PutsTheBracketWhereLilyPondDoes_LpExact()
    {
        var (line, _) = TupletBracketRestBoundTests.Bracket(NoteBound);
        Assert.Equal(2.279441724, line[0], precision: 6);
        Assert.Equal(2.191726958, line[1], precision: 6);
    }

    /// <summary>
    /// LP 2.26.0 (scratch/p346/upr-probe.ly): Beam <c>positions=(0.81 . 0.19)</c>, bracket
    /// <c>positions=(1.9896883028340395 . 1.5699421598295629)</c>, <c>beam=SET</c>,
    /// <c>X-positions=(0.0 . 6.0084)</c> from the rest's ink left; the rest's invisible stem
    /// reports its extent as the single point 0.854758 = the beam's upper face at the rest's
    /// ink centre.
    /// </summary>
    /// <remarks>
    /// Two seams met on this book and they were closed one at a time, each with its number.
    /// With the beam face in the stems' frame the dy was LilyPond's to six digits but the
    /// bracket sat 0.040574 LOW at both ends: the bracket's own X frame at a REST bound.
    /// Lily# opened the bracket one up-stem attach (1.1742 ≈ 1.2392 − 0.065) to the right of
    /// where LilyPond does — LilyPond's <c>X-positions</c> start at the rest's ink left
    /// (MEASURED: relX 11.791155 = the Rest's and its NoteColumn's extent left; the beam's
    /// left is 9.7592), because <c>get_x_bound_item</c> hands back the COLUMN when the bound's
    /// stem is invisible — so the encompass points' x were read against a shorter,
    /// right-shifted span and the offset pass landed dy × 0.581 / 6.0084 lower. That was the
    /// disclosed clause ⑸ of TupletBracketEngraver's port; BoundEdgeOffset now reads the
    /// rest's ink edge, and this asserts LilyPond's positions outright.
    /// </remarks>
    [Fact]
    public void AnUpStemSlopedBeamOverABoundingRest_SlopesTheBracketWithIt_LpExact()
    {
        var (line, _) = TupletBracketRestBoundTests.Bracket(RestBound);
        Assert.Equal(1.989688303, line[0], precision: 6);
        Assert.Equal(1.569942160, line[1], precision: 6);
    }
}
