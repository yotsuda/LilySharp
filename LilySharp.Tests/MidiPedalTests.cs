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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.MidiOut;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The sustain pedal sounds (owner's decisions 2026-11-02, docs/pedal-playback-design.md): real
/// pedal events, on all the staves of a grand staff, up at the onset of the note its release is
/// written on; the notes' written lengths untouched.
/// </summary>
public class MidiPedalTests
{
    private const string Hands = "part rh { instrument piano-right }\npart lh { instrument piano-left }";

    private static MidiFile Midi(string source) => new MidiExporter().Export(SyntaxTree.Parse(source));

    private static string Piano(string rh, string lh, string form = "A", string score = "grandStaff { staff rh  staff lh }")
        => $"title \"Rain\"\ntime 4/4\noctave absolute\n{Hands}\nsection A {{\n  rh {{ {rh} }}\n  lh {{ {lh} }}\n}}\nform {{ {form} }}\nscore {{ {score} }}\n";

    private static string Changes(MidiTrack track) => string.Join(" ", track.Pedals.Select(c => (c.Down ? "v" : "^") + c.Tick));

    private static MidiTrack Track(MidiFile midi, string name) => midi.Tracks.Single(t => t.Name == name);

    [Fact]
    public void APedalIsPressed_Changed_AndLifted_AtItsNotesOnsets()
    {
        var midi = Midi(Piano("g'1 | a'1 | b'1 |", "c1@sustain | f1@sustain | g1@!sustain |"));
        Assert.Equal("v0 ^1920 v1920 ^3840", Changes(Track(midi, "lh")));
    }

    [Fact]
    public void TheHandsOfAGrandStaff_ShareIt()
    {
        var midi = Midi(Piano("g'1 | a'1 |", "c1@sustain | f1@!sustain |"));
        Assert.Equal("v0 ^1920", Changes(Track(midi, "rh")));
        Assert.Equal("v0 ^1920", Changes(Track(midi, "lh")));
        // Both hands marking one moment press once.
        Assert.Equal("v0 ^1920", Changes(Track(Midi(Piano("g'1@sustain | a'1@!sustain |", "c1@sustain | f1@!sustain |")), "lh")));
        // Apart, on staves of no grand staff, it stays in its own part.
        var apart = Midi(Piano("g'1 | a'1 |", "c1@sustain | f1@!sustain |", score: "staff rh  staff lh"));
        Assert.Empty(Track(apart, "rh").Pedals);
        Assert.Equal("v0 ^1920", Changes(Track(apart, "lh")));
    }

    [Fact]
    public void ItIsReadWhereverTheMusicPlaysIt()
    {
        // On a rest; in a repeat, each time; in a phrase; in a tuplet.
        Assert.Equal("v0 ^1920", Changes(Track(Midi(Piano("g'1 | a'1 |", "c1@sustain | r1@!sustain |")), "lh")));
        Assert.Equal("v0 ^1920 v3840 ^5760", Changes(Track(Midi(Piano("g'1 | a'1 |", "c1@sustain | f1@!sustain |", "A A")), "lh")));
        string phrase = Piano("g'1 | a'1 |", "held | f1@!sustain |").Replace("section A", "phrase held { c1@sustain }\nsection A");
        Assert.Equal("v0 ^1920", Changes(Track(Midi(phrase), "lh")));
        Assert.Equal("v0 ^1920", Changes(Track(Midi(Piano("g'1 | a'1 |", "tuplet 3/2 { c4@sustain e g } c2 | f1@!sustain |")), "lh")));
        // A chord, and its release on a chord.
        Assert.Equal("v0 ^1920", Changes(Track(Midi(Piano("g'1 | a'1 |", "<c e>1@sustain | <f a>1@!sustain |")), "lh")));
        // A form that ends with it down lifts it at the end.
        Assert.Equal("v0 ^1920 v3840 ^7680", Changes(Track(Midi(Piano("g'1 | a'1 |", "c1 | f1 |").Replace("section A", "section B {\n  rh { g'1 | a'1 | }\n  lh { c1@sustain | f1@!sustain | }\n}\nsection C {\n  rh { g'1 | a'1 | }\n  lh { c1@sustain | f1 | }\n}\nsection A").Replace("form { A }", "form { B C }")), "lh")));
    }

    [Fact]
    public void TheNotes_KeepTheirWrittenLengths()
    {
        string written = Piano("g'1 | a'1 |", "c4 e g e | f1 |");
        string pedalled = written.Replace("c4 e g e", "c4@sustain e g e").Replace("f1", "f1@!sustain");
        static List<(int, int, int, int)> Notes(string s) => Midi(s).Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.StartTick, n.DurationTicks, n.Velocity)).ToList();
        Assert.Equal(Notes(written), Notes(pedalled));
    }

    private static List<string> FileEvents(MidiFile midi)
    {
        using var stream = new MemoryStream();
        midi.WriteTo(stream);
        byte[] b = stream.ToArray();
        var events = new List<string>();
        int i = 14;
        while (i < b.Length)
        {
            int end = i + 8 + (b[i + 4] << 24 | b[i + 5] << 16 | b[i + 6] << 8 | b[i + 7]);
            i += 8;
            int tick = 0;
            while (i < end)
            {
                int delta = 0;
                while (true) { byte v = b[i++]; delta = delta << 7 | v & 0x7F; if ((v & 0x80) == 0) break; }
                tick += delta;
                byte status = b[i++];
                if (status == 0xFF) { i++; int len = 0; while (true) { byte v = b[i++]; len = len << 7 | v & 0x7F; if ((v & 0x80) == 0) break; } i += len; continue; }
                switch (status & 0xF0)
                {
                    case 0xB0: events.Add($"{tick} ch{status & 15} cc{b[i]}={b[i + 1]}"); i += 2; break;
                    case 0x90: events.Add($"{tick} ch{status & 15} on{b[i]}"); i += 2; break;
                    case 0x80: events.Add($"{tick} ch{status & 15} off{b[i]}"); i += 2; break;
                    case 0xC0: i += 1; break;
                    default: i += 2; break;
                }
            }
        }
        return events;
    }

    [Fact]
    public void TheFile_LiftsBeforeTheOldNotesEnd_AndPressesBeforeTheNewOneStarts()
    {
        var events = FileEvents(Midi(Piano("g'1 | a'1 |", "c1@sustain | f1@!sustain@sustain |")))
            .Where(e => e.StartsWith("1920 ch1") || e.StartsWith("1920 ch0")).ToList();
        var lh = events.Where(e => e.Contains(" ch1 ")).Select(e => e.Split(' ')[2]).ToList();
        Assert.Equal(["cc64=0", "off48", "cc64=127", "on53"], lh);
        // The right hand's channel takes the pedal as well.
        Assert.Contains("1920 ch0 cc64=127", events);
    }

    // MeltySynth, the road `lysc wav` takes: a note released under the pedal goes on sounding.
    [Fact]
    public void ARealSynth_RingsUnderThePedal_AndStopsWhenItLifts()
    {
        string dir = Directory.CreateTempSubdirectory("lys-pedal-").FullName;
        try
        {
            string font = TinySoundFont.Write(Path.Combine(dir, "sine.sf2"));
            static string Piece(string first, string lift) => "title \"Ring\"\ntime 4/4\ntempo 4 = 120\noctave absolute\npart p { instrument piano }\n"
                + $"section A {{\n  p {{ c'4{first} r2. | r1{lift} | r1 | }}\n}}\nform {{ A }}\nscore {{ staff p }}\n";
            double Level(string source, double from, double to)
            {
                using var wav = new MemoryStream();
                LilySharp.Core.Audio.WavRenderer.Render(Midi(source), font, wav, 22050);
                byte[] b = wav.ToArray();
                int a = 44 + (int)(from * 22050) * 4, z = 44 + (int)(to * 22050) * 4;
                double sum = 0;
                for (int i = a; i + 1 < z && i + 1 < b.Length; i += 4)
                    sum += Math.Abs((double)BitConverter.ToInt16(b, i));
                return sum / Math.Max(1, (z - a) / 4);
            }
            // The note's written end is 0.5 s; the release of the tiny font is over well before 1.5 s.
            double dry = Level(Piece("", ""), 1.5, 1.9), held = Level(Piece("@sustain", "@!sustain"), 1.5, 1.9);
            Assert.True(held > 100 && held > dry * 10, $"dry {dry}, held {held}");
            // Lifted at 2 s, it has stopped by 4 s.
            double after = Level(Piece("@sustain", "@!sustain"), 4.5, 5.5);
            Assert.True(after < held / 10, $"held {held}, after {after}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AFileWithNoPedal_HasNoControlChange()
        => Assert.DoesNotContain(FileEvents(Midi(Piano("g'1 |", "c1 |"))), e => e.Contains(" cc"));

    [Fact]
    public void TheNoteList_CarriesThePedalInTimeOrder()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var list = server.GetPlaybackText(new PlaybackTextParams { Text = Piano("g'1 | a'1 |", "c1@sustain | f1@!sustain |") });
        Assert.Null(list.Error);
        var pedals = list.Notes!.Where(n => n.P == PlaybackNote.PedalPitch).ToList();
        Assert.Equal(4, pedals.Count);   // both hands' channels, down and up
        Assert.All(pedals, n => Assert.Equal(-1, n.S));
        Assert.Equal([127, 127, 0, 0], pedals.Select(n => n.V));
        Assert.Equal(list.Notes!.OrderBy(n => n.T).Select(n => n.T), list.Notes!.Select(n => n.T));
        // The notes are the notes: no entry but a pedal below pitch 0.
        Assert.Equal(4, list.Notes!.Count(n => n.P >= 0));
    }

    [Fact]
    public void MidiOut_SendsIt_InTheFilesOrder()
    {
        var pedalUp = new PlaybackNote { T = 1, P = PlaybackNote.PedalPitch, V = 0, S = -1, C = 2 };
        var pedalDown = new PlaybackNote { T = 1, P = PlaybackNote.PedalPitch, V = 127, S = -1, C = 2 };
        var oldNote = new PlaybackNote { T = 0, D = 1, P = 48, V = 90, C = 2 };
        var newNote = new PlaybackNote { T = 1, D = 1, P = 53, V = 90, C = 2 };
        var at1 = MidiOutEvents.From([oldNote, newNote, pedalDown, pedalUp], 0).Where(e => e.Time == 1).ToList();
        Assert.Equal([(byte)(0xB0 | 2), (byte)(0x80 | 2), (byte)(0xB0 | 2), (byte)(0x90 | 2)], at1.Select(e => e.Status));
        Assert.Equal([0, 0, 127, 90], at1.Select(e => (int)e.Data2));
        // No entry of the pedal sounds as a note, or sets a program, or makes the piece longer.
        Assert.DoesNotContain(MidiOutEvents.From([pedalDown], 0), e => (e.Status & 0xF0) is MidiOutEvents.NoteOn or MidiOutEvents.Program);
        Assert.Equal(0, MidiOutEvents.EndOf([new PlaybackNote { T = 0, P = PlaybackNote.PedalPitch, V = 0 }], 0));
        // A stop lifts it.
        Assert.Contains(MidiOutEvents.Silence(3), e => e.Status == (0xB0 | 3) && e.Data1 == MidiOutEvents.Sustain && e.Data2 == 0);
    }
}
