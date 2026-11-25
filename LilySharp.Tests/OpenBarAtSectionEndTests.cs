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
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A bar left open at a section's end — music after the part's last bar line, as a bar being
/// written by hand is (owner's report 2026-11-17: a bass line typed under a chord chart, `d,4`
/// with no `|` yet) — is closed with silence: the page draws what the bar holds and the rest of
/// it empty, and the part is padded through the section and the ones after it. Until then the
/// open bar was never closed, the padding (which waits for a clean bar boundary) never ran, and
/// every later section's bars fell into it: the staff ended there and the chords of the rest of
/// the piece crowded over its last bars. A bar left open for the next section's pickup
/// (`partial`) is the one way a section starts inside a bar, and stays open for it.
/// Lab sessions/p1290/mylife.
/// </summary>
[Trait("Category", "Unit")]
public class OpenBarAtSectionEndTests
{
    /// <summary>Section B: 8 chord bars, the bass writing <paramref name="bass"/>; section C:
    /// 4 chord bars, no bass.</summary>
    private static string Book(string bass, string bRow = "D | C | F | G | D | C | F | G |") => $$"""
        time 4/4
        part bass { instrument bass }
        section B {
          chords harmony { {{bRow}} }
          bass { {{bass}} }
        }
        section C { chords harmony { A | A | B | B | } }
        form { B C }
        score { staff bass  chords harmony }
        """;

    private static Measure[] BassMeasures(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return score.EnumerateStaves().Single(t => t.Staff.PrimaryVoice.Name == "bass").Staff.PrimaryVoice.Measures.ToArray();
    }

    [Fact]
    public void AnOpenBar_ShortOfTheSection_IsClosed_AndThePartPaddedThroughTheRest()
    {
        var open = BassMeasures(Book("d4 r r r | c1 | f1 | g1 | d,4"));
        var closed = BassMeasures(Book("d4 r r r | c1 | f1 | g1 | d,4 |"));
        // Eight bars of B and four of C either way.
        Assert.Equal(12, open.Length);
        Assert.Equal(12, closed.Length);
        // Open, the bar is a whole bar: its quarter, then silence.
        Assert.Equal(new Fraction(1, 1), open[4].Items.Aggregate(Fraction.Zero, (s, it) => s + it.Duration));
    }

    [Fact]
    public void AnOpenLastBar_OfAFullSection_IsClosedWhereTheNextSectionBegins()
    {
        var bars = BassMeasures(Book("d4 r r r | c1 | f1 | g4", bRow: "D | C | F | G |"));
        Assert.Equal(8, bars.Length);
    }

    [Fact]
    public void ABarLeftOpenForTheNextSectionsPickup_StaysOpenForIt()
    {
        const string pickup = """
            time 4/4
            part bass { instrument bass }
            section B { chords harmony { D | C | F | G } bass { d1 | c1 | f1 | g2. } }
            section C { partial 4 chords harmony { A | A | B | } bass { a4 | a1 | b1 | } }
            form { B C }
            score { staff bass  chords harmony }
            """;
        Assert.Equal(6, BassMeasures(pickup).Length);
    }

