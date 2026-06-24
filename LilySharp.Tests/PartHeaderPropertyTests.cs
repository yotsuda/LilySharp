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
/// Every directive — at the top level, in a part/staff header, or in the music
/// stream — is written bare (<c>keyword value</c>): <c>clef treble</c>,
/// <c>transpose d</c>, <c>time 4/4</c>. There is one form, no colon; the old
/// <c>name: value</c> header spelling is rejected.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PartHeaderPropertyTests
{
    [Fact]
    public void PartHeader_BareAttributes_Parse()
    {
        var tree = SyntaxTree.Parse("part melody { clef treble transpose d octave 0 }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void PartHeader_TimeBare_Parses()
    {
        var tree = SyntaxTree.Parse("part melody { time 4/4 }");
        Assert.False(tree.HasErrors);

        var time = tree.GetRoot().DescendantNodes<TimeSignatureSyntax>().Single();
        Assert.Null(time.Colon);
        Assert.Equal(4, time.Beats);
        Assert.Equal(4, time.BeatType);
    }

    [Fact]
    public void PartHeader_ClefColon_IsError()
    {
        var tree = SyntaxTree.Parse("part melody { clef: treble }");
        Assert.True(tree.HasErrors);
    }

    [Fact]
    public void PartHeader_TimeColon_IsError()
    {
        var tree = SyntaxTree.Parse("part melody { time: 4/4 }");
        Assert.True(tree.HasErrors);
    }

    [Fact]
    public void PartHeader_TempoColon_IsError()
    {
        var tree = SyntaxTree.Parse("part melody { tempo: 120 }");
        Assert.True(tree.HasErrors);
    }

    [Fact]
    public void MusicStream_TimeBare_Works()
    {
        var source = "time 4/4 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());

        var time = tree.GetRoot().DescendantNodes<TimeSignatureSyntax>().Single();
        Assert.Null(time.Colon);
        Assert.Equal(4, time.Beats);
    }

    [Fact]
    public void TopLevel_TransposeBare_Parses()
    {
        var tree = SyntaxTree.Parse("transpose d part melody { clef treble } section Main { melody { c4 } } structure { Main }");
        Assert.False(tree.HasErrors);
    }
}
