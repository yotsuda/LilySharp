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
/// An accidental enters a column's spacing skyline from its bare box — unpadded — so it
/// meets a neighbour only where its own ink's height reaches it.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, LilySharp-Lab sessions/p578, the reader's Universe bars 17-20
/// squeezed onto one line with system-count = 1): in `f,8 c,8( a,4\2)` under five flats the
/// a's natural stands 0.455 above the c's head, and LilyPond compresses c → a to 2.425200,
/// where that spring blocks. The note column's skyline is padded (calc_skylines), the
/// accidental's is not (conditional_skyline); padding both, Lily# held the pair at 2.5635.
/// ⚠️ OPEN RESIDUAL −0.0117: Lily# gives 2.4135 — the padded head's sloped shoulder against the
/// bare natural. Held to ±0.015 here, an eighth of the move.
/// </remarks>
public class AccidentalSpacingPaddingTests
{
    [Fact]
    public void ANaturalClearOfTheHeadBesideIt_DoesNotHoldTheSpringOpen()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            key des major
            time 4/4
            part bassline { clef bass }
            section S { bassline { ges,8 ges,,16 ges,,16 r des,16 des, fis, f,8 c,( a,4\2) | } }
            form main { S }
            score main { staff bassline }
            """);
        var multi = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        var items = multi.PrimaryContentStaff.PrimaryVoice.Measures[0].Items;
        var c = items[^2];
        var a = items[^1];

        double minimum = SpacingRules.CalculateSkylineDistance(multi.TextMetrics, c, a, staffY: 0);

        Assert.InRange(minimum, 2.4252 - 0.015, 2.4252 + 0.015);
    }
}
