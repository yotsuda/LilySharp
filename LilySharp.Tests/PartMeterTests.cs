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
using LilySharp.Core;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// HANDOFF §2 F-partmeter ⒜ (owner's decision 2026-10-06): a <c>time</c> written in ONE part's
/// music is the meter of every part from that bar of the section — LilyPond's Timing, which
/// lives in the Score — so a part may be written in pieces. A part that writes nothing there
/// (a section it does not write, the bars it is short of) is padded with bars of that meter
/// and shows the change; a part that writes music there is measured against it.
/// <see cref="SectionMeterPlan"/> holds the rule; the page, the validator, the twin, the MIDI
/// and the MusicXML read it.
/// </summary>
public class PartMeterTests
{
    // Lab sessions/p845/gs/c7-silent.lys, with a section after: B is bot's alone.
    private const string Silent = """
        octave absolute
        time 4/4
        part top { clef treble }
        part bot { clef bass }
        section A { top { c'1 | } bot { c1 | } }
        section B { bot { time 3/4 g2. | a2. | } }
        section C { top { e'1 | } bot { c1 | } }
        form main { A B C }
        score main { staff top staff bot }
        """;

    // top writes B without restating bot's 3/4.
    private const string Writing = """
        octave absolute
        time 4/4
        part top { clef treble }
        part bot { clef bass }
        section A { top { c'1 | } bot { c1 | } }
        section B { top { e'2. | f'2. | } bot { time 3/4 g2. | a2. | } }
        section C { top { g'1 | } bot { c1 | } }
        form main { A B C }
        score main { staff top staff bot }
        """;

    // top writes one bar of B, bot two: top's padding bar is a 3/4 bar.
    private const string Short = """
        octave absolute
        time 4/4
        part top { clef treble }
        part bot { clef bass }
        section A { top { c'1 | } bot { c1 | } }
        section B { top { e'2. | } bot { time 3/4 g2. | a2. | } }
        section C { top { g'1 | } bot { c1 | } }
        form main { A B C }
        score main { staff top staff bot }
        """;

    private static IReadOnlyList<Measure> Staff(string source, int staff)
    {
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return score.StaffGroups.SelectMany(g => g.Staves).ElementAt(staff).Voices[0].Measures;
    }

    private static TimeSignature? MeterChangeOf(Measure m)
        => m.Items.OfType<TimeSignatureChangeItem>().FirstOrDefault()?.NewTime;

    private static IReadOnlyList<Diagnostic> Validate(string source)
    {
        var v = new MeasureValidator();
        v.Validate(SyntaxTree.Parse(source));
        return v.Diagnostics;
    }

    // ---------------------------------------------------------------- the page

    [Fact]
    public void ASilentPart_IsPaddedWithBarsOfTheOtherPartsMeter_AndShowsIt()
    {
        var top = Staff(Silent, 0);
        Assert.Equal(4, top.Count);
        Assert.Equal((3, 4), MeterChangeOf(top[1]) is { } t ? (t.Beats, t.BeatType) : default);
        Assert.Null(MeterChangeOf(top[2]));
        Assert.All(new[] { top[1], top[2] }, m =>
            Assert.Equal(new Fraction(3, 4), m.Items.OfType<RestItem>().Single().BaseDuration));
        // The section after resets the meter on both staves.
        Assert.Equal((4, 4), MeterChangeOf(top[3]) is { } c ? (c.Beats, c.BeatType) : default);
        Assert.Equal(4, Staff(Silent, 1).Count);
    }

    [Fact]
    public void APartWritingTheBars_TakesTheOtherPartsMeter()
    {
        var top = Staff(Writing, 0);
        Assert.Equal(4, top.Count);
        Assert.Equal((3, 4), MeterChangeOf(top[1]) is { } t ? (t.Beats, t.BeatType) : default);
        Assert.Equal(Staff(Writing, 1).Count, top.Count);
    }

    [Fact]
    public void AShortPart_IsPaddedWithABarOfTheMeterThere()
    {
        var top = Staff(Short, 0);
        Assert.Equal(4, top.Count);
        Assert.Equal(new Fraction(3, 4), top[2].Items.OfType<RestItem>().Single().BaseDuration);
    }

