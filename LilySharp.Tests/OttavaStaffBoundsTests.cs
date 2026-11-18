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
/// An ottava bracket's two ends are the STAFF's columns, not the columns of the voice that
/// wrote the mark (session 1304): it begins at the first voice's column of its first moment
/// and ends after the last voice's column of its last, each where the note collision put it.
/// Until then the right end read the first voice's column, unshifted — 1.377 short where the
/// lower voice stands a second under the upper — and the left end the mark's own.
/// MEASURED (LilyPond 2.26.0, Lab sessions/p1304/ot o0-o10, dump.ily, raggedRight): the
/// bracket's X extent. Its right is 0.05 outside the hook's centre line, which is what
/// <c>EndX</c> is — the control o0 has the same 0.05.
/// </summary>
[Trait("Category", "Unit")]
public class OttavaStaffBoundsTests
{
    private static OttavaBracketLayout BracketOf(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return Assert.Single(new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)))
            .OttavaBracketLayouts);
    }

    [Theory]
    // The control: two voices a third apart, nothing shifted.
    [InlineData("o0", "voice { d''2 e''2 } { b'2@8vb c''2@!8vb } |", 16.321, 23.424)]
    // The lower voice a second under the upper: the end is after ITS shifted head…
    [InlineData("o1", "voice { c''2 d''2 } { b'2@8vb c''2@!8vb } |", 16.321, 25.490)]
    // …whichever voice wrote the mark.
    [InlineData("o2", "voice { c''2@8va d''2@!8va } { b'2 c''2 } |", 16.321, 25.490)]
    // The last moment is the other voice's alone.
    [InlineData("o3", "voice { c''4 d'' e'' f'' } { b'2@8vb c''2@!8vb } |", 16.321, 28.450)]
    // Stopping inside the bar, on a shifted column.
    [InlineData("o4", "voice { c''4 b' e'' f'' } { b'4@8vb a'@!8vb g'2 } |", 16.321, 24.034)]
    // The last voice's quarter head, not the first voice's half.
    [InlineData("o5", "voice { c''2 d''2 } { b'4 a'@8vb g'@!8vb f' } |", 20.950, 26.707)]
    // Beginning where the mark's own column is the shifted one: at the first voice's.
    [InlineData("o6", "voice { c''4 b' e'' f'' } { b'4 a'@8vb g'@!8vb f' } |", 20.225, 26.759)]
    [InlineData("o7", "voice { c''2@8va d''4@!8va e'' } { b'2 c''2 } |", 16.321, 25.417)]
    // Crossed voices: the FIRST voice's heads are the shifted ones, and the bracket begins there.
    [InlineData("o8", "voice { b'2 c''2 } { c''2@8vb d''2@!8vb } |", 17.423, 23.975)]
    [InlineData("o9", "voice { b'2@8va c''2@!8va } { c''2 d''2 } |", 17.423, 23.975)]
    // The first voice rests at the last moment: the end is after the last voice's head.
    [InlineData("o10", "voice { c''2 r2 } { b'2@8vb c''2@!8vb } |", 16.321, 24.112)]
    public void TheBracket_BeginsAndEndsWhereLilyPondsDoes(string book, string bars, double left, double right)
    {
        var bracket = BracketOf(bars);
        Assert.True(System.Math.Abs(left - bracket.StartX) <= 0.005, $"{book} left: {bracket.StartX:F3}, LilyPond {left:F3}");
        Assert.True(System.Math.Abs(right - 0.05 - bracket.EndX) <= 0.005, $"{book} right: {bracket.EndX:F3}, LilyPond {right - 0.05:F3}");
    }
}
