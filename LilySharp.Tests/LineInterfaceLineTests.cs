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
using LilySharp.Core.Rendering;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The lines LilyPond draws through Line_interface — a text spanner's, an ottava's, a
/// glissando, a volta bracket — are round-capped <c>draw-line</c>s, and a dashed one has its
/// period fitted so it begins and ends with a dash (lily/line-interface.cc:235-268
/// Line_interface::line, scm/output-svg.scm:322-337 dashed-line). MEASURED 2.26.0 in Lab sessions/p1388/m (x1, p1, o1, g1, vb).
/// </summary>
public class LineInterfaceLineTests
{
    private const string Head = "octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\nsection A { m {\n";
    private const string Tail = "\n} }\nform { A }\nscore { staff m }\n";

    [Theory]
    // "rit.": 6.052 long, period 3.0, fraction 0.2 (x1).
    [InlineData(6.052, 3.0, 0.2, 0.5502, 2.2007)]
    // A palm mute 3.0736 long (p1: 0.51227 + 2.04909 = 2.56136, × 1.2).
    [InlineData(3.07364, 3.0, 0.2, 0.5123, 2.0491)]
    // An ottava 7.519 long, period 1.0, fraction 0.3 (o1).
    [InlineData(7.519, 1.0, 0.3, 0.3090, 0.7210)]
    // Shorter than one dash and its gap: no whole period, the period stays.
    [InlineData(0.5, 3.0, 0.2, 0.6, 2.4)]
    public void TheDash_IsFittedToTheLine_AsLilyPondFitsIt(double length, double period, double fraction,
        double on, double off)
    {
        var d = SharedRenderer.FittedDash(length, period, fraction);
        Assert.NotNull(d);
        Assert.Equal(on, d.Value.On, 4);
        Assert.Equal(off, d.Value.Off, 4);
    }

    private static Match[] Lines(string music)
    {
        var svg = TestPaper.SvgFromRenderSpec(Head + music + Tail);
        return Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*>")
            .Cast<Match>().ToArray();
    }

    private static double D(Match m, int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void ATextSpannersDashedLine_IsRoundCapped_AndEndsWithADash()
    {
        var dashed = Assert.Single(Lines("c''4@rit d'' e'' f''@!rit | c''1 |"),
            m => m.Value.Contains("stroke-dasharray"));
        Assert.Contains("stroke-linecap=\"round\"", dashed.Value);
        // LilyPond: 0.550 on, 2.201 off over its 6.052 (the raw 0.6 3.0 ended in a gap).
        Assert.Contains("stroke-dasharray=\"0.550 2.201\"", dashed.Value);
    }

    [Fact]
    public void AnOttavasLine_DashesAboutOnceAStaffSpace_RoundCapped()
    {
        var lines = Lines("c''4@8va d'' e'' f''@!8va | c''1 |");
        var dashed = Assert.Single(lines, m => m.Value.Contains("stroke-dasharray"));
        Assert.Contains("stroke-linecap=\"round\"", dashed.Value);
        var arr = Regex.Match(dashed.Value, "stroke-dasharray=\"([\\d.]+) ([\\d.]+)\"");
        double period = D(arr, 1) + D(arr, 2);
        // LilyPond: 1.030 (seven periods of the default 1.0 over 7.519); this drew 2.0.
        Assert.InRange(period, 0.95, 1.10);
        double length = D(dashed, 3) - D(dashed, 1);
        // The line ends on a dash: a whole number of periods plus one dash.
        double periods = (length - D(arr, 1)) / period;
        Assert.Equal(Math.Round(periods), periods, 3);
        // The hook at its end, too.
        Assert.Contains(lines, m => !m.Value.Contains("dasharray") && m.Value.Contains("stroke-linecap=\"round\"")
            && D(m, 1) == D(m, 3) && Math.Abs(Math.Abs(D(m, 4) - D(m, 2)) - 0.8) < 0.005);
    }

    [Fact]
    public void AGlissando_IsRoundCapped()
    {
        var gliss = Assert.Single(Lines("c'4@glissando g'' c''2 | c''1 |"),
            m => D(m, 1) != D(m, 3) && D(m, 2) != D(m, 4));
        Assert.Contains("stroke-linecap=\"round\"", gliss.Value);
    }

    [Fact]
    public void AVoltasHooks_AreTwoStaffSpacesFromTheLine_RoundCapped()
    {
        var svg = TestPaper.SvgFromRenderSpec("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m { c''1 | } }\nsection E1 { m { d''1 | } }\nsection E2 { m { e''1 | } }\n"
            + "form { |: A [1. E1] :| [2. E2] }\nscore { staff m }\n");
        var hooks = Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"\\1\" y2=\"([-\\d.]+)\" stroke=\"#000000\" stroke-width=\"0\\.160\"([^>]*)>")
            .Cast<Match>().ToArray();
        Assert.Equal(4, hooks.Length);   // both ends of both brackets
        foreach (var h in hooks)
        {
            Assert.Contains("stroke-linecap=\"round\"", h.Groups[4].Value);
            // LilyPond's draw-line runs 2.000; its cap takes the ink to 2.08 (the grob's -2.08).
            Assert.Equal(2.0, Math.Abs(D(h, 3) - D(h, 2)), 2);
        }
    }
}
