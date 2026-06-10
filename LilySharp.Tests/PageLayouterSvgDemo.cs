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
using Xunit;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;

namespace LilySharp.Tests;

/// <summary>
/// Page-breaking demo (visualization aid) — renders via the live SvgGenerator
/// pipeline and writes output/page-breaking-demo.svg.
/// </summary>
[Trait("Category", "Integration")]
public class PageLayouterSvgDemo
{
    private const string DemoSource = """
        title "Force-Based Vertical Spacing Demo"
        composer "LilySharp"
        time 4/4
        key c major

        part melody

        phrase theme1 {
          c'4 d e f | g2 f | e4 d c d | e1 |
        }

        phrase theme2 {
          g4 a b c | d'2 c | b,4 a g a | b1 |
        }

        phrase theme3 {
          e'4 d c b, | a,2 b, | c4 d e d | c1 |
        }

        section Intro { melody { theme1 } }
        section Dev { melody { theme2 } }
        section Recap { melody { theme3 } }

        structure { Intro Dev Recap Intro Dev Recap }

        render score "demo" { staff { melody } }
        """;

    [Fact]
    public void GeneratePageBreakingDemo()
    {
        var tree = SyntaxTree.Parse(DemoSource);
        Assert.False(tree.HasErrors, $"Parse errors: {string.Join(", ", tree.Diagnostics)}");

        var fontDir = Path.Combine(
            Path.GetDirectoryName(typeof(PageLayouterSvgDemo).Assembly.Location)!,
            "..", "..", "..", "..", "LilySharp.Core", "Fonts");
        fontDir = Path.GetFullPath(fontDir);
        var renderOptions = SvgRenderOptions.Export(fontDir);
        var svg = SvgGenerator.Generate(tree, renderOptions);

        var outputPath = Path.Combine(
            Path.GetDirectoryName(typeof(PageLayouterSvgDemo).Assembly.Location)!,
            "..", "..", "..", "..", "output", "page-breaking-demo.svg");
        outputPath = Path.GetFullPath(outputPath);
        File.WriteAllText(outputPath, svg);

        // 24 measures at default density break into several systems.
        var layout = new LayoutEngine().Layout(new MeasureCollector().Collect(tree));
        Assert.True(layout.Systems.Length >= 3, $"Expected ≥3 systems, got {layout.Systems.Length}");
    }

    [Fact]
    public void SkylineExtents_AreReasonable()
    {
        var tree = SyntaxTree.Parse(DemoSource);
        Assert.False(tree.HasErrors);

        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        var options = LayoutOptions.Default;
        var layoutEngine = new LayoutEngine(options);
        var layout = layoutEngine.Layout(score);

        var skylineBuilder = new SkylineBuilder(options.StaffHeight);
        var systemBreaker = new SystemBreaker(options);
        var sysMeasures = systemBreaker.BreakIntoSystems(score);

        for (int si = 0; si < layout.Systems.Length && si < sysMeasures.Count; si++)
        {
            var sys = layout.Systems[si];
            var measures = sysMeasures[si];
            var (upSky, _) = skylineBuilder.BuildSystemSkylines(measures, sys.Measures);
            double upExtent = LayoutUtilities.CalculateUpExtent(upSky);

            // upExtent should be reasonable (notes within ~2 octaves of staff)
            Assert.True(upExtent < 10,
                $"System {si}: upExtent={upExtent:F2} is too large — likely a notation or skyline bug");
        }
    }
}
