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
/// A tuplet bracket on a TAB staff is bounded by the tab stems, which stand at the fret
/// digits' centres, and reaches 0.3 past them — LilyPond 2.26.0 on the twin
/// (\tabFullNotation, Lab sessions/p690/probes): the bracket's X-positions start at the
/// stem's edge, centre − 0.065, and print draws the hook shorten-pair × ss = 0.2 × 1.5
/// further out. Lily# read the notation head's offsets there, so the hook stood 0.5–0.7 off
/// the digit at either end (user report 2026-09-29, bohemian-rhapsody.lys score "tab2" bar 25).
/// </summary>
public class TabTupletBracketTests
{
    // The tuplet on the top string (stems down, bracket below) …
    private const string HighString = """
        part cb {
          instrument bass
          section A { tuplet 3/2 { ees8@f ees4 } ees4 r8 bes'\2 bes bes | }
        }
        form main { A }
        score main { tab cb }
        """;

    // … and on the bottom string (stems up, bracket above): the same x rule on either side.
    private const string LowString = """
        octave absolute
        part cb {
          instrument bass
          section A { tuplet 3/2 { bes,,8@f bes,,4 } bes,,4 r8 bes\2 bes bes | }
        }
        form main { A }
        score main { tab cb }
        """;

    // … and where the string and the notated pitch disagree: E2 on the A string (fret 7)
    // sits below the tab's middle (stem UP, bracket above) while the bass staff stems its
    // written E3 down — the side is the strings', as LilyPond's TabVoice reads it.
    private const string StringAgainstPitch = """
        octave absolute
        part cb {
          instrument bass
          section A { tuplet 3/2 { e,8\3@f e,4\3 } e,4\3 r8 bes\2 bes bes | }
        }
        form main { A }
        score main { tab cb }
        """;

    private static string Render(string lys)
        => SvgGenerator.Generate(SyntaxTree.Parse(lys), new SvgRenderOptions { EmbedFont = false });

    /// <summary>Every fret digit's centre x, by its source offset (text-anchor middle).</summary>
    private static Dictionary<int, double> DigitCentres(string svg) =>
        Regex.Matches(svg, "<text x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"[\\d.]+\" font-weight=\"bold\" text-anchor=\"middle\" data-pos=\"(\\d+)\">\\d+</text>")
            .ToDictionary(m => int.Parse(m.Groups[2].Value), m => double.Parse(m.Groups[1].Value));

    /// <summary>The bracket's vertical hooks (x1 == x2) tagged with the tuplet's source offset,
    /// as (x, top y, bottom y).</summary>
    private static List<(double X, double Y1, double Y2)> Hooks(string svg, int tupletPos) =>
        Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*data-pos=\"" + tupletPos + "\"")
            .Where(m => m.Groups[1].Value == m.Groups[3].Value)
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value), double.Parse(m.Groups[4].Value)))
            .OrderBy(h => h.Item1).ToList();

    [Theory]
    [InlineData(HighString, "ees8@f", "ees4 }", false)]
    [InlineData(LowString, "bes,,8@f", "bes,,4 }", true)]
    [InlineData(StringAgainstPitch, "e,8\\3@f", "e,4\\3 }", true)]
    public void TheBracket_ReachesFromTheFirstStemToTheLast_OnTheStemsSide(
        string book, string first, string last, bool bracketUp)
    {
        string svg = Render(book);
        int tupletPos = book.IndexOf("tuplet 3/2", System.StringComparison.Ordinal);
        var digits = DigitCentres(svg);
        double firstCentre = digits[book.IndexOf(first, System.StringComparison.Ordinal)];
        double lastCentre = digits[book.IndexOf(last, System.StringComparison.Ordinal)];

        var hooks = Hooks(svg, tupletPos);
        Assert.Equal(2, hooks.Count);
        // The stem's edge (half a stem thickness off the digit's centre), then 0.2 × the
        // tab's 1.5 line spacing further out.
        const double reach = 0.065 + 0.3;
        Assert.Equal(firstCentre - reach, hooks[0].X, 2);
        Assert.Equal(lastCentre + reach, hooks[1].X, 2);
        // The hook is 0.7 × 1.5 tall, pointing at the staff.
        Assert.Equal(1.05, System.Math.Abs(hooks[0].Y2 - hooks[0].Y1), 2);
        // Above the tab when the strings stem up, below when they stem down (device y grows down).
        // The tab's own strings: the thin (0.100) horizontal lines, not the bracket's run.
        double topLine = Regex.Matches(svg, "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" stroke=\"#000000\" stroke-width=\"0.100\"/>")
            .Select(m => double.Parse(m.Groups[1].Value)).Min();
        Assert.Equal(bracketUp, hooks[0].Y1 < topLine);
    }
}
