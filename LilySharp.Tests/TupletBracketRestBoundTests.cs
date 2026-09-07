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
    internal static (double[] Line, double InkTop) Bracket(string source)
    {
        var g = RenderedGeometry.Render(source);
        double middle = g.StaffRefpoints()[0];
        var strokes = g.Lines
            .Where(l => Math.Abs(l.StrokeWidth - 0.16) < 1e-9)
            .ToList();
        Assert.NotEmpty(strokes);

        // The bracket LINE, as its two ENDS — not as its two drawn segments' left corners:
        // the number splits a sloped bracket into two pieces, and the rise between the pieces
        // is not the bracket's own dy.
        var pieces = strokes
            .Where(l => Math.Abs(l.X1 - l.X2) > 1e-9)          // the horizontal segments
            .OrderBy(l => Math.Min(l.X1, l.X2))
            .ToList();
        Assert.Equal(2, pieces.Count);                          // split by the number
        var first = pieces[0];
        var last = pieces[^1];
        double leftY = first.X1 <= first.X2 ? first.Y1 : first.Y2;
        double rightY = last.X1 >= last.X2 ? last.Y1 : last.Y2;
        double[] line = { middle - leftY, middle - rightY };
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

    // A manual beam that starts OUTSIDE the tuplet and runs over its bounding rest. LilyPond
    // follows it (the rest's stem carries it), so the bracket slopes with the beam.
    private const string BeamOverTheBoundingRest = """
        octave absolute
        part melody { instrument bass
          section A { c,16[ tuplet 3/4 { r16 c a,\2 ] } g,4\2 a,,4 r8. | }
        }
        form main { A }
        score main { staff melody }
        """;

    // A GrandStaff whose UPPER tuplet is three unbeamed quarters while the LOWER staff of the
    // same measure carries a beam. LilyPond's par_beam lives on the tuplet's own columns, so
    // there is none here at all.
    // The music is LilySharp.Tests/Fixtures/test/multistaff-tuplet-beams.lys, which is the
    // book the LilyPond twin below was taken from.
    private const string OtherStaffHasTheBeam = """
        octave absolute
        part rh { clef treble }
        part lh { clef bass }
        section S {
          rh { tuplet 3/2 { c''4 c'' c'' } c''2 | }
          lh { c8 d e f g a b c' | }
        }
        form main { S }
        score main { grandStaff { staff rh staff lh } }
        """;

    /// <summary>
    /// A beam that runs over the tuplet's bounding rest IS the tuplet's parallel beam, so the
    /// bracket follows it and slopes — LilyPond's follow-beam arm takes the outer COLUMNS'
    /// stem tips, the rest's invisible one included.
    /// </summary>
    /// <remarks>
    /// LP 2.26.0 (scratch/p345/e1-probe.ly): <c>beam=&lt;Beam&gt;</c>,
    /// <c>positions=(-4.315073 . -3.271576)</c> — a rise of 1.043497 over the bracket. Lily#
    /// drew it FLAT at -3.730 until 2026-09-07: the follow arm was running the general arm's
    /// musical sign gates, and the beam rises where the heads descend, so the gate zeroed the
    /// slope. LILYPOND-REF: lily/tuplet-bracket.cc:495-519 calc_position_and_height — the
    ///   gates and the damping are in the ELSE arm (:520-631), not this one.
    /// </remarks>
    [Fact]
    public void ABeamOverTheBoundingRest_SlopesTheBracketWithIt_LpExact()
    {
        var (line, _) = Bracket(BeamOverTheBoundingRest);
        Assert.Equal(1.043497, line[1] - line[0], precision: 6);
    }

    /// <summary>
    /// …and a beam on ANOTHER STAFF is never the tuplet's, however well it covers the same
    /// item range: LilyPond reads the beam off the tuplet's own columns' stems.
    /// </summary>
    /// <remarks>
    /// LP 2.26.0 (scratch/p345/mtb-probe.ly): the bracket's ink is (-3.48 . -2.62) about the
    /// upper staff's middle line, i.e. the flat staff-driven <c>positions</c> -3.4 — staff ink
    /// 2.05 widened by staff-padding 0.25, plus padding 1.1. Lily# read -1.500 while it was
    /// following the LEFT hand's beam.
    /// </remarks>
    [Fact]
    public void ATupletDoesNotFollowAnotherStaffsBeam_LpExact()
    {
        var (line, _) = Bracket(OtherStaffHasTheBeam);
        Assert.Equal(line[0], line[1], precision: 9);
        Assert.Equal(-3.4, line[0], precision: 6);
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

    // A manual beam that OPENS on the tuplet's bounding rest and closes on its last note: the
    // beam's bounds are the tuplet's, rest included.
    private const string BeamOpensOnTheBoundingRest = """
        octave absolute
        part melody {
          section A { tuplet 3/2 { r8[ c c] } c4 c4 r4 | }
        }
        form main { A }
        score main { staff melody }
        """;

    /// <summary>
    /// A beam bracketed onto the tuplet's bounding rest is bound to that rest's column, so it
    /// is EQUALLY LONG and hides the bracket — and the number then centres on the bracket's
    /// X span, which starts at the rest's ink, at the bracket's own height.
    /// </summary>
    /// <remarks>
    /// LP 2.26.0 (scratch/p346/hid-probe.ly): TupletBracket extent <c>(+inf.0 . -inf.0)</c>,
    /// <c>X-positions=(0.0 . 6.0084)</c> from relX 8.585 = the rest's ink left,
    /// <c>positions=(1.5 . 1.5)</c>, TupletNumber x extent (11.1112 . 12.0672) — centre 11.589
    /// = relX + 3.0042. Lily# drew the bracket until 2026-09-07 (its beam bounds were read
    /// from the note members alone, so the rest-bound beam looked shorter than the tuplet).
    /// </remarks>
    [Fact]
    public void ABeamOpeningOnTheBoundingRest_IsEquallyLong_AndHidesTheBracket_LpExact()
    {
        var g = RenderedGeometry.Render(BeamOpensOnTheBoundingRest);
        double middle = g.StaffRefpoints()[0];
        var strokes = g.Lines.Where(l => Math.Abs(l.StrokeWidth - 0.16) < 1e-9).ToList();
        Assert.True(strokes.Count == 0,
            $"{strokes.Count} bracket stroke(s) drawn; LilyPond hides this bracket "
            + "(scratch/p346/hid-probe.ly: TupletBracket extent (+inf.0 . -inf.0)).");

        var rest = Assert.Single(g.Glyphs.Where(q => q.Glyph == EmmentalerGlyphs.Rest8th));
        var number = Assert.Single(g.Texts.Where(t => t.Text == "3"));
        // The rest's ink left is its glyph origin (Rest8th.Left = 0); the number's centre is
        // half the bracket's X span from it.
        Assert.Equal(6.0084 / 2.0, number.X - (rest.X + GlyphMetrics.GetRestBBox(8).Left), 3);
        Assert.Equal(1.5, middle - number.Y, 6);
    }
}
