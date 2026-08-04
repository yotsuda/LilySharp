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

using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A below-staff annotation belonging to the UPPER staff of two must not extend the room
/// below the LAST staff. Its ink lives BETWEEN the staves, so the room it needs is the staff
/// spring's to give.
/// </summary>
/// <remarks>
/// <para>
/// This is the machine for a claim no ledger reading could take. The corpus measures the
/// foot of a page's spring chain (audit/lp-geometry <c>dynamic.page.*</c>,
/// <c>hairpin.page.*</c>, <c>figbass.page.*</c>) and that reading only means the ink under
/// the last staff while the foot spring sits on its FLOOR — which needs a full page. A
/// two-staff system is tall enough that LilyPond puts seven of them on the page and stretches
/// the rest, so books DYPU / DYPHU in <c>dynamic-page.ly</c> were measured and left
/// unentered: their foot reads f ≈ 0.378 against a block of 0.068, i.e. the page's force and
/// not the ink. That header says what a ledger entry would need instead (a two-staff page
/// that COMPRESSES).
/// </para>
/// <para>
/// So the claim gets a test in the commit that relies on it (HANDOFF §5.0 — a re-based
/// snapshot is not an observer). What it guards is the defect
/// <c>EstimateLooseLineExtents</c> had: the estimate was taken per SYSTEM from the ITEMS,
/// with no staff anywhere in the sentence, so a dynamic on the upper staff of two charged its
/// 2.0 (a hairpin its 1.5) below the WHOLE system — the same shape as the figured-bass drop
/// that had no staff in it either. Five committed fixtures shortened by 0.33 to 0.67 of page
/// height when it went, every one of them multi-staff.
/// </para>
/// <para>
/// LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 <c>build_system_skyline</c> — a
/// system's bottom skyline is built from what is BELOW its last spaceable staff; a grob
/// between two staves is inside the alignment and reaches it through
/// <c>Align_interface</c>'s translation instead (lily/align-interface.cc:228-238), which is
/// the second assertion here.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LooseLineExtentScopeTests
{
    /// <summary>Two staves, the upper one optionally carrying <paramref name="mark"/>.</summary>
    /// <remarks>The two halves are the same string with one substitution, so they cannot drift
    /// apart the way a hand-copied pair can (HANDOFF §5.0).</remarks>
    private static string TwoStaffScore(string mark) => $$"""
        octave absolute
        time 4/4
        key c major

        part up { clef treble }
        part down { clef bass }

        section Main {
          up { c'4{{mark}} d' e' f' | g'1 | }
          down { c4 d e f | g1 | }
        }

        form main { ~Main }

        score main "loose-line-scope" {
          staff ~up
          staff ~down
        }
        """;

    [Theory]
    [InlineData("@f")]      // a dynamic — EstimateLooseLineExtents' 2.0
    [InlineData("@cresc")]  // a hairpin — its 1.5, the branch gated on there being no dynamics
    public void AnUpperStaffAnnotationDoesNotExtendTheRoomBelowTheLastStaff(string mark)
    {
        var bare = RenderedGeometry.Render(TwoStaffScore(""));
        var annotated = RenderedGeometry.Render(TwoStaffScore(mark));

        // THE CLAIM: nothing hangs below the LOWER staff in either score, so the page leaves
        // the same room under it. An estimate that charges an upper-staff annotation to the
        // system's down extent breaks this by exactly its own constant.
        Assert.Equal(bare.LastStaffRefpointToFoot(), annotated.LastStaffRefpointToFoot(), 9);

        // ...AND THE TEST IS NOT VACUOUS, which needs saying separately because the assertion
        // above is an EQUALITY: a mark that never reached the layout at all would satisfy it
        // perfectly. ⚠️ The witness is the PLACED grob and not a second distance — measured
        // while writing this, neither the staff gap nor the room under a lone staff moves for
        // a hairpin (9.000000 and 9.230551 with it and without), because its thin ink loses to
        // the staff spring's ideal on one side and to the notes' own reach on the other. A
        // pair whose witness is a distance that does not move proves nothing.
        var layout = Layout(TwoStaffScore(mark));
        Assert.True(layout.DynamicLayouts.Length + layout.HairpinLayouts.Length > 0,
            $"no {mark} reached the layout at all, so the equality above is vacuous.");
    }

    private static LilySharp.Core.Svg.Layout.ScoreLayout Layout(string source)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(source);
        var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!;
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, spec);
        return new LilySharp.Core.Svg.Layout.LayoutEngine().Layout(score);
    }

    // Two systems, an independent chord ROW leading the score, so system 2 OPENS with a loose
    // line and the block hanging below system 1 is closed by system 2's first spaceable staff.
    // That staff's FIRST voice holds the given item; `n` and its lyrics are there because
    // BuildLooseChainEnds does not run on a score with no lyrics at all.
    private static string LeadingRowScore(string firstVoice)
    {
        string bar = $"voice {{ {firstVoice} }} {{ b4 b b b }} |";
        return
            "octave absolute\n" +
            "part m { clef treble }\npart n { clef bass }\n" +
            "chords prog { c1 | g1 | c1 | g1 | }\n" +
            $"section Main {{\n  m {{ {bar} {bar} break {bar} {bar} }}\n" +
            "  n { b4 b b b | b4 b b b | b4 b b b | b4 b b b | }\n" +
            "  lyrics w { la le li lo la le li lo la le li lo la le li lo }\n}\n" +
            "form main { Main }\n" +
            "score main \"leading-row-closing\" { chords prog staff m staff n with lyrics w }\n";
    }

    // How far the chord row that OPENS SYSTEM 2 stands above the staff that closes its chain.
    // System 2's row is the chain-solved one; system 1's is placed by the alignment, so
    // reading system 1 would pass whatever the chain did.
    private static (double Clearance, double RestShift, int Systems) LeadingRowClearance(
        string firstVoice)
    {
        var layout = Layout(LeadingRowScore(firstVoice));
        var staves = layout.Systems[^1].StaffGroups.SelectMany(g => g.Staves).ToList();
        return (staves.Single(s => s.StaffIndex == 0).Y - staves.Single(s => s.StaffIndex == 1).Y,
                layout.GetRestShift(measureIndex: 0, voiceIndex: 0, itemIndex: 0),
                layout.Systems.Length);
    }

    /// <summary>
    /// The loose-line chain closes on the next system's first spaceable staff, and that
    /// staff's up-skyline has to carry the rest another voice pushed UP out of it — or the
    /// row that opens the system is solved into the rest.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:923-925 <c>loose_line_min_distances</c> — the
    /// closing member of a loose block is pushed the same <c>min_offsets</c> difference as
    /// every other, and those offsets are measured against each staff's own
    /// <c>vertical-skylines</c> (lily/align-interface.cc:71-87 <c>get_skylines</c>). A Rest is
    /// inside-staff ink and LilyPond translates the one grob (lily/rest-collision.cc:211-290
    /// <c>calc_positioning_done</c>), so the skyline it hands the chain already holds the
    /// moved rest.
    /// <para>
    /// ⚠️ THE FOURTH AND LAST of the call sites that build their own profile from
    /// <c>SkylineBuilder.BuildStaffSkylines</c>, and the only one reached from the PAGE pass —
    /// which is why the rest shift is the ONE side table it can be given: <c>Rest_collision</c>
    /// is a function of the music alone, so the room's memo already holds the answer this
    /// early (see <c>LayoutEngine.LeadingLinesOfSystem</c>'s remark for the six that cannot
    /// follow). MEASURED: system 2's row stood 3.497100 above the closing staff with the rests
    /// printed and 3.497100 with them spacers — the moved rest bought the row nothing, while
    /// the ROOM had already opened 2.534000 for it, so the row was engraved into the gap the
    /// rest occupies. After: 6.031100 against the control's 3.497100, the 2.534000 LilyPond
    /// itself gives a rest pushed up out of a staff (audit/lp-geometry
    /// <c>staff.staff.rest-over-notes</c> against its control).
    /// </para>
    /// <para>
    /// ⚠️ SYSTEM 2, NOT SYSTEM 1. System 1's leading row is placed by the alignment inside its
    /// own system and moved with the room from the first day the room had the table, so an
    /// assertion on it is green either way. The chain only reaches the row that OPENS the
    /// NEXT system.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARowOpeningTheNextSystem_ClearsARestPushedOutOfTheStaffThatClosesItsChain()
    {
        var moved = LeadingRowClearance("r4 r r r");
        var spacer = LeadingRowClearance("s4 s s s");
        var highNotes = LeadingRowClearance("g''4 g'' g'' g''");

        Assert.Equal(2, moved.Systems);   // the break took; there IS a next system to open
        Assert.True(moved.RestShift >= 5.0,
            "premise: Rest_collision must push this rest up out of the staff, "
            + $"got {moved.RestShift:F6} staff positions");

        Assert.True(highNotes.Clearance > spacer.Clearance + 0.1,
            "control: the closing distance must respond to that staff's up-skyline: "
            + $"high notes {highNotes.Clearance:F6}, spacer control {spacer.Clearance:F6}");

        Assert.True(moved.Clearance > spacer.Clearance + 0.1,
            "the row must clear the rest pushed up out of the staff that closes its chain: "
            + $"printed rests {moved.Clearance:F6}, spacer control {spacer.Clearance:F6}");
    }

    /// <summary>
    /// ...and the same closing staff's up-skyline has to carry its TUPLET BRACKET, which is
    /// inside-staff ink in LilyPond exactly as the rest is.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:4097 — TupletBracket lists outside-staff-interface
    /// and sets no outside-staff-priority, so it is never pushed out and joins the staff's
    /// own vertical-skylines like the clef.
    /// Lily#'s priority 200 is its own number and does not license leaving it out of a
    /// profile nothing places it in.
    /// <para>
    /// ★ THE SIBLING ABOVE SAID THE OTHER SIX SIDE TABLES COULD NOT FOLLOW THE REST SHIFT
    /// HERE, "because the fix is to reach the room's result, and the per-staff list does not
    /// exist before the page pass". Three of them can now: the room hands its slurs, ties and
    /// tuplet brackets out with the skylines (<c>MultiStaffLayouter.StaffInsideSpanners</c>)
    /// and <c>BuildLooseChainEnds</c> runs after the placement that produces them, so this
    /// call site reaches them by lookup and still does not lay anything out twice.
    /// </para>
    /// <para>
    /// ⚠️ THE BRACKET AND NOT A BOW, and that is this book rather than the rule: the closing
    /// staff's first voice carries stems UP, so its tuplet bracket sits above (the stem side)
    /// while a slur would curve below and a tie stays inside the notes' own reach. MEASURED
    /// on this book: the tie pair reads 9.947093 both ways. What reaches UP out of THIS staff
    /// is the bracket, so the bracket is what can witness the up-skyline.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARowOpeningTheNextSystem_ClearsATupletBracketOverTheStaffThatClosesItsChain()
    {
        var spacer = LeadingRowClearance("s4 s s s");
        var highNotes = LeadingRowClearance("g''4 g'' g'' g''");
        var with = LeadingRowClearance("tuplet 3/2 { g''4 g'' g'' } g''2");
        var without = LeadingRowClearance("g''4 g'' g''2");

        Assert.Equal(2, with.Systems);   // the break took; there IS a next system to open

        Assert.True(highNotes.Clearance > spacer.Clearance + 0.1,
            "control: the closing distance must respond to that staff's up-skyline: "
            + $"high notes {highNotes.Clearance:F6}, spacer control {spacer.Clearance:F6}");

        Assert.True(with.Clearance > without.Clearance + 0.1,
            "the row must clear the tuplet bracket over the staff that closes its chain: "
            + $"with {with.Clearance:F6}, without {without.Clearance:F6}");
    }
}
