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
        // `octave absolute` + <eis eis' cis cis'> = E♯4(-4) E♯5(3) C♯4(-6) C♯5(1) — the
        // spelling `test/accidental-octave-straddle` carries, read off
        // `lysc check --pitches` rather than counted by hand.
        //
        // ⚠️ This line said <eis' eis'' cis' cis''> until 2026-08-16, which is a DIFFERENT
        // chord: E♯5(3) E♯6(10) C♯5(1) C♯6(8), every position non-negative. The positions
        // below are the ones that matter — they STRADDLE ZERO, which is the whole point,
        // since ((p % 7) + 7) % 7 and a bare `p % 7` agree everywhere else. The fixture had
        // been written from this comment and inherited the mistake: swapping in a bare `%`
        // left that book's SVG byte-identical while this test went red. A model test and a
        // corpus book that name the same case have to name the same notes, or one of them
        // is measuring nothing.
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

    /// <summary>
    /// The corpus book above names the same case, and has to keep naming it: the positions
    /// must land on BOTH SIDES of the middle line, because that is the only arrangement in
    /// which a floored modulo and a truncating one disagree.
    /// </summary>
    /// <remarks>
    /// ⚠️ Stated in PITCHES rather than positions, because a pitch is what the book writes
    /// and what <c>lysc check --pitches</c> reports back — the two things a reader edits and
    /// reads. E♯4 and C♯4 are below the treble middle line, E♯5 and C♯5 above it.
    /// Until 2026-08-16 the book wrote the octave above (E♯5/E♯6, C♯5/C♯6), all on one side,
    /// and swapping the modulo left its SVG byte-identical: it had never observed the rule
    /// its own header named. A snapshot cannot say this — it is rebased whenever the book
    /// changes, so it agrees with whatever the book last became.
    /// </remarks>
    [Fact]
    public void TheStraddleFixture_StillStraddlesTheMiddleLine()
    {
        var path = System.IO.Path.Combine(CollectResumeTests.FindRepoRoot(),
            "LilySharp.Tests", "Fixtures", "test", "accidental-octave-straddle.lys");
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(System.IO.File.ReadAllText(path));
        var trace = LilySharp.Core.Semantics.ResolvedPitches.ForFile(tree);

        Assert.NotNull(trace);
        Assert.Equal(
            new[] { "C#4", "C#5", "E#4", "E#5" },
            trace!.Select(e => e.Pitch).OrderBy(p => p, System.StringComparer.Ordinal).ToArray());
    }
}
