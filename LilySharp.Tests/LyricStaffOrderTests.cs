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
        // `staff melody with lyrics ly` then `staff back`: the words sit BELOW melody
        // (staff 0) and ABOVE back (staff 1) — the reported bug placed them below back.
        var (lyricYs, staffY) = LayoutOf(
            "part melody { section A { c4 d e f } }\n" +
            "part back { section A { e4 f g a } }\n" +
            "lyrics ly { section A { la le li lo } }\n" +
            "form main { A }\n" +
            "score main {\n  staff melody with lyrics ly\n  staff back\n}\n");

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
            "score main {\n  staff melody with lyrics w\n  staff back\n}\n";
        var (latin, _) = LayoutOf(head + "lyrics w { section A { la le li lo } }" + tail);
        var (cjk, _) = LayoutOf(head + "lyrics w { section A { か え る の } }" + tail);

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
            "lyrics v1 { section A { la le li lo } }\n" +
            "lyrics v2 { section A { do re mi fa } }\n" +
            "form main { A }\n";
        var (_, oneVerse) = LayoutOf(head +
            "score main {\n  staff melody with lyrics v1\n  staff back\n}\n");
        var (_, twoVerse) = LayoutOf(head +
            "score main {\n  staff melody with lyrics v1 with lyrics v2\n  staff back\n}\n");

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
            "score main {\n  staff melody with lyrics w\n  staff back\n}\n";
        var (latinLyrics, latinStaff) = LayoutOf(
            head + "lyrics w { section A { la le li lo } }" + tail);
        var (cjkLyrics, cjkStaff) = LayoutOf(
            head + "lyrics w { section A { か え る の } }" + tail);

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
            + $"lyrics w {{ section A {{ {words} }} }}\n"
            + "form main { A }\n"
            + "score main {\n  staff melody with lyrics w\n}\n";

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

    [Fact]
    public void GrandStaffLyrics_StayBelowTheWholeGrandStaff()
    {
        // On a grand staff (one group), lyrics on the top staff sit below the WHOLE
        // group (below the bottom staff), per chorale convention — not between them.
        var (lyricYs, staffY) = LayoutOf(
            "part up { clef treble }\n" +
            "part lo { clef bass }\n" +
            "section A {\n  up { c'4 d' e' f' }\n  lo { c4 d e f }\n  lyrics w { la le li lo }\n}\n" +
            "form main { A }\n" +
            "score main {\n  grandStaff {\n    staff up with lyrics w\n    staff lo\n  }\n}\n");

        double lyricY = lyricYs[0];
        Assert.True(lyricY > staffY[1], $"grand-staff lyrics ({lyricY:F2}) should be below the bottom staff ({staffY[1]:F2})");
    }
}
