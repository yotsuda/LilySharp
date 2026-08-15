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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tuplet numbers of flat beams vertically align with similar looking beams
/// (tuplet-number-alignment.ly). Two pins from the LP twins
/// (audit/lpreg/tupnum{a,b}*): a number centres on ITS OWN tuplet's stems even
/// when one auto-beam covers several tuplets, and a beamed stem ends at the
/// PRIMARY beam line — secondary beams stack toward the heads — so the number of
/// a 16th-beamed tuplet sits at the same height as an 8th-beamed one (LP draws
/// both books' numbers at the identical Y).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-number.cc:294-299 calc_x_offset (bracket X-positions
///   centre); lily/tuplet-bracket.cc:495-519 calc_position_and_height follow-beam.
/// </remarks>
[Trait("Category", "Unit")]
public class TupletNumberAlignmentTests
{
    private static ScoreLayout BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);
        return new LayoutEngine(new LayoutOptions()).Layout(score);
    }

    [Fact]
    public void TwoTupletsUnderOneBeam_KeepTheirOwnNumberCentres()
    {
        // Two 16th triplets inside one beat: one auto-beam covers both. Before
        // 2026-08-09 both numbers centred on the same beam midpoint and overprinted.
        // Relative-from-C4 spelling of the book's B5..G6 run (LP \relative c''').
        var layout = BuildLayout(
            "tuplet 3/2 { b''16 c'16 d16 } tuplet 3/2 { e16 f16 g16 } r2 r4 |");
        var brackets = layout.TupletBracketLayouts.OrderBy(b => b.NumberX).ToArray();
        Assert.Equal(2, brackets.Length);
        // ⚠️ THIS USED TO ASSERT THE OPPOSITE ("beamed tuplets draw no bracket"), which is
        // not LilyPond's rule. The bracket is hidden only when the beam is EQUALLY LONG;
        // here ONE beam covers BOTH tuplets, so neither tuplet's bounds are the beam's.
        // LILYPOND-REF: lily/tuplet-bracket.cc:100-115 bracket_basic_visibility, whose
        //   equally_long is lily/tuplet-bracket.cc:88-98 equal_bounds.
        // MEASURED on this test's own LP twin audit/lpreg/tupnumb-lp.svg: 8 bracket
        // lines = 4 per tuplet (two 0.7 edge hooks, and the horizontal run broken around
        // the number). Its 8th-note sibling tupnuma-lp.svg, where each beam DOES match its
        // tuplet, has 0.
        Assert.True(brackets.All(b => b.ShowBracket),
            "one beam over two tuplets is equally long with neither — LilyPond brackets both");

        // LP twin: number-to-number spacing 7.51 ss — the pin here is that they
        // are DISTINCT and each centres within its own tuplet's X span.
        Assert.True(brackets[1].NumberX - brackets[0].NumberX > 3.0,
            $"numbers collapsed: {brackets[0].NumberX} vs {brackets[1].NumberX}");
    }

    /// <summary>
    /// tuplet-number-slur-script.ly: the number position is correct when slurs
    /// and scripts are present. With up stems the slur and the accent both sit
    /// BELOW the notes while the number rides the beam above — so neither may
    /// move it. LP twin (audit/lpreg/tupnumss*): LP number centre 26.69 =
    /// outer stem midpoint 26.73 (±0.04); slur start 3.045 below the middle
    /// line, accent 4.001 below — all matched by Lily# after the head-anchor
    /// attach fix (the number sat half a head left of the stems).
    /// </summary>
    [Fact]
    public void SlurAndScript_DoNotMoveTheBeamedNumber()
    {
        var plain = BuildLayout("tuplet 3/2 { e8 e8 e8 } r4 r2 |");
        var scripted = BuildLayout("tuplet 3/2 { e8(@accent e8 e8) } r4 r2 |");
        var p = Assert.Single(plain.TupletBracketLayouts);
        var s = Assert.Single(scripted.TupletBracketLayouts);

        Assert.Equal(p.NumberX, s.NumberX, precision: 6);
        Assert.Equal(p.NumberYUp, s.NumberYUp, precision: 6);

        // And the number centres on the OUTER STEMS (head anchor + attach),
        // not on the head anchors.
        var beam = Assert.Single(scripted.BeamLayouts);
        double StemX(int mi) => beam.MemberXPositions[mi]
            + LilySharp.Core.Svg.Layout.LayoutUtilities.StemAttachX(s.IsStemUp,
                LilySharp.Core.Svg.Layout.GlyphMetrics.NoteValueOf(beam.Group.Members[mi].Item),
                LilySharp.Core.Svg.Layout.LayoutUtilities.NoteheadStyleOf(beam.Group.Members[mi].Item));
        double stemMid = (StemX(0) + StemX(beam.Group.Members.Length - 1)) / 2.0;
        Assert.Equal(stemMid, s.NumberX, precision: 6);
    }

    [Fact]
    public void EighthAndSixteenthBeams_PutTheNumberAtTheSameHeight()
    {
        // LP renders both books' numbers at the identical baseline (15.315 page):
        // the stem-side face of a beam stack is the primary line ± thickness/2,
        // with no per-beam-count translation.
        var eighths = BuildLayout(
            "tuplet 3/2 { b''8 c'8 d8 } tuplet 3/2 { e8 f8 g8 } r2 |");
        var sixteenths = BuildLayout(
            "tuplet 3/2 { b''16 c'16 d16 } tuplet 3/2 { e16 f16 g16 } r2 r4 |");
        var e0 = eighths.TupletBracketLayouts[0];
        var s0 = sixteenths.TupletBracketLayouts[0];

        Assert.Equal(e0.NumberYUp, s0.NumberYUp, precision: 6);
    }
}
