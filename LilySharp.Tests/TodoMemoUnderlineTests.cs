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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A memo's underline stands the same distance under Latin and Japanese words (owner's report
/// 2026-10-12: too close under the one, too far under the other) — under the glyphs' own ink
/// where the face can measure it, so a descender takes it down.
/// </summary>
[Trait("Category", "Unit")]
public class TodoMemoUnderlineTests
{
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The underline's distance under the memo's baseline, in the preview.</summary>
    private static double UnderlineDrop(string memo)
    {
        string text = $"part p {{ clef treble }}\nsection A {{ p {{ c'4@todo(\"{memo}\") r2. | }} }}\nform {{ ~A }}\nscore {{ staff p }}\n";
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(text), SvgRenderOptions.Preview());
        double baseline = D(Regex.Match(svg, @"<text x=""[\d.]+"" y=""([\d.]+)""[^>]*fill=""#E53935""").Groups[1].Value);
        // The underline: the horizontal red line (the leader is slanted).
        double underline = Regex.Matches(svg, @"<line x1=""[\d.]+"" y1=""([\d.]+)"" x2=""[\d.]+"" y2=""([\d.]+)"" stroke=""#E53935""")
            .Where(m => m.Groups[1].Value == m.Groups[2].Value).Select(m => D(m.Groups[1].Value)).Single();
        return underline - baseline;   // SVG's Y grows downward
    }

    [Fact]
    public void LatinAndJapanese_HangTheUnderlineAlike_AndADescenderTakesItDown()
    {
        double latin = UnderlineDrop("check the notes");
        double japanese = UnderlineDrop("確認する");
        // Alike to a hair: round Latin letters (c, e, o) overshoot the baseline by ~0.02.
        Assert.InRange(latin - japanese, -0.05, 0.05);
        Assert.True(latin >= 0.25, $"too close under a Latin word: {latin}");
        Assert.True(UnderlineDrop("glyphs") > latin + 0.2, "a descender must take the underline down");
    }
}
