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
/// A tab staff prints no KEY signature in either mode, so an ALL-TAB score must not reserve
/// one at the system head — there is no notation staff to align against, and the reclaimed
/// space lets the notes sit close to the compact "TAB" clef. A score with any notation staff
/// keeps it (and the tab aligns to it), unchanged.
/// </summary>
/// <remarks>
/// ⚠️ THE METER IS NOT THE KEY'S TWIN HERE, THOUGH IT READ LIKE IT FOR A LONG TIME.
/// ly/engraver-init.ly:1214 is \remove Key_engraver in the TabStaff context and nothing puts
/// it back, while ly/engraver-init.ly:1219-1220, five lines below that \remove Key_engraver,
/// only BLANKS the meter's stencil — and the revert is at ly/property-init.ly:825-826, first
/// in tabFullNotation, above its no-stem-extend one. Lily#'s default
/// <c>tab</c> IS full notation, so it carries a meter and its prefix widens with one;
/// <c>tab … as numbers</c> is the bare TabStaff and reclaims that width like the key's.
/// </remarks>
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
    public void AllTabScore_DrawsNoKeySignature_SoLeadingKeyIsEmpty()
    {
        var score = Collect(Src("e major", "tab bass bl"));
        Assert.True(score.AllStavesTab);
        Assert.Equal(KeySignature.CMajor, score.LeadingKey);   // reclaimed — E major would be 4
    }

    [Fact]
    public void ScoreWithANotationStaff_KeepsItsKeySignature()
    {
        var score = Collect(Src("e major", "staff bass bl  tab bass bl"));
        Assert.False(score.AllStavesTab);
        Assert.Equal(4, score.LeadingKey.Sharps);              // E major = 4 sharps, still reserved
        Assert.Equal(score.KeySignature, score.LeadingKey);
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
    public void AllTabPrefix_CarriesTheMeter_BecauseTheDefaultTabIsFullNotation()
    {
        // The falsifier for the half of this pair that changed: a full-notation tab staff
        // engraves the meter, so its prefix is NOT independent of it. 4/4 takes the single
        // C glyph and 3/4 a stacked pair of digits, so the two widths differ — and both are
        // wider than the meterless prefix the numbers-only twin below keeps.
        double m44 = PrefixWidth(Src("c major", "tab bass bl", "4/4"));
        double m34 = PrefixWidth(Src("c major", "tab bass bl", "3/4"));
        Assert.NotEqual(m44, m34);
        Assert.True(m44 > PrefixWidth(Src("c major", "tab bass bl as numbers", "4/4")));
    }

    [Fact]
    public void AllNumbersOnlyTabPrefix_IsIndependentOfTheMeter()
    {
        // `as numbers` IS the bare TabStaff, whose TimeSignature stencil is blanked — so
        // here the width really is reclaimed and the prefix is the same whatever the meter
        // (a notation staff would widen 4/4 vs 3/4).
        Assert.Equal(PrefixWidth(Src("c major", "tab bass bl as numbers", "4/4")),
                     PrefixWidth(Src("c major", "tab bass bl as numbers", "3/4")));
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
