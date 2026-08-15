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
/// A beamed stem is spaced by its PURE height — the beam group's united reach — not by
/// its own unbeamed length: spacing runs before any beam is quanted, and LilyPond's
/// skyline boxes, note-spacing stem correction and staff-spacing optical correction all
/// read the same pure Y extent.
/// LILYPOND-REF: lily/stem.cc:387-447 Stem::internal_pure_height (the calc_beam branch);
/// LILYPOND-REF: lily/note-spacing.cc:272-273 stem_dir_correction — pure_y_extent.
/// </summary>
[Trait("Category", "Unit")]
public class BeamedPureStemSpacingTests
{
    [Fact]
    public void AccidentalClearsTheBeamCarriedStem()
    {
        // stem-pure-height-beamed.ly: forced-up eighths alternating A3 with G#5/Bb5,
        // beamed — the A3 stems are carried two octaves past their heads by the beam.
        // The Bb5's flat must clear the PRECEDING A3 stem's pure reach: with only the
        // unbeamed band the two never meet in Y and the flat packed 0.67 too close
        // (gap 1.35 where LilyPond draws 2.02). LilyPond's `!` forced accidentals are
        // dropped on both sides (no Lily# spelling); the claim stands without them.
        // Pinned against the LP twin (audit\lpreg\stempure.{lys,-gen.ly}).
        string svg = Render(
            "octave absolute\n\n" +
            "part m { }\n" +
            "section A {\n" +
            "  m { gis'8@stemUp a,@stemUp bes'@stemUp a,@stemUp" +
            " bes'@stemUp a,@stemUp bes'@stemUp a,@stemUp | }\n" +
            "}\n" +
            "form main { A }\n" +
            "score main { staff m }\n");

        var heads = MusicGlyphs(svg, EmmentalerGlyphs.NoteheadBlack);
        Assert.Equal(8, heads.Count);
        Assert.Equal(9.34, heads[0].X, 0.02);    // LP 9.34  (G#5)
        Assert.Equal(11.59, heads[1].X, 0.02);   // LP 11.59 (A3)
        Assert.Equal(14.76, heads[2].X, 0.02);   // LP 14.76 (Bb5 — the flat's column)
        Assert.Equal(17.02, heads[3].X, 0.02);   // LP 17.02
        Assert.Equal(19.77, heads[4].X, 0.02);   // LP 19.77
        Assert.Equal(22.03, heads[5].X, 0.02);   // LP 22.03
        Assert.Equal(24.78, heads[6].X, 0.02);   // LP 24.78
        Assert.Equal(27.03, heads[7].X, 0.02);   // LP 27.03

        // The claim itself: the flat stands clear of the beam-carried A3 stem.
        var flats = MusicGlyphs(svg, EmmentalerGlyphs.AccidentalFlat);
        Assert.Single(flats);
        Assert.Equal(13.61, flats[0].X, 0.02);   // LP 13.61 (was 12.94 = on the stem)
    }

    private static string Render(string source) =>
        LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>All music glyphs of one codepoint: (X, Y) in document order, X-sorted.</summary>
    private static List<(double X, double Y)> MusicGlyphs(string svg, char glyph) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Where(m => m.Groups[3].Value[0] == glyph)
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value)))
            .OrderBy(g => g.X)
            .ToList();
}
