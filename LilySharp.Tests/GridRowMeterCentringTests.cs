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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A lead-sheet grid row engraves the score meter, and the meter is CENTRED on the band
/// its barlines run — not hung from the band's top.
/// </summary>
/// <remarks>
/// <para>
/// <c>DrawTimeSignature</c> drops a NOMINAL half staff (<c>StaffMiddleLineDrop</c> = 2.0)
/// from whatever top line it is handed, which is the band's centre only while the band is
/// four staff spaces tall. A grid row is <c>StaffHeight + (verses-1) * LyricVerseSpacing</c>,
/// so a TWO-verse row is 7.2 and its centre is 3.6 down: the meter sat 1.600000 above the
/// middle of the very bars it opens (user report, 2026-08-24).
/// </para>
/// <para>
/// ⚠️ THE SAME FOLD, ONE GROB LATER. The repeat dots were placed from the band's TOP by a
/// pair of constants with a half staff baked into them, and were unfolded onto a
/// centre-relative <c>EngravingDefaults.RepeatDotHalfSpan</c> in session 226 — for a user
/// report of the same shape ("centred looks more like it"). The meter kept its fold for
/// seventeen more sessions because the two grobs are drawn by different files and only a
/// band that is NOT four spaces tall can tell either of them apart from correct.
/// ⇒ ★ When one grob is unfolded off a nominal half staff, grep the others drawn in the
/// same band: the fold is a property of the BAND MODEL, not of the grob.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class GridRowMeterCentringTests
{
    /// <summary>A rows-only lead sheet: a chords row over a lyrics row of N verses.</summary>
    private static string Book(int verses)
    {
        string b = verses == 1
            ? "one two | three four |"
            : """
              [~1. one two | three four |]
                  [~2. five six | seven eight |]
              """;
        return $$"""
            time 4/4
            part melody {
              clef treble
              section A { c4 c g' g | a a g2 | }
            }
            chords prog { section A { C | G | } }
            lyrics verse { section A { {{b}} } }
            form main { A }
            score main {
              chords prog as names
              lyrics verse sings melody
            }
            """;
    }

    private static string Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The grid's barline band: the narrow, tall rectangles the bars are drawn as.</summary>
    private static (double Top, double Bottom) Band(string svg)
    {
        var bars = Regex.Matches(svg,
                "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (Y: Num(m.Groups[2].Value), W: Num(m.Groups[3].Value), H: Num(m.Groups[4].Value)))
            .Where(r => r.W < 1 && r.H > 2)
            .ToList();
        Assert.True(bars.Count > 0, "the grid drew no barlines");
        return (bars[0].Y, bars[0].Y + bars[0].H);
    }

    /// <summary>The common-time glyph's drawn Y. It is centred on its own origin, so this
    /// IS the meter's vertical centre — no ink model needed.</summary>
    private static double MeterY(string svg)
    {
        var m = Regex.Match(svg, "<text[^>]*y=\"([0-9.-]+)\"[^>]*>\uE095</text>");
        Assert.True(m.Success, "the grid drew no common-time glyph");
        return Num(m.Groups[1].Value);
    }

    /// <summary>
    /// ⚠️ TWO VERSES IS THE LOAD-BEARING CASE. With ONE verse the band is exactly four
    /// staff spaces, the nominal half staff IS its centre, and the old arithmetic agrees
    /// with the new one to the last bit — so a one-verse theory cannot fail. It is here as
    /// the control that says so.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void TheGridMeterIsCentredOnTheBarlineBand(int verses)
    {
        string svg = Render(Book(verses));
        var (top, bottom) = Band(svg);
        double centre = (top + bottom) / 2;
        double meter = MeterY(svg);
        Assert.True(Math.Abs(meter - centre) < 1e-6,
            $"{verses} verse(s): the band runs {top:F3}‥{bottom:F3} (centre {centre:F3}) "
            + $"but the meter is drawn at {meter:F3}, {meter - centre:+0.000;-0.000} off");
    }

    /// <summary>
    /// …and the repeat dots — the grob that was unfolded first — agree with it, so the two
    /// are pinned to ONE centre rather than to two numbers that happen to match.
    /// </summary>
    [Fact]
    public void TheRepeatDotsAndTheMeterShareOneCentre()
    {
        string src = Book(2).Replace("form main { A }", "form main { |: A :| }");
        string svg = Render(src);
        var (top, bottom) = Band(svg);
        double centre = (top + bottom) / 2;

        // Repeat dots are the small filled circles of the repeat barline.
        var dots = Regex.Matches(svg, "<circle[^>]*cy=\"([0-9.-]+)\"")
            .Select(m => Num(m.Groups[1].Value)).ToList();
        Assert.True(dots.Count >= 2, $"expected a repeat's dots, found {dots.Count}");
        double dotCentre = (dots.Min() + dots.Max()) / 2;

        Assert.True(Math.Abs(dotCentre - centre) < 1e-6,
            $"the dots centre on {dotCentre:F3}, the band on {centre:F3}");
        Assert.True(Math.Abs(MeterY(svg) - dotCentre) < 1e-6,
            $"the meter is at {MeterY(svg):F3} and the dots centre on {dotCentre:F3}");
    }
}
