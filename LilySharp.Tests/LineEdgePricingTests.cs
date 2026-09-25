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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The line breaker prices each candidate line with the width the layout will take from it at
/// its two edges: the prefix of a line that opens on a meter or key change (the change is
/// hoisted into the prefix), and the courtesy signatures after the final bar line of a line
/// whose successor opens on one.
/// </summary>
/// <remarks>
/// LilyPond prices every candidate line from its real columns, the prefatory ones at both
/// ends included (lily/constrained-breaking.cc:388-482 → simple-spacer.cc get_line_forces).
/// Lily#'s breaker charged one continuation prefix and no courtesy, so a line before a return
/// from 2/4 to 4/4 was priced 0.049 of force looser than it is set, and 奏（かなで） split A2
/// 4|5 where LilyPond splits it 5|4 (Lab sessions/p583).
/// </remarks>
[Trait("Category", "Unit")]
public class LineEdgePricingTests
{
    /// <summary>Bar 3 is 2/4 and bar 4 returns to 4/4: both open on a meter change.</summary>
    private const string MeterChanges = """
        time 4/4
        octave absolute
        part m { clef bass }
        section S {
          m { c4 d e f | c4 d e f | time 2/4 c4 d | time 4/4 c4 d e f | c4 d e f | }
        }
        form main { S }
        score main "x" { staff m }
        """;

    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    /// <summary>
    /// The gate's per-measure edge widths are the layout's own: the courtesy a line ending
    /// before the measure reserves (MultiStaffLayouter.LineEndCourtesyWidth) and the prefix a
    /// line opening at it engraves (SolveLineStartPrefix) beyond the one continuation prefix.
    /// </summary>
    [Fact]
    public void SpringData_CarriesTheLayoutsEdgeWidths()
    {
        var score = Collect(MeterChanges);
        var data = SystemBreaker.ComputeMultiStaffSpringData(score, null);
        double gate = SystemBreaker.GateContinuationPrefixWidth(score, SpacingRules.MaxClefWidth(score));

        for (int i = 1; i < data.Length; i++)
        {
            Assert.Equal(MultiStaffLayouter.LineEndCourtesyWidth(score, i - 1, i),
                data[i].LineEndCourtesyWidth, precision: 9);
            Assert.Equal(
                MultiStaffLayouter.SolveLineStartPrefix(score, i, isFirstSystem: false).Columns.Right - gate,
                data[i].LineStartPrefixExtra, precision: 9);
        }

        // The regime this test exists for: the two measures that open on a change carry
        // both widths, and the ones that do not carry neither.
        foreach (int i in new[] { 2, 3 })
        {
            Assert.True(data[i].LineEndCourtesyWidth > 1.0, $"bar {i + 1} courtesy");
            Assert.True(data[i].LineStartPrefixExtra > 1.0, $"bar {i + 1} prefix");
        }
        foreach (int i in new[] { 1, 4 })
        {
            Assert.Equal(0.0, data[i].LineEndCourtesyWidth);
            Assert.Equal(0.0, data[i].LineStartPrefixExtra);
        }
    }

    private static string EighthsBook(bool named) => $$"""
        time 4/4
        octave absolute
        part m { clef treble {{(named ? "instrument bass" : "")}} }
        section S {
          m { {{string.Concat(Enumerable.Repeat("c'8 d' e' f' g' a' b' c'' | ", 24))}} }
        }
        form main { S }
        score main "x" { staff m }
        """;

    private static List<int> SystemBreaks(string src, LayoutOptions options)
    {
        var score = Collect(src);
        new SystemBreaker(options).BreakIntoSystems(score, null, null, null, out var breaks);
        return breaks.IdealBreaks;
    }

    /// <summary>
    /// The first line is priced with the indent the layout sets it with
    /// (LayoutEngine.EffectiveIndent — the paper's, LilyPond's 15mm unless the book writes
    /// one): the breaker moves with the paper's indent, and a name on the staff changes
    /// nothing. Until session 584 the breaker read the paper's indent alone while the layout
    /// added one for named staves; until session 586 a nameless book was set with none.
    /// </summary>
    [Fact]
    public void Breaker_PricesThePapersIndent_WhateverTheNames()
    {
        var plain = new LayoutOptions();
        Assert.Equal(LayoutOptions.LilyPondDefaultIndent, plain.Indent);

        var named = SystemBreaks(EighthsBook(true), plain);
        Assert.Equal(SystemBreaks(EighthsBook(false), plain), named);
        // The fixture is one the indent moves at all — otherwise the equality above is idle.
        Assert.NotEqual(SystemBreaks(EighthsBook(false), plain with { Indent = 0 }), named);
    }

    // Four bars of ideal 10 (minimum 6) on a 30-wide line: 2|2 is the breaking, every other
    // one leaves a line near-empty or crushed.
    private static MeasureSpringData Bar(double courtesy = 0, double prefix = 0)
        => new(10, 6, 1, InverseCompressStrength: 1,
               LineStartPrefixExtra: prefix, LineEndCourtesyWidth: courtesy);

    private static List<int> Breaks(params MeasureSpringData[] bars)
        => new KnuthPlassBreaker(30, 0, 0, raggedRight: false).Solve(bars).IdealBreaks;

    /// <summary>The breaker reads both widths: a line that can no longer be set once its
    /// edge is paid for is not chosen.</summary>
    [Fact]
    public void Breaker_PaysTheEdgeWidths()
    {
        var control = Breaks(Bar(), Bar(), Bar(), Bar());
        Assert.Equal(new[] { 2, 4 }, control);

        // A courtesy on bar 3 is paid by the line that ENDS before it: 20 − 25 < the 12 minimum.
        Assert.NotEqual(control, Breaks(Bar(), Bar(), Bar(courtesy: 25), Bar()));
        // A prefix on bar 3 is paid by the line that OPENS at it.
        Assert.NotEqual(control, Breaks(Bar(), Bar(), Bar(prefix: 25), Bar()));
    }
}
