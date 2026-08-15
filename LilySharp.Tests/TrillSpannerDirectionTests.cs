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
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A trill spanner's direction: the writer's <c>.up</c>/<c>.down</c> wins, then the
/// voice default (TrillSpanner is a direction-polyphonic grob), then the grob's UP —
/// and a new start ends the running spanner at its own column.
/// LILYPOND-REF: scm/scheme-engravers.scm:1806-1822 Trill_spanner_engraver;
/// LILYPOND-REF: scm/music-functions.scm:617-634 direction-polyphonic-grobs.
/// </summary>
[Trait("Category", "Unit")]
public class TrillSpannerDirectionTests
{
    [Fact]
    public void VoiceTwoTrillsSitBelowAndUpDownOverride()
    {
        // trill-spanner-direction.ly: \voiceTwo, four g's each starting a trill with no
        // stop — bare (voice default DOWN), .up, .down, bare. LP 2.26.0 draws the "tr"
        // glyphs at rel +9.6, −2.55, +9.6, +11.564 (the fourth stacks BELOW the third's
        // wave, which its glyph overlaps in X — the below-staff collision pass).
        // Pinned against the LP twin (audit\lpreg\trillsdir-lp.{ly,svg}).
        // Before the port: all four UP, and the chained starts lost the first three
        // spanners entirely (the pending start was overwritten, not ended).
        string svg = Render(
            "octave absolute\ntime 4/4\n\n"
            + "voice { s1 } { g,4@startTrillSpan g,4@startTrillSpan.up"
            + " g,4@startTrillSpan.down g,4@startTrillSpan } |\n");
        double middle = MiddleLineY(svg);

        var trs = MusicGlyphs(svg, EmmentalerGlyphs.OrnTrill)
            .OrderBy(t => t.X).ToList();
        Assert.Equal(4, trs.Count);
        Assert.Equal(9.6, trs[0].Y - middle, 0.02);    // LP +9.6  (voiceTwo default DOWN)
        Assert.Equal(-2.55, trs[1].Y - middle, 0.02);  // LP −2.55 (.up overrides)
        Assert.Equal(9.6, trs[2].Y - middle, 0.02);    // LP +9.6  (.down)
        Assert.Equal(11.56, trs[3].Y - middle, 0.02);  // LP +11.564 (stacked below #3)
    }

    private static string Render(string source) =>
        LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>The middle staff line's device Y: the 3rd of the five long horizontals.</summary>
    private static double MiddleLineY(string svg)
    {
        var lineYs = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                && double.Parse(m.Groups[3].Value) - double.Parse(m.Groups[1].Value) > 5)
            .Select(m => double.Parse(m.Groups[2].Value))
            .Distinct().OrderBy(v => v).ToList();
        Assert.Equal(5, lineYs.Count);
        return lineYs[2];
    }

    /// <summary>All music glyphs of one codepoint: (X, Y) in document order.</summary>
    private static List<(double X, double Y)> MusicGlyphs(string svg, char glyph) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => m.Groups[3].Value[0] == glyph)
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value)))
            .ToList();
}
