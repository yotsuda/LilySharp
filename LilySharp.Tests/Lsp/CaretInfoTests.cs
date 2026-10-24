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
using System.IO;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// lilysharp/caretInfo (owner's request 2026-10-24): the setting under the caret says what it does
/// in one status-bar line, where a hover would cover the text. ‸ marks the caret.
/// </summary>
public class CaretInfoTests
{
    private static string? InfoAt(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///caret-info.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        return server.CaretInfo(new AuditionAtParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Offset = offset }).Text;
    }

    private static string Part(string header, string music = "c4 d e f")
        => $"part b {{ {header} }}\nsection A {{ b {{ {music} }} }}\n";

    [Fact]
    public void AClef_SaysItsMiddleLine_AndThatItMovesNoNote()
    {
        Assert.Equal("clef bass: middle line D3 · a clef moves no note - octave or instrument sets the register",
            InfoAt(Part("clef ba‸ss")));
        Assert.Contains("it sounds an octave below the page", InfoAt(Part("clef treble_8‸")));
        Assert.Contains("middle line B4", InfoAt(Part("c", "‸clef treble c4 d e f")));
    }

    [Fact]
    public void AKey_SaysItsSignature_AndItsRelativeKey()
    {
        Assert.Equal("key d minor: 1 flat (B♭) · the relative major is F", InfoAt(Part("clef treble", "key d mi‸nor c4 d e f")));
        Assert.Equal("key c major: no sharps or flats · the relative minor is a", InfoAt(Part("clef treble", "k‸ey c major c4 d e f")));
        Assert.Equal("key e major: 4 sharps (F♯ C♯ G♯ D♯) · the relative minor is c♯", InfoAt(Part("clef treble", "key e‸ major c4 d e f")));
    }

    [Fact]
    public void AnOctave_SaysTheWrittenRegister_AndTheModes()
    {
        Assert.Equal("octave 3: a bare c is written C3 - the page's register (transposition moves only the sound)",
            InfoAt(Part("octave 3‸")));
        Assert.StartsWith("octave absolute: a bare letter is in the part's octave", InfoAt("octave abso‸lute\n" + Part("clef treble")));
    }

    [Fact]
    public void ATransposition_SaysWhatSounds_AndThatThePageStays()
        => Assert.Equal("transposition 8vb: sounds an octave below the page (written C4 sounds C3) - the written notes do not move",
            InfoAt(Part("transposition 8v‸b")));

    [Fact]
    public void AMidiInstrument_SaysItsNumber_Family_AndPresets()
        => Assert.Equal("midiInstrument \"electric bass (finger)\": GM 34, the bass family · the sound of instrument bass, bass5, bass6",
            InfoAt(Part("midiInstrument \"electric bass ‸(finger)\"")));

    [Fact]
    public void ATuning_NumbersItsStrings_AndNamesAReEntrantOne()
    {
        Assert.Equal("tuning ukulele: 4 G4 · 3 C4 · 2 E4 · 1 A4 - re-entrant: string 4 sounds above string 3",
            InfoAt(Part("tuning uku‸lele")));
        Assert.Equal("tuning bass: 4 E1 · 3 A1 · 2 D2 · 1 G2", InfoAt(Part("tuning ba‸ss")));
    }

    [Fact]
    public void AnInstrumentPreset_SaysItsDefaults_WithoutThePrintedName()
        => Assert.Equal("instrument bass: clef bass · octave 3 · transposition 8vb (written C3 sounds C2) · tuning bass"
            + " · midiInstrument \"electric bass (finger)\"", InfoAt(Part("instrument ba‸ss")));

    [Fact]
    public void AStaffGroup_SaysHowItEngraves()
    {
        string book = "part a { clef treble }\npart b { clef bass }\nsection A { a { c4 } b { c4 } }\n";
        Assert.StartsWith("grandStaff: a brace", InfoAt(book + "score { grand‸Staff { staff a staff b } }\n"));
        Assert.StartsWith("combinedStaff: exactly 2 parts", InfoAt(book + "score { combined‸Staff { a b } }\n"));
    }

