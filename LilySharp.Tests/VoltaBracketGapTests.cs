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
/// A closed ending's hook stops short of the bar line where the next ending's bracket opens,
/// so the two read as two brackets (owner's request 2026-11-15; LilyPond draws them meeting on
/// the bar line — <c>VoltaBracketEngraver.GapBeforeTheNextBracket</c>).
/// </summary>
public class VoltaBracketGapTests
{
    private static VoltaBracketLayout[] Brackets(string music)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart m { section S { " + music + " } }\nform { S }\nscore { staff m }\n");
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        return [.. layout.VoltaBracketLayouts.OrderBy(v => v.StartMeasureIndex)];
    }

    [Theory]
    [InlineData("c''1 |: d''1 | [1. e''1 :| [2. f''1 ] | g''1 |")]
    [InlineData("c''1 |: d''1 | [1. e''1 ] [2. f''1 ] | g''1 |")]   // the first closed by its ']'
    public void TheFirstEndingsHook_StopsShortOfTheSecondsStart(string music)
    {
        var b = Brackets(music);
        Assert.Equal(2, b.Length);
        double gap = b[1].StartX - b[0].EndX;
        if (music.Contains(":|"))
            // A ':|' leaves more already (its thick line, as LilyPond spaces it): left alone.
            Assert.InRange(gap, 0.9, 1.0);
        else
            Assert.Equal(VoltaBracketEngraver.GapBeforeTheNextBracket, gap, 6);
    }
}
