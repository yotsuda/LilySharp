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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The measure-boundary break-align group (LilyPond's NonMusicalPaperColumn).
/// These pin the ORDER and the intra-column placement, which is the whole point of
/// the type: before it existed the same column was rebuilt ad hoc per call site.
/// </summary>
[Trait("Category", "Unit")]
public class BoundaryColumnTests
{
    private static ClefChangeItem Clef() => new(ClefType.Bass, 0);
    private static KeySignatureChangeItem Key() => new(new KeySignature(1), new KeySignature(0), 0);
    private static TimeSignatureChangeItem Time() => new(new TimeSignature(2, 4), 0);

    private static BoundaryColumnGrob Grob(BoundaryColumn c, BreakAlignSymbol s)
        => Assert.Single(c.Grobs, g => g.Symbol == s);

    [Fact]
    public void ClefChange_SitsBeforeTheBarLine()
    {
        // scm/define-grobs.scm:650-664 break-align-orders, UNBROKEN row:
        //   … clef, cue-clef, staff-bar, key-cancellation, key-signature, time-signature …
        // so a clef change at a mid-line bar is engraved BEFORE the bar line — unlike a
        // key or time change. Verified on LilyPond 2.24.4: with `\clef bass R1*5` the
        // boundary column's origin is the clef's left edge and the bar line sits 2.84668
        // inside it, while the bar line's ABSOLUTE position does not move at all.
        var col = BoundaryColumn.Build(BarlineType.Single, new MusicItem[] { Clef() });

        Assert.Equal(BreakAlignSymbol.Clef, col.Grobs[0].Symbol);
        Assert.Equal(BreakAlignSymbol.StaffBar, col.Grobs[1].Symbol);

        // Clef.space-alist (staff-bar . (extra-space . 0.7)) — measured 0.700 on 2.24.4.
        double clefWidth = SpacingRules.GetClefChangeWidth(ClefType.Bass);
        Assert.Equal(clefWidth + 0.7, Assert.NotNull(col.BarLineLeft), 6);
    }

    [Fact]
    public void WithoutAClefChange_TheBarLineIsTheColumnOrigin()
    {
        var col = BoundaryColumn.Build(BarlineType.Single, new MusicItem[] { Key() });
        Assert.Equal(BreakAlignSymbol.StaffBar, col.Grobs[0].Symbol);
        Assert.Equal(0.0, Assert.NotNull(col.BarLineLeft), 6);
    }

    [Fact]
    public void KeyAndTimeSitAfterTheBarLine_AtTheirSpaceAlistGaps()
    {
        var col = BoundaryColumn.Build(
            BarlineType.Single, new MusicItem[] { Key(), Time() });

        double bw = EngravingDefaults.BarlineDrawnWidth(BarlineType.Single);
        var key = Grob(col, BreakAlignSymbol.KeySignature);
        var time = Grob(col, BreakAlignSymbol.TimeSignature);

        // BarLine.space-alist (key-signature . (extra-space . 1.0)) — define-grobs.scm:296
        Assert.Equal(bw + 1.0, key.Left, 6);
        // KeySignature.space-alist (time-signature . (extra-space . 1.15)) — :2012 area
        Assert.Equal(key.Right + 1.15, time.Left, 6);
    }

    [Fact]
    public void TimeAloneUsesTheBarLineToTimeGap_NotTheGenericDefault()
    {
        // BarLine.space-alist (time-signature . (extra-space . 0.75)) —
        // define-grobs.scm:293. BreakAlignSpacing had no explicit staff-bar -> time
        // entry and fell through to a 1.0 default, which is a different number.
        var col = BoundaryColumn.Build(BarlineType.Single, new MusicItem[] { Time() });
        double bw = EngravingDefaults.BarlineDrawnWidth(BarlineType.Single);
        Assert.Equal(bw + 0.75, Grob(col, BreakAlignSymbol.TimeSignature).Left, 6);
    }

    [Fact]
    public void ABarLineThatDrawsNothing_IsSkipped_LikeAnEmptyBreakAlignGroup()
    {
        // break-alignment-interface.cc:145-146 and :155-156 walk PAST break-align groups
        // whose extent is empty, and find_nonempty_break_align_group (:96-107) returns
        // null for them — so a bar line that draws nothing must neither consume a
        // space-alist gap nor anchor the grob after it. Placing it anyway put the key
        // signature a full staff-bar->key 1.0 further right than LilyPond does.
        var col = BoundaryColumn.Build(BarlineType.None, new MusicItem[] { Key() });

        Assert.DoesNotContain(col.Grobs, g => g.Symbol == BreakAlignSymbol.StaffBar);
        Assert.Null(col.BarLineLeft);
        Assert.Equal(0.0, Grob(col, BreakAlignSymbol.KeySignature).Left, 6);
    }

    [Fact]
    public void RightSkylineFromTheBarLine_IsUnaffectedByAClefChange()
    {
        // The clef is excluded from the bar-line-framed skyline not by a special case
        // but by WHERE IT SITS. This is the fact that keeps the multi-measure-rest rod
        // correct: measured on LilyPond 2.24.4, bar line to bar line across `R1*5` is
        // 14.133856 both with and without a leading clef — only the column origin moves.
        var withClef = BoundaryColumn.Build(
            BarlineType.Single, new MusicItem[] { Clef() }).RightSkylineFromBarLine();
        var without = BoundaryColumn.Build(
            BarlineType.Single, System.Array.Empty<MusicItem>()).RightSkylineFromBarLine();

        var probe = HorizontalSkyline.FromBox(
            -2.0, 2.0, xLeft: -0.1, xRight: 0.1, HorizontalDirection.Left);
        Assert.Equal(without.Distance(probe), withClef.Distance(probe), 6);
    }

    [Fact]
    public void BoundaryClefAllowance_ChargesTheClefToThePrecedingMeasure()
    {
        // A clef change opening a measure is drawn BEFORE the shared bar line, so the
        // room it needs belongs to the PREVIOUS measure's closing gap, not to the spring
        // after the bar line. The amount is the boundary column's own geometry: the
        // clef's width plus Clef.space-alist (staff-bar . (extra-space . 0.7)).
        var opensWithClef = MeasureOf(Clef(), Rest());
        var opensWithKey = MeasureOf(Key(), Rest());

        Assert.Equal(
            SpacingRules.GetClefChangeWidth(ClefType.Bass) + 0.7,
            SpacingRules.BoundaryClefAllowance(BarlineType.Single, opensWithClef), 6);

        // A key change rides the spring AFTER the bar line, so it costs nothing here.
        Assert.Equal(0.0,
            SpacingRules.BoundaryClefAllowance(BarlineType.Single, opensWithKey), 6);
        Assert.Equal(0.0,
            SpacingRules.BoundaryClefAllowance(BarlineType.Single, null), 6);
    }

    private static RestItem Rest() => new(new Fraction(1, 1), dots: 0, sourcePosition: 0);

    private static Measure MeasureOf(params MusicItem[] items)
        => new(System.Collections.Immutable.ImmutableArray.Create(items),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    [Fact]
    public void ScanStopsAtTheMusic_SoALaterChangeIsNotOnThisColumn()
    {
        // Only the LEADING zero-duration changes ride the boundary column; a change
        // after the first sounding item belongs to a later column.
        var rest = new RestItem(new Fraction(1, 1), dots: 0, sourcePosition: 0);
        var col = BoundaryColumn.Build(
            BarlineType.Single, new MusicItem[] { rest, Key() });

        Assert.DoesNotContain(col.Grobs, g => g.Symbol == BreakAlignSymbol.KeySignature);
    }

    /// <summary>
    /// <see cref="BoundaryColumn.OpensWithClefChange"/> is a SHORT-CUT past building the
    /// column, so the only thing that keeps it from being a second model of the boundary is
    /// that the two agree — everywhere, not just on the shape the short-cut was written for.
    /// This holds them together.
    /// </summary>
    /// <remarks>
    /// The equation is "the short-cut is false exactly where the column's own answer is 0",
    /// asserted across every leading shape this column can see and every bar line — including
    /// <see cref="BarlineType.None"/>, where the column answers null rather than 0 and the
    /// callers' <c>?? 0</c> is what makes the two meet.
    /// ⚠️ The clef rows are what make this net non-vacuous: without one it would pass for a
    /// short-cut that always said false.
    /// </remarks>
    [Fact]
    public void TheClefLessShortCut_AgreesWithTheColumnItBuildsPast()
    {
        MusicItem[][] shapes =
        {
            System.Array.Empty<MusicItem>(),
            new MusicItem[] { Key() },
            new MusicItem[] { Time() },
            new MusicItem[] { Key(), Time() },
            new MusicItem[] { Rest() },
            new MusicItem[] { Rest(), Clef() },          // after the music — a later column
            new MusicItem[] { Clef() },
            new MusicItem[] { Clef(), Key() },
            new MusicItem[] { Key(), Clef() },           // key first, clef still on this column
            new MusicItem[] { Clef(), Key(), Time(), Rest() },
        };
        var barlines = new[]
        {
            BarlineType.Single, BarlineType.None, BarlineType.Double, BarlineType.Final,
        };

        int nonZero = 0;
        foreach (var shape in shapes)
        {
            var items = System.Collections.Immutable.ImmutableArray.Create(shape);
            bool shortCut = BoundaryColumn.OpensWithClefChange(items);
            foreach (var barline in barlines)
            {
                double built = BoundaryColumn.Build(barline, shape).BarLineLeft ?? 0;
                if (built != 0) nonZero++;

                // ⑴ The claim the caller makes: what it answers IS the column's answer.
                Assert.Equal(built,
                    SpacingRules.BoundaryClefAllowance(barline, MeasureOf(shape)), 12);

                // ⑵ ...and the short-cut only ever skips a column that answers 0. It is
                // allowed to be conservative the other way (a clef against a bar line that
                // draws nothing builds the column and still gets 0), which is why this is
                // one implication and not an equivalence.
                if (!shortCut)
                    Assert.Equal(0.0, built, 12);
            }
        }
        Assert.True(nonZero > 0, "no shape reserved anything — the net is vacuous");
    }
}
