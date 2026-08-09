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

    [Fact]
    public void VoicedDisplacementDiesWithTheSpan()
    {
        // The voiced position is the SPAN's, not the measure's: LilyPond forces a rest
        // inside << { } \\ { } >> (a spacer partner included — the context alone sets
        // Rest.direction), and leaves the music after the span unforced. Pinned against
        // LP 2.26.0 (scratch\lpreg\vrest-probe.{ly,-lp.svg}): the span's r4 at rel −2.0,
        // the trailing r2 ON the middle line. Before the stamp was scoped to the span's
        // reach, the measure-granular voice default voiced the trailing rest too
        // (collision-harmonic-no-dots.ly's r4 sat at −2.0 where LP has the middle), and
        // rests in a spanned staff's OTHER measures leaked the same way through the
        // collision pass's own re-derivation.
        // LILYPOND-REF: scm/music-functions.scm:1042-1057 make-voice-props-set —
        // the forcing lives and dies with each \\ sublist's own Voice context
        // (voicify-sublist wraps each block in its own Voice).
        string svg = Render("time 4/4\n\nvoice { r4 c'4 } { s2 } r2 |\n");
        double middle = MiddleLineY(svg);

        var r4 = Assert.Single(MusicGlyphs(svg, EmmentalerGlyphs.RestQuarter));
        Assert.Equal(-2.0, r4.Y - middle, 0.011);                  // LP −2.0 (voiced +4)

        var r2 = Assert.Single(MusicGlyphs(svg, EmmentalerGlyphs.RestHalf));
        Assert.Equal(0.0, r2.Y - middle, 0.011);                   // LP 0.0 (middle line)
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
