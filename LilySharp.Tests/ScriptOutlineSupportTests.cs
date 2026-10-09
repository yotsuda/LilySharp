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
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A script stands off its supports by its OUTLINE, measured over each support's own X range:
/// the head over the head's width, the stem (add-stem-support) and the staff across the whole
/// width. One number per script — its box's bottom, and a ±0.5 stand-in box for the ornament
/// family — was wrong for any glyph whose outline dips below its box or away from its centre.
/// LILYPOND-REF: lily/side-position-interface.cc:225-330 Side_position_interface::aligned_side
/// LILYPOND-REF: scm/define-grobs.scm:2994-3006 Script — add-stem-support, vertical-skylines from the stencil
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScriptOutlineSupportTests
{
    /// <summary>MEASURED, LilyPond 2.26.0 (Lab sessions/p695/ns, the lysc-ly twins through
    /// sessions/p694/scriptdump.ily): the Script's origin above the staff's middle line.
    /// <c>c''4</c> is stem-down (the head supports it); <c>g'4</c> is stem-up (the stem's tip,
    /// across the whole width). Until session 695 Lily# read mordent −0.173, turn −0.033 and
    /// trill −0.039 / −0.049 low.</summary>
    [Theory]
    [InlineData("c''4@mordent", 5.4130)]
    [InlineData("g'4@mordent", 3.9130)]
    [InlineData("c''4@turn", 5.2730)]
    [InlineData("g'4@turn", 3.7730)]
    [InlineData("c''4@trill", 4.7890)]
    [InlineData("g'4@trill", 3.2890)]
    [InlineData("c''4@staccatissimo", 4.7970)]
    [InlineData("g'4@staccatissimo", 3.2970)]
    [InlineData("c''4@fermata", 5.0810)]
    [InlineData("g'4@accent", 3.6650)]
    public void AScript_ClearsItsSupportsByItsOutline(string note, double originUp)
    {
        string src = $$"""
            octave absolute
            part m {
              section A { {{note}} r4 r2 | }
            }
            form { A }
            score { staff m }
            """;
        var g = RenderedGeometry.Render(src);
        double middle = g.StaffRefpoints()[0];
        // The script is the highest glyph on the page (a clef's origin is on its G line).
        var script = g.Glyphs.OrderBy(x => x.Y).First();
        Assert.Equal(originUp, middle - script.Y, 0.002);
    }

    /// <summary>MEASURED, LilyPond 2.26.0 (Lab sessions/p696/led2): a staccato forced DOWN
    /// under a high head of the upper voice (stem up, so the stem is not its support) stands
    /// between the head and the staff, where quantize-position still applies to a script on a
    /// note head — and a rounded position on a LEDGER line (8, 10) is pushed a half space
    /// further, as on_line allows ledgers. An odd position (b''', 9.5) stays. Until session 696
    /// the notation staff's copy counted only the five lines (±4) and left 8.5 at 9.0.</summary>
    [Theory]
    [InlineData("a'''4@staccato.down", 8.5)]
    [InlineData("c''''4@staccato.down", 9.5)]
    [InlineData("b'''4@staccato.down", 9.5)]
    public void AQuantizedScriptBetweenItsHeadAndTheStaff_LeavesALedgerPosition(string note, double originUp)
    {
        string src = $$"""
            octave absolute
            part m {
              section A { voice { {{note}} r4 r2 } { c'1 } | }
            }
            form { A }
            score { staff m }
            """;
        var g = RenderedGeometry.Render(src);
        double middle = g.StaffRefpoints()[0];
        var dot = g.Glyphs.Single(x => x.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.ArticStaccatoAbove);
        Assert.Equal(originUp, middle - dot.Y, 0.002);
    }
}
