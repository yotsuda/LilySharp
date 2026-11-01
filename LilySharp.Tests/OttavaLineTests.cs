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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>layout { ottavaLine wave }</c> (OMR request 17, 2026-11-01; owner's decision): an old print's
/// ottava, a wave with no hook, against the default dashes and hook.
/// </summary>
[Trait("Category", "Unit")]
public class OttavaLineTests
{
    private static string Book(string layout) => $$"""
        octave absolute
        time 4/4
        {{layout}}
        part rh { clef treble }
        section A { rh { c'''4@8va d''' e''' f'''@!8va | c''4 d'' e'' f'' | } }
        form { A }
        score { staff rh }
        """;

    private const string Wave = "layout { ottavaLine wave }";

    private static ScoreLayout LayoutOf(string layout)
    {
        var tree = SyntaxTree.Parse(Book(layout));
        Assert.False(tree.HasErrors);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        return new LayoutEngine(score.Paper).Layout(score);
    }

    [Fact]
    public void TheDefault_IsDashesAndAHook_AndDashedWritesTheSamePicture()
    {
        Assert.True(Assert.Single(LayoutOf("").OttavaBracketLayouts).EdgeHeight > 0);
        static string Picture(string layout) => Regex.Replace(
            SvgGenerator.Generate(SyntaxTree.Parse(Book(layout))), "data-pos=\"[^\"]*\"", "");
        Assert.Equal(Picture(""), Picture("layout { ottavaLine dashed }"));
    }

    [Fact]
    public void Wave_HasNoHook()
        => Assert.Equal(0, Assert.Single(LayoutOf(Wave).OttavaBracketLayouts).EdgeHeight);

    [Fact]
    public void Wave_DrawsTheElementRun_AndNoDashes()
    {
        var dashed = SvgGenerator.Generate(SyntaxTree.Parse(Book("")));
        var wave = SvgGenerator.Generate(SyntaxTree.Parse(Book(Wave)));
        Assert.Contains("stroke-dasharray", dashed);
        Assert.DoesNotContain("stroke-dasharray", wave);
        Assert.True(wave.Length > dashed.Length, "the wave's elements are glyphs along the line");
    }

    [Fact]
    public void TheTwin_WritesLilyPondsTwoOverrides_OnlyForWave()
    {
        var wave = new LilyPondExporter().Export(SyntaxTree.Parse(Book(Wave)));
        Assert.Contains("OttavaBracket.style = #'trill", wave);
        Assert.Contains("OttavaBracket.edge-height = #'(0 . 0)", wave);
        Assert.DoesNotContain("OttavaBracket.style", new LilyPondExporter().Export(SyntaxTree.Parse(Book(""))));
    }

    [Fact]
    public void AnotherWord_IsAProblem()
    {
        var tree = SyntaxTree.Parse("layout { ottavaLine zigzag }");
        LayoutPlanReader.Read(tree.GetRoot().DescendantNodes().OfType<LayoutDeclarationSyntax>().Single(), out var found);
        Assert.NotEmpty(found);
    }
}
