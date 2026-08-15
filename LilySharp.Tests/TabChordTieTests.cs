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
/// A tab chord's ties: each one hangs off ITS OWN fret digit — which, at Lily#'s digit
/// size, is in one of the zigzag's two columns — and the column's directions are
/// LilyPond's own spread, the bottom string below its digit and the ones above it above
/// theirs.
/// </summary>
/// <remarks>
/// ⚠️ THE SNAPSHOT CANNOT HOLD EITHER OF THESE DOWN. test/tab-chord-tie draws them, and
/// it can be rebased — approving a picture is not observing a rule (docs/RULES.md §5.0).
/// Both claims are relations between numbers in one drawing, so they are asserted as such.
/// <para>
/// MEASURED before the fix, on test/tab-chord-tie: all three bows came out over the
/// IDENTICAL span 10.95 … 13.45 while the digits they belong to sat at 8.42 / 10.33 / 8.42
/// and 12.70 / 14.60 / 12.70 — the middle string's bow started inside its own digit and
/// the outer two started 1.67 clear of theirs. The x came from
/// <c>ElementCoordinator.GetChordHeadXOffset</c>, which is the NOTATION chord's seconds
/// displacement: a different question with a different answer.
/// </para>
/// <para>
/// MEASURED on LilyPond 2.26.0 (the <c>lysc ly</c> twin of that fixture, dumping every
/// Tie's <c>direction</c>): the TabStaff's three ties report −1, +1, +1 at TabNoteHead
/// staff-positions 1, 3, 5. USER DECISION (2026-08-16): defer to that spread for chords.
/// LilyPond's own tab digits share ONE x (all three report X-offset 8.82, then 12.951), so
/// the column question below is Lily#'s alone — it is the zigzag that creates it.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TabChordTieTests
{
    private readonly record struct Digit(double X, double Y, int Source);
    private readonly record struct Bow(double StartX, double EndX, double StartY, double ControlY)
    {
        /// <summary>SVG Y grows DOWNWARD, so a bow whose control point is above its ends
        /// curves up.</summary>
        public bool CurveUp => ControlY < StartY;
    }

    private static double Num(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

    private static List<Digit> ReadDigits(string svg) =>
        Regex.Matches(svg,
            "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"[\\d.]+\" font-weight=\"bold\""
            + " text-anchor=\"middle\" data-pos=\"(\\d+)\">\\d+</text>")
        .Select(m => new Digit(Num(m.Groups[1]), Num(m.Groups[2]), int.Parse(m.Groups[3].Value)))
        .ToList();

    private static List<Bow> ReadBows(string svg) =>
        Regex.Matches(svg,
            "<path d=\"M ([-\\d.]+),([-\\d.]+) C ([-\\d.]+),([-\\d.]+) [-\\d.]+,[-\\d.]+ "
            + "([-\\d.]+),([-\\d.]+)")
        .Select(m => new Bow(Num(m.Groups[1]), Num(m.Groups[5]), Num(m.Groups[2]), Num(m.Groups[4])))
        .ToList();

    /// <summary>
    /// A TAB-ONLY score, so every bow in the SVG is a tab tie. The octave mode is the
    /// caller's because it decides which string a bare pitch lands on, which is the whole
    /// subject here.
    /// </summary>
    private static string Book(string music, string part, string octave) => $$"""
        {{octave}}
        time 4/4
        part m { {{part}} }
        section A { m { {{music}} } }
        form main { A }
        score main { tab m }
        """;

    private static string Guitar(string music) =>
        Book(music, "clef treble_8 tuning guitar", "octave absolute");

    /// <summary>A four-string bass, written relative — the spelling test/tab-tie uses.</summary>
    private static string Bass(string music) =>
        Book(music, "clef bass tuning bass", "");

    /// <summary>
    /// The defect this file exists for: three ties, two digit columns, and the bows must
    /// follow the columns rather than all leaving one x.
    /// </summary>
    [Fact]
    public void EachTieOfATabChord_LeavesItsOwnZigzagColumn()
    {
        string svg = LiveRender.SvgFromRenderSpec(Guitar("<c' e' g'>2~ <c' e' g'>4 r4 |"));
        var digits = ReadDigits(svg);
        var bows = ReadBows(svg);

        Assert.Equal(6, digits.Count);   // three strings, twice
        Assert.Equal(3, bows.Count);

        // The struck chord is the first source position; the held one the second.
        int firstSource = digits.Min(d => d.Source);
        var struck = digits.Where(d => d.Source == firstSource).ToList();
        var held = digits.Where(d => d.Source != firstSource).ToList();
        Assert.Equal(3, struck.Count);
        Assert.Equal(3, held.Count);

        // Three adjacent strings zigzag into exactly two columns, in each chord.
        var struckColumns = struck.Select(d => d.X).Distinct().OrderBy(x => x).ToList();
        var heldColumns = held.Select(d => d.X).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(2, struckColumns.Count);
        Assert.Equal(2, heldColumns.Count);

        // So the bows stand over exactly two spans, not one, and the two are separated by
        // the SAME distance the digit columns are — read out of the digits themselves, so
        // this says "the bows follow the zigzag" without naming the zigzag's constant.
        var starts = bows.Select(b => b.StartX).Distinct().OrderBy(x => x).ToList();
        var ends = bows.Select(b => b.EndX).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(2, starts.Count);
        Assert.Equal(2, ends.Count);
        Assert.Equal(struckColumns[1] - struckColumns[0], starts[1] - starts[0], 1);
        Assert.Equal(heldColumns[1] - heldColumns[0], ends[1] - ends[0], 1);

        // And each bow clears the digit it leaves, rather than starting on top of it: the
        // left column's bows start right of the left column, the right column's right of it.
        Assert.True(starts[0] > struckColumns[0], $"{starts[0]} must clear {struckColumns[0]}");
        Assert.True(starts[1] > struckColumns[1], $"{starts[1]} must clear {struckColumns[1]}");
        Assert.True(ends[0] < heldColumns[0], $"{ends[0]} must stop short of {heldColumns[0]}");
        Assert.True(ends[1] < heldColumns[1], $"{ends[1]} must stop short of {heldColumns[1]}");
    }

    /// <summary>
    /// USER DECISION (2026-08-16): a tab CHORD's ties spread the way LilyPond spreads them.
    /// </summary>
    [Fact]
    public void ATabChordsTies_SpreadBottomDownAndTheRestUp()
    {
        var bows = ReadBows(LiveRender.SvgFromRenderSpec(
            Guitar("<c' e' g'>2~ <c' e' g'>4 r4 |")));
        Assert.Equal(3, bows.Count);

        // Device Y grows downward, so the largest StartY is the LOWEST bow on the page —
        // the bottom string's, which is LilyPond's dir = −1.
        var bottomUp = bows.OrderByDescending(b => b.StartY).ToList();
        Assert.False(bottomUp[0].CurveUp, "the bottom string's tie hangs BELOW its digit");
        Assert.True(bottomUp[1].CurveUp, "the middle string's tie sits ABOVE its digit");
        Assert.True(bottomUp[2].CurveUp, "the top string's tie sits ABOVE its digit");
    }

    /// <summary>
    /// And a SINGLE tab tie is not touched by the column rule — it answers the same side it
    /// always did, from the string it is played on.
    /// </summary>
    /// <remarks>
    /// The two are the same rule, which is why one replaced the other: a column of one takes
    /// <c>sign(position)</c> where position is <c>StringCount+1−2·string</c>, positive
    /// exactly when the string is above the middle of the fretboard; the old rule was
    /// "opposite the tab stem", and a tab stem points up exactly when the string is BELOW
    /// that middle. On the exact middle of an odd tuning both answer UP (LilyPond through
    /// <c>neutral-direction</c>, the old rule through a strict &gt;).
    /// <para>
    /// Both notes here are on the lower half of a four-string bass, so both bows hang below
    /// — which is what LilyPond 2.26.0 answers for the twin (dir = −1 for each).
    /// </para>
    /// </remarks>
    [Fact]
    public void ASingleTabTie_TakesItsSideFromItsString()
    {
        var bows = ReadBows(LiveRender.SvgFromRenderSpec(Bass("a4\\4~ a\\4 r2 | d4\\3~ d\\3 r2 |")));
        Assert.Equal(2, bows.Count);
        Assert.All(bows, b => Assert.False(b.CurveUp));
    }

    /// <summary>
    /// And it is observed in BOTH directions, not only where it happens to answer DOWN: one
    /// book, one tuning, two notes on opposite halves of the fretboard.
    /// </summary>
    /// <remarks>
    /// MEASURED (guitar, string lines 12.35 … 19.85 at 1.5 apart): <c>g'</c> is allocated
    /// string 1 (row 12.35, the upper half) and its bow sits above its digit; <c>e</c> is
    /// allocated string 4 (row 16.85, the lower half) and its bow hangs below. A rule that
    /// answered one side always would fail exactly one of these.
    /// </remarks>
    [Fact]
    public void ASingleTabTiesSide_FollowsWhichHalfOfTheFretboardItIsOn()
    {
        var bows = ReadBows(LiveRender.SvgFromRenderSpec(Guitar("g'4~ g'4 r2 | e4~ e4 r2 |")));
        Assert.Equal(2, bows.Count);
        Assert.True(bows[0].CurveUp, "string 1 is the upper half — the bow sits above");
        Assert.False(bows[1].CurveUp, "string 4 is the lower half — the bow hangs below");
    }
}
