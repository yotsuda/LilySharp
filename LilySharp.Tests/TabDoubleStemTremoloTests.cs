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

using LilySharp.Tests.LpFidelity;
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

    /// <remarks>
    /// ⚠️ READ OFF THE RECORDER, NOT THE SVG TEXT. This probe read <c>&lt;line&gt;</c>
    /// attributes, and SvgGenerator formats every coordinate with <c>F2</c> — so a slash's
    /// rise came back as a DIFFERENCE OF TWO NUMBERS ON A 0.01 GRID, which can never be the
    /// 0.375 claimed here; it is 0.37 or 0.38 depending only on where the staff happens to
    /// sit on the page. MEASURED 2026-09-07 (scratch/p345): the drawn rise is 0.375000000000
    /// and the run 1.500000000000, exactly, both before and after the rehearsal box moved
    /// 0.25 up the page — the F2 reading flipped 0.38 → 0.37 and turned this probe red while
    /// nothing it measures had changed. RecordingDocumentContext exists for exactly this
    /// (its own remark says so), so the tolerances below are the shape's, not the grid's.
    /// </remarks>
    [Fact]
    public void TabHalfNoteTremolo_CentresOnTheDoubleStem()
    {
        var g = RenderedGeometry.Render(BookTwin);

        // The double stem: two stem-thickness verticals 0.5 apart (the
        // double-stem-separation fallback, scm/tablature.scm:107 — was 0.355, a
        // pasted measurement, until this book pinned it).
        var stemLines = g.Lines
            .Where(l => Math.Abs(l.StrokeWidth - 0.13) < 5e-4 && Math.Abs(l.X1 - l.X2) < 1e-9)
            .ToList();
        var slashes = g.Lines
            .Where(l => Math.Abs(l.StrokeWidth - 0.48) < 5e-4 && Math.Abs(l.X1 - l.X2) > 1e-9)
            .Select(l => (l.X1, l.Y1, l.X2, l.Y2))
            .ToList();

        var stems = stemLines.Select(l => l.X1).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(2, stems.Count);
        Assert.Equal(0.5, stems[1] - stems[0], 9);
        double stemCenterX = (stems[0] + stems[1]) / 2;
        double stemFarY = stemLines.Max(l => Math.Max(l.Y1, l.Y2));

        // :32 on a half = 3 slashes (32nd flags − no beams on a half).
        Assert.Equal(3, slashes.Count);
        foreach (var s in slashes)
        {
            // Centred on the double stem — the claim of the book.
            Assert.Equal(stemCenterX, (s.X1 + s.X2) / 2, 9);
            // Width 1.5, rising to the right by width × slope 0.25 (device y-down:
            // the right end is the smaller y).
            Assert.Equal(1.5, s.X2 - s.X1, 9);
            Assert.Equal(0.375, s.Y1 - s.Y2, 9);
        }

        // Ladder 0.81, and the end-side slash centres one translation inside
        // the (down) stem's far end.
        var centers = slashes.Select(s => (s.Y1 + s.Y2) / 2).OrderBy(v => v).ToList();
        Assert.Equal(0.81, centers[1] - centers[0], 9);
        Assert.Equal(0.81, centers[2] - centers[1], 9);
        Assert.Equal(stemFarY - 0.81, centers[2], 9);
    }
}
