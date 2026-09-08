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
using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A syllable and a bar line never meet in LilyPond's spacing: LyricText's spacing box
/// recedes 0.2 each side in Y (scm/define-grobs.scm LyricText extra-spacing-height), so
/// nothing is reserved between a line's first syllable and the bar before it, or its last
/// and the bar after it — only the next SYLLABLE's ink binds, rodded straight across the
/// bar. Until session 351 Lily# reserved half the word plus 0.4 ss at both bars of a
/// staff-backed score (a lead sheet keeps that clearance by user decision, see
/// LeadSheetLyricSpacingTests). The numbers below are LilyPond 2.26.0's on the twins
/// (ragged-right, serif pinned to "LilyPond Serif"; scratch/p352/lp/pair.ps1, 2026-09-08).
/// </summary>
[Trait("Category", "Unit")]
public class LyricBarEdgeTests
{
    /// <summary>test/lyrics-after-rest-bar: the words start one bar in, after a rest bar.</summary>
    private const string AfterRestBar = """
        time 4/4
        key c major
        octave absolute
        part melody { clef treble }
        section A { r1 | c'4 c' g' g' | a' a' g'2 | f'4 f' e' e' | d' d' c'2 | }
        lyrics words sings melody { | Twin- kle twin- kle | lit- tle star | How I won- der | what you are | }
        form main { A }
        score main { staff melody  lyrics words }
        """;

    /// <summary>
    /// LilyPond's bar widths on the twin: 18.147 / 12.465 / 15.955 / 15.142 (bar lines at
    /// 14.183, 32.330, 44.795, 60.750, 75.892 relative to the system). The first sung bar
    /// opens with "Twin-", whose ink starts 1.11 ss LEFT of the bar line it follows; with
    /// the leading half it was 19.67 (+1.523). What is left against LilyPond is the two
    /// faces' glyph widths (Schola against C059, a few hundredths per rod).
    /// </summary>
    [Fact]
    public void TheBarOpeningALyricLine_IsAsWideAsLilyPonds_NoClearanceBeforeItsFirstWord()
    {
        var widths = StaffBarWidths(Render(AfterRestBar));
        Assert.Equal(4, widths.Count);
        Assert.InRange(widths[0], 18.147 - 0.25, 18.147 + 0.25);   // 19.67 with the half
        Assert.InRange(widths[1], 12.465 - 0.25, 12.465 + 0.25);
        Assert.InRange(widths[2], 15.955 - 0.25, 15.955 + 0.25);   // 16.14 with the clamp
        Assert.InRange(widths[3], 15.142 - 0.25, 15.142 + 0.25);
    }

    /// <summary>The first word's ink reaches left of its bar line, as LilyPond's does.</summary>
    [Fact]
    public void ALinesFirstWord_OverhangsTheBarLineBeforeIt()
    {
        string svg = Render(AfterRestBar);
        double firstBar = StaffBarXs(svg)[0];
        var twin = Regex.Match(svg, "<text x=\"([0-9.-]+)\"[^>]*text-anchor=\"middle\"[^>]*>Twin</text>");
        Assert.True(twin.Success, "no 'Twin' syllable drawn");
        double centre = double.Parse(twin.Groups[1].Value, CultureInfo.InvariantCulture);
        double width = ScoreTextMetrics.Bundled.Advance("Twin", EngravingDefaults.LyricTextFontSize, TextRole.LyricText);
        double inkLeft = centre - width / 2;
        Assert.True(inkLeft < firstBar - 0.5,
            $"'Twin' ink starts at {inkLeft:F3}, bar line at {firstBar:F3}: LilyPond's overhang is 1.11");
    }

    /// <summary>
    /// The rod to a syllable narrower than its head's alignment extent is SHORTER than the
    /// previous syllable's reach plus the word space: LilyPond's bounds_protrusion is signed
    /// (lily/rod.cc), and "I" centred on a quarter's 0.652 starts 0.157 right of its column.
    /// "How I" is 3.505 in LilyPond; a 0-clamped reach priced it at 3.67.
    /// </summary>
    [Fact]
    public void ANarrowSyllable_ShortensTheRodToIt_ByItsNegativeReach()
    {
        var fonts = ScoreTextMetrics.Bundled;
        var edge = (Left: 0.0, Centre: 0.652);
        var how = new List<LyricItem> { new(Text: "How", MeasureIndex: 0, ItemIndex: 0, Timing: Fraction.Zero) };
        var i = new List<LyricItem> { new(Text: "I", MeasureIndex: 0, ItemIndex: 1, Timing: new Fraction(1, 4)) };

        double iLeft = LyricSpacing.GetLyricLeftExtent(fonts, i, edge);
        Assert.True(iLeft < 0, $"'I' reaches {iLeft:F3} left of its column; LilyPond's is −0.157");

        double rod = LyricSpacing.CalculateLyricDistance(fonts, how, i, edge, edge);
        double howRight = LyricSpacing.GetLyricRightExtent(fonts, how, edge);
        Assert.Equal(howRight + LyricSpacing.WordSpaceMinimum + iLeft, rod, 9);
        Assert.True(rod < howRight + LyricSpacing.WordSpaceMinimum, "the negative reach must shorten the rod");
        Assert.InRange(rod, 3.505 - 0.1, 3.505 + 0.1);
    }

    private static string Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    /// <summary>The staff's bar-line Xs in order (the thin tall rects on the lowest band).</summary>
    private static List<double> StaffBarXs(string svg)
    {
        var bars = Regex.Matches(svg,
                "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                          W: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                          H: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .Where(r => r.W < 0.5 && r.H > 3)
            .ToList();
        Assert.True(bars.Count > 0, "no barlines drawn");
        double staffY = bars.Max(b => b.Y);
        return bars.Where(b => System.Math.Abs(b.Y - staffY) < 0.1).Select(b => b.X).Distinct().OrderBy(x => x).ToList();
    }

    private static List<double> StaffBarWidths(string svg)
    {
        var xs = StaffBarXs(svg);
        var w = new List<double>();
        for (int k = 1; k < xs.Count; k++)
            w.Add(xs[k] - xs[k - 1]);
        return w;
    }
}
