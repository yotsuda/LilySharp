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
using System.Linq;
using Xunit;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Rendering;

namespace LilySharp.Tests;

/// <summary>
/// The annotation pass builds ONE measure→system map and hands it to both outside-staff
/// stackers instead of each of them walking the whole score for its own copy (session 407).
/// This is the net for the equation that makes that legal: the handed map must give the
/// answer the house's own build gives, on the memo-free path AND on the memoized one —
/// where the fold also removed a second fill inside a single call chain
/// (<c>StackAboveStaffMemoized</c> built the map and then <c>StackAboveStaffCore</c> built
/// the identical one again).
/// </summary>
/// <remarks>
/// The map is a pure function of the systems, so this cannot fail while the two spellings
/// agree — which is the point: it fails the day someone gives one of them a different scope
/// (a per-system slice, a filtered array) and the other keeps the whole score. The existing
/// 249 SVG snapshots observe the pass's own arm; poisoning the shared map reddens 205 tests
/// (session 407), so the whole-score arm is not unobserved either. What neither of those
/// covers is the SELF-BUILD arm the CLI and the per-system callers still take, which is the
/// side this net holds still.
/// </remarks>
[Trait("Category", "Unit")]
public class MeasureToSystemHandoffTests
{
    /// <summary>Three systems, so a measure index alone does not name a system and the map
    /// is load-bearing (a one-system book would pass with any map at all).</summary>
    private static ImmutableArray<SystemLayout> CreateThreeSystems()
    {
        var m0 = ImmutableArray.Create(
            new MeasureLayout(0, 5, 30, ImmutableArray<ItemLayout>.Empty),
            new MeasureLayout(1, 35, 30, ImmutableArray<ItemLayout>.Empty));
        var m1 = ImmutableArray.Create(
            new MeasureLayout(2, 5, 30, ImmutableArray<ItemLayout>.Empty),
            new MeasureLayout(3, 35, 30, ImmutableArray<ItemLayout>.Empty));
        var m2 = ImmutableArray.Create(
            new MeasureLayout(4, 5, 30, ImmutableArray<ItemLayout>.Empty),
            new MeasureLayout(5, 35, 30, ImmutableArray<ItemLayout>.Empty));
        return ImmutableArray.Create(
            new SystemLayout(SystemIndex: 0, Y: 10, Width: 70, PrefixWidth: 5, Measures: m0),
            new SystemLayout(SystemIndex: 1, Y: 40, Width: 70, PrefixWidth: 5, Measures: m1),
            new SystemLayout(SystemIndex: 2, Y: 70, Width: 70, PrefixWidth: 5, Measures: m2));
    }

    /// <summary>The map the annotation pass hands down.</summary>
    private static Dictionary<int, int> PassMap(ImmutableArray<SystemLayout> systems)
        => SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

    private sealed class ProfileSource
    {
        public readonly Dictionary<(int Sys, int Staff), double> Height = new();
        public readonly Dictionary<(int Sys, int Staff), (object Up, object Down)> Ids = new();

        public void Set(int sys, int staff, double height)
        {
            Height[(sys, staff)] = height;
            Ids[(sys, staff)] = (new object(), new object());
        }

        // ⚠️ BOTH SIDES VARY WITH THE SYSTEM. With a constant DOWN skyline the below-staff
        // half of this net passed against a deliberately wrong map (checked, session 407):
        // every system placed its dynamic at the same Y, so the map could not be read off
        // the answer.
        public (VerticalSkyline Up, VerticalSkyline Down)? Profile(int sys, int staff)
            => Height.TryGetValue((sys, staff), out double h)
                ? (VerticalSkyline.FromBox(15, 25, h, h, VerticalDirection.Up),
                   VerticalSkyline.FromBox(15, 25, -h, -h, VerticalDirection.Down))
                : null;

        public (object Up, object Down)? Identity(int sys, int staff)
            => Ids.TryGetValue((sys, staff), out var id) ? id : null;
    }

    private static string Surface(ImmutableArray<CustomTextLayout> texts,
                                  ImmutableArray<BarNumberLayout> barNumbers)
        => string.Join(";", texts.Select(t => $"t{t.MeasureIndex}:{t.X:F6}:{t.YUp:F6}:{t.Text}"))
           + "|" + string.Join(";", barNumbers.Select(b => $"b{b.MeasureIndex}:{b.X:F6}:{b.YUp:F6}"));

