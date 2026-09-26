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
/// A tie broken at a line end runs to the LEFT edge of the closing bar line, less the
/// note-head gap — not to the measure's end, which is the bar line's RIGHT edge.
/// </summary>
/// <remarks>
/// ABC.lys (Lab corpus, main score) bar 63: twelve bars on one line leave the last
/// <c>aes,,8~</c> hard against the bar line. Lily# ran the piece 0.19 (the thin bar's
/// width) further right, so the tie beside the head, (−11, dn), was 0.89 wide there and
/// kept its short-tie penalty low enough to stay; LilyPond, 0.70 wide, takes (−12, dn),
/// which clears the head and attaches under its centre.
/// <para>
/// This book is that line. LilyPond 2.26.0 on its <c>lysc ly</c> export ends the piece at
/// 110.5757, the bar line (110.7757) less note-head-gap 0.2. MEASURED before the fix:
/// 110.7657. ⚠️ THE CHOSEN POSITION IS NOT ASSERTED: on LilyPond's paper Lily# stands this
/// head 0.103 left of LilyPond's (108.2640 vs 108.3672, the spacing's own residue), which
/// makes the (−11) tie 0.81 wide and keeps it — the bound is the part that is this fix's.
/// </para>
/// LILYPOND-REF: lily/tie-formatting-problem.cc:262-270 set_column_chord_outline — the
/// broken bound's floor is <c>staff_extent[-dir]</c>; :581 widens by −note-head-gap.
/// </remarks>
[Trait("Category", "Unit")]
public class TieBrokenAtLineEndTests
{
    private const string Book = """
        octave absolute
        key aes major
        time 4/4
        part bassline {
          clef bass
          section Tacet {
            aes,,4 r2. | r1 | r | r | r | r | r | r | r | r | r | r2.. aes,,8~ | break
            aes,,4 r2. |
          }
        }
        form main { Tacet }
        score main { staff bassline }
        """;

    [Fact]
    public void ALineEndTie_StopsANoteHeadGapShortOfTheBarLinesInk()
    {
        var g = RenderedGeometry.Render(Book);

        Assert.Equal(110.5757, g.BowEndX(0), 3);
    }
}
