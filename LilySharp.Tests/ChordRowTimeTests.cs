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
/// A chord row writes <c>time</c> at a bar's head (Orecchiante's request 13, owner's decision
/// 2026-11-16): <c>| G C#m7(b5) | time 2/4 C#m7(b5) | time 4/4 C Dsus4 |</c>. It is the score's
/// meter from that bar to the section's end, as a <c>time</c> in a part's music is
/// (<see cref="SectionMeterPlan"/>): the page engraves it, the slots grid on it, every part
/// reads it, and the MIDI, the MusicXML and the LilyPond twin follow. After a chord of its bar
/// it is an error (LYS1073); <c>partial</c> stays the section header's.
/// </summary>
public class ChordRowTimeTests
{
    private const string Row = "G C | G C#m7(b5) | time 2/4 C#m7(b5) | time 4/4 C Dsus4 | C D |";

    private static string RowsOnly(string row = Row) =>
        $"time 4/4\nsection A {{\n  chords harmony {{ {row} }}\n}}\nform {{ A }}\nscore {{ chords harmony }}\n";

    private const string Mixed = "time 4/4\noctave absolute\npart mel { }\nsection A {\n"
        + "  chords harmony { " + Row + " }\n"
        + "  mel { g'1 | g'1 | g'2 | e'1 | d'1 | }\n}\nform { A }\nscore { chords harmony\n staff mel }\n";

    private static IEnumerable<Staff> Staves(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return score.StaffGroups.SelectMany(g => g.Staves);
    }

    private static string?[] Meters(Staff staff) => staff.Voices[0].Measures
        .Select(m => m.Items.OfType<TimeSignatureChangeItem>().FirstOrDefault()?.NewTime is { } t
            ? $"{t.Beats}/{t.BeatType}" : null)
        .ToArray();

    // ---------------------------------------------------------------- the grammar

