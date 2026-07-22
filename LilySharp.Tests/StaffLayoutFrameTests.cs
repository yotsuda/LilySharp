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
/// Pins the STORAGE FRAME of <see cref="StaffLayout.Y"/>, <see cref="StaffGroupLayout.Y"/>
/// and the delimiter's <c>BraceTop</c>/<c>BraceBottom</c>: LilyPond's Y-up, where the first
/// staff sits at 0 and every staff below it is NEGATIVE.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:274 — <c>where += stacking_dir * dy</c> with
/// <c>stacking_dir = DOWN = -1</c>: the stacking accumulator walks negative.
/// LILYPOND-REF: lily/page-layout-problem.cc:915-917 — "this is relative to the system:
/// negative numbers are down".
///
/// ⚠️ These assertions exist because the byte-identical snapshot oracle CANNOT see a sign
/// error here. A producer and a consumer that flip together cancel, so the rendered output
/// is unchanged while the stored frame is upside down. Only a direct assertion on the
/// stored value catches that — which is why every one of the three <c>LayoutStaffGroups</c>
/// overloads (plain, hara-kiri, skyline) is pinned separately: they stack independently.
/// </remarks>
[Trait("Category", "Unit")]
public class StaffLayoutFrameTests
{
    private static readonly LayoutOptions Options = LayoutOptions.Default;

    private static NoteItem MakeNote(int staffPosition = 0) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    private static Measure MakeNoteMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeNote(), MakeNote(), MakeNote(), MakeNote()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Staff CreateStaff(ClefType clef) =>
        Staff.Create(clef, new Voice("v", ImmutableArray.Create(MakeNoteMeasure())));

    private static MultiStaffScore ScoreOf(StaffGroup group) =>
        new(ImmutableArray.Create(group), new TimeSignature(4, 4), KeySignature.CMajor);

    private static MultiStaffLayouter Layouter() => new(Options, new MeasureLayouter());

    private static ImmutableArray<MeasureLayout> SimpleMeasureLayouts()
    {
        var items = ImmutableArray.CreateBuilder<ItemLayout>(4);
        for (int i = 0; i < 4; i++)
            items.Add(new ItemLayout(i, 5.0 + i * 4.0, 1.0));
        return ImmutableArray.Create(new MeasureLayout(0, 0, 40, items.ToImmutable()));
    }

    /// <summary>The shared frame contract: first staff at the origin, the rest below it.</summary>
    private static void AssertYUpStacking(ImmutableArray<StaffLayout> staves)
    {
        Assert.Equal(0.0, staves[0].Y);
        for (int i = 1; i < staves.Length; i++)
        {
            Assert.True(staves[i].Y < 0,
                $"staff {i} Y ({staves[i].Y:F6}) must be NEGATIVE — it sits below the first staff");
            Assert.True(staves[i].Y < staves[i - 1].Y - staves[i - 1].Height + 1e-9,
                $"staff {i} Y ({staves[i].Y:F6}) must clear staff {i - 1}'s bottom " +
                $"({staves[i - 1].Y - staves[i - 1].Height:F6})");
        }
        // Height is a LENGTH — the flip must not make it negative.
        foreach (var s in staves)
            Assert.True(s.Height >= 0, $"staff {s.StaffIndex} Height ({s.Height:F6}) must be a positive length");
    }

    [Fact]
    public void GrandStaff_StoresStaffYAsYUp()
    {
        var group = StaffGroup.CreateGrandStaff(CreateStaff(ClefType.Treble), CreateStaff(ClefType.Bass));
        var layout = Layouter().LayoutStaffGroups(ScoreOf(group))[0];

        AssertYUpStacking(layout.Staves);
        Assert.Equal(0.0, layout.Y);

        var delim = layout.GrandStaffLayout!;
        Assert.True(delim.BraceTop > delim.BraceBottom,
            $"BraceTop ({delim.BraceTop:F6}) must be ABOVE BraceBottom ({delim.BraceBottom:F6}) in the Y-up frame");
        Assert.Equal(layout.Staves[0].Y, delim.BraceTop);
        Assert.Equal(layout.Staves[^1].Y - layout.Staves[^1].Height, delim.BraceBottom);
        Assert.True(delim.TotalHeight > 0, $"TotalHeight ({delim.TotalHeight:F6}) must be positive");
    }

