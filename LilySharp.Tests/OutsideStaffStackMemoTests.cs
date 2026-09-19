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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Rendering;

namespace LilySharp.Tests;

/// <summary>
/// The above-staff stacking memo (<see cref="AboveStackMemo"/>): per-system replay must
/// be byte-identical to a memo-free pass, a changed input value must decline exactly its
/// system, and the profile REFERENCE key must be load-bearing (a swapped profile moves
/// the answer instead of replaying the stale one).
/// </summary>
[Trait("Category", "Unit")]
public class OutsideStaffStackMemoTests
{
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

    /// <summary>Inputs touching three families in both systems: movable texts, the
    /// always-present bar numbers, and a seed-only tuplet bracket in system 1.</summary>
    private static (ImmutableArray<CustomTextLayout> Texts,
                    ImmutableArray<BarNumberLayout> BarNumbers,
                    ImmutableArray<TupletBracketLayout> Tuplets) Inputs()
    {
        var texts = ImmutableArray.Create(
            new CustomTextLayout(MeasureIndex: 0, X: 20, YUp: -4.0, Text: "dolce", SourcePosition: 0),
            new CustomTextLayout(MeasureIndex: 2, X: 20, YUp: -4.0, Text: "poco", SourcePosition: 1));
        var barNumbers = ImmutableArray.Create(
            new BarNumberLayout(MeasureIndex: 0, Text: "1", X: 5, YUp: 3.0),
            new BarNumberLayout(MeasureIndex: 2, Text: "3", X: 5, YUp: 3.0));
        var tuplets = ImmutableArray.Create(
            new TupletBracketLayout(MeasureIndex: 3, StartX: 40, EndX: 60, StartYUp: 4.0,
                EndYUp: 4.0, NumberText: "3", IsStemUp: true, ShowBracket: true,
                SourcePosition: 0));
        return (texts, barNumbers, tuplets);
    }

    /// <summary>A per-(system, staff) profile source: content the staffProfile delegate
    /// copies fresh per call (the production delegate's shape) and a stable identity pair
    /// per key (the stored-table instances the production key reads).</summary>
    private sealed class ProfileSource
    {
        public readonly Dictionary<(int Sys, int Staff), double> Height = new();
        public readonly Dictionary<(int Sys, int Staff), (object Up, object Down)> Ids = new();

        public void Set(int sys, int staff, double height)
        {
            Height[(sys, staff)] = height;
            Ids[(sys, staff)] = (new object(), new object());
        }

        public (VerticalSkyline Up, VerticalSkyline Down)? Profile(int sys, int staff)
        {
            if (!Height.TryGetValue((sys, staff), out double h))
                return null;
            // Fresh copies per call, like the production delegate; a bump over x 15..25
            // that the text at X=20 must clear, so the placed YUp DEPENDS on h.
            return (VerticalSkyline.FromBox(15, 25, h, h, VerticalDirection.Up),
                    VerticalSkyline.FromBox(15, 25, -2, -2, VerticalDirection.Down));
        }

        public (object Up, object Down)? Identity(int sys, int staff)
            => Ids.TryGetValue((sys, staff), out var id) ? id : null;
    }

    private static (ImmutableArray<CustomTextLayout> Texts,
                    ImmutableArray<BarNumberLayout> BarNumbers)
        Run(ImmutableArray<SystemLayout> systems,
            (ImmutableArray<CustomTextLayout> Texts,
             ImmutableArray<BarNumberLayout> BarNumbers,
             ImmutableArray<TupletBracketLayout> Tuplets) inputs,
            ProfileSource profiles, AboveStackMemo? memo)
    {
        var (_, bn, _, texts, _, _, _, _, _, _) = OutsideStaffStacker.StackAboveStaff(ScoreTextMetrics.Bundled, 
            systems, systemSkylines: null,
            inputs.Tuplets,
            ImmutableArray<TrillSpannerLayout>.Empty,
            inputs.BarNumbers,
            ImmutableArray<OttavaBracketLayout>.Empty,
            inputs.Texts,
            ImmutableArray<VoltaBracketLayout>.Empty,
            ImmutableArray<MusicMarkLayout>.Empty,
            staffProfile: profiles.Profile,
            memo: memo, profileIdentity: profiles.Identity);
        return (texts, bn);
    }

