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
/// baked outlines composed at pen positions — hmtx advance plus GPOS kern, which IS the
/// computation LilyPond runs through Pango. Baseline origin; X from the FIRST letter's
/// pen (the DynamicText X-extent is the logical rect <c>[0, advance run]</c>, measured
/// in audit/lp-geometry/probes/dynamic-text-x.ly — the lsb overhang is ink, not extent).
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:1446 DynamicText Grob::vertical_skylines_from_stencil
///   (the callback behind <c>grob::always-vertical-skylines-from-stencil</c>),
///   :1412-1413 DynamicLineSpanner
///   <c>from-element-stencils</c> — the spanner's facing profile side-position measures
///   against (side-position-interface.cc:353-358 <c>Skyline::distance</c>) is the drawn
///   glyphs' real outline.
/// <para>
/// ⚠️ The per-glyph Pango shaping quantisation (&lt;= 0.0167 ss, both signs, measured in
/// dynamic-text-x.ly) is NOT modelled — baking the measured widths would paste evaluation
/// results (HANDOFF §5.2). It stays a named residual family, the X-side sibling of the
/// Y 2e-5 family.
/// </para>
/// <para>
/// Resolved once per label and cached (the overlap resolve is the expensive step; a
/// placement is a shift/raise copy — a monotone transform, which commutes with the
/// resolve). The same shape as <see cref="TextOutlineSkylines"/>' caches.
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
                pen += GlyphMetrics.DynamicLetterAdvance(c)!.Value;
                if (i + 1 < t.Length)
                    pen += GlyphMetrics.DynamicLetterKern(c, t[i + 1]);
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
