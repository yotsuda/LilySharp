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

using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An '@' followed by no annotation name (<c>c4@@staccato</c>, a doubled key press) is
/// reported AND kept. It used to be consumed and dropped with its width, so every later
/// node's position slid one left — the preview clicked a character early, and the
/// incremental compile (which adopts positions recorded before the typo) and a full
/// compile disagreed about the same book (session 594, found by comparing the two over
/// random edits of the owner's corpus). Same shape as ChordBlockStrayTokenTests.
/// </summary>
[Trait("Category", "Unit")]
public class DoubledAtRoundTripTests
{
    private const string Source =
        "octave absolute\n"
        + "part melody { clef treble }\n"
        + "section A { melody { c'4@@staccato d' e' f' | g'1 | } }\n"
        + "form main { A |: A :| }\n"
        + "score main { staff melody }\n";

    [Fact]
    public void ADoubledAt_RoundTripsExactly()
    {
        var root = SyntaxTree.Parse(Source).GetRoot();
        Assert.Equal(Source, root.ToFullString());
    }

    [Fact]
    public void ADoubledAt_IsReported()
    {
        var tree = SyntaxTree.Parse(Source);
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains("after '@'"));
    }

    [Fact]
    public void ADoubledAt_LeavesTheLaterPositionsWhereTheTextHasThem()
    {
        var root = SyntaxTree.Parse(Source).GetRoot();
        int bar = Source.IndexOf("|:", System.StringComparison.Ordinal);
        Assert.Contains(root.DescendantNodes(), n => n is SyntaxTokenNode { Text: "|:" } t && t.SourceStart == bar);
    }
}
