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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An instrument name too wide for LilyPond's indent stays on the paper (owner's report
/// 2026-10-27: a composed band score printed "lectric Guitar" on its first system and ". Bass" on
/// the next ones — LilyPond lets such a name overflow into the margin and past the paper's edge).
/// </summary>
[Trait("Category", "Unit")]
public class InstrumentNameOnPaperTests
{
    private static string Band(string guitar, string bass) => $$"""
        time 4/4
        part piano "Piano" "Pno." { instrument piano-right }
        part guitar {{guitar}} { instrument guitar }
        part bass {{bass}} { instrument bass }
        section A {
          piano { {{string.Concat(Enumerable.Repeat("c'1 | ", 24))}} }
          guitar { {{string.Concat(Enumerable.Repeat("c1 | ", 24))}} }
          bass { {{string.Concat(Enumerable.Repeat("c1 | ", 24))}} }
        }
        form { A }
        score { staffGroup { staff piano  staff guitar  staff bass } }
        """;

    private static (double First, double Later, LayoutOptions Paper) Indents(string text)
    {
        var tree = SyntaxTree.Parse(text);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        var systems = layout.Pages.SelectMany(p => p.Systems).ToList();
        Assert.True(systems.Count >= 2, "one system only");
        return (systems[0].Indent, systems[1].Indent, score.Paper);
    }

    // The name's left edge on the page, as the SVG draws it (anchored at its right end).
    private static double LeftEdgeOf(string svg, string name)
    {
        var m = Regex.Match(svg, "<text[^>]*\\bx=\"([-0-9.]+)\"[^>]*>" + Regex.Escape(name) + "</text>");
        Assert.True(m.Success, $"no \"{name}\" drawn");
        double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Value.Contains("text-anchor=\"end\"") ? x - Width(name) : m.Value.Contains("text-anchor=\"middle\"") ? x - Width(name) / 2 : x;
    }

    private static double Width(string name)
    {
        var tree = SyntaxTree.Parse("time 4/4\npart p { }\nsection A { p { c'1 | } }\nform { A }\n");
        var fonts = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).TextMetrics;
        return fonts.Advance(name, LilySharp.Core.Rendering.SharedRenderer.InstrumentNameEm(fonts),
            LilySharp.Core.Rendering.TextRole.Instrument, LilySharp.Core.Rendering.SharedRenderer.InstrumentNameStyle(fonts));
    }

    [Fact]
    public void ANameTooWideForThePaper_WidensTheIndent_ToKeepItOn()
    {
        var (first, later, paper) = Indents(Band("\"Electric Guitar\" \"E. Gtr.\"", "\"Electric Bass\" \"E. Bass\""));
        Assert.True(first > LayoutOptions.LilyPondDefaultIndent, $"first indent {first}");
        Assert.True(later > 0, $"later indent {later}");
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Band("\"Electric Guitar\" \"E. Gtr.\"", "\"Electric Bass\" \"E. Bass\"")));
        // The music is drawn in a group moved right by the left margin; the paper's edge is at −margin.
        double guitar = paper.MarginLeft + LeftEdgeOf(svg, "Electric Guitar"), bass = paper.MarginLeft + LeftEdgeOf(svg, "E. Bass");
        Assert.True(guitar >= 0.9, $"the long name stands {guitar} from the paper's edge");
        Assert.True(bass >= 0.9, $"the short name stands {bass} from the paper's edge");
    }

    [Fact]
    public void NamesThatFitThePaper_KeepLilyPondsIndents()
    {
        var (first, later, _) = Indents(Band("\"Guitar\" \"Gt.\"", "\"Bass\" \"B.\""));
        Assert.Equal(LayoutOptions.LilyPondDefaultIndent, first);
        Assert.Equal(0, later);
    }
}
