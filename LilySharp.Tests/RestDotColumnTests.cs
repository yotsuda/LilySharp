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
/// A rest's augmentation dot is placed by the dot COLUMN it shares with the other
/// voices' dotted items, solved at PURE positions, and rides the rest's later shift.
/// LILYPOND-REF: lily/dot-column.cc:143-150, 194-227 calc_positioning_done;
/// lily/dot-configuration.cc:25-44 badness.
/// </summary>
[Trait("Category", "Unit")]
public class RestDotColumnTests
{
    [Fact]
    public void RestDotYieldsToTheNoteDotInItsColumn()
    {
        // dot-column-vertical-positioning.ly: voice one's f'8. dot and voice three's
        // r8. dot both enter the column at +4 (the head's line / the voiced rest's
        // pure position). The note dot takes +5; the rest dot then goes DOWN to +3 —
        // pushing it up would cascade the note dot to +7 (badness 20 against 5) —
        // and rides the rest's unpure push (+4 → +14) to land at +13, LP's rel −6.5.
        // Solo it would have gone UP (+15, rel −7.5), which is what the old fixed
        // "one position above the origin" rule drew.
        // Pinned against the LP twin (scratch\lpreg\dot-column-vertical-positioning.{ly,svg}).
        string svg = Render("time 4/4\n\nvoice { f'8. e16 } { s8. s16 } { r8. a'16 } |\n");
        double middle = MiddleLineY(svg);

        var r8 = Assert.Single(MusicGlyphs(svg, EmmentalerGlyphs.Rest8th));
        Assert.Equal(-7.0, r8.Y - middle, 0.011);                  // LP −7.0 (unpure push)

        var dots = MusicGlyphs(svg, EmmentalerGlyphs.AugmentationDot)
            .OrderBy(d => d.Y).ToList();
        Assert.Equal(2, dots.Count);
        Assert.Equal(-6.5, dots[0].Y - middle, 0.011);             // LP −6.5 rest dot (+3 ridden to +13)
        Assert.Equal(-2.5, dots[1].Y - middle, 0.011);             // LP −2.5 f' dot (+5)
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
