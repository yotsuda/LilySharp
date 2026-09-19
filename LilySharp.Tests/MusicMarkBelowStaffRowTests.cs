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
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The BELOW-staff half of <see cref="MusicMarkEngraver"/>'s placement: the pedal family
/// rows, and the base they hang from.
/// </summary>
/// <remarks>
/// ⚠️ THESE TWO ARMS HAD NO OBSERVER AT ALL, and that is why this file exists. Measured by
/// poison on 2026-09-19 (session 420) against the whole 8,737-test suite:
/// <list type="bullet">
/// <item>emptying the pedal-family row table turned <b>nothing</b> red;</item>
/// <item>emptying the measure→system-bottom map turned <b>nothing</b> red.</item>
/// </list>
/// The ledger's <c>mark.pedal.rows.*</c> points do watch pedal rows, but they reach them
/// through <c>solvedPedalRowUp</c> — the skyline-time solver — so the LEGACY stack under it
/// (the arm every caller without per-staff skylines takes, and the arm <c>Calculate</c> takes
/// when the solver answers null) was unwatched. A below-staff mark is simply rare: on the
/// owner's 231-book corpus, 42,312 of 42,328 mark groups carry none.
/// <para>
/// Both tests are DIFFERENTIAL (HANDOFF §7.7): they compare two placements of the same
/// engraver rather than pin a hand-written number, so they say what the arm is FOR without
/// freezing geometry the LilyPond ledger owns.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class MusicMarkBelowStaffRowTests
{
    private static ImmutableArray<MeasureLayout> Measures(int count, double width = 20.0)
    {
        var builder = ImmutableArray.CreateBuilder<MeasureLayout>(count);
        for (int i = 0; i < count; i++)
            builder.Add(new MeasureLayout(i, i * width, width,
                ImmutableArray.Create(new ItemLayout(0, 1.0, 2.0))));
        return builder.ToImmutable();
    }

    /// <summary>One system whose staves are given explicitly, so the system's BOTTOM — the
    /// quantity the below-staff base is measured from — is a property of the fixture.</summary>
    private static ImmutableArray<SystemLayout> System(params (double Y, double Height)[] staves)
    {
        var placed = ImmutableArray.CreateRange(
            staves.Select((s, i) => new StaffLayout(i, ClefType.Treble, s.Y, s.Height)));
        var group = StaffGroupLayout.CreateSingle(placed[0], 0, 4.0) with { Staves = placed };
        return ImmutableArray.Create(new SystemLayout(
            0, 10.0, 200.0, 5.0, Measures(2), ImmutableArray.Create(group)));
    }

    /// <summary>
    /// Sustain, sostenuto and una corda are three separate LilyPond grobs at the same
    /// outside-staff priority, so they take three ROWS rather than sharing a baseline, and
    /// PedalFamilyRank puts una corda nearest the staff (measured on 2.26.0 —
    /// audit/lp-geometry/probes/pedal-three.ly). This watches the table that spells those
    /// rows: without it every family answers the one plain pedal baseline.
    /// </summary>
    [Fact]
    public void TwoPedalFamiliesInOneGroup_StandOnSeparateRows_UnaCordaNearestTheStaff()
    {
        var systems = System((0, 4.0));
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var marks = ImmutableArray.Create(
            new MusicMarkItem(MusicMarkType.SustainOn, 0, 10),
            new MusicMarkItem(MusicMarkType.UnaCordaOn, 0, 20));

        var result = MusicMarkEngraver.Calculate(
            ScoreTextMetrics.Bundled, null, marks, systems, ml);

        double sustain = result.Single(l => l.MarkType == MusicMarkType.SustainOn).YUp;
        double unaCorda = result.Single(l => l.MarkType == MusicMarkType.UnaCordaOn).YUp;

        Assert.True(unaCorda < 2.0, $"una corda ({unaCorda:F3}) hangs BELOW the staff");
        Assert.True(sustain < unaCorda,
            $"sustain ({sustain:F3}) stands on a row OUTSIDE una corda's ({unaCorda:F3}) — "
            + "one baseline for both means the family table never answered");
    }

    /// <summary>
    /// The below-staff base drops past the system's LOWEST staff, not past a nominal single
    /// staff: on a grand staff the old top-staff constant dropped "Ped." between the staves.
    /// Differential — the same mark on a one-staff and a two-staff system.
    /// </summary>
    [Fact]
    public void TheBelowBase_DropsPastTheSystemsLowestStaff_NotTheTopOne()
    {
        var marks = ImmutableArray.Create(new MusicMarkItem(MusicMarkType.DalSegno, 0, 10));

        double YOf(ImmutableArray<SystemLayout> systems)
        {
            var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
            return MusicMarkEngraver.Calculate(
                ScoreTextMetrics.Bundled, null, marks, systems, ml).Single().YUp;
        }

        double oneStaff = YOf(System((0, 4.0)));
        double twoStaves = YOf(System((0, 4.0), (-14.0, 4.0)));

        Assert.True(oneStaff < 2.0, $"the jump instruction ({oneStaff:F3}) hangs below the staff");
        Assert.True(twoStaves < oneStaff - 1.0,
            $"with a second staff 14 spaces down the mark ({twoStaves:F3}) must clear IT, not "
            + $"the top staff ({oneStaff:F3}) — equal means the system-bottom map went unread");
    }
}