    [Theory]
    [InlineData(Row)]
    [InlineData("time 2/4 C | time 4/4 G |")]   // at the row's start
    [InlineData("C |time 3/4 G . . |")]          // glued to the bar line
    public void AtABarsHead_TimeIsRead_AndTheSourceRoundTrips(string row)
    {
        var tree = SyntaxTree.Parse(RowsOnly(row));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(tree.Text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void AfterAChordOfItsBar_TimeIsAnError_ThatSaysWhereToWriteIt()
    {
        var tree = SyntaxTree.Parse(RowsOnly("G C time 2/4 | C |"));
        var error = Assert.Single(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.ChordsTimeInsideBar, error.Code);
        Assert.Equal("time", tree.Text.Substring(error.Span.Start, error.Span.Length));
        Assert.Contains("right after the '|'", error.Message);
        Assert.Equal(tree.Text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void Partial_StaysTheSectionHeaders()
    {
        var tree = SyntaxTree.Parse(RowsOnly("| partial 4 G | C |"));
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.ChordBlockBadMember);
    }

    // ---------------------------------------------------------------- the page

    [Fact]
    public void ARowsOnlySheet_EngravesTheChange_OnItsOneLineStaff()
    {
        var staff = Staves(RowsOnly()).First(s => s.Voices[0].Measures.Length == 5
            && s.Voices[0].Measures.Any(m => m.Items.OfType<TimeSignatureChangeItem>().Any()));
        var meters = Meters(staff);
        Assert.Null(meters[1]);
        Assert.Equal("2/4", meters[2]);
        Assert.Equal("4/4", meters[3]);
        Assert.Null(meters[4]);
    }

    // Orecchiante's request 18: on a rows-only sheet the slots of the bar a row's `time` opens were
    // gridded on the score's meter — `time 3/4 E . F` was "3 chord slot(s) in a 4/4 bar" (LYS2009)
    // and F stood two thirds of the way through the bar, not on its third beat.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSlotsOfTheBarItOpens_GridOnTheRowsMeter(bool withAPart)
    {
        const string row = "C | D | time 3/4 E . F | time 4/4 G . . A |";
        string book = withAPart
            ? "time 4/4\noctave absolute\npart mel { }\nsection A {\n  chords harmony { " + row + " }\n"
              + "  mel { g'1 | g'1 | g'2. | g'1 | }\n}\nform { A }\nscore { chords harmony\n staff mel }\n"
            : RowsOnly(row);
        var tree = SyntaxTree.Parse(book);
        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Empty(collector.ChordRowGridWarnings);
        var f = Assert.Single(score.ChordNames, c => c.ChordText == "F");
        Assert.Equal(new Fraction(2, 4), f.Timing);
        var a = Assert.Single(score.ChordNames, c => c.ChordText == "A");
        Assert.Equal(new Fraction(3, 4), a.Timing);

        var v = new ChordRowGridValidator();
        v.Validate(tree);
        Assert.Empty(v.Diagnostics);
    }

    // The same table carries a section header's meter on a rows-only sheet (session 1120's rule):
    // the one-line staff's empty voice hid it too, and `E . F` under the header's 3/4 was LYS2009.
    [Fact]
    public void OnARowsOnlySheet_TheSlotsGridOnTheSectionHeadersMeter()
    {
        const string book = "time 4/4\nsection A {\n  chords harmony { C | D | }\n}\n"
            + "section B {\n  time 3/4\n  chords harmony { E . F | G | }\n}\n"
            + "form { A B }\nscore { chords harmony }\n";
        var tree = SyntaxTree.Parse(book);
        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        Assert.Empty(collector.ChordRowGridWarnings);
        var f = Assert.Single(score.ChordNames, c => c.ChordText == "F");
        Assert.Equal(new Fraction(2, 4), f.Timing);
    }

    [Fact]
    public void APartThatDoesNotWriteIt_ReadsTheRowsMeter()
    {
        var mel = Staves(Mixed).Single(s => s.Voices[0].Name == "mel");
        var meters = Meters(mel);
        Assert.Equal("2/4", meters[2]);
        Assert.Equal("4/4", meters[3]);
        Assert.Null(meters[4]);
        var v = new MeasureValidator();
        v.Validate(SyntaxTree.Parse(Mixed));
        Assert.DoesNotContain(v.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void APartThatWritesAnotherMeterThere_IsWarned_TheRowsStanding()
    {
        const string book = "time 4/4\noctave absolute\npart mel { }\nsection A {\n"
            + "  chords harmony { G | time 2/4 C | }\n  mel { g'1 | time 3/4 g'2. | }\n}\n"
            + "form { A }\nscore { chords harmony\n staff mel }\n";
        var v = new MeasureValidator();
        v.Validate(SyntaxTree.Parse(book));
        var warning = Assert.Single(v.Diagnostics, d => d.Code == DiagnosticCodes.ConflictingTimeSignatures);
        Assert.Contains("'time 2/4' there first", warning.Message);
    }

    // ---------------------------------------------------------------- sound and exports

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRow_SoundsTheBarTwoBeatsLong(bool withAPart)
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(withAPart ? Mixed : RowsOnly()));
        var starts = midi.Tracks.Where(t => t.Name == "harmony (chords)").SelectMany(t => t.Notes)
            .Select(n => n.StartTick).Distinct().OrderBy(t => t).ToArray();
        // G C | G C#ø | C#ø (2/4) | C Dsus4 | C D — the 4/4 bar after the 2/4 one starts at beat 10.
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10, 12, 14, 16 }.Select(b => b * 480), starts);
    }

    [Fact]
    public void TheMusicXml_WritesTheChange()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(RowsOnly())).ToXml();
        var beats = xml.Descendants("measure")
            .Select(m => m.Descendants("beats").Select(b => (int)b).FirstOrDefault())
            .ToArray();
        Assert.Equal(new[] { 4, 0, 2, 4, 0 }, beats);
    }

    [Fact]
    public void TheLilyPondTwin_SpellsTheMeterAsWritten()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(RowsOnly()));
        Assert.Contains("\\time 2/4 s2 |", ly);
        Assert.DoesNotContain("\\time 1/2", ly);
    }

    // ---------------------------------------------------------------- tempo, by the same rule

    private static string TempoBook(string row = "G C | G | tempo \"Slower\" 4 = 60 C | D |", bool withAPart = false) =>
        "time 4/4\ntempo 120\noctave absolute\n" + (withAPart ? "part mel { }\n" : "")
        + $"section A {{\n  chords harmony {{ {row} }}\n"
        + (withAPart ? "  mel { g'1 | g'1 | g'1 | g'1 | }\n" : "")
        + "}\nform { A }\nscore { chords harmony" + (withAPart ? "\n staff mel" : "") + " }\n";

    [Fact]
    public void ATempoAfterAChordOfItsBar_IsTheSameError()
    {
        var tree = SyntaxTree.Parse(TempoBook("G tempo 60 C | D |"));
        var error = Assert.Single(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.ChordsTimeInsideBar, error.Code);
        Assert.Equal("tempo", tree.Text.Substring(error.Span.Start, error.Span.Length));
        Assert.Equal(tree.Text, tree.GetRoot().ToFullString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATempoAtABarsHead_IsTheScoresMark_OverThatBar(bool withAPart)
    {
        var tree = SyntaxTree.Parse(TempoBook(withAPart: withAPart));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var mark = Assert.Single(score.MusicMarks, m => m.Type == MusicMarkType.Tempo && m.MeasureIndex > 0);
        Assert.Equal(2, mark.MeasureIndex);
        Assert.Equal("Slower", mark.TempoText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATempoAtABarsHead_SoundsFromThatBar(bool withAPart)
    {
        var tempos = new MidiExporter().Export(SyntaxTree.Parse(TempoBook(withAPart: withAPart)))
            .Tracks.SelectMany(t => t.TempoChanges).OrderBy(t => t.Tick)
            .Select(t => (t.Tick, Bpm: (int)Math.Round(60_000_000.0 / t.MicrosecondsPerBeat))).ToArray();
        Assert.Equal(new[] { (0, 120), (8 * 480, 60) }, tempos);
    }

    [Fact]
    public void ATempoAtABarsHead_IsInTheMusicXml_AndTheTwin()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(TempoBook())).ToXml();
        var bar3 = xml.Descendants("measure").First(m => (string)m.Attribute("number")! == "3");
        Assert.Equal("60", bar3.Descendants("sound").Single().Attribute("tempo")!.Value);
        Assert.Equal("Slower", bar3.Descendants("words").Single().Value);

        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(TempoBook()));
        Assert.Contains("\\tempo \"Slower\" 4 = 60 s1 |", ly);
    }
}
