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

using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The profile the outside-staff collision pass is SEEDED from has to hold everything
/// LilyPond calls inside-staff ink, exactly as the alignment's silhouette does.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/axis-group-interface.cc:860-935 skyline_spacing seeds inside_staff_skylines
/// LILYPOND-REF: lily/axis-group-interface.cc:969-971 add_grobs_of_one_priority adds the
/// outside-staff grobs afterwards, in ascending priority order.
/// So a grob that declares no priority is in that seed from the start and every mover clears
/// it. Slur, Tie and TupletBracket are exactly that: each lists
/// <c>outside-staff-interface</c> and none sets an <c>outside-staff-priority</c> — the
/// addresses are at their seeding sites in <c>SkylineBuilder</c>, not repeated here, because
/// a claim with two addresses has one that rots (HANDOFF 7.6).
/// <para>
/// ⚠️ THE ROOM IS THE POSITIVE CONTROL, and these books need one. Two equal numbers have
/// three possible causes and only one is the defect (HANDOFF 5.3): the grob's ink might not
/// reach a staff profile at all, or nothing might reach this placement. The room
/// (<c>MultiStaffLayouter.BuildAllStaffSkylines</c>) is handed every side table, so ink that
/// reaches a profile MUST show up in the alignment's own minimum for a staff pair — that is
/// what the control reads, on the same music with the movers taken out.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class OutsideStaffSeedTests
{
    // The lowest below-staff MOVER the stacker placed on a one-staff book — the quantity
    // the seed decides. Dynamics and hairpins together, because which of the two can see a
    // given grob is a matter of geometry rather than of the seed: a dynamic is a box on one
    // note's column, while a hairpin spans a range of x. A tie's arc peaks BETWEEN columns
    // (its ends sit on the noteheads), so only the spanning mover ever stands under it.
    private static double SeedBaseline(string body, string time)
    {
        var layout = LayoutOf(
            $"time {time}\n" +
            "part m { clef treble }\n" +
            $"section S {{ m {{ {body} | }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n");
        double y = double.MaxValue;
        foreach (var d in layout.DynamicLayouts.Where(d => !d.IsAbove))
            y = System.Math.Min(y, d.YUp);
        foreach (var h in layout.HairpinLayouts)
            y = System.Math.Min(y, h.YUp);
        Assert.True(y < double.MaxValue, "the book must place a below-staff mover");
        return y;
    }

    // The SAME music on the upper staff of a two-staff book, read as the alignment's own
    // minimum for the pair. ⚠️ WITH THE MOVERS STRIPPED: the control has to say that the
    // SPANNER's ink reaches a staff profile, and a dynamic left in would answer for itself.
    private static double RoomMinimum(string body, string time, string lower)
    {
        var layout = LayoutOf(
            $"time {time}\n" +
            "part m { clef treble }\n" +
            "part n { clef bass }\n" +
            $"section S {{ m {{ {StripMovers(body)} | }} "
                + $"n {{ {lower} | }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { grandStaff { staff m staff n } }\n");
        return layout.Systems[0].StaffSprings.Single().MinimumDistance;
    }

    // ⚠️ THE PAREN-TAKING ANNOTATIONS BY NAME, not "@word optionally followed by (...)".
    // A slur opens with '(' too, so the general form turns `g,1@f( g,1)` into `g,1` and
    // deletes the very grob the control is about to look for.
    private static string StripMovers(string body)
        => Regex.Replace(body, @"@(fig|chord|text|mark)\([^)]*\)|@[a-zA-Z]+", "");

    private static ScoreLayout LayoutOf(string body)
    {
        var tree = SyntaxTree.Parse("octave absolute\n" + body);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(
            SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    // CONTROL then assertion: the room must widen for the grob (its ink reaches a staff
    // profile at all), and the stacker's seed must be that same profile.
    private static void SeedSeesWhatTheRoomSees(
        string with, string without, string what, string time, string lower)
    {
        double roomWith = RoomMinimum(with, time, lower);
        double roomWithout = RoomMinimum(without, time, lower);
        Assert.True(roomWith > roomWithout + 0.1,
            $"control: the room must widen for the {what}: "
            + $"with {roomWith:F6}, without {roomWithout:F6}");

        double seedWith = SeedBaseline(with, time), seedWithout = SeedBaseline(without, time);
        Assert.True(seedWith < seedWithout - 0.1,
            $"the below-staff mover must clear the {what}: "
            + $"with {seedWith:F6}, without {seedWithout:F6} "
            + $"(the room moved by {roomWith - roomWithout:F6})");
    }

    // The figure row's baseline on a one-staff book. FiguredBassLayout.YUp is already
    // measured about its own staff's middle, so unlike the chord row below it needs no
    // correction for where the staff ended up.
    private static double FigureBaseline(string body, string time)
    {
        var layout = LayoutOf(
            $"time {time}\n" +
            "part m { clef treble }\n" +
            $"section S {{ m {{ {body} | }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff m }\n");
        return layout.FiguredBassLayouts.Select(f => f.YUp).Min();
    }

    // A chord row hosted by the LOWER staff of a two-staff book, measured ABOVE THAT
    // STAFF.
    // ⚠️ NOT ChordNameLayout.YUp, WHICH IS SYSTEM-RELATIVE — and this is the trap this
    // island keeps setting. Ink that lifts the row also widens the room above the staff
    // that carries it, by the SAME amount, so the row's system-relative Y is unchanged and
    // the reading says "nothing happened" while the row has in fact moved. Measured that
    // way, every book here reads -6.950000 whatever the seed holds.
    private static double ChordRowAboveItsStaff(string upper, string lower, string time)
    {
        var layout = LayoutOf(
            $"time {time}\n" +
            "part pt { clef treble }\n" +
            "part pb { clef bass }\n" +
            $"section S {{ pt {{ {upper} | }} pb {{ {lower} | }} }}\n" +
            "form main { S }\n" +
            "score main \"o\" { staff pt staff pb }\n");
        var staves = layout.Systems[0].StaffGroups.SelectMany(g => g.Staves).ToList();
        return layout.ChordNameLayouts.Select(c => c.YUp).Max() - staves[1].Y;
    }

    /// <summary>
    /// The figured-bass drop clears a SLUR and a TUPLET BRACKET under the staff.
    /// </summary>
    /// <remarks>
    /// The priority argument the drop is built on is exactly what puts these in:
    /// BassFigureAlignmentPositioning declares outside-staff-priority 25, so it is placed
    /// before the dynamics at 250 and clears the INSIDE-staff ink — and a slur, a tie and a
    /// tuplet bracket are inside-staff ink, having no priority of their own. Before
    /// 2026-08-04 the figures read -6.667462 with the slur and without it, and -7.122462
    /// with the bracket and without it.
    /// <para>
    /// ⚠️ THE FIGURE SITS ON THE SECOND NOTE, and that is the same geometry the tie needed:
    /// a figure is a box on one note's column, so a slur whose arc is still at notehead
    /// height there reserves nothing under it. On the FIRST note this book reads -6.667462
    /// before and after the fix alike.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("g,1( g,1@fig(6) g,1)", "g,1 g,1@fig(6) g,1", "slur", "12/4", "d,1 d,1 d,1")]
    [InlineData("voice { b4 b b b } { tuplet 3/2 { c4@fig(6) c c } c2 }",
                "voice { b4 b b b } { c4@fig(6) c c2 }", "tuplet bracket", "4/4", "d,1")]
    public void FiguredBass_ClearsTheInsideStaffSpanners(
        string with, string without, string what, string time, string lower)
    {
        double roomWith = RoomMinimum(with, time, lower);
        double roomWithout = RoomMinimum(without, time, lower);
        Assert.True(roomWith > roomWithout + 0.1,
            $"control: the room must widen for the {what}: "
            + $"with {roomWith:F6}, without {roomWithout:F6}");

        double figWith = FigureBaseline(with, time), figWithout = FigureBaseline(without, time);
        Assert.True(figWith < figWithout - 0.1,
            $"the figures must drop below the {what}: "
            + $"with {figWith:F6}, without {figWithout:F6}");
    }

    /// <summary>
    /// A chord row on a NON-TOP staff clears a SLUR and a TUPLET BRACKET arching over that
    /// staff.
    /// </summary>
    /// <remarks>
    /// The row is placed against its own staff's up-skyline
    /// (<c>LayoutEngine.lowerStaffUpSkyline</c>), because the system silhouette carries only
    /// the topmost staff. That profile is rebuilt rather than read from the room, and until
    /// 2026-08-04 it was rebuilt with no spanners: the row cleared the staff's high notes and
    /// was drawn straight through the bow over them.
    /// <para>
    /// ⚠️ IT CANNOT READ THE ROOM'S FINISHED UP-SKYLINE — that one has
    /// <c>ReserveChordRowBand</c> merged in, so the row would clear the band it reserved for
    /// itself. The SIDE TABLES carry no such reservation, which is what makes them shareable
    /// where the skyline is not.
    /// </para>
    /// </remarks>
    [Fact]
    public void ChordRowOnALowerStaff_ClearsTheInsideStaffSpanners()
    {
        // CONTROL: this row reads its own staff's profile at all. Two pitches, nothing else.
        double high = ChordRowAboveItsStaff("b1 b1 b1", "b'1 b'1@chord(Cm7) b'1", "12/4");
        double low = ChordRowAboveItsStaff("b1 b1 b1", "d,1 d,1@chord(Cm7) d,1", "12/4");
        Assert.True(high > low + 1.0,
            $"control: the row must rise for its own staff's ink: high {high:F6}, low {low:F6}");

        double slurWith = ChordRowAboveItsStaff("b1 b1 b1", "b1( b1@chord(Cm7) b1)", "12/4");
        double slurWithout = ChordRowAboveItsStaff("b1 b1 b1", "b1 b1@chord(Cm7) b1", "12/4");
        Assert.True(slurWith > slurWithout + 0.1,
            $"the row must clear the slur: with {slurWith:F6}, without {slurWithout:F6}");

        double tupWith = ChordRowAboveItsStaff(
            "b1", "voice { tuplet 3/2 { b'4@chord(Cm7) b' b' } b'2 } { d4 d d d }", "4/4");
        double tupWithout = ChordRowAboveItsStaff(
            "b1", "voice { b'4@chord(Cm7) b' b'2 } { d4 d d d }", "4/4");
        Assert.True(tupWith > tupWithout + 0.1,
            $"the row must clear the tuplet bracket: with {tupWith:F6}, without {tupWithout:F6}");
    }

    /// <summary>A below-staff dynamic clears a SLUR drooping out of the staff.</summary>
    /// <remarks>
    /// The shape of the ledger's SD book (audit/lp-geometry/probes/page-vertical.ly,
    /// staff.staff.slur-under-notes): the pitch puts the notes under StaffGrouper's
    /// basic-distance of 9 while the slur's droop beats it, so the reading is the slur's and
    /// not the floor's. Before 2026-08-04 the dynamic read -7.541000 with the slur and
    /// -7.541000 without, while the room widened by 1.417596.
    /// </remarks>
    [Fact]
    public void BelowDynamic_ClearsASlurUnderTheStaff()
        => SeedSeesWhatTheRoomSees(
            "g,1@f( g,1)", "g,1@f g,1", "slur", "8/4", "d,1 d,1");

    /// <summary>A below-staff hairpin clears a TIE drooping out of the staff.</summary>
    /// <remarks>
    /// ⚠️ A HAIRPIN, NOT A DYNAMIC, and the reason is geometry rather than the defect: a tie
    /// attaches AT the two noteheads and dips between them, so the deepest ink it has is at
    /// an x where no note stands and no note-bound box is placed. Measured on the dynamic
    /// instead, this book reads the same number with the tie and without it whatever the seed
    /// holds — a fourth way for two equal numbers to happen, and one that would have looked
    /// like the defect. The hairpin spans the tie and is placed against the profile over that
    /// span. Before 2026-08-04 it read -8.887 with the tie and -8.887 without, while the room
    /// widened by 0.420441.
    /// <para>
    /// ⚠️ THE TERMINATOR IS pp, NOT f, since the DynamicLineSpanner grouping (2026-08-07):
    /// the terminating text rides the hairpin's line, so its descender is in the group's own
    /// quiet outline. With f the quiet line is already deep enough to clear this tie —
    /// MEASURED IN LILYPOND on the twin pair (audit\lpreg\dyngroup-{tie,notie}.ly): the
    /// f book's dynamics sit at identical Y with the tie and without it (20.2316 both),
    /// while the pp book moves 0.187 for the tie (8.000 vs 7.813 below the middle line).
    /// A book that does not separate cannot observe the seed; pp still does.
    /// </para>
    /// </remarks>
    [Fact]
    public void BelowHairpin_ClearsATieUnderTheStaff()
        => SeedSeesWhatTheRoomSees(
            "e,1@cresc e,1~ e,1@pp", "e,1@cresc e,1 e,1@pp", "tie", "12/4", "d,1 d,1 d,1");

    /// <summary>
    /// A below-staff dynamic clears a slur belonging to the staff's SECOND voice.
    /// </summary>
    /// <remarks>
    /// The staff-local layouts are built from a <c>Score</c> made of this staff, and until
    /// 2026-08-04 slurs and ties built it from the primary voice alone — so this book's slur
    /// existed for the renderer and for nothing else. It is the room that was blind here, not
    /// the seed, which is why the CONTROL is the leg that used to fail:
    /// <c>MultiStaffLayouter.StaffLocalScore</c>.
    /// </remarks>
    [Fact]
    public void BelowDynamic_ClearsASecondVoicesSlur()
        => SeedSeesWhatTheRoomSees(
            "voice { b1@f b1 } { g,1( g,1) }", "voice { b1@f b1 } { g,1 g,1 }",
            "second voice's slur", "8/4", "d,1 d,1");

    /// <summary>A below-staff dynamic clears a TUPLET BRACKET under the staff.</summary>
    /// <remarks>
    /// Lily# gives TupletBracket an outside-staff-priority of 200 and stacks it as an
    /// immovable seed of the ABOVE pass; LilyPond gives it none. So "the seed leaves out what
    /// this pass is about to place" does not justify leaving it out of the BELOW pass's seed,
    /// where nothing places it at all. The bracket sits on its voice's stem side, so a second
    /// voice is what puts it under the staff (the ledger's TD book takes the same step).
    /// Before 2026-08-04 the dynamic read -5.880302 either way, while the room widened by
    /// 1.727738.
    /// </remarks>
    [Fact]
    public void BelowDynamic_ClearsATupletBracketUnderTheStaff()
        => SeedSeesWhatTheRoomSees(
            "voice { b4@f b b b } { tuplet 3/2 { c4 c c } c2 }",
            "voice { b4@f b b b } { c4 c c2 }", "tuplet bracket", "4/4", "d,1");
}
