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
/// Integration tests for spanner break-substitution: when a slur, tie, hairpin
/// (or other spanner) crosses a system break, the engraver must emit one
/// Layout per system rather than a single Layout with stretched bounds.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/break-substitution.cc — break_substitute
/// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
/// </remarks>
[Trait("Category", "Unit")]
public class MultiLineSpannerTests
{
    private static (LilySharp.Core.Svg.Model.Score Score, ScoreLayout Layout) BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var options = new LayoutOptions { UseOptimalLineBreaking = true };
        var engine = new LayoutEngine(options);
        return (score, engine.Layout(score));
    }

    [Fact]
    public void SlurAcrossLineBreak_SplitsIntoTwoLayouts()
    {
        // 4 measures, forced break after measure 1: slur runs through the break.
        var source = "c4( d e f | g a b c' | break d' c' b a | g f e d) |";
        var (_, layout) = BuildLayout(source);

        Assert.True(layout.AllSystems.Length >= 2,
            $"Expected at least 2 systems for forced break, got {layout.AllSystems.Length}");

        // The single SlurItem should produce >= 2 SlurLayouts (one per system).
        Assert.True(layout.SlurLayouts.Length >= 2,
            $"Cross-system slur should split into multiple Layouts, got {layout.SlurLayouts.Length}");

        // First piece flagged as broken on the right; subsequent pieces broken on the left.
        var first = layout.SlurLayouts[0];
        var last = layout.SlurLayouts[^1];
        Assert.False(first.IsBrokenLeft);
        Assert.True(first.IsBrokenRight);
        Assert.True(last.IsBrokenLeft);
        Assert.False(last.IsBrokenRight);
    }

    [Fact]
    public void TieAcrossLineBreak_SplitsIntoTwoLayouts()
    {
        // The tie c2~ ... c spans the break.
        var source = "c2 e2~ | break e4 f g a |";
        var (_, layout) = BuildLayout(source);

        Assert.True(layout.AllSystems.Length >= 2);
        Assert.True(layout.TieLayouts.Length >= 2,
            $"Cross-system tie should split into multiple Layouts, got {layout.TieLayouts.Length}");

        Assert.False(layout.TieLayouts[0].IsBrokenLeft);
        Assert.True(layout.TieLayouts[0].IsBrokenRight);
        Assert.True(layout.TieLayouts[^1].IsBrokenLeft);
        Assert.False(layout.TieLayouts[^1].IsBrokenRight);
    }

    [Fact]
    public void HairpinAcrossLineBreak_SplitsAndAppliesBrokenFractions()
    {
        // Crescendo from p (measure 0) to f (measure 3) crossing the break.
        var source = "c4@p d@cresc e f | break g4 a b c@f |";
        var (_, layout) = BuildLayout(source);

        Assert.True(layout.AllSystems.Length >= 2);
        Assert.True(layout.HairpinLayouts.Length >= 2,
            $"Cross-system hairpin should split into multiple Layouts, got {layout.HairpinLayouts.Length}");

        // Crescendo: first piece grows from 0 to ContinuedFraction*full,
        // last piece grows from ContinuingFraction*full to full.
        // LILYPOND-REF: lily/hairpin.cc:180-220 — broken hairpin height fractions
        var first = layout.HairpinLayouts[0];
        var last = layout.HairpinLayouts[^1];

        // First piece: starts at 0 (cresc start), ends at a partial opening (continued).
        Assert.Equal(0.0, first.StartOpening, precision: 4);
        Assert.True(first.EndOpening > 0 && first.EndOpening < 0.34,
            $"First piece's end opening should be a partial fraction, got {first.EndOpening}");

        // Last piece: starts at a partial opening (continuing), ends at full opening.
        Assert.True(last.StartOpening > 0 && last.StartOpening < 0.34,
            $"Last piece's start opening should be a partial fraction, got {last.StartOpening}");
        Assert.True(last.EndOpening > last.StartOpening,
            $"Last piece should grow from continuing to full, got start={last.StartOpening}, end={last.EndOpening}");
    }

    [Fact]
    public void SingleSystemSlur_DoesNotMarkBroken()
    {
        // 1 measure, no line break — slur stays within a single system.
        var source = "c4( d e f) |";
        var (_, layout) = BuildLayout(source);

        Assert.Single(layout.SlurLayouts);
        Assert.False(layout.SlurLayouts[0].IsBrokenLeft);
        Assert.False(layout.SlurLayouts[0].IsBrokenRight);
    }

    [Fact]
    public void SingleSystemTie_DoesNotMarkBroken()
    {
        var source = "c2~ c2 |";
        var (_, layout) = BuildLayout(source);

        Assert.Single(layout.TieLayouts);
        Assert.False(layout.TieLayouts[0].IsBrokenLeft);
        Assert.False(layout.TieLayouts[0].IsBrokenRight);
    }

    [Fact]
    public void CrossSystemSlur_PiecesAttachToCorrectSystems()
    {
        // 4 measures, forced break: slur from m0 to m3.
        var source = "c4( d e f | g a b c' | break d' c' b a | g f e d) |";
        var (_, layout) = BuildLayout(source);

        // First piece's StartMeasureIndex should be 0; last piece's StartMeasureIndex
        // should belong to a system after the break.
        var first = layout.SlurLayouts[0];
        var last = layout.SlurLayouts[^1];
        Assert.Equal(0, first.Slur.StartMeasureIndex);
        Assert.Equal(3, last.Slur.EndMeasureIndex);
    }
}
