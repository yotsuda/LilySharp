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

using Xunit;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests.Svg;

[Trait("Category", "Integration")]
public class GrandStaffRenderTests
{
    [Fact]
    public void RenderGrandStaff_ContainsBrace()
    {
        var source = """
            title "Test"
            time 4/4

            phrase rh { c''4 d'' e'' f'' | }
            phrase lh { c4 e g c' | }

            section Main {
              melody { rh }
              bass { lh }
            }

            form main { Main }

            score main "test" {
              grandStaff {
                staff treble melody
                staff bass bass
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var svg = LiveRender.SvgFromRenderSpec(source);

        // Should contain brace (rendered using Emmentaler-Brace font)
        Assert.Contains("<text", svg);
        Assert.Contains("Emmentaler-Brace", svg);

        // Should contain two sets of staff lines (10 lines total).
        // Live staff lines are full-width horizontal lines at staff-line thickness.
        var staffLineCount = System.Text.RegularExpressions.Regex.Matches(
            svg, @"<line x1=""0\.00"" [^/]*stroke-width=""0\.100""").Count;
        Assert.Equal(10, staffLineCount);

        // Should contain treble and bass clef glyphs
        Assert.Contains("class=\"music\"", svg);
    }

    [Fact]
    public void RenderGrandStaff_HasCorrectStaffPositions()
    {
        var source = """
            title "Test"
            time 4/4

            phrase rh { c''4 | }
            phrase lh { c4 | }

            section Main {
              melody { rh }
              bass { lh }
            }

            form main { Main }

            score main "test" {
              grandStaff {
                staff treble melody
                staff bass bass
              }
            }
            """;

        var tree = SyntaxTree.Parse(source);
        var renderSpec = RenderSpecParser.FindFirst(tree)!;

        var collector = new MeasureCollector();
        var score = collector.CollectMultiStaff(tree, renderSpec);

        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);

        // Verify staff groups exist
        var system = layout.Systems[0];
        Assert.False(system.StaffGroups.IsDefaultOrEmpty);
        Assert.Single(system.StaffGroups);

        var staffGroup = system.StaffGroups[0];
        Assert.True(staffGroup.IsGrandStaff);
        Assert.Equal(2, staffGroup.Staves.Length);
        Assert.Equal(ClefType.Treble, staffGroup.Staves[0].Clef);
        Assert.Equal(ClefType.Bass, staffGroup.Staves[1].Clef);
    }

    [Fact]
    public void RenderGrandStaff_SystemBarlinesSpanBothStaves()
    {
        var source = """
            title "Test"
            time 4/4

            phrase rh { c''4 d'' e'' f'' | g'' a'' b'' c''' | }
            phrase lh { c4 e g c' | d e f g | }

            section Main {
              melody { rh }
              bass { lh }
            }

            form main { Main }

            score main "test" {
              grandStaff {
                staff treble melody
                staff bass bass
              }
            }
            """;

        var svg = LiveRender.SvgFromRenderSpec(source);

        // Should have a SpanBar: a barline rect taller than one staff (4.0 spaces)
        // bridging the gap between the two staves.
        var heights = System.Text.RegularExpressions.Regex.Matches(
                svg, @"<rect [^/]*height=""([0-9.]+)""")
            .Select(m => double.Parse(m.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(heights, h => h > 5.0);
    }
}
