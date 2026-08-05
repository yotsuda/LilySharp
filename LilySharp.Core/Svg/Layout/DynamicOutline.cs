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

using System.Collections.Concurrent;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A dynamic label's OWN vertical-skyline pair (<c>my_dim</c>): the fetaText letters'
/// baked outlines composed at pen positions — each letter's hmtx advance PLUS its GPOS
/// kern, snapped as one whole device pixel, which IS the computation LilyPond runs
/// through Pango. Baseline origin; X from the FIRST letter's pen (the DynamicText
/// X-extent is the logical rect <c>[0, advance run]</c>, measured in
/// audit/lp-geometry/probes/dynamic-text-x.ly — the lsb overhang is ink, not extent).
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:1446 DynamicText Grob::vertical_skylines_from_stencil
///   (the callback behind <c>grob::always-vertical-skylines-from-stencil</c>),
///   :1412-1413 DynamicLineSpanner
///   <c>from-element-stencils</c> — the spanner's facing profile side-position measures
///   against (side-position-interface.cc:353-358 <c>Skyline::distance</c>) is the drawn
///   glyphs' real outline.
/// <para>
/// ★ THE PEN IS QUANTISED, ONE WHOLE DEVICE PIXEL PER GLYPH (ported 2026-08-05). Pango
/// hints each glyph's advance to a whole pixel, and that pixel is
/// <see cref="Rendering.TextFontMetrics.PangoPixelStaffSpaces"/> — derived in this tree
/// from LilyPond's own INCH_TO_BP / PANGO_RESOLUTION / output_scale, and already applied
/// to the time signature's digits and to every text advance. Arithmetic, not a fit:
/// <c>f</c>'s raw advance 1.280000 is 37.4888 pixels, rounds to 37, and comes back as
/// 1.263302 — the width LilyPond's own dump reports for <c>\f</c>
/// (probes/dynamic-support.ly DSQ: DynamicText x = (8.723849 . 9.987151)). Nine digits.
/// Half that difference, 0.008349, is the pen offset centring moves the label by.
/// </para>
/// <para>
/// ⚠️⚠️ THE KERN IS INSIDE THE SNAP, and the ledger is what says so. Session 93 tried this
/// with the kern added AFTER the snap: four points landed and the fifth,
/// <c>dynamic-stem-binding</c> (the <c>\fff</c> book), went +0.001793 → −0.003007 — worse,
/// and across zero. GPOS adjusts the glyph's OWN advance and Pango hints the result, so a
/// kerned pair carries ONE rounding, not a rounding plus a raw kern; that spelling misses
/// 0.015426772 per pair, and <c>\fff</c> has two. With the kern inside, all twenty labels
/// of dynamic-text-x.ly come back exactly (DynamicLabelWidthTests) and the five points
/// land at −0.000088 / −0.000050 / +0.000903 / +0.001396 / −0.000089, i.e. the same e-5
/// face-sliver family the controls have always sat in.
/// </para>
/// <para>
/// ⚠️ THIS IS THE SECOND SPELLING OF ONE COMPUTATION, and the first one is the real
/// shaper. <see cref="Rendering.TextFontMetrics.Run"/> shapes a string through HarfBuzz
/// — the shaper Pango itself calls — and snaps each SHAPED advance per glyph, which is
/// this loop with the pair adjustments coming from the font instead of from a baked
/// table (<see cref="GlyphMetrics.DynamicLetterKern"/>, extracted from the same GPOS).
/// This file composes by hand because it needs each letter's BAKED OUTLINE at its pen,
/// not just a width. ⇒ The literal shape of the port is "ask the shaper, snap what it
/// answers", and the way to get there is to take the pen positions from a shaped run of
/// Emmentaler and keep only the outlines here. Until then the two spellings agree
/// because DynamicLabelWidthTests pins this one to LilyPond's twenty measured labels;
/// what would break them apart is a font update that changes a GPOS pair without the
/// extractor being re-run (HANDOFF §5.2.1②).
/// </para>
/// <para>
/// ⚠️ COMPOSED AT FULL SIZE, always — <see cref="GlyphMetrics.DynamicLetterAdvance"/> is
/// the full-size table and the pixel is a DEVICE quantum, so a label on a scaled staff
/// would snap at its own ppem in LilyPond and does not here. The below-staff collision
/// pass guards this (<c>SkylineBuilder</c> takes the outline branch only when
/// <c>size.Span(1.0) == 1.0</c>); the two outside-staff stacker paths do NOT, and use
/// this profile for an ossia or cue staff's dynamic as well. The fold predates the snap
/// (the outlines and advances were already full-size), it can only misplace a scaled
/// staff's label by a pixel or two of ITS scale, and there is NO measured point on it
/// today — an ossia dynamic pair is what would open one.
/// </para>
/// <para>
/// Resolved once per label and cached (the overlap resolve is the expensive step; a
/// placement is a shift/raise copy — a monotone transform, which commutes with the
/// resolve). The same shape as <see cref="TextOutlineSkylines"/>' caches.
/// ★ COUNTED, not asserted (2026-08-05, HANDOFF §7.9 — preview speed is a requirement,
/// and the snap above is arithmetic this file did not run before): twenty full renders
/// of test/dynamics call <see cref="Place"/> 1800 times and enter the factory EIGHT —
/// the book's eight distinct labels, all of them on the FIRST render, none on the other
/// nineteen. The control (test/notes, no dynamics) is 0 and 0, i.e. a book that does not
/// reach this code at all. ⇒ The snap costs eight roundings per PROCESS, not per layout,
/// which is the property a keystroke-driven preview needs. The per-placement cost is
/// unchanged: the same buildings at different pen X.
/// </para>
/// </remarks>
internal static class DynamicOutline
{
    private static readonly ConcurrentDictionary<string,
        (SkylineBuilding[] Up, SkylineBuilding[] Down, double Width)?> Cache = new();

