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

using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Rendering;

namespace LilySharp.Tests;

/// <summary>
/// The memoized front's LIVE SUBSET, below the staff: when one system replays and another
/// declines, the declining system's grobs are filtered into a smaller array, stacked there,
/// and scattered back by index — and a line group anchored in a live system has to name its
/// members by their index IN THAT SMALLER ARRAY.
///
/// The below side had no unit observer for either half: every existing memo test either hits
/// both systems or misses both, so the filter's index arithmetic never ran under a partial
/// hit, and <see cref="DynamicAlignEngraver.AlignedLineGroup"/> appeared in no test at all
/// (the reader's corpus builds none either — session 435 measured the group arm allocating
/// 0 B over 1,848 keystrokes). These three are that observer.
/// </summary>
[Trait("Category", "Unit")]
public class OutsideStaffStackLiveSubsetTests
{
    /// <summary>Two systems, measures 0-1 and 2-3 — so a grob in measure 0 belongs to
    /// system 0 and one in measure 2 to system 1, and "system 0 replays" makes every
    /// live index differ from its original.</summary>
    private static ImmutableArray<SystemLayout> CreateTwoSystems()
    {
        var measures0 = ImmutableArray.Create(
            new MeasureLayout(0, 5, 30, ImmutableArray<ItemLayout>.Empty),
            new MeasureLayout(1, 35, 30, ImmutableArray<ItemLayout>.Empty));
        var measures1 = ImmutableArray.Create(
            new MeasureLayout(2, 5, 30, ImmutableArray<ItemLayout>.Empty),
            new MeasureLayout(3, 35, 30, ImmutableArray<ItemLayout>.Empty));
        return ImmutableArray.Create(
            new SystemLayout(SystemIndex: 0, Y: 10, Width: 70, PrefixWidth: 5, Measures: measures0),
            new SystemLayout(SystemIndex: 1, Y: 40, Width: 70, PrefixWidth: 5, Measures: measures1));
    }

    private sealed class BelowProfileSource
    {
        public readonly Dictionary<(int Sys, int Staff), double> Depth = new();
        public readonly Dictionary<(int Sys, int Staff), (object Up, object Down)> Ids = new();

        public void Set(int sys, int staff, double depth)
        {
            Depth[(sys, staff)] = depth;
            Ids[(sys, staff)] = (new object(), new object());
        }

        public (VerticalSkyline Up, VerticalSkyline Down)? Profile(int sys, int staff)
            => Depth.TryGetValue((sys, staff), out double d)
                ? (VerticalSkyline.FromBox(15, 25, 2, 2, VerticalDirection.Up),
                   VerticalSkyline.FromBox(15, 25, -d, -d, VerticalDirection.Down))
                : null;

        public (object Up, object Down)? Identity(int sys, int staff)
            => Ids.TryGetValue((sys, staff), out var id) ? id : null;
    }

