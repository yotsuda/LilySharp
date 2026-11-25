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
        // right (measured 18.70 on the twin, audit\lpreg\lyext).
        var svg = LiveRender.SvgFromRenderSpec("""
            octave absolute
            time 4/4
            part v { }
            lyrics w { section Main { Ah __ | | | } }
            section Main {
              v { g'1( | c') | d' | }
            }
            form { ~Main }
            score { staff ~v  lyrics w on v }
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

        // The extender: the long thin box below the staff (past the syllable).
        var extenders = Extenders(svg).Where(e => e.Y > 15.5).ToList();
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
              v { c'4 d' e' f'~ | break f'4 e' d' c' | }
            }
            form { ~Main }
            score { staff ~v  lyrics w on v }
            """);

        // Extender segments: thin (0.08 = the LyricExtender's 0.8 line-thicknesses; a flat
        // 0.100 until session 565) boxes below the first staff.
        var segments = Extenders(svg).Where(e => e.Y > 15).ToList();
        Assert.Equal(2, segments.Count);
        var first = segments.OrderBy(s => s.Y).First();
        var second = segments.OrderBy(s => s.Y).Last();

        // The second piece sits ~one system further down, on the LINE-2 "e"
        // syllable's row (its baseline, the line's box sitting ON it: centre half a
        // thickness above — it sat 0.7 below until session 565), not on line 1's.
        var eNext = Regex.Matches(svg, "<text ([^>]*)>e</text>")
            .Select(m => (Y: Attr2(m.Groups[1].Value, "y"), X: Attr2(m.Groups[1].Value, "x")))
            .OrderBy(t => t.Y).Last();
        Assert.True(second.Y > first.Y + 5,
            $"second segment must be on the next system (got {first.Y} and {second.Y})");
        // (A tolerance, not one decimal: the value is a midpoint of that rounding, which
        // .NET 10 and .NET 11 round apart.)
        Assert.Equal(eNext.Y - 0.04, second.Y, 0.011);
        // And it ends before the "e" syllable it leads into.
        Assert.True(second.X2 < eNext.X,
            "the stub must stop before the syllable it leads into");

        static double Attr2(string attrs, string name) => double.Parse(
            Regex.Match(attrs, name + "=\"([^\"]+)\"").Groups[1].Value,
            CultureInfo.InvariantCulture);
    }

    [Fact]
    public void ExtenderBeforeABreak_WhoseMelismaEndsOnItsOwnLine_DoesNotCrossTheBreak()
    {
        // `la _ _ _ | lu` over `c4 d e f | break g1`: the next syllable is on line 2, but
        // every held note is on line 1. LilyPond completizes the extender on f, so it is
        // never broken: it ends at f's ink right (not the line end) and line 2 draws
        // nothing before "lu" (measured, Lab probes/lyric-extender lp-hand.ly). It ran to
        // the line end and drew a stub on line 2 until session 905.
        var svg = LiveRender.SvgFromRenderSpec("""
            part m { clef treble }
            section S {
              m { c'4 d e f | break g1 | }
              lyrics w { la _ _ _ | lu | }
            }
            form { ~S }
            score { staff m  lyrics w on m }
            """);

        var ext = Assert.Single(Extenders(svg));

        // The quarter-note heads of line 1: the ones above the extender, in x order;
        // f is the last of them.
        var heads = Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*data-pos[^>]*>")
            .Select(m => (X: Attr3(m.Value, "x"), Y: Attr3(m.Value, "y")))
            .Where(h => h.Y < ext.Y)
            .Select(h => h.X).Distinct().OrderBy(x => x).ToList();
        double fInkRight = heads[^1] + GlyphMetrics.GetNoteheadBBox(4).Right;
        // The svg writes two decimals, the head's x included.
        Assert.InRange(ext.X2, fInkRight - 0.02, fInkRight + 0.02);

        static double Attr3(string attrs, string name) => double.Parse(
            Regex.Match(attrs, name + "=\"([^\"]+)\"").Groups[1].Value,
            CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("c4 d e f | break g4 a2. |", "la _ _ _ | _ lu |")]   // held by skips
    [InlineData("c4 d e f~ | break f4 g2. |", "la _ _ _ | lu |")]    // held by a tie
    public void ExtenderHeldAcrossABreak_EndsItsPiecesWhereLilyPondDoes(string music, string words)
    {
        // A broken extender: line 1's piece runs to the end barline's ink left less the
        // padding; line 2's starts at its first held head's ink left plus the padding and
        // ends minimum-length (1.5) past that head — not at the next syllable. LP 2.26:
        // 110.696 = barline 110.78 − 0.08, and 14.416 → 15.836 = head 14.34 + 0.08 → + 1.5
        // (Lab probes/lyric-extender lp-cross.ly). Until session 905 line 1 ended 0.5 short
        // of the system's right and line 2 ran from the measure's left + 0.5 to "lu".
        var svg = LiveRender.SvgFromRenderSpec($$"""
            part m { clef treble }
            section S {
              m { {{music}} }
              lyrics w { {{words}} }
            }
            form { ~S }
            score { staff m  lyrics w on m }
            """);

        var segments = Extenders(svg);
        Assert.Equal(2, segments.Count);
        var first = segments.OrderBy(s => s.Y).First();
        var second = segments.OrderBy(s => s.Y).Last();

        // Line 1's end barline: the first thin bar rect (both systems share its x).
        double barLeft = Regex.Matches(svg, "<rect x=\"([-\\d.]+)\" y=\"[-\\d.]+\" width=\"0.19\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Max();
        Assert.InRange(first.X2, barLeft - 0.08 - 0.02, barLeft - 0.08 + 0.02);

        // Line 2's first head: the leftmost notehead below line 1's lyric row.
        double headLeft = Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*data-pos[^>]*>")
            .Select(m => (X: Attr4(m.Value, "x"), Y: Attr4(m.Value, "y")))
            .Where(h => h.Y > first.Y).Select(h => h.X).Where(x => x > 2).Min();
        Assert.InRange(second.X1, headLeft + 0.08 - 0.02, headLeft + 0.08 + 0.02);
        Assert.InRange(second.X2, headLeft + 1.5 - 0.02, headLeft + 1.5 + 0.02);

        static double Attr4(string attrs, string name) => double.Parse(
            Regex.Match(attrs, name + "=\"([^\"]+)\"").Groups[1].Value,
            CultureInfo.InvariantCulture);
    }

    /// <summary>The extender pieces on the page: LilyPond's round_filled_box, 0.08 tall
    /// (lily/lyric-extender.cc:128-129 Lyric_extender::print) — a rect since session 1389, a 0.080 line before.
    /// Y is the box's middle, where the line's was.</summary>
    private static List<(double X1, double X2, double Y)> Extenders(string svg)
        => Regex.Matches(svg, "<rect x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" width=\"([\\d.]+)\" height=\"0\\.08\"")
            .Select(m =>
            {
                double x = D(m, 1), y = D(m, 2), w = D(m, 3);
                return (x, x + w, y + 0.04);
            })
            .ToList();

    private static double D(Match m, int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
}