    /// <summary>
    /// The label's advance-run width (staff spaces), or null when the label is not
    /// spelled from the seven fetaText dynamic letters (free <c>@text</c>,
    /// <c>cresc.</c>/<c>dim.</c> words — those have no feta outline and the caller
    /// falls back to its box model, as <see cref="GlyphMetrics.TryGetDynamicInk"/> does).
    /// </summary>
    public static double? AdvanceWidth(string? text)
        => text is { Length: > 0 } ? Resolved(text)?.Width : null;

    /// <summary>
    /// The label's outline skylines, placed: first letter's pen at
    /// (<paramref name="xPen"/>, <paramref name="yBaseline"/>) in the caller's Y-up
    /// frame. Fresh instances — safe to merge or raise. Null when the label is not
    /// spelled from the fetaText dynamic letters.
    /// </summary>
    public static (VerticalSkyline Up, VerticalSkyline Down)? Place(
        string? text, double xPen, double yBaseline)
    {
        if (text is not { Length: > 0 } || Resolved(text) is not { } r)
            return null;
        return (PlaceResolved(VerticalDirection.Up, r.Up, xPen, yBaseline),
                PlaceResolved(VerticalDirection.Down, r.Down, xPen, yBaseline));
    }

    private static (SkylineBuilding[] Up, SkylineBuilding[] Down, double Width)? Resolved(
        string text)
        => Cache.GetOrAdd(text, static t =>
        {
            foreach (char c in t)
                if (GlyphMetrics.DynamicLetterAdvance(c) is null)
                    return null;    // not a fetaText dynamic letter

            var up = new VerticalSkyline(VerticalDirection.Up);
            var down = new VerticalSkyline(VerticalDirection.Down);
            double pen = 0;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                var (dQuads, uQuads) = GlyphMetrics.DynamicLetterVerticalSkylineQuads(c);
                up.Merge(VerticalSkyline.FromGlyphOutline(
                    VerticalDirection.Up, uQuads, StaffSize.FullSize, pen, 0));
                down.Merge(VerticalSkyline.FromGlyphOutline(
                    VerticalDirection.Down, dQuads, StaffSize.FullSize, pen, 0));
                // ONE glyph, ONE device advance: the GPOS kern is an adjustment to THIS
                // glyph's advance, so it is inside the snap — not added to a snapped
                // advance afterwards. Measured over all twenty labels of
                // audit/lp-geometry/probes/dynamic-text-x.ly (DynamicLabelWidthTests).
                // LILYPOND-REF: lily/pango-font.cc:345-362 Pango_font::pango_item_string_stencil
                //   takes the X extent from pango_glyph_string_extents' LOGICAL rect over the
                //   SHAPED run — so kerning and the per-glyph hint are already in it.
                double advance = GlyphMetrics.DynamicLetterAdvance(c)!.Value;
                if (i + 1 < t.Length)
                    advance += GlyphMetrics.DynamicLetterKern(c, t[i + 1]);
                pen += Rendering.TextFontMetrics.QuantiseToPangoPixel(advance);
            }
            return (up.Buildings.ToArray(), down.Buildings.ToArray(), pen);
        });

    private static VerticalSkyline PlaceResolved(
        VerticalDirection direction, SkylineBuilding[] resolved, double x, double y)
    {
        var placed = new SkylineBuilding[resolved.Length];
        double raise = (int)direction * y;
        for (int i = 0; i < resolved.Length; i++)
            placed[i] = resolved[i].ShiftedHorizon(x).RaisedBy(raise);
        return VerticalSkyline.FromResolvedBuildings(direction, placed);
    }
}
