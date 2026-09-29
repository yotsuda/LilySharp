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
using LilySharp.Core.Rendering;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A Script on a TAB staff is placed by LilyPond's aligned_side read in the tab's own
/// geometry: its staff-space is the string gap (1.5), so <c>padding</c> and
/// <c>staff-padding</c> are multiplied by it, the support is the digit on the script's side
/// and the stem when it travels the script's way, and a quantized script rounds to the tab's
/// half-spaces.
/// LILYPOND-REF: lily/side-position-interface.cc:188-456 Side_position_interface::aligned_side
/// </summary>
/// <remarks>
/// The books set the fret digits to LilyPond's height (<c>fonts { tab size 1.6343 }</c>: the
/// bundled bold digit inks 0.722 of its size, LilyPond's TabNoteHead is 1.18 tall), so a
/// digit-supported script can be held to LilyPond's number; Lily#'s default larger digit
/// moves exactly those scripts out by the difference, which is the owner's decision
/// (2026-09-30), not a defect. Single notes only — a beamed stem's end follows the tab beam,
/// whose height is another decision (session 569's uniformBeamedLength).
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabScriptAlignedSideTests
{
    /// <summary>MEASURED, LilyPond 2.26.0 (Lab sessions/p694/ts3, the lysc-ly twins through
    /// sessions/p694/scriptdump.ily): the Script's origin above (+) or below (−) the tab
    /// staff's middle line. Until session 694 Lily# put the glyph centre a flat 1.0 from the
    /// digit, the stem tip or the staff edge: 5.50 / 5.50 / −2.83 / 4.34 / −4.33 / 4.34.</summary>
    [Theory]
    [InlineData("bes,,4@fermata", 5.176)]    // above, the up-stem in the support (padding 0.4 × 1.5)
    [InlineData("bes,,4@staccato", -3.000)]  // below the A string, quantized onto a space
    [InlineData("g4@staccato", 3.340)]       // above the top digit: past the staff span, not quantized
    [InlineData("e,,4@staccato", -3.352)]    // below the bottom digit
    [InlineData("g4@fermata", 3.516)]        // above the top digit, the stem pointing away
    public void ATabScript_StandsWhereLilyPondsAlignedSidePutsIt(string note, double originUp)
    {
        string src = $$"""
            fonts { tab size 1.6343 }
            octave absolute
            part cb {
              instrument bass
              section A { {{note}} r4 r2 | }
            }
            form main { A }
            score main { tab cb }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        var page = doc.Page;

        // The tab staff: the four string lines, a string gap apart — each broken around the
        // digits on it, so a line is the reach of all the pieces at one Y.
        var rows = page.Lines
            .Where(l => System.Math.Abs(l.Y1 - l.Y2) < 1e-9)
            .GroupBy(l => System.Math.Round(l.Y1, 6))
            .Where(g => g.Max(l => System.Math.Max(l.X1, l.X2)) - g.Min(l => System.Math.Min(l.X1, l.X2)) > 10.0)
            .Select(g => g.Key).OrderBy(y => y).ToList();
        Assert.Equal(4, rows.Count);
        double middle = (rows[0] + rows[^1]) / 2;

        var script = page.Glyphs.Single(g => g.Glyph is EmmentalerGlyphs.FermataAbove
                                              or EmmentalerGlyphs.ArticStaccatoAbove);
        Assert.Equal(originUp, middle - script.Y, 0.015);
    }
}
