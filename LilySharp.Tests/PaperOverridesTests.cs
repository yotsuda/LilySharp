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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc svg|png|pdf --set KEY=VALUE</c> (LilySharp-Omr's proposal of 2026-10-02, P1): paper
/// values given from outside the file, read as paper entries and laid over the file's paper.
/// </summary>
[Trait("Category", "Unit")]
public class PaperOverridesTests
{
    private static LayoutOptions Over(LayoutOptions paper, params string[] settings)
    {
        var parsed = PaperOverrides.Parse(settings, out var error);
        Assert.Null(error);
        return parsed!.Apply(paper);
    }

    [Fact]
    public void ASettingIsAPaperEntry()
    {
        var p = Over(LayoutOptions.Default, "spacingIncrement=1.6", "leftMargin=13mm",
            "staffStaffSpacing.padding=2.5", "raggedRight");
        Assert.Equal(1.6, p.SpacingIncrement);
        Assert.Equal(7.397717, p.MarginLeft);   // 13mm, as the block reads it
        Assert.Equal(2.5, p.StaffSpacing.StaffStaff.Padding);
        Assert.True(p.RaggedRight);
    }

    [Fact]
    public void AFlagCanBeTurnedOff()
    {
        var on = LayoutOptions.Default with { RaggedRight = true, BreaksOnly = true };
        var p = Over(on, "raggedRight=false", "breaksOnly=false");
        Assert.False(p.RaggedRight);
        Assert.False(p.BreaksOnly);
    }

    [Theory]
    [InlineData("noSuchKey=1")]
    [InlineData("leftMargin=13furlongs")]
    [InlineData("spacingIncrement")]
    [InlineData("raggedRight=maybe")]
    [InlineData("=3")]
    public void ASettingThatDoesNotRead_IsRefused(string setting)
    {
        Assert.Null(PaperOverrides.Parse([setting], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    /// <summary>The settings win over the file's paper and reach the page.</summary>
    [Fact]
    public void TheSettingsOverlayTheFilesPaper()
    {
        const string book = """
            paper { spacingIncrement 1.2  raggedRight }
            octave absolute
            part m { }
            section A { m { c'4 d' e' f' | g'1 | } }
            form main { A }
            score main { staff m }
            """;
        var tree = SyntaxTree.Parse(book);
        var settings = PaperOverrides.Parse(["spacingIncrement=2.4", "raggedRight=false"], out _);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree), settings: settings);
        Assert.Equal(2.4, score.Paper.SpacingIncrement);
        Assert.False(score.Paper.RaggedRight);

        string plain = SvgGenerator.Generate(tree, new SvgRenderOptions());
        string set = SvgGenerator.Generate(tree, new SvgRenderOptions { PaperOverrides = settings });
        Assert.NotEqual(plain, set);
        Assert.Equal(plain, SvgGenerator.Generate(tree, new SvgRenderOptions { PaperOverrides = PaperOverrides.Parse([], out _) }));
    }
}
