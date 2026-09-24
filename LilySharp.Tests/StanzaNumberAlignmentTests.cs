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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A stanza number is LilyPond's StanzaNumber: side-positioned LEFT of its supports with
/// padding 1.0, and its supports are EVERY verse's first syllable (Stanza_number_align_engraver),
/// so "1." and "2." end together, 1.0 left of the leftmost first syllable's ink — even when
/// verse 1's own syllable starts further right. Until session 567 the number started a flat
/// 4.0 left of the first measure, wherever the syllables were.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3412-3427 StanzaNumber — direction LEFT, padding 1.0, X-offset x-aligned-side (stanza-number-interface)
/// LILYPOND-REF: lily/stanza-number-align-engraver.cc:62-71 Stanza_number_align_engraver — add_support of every syllable to every number
/// MEASURED on 2.26.0 (Lab sessions/p567/stanza-lp.log, a hand twin of test/lyrics-verses
/// with \set stanza): "1." and "2." both end at 5.659 = "Twas" 6.659 − 1.0, verse 1's "A"
/// starting at 8.349. Poison: the old measures[0].X − 4.0 reddens both facts.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class StanzaNumberAlignmentTests
{
    private const double Tolerance = 0.012;   // two decimals in the SVG, twice (two texts)

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static string Render() => LiveRender.SvgFromRenderSpec("""
        time 4/4
        key c major
        part melody { clef treble }
        section Main {
          melody { c'4 d e f | g2 a4 g | }
          lyrics words sings melody { A- ma- zing grace | how~ sweet | }
          lyrics words sings melody { Twas grace that taught | my~ heart | }
        }
        form main { Main }
        score main { staff melody  lyrics words }
        """);

    /// <summary>Each stanza number's right edge: its start plus its advance.</summary>
    private static double[] NumberRightEdges(string svg) => Regex.Matches(svg,
            "<text x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"([\\d.]+)\" font-weight=\"bold\">(\\d\\.)</text>")
        .Select(m => D(m.Groups[1].Value)
            + TextFontMetrics.Advance(m.Groups[3].Value, D(m.Groups[2].Value), sans: false, FontStyle.Bold))
        .ToArray();

    /// <summary>A centred syllable's ink left: its centre less half its advance.</summary>
    private static double SyllableInkLeft(string svg, string syllable)
    {
        var m = Regex.Match(svg,
            "<text x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"([\\d.]+)\" text-anchor=\"middle\" data-pos=\"\\d+\">"
            + Regex.Escape(syllable) + "</text>");
        Assert.True(m.Success, syllable);
        return D(m.Groups[1].Value)
            - TextFontMetrics.Advance(syllable, D(m.Groups[2].Value), sans: false, FontStyle.Regular) / 2;
    }

    [Fact]
    public void BothNumbersEndTogether_APaddingLeftOfTheLeftmostFirstSyllable()
    {
        var svg = Render();
        var edges = NumberRightEdges(svg);
        Assert.Equal(2, edges.Length);
        // "A-" prints as "A" plus a hyphen line; the syllable's ink is the "A".
        double leftmost = System.Math.Min(SyllableInkLeft(svg, "A"), SyllableInkLeft(svg, "Twas"));
        double expected = leftmost - StanzaNumberEngraver.Padding;
        Assert.InRange(edges[0], expected - Tolerance, expected + Tolerance);
        Assert.InRange(edges[1], expected - Tolerance, expected + Tolerance);
    }

    [Fact]
    public void TheWiderVersesSyllableDecides_NotEachVersesOwn()
    {
        var svg = Render();
        // "Twas" is wider than "A": verse 1's number does not creep right to its own syllable.
        double aLeft = SyllableInkLeft(svg, "A");
        double twasLeft = SyllableInkLeft(svg, "Twas");
        Assert.True(twasLeft < aLeft, $"the probe needs the wider syllable on verse 2: A- {aLeft}, Twas {twasLeft}");
        foreach (var edge in NumberRightEdges(svg))
            Assert.True(edge < aLeft - StanzaNumberEngraver.Padding - 0.1,
                $"the number ends at {edge}, which is not clear of verse 1's own syllable {aLeft} by more than the padding");
    }
}
