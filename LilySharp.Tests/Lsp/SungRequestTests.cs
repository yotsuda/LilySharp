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
using System.Linq;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>lilysharp/sung</c> request (docs/singing-design.md §4): the words of the score the
/// preview shows, on their notes in seconds, one voice per part a lyrics row sings — the score
/// the editor hands a singing engine. Its notes stand where <c>lilysharp/playback</c>'s do.
/// </summary>
public class SungRequestTests
{
    private const string Doc = """
        tempo 4 = 120
        time 2/4
        part melody { clef treble }
        part bass { clef bass }
        section A {
          melody { c'8. d16 c8. d16 | c4 a8 r | }
          bass { c,2 | f,2 | }
          lyrics verse { ゆ _ き や | こん こ | }
        }
        form { A }
        score { staff melody  lyrics verse sings melody  staff bass }
        score bare { staff bass }
        """;

    private static LilySharpLanguageServer Open(out Uri uri)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        uri = new Uri("file:///yuki.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = Doc, Version = 1, LanguageId = "lilysharp" },
        });
        return server;
    }

    [Fact]
    public void TheSungPart_ComesBackInSeconds_WhereThePlaybackPutsIt()
    {
        var server = Open(out var uri);
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(sung.Error);
        var voice = Assert.Single(sung.Voices!);
        Assert.Equal("melody", voice.Part);
        Assert.Equal("ja", voice.Language);
        Assert.Equal(new[] { "ゆ", null, "き", "や", "こん", "こ" }, voice.Notes.Select(n => n.L));
        Assert.True(voice.Notes[1].M);
        Assert.True(voice.Notes[5].B);

        var playback = server.GetPlayback(new PlaybackParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        var melody = playback.Notes!.Where(n => n.P != PlaybackNote.PedalPitch && playback.Parts![n.N] == "melody").ToArray();
        Assert.Equal(melody.Select(n => n.T), voice.Notes.Select(n => n.T));
        Assert.Equal(melody.Select(n => n.S), voice.Notes.Select(n => n.S));
    }

    [Fact]
    public void AScoreWithNoWords_HasNoVoice()
    {
        var server = Open(out var uri);
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, RenderName = "bare" });
        Assert.Null(sung.Error);
        Assert.Empty(sung.Voices!);
    }
}
