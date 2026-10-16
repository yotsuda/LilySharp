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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A bar rest written with no duration — <c>R</c>, <c>R*3</c> — lasts its bar, whatever the
/// meter (owner's decision 2026-10-02, <see cref="BarRest"/>): a 5/4 bar has no single note
/// value to write it with. It leaves the running duration alone; <c>R | R</c> stays two
/// one-bar rests and <c>R*2</c> is one two-bar rest, as LilyPond engraves <c>R1 | R1</c> and
/// <c>R1*2</c>.
/// </summary>
[Trait("Category", "Unit")]
public class BarRestTests
{
    private static string Book(string time, string music, string header = "") => $$"""
        octave absolute
        time {{time}}
        part m { clef treble }
        section A { {{header}} m { {{music}} } }
        form { ~A }
        score { staff m }
        """;

    private static SyntaxTree Parse(string lys)
    {
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return tree;
    }

    private static string[] Codes(string lys)
        => SemanticValidation.Run(Parse(lys)).Select(d => d.Code).ToArray();

    [Theory]
    [InlineData(4, 4, 1, 0, 1)]
    [InlineData(3, 4, 2, 1, 1)]
    [InlineData(6, 8, 2, 1, 1)]
    [InlineData(7, 8, 2, 2, 1)]
    [InlineData(2, 2, 1, 0, 1)]
    [InlineData(4, 2, 0, 0, 1)]
    [InlineData(5, 4, 4, 0, 5)]
    [InlineData(5, 8, 8, 0, 5)]
    [InlineData(1, 4, 4, 0, 1)]
    public void TheShape_IsTheNoteValueThatSpellsTheBar_ElseItsDenominatorTimesItsNumerator(
        int beats, int beatType, int value, int dots, int scale)
    {
        Assert.Equal((value, dots, scale), BarRest.Shape(new Fraction(beats, beatType)));
    }

    [Theory]
    [InlineData(4, 4, "1")]
    [InlineData(3, 4, "2.")]
    [InlineData(5, 4, "4*5")]
    [InlineData(4, 2, "\\breve")]
    public void TheTwinsDuration_IsLilyPonds(int beats, int beatType, string expected)
    {
        Assert.Equal(expected, BarRest.LilyPondDuration(new Fraction(beats, beatType)));
    }

    [Theory]
    [InlineData("4/4")]
    [InlineData("3/4")]
    [InlineData("5/4")]
    [InlineData("7/8")]
    public void ABareBarRest_LastsItsBar_OnThePage(string time)
    {
        var parts = time.Split('/');
        var bar = new Fraction(int.Parse(parts[0]), int.Parse(parts[1]));
        var measures = new MeasureCollector().Collect(Parse(Book(time, "R | R*3 | R |")), "m").Voice.Measures;
        Assert.Equal(5, measures.Count());
        Assert.All(measures, m => Assert.Equal(bar,
            m.Items.OfType<RestItem>().Single().Duration));
        Assert.All(measures, m => Assert.True(m.Items.OfType<RestItem>().Single().IsMultiMeasure));
    }

    /// <summary>In 4/4 a bare <c>R</c> is the very event <c>R1</c> makes, so a book that
    /// switches spelling draws the same page.</summary>
    [Fact]
    public void In44_ABareBarRest_IsR1()
    {
        RestItem Rest(string music) => new MeasureCollector().Collect(Parse(Book("4/4", music)), "m")
            .Voice.Measures[0].Items.OfType<RestItem>().Single();
        var bare = Rest("R |");
        var r1 = Rest("R1 |");
        Assert.Equal((r1.BaseDuration, r1.Dots, r1.TimeScale, r1.IsMultiMeasure),
            (bare.BaseDuration, bare.Dots, bare.TimeScale, bare.IsMultiMeasure));
    }

    [Theory]
    [InlineData("4/4", "R | R*2 | c'1 |")]
    [InlineData("5/4", "R | R*2 | c'4 d' e' f' g' |")]
    [InlineData("7/8", "R | c'8 d' e' f' g' a' b' |")]
    public void ABareBarRest_FillsItsBar_ForTheValidator(string time, string music)
    {
        Assert.Empty(Codes(Book(time, music)));
    }

