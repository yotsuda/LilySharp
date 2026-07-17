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
                    staffY[st.StaffIndex] = st.Y;
        var lyricYs = layout.LyricLayouts.Where(l => !l.Item.IsLyricsRow).Select(l => l.Y).ToList();
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