    // ---------------------------------------------------------------- the checks

    [Fact]
    public void BarsUnderTheOtherPartsMeter_AreNotReportedShort()
    {
        var diags = Validate(Writing);
        Assert.DoesNotContain(diags, d => d.Code is DiagnosticCodes.MeasureIncomplete or DiagnosticCodes.PickupWithoutPartial);
    }

    [Fact]
    public void TwoPartsWritingDifferentMetersAtOneBar_TheSecondIsNamed()
    {
        var diags = Validate("""
            octave absolute
            part top { clef treble }
            part bot { clef bass }
            section A { top { time 2/4 c'2 | } bot { time 3/4 c2. | } }
            form main { A }
            score main { staff top staff bot }
            """);
        var d = Assert.Single(diags, x => x.Code == DiagnosticCodes.ConflictingTimeSignatures);
        Assert.Contains("'time 3/4' is not the meter of bar 1 of section 'A'", d.Message);
    }

    [Fact]
    public void EveryPartWritingTheChange_NeedsNoPlan()
    {
        var root = SyntaxTree.Parse("""
            part top { clef treble }
            part bot { clef bass }
            section A { top { c'1 | time 3/4 c'2. | } bot { c1 | time 3/4 c2. | } }
            form main { A }
            score main { staff top staff bot }
            """).GetRoot();
        Assert.True(SectionMeterPlan.Build(root).IsEmpty);
        Assert.False(SectionMeterPlan.Build(SyntaxTree.Parse(Silent).GetRoot()).IsEmpty);
    }

    // ---------------------------------------------------------------- the exporters

    [Fact]
    public void TheTwin_WritesTheSilentAndPaddedBarsInThatMeter()
    {
        var silent = new LilyPondExporter().Export(SyntaxTree.Parse(Silent));
        Assert.Contains("\\mark \\markup \\box \"B\" \\time 3/4 s2. |", silent);
        Assert.DoesNotContain("s1", silent);
        var shorter = new LilyPondExporter().Export(SyntaxTree.Parse(Short));
        // The voice that writes the bar takes the change too, so its padding is in it.
        Assert.Contains("\\box \"B\" \\time 3/4 e'2. |\n  s2. |\n  \\time 4/4 \\mark", shorter.Replace("\r\n", "\n"));
        Assert.DoesNotContain("s1", shorter);
    }

    [Fact]
    public void TheTwin_WritesAChordRowsSilentBarsInThatMeter()
    {
        var twin = new LilyPondExporter().Export(SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part bot { clef bass }
            chords prog { section A { C | } section C { G | } }
            section A { bot { c1 | } }
            section B { bot { time 3/4 g2. | a2. | } }
            section C { bot { c1 | } }
            form main { A B C }
            score main { chords prog staff bot }
            """));
        var chords = twin[twin.IndexOf("\\chordmode", System.StringComparison.Ordinal)..];
        chords = chords[..chords.IndexOf('}')];
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(chords, @"s2\.").Count);
        Assert.DoesNotContain("s1", chords);
    }

    [Fact]
    public void TheMidi_PadsAShortPartWithBarsOfThatMeter()
    {
        var file = new MidiExporter().Export(SyntaxTree.Parse(Short));
        // C opens after 4/4 + 3/4 + 3/4 on both parts — a 4/4 padding bar put top's C a
        // quarter late.
        var lastStarts = file.Tracks.Where(t => t.Notes.Count > 0)
            .Select(t => t.Notes.Max(n => n.StartTick) / file.TicksPerQuarterNote).ToList();
        Assert.Equal(new[] { 10, 10 }, lastStarts);
    }

    [Fact]
    public void TheMusicXml_WritesTheOtherPartsTimeInEveryPart()
    {
        foreach (var source in new[] { Silent, Writing, Short })
        {
            var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();
            var parts = xml.Descendants("part").ToList();
            Assert.Equal(2, parts.Count);
            Assert.All(parts, p =>
            {
                var measures = p.Elements("measure").ToList();
                Assert.Equal(4, measures.Count);
                Assert.Equal("3", measures[1].Descendants("beats").Single().Value);
                Assert.Equal("4", measures[3].Descendants("beats").Single().Value);
            });
        }
    }
}
