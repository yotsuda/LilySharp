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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Two adjacent staves whose stems reach into the shared gap are spaced so the stems
/// clear each other — even when the constraint lives on a collision-SHIFTED voice whose
/// seed box merely TOUCHES the other staff's stem in X. Two ports meet here:
/// the skyline seed carries the note-collision shift (a shifted voice is reserved where
/// it is DRAWN — SkylineBuilder.AddStaffToSkylines), and skyline distance counts a
/// zero-width touch (SkylineBuilding.DistanceResolved, skyline.cc:628-645).
/// LILYPOND-REF: lily/align-interface.cc:228-238 internal_get_minimum_translations.
/// Pinned against the LP twin audit\lpreg\stclash.{lys,-lp.ly,-lp.svg}
/// (stems-clash-between-staves.ly).
/// </summary>
[Trait("Category", "Unit")]
public class StaffClashSpacingTests
{
    [Fact]
    public void ShiftedVoicesDownStem_PushesTheStaffBelowClear()
    {
        // stems-clash-between-staves.ly: upper staff << d \\ <c a,> >> — the second
        // voice is collision-shifted one head right, and its down stem is the deepest
        // ink; lower staff <a b> answers with an up stem. LP spaces the two staff
        // refpoints 10.833 apart = 6.5 (down-stem tip) + 3.333 (up-stem tip) + 1.0
        // (staff-staff padding), the whole of which rides on the two stems' seed
        // boxes touching at one x.
        string svg = Render(
            "octave absolute\n" +
            "time 4/4\n\n" +
            "part up { clef treble }\n" +
            "part lo { clef treble }\n\n" +
            "section Main {\n" +
            "  up {\n" +
            "    voice { d4 }\n" +
            "    { <c a,>4 }\n" +
            "  }\n" +
            "  lo { <a b>4 }\n" +
            "}\n\n" +
            "form main { ~Main }\n\n" +
            "score main { staff up staff lo }\n");

        var middles = StaffMiddleLines(svg);
        Assert.Equal(2, middles.Count);
        // LP: refpoint (middle line) to refpoint 10.833.
        Assert.Equal(10.833, middles[1] - middles[0], 0.02);

        // The stems the gap is spaced by, in each staff's own frame (LP: the upper
        // staff's shifted down stem reaches +6.5 below its middle, the lower staff's
        // up stem 3.333 above its own) — their tips exactly one padding apart.
        var stems = StemSegments(svg);
        double midGap = (middles[0] + middles[1]) / 2;
        var upperDownTip = stems.Where(s => s.YMin < midGap).Max(s => s.YMax) - middles[0];
        var lowerUpTip = middles[1] - stems.Where(s => s.YMin > midGap).Min(s => s.YMin);
        Assert.Equal(6.5, upperDownTip, 0.02);
        Assert.Equal(3.333, lowerUpTip, 0.02);
        Assert.Equal(1.0, (middles[1] - lowerUpTip) - (middles[0] + upperDownTip), 0.02);
    }

    private static string Render(string source) =>
        LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(source),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>Middle (3rd) staff line Y of each staff, top to bottom.</summary>
    private static List<double> StaffMiddleLines(string svg)
    {
        var ys = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\"[^>]*stroke-width=\"0.100\"")
            .Select(m => double.Parse(m.Groups[1].Value))
            .OrderBy(y => y)
            .ToList();
        var middles = new List<double>();
        for (int i = 0; i + 5 <= ys.Count; i += 5)
            middles.Add(ys[i + 2]);
        return middles;
    }

    /// <summary>All drawn stems: (YMin, YMax) page-down spans of the 0.130 lines.</summary>
    private static List<(double YMin, double YMax)> StemSegments(string svg) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"0.130\"")
            .Select(m =>
            {
                double a = double.Parse(m.Groups[1].Value);
                double b = double.Parse(m.Groups[2].Value);
                return (YMin: System.Math.Min(a, b), YMax: System.Math.Max(a, b));
            })
            .ToList();
}
