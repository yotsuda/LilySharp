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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The order <see cref="MusicMarkEngraver"/> stacks one anchor's ABOVE-staff marks in:
/// outside-staff priority first, and — for the marks that share one — the order the score
/// wrote them.
/// </summary>
/// <remarks>
/// ⚠️ THE TIE-BREAK HAD NO OBSERVER, which is why this file exists. Measured by poison on
/// 2026-09-20 (session 436) against the whole 8,774-test suite: making the sort UNSTABLE (one
/// character: <c>&gt;</c> → <c>&gt;=</c> in the insertion sort's shift test) turned <b>nothing
/// at all</b> red, while it moved 32 rendered pages of the owner's corpus — four books
/// (<c>The Hustle</c>, <c>Top of the World</c>, <c>おもかげ</c>, <c>キミ、メグル、ボク</c>),
/// eight keystrokes each. A book earns that by writing two marks of one priority on one
/// anchor, which <c>The Hustle</c> does in its very first measure.
/// <para>
/// The tie is the COMMON case rather than a corner: <see cref="MusicMarkEngraver"/> names five
/// types in its priority table and every other type answers 1500, so any two of those on one
/// anchor are a tie. The <c>OrderBy</c> that used to stand here was stable and the sort that
/// replaced it (session 436, to stop allocating two LINQ chains a group) had to be too —
/// the contract survived only in the choice of algorithm until this file.
/// </para>
/// <para>
/// Both tests are DIFFERENTIAL (HANDOFF §7.7): each renders the same marks TWICE and changes
/// exactly one thing — the order they are handed over — so they say what decides the stack
/// without pinning a geometry the LilyPond ledger owns.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class MusicMarkAnchorOrderTests
{
    private static ImmutableArray<MeasureLayout> Measures(int count, double width = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
            builder.Add(new MeasureLayout(i, i * width, width,
                ImmutableArray.Create(new ItemLayout(0, 1.0, 2.0))));
        return builder.ToImmutable();
    }

    private static ImmutableArray<SystemLayout> OneStaffSystem()
    {
        var staff = new StaffLayout(0, ClefType.Treble, 0, 4.0);
        var group = StaffGroupLayout.CreateSingle(staff, 0, 4.0);
        return ImmutableArray.Create(new SystemLayout(
            0, 10.0, 200.0, 5.0, Measures(2), ImmutableArray.Create(group)));
    }

    /// <summary>Y-up of the mark carrying <paramref name="text"/>, with the marks handed to the
    /// engraver in the order given.</summary>
    private static double YOf(string text, params MusicMarkItem[] marks)
    {
        var systems = OneStaffSystem();
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var result = MusicMarkEngraver.Calculate(
            ScoreTextMetrics.Bundled, null, ImmutableArray.Create(marks), systems, ml);
        return result.Single(l => l.Text == text).YUp;
    }

    private static MusicMarkItem Rehearsal(string text, int sourcePosition)
        => new(MusicMarkType.Rehearsal, text, 0, sourcePosition);

    /// <summary>
    /// Two marks of EQUAL priority on one anchor stack in the order the score wrote them: the
    /// first stands on the staff's own base and the second above it. Handing the same two over
    /// in the other order must give the other picture — an unstable sort gives one picture for
    /// both, and a book that writes two <c>mark()</c>s in one bar prints them swapped.
    /// </summary>
    [Fact]
    public void TwoMarksOfEqualPriority_StackInTheOrderTheScoreWroteThem()
    {
        var first = Rehearsal("A", 10);
        var second = Rehearsal("B", 20);

        double aWhenFirst = YOf("A", first, second);
        double bWhenSecond = YOf("B", first, second);
        double bWhenFirst = YOf("B", second, first);
        double aWhenSecond = YOf("A", second, first);

        Assert.True(aWhenFirst < bWhenSecond,
            $"written first, A ({aWhenFirst:F3}) must sit NEARER the staff than B "
            + $"({bWhenSecond:F3})");
        Assert.True(bWhenFirst < aWhenSecond,
            $"written first, B ({bWhenFirst:F3}) must sit NEARER the staff than A "
            + $"({aWhenSecond:F3}) — equal priorities are broken by the source order, so "
            + "swapping the two must swap the stack");
        // ⚠️ NOT asserted: that the two pictures are mirror images. The inner mark stands on
        // its OWN half-extent, and "A" and "B" are not the same width, so the two runs differ
        // by a fraction of a space (4.223 against 4.203 as this was written). The contract is
        // WHICH mark is inner, not how far up the other one lands.
    }

    /// <summary>
    /// A DIFFERENCE in priority outranks the source order: a section label (1450) stands nearer
    /// the staff than a rehearsal mark (1500) whichever way round they are written. The pair to
    /// the test above — together they say the sort orders by priority and ONLY breaks ties by
    /// arrival.
    /// </summary>
    [Fact]
    public void ADifferenceInPriority_OutranksTheOrderTheScoreWroteThem()
    {
        var label = new MusicMarkItem(MusicMarkType.SectionLabel, "Verse", 0, 10);
        var rehearsal = Rehearsal("A", 20);

        double labelFirst = YOf("Verse", label, rehearsal);
        double rehearsalAfter = YOf("A", label, rehearsal);
        double rehearsalFirst = YOf("A", rehearsal, label);
        double labelAfter = YOf("Verse", rehearsal, label);

        Assert.True(labelFirst < rehearsalAfter,
            $"the section label ({labelFirst:F3}) is the inner mark (1450 < 1500), so the "
            + $"rehearsal mark ({rehearsalAfter:F3}) stacks outside it");
        Assert.True(labelAfter < rehearsalFirst,
            $"and it stays the inner mark when the score writes it SECOND: label "
            + $"({labelAfter:F3}), rehearsal ({rehearsalFirst:F3})");
    }
}
