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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A rows-only score draws every bar its form plays, whatever the rows it shows write (owner's
/// report 2026-11-16, "1-01 My Life.lys" with its chords row commented out: a lyrics row of one
/// unbarred word in section B drew A, then B as one bar with no bar line, and nothing after —
/// the row stopped at its last word). Each section is as long as the rows it writes, shown or
/// not; a row that stops early is carried on in empty bars.
/// </summary>
[Trait("Category", "Unit")]
public class RowsOnlyGridPadTests
{
    private const string Book = """
        time 4/4
        key d major
        section A {
          chords harmony { D | D | D | D | }
        }
        section B {
          chords harmony { D | C7 | F7 | G | }
          lyrics words { ほえほえ }
        }
        section C {
          chords harmony { A | A | }
        }
        form { A B C B }
        score { lyrics words }
        """;

    private static int Bars(string book) =>
        Regex.Matches(SvgGenerator.Generate(SyntaxTree.Parse(book), SvgRenderOptions.Preview()),
            @"<rect class=""bar-box"" data-bar=""(\d+)""").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().Count();

    [Fact]
    public void ALyricsRowThatStopsEarly_IsCarriedOnToTheFormsEnd()
        // A 4 + B 4 + C 2 + B 4.
        => Assert.Equal(14, Bars(Book));

    [Fact]
    public void TheSameBars_AsWithTheChordsRowShown()
        => Assert.Equal(Bars(Book.Replace("score { lyrics words }", "score { chords harmony  lyrics words }")), Bars(Book));

    [Fact]
    public void AnUnbarredWord_StandsInItsSectionsFirstBar_AndTheSectionKeepsItsBarLines()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book), SvgRenderOptions.Preview());
        // The word is drawn twice (B is played twice), and bars 5-8 (B) each have a box.
        Assert.Equal(2, Regex.Matches(svg, ">ほえほえ<").Count);
        foreach (int bar in new[] { 5, 6, 7, 8 })
            Assert.Contains($@"data-bar=""{bar}""", svg);
    }
}