    /// <summary>Against another part, a bare <c>R</c> is a full bar (MeasureModel, the
    /// cross-part counter) — a part resting <c>R | R*2</c> beside three bars of notes.</summary>
    [Fact]
    public void ABareBarRest_IsAFullBar_AgainstAnotherPart()
    {
        const string book = """
            octave absolute
            time 5/4
            part up { clef treble }
            part low { clef bass }
            section A {
              up { R | R*2 | c'4 d' e' f' g' | }
              low { c4 d e f g | c4 d e f g | c4 d e f g | c4 d e f g | }
            }
            form { ~A }
            score { staff up  staff low }
            """;
        Assert.Empty(Codes(book));
    }

    /// <summary>The note after a bare <c>R</c> inherits what it would have before it.</summary>
    [Fact]
    public void ABareBarRest_LeavesTheRunningDurationAlone()
    {
        string book = Book("5/4", "c'8 d' e' f' g' a' b' c'' d'' e'' | R | c'8 d' e' f' g' a' b' c'' d'' e'' |");
        Assert.Empty(Codes(book));
        var notes = new MeasureCollector().Collect(Parse(book), "m").Voice.Measures[2].Items.OfType<NoteItem>();
        Assert.All(notes, n => Assert.Equal(new Fraction(1, 8), n.Duration));
    }

    /// <summary>In a pickup the bar is the pickup.</summary>
    [Fact]
    public void ABareBarRest_InAPickup_LastsThePickup()
    {
        string book = Book("4/4", "R | c'1 |", header: "partial 4");
        Assert.Empty(Codes(book));
        var first = new MeasureCollector().Collect(Parse(book), "m").Voice.Measures[0];
        Assert.Equal(Fraction.Quarter, first.Items.OfType<RestItem>().Single().Duration);
    }

    [Theory]
    [InlineData("4/4", "c'2 R |")]
    [InlineData("4/4", "c'1 | time none R | time 4/4 c'1 |")]
    public void ABareBarRest_ThatOpensNoBar_IsAnError(string time, string music)
    {
        Assert.Contains(DiagnosticCodes.BareBarRestNeedsABar, Codes(Book(time, music)));
    }

    /// <summary>A run <c>R&lt;dur&gt;*N</c> is dur × N in whole bars of the meter
    /// (<see cref="BarRest.WholeBars"/>): <c>R4*5</c> in 5/4 is the bar's rest and <c>R4*40</c>
    /// eight bars of it, each the sign a bare <c>R</c> draws — not N bars of dur, which drew
    /// five quarter rests under LYS2001 and left 32 empty bars after <c>R4*40</c> (the OMR's
    /// report, 2026-10-16).</summary>
    [Theory]
    [InlineData("5/4", "R4*5", 1)]
    [InlineData("5/4", "R4*40", 8)]
    [InlineData("5/4", "R8*20", 2)]
    [InlineData("9/8", "R8*18", 2)]
    [InlineData("3/4", "R1*3", 4)]
    [InlineData("4/4", "R1*3", 3)]
    public void ARestRun_IsItsLengthInWholeBarsOfTheMeter(string time, string run, int bars)
    {
        var parts = time.Split('/');
        var bar = new Fraction(int.Parse(parts[0]), int.Parse(parts[1]));
        string book = Book(time, $"{run} | R |");
        Assert.Empty(Codes(book));
        var measures = new MeasureCollector().Collect(Parse(book), "m").Voice.Measures;
        Assert.Equal(bars + 1, measures.Count());
        Assert.All(measures, m => Assert.Equal(bar, m.Items.OfType<RestItem>().Single().Duration));
        Assert.All(measures, m => Assert.True(m.Items.OfType<RestItem>().Single().IsMultiMeasure));
        // ONE written run, then the bare R's: the interior bars open no run of their own.
        Assert.Equal(2, measures.Count(m => m.Items.OfType<RestItem>().Single().OpensWrittenRun));
    }

    /// <summary>The run is one symbol with its count on the page.</summary>
    [Fact]
    public void ARestRun_InAnOddMeter_IsOneSymbolWithItsCount()
    {
        var tree = Parse(Book("5/4", "c'4 d' e' f' g' | R4*40 | c'4 d' e' f' g' |"));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        var mmr = Assert.Single(layout.MultiMeasureRestLayouts);
        Assert.Equal((1, 8), (mmr.StartMeasureIndex, mmr.MeasureCount));
    }

