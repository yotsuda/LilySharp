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
/// The rest movers CHAIN in LilyPond's callback order: the voiced position and
/// Rest_collision translate first, and Beam::rest_collision_callback evaluates the
/// rest's ink where they put it, adding its push on top.
/// LILYPOND-REF: lily/beam.cc:1388-1414 Beam::rest_collision_callback — prev_offset
/// translates rest_extent (:1388-1390) and the return is offset + shift (:1414).
/// </summary>
[Trait("Category", "Unit")]
public class BeamRestChainTests
{
    [Fact]
    public void BeamPushChainsOnTheVoicedPosition()
    {
        // dot-rest-beam-trigger.ly: voice one beams six sixteenths across a rest,
        // voice two holds two dotted eighth rests. The beamed r16 starts at the
        // voiced +4 (rel −2.0) and the beam callback, pricing its ink THERE,
        // pushes it one whole space down to +2 — LP renders it at rel −1.0.
        // Larger-wins merging kept the voiced +4 and the push never landed.
        // Pinned against the LP twin (audit\lpreg\dot-rest-beam-trigger.{ly,svg}).
        string svg = Render("time 12/16\n\nvoice { c'16[ b a r b g] } { r8. r } |\n");
        double middle = MiddleLineY(svg);

        var r16 = Assert.Single(MusicGlyphs(svg, EmmentalerGlyphs.Rest16th));
        Assert.Equal(-1.0, r16.Y - middle, 0.011);                 // LP −1.0 (voiced +4, beam −2)

        // Voice two's rests take the voiced −4 (rel +2.0) untouched — they are in
        // no beam — and their dots ride along at +1.5.
        var r8s = MusicGlyphs(svg, EmmentalerGlyphs.Rest8th);
        Assert.Equal(2, r8s.Count);
        Assert.All(r8s, r => Assert.Equal(2.0, r.Y - middle, 0.011));   // LP +2.0
        var dots = MusicGlyphs(svg, EmmentalerGlyphs.AugmentationDot);
        Assert.Equal(2, dots.Count);
        Assert.All(dots, d => Assert.Equal(1.5, d.Y - middle, 0.011));  // LP +1.5
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
