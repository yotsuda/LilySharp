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

using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A sharp or a flat written the English way says what was meant, and the quick fix writes it
/// (owner's decision 2026-11-02). <c>c#</c> and <c>ab4</c> were errors that named nothing of the
/// kind, and <c>cb4</c> passed in silence as a cowbell on a treble staff.
/// </summary>
[Trait("Category", "Unit")]
public class PitchSpellingTests
{
    private static string Book(string music) =>
        "part m { clef treble }\nsection A { m { " + music + " } }\nform { A }\nscore { staff m }";

    private static System.Collections.Generic.List<Diagnostic> Said(string text)
    {
        var tree = SyntaxTree.Parse(text);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree))];
    }

    [Theory]
    [InlineData("c#4 d e f |", "cis")]
    [InlineData("c4 f# g a |", "fis")]
    [InlineData("<c# e>4 d e f |", "cis")]
    [InlineData("ab4 d e f |", "aes4")]
    [InlineData("bb16 d e f |", "bes16")]
    [InlineData("eb d e f |", "ees")]
    [InlineData("cb4 d e f |", "ces")]
    public void AnEnglishSpelling_NamesThePitch(string music, string pitch)
        => Assert.Contains(Said(Book(music)), d => d.Message.Contains($"did you mean the pitch '{pitch}'"));

    [Theory]
    [InlineData("c#4 d e f |", "#", "cis4 d e f |")]
    [InlineData("c4 f# g a |", "#", "c4 fis g a |")]
    [InlineData("<c# e>4 d e f |", "#", "<cis e>4 d e f |")]
    [InlineData("ab4 d e f |", "ab4", "aes4 d e f |")]
    [InlineData("bb16 d e f |", "bb16", "bes16 d e f |")]
    [InlineData("c4 eb d e |", "eb", "c4 ees d e |")]
    [InlineData("cb4 d e f |", "cb", "ces4 d e f |")]
    [InlineData("<c cb>4 d e f |", "cb", "<c ces>4 d e f |")]
    public void TheQuickFix_WritesIt_AndNothingIsLeftToSay(string music, string squiggled, string fixedMusic)
    {
        string text = Book(music);
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///spelling.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        int at = text.IndexOf(squiggled, text.IndexOf("section", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
        var (line, character) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, at);
        var fix = Assert.Single(server.GetCodeActions(new LilySharp.Lsp.Protocol.CodeActionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range
            {
                Start = new LilySharp.Lsp.Protocol.Position(line, character),
                End = new LilySharp.Lsp.Protocol.Position(line, character),
            },
        }) ?? [], a => a.Title.StartsWith("Write '", System.StringComparison.Ordinal));
        var edit = fix.Edit!.Changes!.Values.Single().Single();
        int Offset(LilySharp.Lsp.Protocol.Position p)
            => text.Split('\n').Take(p.Line).Sum(l => l.Length + 1) + p.Character;
        string after = text[..Offset(edit.Range.Start)] + edit.NewText + text[Offset(edit.Range.End)..];
        Assert.Equal(Book(fixedMusic), after);
        Assert.DoesNotContain(Said(after), d => d.Severity == DiagnosticSeverity.Error
            || d.Message.Contains("did you mean"));
    }

    [Theory]
    [InlineData("part k { clef percussion }\nsection A { k { cb4 sn bd hh | } }\nform { A }\nscore { staff k }")]
    [InlineData("part k { clef treble }\nsection A { k { drums { cb4 cb cb cb | } } }\nform { A }\nscore { staff k }")]
    public void ACowbell_InABookWithDrums_IsACowbell(string book)
        => Assert.DoesNotContain(Said(book), d => d.Code == DiagnosticCodes.CowbellWithNoDrumPart);

    [Fact]
    public void ACowbell_WithNoDrumAnywhere_IsAWarning_NotAnError_AndOnlyOnTheName()
    {
        string text = Book("cb4 d e f |");
        var report = Assert.Single(Said(text), d => d.Code == DiagnosticCodes.CowbellWithNoDrumPart);
        Assert.Equal(DiagnosticSeverity.Warning, report.Severity);
        Assert.Equal("cb", text[report.Span.Start..report.Span.End]);
    }

    [Fact]
    public void ACowbell_InAMelodyPart_WarnsThoughAnotherPartHasDrums()
    {
        string book = "part k { clef percussion }\npart m { clef treble }\n"
            + "section A { k { cb4 sn bd hh | } m { cb4 d e f | } }\nform { A }\nscore { staff m  staff k }";
        var report = Assert.Single(Said(book), d => d.Code == DiagnosticCodes.CowbellWithNoDrumPart);
        Assert.True(report.Span.Start > book.IndexOf(" m {", System.StringComparison.Ordinal), "the melody's, not the kit's");
    }

    [Theory]
    [InlineData("part kit { clef percussion }\nsection A { kit { cb4 cb cb cb | } }\nform { A }\nscore { staff kit }")]
    [InlineData("section A { drums { cb4 cb cb cb | } }\nform { A }\nscore { staff drums }")]
    [InlineData("phrase Bell { cb4 cb cb cb | }\npart k { clef percussion }\nsection A { k { Bell } }\nform { A }\nscore { staff k }")]
    public void ACowbell_WhereItMayBeOne_IsLeftAlone(string book)
        => Assert.DoesNotContain(Said(book), d => d.Code == DiagnosticCodes.CowbellWithNoDrumPart);

    [Fact]
    public void ACowbell_InAPhrase_WithNoDrumInTheBook_Warns()
        => Assert.Contains(
            Said("phrase Bell { cb4 d e f | }\npart m { clef treble }\nsection A { m { Bell } }\nform { A }\nscore { staff m }"),
            d => d.Code == DiagnosticCodes.CowbellWithNoDrumPart);

    [Fact]
    public void APhraseOfThatName_IsAPhrase()
        => Assert.DoesNotContain(
            Said("phrase ab4 { c4 d e f | }\npart m { clef treble }\nsection A { m { ab4 } }\nform { A }\nscore { staff m }"),
            d => d.Message.Contains("did you mean"));

    [Fact]
    public void AnotherStrayCharacter_IsSaidAsItWas()
        => Assert.Contains(Said(Book("c4 ; d e f |")), d => d.Message.StartsWith("Unexpected character ';'", System.StringComparison.Ordinal));

    [Fact]
    public void AChordSymbolsSharp_IsNotAPitch()
        => Assert.DoesNotContain(Said(Book("c4@chord(F#) d e f |")), d => d.Message.Contains("did you mean"));
}
