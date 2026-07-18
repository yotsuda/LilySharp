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
    public void BarNumberX_LeftAlignsToSystemLeftEdge()
    {
        // Line-start bar numbers break-align to the LEFT EDGE (before the clef)
        // and LEFT-align to it (+ horizon padding 0.05), so the number sits
        // above the staff start and extends rightward — clear of the
        // system-start brace in the left margin.
        // LILYPOND-REF: scm/define-grobs.scm BarNumber —
        //   break-align-symbols (left-edge staff-bar),
        //   self-alignment-X (break-alignment-list LEFT LEFT RIGHT) = LEFT at line start.
        var layout = BuildLayout("c4 d e f | break g4 a b c |");
        var bn = layout.BarNumberLayouts[0];
        var system = layout.AllSystems[1];
        Assert.False(bn.RightAligned);
        Assert.Equal(system.Indent + 0.05, bn.X, precision: 4);
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
