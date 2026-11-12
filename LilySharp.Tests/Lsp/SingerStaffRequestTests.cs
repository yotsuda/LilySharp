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
using System.Threading;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// A score's <c>singer NAME</c> through the server (docs/lyrics-singer-design.md §13): the
/// preview, the sung request and the Problems panel read the one expansion the server's house
/// makes, so the page shows the singer's staff, the voice sings its notes, and nothing is said
/// of the text the file does not write.
/// </summary>
[Trait("Category", "Unit")]
public class SingerStaffRequestTests
{
    private const string Book = """
        octave relative
        time 4/4
        key c major
        language english
        part lead { instrument alto  lyrics words }
        section Verse {
          lead { e'4 e d c | }
          lyrics words { one two three four | }
        }
        section Chorus {
          lead { g'4 g a g | }
          lyrics words { la la la la | }
        }
        form { Verse Chorus }
        singer alice female sings on lead in Verse
        singer bob male sings on lead octave -1 in Chorus
        singer carol female sings on lead up 3 in Chorus
        score { staff lead  lyrics words  singer carol  singer bob }
        """;

    private static (LilySharpLanguageServer Server, Uri Uri) Opened(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///singer-staff.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        return (server, uri);
    }

    [Fact]
    public void ThePreview_ShowsTheSingersStaves()
    {
        var (server, uri) = Opened(Book);
        var svg = server.GetSvg(new SvgParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(svg.Error);
        Assert.Contains("Carol", svg.Svg);
        Assert.Contains("Bob", svg.Svg);
    }

    [Fact]
    public void TheVoices_SingTheStavesTheScoreShows()
    {
        var (server, uri) = Opened(Book);
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(sung.Error);
        Assert.Equal(["bob", "carol", "lead"], sung.Voices!.Select(v => v.Part).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal([71.0, 71, 72, 71], sung.Voices!.Single(v => v.Part == "carol").Notes.Select(n => n.P));
        Assert.Equal([55.0, 55, 57, 55], sung.Voices!.Single(v => v.Part == "bob").Notes.Select(n => n.P));
    }

    [Fact]
    public void ANamedSingersLine_OffersToWriteTheNotesOut_AndTheServerWritesThem()
    {
        var (server, uri) = Opened(Book);
        var doc = server.DocumentAt(uri)!;
        int onCarol = Book.IndexOf("singer carol female", StringComparison.Ordinal) + 3;
        var action = LilySharpLanguageServer.WriteSingerPartAction(doc, uri, onCarol);
        Assert.Equal("Write carol's notes out as a part", action!.Title);
        Assert.Equal(LilySharpLanguageServer.WriteSingerPartCommand, action.Command!.CommandIdentifier);
        Assert.Equal([uri.ToString(), "carol"], action.Command.Arguments!.Select(a => a?.ToString()));
        // Not on the score's item, nor on a line with no name.
        Assert.Null(LilySharpLanguageServer.WriteSingerPartAction(doc, uri, Book.IndexOf("singer carol  singer bob", StringComparison.Ordinal) + 3));

        var written = server.HarmonyPart(new HarmonyPartParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Singer = "carol" });
        Assert.Null(written.Error);
        Assert.Contains("part carol \"Carol\" {", written.NewText);
        Assert.Contains("singer carol female sings on carol", written.NewText);
    }

    [Fact]
    public void AHarmonyPartOfAnInstrument_IsAskedAndWritten()
    {
        var (server, uri) = Opened(Book);
        var choices = server.HarmonyPartChoices(new HarmonyPartChoicesParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Offset = Book.IndexOf("lead { g'4", StringComparison.Ordinal) + 8,
        });
        Assert.Equal(["lead"], choices.Parts);
        Assert.Equal("lead", choices.Part);
        Assert.Equal(["Verse", "Chorus"], choices.Sections);
        Assert.Equal(["lead", "alice", "bob", "carol"], choices.Taken);

        var written = server.HarmonyPart(new HarmonyPartParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }, From = "lead", Name = "lead2", Interval = -3, Sections = ["Chorus"],
        });
        Assert.Null(written.Error);
        Assert.Contains("lead2 { e'4 e f e | }", written.NewText);
        Assert.Contains("lead2 { R*1 }", written.NewText);

        var refused = server.HarmonyPart(new HarmonyPartParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri }, From = "lead", Name = "carol", Interval = 3,
        });
        Assert.Null(refused.NewText);
        Assert.Contains("is in the score already", refused.Error);
    }

    [Fact]
    public void TheProblemsPanel_SaysNothingOfTheMadeText_AndSaysASingerNobodyNames()
    {
        var (server, uri) = Opened(Book);
        Assert.Empty(server.DocumentDiagnostics(server.DocumentAt(uri)!, CancellationToken.None, out _).Select(d => d.Message));

        var (other, otherUri) = Opened(Book.Replace("singer bob }", "singer dave }"));
        var said = other.DocumentDiagnostics(other.DocumentAt(otherUri)!, CancellationToken.None, out _);
        Assert.Contains("No singer is called dave", Assert.Single(said).Message);
    }
}