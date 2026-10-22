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
/// A SCRIPT ON A TAB GRACE stands over — or under — the grace DIGIT, at general-grace-settings'
/// Script size, on the side opposite the grace's stem, which is UP (sessions 958–959). Until
/// then the tab branch of ArticulationEngraver read the grace item's column offset — its MAIN
/// note's — the full size, and the string-based stem direction: an accent on
/// <c>grace { c16@accent }</c> stood over the main note's digit, full-size, while the notation
/// staff's grace accent had stood over the grace head at −3 since session 916.
/// </summary>
/// <remarks>
/// MEASURED on LilyPond 2.26.0 (Lab sessions/p959/probe/tabgrace.ly, a TabStaff under
/// <c>\tabFullNotation</c> — a plain TabStaff blanks every Script, which Lily#'s full tab does
/// not, TabStaffStencils): <c>r4 \grace { c'16-> } d'4 e'-> f'</c> — the grace accent (Script
/// font-size −3) centred on the grace TabNoteHead (−4) at X, its origin 0.646985 UNDER the
/// bottom string (refY −4.396985 against the strings' ±3.75); the main note's accent centred on
/// its TabNoteHead, 1.289309 OVER the top string (refY 5.039309).
/// LILYPOND-REF: scm/music-functions.scm:636-650 general-grace-settings — (Voice Script font-size -3),
///   which a TabVoice inherits; :652-656 score-grace-settings — the grace Stem direction UP.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabGraceScriptTests
{
    private const double Tolerance = 0.006;

    private const string Book = """
        part gtr { tuning guitar }
        section A { gtr { r4 grace { c16@accent } d4 e@accent f | } }
        form { A }
        score { tab gtr }
        """;

    private static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

    [Fact]
    public void AnAccentOnATabGrace_IsCentredOnTheGraceDigit_AtTheGraceSize()
    {
        var svg = LiveRender.SvgFromRenderSpec(Book);
        // The grace digit: the one fret digit drawn at the grace scale (font-size under 2).
        var digit = Regex.Matches(svg,
                "<text x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"([\\d.]+)\" text-anchor=\"middle\" data-pos=\"\\d+\">\\d+</text>")
            .Select(m => (X: D(m.Groups[1]), Size: D(m.Groups[2])))
            .Single(d => d.Size < 2.0);
        var accents = Regex.Matches(svg,
                "<text class=\"music\"(?: style=\"[^\"]*\")? x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"([\\d.]+)\" data-pos=\"\\d+\">(.)</text>")
            .Where(m => m.Groups[4].Value[0] is EmmentalerGlyphs.ArticAccentAbove)   // the one sforzato, either side
            .Select(m => (X: D(m.Groups[1]), Y: D(m.Groups[2]), Size: D(m.Groups[3])))
            .OrderBy(a => a.X).ToList();
        Assert.Equal(2, accents.Count);
        // Centred on the digit (the accent's origin is its centre; the SVG folds OriginFromCentre in).
        Assert.InRange(accents[0].X, digit.X - Tolerance, digit.X + Tolerance);
        // general-grace-settings: Script font-size −3 = 4.0 × magstep(−3); the main one full-size.
        Assert.Equal(4.0 * EmmentalerDesignSize.Magstep(-3), accents[0].Size, 2);
        Assert.Equal(4.0, accents[1].Size, 2);
    }

    /// <summary>LilyPond's height, at full precision: the grace accent under the bottom string
    /// (the grace stem is up).</summary>
    /// <remarks>
    /// ⚠️ THE MAIN NOTE'S ACCENT IN THE SAME BOOK IS A DECIDED FLOOR, NOT A DEFECT (session 960):
    /// <c>e@accent</c> over the top string reads 1.546850 here against LilyPond's 1.289309, and
    /// the 0.257541 is the DIGIT. Both engines stand the accent at the digit's ink half-height
    /// + its own reach 0.42 + padding 0.2 × the tab's staff-space 1.5: Lily#'s guitar digit is
    /// em 2.3 regular (owner's decision 2026-10-01, TabConstants.ReducedFretEm), whose "0" inks
    /// 1.653700 tall — half 0.826850 + 0.42 + 0.30 = 1.546850 exactly — where LilyPond's
    /// TabNoteHead (font-size −2) inks 0.569309 over the string. Session 694's bass-tab
    /// agreement was on notes whose STEM decided the support; an open-string note on the top
    /// string is the shape the digit decides, and the decided digit shows.
    /// </remarks>
    [Fact]
    public void AnAccentOnATabGrace_StandsUnderTheBottomString_AsLilyPondsDoes()
    {
        var g = LpFidelity.RenderedGeometry.Render(Book);
        // Six strings, top down: line 0 the top string, line 5 the bottom. Accents left to right:
        // the grace's (below) is the first.
        Assert.Equal(-0.646985, g.GlyphAboveStaffLine(EmmentalerGlyphs.ArticAccentAbove, 0, 5), 3);
    }
}
