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
/// A low tie's curve top pays the "line center" penalty for a LEDGER line's position, not only
/// for the five staff lines (HANDOFF R9(c), session 572).
/// </summary>
/// <remarks>
/// <c>score_configuration</c>'s line-center term asks <c>Staff_symbol_referencer::on_line</c>,
/// whose <c>allow_ledger</c> defaults to true, so on a five-line staff every even position
/// below the staff is a line. Lily# asked the five-line predicate (|pos| ≤ 4), so a down tie
/// under the staff never paid for a top on a ledger's position. It decides a SHORT tie: here
/// the four bars share one line, the pos −4 tie is 1.180300 wide, and the base
/// (−5, −0.25) — whose top lands near −6 — loses to (−6, 0.00).
/// The numbers are LilyPond 2.26.0's, measured on
/// audit/lp-geometry/probes/tie-ledger-line-center.ly (this book exported by <c>lysc ly</c>):
/// <code>
/// PROBE TIE loc=(1 . 3/8) pos=-4 dir=-1 cps=((0.852100 . -3.0) ... (2.032400 . -3.0))
///   card="-6 (0.00) d: vdist=2.73 rhdist=1.79 TOTAL=4.51"
/// </code>
/// MEASURED: before the fix Lily# drew all four tips at −2.75 (the base), at the same width.
/// LILYPOND-REF: lily/tie-formatting-problem.cc:762-774 score_configuration;
/// LILYPOND-REF: lily/staff-symbol.cc:372-396 Staff_symbol::on_line.
/// </remarks>
[Trait("Category", "Unit")]
public class TieLedgerLineCenterTests
{
    private const string Bar = "g,,8 g,,16 g,, r8 g,,8~ g,, a, a,16 a, a, a, |";

    private static string Book() => $$"""
        time 4/4
        octave absolute
        key d major
        part bassline
        section Main { bassline { clef bass {{Bar}} noBreak {{Bar}} noBreak {{Bar}} noBreak {{Bar}} } }
        form main { Main }
        score main "x" { staff bassline }
        """;

    [Fact]
    public void AShortTieUnderTheStaff_AvoidsATopOnALedgerPosition()
    {
        var g = RenderedGeometry.Render(Book());

        for (int bow = 0; bow < 4; bow++)
        {
            Assert.Equal(1.180300, g.BowSpan(bow), 6);
            Assert.Equal(-3.000000, g.BowAttachmentAboveStaffMiddle(bow), 6);
        }
    }
}