    private static string Surface(ImmutableArray<DynamicLayout> dynamics,
                                  ImmutableArray<HairpinLayout> hairpins)
        => string.Join(";", dynamics.Select(d => $"d{d.MeasureIndex}:{d.X:F6}:{d.YUp:F6}"))
           + "|" + string.Join(";", hairpins.Select(h => $"h{h.StartMeasureIndex}:{h.StartX:F6}:{h.YUp:F6}"));

    [Fact]
    public void StackAboveStaff_HandedThePassMap_AnswersWhatItsOwnBuildAnswers()
    {
        var systems = CreateThreeSystems();
        var texts = ImmutableArray.Create(
            new CustomTextLayout(MeasureIndex: 0, X: 20, YUp: -4.0, Text: "dolce", SourcePosition: 0),
            new CustomTextLayout(MeasureIndex: 2, X: 20, YUp: -4.0, Text: "poco", SourcePosition: 1),
            new CustomTextLayout(MeasureIndex: 4, X: 20, YUp: -4.0, Text: "meno", SourcePosition: 2));
        var barNumbers = ImmutableArray.Create(
            new BarNumberLayout(MeasureIndex: 0, Text: "1", X: 5, YUp: 3.0),
            new BarNumberLayout(MeasureIndex: 2, Text: "3", X: 5, YUp: 3.0),
            new BarNumberLayout(MeasureIndex: 4, Text: "5", X: 5, YUp: 3.0));

        var profiles = new ProfileSource();
        // A DIFFERENT bump per system, so an answer computed against the wrong system's
        // profile lands at a different Y — without this the net would pass on a broken map.
        profiles.Set(0, 0, 3.0);
        profiles.Set(1, 0, 6.0);
        profiles.Set(2, 0, 9.0);

        (ImmutableArray<CustomTextLayout>, ImmutableArray<BarNumberLayout>) Run(
            Dictionary<int, int>? prebuilt, AboveStackMemo? memo)
        {
            var (_, bn, _, tx, _, _, _, _, _, _) = OutsideStaffStacker.StackAboveStaff(
                ScoreTextMetrics.Bundled, systems, systemSkylines: null,
                ImmutableArray<TupletBracketLayout>.Empty,
                ImmutableArray<TrillSpannerLayout>.Empty,
                barNumbers,
                ImmutableArray<OttavaBracketLayout>.Empty,
                texts,
                ImmutableArray<VoltaBracketLayout>.Empty,
                ImmutableArray<MusicMarkLayout>.Empty,
                staffProfile: profiles.Profile,
                memo: memo, profileIdentity: memo == null ? null : profiles.Identity,
                prebuiltMeasureToSystem: prebuilt);
            return (tx, bn);
        }

        // The memo-free path (StackAboveStaffCore).
        var ownBuild = Run(prebuilt: null, memo: null);
        var handed = Run(prebuilt: PassMap(systems), memo: null);
        Assert.Equal(Surface(ownBuild.Item1, ownBuild.Item2), Surface(handed.Item1, handed.Item2));

        // The memoized path, where the fold ALSO removed the second fill inside the chain
        // (Memoized built it, then Core built it again).
        var ownMemo = Run(prebuilt: null, memo: new AboveStackMemo());
        var handedMemo = Run(prebuilt: PassMap(systems), memo: new AboveStackMemo());
        Assert.Equal(Surface(ownMemo.Item1, ownMemo.Item2), Surface(handedMemo.Item1, handedMemo.Item2));
        Assert.Equal(Surface(ownBuild.Item1, ownBuild.Item2), Surface(ownMemo.Item1, ownMemo.Item2));

        // The net is not vacuous: the systems really did move their texts apart.
        Assert.Equal(3, handed.Item1.Length);
        Assert.True(handed.Item1.Select(t => t.YUp).Distinct().Count() > 1,
            "every text landed at the same Y — the per-system profiles are not discriminating");
    }

