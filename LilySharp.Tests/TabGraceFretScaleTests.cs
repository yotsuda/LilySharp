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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// tablature-grace-notes.ly: fret numbers belonging to grace notes are smaller —
/// by LilyPond's ratio. A normal TabNoteHead is font-size −2 and a grace one −4,
/// so grace/normal = 2^(−2/6) ≈ 0.7937 (LP 2.26.0 oracle, tabgrace twin:
/// whiteout heights 0.9366 vs 1.1800). The ratio rides on Lily#'s own (ratified
/// larger) base size, so the ABSOLUTE sizes differ from LP by design.
/// LILYPOND-REF: scm/music-functions.scm:636-650 general-grace-settings.
/// </summary>
[Trait("Category", "Unit")]
public class TabGraceFretScaleTests
{
    private const string BookTwin = """
        octave absolute
        part m { clef treble_8 tuning guitar }
        section A {
          m {
            c4 d e f |
            grace { e8 } c4 d e f |
          }
        }
        form { A }
        score { tab m as numbers }
        """;

    [Fact]
    public void GraceFret_ShrinksByTwoFontSizeSteps()
    {
        var svg = LiveRender.SvgFromRenderSpec(BookTwin);

        // Fret digits only: centre-anchored DIGIT texts in either fret face (the section label
        // "A" is bold+centred too, hence the content filter).
        var sizes = new List<double>();
        foreach (Match m in Regex.Matches(svg,
            "<text [^>]*font-size=\"([\\d.]+)\"(?: font-weight=\"bold\")? text-anchor=\"middle\"[^>]*>(\\d+)</text>"))
            sizes.Add(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));

        // 8 normal frets + 1 grace fret.
        Assert.Equal(9, sizes.Count);
        double normal = sizes.Max();
        double grace = sizes.Min();
        Assert.True(grace < normal);
        // The SVG prints a size to two places, so the ratio carries up to 0.005 / normal of
        // rounding on the grace side (at the 6-string face, 2.0 × 0.7937 = 1.587 prints 1.59).
        Assert.InRange(grace / normal, Math.Pow(2, -2.0 / 6) - 0.005 / normal, Math.Pow(2, -2.0 / 6) + 0.005 / normal);
    }
}
