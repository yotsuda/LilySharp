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
/// A below-staff jump text ("D.S. al Coda") clears the staff's own ink under it — a low note's
/// accent here — as LilyPond's JumpScript, an outside-staff grob, clears the inside-staff
/// skyline (owner's report 2026-10-12, b-bigband.lys bar 24: the words were printed through the
/// accent under the bass drum). Until then the text hung at a fixed depth under the staff.
/// </summary>
[Trait("Category", "Unit")]
public class BelowMarkInkClearanceTests
{
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void AJumpText_ClearsAnAccentUnderTheStaff()
    {
        const string book = """
            octave absolute
            part m { clef treble }
            section A { m { c''4 d'' e'' f'' | } }
            section B { m { e''4 d'' c'' c4@accent | } }
            section C { m { g'1 | } }
            form { segno A to coda B ds al coda coda C }
            score { staff m }
            """;
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(book), SvgRenderOptions.Preview());
        // The accent's glyph (the last music glyph of the c's column) and the jump text's
        // baseline, both in the page frame on a single staff.
        var ds = Regex.Match(svg, @"<text x=""-?[\d.]+"" y=""(-?[\d.]+)""[^>]*>D\.S\. al Coda<");
        Assert.True(ds.Success, "no D.S. al Coda in the SVG");
        // The script is stamped with its own source position, the @ of @accent.
        int pos = book.IndexOf("@accent", System.StringComparison.Ordinal);
        var accent = Regex.Match(svg, $@"<text class=""music"" x=""-?[\d.]+"" y=""(-?[\d.]+)""[^>]*data-pos=""{pos}""");
        Assert.True(accent.Success, "no accent glyph at its source position");
        // The words' capitals reach about 1.5 above their baseline at this size, and the
        // outside-staff padding is 0.46: 2.2 under the accent's origin is clear of it. The fixed
        // depth this replaced stood 1.34 under it, through the glyph.
        double gap = D(ds.Groups[1].Value) - D(accent.Groups[1].Value);
        Assert.True(gap > 2.2, $"D.S. baseline only {gap:F2} under the accent");
    }
}
