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
/// The DynamicLineSpanner grouping (input/regression/dynamics-line.ly's claim): dynamics
/// linked by a running hairpin sit on ONE line; isolated dynamics get their own spanner.
/// LILYPOND-REF: lily/dynamic-align-engraver.cc:194-235 stop_translation_timestep —
///   the line closes only in a timestep where no hairpin runs (:210).
/// </summary>
public class DynamicAlignTests
{
    private static ScoreLayout LayoutOf(string measures)
    {
        var tree = SyntaxTree.Parse(
            "octave absolute\n" +
            "part m { clef treble }\n" +
            $"section S {{ m {{ {measures} }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n");
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(
            SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    /// <summary>
    /// dynamics-line.ly's core: the fff opening a hairpin and the pp terminating it ride
    /// one line (identical baselines), where the fff alone would sit HIGHER — the group's
    /// quiet position is the spanner's, side-positioned once over the whole span.
    /// Perturbation, not a pinned constant: the rule's input is the linkage, so removing
    /// the hairpin must split the baselines.
    /// </summary>
    [Fact]
    public void HairpinLinkedDynamics_ShareOneLine_UnlinkedOnesDoNot()
    {
        var linked = LayoutOf("a'1@fff @decresc | c,1@pp | a'1@p |");
        var byText = linked.DynamicLayouts.ToDictionary(d => d.Text);
        Assert.Equal(byText["fff"].YUp, byText["pp"].YUp, 6);
        // The isolated p (no hairpin at its moment) keeps its own one-column line,
        // ABOVE the linked pair's (its column is a high a', the pair's span dips to c,).
        Assert.True(byText["p"].YUp > byText["pp"].YUp + 0.1,
            $"isolated p {byText["p"].YUp:F6} must sit above the linked line {byText["pp"].YUp:F6}");

        var unlinked = LayoutOf("a'1@fff | c,1@pp | a'1@p |");
        var un = unlinked.DynamicLayouts.ToDictionary(d => d.Text);
        Assert.True(un["fff"].YUp > un["pp"].YUp + 0.1,
            $"without the hairpin the fff {un["fff"].YUp:F6} must leave the pp's "
            + $"low-column line {un["pp"].YUp:F6}");
    }

    /// <summary>
    /// The wedge rides the same line as its texts: hairpin centre == spanner origin
    /// (self-alignment-Y CENTER), text baseline == spanner − 0.6 ("center on an 'm'"),
    /// so centre − baseline is exactly the child-offset difference, whatever level the
    /// group settles at. LILYPOND-REF: scm/define-grobs.scm:1450 DynamicText Y-offset.
    /// </summary>
    [Fact]
    public void WedgeAndTexts_RideTheSameSpanner()
    {
        var layout = LayoutOf("a'1@fff @decresc | c,1@pp | a'1@p |");
        var hp = Assert.Single(layout.HairpinLayouts);
        var pp = layout.DynamicLayouts.Single(d => d.Text == "pp");
        // Frames differ: hairpin YUp is from the SYSTEM TOP (staff top line), the text's
        // from the staff middle — 2.0 apart on a five-line staff.
        double wedgeCentreAboveMiddle = hp.YUp + EngravingDefaults.StaffMiddle;
        Assert.Equal(0.6, wedgeCentreAboveMiddle - pp.YUp, 6);
    }

    /// <summary>
    /// A dynamic rides a rest (r2@p) exactly as it rides a note, X-centred on the
    /// rest's ink — regression dynamics-rest-positioning.ly. The collector's shared
    /// ArticulationsOf had no RestSyntax arm, so every rest dynamic was dropped
    /// SILENTLY (the rest itself drew; only the p vanished), while rest scripts
    /// (r4@fermata) kept working through CollectArticulations' own switch.
    /// </summary>
    [Fact]
    public void DynamicOnARest_IsCollectedAndCentredOnTheRestInk()
    {
        var layout = LayoutOf("g2@p r2@p |");
        Assert.Equal(2, layout.DynamicLayouts.Length);
        var restP = layout.DynamicLayouts[1];
        // The rest's item is the second column; the label centres on the rest
        // glyph's ink (AnchorCentreOffset's rest branch), which is RIGHT of the
        // column X — so it must differ from the column X itself and from the
        // note-anchored twin's offset-from-column only via each glyph's own ink.
        Assert.Equal(1, restP.ItemIndex);
        var noteP = layout.DynamicLayouts[0];
        // Both hang below the staff at their side-positioned level.
        Assert.True(restP.YUp < 0 && noteP.YUp < 0);
    }

    /// <summary>
    /// A hairpin terminated by a dynamic on a trailing EMPTY CHORD (c1@decresc <>@pp)
    /// still draws: the terminator's moment is one past the measure's last item.
    /// </summary>
    [Fact]
    public void Hairpin_TerminatedPastTheLastNote_DrawsToTheFinalBar()
    {
        // "c1\> <>\pp": the trailing empty chord anchors its pp one moment PAST the
        // last item — the final barline's timestep — so the wedge's to-barline right
        // bound is the FINAL bar, inside the last measure's width. The whole span
        // used to be skipped by the out-of-range guard (empty-chord.ly, wedge 2:
        // LP draws 50.503..57.355 and Lily# drew nothing).
        var layout = LayoutOf("c'2@f c'2 | c'1 @decresc <>@pp |");
        var hp = Assert.Single(layout.HairpinLayouts);
        Assert.True(hp.EndX > hp.StartX + 1.0,
            $"wedge must span to the final bar: {hp.StartX:F3}..{hp.EndX:F3}");
    }

    /// <summary>
    /// The wedge starts at its mark's OWN moment, and a dynamic AT that moment is
    /// the opening text (left bound = text ink + padding), never the terminator —
    /// "c\f\> …" used to end its own wedge on that f (empty-chord.ly, wedge 1:
    /// LP 19.638..36.492 where Lily# drew 8.59..16.37).
    /// LILYPOND-REF: lily/dynamic-align-engraver.cc:119-160 acknowledge_dynamic.
    /// </summary>
    [Fact]
    public void Hairpin_OpeningTextAtItsOwnMoment_IsTheLeftBound_NotTheTerminator()
    {
        var layout = LayoutOf("c'4 c'4@f @decresc c'4 c'4 | c'1@pp |");
        var hp = Assert.Single(layout.HairpinLayouts);
        var f = layout.DynamicLayouts.Single(d => d.Text == "f");
        double fw = DynamicOutline.AdvanceWidth("f")!.Value;
        // Left bound = the f's ink right + bound-padding 1.0 (hairpin.cc:214-218
        // Text_interface bound), NOT the measure's first column.
        Assert.Equal(f.X + fw / 2.0 + 1.0, hp.StartX, 6);
    }

    /// <summary>
    /// BuildLines chains hairpins that share a boundary moment (the running set never
    /// empties there) and splits at a real gap.
    /// </summary>
    [Fact]
    public void BuildLines_ChainsAtSharedMoments_SplitsAtGaps()
    {
        var items = ImmutableArray.Create(
            new HairpinItem(HairpinDirection.Crescendo, 0, 0, 1, 0, 0, SourceIndex: 0),
            // starts exactly where the first ends -> same line
            new HairpinItem(HairpinDirection.Decrescendo, 1, 0, 2, 0, 0, SourceIndex: 1),
            // starts after a gap -> new line
            new HairpinItem(HairpinDirection.Crescendo, 3, 0, 4, 0, 0, SourceIndex: 2));

        var lines = DynamicAlignEngraver.BuildLines(
            items, ImmutableArray<DynamicLayout>.Empty);

        Assert.Equal(2, lines.Length);
        Assert.Equal(new[] { 0, 1 }, lines[0].HairpinItemIndices);
        Assert.Equal(new[] { 2 }, lines[1].HairpinItemIndices);
    }
}
