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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Navigation marks written in the <c>structure</c> block (segno / coda / fine /
/// to coda / D.C. / D.S. al fine|coda) are engraved like the inline @-marks.
/// </summary>
[Trait("Category", "Unit")]
public class FormNavigationTests
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
            form main { {{structure}} }
            score main "x" { staff m }
            """;
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));
        return score.MusicMarks.Select(m => m.Type).ToArray();
    }

    private static MusicMarkItem[] MarkItems(string structure)
    {
        var source = $$"""
            part m {
              clef treble
              section A { c4 d e f | }
              section B { g4 a b c | }
              section C { e4 f g a | }
              section D { c'4 b a g | }
            }
            form main { {{structure}} }
            score main "x" { staff m }
            """;
        return new MeasureCollector().Collect(SyntaxTree.Parse(source)).MusicMarks.ToArray();
    }

    [Fact]
    public void JumpInstructions_SitBelowStaff_TargetsAndToCodaAbove()
    {
        var marks = MarkItems("A segno B to coda C ds al coda coda D");
        MusicMarkVertical V(MusicMarkType t) => marks.First(m => m.Type == t).Vertical;

        // Jump-FROM instructions (D.S./D.C. family) are below the staff.
        Assert.Equal(MusicMarkVertical.Below, V(MusicMarkType.DalSegnoAlCoda));
        // Targets (segno/coda) and "To Coda" stay above.
        Assert.Equal(MusicMarkVertical.Above, V(MusicMarkType.Segno));
        Assert.Equal(MusicMarkVertical.Above, V(MusicMarkType.Coda));
        Assert.Equal(MusicMarkVertical.Above, V(MusicMarkType.ToCoda));
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
    public void ToCoda_OneWordSpelling_IsCollected()
    {
        // "tocoda" (run together) is accepted as "to coda" — it previously read as
        // an undefined section name.
        var marks = Marks("A tocoda B coda C D");
        Assert.Contains(MusicMarkType.ToCoda, marks);
    }

    [Fact]
    public void ToCoda_OneWordAndTwoWord_AreEquivalent()
    {
        Assert.Equal(
            Marks("A to coda B coda C D"),
            Marks("A tocoda B coda C D"));
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

    [Fact]
    public void CoPlaceToCoda_SitsLeftOfTheLabelOnTheCommonLine()
    {
        // A "To Coda" (end of one section) and the next section's label sit on the
        // same barline (close X); label C was raised to clear the sign, while B
        // sits on the common line. Co-placement drops C back to the common line,
        // keeps the sign in its own measure, and tucks it to C's left.
        var marks = System.Collections.Immutable.ImmutableArray.Create(
            new MusicMarkLayout(1, 39.65, -2.50, MusicMarkType.ToCoda, "To Coda", false, 0),
            new MusicMarkLayout(2, 41.15, -6.36, MusicMarkType.SectionLabel, "C", false, 0),
            new MusicMarkLayout(1, 23.93, -2.50, MusicMarkType.SectionLabel, "B", false, 0));

        var result = MusicMarkEngraver.CoPlaceToCodaWithLabels(marks);
        var tc = result.First(m => m.MarkType == MusicMarkType.ToCoda);
        var cLabel = result.First(m => m.MarkType == MusicMarkType.SectionLabel && m.Text == "C");

        Assert.Equal(1, tc.MeasureIndex);   // keeps its own (prev-section) measure
        Assert.Equal(-2.50, cLabel.Y, 3);   // C drops to the common (B) line
        // Sign baseline meets the box bottom: commonY + boxHalf, boxHalf =
        // (4.0*0.55 + 0.4)/2 = 1.3, so -2.50 + 1.3 = -1.20.
        Assert.Equal(-1.20, tc.Y, 3);
        Assert.True(tc.X < cLabel.X, "To Coda should sit left of the label");
    }
}
