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
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The completion's lists are functions of a document's TEXT, and those that need its tree
/// parsed the whole text again — seven of them, one parse for each list asked, beside the tree
/// the document already had (review of session 1291). They now ask <see cref="Document.TreeOf"/>:
/// the document's own tree when the text is a document's, a parse when it is anyone else's.
/// Counted, not timed. The poison: <c>SyntaxTree.Parse(text)</c> back in any of them.
/// </summary>
public class CompletionTreeTests
{
    private const string Book = """
        part gt "Guitar"
        part m
        chordShapes { C x32010  section A { F 133211 } }
        capo 0
        section A {
          chords { F | C | Bb | }
          m { c'1 | c'1 | c'1 | }
        }
        section B {
          m { c'1 | }
        }
        form { A B }
        score { staff m }

        """;

    [Fact]
    public void TheListsThatReadTheTree_ReadTheDocumentsOwn_AndParseNothing()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///completion-tree.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = new string(Book.AsSpan()), Version = 1, LanguageId = "lilysharp" },
        });
        string text = server.DocumentAt(uri)!.Text;
        int end = text.Length;
        int inChords = text.IndexOf("F | C", StringComparison.Ordinal);
        var lists = new (string Name, Func<string, CompletionList> Of)[]
        {
            ("the capo's frets", t => LilySharpLanguageServer.GetChordDiagramCapoCompletions(t)),
            ("the shape table's chords", t => LilySharpLanguageServer.GetChordDiagramTableCompletions(t, end)),
            ("the shape table's sections", t => LilySharpLanguageServer.GetChordDiagramTableSectionCompletions(t, end)),
            ("the sections not written yet", t => LilySharpLanguageServer.GetMissingSectionNameCompletions(t, end)),
            ("a lyrics track's sections", t => LilySharpLanguageServer.GetLyricsSectionCompletions(t, end)),
            ("a chords track's cells", t => LilySharpLanguageServer.GetChordsTrackCompletions(t, end)),
            // (In a block that writes no section the list asks the tree whether the file is
            // written by sections at all — its own read, which the one above never reaches.)
            ("a chords track's cells, in a block with no section", t => LilySharpLanguageServer.GetChordsTrackCompletions(t, inChords)),
            ("a part's block", t => LilySharpLanguageServer.GetPartBlockCompletions(t, end)),
            ("a section's block", t => LilySharpLanguageServer.GetSectionBlockCompletions(t, end)),
        };
        foreach (var (name, of) in lists)
        {
            // The same text in another string is no document's: its list parses it — so the list
            // does read a tree — and is the same list.
            int before = SyntaxTree.FullParsesOnThisThread;
            var parsed = of(new string(text.AsSpan()));
            Assert.True(SyntaxTree.FullParsesOnThisThread > before, $"{name}: reads no tree here, the net says nothing");

            before = SyntaxTree.FullParsesOnThisThread;
            var own = of(text);
            Assert.True(SyntaxTree.FullParsesOnThisThread == before, $"{name}: parsed the document's text again");
            Assert.Equal(parsed.Items.Select(i => (i.Label, i.InsertText, i.Detail)),
                own.Items.Select(i => (i.Label, i.InsertText, i.Detail)));
        }
    }

    [Fact]
    public void ACompletionAsked_OfTheServer_ParsesNothing()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///completion-tree-asked.lys");
        string text = Book.Replace("section B {\n  m { c'1 | }\n}", "section B {\n  \n}").Replace("\r\n", "\n");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        // The caret on the empty line of section B's body: the parts to write a cell for.
        int at = text.IndexOf("section B {\n  ", StringComparison.Ordinal) + "section B {\n  ".Length;
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, at);
        int before = SyntaxTree.FullParsesOnThisThread;
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        Assert.Equal(0, SyntaxTree.FullParsesOnThisThread - before);
        Assert.Contains(list!.Items, i => i.Label == "m");
    }
}
