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

using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tuplet bracket's lines are round-capped, as LilyPond's are (lily/bracket.cc
/// Bracket::make_bracket over lily/line-interface.cc make_line). MEASURED 2.26.0 (Lab
/// sessions/p1387/m t1, <c>tuplet 3/2 { c'4 d' e' } c'2</c>): the TupletBracket spans
/// x 0.894 … 6.592, y 2.620 … 3.480 — 0.860 high, the hook plus the 0.08 cap of a 0.16 line at
/// each end. Butt-capped, the hooks stopped at 2.700 and the bracket stood 0.780 high.
/// </summary>
public class TupletBracketCapTests
{
    [Fact]
    public void TheBracket_IsTheHookPlusACapAtEachEnd_High()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\ntuplet 3/2 { c'4 d' e' } c'2 | c''1 |\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var page = Assert.Single(BoxesGenerator.GeneratePages(tree, RenderSpecParser.FindFirst(tree)));
        var pieces = page.Symbols.Where(s => s.Kind == "tupletBracket").ToArray();
        Assert.Equal(4, pieces.Length);   // two hooks, the line either side of the number
        Assert.Equal(0.860, pieces.Max(s => s.Box[3]) - pieces.Min(s => s.Box[1]), 3);
        Assert.Equal(5.698, pieces.Max(s => s.Box[2]) - pieces.Min(s => s.Box[0]), 3);
    }
}
