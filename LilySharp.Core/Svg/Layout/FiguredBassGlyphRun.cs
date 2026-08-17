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
/// One bass figure as a run of drawn pieces — the glyphs LilyPond's <c>\number</c> markup
/// puts on the page, at the em <see cref="EngravingDefaults.FiguredBassFontSize"/> sets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/translation-functions.scm:349-470 <c>format-bass-figure</c> — a figure
/// is <c>\number</c> markup of the digit, with the alteration put adjacent to it, all wrapped
/// in <c>(make-fontsize-markup -5 …)</c>. LILYPOND-REF: scm/define-grobs.scm:352-356 BassFigure (bass-figure-interface at :359) —
/// <c>ly:text-interface::print</c> with
/// <c>grob::always-Y-extent-from-stencil</c>, so the grob's extent IS this run's ink.
/// <para>
/// ⚠️ ONE HOME for a reason (HANDOFF §5.0): the em and the metrics are two halves of one
/// claim, and the reservation (<see cref="FiguredBassEngraver"/>) and the drawing
/// (<c>SharedRenderer.DrawFiguredBass</c>) must read the same halves. Before 2026-07-30 they
/// did not — the drawing used a serif face at 3.0 ss and the reservation a nominal 1.5 cap —
/// and the gap between them was the <c>+0.375204764</c> under every figured-bass ledger point
/// plus digits that printed 0.112 through their own stem.
/// </para>
/// <para>
/// ⚠️ NAMED RESIDUAL, not fitted away — AND ITS FIRST NAME WAS WRONG. It was recorded here
/// as an optical-size difference (a figure is 11.2246pt, so LilyPond draws it from
/// <c>emmentaler-11</c>, whose digits were said to be 2.004 design-ss against the 20's
/// 2.000). ⚠️ THAT IS FALSIFIED: all eight designs are bundled and extracted now, and their
/// <c>fattened.fixedwidth</c> digits share one cap — 2.000000, with 2.004000 for the four and
/// 2.016000 for the one — in every table. The designs differ in WIDTH, not in height.
/// </para>
/// <para>
/// WHAT THE RESIDUAL ACTUALLY IS, measured 2026-08-03: LilyPond's per-digit ink at font-size
/// −5 is 2.000117 design-ss for 6/7/8, 2.004157 for 4 AND FOR 5, and 2.023843 for 1 — while
/// the five's own outline is 2.000000. The ledger books' top figure is a 5, so the whole
/// −0.002333 is that one digit. HOW LilyPond arrives at it is the open question: a figure is
/// the TEXT path (Pango over FreeType), so its extent may carry hinting the raw outline bbox
/// does not, and LilyPond reports one X extent for every digit where the inks differ.
/// audit/lp-geometry <c>figbass.alone.staff-to-baseline</c> carries the account and is the
/// only address for it.
/// </para>
/// <para>
/// ⚠️ NOT PORTED HERE: LilyPond puts the alteration on the LEFT of the digit by default
/// (:446-455, <c>alt-dir</c> defaulting to <c>LEFT</c>) with a 0.1 pad, where Lily#'s
/// <c>FiguredBassFigure.DisplayText</c> spells it after the digit and pads nothing. No
/// ledger point watches the X of a figure yet, so the order stays as it was rather than
/// moving output no observer can check.
/// </para>
/// </remarks>
internal static class FiguredBassGlyphRun
{
    /// <summary>A piece of the run: a music glyph, or a character the music font has no
    /// bass-figure glyph for (Lily#'s continuation dash) drawn as text.</summary>
    internal readonly record struct Piece(char Ch, double X, double Advance, bool IsGlyph);

    /// <summary>The em a figure is drawn at, in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: lily/font-select.cc:99-117 select_font over
    /// scm/translation-functions.scm:468-470 format-bass-figure — see
    /// <see cref="EngravingDefaults.FiguredBassFontSize"/>, where the derivation lives.</remarks>
    internal static double Em => EngravingDefaults.FiguredBassFontSize;

    /// <summary>The <c>font-size</c> a figure is set at, in LilyPond's sixths of an octave.</summary>
    /// <remarks>LILYPOND-REF: scm/translation-functions.scm:468-470 format-bass-figure —
    /// <c>(make-fontsize-markup -5 fig-markup)</c>.</remarks>
    internal const double FontSizeStep = -5.0;

