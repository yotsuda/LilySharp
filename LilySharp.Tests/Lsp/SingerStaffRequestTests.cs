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
/// A score's <c>staff SINGER</c> and <c>lyrics SINGER</c> through the server (docs/lyrics-singer-design.md §13): the
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
        score { staff lead  lyrics words  staff carol  lyrics carol  staff bob  lyrics bob }
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
        Assert.Equal(["bob__0", "carol__0", "lead"], sung.Voices!.Select(v => v.Part).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal([71.0, 71, 72, 71], sung.Voices!.Single(v => v.Part == "carol__0").Notes.Select(n => n.P));
        Assert.Equal([55.0, 55, 57, 55], sung.Voices!.Single(v => v.Part == "bob__0").Notes.Select(n => n.P));
        // Each voice carries its singer's own name: what the practice menu calls it.
        Assert.Equal("carol", sung.Voices!.Single(v => v.Part == "carol__0").Name);
        Assert.Equal("bob", sung.Voices!.Single(v => v.Part == "bob__0").Name);
        Assert.Equal("alice", sung.Voices!.Single(v => v.Part == "lead").Name);
    }

    [Fact]
    public void InAScoreThatPrintsNoSinger_EachVoiceOfThePartStillHasItsName_AndALineWithNoneHasNone()
    {
        var (server, uri) = Opened(Book.Replace("score { staff lead  lyrics words  staff carol  lyrics carol  staff bob  lyrics bob }", "score { staff lead  lyrics words }")
            .Replace("singer alice female sings on lead in Verse", "singer female sings on lead in Verse"));
        var sung = server.GetSung(new SungParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        Assert.Null(sung.Error);
        Assert.Equal(["lead", "lead#2", "lead#3"], sung.Voices!.Select(v => v.Key));
        Assert.Equal([null, "bob", "carol"], sung.Voices!.Select(v => v.Name));
    }

    /// <summary>Owner's report 2026-11-12: in a score written a line an item, the singer's item
    /// was plain where <c>staff lead</c> and <c>lyrics words</c> were coloured. Now the items ARE
    /// <c>staff</c> and <c>lyrics</c>, and the singer's name is coloured as a part's or a track's is.</summary>
    [Fact]
    public void AScoresSingerItems_AreColouredAsItsOtherItemsAre()
    {
        string source = Book.Replace("score { staff lead  lyrics words  staff carol  lyrics carol  staff bob  lyrics bob }",
            "score {\n   staff lead\n   lyrics words\n   staff carol\n   lyrics carol\n   staff dave\n}");
        var tokens = LilySharpLanguageServer.CollectSemanticTokens(LilySharp.Core.Syntax.SyntaxTree.Parse(source).GetRoot(), source).ToList();
        int TypeAt(string line, string word)
        {
            int at = source.IndexOf(line, StringComparison.Ordinal) + line.IndexOf(word, StringComparison.Ordinal);
            var (l, c) = LilySharpLanguageServer.GetLineAndCharacter(source, at);
            return tokens.FirstOrDefault(t => t.Line == l && t.Character == c && t.Length == word.Length)?.TokenType ?? -1;
        }

        const int Keyword = 0, Part = 9;
        Assert.Equal(Keyword, TypeAt("   staff lead", "staff"));
        Assert.Equal(Part, TypeAt("   staff lead", "lead"));
        Assert.Equal(Keyword, TypeAt("   staff carol", "staff"));
        Assert.Equal(Part, TypeAt("   staff carol", "carol"));
        Assert.Equal(Keyword, TypeAt("   lyrics carol", "lyrics"));
        Assert.Equal(Part, TypeAt("   lyrics carol", "carol"));
        // A name nobody has stays plain beside its error, as an unknown part does.
        Assert.Equal(-1, TypeAt("   staff dave", "dave"));
        // …and the name is coloured where the singer line gives it.
        Assert.Equal(Part, TypeAt("singer carol female sings", "carol"));
    }

    private static string[] LabelsAt(string text, int offset)
    {
        var (server, uri) = Opened(text);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        return list?.Items.Select(i => i.Label!).ToArray() ?? [];
    }

    [Fact]
    public void OnAFreshLineOfAScore_SingerIsOffered()
    {
        string source = Book.Replace("score { staff lead  lyrics words  staff carol  lyrics carol  staff bob  lyrics bob }",
            "score {\n   staff lead\n   lyrics words\n   \n}");
        int at = source.IndexOf("lyrics words\n   ", StringComparison.Ordinal) + "lyrics words\n   ".Length;
        var labels = LabelsAt(source, at);
        Assert.Contains("singer", labels);
        Assert.Contains("staff", labels);

        // …under a staff too, and not in the middle of a line, where the word would be the row's.
        string underStaff = source.Replace("   lyrics words\n   \n}", "   \n}");
        Assert.Contains("singer", LabelsAt(underStaff, underStaff.IndexOf("staff lead\n   ", StringComparison.Ordinal) + "staff lead\n   ".Length));

        int midLine = source.IndexOf("lyrics words", StringComparison.Ordinal) + "lyrics words".Length;
        Assert.DoesNotContain("singer", LabelsAt(source.Insert(midLine, " "), midLine + 1));
    }

    /// <summary>Owner's report 2026-11-12: after the <c>sings</c> of <c>singer alice female sings</c>
    /// the list had no solfege — the line's steps were read as <c>singer [gender] ["voice"]</c>, and
    /// a line that begins with the singer's own name was no singer line.</summary>
    [Theory]
    [InlineData("singer alice female sings ")]
    [InlineData("singer alice female \"neutrino/SEVEN\" sings ")]
    [InlineData("singer ようこ sings ")]
    [InlineData("singer female sings ")]
    public void AfterSingsOnANamedSingersLine_TheWordsAreListed_SolfegeAmongThem(string line)
    {
        string source = Book + "\n" + line;
        var labels = LabelsAt(source, source.Length);
        Assert.Contains("words", labels);
        Assert.Contains("solfege", labels);
        Assert.Contains("hum", labels);
        Assert.DoesNotContain("staff", labels);
    }

    [Theory]
    [InlineData("singer alice female ", "sings")]
    [InlineData("singer alice female sings words ", "on")]
    [InlineData("singer alice female sings words on ", "lead")]
    [InlineData("singer alice female ", "on")]
    [InlineData("singer alice female on ", "lead")]
    [InlineData("singer alice female on lead ", "sings")]
    [InlineData("singer alice female on lead ", "up")]
    [InlineData("singer alice female on lead ", "in")]
    [InlineData("singer alice female on lead sings ", "words")]
    [InlineData("singer alice female on lead sings ", "solfege")]
    [InlineData("singer alice female on lead sings words ", "up")]
    [InlineData("singer alice female on lead up 3 in ", "Chorus")]
    [InlineData("singer alice female ", "japanese")]
    [InlineData("singer alice ", "english")]
    [InlineData("singer alice female japanese ", "sings")]
    [InlineData("singer alice female english \"neutrino/SEVEN\" sings ", "solfege")]
    [InlineData("singer alice english sings words on ", "lead")]
    [InlineData("singer alice female sings words on lead ", "up")]
    [InlineData("singer alice female sings words on lead up 3 in ", "Chorus")]
    public void TheStepsOfANamedSingersLine_AreTheLinesOwn(string line, string offered)
    {
        string source = Book + "\n" + line;
        Assert.Contains(offered, LabelsAt(source, source.Length));
    }

    /// <summary>The owner's reason for the part before the words (2026-11-12): once the part is
    /// said, the words IT names are the list — and not the file's other lyrics.</summary>
    [Fact]
    public void AfterThePartAndSings_TheWordsThatPartNamesAreListed()
    {
        string source = Book.Replace("part lead { instrument alto  lyrics words }", "part lead { instrument alto  lyrics words }\npart other { instrument alto  lyrics theirs }")
            .Replace("section Verse {", "section Verse {\n  other { c'1 | }\n  lyrics theirs { oh | }")
            + "\nsinger dora female on lead sings ";
        var labels = LabelsAt(source, source.Length);
        Assert.Contains("words", labels);
        Assert.DoesNotContain("theirs", labels);
        Assert.DoesNotContain("on", labels);

        // The older order lists every lyrics track, as it did: the part is not said yet.
        string older = source[..^"singer dora female on lead sings ".Length] + "singer dora female sings ";
        Assert.Contains("theirs", LabelsAt(older, older.Length));
    }

    [Fact]
    public void AfterStaffAndAfterLyricsInAScore_TheNamedSingersAreListedBesideThePartsAndTheTracks()
    {
        string afterStaff = Book.Replace("staff carol  lyrics carol  staff bob  lyrics bob }", "staff  }");
        var staves = LabelsAt(afterStaff, afterStaff.IndexOf("staff  }", StringComparison.Ordinal) + 6);
        Assert.Contains("lead", staves);
        Assert.Contains("carol", staves);
        Assert.Contains("bob", staves);
        Assert.Contains("alice", staves);

        string afterLyrics = Book.Replace("staff carol  lyrics carol  staff bob  lyrics bob }", "staff carol  lyrics  }");
        var rows = LabelsAt(afterLyrics, afterLyrics.IndexOf("lyrics  }", StringComparison.Ordinal) + 7);
        Assert.Contains("words", rows);
        Assert.Contains("carol", rows);

        // A file that names no singer lists what it always did.
        string plain = Book.Replace("singer alice female", "singer female").Replace("singer bob male", "singer male").Replace("singer carol female", "singer female")
            .Replace("staff carol  lyrics carol  staff bob  lyrics bob }", "staff  }");
        Assert.DoesNotContain("carol", LabelsAt(plain, plain.IndexOf("staff  }", StringComparison.Ordinal) + 6));
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
        Assert.Null(LilySharpLanguageServer.WriteSingerPartAction(doc, uri, Book.IndexOf("staff carol  lyrics carol  staff bob  lyrics bob", StringComparison.Ordinal) + 3));

        var written = server.HarmonyPart(new HarmonyPartParams { TextDocument = new TextDocumentIdentifier { Uri = uri }, Singer = "carol" });
        Assert.Null(written.Error);
        Assert.Contains("part carol \"Carol\" {", written.NewText);
        Assert.Contains("singer carol female on carol", written.NewText);
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
    public void TheProblemsPanel_SaysNothingOfTheMadeText_AndSaysANameNobodyHas()
    {
        var (server, uri) = Opened(Book);
        Assert.Empty(server.DocumentDiagnostics(server.DocumentAt(uri)!, CancellationToken.None, out _).Select(d => d.Message));

        var (other, otherUri) = Opened(Book.Replace("staff bob  lyrics bob }", "staff dave  lyrics dave }"));
        var said = other.DocumentDiagnostics(other.DocumentAt(otherUri)!, CancellationToken.None, out _);
        // A name no singer has is an unknown part and an unknown track, as any other.
        Assert.Equal(2, said.Count);
        Assert.All(said, d => Assert.Contains("'dave'", d.Message));
    }
}