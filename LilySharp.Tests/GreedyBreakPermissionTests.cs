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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Break permissions are ABSOLUTE constraints (LILYPOND-REF:
/// lily/constrained-breaking.cc break_permission_), and the DP honors them —
/// but the two greedy paths (the DP-failure fallback in KnuthPlassBreaker and
/// the UseOptimalLineBreaking=false first-fit in SystemBreaker) used to ignore
/// Forbid (and the KP fallback even Force), silently violating <c>break</c> /
/// noBreak exactly when the primary algorithm bails. These pin the contract:
/// Force always breaks, a width-driven break never lands on a Forbid boundary
/// (back up, or go overfull when the whole line is one chain).
/// </summary>
[Trait("Category", "Unit")]
public class GreedyBreakPermissionTests
{
    // === KnuthPlassBreaker.GreedyBreak — the DP-failure fallback ===
    // Springs of min width 10 on a 25-wide line: two fit, a third overflows.

    private static MeasureSpringData Spring(BreakPermission perm = BreakPermission.Allow)
        => new(10, 10, 1, 0, perm);

    private static List<int> Break(params MeasureSpringData[] data)
    {
        var cumMin = new double[data.Length + 1];
        for (int i = 0; i < data.Length; i++)
            cumMin[i + 1] = cumMin[i] + data[i].MinWidth;
        var breaker = new KnuthPlassBreaker(25, 0, 0, 1.33, raggedRight: false);
        return breaker.GreedyBreak(data, cumMin);
    }

    [Fact]
    public void GreedyBreak_AllowOnly_BreaksByWidth()
    {
        var breaks = Break(Spring(), Spring(), Spring(), Spring(), Spring());
        Assert.Equal(new[] { 2, 4, 5 }, breaks);
    }

    [Fact]
    public void GreedyBreak_Force_EndsTheLineImmediately()
    {
        // `break` after the first measure: the line must end there even though
        // a second measure would fit.
        var breaks = Break(Spring(BreakPermission.Force), Spring(), Spring(), Spring(), Spring());
        Assert.Equal(new[] { 1, 3, 5 }, breaks);
    }

    [Fact]
    public void GreedyBreak_ForbidBoundary_BacksUpToTheNearestAllowedBreak()
    {
        // Width wants to break after measure 1, but that boundary is noBreak:
        // the break backs up to after measure 0, keeping 1 and 2 together.
        var breaks = Break(Spring(), Spring(BreakPermission.Forbid), Spring(), Spring(), Spring());
        Assert.Equal(new[] { 1, 3, 5 }, breaks);
    }

    [Fact]
    public void GreedyBreak_AllForbidLine_GoesOverfullRatherThanSplitting()
    {
        // Every boundary is noBreak: there is no legal break anywhere, so the
        // chain stays on one (overfull) line instead of being split.
        var breaks = Break(
            Spring(BreakPermission.Forbid), Spring(BreakPermission.Forbid),
            Spring(BreakPermission.Forbid), Spring(BreakPermission.Forbid), Spring());
        Assert.Equal(new[] { 5 }, breaks);
    }

    // === SystemBreaker first-fit (UseOptimalLineBreaking = false) ===
    // Identical 4-quarter measures on a line 2.5 measures wide: two fit.

    private static Measure M(BreakPermission perm = BreakPermission.Allow, bool breakAfter = false)
    {
        var items = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(2, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(4, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));
        return new Measure(items, BarlineType.None, BarlineType.Single, null, 0, 0,
            hasBreakAfter: breakAfter, lineBreakPermission: perm);
    }

    private static List<int> GreedySizes(params Measure[] measures)
    {
        double w = SpacingRules.CalculateMeasureIdealWidth(measures[0]);
        var options = new LayoutOptions
        {
            PageWidth = w * 2.5,
            MarginLeft = 0,
            MarginRight = 0,
            UseOptimalLineBreaking = false,
        };
        var systems = new SystemBreaker(options).BreakIntoSystemsGreedy(
            ImmutableArray.Create(measures), firstPrefixWidth: 0, continuationPrefixWidth: 0);
        return systems.Select(s => s.Count).ToList();
    }

    [Fact]
    public void Greedy_AllowOnly_TwoPerSystem()
    {
        Assert.Equal(new[] { 2, 2, 1 }, GreedySizes(M(), M(), M(), M(), M()));
    }

    [Fact]
    public void Greedy_ForcedBreak_StillEndsTheSystem()
    {
        // Pre-existing behavior, pinned: `break` after the first measure.
        Assert.Equal(new[] { 1, 2, 2 }, GreedySizes(M(breakAfter: true), M(), M(), M(), M()));
    }

    [Fact]
    public void Greedy_ForbidBoundary_CarriesTheChainToTheNextSystem()
    {
        // The width-driven break would land after measure 1 (noBreak): measure 1
        // moves to the next system with measure 2 instead of being separated.
        var sizes = GreedySizes(M(), M(BreakPermission.Forbid), M(), M(), M());
        Assert.Equal(new[] { 1, 2, 2 }, sizes);
    }

    [Fact]
    public void Greedy_AllForbidChain_StaysOverfull()
    {
        // No legal boundary exists: the whole chain stays on one system.
        var sizes = GreedySizes(
            M(BreakPermission.Forbid), M(BreakPermission.Forbid),
            M(BreakPermission.Forbid), M(BreakPermission.Forbid), M());
        Assert.Equal(new[] { 5 }, sizes);
    }
}
