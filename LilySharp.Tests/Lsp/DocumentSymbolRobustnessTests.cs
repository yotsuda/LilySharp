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
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// documentSymbol must survive malformed / mid-edit source. VS Code rejects any Position
/// with a negative character, and one bad range aborts the ENTIRE outline
/// ("Illegal argument: character must be non-negative"). A malformed or synthetic node can
/// report a span outside the document, so GetLineAndCharacter clamps the offset to
/// [0, text.Length]; these pin that the handler never throws and never emits a negative
/// position for a range of broken inputs.
/// </summary>
public class DocumentSymbolRobustnessTests
{
    private static DocumentSymbol[]? Symbols(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///broken.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return server.DocumentSymbol(new DocumentSymbolParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
        });
    }

    private static void AssertNonNegative(DocumentSymbol[]? symbols)
    {
        if (symbols == null) return;
        foreach (var s in symbols)
        {
            Assert.True(s.Range.Start.Character >= 0, $"start char < 0 for {s.Name}");
            Assert.True(s.Range.Start.Line >= 0, $"start line < 0 for {s.Name}");
            Assert.True(s.Range.End.Character >= 0, $"end char < 0 for {s.Name}");
            Assert.True(s.SelectionRange.Start.Character >= 0, $"selection char < 0 for {s.Name}");
            AssertNonNegative(s.Children);
        }
    }

    [Theory]
    [InlineData("")]                                        // empty
    [InlineData("part m { section A { c'4 ")]               // unterminated braces at EOF
    [InlineData("form main { ")]                            // unterminated form
    [InlineData(">> <> {} \\ @ |")]                         // stray operator tokens, no music
    [InlineData("\r")]                                      // lone classic-Mac CR
    [InlineData("part\npart\npart")]                        // keywords with no bodies
    [InlineData("section")]                                 // bare keyword, missing name + body
    [InlineData("key major\ntempo\ntime\nclef")]           // header keywords missing operands
    [InlineData("part m { << r e c ")]                      // unterminated arpeggio
    [InlineData("\n\n\n   \t  \n")]                         // whitespace only
    public void DocumentSymbol_OnMalformedInput_DoesNotThrowAndStaysNonNegative(string text)
    {
        var symbols = Symbols(text); // must not throw
        AssertNonNegative(symbols);
    }

    [Fact]
    public void DocumentSymbol_OnWellFormedScore_ListsTheLandmarks()
    {
        var symbols = Symbols("""
            title "T"
            part melody { section A { c'4 d' e' } }
            form main { A }
            score main { staff melody }
            """);
        Assert.NotNull(symbols);
        AssertNonNegative(symbols);
        Assert.NotEmpty(symbols!);
    }
}
