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
/// A measure ending with a repeat (<c>:|</c> / <c>:|:</c>) immediately followed by one
/// starting with a repeat (<c>|:</c> / <c>:|:</c>) is ONE combined <c>:|:</c> barline, not
/// two stacked — most visible when a phrase that ends <c>:|</c> is referenced right before
/// another that opens <c>|:</c>, which used to pile thick bars and double the dots.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BackToBackRepeatTests
{
    private static Measure[] Measures(string src) =>
        new MeasureCollector().Collect(SyntaxTree.Parse(src), "m").Voice.Measures.ToArray();

    [Fact]
    public void RepeatEndThenRepeatStart_MergeIntoOneRepeatBoth()
    {
        // `x` = one bar `|: c1 :|`; `x x` places two, so bar1's `:|` meets bar2's `|:`.
        var m = Measures("phrase x { |: c1 :| }\n"
                       + "part m { clef treble section A { x x } }\n"
                       + "form main { A }\nscore main { staff m }");
        Assert.Equal(2, m.Length);
        Assert.Equal(BarlineType.RepeatBoth, m[0].EndBarline); // :| + |: -> :|:
        Assert.Equal(BarlineType.None, m[1].StartBarline);     // the duplicate start is dropped
        Assert.Equal(BarlineType.RepeatStart, m[0].StartBarline); // the leading |: is kept
    }

    [Fact]
    public void SectionRepeatBothBetweenPhrases_StaysOneBarline()
    {
        // `x` ends `:|`; a section `:|:` between two references, then the next `x` opens
        // `|:` — all at one boundary must collapse to a single `:|:`, not three bars.
        var m = Measures("phrase x { |: c1 | d1 :| }\n"
                       + "part m { clef treble section A { x :|: x } }\n"
                       + "form main { A }\nscore main { staff m }");
        // x = 2 bars; two x's = 4 bars. The join between them (bars index 1 and 2).
        Assert.Equal(BarlineType.RepeatBoth, m[1].EndBarline);
        Assert.Equal(BarlineType.None, m[2].StartBarline);
    }

    [Fact]
    public void AnOrdinaryRepeatIsUntouched()
    {
        // A lone `|: … :|` with no adjacent repeat keeps its plain end/start.
        var m = Measures("part m { clef treble section A { |: c1 :| d1 } }\n"
                       + "form main { A }\nscore main { staff m }");
        Assert.Equal(BarlineType.RepeatStart, m[0].StartBarline);
        Assert.Equal(BarlineType.RepeatEnd, m[0].EndBarline);
    }
}
