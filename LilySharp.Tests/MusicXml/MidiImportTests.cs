// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Midi;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A Standard MIDI File read into Lily# (owner's request 2026-11-12, <c>MidiReader</c>). The
/// tests write a book, export it as the .mid Lily# writes (<see cref="MidiForFile"/>), import
/// that, and ask that what comes back compiles clean and SOUNDS what the book sounds: the same
/// keys at the same ticks for the same lengths. How it is spelled and barred is asked apart.
/// </summary>
public class MidiImportTests
{
    private static byte[] MidOf(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        using var bytes = new MemoryStream();
        MidiForFile.Shape(midi, tree.GetRoot(), spec).WriteTo(bytes);
        return bytes.ToArray();
    }

    private static List<(int Tick, int Pitch, int Ticks)> Sounds(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)) + "\n" + source);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        // A drum is struck: its written length is the page's, not the sound's.
        return [.. midi.Tracks.SelectMany(t => t.Notes).Select(n => (n.StartTick, n.Pitch, n.Channel == 9 ? 0 : n.DurationTicks)).Order()];
    }

    private static (string Lys, ImportReport Report) RoundTrip(string source)
    {
        var (lys, report) = new MusicXmlImporter().ImportBytes(MidOf(source));
        Assert.Equal(Sounds(source), Sounds(lys));
        return (lys, report);
    }

    private static string Book(string music, string head = "time 4/4\nkey c major", string part = "part p { }")
        => $"octave absolute\ntempo 96\n{head}\n{part}\nsection A {{ p {{ {music} }} }}\nform {{ A }}\nscore {{ staff p }}\n";

    [Theory]
    [InlineData("c'4 d'8 e'8 f'4. g'8 | a'16 b'16 c''8 r4 r2 | c''1 |")]                       // plain values and rests
    [InlineData("c'8 d'4 e'8~ e'4 f'4 | g'1~ | g'2 r2 |")]                                    // syncopation, ties in and across bars
    [InlineData("<c' e' g'>2 <d' f' a'>4 <e' g'>4 | <c' e' g' c''>1 |")]                      // chords
    [InlineData("tuplet 3/2 { c'8 d'8 e'8 } f'4 tuplet 3/2 { g'4 a'8 } b'4 | c''1 |")]         // triplets, a beat at a time
    [InlineData("voice { e''4 d''4 c''4 d''4 | e''1 | } { c'1 | c'2 g2 | }")]                  // a held bass under a tune: two voices
    public void WhatComesBackSoundsTheSame(string music) => Assert.Empty(RoundTrip(Book(music)).Report.Warnings);

    [Theory]
    [InlineData("time 3/4\nkey c major", "c'2. | d'4 e'8 f'8 g'4 | c''2. |")]
    [InlineData("time 6/8\nkey c major", "c'4. d'4. | e'8 f'8 g'8 a'4 b'8 | c''2. |")]
    [InlineData("time 2/2\nkey c major", "c'1 | d'2 e'2 |")]
    public void TheMeterIsRead(string head, string music)
    {
        var (lys, _) = RoundTrip(Book(music, head));
        Assert.Contains(head.Split('\n')[0], lys);
    }

    [Theory]
    [InlineData("key g major", "fis'4 g'4 a'4 b'4 | cis''4 d''2. |", "fis'4", "cis''4")]         // the key's sharp, and one outside it
    [InlineData("key bes major", "bes'4 ees''4 e''4 f''4 | fis''4 g''2. |", "ees''4", "e''4")]   // a flat key: E natural is E, not F flat
    [InlineData("key d minor", "d'4 e'4 f'4 g'4 | a'4 bes'4 cis''4 d''4 |", "bes'4", "cis''4")]  // the raised seventh is a sharp
    public void NotesAreSpelledByTheKeyTheFileStates(string key, string music, string one, string other)
    {
        var (lys, report) = RoundTrip(Book(music, "time 4/4\n" + key));
        Assert.Contains(" " + one + " ", " " + lys.Replace("|", " "));
        Assert.Contains(" " + other + " ", " " + lys.Replace("|", " "));
        Assert.DoesNotContain(report.Warnings, w => w.Contains("states no key"));
    }

    [Fact]
    public void ADrumPart_ComesBackAsDrums()
    {
        string source = "octave absolute\ntempo 120\ntime 4/4\npart kit { clef percussion }\npart p { }\n"
            + "section A { kit { <bd hh>8 hh8 <sn hh>8 hh8 hh8 <bd hh>8 <sn hh>8 hh8 | bd4 sn4 r2 | } p { c'1 | c'1 | } }\nform { A }\nscore { staff kit  staff p }\n";
        var (lys, _) = RoundTrip(source);
        Assert.Contains("clef percussion", lys);
        Assert.Contains("<bd hh>8 hh8 <sn hh>8", lys);
        // …and the drum chords are counted once each: the bar is whole (a chord's members were
        // counted beside it, LYS2004, until this import met it).
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(lys)), d => d.Message.Contains("will not align"));
    }

    [Fact]
    public void WordsVoicesAndSounds_AreRead()
    {
        string source = "octave absolute\nlanguage english\ntempo 100\ntime 4/4\nkey c major\n"
            + "part lead \"Lead\" { midiInstrument \"flute\"  lyrics words }\npart bass { midiInstrument \"acoustic bass\" }\n"
            + "section A { lead { c''4 d''4 e''4 f''4 | g''1 | } bass { c2 g,2 | c1 | } lyrics words { Morn- ing has come | now | } }\nform { A }\n"
            + "singer \"neutrino/MERROW\" on lead\nscore { staff lead  lyrics words  staff bass }\n";
        var (lys, _) = RoundTrip(source);
        Assert.Contains("lyrics words { Morn- ing has come | now | }", lys);
        Assert.Contains("singer \"neutrino/MERROW\" on lead sings words", lys);
        Assert.Contains("midiInstrument \"acoustic bass\"", lys);
        Assert.Contains("part bass { clef bass", lys);
        Assert.Contains("tempo 100", lys);
    }

    /// <summary>A played file is not on the page's grid: its points go to the nearest sixteenth
    /// or triplet eighth, and the report says how many moved.</summary>
    [Fact]
    public void APlayedFile_IsQuantized_AndSaysSo()
    {
        var midi = new MidiFile { TicksPerQuarterNote = 480 };
        var track = new MidiTrack { Name = "Piano", Channel = 0, Program = 0 };
        // Four quarters, each a little early or late and a little short.
        int[] starts = [7, 470, 965, 1436];
        for (int i = 0; i < starts.Length; i++)
            track.Notes.Add(new MidiNote(0, 60 + 2 * i, 80, starts[i], 430));
        midi.Tracks.Add(track);
        using var bytes = new MemoryStream();
        midi.WriteTo(bytes);

        var (lys, report) = new MusicXmlImporter().ImportBytes(bytes.ToArray());
        Assert.Contains("c'4 d'4 e'4 fis'4 |", lys);
        Assert.Contains(report.Warnings, w => w.Contains("4 notes did not begin on a sixteenth"));
        Assert.Contains(report.Warnings, w => w.Contains("states no key"));
        Assert.False(SyntaxTree.Parse(lys).HasErrors);
    }

    [Fact]
    public void WhatIsNotAMidiFileWithBars_IsRefused()
    {
        byte[] smpte = [(byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 0, 0xE7, 0x28];
        Assert.Contains("SMPTE", Assert.Throws<InvalidDataException>(() => new MusicXmlImporter().ImportBytes(smpte)).Message);
        byte[] empty = [(byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 1, 0, 0, 0x01, 0xE0];
        Assert.Contains("no notes", Assert.Throws<InvalidDataException>(() => new MusicXmlImporter().ImportBytes(empty)).Message);
    }
}
