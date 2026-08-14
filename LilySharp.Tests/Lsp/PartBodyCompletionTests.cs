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
using System.IO;
using LilySharp.Lsp.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// End-to-end guard on what completion offers DIRECTLY inside a <c>part { }</c> body:
/// only part properties and section scaffolds — never music (pitch letters, <c>break</c>,
/// …), which belong in a section's music, not the part header. Drives the real
/// <see cref="LilySharpLanguageServer.Completion"/> path (context detection + dispatch),
/// so a routing regression that sent the body to the music completions is caught here.
/// Regression: <c>part melody "Violin I" {</c> — the inline display name's closing quote
/// hid the part frame, routing the body to music completions.
/// </summary>
public class PartBodyCompletionTests
{
    private static string[] CompletionLabelsAt(string text, int offset)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///part.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        return list?.Items.Select(i => i.Label!).ToArray() ?? System.Array.Empty<string>();
    }

    [Theory]
    [InlineData("part melody {\n  ")]                 // plain part header
    [InlineData("part melody \"Violin I\" {\n  ")]    // with inline display name (the regressed case)
    public void PartBody_OffersProperties_NeverNotesOrBreak(string doc)
    {
        var labels = CompletionLabelsAt(doc, doc.Length);

        // It IS the part-property list (a positive check, so the test can't pass by the
        // completion silently returning nothing).
        Assert.Contains("clef", labels);

        // Music items must not leak into a part header.
        Assert.DoesNotContain("break", labels);
        foreach (var pitch in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(pitch, labels);
    }

    [Fact]
    public void PartMajorInnerSection_StillOffersMusic()
    {
        // Control: inside `part melody "…" { section A { ▮ } }` the caret IS in music,
        // so pitch letters SHOULD be offered — the fix must not over-suppress.
        var doc = "part melody \"Violin I\" {\n  section A {\n    ";
        var labels = CompletionLabelsAt(doc, doc.Length);
        Assert.Contains("c", labels);
    }
}
