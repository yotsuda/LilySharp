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
using LilySharp.Core.Svg;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A slur's base attachment steps off the lines ITS staff draws, and a tuplet bracket
/// keeps off ITS staff's extent and quantises inside ITS widened line span — not a
/// five-line staff's whatever the staff draws.
/// </summary>
/// <remarks>
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p875/probe/marks, the `as lines 1` twin under
/// <c>-dbackend=svg</c>, against the staff's one line):
/// <list type="bullet">
/// <item><c>b8( d' f' a')</c>: the slur leaves the b at 1.045 above the line (1.195 on five
///   lines — there the attachment rounds onto the line at +2 and steps 0.15 off it; a one-line
///   staff has no line at +2);</item>
/// <item><c>g( e c a,)</c>: −2.045 (−2.195 on five lines);</item>
/// <item><c>tuplet 3/2 { g4 e c } r2</c>, bracket above, stems up: its line stands 3.246 over
///   the staff line at the left hook and 1.443 at the right (on five lines the staff's own
///   edge 2.3 and the ±5 quantising span take part, and Lily# agreed there already).</item>
/// </list>
/// Before 2026-10-10 the notation callers never handed either scorer the line count, and the
/// bracket's staff edge was the five-line 2.3 and its span ±5 written in.
/// LILYPOND-REF: lily/slur-scoring.cc:639-658 move_away_from_staffline (on_staff_line);
///   lily/tuplet-bracket.cc:463-477 calc_position_and_height (the staff's extent widened by
///   staff-padding), :726-746 (the widened line span, on_line).
/// </remarks>
[Trait("Category", "Unit")]
public class SlurTupletFewLineStaffTests
{
    [Fact]
    public void ASlur_StepsOffTheLinesItsStaffDraws()
    {
        var g = RenderedGeometry.RenderProduct("""
            octave absolute
            time 4/4
            part p { }
            section A { p { b8( d' f' a') g( e c a,) | } }
            form { A }
            score { staff p as lines 1 }
            """);
        Assert.Equal(1.045, g.BowAttachmentAboveStaffLine(0, 0), 2);
        Assert.Equal(-2.045, g.BowAttachmentAboveStaffLine(1, 0), 2);
    }

    [Fact]
    public void ATupletBracket_KeepsOffItsOwnStaff()
    {
        var g = RenderedGeometry.RenderProduct("""
            octave absolute
            time 4/4
            part p { }
            section A { p { tuplet 3/2 { g4 e c } r2 | } }
            form { A }
            score { staff p as lines 1 }
            """);
        // The bracket is drawn as rules of its own thickness; its two hooks are the vertical
        // ones at its ends, and each hook's top is where the bracket line stands there.
        var line = g.Lines.Single(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && Math.Abs(l.StrokeWidth - EngravingDefaults.StaffLineThickness) < 1e-9 && Math.Abs(l.X2 - l.X1) > 4.0);
        var rules = g.Lines.Where(l => Math.Abs(l.StrokeWidth - EngravingDefaults.TupletBracketThickness) < 1e-9).ToList();
        var hooks = rules.Where(r => Math.Abs(r.X1 - r.X2) < 1e-9).OrderBy(r => r.X1).ToList();
        Assert.Equal(2, hooks.Count);
        double leftLine = line.Y1 - Math.Min(hooks[0].Y1, hooks[0].Y2);
        double rightLine = line.Y1 - Math.Min(hooks[1].Y1, hooks[1].Y2);
        Assert.InRange(leftLine, 3.246 - 0.02, 3.246 + 0.02);
        Assert.InRange(rightLine, 1.443 - 0.02, 1.443 + 0.02);
    }
}
