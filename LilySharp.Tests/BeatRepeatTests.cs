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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Tests;

/// <summary>
/// The beat repeat (owner's decision 2026-11-19, Calamo's request 27; docs/percent-repeat-design.md
/// §5): a figure shorter than a bar written in braces, then one <c>%</c> a repetition, inside the
/// bar — <c>c4 { d8 e } % %</c>. Each mark is as long as the figure, is drawn as LilyPond's
/// beat slash and sounds the figure again. Braces no mark follows are LYS1074; a <c>%N</c> after
/// a figure is LYS1048.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BeatRepeatTests
{
    private static string Book(string music, string time = "4/4") => $$"""
        octave absolute
        time {{time}}
        part m { clef treble }
        section A { m { {{music}} } }
        form { A }
        score { staff m }
        """;

    private static IReadOnlyList<Diagnostic> Diagnose(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
    }

    private static List<(int Pitch, int Start, int Length)> Notes(string music, string time = "4/4") =>
        new MidiExporter().Export(SyntaxTree.Parse(Book(music, time))).Tracks
            .SelectMany(t => t.Notes).Select(n => (Pitch: n.Pitch, Start: n.StartTick, Length: n.DurationTicks))
            .OrderBy(n => n.Start).ThenBy(n => n.Pitch).ToList();

    // --- the tree ---

    [Fact]
    public void AFigureAndTheMarksAfterIt_AreOneNode_AndTheTextIsKept()
    {
        string src = Book("c'4 { d'8 e' } % % |");
        var tree = SyntaxTree.Parse(src);
        Assert.Equal(src, tree.GetRoot().ToFullString());
        var beat = Assert.Single(tree.GetRoot().DescendantNodes().OfType<BeatRepeatSyntax>());
        Assert.Equal(3, beat.Count);
        Assert.Equal(2, beat.Marks.Count());
        Assert.Equal("d'8 e'", string.Join(" ", beat.Figure.Items.Select(i => i.ToFullString().Trim())));
        // The marks are the figure's, not a bar's: no percent repeat of bars is made of them.
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<PercentRepeatSyntax>());
        Assert.Empty(Diagnose(src));
    }

    [Fact]
    public void ANoteWrittenRightBeforeTheFigure_StaysANote()
    {
        // `c4 {` was a name that reads as music (LYS0045: `a1 { … }` in a section is a part's
        // cell) — before a figure it is the note it looks like.
        string src = Book("c4 { d8 e } % % |");
        Assert.Empty(Diagnose(src));
        Assert.Single(SyntaxTree.Parse(src).GetRoot().DescendantNodes().OfType<BeatRepeatSyntax>());
        // …and with no mark after the braces it is the name it was.
        Assert.Contains(Diagnose("time 4/4\npart m { clef treble }\nsection A { a1 { c4 d e f | } }\nform { A }\nscore { staff m }"),
            d => d.Code == DiagnosticCodes.NameReadsAsMusic);
    }

    // --- what it sounds ---

    [Theory]
    [InlineData("c'4 { d'8 e' } % % |", "c'4 d'8 e' d'8 e' d'8 e' |", "4/4")]
    [InlineData("{ c'16 d' e' f' } % % % |", "c'16 d' e' f' c'16 d' e' f' c'16 d' e' f' c'16 d' e' f' |", "4/4")]
    [InlineData("fis16 { <fis b>64 dis' <fis b> dis' } % % % % r4. |", "fis16 <fis b>64 dis' <fis b> dis' <fis b>64 dis' <fis b> dis' <fis b>64 dis' <fis b> dis' <fis b>64 dis' <fis b> dis' <fis b>64 dis' <fis b> dis' r4. |", "6/8")]
    public void EachMark_SoundsTheFigureAgain_InsideTheBar(string written, string writtenOut, string time)
    {
        Assert.Empty(Diagnose(Book(written, time)));
        Assert.Equal(Notes(writtenOut, time), Notes(written, time));
    }

    [Fact]
    public void ARepetition_StartsFromTheFrameTheFigureOpenedIn()
    {
        // Relative octaves: the figure rises a fifth, and every copy starts where the first did.
        const string head = "time 4/4\npart m { clef treble }\nsection A { m { ";
        const string tail = " } }\nform { A }\nscore { staff m }";
        static List<int> Pitches(string src) => new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).Select(n => n.Pitch).ToList();
        var pitches = Pitches(head + "{ c'8 g' } % % % |" + tail);
        Assert.Equal(8, pitches.Count);
        Assert.Equal(pitches.Take(2), pitches.Skip(2).Take(2));
        Assert.Equal(pitches.Take(2), pitches.Skip(6).Take(2));
    }

    // --- what the page draws ---

    [Theory]
    [InlineData("c'4 { d'8 e' } % % |", 2, 1)]                 // eighths: one slash
    [InlineData("{ c'16 d' e' f' } % % % |", 3, 2)]               // sixteenths: two
    [InlineData("c'4 { d'8. e'16 } % % |", 2, 0)]              // unequal values: the double slash
    public void EachMark_IsABeatSlashInItsBar(string music, int marks, int slashes)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(Book(music)));
        Assert.Single(score.Voice.Measures);
        Assert.Equal(marks, score.PercentRepeats.Length);
        Assert.All(score.PercentRepeats, sign =>
        {
            Assert.True(sign.IsBeatSlash);
            Assert.Equal(0, sign.MeasureIndex);
            Assert.Equal(slashes, sign.SlashCount);
        });
        // LILYPOND-REF: scm/music-functions.scm:378-390 calc-repeat-slash-count — the count is
        //   the body's beams when its durations are equal, 0 (the double slash) when they are not.
    }

    // --- the exports ---

    [Fact]
    public void TheLilyPondTwin_WritesRepeatPercentOverTheFigure()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book("c'4 { d'8 e' } % % |")));
        Assert.Contains(@"\repeat percent 3 { d'8 e' }", ly);
    }

    [Fact]
    public void MusicXml_WritesTheNotesOfEveryRepetition()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book("c'4 { d'8 e' } % % |"))).ToXml().ToString();
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(xml, "<pitch>").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xml, "<measure "));
    }

    // --- what is not one ---

    [Fact]
    public void BracesNoMarkFollows_AreReported_AndReadAsTheirMusicOnce()
    {
        string src = Book("c'4 { d'8 e' } f'2 |");
        var d = Assert.Single(Diagnose(src), d => d.Code == DiagnosticCodes.BeatRepeatMisspelled);
        Assert.Equal('{', src[d.Span.Start]);
        Assert.Contains("'%'", d.Message);
        Assert.True(SyntaxTree.Parse(src).HasErrors);       // a parse error: the file does not compile
        Assert.Equal(Notes("c'4 d'8 e' f'2 |"), Notes("c'4 { d'8 e' } f'2 |"));
        Assert.Empty(SyntaxTree.Parse(src).GetRoot().DescendantNodes().OfType<BeatRepeatSyntax>());
    }

    [Fact]
    public void ACountedMarkAfterAFigure_IsNoBeatRepeat()
    {
        var diagnostics = Diagnose(Book("c'4 { d'8 e' } %2 r2 |"));
        var d = Assert.Single(diagnostics, d => d.Code == DiagnosticCodes.PercentMarkUngrouped);
        Assert.Contains("counts bars", d.Message);
    }

    [Fact]
    public void APlainMarkInsideABar_WithNoFigureBeforeIt_IsStillNoRepeat()
    {
        Assert.Contains(Diagnose(Book("c'1 | c'4 % r2 |")), d => d.Code == DiagnosticCodes.PercentMarkUngrouped);
    }

    [Fact]
    public void TheBarsMarkStillRepeatsABarThatHoldsAFigure()
    {
        string src = Book("c'4 { d'8 e' } % % | % |");
        Assert.Empty(Diagnose(src));
        Assert.Single(SyntaxTree.Parse(src).GetRoot().DescendantNodes().OfType<PercentRepeatSyntax>());
        Assert.Equal(14, Notes("c'4 { d'8 e' } % % | % |").Count);
    }
}
