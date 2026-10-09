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

using System.Linq;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie shorter than <c>min-length</c> is as high as ITS OWN width makes it — LilyPond
/// floors nothing there; <c>min-length</c> is only a penalty.
/// </summary>
/// <remarks>
/// Found by the bow twin sweep (HANDOFF §2 S0): Are You Gonna Go My Way (Lab corpus) draws
/// eighth-note ties 0.72–0.85 wide, and Lily# gave every one the height of a 1.0-wide tie.
/// This is two lines of that book. The numbers are LilyPond 2.26.0's, off its <c>lysc ly</c>
/// twin through Lab <c>sessions/p647/bows</c> (the tie's control points, cp1.y − cp0.y):
/// the tie <c>d,~</c> in bar 3 of the second line spans 0.846300 and rises 0.265300 (the
/// other ties agreed before the fix: longer than 1.0, or broken at a line end).
/// MEASURED before the fix: the same span, a rise of 0.306800.
/// LILYPOND-REF: lily/tie-configuration.cc:62-72 get_untransformed_bezier (no floor on the
/// width); lily/tie-formatting-problem.cc:751-754 (min-length as a penalty only).
/// </remarks>
[Trait("Category", "Unit")]
public class TieShortTieHeightTests
{
    private const string Book = """
        octave absolute
        key g major
        time 4/4
        part bassline {
          clef bass
          tuning bass
          section A {
            e,8 b,, dis, d,~ d, g, gis, a,~ | a, g, gis, a,~ a, cis, d, dis, | e, b,, e, d,~ d, e, f, fis,~ | fis, fis, g, a,~ a, a,, d, dis, | break
            e, b,, e, d,~ d, g,, gis,, a,,~ | a,, c, cis, d,~ d, a,, c, d, | e, b,, e, d,~ d, e, f, fis,~ | fis, b,, c, cis,~ cis, cis, d, dis, | break
          }
        }
        form { A }
        score { staff bassline }
        """;

    [Fact]
    public void AShortTie_RisesAsItsOwnWidthMakesIt()
    {
        var g = RenderedGeometry.RenderProduct(Book);

        // The tie LilyPond measures 0.8463 wide (the second line's `d,~` into bar 3); the
        // line-end pieces are short too, but a broken piece is not this question.
        int shortTie = Enumerable.Range(0, 12).Single(i => System.Math.Abs(g.BowSpan(i) - 0.8463) < 0.005);
        Assert.Equal(0.2653, g.BowControlLift(shortTie), 3);   // an UP tie
    }
}