    [Theory]
    [InlineData("d4 r r r | c1 | f1 | g1 | d,4", "D | C | F | G | D | C | F | G |", 12)]
    [InlineData("d4 r r r | c1 | f1 | g4", "D | C | F | G |", 8)]
    public void TheSound_PutsTheNextSectionsChordsOnItsOwnBars(string bass, string bRow, int bars)
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(Book(bass, bRow)));
        var chords = midi.Tracks.Single(t => t.Name == "harmony (chords)").Notes.Select(n => n.StartTick).Distinct().OrderBy(t => t).ToArray();
        int bar = 4 * midi.TicksPerQuarterNote;
        Assert.Equal(Enumerable.Range(0, bars).Select(i => i * bar), chords);
    }

    /// <summary>A section's last bar left open stands its bar line where it is closed, as the page
    /// draws one there: the metronome accents it, the bars after it keep their printed numbers
    /// (<see cref="MidiNote.PrintedBar"/>, what the preview's A-B repeat picks its bars by), and a
    /// recording lined up bar by bar gives it its bar. Owner's report 2026-11-25 (今宵の月のように,
    /// Lab sessions/p1382): a bass line ending `… | a, d` with no `|` stood no line there — every
    /// later bar was numbered one less, and A-B on bar 57 sounded bar 58.</summary>
    [Theory]
    [InlineData("d1 | c1 | f1 | g1")]     // the last bar full
    [InlineData("d1 | c1 | f1 | g4")]     // …and short, closed with silence
    public void AnOpenLastBar_StandsItsBarLine(string bass)
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(Book(bass, bRow: "D | C | F | G |")));
        int bar = 4 * midi.TicksPerQuarterNote;
        Assert.Equal(Enumerable.Range(1, 8).Select(i => i * bar), midi.BarTicks.Where(t => t > 0));
        var c = midi.Tracks.Single(t => t.Name == "harmony (chords)").Notes.Where(n => n.StartTick >= 4 * bar);
        Assert.Equal(new[] { 4, 5, 6, 7 }, c.Select(n => n.PrintedBar).Distinct().OrderBy(b => b));
    }

    /// <summary>A bar its `|` closes SHORT (the bar line is the bar, LYS2001) is that short under the
    /// chord row too: the page draws the row's D over the bar's one beat and the next chords after
    /// it, and the sound plays them so. Until 2026-11-17 the row's bar stayed a whole one — D for a
    /// bar, every later chord of the section a bar and three beats late, its last one cut
    /// (Lab sessions/p1290/mylife/m2).</summary>
    [Fact]
    public void ABarClosedShort_IsThatShortUnderTheChordRow_InTheSound()
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(Book("d4 r r r | c1 | f1 | g1 | d,4 |")));
        var notes = midi.Tracks.Single(t => t.Name == "harmony (chords)").Notes;
        int q = midi.TicksPerQuarterNote, bar = 4 * q;
        var onsets = notes.Select(n => n.StartTick).Distinct().OrderBy(t => t).ToArray();
        // B: four whole bars, the short one (a beat), three whole; then C's four.
        int shortAt = 4 * bar, after = shortAt + q;
        Assert.Equal(new[] { 0, bar, 2 * bar, 3 * bar, shortAt, after, after + bar, after + 2 * bar,
            after + 3 * bar, after + 4 * bar, after + 5 * bar, after + 6 * bar }, onsets);
        // The short bar's D lasts its beat.
        Assert.All(notes.Where(n => n.StartTick == shortAt), n => Assert.Equal(q, n.DurationTicks));
    }

    /// <summary>The same short bar under a chord track written BY PART (`chords h { section B { … } }`),
    /// in the sound and the MusicXML: the row's bar there is the music's beat, its two chords half of
    /// it each (Lab sessions/p1290/mylife/m7).</summary>
    [Fact]
    public void ABarClosedShort_UnderAChordTrackWrittenByPart_IsThatShort()
    {
        const string book = """
            time 4/4
            part bass { instrument bass }
            chords harmony {
              section B { D | C | F | G | D A | C | F | G | }
              section C { A | A | B | B | }
            }
            section B { bass { d4 r r r | c1 | f1 | g1 | d,4 | } }
            section C { bass { a1 | a1 | b1 | b1 | } }
            form { B C }
            score { staff bass  chords harmony }
            """;
        var midi = new MidiExporter().Export(SyntaxTree.Parse(book));
        int q = midi.TicksPerQuarterNote, bar = 4 * q, shortAt = 4 * bar, after = shortAt + q;
        var onsets = midi.Tracks.Single(t => t.Name == "harmony (chords)").Notes.Select(n => n.StartTick).Distinct().OrderBy(t => t).ToArray();
        Assert.Equal(new[] { 0, bar, 2 * bar, 3 * bar, shortAt, shortAt + q / 2, after, after + bar, after + 2 * bar,
            after + 3 * bar, after + 4 * bar, after + 5 * bar, after + 6 * bar }, onsets);
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().ToString();
        var fifth = System.Text.RegularExpressions.Regex.Match(xml, @"(?s)<measure number=""5""[^>]*>.*?</measure>").Value;
        int divisions = int.Parse(System.Text.RegularExpressions.Regex.Match(xml, @"<divisions>(\d+)</divisions>").Groups[1].Value);
        Assert.Contains($"<offset>{divisions / 2}</offset>", fifth);
    }

    /// <summary>A short bar ANOTHER part writes whole — here as an empty bar, which Lily# reads as a
    /// whole bar (LilyPond has none) — is as long as the longest (owner's decision 2026-11-17): the
    /// sound plays it whole, the twin fills the short voice out (`d,4 s2.`) with no `\partial`, the
    /// MusicXML forwards it to the bar's end, and the check says so on the whole bar. Until then the
    /// twin wrote `\partial 4` for the bass and `s1` for the guitar — "bar check failed at: 3/4"
    /// (Lab sessions/p1290/mylife/m9). A part that writes nothing there decides nothing (m8).</summary>
    [Fact]
    public void AShortBarAnotherPartWritesWhole_IsAWholeBar()
    {
        const string book = """
            time 4/4
            part bass { instrument bass }
            part gt { instrument guitar }
            section B { chords harmony { D | C | F | G | D | } bass { d4 r r r | c1 | f1 | g1 | d,4 | } gt { | | | | | } }
            section C { chords harmony { A | B | } bass { a1 | b1 | } gt { a1 | b1 | } }
            form { B C }
            score { staff bass  staff gt  chords harmony }
            """;
        var ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(book));
        Assert.Contains("d,4 s2. |", ly);
        Assert.DoesNotContain("\\partial", ly);
        var midi = new MidiExporter().Export(SyntaxTree.Parse(book));
        int bar = 4 * midi.TicksPerQuarterNote;
        Assert.Equal(5 * bar, midi.Tracks.Single(t => t.Name == "gt").Notes.Min(n => n.StartTick));
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().ToString();
        int divisions = int.Parse(System.Text.RegularExpressions.Regex.Match(xml, @"<divisions>(\d+)</divisions>").Groups[1].Value);
        var bassFifth = System.Text.RegularExpressions.Regex.Match(xml, @"(?s)<part id=""P1"">.*?<measure number=""5""[^>]*>(.*?)</measure>").Groups[1].Value;
        Assert.Matches($@"<forward>\s*<duration>{3 * divisions}</duration>", bassFifth);
        var validator = new LilySharp.Core.Semantics.MeasureValidator();
        validator.Validate(SyntaxTree.Parse(book));
        Assert.Contains(validator.Diagnostics, d => d.Code == LilySharp.Core.Syntax.DiagnosticCodes.MeasureDurationMismatch
            && d.Message.Contains("is an empty bar, a whole bar") && d.Message.Contains("'gt'"));

        var silent = book.Replace("gt { | | | | | } ", "");
        var lySilent = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(silent));
        Assert.Contains("\\partial 4 d,4 |", lySilent);
    }

    /// <summary>The LilyPond twin of the same two shapes. LilyPond's `|` is a check, so a bar its `|`
    /// closes short is said to be short before its first note — `\partial 4`, which LilyPond 2.26.0
    /// takes mid-piece — and the chord row's bar there is that beat (`d4`); a bar left open is a
    /// whole bar, silent after its music (`d,4 s2.`). Until 2026-11-17 both went out as `d,4 |`:
    /// "bar check failed at: 1/4", every later bar three beats off (Lab sessions/p1290/mylife).</summary>
    [Fact]
    public void TheTwin_SaysABarClosedShort_AndFillsABarLeftOpen()
    {
        var shortBar = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book("d4 r r r | c1 | f1 | g1 | d,4 |")));
        Assert.Contains("\\partial 4 d,4 |", shortBar);
        var chords = System.Text.RegularExpressions.Regex.Match(shortBar, @"harmonyChords = \\chordmode \{([^}]*)\}").Groups[1].Value
            .Split('|').Select(b => b.Trim()).Where(b => b.Length > 0).ToArray();
        Assert.Equal("d4", chords[4]);
        Assert.Equal("c1", chords[5]);
        var openBar = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book("d4 r r r | c1 | f1 | g1 | d,4")));
        Assert.Contains("d,4 s2. |", openBar);
        Assert.DoesNotContain("\\partial", openBar);
    }

    /// <summary>The twin of the two other shapes: a full section's last bar left open is a whole
    /// bar where a section with no pickup follows (`g4 s2.`, as the page closes it at the next
    /// section's head); and where the next section's pickup completes a short last bar, the chord
    /// row's bar there is as short as the music's (`g2.`, then the pickup's chord). Until 2026-11-17
    /// both failed LilyPond's bar check (Lab sessions/p1290/mylife/m3, m4).</summary>
    [Fact]
    public void TheTwin_ClosesAnOpenLastBar_AndShortensTheRowsBarBeforeAPickup()
    {
        var open = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book("d4 r r r | c1 | f1 | g4", bRow: "D | C | F | G |")));
        Assert.Contains("g4 s2.", open);
        var pickup = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse("""
            time 4/4
            part bass { instrument bass }
            section B { chords harmony { D | C | F | G } bass { d1 | c1 | f1 | g2. } }
            section C { partial 4 chords harmony { A | A | B | } bass { a4 | a1 | b1 | } }
            form { B C }
            score { staff bass  chords harmony }
            """));
        var chords = System.Text.RegularExpressions.Regex.Match(pickup, @"harmonyChords = \\chordmode \{([^}]*)\}").Groups[1].Value;
        Assert.Contains("g2. a4 |", System.Text.RegularExpressions.Regex.Replace(chords, @"\s+", " "));
        // (the open bar the pickup completes is left open: no spacer)
        Assert.DoesNotContain("g2. s", pickup);
    }
}
