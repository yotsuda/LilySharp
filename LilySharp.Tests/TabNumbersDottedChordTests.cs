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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A dotted CHORD on an <c>as numbers</c> tab draws no augmentation dot, exactly as a
/// dotted single note does not: the numbers style is fret digits and nothing of the
/// rhythm. Owner report, 2026-09-02 (tab-dot.lys — <c>&lt;c e g&gt;4.</c> printed its dots
/// while <c>e4.</c> beside it did not): the chord arm of the tab renderer passed no gate.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TabNumbersDottedChordTests
{
    private static RecordingDrawingContext Render(string style)
    {
        var tree = SyntaxTree.Parse($$"""
            time 6/8
            part melody {
              instrument guitar
              section A { <c e g>4. e4. }
            }
            form main { A }
            score main { tab melody as {{style}} }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    private static int Dots(RecordingDrawingContext page)
        => page.Glyphs.Count(g => g.Glyph == EmmentalerGlyphs.AugmentationDot);

    [Fact]
    public void AsNumbers_DrawsNoDot_ForTheChordOrTheNote()
        => Assert.Equal(0, Dots(Render("numbers")));

    [Fact]
    public void AsFull_DrawsTheDots_ForBoth()
    {
        // The positive control: the same book in the full style draws one dot per fret
        // row — three for the chord, one for the note.
        Assert.Equal(4, Dots(Render("full")));
    }
}
