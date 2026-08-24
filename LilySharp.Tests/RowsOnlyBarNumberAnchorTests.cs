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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What a continuation bar number HANGS ON when the system has no staff at all.
/// </summary>
/// <remarks>
/// <para>
/// LilyPond re-parents a bar number onto the topmost element of the system's
/// VerticalAlignment whose X extent — widened by 1.0 — overlaps the number's own, and that
/// walk tests neither <c>is_spaceable</c> nor for a StaffSymbol: chord rows and lyric rows
/// are candidates like any staff. A staff normally wins only because a StaffSymbol's line
/// spans the system from x≈0 while the first chord name starts after the clef.
/// LILYPOND-REF: lily/staff-grouper-interface.cc get_extremal_staff — the walk itself.
/// </para>
/// <para>
/// A Lily# lead sheet has no staff, so <c>BarNumberEngraver.AnchorStaff</c> answered null
/// and the number was measured from the SYSTEM TOP, a whole band above where it belongs.
/// The row that reaches its column is the GRID row — the one that opens each system with a
/// barline — and that is what it hangs on now (user decision 2026-08-24, taken against the
/// rendered picture of <c>samples/drunken-sailor.lys</c>).
/// </para>
/// <para>
/// ⚠️ THE MEASUREMENT HERE IS THE BAND TOP, NOT THE SYSTEM TOP, and that is the whole point:
/// the two differ by exactly the chord row that leads the system, so a test written against
/// the system top would pass whichever anchor the code used. Reverting the anchor moves the
/// answer from ~1.02 to ~4.2 — verified by poisoning, not assumed.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RowsOnlyBarNumberAnchorTests
{
    // The reported shape: a chord row above a two-verse lyric row, no staff, and a form
    // that produces a second system so there is a CONTINUATION number to place ("6").
    // Same music as RowsOnlySystemGapTests' Head — deliberately, so the two tests speak
    // about one book: that one guards the gap BETWEEN the systems, this one guards what
    // the second system's number hangs on inside it.
    private const string Head = """
        title "T"
        time 4/4
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }
          section B { g'4 g f f | e e d2 | }
        }
        chords prog {
          section A { C | F | G | C }
          section B { Am | G }
        }
        lyrics verse {
          section A { one two | three four | five six | sev- en | }
          section B { [~1. eight nine | ten e- ]
            [~2. le- ven | twelve thir- ] }
        }
        form main { A |: B :| A "A2" }
        """;

    private static string Render(string scoreBody)
    {
        var tree = SyntaxTree.Parse($"{Head}\nscore main {{\n{scoreBody}\n}}\n");
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    /// <summary>Each system's grid band: the vertical span of its barlines (a narrow, tall
    /// rect), top-to-bottom in page order. The band's TOP is the grid row's top, which is
    /// the datum the number is placed from.</summary>
    private static List<double> BandTops(string svg)
        => Regex.Matches(svg,
                "<rect[^>]*x=\"([0-9.-]+)\"[^>]*y=\"([0-9.-]+)\"[^>]*width=\"([0-9.-]+)\"[^>]*height=\"([0-9.-]+)\"")
            .Select(m => (Y: double.Parse(m.Groups[2].Value),
                          W: double.Parse(m.Groups[3].Value),
                          H: double.Parse(m.Groups[4].Value)))
            .Where(r => r.W < 1 && r.H > 2)
            .Select(r => r.Y)
            .Distinct().OrderBy(y => y).ToList();

    /// <summary>The continuation bar number: the only right-anchored numeric text.</summary>
    private static double BarNumberBaseline(string svg)
    {
        var m = Regex.Match(svg,
            "<text[^>]*y=\"([0-9.-]+)\"[^>]*text-anchor=\"end\"[^>]*>([0-9]+)</text>");
        Assert.True(m.Success, "no right-anchored bar number in the SVG");
        return double.Parse(m.Groups[1].Value);
    }

    // The number's INK BOTTOM sits `padding` = 1.0 above whatever it hangs on
    // (LILYPOND-REF: scm/define-grobs.scm BarNumber padding), and its BASELINE is that plus
    // the digit's own overshoot below the baseline — per string, and small: a round digit
    // measures 0.024446 against LilyPond's own dump and a "1" measures nothing. So the
    // baseline-to-datum distance is padding..padding + a tenth, and the window says so
    // rather than pinning a face-dependent digit.
    private const double Padding = 1.0;
    private const double MaxDigitOvershoot = 0.1;

    /// <summary>
    /// ONE INVARIANT, TWO BOOKS: a continuation bar number's baseline sits padding (plus its
    /// own small overshoot) above the TOP OF WHATEVER CARRIES THE SYSTEM'S BARLINES — the
    /// staff when there is one, the grid row when there is not.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE CONTROL IS NOT A NEGATIVE ONE, AND THE FIRST DRAFT GOT THAT WRONG. It asserted
    /// that a staffful book must NOT give this answer, and the book gave it anyway — because
    /// the tall narrow rect a staffful system draws IS the staff, and a staff's barline top
    /// IS its top line, the datum session 220 already anchored on. The two halves are the
    /// same expression applied to whichever element plays the staff's part, so the honest
    /// shape is one claim measured twice, not a claim and its negation.
    /// <para>
    /// ⚠️ AND THE CONTROL IS THE HALF THAT WAS ALWAYS RIGHT, which is what makes the pair
    /// readable when it breaks: poison the anchor and only the STAFFLESS row goes red, so
    /// the failure names which half it guards. Both rows read the same window, and the
    /// window is not the system top — on this book the system top is a whole chord row
    /// (3.1) above the band, far outside it, which is why reverting the anchor moves the
    /// staffless answer from ~1.02 to ~4.2.
    /// </para>
    /// </remarks>
    [Theory]
    // The reported shape: no staff at all — the half this island added.
    [InlineData("  chords prog as names\n  lyrics verse sings melody")]
    // …and the control that was always right, so the pair says which half it is guarding.
    [InlineData("  staff melody\n  chords prog as names\n  lyrics verse sings melody")]
    public void TheNumberIsPlacedFromWhateverCarriesTheBarlines(string scoreBody)
    {
        var svg = Render(scoreBody);
        var bandTops = BandTops(svg);
        Assert.True(bandTops.Count >= 2, $"expected two systems, found {bandTops.Count}");

        double gap = bandTops[1] - BarNumberBaseline(svg);
        Assert.InRange(gap, Padding, Padding + MaxDigitOvershoot);
    }

    /// <summary>
    /// And the datum is NOT the system top: the book is built so the two differ, because a
    /// chord row leads the system. Without this the window above would be satisfied by a
    /// system whose top happened to coincide with its band.
    /// </summary>
    [Fact]
    public void TheBandTopAndTheSystemTop_AreDifferentPlacesInThisBook()
    {
        var svg = Render("  chords prog as names\n  lyrics verse sings melody");
        var bandTops = BandTops(svg);
        Assert.True(bandTops.Count >= 2, $"expected two systems, found {bandTops.Count}");

        // The chord row leads the system, so its symbols are printed ABOVE the band that
        // holds the barlines. Their baseline standing clear of the band top is what says
        // the two data are separated at all — and by more than the window's width.
        double chordBaseline = Regex.Matches(svg, "<text[^>]*y=\"([0-9.-]+)\"[^>]*>Am</text>")
            .Select(m => double.Parse(m.Groups[1].Value)).Min();
        Assert.True(bandTops[1] - chordBaseline > Padding + MaxDigitOvershoot,
            $"band top {bandTops[1]:F4} is only {bandTops[1] - chordBaseline:F4} below the "
            + "chord row, so this book can no longer tell the two anchors apart");
    }
}
