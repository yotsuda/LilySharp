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
/// A hairpin is its own voice's (session 1238): it is paired with the end its voice writes,
/// its bounds are that voice's columns and it sides off that voice's heads and stems. Until
/// then the staff was one stream read on its first voice's items — a lower voice's hairpin
/// ended on the upper voice's note of the same number.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, raggedRight, Lab sessions/p1237/hpv/f.ly — the twin of the book
/// below, bar by bar; the printed extents are the wedge's, half a line thickness wider at
/// either end than its bounds):
///   bar 1  17.071..25.706  the rests' left edges, 17.121 and 25.656
///   bar 2  35.819..42.570  the p's right edge + 1.0, the f's left edge − 1.0
///   bar 4  68.134..79.628  the first c' head's left 68.184, the last c' column's right 79.578
/// </remarks>
[Trait("Category", "Unit")]
public class HairpinVoiceTests
{
    private const string Head = "octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\nsection A { m {\n";
    private const string Tail = "\n} }\nform { A }\nscore { staff m }\n";

    private static Score Collect(string bars)
        => new MeasureCollector().Collect(SyntaxTree.Parse(Head + bars + Tail));

    private static ImmutableArray<HairpinItem> Pins(string bars)
    {
        var score = Collect(bars);
        return HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics);
    }

    private static string Say(HairpinItem p)
        => $"voice {p.VoiceIndex} ({p.StartMeasureIndex},{p.StartItemIndex})..({p.EndMeasureIndex},{p.EndItemIndex}) end voice {p.EndVoiceIndex} stop {p.EndsAtStop}";

    [Fact]
    public void ALowerVoicesHairpin_EndsAtItsOwnVoicesStop()
    {
        var pin = Assert.Single(Pins("voice { c''8 c'' c''4 c''2 } { r4@cresc r r@!cresc r } |"));
        Assert.Equal("voice 1 (0,0)..(0,2) end voice 1 stop True", Say(pin));
    }

    [Fact]
    public void ALowerVoicesHairpin_EndsAtItsOwnVoicesLevel()
    {
        var pin = Assert.Single(Pins("voice { c''8 c'' c'' c'' c''2 } { c'2@p@cresc c'2@f } |"));
        Assert.Equal("voice 1 (0,0)..(0,1) end voice 1 stop False", Say(pin));
    }

    /// <summary>The upper voice's <c>@mf</c> is its second item; the lower voice's hairpin runs
    /// past its own second item to the stop on its fifth. Read as one stream, the mf ended it.</summary>
    [Fact]
    public void AnotherVoicesLevel_DoesNotEndAHairpinThatHasItsOwnEnd()
    {
        var pin = Assert.Single(Pins("voice { c''2 c''2@mf } { c'8@cresc c' c' c' c'2@!cresc } |"));
        Assert.Equal("voice 1 (0,0)..(0,4) end voice 1 stop True", Say(pin));
    }

    /// <summary>
    /// The page, against LilyPond 2.26.0 on the same five bars (the class remarks): the wedge's
    /// two bounds and its line's height about the staff middle. Bars 1, 2 and 4 are a lower
    /// voice's hairpins over an upper voice of another rhythm; bar 3 is the upper voice's own,
    /// the control that was right before.
    /// </summary>
    [Theory]
    [InlineData(0, 17.121, 25.656, -5.3436)]   // r4@cresc r r@!cresc r — rest to rest
    [InlineData(1, 35.869, 42.520, -6.6078)]   // c'2@p@cresc c'2@f — text to text
    [InlineData(2, 49.575, 65.800, -6.5244)]   // the upper voice's c''2@cresc … c''@!cresc
    [InlineData(3, 68.184, 79.578, -6.8361)]   // c'8@cresc c' c' c' c'2@!cresc — under down stems
    public void TheWedge_StandsOnItsOwnVoicesColumns_AsLilyPondDrawsIt(
        int bar, double startX, double endX, double lineY)
    {
        var score = Collect("""
            voice { c''8 c'' c''4 c''2 } { r4@cresc r r@!cresc r } |
            voice { c''8 c'' c'' c'' c''2 } { c'2@p@cresc c'2@f } |
            voice { c''2@cresc c''8 c'' c'' c''@!cresc } { c'4 c' c' c' } |
            voice { c''1 } { c'8@cresc c' c' c' c'2@!cresc } |
            voice { c''4@cresc c'' c'' c'' } { c'2 c'2@f } |
            """);
        var wedge = Assert.Single(new LayoutEngine().Layout(score).HairpinLayouts,
            h => h.StartMeasureIndex == bar);
        Assert.Equal(startX, wedge.StartX, 0.005);
        Assert.Equal(endX, wedge.EndX, 0.005);
        // YUp is about the system's top, which is the staff's top line here.
        Assert.Equal(lineY, wedge.YUp + EngravingDefaults.StaffMiddle, 0.005);
    }

    private static (double StartX, double EndX, double Y)[] Wedges(string bar)
        => [.. new LayoutEngine().Layout(Collect(bar)).HairpinLayouts
            .Select(h => (h.StartX, h.EndX, Y: h.YUp + EngravingDefaults.StaffMiddle))
            .OrderByDescending(w => w.Y)];

    private static double[] LevelHeights(string bar)
        => [.. new LayoutEngine().Layout(Collect(bar)).DynamicLayouts
            .OrderBy(d => d.X).ThenByDescending(d => d.YUp).Select(d => d.YUp)];

    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 0.005);

    /// <summary>
    /// Two voices' hairpins over the same beats are two lines, the upper voice's by the staff
    /// and the lower voice's under it — until session 1238 one line, a <c>&lt;</c> drawn through
    /// a <c>&gt;</c>. MEASURED (LilyPond 2.26.0, Lab sessions/p1238/hpv v6 and v1; a printed
    /// extent is half a line thickness wider at either end than the bound).
    /// </summary>
    [Fact]
    public void TwoVoicesHairpinsAtOnce_AreTwoLines()
    {
        var both = Wedges("voice { c''8@cresc c'' c'' c''@!cresc c''2 } { c'2@decresc c'2@!decresc } |");
        Assert.Equal(2, both.Length);
        Near(17.121, both[0].StartX); Near(25.938, both[0].EndX); Near(-6.0235, both[0].Y);
        Near(17.121, both[1].StartX); Near(28.515, both[1].EndX); Near(-7.3995, both[1].Y);

        // …and each line holds its own voice's levels: the lower voice's p and pp ride its
        // wedge's line (0.6 under it), under the upper voice's.
        const string levelled = "voice { c''2@cresc c''2@!cresc } { c'2@p@decresc c'2@pp } |";
        var lines = Wedges(levelled);
        Near(17.121, lines[0].StartX); Near(22.774, lines[0].EndX); Near(-6.5263, lines[0].Y);
        Near(-8.2228, lines[1].Y);
        Assert.All(LevelHeights(levelled), y => Near(-8.8228, y));
    }

    /// <summary>Both voices write the same <c>@p@cresc … @f</c>: two lines, each with its
    /// levels, one under the other — LilyPond engraves each voice's. MEASURED (v3).</summary>
    [Fact]
    public void TheSameHairpinInBothVoices_IsDrawnForEach()
    {
        const string bar = "voice { c''2@p@cresc c''2@f } { c'2@p@cresc c'2@f } |";
        var wedges = Wedges(bar);
        Assert.Equal(2, wedges.Length);
        Near(19.544, wedges[0].StartX); Near(20.453, wedges[0].EndX); Near(-6.4678, wedges[0].Y);
        Near(19.544, wedges[1].StartX); Near(20.453, wedges[1].EndX); Near(-8.6745, wedges[1].Y);
        var levels = LevelHeights(bar);
        Near(-7.0678, levels[0]); Near(-9.2745, levels[1]); Near(-7.0678, levels[2]); Near(-9.2745, levels[3]);
    }

    /// <summary>The upper voice's <c>@f</c> is its second item, and so is the lower voice's
    /// stop — another moment. The wedge runs to its own voice's column, not to the f. MEASURED (v2).</summary>
    [Fact]
    public void AnotherVoicesLevelAtTheSameItemNumber_IsNotTheWedgesBound()
    {
        const string bar = "voice { c''4 c''@f c'' c'' } { c'2@cresc c'2@!cresc } |";
        var wedge = Assert.Single(Wedges(bar));
        Near(17.121, wedge.StartX); Near(24.503, wedge.EndX); Near(-6.7010, wedge.Y);
        Near(-4.5461, Assert.Single(LevelHeights(bar)));

        // The same under a p (v8): the level hangs on its own voice's head, inside the wedge's
        // span and no member of its line.
        const string soft = "voice { c''4 c''4@p c'' c'' } { c'2@cresc c'2@!cresc } |";
        Near(-6.7010, Assert.Single(Wedges(soft)).Y);
        Near(-3.8180, Assert.Single(LevelHeights(soft)));
    }

    /// <summary>
    /// The upper voice's <c>@p</c> and the lower voice's hairpin start on one column: two lines
    /// of one priority, placed in the order of their left edges — the p's box starts 0.03 left
    /// of the wedge's, so the p stands by the staff and the wedge under it
    /// (OutsideStaffStacker.TurnLeft). MEASURED (v5).
    /// </summary>
    [Fact]
    public void ALevelAndAnotherVoicesWedgeOnOneColumn_AreOrderedByTheirLeftEdges()
    {
        const string bar = "voice { c''4@p c'' c'' c'' } { c'2@cresc c'2@!cresc } |";
        var wedge = Assert.Single(Wedges(bar));
        Near(17.121, wedge.StartX); Near(24.503, wedge.EndX); Near(-8.2137, wedge.Y);
        Near(-7.0827, Assert.Single(LevelHeights(bar)));
    }

    /// <summary>
    /// The same bars under a second staff: a system of several staves resolves an item's X
    /// through the timing columns, from the measures the index counts in — the hairpin's own
    /// voice's. The wedge keeps the length it has on the staff alone (the lower staff's whole
    /// rest adds no column) — the length LilyPond gives it there, an absolute claim: read on
    /// the staff's first voice, both layouts were wrong by the same amount.
    /// </summary>
    [Theory]
    [InlineData("voice { c''8 c'' c''4 c''2 } { r4@cresc r r@!cresc r } |", 25.656 - 17.121)]
    [InlineData("voice { c''1 } { c'8@cresc c' c' c' c'2@!cresc } |", 79.578 - 68.184)]
    [InlineData("voice { c''4 c''@f c'' c'' } { c'2@cresc c'2@!cresc } |", 24.503 - 17.121)]
    public void OnASystemOfTwoStaves_TheWedgeStillReadsItsOwnVoice(string bar, double length)
    {
        var alone = Assert.Single(Wedges(bar));
        Near(length, alone.EndX - alone.StartX);
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(
            "octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\npart n { clef bass }\n"
            + "section A {\n m { " + bar + " }\n n { R1 | }\n}\nform { A }\nscore { staff m staff n }\n"));
        var wedge = Assert.Single(new LayoutEngine().Layout(score).HairpinLayouts);
        Near(length, wedge.EndX - wedge.StartX);
    }

    /// <summary>
    /// What stands at "the hairpin's own note" is its own voice's: another voice's item of the
    /// same number is another moment. Three readings of that note, each with the other voice
    /// writing the mark at its item of the same number — and none of them taken.
    /// </summary>
    [Fact]
    public void AnotherVoicesMarkAtTheSameItemNumber_IsNotOnTheHairpinsNote()
    {
        // Back to back (HairpinItem.AdjacentAtEnd): the upper voice's crescendo ends on its
        // second item (beat 2), the lower voice's decrescendo starts on ITS second (beat 3).
        var apart = Pins("voice { c''4@cresc c''@!cresc c''2 } { c'2 c'4@decresc c'@!decresc } |");
        Assert.Equal(2, apart.Length);
        Assert.All(apart, p => Assert.False(p.AdjacentAtEnd || p.AdjacentAtStart, Say(p)));

        // A niente at the start (dal niente): the upper voice's stands on beat 2, the lower
        // voice's crescendo starts on beat 3.
        var plain = Assert.Single(Pins("voice { c''4 c''@niente c''2 } { c'2 c'4@cresc c'@!cresc } |"));
        Assert.Null(plain.NienteAtStart);

        // A stop written after the start on one note (`@cresc@!cresc` spans that note): the
        // lower voice's stop on beat 2 is not on the upper voice's beat 3, whose crescendo
        // runs on to its own voice's level.
        var onward = Assert.Single(Pins("voice { c''2 c''2@cresc } { c'4 c'@!cresc c'2 } | voice { c''1@f } { c'1 } |"));
        Assert.Equal("voice 0 (0,1)..(1,0) end voice 0 stop False", Say(onward));
    }

    /// <summary>
    /// An end another voice wrote is read back into the hairpin's own voice for the columns it
    /// sides off: the last own item starting no later than that end's moment. The upper voice's
    /// quarters against the lower voice's second half note (beat 3): the third quarter.
    /// </summary>
    [Fact]
    public void AnEndInAnotherVoice_IsReadBackIntoTheHairpinsOwnVoice()
    {
        var score = Collect("voice { c''4@cresc c'' c'' c'' } { c'2 c'2@f } |");
        var upper = score.Voices[0].Measures;
        var lower = score.Voices[1].Measures;
        Assert.Equal(2, HairpinEngraver.OwnVoiceEndItem(0, 1, sameVoice: false, upper, lower));
        // …the first quarter for an end on the bar's first beat, and its own number when the
        // end is its own voice's.
        Assert.Equal(0, HairpinEngraver.OwnVoiceEndItem(0, 0, sameVoice: false, upper, lower));
        Assert.Equal(1, HairpinEngraver.OwnVoiceEndItem(0, 1, sameVoice: true, upper, lower));
    }

    /// <summary>LILYSHARP-OWN (HairpinEngraver.DetectHairpins): with no end in its own voice the
    /// hairpin takes the staff's next one. LilyPond draws none ("unterminated crescendo").</summary>
    [Fact]
    public void WithNoEndInItsOwnVoice_TheStaffsNextLevelEndsIt()
    {
        var pin = Assert.Single(Pins("voice { c''4@cresc c'' c'' c'' } { c'2 c'2@f } |"));
        Assert.Equal("voice 0 (0,0)..(0,1) end voice 1 stop False", Say(pin));
    }
}
