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
/// A slur or tie on a voice the note collision shifted leaves the SHIFTED column: LilyPond's
/// bound is the NoteColumn where the collision put it, so the head centre and the stem a bow
/// attaches to move with it. Until session 658 Lily# drew the head there but bowed from the
/// column's unshifted place, 0.443 to the left.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, Lab sessions/p658: the reader's beam-slur.lys and vtie.lys, the
/// up-stemmed c of one part shifted right of the other part's e in a condensedStaff): the slur
/// starts on the shifted stem 1.2392 right of the c head's left edge, the tie leaves it 1.6392
/// right. Found by the S4 bow sweep (session 657, unexplained residual ⑵).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class VoiceShiftBowTests
{
    private static RecordingDrawingContext Render(string first)
    {
        var tree = SyntaxTree.Parse($$"""
            part melody {
              section A { {{first}} }
            }

            part melody2 {
              section A { e g b r }
            }

            form { A }

            score {
              condensedStaff {
                melody
                melody2
              }
            }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    // The first column holds the other part's e and, shifted right of it, the bowed c.
    private static double ShiftedHeadX(RecordingDrawingContext page) => page.Glyphs
        .Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack)
        .Select(g => g.X).OrderBy(x => x).ElementAt(1);

    [Fact]
    public void ASlur_StartsOnTheShiftedStem()
    {
        var page = Render("c8[( c)] d[ d d d d d]");
        var slur = Assert.Single(page.Beziers);
        Assert.Equal(1.2392, slur.P0.X - ShiftedHeadX(page), 2);
    }

    [Fact]
    public void ATie_LeavesTheShiftedHead()
    {
        var page = Render("c8[~ c] d[ d d d d d]");
        var tie = Assert.Single(page.Beziers);
        Assert.Equal(1.6392, tie.P0.X - ShiftedHeadX(page), 2);
    }
}
