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

using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>voice { } { }</c> span that opens MID-BAR forces \voiceOne only on the part of the bar
/// it covers. The music before it is in the surrounding Voice context, which LilyPond leaves
/// unforced (scm/music-functions.scm:1042-1057 voicify-sublist): in
/// <c>c8( d) e4~ e8 voice { f8 g4 } { a,8 b,4 }</c> LilyPond 2.26.0 hangs the slur and the tie
/// UNDER the stem-up notes (Lab sessions/p652 midspan, bow dump: both dir=-1). Until session
/// 652 the forcing was asked per MEASURE, so both were pinned over them — SUMMER.lys (Lab
/// corpus), last bar. The span's start in the bar is the collector's padding in front of the
/// later voice (RestItem.IsSpanLead).
/// </summary>
[Trait("Category", "Unit")]
public sealed class MidBarVoiceSpanTests
{
    [Fact]
    public void BowsBeforeAMidBarSpan_AreNotForcedByTheVoice()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            part melody {
              section A { c8( d) e4~ e8 voice { f8 g4 } { a,8 b,4 } }
            }
            form main { A }
            score main { staff melody }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        Assert.Equal(2, doc.Page.Beziers.Count);
        // Device Y grows downward: a bow hanging under its ends has its middle BELOW them.
        foreach (var bow in doc.Page.Beziers)
            Assert.True(bow.Centreline1.Y > bow.P0.Y,
                $"bow from x={bow.P0.X:F3} curves up; LilyPond hangs it under the notes");
    }

    [Fact]
    public void AWrittenLeadingSpacer_DoesNotMoveTheSpansStart()
    {
        // `{ s8 … }` is music the writer put in the block, not the collector's padding: the
        // span still starts with the bar, and voice one's first note is \voiceOne (stem up)
        // even though its pitch alone would turn the stem down.
        var tree = SyntaxTree.Parse("""
            octave absolute
            part melody {
              section A { voice { c8( d) e4 f2 } { s8 a,8 b,4 a,2 } }
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
        Assert.True(slur.Centreline1.Y < slur.P0.Y, "voice one's slur is forced up by the span");
    }
}
