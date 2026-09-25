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
using System.Globalization;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>MultiStaffLayouter.LayoutMeasures</c> reads a measure's springs from the break gate's
/// vector (<c>MultiStaffLayouter.GateSprings</c>) instead of building them again. Both halves
/// of a reuse net (HANDOFF RULES §5.4): the read answers exactly what the build answers, and
/// the read actually happens — with a poisoned vector to show the layout really takes its
/// springs from there, so the equality is not vacuous. And the vector is read only for the
/// score and the shortest it was built for.
/// </summary>
public class GateSpringReuseTests
{
    /// <summary>Two staves and mixed durations, so the springs carry per-column shape and
    /// the cross-staff timings the gate and the layout both collect.</summary>
    private const string Book = """
        time 4/4
        key g major
        part upper { clef treble }
        part lower { clef bass }
        section Main {
          upper { g'8 a' b' c'' d''4 e''8 fis'' | g''2 d''4. c''8 | b'1 | }
          lower { g,4 d g, d | c2 d | g,1 | }
        }
        form main { Main }
        score main "x" { staff upper staff lower }
        """;

    private static (MultiStaffScore Score, double Shortest, MeasureSpringData[] Gate) Input()
    {
        var tree = SyntaxTree.Parse(Book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        double shortest = SpacingRules.CalculateCommonShortestDuration(score);
        return (score, shortest, SystemBreaker.ComputeMultiStaffSpringData(score, shortest));
    }

    private static MultiStaffLayouter NewLayouter() =>
        new(LayoutOptions.Default, new MeasureLayouter());

    private static string Surface(ImmutableArray<MeasureLayout> layouts) =>
        string.Join(";", layouts.Select(m =>
            string.Create(CultureInfo.InvariantCulture, $"{m.MeasureIndex}:{m.X:R}:{m.Width:R}|")
            + string.Join(",", m.Columns.Select(c => c.ToString()))));

    [Fact]
    public void TheGatesSprings_LayOutWhatBuiltSpringsLayOut_AndAreRead()
    {
        var (score, shortest, gate) = Input();
        int count = score.PrimaryContentStaff.PrimaryVoice.Measures.Length;
        Assert.True(count >= 3, "the book has too few measures — the net is vacuous");

        var built = NewLayouter().LayoutMeasures(score, 0, 0, count, isLastSystem: true,
            baseShortestDuration: shortest);

        var reading = NewLayouter();
        reading.GateSprings = (score, shortest, gate);
        long before = MultiStaffLayouter.t_gateSpringReads;
        var read = reading.LayoutMeasures(score, 0, 0, count, isLastSystem: true,
            baseShortestDuration: shortest);

        Assert.Equal(before + count, MultiStaffLayouter.t_gateSpringReads);
        Assert.Equal(Surface(built), Surface(read));
    }

    /// <summary>The read path is the one the layout takes its springs from: a vector whose
    /// springs are stretched lays the measures out wider. Without this, the equality above
    /// would also hold for a layout that read the vector and ignored it.</summary>
    [Fact]
    public void APoisonedVector_MovesTheLayout()
    {
        var (score, shortest, gate) = Input();
        int count = score.PrimaryContentStaff.PrimaryVoice.Measures.Length;
        var poisoned = gate.Select(d => d with
        {
            Springs = d.Springs.IsDefaultOrEmpty
                ? d.Springs
                : d.Springs.Select(s => s with { IdealDistance = s.IdealDistance * 2 }).ToImmutableArray(),
        }).ToArray();

        var honest = NewLayouter();
        honest.GateSprings = (score, shortest, gate);
        var liar = NewLayouter();
        liar.GateSprings = (score, shortest, poisoned);

        Assert.NotEqual(
            Surface(honest.LayoutMeasures(score, 0, 0, count, isLastSystem: false, baseShortestDuration: shortest)),
            Surface(liar.LayoutMeasures(score, 0, 0, count, isLastSystem: false, baseShortestDuration: shortest)));
    }

    [Fact]
    public void AVectorForAnotherScoreOrShortest_IsNotRead()
    {
        var (score, shortest, gate) = Input();
        var (other, _, _) = Input();
        int count = score.PrimaryContentStaff.PrimaryVoice.Measures.Length;
        var layouter = NewLayouter();

        long before = MultiStaffLayouter.t_gateSpringReads;
        layouter.GateSprings = (other, shortest, gate);
        layouter.LayoutMeasures(score, 0, 0, count, isLastSystem: true, baseShortestDuration: shortest);
        layouter.GateSprings = (score, shortest / 2, gate);
        layouter.LayoutMeasures(score, 0, 0, count, isLastSystem: true, baseShortestDuration: shortest);

        Assert.Equal(before, MultiStaffLayouter.t_gateSpringReads);
    }
}
