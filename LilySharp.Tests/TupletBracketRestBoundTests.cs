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
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A REST AT A TUPLET'S BOUND LEAVES THE BRACKET WITH NO PARALLEL BEAM TO FOLLOW, so the
/// staff joins the encompass points and the bracket comes out flat, one padding clear of the
/// staff — even though a beam does cover every note inside the tuplet.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: scm/output-lib.scm:3945-3968 <c>ly:tuplet-bracket::calc-potential-beam</c> —
///   <c>TupletBracket.beam</c> is answered only when the tuplet's FIRST and LAST note columns
///   both carry a stem and both stems are on one beam; a Rest column has no stem.
/// LILYPOND-REF: lily/tuplet-bracket.cc:491-492 <c>follow_beam</c>; :633-637 — the staff edge
///   joins the points <c>if (!follow_beam)</c>; :708-726 the offset pass and padding 1.1.
/// </para>
/// <para>
/// LP 2.26.0 ORACLE (scratch/p345/m4-probe.ly, the twin of the reported book's bar 4):
/// <c>beam=#f</c>, <c>positions=(-3.4 . -3.4)</c>, <c>edge-height=(0.7 . 0.7)</c>, bracket ink
/// Y (-3.48 . -2.62) and Rest ink Y (-2.05 . 0.82), all about the middle line. Staff ink 2.05
/// widened by <c>staff-padding</c> 0.25 is 2.30, and 2.30 + <c>padding</c> 1.1 = 3.40.
/// </para>
/// <para>
/// ⚠️ THE BOOK IS THE READER'S (scratch/ベースタブLy/rest-tuplet.lys bar 4, reported
/// 2026-09-07: "the tuplet's brace and the 16th rest overlap"). Lily# read the beam over the
/// two notes as the tuplet's own, followed it, and drew a SLOPED bracket at -1.383 .. -2.149 —
/// straight through the rest, whose ink reaches -2.05.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TupletBracketRestBoundTests
{
    // Bar 4 of the reported book, alone. Every note names its string, as the book does.
    private const string LeadingRest = """
        octave absolute
        part melody {
          instrument bass
          section A { d,4\3 tuplet 3/4 { r16 c a,\2 } g,4\2 a,,4 | }
        }
        form main { A }
        score main { staff melody }
        """;

    // THE CONTROL: the same tuplet with the rest replaced by a NOTE. Now the beam DOES reach
    // both bounds, so it is the bracket's parallel beam AND equally long — and LilyPond draws
    // no bracket at all. That is what says the flat bracket above is not there for want of a
    // beam: the beam is right there, and only the rest at the bound stops it counting.
    // LP 2.26.0 (scratch/p345/m4ctl.ly): TupletBracket extent (+inf.0 . -inf.0) — empty.
    private const string NoLeadingRest = """
        octave absolute
        part melody {
          instrument bass
          section A { d,4\3 tuplet 3/4 { e16 c a,\2 } g,4\2 a,,4 | }
        }
        form main { A }
        score main { staff melody }
        """;

    /// <summary>
    /// The DRAWN bracket, in staff-middle spaces (Y-up): the horizontal line's two segments
    /// and the topmost ink of its end hooks.
    /// </summary>
    /// <remarks>
    /// ⚠️ Read off the recorder, and identified by THICKNESS. A tuplet bracket is drawn at
    /// <c>TupletBracket.thickness</c> 1.6 × line-thickness 0.1 = 0.16, which nothing else on
    /// these books is — <see cref="RenderedGeometry"/>'s own beam reader documents the same
    /// separation from the other side (a stem is EngravingDefaults.StemThickness thick).
    /// </remarks>
    private static (double[] Line, double InkTop) Bracket(string source)
    {
        var g = RenderedGeometry.Render(source);
        double middle = g.StaffRefpoints()[0];
        var strokes = g.Lines
            .Where(l => Math.Abs(l.StrokeWidth - 0.16) < 1e-9)
            .ToList();
        Assert.NotEmpty(strokes);

        var line = strokes
            .Where(l => Math.Abs(l.X1 - l.X2) > 1e-9)          // the horizontal segments
            .Select(l => middle - l.Y1)
            .ToArray();
        Assert.Equal(2, line.Length);                           // split by the number
        double inkTop = strokes.Max(l => middle - Math.Min(l.Y1, l.Y2));
        return (line, inkTop);
    }

    [Fact]
    public void ARestAtTheTupletsBound_LeavesTheBracketFlatOnTheStaff_LpExact()
    {
        var (line, _) = Bracket(LeadingRest);

        Assert.Equal(line[0], line[1], precision: 9);
        Assert.Equal(-3.4, line[0], precision: 6);
    }

    /// <summary>
    /// …and that is what keeps the bracket off the rest: its topmost ink — the end hooks,
    /// <c>edge-height</c> 0.7 back towards the staff — stays below the rest's own ink bottom.
    /// </summary>
    /// <remarks>
    /// The rest's ink is read from the glyph, not from a literal: LilyPond has no rest-size
    /// constant, a Rest's extent IS its stencil's (lily/rest.cc:229-257), and the same is true
    /// here. The r16 sits on the middle line, so its ink bottom is the box's — asserted, not
    /// assumed, off the drawn glyph's own origin.
    /// </remarks>
    [Fact]
    public void TheBracketDoesNotRunThroughTheRest()
    {
        var (_, inkTop) = Bracket(LeadingRest);

        var g = RenderedGeometry.Render(LeadingRest);
        double middle = g.StaffRefpoints()[0];
        var rest = Assert.Single(g.Glyphs.Where(q => q.Glyph == EmmentalerGlyphs.Rest16th));
        double restInkBottom = (middle - rest.Y) + GlyphMetrics.GetRestBBox(16).Bottom;

        Assert.True(inkTop < restInkBottom,
            $"the bracket's ink reaches {inkTop:F6} above the middle line and the r16's ink "
            + $"bottom is {restInkBottom:F6} — the two overlap, which is the reported defect.");
    }

    /// <summary>
    /// THE CONTROL. Replace the rest with a note and the same beam becomes the tuplet's own
    /// AND equally long, so no bracket is drawn at all — which is what says the beam was
    /// always there and the rest is the only thing keeping the bracket off it.
    /// </summary>
    [Fact]
    public void WithNoRestAtEitherBound_TheBeamCountsAgain_AndHidesTheBracket()
    {
        var g = RenderedGeometry.Render(NoLeadingRest);
        var strokes = g.Lines.Where(l => Math.Abs(l.StrokeWidth - 0.16) < 1e-9).ToList();

        Assert.True(strokes.Count == 0,
            $"the control book drew {strokes.Count} bracket stroke(s); with no rest at either "
            + "bound the beam is the tuplet's own and equally long, so LilyPond draws none "
            + "(scratch/p345/m4ctl.ly: TupletBracket extent (+inf.0 . -inf.0)) — and if a "
            + "bracket appears here, the beam is not being found and the pair proves nothing.");
    }
}
