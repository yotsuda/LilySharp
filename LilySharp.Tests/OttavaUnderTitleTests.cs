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

using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A first system's ottava is in the scalar the title's spring reads (OMR request 18,
/// 2026-11-01): the bracket was only on the system's silhouette, so "8va" and its dashes printed
/// through the title, where LilyPond 2.26.0 stands the title clear over them.
/// </summary>
[Trait("Category", "Unit")]
public class OttavaUnderTitleTests
{
    private const string Book = """
        title "Ottava Wave"
        octave absolute
        time 9/8
        key f major
        part rh { clef treble }
        section Piece {
          rh {
            a'8 c''8 f''8 a''8 g''8 f''8 e''8 d''8 c''8 |
            f'''4.@8va f'''8 e'''8 d'''8 cis'''8 d'''8 bes''8@!8va |
            a''8 a''8 a''8 g''8 g''8 g''8 f''4. |
          }
        }
        form { ~Piece }
        score { staff rh }
        """;

    [Fact]
    public void AnOttavaOnTheFirstSystem_StandsClearUnderTheTitle()
    {
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(Book));
        // The two baselines as drawn: the label's cap height stands about 1.5 over its own, so
        // a gap under that is the label inside the title's line.
        double gap = BaselineOf(svg, "8va") - BaselineOf(svg, "Ottava Wave");
        Assert.True(gap > 2.0, $"the 8va's baseline stands {gap:F3} under the title's");
    }

    private static double BaselineOf(string svg, string text)
    {
        var match = Regex.Match(svg, "<text[^>]* y=\"([0-9.]+)\"[^>]*>" + text + "</text>");
        Assert.True(match.Success, $"no <text> says '{text}'");
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
