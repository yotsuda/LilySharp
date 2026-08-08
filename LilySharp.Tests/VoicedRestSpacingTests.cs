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
/// A voiced rest's separation box: the rest enters horizontal spacing at its PURE
/// voiced position with its real glyph box, so ink that only overlaps the rest
/// where the voice puts it is priced there.
/// LILYPOND-REF: lily/separation-item.cc:163 boxes — pure_y_extent.
/// </summary>
[Trait("Category", "Unit")]
public class VoicedRestSpacingTests
{
    [Fact]
    public void AccidentalClearsTheShiftedDownRest()
    {
        // spacing-accidental-rest.ly: "Accidentals don't collide with
        // shifted-down rests." voice-2's r8 lowers (voiced base −4, pushed to
        // −7 by the collision), and the next column's DOUBLE FLAT reaches left
        // at exactly that height. The spacing prices the flat against the
        // rest's PURE voiced box (the collision push is unpure and stays out:
        // pure-chain-offset-callback passes the previous offset through) —
        // before the port the rest entered as a phantom notehead on the MIDDLE
        // line, the flat's Y never met it, and the flat ran 1.01 into the rest.
        // Pinned against the LP twin (scratch\lpreg\spacc-rest.{lys,-gen.ly}).
        // LILYPOND-REF: lily/rest-collision.cc:76-84 add_column —
        //   Lily::pure_chain_offset_callback;
        // LILYPOND-REF: scm/output-lib.scm:1273-1278 pure-chain-offset-callback.
        string svg = Render("octave absolute\n\nvoice { g4 } { r8 aeses,8 } |\n");
        double middle = MiddleLineY(svg);

        var rest = Assert.Single(MusicGlyphs(svg, EmmentalerGlyphs.Rest8th));
        Assert.Equal(8.58, rest.X, 0.05);                          // LP 8.5850
        Assert.Equal(3.50, rest.Y - middle, 0.011);                // LP +3.50 (shifted down)

        var flat = Assert.Single(MusicGlyphs(svg, EmmentalerGlyphs.AccidentalDoubleFlat));
        Assert.Equal(10.30, flat.X, 0.06);                         // LP 10.3022 (was 9.29)

        var heads = MusicGlyphs(svg, EmmentalerGlyphs.NoteheadBlack);
        Assert.Equal(2, heads.Count);
        Assert.Equal(12.10, heads.Max(h => h.X), 0.06);            // LP 12.1022 (was 11.09)
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
