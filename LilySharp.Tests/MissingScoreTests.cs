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
using LilySharp.Core.Editing;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A book with music and no <c>score</c> is LYS6014 (owner's decision 2026-10-09): the score is
/// the one place a book says what is engraved, and a score-less book used to render its FIRST
/// part alone and drop the rest in silence. The quick fix appends <see cref="ScoreSkeleton"/>'s
/// score; a file with no section (a library of phrases or part headers) is not reported.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MissingScoreTests
{
    private static LilySharp.Core.Syntax.Diagnostic[] Missing(string source)
        => SemanticValidation.Run(SyntaxTree.Parse(source))
            .Where(d => d.Code == DiagnosticCodes.MissingScore).ToArray();

    [Fact]
    public void ABookWithMusicAndNoScore_IsOneError_NamingTheScoreToWrite()
    {
        var d = Assert.Single(Missing(
            "part melody { clef treble }\npart bass { clef bass }\n"
            + "section A { melody { c'4 d' e' f' | } bass { c4 d e f | } }\nform { A }\n"));
        Assert.Equal(LilySharp.Core.Syntax.DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("score { staff melody  staff bass }", d.Message, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("phrase motif { c'4 d' e' f' }\n")]
    [InlineData("part melody { clef treble }\nphrase motif { c'4 d' e' f' }\n")]
    [InlineData("part melody { clef treble }\nsection A { melody { c'4 d' e' f' | } }\nscore { staff melody }\n")]
    public void ALibraryOrABookWithAScore_IsNotReported(string source)
        => Assert.Empty(Missing(source));

    [Theory]
    [InlineData("section A { c'4 d' e' f' | }\n", "score { staff }")]
    [InlineData("part { clef bass }\nsection A { c4 d e f | }\n", "score { staff }")]
    [InlineData("part melody { clef treble }\nchords prog { section A { C | } }\n"
        + "lyrics words sings melody { section A { la la la la | } }\nsection A { melody { c'4 d' e' f' | } }\n",
        "score { chords prog  staff melody  lyrics words }")]
    [InlineData("part b { section A { c4 d e f | } }\npart a { section A { c'4 d' e' f' | } }\n",
        "score { staff b  staff a }")]
    public void TheSkeleton_StacksChordsPartsAndLyrics_InDeclarationOrder(string source, string expected)
        => Assert.Equal(expected, ScoreSkeleton.For(SyntaxTree.Parse(source).GetRoot()));

    [Fact]
    public void TheQuickFix_AppendsTheScore_AndTheBookIsClean()
    {
        const string text = "part melody { clef treble }\npart bass { clef bass }\n"
            + "section A { melody { c'4 d' e' f' | } bass { c4 d e f | } }\nform { A }\n";
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///no-score.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        int at = text.IndexOf("section", System.StringComparison.Ordinal) + 2;
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, at);
        var fix = Assert.Single(server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = new Position(line, character), End = new Position(line, character) },
        }) ?? [], a => a.Title.StartsWith("Add score", System.StringComparison.Ordinal));

        var edit = fix.Edit!.Changes!.Values.Single().Single();
        Assert.Equal(text.Length, LineOffset(text, edit.Range.Start));
        string after = text + edit.NewText;

        Assert.EndsWith("\nscore { staff melody  staff bass }\n", after, System.StringComparison.Ordinal);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(after)).Where(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error));
    }

    private static int LineOffset(string text, Position p)
    {
        int offset = 0;
        for (int l = 0; l < p.Line; l++)
            offset = text.IndexOf('\n', offset) + 1;
        return offset + p.Character;
    }
}
