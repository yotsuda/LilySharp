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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>chords { }</c> row the score places sounds in the MIDI (owner decision 2026-09-25):
/// the WINDOW voicing (every tone in G3..F#4, a slash bass an octave below — an inverted
/// tone once, there alone), on the row's
/// own track at 70% velocity, each symbol over exactly the span the page prints it, struck
/// again at every written symbol. A row the score does not place sounds nothing.
/// </summary>
public class ChordRowMidiTests
{
    private const int Bar = 1920; // 4/4 at 480 ticks a quarter

    private static MidiFile Export(string lys) => new MidiExporter().Export(SyntaxTree.Parse(lys));

    private static (int Start, int Length, int Pitch)[] ChordNotes(MidiFile midi, string row = "harmony")
        => midi.Tracks.Where(t => t.Name == row + " (chords)")
            .SelectMany(t => t.Notes)
            .Select(n => (n.StartTick, n.DurationTicks, n.Pitch))
            .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .ToArray();

    [Fact]
    public void ASectionRow_SoundsTheWindowVoicing_OnThePagesGrid()
    {
        var midi = Export("""
            time 4/4
            part melody
            section A { melody { c''4 d' e' f' | g'1 | } chords harmony { C . G7 . | Dm/F | } }
            form { A }
            score { chords harmony  staff melody }
            """);
        Assert.Equal(new[]
        {
            (0, 960, 55), (0, 960, 60), (0, 960, 64),                          // C: G3 C4 E4
            (960, 960, 55), (960, 960, 59), (960, 960, 62), (960, 960, 65),    // G7: G3 B3 D4 F4
            // Dm/F: F3 | A3 D4 — the inverted F sounds once, below (session 967; LilyPond's
            // process-inversion). Until then F4 sounded again inside the window.
            (Bar, Bar, 53), (Bar, Bar, 57), (Bar, Bar, 62),
        }, ChordNotes(midi));
        var track = midi.Tracks.Single(t => t.Name == "harmony (chords)");
        Assert.All(track.Notes, n => Assert.Equal(63, n.Velocity));
        Assert.NotEqual(midi.Tracks.Single(t => t.Name == "melody").Channel, track.Channel);
    }

    [Theory]
    [InlineData("chords harmony { C | G7 | }\nsection A { melody { c''1 | g'1 | } }\n")]   // before the section
    [InlineData("section A { melody { c''1 | g'1 | } }\nchords harmony { C | G7 | }\n")]   // after it
    public void AFlatTopLevelRow_SoundsFromBarOne_AsThePageDrawsIt_AndTakesNoTime(string music)
    {
        var midi = Export($"time 4/4\npart melody\n{music}form {{ A }}\nscore {{ chords harmony  staff melody }}\n");
        Assert.Equal(new[] { 0, Bar }, midi.Tracks.Single(t => t.Name == "melody").Notes.Select(n => n.StartTick));
        Assert.Equal(new[]
        {
            (0, Bar, 55), (0, Bar, 60), (0, Bar, 64),                                  // C
            (Bar, Bar, 55), (Bar, Bar, 59), (Bar, Bar, 62), (Bar, Bar, 65),            // G7
        }, ChordNotes(midi));
        Assert.Equal(new[] { Bar, 2 * Bar }, midi.BarTicks.Where(b => b > 0).Distinct().OrderBy(b => b));
    }

    private static int[] Onsets(string music, string form = "A", string head = "")
    {
        var midi = Export($"time 4/4\n{head}part melody\n{music}\nform {{ {form} }}\nscore {{ chords harmony  staff melody }}\n");
        return ChordNotes(midi).Select(n => n.Start).Distinct().ToArray();
    }

    // What a flat row sounds is what the same row sounds written in its sections.
    private static (int, int, int)[] Sounds(string music, string form)
    {
        string book = $"time 4/4\npart melody\n{music}\nform {{ {form} }}\nscore {{ chords harmony  staff melody }}\n";
        Assert.False(SyntaxTree.Parse(book).HasErrors, book);
        return ChordNotes(Export(book));
    }