    [Fact]
    public void GrandStaff_HaraKiriPath_StoresStaffYAsYUp()
    {
        var group = StaffGroup.CreateGrandStaff(CreateStaff(ClefType.Treble), CreateStaff(ClefType.Bass));
        var layout = Layouter().LayoutStaffGroups(ScoreOf(group), 0, 1, isFirstSystem: true)[0];

        AssertYUpStacking(layout.Staves);
        var delim = layout.GrandStaffLayout!;
        Assert.True(delim.BraceTop > delim.BraceBottom,
            $"BraceTop ({delim.BraceTop:F6}) must be ABOVE BraceBottom ({delim.BraceBottom:F6})");
    }

    [Fact]
    public void GrandStaff_SkylinePath_StoresStaffYAsYUp()
    {
        var group = StaffGroup.CreateGrandStaff(CreateStaff(ClefType.Treble), CreateStaff(ClefType.Bass));
        var layout = Layouter().LayoutStaffGroups(
            ScoreOf(group), new SkylineBuilder(Options.StaffHeight), SimpleMeasureLayouts())[0];

        AssertYUpStacking(layout.Staves);
        var delim = layout.GrandStaffLayout!;
        Assert.True(delim.BraceTop > delim.BraceBottom,
            $"BraceTop ({delim.BraceTop:F6}) must be ABOVE BraceBottom ({delim.BraceBottom:F6})");
    }

    [Fact]
    public void BracketGroup_StoresStaffYAsYUp()
    {
        var group = StaffGroup.CreateBracketGroup(
            CreateStaff(ClefType.Treble), CreateStaff(ClefType.Treble), CreateStaff(ClefType.Bass));
        var layout = Layouter().LayoutStaffGroups(ScoreOf(group))[0];

        AssertYUpStacking(layout.Staves);
        var delim = layout.GrandStaffLayout!;
        Assert.Equal(layout.Staves[0].Y, delim.BraceTop);
        Assert.Equal(layout.Staves[^1].Y - layout.Staves[^1].Height, delim.BraceBottom);
        Assert.True(delim.TotalHeight > 0, $"TotalHeight ({delim.TotalHeight:F6}) must be positive");
    }

    /// <summary>
    /// Separate single-staff GROUPS stack in the same direction as staves within a group —
    /// the dispatcher's accumulator and the group builders' must not disagree in sign.
    /// </summary>
    [Fact]
    public void SeparateGroups_StackDownwardInTheYUpFrame()
    {
        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(CreateStaff(ClefType.Treble)),
                StaffGroup.CreateSingle(CreateStaff(ClefType.Bass))),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var groups = Layouter().LayoutStaffGroups(score);

        Assert.Equal(0.0, groups[0].Y);
        Assert.True(groups[1].Y < 0,
            $"second group Y ({groups[1].Y:F6}) must be NEGATIVE — it sits below the first");
        Assert.True(groups[1].Y < groups[0].Y - groups[0].Height + 1e-9,
            $"second group Y ({groups[1].Y:F6}) must clear the first group's bottom");
        Assert.True(groups[0].Height > 0 && groups[1].Height > 0,
            "group Height is a LENGTH and must stay positive");
    }

    /// <summary>
    /// The two accessors must remain exact reflections of each other, and their composition
    /// with the system origin must be the plain SUM LilyPond performs.
    /// </summary>
    [Fact]
    public void StaffOffsetAccessors_AgreeWithStorageAndWithEachOther()
    {
        var group = StaffGroup.CreateGrandStaff(CreateStaff(ClefType.Treble), CreateStaff(ClefType.Bass));
        var layout = new LayoutEngine().Layout(ScoreOf(group));
        var system = layout.Systems[0];

        foreach (var sg in system.StaffGroups)
        {
            foreach (var st in sg.Staves)
            {
                Assert.Equal(st.Y, LayoutUtilities.StaffOffsetInSystemUp(system, st.StaffIndex));
                Assert.Equal(-st.Y, LayoutUtilities.StaffOffsetInSystemDown(system, st.StaffIndex));
                Assert.Equal(system.Y + st.Y,
                    LayoutUtilities.FindStaffYInSystem(system, st.StaffIndex));
            }
        }
    }
}
