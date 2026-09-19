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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The two measure-index tables — <see cref="SpannerBreakSubstitution.BuildMeasureToSystemMap"/>
/// and <see cref="LayoutUtilities.BuildMeasureMap"/> — are built ONCE per system array and
/// shared by every house that asks. This net holds that still.
/// </summary>
/// <remarks>
/// ⚠️ THE ONLY OBSERVER OF THE SHARING IS COST, which is why this net exists (the same shape
/// as session 416's scratch net and session 421's merge-cost gate). The tables' CONTENTS are
/// richly observed — poisoning the shared map reddens 205 tests (session 407) and
/// <c>MeasureToSystemHandoffTests</c> holds the self-build arm — but nothing in the suite
/// notices if every caller goes back to building its own: the picture is identical and only
/// the bill changes.
/// <para>
/// MEASURED (session 422, the owner's 231 books × 8 forward keystrokes through
/// <c>RenderIncrementalPages</c>): the two tables were built 29,108 times over 1,848
/// keystrokes — 15.75 times a keystroke, 0.755% of one — because sixteen houses each built
/// their own and <c>LayoutEngine.LayoutPreliminaryStaffBeams</c> had a hand copy of the walk
/// that ran once per STAFF. Session 420 had given ONE house (MusicMarkEngraver) a door to a
/// prebuilt map, which is the shape RULES §7.6 warns about: "N 箇所を 1 軒にした" is not
/// "counted them all".
/// </para>
/// <para>
/// THE KEY IS THE ARRAY'S IDENTITY, not its contents, and test ⒝ is what says so on purpose:
/// an equal-but-distinct array gets its own table. Identity is what makes the sharing sound
/// without a comparison — a <see cref="SystemLayout"/> is a sealed record whose
/// <c>Measures</c> and whose <c>MeasureLayout.MeasureIndex</c> are get-only, so the same
/// array can never describe two different tables.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class MeasureMapSharingTests
{
    private static ImmutableArray<SystemLayout> ThreeSystems()
    {
        var m0 = ImmutableArray.Create(
            new MeasureLayout(0, 5, 30, ImmutableArray<ItemLayout>.Empty),
            new MeasureLayout(1, 35, 30, ImmutableArray<ItemLayout>.Empty));
        var m1 = ImmutableArray.Create(
            new MeasureLayout(2, 5, 30, ImmutableArray<ItemLayout>.Empty),
            new MeasureLayout(3, 35, 30, ImmutableArray<ItemLayout>.Empty));
        var m2 = ImmutableArray.Create(
            new MeasureLayout(4, 5, 30, ImmutableArray<ItemLayout>.Empty));
        return ImmutableArray.Create(
            new SystemLayout(SystemIndex: 0, Y: 10, Width: 70, PrefixWidth: 5, Measures: m0),
            new SystemLayout(SystemIndex: 1, Y: 40, Width: 70, PrefixWidth: 5, Measures: m1),
            new SystemLayout(SystemIndex: 2, Y: 70, Width: 70, PrefixWidth: 5, Measures: m2));
    }

    /// <summary>⒜ Ask twice with the SAME systems and the table is built once.</summary>
    [Fact]
    public void AskingTwiceWithTheSameSystemsBuildsTheTableOnce()
    {
        var systems = ThreeSystems();

        Assert.Same(
            SpannerBreakSubstitution.BuildMeasureToSystemMap(systems),
            SpannerBreakSubstitution.BuildMeasureToSystemMap(systems));
        Assert.Same(
            LayoutUtilities.BuildMeasureMap(systems),
            LayoutUtilities.BuildMeasureMap(systems));
    }

    /// <summary>⒝ An EQUAL but distinct array gets its own table — the key is identity, and
    /// the contents agree, which is what makes either answer correct.</summary>
    [Fact]
    public void AnEqualButDistinctArrayGetsItsOwnTable()
    {
        var a = ThreeSystems();
        var b = ThreeSystems();

        var mapA = SpannerBreakSubstitution.BuildMeasureToSystemMap(a);
        var mapB = SpannerBreakSubstitution.BuildMeasureToSystemMap(b);
        Assert.NotSame(mapA, mapB);
        Assert.Equal(mapA, mapB);

        Assert.NotSame(LayoutUtilities.BuildMeasureMap(a), LayoutUtilities.BuildMeasureMap(b));
    }

    /// <summary>⒞ What the tables actually say — the walk itself, over three systems, so a
    /// measure index alone does not name a system.</summary>
    [Fact]
    public void EveryMeasureNamesItsOwnSystem()
    {
        var systems = ThreeSystems();

        var toSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        Assert.Equal(new[] { 0, 0, 1, 1, 2 }, new[] { 0, 1, 2, 3, 4 }.Select(m => toSystem[m]));
        Assert.False(toSystem.ContainsKey(5));

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        Assert.Equal(1, measureMap[3].System.SystemIndex);
        Assert.Equal(3, measureMap[3].Measure.MeasureIndex);
        Assert.Equal(35, measureMap[3].Measure.X);
    }

    /// <summary>⒟ No systems, no table — and the empty answer is shared too, so the callers'
    /// "is it in here" still works without an allocation.</summary>
    [Fact]
    public void NoSystemsGiveAnEmptyTable()
    {
        Assert.Empty(SpannerBreakSubstitution.BuildMeasureToSystemMap(
            ImmutableArray<SystemLayout>.Empty));
        Assert.Empty(LayoutUtilities.BuildMeasureMap(ImmutableArray<SystemLayout>.Empty));
        Assert.Empty(SpannerBreakSubstitution.BuildMeasureToSystemMap(default));
    }
}
