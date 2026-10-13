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
/// LilyPond's script shorthands (<c>c4-.</c>, <c>-&gt;</c>, <c>--</c>, <c>-^</c>, <c>-_</c>,
/// <c>-!</c>) are reported ONCE each, naming the annotation Lily# writes (grammar feedback B8,
/// 2026-10-12). They used to draw two or three unrelated errors apiece, and <c>-!</c> closed
/// the bar with a dashed barline.
/// </summary>
[Trait("Category", "Unit")]
public class LilyPondScriptShorthandTests
{
    private static string Book(string music) =>
        "part m { clef treble }\nsection A { m { " + music + " } }\nform { A }\nscore { staff m }";

    [Theory]
    [InlineData("c'4-. d e f |", "-.", "@staccato")]
    [InlineData("c'4-> d e f |", "->", "@accent")]
    [InlineData("c'4-- d e f |", "--", "@tenuto")]
    [InlineData("c'4-^ d e f |", "-^", "@marcato")]
    [InlineData("c'4-_ d e f |", "-_", "@portato")]
    [InlineData("c'4-! d e f |", "-!", "@staccatissimo")]
    public void AShorthand_IsOneError_NamingTheAnnotation(string music, string written, string annotation)
    {
        var tree = SyntaxTree.Parse(Book(music));
        var all = tree.Diagnostics.Concat(SemanticValidation.Run(tree)).ToList();
        var error = Assert.Single(all, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains($"'{written}'", error.Message);
        Assert.Contains(annotation, error.Message);
        // Nothing else is said about the bar — `-!` is not a dashed barline here.
        Assert.DoesNotContain(all, d => d.Severity == DiagnosticSeverity.Warning);
        // Both characters stay in the tree, so every later position is true.
        Assert.Equal(Book(music), tree.GetRoot().ToFullString());
    }

    // The quick fix writes the annotation the error names, glued to its note (the B8 leftover,
    // 2026-10-13): the book that comes out has no error left.
    [Theory]
    [InlineData("c'4-. d e f |", "c'4@staccato d e f |")]
    [InlineData("c'4-! d e f |", "c'4@staccatissimo d e f |")]
    [InlineData("c'4 -- d e f |", "c'4@tenuto d e f |")]
    public void TheQuickFix_WritesTheAnnotation(string music, string fixedMusic)
    {
        string text = Book(music);
        var server = new LilySharp.Lsp.LilySharpLanguageServer(System.IO.Stream.Null, System.IO.Stream.Null);
        var uri = new System.Uri("file:///shorthand.lys");
        server.DidOpen(new LilySharp.Lsp.Protocol.DidOpenTextDocumentParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        int at = text.IndexOf('-', System.StringComparison.Ordinal) + 1;
        var (line, character) = LilySharp.Lsp.LilySharpLanguageServer.GetLineAndCharacter(text, at);
        var fix = Assert.Single(server.GetCodeActions(new LilySharp.Lsp.Protocol.CodeActionParams
        {
            TextDocument = new LilySharp.Lsp.Protocol.TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range
            {
                Start = new LilySharp.Lsp.Protocol.Position(line, character),
                End = new LilySharp.Lsp.Protocol.Position(line, character),
            },
        }) ?? [], a => a.Title.StartsWith("Write '@", System.StringComparison.Ordinal));
        var edit = fix.Edit!.Changes!.Values.Single().Single();
        int Offset(LilySharp.Lsp.Protocol.Position p)
            => text.Split('\n').Take(p.Line).Sum(l => l.Length + 1) + p.Character;
        string after = text[..Offset(edit.Range.Start)] + edit.NewText + text[Offset(edit.Range.End)..];
        Assert.Equal(Book(fixedMusic), after);
        var tree = SyntaxTree.Parse(after);
        Assert.DoesNotContain(tree.Diagnostics.Concat(SemanticValidation.Run(tree)), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AMinusWithSpaceAfterIt_IsStillJustAStrayMinus()
    {
        var tree = SyntaxTree.Parse(Book("c'4- . d e f |"));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Message.Contains("shorthand"));
    }
}
