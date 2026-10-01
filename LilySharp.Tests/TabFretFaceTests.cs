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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The fret face a tab staff draws its digits in follows its STRING COUNT: six strings or
/// more take the reduced face (em 2.3, regular weight), fewer keep the full one (2.8, bold),
/// and a score's <c>fonts { tab … }</c> still wins over either default.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN, the owner's decision of 2026-10-01 (session 733): a guitar chord's
/// zigzagged 2.8 digits stood so close to the next chord that a reader could not tell which
/// beat a digit belonged to (Lab sessions/p733/tabfont compare3–compare11). See
/// <see cref="TabConstants.ReducedFretMinStrings"/>.
/// </remarks>
[Trait("Category", "Unit")]
public class TabFretFaceTests
{
    private const string Guitar = """
        part gt { instrument guitar }
        section A { gt { chord(Em 022000)2 chord(C x32010)2 | } }
        form main { ~A }
        score main { staff gt  tab gt }
        """;

    private const string Bass = """
        octave absolute
        part bs { clef bass tuning bass }
        section A { bs { e,,4\4 a,,\3 d,\2 g,\1 | } }
        form main { ~A }
        score main { tab bs }
        """;

    private static string Svg(string source)
        => SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });

    /// <summary>Every fret digit the page draws, as its (font size, bold) pair — a digit carries its note's source position, which the clef's italic octave 8 does not.</summary>
    private static (string Size, bool Bold)[] FretFaces(string svg)
        => Regex.Matches(svg, "<text (?![^>]*class=)[^>]*font-size=\"([\\d.]+)\"([^>]*data-pos=[^>]*)>\\d+</text>")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value.Contains("font-weight=\"bold\"")))
            .Distinct()
            .ToArray();

    private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

    [Fact]
    public void ASixStringTab_DrawsTheReducedFace()
    {
        var face = Assert.Single(FretFaces(Svg(Guitar)));
        Assert.Equal(F2(TabConstants.ReducedFretEm), face.Size);
        Assert.False(face.Bold);
    }

    [Fact]
    public void AFourStringTab_KeepsTheFullFace()
    {
        var face = Assert.Single(FretFaces(Svg(Bass)));
        Assert.Equal(F2(TabConstants.FretFontSize), face.Size);
        Assert.True(face.Bold);
    }

    [Fact]
    public void AWrittenTabSize_WinsOverTheSixStringDefault()
    {
        var face = Assert.Single(FretFaces(Svg("fonts { tab size 2.4 }\n" + Guitar)));
        Assert.Equal(F2(2.4), face.Size);
    }

    /// <summary>
    /// A two-digit fret whose first digit is a 1 is drawn as two runs, the second pulled
    /// <see cref="TabConstants.ReducedLeadingOneTighten"/> of the em closer than the face's
    /// tabular advance would put it (Lab sessions/p733/tabfont compare10); a 2 is not pulled.
    /// </summary>
    [Fact]
    public void ALeadingOne_IsDrawnCloserToItsSecondDigit()
    {
        string svg = Svg("""
            octave absolute
            part gt { instrument guitar }
            section A { gt { e''4\1 b''4\1 e'''2\1 | } }
            form main { ~A }
            score main { tab gt }
            """);
        double em = TabConstants.ReducedFretEm;
        double one = LilySharp.Core.Rendering.TextFontMetrics.Advance("1", em, sans: false,
            style: TabConstants.ReducedFretStyle);
        double X(string digit, int nth) => double.Parse(Regex.Matches(svg,
                "<text x=\"([\\d.]+)\"[^>]*data-pos=[^>]*>" + digit + "</text>")[nth].Groups[1].Value,
            CultureInfo.InvariantCulture);
        // Fret 12 (e'' on string 1) and fret 19 (b'') — each a "1" run and a second-digit run.
        Assert.Equal(one - em * TabConstants.ReducedLeadingOneTighten, X("2", 0) - X("1", 0), 2);
        Assert.Equal(one - em * TabConstants.ReducedLeadingOneTighten, X("9", 0) - X("1", 1), 2);
        // Fret 24 (e''') keeps its digits as one run.
        Assert.Matches(">24</text>", svg);
    }
}
