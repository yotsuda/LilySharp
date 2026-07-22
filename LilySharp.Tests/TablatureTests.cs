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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tablature parsing (tunings, fret calculation) and live-path rendering
/// (SharedRenderer: TAB clef, string lines, fret numbers).
/// </summary>
[Trait("Category", "Unit")]
public class TablatureTests
{
    [Fact]
    public void ParseTabStaff_NoTuning()
    {
        var source = @"$tabStaff { e4 a d' }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseTabStaff_WithGuitarTuning()
    {
        var source = @"$tabStaff \tuning $guitar { e4 a d' }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseTabStaff_WithBassTuning()
    {
        var source = @"$tabStaff \tuning bass { e,4 a, d g }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseTabStaff_WithBass5Tuning()
    {
        var source = @"$tabStaff \tuning $bass5 { b,,4 e, a, d g }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void ParseTabStaff_WithUkuleleTuning()
    {
        var source = @"$tabStaff \tuning $ukulele { g c' e' a' }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors);
    }

    [Fact]
    public void Tunings_CalculateFret_OpenLowE()
    {
        // E2 (40) = low E string = string 6 in guitar notation
        var (stringNum, fret) = Tunings.CalculateFret(40, Tunings.Guitar);
        Assert.Equal(6, stringNum);  // 6th string (lowest)
        Assert.Equal(0, fret);       // open string
    }

    [Fact]
    public void Tunings_CalculateFret_FrettedNote()
    {
        // G2 (43) on low E string = fret 3
        var (stringNum, fret) = Tunings.CalculateFret(43, Tunings.Guitar);
        Assert.Equal(3, fret);
    }

    [Fact]
    public void Tunings_CalculateFret_OpenHighE()
    {
        // E4 (64) = high E string = string 1 in guitar notation
        var (stringNum, fret) = Tunings.CalculateFret(64, Tunings.Guitar);
        Assert.Equal(1, stringNum);  // 1st string (highest)
        Assert.Equal(0, fret);       // open string
    }

    [Fact]
    public void Tunings_CalculateFret_PreferredString()
    {
        // A2 (45) on string 5 (A string) = fret 0
        var (stringNum, fret) = Tunings.CalculateFret(45, Tunings.Guitar, 5);
        Assert.Equal(5, stringNum);
        Assert.Equal(0, fret);
    }

    [Fact]
    public void Tunings_Bass_CalculateFret()
    {
        // E1 (28) on low E string = fret 0
        var (stringNum, fret) = Tunings.CalculateFret(28, Tunings.Bass);
        Assert.Equal(0, fret);
    }

    [Fact]
    public void RenderTabStaff_ContainsTabClefAndFretNumbers()
    {
        var source = """
            section Main {
                guitar {
                    e,4 a, d g |
                }
            }

            form main { Main }

            score main "test" {
                staff treble guitar
                tab standard guitar
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var svg = LiveRender.SvgFromRenderSpec(source);

        // Should contain TAB clef glyph (Emmentaler clefs.tab = U+E08F)
        Assert.Contains(EmmentalerGlyphs.TabClef.ToString(), svg);

        // Should contain serif fret-number text. The root font-family now NAMES the
        // bundled face the engine measured with and keeps the generic as the fallback,
        // so assert on the fallback rather than on the whole attribute.
        Assert.Contains(", serif\"", svg);

        // Should contain 6 staff lines for guitar tab (in addition to 5 for treble):
        // live staff/string lines are full-width horizontals at line thickness.
        var staffLineCount = System.Text.RegularExpressions.Regex.Matches(
            svg, "<line x1=\"0\\.00\" [^/]*stroke-width=\"0\\.100\"").Count;
        Assert.True(staffLineCount >= 11, $"Expected at least 11 staff lines (5 treble + 6 tab), got {staffLineCount}");
    }

    [Fact]
    public void RenderTabStaff_HasCorrectNumberOfStrings()
    {
        var source = """
            section Main {
                melody {
                    e,4 a, d g |
                }
            }

            form main { Main }

            score main "test" {
                tab standard melody
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(renderSpec);

        // Verify the render spec contains a tab item
        Assert.Contains(renderSpec.Items, i => i is TabStaffSpec);
        var tabItem = renderSpec.Items.OfType<TabStaffSpec>().First();
        Assert.Equal(Core.Syntax.TuningType.Guitar, tabItem.Tuning);
    }

    [Fact]
    public void GuitarTabSample_EndToEnd()
    {
        // Simplified version of guitar-tab.lys
        var source = """
            title "Guitar Tab Demo"
            tempo 120
            time 4/4

            part guitar {
              clef treble
            }

            section Main {
              guitar {
                e,4 a, d g | b4 e' a b |
              }
            }

            form main { Main }

            score main "guitar-tab" {
              staff guitar
              tab standard guitar
            }
            """;

        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics));

        var renderSpec = RenderSpecParser.FindByName(tree, "guitar-tab");
        Assert.NotNull(renderSpec);
        Assert.Equal(2, renderSpec.Items.Length); // staff + tab

        var svg = LiveRender.SvgFromRenderSpec(source);

        // Basic SVG structure
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);

        // Tab-specific elements: TAB clef glyph + the white fret-number
        // background that occludes the string line.
        Assert.Contains(EmmentalerGlyphs.TabClef.ToString(), svg);
        Assert.Contains("fill=\"#FFFFFF\"", svg);
    }
}
