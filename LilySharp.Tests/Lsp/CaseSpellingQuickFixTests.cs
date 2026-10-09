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

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The quick fix for a spelling that differs from a real one only in case (HANDOFF §1.0,
/// 2026-09-29): every "… case-sensitive: write 'X'" hint — an annotation name, a value, a chord
/// shape word, a layout key or value — offers X for the squiggled text, and the warning is gone
/// once the edit is applied. Offered only where the squiggled text IS X in another case; a
/// typo's "Did you mean" is not a case hint and gets no such offer.
/// </summary>
public class CaseSpellingQuickFixTests
{
    private static CodeAction[] ActionsAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///case.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var at = new Position(line, character);
        return server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? [];
    }

    private static CodeAction? WriteAction(string text, string caretAt)
        => ActionsAt(text, text.IndexOf(caretAt, System.StringComparison.Ordinal) + 1)
            .SingleOrDefault(a => a.Title.StartsWith("Write '", System.StringComparison.Ordinal));

    /// <summary>Applies the action's single edit and returns the edited text.</summary>
    private static string Apply(string text, CodeAction action)
    {
        var edit = action.Edit!.Changes!.Values.Single().Single();
        int start = OffsetOf(text, edit.Range.Start);
        int end = OffsetOf(text, edit.Range.End);
        return text.Substring(0, start) + edit.NewText + text.Substring(end);
    }

    private static int OffsetOf(string text, Position p)
    {
        int offset = 0;
        for (int line = 0; line < p.Line; line++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + p.Character;
    }

    private static int CaseHints(string text)
        => SemanticValidation.Run(SyntaxTree.Parse(text)).Count(d => d.Message.Contains("case-sensitive: write"));

    private static string Book(string music, string layout = "") => $$"""
        {{layout}}part gt { clef treble }
        section A { gt { {{music}} } chords prog { C(X32010) | } }
        form { A }
        score { chords prog  staff gt }
        """;

    [Theory]
    [InlineData("c'4@upbow d e f |", "@upbow", "@upBow")]                     // a name
    [InlineData("c'4@ottava(BASSA) d e f@!ottava |", "@ottava(BASSA)", "@ottava(bassa)")]   // a value
    [InlineData("c'4@Mark(\"A\") d e f |", "@Mark", "@mark(\"A\")")]          // a name with its argument kept
    public void AWrongCaseAnnotation_IsOfferedItsSpelling_AndTheWarningGoes(string music, string caret, string wanted)
    {
        string text = Book(music);
        var action = WriteAction(text, caret);
        Assert.NotNull(action);
        Assert.Equal($"Write '{wanted}'", action!.Title);
        string fixedText = Apply(text, action);
        Assert.Contains(wanted, fixedText);
        Assert.DoesNotContain(caret, fixedText);
        // The row's X32010 hint is the other one in the book: one hint left after this fix.
        Assert.Equal(2, CaseHints(text));
        Assert.Equal(1, CaseHints(fixedText));
    }

    [Fact]
    public void AWrongCaseShapeWord_AndALayoutKey_AreOfferedTheirSpellings()
    {
        string text = Book("c'1 |", "layout { ChordDiagrams guitar }\n");
        var shape = WriteAction(text, "X32010");
        Assert.NotNull(shape);
        Assert.Equal("Write 'x32010'", shape!.Title);
        Assert.Contains("C(x32010)", Apply(text, shape));
        var key = WriteAction(text, "ChordDiagrams");
        Assert.NotNull(key);
        Assert.Equal("Write 'chordDiagrams'", key!.Title);
        Assert.Contains("layout { chordDiagrams guitar }", Apply(text, key));
    }

    [Fact]
    public void ATypo_GetsNoSpellingOffer()
    {
        string text = Book("c'4@upbw d e f |");
        Assert.Null(WriteAction(text, "@upbw"));
    }
}
