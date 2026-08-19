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
using System.Linq;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Where a note-bound (<c>with lyrics</c>) line sits vertically. Y is Y-DOWN, so a
/// larger Y is lower on the page. Attached to a staff in a NON-last group, the line
/// sits directly below THAT group (between it and the next staff) — not dropped below
/// the whole system. On a grand staff (a single group), it stays below the whole group
/// per chorale convention.
/// </summary>
public class LyricStaffOrderTests
{
    private static (List<double> NoteBoundLyricYs, Dictionary<int, double> StaffY) LayoutOf(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree);
        var multi = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(multi);

        var staffY = new Dictionary<int, double>();
        foreach (var sys in layout.Systems)
            foreach (var sg in sys.StaffGroups)
                foreach (var st in sg.Staves)
                    staffY[st.StaffIndex] = -st.Y;   // Y-up storage → device-down depth
        // LyricLayout stores Y-up from the system top; this test reasons in the
        // system-relative device frame (comparing to staff.Y), so reflect back (= -YUp).
        var lyricYs = layout.LyricLayouts.Where(l => !l.Item.IsLyricsRow).Select(l => -l.YUp).ToList();
        return (lyricYs, staffY);
    }

    [Fact]
    public void UpperStaffLyrics_SitBetweenTheTwoStaves()
    {
        // `staff melody  lyrics ly` then `staff back`: the words sit BELOW melody
        // (staff 0) and ABOVE back (staff 1) — the reported bug placed them below back.
        var (lyricYs, staffY) = LayoutOf(
            "part melody { section A { c4 d e f } }\n" +
            "part back { section A { e4 f g a } }\n" +
            "lyrics ly sings melody { section A { la le li lo } }\n" +
            "form main { A }\n" +
            "score main {\n  staff melody  lyrics ly\n  staff back\n}\n");

        double lyricY = lyricYs[0];
        Assert.True(lyricY > staffY[0], $"lyrics ({lyricY:F2}) should be below melody ({staffY[0]:F2})");
        Assert.True(lyricY < staffY[1], $"lyrics ({lyricY:F2}) should be ABOVE back ({staffY[1]:F2})");
    }

    [Fact]
    public void UpperStaffLyrics_DropByFontHeight_TallCjkClearsFurtherThanLatin()
    {
        // The upper-staff line clears the attached staff via its OWN down-skyline built
        // from real font metrics: a full-em CJK glyph is taller than Latin x/ascender
        // height, so it must drop further to stay clear of the staff. This proves the
        // clearance is dynamic (font height), not a fixed padding.
        const string head =
            "part melody { section A { c4 d e f } }\n" +
            "part back { section A { e4 f g a } }\n";
        const string tail =
            "\nform main { A }\n" +
            "score main {\n  staff melody  lyrics w\n  staff back\n}\n";
        var (latin, _) = LayoutOf(head + "lyrics w sings melody { section A { la le li lo } }" + tail);
        var (cjk, _) = LayoutOf(head + "lyrics w sings melody { section A { か え る の } }" + tail);

        Assert.True(cjk[0] > latin[0] + 0.1,
            $"CJK line ({cjk[0]:F2}) should sit lower than Latin ({latin[0]:F2}) to clear the staff");
    }

    [Fact]
    public void SecondVerseOnUpperStaff_PushesTheLowerStaffDown()
    {
        // Verse 1 fits the ordinary staff-staff gap (so a single verse leaves the
        // lower staff where it was); a 2nd verse reserves extra room, dropping the
        // lower staff by about one verse-spacing so verse 2 doesn't collide with it.
        const string head =
            "part melody { section A { c4 d e f } }\n" +
            "part back { section A { e4 f g a } }\n" +
            "lyrics v1 sings melody { section A { la le li lo } }\n" +
            "lyrics v2 sings melody { section A { do re mi fa } }\n" +
            "form main { A }\n";
        var (_, oneVerse) = LayoutOf(head +
            "score main {\n  staff melody  lyrics v1\n  staff back\n}\n");
        var (_, twoVerse) = LayoutOf(head +
            "score main {\n  staff melody  lyrics v1  lyrics v2\n  staff back\n}\n");

        Assert.True(twoVerse[1] > oneVerse[1] + 2.0,
            $"a 2nd verse should drop the lower staff: 1-verse Y={oneVerse[1]:F2}, 2-verse Y={twoVerse[1]:F2}");
    }

    /// <summary>
    /// The ROOM two staves leave for the block between them is that block's own INK, not a
    /// constant per verse: a tall CJK syllable pushes the lower staff further than a Latin
    /// one, on identical music with identical verse counts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:201-285 — the alignment walks the loose lines
    /// between two spaceable staves and each step is <c>down_skyline.distance (up) +
    /// padding</c>, so the block's glyph heights are IN the distance.
    /// <para>
    /// ⚠️ THE RULE, NOT A VALUE (HANDOFF 5.4): what is asserted is that the room RESPONDS to
    /// the input, which is the only net that catches the shape this island removed —
    /// the constant it replaced (<c>NoteBoundLyricExtraGap</c>, since deleted) read
    /// <c>(verses - 1) * 3.2</c> and nothing else, so both books here returned the SAME
    /// distance and every value-based test still passed. Its sibling
    /// <c>UpperStaffLyrics_DropByFontHeight_TallCjkClearsFurtherThanLatin</c> asserts the
    /// LINE moves; this one asserts the STAFF BELOW moves with it, which is the half that
    /// was missing (the line used to drop into the lower staff's room instead of being
    /// given room of its own).
    /// </para>
    /// </remarks>
    [Fact]
    public void RoomBetweenStaves_ComesFromTheBlocksInk_NotAConstantPerVerse()
    {
        const string head =
            "part melody { section A { c4 d e f } }\n" +
            "part back { section A { e4 f g a } }\n";
        const string tail =
            "\nform main { A }\n" +
            "score main {\n  staff melody  lyrics w\n  staff back\n}\n";
        var (latinLyrics, latinStaff) = LayoutOf(
            head + "lyrics w sings melody { section A { la le li lo } }" + tail);
        var (cjkLyrics, cjkStaff) = LayoutOf(
            head + "lyrics w sings melody { section A { か え る の } }" + tail);

        double latinInside = latinStaff[1] - latinStaff[0];
        double cjkInside = cjkStaff[1] - cjkStaff[0];

        // The line itself drops further (its own sibling asserts this) ...
        Assert.True(cjkLyrics[0] > latinLyrics[0] + 0.1,
            $"the CJK line should sit lower: latin {latinLyrics[0]:F6}, cjk {cjkLyrics[0]:F6}");
        // ... and the staff below has to follow, or the extra drop is spent on the lower
        // staff's clearance instead of being room the block was given.
        Assert.True(cjkInside > latinInside + 0.1,
            "the room between the two staves must respond to the block's ink: "
            + $"latin {latinInside:F6}, cjk {cjkInside:F6}");
    }

    /// <summary>
    /// The PAGE's reservation for a lyric block responds to that block's ink too — a tall
    /// CJK syllable pushes the next system further than a Latin one, on identical music.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:593-599 — <c>build_system_skyline</c> is
    /// handed <c>Align_interface::get_minimum_translations</c>, so the block is in the
    /// system's silhouette at its alignment minimum, and that minimum is made of ink.
    /// <para>
    /// ⚠️ THE RULE, NOT A VALUE (HANDOFF 5.4), and it is the net for TWO shapes this island
    /// removed: an extent sum that never saw the staff's own skyline
    /// (<c>LyricEngraver.AlignmentMinimumBand</c>), and a reservation taken at the DRAWN
    /// force-0 distance (<c>EnrichExtentsWithAnnotationProtrusions</c>), which is a
    /// CONSTANT 5.500000 whatever the syllable is. Under the second one both books here
    /// returned the same gap, and every value-based test still passed.
    /// </para>
    /// </remarks>
    [Fact]
    public void SystemGap_RespondsToTheLyricBlocksInk_NotToTheDrawnDistance()
    {
        // Enough bars to wrap, so there IS an inter-system gap, and one staff, so the block
        // hangs below the system rather than between two staves.
        string bars = string.Concat(Enumerable.Repeat("c4 d e f | ", 24)).Trim();
        string latinWords = string.Concat(Enumerable.Repeat("la le li lo | ", 24)).Trim();
        string cjkWords = string.Concat(Enumerable.Repeat("か え る の | ", 24)).Trim();
        string Src(string words) =>
            $"part melody {{ section A {{ {bars} }} }}\n"
            + $"lyrics w sings melody {{ section A {{ {words} }} }}\n"
            + "form main { A }\n"
            + "score main {\n  staff melody  lyrics w\n}\n";

        double latin = SystemGapOf(Src(latinWords), FloorBindingPaper);
        double cjk = SystemGapOf(Src(cjkWords), FloorBindingPaper);

        Assert.True(latin > 0 && cjk > 0, "the books must wrap, or there is no gap to read");
        // ⚠️ AND THAT THE FLOOR IS WHAT IS BEING READ (HANDOFF 5.0 trap 7): a reading of the
        // spring's ideal means the reservation lost, and then the two books agree whatever
        // the ink is — the test would go on passing while measuring nothing.
        Assert.True(latin > FloorBindingPaper.VerticalSpacing.SystemSystem.BasicDistance,
            $"the reservation must beat the spring's ideal, or nothing is being measured: {latin:F6}");
        Assert.True(cjk > latin + 0.05,
            "the page must reserve the block's own ink: "
            + $"latin gap {latin:F6}, cjk gap {cjk:F6}");
    }

    [Fact]
    public void SystemGap_ReadsARowsBandOnce_TheTwoSpellingsAgree()
    {
        // The same words under the same melody, spelled as an unbound ROW and as a
        // `sings` verse: the page must hold the next system at the SAME distance,
        // because the reservation both floors read is the same block — the anchor
        // staff's bottom line plus the block's own ink (LyricReservationBelowSystem).
        // The row spelling used to add the band on top of the system's BODY height,
        // which already contains the row's drawn band — a frame mix that priced the
        // gap 19.836 where the verse spelling (and the intended floor) says 14.571,
        // against LilyPond 2.26.0's 12.000 on the machine-exported twin (the residual
        // is the scalar band vs LilyPond's in-skyline X-aware distance — HANDOFF §2D).
        // Notes stay inside the staff so the anchor's down skyline is flat and the
        // walk cannot split the two spellings by X.
        string body =
            "part melody {\n"
            + "  section A { e'4 f' g' f' | e' e' e'2 | }\n"
            + "  section B { g'4 g' f' f' | e' e' e'2 | }\n"
            + "}\n"
            + "lyrics w{SINGS} {\n"
            + "  section A { One two three four | five six se- ven | }\n"
            + "  section B { Aa bb cc dd | ee ff gg | }\n"
            + "}\n"
            + "form main { A break B }\n"
            + "score main {\n  staff melody\n  lyrics w\n}\n";

        double row = SystemGapOf(body.Replace("{SINGS}", ""));
        double sings = SystemGapOf(body.Replace("{SINGS}", " sings melody"));

        Assert.True(row > 0 && sings > 0, "both spellings must break into two systems");
        Assert.True(System.Math.Abs(row - sings) < 1e-6,
            $"the two spellings must price the gap identically: row {row:F6}, sings {sings:F6}");
    }

    /// <summary>
    /// Paper whose inter-system spring has almost no ideal left, so the SKYLINE FLOOR is what
    /// holds two systems apart.
    /// </summary>
    /// <remarks>
    /// ⚠️ ADDED 2026-07-28, and the reason is the point of the test rather than plumbing. On
    /// the shipping spring this book used to bind because Lily#'s lyric em was 3.2 — 29.6%
    /// larger than LilyPond's LyricText size — so the block's ink beat basic-distance 12. At
    /// the true size it does not, and LilyPond agrees: its own reading on this shape is
    /// 12.000000, the ideal (audit/lp-geometry lyrics.*.system-gap, exact on both sides since
    /// the em was corrected). So the test had quietly left its regime — both books returned
    /// 12.000000 and the assertion below could no longer fail for the right reason. Taking the
    /// ideal away puts the floor back in charge without inventing ink, the same move book SCF
    /// makes in probes/system-clef-floor.ly.
    /// LILYPOND-REF: lily/page-layout-problem.cc:625-632 append_system.
    /// </remarks>
    private static readonly LayoutOptions FloorBindingPaper =
        LayoutOptions.Default with
        {
            VerticalSpacing = VerticalSpacingParameters.Default with
            {
                SystemSystem = VerticalSpacingParameters.Default.SystemSystem with
                {
                    BasicDistance = 0,
                    MinimumDistance = 0,
                },
            },
        };

    /// <summary>The distance between the first two systems, 0 if the book does not wrap.</summary>
    private static double SystemGapOf(string src, LayoutOptions? options = null)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree);
        var engine = options is null ? new LayoutEngine() : new LayoutEngine(options);
        var layout = engine.Layout(SvgGenerator.CollectScore(tree, spec));
        if (layout.Systems.Length < 2) return 0;
        return System.Math.Abs(layout.Systems[0].Y - layout.Systems[1].Y);
    }

    /// <summary>
    /// A lyric hangs off the staff it is attached to, INSIDE a grand staff as much as
    /// anywhere else: words on the top staff of a grand staff sit between the two staves.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS TEST ASSERTED THE OPPOSITE until 2026-08-04, under the name
    /// <c>GrandStaffLyrics_StayBelowTheWholeGrandStaff</c> and the reason "per chorale
    /// convention". The convention is real, but the rule that implemented it was per-GROUP
    /// and a grand staff is ONE group, so it also said that every staff of an SATB grand
    /// staff shares one baseline — and four voices with a line each were drawn on top of
    /// each other. The fixture it was written for (showcase/08-chorale) had no LilyPond
    /// evidence behind it either: `lysc ly` drops lyrics entirely, so the twin that
    /// "verified" it carries none.
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 loose_lines — the run a
    /// non-spaceable line belongs to is bounded by the spaceable lines around it, which on
    /// a grand staff means the staff below ends the top staff's run.
    /// </remarks>
    [Fact]
    public void GrandStaffLyrics_SitUnderTheirOwnStaff()
    {
        var (lyricYs, staffY) = LayoutOf(
            "part up { clef treble }\n" +
            "part lo { clef bass }\n" +
            "section A {\n  up { c'4 d' e' f' }\n  lo { c4 d e f }\n  lyrics w sings up { la le li lo }\n}\n" +
            "form main { A }\n" +
            "score main {\n  grandStaff {\n    staff up  lyrics w\n    staff lo\n  }\n}\n");

        double lyricY = lyricYs[0];
        Assert.True(lyricY > staffY[0],
            $"the words ({lyricY:F2}) should be below the staff they are attached to ({staffY[0]:F2})");
        Assert.True(lyricY < staffY[1],
            $"the words ({lyricY:F2}) should be ABOVE the lower staff ({staffY[1]:F2}), not below the whole group");
    }

    /// <summary>
    /// A note-bound line clears the DYNAMIC hanging under its own staff, not just that
    /// staff's notes: the silhouette the line is placed against is the one the room was
    /// measured from, side tables and all.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:71-87 get_skylines — the alignment asks each
    /// element for its silhouette by reading that grob's own <c>vertical-skylines</c>
    /// property, and for a staff that is its VerticalAxisGroup's, which every outside-staff
    /// grob of the staff is in (lily/axis-group-interface.cc:220-238 generic_group_extent).
    /// There is no second, thinner silhouette for the placement to read.
    /// <para>
    /// ⚠️ TWO ASSERTIONS BECAUSE THERE WERE TWO SPELLINGS. The ROOM has always read the
    /// full tables (<c>MultiStaffLayouter.BuildAllStaffSkylines</c> passes dynamics,
    /// articulations, tuplet brackets, slurs, ties and beams); the DRAWN baseline read
    /// <c>SkylineBuilder.BuildStaffSkylines</c> with all of them left at their defaults,
    /// under a comment claiming it was "the same silhouette the room was measured from".
    /// So the staff below moved to make room for the <c>f</c> and the syllable did not
    /// move with it — a syllable drawn over a dynamic, with the gap between the staves
    /// still correct. A test that reads only the room, or only the line, sees nothing.
    /// </para>
    /// <para>
    /// ⚠️ THE RULE, NOT A VALUE (HANDOFF 5.4): what is asserted is that both quantities
    /// RESPOND to the dynamic. Pinning either number would have to be re-approved every
    /// time the dynamic's own placement moves, and would not say the two agree.
    /// </para>
    /// <para>
    /// ⚠️ AND WHAT IS STILL BLIND, MEASURED 2026-08-04 AND NOT FIXED HERE: an ordinary
    /// below-staff SCRIPT is in neither spelling. <c>BuildAllStaffSkylines</c> passes only
    /// <c>CalculateTabStaffLocal</c> — a tab staff's forced-above marks — so an ordinary
    /// marcato is not in the room either, and making the drawn side read the room (which is
    /// what this test's fix did) puts the two on ONE blind spot instead of giving each its
    /// own. The same mark on the same note, this book with the second staff removed:
    /// <code>
    ///                      room (staff gap)          drawn lyric baseline
    /// one staff            n/a                       7.864960 → 9.119960   cleared
    /// two staves, upper    10.402001 → 10.402001     7.864960 → 7.864960   overprinted
    /// </code>
    /// The one-staff case was then cleared by a DIFFERENT path — <c>AugmentSkylinesWithScripts</c>
    /// over the SYSTEM silhouette — which is also why the corpus never caught this: its only
    /// script-versus-lyric book, <c>test/lyrics-below-marcato</c>, has one staff.
    /// ⚠️ BOTH HALVES HAVE SINCE CLOSED: the scripts went into the per-staff skyline
    /// (SkylineBuilder.AddArticulationLayoutsToSkyline), and since 2026-08-20 the one-staff
    /// lyric block reads that same per-staff profile — LyricEngraver.DistributeLooseLines'
    /// below-the-system family reads its anchor staff's own Down instead of the silhouette,
    /// which is what put the staff's DYNAMICS in front of it too (audit/lp-geometry
    /// lyrics.dynamic.staff-to-lyric, −1.668349 → +0.024651). One profile, every family.
    /// </para>
    /// </remarks>
    [Fact]
    public void LyricBaseline_RespondsToADynamicUnderItsOwnStaff()
    {
        const string tail =
            "part back { section A { e4 f g a } }\n" +
            "lyrics w sings melody { section A { la le li lo } }\n" +
            "form main { A }\n" +
            "score main {\n  staff melody  lyrics w\n  staff back\n}\n";
        var (plainLyrics, plainStaff) = LayoutOf(
            "part melody { section A { c4 d e f } }\n" + tail);
        var (dynLyrics, dynStaff) = LayoutOf(
            "part melody { section A { c4@f d e f } }\n" + tail);

        // The room does respond, and has all along — this is the control. If it ever stops
        // responding, the two assertions below agree for the wrong reason.
        double plainInside = plainStaff[1] - plainStaff[0];
        double dynInside = dynStaff[1] - dynStaff[0];
        Assert.True(dynInside > plainInside + 0.1,
            "control: the room between the staves must widen for the dynamic: "
            + $"plain {plainInside:F6}, with f {dynInside:F6}");

        // ...and the syllable has to travel with it, or it is drawn where the `f` is.
        // MEASURED: room 10.402001 → 13.569303 and line 7.864960 → 11.032262, the same
        // 3.167302 on both. Before the fix the room moved by it and the line by nothing.
        Assert.True(dynLyrics[0] > plainLyrics[0] + 0.1,
            "the line must clear the dynamic under its own staff: "
            + $"plain {plainLyrics[0]:F6}, with f {dynLyrics[0]:F6}");
    }

    /// <summary>
    /// Every staff of a grand staff carrying its own line gets its OWN baseline — the SATB
    /// case, and the one the per-group rule collapsed to a single Y.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ASSERTION IS ON DISTINCTNESS, not on values: four rows drawn at one Y is the
    /// defect, and it is invisible to any test that looks at one row. MEASURED before the
    /// fix on the reported book: four rows, all at y=44.22.
    /// </remarks>
    [Fact]
    public void EachGrandStaffVoiceWithLyrics_GetsItsOwnBaseline()
    {
        // ⚠️ NOT `s`/`a`/`b` FOR THE PART NAMES — they are reserved (a spacer rest and two
        // pitches), and the harness does not throw on a source that fails to parse: the
        // first draft of this test named them that, engraved nothing, and reported one
        // baseline, which is indistinguishable from the defect it is meant to catch.
        var (lyricYs, _) = LayoutOf(
            "part sop { clef treble }\npart alt { clef treble }\n" +
            "part ten { clef treble }\npart bas { clef bass }\n" +
            "section A {\n  sop { c'4 d' e' f' }\n  alt { c'4 d' e' f' }\n" +
            "  ten { c'4 d' e' f' }\n  bas { c4 d e f }\n" +
            "  lyrics w1 sings sop { la le li lo }\n  lyrics w2 sings alt { la le li lo }\n" +
            "  lyrics w3 sings ten { la le li lo }\n  lyrics w4 sings bas { la le li lo }\n}\n" +
            "form main { A }\n" +
            "score main {\n  grandStaff {\n    staff sop  lyrics w1\n    staff alt  lyrics w2\n" +
            "    staff ten  lyrics w3\n    staff bas  lyrics w4\n  }\n}\n");

        var baselines = lyricYs.Select(y => System.Math.Round(y, 6)).Distinct().ToList();
        Assert.Equal(4, baselines.Count);
    }

    /// <summary>
    /// A line under one staff clears the SCRIPT that the staff BELOW it hangs into the same
    /// gap — the fermata of a lower part is drawn over the syllable of the upper part's line
    /// otherwise. The reported book: an SATB chorale where every part ends on a fermata and
    /// every staff carries the same verse.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:937-978 <c>skyline_spacing</c> — a staff's
    /// <c>vertical-skylines</c> is <c>inside_staff_skylines</c> MERGED with every outside-staff
    /// grob the collision pass placed (<c>add_grobs_of_one_priority</c>), so a Script is in the
    /// silhouette <c>Align_interface</c> walks whether it declares an outside-staff-priority
    /// (the fermata family's 75, scm/script.scm) or not. There is exactly one silhouette per
    /// staff in LilyPond and the scripts are in it.
    /// <para>
    /// ⚠️ THE RULE, NOT A VALUE (HANDOFF 5.4): what is asserted is that the drawn ink does not
    /// overlap, and — as the control — that the ROOM responds at all. Pinning either number
    /// would have to be re-approved every time the fermata's own placement moves.
    /// </para>
    /// <para>
    /// ⚠️ MEASURED IN THE DEVICE FRAME THE RENDERER ACTUALLY DREW, not from the layout
    /// records: the defect this closes was two spellings of one quantity (HANDOFF 7.7), and a
    /// test that reads one of the two spellings cannot see them disagree.
    /// </para>
    /// </remarks>
    [Fact]
    public void LyricBaseline_ClearsAScriptOnTheStaffBelowIt()
    {
        // ⚠️ WHOLE NOTES AND A HIGH ONE, AND BOTH MATTER. A quarter's stem already holds the
        // staves further apart than the mark ever reaches, so on a stemmed book the fermata
        // does not bind and the numbers are identical with the defect and without it
        // (measured on the first draft of this test: 19.902001 either way). The mark must also
        // sit above a note that is itself out of the staff, or the staff lines are what binds.
        // The reported book ends on whole notes under a shared verse.
        const string head =
            "part up { section A { e'1 } }\n";
        const string tail =
            "lyrics w sings up { section A { la } }\n" +
            "form main { A }\n" +
            "score main {\n  grandStaff {\n    staff up  lyrics w\n    staff lo\n  }\n}\n";
        var plain = LpFidelity.RenderedGeometry.Render(
            head + "part lo { section A { c''1 } }\n" + tail);
        var fermata = LpFidelity.RenderedGeometry.Render(
            head + "part lo { section A { c''1@fermata } }\n" + tail);

        // The control: the gap between the two staves must widen for the mark at all. If it
        // stops responding the overlap assertion below can pass for the wrong reason.
        double plainGap = plain.StaffGapAt(0);
        double fermataGap = fermata.StaffGapAt(0);
        Assert.True(fermataGap > plainGap + 0.1,
            "control: the room between the staves must widen for the lower staff's fermata: "
            + $"plain {plainGap:F6}, with the mark {fermataGap:F6}");

        // ...and the syllable over that note must not be printed on the mark. Device Y is
        // DOWN, so the glyph's ink TOP is its drawn origin minus the box's up-extent.
        var marks = fermata.Glyphs
            .Where(g => g.Glyph == EmmentalerGlyphs.FermataAbove).ToList();
        Assert.Single(marks);
        double markInkTop = marks[0].Y - GlyphMetrics.FermataAboveGlyph.Top;

        var syllable = fermata.LyricSyllables
            .OrderBy(t => System.Math.Abs(t.X - marks[0].X)).First();
        Assert.True(markInkTop > syllable.Y,
            $"the syllable \"{syllable.Text}\" (baseline {syllable.Y:F6}) is printed on the "
            + $"lower staff's fermata (ink top {markInkTop:F6}): the mark is above the "
            + "syllable's baseline, so the two overlap");
    }

    /// <summary>
    /// The ONE-STAFF half of the same rule, which travels by a different road: a script under
    /// the staff joins the SYSTEM silhouette (<c>LayoutEngine.AugmentSkylinesWithScripts</c>)
    /// and the line drops below that.
    /// </summary>
    /// <remarks>
    /// ⚠️ WRITTEN BECAUSE THE ROAD ACQUIRED A WAY TO BE SILENT (session 191). The augment is
    /// now built on DEMAND — the consumer that indexes it is what runs the merge — which
    /// removes it entirely from the many books that carry scripts and no row below the staff.
    /// A consumer that stops indexing it therefore stops getting it, and until this test the
    /// only thing that would have noticed on this road was ONE snapshot
    /// (<c>test/lyrics-below-marcato</c>, whose byte-identity a rebase can approve away). The
    /// sibling test above covers the MULTI-staff road. Since 2026-08-20 the two roads read
    /// ONE profile — the anchor staff's own down-skyline — so this test now guards that the
    /// below-the-system family kept it (the numbers it measured on the old road: baseline
    /// 7.864960 → 9.119960).
    /// <para>
    /// ⚠️ THE RULE, NOT A VALUE (HANDOFF 5.4): what is asserted is that the baseline RESPONDS
    /// to the mark, plus the positive control that the mark is actually engraved. Pinning
    /// either number would need re-approval every time the marcato's own placement moves.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnOneStaff_TheLyricBaselineDropsBelowAScriptUnderTheStaff()
    {
        // The fixture's own shape (test/lyrics-below-marcato): a LOW note so the mark hangs
        // under the staff rather than over it, and one staff so the multi-staff road is not
        // the one being measured.
        const string tail =
            "lyrics w sings melody { section A { la le li lo } }\n" +
            "form main { A }\n" +
            "score main { staff melody  lyrics w }\n";
        var (plainLyrics, _) = LayoutOf(
            "part melody { clef treble\n  section A { c4 c g' g } }\n" + tail);
        var (markedLyrics, _) = LayoutOf(
            "part melody { clef treble\n  section A { c4@marcato c g' g } }\n" + tail);

        Assert.NotEmpty(plainLyrics);
        Assert.NotEmpty(markedLyrics);
        Assert.True(markedLyrics[0] > plainLyrics[0] + 0.1,
            "the one-staff line must drop below the marcato under its own staff: "
            + $"plain {plainLyrics[0]:F6}, with the mark {markedLyrics[0]:F6}");
    }
}
