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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A mark on an empty chord after a piece's last note, in a bar that note fills
/// (<c>fis4.@sustain b4. &lt;&gt;@!sustain |</c>, Calamo request 30, 2026-11-19): it stands on the
/// END of the last bar. Until session 1314 it was addressed to the head of a bar the voice
/// never gets, and every engraver passed over it — the pedal bracket was not drawn at all, nor
/// a text spanner ended there, nor a level written there.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, by the request — Calamo docs/repro/pedal-last-bar-2026-11-19-lp.ly):
/// <c>fis4.\sustainOn b4. &lt;&gt;\sustainOff \bar "|."</c> draws the bracket to the closing bar line.
/// </remarks>
[Trait("Category", "Unit")]
public class TrailingEmptyChordTests
{
    private static MultiStaffScore Collect(string bars, string staff = "staff m")
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\n" + bars + "\n} }\nform { A }\nscore { " + staff + " }\n");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static string Where(MusicMarkItem m) => $"bar {m.MeasureIndex} item {m.AnchorItemIndex} at {m.AnchorTiming}";

    /// <summary>The last bar's end: past its last item, the bar's length in — the address the
    /// same empty chord has when the bar before it is NOT full, which was always drawn.</summary>
    [Theory]
    [InlineData("c'2@sustain e'2 <>@!sustain |", MusicMarkType.SustainOff, "bar 0 item 2 at 1")]
    [InlineData("c'1 | c'2@sustain e'2 <>@!sustain |", MusicMarkType.SustainOff, "bar 1 item 2 at 1")]
    [InlineData("c'2@sustain e'4 <>@!sustain |", MusicMarkType.SustainOff, "bar 0 item 2 at 3/4")]          // the bar not full: as before
    [InlineData("c'2@sustain repeat tremolo 4 { e'16 g' }@!sustain |", MusicMarkType.SustainOff, "bar 0 item 3 at 1")]
    [InlineData("c'2@sostenuto e'2 <>@!sostenuto |", MusicMarkType.SostenutoOff, "bar 0 item 2 at 1")]
    [InlineData("c'2@unaCorda e'2 <>@!unaCorda |", MusicMarkType.UnaCordaOff, "bar 0 item 2 at 1")]
    [InlineData("c'4@rit d' e' f' <>@!rit |", MusicMarkType.TextSpanStop, "bar 0 item 4 at 1")]
    [InlineData("c'4@8va d' e' f' <>@!8va |", MusicMarkType.OttavaStop, "bar 0 item 4 at 1")]
    // A hairpin's stop keeps its address: HairpinEngraver reads it as the final bar line itself.
    [InlineData("c'4@cresc d' e' f' <>@!cresc |", MusicMarkType.HairpinStop, "bar 1 item 0 at 0")]
    public void AMarkAfterTheLastFullBar_StandsOnThatBarsEnd(string bars, MusicMarkType type, string where)
        => Assert.Equal(where, Where(Assert.Single(Collect(bars).MusicMarks, m => m.Type == type)));

    /// <summary>With music after it the empty chord's moment is the next bar's head, where
    /// LilyPond's timestep puts its marks — as it was.</summary>
    [Fact]
    public void WithABarAfterIt_TheMarkStandsOnThatBarsHead()
    {
        var off = Assert.Single(Collect("c'2@sustain e'2 <>@!sustain | g'1 |").MusicMarks, m => m.Type == MusicMarkType.SustainOff);
        Assert.Equal("bar 1 item 0 at 0", Where(off));
    }

    /// <summary>
    /// A level and a text written there stand on the last bar's end too and are drawn (owner's
    /// decision 2026-11-19, session 1315) — LILYSHARP-OWN: LilyPond prints neither, its final
    /// moment having no note column (MEASURED 2.26.0, Lab sessions/p1314/lp/end.ly: <c>&lt;&gt;\f</c>
    /// and <c>&lt;&gt;^"fine"</c> before <c>\bar "|."</c> print nothing).
    /// </summary>
    [Fact]
    public void ALevelAndATextAfterTheLastFullBar_StandOnItsEnd_AndAreDrawn()
    {
        var score = Collect("c'4 d' e' f' <>@f |");
        var f = Assert.Single(score.Dynamics);
        Assert.Equal((0, 4, (Fraction?)new Fraction(1, 1)), (f.MeasureIndex, f.ItemIndex, f.Moment));
        Assert.Single(new LayoutEngine().Layout(score).DynamicLayouts);

        var fine = Collect("c'4 d' e' f' <>@text(\"fine\") |");
        Assert.Equal((0, 4), (Assert.Single(fine.Dynamics).MeasureIndex, fine.Dynamics[0].ItemIndex));
        Assert.Single(new LayoutEngine().Layout(fine).DynamicLayouts);

        // The level a closing hairpin runs to: the hairpin ends on it, where it ran to the
        // bar line with nothing after it.
        var closing = Collect("c'1@decresc <>@pp |");
        var pin = Assert.Single(HairpinEngraver.DetectHairpins(closing.MusicMarks, closing.Dynamics));
        Assert.Equal((0, 1, false), (pin.EndMeasureIndex, pin.EndItemIndex, pin.EndsAtStop));
        Assert.Single(new LayoutEngine().Layout(closing).DynamicLayouts);

        // With a bar after it: the next bar's head, as it was.
        var mid = Assert.Single(Collect("c'4 d' e' f' <>@f | g'1 |").Dynamics);
        Assert.Equal((1, 0), (mid.MeasureIndex, mid.ItemIndex));
    }

    /// <summary>
    /// A FERMATA written there stands over the closing bar line: centred on the bar's end,
    /// outside the staff — above, or below when it is written <c>.down</c> (owner's decision
    /// 2026-11-19, session 1315; LILYSHARP-OWN, ArticulationEngraver's bar-end arm — LilyPond
    /// prints none, Lab sessions/p1314/lp/end.ly 5). Its height is the one a fermata takes over
    /// a note that lies inside the staff: the staff alone supports both.
    /// Any other script is drawn on an item, keeps its address and is not drawn.
    /// </summary>
    [Fact]
    public void AFermataAfterTheLastFullBar_StandsOverTheClosingBarLine()
    {
        var score = Collect("c''4 b' a' b' <>@fermata |");
        var fermata = Assert.Single(score.Articulations);
        Assert.Equal((0, 4), (fermata.MeasureIndex, fermata.ItemIndex));
        var page = new LayoutEngine().Layout(score);
        var drawn = Assert.Single(page.ArticulationLayouts);
        Assert.True(drawn.IsAbove);
        // Right of every note, and at the height of the fermata on the b' (the middle line).
        var onANote = Assert.Single(new LayoutEngine().Layout(Collect("c''4 b' a' b'@fermata |")).ArticulationLayouts);
        Assert.True(drawn.X > onANote.X + 1.0, $"{drawn.X} against the last note's {onANote.X}");
        Assert.Equal(onANote.YUp, drawn.YUp, 6);

        var below = Assert.Single(new LayoutEngine().Layout(Collect("c''4 b' a' b' <>@fermata.down |")).ArticulationLayouts);
        Assert.False(below.IsAbove);
        Assert.Equal(-drawn.YUp, below.YUp, 6);

        // An accent there marks no note: left where it was, and not drawn.
        var accent = Collect("c''4 b' a' b' <>@accent |");
        Assert.Equal((1, 0), (Assert.Single(accent.Articulations).MeasureIndex, accent.Articulations[0].ItemIndex));
        Assert.Empty(new LayoutEngine().Layout(accent).ArticulationLayouts);
    }

    /// <summary>
    /// A trill's line ended on an empty chord ends at the chord's moment: an <c>@!trillSpan</c>
    /// includes its note (owner's decision 2026-10-28) and an empty chord has none. Until
    /// session 1315 it was inclusive there too: mid-piece the line ran over the next bar's
    /// note, and after a piece's last note it ran to a note no bar has and was not drawn at
    /// all — neither the "tr" nor the line. MEASURED (LilyPond 2.26.0, Lab
    /// sessions/p1314/lp/end.ly 7): <c>c'2\startTrillSpan e'2 &lt;&gt;\stopTrillSpan \bar "|."</c>
    /// draws the line to the end.
    /// </summary>
    [Fact]
    public void ATrillsLineEndedOnAnEmptyChord_IncludesNoNote()
    {
        static string Say(TrillSpannerItem t) => $"({t.StartMeasureIndex},{t.StartItemIndex})..({t.EndMeasureIndex},{t.EndItemIndex}) inclusive {t.EndInclusive}";
        // At the piece's end: what a trill with no stop at all has (the virtual stop past the
        // last bar), which the engraver draws to the final bar line.
        var last = Collect("c'2@trillSpan e'2 <>@!trillSpan |");
        Assert.Equal("(0,0)..(1,0) inclusive False", Say(Assert.Single(last.TrillSpanners)));
        Assert.Equal(Say(Assert.Single(Collect("c'2@trillSpan e'2 |").TrillSpanners)), Say(last.TrillSpanners[0]));
        Assert.Single(new LayoutEngine().Layout(last).TrillSpannerLayouts);
        // Mid-piece: the next bar's head, not past its note.
        Assert.Equal("(0,0)..(1,0) inclusive False", Say(Assert.Single(Collect("c'2@trillSpan e'2 <>@!trillSpan | g'1 |").TrillSpanners)));
        // On a note it includes the note, as before.
        Assert.Equal("(0,0)..(0,1) inclusive True", Say(Assert.Single(Collect("c'2@trillSpan e'2@!trillSpan |").TrillSpanners)));
    }

    /// <summary>Each voice by its own last bar, on a staff of two voices and on a second staff.</summary>
    [Fact]
    public void EveryVoiceAndStaff_ByItsOwnLastBar()
    {
        var two = Collect("voice { c''1 <>@!sustain } { c'2@sustain e'2 } |");
        Assert.Equal("bar 0 item 1 at 1", Where(Assert.Single(two.MusicMarks, m => m.Type == MusicMarkType.SustainOff)));

        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart a { clef treble }\npart b { clef bass }\n"
            + "section A { a { c''1 | } b { c2@sustain e2 <>@!sustain | } }\nform { A }\nscore { staff a  staff b }\n");
        var staves = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var off = Assert.Single(staves.MusicMarks, m => m.Type == MusicMarkType.SustainOff);
        Assert.Equal((1, "bar 0 item 2 at 1"), (off.StaffIndex, Where(off)));
    }

    /// <summary>The one-staff collect (<c>MeasureCollector.Collect</c>), which builds its score
    /// on its own two paths — one voice, and several.</summary>
    [Fact]
    public void TheOneStaffCollect_DoesTheSame()
    {
        static Score One(string bars) => new MeasureCollector().Collect(SyntaxTree.Parse(
            "octave absolute\ntime 4/4\npart m { clef treble }\nsection A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n"));
        Assert.Equal("bar 0 item 2 at 1", Where(Assert.Single(One("c'2@sustain e'2 <>@!sustain |").MusicMarks, m => m.Type == MusicMarkType.SustainOff)));
        Assert.Equal("bar 0 item 2 at 1", Where(Assert.Single(
            One("voice { c''1 } { c'2@sustain e'2 <>@!sustain } |").MusicMarks, m => m.Type == MusicMarkType.SustainOff)));
    }

    /// <summary>The page: the bracket, the spanner and the octave bracket are laid out — one
    /// each, where there was none (the octave bracket: over all four notes).</summary>
    [Fact]
    public void ThePage_DrawsThem()
    {
        static ScoreLayout Page(string bars) => new LayoutEngine().Layout(Collect(bars));
        Assert.Single(Page("c'2@sustain e'2 <>@!sustain |").PedalBracketLayouts);
        Assert.Single(Page("c'4@rit d' e' f' <>@!rit |").TextSpannerLayouts);
        // An ottava's end on an empty chord is the chord's moment: it carries no note to
        // include. The bracket is the one `f'@!8va` draws, with a bar after it or without.
        string Ottava(string bars) => Assert.Single(Page(bars).OttavaBracketLayouts).ToString();
        Assert.Equal(Ottava("c'4@8va d' e' f'@!8va |"), Ottava("c'4@8va d' e' f' <>@!8va |"));
        // …and with a bar after it the bracket ends with the bar it was written in: the next
        // bar's note is not under it (until session 1314 the g was, the mark standing past it).
        var mid = Assert.Single(OttavaBracketEngraver.DetectOttavaBrackets(Collect("c'4@8va d' e' f' <>@!8va | g'1 |").MusicMarks, null));
        Assert.Equal(0, mid.EndMeasureIndex);
    }
}
