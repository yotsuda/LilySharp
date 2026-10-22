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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A SCRIPT ON A TAB GRACE stands over the grace DIGIT, at general-grace-settings' Script size
/// (session 958). Until then the tab branch of ArticulationEngraver read the grace item's
/// column offset — its MAIN note's — and the full size: an accent on <c>grace { c16@accent }</c>
/// stood over the main note's digit, full-size, while the notation staff's grace accent had
/// stood over the grace head at −3 since session 916.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/music-functions.scm:636-650 general-grace-settings — (Voice Script font-size -3),
///   which a TabVoice inherits; the TabNoteHead is the script's X parent as the NoteHead is.
/// Identity values today (the grace digit is centred on its column, SharedRenderer.TabGraceDigits);
/// a twin of a tab grace script is a candidate for the ledger.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabGraceScriptTests
{
    private const double Tolerance = 0.006;

    private static string RenderTab(string sectionA) => LiveRender.SvgFromRenderSpec($$"""
        part gtr { tuning guitar }
        section A { gtr { {{sectionA}} } }
        form { A }
        score { tab gtr }
        """);

    private static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

    [Fact]
    public void AnAccentOnATabGrace_StandsOverTheGraceDigit_AtTheGraceSize()
    {
        var svg = RenderTab("r4 grace { c16@accent } d4 e f |");
        // The grace digit: the one fret digit drawn at the grace scale (font-size under 2).
        var digit = Regex.Matches(svg,
                "<text x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"([\\d.]+)\" text-anchor=\"middle\" data-pos=\"\\d+\">\\d+</text>")
            .Select(m => (X: D(m.Groups[1]), Size: D(m.Groups[2])))
            .Single(d => d.Size < 2.0);
        var accent = Regex.Matches(svg,
                "<text class=\"music\"(?: style=\"[^\"]*\")? x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"([\\d.]+)\" data-pos=\"\\d+\">(.)</text>")
            .Where(m => m.Groups[4].Value[0] == EmmentalerGlyphs.ArticAccentAbove)
            .Select(m => (X: D(m.Groups[1]), Y: D(m.Groups[2]), Size: D(m.Groups[3])))
            .Single();
        // Centred on the digit: the glyph's origin is its centre less OriginFromCentre, which the
        // SVG folds into x; the accent's own centre is at its origin, so x IS the digit's.
        Assert.InRange(accent.X, digit.X - Tolerance, digit.X + Tolerance);
        // general-grace-settings: Script font-size −3 = 4.0 × magstep(−3).
        Assert.Equal(4.0 * EmmentalerDesignSize.Magstep(-3), accent.Size, 2);
        // Over the tab's top string, not under the main note's.
        double topString = Regex.Matches(svg,
                "<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"[-\\d.]+\" stroke=\"#000000\" stroke-width=\"0.100\"/>")
            .Select(l => D(l.Groups[1])).Min();
        Assert.True(accent.Y < topString, $"the accent {accent.Y} stands over the top string {topString}");
    }
}
