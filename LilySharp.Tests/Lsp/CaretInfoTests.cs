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
        Assert.StartsWith("|: opens a repeat", InfoAt(book.Replace("|: A", "|‸: A")));
    }

    [Fact]
    public void APageSize_SaysItsSize()
        => Assert.Equal("size a4: 210 × 297 mm (8.3 × 11.7 in)", InfoAt("paper { size a‸4 }\n" + Part("clef treble")));

    [Fact]
    public void ANote_HasNoLine()
        => Assert.Null(InfoAt(Part("clef treble", "c‸4 d e f")));
}
