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
/// textDocument/references (Find All References) and textDocument/documentHighlight
/// must list/mark every occurrence of a symbol — declaration and references — across
/// every named namespace, using the same occurrence model as Rename and Go to
/// Definition. Highlight tags the declaration as Write, references as Read.
/// </summary>
public class FindReferencesTests
{
    private const string Source =
        "part tune {\n" +
        "  section Verse { riff riff }\n" +
        "}\n" +
        "phrase riff {\n" +
        "  c4 d e f\n" +
        "}\n" +
        "lyrics singwords { section Verse { la la la la } }\n" +
        "chords harm { section Verse { c1 } }\n" +
        "form whole { Verse }\n" +
        "score whole {\n" +
        "  staff tune  lyrics singwords\n" +
        "  chords harm\n" +
        "}\n";

    private static LilySharpLanguageServer Open(string text, out System.Uri uri)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        uri = new System.Uri("file:///refs.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return server;
    }

    private static Location[] Refs(string text, int offset, bool includeDeclaration = true)
    {
        var server = Open(text, out var uri);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return server.References(new ReferenceParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
            Context = new ReferenceContext { IncludeDeclaration = includeDeclaration },
        }) ?? System.Array.Empty<Location>();
    }

    private static DocumentHighlight[] Highlights(string text, int offset)
    {
        var server = Open(text, out var uri);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return server.GetDocumentHighlight(new DocumentHighlightParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        }) ?? System.Array.Empty<DocumentHighlight>();
    }

    private static int ToOffset(string text, int line, int character)
    {
        int offset = 0;
        for (int l = 0; l < line; l++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + character;
    }

    private static void AssertAllCover(string text, Location[] locs, string name, int expectedCount)
    {
        Assert.Equal(expectedCount, locs.Length);
        foreach (var loc in locs)
        {
            int start = ToOffset(text, loc.Range.Start.Line, loc.Range.Start.Character);
            Assert.Equal(name, text.Substring(start, name.Length));
        }
    }

    [Theory]
    [InlineData("tune", 2)]        // part decl + staff target
    [InlineData("Verse", 4)]       // section decl ×3 (part/lyrics/chords tracks) + form ref
    [InlineData("whole", 2)]       // form decl + score ref
    [InlineData("singwords", 2)]   // lyrics block + with-lyrics clause
    [InlineData("harm", 2)]        // chords block + chords row
    [InlineData("riff", 3)]        // phrase decl + two bare refs
    public void References_ListEveryOccurrence_AcrossNamespaces(string name, int expected)
        => AssertAllCover(Source, Refs(Source, Source.IndexOf(name)), name, expected);

    [Fact]
    public void References_ExcludeDeclaration_WhenNotRequested()
    {
        // riff: phrase decl + 2 refs; excluding the declaration leaves the 2 refs.
        var withDecl = Refs(Source, Source.IndexOf("phrase riff") + "phrase ".Length, includeDeclaration: true);
        var noDecl = Refs(Source, Source.IndexOf("phrase riff") + "phrase ".Length, includeDeclaration: false);
        Assert.Equal(3, withDecl.Length);
        Assert.Equal(2, noDecl.Length);
    }

    [Fact]
    public void References_FromAReference_FindsTheWholeSet()
        // From a `riff` USE, not the declaration.
        => AssertAllCover(Source, Refs(Source, Source.IndexOf("riff riff")), "riff", 3);

    [Fact]
    public void References_CaretNotOnASymbol_IsEmpty()
        => Assert.Empty(Refs(Source, Source.IndexOf("c4")));

    [Fact]
    public void DocumentHighlight_MarksDeclarationWrite_ReferencesRead()
    {
        var highlights = Highlights(Source, Source.IndexOf("riff riff"));
        Assert.Equal(3, highlights.Length);
        Assert.Equal(1, highlights.Count(h => h.Kind == DocumentHighlightKind.Write)); // phrase riff
        Assert.Equal(2, highlights.Count(h => h.Kind == DocumentHighlightKind.Read));  // riff riff
    }

    [Fact]
    public void DocumentHighlight_CoversLyricsAttachment()
    {
        // The lyrics block name (Write) + the `with lyrics singwords` clause (Read).
        var highlights = Highlights(Source, Source.LastIndexOf("singwords"));
        Assert.Equal(2, highlights.Length);
        Assert.Equal(1, highlights.Count(h => h.Kind == DocumentHighlightKind.Write));
        Assert.Equal(1, highlights.Count(h => h.Kind == DocumentHighlightKind.Read));
    }
}
