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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// The live renderer (SharedRenderer, via SvgGenerator) must draw barline TYPES,
/// not a thin line for every measure: repeats need dots and a thick stroke, and a
/// double barline draws two thin strokes. Previously SharedRenderer drew only a
/// single thin rect per measure. (The last bar of a piece is auto-finalized, so
/// these tests vary a MID-piece barline to isolate the type.)
/// </summary>
public sealed class SharedRendererBarlineTests
{
    private static string Render(string body)
    {
        var source = $$"""
            key C major
            time 4/4

            section Demo {
                line { {{body}} }
            }

            structure { Demo }
            score "out" { staff { line } }
            """;
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    [Fact]
    public void DoubleBarline_AddsAnExtraThinStroke()
    {
        // Two measures divided by a single vs a double barline mid-piece. The
        // double draws two adjacent thin strokes, so it has one more thin rect.
        var single = Render("| c'4 d e f | g a b c'' |");
        var dbl    = Render("| c'4 d e f || g a b c'' |");

        string thin = $"width=\"{EngravingDefaults.ThinBarlineThickness:F2}\""; // width="0.16"
        Assert.True(CountOccurrences(dbl, thin) == CountOccurrences(single, thin) + 1,
            $"double barline should add exactly one thin stroke " +
            $"(single={CountOccurrences(single, thin)}, double={CountOccurrences(dbl, thin)})");
    }

    [Fact]
    public void Repeat_DrawsDotsAndThickStroke()
    {
        var plain = Render("| c'4 d e f | g a b c'' |");
        var repeat = Render("|: c'4 d e f :| g a b c'' |");

        Assert.DoesNotContain("<circle", plain);
        Assert.Contains("<circle", repeat);   // repeat dots (start + end)
        // The repeat-start/end glyphs include a thick stroke.
        string thick = $"width=\"{EngravingDefaults.ThickBarlineThickness:F2}\"";
        Assert.True(CountOccurrences(repeat, thick) > CountOccurrences(plain, thick),
            "repeat barlines should add thick strokes");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }
}
