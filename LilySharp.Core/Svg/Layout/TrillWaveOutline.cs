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
/// The trill line's OWN geometry: a run of <c>scripts.trill_element</c> glyphs, which is
/// what LilyPond's wavy line IS — its length and its vertical profile both come from
/// repeating that one glyph, not from an amplitude.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/line-interface.cc:48-108 make_trill_line — the element is fetched by
///   name, <c>align_to (Y_AXIS, CENTER)</c>'d onto the line, its stencil extent gives the
///   repetition step <c>elt_len</c> and its horizontal skylines the first copy's true
///   length <c>elt_true_len</c>; copies are added with <c>add_at_edge (X_AXIS, RIGHT, elt,
///   0)</c> while whole ones fit, and the run's box is <c>Box (Interval (0, total_len), …)</c>.
/// LILYPOND-REF: scm/define-grobs.scm:4083 TrillSpanner <c>(style . trill)</c> selects that
///   line, :4085 <c>grob::unpure-vertical-skylines-from-stencil</c> makes the run's outline
///   the grob's own profile — read by side-position-interface.cc:353-358 (aligned_side's
///   <c>my_dim</c>) and axis-group-interface.cc:770-773 (the outside-staff pass's mover).
/// <para>
/// ⚠️ TWO boxes of one glyph, and they are different numbers: the LILC bbox
/// (<c>OrnTrillElementGlyph</c>, width 1.0) is the STEP, the outline
/// (<c>OrnTrillElementGlyphOutline</c>, width 1.448) is the first copy's LENGTH. The
/// difference is the overhang two neighbours blend across — LilyPond's own comment at
/// line-interface.cc:72-74 — and it is why a trill line ends SHORT of its right bound by
/// whatever does not make a whole element (0.0486 in the TXW book).
/// </para>
/// <para>
/// ⚠️ The DRAWN wave is still Lily#'s parabolic polyline
/// (<c>SharedRenderer.DrawTrillSpanners</c>, amplitude
/// <see cref="EngravingDefaults.TrillWaveAmplitude"/>), so reservation and drawing differ
/// here as they already do for the dynamics (feta outline reserved, serif drawn). The
/// reservation is the LARGER of the two (this outline reaches 0.404 either side of the
/// line, the polyline 0.25), so nothing can overlap on account of it; drawing the glyph run
/// is the other half of this port and changes how every trill LOOKS.
/// </para>
/// <para>
/// Resolved per element COUNT and cached, then placed by shift/raise — the same shape as
/// <see cref="DynamicOutline"/> (the overlap resolve is the expensive step and a placement
/// is a monotone transform that commutes with it).
/// </para>
/// </remarks>
internal static class TrillWaveOutline
{
    /// <summary>The repetition step: the element's LILC stencil width, LilyPond's
    /// <c>elt_len = elt.extent (X_AXIS).length ()</c>.</summary>
    private static double ElementStep
        => GlyphMetrics.OrnTrillElementGlyph.Right - GlyphMetrics.OrnTrillElementGlyph.Left;

    /// <summary>The first copy's own length: the element's OUTLINE width, LilyPond's
    /// <c>elt_true_len</c> (taken there from the element stencil's horizontal skylines,
    /// which is the outline).</summary>
    private static double ElementTrueLength
        => GlyphMetrics.OrnTrillElementGlyphOutline.Right
           - GlyphMetrics.OrnTrillElementGlyphOutline.Left;

    /// <summary>What <c>align_to (Y_AXIS, CENTER)</c> subtracts: the element's stencil
    /// (LILC) extent centre, so the run straddles the line.</summary>
    private static double CentreOffset
        => (GlyphMetrics.OrnTrillElementGlyph.Bottom + GlyphMetrics.OrnTrillElementGlyph.Top) / 2.0;

    /// <summary>
    /// How many whole elements a line of <paramref name="allotted"/> length carries —
    /// always at least one, as LilyPond insists ("Always have at least one trill element,
    /// even if the space allotted technically doesn't allow it").
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/line-interface.cc:84-98 make_trill_line —
    ///   <c>num_extra_elements = static_cast&lt;vsize&gt; (delta / elt_len)</c>.</remarks>
    private static int ElementCount(double allotted)
    {
        double delta = allotted - ElementTrueLength;
        int extra = delta > 0 ? (int)(delta / ElementStep) : 0;
        return 1 + extra;
    }

    /// <summary>
    /// The line's DRAWN length for an allotted span: <c>elt_true_len + n * elt_len</c>,
    /// which is what LilyPond puts in the stencil's box — never more than
    /// <paramref name="allotted"/>, and short of it by the element remainder.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/line-interface.cc:88-102 make_trill_line —
    ///   <c>total_len</c> and <c>new_b = Box (Interval (0, total_len), b[Y_AXIS])</c>.</remarks>
    public static double DrawnLength(double allotted)
        => ElementTrueLength + (ElementCount(allotted) - 1) * ElementStep;

    /// <summary>
    /// The line's vertical skyline pair, with the run's ink starting at
    /// <paramref name="startX"/> and the elements centred on <paramref name="lineY"/> in
    /// the caller's Y-up frame. Fresh instances — safe to merge or raise.
    /// </summary>
    public static (VerticalSkyline Up, VerticalSkyline Down) Place(
        double startX, double allotted, double lineY)
    {
        var r = Resolved(ElementCount(allotted));
        double y = lineY - CentreOffset;
        return (PlaceResolved(VerticalDirection.Up, r.Up, startX, y),
                PlaceResolved(VerticalDirection.Down, r.Down, startX, y));
    }

    // Keyed by element count: the run of that many copies, with the FIRST copy's outline
    // left edge at x = 0 (LilyPond's `line.translate_axis (-elt_true_ext[LEFT], X_AXIS)`)
    // and the glyph baseline at y = 0.
    private static readonly ConcurrentDictionary<int,
        (SkylineBuilding[] Up, SkylineBuilding[] Down)> Cache = new();

    private static (SkylineBuilding[] Up, SkylineBuilding[] Down) Resolved(int count)
        => Cache.GetOrAdd(count, static n =>
        {
            var (dQuads, uQuads) = GlyphMetrics.TrillElementVerticalSkylineQuads();
            var up = new VerticalSkyline(VerticalDirection.Up);
            var down = new VerticalSkyline(VerticalDirection.Down);
            // The i-th copy's ORIGIN: the run is shifted so copy 0's outline starts at 0,
            // and each further copy sits one STEP right (add_at_edge on the stencil boxes).
            for (int i = 0; i < n; i++)
            {
                double origin = i * ElementStep - GlyphMetrics.OrnTrillElementGlyphOutline.Left;
                up.Merge(VerticalSkyline.FromGlyphOutline(
                    VerticalDirection.Up, uQuads, StaffSize.FullSize, origin, 0));
                down.Merge(VerticalSkyline.FromGlyphOutline(
                    VerticalDirection.Down, dQuads, StaffSize.FullSize, origin, 0));
            }
            return (up.Buildings.ToArray(), down.Buildings.ToArray());
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
