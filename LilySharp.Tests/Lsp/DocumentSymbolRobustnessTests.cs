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
using LilySharp.Lsp.Protocol;
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

    // ---- GetLineAndCharacter: the offset -> (line, character) converter ----
    // A node boundary landing exactly on the '\n' of a CRLF pair used to make the
    // converter overshoot lastLineStart past the offset and return character = -1 —
    // the "Illegal argument: character must be non-negative" that aborts documentSymbol
    // on CRLF files (which is every file saved on Windows).

    [Fact]
    public void GetLineAndCharacter_OnTheLfOfACrlf_IsNotNegative()
    {
        // "ab\r\ncd": offset 3 is the '\n' (between '\r' at 2 and 'c' at 4).
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter("ab\r\ncd", 3);
        Assert.True(character >= 0, $"character was {character}");
        Assert.Equal(1, line);
        Assert.Equal(0, character);
    }

    [Theory]
    [InlineData("ab\r\ncd", 0, 0, 0)]   // start
    [InlineData("ab\r\ncd", 2, 0, 2)]   // the '\r' — end of line 0
    [InlineData("ab\r\ncd", 3, 1, 0)]   // the '\n' — the crash case, now column 0
    [InlineData("ab\r\ncd", 4, 1, 0)]   // 'c' — start of line 1 (CRLF counted once)
    [InlineData("ab\r\ncd", 5, 1, 1)]   // 'd'
    [InlineData("ab\ncd", 3, 1, 0)]     // plain LF still maps correctly
    [InlineData("x\r\ny\r\nz", 6, 2, 0)] // 'z' after two CRLFs — lines counted once each
    public void GetLineAndCharacter_MapsCrlfAndLf(string text, int offset, int line, int character)
    {
        var (l, c) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        Assert.Equal((line, character), (l, c));
    }
}
