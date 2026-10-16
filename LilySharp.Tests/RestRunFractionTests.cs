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

using System.IO;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LYS2018 (<see cref="RestRunFractionValidator"/>): LilyPond's <c>R1*5/4</c> is the run
/// <c>R1*5</c> and the slash note <c>/4</c> to Lily#, and the report says so and names the
/// spelling Lily# has — which the editor's quick fix writes. Owner's decision 2026-10-17: the
/// fraction stays out of the grammar.
/// </summary>
[Trait("Category", "Unit")]
public class RestRunFractionTests
{
    private static string Book(string bars, string time = "5/4")
        => $"time {time}\npart a {{ clef treble }}\nsection A {{ a {{ {bars} }} }}\nform {{ A }}\nscore {{ staff a }}\n";

    private static LilySharp.Core.Syntax.Diagnostic[] Reports(string book)
        => [.. SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.RestRunFraction)];

    [Fact]
    public void AFractionGluedToTheCount_IsNamed_WithTheSpellingLilySharpHas()
    {
        string book = Book("c'4 d' e' f' g' | R1*5/4 | c'4 d' e' f' g' |");
        var report = Assert.Single(Reports(book));
        Assert.Equal("R1*5/4", book.Substring(report.Span.Start, report.Span.End - report.Span.Start));
        Assert.Contains("the rest run 'R1*5' and the slash note '/4'", report.Message);
        Assert.EndsWith("write 'R4*5'.", report.Message);
        Assert.Equal(LilySharp.Core.Syntax.DiagnosticSeverity.Error, report.Severity);
    }

    [Theory]
    // The meter's unit when it divides the length; else the reduced fraction's unit.
    [InlineData("R1*10/4", "5/4", "R4*10")]
    [InlineData("R1*5/4", "4/4", "R4*5")]
    [InlineData("R2*3/4", "4/4", "R8*3")]
    [InlineData("R4.*2/3", "4/4", "R4*1")]
    public void TheSpelling_IsTheRunOfTheSameLength(string run, string time, string spelled)
        => Assert.EndsWith($"write '{spelled}'.", Assert.Single(Reports(Book($"{run} |", time))).Message);

    [Fact]
    public void ALengthNoUnitWrites_GetsTheReadingAlone()
    {
        var report = Assert.Single(Reports(Book("R1*2/3 |", "4/4")));
        Assert.DoesNotContain("write '", report.Message);
        Assert.Contains("R4*5 is one 5/4 bar", report.Message);
    }

    [Fact]
    public void ASpacedSlash_AndARunWithNoFraction_AreNotReported()
    {
        Assert.Empty(Reports(Book("R1*5 /4 |")));
        Assert.Empty(Reports(Book("R4*5 | R4*40 |")));
        Assert.Empty(Reports(Book("c'4 d' e' f' g' | /4 /4 /4 /4 /4 |")));
    }

    [Fact]
    public void Suggest_WritesTheLength()
    {
        Assert.Equal("R4*5", RestRunFractionValidator.Suggest(1, 0, 5, 4, 4));
        Assert.Equal("R8*3", RestRunFractionValidator.Suggest(2, 0, 3, 4, 4));
        Assert.Equal("R2*5", RestRunFractionValidator.Suggest(1, 0, 10, 4, 0));
        Assert.Null(RestRunFractionValidator.Suggest(1, 0, 2, 3, 4));
    }

    // ---------------------------------------------------------------- the quick fix

    [Fact]
    public void TheEditor_OffersToWriteIt()
    {
        string text = Book("c'4 d' e' f' g' | R1*5/4 | c'4 d' e' f' g' |");
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///run.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        int offset = text.IndexOf("R1*5/4", System.StringComparison.Ordinal) + 2;
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var at = new Position(line, character);
        var action = Assert.Single(server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? [], a => a.Title == "Write 'R4*5'");
        var edit = action.Edit!.Changes!.Values.Single().Single();
        Assert.Equal("R4*5", edit.NewText);
        Assert.Equal(text.Replace("R1*5/4", "R4*5"),
            text[..OffsetOf(text, edit.Range.Start)] + edit.NewText + text[OffsetOf(text, edit.Range.End)..]);
    }

    private static int OffsetOf(string text, Position p)
    {
        int offset = 0;
        for (int line = 0; line < p.Line; line++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + p.Character;
    }
}
