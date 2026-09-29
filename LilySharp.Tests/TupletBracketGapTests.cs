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
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tuplet bracket breaks for its number over (the number's width + 1.0), taken ALONG the
/// bracket from its midpoint and 0.1 to the right — "more space at right due to italics".
/// LILYPOND-REF: lily/tuplet-bracket.cc:326-337 Tuplet_bracket::print — gap = ext.length () + 1.0
/// LILYPOND-REF: lily/tuplet-bracket.cc:401-407 Tuplet_bracket::print — Interval (-0.5, 0.5) * gap + 0.1
/// LILYPOND-REF: lily/bracket.cc:57-61 Bracket::make_bracket — gap_corners[d] = (dz * 0.5) + gap[d] / length * dz
/// </summary>
/// <remarks>
/// It asserts the RULE off the drawn bracket, not LilyPond's numbers: the number's width is
/// the bundled face's advance, which is not LilyPond's (Lab sessions/p694/tup: LilyPond's gap
/// on a sloped `tuplet 3/2 { c'4 e' g' }` is 1.874, Lily#'s 1.96). Until session 694 the gap
/// was a flat ±1.0 about the midpoint, taken along X, whatever the number.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TupletBracketGapTests
{
    [Fact]
    public void TheNumbersGap_IsItsWidthPlusOne_AlongTheBracket_ShiftedRightByATenth()
    {
        const string book = """
            octave absolute
            part m {
              section A { tuplet 3/2 { c'4 e' g' } r2 | }
            }
            form main { A }
            score main { staff m }
            """;
        var g = RenderedGeometry.Render(book);
        var strokes = g.Lines.Where(l => Math.Abs(l.StrokeWidth - 0.16) < 1e-9).ToList();
        var hooks = strokes.Where(l => Math.Abs(l.X1 - l.X2) < 1e-9).OrderBy(l => l.X1).ToList();
        var pieces = strokes.Where(l => Math.Abs(l.X1 - l.X2) > 1e-9)
            .Select(l => l.X1 <= l.X2 ? (X1: l.X1, Y1: l.Y1, X2: l.X2, Y2: l.Y2) : (X1: l.X2, Y1: l.Y2, X2: l.X1, Y2: l.Y1))
            .OrderBy(l => l.X1).ToList();
        Assert.Equal(2, hooks.Count);
        Assert.Equal(2, pieces.Count);

        // The line through the two drawn ends, and the regime: it slopes.
        var (lx, ly) = (pieces[0].X1, pieces[0].Y1);
        var (rx, ry) = (pieces[1].X2, pieces[1].Y2);
        Assert.True(Math.Abs(ry - ly) > 0.3, "the bracket should slope — this book no longer tests the along-the-line rule.");
        double length = Math.Sqrt((rx - lx) * (rx - lx) + (ry - ly) * (ry - ly));
        double ux = (rx - lx) / length, uy = (ry - ly) / length;
        double midX = (lx + rx) / 2, midY = (ly + ry) / 2;

        // Both gap corners stand ON the line…
        foreach (var (x, y) in new[] { (pieces[0].X2, pieces[0].Y2), (pieces[1].X1, pieces[1].Y1) })
            Assert.Equal(0.0, (x - lx) * uy - (y - ly) * ux, 6);

        // …at −gap/2 + 0.1 and +gap/2 + 0.1 from the midpoint, measured along it.
        var fonts = ScoreTextMetrics.Bundled;
        double gap = fonts.Advance("3", TupletBracketEngraver.NumberEm(fonts), TextRole.Tuplet,
            TupletBracketEngraver.NumberStyle(fonts)) + 1.0;
        double along0 = (pieces[0].X2 - midX) * ux + (pieces[0].Y2 - midY) * uy;
        double along1 = (pieces[1].X1 - midX) * ux + (pieces[1].Y1 - midY) * uy;
        Assert.Equal(-0.5 * gap + 0.1, along0, 6);
        Assert.Equal(0.5 * gap + 0.1, along1, 6);
    }
}