    /// <summary>
    /// The Emmentaler design a figure is drawn from — the PEN needs it as well as the metrics.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/font-select.cc:41-70 best_rounded_design_size — 20·magstep(−5)
    /// = 11.2246 pt lands on <c>emmentaler-11</c>.</remarks>
    internal static int Design => EmmentalerDesignSize.ForFontSizeStep(FontSizeStep).Rounded;

    /// <summary>That design's table, already in the PAGE's staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/modified-font-metric.cc:62-68 <c>Modified_font_metric</c>'s
    /// <c>b.scale (magnification_)</c> — LilyPond does not multiply at the call sites; the
    /// grob reads a font that is ALREADY at its size, and every dimension that font reports
    /// is scaled once.
    /// <para>
    /// ⚠️ IT READ THE 20 DESIGN AND MULTIPLIED BY <c>Em/4</c> until 2026-08-11, which is the
    /// same defect the fingering digits carried: Emmentaler is optically sized, and 11.2246 pt
    /// lands on <c>emmentaler-11</c>, where the fixed-width five's ink top is 2.004 design-ss
    /// against the 20 design's 2.000. That difference IS the <c>-0.002333187</c> six
    /// <c>figbass.*</c> ledger points carried — the books' top figure is a five — and the
    /// residual it leaves is the ~9e-5 Pango hinting the whole fetaText family has.
    /// </para>
    /// </remarks>
    private static GlyphMetrics.DesignMetrics Font => GlyphMetrics.AtFontSize(FontSizeStep);

    /// <summary>The run's pieces, left to right, with X relative to the run's left edge.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/modified-font-metric.cc:125-143 <c>text_stencil</c> — a text
    /// stencil is its glyphs fed by their own advances, which for these digits is the hmtx
    /// advance the font declares (all ten are the same width: "tnum" is TABULAR figures, so
    /// a two-digit figure cannot shift its column — 1.656 design-ss at the 11 design).
    /// LILYPOND-REF: lily/pango-font.cc:344-362 Pango_font::pango_item_string_stencil — the
    ///   advance is HINTED to a device pixel at PANGO_RESOLUTION 1200, per glyph, which is
    ///   why this snaps rather than multiplying straight through.
    /// ⚠️ LILYSHARP-OWN: the fallback branch. A character with no bass-figure glyph is
    /// Lily#'s continuation dash (<c>FiguredBassFigure.DisplayText</c>'s en dash for a held
    /// figure), which LilyPond does not draw as text at all — it is a
    /// <c>BassFigureContinuation</c> spanner (lily/figured-bass-engraver.cc:197-238
    /// center_continuations). Drawn in the serif face at this em so that the fallback's size
    /// and its metric still come from one place; it retires with the continuation port.
    /// <para>
    /// ⚠️ NOTHING WATCHES THIS WIDTH. Since the figure was left-aligned on its column
    /// (2026-08-11, ledger <c>figbass.alone.head-anchor-to-box-left</c>) the run's total no
    /// longer reaches the drawn X at all; it survives as the feed BETWEEN glyphs, and Lily#'s
    /// grammar has no two-glyph figure to show it (<c>@fig(13)</c> does not parse). It is
    /// corrected here only because the design is one decision for the whole run and splitting
    /// it would be the invention.
    /// </para>
    /// </remarks>
    internal static ImmutableArray<Piece> Pieces(string text)
    {
        var run = FetaTextRun.Pieces(text, TryGetFigure, Em);
        var pieces = ImmutableArray.CreateBuilder<Piece>(run.Length);
        foreach (var p in run) pieces.Add(new Piece(p.Ch, p.X, p.Advance, p.IsGlyph));
        return pieces.ToImmutable();
    }

