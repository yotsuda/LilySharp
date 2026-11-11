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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The side a hairpin stands on (docs/dynamics-above-lyrics-design.md, D1 to D3): above on a
/// staff with a lyrics row directly under it, below elsewhere, and where it is written
/// (<c>@cresc.up</c> / <c>@cresc.down</c>) whatever the staff. This is the DECISION only —
/// step 2 of the design's order; the hairpin is still drawn below until step 3.
/// </summary>
[Trait("Category", "Unit")]
public class DynamicsAboveLyricsTests
{
    /// <summary>Two parts, each one bar with a hairpin and whatever else <paramref name="sop"/>
    /// and <paramref name="alt"/> write, under the given score rows.</summary>
    private static List<HairpinItem> Hairpins(string sop, string alt, string scoreRows)
    {
        var src =
            "time 4/4\noctave absolute\n" +
            "part sop { clef treble }\npart alt { clef treble }\n" +
            $"section A {{\n  sop {{ {sop} }}\n  alt {{ {alt} }}\n}}\n" +
            "lyrics w { la la la la | }\n" +
            "form { A }\n" +
            $"score {{ {scoreRows} }}\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics)
            .OrderBy(h => h.StaffIndex).ThenBy(h => h.StartItemIndex).ToList();
    }

    private const string Plain = "c''4@cresc d'' e'' f''@f |";

    [Fact]
    public void AHairpinOverLyrics_StandsAbove_AndTheStaffWithoutLyricsKeepsItsBelow()
    {
        var hairpins = Hairpins(Plain, Plain, "staff sop  lyrics w  staff alt");
        Assert.Equal(2, hairpins.Count);
        Assert.True(hairpins[0].IsAbove);    // sop: the row is directly under it
        Assert.False(hairpins[1].IsAbove);   // alt: no lyrics under it
    }

    [Fact]
    public void WithNoLyricsInTheScore_EveryHairpinStandsBelow()
    {
        // The control: the same music, no row. Without it the test above passes as well on a
        // collector that says "above" for every staff.
        var hairpins = Hairpins(Plain, Plain, "staff sop  staff alt");
        Assert.Equal(2, hairpins.Count);
        Assert.All(hairpins, h => Assert.False(h.IsAbove));
    }

    [Fact]
    public void AWrittenSide_Wins_OnEitherKindOfStaff()
    {
        var hairpins = Hairpins(
            "c''4@cresc.down d'' e'' f''@f |",   // over lyrics, sent back below
            "c''4@decresc.up d'' e'' f''@p |",   // no lyrics, sent above
            "staff sop  lyrics w  staff alt");
        Assert.Equal(2, hairpins.Count);
        Assert.False(hairpins[0].IsAbove);
        Assert.True(hairpins[1].IsAbove);
    }

    [Fact]
    public void ARowFollowingAnotherPart_DoesNotMakeTheStaffAboveItAVocalStaff()
    {
        // D1: `lyrics w on alt` under the soprano's staff follows alt's rhythm and stands on a
        // band of its own; it is the lyrics of neither staff.
        var hairpins = Hairpins(Plain, Plain, "staff sop  lyrics w on alt  staff alt");
        Assert.Equal(2, hairpins.Count);
        Assert.All(hairpins, h => Assert.False(h.IsAbove));
    }

    [Fact]
    public void ADynamicsWrittenSide_DoesNotMoveTheHairpinBesideIt()
    {
        // D3: `@f.down` moves that f alone; the hairpin starting on the same note keeps the
        // staff's side.
        var hairpins = Hairpins(
            "c''4@f.down@decresc d'' e'' f''@p |", Plain, "staff sop  lyrics w  staff alt");
        Assert.True(hairpins[0].IsAbove);
    }

    [Fact]
    public void EachPieceOfTheChorus_TakesItsOwnStaffsSide()
    {
        // A choir staff: the rule is per staff, inside a group as outside one.
        var hairpins = Hairpins(Plain, Plain, "choirStaff { staff sop  lyrics w  staff alt }");
        Assert.Equal(2, hairpins.Count);
        Assert.True(hairpins[0].IsAbove);
        Assert.False(hairpins[1].IsAbove);
    }

    [Theory]
    [InlineData("@cresc.up", "^\\<")]
    [InlineData("@cresc.down", "_\\<")]
    [InlineData("@decresc.up", "^\\>")]
    [InlineData("@dim.down", "_\\>")]
    public void TheTwin_WritesAHairpinsWrittenSide(string mark, string expected)
    {
        var tree = SyntaxTree.Parse(
            "time 4/4\noctave absolute\npart m { clef treble }\n" +
            $"section A {{ m {{ c''4{mark} d'' e'' f''@mf | }} }}\n" +
            "form { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        Assert.Contains(expected, new LilyPondExporter().Export(tree));
    }
}
