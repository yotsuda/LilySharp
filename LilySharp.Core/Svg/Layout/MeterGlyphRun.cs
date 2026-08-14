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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One ROW of a time signature — a numerator or a denominator — as the glyph run LilyPond
/// puts on the page: the PLAIN fetaText digits at the score's own size, each advance kerned
/// to its neighbour and hinted to a device pixel.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/time-signature.scm:31-41 <c>time-signature-style-markup-procedures</c>
///   — <c>ly:time-signature::print</c> picks a procedure out of it and ends at
///   <c>grob-interpret-markup</c>, i.e. it builds a MARKUP and interprets it.
/// LILYPOND-REF: scm/time-signature-settings.scm:922-923 <c>format-compound-time</c> — it
///   wraps the rows in <c>make-number-markup</c>.
/// LILYPOND-REF: scm/define-markup-commands.scm:3872-3981 <c>define-markup-command</c> for
///   <c>number</c> — it prepends <c>font-encoding fetaText</c> and NOTHING else. So a meter
///   digit goes through Pango over the FreeType outline, exactly as a DynamicText does.
/// <para>
/// ⚠️ IT IS THE PLAIN CUT, and that is the whole reason this file exists. <c>ss01</c> is what
/// selects the FATTENED digits, and a Fingering asks for it in
/// scm/define-grobs.scm:1543-1560 <c>add-stem-support</c> … <c>font-features</c>. Where
/// <c>\number</c> does not — so Lily# reading <c>fattened.*</c> for the meter was the same
/// class of defect the fingering carried until session 134, one cut over. MEASURED, not
/// deduced: <c>ly:stencil-expr</c> prints <c>one</c> / <c>four</c> / <c>seven</c> out of
/// <c>emmentaler-20.otf</c> for a time signature — plain names, no <c>fattened.</c> prefix and
/// no <c>.alt</c>. Eight of the ten digits are the same width in both cuts, so only the 1
/// (1.268 against 1.292) and the 7 (1.348 against 1.288) move, in OPPOSITE directions
/// (ledger <c>line-start.time-to-first-note.digit-one</c> / <c>.digit-seven</c>).
/// </para>
/// <para>
/// ⚠️ THE DESIGN WAS ALREADY RIGHT. TimeSignature declares no <c>font-size</c> at all:
/// scm/define-grobs.scm:3922-3934 <c>break-align-anchor-alignment</c> … <c>break-align-symbol</c>
/// … <c>extra-spacing-height</c>, with no font size among them —
/// so it asks for 20·magstep(0) = 20pt and lands on
/// <c>emmentaler-20</c> — which is the table Lily# already read. The handoff's remaining ⒡
/// item called this an optical-size defect; it is not one, and the −0.000076 that item hangs
/// on is Pango quantising the INK HEIGHT (HANDOFF §1 ⒧, and <see cref="GlyphMetrics"/>'s own
/// remark on the Pango quantum says so).
/// </para>
/// <para>
/// ⚠️ ONE HOME, for the reason the fingering and the figured bass each have one: the
/// reservation (<see cref="GlyphMetrics.GetTimeSigWidth"/>, which
/// <see cref="BreakAlignSpacing"/> and <see cref="SpacingRules"/> book the prefix column
/// from) and the drawing (<c>SharedRenderer.DrawTimeSignature</c>) must read one run. Before
/// session 164 there were THREE spellings: the reservation took the max of two per-digit
/// fattened advances, the drawing stepped a flat 1.4 per digit, and neither could lay out a
/// two-digit number at all.
/// </para>
/// <para>
/// ⚠️ THE MULTI-MEASURE REST'S NUMBER IS THE SECOND CONSUMER of this cut, not a third
/// spelling: scm/define-grobs.scm:2402-2417 <c>multi-measure-rest-number-interface</c> — the
/// same block's <c>font-features</c> is <c>("cv47")</c>, i.e. cv47 and no <c>ss01</c>, so its
/// base cut is this one, and
/// cv47's <c>.alt</c> pair carries IDENTICAL advances to its base in all eight designs
/// (measured from the font files). Only the PEN tells the two apart, so the metric is shared
/// and the glyph choice is not.
/// </para>
/// </remarks>
internal static class MeterGlyphRun
{
    /// <summary>A drawn piece of the row: a feta digit, or a character the cut has no glyph
    /// for (Lily#'s <c>+</c> in a compound meter) left to the caller's text fallback.</summary>
    internal readonly record struct Piece(char Ch, double X, double Advance, bool IsGlyph);

