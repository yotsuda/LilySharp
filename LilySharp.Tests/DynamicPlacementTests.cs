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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// '@p.up' / '@f.down' force a dynamic above / below the staff (default is below).
/// Unlike articulation placement, this is a real engraver path: the above branch
/// computes a Y above the staff (clearing up-stems) and the below-staff stacker
/// leaves it alone.
/// </summary>
[Trait("Category", "Unit")]
public class DynamicPlacementTests
{
    private static MultiStaffScore Collect(string body)
    {
        var src =
            "part m { clef treble }\n" +
            $"section S {{ m {{ {body} }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void UpQualifier_MarksDynamicAbove()
    {
        var dyns = Collect("c''4@f.up d''4@p.down e''4@mf").Dynamics
            .OrderBy(d => d.ItemIndex).ToList();
        Assert.Equal(3, dyns.Count);
        Assert.True(dyns[0].IsAbove);   // @f.up
        Assert.False(dyns[1].IsAbove);  // @p.down (explicit below)
        Assert.False(dyns[2].IsAbove);  // @mf (default below)
    }

    [Fact]
    public void Placement_OnHairpinTrigger_IsRejected_NotSilentlyDropped()
    {
        // cresc/decresc/dim drive a hairpin (always below); '.up'/'.down' is meaningless
        // there and must be flagged, not silently swallowed.
        var tree = SyntaxTree.Parse(
            "part m { clef treble } section S { m { c''4@p@cresc.up d e f@f } }\n" +
            "form main { S } score main \"o\" { staff m }\n");
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains("cresc"));

        // A dynamic LEVEL placement is fine.
        Assert.False(SyntaxTree.Parse(
            "part m { clef treble } section S { m { c''4@f.up } }\n" +
            "form main { S } score main \"o\" { staff m }\n").HasErrors);
    }

    [Fact]
    public void AboveDynamic_LaysOutHigherThanBelow()
    {
        var score = Collect("c''4@f.up c''4@mf");
        var layout = new LayoutEngine().Layout(score);

        var above = layout.DynamicLayouts.Single(d => d.IsAbove);
        var below = layout.DynamicLayouts.Single(d => !d.IsAbove);

        // Y-up (frame B): larger YUp = higher on the page.
        Assert.True(above.YUp > below.YUp,
            $"above dynamic (YUp={above.YUp}) should sit higher than below (YUp={below.YUp})");
    }

    // How far the lower staff hangs BELOW the system top in a treble-over-bass score;
    // the lower staff carries a high chord (so the inter-staff gap is skyline-driven,
    // not pinned at the basic-distance floor), then the dynamic under test rides on it.
    // StaffGroupLayout.Y is Y-up, so the downward depth is its negation.
    private static double LowerStaffDepthWithDynamic(string dynamic)
    {
        var src =
            "part top { clef treble }\npart bot { clef bass }\n" +
            $"section S {{ top {{ c'1 }} bot {{ <c' e' g'>1{dynamic} }} }}\n" +
            "form main { S }\nscore \"o\" { staff top staff bot }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        return -layout.Systems[0].StaffGroups[1].Y;
    }

    [Fact]
    public void AboveDynamic_OnLowerStaff_WidensGapToStaffAbove()
    {
        // A forced-above dynamic on the LOWER staff rises into the inter-staff gap and
        // must push the lower staff further down. A below dynamic on the same note hangs
        // under the lower staff and leaves the gap above untouched.
        double above = LowerStaffDepthWithDynamic("@f.up");
        double below = LowerStaffDepthWithDynamic("@f");

        Assert.True(above > below,
            $"@f.up lower staff (depth={above}) should sit lower than @f-below (depth={below})");
    }

    [Fact]
    public void AboveDynamic_StaffClearance_MatchesLilyPond()
    {
        // Ground truth (LilyPond, \dynamicUp on an on-staff note): the forced-up dynamic's
        // baseline sits 1.342 staff-spaces above the staff top. The DESCENDER — not the
        // baseline — is the edge facing the staff, so a pure ascent-mirror would let the
        // f/p swash nearly touch the top line. ColumnAboveBaselineY with no voices returns
        // the staff-governed baseline, in the native Y-up frame (staff-spaces above the
        // staff MIDDLE), so 1.342 above the top line reads as 3.342 above the middle.
        //
        // The value decomposes ONLY under the ported formula: the staff enters as its
        // EXTENT 2.05 (side-position-interface.cc:323-330), padding 0.6 is spent once, and
        // the descent is the `f` glyph's own ink 0.692002 — 2.05 + 0.6 + 0.692 = 3.342.
        // The reading it replaced (staff line centre 2.0 + staff-padding 0.1 + padding 0.6
        // + a nominal descent 0.64 = 3.34) reached the same total by cancelling two
        // errors, which is why this test could not tell them apart at 2 decimals. It is
        // asserted at 3 now: the port's 3.342000 against LilyPond's 3.342002, the residual
        // being Pango's quantisation of the outline.
        var (ascent, descent) = DynamicEngraver.InkOf("f", expressive: false);
        double aboveBaseline = DynamicEngraver.ColumnAboveBaselineY(
            ImmutableArray<Voice>.Empty, 0, 0, ascent, descent);
        Assert.Equal(3.342, aboveBaseline, 3);
    }

    [Fact]
    public void AboveGrobsSharingAColumn_StackClearInsteadOfOverprinting()
    {
        // Two forced-above marks on the SAME note (@f.up and @text.up — both ride
        // the above-dynamic pipeline) genuinely share a column. The above-staff
        // stacker must separate them (~StackStep apart), the second sitting ABOVE
        // (smaller Y), not overprinting the first.
        //
        // (An earlier version paired @f.up with @mark(A). But a rehearsal mark is
        // engraved at the measure/section START, never on its host note's column,
        // so it never actually collides with a note's dynamic — that test only
        // passed because a lone-section box happened to occupy the mark's column.
        // Lone-section boxes are now suppressed, exposing the non-overlap.)
        var score = Collect("c''4@f.up@text(\"cresc\").up");
        var layout = new LayoutEngine().Layout(score);

        var above = layout.DynamicLayouts.Where(d => d.IsAbove).OrderBy(d => d.YUp).ToList();
        Assert.Equal(2, above.Count);
        Assert.Equal(above[0].X, above[1].X, 3); // genuinely the same column
        Assert.True(above[1].YUp - above[0].YUp >= 1.5,
            $"stacked above-staff grobs must be separated (got {above[0].YUp} and {above[1].YUp})");
    }
}
