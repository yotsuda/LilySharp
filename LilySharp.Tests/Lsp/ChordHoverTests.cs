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
/// Hovering a chord, a <c>&lt;&lt; &gt;&gt;</c> arpeggio or a <c>q</c> shows the chord symbol
/// its notes name — the one a bare <c>@chord</c> on it would print — with its degree and the
/// pitches it sounds, so the written first member's role is visible: <c>&lt;f d a&gt;</c> hovers
/// as Dm/F (F is the bass) over F4 A4 D5.
/// </summary>
public class ChordHoverTests
{
    private const string Doc =
        "part melody {\n" +
        "  section A { <d f a>4 <f d a>4 << c e g >>4 q4 | <c des d>4 <d f a>4 q4 << g' e c >>4 | c4 d e f | }\n" +
        "}\n\nform main { A }\n\nscore main {\n  staff melody\n}\n";

    private static string? HoverAt(string needle, int skip = 0, int occurrence = 0, string doc = Doc)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///chord-hover.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = doc, Version = 1, LanguageId = "lilysharp" },
        });
        int at = -1;
        for (int i = 0; i <= occurrence; i++)
            at = doc.IndexOf(needle, at + 1, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{needle}' not in the fixture");
        at += skip;
        int line = 0, col = 0;
        for (int i = 0; i < at; i++)
        {
            if (doc[i] == '\n') { line++; col = 0; }
            else col++;
        }
        var hover = server.Hover(new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col),
        });
        return hover?.Contents.Value;
    }

    [Fact]
    public void AChord_HoversAsItsSymbol() =>
        Assert.Equal("`Dm (IIm)` \u00A0D4 \u00A0F4 \u00A0A4", HoverAt("<d f a>"));

    private const string ChordRowDoc =
        "key c major\n" +
        "part melody { clef treble }\n" +
        "section A { melody { e'4 e' f' g' | a' g' e' d' | c'1 | } }\n" +
        "chords harmony { Dm | G7/B C/E | Cx | }\n" +
        "form main { A }\n" +
        "score main { chords harmony  staff melody }\n";

    [Fact]
    public void AChordRowEntry_HoversAsItsSymbolDegreeAndTones() =>
        Assert.Equal("`Dm (IIm)` \u00A0A3 \u00A0D4 \u00A0F4", HoverAt("Dm", doc: ChordRowDoc));

    [Fact]
    public void AChordRowSlash_ListsTheBassAnOctaveBelowTheWindow()
    {
        Assert.Equal("`G7/B (V7/VII)` \u00A0B2 \u00A0G3 \u00A0B3 \u00A0D4 \u00A0F4", HoverAt("G7/B", doc: ChordRowDoc));
        Assert.Equal("`C/E (I/III)` \u00A0E3 \u00A0G3 \u00A0C4 \u00A0E4", HoverAt("C/E", doc: ChordRowDoc));
    }

    [Fact]
    public void AnUnregisteredQuality_SoundsItsRootAlone() =>
        Assert.Equal("`Cx (Ix)` \u00A0C4", HoverAt("Cx", doc: ChordRowDoc));

    [Fact]
    public void TheDegree_ReadsTheKeyInForce() =>
        Assert.Equal("`D (V)` \u00A0D4 \u00A0F♯4 \u00A0A4", HoverAt("<d fis a>", doc:
            "key g major\n\npart melody {\n  section A { <d fis a>4 r2. | }\n}\n\nform main { A }\n\nscore main {\n  staff melody\n}\n"));

    [Fact]
    public void AnInversion_ShowsTheWrittenBassAsTheSlash_AndListsLowestFirst() =>
        Assert.Equal("`Dm/F (IIm/IV)` \u00A0F4 \u00A0A4 \u00A0D5", HoverAt("<f d a>"));

    [Fact]
    public void AMemberPitch_HoversAsItsChord() =>
        Assert.Equal("`Dm/F (IIm/IV)` \u00A0F4 \u00A0A4 \u00A0D5", HoverAt("<f d a>", skip: 3));

    [Fact]
    public void AnArpeggio_HoversAsItsSymbol() =>
        Assert.Equal("`C (I)` \u00A0C4 \u00A0E4 \u00A0G4", HoverAt("<< c e g >>"));

    [Fact]
    public void AnArpeggio_ListsItsPitchesInPlayedOrder() =>
        Assert.EndsWith("G4 \u00A0E4 \u00A0C4", HoverAt("<< g' e c >>"));

    [Fact]
    public void AChordRepetition_HoversAsWhatItRepeats() =>
        Assert.Equal("`Dm (IIm)` \u00A0D4 \u00A0F4 \u00A0A4", HoverAt("q4", occurrence: 1));

    [Fact]
    public void NotesThatNameNoChord_StillListTheirPitches() =>
        Assert.Equal("C4 \u00A0D♭4 \u00A0D4", HoverAt("<c des d>"));

    [Fact]
    public void ASingleNote_IsNotAChord()
    {
        var hover = HoverAt("c4 d");
        Assert.True(hover == null || hover.StartsWith("**Note**", StringComparison.Ordinal), hover);
    }
}
