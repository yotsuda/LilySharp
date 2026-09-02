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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A pending <c>break</c> / <c>noBreak</c> just before a bare-barline empty placeholder
/// measure belongs to THAT measure — exactly as it does for a content measure — not the
/// next real bar (it must not silently jump the placeholder).
/// </summary>
[Trait("Category", "Unit")]
public class EmptyPlaceholderBreakTests
{
    private static IReadOnlyList<LilySharp.Core.Svg.Model.Measure> PrimaryMeasures(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.EnumerateStaves().First().Staff.PrimaryVoice.Measures;
    }

    [Fact]
    public void BreakBeforeLeadingPlaceholder_BreaksAfterThePlaceholder()
    {
        // A leading placeholder is an explicit `| |` pair (a single leading `|`
        // just anchors the section start and holds no measure).
        var measures = PrimaryMeasures(
            "part m { section A { break | | c4 c g' g | a a g2 } } form main { A } score main { staff m }");
        Assert.True(measures[0].IsEmptyPlaceholder);
        Assert.True(measures[0].HasBreakAfter);   // the break lands on the placeholder
        Assert.False(measures[1].HasBreakAfter);  // NOT deferred onto the content bar
    }

    [Fact]
    public void NoBreakBeforeLeadingPlaceholder_ForbidsBreakAfterThePlaceholder()
    {
        var measures = PrimaryMeasures(
            "part m { section A { noBreak | | c4 c g' g | a a g2 } } form main { A } score main { staff m }");
        Assert.True(measures[0].IsEmptyPlaceholder);
        Assert.Equal(BreakPermission.Forbid, measures[0].LineBreakPermission);
        Assert.Equal(BreakPermission.Allow, measures[1].LineBreakPermission);
    }

    [Fact]
    public void EmptyPlaceholderStaff_AlignedWithAContentStaff_Renders()
    {
        // A staff whose bar is an empty placeholder, sitting beside a staff that
        // plays real notes at the same index, must render. The shared timing
        // columns come from the SIBLING (the union), so the placeholder's springs
        // have to match that column count — reading the duration off the empty
        // primary alone returned no springs and LayoutColumns indexed past the
        // solved positions (an IndexOutOfRange crash on the user's exact input).
        var svg = SvgGenerator.Generate(SyntaxTree.Parse("""
            section B {
              melody {| | | |}
              melody2 { c1 | c1 | c1 | }
            }
            form main { B }
            score main { staff melody  staff melody2 }
            """), new SvgRenderOptions { EmbedFont = false });
        Assert.False(string.IsNullOrWhiteSpace(svg));
        Assert.Contains("<svg", svg);
    }
}