    [Fact]
    public void SecondIdenticalCall_ReplaysEverySystem_AndMatchesMemoFree()
    {
        var systems = CreateTwoSystems();
        var inputs = Inputs();
        var profiles = new ProfileSource();
        profiles.Set(0, 0, 3.0);
        profiles.Set(1, 0, 3.0);

        var expected = Run(systems, inputs, profiles, memo: null);

        var memo = new AboveStackMemo();
        var first = Run(systems, inputs, profiles, memo);
        Assert.Equal(expected.Texts, first.Texts);
        Assert.Equal(expected.BarNumbers, first.BarNumbers);
        Assert.Equal(0, memo.Hits);
        Assert.Equal(2, memo.Misses); // both systems stored

        var second = Run(systems, inputs, profiles, memo);
        Assert.Equal(expected.Texts, second.Texts);
        Assert.Equal(expected.BarNumbers, second.BarNumbers);
        Assert.Equal(2, memo.Hits); // both systems replayed
        Assert.Equal(2, memo.Misses);

        // The profile is load-bearing in this harness — a text over the bump must sit
        // above it, or the replay equality above proves nothing about placement.
        Assert.True(expected.Texts[0].YUp > 3.0,
            $"harness must place the text over the profile bump (YUp {expected.Texts[0].YUp:F2})");
    }

    [Fact]
    public void AChangedInputValue_DeclinesExactlyItsSystem_AndMatchesMemoFree()
    {
        var systems = CreateTwoSystems();
        var inputs = Inputs();
        var profiles = new ProfileSource();
        profiles.Set(0, 0, 3.0);
        profiles.Set(1, 0, 3.0);

        var memo = new AboveStackMemo();
        Run(systems, inputs, profiles, memo); // populate

        // System 1's text changes VALUE only (same measure, same X): the record fold
        // alone must decline system 1 while system 0 replays.
        var edited = (Texts: inputs.Texts.SetItem(1,
                inputs.Texts[1] with { Text = "molto cresc." }),
            inputs.BarNumbers, inputs.Tuplets);

        var expected = Run(systems, edited, profiles, memo: null);
        var actual = Run(systems, edited, profiles, memo);

        Assert.Equal(expected.Texts, actual.Texts);
        Assert.Equal(expected.BarNumbers, actual.BarNumbers);
        Assert.Equal(1, memo.Hits);   // system 0 replayed
        Assert.Equal(3, memo.Misses); // 2 initial + system 1's decline
    }

    [Fact]
    public void ASwappedProfile_Declines_AndTheAnswerMoves()
    {
        var systems = CreateTwoSystems();
        var inputs = Inputs();
        var profiles = new ProfileSource();
        profiles.Set(0, 0, 3.0);
        profiles.Set(1, 0, 3.0);

        var memo = new AboveStackMemo();
        var before = Run(systems, inputs, profiles, memo); // populate

        // System 1's staff profile grows (a taller bump under the same text) and its
        // identity objects change with it — the production shape: an edited system's
        // StaffInside entry is a NEW instance. Every grob record is UNCHANGED, so only
        // the profile reference in the key can decline the replay; a memo without it
        // would replay the stale YUp verbatim.
        profiles.Set(1, 0, 6.0);

        var expected = Run(systems, inputs, profiles, memo: null);
        var actual = Run(systems, inputs, profiles, memo);

        Assert.Equal(expected.Texts, actual.Texts);
        Assert.Equal(expected.BarNumbers, actual.BarNumbers);
        Assert.Equal(1, memo.Hits);   // system 0 replayed
        Assert.Equal(3, memo.Misses); // 2 initial + system 1's decline
        Assert.True(actual.Texts[1].YUp > before.Texts[1].YUp,
            $"the taller profile must move the answer (stale {before.Texts[1].YUp:F2} "
            + $"vs live {actual.Texts[1].YUp:F2})");
    }

