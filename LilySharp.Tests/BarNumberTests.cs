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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for BarNumber detection at system starts.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/bar-number-engraver.cc — BarNumber grob
/// </remarks>
[Trait("Category", "Unit")]
public class BarNumberTests
{
    private static ScoreLayout BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return engine.Layout(score);
    }

    [Fact]
    public void SingleMeasure_ProducesNoBarNumber()
    {
        // First measure of a single-system score: LP default skips number 1.
        var layout = BuildLayout("c4 d e f |");
        Assert.Empty(layout.BarNumberLayouts);
    }

    [Fact]
    public void TwoMeasures_OneSystem_NoBarNumber()
    {
        // Both measures fit on one system; only system starts get numbered.
        var layout = BuildLayout("c4 d e f | g4 a b c |");
        Assert.Empty(layout.BarNumberLayouts);
    }

    [Fact]
    public void TwoSystems_SecondSystemStartGetsNumber()
    {
        // Force a break so measure 2 starts a new system; bar number "2" appears.
        var layout = BuildLayout("c4 d e f | break g4 a b c |");
        Assert.Single(layout.BarNumberLayouts);
        Assert.Equal("2", layout.BarNumberLayouts[0].Text);
        Assert.Equal(1, layout.BarNumberLayouts[0].MeasureIndex);
    }

    [Fact]
    public void ThreeSystems_TwoBarNumbers()
    {
        // Three measures, three systems via two breaks → numbers 2 and 3.
        var layout = BuildLayout("c4 d e f | break g4 a b c | break d4 e f g |");
        Assert.Equal(2, layout.BarNumberLayouts.Length);
        Assert.Equal("2", layout.BarNumberLayouts[0].Text);
        Assert.Equal("3", layout.BarNumberLayouts[1].Text);
    }

    [Fact]
    public void BarNumberAtALineStart_RightAlignsToTheStaffOrigin_LeavingTheClefClear()
    {
        // LILYPOND-REF: scm/define-grobs.scm:323,334 BarNumber —
        //   break-align-symbols = (left-edge staff-bar) with LilyPond's own comment
        //   "want the bar number before the clef at line start", and
        //   self-alignment-X = (break-alignment-list LEFT LEFT RIGHT).
        // ⚠️ THAT TRIPLE IS (end-of-line middle begin-of-line) — scm/output-lib.scm:506
        // names the arguments in that order — so a LINE-START number aligns RIGHT: its
        // right edge sits on the left-edge break-align point (the staff-line origin) and
        // the number hangs into the margin, which is what keeps the clef out from under
        // it. Only a mid-line number is LEFT.
        //
        // MEASURED, LilyPond 2.26.0 on a continuation system
        // (audit/lp-geometry/probes/page-vertical.ly, book BNL): the number spans
        // X (-0.956013 .. 0.000000) and the clef (0.800000 .. 3.365000) — disjoint by 0.8.
        //
        // ⚠️ THIS TEST USED TO ASSERT THE OPPOSITE and that is why it is worth reading:
        // it pinned the triple read backwards, which put the number over the clef, made
        // the above-staff stacker lift it clear, and cost 1.185560 ss of reserved ink
        // above EVERY continuation system — a quantity that floors the system-to-system
        // spring. See audit/lp-geometry, barnumber.{low,high}-melody.staff-to-baseline.
        var layout = BuildLayout("c4 d e f | break g4 a b c |");
        var bn = layout.BarNumberLayouts[0];
        var system = layout.AllSystems[1];
        Assert.True(bn.RightAligned);
        Assert.Equal(system.Indent, bn.X, precision: 6);
    }

    [Fact]
    public void BarNumberInkBottom_SitsOnePaddingAboveTheStaffsOwnUpSkyline()
    {
        // LILYPOND-REF: scm/define-grobs.scm:333 BarNumber padding = 1.0, placed by
        // side-position-interface::y-aligned-side against the staff. The staff's up
        // skyline is the top LINE plus half its thickness, not the line's centre, so the
        // number's ink bottom is 0.05 + 1.0 above the top line.
        // MEASURED (book BNL): 3.050000 above the staff REFPOINT for a flat-bottomed
        // digit, i.e. 2.050000 + 1.0. Asserted here as the derivation rather than as the
        // number, so it follows the staff symbol if that ever changes.
        var layout = BuildLayout("c4 d e f | break g4 a b c |");
        var bn = layout.BarNumberLayouts[0];
        Assert.Equal(LilySharp.Core.Svg.EngravingDefaults.StaffLineThickness / 2 + 1.0,
                     bn.YUp, precision: 6);
    }

    [Fact]
    public void BarNumberY_SitsAboveTheSystem()
    {
        var layout = BuildLayout("c4 d e f | break g4 a b c |");
        var bn = layout.BarNumberLayouts[0];
        // Y-up (frame B): sitting ABOVE the system top means a positive value.
        Assert.True(bn.YUp > 0.0,
            $"BarNumber YUp ({bn.YUp}) should be ABOVE the system top (positive).");
    }
}
