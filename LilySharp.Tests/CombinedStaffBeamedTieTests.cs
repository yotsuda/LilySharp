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
/// audit/lpreg/pcglobal-probe (the twin of input/regression/part-combine-global.ly): in bar 1
/// the combiner splits the parts, so part two's beamed <c>f'8[ f~] f8[ f]</c> is \voiceTwo —
/// stems down — and its tie hangs under the heads. Each part's collector had baked its beams'
/// pure stem tip at the direction the beam took from the pitches (up, for f'), and the
/// combiner turned the stems without baking it again. The tie's chord outline boxes the stem
/// from that tip, so the next f's down stem stood ABOVE its head there, nothing held the tie's
/// right end off it, and the tie ended 0.787 right of LilyPond's. LilyPond 2.26.0 (Lab
/// sessions/p651 pcg, bow dump): the tie ends 0.335 before the next head's left edge
/// (21.698962 against 22.033962), as the same music written as `voice { } { }` does.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CombinedStaffBeamedTieTests
{
    [Fact]
    public void VoiceTwosTie_StopsShortOfItsNextDownStem()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 2/4
            part vone { clef treble }
            part vtwo { clef treble }
            section A {
              vone { a8[ a] a8[ a] | a8[ a] a8[ a] | }
              vtwo { f8[ f~] f8[ f] | f8[ f] f8[ f] | }
            }
            form main { ~A }
            score main { combinedStaff { vone vtwo } }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        var tie = Assert.Single(doc.Page.Beziers);
        // The tie's right head: the first head whose left edge is past the tie's start.
        double nextHeadLeft = doc.Page.Glyphs
            .Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack && g.X > tie.P0.X)
            .Min(g => g.X);
        Assert.Equal(-0.335, tie.P1.X - nextHeadLeft, 3);
    }
}