    /// <summary>Two rooms per system index: the annotation pass's PRELIMINARY half runs
    /// once per placement, and a keystroke whose page score picks another line count places
    /// the book twice. Both placements must keep hitting. ⚠️ WITH ONE ROOM THIS READS
    /// 0 hits / 8 misses — each placement evicted the other's entry every time, which is
    /// what session 415 measured in the owner's corpus (second placement 28.6% hit).</summary>
    [Fact]
    public void TwoPlacementsAlternating_BothKeepHitting()
    {
        var systems = CreateTwoSystems();
        var inputs = Inputs();
        var first = new ProfileSource();
        first.Set(0, 0, 3.0);
        first.Set(1, 0, 3.0);
        var second = new ProfileSource();
        second.Set(0, 0, 6.0);
        second.Set(1, 0, 6.0);

        var expectedFirst = Run(systems, inputs, first, memo: null);
        var expectedSecond = Run(systems, inputs, second, memo: null);
        // The two placements must not agree by accident, or "both keep hitting" would be
        // one room answering both.
        Assert.NotEqual(expectedFirst.Texts, expectedSecond.Texts);

        var memo = new AboveStackMemo();
        Run(systems, inputs, first, memo);
        Run(systems, inputs, second, memo);
        Assert.Equal(0, memo.Hits);
        Assert.Equal(4, memo.Misses); // two systems, twice: each placement's cold fill

        for (int keystroke = 0; keystroke < 2; keystroke++)
        {
            var a = Run(systems, inputs, first, memo);
            var b = Run(systems, inputs, second, memo);
            Assert.Equal(expectedFirst.Texts, a.Texts);
            Assert.Equal(expectedFirst.BarNumbers, a.BarNumbers);
            Assert.Equal(expectedSecond.Texts, b.Texts);
            Assert.Equal(expectedSecond.BarNumbers, b.BarNumbers);
        }
        Assert.Equal(8, memo.Hits);   // both systems, both placements, both keystrokes
        Assert.Equal(4, memo.Misses); // nothing evicted anything
    }

    /// <summary>Two is the count loop's bound, so a THIRD distinct placement evicts the
    /// older room — and the evicted one recomputes to the same answer rather than replaying
    /// a wrong one. This is the half that says eviction is sound, not merely rare.</summary>
    [Fact]
    public void AThirdPlacement_EvictsTheOlderRoom_AndTheEvictedOneRecomputes()
    {
        var systems = CreateTwoSystems();
        var inputs = Inputs();
        var first = new ProfileSource();
        first.Set(0, 0, 3.0);
        first.Set(1, 0, 3.0);
        var second = new ProfileSource();
        second.Set(0, 0, 6.0);
        second.Set(1, 0, 6.0);
        var third = new ProfileSource();
        third.Set(0, 0, 9.0);
        third.Set(1, 0, 9.0);

        var expectedSecond = Run(systems, inputs, second, memo: null);

        var memo = new AboveStackMemo();
        Run(systems, inputs, first, memo);            // rooms: [first, -]
        Run(systems, inputs, second, memo);           // rooms: [second, first]
        Assert.Equal(0, memo.Hits);
        Assert.Equal(4, memo.Misses);

        Run(systems, inputs, first, memo);            // served from the OLDER room, promoted
        Assert.Equal(2, memo.Hits);
        Assert.Equal(4, memo.Misses);

        Run(systems, inputs, third, memo);            // rooms: [third, first] — second evicted
        Assert.Equal(2, memo.Hits);
        Assert.Equal(6, memo.Misses);

        var backAgain = Run(systems, inputs, second, memo);
        Assert.Equal(2, memo.Hits);
        Assert.Equal(8, memo.Misses); // the evicted placement recomputes…
        Assert.Equal(expectedSecond.Texts, backAgain.Texts);       // …to the same answer
        Assert.Equal(expectedSecond.BarNumbers, backAgain.BarNumbers);
    }

    // --- the BELOW-side mirror's rooms (session 417) ------------------------------
    // The above memo got its second room in session 415; its below-side mirror kept ONE,
    // and the two placements took each other's slot exactly as the above pair had.
    // MEASURED (session 417, the owner's 231 books × 8 forward keystrokes, Release): of the
    // preliminary below store's 1,932 misses, 866 landed on a slot the SECOND placement
    // wrote and 866 on a slot the FIRST wrote — the two counts EQUAL, which is the
    // signature — against a floor of 199 on a slot the same placement wrote. Hit rate
    // 85.6% / 55.5% by placement before, 97.0% / 97.2% after.

    /// <summary>A per-(system, staff) profile source for the BELOW pass: the bump is under
    /// the staff, so the placed YUp depends on its DEPTH (the above source's down side is a
    /// constant and would make every placement agree).</summary>
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
        {
            if (!Depth.TryGetValue((sys, staff), out double d))
                return null;
            return (VerticalSkyline.FromBox(15, 25, 2, 2, VerticalDirection.Up),
                    VerticalSkyline.FromBox(15, 25, -d, -d, VerticalDirection.Down));
        }