    /// <summary>A run that is not whole bars of the meter stays N rests of its value, and the
    /// bar check says so, as before.</summary>
    [Fact]
    public void ARestRun_ThatIsNotWholeBars_IsReportedAsBefore()
    {
        Assert.Contains(DiagnosticCodes.MeasureIncomplete, Codes(Book("5/4", "c'4 d' e' f' g' | R4*3 | c'4 d' e' f' g' |")));
    }

    /// <summary>Against another part, the run is its whole bars (MeasureModel).</summary>
    [Fact]
    public void ARestRun_IsItsWholeBars_AgainstAnotherPart()
    {
        const string book = """
            octave absolute
            time 5/4
            part up { clef treble }
            part low { clef bass }
            section A {
              up { R4*10 | c'4 d' e' f' g' | }
              low { c4 d e f g | c4 d e f g | c4 d e f g | }
            }
            form { ~A }
            score { staff up  staff low }
            """;
        Assert.Empty(Codes(book));
    }

    [Fact]
    public void TheMidi_RestsForTheBars()
    {
        int Start(string time, string music) => new MidiExporter().Export(Parse(Book(time, music)))
            .Tracks.SelectMany(t => t.Notes).Min(n => n.StartTick);
        // A run's length is dur × N whatever bars it makes: `R4*40` in 5/4 is eight of them.
        Assert.Equal(8 * 2400, Start("5/4", "R4*40 | c'1 r4 |"));
    }

    [Fact]
    public void TheMusicXml_WritesARestRunsWholeBars()
    {
        var measures = new MusicXmlExporter().Export(Parse(Book("5/4", "R4*10 | c'4 d' e' f' g' |"))).Parts.Single().Measures;
        Assert.Equal(3, measures.Count);
        Assert.Equal(2, measures[0].Attributes?.MultipleRest);
        var rest = measures[0].Notes.Single();
        Assert.True(rest.IsMeasureRest);
        Assert.Null(rest.Type);
        Assert.Equal(measures[2].Notes.Sum(n => n.Duration), rest.Duration);
    }

    [Fact]
    public void TheMidi_RestsForTheBareBars()
    {
        int Start(string time, string music) => new MidiExporter().Export(Parse(Book(time, music)))
            .Tracks.SelectMany(t => t.Notes).Min(n => n.StartTick);
        Assert.Equal(2 * 2400, Start("5/4", "R*2 | c'1 r4 |"));
        Assert.Equal(2400 + 2400, Start("5/4", "R | R | c'1 r4 |"));
        // ⚠️ `R1*3` sounded ONE bar until 2026-10-02 while the page drew three.
        Assert.Equal(3 * 1920, Start("4/4", "R1*3 | c'1 |"));
    }

    [Fact]
    public void TheTwin_WritesLilyPondsBar_AndTheNextEventsOwnDuration()
    {
        // The c' after the rest inherits an eighth in Lily#; in LilyPond it would inherit the
        // rest's quarter, so the twin must write the 8.
        string ly = new LilyPondExporter().Export(Parse(Book("5/4", "c'8 d' e' f' g' a' b' c'' d'' e'' | R*2 | c' d' e' f' g' a' b' c'' d'' e'' |")));
        Assert.Contains("R4*5*2", ly);
        Assert.Matches(@"R4\*5\*2\s*\|\s*c[',]*8", ly);
        // …compressed as the page compresses it, in \compressMMRests' own words ON THE REST
        // — not as a Score-wide \layout property, which would also swallow the bar lines
        // inside a percent repeat (LilyPondExporterTests.RepeatPercent_CarriesNoSkipBars…).
        Assert.Contains("\\once \\set Score.skipBars = ##t R4*5*2", ly);
        Assert.DoesNotContain("skipBars = ##t }", ly);
    }

    [Fact]
    public void TheMusicXml_WritesAWholeMeasureRestOfTheBar_WithNoTypeWhereNoneSpellsIt()
    {
        var measures = new MusicXmlExporter().Export(Parse(Book("5/4", "R | c'1 r4 |"))).Parts.Single().Measures;
        var rest = measures[0].Notes.Single();
        Assert.True(rest.IsMeasureRest);
        Assert.Null(rest.Type);
        Assert.Equal(measures[1].Notes.Sum(n => n.Duration), rest.Duration);
    }
}
