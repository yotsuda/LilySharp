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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Music at the top level of a file is rejected (LYS0020). A file declares parts, sections
/// and scores; the notes live inside a part.
///
/// This replaces BareTopLevelChangeTests, which pinned how a headerless note stream should
/// engrave a `key`/`time`/`tempo` written after its first note. That question is gone with
/// the form: a top-level directive is now unconditionally the file default, because no music
/// can stand beside it to make the same spelling mean something else. The four ways that
/// ambiguity was got wrong (each directive opened the piece in the changed value AND printed
/// the change again) can no longer be written.
/// </summary>
[Trait("Category", "Unit")]
public class TopLevelMusicRejectedTests
{
    private static bool RejectsAsTopLevelMusic(string source)
        => SyntaxTree.Parse(source).Diagnostics.Any(d => d.Code == DiagnosticCodes.TopLevelMusic);

    [Theory]
    // The note stream itself, in the shapes it was actually written in.
    [InlineData("c4 d e f |")]
    [InlineData("g'1 key g major g'1")]      // the old double-print case
    [InlineData("r4 r8 r8")]
    [InlineData("<c e g>4")]
    [InlineData("voice { c2 d } { e2 f }")]
    [InlineData("repeat tremolo 4 { d8 e }")]
    // The rest of the top-level music surface, closed at the same time so that adding one
    // pair of braces is not a way back in.
    [InlineData("{ c4 d e f }")]
    [InlineData("tuplet 3/2 { c d e }")]
    [InlineData("grace { c16 d e } c4")]
    [InlineData("acciaccatura { c16 } d4")]
    [InlineData("break")]
    [InlineData("phrase theme { c d e f }\ntheme")]
    public void TopLevelMusic_IsRejected(string source)
        => Assert.True(RejectsAsTopLevelMusic(source), $"expected LYS0020 for: {source}");

    [Theory]
    // Declarations keep the top level. With no music able to stand beside them, these are
    // unambiguously the file's defaults — the point of closing the form.
    [InlineData("title \"T\"\ncomposer \"C\"")]
    [InlineData("tempo 120\ntime 4/4\nkey g major\nclef bass\noctave absolute\npartial 8")]
    [InlineData("part melody { clef treble }")]
    [InlineData("phrase theme { c d e f }")]
    public void TopLevelDeclarations_AreStillAccepted(string source)
        => Assert.False(RejectsAsTopLevelMusic(source), $"unexpected LYS0020 for: {source}");

    [Fact]
    public void TheMinimalDocument_IsClean()
    {
        var tree = SyntaxTree.Parse(MusicSource.Wrap("c4 d e f |"));
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void FileDefaultsBesideTheWrappedMusic_AreClean()
    {
        var tree = SyntaxTree.Parse(MusicSource.Wrap("c4 d e f |", "key g major\ntime 4/4\nclef bass"));
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void ANoteStream_IsReportedOnce_NotOncePerNote()
    {
        // A three-hundred-note file saying the same thing three hundred times buries the one
        // sentence that fixes it.
        var tree = SyntaxTree.Parse(string.Join(" ", Enumerable.Repeat("c4", 300)));
        Assert.Single(tree.Diagnostics.Where(d => d.Code == DiagnosticCodes.TopLevelMusic));
    }

    [Fact]
    public void TheMessage_HandsBackTheWrapping()
    {
        var message = SyntaxTree.Parse("c4 d e f")
            .Diagnostics.First(d => d.Code == DiagnosticCodes.TopLevelMusic).Message;

        // The fix, not just the complaint: someone who pasted a note stream needs the four
        // lines that make it a file.
        Assert.Contains("part melody", message);
        Assert.Contains("section A", message);
        Assert.Contains("score main", message);
    }

    [Fact]
    public void ARejectedFile_StillParsesIntoATree()
    {
        // Recovery matters for the editor: highlighting, completion and incremental reparse
        // all run on the tree of a file being typed, and it is mid-edit that a note stream is
        // most likely to exist.
        var tree = SyntaxTree.Parse("c4 d e f");
        Assert.Equal(5, tree.Root.SlotCount); // 4 notes + EOF
    }
}
