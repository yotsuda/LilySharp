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
/// tablature-double-stem-tremolo.ly: tremolo slashes on a tab half note centre
/// on its DOUBLE stem, hang one beam-translation inside the stem end, and step
/// 0.81 apart. LP 2.26.0 oracle (tabdbltrem twin, `a2:32` under
/// \tabFullNotation): stem-line centres 17.44/17.94 (0.5 apart), slash centres
/// X 17.69 = the pair's centre, Y 15.38/14.57/13.76 = stem end 16.19 − 0.81
/// ladder, slash rises to the right by 1.5 × 0.25.
/// LILYPOND-REF: scm/tablature.scm:97-111 make-double-stem-width-for-half-notes.
/// LILYPOND-REF: lily/stem-tremolo.cc:314-368 y_offset; :115-125 translation 0.81.
/// </summary>
[Trait("Category", "Unit")]
public class TabDoubleStemTremoloTests
{
    private const string BookTwin = """
        octave absolute
        part m { clef treble_8 tuning guitar }
        section A {
          m { a2:32 | }
        }
        form main { A }
        score main { tab m }
        """;

    [Fact]
    public void TabHalfNoteTremolo_CentresOnTheDoubleStem()
    {
        var svg = LiveRender.SvgFromRenderSpec(BookTwin);

        // The double stem: two stem-thickness verticals 0.5 apart (the
        // double-stem-separation fallback, scm/tablature.scm:107 — was 0.355, a
        // pasted measurement, until this book pinned it).
        var stems = new List<double>();
        var slashes = new List<(double X1, double Y1, double X2, double Y2)>();
        foreach (Match m in Regex.Matches(svg,
            "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\" stroke=\"[^\"]*\" stroke-width=\"([\\d.]+)\""))
        {
            double x1 = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double y1 = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double x2 = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            double y2 = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            string w = m.Groups[5].Value;
            if (w == "0.130" && x1 == x2)
                stems.Add(x1);
            else if (w == "0.480" && x1 != x2)
                slashes.Add((x1, y1, x2, y2));
        }

        Assert.Equal(2, stems.Count);
        stems.Sort();
        Assert.Equal(0.5, stems[1] - stems[0], 2);
        double stemCenterX = (stems[0] + stems[1]) / 2;
        double stemFarY = 0;
        foreach (Match m in Regex.Matches(svg,
            "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"\\1\" y2=\"([-\\d.]+)\" stroke=\"[^\"]*\" stroke-width=\"0.130\""))
            stemFarY = Math.Max(stemFarY, double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));

        // :32 on a half = 3 slashes (32nd flags − no beams on a half).
        Assert.Equal(3, slashes.Count);
        foreach (var s in slashes)
        {
            // Centred on the double stem — the claim of the book.
            Assert.Equal(stemCenterX, (s.X1 + s.X2) / 2, 2);
            // Width 1.5, rising to the right by width × slope 0.25 (SVG y-down:
            // the right end is the smaller y).
            Assert.Equal(1.5, s.X2 - s.X1, 2);
            Assert.Equal(0.375, s.Y1 - s.Y2, 2);
        }

        // Ladder 0.81, and the end-side slash centres one translation inside
        // the (down) stem's far end.
        var centers = slashes.Select(s => (s.Y1 + s.Y2) / 2).OrderBy(v => v).ToList();
        Assert.Equal(0.81, centers[1] - centers[0], 2);
        Assert.Equal(0.81, centers[2] - centers[1], 2);
        Assert.Equal(stemFarY - 0.81, centers[2], 2);
    }
}
