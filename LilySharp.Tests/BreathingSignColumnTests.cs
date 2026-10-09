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

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A breathing sign or caesura between two notes is a column of its own, as LilyPond's
/// break-aligned BreathingSign is (scm/define-grobs.scm:697-731): Note_spacing prices the note
/// before it, its space-alist's first-note fixed-space 1.0 the note after it, and its glyph's
/// origin sits on the staff's top line (lily/breathing-sign.cc:262-277 offset_callback).
/// MEASURED, LilyPond 2.26.0 on this book's twin (Lab sessions/p851/breath): every head and
/// sign below, from the first head's left. Lily# drew the sign a fixed 0.55 past the head,
/// 0.5 above the top line, and reserved nothing, so a caesura ran into the next stem.
/// </summary>
public class BreathingSignColumnTests
{
    private const string Book = """
        octave absolute
        time 4/4
        part m { clef treble }
        section A {
          m {
            c'4@breath d'4 e'4 f'4 |
            c'8@breath d'8 e'8@breath f'8 g'4 a'4 |
            c'4@caesura d'4 e'4 f'4 |
            c'4@breath dis'4 e'4@breath ges'4 |
          }
        }
        form { A }
        score { staff m }
        """;

    // LilyPond 2.26.0: N = a note head's left, B = a sign's ink left.
    private static readonly double[] LilyPond =
    [
        0.00, 2.50, 4.00, 7.00, 10.01,          // c' , d' e' f'
        13.99, 15.59, 17.10, 19.20, 21.01, 22.51, 24.62, 27.62,
        31.67, 33.92, 36.92, 39.92, 42.92,      // c' // d' e' f'
        46.91, 49.40, 51.96, 54.96, 57.46, 59.84,
    ];

    [Fact]
    public void HeadsAndSigns_StandWhereLilyPondPutsThem()
    {
        string svg = LiveRender.SvgFromRenderSpec(Book);
        var glyphs = Regex.Matches(svg,
                @"<text class=""music"" x=""(?<x>-?[\d.]+)"" y=""(?<y>-?[\d.]+)""[^>]*>(?<c>[])</text>")
            .Select(m => (X: double.Parse(m.Groups["x"].Value), Y: double.Parse(m.Groups["y"].Value),
                          Sign: m.Groups["c"].Value[0] != ''))
            .OrderBy(g => g.X).ToList();
        Assert.Equal(LilyPond.Length, glyphs.Count);
        double x0 = glyphs[0].X;
        for (int i = 0; i < glyphs.Count; i++)
            // The SVG speaks in hundredths: ±0.005 a glyph, both sides rounded.
            Assert.True(Math.Abs(glyphs[i].X - x0 - LilyPond[i]) <= 0.015,
                $"glyph {i}: Lily# {glyphs[i].X - x0:F2}, LilyPond {LilyPond[i]:F2}");

        // Every sign's origin on the staff's top line.
        double topLine = Regex.Matches(svg,
                @"<line x1=""[\d.]+"" y1=""(?<y>[\d.]+)"" x2=""[\d.]+"" y2=""\k<y>"" stroke=""#000000"" stroke-width=""0\.100""")
            .Select(m => double.Parse(m.Groups["y"].Value)).Min();
        Assert.All(glyphs.Where(g => g.Sign), g => Assert.Equal(topLine, g.Y, 2));
    }
}
