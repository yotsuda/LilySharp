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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A fret diagram (<c>@frame(…)</c>) stands over its chord whatever the stem, and under it
/// with <c>.down</c> — owner's decision, session 646. It took the side opposite the stem like a
/// staccato until 2026-09-26, and the LilyPond twin dropped every one (Lab
/// probes/complex-lys/06); LilyPond's TextScript default is DOWN, so the twin writes the side.
/// </summary>
[Trait("Category", "Unit")]
public class FretFrameTests
{
    private const string Book = """
        octave absolute
        part gt { clef treble }
        section A { gt { <g,@frame(320003) d g>2 <g' d'' g''@frame(320003)>2 | e2 c2@frame(x32010).down | } }
        form main { A }
        score main { staff gt }
        """;

    [Fact]
    public void AFrame_StandsAboveWhateverTheStem_AndBelowWithDown()
    {
        var tree = SyntaxTree.Parse(Book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var sides = new LayoutEngine().Layout(score).ArticulationLayouts
            .OrderBy(a => a.MeasureIndex).ThenBy(a => a.X)
            .Select(a => a.IsAbove).ToArray();
        // a low chord (stem up), a high chord (stem down), a `.down` frame
        Assert.Equal(new[] { true, true, false }, sides);
    }

    [Fact]
    public void TheTwin_WritesEachFrameAsAFretDiagramOnItsSide()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book));
        // A member's frame goes after the chord: LilyPond takes no text script on one head.
        Assert.Contains("<g, d g>2^\\markup \\fret-diagram-terse \"3;2;o;o;o;3;\"", ly);
        Assert.Contains("c2_\\markup \\fret-diagram-terse \"x;3;2;o;1;o;\"", ly);
    }
}
