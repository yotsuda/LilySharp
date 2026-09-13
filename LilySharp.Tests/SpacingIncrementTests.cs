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
using Xunit;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// <c>paper { spacingIncrement }</c> reaches the duration springs, by LilyPond's numbers.
/// </summary>
/// <remarks>
/// Until session 379 the key was parsed into <c>LayoutOptions.SpacingIncrement</c> and read by
/// nothing — every spacing rule read the constant — so the documented "horizontal note-spacing
/// unit" moved no page (HANDOFF §2 E ⒠; owner decision: wire it). The carrier is now
/// <c>SpacingOptions</c>, LilyPond's <c>Spacing_options</c> (lily/spacing-options.cc:30-53).
/// <para>
/// MEASURED, LilyPond 2.26.0 (scratch/p380/incr/incr.ps1): the <c>lysc ly --pin-fonts</c> twin of
/// the book below, ragged-right, with <c>\override Score.SpacingSpanner.spacing-increment</c>
/// 1.2 / 1.5 / 1.8, has one system whose staff is 74.95 / 86.89 / 98.83 long. Before the wiring
/// Lily# drew 74.95 for all three. The book exercises note springs (quarter, eighths,
/// sixteenths), a bar of skips only (standard_breakable_column_spacing's increment,
/// lily/spacing-basic.cc:53) and a whole note. ⚠️ At 2.4 LilyPond breaks the line, so the
/// reading (one system's staff length) stops being comparable — the values stay under that.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class SpacingIncrementTests
{
    private const string Music = """
        octave absolute
        part m { clef treble
          section A { c'4 d'8 e' f'2 | g'16 a' b' c'' d''4 e''2 | s1 | c'1 | }
        }
        form main { A }
        score main { staff m }
        """;

    private static string Svg(string paper) =>
        SvgGenerator.Generate(SyntaxTree.Parse(paper + "\n" + Music), new SvgRenderOptions { EmbedFont = false });

    /// <summary>(staff length per system summed, system count) — staff lines are the horizontal
    /// lines of stroke 0.100 (ledger lines are 0.200).</summary>
    private static (double Length, int Systems) Staff(string svg)
    {
        double sum = 0;
        int n = 0;
        foreach (Match m in Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"0.100\""))
        {
            if (m.Groups[2].Value != m.Groups[4].Value)
                continue;
            sum += double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                   - double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            n++;
        }
        return (sum / 5, n / 5);
    }

    [Fact]
    public void TheDefaultIncrement_StatedOrNot_IsTheSamePage()
    {
        // data-pos is a source byte offset, and the stated key shifts every one after it.
        static string Masked(string svg) => Regex.Replace(svg, "data-pos=\"\\d+\"", "data-pos=\"\"");
        Assert.Equal(Masked(Svg("paper { raggedRight }")),
                     Masked(Svg("paper { raggedRight  spacingIncrement 1.2 }")));
    }

    [Theory]
    [InlineData("1.2", 74.95)]
    [InlineData("1.5", 86.89)]
    [InlineData("1.8", 98.83)]
    public void TheRaggedLine_IsLilyPondsLength(string increment, double lilyPond)
    {
        var (length, systems) = Staff(Svg("paper { raggedRight  spacingIncrement " + increment + " }"));
        Assert.Equal(1, systems);
        Assert.Equal(lilyPond, length, 2);
    }
}
