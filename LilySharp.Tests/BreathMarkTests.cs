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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Breathing signs: '@breath' (scripts.rcomma) and '@caesura'
/// (scripts.caesura.straight) are BreathingSign grobs placed at the top of the
/// staff, right of the note they follow. LILYPOND-REF: lily/breathing-sign.cc.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BreathMarkTests
{
    [Theory]
    [InlineData("breath", ArticulationType.Breath)]
    [InlineData("caesura", ArticulationType.Caesura)]
    public void Registry_ResolvesBreathingSigns(string name, ArticulationType expected)
    {
        Assert.Equal(expected, ArticulationRegistry.Resolve(name));
        Assert.True(ArticulationRegistry.IsKnown(name));
    }

    [Fact]
    public void GetGlyph_MapsToBreathingSignConstants()
    {
        // scripts.rcomma / scripts.caesura.straight (verified against the cmap).
        Assert.Equal(EmmentalerGlyphs.BreathComma.ToString(),
            new ArticulationItem(ArticulationType.Breath, 0, 0, true, 0).GetGlyph());
        Assert.Equal(EmmentalerGlyphs.CaesuraStraight.ToString(),
            new ArticulationItem(ArticulationType.Caesura, 0, 0, true, 0).GetGlyph());
        // Distinct glyphs, neither empty.
        Assert.NotEqual(EmmentalerGlyphs.BreathComma, EmmentalerGlyphs.CaesuraStraight);
    }

    [Fact]
    public void Collector_AnchorsBreathToItsNote()
    {
        var src = "section Main {\n  m {\n    time 4/4\n    c4@breath d e f |\n  }\n}\nstructure { Main }\nscore \"x\" { staff { m } }\n";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src));
        var breath = score.Articulations.Single(a => a.Type == ArticulationType.Breath);
        Assert.Equal(0, breath.MeasureIndex);
        Assert.Equal(0, breath.ItemIndex); // attached to the first note
    }

    [Fact]
    public void BreathAndCaesura_BothRecognized_NoUnknownAnnotation()
    {
        // The annotation sweep must not flag @breath / @caesura as unknown.
        var src = "{ time 4/4 c4@breath d e a@caesura }";
        var tree = SyntaxTree.Parse(src);
        Assert.DoesNotContain(tree.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Breath_RendersGlyphInSvg()
    {
        var src = "{ time 4/4 c4@breath d e f }";
        var svg = SvgGenerator.Generate(SyntaxTree.Parse(src), new SvgRenderOptions { EmbedFont = false });
        Assert.Contains(EmmentalerGlyphs.BreathComma.ToString(), svg);
    }
}