    [Fact]
    public void StackBelowStaff_HandedThePassMap_AnswersWhatItsOwnBuildAnswers()
    {
        var systems = CreateThreeSystems();
        var dynamics = ImmutableArray.Create(
            new DynamicLayout(MeasureIndex: 0, ItemIndex: 0, X: 20, YUp: -4.0, Text: "f", SourcePosition: 0),
            new DynamicLayout(MeasureIndex: 2, ItemIndex: 0, X: 20, YUp: -4.0, Text: "p", SourcePosition: 1),
            new DynamicLayout(MeasureIndex: 4, ItemIndex: 0, X: 20, YUp: -4.0, Text: "mf", SourcePosition: 2));
        var hairpins = ImmutableArray.Create(
            new HairpinLayout(StartMeasureIndex: 0, StartX: 18, EndX: 25, YUp: -5.2,
                StartOpening: 0, EndOpening: 0.333, Direction: HairpinDirection.Crescendo, SourcePosition: 0),
            new HairpinLayout(StartMeasureIndex: 2, StartX: 18, EndX: 25, YUp: -5.2,
                StartOpening: 0, EndOpening: 0.333, Direction: HairpinDirection.Crescendo, SourcePosition: 1),
            new HairpinLayout(StartMeasureIndex: 4, StartX: 18, EndX: 25, YUp: -5.2,
                StartOpening: 0, EndOpening: 0.333, Direction: HairpinDirection.Crescendo, SourcePosition: 2));

        var profiles = new ProfileSource();
        profiles.Set(0, 0, 3.0);
        profiles.Set(1, 0, 6.0);
        profiles.Set(2, 0, 9.0);

        (ImmutableArray<DynamicLayout>, ImmutableArray<HairpinLayout>) Run(
            Dictionary<int, int>? prebuilt, BelowStackMemo? memo)
        {
            var (d, h, _, _) = OutsideStaffStacker.StackBelowStaff(
                ScoreTextMetrics.Bundled, systems, dynamics, hairpins,
                staffProfile: profiles.Profile,
                memo: memo, profileIdentity: memo == null ? null : profiles.Identity,
                prebuiltMeasureToSystem: prebuilt);
            return (d, h);
        }

        var ownBuild = Run(prebuilt: null, memo: null);
        var handed = Run(prebuilt: PassMap(systems), memo: null);
        Assert.Equal(Surface(ownBuild.Item1, ownBuild.Item2), Surface(handed.Item1, handed.Item2));

        var ownMemo = Run(prebuilt: null, memo: new BelowStackMemo());
        var handedMemo = Run(prebuilt: PassMap(systems), memo: new BelowStackMemo());
        Assert.Equal(Surface(ownMemo.Item1, ownMemo.Item2), Surface(handedMemo.Item1, handedMemo.Item2));
        Assert.Equal(Surface(ownBuild.Item1, ownBuild.Item2), Surface(ownMemo.Item1, ownMemo.Item2));

        // Not vacuous: the three systems' profiles really did drive their hairpins apart, so
        // a wrong map would show up in this surface.
        Assert.Equal(3, handed.Item2.Length);
        Assert.True(handed.Item2.Select(h => h.YUp).Distinct().Count() > 1,
            "every hairpin landed at the same Y — the per-system profiles are not discriminating");
    }

    /// <summary>The two maps the pass hands down describe the SAME measures — the property
    /// that lets one walk of the systems serve both the system-index readers and the
    /// layout readers.</summary>
    [Fact]
    public void ThePassesTwoMaps_AgreeOnEveryMeasure_AndKeepTheLastSystemsEntry()
    {
        var systems = CreateThreeSystems();
        var toSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var measureMap = LayoutUtilities.BuildMeasureMap(systems);

        Assert.Equal(toSystem.Keys.OrderBy(k => k), measureMap.Keys.OrderBy(k => k));
        foreach (var (measureIndex, systemIndex) in toSystem)
            Assert.Equal(systemIndex, measureMap[measureIndex].System.SystemIndex);

        // A repeated MeasureIndex resolves to the LAST system that carries it — the property
        // ComputeFingeringsAndScripts's unit plan is derived from, in both spellings.
        var repeated = systems.Add(new SystemLayout(SystemIndex: 3, Y: 100, Width: 70,
            PrefixWidth: 5, Measures: ImmutableArray.Create(
                new MeasureLayout(0, 5, 30, ImmutableArray<ItemLayout>.Empty))));
        Assert.Equal(3, SpannerBreakSubstitution.BuildMeasureToSystemMap(repeated)[0]);
        Assert.Equal(3, LayoutUtilities.BuildMeasureMap(repeated)[0].System.SystemIndex);
    }
}
