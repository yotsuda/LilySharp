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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What the room seeds into a staff's silhouette on a system that is not the first — the
/// two defects samples/nocturne.lys showed on 2026-09-23, when its m6 decrescendo ran
/// through the left hand's beam: a PHANTOM tuplet number (an earlier system's tuplet laid
/// out on this system's columns) held the staves apart by accident, and once it was gone
/// nothing reserved the wedge, because no hairpin was ever in the room's silhouette.
/// </summary>
/// <remarks>
/// The nets are DIFFERENTIAL where the quantity is a whole layout (RULES §7.7 — a
/// difference net needs no hand-written expected value and is the stronger net against a
/// second spelling): the same book with and without the one thing under test, compared on
/// the one number the thing may move. Each has a control the other way, so a net that
/// moved EVERY number would fail too.
/// </remarks>
[Trait("Category", "Unit")]
public class StaffSilhouetteSeedTests
{
    private static ScoreLayout Layout(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics.Select(d => d.Message)));
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine(new LayoutOptions()).Layout(multi);
    }

    private static ImmutableArray<MeasureLayout> KeyedLayouts(int itemCount = 3)
    {
        var items = ImmutableArray.CreateBuilder<ItemLayout>(itemCount);
        for (int i = 0; i < itemCount; i++)
            items.Add(new ItemLayout(i, 2.0 + i * 3.0, 1.0));
        // Keyed by MEASURE INDEX: measure 0 is not on this "system", measure 1 is.
        return ImmutableArray.Create<MeasureLayout>(
            null!, new MeasureLayout(1, 0, 30, items.ToImmutable()));
    }

    /// <summary>
    /// The engraver's <c>measureLayouts</c> is keyed by measure index, and a caller scoped
    /// to one system leaves the other systems' slots EMPTY: a tuplet whose measure is such
    /// a hole is dropped, not laid out on whatever measure happens to sit at that position.
    /// </summary>
    [Fact]
    public void TupletBracketEngraver_DropsATupletWhoseMeasureIsAHole()
    {
        var onHole = new TupletBracketItem(3, 2, 0, 2, MeasureIndex: 0, SourcePosition: 0);
        var onSystem = new TupletBracketItem(3, 2, 0, 2, MeasureIndex: 1, SourcePosition: 0);
        var result = TupletBracketEngraver.Calculate(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled,
            ImmutableArray.Create(onHole, onSystem), KeyedLayouts(),
            ImmutableArray<Measure>.Empty, ImmutableArray<BeamGroup>.Empty);

        var only = Assert.Single(result);
        Assert.Equal(1, only.MeasureIndex);
    }

    // A two-system grand staff (the break is explicit). The right hand's FOURTH bar carries
    // the thing under test, and its EIGHTH — the fourth bar of the SECOND system, the same
    // position on that system — carries the pp whose level is measured.
    private static string TwoSystemBook(string rhBar4)
    {
        const string four = "c''4 d'' e'' f'' |";
        const string lhFour = "c4 d e f |";
        return $$"""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section S {
              rh { {{four}} {{four}} {{four}} {{rhBar4}} break {{four}} {{four}} {{four}} <a' d'' fis''>1@pp | }
              lh { {{lhFour}} {{lhFour}} {{lhFour}} {{lhFour}} {{lhFour}} {{lhFour}} {{lhFour}} {{lhFour}} }
            }
            form main { ~S }
            score main "x" {
              grandStaff {
                staff rh
                staff lh
              }
            }
            """.Replace("\r\n", "\n");
    }

    /// <summary>
    /// A tuplet on the FIRST system's fourth bar (stems down, so its number hangs below the
    /// staff) must not reach the pp under the SECOND system's fourth bar: the room used to
    /// hand the tuplet engraver this system's measures in system order while the engraver
    /// reads them by measure index, so measure 3's number was seeded under measure 7 and
    /// pushed that pp — and the whole dynamic line it shares with any wedge — down by the
    /// number's height (2.94 on the nocturne).
    /// </summary>
    [Fact]
    public void ATupletOnTheFirstSystem_DoesNotMoveADynamicOnTheSecond()
    {
        var plain = Layout(TwoSystemBook("c''4 d'' e'' f'' |"));
        var withTuplet = Layout(TwoSystemBook("tuplet 3/2 { b'8 c'' d'' } e''4 f''4 g''4 |"));

        Assert.Equal(2, plain.AllSystems.Length);
        Assert.Equal(2, withTuplet.AllSystems.Length);
        // The control: the tuplet IS there, once, on measure 3.
        Assert.Single(withTuplet.TupletBracketLayouts, t => t.MeasureIndex == 3);
        Assert.Empty(plain.TupletBracketLayouts);

        var ppPlain = Assert.Single(plain.DynamicLayouts, d => d.MeasureIndex == 7);
        var ppTuplet = Assert.Single(withTuplet.DynamicLayouts, d => d.MeasureIndex == 7);
        Assert.Equal(ppPlain.YUp, ppTuplet.YUp, 9);
    }

    // One system, the nocturne's own last three bars (absolute spelling): the right hand's
    // decrescendo runs from m1 to the pp in m3, pushed down under m2's slur, over the left
    // hand's second beam of m1, whose stems point up and reach into the gap.
    private static string WedgeBook(bool withWedge, bool lhReachesUp)
    {
        // `octave absolute` spells C4 as `c` (the twin's \fixed c'): b4 d5 / a4 … / a4 d5 fis5.
        string rh = withWedge
            ? "<b d'>2( <bes d'>2@decresc) | tuplet 3/2 { a8( b a } fis4 e8 g fis e) | <a d' fis'>1@pp |"
            : "<b d'>2( <bes d'>2) | tuplet 3/2 { a8( b a } fis4 e8 g fis e) | <a d' fis'>1@pp |";
        string lh = lhReachesUp
            ? "g,8 d g b g,,8 bes,, d, g, | a,,8 d, fis, a, a,, cis, e, g, | d,,8 a, d fis a,2 |"
            : "g,,8 a,, b,, c, g,,8 a,, b,, c, | a,,8 b,, c, d, a,, b,, c, d, | d,,1 |";
        return $$"""
            octave absolute
            time 4/4
            key d major
            part rh { clef treble }
            part lh { clef bass }
            section S {
              rh { {{rh}} }
              lh { {{lh}} }
            }
            form main { ~S }
            score main "x" {
              grandStaff {
                staff rh
                staff lh
              }
            }
            """.Replace("\r\n", "\n");
    }

    private static double StaffGap(ScoreLayout layout)
    {
        var staves = Assert.Single(layout.AllSystems).StaffGroups[0].Staves;
        // Y is Y-up from the system top: the lower staff's Y is negative, and the gap is the
        // distance between the upper staff's bottom line and the lower staff's top line.
        return (staves[0].Y - staves[0].Height) - staves[1].Y;
    }

    /// <summary>
    /// The wedge is in the staff's silhouette: a left hand whose beam reaches up under the
    /// decrescendo is spaced off the wedge (LilyPond leaves a placed DynamicLineSpanner in
    /// the axis group's skyline — on the nocturne it opens the gap from 5.00 to 6.22, and
    /// the room reserved nothing for it until 2026-09-23). A left hand that stays low is
    /// not moved by the wedge at all.
    /// </summary>
    [Fact]
    public void AHairpin_OpensTheGapToAStaffWhoseInkReachesIt_AndOnlyThen()
    {
        double high = StaffGap(Layout(WedgeBook(withWedge: true, lhReachesUp: true)));
        double highNoWedge = StaffGap(Layout(WedgeBook(withWedge: false, lhReachesUp: true)));
        Assert.True(high > highNoWedge + 0.5,
            $"the wedge should open the gap: with {high:F3}, without {highNoWedge:F3}");

        double low = StaffGap(Layout(WedgeBook(withWedge: true, lhReachesUp: false)));
        double lowNoWedge = StaffGap(Layout(WedgeBook(withWedge: false, lhReachesUp: false)));
        Assert.Equal(lowNoWedge, low, 9);
    }

    // The nocturne's last three bars TWICE, on two systems, the left hand pedalled through
    // its first bar on each: the second system's right hand carries the decrescendo that
    // opens that system's gap, the first system's does not.
    private static string PedalledTwoSystemBook()
    {
        const string rhPlain = "<b d'>2( <bes d'>2) | tuplet 3/2 { a8( b a } fis4 e8 g fis e) | <a d' fis'>1@pp |";
        const string rhWedge = "<b d'>2( <bes d'>2@decresc) | tuplet 3/2 { a8( b a } fis4 e8 g fis e) | <a d' fis'>1@pp |";
        const string lh = "g,8@sustain d g b g,,8 bes,, d, g,@!sustain | a,,8 d, fis, a, a,, cis, e, g, | d,,8 a, d fis a,2 |";
        return $$"""
            octave absolute
            time 4/4
            key d major
            part rh { clef treble }
            part lh { clef bass }
            section S {
              rh { {{rhPlain}} break {{rhWedge}} }
              lh { {{lh}} {{lh}} }
            }
            form main { ~S }
            score main "x" {
              grandStaff {
                staff rh
                staff lh
              }
            }
            """.Replace("\r\n", "\n");
    }

    /// <summary>
    /// A pedal bracket is DRAWN where the room SOLVED it on its own system. The left hand is
    /// the same on both systems, so the room solves the same line under it on both; the
    /// second system's staves stand further apart (the wedge), and the bracket has to
    /// follow its staff down. Until 2026-09-23 the draw converted the solved line with the
    /// FIRST system's staff offset for every system, so on samples/nocturne.lys the second
    /// system's brackets stood 1.22 too high, touching the left hand's beam.
    /// </summary>
    [Fact]
    public void APedalBracketOnTheSecondSystem_FollowsItsStaffDown()
    {
        var layout = Layout(PedalledTwoSystemBook());
        Assert.Equal(2, layout.AllSystems.Length);
        double GapOf(int s)
        {
            var staves = layout.AllSystems[s].StaffGroups[0].Staves;
            return (staves[0].Y - staves[0].Height) - staves[1].Y;
        }
        // The control: the wedge opened the second system's gap and not the first's.
        Assert.True(GapOf(1) > GapOf(0) + 0.5,
            $"the wedge should open the second system's gap: {GapOf(1):F3} against {GapOf(0):F3}");

        var first = Assert.Single(layout.PedalBracketLayouts, b => b.StartMeasureIndex == 0);
        var second = Assert.Single(layout.PedalBracketLayouts, b => b.StartMeasureIndex == 3);
        // Y is device-down from the system top; a staff's Y is Y-up from it.
        double belowFirst = first.Y + layout.AllSystems[0].StaffGroups[0].Staves[1].Y;
        double belowSecond = second.Y + layout.AllSystems[1].StaffGroups[0].Staves[1].Y;
        Assert.Equal(belowFirst, belowSecond, 9);
    }

    // A 24-bar single staff with a crescendo from bar 3 to the f in bar 22, or without.
    private const int Bars = 24;

    private static string SpanBook(bool wedged)
    {
        var music = new System.Text.StringBuilder();
        for (int bar = 1; bar <= Bars; bar++)
            music.Append(wedged && bar == 3 ? "c4 d@cresc e f | "
                : wedged && bar == 22 ? "c4 d@f e f | "
                : "c4 d e f | ");
        return $$"""
            time 4/4
            part melody { clef treble }
            section Main {
              melody { {{music}} }
            }
            form main { Main }
            score main "x" { staff melody }
            """.Replace("\r\n", "\n");
    }

    /// <summary>
    /// A hairpin reaches the content keys of the measures it merely CROSSES, as a pedal
    /// bracket does (<see cref="MeasureContentKeySpanTests"/>): the room now reserves the
    /// wedge on every system it crosses, so deleting the terminating dynamic must re-derive
    /// the systems between the cresc mark and it.
    /// </summary>
    [Fact]
    public void AHairpinCrossingBareMeasures_MovesTheirContentKeys()
    {
        static MultiStaffScore ScoreOf(string src)
        {
            var tree = SyntaxTree.Parse(src);
            Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics.Select(d => d.Message)));
            return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        }
        var wedged = MeasureContentKey.Compute(ScoreOf(SpanBook(wedged: true)));
        var plain = MeasureContentKey.Compute(ScoreOf(SpanBook(wedged: false)));
        Assert.Equal(Bars, wedged.Length);
        Assert.Equal(Bars, plain.Length);

        const int crossed = 10;   // 0-based: bar 11, between the cresc and its f
        Assert.NotEqual(plain[crossed], wedged[crossed]);
        foreach (int outside in new[] { 0, 1, Bars - 2, Bars - 1 })
            Assert.Equal(plain[outside], wedged[outside]);
    }
}
