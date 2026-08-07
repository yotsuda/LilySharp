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
        // f/p swash nearly touch the top line. PointwiseBaselineY with no voices returns
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
        // being Pango's quantisation of the outline. Pointwise (2026-07-29) the binding
        // is still the f's own DEEPEST outline point against the flat staff extent, so
        // the number is the scalar chain's — the pointwise machinery must reproduce it.
        double aboveBaseline = DynamicEngraver.PointwiseBaselineY(above: true,
            ImmutableArray<Voice>.Empty, voiceIndex: 0, 0, 0, xColumn: 0.0, xLabel: 0.65,
            "f", expressive: false, beamOf: null);
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
        // Genuinely the same column, but with each grob's own LP alignment: the
        // dynamic's ink CENTRE on the main note head, the expressive text's ink
        // LEFT on the column origin (LP TextScript declares no self/parent
        // alignment — input-order-alignment.ly; see DynamicEngraver.Calculate).
        var f = above.Single(d => !d.IsExpressiveText);
        var txt = above.Single(d => d.IsExpressiveText);
        double column = f.X - GlyphMetrics.GetNoteheadBBox(4).CenterX;
        Assert.Equal(column,
            txt.X - LilySharp.Core.Rendering.TextFontMetrics.Serif("cresc", 2.0) / 2.0, 3);
        Assert.True(above[1].YUp - above[0].YUp >= 1.5,
            $"stacked above-staff grobs must be separated (got {above[0].YUp} and {above[1].YUp})");
    }

    // The below-staff dynamic's baseline on a staff whose SECOND voice holds the given
    // item — printed rests in the book under test, spacer rests in its control. One
    // token apart, so the difference is the rest's ink and nothing else (the shape
    // audit/lp-geometry's RSTD/RSTC pair uses, with the second staff removed).
    private static (double Baseline, double RestShift) BelowDynamicBaseline(string secondVoice)
    {
        var src =
            "octave absolute\n" +
            "part m { clef treble }\n" +
            $"section S {{ m {{ voice {{ b4@f b b b }} {{ {secondVoice} }} | }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        return (layout.DynamicLayouts.Single(d => !d.IsAbove).YUp,
                layout.GetRestShift(measureIndex: 0, voiceIndex: 1, itemIndex: 0));
    }

    /// <summary>
    /// A below-staff dynamic clears the rest ANOTHER VOICE pushed out of the staff — the
    /// profile the outside-staff pass is seeded from has to hold that rest where
    /// <c>Rest_collision</c> put it, not where it would have stood alone.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-950 <c>skyline_spacing</c> — the pass
    /// starts from <c>inside_staff_skylines</c> and adds the outside-staff grobs by ascending
    /// priority. A Rest is inside-staff ink and there is only ONE of it: LilyPond translates
    /// the grob (lily/rest-collision.cc:211-290 <c>calc_positioning_done</c>) and the group's
    /// skyline sees the result, so nothing downstream can read an unmoved position.
    /// <para>
    /// ⚠️ THE ROOM HAD THIS AND THE PROFILE DID NOT. <c>MultiStaffLayouter</c> began passing
    /// <c>RestCollisionsOf</c> into the alignment's silhouette on 2026-08-04; the four call
    /// sites that build their OWN profile from <c>SkylineBuilder.BuildStaffSkylines</c> — the
    /// loose-line chain's closing staff, the figured-bass drop, this stacker's seed and a
    /// chord row under a non-top staff — were left on the default, which is HANDOFF 7.7's two
    /// spellings and the same walk the session before had already been bitten by one caller
    /// further along. MEASURED here before the fix: the dynamic read -4.546000 whether the
    /// second voice held printed rests or spacers, i.e. the moved rest contributed exactly
    /// nothing and the `f` was engraved over it. After: -6.465450 against the control's
    /// -4.546000, a drop of 1.919450 on a rest pushed 6 staff positions down.
    /// </para>
    /// <para>
    /// ⚠️ THREE LEGS, because two equal numbers have three possible causes and only one of
    /// them is the defect: the PREMISE says the rest really left the staff in this book, the
    /// CONTROL says the profile reaches this placement at all, and the assertion is the
    /// quantity. Without the control the fix could be "the dynamic never reads a profile",
    /// which reads identically from the outside (HANDOFF 5.3, and the miss the session before
    /// spent a book on).
    /// </para>
    /// </remarks>
    [Fact]
    public void BelowDynamic_ClearsARestAnotherVoicePushedOutOfTheStaff()
    {
        var moved = BelowDynamicBaseline("r4 r r r");
        var spacer = BelowDynamicBaseline("s4 s s s");
        var lowNotes = BelowDynamicBaseline("c4 c c c");

        // PREMISE: the rest really is pushed out of the staff in this book. Without this
        // the two baselines could agree honestly and the assertion below would be empty.
        Assert.True(moved.RestShift <= -5.0,
            "premise: Rest_collision must push this rest out of the staff, "
            + $"got {moved.RestShift:F6} staff positions");

        // CONTROL: this dynamic is placed against the staff's PROFILE at all. Two equal
        // numbers otherwise mean only that nothing reaches the placement.
        Assert.True(lowNotes.Baseline < spacer.Baseline - 0.1,
            "control: the dynamic must respond to ink in the profile: "
            + $"low notes {lowNotes.Baseline:F6}, spacer control {spacer.Baseline:F6}");

        Assert.True(moved.Baseline < spacer.Baseline - 0.1,
            "the dynamic must hang below the rest Rest_collision pushed out of the staff: "
            + $"printed rests {moved.Baseline:F6}, spacer control {spacer.Baseline:F6}");
    }
}
