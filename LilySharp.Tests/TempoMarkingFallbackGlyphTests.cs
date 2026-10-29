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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tempo marking the face cannot spell — kana and kanji, drawn from a system fallback face —
/// is still in the skyline a section label of its bar stacks on.
/// </summary>
/// <remarks>
/// Owner's report 2026-10-29 (a pop song composed in Copilot's chat, <c>tempo "そよぐ夕暮れ" 4 = 108</c>
/// under <c>section Intro</c>): the "Intro" box stood ON the marking's first characters. The
/// stacker walked the marking's outline in the tempo face, which has no such glyphs, so it held
/// the parentheses and the note alone; "Allegro" in the same place put the box over it. It now
/// reserves the ideographic em box over the marking's advance, as expressive text does.
/// </remarks>
[Trait("Category", "Unit")]
public class TempoMarkingFallbackGlyphTests
{
    private static string Svg(string marking) => SvgGenerator.Generate(
        SyntaxTree.Parse($$"""
            tempo "{{marking}}" 4 = 108
            time 4/4
            part vocal "Vocal" { instrument soprano }
            section Intro { vocal { R | } }
            section A { vocal { c'1 | } }
            form { Intro A }
            score { staff vocal }
            """),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    private static double Num(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("そよぐ夕暮れ")]
    [InlineData("Allegro")]
    public void TheBarsLabel_StandsOverTheMarking(string marking)
    {
        string svg = Svg(marking);
        var box = Regex.Match(svg,
            "<rect x=\"([\\d.-]+)\" y=\"([\\d.-]+)\" width=\"([\\d.]+)\" height=\"([\\d.]+)\"[^>]*stroke=\"#000000\"[^>]*/>\\s*"
            + "<text [^>]*>Intro</text>");
        Assert.True(box.Success, "the Intro box was not drawn");
        var text = Regex.Match(svg,
            "<text x=\"([\\d.-]+)\" y=\"([\\d.-]+)\" font-size=\"([\\d.]+)\"[^>]*>" + Regex.Escape(marking) + "</text>");
        Assert.True(text.Success, "the marking was not drawn");
        double boxBottom = Num(box.Groups[2]) + Num(box.Groups[4]);
        double boxLeft = Num(box.Groups[1]), boxRight = boxLeft + Num(box.Groups[3]);
        double textLeft = Num(text.Groups[1]);
        // They do meet across: the box is over the marking's opening characters.
        Assert.True(boxLeft < textLeft + 2 && boxRight > textLeft, $"box {boxLeft:F2}..{boxRight:F2}, marking at {textLeft:F2}");
        // Device Y-down: the box bottom is above the tallest the marking may be — the
        // ideographic em box's top, which a kana's ink never passes, over its baseline.
        double inkTop = Num(text.Groups[2]) - TextFontMetrics.IdeographicEmBoxTop * Num(text.Groups[3]);
        Assert.True(boxBottom <= inkTop + 1e-6,
            $"the Intro box bottom {boxBottom:F2} is below the marking's top {inkTop:F2}");
    }
}
