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
/// A lyric hyphen's dash and an extender are LilyPond's <c>round_filled_box</c>, not lines:
/// the dash 0.13 tall with a blot of 0.8 line-thicknesses (corner radius 0.04), the extender
/// 0.08 tall with a blot of 0.8 of its own thickness (radius 0.032). Until session 1389 both
/// were square-ended lines over the same box.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-hyphen.cc:125-126 Lyric_hyphen::print — dash_mol.
/// LILYPOND-REF: lily/lyric-extender.cc:128-129 Lyric_extender::print.
/// LILYPOND-REF: lily/lookup.cc:152-157 Lookup::round_filled_box — the blot clamped to the box.
/// LILYPOND-REF: scm/output-svg.scm:574-590 round-filled-box — a rect with ry = blot / 2.
/// MEASURED 2.26.0 in Lab sessions/p1389/m: `bla -- bla` under c'2 d'2 — a rect 0.5405 × 0.13,
/// ry 0.04; `looong __` under d'4( e'4), a hand twin with \lyricsto (l2-to.ly) — 0.29 × 0.08,
/// ry 0.032. Lily#'s l2.lys draws the same widths.
/// </remarks>
public class LyricConnectorRoundBoxTests
{
    private const string Song =
        "octave absolute\ntime 4/4\npart v { }\n"
        + "lyrics w { section Main { ha -- ha looong __ | bla -- bla | } }\n"
        + "section Main {\n  v { c'4 c' d'4( e'4) | c'2 d'2 | }\n}\n"
        + "form { Main }\nscore { staff v  lyrics w on v }\n";

    private static Match[] ThinRects(string svg)
        => Regex.Matches(svg, "<rect x=\"[-\\d.]+\" y=\"[-\\d.]+\" width=\"([\\d.]+)\" height=\"(0\\.[01]\\d*)\"([^>]*)>")
            .Cast<Match>().ToArray();

    private static double D(Match m, int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void AHyphensDash_AndAnExtender_AreRoundCorneredBoxes()
    {
        var svg = TestPaper.SvgFromRenderSpec(Song);
        var rects = ThinRects(svg);
        var dash = Assert.Single(rects, m => D(m, 2) == 0.13);
        Assert.Contains("ry=\"0.040\"", dash.Groups[3].Value);
        var extender = Assert.Single(rects, m => D(m, 2) == 0.08);
        Assert.Contains("ry=\"0.032\"", extender.Groups[3].Value);
        Assert.Equal(0.29, D(extender, 1), 2);
        // Neither is a line any more (a stem is a 0.13 line too, but upright).
        Assert.DoesNotMatch("<line x1=\"[-\\d.]+\" y1=\"([-\\d.]+)\" x2=\"[-\\d.]+\" y2=\"\\1\" [^>]*stroke-width=\"0\\.(130|080)\"", svg);
    }

    [Theory]
    // The hyphen's dash: blot 0.08 under its 0.13 height.
    [InlineData(0.66, 0.13, 0.08, 0.04)]
    // The extender: blot 0.064 under its 0.08.
    [InlineData(0.29, 0.08, 0.064, 0.032)]
    // A box thinner than its blot: the blot is the height — a capsule.
    [InlineData(1.0, 0.05, 0.08, 0.025)]
    // Narrower than its blot: the blot is the width.
    [InlineData(0.02, 0.13, 0.08, 0.01)]
    public void TheCornerRadius_IsHalfTheBlot_ClampedToTheBox(double width, double height, double blot, double radius)
        => Assert.Equal(radius, IDrawingContext.RoundedBoxRadius(width, height, blot), 9);
}
