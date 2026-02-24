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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class GrobOverrideTests
{
    // --- Parser Tests ---

    [Fact]
    public void Parse_Override_Basic()
    {
        var tree = SyntaxTree.Parse("override Stem.length = 10 c4 d e f");
        Assert.False(tree.HasErrors);
        var overrideNode = tree.GetRoot().DescendantNodes()
            .OfType<OverrideDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(overrideNode);
        Assert.Equal("Stem", overrideNode.GrobName.Text);
        Assert.Equal("length", overrideNode.PropertyName.Text);
        Assert.Equal("10", overrideNode.ValueToken.Text);
    }

    [Fact]
    public void Parse_Revert_Basic()
    {
        var tree = SyntaxTree.Parse("revert Stem.length c4 d e f");
        Assert.False(tree.HasErrors);
        var revertNode = tree.GetRoot().DescendantNodes()
            .OfType<RevertDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(revertNode);
        Assert.Equal("Stem", revertNode.GrobName.Text);
        Assert.Equal("length", revertNode.PropertyName.Text);
    }

    [Fact]
    public void Parse_OnceOverride()
    {
        var tree = SyntaxTree.Parse("once override Stem.length = 10 c4 d e f");
        Assert.False(tree.HasErrors);
        var onceNode = tree.GetRoot().DescendantNodes()
            .OfType<OnceModifierSyntax>()
            .FirstOrDefault();
        Assert.NotNull(onceNode);
        Assert.IsType<OverrideDeclarationSyntax>(onceNode.Command);
        var innerOverride = (OverrideDeclarationSyntax)onceNode.Command;
        Assert.Equal("Stem", innerOverride.GrobName.Text);
    }

    [Fact]
    public void Parse_Override_IdentifierValue()
    {
        var tree = SyntaxTree.Parse("override Stem.direction = up c4 d e f");
        Assert.False(tree.HasErrors);
        var overrideNode = tree.GetRoot().DescendantNodes()
            .OfType<OverrideDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(overrideNode);
        Assert.Equal("direction", overrideNode.PropertyName.Text);
        Assert.Equal("up", overrideNode.ValueToken.Text);
    }

    [Fact]
    public void Parse_Override_NegativeValue()
    {
        var tree = SyntaxTree.Parse("override Beam.positions = -3 c8 d e f");
        Assert.False(tree.HasErrors);
        var overrideNode = tree.GetRoot().DescendantNodes()
            .OfType<OverrideDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(overrideNode);
        Assert.Equal("-3", overrideNode.ValueToken.Text);
    }

    [Fact]
    public void Parse_MultipleOverrides()
    {
        var tree = SyntaxTree.Parse("override Stem.length = 10 override Beam.thickness = 2 c8 d e f");
        Assert.False(tree.HasErrors);
        var overrides = tree.GetRoot().DescendantNodes()
            .OfType<OverrideDeclarationSyntax>()
            .ToList();
        Assert.Equal(2, overrides.Count);
        Assert.Equal("Stem", overrides[0].GrobName.Text);
        Assert.Equal("Beam", overrides[1].GrobName.Text);
    }

    [Fact]
    public void Parse_OverrideThenRevert()
    {
        var tree = SyntaxTree.Parse("override Stem.length = 10 c4 d revert Stem.length e f");
        Assert.False(tree.HasErrors);
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<OverrideDeclarationSyntax>());
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<RevertDeclarationSyntax>());
    }

    // --- Collector Tests ---

    [Fact]
    public void Collector_Override_Basic()
    {
        var tree = SyntaxTree.Parse("override Stem.length = 10 c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobOverrides);
        Assert.Equal("Stem", score.GrobOverrides[0].GrobType);
        Assert.Equal("length", score.GrobOverrides[0].PropertyName);
        Assert.Equal("10", score.GrobOverrides[0].Value);
        Assert.Equal(0, score.GrobOverrides[0].MeasureIndex);
        Assert.False(score.GrobOverrides[0].IsOnce);
    }

    [Fact]
    public void Collector_Revert_Basic()
    {
        var tree = SyntaxTree.Parse("revert Stem.length c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobReverts);
        Assert.Equal("Stem", score.GrobReverts[0].GrobType);
        Assert.Equal("length", score.GrobReverts[0].PropertyName);
        Assert.Equal(0, score.GrobReverts[0].MeasureIndex);
    }

    [Fact]
    public void Collector_OnceOverride()
    {
        var tree = SyntaxTree.Parse("once override Stem.length = 10 c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobOverrides);
        Assert.True(score.GrobOverrides[0].IsOnce);
    }

    [Fact]
    public void Collector_NoOverrides()
    {
        var tree = SyntaxTree.Parse("c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.GrobOverrides.IsEmpty);
        Assert.True(score.GrobReverts.IsEmpty);
    }

    [Fact]
    public void Collector_Override_AcrossMeasures()
    {
        var tree = SyntaxTree.Parse("c4 d e f | override Stem.length = 7 g a b c'");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobOverrides);
        Assert.Equal(1, score.GrobOverrides[0].MeasureIndex);
    }

    [Fact]
    public void Collector_OverrideThenRevert()
    {
        var tree = SyntaxTree.Parse("override Stem.length = 10 c4 d revert Stem.length e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobOverrides);
        Assert.Single(score.GrobReverts);
    }

    // --- GrobPropertyResolver Tests ---

    [Fact]
    public void Resolver_Empty_ReturnsNull()
    {
        var resolver = GrobPropertyResolver.Empty;
        Assert.Null(resolver.GetDouble("Stem", "length"));
        Assert.False(resolver.HasOverrides);
    }

    [Fact]
    public void Resolver_Override_GetDouble()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", "10", 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));
    }

    [Fact]
    public void Resolver_Override_GetDouble_NotYetActive()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", "10", 0, 2));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Null(resolver.GetDouble("Stem", "length"));

        resolver.AdvanceTo(0, 2);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));
    }

    [Fact]
    public void Resolver_Revert_RemovesOverride()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", "10", 0, 0));
        var reverts = ImmutableArray.Create(
            new GrobRevert("Stem", "length", 0, 2));
        var resolver = new GrobPropertyResolver(overrides, reverts);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));

        resolver.AdvanceTo(0, 2);
        Assert.Null(resolver.GetDouble("Stem", "length"));
    }

    [Fact]
    public void Resolver_OnceOverride_ClearsAfterOneAdvance()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", "10", 0, 0, IsOnce: true));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));

        // Advance to next item — once override should be cleared
        resolver.AdvanceTo(0, 1);
        Assert.Null(resolver.GetDouble("Stem", "length"));
    }

    [Fact]
    public void Resolver_IsOverridden()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", "10", 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.True(resolver.IsOverridden("Stem", "length"));
        Assert.False(resolver.IsOverridden("Stem", "thickness"));
        Assert.False(resolver.IsOverridden("Beam", "length"));
    }

    [Fact]
    public void Resolver_GetString()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "direction", "up", 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal("up", resolver.GetString("Stem", "direction"));
    }

    [Fact]
    public void Resolver_GetBool()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("NoteHead", "transparent", "true", 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.True(resolver.GetBool("NoteHead", "transparent"));
    }

    [Fact]
    public void Resolver_GetInt()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("StaffSymbol", "linecount", "1", 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(1, resolver.GetInt("StaffSymbol", "linecount"));
    }

    [Fact]
    public void Resolver_MultipleOverrides_SameGrob()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", "10", 0, 0),
            new GrobOverride("Stem", "thickness", "3", 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));
        Assert.Equal(3.0, resolver.GetDouble("Stem", "thickness"));
    }

    [Fact]
    public void Resolver_Override_ReplacesPrevious()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", "10", 0, 0),
            new GrobOverride("Stem", "length", "7", 0, 2));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));

        resolver.AdvanceTo(0, 2);
        Assert.Equal(7.0, resolver.GetDouble("Stem", "length"));
    }

    // --- Pipeline Integration Tests ---
    // LILYPOND-REF: lily/grob-property.cc — end-to-end override/revert pipeline

    private static string RenderToSvg(string input)
    {
        var tree = SyntaxTree.Parse(input);
        Assert.False(tree.HasErrors, $"Parse errors: {string.Join(", ", tree.Diagnostics)}");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine();
        var layout = engine.Layout(score);
        var renderer = new SvgRenderer();
        return renderer.Render(score, layout);
    }

    [Fact]
    public void Pipeline_Override_NoteHeadColor_AppearsInSvg()
    {
        var svg = RenderToSvg("override NoteHead.color = red c4 d e f");
        // All noteheads should have fill="red"
        Assert.Contains("fill=\"red\"", svg);
    }

    [Fact]
    public void Pipeline_Override_StemColor_AppearsInSvg()
    {
        var svg = RenderToSvg("override Stem.color = blue c4 d e f");
        // Stems should have stroke="blue"
        Assert.Contains("stroke=\"blue\"", svg);
    }

    [Fact]
    public void Pipeline_OnceOverride_NoteHeadColor_OnlyFirstNote()
    {
        var svg = RenderToSvg("once override NoteHead.color = red c4 d e f");
        // Only one note should have fill="red" (the first one)
        int count = CountOccurrences(svg, "fill=\"red\"");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Pipeline_Override_ThenRevert_NoteHeadColor()
    {
        var svg = RenderToSvg("override NoteHead.color = red c4 d revert NoteHead.color e f");
        // First two notes have red, last two don't
        int count = CountOccurrences(svg, "fill=\"red\"");
        Assert.Equal(2, count);
    }

    [Fact]
    public void Pipeline_NoOverrides_NoColorAttributes()
    {
        var svg = RenderToSvg("c4 d e f");
        // No fill="..." on noteheads (default rendering)
        Assert.DoesNotContain("fill=\"red\"", svg);
        Assert.DoesNotContain("fill=\"blue\"", svg);
    }

    [Fact]
    public void Pipeline_Override_NoteHeadTransparent_HidesNoteheads()
    {
        var svgWithTransparent = RenderToSvg("override NoteHead.transparent = true c4 d e f");
        var svgNormal = RenderToSvg("c4 d e f");
        // Transparent score should have fewer music text elements (noteheads hidden)
        int transparentGlyphs = CountOccurrences(svgWithTransparent, "class=\"music\"");
        int normalGlyphs = CountOccurrences(svgNormal, "class=\"music\"");
        Assert.True(transparentGlyphs < normalGlyphs,
            $"Transparent ({transparentGlyphs}) should have fewer glyphs than normal ({normalGlyphs})");
    }

    [Fact]
    public void Pipeline_GrobPropertyResolver_InScoreLayout()
    {
        var tree = SyntaxTree.Parse("override Stem.color = red c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine();
        var layout = engine.Layout(score);

        // Verify resolver is attached to layout
        Assert.True(layout.GrobPropertyResolver.HasOverrides);
    }

    [Fact]
    public void Pipeline_NoOverrides_EmptyResolver()
    {
        var tree = SyntaxTree.Parse("c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine();
        var layout = engine.Layout(score);

        // No overrides → empty resolver
        Assert.False(layout.GrobPropertyResolver.HasOverrides);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