    /// <summary>One dynamic and one hairpin per system, in that order: index 0 in system 0,
    /// index 1 in system 1.</summary>
    private static (ImmutableArray<DynamicLayout> Dynamics, ImmutableArray<HairpinLayout> Hairpins)
        Inputs()
    {
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 20, YUp: -4.0, Text: "f",
                SourcePosition: 0),
            new DynamicLayout(MeasureIndex: 2, ItemIndex: 0, X: 20, YUp: -4.0, Text: "p",
                SourcePosition: 1));
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 0, StartX: 18, EndX: 25, YUp: -5.2,
                StartOpening: 0, EndOpening: 0.333, Direction: HairpinDirection.Crescendo,
                SourcePosition: 0),
            new HairpinLayout(StartMeasureIndex: 2, StartX: 18, EndX: 25, YUp: -5.2,
                StartOpening: 0, EndOpening: 0.333, Direction: HairpinDirection.Crescendo,
                SourcePosition: 1));
        return (dynamics, hairpins);
    }

    private static (ImmutableArray<DynamicLayout> Dynamics, ImmutableArray<HairpinLayout> Hairpins)
        Run(ImmutableArray<SystemLayout> systems, BelowProfileSource profiles,
            BelowStackMemo? memo,
            ImmutableArray<DynamicAlignEngraver.AlignedLineGroup> groups = default)
    {
        var (dynamics, hairpins) = Inputs();
        var (d, h, _, _) = OutsideStaffStacker.StackBelowStaff(
            ScoreTextMetrics.Bundled, systems, dynamics, hairpins,
            staffProfile: profiles.Profile,
            lineGroups: groups,
            memo: memo, profileIdentity: memo == null ? null : profiles.Identity);
        return (d, h);
    }

    private static ImmutableArray<DynamicAlignEngraver.AlignedLineGroup> GroupOf(
        int[] dynamicIndices, int[] hairpinIndices)
        => ImmutableArray.Create(new DynamicAlignEngraver.AlignedLineGroup(
            ImmutableArray.Create(dynamicIndices), ImmutableArray.Create(hairpinIndices)));

    /// <summary>
    /// System 0 replays, system 1 declines: the live subset holds only system 1's grobs, at
    /// index 0 rather than index 1, and its answers have to land back on index 1 of the
    /// result. ⚠️ A live-subset array built at the WHOLE family's size cannot be moved out
    /// of its builder, and a survivor count that is off by one fails there rather than
    /// returning a quietly short array.
    /// </summary>
    [Fact]
    public void BelowOneSystemDeclines_TheLiveSubsetMatchesTheMemoFreePass()
    {
        var systems = CreateTwoSystems();
        var profiles = new BelowProfileSource();
        profiles.Set(0, 0, 3.0);
        profiles.Set(1, 0, 3.0);

        var memo = new BelowStackMemo();
        var before = Run(systems, profiles, memo);   // populate both systems
        Assert.Equal(0, memo.Hits);
        Assert.Equal(2, memo.Misses);

        // Only system 1's profile grows, so only system 1 declines.
        profiles.Set(1, 0, 7.0);

        var expected = Run(systems, profiles, memo: null);
        var actual = Run(systems, profiles, memo);

        Assert.Equal(1, memo.Hits);     // system 0 replayed
        Assert.Equal(3, memo.Misses);   // 2 initial + system 1's decline
        Assert.Equal(expected.Dynamics, actual.Dynamics);
        Assert.Equal(expected.Hairpins, actual.Hairpins);

        // Non-vacuous on both sides of the seam: system 1's answer MOVED (so the live
        // subset really was stacked) and system 0's did NOT (so it really was replayed).
        Assert.True(actual.Dynamics[1].YUp < before.Dynamics[1].YUp,
            $"the deeper profile must push system 1's dynamic down (was "
            + $"{before.Dynamics[1].YUp:F3}, now {actual.Dynamics[1].YUp:F3})");
        Assert.Equal(before.Dynamics[0].YUp, actual.Dynamics[0].YUp);
    }

    /// <summary>
    /// The same partial hit, with a line group anchored in the LIVE system: the group names
    /// its members by index, and those indices have to be rewritten into the filtered
    /// arrays. Feeding the originals through would move the wrong grobs — or, when the live
    /// array is shorter than the original index, index past its end.
    /// </summary>
    [Fact]
    public void BelowALineGroupInTheLiveSystem_IsRemappedIntoTheFilteredArrays()
    {
        var systems = CreateTwoSystems();
        var profiles = new BelowProfileSource();
        profiles.Set(0, 0, 3.0);
        profiles.Set(1, 0, 3.0);

        // System 1's dynamic and hairpin are ONE line: original indices 1 and 1.
        var groups = GroupOf([1], [1]);

        var memo = new BelowStackMemo();
        Run(systems, profiles, memo, groups);        // populate both systems
        profiles.Set(1, 0, 7.0);                     // only system 1 declines

        var expected = Run(systems, profiles, memo: null, groups);
        var actual = Run(systems, profiles, memo, groups);

        Assert.Equal(1, memo.Hits);
        Assert.Equal(expected.Dynamics, actual.Dynamics);
        Assert.Equal(expected.Hairpins, actual.Hairpins);

        // Non-vacuous: the group has to have MOVED its members, and moved them TOGETHER —
        // that is the whole reason the pass is given the group instead of the members.
        var ungrouped = Run(systems, profiles, memo: null);
        Assert.NotEqual(ungrouped.Hairpins[1].YUp, expected.Hairpins[1].YUp);
        var (rawDynamics, rawHairpins) = Inputs();
        Assert.Equal(expected.Dynamics[1].YUp - rawDynamics[1].YUp,
                     expected.Hairpins[1].YUp - rawHairpins[1].YUp, 9);
    }

    /// <summary>
    /// The guard the remapping leans on: a group whose members do not all land in one system
    /// forces every system it touches onto the live path, so "anchored in a live system"
    /// always implies "every member is in the live arrays". With the guard gone, one member
    /// would be replayed from the memo while the other was stacked live.
    /// </summary>
    [Fact]
    public void BelowALineGroupStraddlingSystems_ForcesEverySystemItTouchesLive()
    {
        var systems = CreateTwoSystems();
        var profiles = new BelowProfileSource();
        profiles.Set(0, 0, 3.0);
        profiles.Set(1, 0, 3.0);

        // One line whose two dynamics sit in DIFFERENT systems.
        var groups = GroupOf([0, 1], []);

        var memo = new BelowStackMemo();
        var first = Run(systems, profiles, memo, groups);
        var second = Run(systems, profiles, memo, groups);

        Assert.Equal(first.Dynamics, second.Dynamics);
        Assert.Equal(first.Hairpins, second.Hairpins);
        Assert.Equal(0, memo.Hits);     // neither system may ever be replayed
        Assert.Equal(0, memo.Misses);   // and neither may be stored for a later keystroke
    }
}