        public (object Up, object Down)? Identity(int sys, int staff)
            => Ids.TryGetValue((sys, staff), out var id) ? id : null;
    }

    private static (ImmutableArray<DynamicLayout> Dynamics, ImmutableArray<HairpinLayout> Hairpins)
        RunBelow(ImmutableArray<SystemLayout> systems, BelowProfileSource profiles,
            BelowStackMemo? memo)
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
        var (d, h, _, _) = OutsideStaffStacker.StackBelowStaff(
            ScoreTextMetrics.Bundled, systems, dynamics, hairpins,
            staffProfile: profiles.Profile,
            memo: memo, profileIdentity: memo == null ? null : profiles.Identity);
        return (d, h);
    }

    /// <summary>Two rooms per system index, below side: both placements must keep hitting.
    /// ⚠️ WITH ONE ROOM THIS READS 0 hits / 8 misses.</summary>
    [Fact]
    public void BelowTwoPlacementsAlternating_BothKeepHitting()
    {
        var systems = CreateTwoSystems();
        var first = new BelowProfileSource();
        first.Set(0, 0, 3.0);
        first.Set(1, 0, 3.0);
        var second = new BelowProfileSource();
        second.Set(0, 0, 6.0);
        second.Set(1, 0, 6.0);

        var expectedFirst = RunBelow(systems, first, memo: null);
        var expectedSecond = RunBelow(systems, second, memo: null);
        // The two placements must not agree by accident, or "both keep hitting" would be
        // one room answering both.
        Assert.NotEqual(expectedFirst.Dynamics, expectedSecond.Dynamics);

        var memo = new BelowStackMemo();
        RunBelow(systems, first, memo);
        RunBelow(systems, second, memo);
        Assert.Equal(0, memo.Hits);
        Assert.Equal(4, memo.Misses); // two systems, twice: each placement's cold fill

        for (int keystroke = 0; keystroke < 2; keystroke++)
        {
            var a = RunBelow(systems, first, memo);
            var b = RunBelow(systems, second, memo);
            Assert.Equal(expectedFirst.Dynamics, a.Dynamics);
            Assert.Equal(expectedFirst.Hairpins, a.Hairpins);
            Assert.Equal(expectedSecond.Dynamics, b.Dynamics);
            Assert.Equal(expectedSecond.Hairpins, b.Hairpins);
        }
        Assert.Equal(8, memo.Hits);   // both systems, both placements, both keystrokes
        Assert.Equal(4, memo.Misses); // nothing evicted anything
    }

    /// <summary>Two is the count loop's bound below the staff too, so a THIRD distinct
    /// placement evicts the older room — and the evicted one recomputes to the same answer
    /// rather than replaying a wrong one.</summary>
    [Fact]
    public void BelowAThirdPlacement_EvictsTheOlderRoom_AndTheEvictedOneRecomputes()
    {
        var systems = CreateTwoSystems();
        var first = new BelowProfileSource();
        first.Set(0, 0, 3.0);
        first.Set(1, 0, 3.0);
        var second = new BelowProfileSource();
        second.Set(0, 0, 6.0);
        second.Set(1, 0, 6.0);
        var third = new BelowProfileSource();
        third.Set(0, 0, 9.0);
        third.Set(1, 0, 9.0);

        var expectedSecond = RunBelow(systems, second, memo: null);

        var memo = new BelowStackMemo();
        RunBelow(systems, first, memo);            // rooms: [first, -]
        RunBelow(systems, second, memo);           // rooms: [second, first]
        Assert.Equal(0, memo.Hits);
        Assert.Equal(4, memo.Misses);

        RunBelow(systems, first, memo);            // served from the OLDER room, promoted
        Assert.Equal(2, memo.Hits);
        Assert.Equal(4, memo.Misses);

        RunBelow(systems, third, memo);            // rooms: [third, first] — second evicted
        Assert.Equal(2, memo.Hits);
        Assert.Equal(6, memo.Misses);

        var backAgain = RunBelow(systems, second, memo);
        Assert.Equal(2, memo.Hits);
        Assert.Equal(8, memo.Misses); // the evicted placement recomputes…
        Assert.Equal(expectedSecond.Dynamics, backAgain.Dynamics);  // …to the same answer
        Assert.Equal(expectedSecond.Hairpins, backAgain.Hairpins);
    }
}
