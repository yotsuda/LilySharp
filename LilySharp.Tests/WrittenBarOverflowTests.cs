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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A written bar with too much in it is still one bar on the page: what starts after its end
/// is not drawn, and the bars after it stay under the other parts' (Semantics.WrittenBar,
/// MeasureBuilder.OverflowsTheWrittenBar). A bar line left out — a span close to whole bars —
/// keeps every note.
/// </summary>
/// <remarks>
/// WHY IT EXISTS. The owner's sync.lys, 2026-10-14: `g4 g c,8 f1 c1 | e1` beside a part of full
/// bars. The page closed the bar where its beats ran out, made `c1` a bar of its own and `e1` a
/// fourth bar, and the score — laid out at the other part's three bars — never drew it. The
/// owner's decision: the written bar line wins, a left-out bar line is the one exception.
/// </remarks>
public class WrittenBarOverflowTests
{
    private static Measure[] Bars(string p2, string p1 = "c'1 | d | e | f")
    {
        var src = $$"""
            time 4/4
            section A {
              p1 { {{p1}} }
              p2 { {{p2}} }
            }
            form { A }
            score {
              staff p1
              staff p2
            }
            """;
        return new MeasureCollector().Collect(SyntaxTree.Parse(src), "p2").Voice.Measures.ToArray();
    }

    private static int Sounding(Measure m) => m.Items.Count(i => i is NoteItem or ChordItem);

    [Fact]
    public void TheOwnersBook_TheMusicPastTheBar_IsNotDrawn()
    {
        var bars = Bars("c'4 d e | g4 g c,8 f1 c1 | e1", "c'4 d e f | g2 g | c1");
        Assert.Equal(3, bars.Length);
        Assert.Equal(4, Sounding(bars[1])); // g g c f — the c1 after the bar's end is gone
        Assert.Equal(1, Sounding(bars[2])); // the e1, under the other part's third bar
    }

    [Fact]
    public void AnEighthTooMany_IsNotABarOfItsOwn()
    {
        var bars = Bars("c'1 | g2 g c,8 | e1 | f1");
        Assert.Equal(4, bars.Length);
        Assert.Equal(2, Sounding(bars[1]));
    }

    [Fact]
    public void ALeftOutBarLine_KeepsEveryNote()
    {
        // 15/8: two bars with the `|` between them missing (the owner's 銀河鉄道999).
        var bars = Bars("c'1 | d8 d d d d d d d e e e e e e e | f1");
        Assert.Equal(4, bars.Length);
        Assert.Equal(15, Sounding(bars[1]) + Sounding(bars[2]));
    }

    [Fact]
    public void MusicWithNoBarLines_StillFillsBarAfterBar()
    {
        var bars = Bars("c'1 d e f");
        Assert.Equal(4, bars.Length);
        Assert.All(bars, b => Assert.Equal(1, Sounding(b)));
    }

    [Fact]
    public void TwoWholeBarsWithOneBarLine_AreTwoBars()
    {
        var bars = Bars("c'1 d1 | e1 | f1");
        Assert.Equal(4, bars.Length);
    }
}
