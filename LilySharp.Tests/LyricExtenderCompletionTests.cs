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

using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// lyric-extender-completion.ly: a FINAL extender (no syllable after it) ends at
/// the melisma's last note — not at the next note column, and not dropped.
/// LILYPOND-REF: lily/extender-engraver.cc:241-257 completize_extender — RIGHT
/// bound = heads.back(); lily/lyric-extender.cc:80-84 print — right point is the
/// last head's extent RIGHT.
/// </summary>
[Trait("Category", "Unit")]
public class LyricExtenderCompletionTests
{
    [Fact]
    public void FinalExtender_EndsAtMelismaLastNoteInkRight_NotAtFollowingNote()
    {
        // "Ah __" against g1( c) d — the slur melisma covers g..c, the d has no
        // lyric. LP pins the extender's right end to the c whole note's ink
        // right (measured 18.70 on the twin, scratch\lpreg\lyext).
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            lyrics w { section Main { Ah __ | | | } }
            section Main {
              v { g1( | c) | d | }
            }
            form main { ~Main }
            score main { staff ~v with lyrics w }
            """);

        // Note columns: whole-note glyphs with data-pos, one per measure.
        var heads = Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"[-\\d.]+\"[^>]*data-pos[^>]*>")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().OrderBy(x => x).ToList();
        // time signature + g + c + d
        Assert.Equal(4, heads.Count);
        double cHeadX = heads[2];
        double dHeadX = heads[3];

        // The extender: the long thin line below the staff (past the syllable).
        var extenders = new List<(double X1, double X2)>();
        foreach (Match m in Regex.Matches(svg, "<line ([^>]*)/>"))
        {
            var a = m.Groups[1].Value;
            double y1 = Attr(a, "y1"), y2 = Attr(a, "y2");
            if (y1 == y2 && y1 > 15.5)
                extenders.Add((Attr(a, "x1"), Attr(a, "x2")));
        }
        // It used to be dropped entirely (a final extender needed a NEXT
        // syllable to exist at all).
        var ext = Assert.Single(extenders);

        // Right end = the c head's ink right — the melisma's last note — and
        // strictly short of the d column.
        double cInkRight = cHeadX + GlyphMetrics.GetNoteheadBBox(1).Right;
        Assert.Equal(cInkRight, ext.X2, 2);
        Assert.True(ext.X2 < dHeadX, "the extender must not run on to the next note column");

        static double Attr(string attrs, string name) => double.Parse(
            Regex.Match(attrs, name + "=\"([^\"]+)\"").Groups[1].Value,
            CultureInfo.InvariantCulture);
    }

    [Fact]
    public void BrokenExtender_SecondSegment_SitsOnTheNextSystemsLyricRow()
    {
        // lyric-extender-right-margin.ly's shape: a tied melisma across a break.
        // The stub before "e" on line 2 must sit on LINE 2's lyric row — it used
        // to be flipped against the FIRST system's top and drew over line 1.
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            lyrics w { section Main { c d e effffffffffff __ | e d c | } }
            section Main {
              v { c4 d e f~ | break f4 e d c | }
            }
            form main { ~Main }
            score main { staff ~v with lyrics w }
            """);

        // Extender segments: thin (0.100) horizontal lines below the first staff.
        var segments = new List<(double X1, double X2, double Y)>();
        foreach (Match m in Regex.Matches(svg, "<line ([^>]*stroke-width=\"0.100\"[^>]*)/>"))
        {
            var a = m.Groups[1].Value;
            double y1 = Attr2(a, "y1"), y2 = Attr2(a, "y2");
            double x1 = Attr2(a, "x1"), x2 = Attr2(a, "x2");
            if (y1 == y2 && y1 > 15 && x2 - x1 < 50)   // staff lines span the full width
                segments.Add((x1, x2, y1));
        }
        Assert.Equal(2, segments.Count);
        var first = segments.OrderBy(s => s.Y).First();
        var second = segments.OrderBy(s => s.Y).Last();

        // The second piece sits ~one system further down, on the LINE-2 "e"
        // syllable's row (its baseline + the extender offset), not on line 1's.
        var eNext = Regex.Matches(svg, "<text ([^>]*)>e</text>")
            .Select(m => (Y: Attr2(m.Groups[1].Value, "y"), X: Attr2(m.Groups[1].Value, "x")))
            .OrderBy(t => t.Y).Last();
        Assert.True(second.Y > first.Y + 5,
            $"second segment must be on the next system (got {first.Y} and {second.Y})");
        Assert.Equal(eNext.Y + 0.7, second.Y, 1);
        // And it ends before the "e" syllable it leads into.
        Assert.True(second.X2 < eNext.X,
            "the stub must stop before the syllable it leads into");

        static double Attr2(string attrs, string name) => double.Parse(
            Regex.Match(attrs, name + "=\"([^\"]+)\"").Groups[1].Value,
            CultureInfo.InvariantCulture);
    }
}
