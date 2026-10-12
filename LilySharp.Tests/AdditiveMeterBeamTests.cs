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
