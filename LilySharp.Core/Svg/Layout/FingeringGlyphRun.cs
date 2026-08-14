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

using System.Collections.Immutable;
using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One fingering's digits as the glyph run LilyPond puts on the page: the PROPORTIONAL
/// fetaText cut, out of the optical design a fingering's own size lands on, with each
/// advance hinted to a device pixel the way Pango hints it.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:1540-1568 Fingering — a self-alignment-interface,
///   side-position-interface grob (:1567-1568) whose font is <c>font-encoding fetaText</c>
///   (:1547), <c>font-features ("cv47" "ss01")</c> (:1548) and <c>font-size -5</c> (:1549);
///   its stencil is <c>ly:text-interface::print</c> of <c>fingering::calc-text</c>
///   (:1559-1560), so what is drawn IS this run.
/// <para>
/// ⚠️ IT USED TO BE THE FIGURED BASS'S RUN (<see cref="FiguredBassGlyphRun"/>), shared on the
/// argument that both are fetaText digits at font-size −5. They are not the same digits.
/// BassFigure declares <c>font-features ("tnum" "cv47" "ss01")</c> (:352-356) and a Fingering
/// declares the same list MINUS <c>tnum</c> — tabular figures — so a figure is set in the
/// <c>fattened.fixedwidth.*</c> cut, where every digit is as wide as the widest, and a
/// fingering in the PROPORTIONAL <c>fattened.*</c> cut, where a "7" is 1.304 design-ss and a
/// "4" is 1.656. Read off the page and not deduced: <c>ly:stencil-expr</c> prints the glyph
/// name, and it printed <c>fattened.one</c> for a Fingering against
/// <c>fattened.fixedwidth.one</c> for a BassFigure.
/// </para>
/// <para>
/// ⚠️ AND THE BOX WAS NEVER THE INK. LilyPond builds a TEXT stencil's box as X = the LOGICAL
/// rect and Y = the INK rect (lily/pango-font.cc:358-360
/// Pango_font::pango_item_string_stencil), so the X box runs from the pen
/// origin to the run's ADVANCE — which is the shape Lily# already had. The defect was
/// three-fold and all three are in the advance: the wrong CUT (above), the wrong optical
/// DESIGN (a fingering asks for 20·magstep(−5) = 11.2246 pt, which lands on
/// <c>emmentaler-11</c>, not on the 20 table the shared run read), and NO PIXEL HINTING.
/// </para>
/// <para>
/// ★ THE WHOLE MODEL IS <c>quantise(advance(design) · magstep(−5))</c>, and it is not fitted:
/// checked against LilyPond's own ten digits it reproduces every one to double precision
/// (audit/lp-geometry/probes/fingering-digit-width.ly, ledger <c>fingering.digit-*</c>).
/// </para>
/// <para>
/// ⚠️ ONE HOME for the same reason the figured bass has one: the pen
/// (<c>SharedRenderer.DrawFingerings</c>), the script-column profile
/// (<c>ArticulationEngraver.FingeringScriptLayout</c>), the reservation
/// (<c>SkylineBuilder.AddFingeringsToSkyline</c>) and the placement
/// (<see cref="FingeringEngraver"/>) all ask <see cref="FingeringEngraver.DigitRun"/>, which
/// asks here. The em, the design and the metrics are halves of one claim, and a consumer that
/// picked its own would reserve a box the glyph does not fill.
/// </para>
/// </remarks>
internal static class FingeringGlyphRun
{
    /// <summary>The <c>font-size</c> a Fingering declares, in LilyPond's sixths of an octave.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1540-1568 Fingering, a side-position-interface
    /// grob, declares <c>(font-size . -5)</c> at :1549.</remarks>
    internal const double FontSizeStep = -5.0;

    /// <summary>The em a fingering is drawn at, in staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:99-117 select_font — for fetaText the base size is
    ///   the staff height (4 staff spaces), stepped by <c>2^(font-size/6)</c>.
    /// It comes to the same number as <see cref="EngravingDefaults.FiguredBassFontSize"/>
    /// because both grobs declare −5; they are two declarations of one RULE at one step, not
    /// one quantity with two spellings, and each carries its own grob's citation.
    /// </remarks>
    internal static double Em => 4.0 * EmmentalerDesignSize.Magstep(FontSizeStep);

    /// <summary>
    /// The Emmentaler design a fingering is drawn from — the PEN needs it as well as the
    /// metrics, or the box a column reserves stops being the box the glyph fills.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/font-select.cc:41-70 best_rounded_design_size — 20·magstep(−5)
    /// = 11.2246 pt lands on <c>emmentaler-11</c>.</remarks>
    internal static int Design => EmmentalerDesignSize.ForFontSizeStep(FontSizeStep).Rounded;

    /// <summary>That design's table, already in the PAGE's staff spaces.</summary>
    private static GlyphMetrics.DesignMetrics Font => GlyphMetrics.AtFontSize(FontSizeStep);

    /// <summary>A drawn piece of the run: a music glyph, or a character the music font has no
    /// fingering glyph for, drawn as text.</summary>
    internal readonly record struct Piece(char Ch, double X, double Advance, bool IsGlyph);

