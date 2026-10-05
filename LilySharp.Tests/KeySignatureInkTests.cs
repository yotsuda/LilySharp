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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A key signature's INK starts at its column's left edge, as LilyPond's KeySignature stencil
/// starts at its grob's X (extent (0 . w)) — so a flat key, whose first glyph reaches 0.12 left
/// of its origin, stands its glyphs 0.12 right of where a sharp key stands its (session 831; it
/// was drawn by the origin until then, every flat key 0.117 left of LilyPond's).
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p830/ck dflat / dsharp / dbes, one treble staff, the
/// `lysc ly` twins): the first accidental 3.505 right of the clef for F and B♭ major, 3.385 for
/// G major; the meter after either key where Lily# put it before.
/// </summary>
public sealed class KeySignatureInkTests
{
    [Theory]
    [InlineData("f major", 3.505)]
    [InlineData("bes major", 3.505)]
    [InlineData("g major", 3.385)]
    public void TheFirstAccidental_StandsWhereLilyPondsDoes(string key, double fromClef)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse($$"""
            paper { raggedRight }
            octave absolute
            time 4/4
            key {{key}}
            part m { clef treble }
            section A { m { c''1 | } }
            form main { A }
            score main { staff m }
            """), new SvgRenderOptions { EmbedFont = false });
        var glyphs = Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"[-\\d.]+\"[^>]*>(.)</text>")
            .Select(m => (Glyph: m.Groups[2].Value[0], X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)))
            .ToArray();
        double clef = glyphs.Where(g => g.Glyph == '').Min(g => g.X);         // the G clef
        double first = glyphs.Where(g => g.Glyph is '' or '').Min(g => g.X); // flat, sharp
        Assert.InRange(first - clef - fromClef, -0.011, 0.011);
    }
}