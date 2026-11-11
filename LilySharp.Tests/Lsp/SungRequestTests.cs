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
        part melody { clef treble  instrument soprano }
        part bass { clef bass }
        section A {
          melody { c'8. d16 c8. d16 | c4 a8 r | }
          bass { c,2 | f,2 | }
          lyrics verse { ゆ _ き や | こん こ | }
        }
        singer sings verse on melody
        form { A }
        score { staff melody  lyrics verse on melody  staff bass }
        score bare { staff bass }
        score unseen { staff melody hidden  staff bass  lyrics verse on melody }
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

    // Owner's request 2026-11-12, a trial: `@vibrato` on a sung note is told to the singing engine
    // (a DiffSinger voice lays it on its pitch curve) - the swing each way in cents, and the swings
    // a second where written. It draws nothing, and the MIDI does not know it.
    [Fact]
    public void AVibratoWrittenOnANote_IsToldToTheSinger()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///vib.lys");
        string text = Doc.Replace("c'8. d16 c8. d16 | c4 a8 r |", "c'8.@vibrato d16 c8.@vibrato(60) d16 | c4@vibrato(70, 6) a8 r |");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(sung.Error);
        var notes = Assert.Single(sung.Voices!).Notes;
        // Bare: the default depth, the rate left to the engine. Then a depth, then both.
        Assert.Equal(new double?[] { 40, null, 60, null, 70, null }, notes.Select(n => n.Vb));
        Assert.Equal(new double?[] { null, null, null, null, 6, null }, notes.Select(n => n.Vr));
        // The words and the times are what they were without it.
        var plain = Assert.Single(Open(out var plainUri) .GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = plainUri } }).Voices!).Notes;
        Assert.Equal(plain.Select(n => (n.T, n.D, n.P, n.L)), notes.Select(n => (n.T, n.D, n.P, n.L)));

        // Known where it is written - no "unknown annotation" - and out of its range, said.
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(text);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        // The bare word is an articulation's node; with an argument, a mark's.
        var marks = tree.GetRoot().DescendantNodes<LilySharp.Core.Syntax.MusicMarkSyntax>().Where(m => m.Name == "vibrato").ToList();
        Assert.Equal(2, marks.Count);
        Assert.True(LilySharp.Core.Semantics.AnnotationNameValidator.IsKnownPlainName("vibrato"));
        Assert.All(marks, m => Assert.True(LilySharp.Core.Semantics.AnnotationNameValidator.IsKnownCompoundName(m)));
        var wide = LilySharp.Core.Syntax.SyntaxTree.Parse(Doc.Replace("c4 a8 r |", "c4@vibrato(300) a8 r |"))
            .GetRoot().DescendantNodes<LilySharp.Core.Syntax.MusicMarkSyntax>().Single(m => m.Name == "vibrato");
        Assert.Null(LilySharp.Core.Semantics.AnnotationValues.Vibrato(wide));
        Assert.False(LilySharp.Core.Semantics.AnnotationNameValidator.IsKnownCompoundName(wide));
        // …and the slur a reader writes after it is still a slur where no argument is meant: the
        // name takes an argument, so `@vibrato (` is one - the price `@arpeggio` and `@todo` pay too.
    }

    // Owner's request 2026-11-11: a candidate's Play sings. The text is the candidate's, not the
    // document's — read as lilysharp/playbackText reads it, so the two answers agree note for note.
    [Fact]
    public void ATextOfItsOwn_IsSungInsteadOfTheDocument()
    {
        var server = Open(out var uri);
        string candidate = Doc.Replace("bass { c,2 | f,2 | }", "bass { c,4 g, | f,2 | }");
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Text = candidate });
        Assert.Null(sung.Error);
        var voice = Assert.Single(sung.Voices!);
        // The melody is as it was: the same notes and words as the document's voice — the key the
        // engine keeps its voice under is the same, and nothing is sung anew.
        var ofDocument = Assert.Single(server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }).Voices!);
        Assert.Equal(ofDocument.Notes.Select(n => (n.T, n.D, n.P, n.L)), voice.Notes.Select(n => (n.T, n.D, n.P, n.L)));

        // Another melody: its voice is the candidate's, on the candidate's own offsets.
        string resung = Doc.Replace("ゆ _ き や", "は _ る の");
        var other = Assert.Single(server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Text = resung }).Voices!);
        Assert.Equal(new[] { "は", null, "る", "の", "こん", "こ" }, other.Notes.Select(n => n.L));
        var playback = server.GetPlaybackText(new PlaybackTextParams { Text = resung, Uri = uri });
        var melody = playback.Notes!.Where(n => n.P != PlaybackNote.PedalPitch && playback.Parts![n.N] == "melody").ToArray();
        Assert.Equal(melody.Select(n => n.T), other.Notes.Select(n => n.T));
        Assert.Equal(new[] { "melody" }, playback.Sung);

        // A text with no document beside it, and one with an error.
        Assert.Single(new LilySharpLanguageServer(Stream.Null, Stream.Null).GetSung(new SungParams { Text = Doc }).Voices!);
        Assert.Equal("Score has errors", server.GetSung(new SungParams { Text = Doc.Replace("form { A }", "form { A") }).Error);
    }

    /// <summary>Owner's decision 2026-11-09 (雪やこんこ.lys: <c>staff m2  lyrics verse sings melody</c>,
    /// no staff of the melody): the melody is sung, not drawn, not played — the voice has its
    /// words, the playback carries its notes as a sung part (the stand-in while there is no
    /// voice), and the practice menu does not list it as a part the score leaves out.</summary>
    [Fact]
    public void AHiddenStaff_IsSungUnseen_WhenASingerLineSingsIt()
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

    /// <summary>Owner's report 2026-11-09, session 1192: a verse written first took the place of the
    /// one the score named, its singer lost with it.</summary>
    [Fact]
    public void AllLyrics_ThePartTheScoreSings_KeepsTheScoresWordsAndSinger()
    {
        const string source = """
            time 4/4
            part melody { clef treble  section A { c'4 d e f | g a b c' | } }
            lyrics first { section A { か え る の | う た が き | } }
            lyrics chosen { section A { frog frog frog frog | | } }
            singer "diffsinger/TIGER" sings chosen
            form { A }
            score { staff melody  lyrics chosen on melody }
            """;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lilysharp-sung-{Guid.NewGuid():N}.lys");
        System.IO.File.WriteAllText(path, source);
        try
        {
            var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
            var sung = server.GetSung(new SungParams { Path = path, AllLyrics = true });
            Assert.Null(sung.Error);
            var voice = Assert.Single(sung.Voices!);
            Assert.Equal("diffsinger/TIGER", voice.Singer);
            Assert.Equal(new[] { "frog", "frog", "frog", "frog" }, voice.Notes.Where(n => n.L != null).Select(n => n.L));
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

    /// <summary>The practice menu lists every singable lyrics of the file, whatever the score shows
    /// (owner's request 2026-11-09, session 1163): asked with AllLyrics, the bare score — a staff of
    /// the bass alone — still answers the melody's voice, marked as not the score's own; the default
    /// score's answer marks it as its own.</summary>
    [Fact]
    public void AllLyrics_ListsTheFilesSingableVoices_SayingWhichTheScoreSings()
    {
        var server = Open(out var uri);
        var bare = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, RenderName = "bare", AllLyrics = true });
        Assert.Null(bare.Error);
        var voice = Assert.Single(bare.Voices!);
        Assert.Equal("melody", voice.Part);
        Assert.False(voice.InScore);
        Assert.Equal(new[] { "ゆ", null, "き", "や", "こん", "こ" }, voice.Notes.Select(n => n.L));

        var own = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, AllLyrics = true });
        Assert.True(Assert.Single(own.Voices!).InScore);
        // Without AllLyrics the bare score has no voice, as before.
        Assert.Empty(server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, RenderName = "bare" }).Voices!);
    }
}
