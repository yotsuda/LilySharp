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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An additive numerator is the beat structure (grammar feedback A2, 2026-10-12): `time 3+2+2/8`
/// beams its eighths 3 + 2 + 2, as LilyPond beams the twin's <c>\time #'((3 2 2) . 8)</c>
/// (scm/time-signature-settings.scm calc-subdivided-fraction-structure). A plain 7/8 has no
/// grouping in LilyPond's table and stays unbeamed, as there.
/// </summary>
[Trait("Category", "Unit")]
public class AdditiveMeterBeamTests
{
    private static string Groups(string meter)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(
            "part m\nsection A { m { time " + meter + " c8 d e f g a b | } }\nform { ~A }\nscore { staff m }"));
        var groups = new BeamDetector().DetectBeamGroups(score);
        return string.Join("+", groups.Select(g => g.Members.Length));
    }

    [Theory]
    [InlineData("3+2+2/8", "3+2+2")]
    [InlineData("2+2+3/8", "2+2+3")]
    [InlineData("2+3+2/8", "2+3+2")]
    public void AnAdditiveNumerator_BeamsByItsTerms(string meter, string groups)
        => Assert.Equal(groups, Groups(meter));

    /// <summary>The grouping written after a plain meter (owner's decision 2026-10-12): the
    /// signature prints 7/8, the beams group as written, a regrouping or a plain restatement
    /// prints no signature and regroups, and the next section returns to the file's grouping.</summary>
    private const string Grouped = """
        time 7/8 (2+2+3)
        part m
        section A { m { c8 d e f g a b | c8 d e f g a b | time 7/8 (3+2+2) c8 d e f g a b | time 7/8 c8 d e f g a b | } }
        section B { m { c8 d e f g a b | } }
        form { ~A ~B }
        score { staff m }
        """;

    [Fact]
    public void AGrouping_BeamsAsWritten_RegroupsSilently_AndReturnsHome()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(Grouped));
        var groups = new BeamDetector().DetectBeamGroups(score);
        string Bar(int m) => string.Join("+", groups.Where(g => g.MeasureIndex == m).Select(g => g.Members.Length));
        Assert.Equal(["2+2+3", "2+2+3", "3+2+2", "", "2+2+3"], Enumerable.Range(0, 5).Select(Bar).ToArray());
        // Only the opening signature is drawn: the regroupings and the return are blanked.
        var drawn = score.Voice.Measures.SelectMany(m => m.Items).OfType<TimeSignatureChangeItem>().Count(t => !t.Blanked);
        Assert.Equal(0, drawn);
    }

    [Fact]
    public void TheTwin_WritesLilyPondsGrouping_AndOmitsTheSignatureOnARegrouping()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Grouped));
        Assert.Contains("\\time 2,2,3 7/8", ly);
        Assert.Contains("\\once \\omit Staff.TimeSignature \\time 3,2,2 7/8", ly);
        Assert.Contains("\\once \\omit Staff.TimeSignature \\time 7/8", ly);
        Assert.Contains("\\once \\omit Staff.TimeSignature \\time 2,2,3 7/8", ly);
    }

    [Theory]
    [InlineData("time 7/8 (2+3)", "adds up to 5")]
    [InlineData("time 3+2+2/8 (2+2+3)", "an additive numerator groups the beats itself")]
    public void AGroupingThatDoesNotFit_IsAnError(string meter, string message)
    {
        var tree = SyntaxTree.Parse("part m\nsection A { m { " + meter + " c8 d e f g a b | } }\nform { ~A }\nscore { staff m }");
        var error = Assert.Single(tree.Diagnostics, d => d.Code == DiagnosticCodes.BeatGroupingMismatch);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void APlainSevenEight_StaysUnbeamed_AsInLilyPond()
        => Assert.Equal("", Groups("7/8"));

    [Fact]
    public void TheBeatStructure_IsTheTerms_WhenTheyAddUpToTheNumerator()
    {
        Assert.Equal([3, 2, 2], BeamingPattern.Options.For(new TimeSignature(7, 8, "3+2+2")).BeatStructure.ToArray());
        // A spelling whose terms do not add up is not a structure: the plain default stands.
        Assert.Equal(BeamingPattern.Options.For(new TimeSignature(7, 8)).BeatStructure.ToArray(),
            BeamingPattern.Options.For(new TimeSignature(7, 8, "3+3")).BeatStructure.ToArray());
    }
}