    [Theory]
    // Each section its own bars, a section that returns taking the bars after.
    [InlineData("A B", "section A { melody { c''1 | } }\nsection B { melody { d''1 | e''1 | } }", "C | G | Am |", "section A { melody { c''1 | } chords harmony { C | } }\nsection B { melody { d''1 | e''1 | } chords harmony { G | Am | } }")]
    // A pickup: the row's first bar is the pickup bar, as on the page.
    [InlineData("A", "section A { partial 4 melody { g'4 | c''1 | d''1 | } }", "| C | G |", "section A { partial 4 melody { g'4 | c''1 | d''1 | } chords harmony { | C | G | } }")]
    // …and the section after it takes the bars after, in its own key: the opening bar line closed the pickup's.
    [InlineData("A B", "section A { key c major partial 4 melody { g'4 | c''1 | } }\nsection B { key d major melody { d''1 | } }", "| I | I |", "section A { key c major partial 4 melody { g'4 | c''1 | } chords harmony { | I | } }\nsection B { key d major melody { d''1 | } chords harmony { I | } }")]
    // A section's own meter, its slots on that bar's grid.
    [InlineData("A B", "section A { melody { c''1 | } }\nsection B { time 3/4 melody { d''2. | e''2. | } }", "C D | G . A | Am . . |", "section A { melody { c''1 | } chords harmony { C D | } }\nsection B { time 3/4 melody { d''2. | e''2. | } chords harmony { G . A | Am . . | } }")]
    // A degree, read in the key of the section it falls in.
    [InlineData("A B", "section A { key c major melody { c''1 | } }\nsection B { key d major melody { d''1 | } }", "I | I |", "section A { key c major melody { c''1 | } chords harmony { I | } }\nsection B { key d major melody { d''1 | } chords harmony { I | } }")]
    // A repeat is printed once: its bars sound on every pass.
    [InlineData("|: A :| B", "section A { melody { c''1 | } }\nsection B { melody { d''1 | } }", "C | G |", "section A { melody { c''1 | } chords harmony { C | } }\nsection B { melody { d''1 | } chords harmony { G | } }")]
    // Endings are printed one after the other, the second after the first.
    [InlineData("|: A [1. B] :| [2. C] D", "section A { melody { c''1 | } }\nsection B { melody { d''1 | } }\nsection C { melody { e''1 | } }\nsection D { melody { f''1 | } }", "C | Dm | Em | F |", "section A { melody { c''1 | } chords harmony { C | } }\nsection B { melody { d''1 | } chords harmony { Dm | } }\nsection C { melody { e''1 | } chords harmony { Em | } }\nsection D { melody { f''1 | } chords harmony { F | } }")]
    // Endings written out of the order they play: what follows comes after both as printed.
    [InlineData("|: A [2. B] :| [1. C] D", "section A { melody { c''1 | } }\nsection B { melody { d''1 | } }\nsection C { melody { e''1 | } }\nsection D { melody { f''1 | } }", "C | Dm | Em | F |", "section A { melody { c''1 | } chords harmony { C | } }\nsection B { melody { d''1 | } chords harmony { Dm | } }\nsection C { melody { e''1 | } chords harmony { Em | } }\nsection D { melody { f''1 | } chords harmony { F | } }")]
    // A section written twice in the form is printed twice: two sets of bars.
    [InlineData("A A", "section A { melody { c''1 | } }", "C | G |", null)]
    public void AFlatTopLevelRow_SoundsAsItsBarsWrittenInTheSections(string form, string sections, string row, string? same)
    {
        var flat = Sounds($"{sections}\nchords harmony {{ {row} }}", form);
        Assert.NotEmpty(flat);
        if (same != null)
            Assert.Equal(Sounds(same, form), flat);
        else
            Assert.Equal(new[] { 0, Bar }, flat.Select(n => n.Item1).Distinct());
    }

