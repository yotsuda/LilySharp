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
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The exporters' timing and memory, where each was measured to part from the page
/// (HANDOFF §2 R14, session 398): a tie written after a rest, a tempo whose beat is not the
/// crotchet, a grace at the end of one lane, lyric meta events, an additive meter in the
/// twin, and a phrase body's opening note value in the twin. Every book here was run
/// through the exporters BEFORE the repair (LilySharp-Lab/sessions/p398/probes/r14) and the
/// old answer is named beside each assertion.
/// </summary>
[Trait("Category", "Unit")]
public class ExportTimingAndMemoryTests
{
    private static SyntaxTree Book(string body, string header = "octave absolute")
        => SyntaxTree.Parse(header + "\npart m { clef treble }\nsection A { m { " + body + " } }\n"
            + "form main { A }\nscore main { staff m }\n");

    private static MidiNote[] Notes(SyntaxTree tree)
        => new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToArray();

    [Fact]
    public void Midi_ATieWrittenAfterARest_ExtendsNothing()
    {
        // `c'4 r4 ~ c'4`: the tie has no note before it to extend. The MIDI used to keep the
        // pre-rest c as the onset the tie could reach, and merged the c after the rest into
        // it — ONE note of 960 ticks sounding through the rest (probe tie-over-rest).
        var notes = Notes(Book("c'4 r4 ~ c'4 d'4 |"));
        Assert.Equal(new[] { (72, 0, 480), (72, 960, 480), (74, 1440, 480) },
            notes.Select(n => (n.Pitch, n.StartTick, n.DurationTicks)).ToArray());
    }

    [Fact]
    public void Midi_TheTempoIsReadInItsBeatUnit()
    {
        // `tempo 2 = 60` is 120 crotchets a minute; `tempo 4. = 40` is 60. Both used to be
        // written at the bare figure (probe tempo-unit: 60 and 40 quarter-bpm).
        var midi = new MidiExporter().Export(Book("c'4 d' e' f' | tempo 4. = 40 g'4 a' b' c'' |",
            header: "octave absolute\ntempo 2 = 60"));
        var tempos = midi.Tracks.SelectMany(t => t.TempoChanges).OrderBy(t => t.Tick).ToList();
        Assert.Contains(tempos, t => t.Tick == 0 && t.MicrosecondsPerBeat == 500_000);      // 120 qbpm
        Assert.Contains(tempos, t => t.Tick == 1920 && t.MicrosecondsPerBeat == 1_000_000); // 60 qbpm
        Assert.DoesNotContain(tempos, t => t.MicrosecondsPerBeat is 1_500_000);              // the old 40
    }

    [Fact]
    public void Midi_AGraceThatClosesOneLane_StealsNothingFromTheNext()
    {
        // The second part's first crotchet was 453 ticks and its section 27 short, because
        // the first part's trailing grace left its steal pending across the lane boundary
        // (probe grace-steal-lane).
        var tree = SyntaxTree.Parse("""
            octave absolute
            part up { clef treble }
            part lo { clef bass }
            section A {
              up { c'4 d' e' grace { f'16 } | }
              lo { c4 d e f | }
            }
            section B {
              up { g'1 | }
              lo { g1 | }
            }
            form main { A B }
            score main { staff up  staff lo }
            """);
        var notes = Notes(tree);
        var lo = notes.Where(n => n.Part == "lo").ToArray();
        Assert.Equal(new[] { 0, 480, 960, 1440, 1920 }, lo.Select(n => n.StartTick).ToArray());
        Assert.All(lo.Take(4), n => Assert.Equal(480, n.DurationTicks));
        Assert.Contains(notes, n => n.Part == "up" && n.Pitch == 79 && n.StartTick == 1920);
    }

    [Fact]
    public void Midi_LyricSyllables_SitOnTheSungOnsets()
    {
        // Four syllables on four crotchets; a rest is not sung, a tie continuation is one
        // onset, a hyphenated pair is two syllables. Before session 398 the .mid held NO
        // lyric events at all (probe lyrics-midi) — and the older walk wrote them all at
        // the section's start tick.
        var midi = new MidiExporter().Export(SyntaxTree.Parse("""
            octave absolute
            part m { clef treble }
            section A {
              m { c'4 d' e' f' | r4 g'4~ g'4 a'4 | }
              lyrics { la la la la | mor -- ning }
            }
            form main { A }
            score main { staff m  lyrics }
            """));
        var lyrics = midi.Tracks.SelectMany(t => t.Lyrics).OrderBy(l => l.Tick).ToList();
        Assert.Equal(new[] { 0, 480, 960, 1440, 2400, 3360 }, lyrics.Select(l => l.Tick).ToArray());
        Assert.Equal(new[] { "la", "la", "la", "la", "mor", "ning" }, lyrics.Select(l => l.Text).ToArray());
    }

    [Fact]
    public void Xml_TheMetronomeWritesItsBeatUnit_AndTheSoundTempoInCrotchets()
    {
        // The <metronome> said "quarter" whatever the source wrote, and <sound tempo> the
        // bare figure (probe tempo-unit: beat-unit quarter, per-minute 60, tempo="60").
        var doc = new MusicXmlExporter().Export(Book("c'4 d' e' f' | tempo 4. = 40 g'4 a' b' c'' |",
            header: "octave absolute\ntempo 2 = 60"));
        var measures = doc.Parts.Single().Measures;
        string first = measures[0].ToXml().ToString();
        Assert.Contains("<beat-unit>half</beat-unit>", first);
        Assert.Contains("<per-minute>60</per-minute>", first);
        Assert.Contains("<sound tempo=\"120\" />", first);
        string second = measures[1].ToXml().ToString();
        Assert.Contains("<beat-unit>quarter</beat-unit>", second);
        Assert.Contains("<beat-unit-dot />", second);
        Assert.Contains("<per-minute>40</per-minute>", second);
        Assert.Contains("<sound tempo=\"60\" />", second);
    }

    [Fact]
    public void Ly_AnAdditiveMeter_IsWrittenAsLilyPondsPair()
    {
        // `\time 3+2/8` is a syntax error in LilyPond 2.26.0 and \compoundMeter is no longer
        // a command; `\time #'((3 2) . 8)` compiles (probes additive-time, cm3).
        string ly = new LilyPondExporter().Export(Book("c'8 d' e' f' g' | a'8 b' c'' d'' e'' |",
            header: "octave absolute\ntime 3+2/8"));
        Assert.Contains("\\time #'((3 2) . 8)", ly);
        Assert.DoesNotContain("\\time 3+2/8", ly);
    }

    [Fact]
    public void Ly_APhraseBody_OpensAtACrotchet_AndWhatItLastWroteCarriesOut()
    {
        // The page opens a phrase body in a fresh frame — a crotchet — and walks on with
        // whatever the body last wrote. LilyPond's parser carries the last value written,
        // so the body's first bare note has to write the crotchet out, and the note after
        // the reference must NOT be forced (it inherits the body's last on both sides).
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse("""
            octave absolute
            phrase G { d' e' }
            part m { clef treble }
            section A { m { c'8 G f' g' | } }
            form main { A }
            score main { staff m }
            """));
        // Absolute mode inlines the body, so the whole bar is one line: the body's first
        // note carries the crotchet, the rest inherit (probe phrase-value: base wrote
        // `c'8 d' e' f' g'`, which LilyPond reads as five quavers).
        Assert.Contains("c'8 d'4 e' f' g' |", ly);
    }
}
