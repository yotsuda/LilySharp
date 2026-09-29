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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A whole-bar <c>R1</c> on a tab staff draws ONE whole rest, hanging from the upper
/// central string exactly where the tab draws a written <c>r1</c> (LilyPond 2.26.0, a
/// four-string bass tab, measured: <c>R1 | R1 | r1</c> hang from the second string, <c>r2</c>
/// sits on the third). Until 2026-09-29 the tab arm drew the bar's own rest under the
/// multi-measure-rest symbol too, and the symbol stood half a space high — its middle and
/// positions read in notation spaces on strings 1.5 apart — so the user saw "a whole rest
/// and a half rest" in a 4/4 bar (bohemian-rhapsody.lys, score "tab", bars 8–12).
/// </summary>
public class TabWholeBarRestTests
{
    private const string Bass = """
        part cb {
          instrument bass
          section A { R1 | R1 | c'1 | r1 | r2 r2 | }
        }
        form main { A }
        score main { tab cb }
        """;

    private const string Guitar = """
        part gt {
          instrument guitar
          section A { R1 | c'1 | r1 | }
        }
        form main { A }
        score main { tab gt }
        """;

    private static string Render(string lys)
        => SvgGenerator.Generate(SyntaxTree.Parse(lys), new SvgRenderOptions { EmbedFont = false });

    private static List<(double X, double Y, char Glyph)> MusicGlyphs(string svg) =>
        Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*>(.)</text>")
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value), m.Groups[3].Value[0]))
            .ToList();

    /// <summary>The distinct y of the staff's string lines, top first (device y grows down).</summary>
    private static List<double> LineYs(string svg) =>
        Regex.Matches(svg, "<line[^>]*y1=\"([-\\d.]+)\"[^>]*y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[1].Value == m.Groups[2].Value)
            .Select(m => double.Parse(m.Groups[1].Value))
            .Distinct().OrderBy(y => y).ToList();

    [Fact]
    public void AWholeBarRestOnABassTab_IsOneWholeRest_OnTheUpperCentralString()
    {
        string svg = Render(Bass);
        var lines = LineYs(svg);
        Assert.Equal(4, lines.Count);
        var wholes = MusicGlyphs(svg).Where(g => g.Glyph == EmmentalerGlyphs.RestWhole).ToList();
        // R1, R1 and r1: three whole rests, not five.
        Assert.Equal(3, wholes.Count);
        // Every one hangs from the upper central string (the second of four), as LilyPond's.
        Assert.All(wholes, w => Assert.Equal(lines[1], w.Y, 2));
        // The half rests sit on the lower central string, as before.
        var halves = MusicGlyphs(svg).Where(g => g.Glyph == EmmentalerGlyphs.RestHalf).ToList();
        Assert.Equal(2, halves.Count);
        Assert.All(halves, h => Assert.Equal(lines[2], h.Y, 2));
    }

    [Fact]
    public void AWholeBarRestOnAGuitarTab_HangsFromTheUpperCentralString()
    {
        string svg = Render(Guitar);
        var lines = LineYs(svg);
        Assert.Equal(6, lines.Count);
        var wholes = MusicGlyphs(svg).Where(g => g.Glyph == EmmentalerGlyphs.RestWhole).ToList();
        Assert.Equal(2, wholes.Count);
        Assert.All(wholes, w => Assert.Equal(lines[2], w.Y, 2));
    }
}