    /// <summary>
    /// A row's bar sounds on THE METER OF THAT BAR (SectionMeterPlan.MeterOfBar — the one
    /// answer the page's grid, the MusicXML and the LilyPond twin read too): a <c>time</c>
    /// written inside the section's music moves the bar's length and its slot grid from
    /// there, for a flat track and for a row written in the section alike. Until 2026-11-07
    /// both took the meter the section opened with: under <c>time 3/4</c> the row's bars ran a
    /// beat long and <c>G . A</c> put A at beat 3⅔ (Lab sessions/p1120/probe/m-flat).
    /// </summary>
    [Theory]
    [InlineData("section A { melody { c''1 | time 3/4 d''4 d''4 d''4 | e''2. | } }\nchords harmony { C | G . A | Am . . | }")]
    [InlineData("section A { melody { c''1 | time 3/4 d''4 d''4 d''4 | e''2. | } chords harmony { C | G . A | Am . . | } }")]
    public void ARow_UnderATimeWrittenInTheMusic_SoundsOnThatBarsMeter(string music)
    {
        var midi = Export($"time 4/4\npart melody\n{music}\nform {{ A }}\nscore {{ chords harmony  staff melody }}\n");
        // C at bar 1; G at bar 2 (a 3/4 bar from here); A on its beat 3; Am at bar 3.
        Assert.Equal(new[] { 0, Bar, Bar + 2 * 480, Bar + 3 * 480 }, ChordNotes(midi).Select(n => n.Start).Distinct().ToArray());
    }