    [Fact]
    public void AFormBarline_SaysWhatItDoes_AndARepeatsCount()
    {
        string book = Part("clef treble") + "form { |: A :|*3 }\n";
        Assert.Equal(":|*3 closes the repeat - it plays 3 times", InfoAt(book.Replace(":|*3", ":‸|*3")));
        Assert.Equal(":|*3 closes the repeat - it plays 3 times", InfoAt(book.Replace(":|*3", ":|*3‸")));
        Assert.StartsWith("|: opens a repeat", InfoAt(book.Replace("|: A", "|‸: A")));
    }

    [Fact]
    public void APageSize_SaysItsSize()
        => Assert.Equal("size a4: 210 × 297 mm (8.3 × 11.7 in)", InfoAt("paper { size a‸4 }\n" + Part("clef treble")));

    [Fact]
    public void ANote_SaysItsBarBeatAndPitch()
    {
        // Owner's request 2026-10-24: where a note stands and what it is.
        // (Relative octaves from the part's C4: a bare letter is the nearest.)
        string book = "part m { clef treble }\nsection A { m { c4 d e f | g4 a8 b c2 } }\n";
        Assert.Equal("bar 1 · beat 1 · C4", InfoAt(book.Replace("c4 d", "c‸4 d")));
        Assert.Equal("bar 1 · beat 3 · E4", InfoAt(book.Replace(" e ", " e‸ ")));
        Assert.Equal("bar 2 · beat 2 + 1/2 · B4", InfoAt(book.Replace(" b ", " b‸ ")));
        // With a score of two parts, the part is named too.
        string two = "part m { clef treble }\npart n { clef treble }\nsection A { m { c4 d e f } n { e4 f g a } }\n"
            + "score { staff m staff n }\n";
        Assert.Equal("n · bar 1 · beat 2 · F4", InfoAt(two.Replace("e4 f", "e4 f‸")));
    }

    [Fact]
    public void ANoteOfATransposingPart_SaysWhatItSounds()
    {
        string clarinet = "part cl { instrument clarinet }\nsection A { cl { d4 e f g } }\n";
        Assert.Equal("bar 1 · beat 1 · D4 sounds C4", InfoAt(clarinet.Replace("d4", "d‸4")));
        string bass = "part b { instrument bass }\nsection A { b { e,4 a d g } }\nscore { staff b }\n";
        Assert.Equal("b · bar 1 · beat 1 · E2 sounds E1", InfoAt(bass.Replace("e,4", "e‸,4")));
    }

    [Fact]
    public void AChord_SaysEveryPitch_AndAPickupItsPlaceInTheBar()
    {
        string chord = "part m { clef treble }\nsection A { m { <c e g>2 r } }\n";
        Assert.Equal("bar 1 · beat 1 · C4 E4 G4", InfoAt(chord.Replace("<c", "<‸c")));
        Assert.Equal("bar 1 · beat 1 · C4 E4 G4", InfoAt(chord.Replace("g>2", "g>‸2")));
        string pickup = "part m { clef treble }\nsection A { m { partial 4 g4 | c1 } }\n";
        Assert.Equal("bar 0 · beat 4 · G3", InfoAt(pickup.Replace("g4 |", "g‸4 |")));
    }

    [Fact]
    public void AFormsSection_SaysItsLength_AndWhichTimeThisIs()
    {
        // Owner's request 2026-10-24.
        string book = "part m { clef treble }\nsection A { m { c1 | d1 } }\nsection B { m { e1 } }\nform { A B A }\n";
        Assert.Equal("section A: 2 bars · the 1st of 2 times this form names it", InfoAt(book.Replace("{ A B", "{ ‸A B")));
        Assert.Equal("section A: 2 bars · the 2nd of 2 times this form names it", InfoAt(book.Replace("B A }", "B A‸ }")));
        Assert.Equal("section B: 1 bar · named once in this form", InfoAt(book.Replace("A B A", "A B‸ A")));
    }

    [Fact]
    public void ARest_HasNoLine()
        => Assert.Null(InfoAt(Part("clef treble", "r‸4 d e f")));
}
