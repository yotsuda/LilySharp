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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The keep-inside-line rod at a line's END measures to the end bar line's RIGHT edge.
/// LilyPond's rod runs to the end-of-line column (lily/simple-spacer.cc:558
/// <c>add_rod (i, cols.size (), keep_inside_line_[RIGHT])</c>), and a break-aligned group at
/// a line end is placed with its right edge ON that column
/// (lily/break-alignment-interface.cc:273-274 Break_alignment_interface::calc_positioning_done,
/// BarLine's right-edge entry adding nothing), so the bar line hangs to the column's left and
/// ink may run up to the bar line's right edge. Lily#'s spring chain ends where the bar line
/// begins, so its rod is the overhang less the bar line's ink — one rod, MultiStaffLayouter.
/// <para>
/// MEASURED (session 357, scratch/p358/lv, LilyPond 2.26.0 ragged-right on the twin
/// <c>lysc ly</c> writes): test/lyrics-verses' last bar — a g1 under the left-aligned melisma
/// syllable "saved" (ink 6.3507) — is 8.251 wide in LilyPond (bar lines 63.298 → 71.549, the
/// syllable's right edge on 71.739 = the bar line's right edge); Lily# drew it 8.44, one
/// bar-line ink (0.19) too wide, the residual the snapshot carried since session 352. The
/// same book without its lyrics is exact in every bar (8.150 for the last), so the ink half
/// of the rod and every spring were already right.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public class LineEndKeepInsideLineTests
{
    private const string Sung = """
        paper { raggedRight }
        time 4/4
        key c major
        part melody { clef treble }
        section Main {
          melody { c'4 d e f | g2 a4 g | f4 e d c | g1 | }
          lyrics words sings melody { A- ma- zing grace | how~ sweet | the sound that | saved~ | }
          lyrics words sings melody { Twas grace that taught | my~ heart | to fear and | grace~ | }
        }
        form main { Main }
        score main { staff melody  lyrics words }
        """;

    private const string Unsung = """
        paper { raggedRight }
        time 4/4
        key c major
        part melody { clef treble }
        section Main {
          melody { c'4 d e f | g2 a4 g | f4 e d c | g1 | }
        }
        form main { Main }
        score main { staff melody }
        """;

    [Fact]
    public void TheLastBar_UnderAWideMelismaSyllable_IsAsWideAsLilyPonds()
    {
        var bars = BarLineXs(Sung);
        Assert.Equal(4, bars.Count);
        // LilyPond 71.549 − 63.298.
        Assert.InRange(bars[3] - bars[2], 8.251 - 0.03, 8.251 + 0.03);
    }

    [Fact]
    public void TheSameBar_Unsung_IsUntouched()
    {
        // Positive control: no syllable reaches past the whole note's spring, so the rod is
        // inert and the bar is the spring's — LilyPond 52.957 − 44.807.
        var bars = BarLineXs(Unsung);
        Assert.Equal(4, bars.Count);
        Assert.InRange(bars[3] - bars[2], 8.150 - 0.03, 8.150 + 0.03);
    }

    [Fact]
    public void TheSyllable_WidensTheBar_ByItsReachPastTheSpring_NotPastTheBarLine()
    {
        // The two bars differ by what the syllable reaches beyond the spring AND the bar
        // line's ink: LilyPond 8.251 − 8.150 = 0.101. Rodded to the bar line's left edge
        // the difference was 0.29 — the ink counted twice.
        var sung = BarLineXs(Sung);
        var unsung = BarLineXs(Unsung);
        Assert.InRange((sung[3] - sung[2]) - (unsung[3] - unsung[2]), 0.101 - 0.03, 0.101 + 0.03);
    }

    /// <summary>
    /// The same rod at the line's START: LilyPond's runs from the line-start column at the
    /// line's left edge, whose prefix hangs to the right (break-alignment-interface.cc:265-266,
    /// the left-edge item on the column). "Twas" — verse 2's first syllable — reaches 2.387
    /// left of the first head; its left edge sits under the meter (6.198) and LilyPond leaves
    /// the head at 8.585 (same book, same measurement). Lily# rodded the reach from the
    /// prefix's right edge and pushed the head to 8.97 (= the meter's 6.58 + 2.39).
    /// </summary>
    [Fact]
    public void TheFirstNote_UnderAWideFirstSyllable_StaysWhereLilyPondPutsIt()
    {
        Assert.InRange(FirstHeadX(Sung), 8.585 - 0.05, 8.585 + 0.05);
        // Positive control — the unsung line, LilyPond 8.585 / Lily# 8.59.
        Assert.InRange(FirstHeadX(Unsung), 8.585 - 0.05, 8.585 + 0.05);
    }

    private static double FirstHeadX(string source)
    {
        // Music glyphs at full size, in document order: clef (0.80), meter (4.88), first head.
        var xs = Regex.Matches(Svg(source), "<text class=\"music\" x=\"([0-9.-]+)\" y=\"[0-9.-]+\" font-size=\"4.00\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        return xs[2];
    }

    private static string Svg(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    private static List<double> BarLineXs(string source)
    {
        string svg = Svg(source);
        var rows = Regex.Matches(svg, "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                          W: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                          H: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .Where(r => r.W < 0.5 && r.H > 3)
            .GroupBy(r => System.Math.Round(r.Y, 1))
            .Select(g => g.Select(r => r.X).Distinct().OrderBy(x => x).ToList())
            .ToList();
        return Assert.Single(rows);
    }
}