    /// <summary>
    /// The glyph, its outline box and its UNHINTED advance for one figure character, all in
    /// the page's staff spaces out of the figure's own design.
    /// </summary>
    private static bool TryGetFigure(char c, out char glyph, out GlyphMetrics.BBox outline,
        out double advance)
    {
        var f = Font;
        (glyph, outline, advance) = c switch
        {
            '0' => (EmmentalerGlyphs.FigBassDigit0, f.FigBassDigit0Outline, f.FigBassDigit0Advance),
            '1' => (EmmentalerGlyphs.FigBassDigit1, f.FigBassDigit1Outline, f.FigBassDigit1Advance),
            '2' => (EmmentalerGlyphs.FigBassDigit2, f.FigBassDigit2Outline, f.FigBassDigit2Advance),
            '3' => (EmmentalerGlyphs.FigBassDigit3, f.FigBassDigit3Outline, f.FigBassDigit3Advance),
            '4' => (EmmentalerGlyphs.FigBassDigit4, f.FigBassDigit4Outline, f.FigBassDigit4Advance),
            '5' => (EmmentalerGlyphs.FigBassDigit5, f.FigBassDigit5Outline, f.FigBassDigit5Advance),
            '6' => (EmmentalerGlyphs.FigBassDigit6, f.FigBassDigit6Outline, f.FigBassDigit6Advance),
            '7' => (EmmentalerGlyphs.FigBassDigit7, f.FigBassDigit7Outline, f.FigBassDigit7Advance),
            '8' => (EmmentalerGlyphs.FigBassDigit8, f.FigBassDigit8Outline, f.FigBassDigit8Advance),
            '9' => (EmmentalerGlyphs.FigBassDigit9, f.FigBassDigit9Outline, f.FigBassDigit9Advance),
            '♭' => (EmmentalerGlyphs.FigBassFlat, f.FigBassFlatOutline, f.FigBassFlatAdvance),
            '♮' => (EmmentalerGlyphs.FigBassNatural, f.FigBassNaturalOutline, f.FigBassNaturalAdvance),
            '♯' => (EmmentalerGlyphs.FigBassSharp, f.FigBassSharpOutline, f.FigBassSharpAdvance),
            _ => ('\0', default, 0.0),
        };
        return glyph != '\0';
    }

    /// <summary>The run's advance width in staff spaces.</summary>
    internal static double Width(string text) => FetaTextRun.Width(text, TryGetFigure, Em);

    /// <summary>
    /// The run's ink above its baseline — the union of its glyphs' outline tops, which is
    /// what a BassFigure's <c>Y-extent</c> is.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:356 BassFigure's Y-extent (bass-figure-interface at :359),
    /// <c>grob::always-Y-extent-from-stencil</c> — the extent IS the drawn stencil's.
    /// LILYPOND-REF: lily/modified-font-metric.cc:125-143 <c>text_stencil</c> — the text path
    /// measures the OUTLINE through Pango and never reads LILC, which is why the boxes asked
    /// for here are the <c>...Outline</c> ones (the same argument the dynamic letters carry in
    /// <see cref="GlyphMetrics.TryGetDynamicInk"/>); a multi-glyph run's box is the UNION of
    /// its glyphs', measured on <c>\mp</c> when the dynamics went this way.
    /// </remarks>
    // No feta glyph: the same face and size the drawing falls back to, so that path keeps
    // the two halves together as well (FetaTextRun does the fallback).
    internal static double InkTop(string text) => FetaTextRun.InkTop(text, TryGetFigure, Em);

    /// <summary>
    /// The run's ink BELOW its baseline (≤ 0) — the other end of the same
    /// <c>Y-extent</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:356 BassFigure's Y-extent (bass-figure-interface at :359),
    /// <c>grob::always-Y-extent-from-stencil</c> — one stencil, two edges, so the descent is
    /// asked of the same outlines <see cref="InkTop"/> asks.
    /// <para>
    /// ⚠️ IT IS NOT ALWAYS ZERO, which is the whole reason it is a call and not a 0. A digit
    /// of the <c>fattened.fixedwidth</c> cut sits ON the baseline (all ten box bottoms are
    /// 0.000 design-ss, bar the seven's −0.004), and that is why LilyPond's dumped figure
    /// extent bottoms at exactly 0.0 in every book of figured-bass-placement.ly — but the
    /// figured-bass accidentals hang below it (the sharp by 0.252 design-ss), and a row whose
    /// lowest figure carries one both reserves deeper and pushes the next row further down.
    /// </para>
    /// </remarks>
    internal static double InkBottom(string text)
        => FetaTextRun.InkBottom(text, TryGetFigure, Em);
}
