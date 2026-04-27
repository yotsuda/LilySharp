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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Unit tests for the line / page / page-turn permission hierarchy.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/constrained-breaking.cc:378-386 — min_permission
/// LILYPOND-REF: lily/constrained-breaking.cc:520-535 — chained min over (line, page, turn)
/// </remarks>
[Trait("Category", "Unit")]
public class BreakPermissionHierarchyTests
{
    // -- min_permission truth table (LP-faithful) ----------------------------

    [Theory]
    [InlineData(BreakPermission.Force, BreakPermission.Force, BreakPermission.Force)]
    [InlineData(BreakPermission.Force, BreakPermission.Allow, BreakPermission.Allow)]
    [InlineData(BreakPermission.Force, BreakPermission.Forbid, BreakPermission.Forbid)]
    [InlineData(BreakPermission.Allow, BreakPermission.Allow, BreakPermission.Allow)]
    [InlineData(BreakPermission.Allow, BreakPermission.Forbid, BreakPermission.Forbid)]
    // LP's asymmetry: allow + force → forbid (cannot upgrade outer Allow with inner Force).
    [InlineData(BreakPermission.Allow, BreakPermission.Force, BreakPermission.Forbid)]
    [InlineData(BreakPermission.Forbid, BreakPermission.Allow, BreakPermission.Forbid)]
    [InlineData(BreakPermission.Forbid, BreakPermission.Forbid, BreakPermission.Forbid)]
    [InlineData(BreakPermission.Forbid, BreakPermission.Force, BreakPermission.Forbid)]
    public void MinPermission_LpTruthTable(BreakPermission outer, BreakPermission inner, BreakPermission expected)
    {
        Assert.Equal(expected, BreakPermissionExtensions.MinPermission(outer, inner));
    }

    // -- chained Effective* properties on Measure ----------------------------

    private static Measure MakeMeasure(
        BreakPermission line = BreakPermission.Allow,
        BreakPermission page = BreakPermission.Allow,
        BreakPermission turn = BreakPermission.Allow)
    {
        return new Measure(
            ImmutableArray<MusicItem>.Empty,
            BarlineType.Single,
            BarlineType.Single,
            sectionLabel: null,
            sourceStart: 0,
            sourceEnd: 0,
            hasBreakAfter: false,
            lineBreakPermission: line,
            breakPenalty: 0,
            pageBreakPermission: page,
            pageTurnPermission: turn);
    }

    [Fact]
    public void EffectivePage_LineForbidsAll_PropagatesForbidEverywhere()
    {
        var m = MakeMeasure(line: BreakPermission.Forbid,
                            page: BreakPermission.Force,
                            turn: BreakPermission.Force);
        Assert.Equal(BreakPermission.Forbid, m.EffectivePagePermission);
        Assert.Equal(BreakPermission.Forbid, m.EffectiveTurnPermission);
    }

    [Fact]
    public void EffectivePage_AllAllow_ResultIsAllow()
    {
        var m = MakeMeasure();
        Assert.Equal(BreakPermission.Allow, m.EffectivePagePermission);
        Assert.Equal(BreakPermission.Allow, m.EffectiveTurnPermission);
    }

    [Fact]
    public void EffectivePage_LineForceAllPageForce_ResultIsForce()
    {
        var m = MakeMeasure(line: BreakPermission.Force,
                            page: BreakPermission.Force,
                            turn: BreakPermission.Force);
        Assert.Equal(BreakPermission.Force, m.EffectivePagePermission);
        Assert.Equal(BreakPermission.Force, m.EffectiveTurnPermission);
    }

    [Fact]
    public void EffectiveTurn_PageForbidBlocksTurn_EvenIfTurnIsForce()
    {
        // line=Force lets page through; page=Forbid → effective page = Forbid; that
        // forbidden chain blocks turn even if turn was Force.
        var m = MakeMeasure(line: BreakPermission.Force,
                            page: BreakPermission.Forbid,
                            turn: BreakPermission.Force);
        Assert.Equal(BreakPermission.Forbid, m.EffectivePagePermission);
        Assert.Equal(BreakPermission.Forbid, m.EffectiveTurnPermission);
    }

    [Fact]
    public void EffectiveTurn_LineAllowTurnForce_ProducesForbid_PerLpAsymmetry()
    {
        // LP: min_permission(allow, force) = forbid. Chained turn = forbid.
        var m = MakeMeasure(line: BreakPermission.Allow,
                            page: BreakPermission.Allow,
                            turn: BreakPermission.Force);
        Assert.Equal(BreakPermission.Allow, m.EffectivePagePermission);
        Assert.Equal(BreakPermission.Forbid, m.EffectiveTurnPermission);
    }

    [Fact]
    public void DefaultMeasure_HasAllowOnAllThreeAxes()
    {
        var m = MakeMeasure();
        Assert.Equal(BreakPermission.Allow, m.LineBreakPermission);
        Assert.Equal(BreakPermission.Allow, m.PageBreakPermission);
        Assert.Equal(BreakPermission.Allow, m.PageTurnPermission);
    }

    [Fact]
    public void HasBreakAfter_StillForcesLinePermission_BackwardCompatible()
    {
        var m = new Measure(
            ImmutableArray<MusicItem>.Empty,
            BarlineType.Single,
            BarlineType.Single,
            sectionLabel: null,
            sourceStart: 0,
            sourceEnd: 0,
            hasBreakAfter: true);
        Assert.Equal(BreakPermission.Force, m.LineBreakPermission);
        Assert.True(m.HasBreakAfter);
    }
}
