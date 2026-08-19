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
/// textDocument/rename must rewrite every occurrence — declaration AND references —
/// of a symbol, across every named namespace: part, section, form, lyrics/chords
/// block, and phrase (whose declaration is `phrase NAME`, not `NAME = …`, and so was
/// missed by the old variable-only path). The namespace dispatch mirrors Go to
/// Definition so the two never disagree about what a name means at a position.
/// </summary>
public class RenameSymbolTests
{
    // One document exercising every renameable namespace. Names are distinct and
    // non-substring so occurrence counting is unambiguous. The section `Verse` is
    // declared once per part-major track (part / lyrics / chords) plus referenced by
    // the form — all four must rename together.
    private const string Source =
        "part tune {\n" +
        "  section Verse { riff riff }\n" +           // 2 phrase refs; section decl (1/4)
        "}\n" +
        "phrase riff {\n" +                            // phrase declaration
        "  c4 d e f\n" +
        "}\n" +
        "lyrics singwords { section Verse { la la la la } }\n" +  // lyrics decl; section decl (2/4)
        "chords harm { section Verse { c1 } }\n" +               // chords decl; section decl (3/4)
        "form whole { Verse }\n" +                    // form decl; section ref (4/4)
        "score whole {\n" +                           // form ref
        "  staff tune  lyrics singwords\n" +      // part ref + lyrics ref
        "  chords harm\n" +                           // chords row ref
        "}\n";

    private static TextEdit[] RenameEditsAt(string text, int offset, string newName = "NEW")
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///rename.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var edit = server.Rename(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
            NewName = newName,
        });
        if (edit?.Changes == null) return System.Array.Empty<TextEdit>();
        return edit.Changes.Values.SelectMany(e => e).ToArray();
    }

    // (line, character) -> byte offset, for the \n-delimited test sources.
    private static int ToOffset(string text, int line, int character)
    {
        int offset = 0;
        for (int l = 0; l < line; l++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + character;
    }

    // Every edit must land exactly on an occurrence of `name`, and the edits must be
    // distinct — no double-count, none straying onto a similar substring.
    private static void AssertRenames(string text, int caretOffset, string name, int expectedCount)
    {
        var edits = RenameEditsAt(text, caretOffset);
        Assert.Equal(expectedCount, edits.Length);
        var starts = new System.Collections.Generic.HashSet<int>();
        foreach (var e in edits)
        {
            int start = ToOffset(text, e.Range.Start.Line, e.Range.Start.Character);
            Assert.Equal(name, text.Substring(start, name.Length));
            Assert.True(starts.Add(start), $"duplicate edit at offset {start}");
        }
    }

    [Fact]
    public void Part_FromDeclaration_RenamesDeclarationAndStaffReference()
        => AssertRenames(Source, Source.IndexOf("tune"), "tune", 2);

    [Fact]
    public void Part_FromReference_RenamesEveryOccurrence()
        => AssertRenames(Source, Source.LastIndexOf("tune"), "tune", 2);

    [Fact]
    public void Section_RenamesEveryTrackDeclarationAndTheFormReference()
        // section Verse declared in part + lyrics + chords tracks, referenced by the form.
        => AssertRenames(Source, Source.IndexOf("Verse"), "Verse", 4);

    [Fact]
    public void Form_RenamesDeclarationAndScoreReference()
        => AssertRenames(Source, Source.IndexOf("whole"), "whole", 2);

    [Fact]
    public void Lyrics_FromBlock_RenamesBlockAndWithClause()
        => AssertRenames(Source, Source.IndexOf("singwords"), "singwords", 2);

    [Fact]
    public void Lyrics_FromWithClause_RenamesBlockAndWithClause()
        => AssertRenames(Source, Source.LastIndexOf("singwords"), "singwords", 2);

    [Fact]
    public void Chords_RenamesBlockAndRowReference()
        => AssertRenames(Source, Source.IndexOf("harm"), "harm", 2);

    [Fact]
    public void Phrase_FromDeclaration_RenamesDeclarationAndBareReferences()
        // `phrase riff` + two bare `riff` references inside section Verse.
        => AssertRenames(Source, Source.IndexOf("phrase riff") + "phrase ".Length, "riff", 3);

    [Fact]
    public void Phrase_FromReference_RenamesDeclarationAndBareReferences()
        => AssertRenames(Source, Source.IndexOf("riff riff"), "riff", 3);

    [Fact]
    public void CaretNotOnASymbol_ReturnsNoEdits()
        => Assert.Empty(RenameEditsAt(Source, Source.IndexOf("c4")));
}
