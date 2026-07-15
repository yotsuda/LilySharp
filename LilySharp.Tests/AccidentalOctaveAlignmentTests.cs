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

using System.Linq;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LilyPond groups accidentals of the same note name (an "APE") into ONE column, whatever the
/// octave — C♯4 and C♯5 sit in the same column, E♯4 and E♯5 in the next. Lily# used to place
/// each independently, and a spurious skyline self-collision shoved the upper C♯ an extra column
/// left (three columns instead of two). These lock the aligned two-column result.
/// LILYPOND-REF: lily/accidental-placement.cc set_ape_skylines.
/// </summary>
[Trait("Category", "Unit")]
public class AccidentalOctaveAlignmentTests
{
    private static double X(System.Collections.Immutable.ImmutableArray<AccidentalLayout> ls, int pos)
        => ls.First(l => l.StaffPosition == pos).XOffset;

    [Fact]
    public void SameNoteNameSharpsAnOctaveApart_ShareOneColumn()
    {
        // <eis' eis'' cis' cis''> = E♯4(-4) E♯5(3) C♯4(-6) C♯5(1).
        var notes = new[]
        {
            new ChordNoteInfo(-4, "sharp", true),
            new ChordNoteInfo(3, "sharp", true),
            new ChordNoteInfo(-6, "sharp", true),
            new ChordNoteInfo(1, "sharp", true),
        };
        var ls = new AccidentalPlacement().CalculatePositions(notes);

        // Each note-name pair aligns to a single column…
        Assert.Equal(X(ls, -4), X(ls, 3), 3);   // E♯4 == E♯5
        Assert.Equal(X(ls, -6), X(ls, 1), 3);   // C♯4 == C♯5
        // …and there are exactly TWO columns, C♯ one accidental-width left of E♯ (not three).
        Assert.True(X(ls, -6) < X(ls, -4), "C♯ column must sit left of the E♯ column");
        Assert.Equal(2, ls.Select(l => System.Math.Round(l.XOffset, 3)).Distinct().Count());
    }
}