    [Theory]
    [InlineData("score { chords harmony  staff melody }", "melody")]
    [InlineData("score { chords harmony }", "harmony (chords)")]
    public void ARow_UnderATimeWrittenInTheMusic_IsInTheMusicXml_OnThatBarsMeter(string score, string partName)
    {
        var xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(
            "time 4/4\npart melody\nsection A { melody { c''1 | time 3/4 d''4 d''4 d''4 | e''2. | } }\n"
            + $"chords harmony {{ C | G . A | Am . . | }}\nform {{ A }}\n{score}\n")).ToXml();
        var part = xml.Descendants("part").Single(p =>
            xml.Descendants("score-part").Single(sp => (string)sp.Attribute("id")! == (string)p.Attribute("id")!)
               .Element("part-name")!.Value == partName);
        int divisions = (int)part.Descendants("divisions").First();
        var bar2 = part.Elements("measure").Single(m => (string)m.Attribute("number")! == "2");
        // The bar is 3/4 (the lead sheet's own part writes the change too) and A stands on beat 3.
        Assert.Equal("3", bar2.Descendants("beats").Single().Value);
        var a = bar2.Elements("harmony").Single(h => h.Element("root")!.Element("root-step")!.Value == "A");
        Assert.Equal(2 * divisions, (int)a.Element("offset")!);
    }

    [Fact]
    public void AFlatTopLevelRow_OnADaCapo_ReadsItsBarsAgain()
    {
        string music = "section A { melody { c''1 | } }\nsection B { melody { d''1 | } }\nchords harmony { C | G | }";
        var midi = Export($"time 4/4\npart melody\n{music}\nform {{ A B dc }}\nscore {{ chords harmony  staff melody }}\n");
        var melody = midi.Tracks.Single(t => t.Name == "melody").Notes.Select(n => n.StartTick).ToArray();
        var chords = ChordNotes(midi).Select(n => n.Start).Distinct().ToArray();
        Assert.Equal(melody, chords);
        Assert.True(melody.Length > 2, "the D.C. replays");
    }

    [Theory]
    [InlineData("A B", "section A { melody { c''1 | } }\nsection B { time 3/4 melody { d''2. | } }", "C | G . A |", "section A { melody { c''1 | } chords harmony { C | } }\nsection B { time 3/4 melody { d''2. | } chords harmony { G . A | } }")]
    [InlineData("|: A [1. B] :| [2. C]", "section A { melody { c''1 | } }\nsection B { melody { d''1 | } }\nsection C { melody { e''1 | } }", "C | Dm | Em |", "section A { melody { c''1 | } chords harmony { C | } }\nsection B { melody { d''1 | } chords harmony { Dm | } }\nsection C { melody { e''1 | } chords harmony { Em | } }")]
    public void AFlatTopLevelRow_IsInTheMusicXml_AsItsBarsWrittenInTheSections(string form, string sections, string row, string same)
    {
        static string Xml(string music, string form) => new LilySharp.Core.MusicXml.MusicXmlExporter()
            .Export(SyntaxTree.Parse($"time 4/4\npart melody\n{music}\nform {{ {form} }}\nscore {{ chords harmony  staff melody }}\n")).ToXml().ToString();
        string flat = Xml($"{sections}\nchords harmony {{ {row} }}", form);
        Assert.Contains("<harmony", flat);
        Assert.Equal(Xml(same, form), flat);
    }

    [Theory]
    // A pickup: the first bar as short as the music's.
    [InlineData("A", "section A { partial 4 melody { g'4 | c''1 | d''1 | } }", "| C | G |", "section A { partial 4 melody { g'4 | c''1 | d''1 | } chords harmony { | C | G | } }")]
    // …and the next section's bars in its own key.
    [InlineData("A B", "section A { key c major partial 4 melody { g'4 | c''1 | } }\nsection B { key d major melody { d''1 | } }", "| I | I |", "section A { key c major partial 4 melody { g'4 | c''1 | } chords harmony { | I | } }\nsection B { key d major melody { d''1 | } chords harmony { I | } }")]
    // A section's own meter.
    [InlineData("A B", "section A { melody { c''1 | } }\nsection B { time 3/4 melody { d''2. | e''2. | } }", "C D | G . A | Am . . |", "section A { melody { c''1 | } chords harmony { C D | } }\nsection B { time 3/4 melody { d''2. | e''2. | } chords harmony { G . A | Am . . | } }")]
    // A repeat and its endings, in the chord variable as in the music's.
    [InlineData("|: A [1. B] :| [2. C] D", "section A { melody { c''1 | } }\nsection B { melody { d''1 | } }\nsection C { melody { e''1 | } }\nsection D { melody { f''1 | } }", "C | Dm | Em | F |", "section A { melody { c''1 | } chords harmony { C | } }\nsection B { melody { d''1 | } chords harmony { Dm | } }\nsection C { melody { e''1 | } chords harmony { Em | } }\nsection D { melody { f''1 | } chords harmony { F | } }")]
    public void AFlatTopLevelRow_IsInTheLilyPondTwin_AsItsBarsWrittenInTheSections(string form, string sections, string row, string same)
    {
        static string Ly(string music, string form) => new LilySharp.Core.LilyPond.LilyPondExporter()
            .Export(SyntaxTree.Parse($"time 4/4\npart melody\n{music}\nform {{ {form} }}\nscore {{ chords harmony  staff melody }}\n"));
        string flat = Ly($"{sections}\nchords harmony {{ {row} }}", form);
        Assert.Contains("\\chordmode", flat);
        Assert.Equal(Ly(same, form), flat);
    }

    /// <summary>
    /// With NO form, the twin plays every declared section once in declaration order (as the
    /// music stream and the page do) and a flat track gives each the bars it prints — its
    /// pickup, its meter, its key. Until 2026-11-07 the flat bars were written whole at the
    /// head: <c>s1 | c1 | g1*2/3 a1*1/3 |</c> for a one-beat pickup and a 3/4 section.
    /// </summary>
    [Theory]
    [InlineData("section A { partial 4 melody { g'4 | c''1 | } }\nsection B { time 3/4 melody { d''2. | } }", "| C | G . A |",
        "section A { partial 4 melody { g'4 | c''1 | } chords harmony { | C | } }\nsection B { time 3/4 melody { d''2. | } chords harmony { G . A | } }")]
    [InlineData("section A { key d major melody { d''1 | } }\nsection B { melody { c''1 | time 3/4 d''2. | } }", "I | C | G . A |",
        "section A { key d major melody { d''1 | } chords harmony { I | } }\nsection B { melody { c''1 | time 3/4 d''2. | } chords harmony { C | G . A | } }")]
    public void AFlatTopLevelRow_WithNoForm_IsInTheLilyPondTwin_AsItsBarsWrittenInTheSections(string sections, string row, string same)
    {
        static string Ly(string music) => new LilySharp.Core.LilyPond.LilyPondExporter()
            .Export(SyntaxTree.Parse($"time 4/4\npart melody\n{music}\nscore {{ chords harmony  staff melody }}\n"));
        string flat = Ly($"{sections}\nchords harmony {{ {row} }}");
        Assert.Contains("\\chordmode", flat);
        Assert.Equal(Ly(same), flat);
    }

    [Fact]
    public void AFlatTopLevelRow_InTheTwin_GoesOnPastTheMusic_AndFallsSilentWhenItRunsOut()
    {
        static string Ly(string row) => new LilySharp.Core.LilyPond.LilyPondExporter()
            .Export(SyntaxTree.Parse($"time 4/4\npart melody\nsection A {{ melody {{ c''1 | d''1 | }} }}\nchords harmony {{ {row} }}\nform {{ A }}\nscore {{ chords harmony  staff melody }}\n"))
            .ReplaceLineEndings("\n");
        Assert.Matches(@"c1 \|\s+g1 \|\s+a1:m \|\s+\}", Ly("C | G | Am |"));
        Assert.Matches(@"c1 \|\s+s1 \|\s+\}", Ly("C |"));
    }

    [Fact]
    public void AFlatTopLevelRow_PastTheMusic_GoesOn_AsThePageDrawsIt()
        => Assert.Equal(new[] { 0, Bar, 2 * Bar }, Onsets("section A { melody { c''1 | } }\nchords harmony { C | G | Am | }"));

    [Fact]
    public void AFlatTopLevelRow_WithNoMusicAtAll_Sounds()
    {
        var midi = Export("time 4/4\nchords harmony { C | G | }\nscore { chords harmony }\n");
        Assert.Equal(new[] { 0, Bar }, ChordNotes(midi).Select(n => n.Start).Distinct());
    }

    [Fact]
    public void AFlatTopLevelRowTheScoreDoesNotPlace_IsSilent_AndTakesNoTime()
    {
        var midi = Export("time 4/4\npart melody\nchords harmony { C | G7 | }\nsection A { melody { c''1 | g'1 | } }\nform { A }\nscore { staff melody }\n");
        Assert.Equal(new[] { 0, Bar }, midi.Tracks.Single(t => t.Name == "melody").Notes.Select(n => n.StartTick));
        Assert.Empty(ChordNotes(midi));
    }

    [Fact]
    public void ARowTheScoreDoesNotPlace_IsSilent()
    {
        var midi = Export("""
            time 4/4
            part melody
            section A { melody { c''4 d' e' f' | } chords harmony { C | } }
            form { A }
            score { staff melody }
            """);
        Assert.Empty(ChordNotes(midi));
        Assert.Equal(4, midi.Tracks.Sum(t => t.Notes.Count));
    }

    [Fact]
    public void AGroupedByPartTrack_SoundsAtEverySectionItsFormPlays()
    {
        var midi = Export("""
            time 4/4
            part melody
            section A { melody { c''1 | } }
            section B { melody { g'1 | } }
            chords harmony { section A { C | } section B { G | } }
            form { A B A }
            score { chords harmony  staff melody }
            """);
        Assert.Equal(new[]
        {
            (0, Bar, 55), (0, Bar, 60), (0, Bar, 64),                          // C
            (Bar, Bar, 55), (Bar, Bar, 59), (Bar, Bar, 62),                    // G: G3 B3 D4
            (2 * Bar, Bar, 55), (2 * Bar, Bar, 60), (2 * Bar, Bar, 64),        // C again
        }, ChordNotes(midi));
    }

    [Fact]
    public void ARomanEntry_ReadsTheKeyInForce()
    {
        var midi = Export("""
            time 4/4
            key g major
            part melody
            section A { melody { g'1 | d''1 | } chords harmony { I | V | } }
            form { A }
            score { chords harmony  staff melody }
            """);
        Assert.Equal(new[]
        {
            (0, Bar, 55), (0, Bar, 59), (0, Bar, 62),                          // G: G3 B3 D4
            (Bar, Bar, 57), (Bar, Bar, 62), (Bar, Bar, 66),                    // D: A3 D4 F#4
        }, ChordNotes(midi));
    }

    [Fact]
    public void AChordsOnlySection_TakesItsBars()
    {
        var midi = Export("""
            time 4/4
            part melody
            section Intro { chords harmony { C | F | } }
            section A { melody { c''1 | } }
            form { Intro A }
            score { chords harmony  staff melody }
            """);
        var melody = midi.Tracks.Single(t => t.Name == "melody").Notes.Single();
        Assert.Equal(2 * Bar, melody.StartTick);
        Assert.Equal(new[] { 0, Bar }, ChordNotes(midi).Select(n => n.Start).Distinct().ToArray());
    }
}
