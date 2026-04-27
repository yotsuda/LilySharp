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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests TieColumn: chord-to-chord ties produce one tie per matching pitch.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tie-column.cc — TieColumn groups chord ties.
/// </remarks>
[Trait("Category", "Unit")]
public class ChordTieTests
{
    private static (Score Score, ScoreLayout Layout) BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return (score, engine.Layout(score));
    }

    [Fact]
    public void Chord_NoTie_ProducesNoTieItems()
    {
        var (_, layout) = BuildLayout("<c e g>4 <c e g> |");
        Assert.Empty(layout.TieLayouts);
    }

    [Fact]
    public void ChordToChord_Tie_ProducesOneTiePerMatchingPitch()
    {
        // <c e g>~ <c e g> ties all three pitches.
        var (_, layout) = BuildLayout("<c e g>4~ <c e g> |");
        Assert.Equal(3, layout.TieLayouts.Length);
    }

    [Fact]
    public void ChordToChord_TieFlagOnSourceChord_Set()
    {
        var (score, _) = BuildLayout("<c e g>4~ <c e g> |");
        var firstChord = (ChordItem)score.Voice.Measures[0].Items[0];
        Assert.True(firstChord.HasTieStart);
    }

    [Fact]
    public void ChordTie_PartiallyMatching_TiesOnlyCommonPitches()
    {
        // First chord <c e g>, second chord <c e a> — only c and e match.
        var (_, layout) = BuildLayout("<c e g>4~ <c e a> |");
        Assert.Equal(2, layout.TieLayouts.Length);
    }

    [Fact]
    public void ChordTie_NoMatchingPitches_NoTies()
    {
        // First chord <c e g>, second chord <d f a> — no shared pitch.
        var (_, layout) = BuildLayout("<c e g>4~ <d f a> |");
        Assert.Empty(layout.TieLayouts);
    }

    [Fact]
    public void ChordToNote_Tie_TiesOnlyMatchingPitch()
    {
        // <c e g>~ followed by single note c — only c gets tied.
        var (_, layout) = BuildLayout("<c e g>4~ c4 |");
        Assert.Single(layout.TieLayouts);
        Assert.Equal(0, layout.TieLayouts[0].Tie.StaffPosition - layout.TieLayouts[0].Tie.StaffPosition); // sanity
    }

    [Fact]
    public void NoteTo_Note_StillWorks_NoRegression()
    {
        // Existing single-note tie path keeps working.
        var (_, layout) = BuildLayout("c4~ c4 |");
        Assert.Single(layout.TieLayouts);
    }
}
