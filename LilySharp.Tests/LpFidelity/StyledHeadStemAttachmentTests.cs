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

using System;
using System.Linq;
using LilySharp.Core.Svg;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// A STYLED head attaches each stem at the font's own per-direction point — the LILC
/// <c>attachment</c> / <c>attachment-down</c> entries — not at a hand rule. On the
/// s2triangle the two are not mirrors: the up stem begins 0.1262 ABOVE the head's
/// centre at the head's right edge x 1.3828, the down stem 0.6828 BELOW it at x 0.2186.
/// </summary>
/// <remarks>
/// The book is lilypond-src/input/regression/flag-stem-begin-position.ly ("Stems reach
/// correct begin points of merged noteheads"), twinned with \aikenHeads replaced by
/// 'triangle on both sides (audit/lp-regression/lys/flag-stem-begin-position.lys).
/// Every number below is LilyPond 2.26.0's own drawn stem from that twin
/// (audit/lpreg/flagstem-lp.svg): down-stem begin middle+2.1828 on f (centre 1.5 +
/// 0.6828), up-stem begin middle+1.3738 (1.5 − 0.1262), and the two stems of one merged
/// column stand 1.0342 apart (= (1.3828 − 0.065) − (0.2186 + 0.065)).
/// LILYPOND-REF: lily/stem.cc:934-963 internal_calc_stem_begin_position — begin = head
///   position + the font attachment Y.
/// LILYPOND-REF: lily/open-type-font.cc:334-369 attachment_point — DOWN reads the
///   font's attachment-down entry; it is not a reflection of the UP one.
/// </remarks>
[Trait("Category", "Unit")]
public class StyledHeadStemAttachmentTests
{
    private const string Src = """
        octave absolute
        time 4/4

        part v { clef treble }

        section Main {
          v {
            voice { f'8@notehead(triangle) f'4:32@notehead(triangle) e'8@notehead(triangle) f'8@notehead(triangle) s4 s8 }
                  { f'8@notehead(triangle) f'4@notehead(triangle) e'8@notehead(triangle) f'8@notehead(triangle) s4 s8 } |
          }
        }

        form { ~Main }

        score { staff v }
        """;

    [Fact]
    public void TriangleHeads_AttachEachStemAtTheFontsOwnPoint()
    {
        var page = RenderedGeometry.Render(Src);
        double middle = Assert.Single(page.StaffRefpoints());

        // The stems: vertical strokes at stem thickness. The tremolo slashes are
        // 0.48 thick and slanted, the staff and ledger lines horizontal — neither
        // passes the vertical + thickness filter.
        var stems = page.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9
                        && Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-9)
            .Select(l => (X: l.X1, Top: Math.Min(l.Y1, l.Y2), Bottom: Math.Max(l.Y1, l.Y2)))
            .OrderBy(s => s.X)
            .ToList();
        Assert.Equal(8, stems.Count); // 4 merged columns x (down + up)

        // Moment 1, f eighths merged: the DOWN stem begins 0.6828 below the head's
        // centre (LP 2.1828 below the middle line), the UP stem 0.1262 above it
        // (LP 1.3738). LP's drawn values from the twin: 2.1828 / 1.3738.
        var down = stems[0];
        var up = stems[1];
        Assert.Equal(middle + 2.1828, down.Top, 3);
        Assert.Equal(middle + 1.3738, up.Bottom, 3);

        // One merged column's two stems stand (1.3828 − 0.065) − (0.2186 + 0.065)
        // = 1.0342 apart — the font's two attachment X's, each pulled half a stem
        // thickness toward its head. LP: 9.905 − 8.874.
        Assert.Equal(1.0342, up.X - down.X, 3);

        // Moment 3, e eighths (one staff position lower): the same offsets ride the
        // head, LP 2.6828 / 1.8738 below the middle line.
        Assert.Equal(middle + 2.6828, stems[4].Top, 3);
        Assert.Equal(middle + 1.8738, stems[5].Bottom, 3);
    }

    /// <summary>
    /// The DEFAULT head reads the font's point too: a black head's stem begins 0.1862 from its
    /// centre, a half head's 0.259 — until session 1377 a hand rule's 0.15 and the centre.
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1377/stem — `stem-begin-position` dumped):
    /// c''4 down 0.6276, a'4 up −0.6276, c''2 down 0.4820, e'2 up −3.4820 half spaces.
    /// </summary>
    [Fact]
    public void DefaultHeads_AttachTheStemAtTheFontsOwnPoint()
    {
        var page = RenderedGeometry.Render("""
            octave absolute
            time 4/4
            part v { clef treble }
            section Main { v { c''4 a'4 c''2 | e'2 r2 | } }
            form { ~Main }
            score { staff v }
            """);
        double middle = Assert.Single(page.StaffRefpoints());
        var stems = page.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9
                        && Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-9)
            .Select(l => (X: l.X1, Top: Math.Min(l.Y1, l.Y2), Bottom: Math.Max(l.Y1, l.Y2)))
            .OrderBy(s => s.X)
            .ToList();
        Assert.Equal(4, stems.Count);
        Assert.Equal(middle - 0.3138, stems[0].Top, 3);    // c''4, down
        Assert.Equal(middle + 0.3138, stems[1].Bottom, 3); // a'4, up
        Assert.Equal(middle - 0.2410, stems[2].Top, 3);    // c''2, down
        Assert.Equal(middle + 1.7410, stems[3].Bottom, 3); // e'2, up
    }

    /// <summary>
    /// A grace head reads ITS font's point, flagged or beamed: 0.114 from the centre.
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1378/m b1, b3): g' grace stems' feet at
    /// −0.886, a' at −0.386 from the middle line. Session 1377 read the full design (0.186)
    /// on a flagged grace; a beamed grace's stem began at the head's centre until session 1378.
    /// </summary>
    [Fact]
    public void GraceHeads_AttachTheStemAtTheirOwnFontsPoint()
    {
        var page = RenderedGeometry.Render("""
            octave absolute
            time 4/4
            part v { clef treble }
            section Main { v { grace { g'16 } c''4 grace { g'8 a' } c''4 r2 | } }
            form { ~Main }
            score { staff v }
            """);
        double middle = Assert.Single(page.StaffRefpoints());
        var feet = page.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9 && l.StrokeWidth < 0.2)
            .Select(l => Math.Max(l.Y1, l.Y2) - middle)
            .ToList();
        Assert.Equal(2, feet.Count(f => Math.Abs(f - 0.886) < 0.006)); // the g's, flagged and beamed
        Assert.Single(feet, f => Math.Abs(f - 0.386) < 0.006);         // the beamed a'
    }
}
