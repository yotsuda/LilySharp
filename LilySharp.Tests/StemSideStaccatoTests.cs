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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A staccato or staccatissimo on its STEM's side stands on the stem (session 1303): LilyPond's
/// <c>toward-stem-shift</c> 1.0 — and stays on the head where another script shares its note
/// (<c>toward-stem-shift-in-column</c> 0.0). Until then every one was centred on the head, half
/// a head beside the stem it stands under: every lone dot of a second voice, of an upper voice,
/// and of a note whose dot is written to the stem's side.
/// MEASURED (LilyPond 2.26.0, Lab sessions/p1303/st k1-k8, dump.ily, raggedRight): each number
/// is the centre of the dot's extent; a down stem stands 0.065 right of its head's left edge,
/// an up stem 0.065 left of its head's right edge.
/// </summary>
[Trait("Category", "Unit")]
public class StemSideStaccatoTests
{
    private static ScoreLayout LayoutOf(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    // The three glyphs a dot or a wedge is drawn with: read off a book that has nothing else.
    private static readonly HashSet<string> Dots =
        [.. LayoutOf("c''4@staccato d''@staccatissimo a'@staccatissimo b' |").ArticulationLayouts.Select(a => a.Glyph)];

    private static double[] DotsOf(string bars)
        => [.. LayoutOf(bars).ArticulationLayouts.Where(a => Dots.Contains(a.Glyph)).Select(a => a.X).Order()];

    public static TheoryData<string, string, double[]> Books => new()
    {
        // The lower voice's lone dots: on the down stems.
        { "k1", "voice { d''2 e''2 } { b'2@staccato c''2@staccato } |", [17.186, 21.461] },
        // …a wedge too; beside a tenuto or an accent the dot stays on the head.
        { "k2", "voice { d''4 e'' f'' e'' } { b'4@staccato c''@staccatissimo b'@staccato@tenuto c''@staccato@accent } |",
            [17.186, 20.188, 23.777, 26.780] },
        // One voice, the side written: down under down stems, up over up stems.
        { "k3", "c''4@staccato.down d''@staccatissimo.down a'@staccato.up g'@staccatissimo.up |",
            [17.186, 20.188, 24.203, 27.206] },
        // Beamed: the stem is the beam's.
        { "k4", "voice { d''8 e'' f'' e'' d''2 } { b'8@staccato c''@staccato b'@staccato c''@staccato b'2 } |",
            [17.186, 19.690, 22.194, 24.698] },
        // A whole note has no stem to stand on; a chord's dot stands on the chord's stem, a
        // second's head on the stem's other side or not.
        { "k5", "voice { e''1 } { c''1@staccato } | voice { e''2 f''2 } { <a' c''>2@staccato <b' c''>2@staccato } |",
            [18.102, 24.464, 28.739] },
        // Another script on the OTHER side of the note keeps the dot on the head as well.
        { "k6", "voice { d''2 e''2 } { b'2@staccato@fermata.up c''2@staccato@fermata.down } |", [17.810, 22.085] },
        // The upper voice: on the up stems, beamed or not.
        { "k7", "voice { d''2@staccato e''2@staccato@tenuto } { b'2 c''2 } | voice { d''8@staccato e''@staccato f''@staccato e''@staccato d''2 } { b'1 } |",
            [18.433, 22.787, 29.333, 31.838, 34.342, 36.846] },
        // Opposite the stem, where a dot stands unasked: on the head, as ever.
        { "k8", "c''4@staccato d''@staccato a'@staccato g'@staccato |", [17.773, 20.775, 23.616, 26.619] },
        // The OTHER voice's script at the same moment is not of the dot's column.
        { "k9", "voice { d''2@staccato e''2@accent } { b'2@accent c''2@staccato } |", [18.433, 21.461] },
        // A thumb and a digit are fingerings, not scripts: the dot beside one is alone.
        { "k10", "voice { d''2 e''2 } { b'2@staccato@thumb c''2@staccato@finger(2) } |", [17.186, 21.461] },
    };

    [Theory]
    [MemberData(nameof(Books))]
    public void TheDot_StandsWhereLilyPondPutsIt(string book, string bars, double[] centres)
    {
        var dots = DotsOf(bars);
        Assert.True(centres.Length == dots.Length, $"{book}: {dots.Length} dots");
        for (int i = 0; i < centres.Length; i++)
            Assert.True(System.Math.Abs(centres[i] - dots[i]) <= 0.005, $"{book} dot {i}: {dots[i]:F3}, LilyPond {centres[i]:F3}");
    }
}
