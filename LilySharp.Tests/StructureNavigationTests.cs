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
/// Navigation marks written in the <c>structure</c> block (segno / coda / fine /
/// to coda / D.C. / D.S. al fine|coda) are engraved like the inline @-marks.
/// </summary>
[Trait("Category", "Unit")]
public class StructureNavigationTests
{
    private static MusicMarkType[] Marks(string structure)
    {
        var source = $$"""
            part m {
              clef treble
              section A { c4 d e f | }
              section B { g4 a b c | }
              section C { e4 f g a | }
              section D { c'4 b a g | }
            }
            structure { {{structure}} }
            score "x" { staff m }
            """;
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));
        return score.MusicMarks.Select(m => m.Type).ToArray();
    }

    [Fact]
    public void SegnoToCodaDsAlCodaAndCoda_AreCollected()
    {
        var marks = Marks("A segno B to coda C ds al coda coda D");
        Assert.Contains(MusicMarkType.Segno, marks);
        Assert.Contains(MusicMarkType.ToCoda, marks);
        Assert.Contains(MusicMarkType.DalSegnoAlCoda, marks);
        Assert.Contains(MusicMarkType.Coda, marks);
    }

    [Fact]
    public void DaCapoAlFineAndFine_AreCollected()
    {
        var marks = Marks("A B dc al fine C fine D");
        Assert.Contains(MusicMarkType.DaCapoAlFine, marks);
        Assert.Contains(MusicMarkType.Fine, marks);
    }

    [Fact]
    public void NoNavigationMarks_WhenStructureHasNone()
    {
        Assert.Empty(Marks("A B C D"));
    }
}
