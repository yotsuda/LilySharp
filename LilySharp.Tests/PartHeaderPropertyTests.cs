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
/// A part/staff header attribute is written bare (<c>name value</c>), the same
/// as a top-level directive — <c>clef treble</c>, <c>transpose d</c>,
/// <c>time 4/4</c>. The colon form (<c>clef: treble</c>) is still accepted during
/// the migration off colons but is no longer the canonical spelling.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PartHeaderPropertyTests
{
    [Fact]
    public void PartHeader_TimeWithColon_Parses()
    {
        var tree = SyntaxTree.Parse("part melody { time: 4/4 }");
        Assert.False(tree.HasErrors);

        var time = tree.GetRoot().DescendantNodes<TimeSignatureSyntax>().Single();
        Assert.NotNull(time.Colon);
        Assert.Equal(4, time.Beats);
        Assert.Equal(4, time.BeatType);
    }

    [Fact]
    public void PartHeader_TempoWithColon_Parses()
    {
        var tree = SyntaxTree.Parse("part melody { tempo: 120 }");
        Assert.False(tree.HasErrors);

        var tempo = tree.GetRoot().DescendantNodes<TempoDeclarationSyntax>().Single();
        Assert.NotNull(tempo.Colon);
        Assert.Equal(120, tempo.Bpm);
    }

    [Fact]
    public void PartHeader_TimeWithoutColon_Parses()
    {
        // Bare is the canonical header form: `time 4/4` is valid, no colon.
        var tree = SyntaxTree.Parse("part melody { time 4/4 }");
        Assert.False(tree.HasErrors);

        var time = tree.GetRoot().DescendantNodes<TimeSignatureSyntax>().Single();
        Assert.Null(time.Colon);
        Assert.Equal(4, time.Beats);
        Assert.Equal(4, time.BeatType);
    }

    [Fact]
    public void PartHeader_ClefBare_Parses()
    {
        var tree = SyntaxTree.Parse("part melody { clef treble transpose d }");
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void PartHeader_ColonForm_RoundTrips()
    {
        var source = "part melody { time: 4/4 tempo: 120 }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());
    }

    [Fact]
    public void MusicStream_TimeWithoutColon_StillWorks()
    {
        // The bare command form stays valid outside a part/staff header.
        var source = "time 4/4 { c4 d e f }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
        Assert.Equal(source, tree.ToFullString());

        var time = tree.GetRoot().DescendantNodes<TimeSignatureSyntax>().Single();
        Assert.Null(time.Colon);
        Assert.Equal(4, time.Beats);
    }
}
