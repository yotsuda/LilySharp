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
/// A slur broken across a line break pushes the staves apart on BOTH lines: each piece is in
/// the staff's skyline. Until session 660 the per-system staff skylines laid out one system
/// alone and dropped every slur whose other end was on another line, so a slur climbing out of
/// the lower staff ran through the upper one (HANDOFF ⒳¹³⑷, which only guessed at this).
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, Lab sessions/p660/xs/xsys3.lys, the bow sweep's staff dump): the
/// staves stand 11.045 apart on the first line and 14.281 on the second; Lily# had 10.095 and
/// 12.595. LILYPOND-REF: lily/spanner.cc:124-137 Spanner::do_break_processing — every broken piece is a clone of its own.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class BrokenSlurStaffSpacingTests
{
    [Fact]
    public void ASlurOverALineBreak_KeepsTheStavesApartOnBothLines()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            part up { clef treble }
            part lo { clef treble }
            section A {
              up { g1 | g1 | g1 | g1 | break g1 | g1 | g1 | g1 | }
              lo { c''1 | c''1 | c''1 | c''2 c''4( a'' | break c''' d''' e''' f''' | c''1) | c''1 | c''1 | }
            }
            form { A }
            score {
              grandStaff {
                staff up
                staff lo
              }
            }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        var staves = TwinBeamSweep.StavesOf(doc.Page);
        Assert.Equal(4, staves.Count);

        Assert.Equal(11.045, staves[1].Middle - staves[0].Middle, 0.05);   // the first piece
        Assert.Equal(14.281, staves[3].Middle - staves[2].Middle, 0.05);   // the continued piece
    }
    /// <summary>
    /// The same for a tie: a tie climbing out of the lower staff over a line break keeps the
    /// staves apart on both lines. MEASURED (LilyPond 2.26.0, Lab sessions/p660/xs/xtie.lys):
    /// 13.329 on the first line, 13.025 on the second; Lily# had 12.595 on both.
    /// </summary>
    [Fact]
    public void ATieOverALineBreak_KeepsTheStavesApartOnBothLines()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            part up { clef treble }
            part lo { clef treble }
            section A {
              up { g1 | g1 | g1 | g1 | break g1 | g1 | g1 | g1 | }
              lo { c''1 | c''1 | c''1 | f'''1~ | break f'''1 | c''1 | c''1 | c''1 | }
            }
            form { A }
            score {
              grandStaff {
                staff up
                staff lo
              }
            }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        var staves = TwinBeamSweep.StavesOf(doc.Page);
        Assert.Equal(4, staves.Count);

        Assert.Equal(13.329, staves[1].Middle - staves[0].Middle, 0.05);   // the tie's start
        Assert.Equal(13.025, staves[3].Middle - staves[2].Middle, 0.05);   // its continuation
    }
}
