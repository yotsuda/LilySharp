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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// \voiceOne / \voiceTwo fix a slur's and a tie's direction only INSIDE the
/// <c>&lt;&lt; \\ &gt;&gt;</c> span (VoiceScan.SpanCurvesUp). showcase/grammar-tour, LilyPond 2.26.0
/// (Lab sessions/p650 gt): `g2~ g4 a | b4( c d e)` under the staff in bars 13-14, with the
/// piece's only two-voice passage in bar 43 — Lily# drew both over the staff, because any
/// second voice anywhere in the part pinned voice 1 up.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PolyphonyBowDirectionTests
{
    private static ScoreLayout Layout()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part melody
            section A {
              melody {
                g,2~ g,4 a, | b,4( c d e) |
                voice { b'2( a') | } { d'2( e') | }
              }
            }
            form { ~A }
            score { staff melody }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine().Layout(score);
    }

    [Fact]
    public void OutsideTheVoicesSpan_TheSlurAndTheTieTakeTheirOwnDirection()
    {
        var layout = Layout();
        // Bars 1-2: one voice, stems up — LilyPond's calc_direction (slur) and scored search
        // (tie) both put the bow under.
        Assert.All(layout.TieLayouts.Where(t => t.Tie.StartMeasureIndex == 0), t => Assert.False(t.CurveUp));
        Assert.All(layout.SlurLayouts.Where(s => s.Slur.StartMeasureIndex == 1), s => Assert.False(s.CurveUp));
        Assert.NotEmpty(layout.SlurLayouts.Where(s => s.Slur.StartMeasureIndex == 1));
    }

    [Fact]
    public void InsideTheSpan_TheVoiceStillFixesIt()
    {
        var inSpan = Layout().SlurLayouts.Where(s => s.Slur.StartMeasureIndex == 2).ToList();
        Assert.Equal(2, inSpan.Count);
        Assert.Contains(inSpan, s => s.Slur.VoiceIndex == 0 && s.CurveUp);
        Assert.Contains(inSpan, s => s.Slur.VoiceIndex == 1 && !s.CurveUp);
    }
}
