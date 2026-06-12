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
/// Post-events on rests: <c>r4@fermata</c> etc. — standard notation
/// (LilyPond: <c>r4\fermata</c>).
/// </summary>
/// <remarks>LILYPOND-REF: lily/lily-parser.yy — post-events attach to rests.</remarks>
[Trait("Category", "Unit")]
public class RestArticulationTests
{
    [Fact]
    public void RestWithFermata_ParsesWithoutDiagnostics()
    {
        var tree = SyntaxTree.Parse("b2. r4@fermata |");
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void RestWithFermata_CollectsFermataAtRestIndex()
    {
        var tree = SyntaxTree.Parse("b2. r4@fermata |");
        var score = new MeasureCollector().Collect(tree);

        var fermata = Assert.Single(score.Articulations,
            a => a.Type == ArticulationType.Fermata);
        Assert.Equal(0, fermata.MeasureIndex);
        Assert.Equal(1, fermata.ItemIndex); // the rest is the measure's 2nd item
        Assert.True(fermata.IsAbove);
        Assert.IsType<RestItem>(score.Voice.Measures[0].Items[1]);
    }

    [Fact]
    public void RestWithMark_CollectsMusicMark()
    {
        var tree = SyntaxTree.Parse("c2 r2@coda | d1 |");
        Assert.Empty(tree.Diagnostics);
        var score = new MeasureCollector().Collect(tree);

        Assert.Contains(score.MusicMarks, m => m.Type == MusicMarkType.Coda);
    }

    [Fact]
    public void PlainRest_StillParses()
    {
        var tree = SyntaxTree.Parse("c2 r2 | R1*2 | c1 |");
        Assert.Empty(tree.Diagnostics);
        var score = new MeasureCollector().Collect(tree);
        Assert.DoesNotContain(score.Articulations, a => true);
    }
}
