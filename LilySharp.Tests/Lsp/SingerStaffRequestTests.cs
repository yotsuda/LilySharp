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

    /// <summary>Owner's report 2026-11-12: in a score written a line an item, <c>staff lead</c> and
    /// <c>lyrics words</c> were coloured and <c>singer carol</c> was plain — the word is no
    /// reserved one, and the name was looked for among the parts.</summary>
    [Fact]
    public void AScoresSingerItem_IsColouredAsItsOtherItemsAre()
    {
        string source = Book.Replace("score { staff lead  lyrics words  singer carol  singer bob }",
            "score {\n   staff lead\n   lyrics words\n   singer carol\n   singer bob\n   singer dave\n}");
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
        // The singer's item: the word as `staff` is, the name as a part's is.
        Assert.Equal(Keyword, TypeAt("   singer carol", "singer"));
        Assert.Equal(Part, TypeAt("   singer carol", "carol"));
        Assert.Equal(Part, TypeAt("   singer bob", "bob"));
        // A name nobody has stays plain beside its error, as an unknown part does.
        Assert.Equal(Keyword, TypeAt("   singer dave", "singer"));
        Assert.Equal(-1, TypeAt("   singer dave", "dave"));
        // …and the name is coloured where the singer line gives it.
        Assert.Equal(Part, TypeAt("singer carol female sings", "carol"));

        // The grammar paints the word before the server answers: `singer NAME` at a line's end,
        // before a brace or before the next item — and not a singer line, nor a part's block.
        using var grammar = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "editors", "vscode", "syntaxes", "lilysharp.tmLanguage.json")));
        var item = new System.Text.RegularExpressions.Regex(
            grammar.RootElement.GetProperty("repository").GetProperty("singer-item").GetProperty("match").GetString()!,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.Matches(item, "   singer carol");
        Assert.Matches(item, "score { staff lead  singer carol  singer bob }");
        Assert.Matches(item, "  singer carol \"Carol K.\"");
        Assert.DoesNotMatch(item, "singer female sings on lead");
        Assert.DoesNotMatch(item, "singer carol female sings on lead");
        Assert.DoesNotMatch(item, "  singer { c4 d e f | }");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "editors", "vscode", "package.json")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void OnAFreshLineOfAScore_SingerIsOffered()
    {
        string source = Book.Replace("score { staff lead  lyrics words  singer carol  singer bob }",
            "score {\n   staff lead\n   lyrics words\n   \n}");
        var (server, uri) = Opened(source);
        int at = source.IndexOf("lyrics words\n   ", StringComparison.Ordinal) + "lyrics words\n   ".Length;
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(source, at);
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        Assert.Contains("singer", list!.Items.Select(i => i.Label));
        Assert.Contains("staff", list.Items.Select(i => i.Label));

        // …under a staff too, and not in the middle of a line, where the word would be the row's.
        string underStaff = source.Replace("   lyrics words\n   \n}", "   \n}");
        var (other, otherUri) = Opened(underStaff);
        int under = underStaff.IndexOf("staff lead\n   ", StringComparison.Ordinal) + "staff lead\n   ".Length;
        var (l2, c2) = LilySharpLanguageServer.GetLineAndCharacter(underStaff, under);
        Assert.Contains("singer", other.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = otherUri }, Position = new Position(l2, c2),
        })!.Items.Select(i => i.Label));

        int midLine = source.IndexOf("lyrics words", StringComparison.Ordinal) + "lyrics words".Length;
        string spaced = source.Insert(midLine, " ");
        var (third, thirdUri) = Opened(spaced);
        var (l3, c3) = LilySharpLanguageServer.GetLineAndCharacter(spaced, midLine + 1);
        Assert.DoesNotContain("singer", third.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = thirdUri }, Position = new Position(l3, c3),
        })!.Items.Select(i => i.Label));
    }

    [Fact]
    public void AfterSingerInAScore_TheNamedSingersAreListed_AndNotAtTheTopLevel()
    {
        string[] LabelsAt(string text, int offset)
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

        string inScore = Book.Replace("singer carol  singer bob }", "singer  }");
        Assert.Equal(["alice", "bob", "carol"], LabelsAt(inScore, inScore.IndexOf("singer  }", StringComparison.Ordinal) + 7));

        string atTop = Book + "\nsinger ";
        Assert.Empty(LabelsAt(atTop, atTop.Length));
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