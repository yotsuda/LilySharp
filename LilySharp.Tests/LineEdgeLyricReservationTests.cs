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
/// A lyric line broken across two systems reserves NOTHING at the break — LilyPond has no
/// line-end or line-start lyric reservation, only the keep-inside-line rod (lily/simple-spacer.cc:558
/// <c>add_rod (i, cols.size (), keep_inside_line_[RIGHT])</c> and its LEFT twin) and the
/// hyphen's own after-break rod (lily/lyric-hyphen.cc:180-193), which does not bind here.
/// Lily# used to re-supply, at a system's edges, the "halves" the cross-bar rod port dropped
/// mid-line (inkR + 0.4 at the end, inkL + 0.4 at the start — LyricSpacing's
/// LineEndLyricReservation / LineStartLyricFloor, "kept until measured"); both are retired.
/// <para>
/// MEASURED (session 357, scratch/p358/lehyph, LilyPond 2.26.0 ragged-right on the twins
/// <c>lysc ly</c> writes): the first system's last bar — <c>g a b c'</c> under
/// "lit- tle star bright-" — is 18.686 wide (bar lines 25.487 → 44.173) whether the line
/// ends on "bright-" (hyphenated into the next system) or on "bright" (a word end), the
/// syllable's ink (37.329 + 7.034 = 44.363) ending ON the end bar line's right edge; Lily#
/// drew it 19.28 (the 0.4 plus one bar-line ink). The second system's first note stands at
/// the plain 5.8 whether it opens under "ly", "lyrically" (ink 9.287, from "bright-") or
/// "Lyrically" (10.141, after "bright"), the wide syllables starting at 1.809 / 1.382 under
/// the clef; Lily# put it at 7.72 / 8.15 (the floor). Unsung, the same music has a 13.378
/// last bar (20.357 → 33.735) and the same 5.8.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public class LineEdgeLyricReservationTests
{
    private const string Music = "c'4 d e f | g a b c' | break d'4 c' b a | g1 |";

    private static string Sung(string words) => $$"""
        paper { raggedRight }
        time 4/4
        key c major
        part melody { clef treble }
        section Main {
          melody { {{Music}} }
          lyrics words sings melody { {{words}} }
        }
        form main { Main }
        score main { staff melody  lyrics words }
        """;

    private const string Unsung = $$"""
        paper { raggedRight }
        time 4/4
        key c major
        part melody { clef treble }
        section Main {
          melody { {{Music}} }
        }
        form main { Main }
        score main { staff melody }
        """;

    private const string HyphenOnward = "Twin- kle twin- kle | lit- tle star bright- | ly shin- ing far | way |";
    private const string WordEnd = "Twin- kle twin- kle | lit- tle star bright | ly shin- ing far | way |";
    private const string HyphenOntoWide = "Twin- kle twin- kle | lit- tle star bright- | lyrically shin- ing far | way |";
    private const string WordEndOntoWide = "Twin- kle twin- kle | lit- tle star bright | Lyrically shin- ing far | way |";

    [Theory]
    [InlineData(HyphenOnward)]
    [InlineData(WordEnd)]
    public void TheLastBarOfASystem_UnderASyllableThatRunsOn_IsAsWideAsLilyPonds(string words)
    {
        var rows = BarLineXs(Sung(words));
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);
        // LilyPond 44.173 − 25.487, hyphen or word end alike.
        Assert.InRange(rows[0][1] - rows[0][0], 18.686 - 0.03, 18.686 + 0.03);
    }

    [Fact]
    public void AHyphenAtTheBreak_ReservesNoMoreThanAWordEnd()
    {
        // LilyPond's two last bars are the same to the digit; the hyphen's after-break rod
        // does not bind. Asserted as a difference so it stays about the hyphen alone.
        var hy = BarLineXs(Sung(HyphenOnward))[0];
        var wd = BarLineXs(Sung(WordEnd))[0];
        Assert.InRange((hy[1] - hy[0]) - (wd[1] - wd[0]), -0.01, 0.01);
    }

    [Theory]
    [InlineData(HyphenOntoWide)]
    [InlineData(WordEndOntoWide)]
    [InlineData(HyphenOnward)]
    public void TheFirstNoteOfTheNextSystem_UnderAWideFirstSyllable_StaysAtLilyPonds5Point8(string words)
    {
        // LilyPond 5.8 — the plain clef-to-first-note distance of a continuation — with the
        // syllable running back under the clef.
        Assert.InRange(SecondSystemFirstHeadX(Sung(words)), 5.8 - 0.05, 5.8 + 0.05);
    }

    [Fact]
    public void TheSameMusicUnsung_IsThePositiveControl()
    {
        // Without lyrics the last bar is the springs' own (LilyPond 33.735 − 20.357) and the
        // continuation opens at the same 5.8 — so the sung numbers above are the syllables'
        // doing at the END (the bar widens by the ink) and NOT at the START (nothing moves).
        var rows = BarLineXs(Unsung);
        Assert.Equal(2, rows.Count);
        Assert.InRange(rows[0][1] - rows[0][0], 13.378 - 0.03, 13.378 + 0.03);
        Assert.InRange(SecondSystemFirstHeadX(Unsung), 5.8 - 0.05, 5.8 + 0.05);
    }

    /// <summary>
    /// The hyphen of a word broken across the systems ("bright- | ly") is drawn on the FIRST
    /// system only, from the syllable's ink right edge, one full dash long — past the line's
    /// end, since the syllable's ink already stands on the end bar line's right edge — and
    /// nothing opens the next system (the line-start piece spans no musical time and is
    /// killed). MEASURED (session 357, scratch/p358/lv/hy-lp.svg, LilyPond 2.26.0): the one
    /// LyricHyphen rect on the page sits at system x 44.363 = "bright"'s ink right (37.329 +
    /// 7.034) = the end bar line's right edge (44.173 + 0.19), width 0.66 (LyricHyphen
    /// length, scm/define-grobs.scm:2154), and the second system has none.
    /// LILYPOND-REF: lily/lyric-hyphen.cc:107-121 Lyric_hyphen::print — a piece whose RIGHT
    ///   bound is broken neither squeezes nor disappears;
    /// LILYPOND-REF: scm/define-grobs.scm:2151 after-line-breaking = ly:spanner::kill-zero-spanned-time.
    /// </summary>
    [Fact]
    public void TheHyphenAtTheBreak_StartsAtTheSyllablesInkRight_AndNothingOpensTheNextSystem()
    {
        string svg = Svg(Sung(HyphenOnward));
        var rows = BarLineXsOf(svg);
        double endBarRight = rows[0][^1] + 0.19;
        // Horizontal 0.130-thick strokes are the hyphen dashes (staff lines are 0.100,
        // stems are vertical).
        var dashes = Regex.Matches(svg, "<line x1=\"([0-9.-]+)\" y1=\"([0-9.-]+)\" x2=\"([0-9.-]+)\" y2=\"([0-9.-]+)\" stroke=\"#000000\" stroke-width=\"0.130\"")
            .Select(m => (X1: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          Y1: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                          X2: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                          Y2: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .Where(d => d.Y1 == d.Y2)
            .ToList();
        var atBreak = Assert.Single(dashes, d => d.X1 > endBarRight - 0.5);
        Assert.InRange(atBreak.X1 - endBarRight, -0.02, 0.02);
        Assert.InRange(atBreak.X2 - atBreak.X1, 0.66 - 0.01, 0.66 + 0.01);
        // No dash on the second system: every remaining dash lies left of the first
        // system's last bar and is one of the in-line hyphens (Twin- kle, twin- kle,
        // lit- tle, shin- ing), none of which begins under the second system's clef.
        Assert.DoesNotContain(dashes, d => d != atBreak && d.X1 < 5.0);
    }

    private static string Svg(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    /// <summary>Bar-line X positions per system, systems top to bottom, each ascending.</summary>
    private static List<List<double>> BarLineXs(string source) => BarLineXsOf(Svg(source));

    private static List<List<double>> BarLineXsOf(string svg)
    {
        return Regex.Matches(svg, "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                          W: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                          H: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .Where(r => r.W < 0.5 && r.H > 3)
            .GroupBy(r => System.Math.Round(r.Y, 1))
            .OrderBy(g => g.Key)
            .Select(g => g.Select(r => r.X).Distinct().OrderBy(x => x).ToList())
            .ToList();
    }

    /// <summary>
    /// The second system's first note head: the full-size music glyphs come system by
    /// system in document order, each system opening with its clef at x 0.80, and a
    /// continuation carries no meter — so it is the glyph right after the SECOND clef.
    /// </summary>
    private static double SecondSystemFirstHeadX(string source)
    {
        var xs = Regex.Matches(Svg(source), "<text class=\"music\" x=\"([0-9.-]+)\" y=\"[0-9.-]+\" font-size=\"4.00\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        int secondClef = xs.Select((x, i) => (x, i)).Where(p => p.x is > 0.7 and < 0.9).Select(p => p.i).ElementAt(1);
        return xs[secondClef + 1];
    }
}
