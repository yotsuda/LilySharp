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
        score unseen { staff bass  lyrics verse sings melody }
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
        // The score sings the melody (the words under its staff) and plays the bass.
        Assert.Equal(new[] { "melody" }, playback.Sung);
        Assert.Equal(new[] { "bass" }, playback.Played);
        var melody = playback.Notes!.Where(n => n.P != PlaybackNote.PedalPitch && playback.Parts![n.N] == "melody").ToArray();
        Assert.Equal(melody.Select(n => n.T), voice.Notes.Select(n => n.T));
        Assert.Equal(melody.Select(n => n.S), voice.Notes.Select(n => n.S));
    }

    /// <summary>Owner's decision 2026-11-09 (雪やこんこ.lys: <c>staff m2  lyrics verse sings melody</c>,
    /// no staff of the melody): the melody is sung, not drawn, not played — the voice has its
    /// words, the playback carries its notes as a sung part (the stand-in while there is no
    /// voice), and the practice menu does not list it as a part the score leaves out.</summary>
    [Fact]
    public void APartNoStaffShows_IsSungUnseen_WhenARowSingsIt()
    {
        var server = Open(out var uri);
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, RenderName = "unseen" });
        Assert.Null(sung.Error);
        var voice = Assert.Single(sung.Voices!);
        Assert.Equal("melody", voice.Part);
        Assert.Equal(new[] { "ゆ", null, "き", "や", "こん", "こ" }, voice.Notes.Select(n => n.L));

        var playback = server.GetPlayback(new PlaybackParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, RenderName = "unseen" });
        Assert.Null(playback.Error);
        Assert.Equal(new[] { "melody" }, playback.Sung);
        Assert.Equal(new[] { "bass" }, playback.Played);
        Assert.Contains("melody", playback.Parts!);
        Assert.Empty(playback.Extra!);
        var melody = playback.Notes!.Where(n => n.P != PlaybackNote.PedalPitch && playback.Parts![n.N] == "melody").ToArray();
        Assert.Equal(melody.Select(n => n.T), voice.Notes.Select(n => n.T));
    }

    /// <summary>The folder export sings files the editor never opened (session 1153: the Explorer's
    /// Export as MP4 carried no voice): the request takes a path, as the export does.</summary>
    [Fact]
    public void AFileNotOpen_IsSungByItsPath()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lilysharp-sung-{Guid.NewGuid():N}.lys");
        System.IO.File.WriteAllText(path, Doc);
        try
        {
            var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
            var sung = server.GetSung(new SungParams { Path = path });
            Assert.Null(sung.Error);
            Assert.Equal("melody", Assert.Single(sung.Voices!).Part);
            Assert.Equal("File not found: nowhere.lys", server.GetSung(new SungParams { Path = "nowhere.lys" }).Error);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
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