    /// <summary>
    /// The run's pieces, left to right, with X relative to the run's left edge — each fed by
    /// its OWN hinted advance.
    /// </summary>
    /// <remarks>
    /// The loop itself is <see cref="FetaTextRun"/>'s, which is where the LilyPond citations
    /// for it live; this file supplies WHICH glyph a character is and what em the fallback is
    /// drawn at.
    /// ⚠️ NO KERN IS PASSED, and that is a statement about the corpus rather than about the
    /// font: the fattened digits DO carry GPOS pairs, but a fingering is a single integer in
    /// every book measured, and the ten one-digit widths this run is pinned to
    /// (<c>fingering.digit-*</c>) agree with LilyPond to double precision without one. A
    /// two-digit fingering is what would open that; it needs a point first.
    /// ⚠️ LILYSHARP-OWN: the fallback branch. A fingering is an integer, so the only way a
    /// non-digit reaches here is a negative number's sign; it is drawn in the serif face at
    /// this em so that a nonsense input still has one metric home rather than none.
    /// </remarks>
    internal static ImmutableArray<Piece> Pieces(string text)
    {
        var run = FetaTextRun.Pieces(text, TryGetDigit, Em);
        var pieces = ImmutableArray.CreateBuilder<Piece>(run.Length);
        foreach (var p in run) pieces.Add(new Piece(p.Ch, p.X, p.Advance, p.IsGlyph));
        return pieces.ToImmutable();
    }

    /// <summary>The run's advance width in staff spaces — the grob's whole X extent, whose
    /// left edge is the pen origin.</summary>
    /// <remarks>LILYPOND-REF: lily/pango-font.cc:358-360 Pango_font::pango_item_string_stencil
    /// — <c>Interval (PANGO_LBEARING
    /// (logical_rect), PANGO_RBEARING (logical_rect))</c>.</remarks>
    internal static double Width(string text) => FetaTextRun.Width(text, TryGetDigit, Em);

    /// <summary>The run's ink above its baseline — the union of its glyphs' outline tops.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pango-font.cc:360 Pango_font::pango_item_string_stencil — the Y half
    ///   of the same box is the INK rect, so
    ///   this end is measured on the outline and NOT hinted to a pixel the way the width is
    ///   (Pango reports the ink rect in its own finer units). The residual that leaves is the
    ///   ~7e-5 the fetaText family already carries; it is named, not fitted.
    /// ⚠️ THE OUTLINE, NOT LILC: a text-path grob never reads LILC
    ///   (lily/modified-font-metric.cc:125-143 Modified_font_metric::text_stencil — the same argument
    ///   <see cref="GlyphMetrics.TryGetDynamicInk"/> carries).
    /// </remarks>
    internal static double InkTop(string text) => FetaTextRun.InkTop(text, TryGetDigit, Em);

    /// <summary>The run's ink BELOW its baseline (≤ 0) — the other end of the same box.</summary>
    /// <remarks>
    /// ⚠️ IT IS NOT ALWAYS ZERO, which is why it is a call and not a 0: nine of the ten digits
    /// sit ON the baseline and the seven hangs 0.004 design-ss below it, which LilyPond dumps
    /// as <c>yext = (-0.002234 . 1.122528)</c> on a fingering "7".
    /// </remarks>
    internal static double InkBottom(string text)
        => FetaTextRun.InkBottom(text, TryGetDigit, Em);

    /// <summary>
    /// The glyph, its outline box and its UNHINTED advance for one digit, all in the page's
    /// staff spaces out of the fingering's own design.
    /// </summary>
    private static bool TryGetDigit(char c, out char glyph, out GlyphMetrics.BBox outline,
        out double advance)
    {
        var f = Font;
        (glyph, outline, advance) = c switch
        {
            '0' => (EmmentalerGlyphs.FingeringDigit0, f.FingeringDigit0Outline, f.FingeringDigit0Advance),
            '1' => (EmmentalerGlyphs.FingeringDigit1, f.FingeringDigit1Outline, f.FingeringDigit1Advance),
            '2' => (EmmentalerGlyphs.FingeringDigit2, f.FingeringDigit2Outline, f.FingeringDigit2Advance),
            '3' => (EmmentalerGlyphs.FingeringDigit3, f.FingeringDigit3Outline, f.FingeringDigit3Advance),
            '4' => (EmmentalerGlyphs.FingeringDigit4, f.FingeringDigit4Outline, f.FingeringDigit4Advance),
            '5' => (EmmentalerGlyphs.FingeringDigit5, f.FingeringDigit5Outline, f.FingeringDigit5Advance),
            '6' => (EmmentalerGlyphs.FingeringDigit6, f.FingeringDigit6Outline, f.FingeringDigit6Advance),
            '7' => (EmmentalerGlyphs.FingeringDigit7, f.FingeringDigit7Outline, f.FingeringDigit7Advance),
            '8' => (EmmentalerGlyphs.FingeringDigit8, f.FingeringDigit8Outline, f.FingeringDigit8Advance),
            '9' => (EmmentalerGlyphs.FingeringDigit9, f.FingeringDigit9Outline, f.FingeringDigit9Advance),
            _ => ('\0', default, 0.0),
        };
        return glyph != '\0';
    }
}
