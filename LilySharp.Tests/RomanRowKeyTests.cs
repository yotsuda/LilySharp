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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord row under <c>as roman</c> reads its degrees in the FILE's key and the sections', never a
/// part's header key, wherever it stands in the score (owner's decision 2026-11-20, session 1336):
/// a row is one track several bands may show. Until then the walk left the last part's header
/// tonic in the metadata, and <c>key c major</c> over <c>part a { key d major }</c> printed
/// C G7 Am F as "VII IV7 Vm III" — D's tonic beside C's signature (Lab sessions/p1278/probe/row1.lys).
/// </summary>
[Trait("Category", "Unit")]
public class RomanRowKeyTests
{
    private static string Degrees(string partHeader, string score, string sectionHeader = "")
    {
        string text = "octave absolute\ntime 4/4\nkey c major\npart a { clef treble" + partHeader + " }\n"
            + "chords prog { section A { C | G7 | Am | F | } }\n"
            + "section A { " + sectionHeader + " a { c'1 | b1 | a1 | a1 | } }\nform { A }\n" + score + "\n";
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(text));
        return string.Join(" ", Regex.Matches(svg, ">([IVX]+[^<]{0,4})</text>").Select(m => m.Groups[1].Value));
    }

    [Theory]
    // Over the part's staff or on its own, a part's header key does not move the degrees.
    [InlineData(" key d major", "score { chords prog as roman staff a }")]
    [InlineData(" key d major", "score { staff a chords prog as roman }")]
    [InlineData("", "score { chords prog as roman staff a }")]
    public void ARowReadsTheFilesKey(string partHeader, string score)
        => Assert.Equal("I V7 VIm IV", Degrees(partHeader, score));

    /// <summary>A section after one that wrote its own key goes home to the FILE's key in the row,
    /// though the part's own home is its header key.</summary>
    [Fact]
    public void ASectionGoingHome_GoesToTheFilesKey()
    {
        const string text = "octave absolute\ntime 4/4\nkey c major\npart a { clef treble key d major }\n"
            + "chords prog { section A { C | G7 | } section B { C | G7 | } }\n"
            + "section A { key f major a { c'1 | b1 | } }\nsection B { a { c'1 | b1 | } }\nform { A B }\n"
            + "score { chords prog as roman staff a }\n";
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(text));
        Assert.Equal("V II7 I V7", string.Join(" ", Regex.Matches(svg, ">([IVX]+[^<]{0,4})</text>").Select(m => m.Groups[1].Value)));
    }

    /// <summary>A section's key is the score's, and the row follows it as before.</summary>
    [Fact]
    public void ASectionsKeyStillMovesTheDegrees()
        => Assert.Equal("VII IV7 Vm III", Degrees(" key d major", "score { chords prog as roman staff a }", "key d major"));
}
