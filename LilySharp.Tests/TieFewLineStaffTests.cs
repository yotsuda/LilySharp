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

using LilySharp.Core.Svg;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie is scored against the lines ITS staff draws — LilyPond's one
/// <c>staff_symbol_referencer_</c> — and not against five lines whatever the staff has.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p872/probe/slashtie, the books' <c>lysc ly</c> twins
/// under <c>-dbackend=svg</c>, the tie's path read against the staff's middle line):
/// <list type="bullet">
/// <item>five lines, <c>b4 b4~ b4 b4</c>: attachment 0.2604 above the line, 1.2980 wide;</item>
/// <item>five lines, <c>/4 /4~ /4 /4</c>: 0.2469 above, 1.2980 wide — the slash is a wider head
///   (1.7152), so the tie leaves it 0.41 later than a plain head's, and the bar is that much
///   wider (Lily# left it at the plain head's edge, inside the slash's ink, until this fix);</item>
/// <item>ONE line, <c>b4 b4~ b4 b4</c>: 0.7500 above, 2.6022 wide;</item>
/// <item>ONE line, <c>/4 /4~ /4 /4</c>: 0.7500 above, 3.0132 wide.</item>
/// </list>
/// On the one-line staff the widened staff span is EMPTY, so position 0 is not "within the
/// staff" (tie-formatting-problem.cc:522-528); the search lands on position 1 instead, the
/// head-edge hug lifts it to 0.5 + outer-tie-vertical-gap = 0.75 (:495-505), and at that height
/// the attachment sits above the head boxes, on the recession box at each head's CENTRE
/// (:243-258) — hence the width. Before 2026-10-10 Lily# drew both one-line ties as it draws the
/// five-line ones (0.26 above, head edge to head edge), its scorer having <c>|pos| ≤ 3</c> and
/// <c>pos % 2</c> written in where LilyPond asks the staff. The site's chord chart
/// (site/sketch-in-c.lys, a slash tie across a bar line on a one-line staff) is where it showed:
/// the chords row above it stood 1.60 from the staff where LilyPond's stands 1.672.
/// LILYPOND-REF: lily/tie-formatting-problem.cc:495-543 generate_configuration (the head-edge
///   hug, within_staff), lily/tie-formatting-problem.cc:762-792 score_configuration;
///   lily/staff-symbol-referencer.cc:27-38 on_line / on_staff_line, :180-187 staff_span;
///   lily/staff-symbol.cc:372-396 on_line, :122-319 ledger_positions, :399-418 line_span.
/// </remarks>
[Trait("Category", "Unit")]
public class TieFewLineStaffTests
{
    private static string Book(string music, string staff) => $$"""
        octave absolute
        time 4/4
        part comp { }
        section A { comp { {{music}} | } }
        form { A }
        score { {{staff}} }
        """;

    [Theory]
    [InlineData("b4 b4~ b4 b4", "staff comp", 2, 0.2604, 1.2980)]
    [InlineData("/4 /4~ /4 /4", "staff comp", 2, 0.2469, 1.2980)]
    [InlineData("b4 b4~ b4 b4", "staff comp as lines 1", 0, 0.7500, 2.6022)]
    [InlineData("/4 /4~ /4 /4", "staff comp as lines 1", 0, 0.7500, 3.0132)]
    public void ATieIsScoredAgainstItsOwnStaffsLines(string music, string staff, int middleLine, double lift, double span)
    {
        var g = RenderedGeometry.RenderProduct(Book(music, staff));
        // The lift is read from the staff's MIDDLE line (position 0): the third of five, the
        // one of one.
        Assert.Equal(lift, g.BowAttachmentAboveStaffLine(0, middleLine), 3);
        // The span rides on the two heads' distance, which the two engines space within 0.01.
        Assert.InRange(g.BowSpan(0), span - 0.02, span + 0.02);
    }

    /// <summary>
    /// <c>Staff_symbol::on_line</c> with ledgers, on Lily#'s own line tables: a drawn line, or
    /// every other position of the nearest line's parity out to the head — which, the tables
    /// being all even, is "the position is even" on every one of them, the gap between the
    /// timbales pair included (LilyPond's walk puts a ledger there too).
    /// </summary>
    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, false)]
    [InlineData(2, 1, true)]
    [InlineData(-4, 1, true)]
    [InlineData(0, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(3, 3, false)]
    [InlineData(4, 3, true)]
    [InlineData(5, 5, false)]
    [InlineData(6, 5, true)]
    [InlineData(-6, 5, true)]
    public void OnLineOrLedger_WalksTheStaffsOwnLedgers(int pos, int lines, bool expected)
        => Assert.Equal(expected, EngravingDefaults.OnLineOrLedger(pos, lines));
}
