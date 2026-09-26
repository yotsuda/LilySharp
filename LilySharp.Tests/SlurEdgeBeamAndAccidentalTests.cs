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
/// `b,,8 b,,( dis,)` in bass clef, G major (Butterfly.lys, Lab corpus): the slur leaves the
/// LAST note of an auto-beamed pair and arrives at a sharp. LilyPond 2.26.0 (Lab
/// sessions/p649 bfly, debug-slur-scoring: `L edge=0.80, extra=0.85 TOTAL=1.65 idx=18`)
/// draws it flat — both ends 1.195 above the middle line — over the sharp. Two Lily# defects
/// met here: ⑴ the left end sat on the beam, because "beamed on the inner side" was read off
/// the written `[` `]` marks an automatic beam never carries (lily/slur-scoring.cc:549-554
/// `Stem::get_beaming (stem, -d)`); ⑵ once on the head it ran through the sharp, because no
/// accidental entered the slur's extra objects (lily/slur-engraver.cc:73; slur-scoring.cc
/// :860-877 get_extra_encompass_infos).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SlurEdgeBeamAndAccidentalTests
{
    private const string Book = """
        octave absolute
        key g major
        time 4/4
        part bassline {
          clef bass
          section A { b,,4 b,,8 b,,( dis,) dis, dis, dis, | }
        }
        form main { ~A }
        score main { staff bassline }
        """;

    private static RecordingDrawingContext Render()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    [Fact]
    public void TheSlurLeavesTheHead_AndClearsTheSharp_Flat()
    {
        var page = Render();
        var slur = Assert.Single(page.Beziers);
        // D#3 stands on the bass staff's middle line: its head's Y is the middle line's.
        double middle = page.Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack)
            .OrderBy(g => g.X).Last().Y;
        // LilyPond: both ends 1.195 above it (device Y down), to the 0.01 the bow sweep
        // counts as equal. The sharp still grazes it there — LP's winner pays extra=0.85.
        Assert.Equal(middle - 1.195, slur.P0.Y, 2);
        Assert.Equal(middle - 1.195, slur.P1.Y, 2);
    }
}
