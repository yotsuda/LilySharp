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
/// A <c>@todo</c> on a tab's note rings its fret DIGITS (owner's report 2026-10-12: the ring
/// was the notehead's, smaller than the number, which stuck out of it).
/// </summary>
[Trait("Category", "Unit")]
public class TabTodoRingTests
{
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void ARingOnATab_GoesRoundItsDigits_OneOrTwoOrAChords()
    {
        string text = """
            part bass { clef bass  tuning bass  octave 3 }
            section A { bass { c4 d@todo("x") <e b>4@todo("z") d'4@todo("y") | g2@todo("two digits") r2 | } }
            form { ~A }
            score { tab bass }
            """;
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(text), SvgRenderOptions.Preview());
        // The staff's contents are drawn in a group moved down by the system's offset; the
        // rings and memos are overlay, in the page's own frame.
        double dy = D(Regex.Match(svg, @"<g transform=""translate\(0\.00,([\d.]+)\)").Groups[1].Value);
        foreach (int pos in Regex.Matches(svg, @"data-todo="""" data-pos=""(\d+)""/>").Select(m => int.Parse(m.Groups[1].Value)).Distinct())
        {
            var ring = Regex.Matches(svg, $@"<path d=""M ([\d.]+),([\d.]+) C ([\d.]+),([\d.]+) ([\d.]+),([\d.]+) ([\d.]+),([\d.]+)""[^>]*data-pos=""{pos}""")
                .SelectMany(m => Enumerable.Range(0, 4).Select(k => (X: D(m.Groups[1 + 2 * k].Value), Y: D(m.Groups[2 + 2 * k].Value))))
                .ToList();
            var digits = Regex.Matches(svg, $@"<text x=""([\d.]+)"" y=""([\d.]+)"" font-size=""([\d.]+)"" text-anchor=""middle"" data-pos=""{pos}"">(\d+)<")
                .Select(m => (X: D(m.Groups[1].Value), Y: D(m.Groups[2].Value) + dy, Size: D(m.Groups[3].Value), Text: m.Groups[4].Value))
                .ToList();
            Assert.NotEmpty(ring);
            Assert.NotEmpty(digits);
            foreach (var d in digits)
            {
                // A digit's advance is 0.574 of its size in the bundled bold serif
                // (TabConstants.FretGlyphWidth's remarks); its ink stands on its baseline.
                double half = 0.574 * d.Size * d.Text.Length / 2;
                Assert.True(ring.Min(p => p.X) < d.X - half && ring.Max(p => p.X) > d.X + half,
                    $"ring at {pos} [{ring.Min(p => p.X)}, {ring.Max(p => p.X)}] does not go round digit {d.Text} at {d.X}");
                Assert.True(ring.Min(p => p.Y) < d.Y - 0.6 * d.Size && ring.Max(p => p.Y) > d.Y,
                    $"ring at {pos} misses digit {d.Text} vertically");
            }
        }
    }
}
