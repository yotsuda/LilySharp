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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tab staff prints neither a key signature nor a time signature, so an ALL-TAB score
/// must not reserve either at the system head — there is no notation staff to align
/// against, and the reclaimed space lets the notes sit close to the compact "TAB" clef.
/// A score with any notation staff keeps both (and the tab aligns to it), unchanged.
/// </summary>
[Trait("Category", "Unit")]
public class TabOnlyKeyPrefixTests
{
    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        return new MeasureCollector().CollectMultiStaff(tree, spec);
    }

    private static double PrefixWidth(string src)
        => new LayoutEngine().Layout(Collect(src)).Systems[0].PrefixWidth;

    // Bass line in E major (4 sharps). The score block decides tab-only vs staff+tab.
    private static string Src(string key, string scoreBlock, string meter = "4/4") => $$"""
        octave absolute
        key {{key}}
        time {{meter}}
        part bl { clef bass  tuning bass }
        section Main { bl { e,4 e, e, | e,4 e, e, } }
        form main { Main }
        score main { {{scoreBlock}} }
        """;

    [Fact]
    public void AllTabScore_DrawsNoKeySignature_SoLeadingKeySharpsIsZero()
    {
        var score = Collect(Src("e major", "tab bass bl"));
        Assert.True(score.AllStavesTab);
        Assert.Equal(0, score.LeadingKeySharps);           // reclaimed — E major would be 4
    }

    [Fact]
    public void ScoreWithANotationStaff_KeepsItsKeySignature()
    {
        var score = Collect(Src("e major", "staff bass bl  tab bass bl"));
        Assert.False(score.AllStavesTab);
        Assert.Equal(4, score.LeadingKeySharps);           // E major = 4 sharps, still reserved
        Assert.Equal(score.KeySignature.Sharps, score.LeadingKeySharps);
    }

    [Fact]
    public void AllTabPrefix_IsIndependentOfTheKey()
    {
        // With no key signature drawn, the tab prefix is the same whatever the key — the
        // key's accidental count no longer shifts the first note.
        Assert.Equal(PrefixWidth(Src("c major", "tab bass bl")),
                     PrefixWidth(Src("e major", "tab bass bl")));
    }

    [Fact]
    public void AllTabPrefix_IsIndependentOfTheMeter()
    {
        // Tab draws no time signature either, so its width is reclaimed too — the prefix
        // is the same whatever the meter (a notation staff would widen 4/4 vs 3/4).
        Assert.Equal(PrefixWidth(Src("c major", "tab bass bl", "4/4")),
                     PrefixWidth(Src("c major", "tab bass bl", "3/4")));
    }

    [Fact]
    public void NotationStaffPrefix_StillGrowsWithTheKey_AndAllTabReclaimsIt()
    {
        // Control: when a notation staff is present the key signature still widens the
        // prefix (C major < E major). And the all-tab prefix is narrower than the same
        // score carrying a notation staff — the reserved key width is what was reclaimed.
        Assert.True(PrefixWidth(Src("c major", "staff bass bl  tab bass bl"))
                  < PrefixWidth(Src("e major", "staff bass bl  tab bass bl")));
        Assert.True(PrefixWidth(Src("e major", "tab bass bl"))
                  < PrefixWidth(Src("e major", "staff bass bl  tab bass bl")));
    }
}
