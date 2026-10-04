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
/// A tie binds equal PITCHES, not equal staff positions (lily/tie-engraver.cc): where an
/// ottava bracket ends between the two heads, the second one is drawn an octave away and the
/// tie still joins them. LilyPond 2.26.0 (Lab sessions/p655, the S1 book tie-ottava): in
/// `c''4@ottava~ c'' d''~ d''@!ottava` both ties are drawn, the second at its first head's
/// height and ending 0.335 short of the octave-shifted head — its end column is built around
/// the head the tie really reaches, whose stem stands in the way. Until session 655 Lily#
/// matched the heads by position and dropped that tie.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OttavaBoundaryTieTests
{
    [Fact]
    public void ATieIntoTheBracketsEnd_IsDrawn_AndStopsShortOfTheShiftedHead()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            part m {
              section A { c''4@ottava~ c'' d''~ d''@!ottava | }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);

        var ties = doc.Page.Beziers.OrderBy(b => b.P0.X).ToList();
        Assert.Equal(2, ties.Count);
        double lastHead = doc.Page.Glyphs
            .Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack)
            .Max(g => g.X);
        Assert.Equal(-0.335, ties[1].P1.X - lastHead, 3);
    }

    /// <summary>…and the bar check reads the same pitch the detector ties: no LYS4007 on the
    /// head the bracket's end shifted. Until session 797 <c>TieTargetScanner</c> compared the
    /// position and the MIDI number by a spelling of its own; it gave the same answer here
    /// (MEASURED: no LYS4007 on four ottava shapes with either spelling, Lab
    /// sessions/p797/ottava), and now it asks <c>TieDetector.SamePitch</c>, so the two cannot
    /// drift — this pins the answer, not a repair.</summary>
    [Fact]
    public void ATieIntoTheBracketsEnd_IsNoPitchMismatch()
    {
        var tree = SyntaxTree.Parse("""
            octave absolute
            part m {
              section A { c''4@ottava~ c'' d''~ d''@!ottava | }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var diags = LilySharp.Core.Semantics.SemanticValidation.Run(tree);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.TieTargetMismatch);
    }
}
