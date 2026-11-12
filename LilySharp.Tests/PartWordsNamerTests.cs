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
using LilySharp.Core.Editing;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// "Name these words on the part" (owner's decision 2026-11-12): the part's own
/// <c>lyrics NAME</c> is the standard place to say whose words they are, and the editor writes
/// it from a singer line or a lyrics block that says so only elsewhere.
/// </summary>
[Trait("Category", "Unit")]
public class PartWordsNamerTests
{
    private static string Book(string part, string singer = "singer female sings words on lead") => $$"""
        {{part}}
        section A {
          lead { c'4 d e f | }
          lyrics words { la la la la | }
        }
        {{singer}}
        form { A }
        score { staff lead  lyrics words }
        """;

    private static string? Named(string source, string at)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        if (PartWordsNamer.EditAt(tree, source, source.IndexOf(at, System.StringComparison.Ordinal) + 1) is not { } edit)
            return null;
        Assert.Equal("Name these words on the part: part lead { lyrics words }", edit.Title);
        string after = source[..edit.Start] + edit.NewText + source[edit.End..];
        // The file is sound, the part names the words, and nothing else moved.
        var written = SyntaxTree.Parse(after);
        Assert.False(written.HasErrors, string.Join("\n", written.Diagnostics.Select(d => d.Message)));
        Assert.Empty(SemanticValidation.Run(written).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        Assert.Equal(["words"], LyricBindings.TracksOfPart(written.GetRoot(), "lead"));
        return after;
    }

    [Theory]
    [InlineData("part lead { instrument soprano }", "part lead { instrument soprano  lyrics words }")]
    [InlineData("part lead {}", "part lead {  lyrics words }")]
    [InlineData("part lead", "part lead { lyrics words }")]
    [InlineData("part lead {\n  instrument soprano\n}", "part lead {\n  instrument soprano\n  lyrics words\n}")]
    public void TheWordsAreWrittenIntoThePartsHeader(string part, string expected)
    {
        string source = Book(part);
        Assert.Equal(Book(expected), Named(source, "singer female"));
        // From the lyrics block's head too: the part a score prints the words under.
        Assert.Equal(Book(expected), Named(source, "lyrics words {"));
    }

    // Owner's note 2026-11-12: the grammar is one for both writings - a part that holds its own
    // sections (by part) names its words in the same header, with its properties.
    [Theory]
    [InlineData("part lead {\n  instrument soprano\n  section A { c'4 d e f | }\n}", "part lead {\n  instrument soprano\n  lyrics words\n  section A { c'4 d e f | }\n}")]
    [InlineData("part lead { instrument soprano  section A { c'4 d e f | } }", "part lead { instrument soprano  lyrics words  section A { c'4 d e f | } }")]
    public void InTheByPartWriting_TheWordsStandWithTheHeadersProperties(string part, string expected)
    {
        string Written(string p) => p + "\nlyrics words {\n  section A { la la la la | }\n}\nsinger female sings words on lead\nform { A }\nscore { staff lead  lyrics words }\n";
        string source = Written(part);
        Assert.Equal(Written(expected), Named(source, "singer female"));
        Assert.Equal(Written(expected), Named(source, "lyrics words {"));
    }

    [Fact]
    public void NothingIsOffered_WhereThereIsNothingToName()
    {
        // The part names them already.
        Assert.Null(Named(Book("part lead { lyrics words }"), "singer female"));
        // Words made from the notes.
        Assert.Null(Named(Book("part lead { }", "singer female sings hum on lead"), "singer female"));
        // Off a singer line and a lyrics head: in the music, on the score.
        Assert.Null(Named(Book("part lead { }"), "c'4"));
        Assert.Null(Named(Book("part lead { }"), "score {"));
    }

    [Fact]
    public void OnceNamed_TheSingerLineNeedsNoName()
    {
        string source = Book("part lead { instrument soprano  lyrics words }", "singer female sings on lead");
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
        Assert.Equal("words", Assert.Single(LyricSinger.LinesOf(tree.GetRoot())).Track);
    }
}