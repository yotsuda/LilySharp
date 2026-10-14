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

using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie on a head in a SPACE outside the staff (g'' over the treble staff, d''' past its
/// second ledger) is as long as one on a ledger head: LilyPond keeps it at the heads' top edge,
/// beside them, because <c>on_line</c> calls the position past the head a line — but no ledger is
/// drawn there, so there is nothing to clear, and Lily# hugs it over the heads as it does a
/// ledger head's (TieFormattingProblem.IsUndrawnLedger; the owner's report, session 905,
/// leger-tie.lys). Inside the staff nothing changes.
/// </summary>
[Trait("Category", "Unit")]
public class LedgerSpaceTieTests
{
    private static double TieWidth(string note)
    {
        var svg = LiveRender.SvgFromRenderSpec($$"""
            octave absolute
            part m { clef treble }
            section S { m { {{note}}1~ | {{note}} | } }
            form { ~S }
            score { staff m }
            """);
        var tie = Regex.Match(svg, "<path d=\"M ([-\\d.]+),[-\\d.]+ C [^\"]* ([-\\d.]+),[-\\d.]+ C");
        Assert.True(tie.Success, "no tie drawn");
        return double.Parse(tie.Groups[2].Value, CultureInfo.InvariantCulture)
               - double.Parse(tie.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("g'")]    // the space over the top line
    [InlineData("b'")]    // the space over the first ledger
    [InlineData("d''")]   // the space over the second ledger
    [InlineData("d")]     // the space under the bottom line (a down tie)
    [InlineData("g,")]    // the space under the second ledger below
    public void ATieInASpaceOutsideTheStaff_IsAsLongAsALedgerHeadsTie(string note)
    {
        double ledger = TieWidth("c''");   // on the second ledger: hugged over the heads
        Assert.InRange(TieWidth(note), ledger - 0.05, ledger + 0.05);
    }
}
