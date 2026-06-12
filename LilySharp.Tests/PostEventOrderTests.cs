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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Order-free post-events: slur parens / ties / beam brackets may interleave
/// with <c>@</c>-articulations after a note (<c>g4(@cresc</c> ≡
/// <c>g4@cresc(</c>), matching LilyPond's unordered post_events.
/// </summary>
/// <remarks>LILYPOND-REF: lily/lily-parser.yy post_events.</remarks>
[Trait("Category", "Unit")]
public class PostEventOrderTests
{
    private static SyntaxTree Parse(string source) => SyntaxTree.Parse(source);

    [Theory]
    [InlineData("g4(@cresc a b c) |")]      // marker before articulation
    [InlineData("g4@cresc( a b c) |")]      // articulation before marker
    [InlineData("g2~@cresc g2 |")]          // tie + dynamic
    [InlineData("g8[@cresc a b c] d4 e |")] // beam start + dynamic
    [InlineData("g4( a b c)@f |")]          // slur end + dynamic
    public void MarkerAndArticulation_AnyOrder_ParsesClean(string source)
    {
        var tree = Parse(source);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void SlurThenCresc_AttachesCrescToTheNote()
    {
        var score = new MeasureCollector().Collect(Parse("g4(@cresc a b c)@f |"));

        // The cresc mark lands in measure 0 and the f dynamic on the last note.
        Assert.Contains(score.MusicMarks, m => m.Type == MusicMarkType.Cresc);
        var f = Assert.Single(score.Dynamics);
        Assert.Equal(3, f.ItemIndex);

        var first = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);
        Assert.True(first.HasSlurStart, "slur start must survive the reordering");
        var last = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[3]);
        Assert.True(last.HasSlurEnd, "slur end must survive @f after ')'");
    }

    [Fact]
    public void TieThenDynamic_TieSurvives()
    {
        var score = new MeasureCollector().Collect(Parse("g2~@cresc g2 |"));
        var first = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);
        Assert.True(first.HasTieStart);
    }

    [Fact]
    public void BareMarkers_WithoutArticulation_Unchanged()
    {
        var tree = Parse("b4( c d e) | g2~ g4 a | c8[ d e f] g4 a |");
        Assert.Empty(tree.Diagnostics);
        var score = new MeasureCollector().Collect(tree);
        Assert.True(((NoteItem)score.Voice.Measures[0].Items[0]).HasSlurStart);
        Assert.True(((NoteItem)score.Voice.Measures[1].Items[0]).HasTieStart);
        Assert.True(((NoteItem)score.Voice.Measures[2].Items[0]).HasBeamStart);
    }

    [Fact]
    public void InlineVoltaBracket_NotMistakenForBeamMarker()
    {
        // '[1.' after a note is an inline volta ending, not a beam bracket —
        // the marker-run lookahead must not consume it.
        var tree = Parse("c4 d e f |: g4 a b c [1. d1 |] [2. e1 |] :|");
        Assert.Empty(tree.Diagnostics);
    }
}
