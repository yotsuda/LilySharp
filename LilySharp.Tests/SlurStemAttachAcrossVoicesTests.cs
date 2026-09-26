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
/// `voice { g''2( g8) … } { e8 d e e … }` (test/dot-cross-voice-spacing, reduced to two
/// voices; Lab sessions/p651 two.lys): voice one's unbeamed half note shares its item index
/// with voice two's beamed eighth. The slur's stem-attachment rule reads the edge stem's Y
/// extent (lily/slur-scoring.cc:742-752): the candidate above the head lies inside the up
/// stem, so LilyPond 2.26.0 starts the slur at the stem's right face + 0.3 — 1.6774 right of
/// the head's left edge. Until session 651 the slur pass looked beams up by (measure, item)
/// alone, found voice two's DOWN beam as voice one's stem tip, missed the stem and started at
/// the stem's centre, 0.365 left of LilyPond's.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SlurStemAttachAcrossVoicesTests
{
    [Fact]
    public void VoiceOnesSlur_AttachesToItsOwnStem_NotToVoiceTwosBeam()
    {
        var tree = SyntaxTree.Parse("""
            part melody {
              section A { voice { g''2( g8) eis fis g } { e8 d e e e fis r4 } }
            }
            form main { A }
            score main { staff melody }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        var slur = Assert.Single(doc.Page.Beziers);
        double halfHeadLeft = Assert.Single(
            doc.Page.Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.NoteheadHalf)).X;
        // LilyPond (the p651 twin's bow dump): 18.798227 − 17.120827 = 1.6774 = the half
        // head's width 1.3774 (the up stem's right face) + 0.3.
        Assert.Equal(1.6774, slur.P0.X - halfHeadLeft, 3);
    }
}
