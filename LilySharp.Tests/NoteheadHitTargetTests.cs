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

using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Interactive (preview) SVG lays a tight, transparent hit rectangle — the size
/// of the notehead ink — over each head so only the head is clickable, not the
/// whole glyph em-box. Static SVG output carries no such rects.
/// </summary>
[Trait("Category", "Unit")]
public class NoteheadHitTargetTests
{
    private const string Doc = """
        part m { clef treble section A { c'4 d' } }
        structure { A }
        score "s" { staff m }
        """;

    private static string Render(SvgRenderOptions options)
        => SvgGenerator.Generate(SyntaxTree.Parse(Doc), options);

    [Fact]
    public void PreviewEmitsTightNoteheadHitRects()
    {
        var svg = Render(SvgRenderOptions.Preview());

        // A quarter head's ink box (Emmentaler black notehead) is ~1.30 × 1.09
        // staff-spaces — matching GetNoteheadBBox, far tighter than the font-size
        // 4 em-box the <text> would otherwise hit.
        var m = Regex.Match(svg,
            "<rect class=\"nh-hit\" x=\"[\\d.]+\" y=\"[\\d.]+\" width=\"([\\d.]+)\" height=\"([\\d.]+)\" fill=\"none\" pointer-events=\"all\" data-pos=\"\\d+\"/>");
        Assert.True(m.Success, "expected a transparent nh-hit rect per notehead");

        var bbox = GlyphMetrics.GetNoteheadBBox(4);
        Assert.Equal(GlyphMetrics.GetNoteheadAdvance(4), double.Parse(m.Groups[1].Value), 1);
        Assert.Equal(bbox.Height, double.Parse(m.Groups[2].Value), 1);

        // The head glyph itself is made non-interactive so the rect owns the click.
        Assert.Contains("<text class=\"music\" pointer-events=\"none\"", svg);
    }

    [Fact]
    public void StaticSvgHasNoHitRectsAndNoPointerEvents()
    {
        var svg = Render(SvgRenderOptions.Default);
        Assert.DoesNotContain("nh-hit", svg);
        Assert.DoesNotContain("pointer-events", svg);
    }
}
