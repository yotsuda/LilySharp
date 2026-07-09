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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The completion-context brace scanners must ignore braces that sit inside string
/// literals and comments — otherwise a stray '{' in <c>title "a {b"</c> or a '//'
/// comment corrupts the detected block context. Covers both the false-open (a '{'
/// in a string/comment wrongly opening a block) and false-close (a '}' in a string
/// wrongly closing a real block) directions.
/// </summary>
[Trait("Category", "Unit")]
public class CompletionContextCommentStringTests
{
    [Fact]
    public void BraceInStringLiteral_DoesNotOpenBlock()
    {
        var doc = "title \"a { brace\"\n";
        Assert.Null(LilySharpLanguageServer.InnermostOpenBlock(doc, doc.Length));
    }

    [Fact]
    public void BraceInLineComment_DoesNotOpenBlock()
    {
        var doc = "// a comment with {\nc4 d e f\n";
        Assert.Null(LilySharpLanguageServer.InnermostOpenBlock(doc, doc.Length));
    }

    [Fact]
    public void BraceInBlockComment_DoesNotOpenBlock()
    {
        var doc = "/* a block comment { with brace */\n";
        Assert.Null(LilySharpLanguageServer.InnermostOpenBlock(doc, doc.Length));
    }

    [Fact]
    public void CloseBraceInString_DoesNotClosePartBlock()
    {
        // The '}' inside the quoted name must NOT close the part { } block.
        var doc = "part m {\n  name \"a } b\"\n  ";
        Assert.True(LilySharpLanguageServer.IsInsidePartBlock(doc, doc.Length));
    }

    [Fact]
    public void RealPartBlock_StillDetected()
    {
        // Control: a genuine open part block is still detected.
        var doc = "part m {\n  clef treble\n  ";
        Assert.True(LilySharpLanguageServer.IsInsidePartBlock(doc, doc.Length));
    }

    [Fact]
    public void ScoreBlock_NotFooledByBraceInString()
    {
        var doc = "title \"score { x\"\n";
        Assert.False(LilySharpLanguageServer.IsInsideScoreBlock(doc, doc.Length));
    }

    [Fact]
    public void FormatStrip_IgnoresBracesInStringsAndComments()
    {
        bool inBlock = false;
        // A '{' inside a string is blanked, so it drives no indentation.
        Assert.DoesNotContain('{', LilySharpLanguageServer.StripStringsAndComments("title \"a {\"", ref inBlock));
        // A real '{' behind a trailing // comment still counts.
        Assert.Contains('{', LilySharpLanguageServer.StripStringsAndComments("part m { // hi", ref inBlock));
        // A /* … */ block comment carries its state across lines.
        LilySharpLanguageServer.StripStringsAndComments("x /* open", ref inBlock);
        Assert.True(inBlock);
        var reopened = LilySharpLanguageServer.StripStringsAndComments("brace } here */ y", ref inBlock);
        Assert.False(inBlock);
        Assert.DoesNotContain('}', reopened); // the '}' was inside the block comment
    }

    [Fact]
    public void PercussionScan_NotFooledByBraceInString()
    {
        // The backward percussion scan must ignore a '}' inside a string; otherwise
        // the stray brace mis-pairs the braces and the drum part is not detected.
        var clean = "part dr { clef percussion }\nsection A { dr {\n  hihat4\n  ";
        var stray = "part dr { clef percussion }\nsection A { dr {\n  \"x } y\"\n  ";

        Assert.True(LilySharpLanguageServer.IsInsidePercussionPartMusic(clean, clean.Length, out _));
        Assert.True(LilySharpLanguageServer.IsInsidePercussionPartMusic(stray, stray.Length, out _));
    }
}