    /// <summary>The em a meter digit is drawn at, in staff spaces — the staff height, since
    /// TimeSignature declares no <c>font-size</c>.</summary>
    /// <remarks>LILYPOND-REF: lily/font-select.cc:99-117 select_font — for fetaText the base
    /// size is the staff height (4 staff spaces), stepped by <c>2^(font-size/6)</c>, and the
    /// step here is 0.</remarks>
    internal const double Em = 4.0;

    /// <summary>The <c>font-size</c> a TimeSignature states. Zero, and written down rather
    /// than assumed so the design below is ASKED of the rule instead of hard-coded.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3922-3934 <c>break-align-anchor-alignment</c>
    /// … <c>extra-spacing-height</c> — TimeSignature's property list carries no
    /// <c>font-size</c>, and an unstated one is 0.</remarks>
    internal const double FontSizeStep = 0.0;

    /// <summary>The Emmentaler design a meter digit is drawn from — the PEN needs it as well
    /// as the metrics.</summary>
    /// <remarks>LILYPOND-REF: lily/font-select.cc:41-70 best_rounded_design_size —
    /// 20·magstep(0) = 20 pt lands on <c>emmentaler-20</c>.</remarks>
    internal static int Design => EmmentalerDesignSize.ForFontSizeStep(FontSizeStep).Rounded;

    /// <summary>That design's table, already in the PAGE's staff spaces.</summary>
    private static GlyphMetrics.DesignMetrics Font => GlyphMetrics.AtFontSize(FontSizeStep);

    /// <summary>
    /// The row's pieces, left to right, with X relative to the row's left edge.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN: the fallback branch. The only non-digit that reaches here is the
    /// <c>+</c> of a compound meter's numerator (<c>TimeSignatureInfo.BeatsText</c>), which
    /// LilyPond spells with its own markup rather than a feta glyph; it keeps the serif
    /// fallback the drawing already used, so its size and its metric still come from one
    /// place.
    /// </remarks>
    internal static ImmutableArray<Piece> Pieces(string text)
    {
        var run = FetaTextRun.Pieces(text, TryGetDigit, Em, GlyphMetrics.MeterDigitKern);
        var pieces = ImmutableArray.CreateBuilder<Piece>(run.Length);
        foreach (var p in run) pieces.Add(new Piece(p.Ch, p.X, p.Advance, p.IsGlyph));
        return pieces.ToImmutable();
    }

    /// <summary>The row's advance width in staff spaces.</summary>
    internal static double Width(string text)
        => FetaTextRun.Width(text, TryGetDigit, Em, GlyphMetrics.MeterDigitKern);

    /// <summary>
    /// The glyph, its outline box and its UNHINTED advance for one meter digit.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE OUTLINE IS NOT EXTRACTED for these glyphs — the meter's digits are
    /// advance-only in <c>Extract-EmmentalerMetrics.py</c>'s tables, because nothing reads a
    /// meter row's ink: a TimeSignature's height is the staff's, and its skyline is seeded
    /// from the drawn glyph elsewhere. The box comes back <c>default</c> and
    /// <see cref="FetaTextRun.InkTop"/>/<see cref="FetaTextRun.InkBottom"/> are therefore not
    /// exposed here. A consumer that needs the ink adds the outlines to the extractor first.
    /// </remarks>
    private static bool TryGetDigit(char c, out char glyph, out GlyphMetrics.BBox outline,
        out double advance)
    {
        outline = default;
        if (c is < '0' or > '9')
        {
            glyph = '\0';
            advance = 0.0;
            return false;
        }
        glyph = EmmentalerGlyphs.GetTimeSigDigit(c - '0');
        advance = GlyphMetrics.UnquantisedMeterDigitAdvance(Font, c - '0');
        return true;
    }
}
