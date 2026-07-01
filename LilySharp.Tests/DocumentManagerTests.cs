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

using LilySharp.Lsp;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Xunit;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace LilySharp.Tests;

/// <summary>
/// LSP document synchronization: sequential application of incremental
/// change batches (LSP spec: each Range refers to the state AFTER the
/// previous change), and the position→offset conversion with spec clamping.
/// </summary>
[Trait("Category", "Unit")]
public class DocumentManagerTests
{
    private static readonly Uri TestUri = new("file:///test.lys");

    private static TextDocumentContentChangeEvent Change(
        int startLine, int startChar, int endLine, int endChar, string text) => new()
    {
        Range = new LspRange
        {
            Start = new Position(startLine, startChar),
            End = new Position(endLine, endChar),
        },
        Text = text,
    };

    [Fact]
    public void ApplyChanges_SequentialBatch_AppliesInOrder()
    {
        var mgr = new DocumentManager();
        mgr.OpenOrUpdate(TestUri, "{ c4 d e f | }", version: 1);

        // Two changes in one didChange batch; the second range refers to the
        // text state AFTER the first change was applied.
        var doc = mgr.ApplyChanges(TestUri, new[]
        {
            Change(0, 2, 0, 4, "g8"),   // "{ c4 d e f | }" → "{ g8 d e f | }"
            Change(0, 5, 0, 6, "a"),    // → "{ g8 a e f | }"
        }, version: 2);

        Assert.Equal("{ g8 a e f | }", doc.Text);
        Assert.Equal(2, doc.Version);
        Assert.Equal(doc.Text, doc.Tree.Text);
    }

    [Fact]
    public void ApplyChanges_FullReplacement_ReplacesText()
    {
        var mgr = new DocumentManager();
        mgr.OpenOrUpdate(TestUri, "{ c4 | }", version: 1);

        var doc = mgr.ApplyChanges(TestUri,
            new[] { new TextDocumentContentChangeEvent { Text = "{ e4 f g a | }" } },
            version: 2);

        Assert.Equal("{ e4 f g a | }", doc.Text);
    }

    [Fact]
    public void ApplyChanges_MultiLineEdit_DeletesAcrossNewline()
    {
        var mgr = new DocumentManager();
        mgr.OpenOrUpdate(TestUri, "{ c4 |\nd4 | }", version: 1);

        // Join the two lines: delete from end of line 0 to start of line 1.
        var doc = mgr.ApplyChanges(TestUri, new[] { Change(0, 6, 1, 0, " ") }, version: 2);

        Assert.Equal("{ c4 | d4 | }", doc.Text);
    }

    [Fact]
    public void ApplyChanges_UnopenedDocument_DoesNotThrow_AppliesFromEmpty()
    {
        // A didChange before didOpen is a client protocol violation. ApplyChanges must
        // not throw (previously InvalidOperationException): it starts from an empty
        // document and applies the edit best-effort.
        var mgr = new DocumentManager();

        var doc = mgr.ApplyChanges(TestUri, new[] { Change(0, 0, 0, 0, "c4") }, version: 3);

        Assert.Equal("c4", doc.Text);
        Assert.Equal(3, doc.Version);
    }

    [Theory]
    // Character beyond the line length clamps to the line end (LSP spec).
    [InlineData(0, 99, 6)]
    // Negative character clamps to the line start.
    [InlineData(0, -5, 0)]
    // Line beyond the text clamps to the text end.
    [InlineData(99, 0, 13)]
    public void GetOffset_OutOfRange_IsClamped(int line, int character, int expected)
    {
        // "abcdef\nghijkl" — line 0 ends at offset 6, text ends at 13.
        int offset = DocumentManager.GetOffset("abcdef\nghijkl",
            new Position(line, character));
        Assert.Equal(expected, offset);
    }

    [Fact]
    public void GetOffset_CrLfLineEndings()
    {
        // "ab\r\ncd" — line 1 starts at offset 4.
        int offset = DocumentManager.GetOffset("ab\r\ncd", new Position(1, 1));
        Assert.Equal(5, offset);
    }
}
