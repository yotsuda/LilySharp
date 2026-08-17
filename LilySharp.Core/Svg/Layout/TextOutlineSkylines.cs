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
using LilySharp.Core.Rendering;
using SkiaSharp;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// UP/DOWN vertical-skyline profiles of a text string's glyph OUTLINES, at the size,
/// face and style the draw uses — what LilyPond stores in a text grob's
/// <c>vertical-skylines</c> property.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3818 TextScript grob::always-vertical-skylines-from-stencil
/// — a text
/// grob's skylines come from its STENCIL, i.e. the drawn glyph outlines, not from its
/// extent box. The same declaration covers BarNumber, RehearsalMark, DynamicText and the
/// other text grobs the outside-staff pass stacks, which is why
/// <c>avoid_outside_staff_collisions</c> reads two texts' profiles POINTWISE and lands a
/// descender over a neighbour's x-height bowls (ledger <c>textscript.stacked.outline-step</c>:
/// LilyPond 2.104975 against the box arithmetic 2.525870).
/// <para>
/// The walk is LilyPond's own, over the same Skia path the ink box already comes from
/// (<see cref="TextFontMetrics.OutlinePath"/> — one geometry for reservation and drawing):
/// LILYPOND-REF: lily/freetype.cc:96-202 <c>Path_interpreter</c> /
/// <c>ly_FT_add_outline_to_skyline</c> — cubics flatten into
/// <c>max(2, |end-start|/0.2)</c> segments, the length measured between the TRANSFORMED
/// endpoints (staff spaces, so the segment count is taken at the final size, which is why
/// the cache key carries the font size); the final sub-segment of every cubic is an
/// <c>add_segment</c> that feeds BOTH skylines (freetype.cc:147). Each contour segment is
/// classified by winding direction —
/// LILYPOND-REF: lily/include/lazy-skyline-pair.hh:53-65 <c>add_contour_segment</c>,
/// <c>(x1 &gt; x2) == CCW</c> selects the UP list — and the per-direction lists resolve into
/// profiles exactly as the baked Emmentaler glyph skylines do
/// (<c>audit/scripts/Extract-EmmentalerSkylines.py</c> is this walk in Python; the quad
/// format here is the one <see cref="VerticalSkyline.FromGlyphOutline"/> already places).
/// </para>
/// <para>
/// ⚠️ LILYSHARP-OWN, two declared substitutions inside the walk, neither of which changes
/// the result for the bundled faces:
/// (1) LilyPond picks the contour orientation from the FONT FORMAT
/// (freetype.cc:193-195, TrueType ⇒ CW, else CCW); Skia does not contract which winding
/// its backend hands over after the Y-down flip, so the orientation is read off the path
/// itself (dominant signed area — an outer contour always outweighs its counters).
/// (2) LilyPond substitutes a straight line for a conic (freetype.cc:122-127
/// <c>curve2to</c>) because conics only arise in TrueType faces; the bundled TeX Gyre
/// faces are CFF, all cubics, so if a Skia backend ever hands quads/conics for them they
/// are degree-elevated to the cubics LilyPond's FreeType sees rather than chopped to lines.
/// </para>
/// </remarks>
internal static class TextOutlineSkylines
{
    // The RESOLVED baseline-origin profiles, not the raw quads: FromGlyphOutline's
    // overlap resolve is the expensive step and it is a pure function of the string,
    // so it runs once per (face, style, string, size) and every placement is a
    // shift/raise copy of the resolved buildings (a monotone transform, which
    // commutes with the resolve — byte-identical to resolving after placing).
    private static readonly ConcurrentDictionary<(bool Sans, FontStyle Style, string Text, double Size),
        (SkylineBuilding[] Up, SkylineBuilding[] Down)> ProfileCache = new();

    /// <summary>
    /// The string's outline skylines, placed: baseline at (<paramref name="x"/>,
    /// <paramref name="yBaseline"/>) in the caller's Y-up frame. Fresh instances —
    /// safe for the caller to merge or raise.
    /// </summary>
    public static (VerticalSkyline Up, VerticalSkyline Down) Place(
        string text, double fontSize, bool sans, FontStyle style, double x, double yBaseline)
    {
        var (up, down) = ProfileCache.GetOrAdd((sans, style, text, fontSize),
            static key =>
            {
                var (upQuads, downQuads) = BuildQuads(key.Text, key.Size, key.Sans, key.Style);
                return (
                    Resolve(VerticalDirection.Up, upQuads),
                    Resolve(VerticalDirection.Down, downQuads));
            });
        return (PlaceResolved(VerticalDirection.Up, up, x, yBaseline),
                PlaceResolved(VerticalDirection.Down, down, x, yBaseline));
    }

    // Music-glyph profiles, one per (glyph, size, design) — the metronome mark's note
    // pieces, the scripts. The DESIGN is in the key because Emmentaler is optically sized:
    // the 16's accidental is not the 20's scaled, so two grobs at different font-sizes walk
    // two different outlines (EmmentalerFaces).
    // ⚠️ The HORIZON PADDING is in the key, and the padding happens INSIDE the cached
    // factory: a grob that declares skyline-horizontal-padding (three scripts do) would
    // otherwise pay pad + merge + resolve on every placement, and a script-dense page places
    // one per note per pass.
    // ⚠️ TWO PADDINGS, IN SEQUENCE. Pad is the grob's own declaration; ExtraPad is the
    // CONSUMER's, which LilyPond spends inside Skyline::distance by padding a copy of the
    // moving grob. Both are folded in here — see ArticulationEngraver.ScriptSkylines's
    // extraPad remark for why that is a decision about ORDER and not a bit-preserving
    // optimisation, and for the four observers that were checked before taking it.
    // ⚠️ NOT ONE PAD OF THEIR SUM: Skyline::padded is not additive (each pass extends flat
    // and then slopes at 45° from what it already has), so the sequence is kept.
    // ⚠️ PADDING GROB-LOCAL IS ALSO WHERE LILYPOND PADS — LILYPOND-REF:
    // lily/stencil-integral.cc:881-893 Grob::vertical_skylines_from_stencil pads the
    // STENCIL's skyline, i.e. before the grob is placed. Doing it here makes the
    // decomposition a function of the glyph alone; done after placement it was a function of
    // WHERE the glyph sat, because the resolve's epsilons are absolute (measured: one
    // fermata at x = 0/0.5/1/17.5/100/1000 resolved to 35/37/37/33/39/33 buildings).
    private static readonly ConcurrentDictionary<
        (char Glyph, double Size, int Design, double Pad, double ExtraPad),
        (SkylineBuilding[] Up, SkylineBuilding[] Down)> MusicProfileCache = new();

    /// <summary>
    /// One music-font (Emmentaler) glyph's outline skylines at
    /// <paramref name="fontSize"/> (staff spaces, 4.0 = the nominal staff), in the GLYPH's
    /// own frame — its origin at 0, unplaced; <see cref="PlaceMusicGlyph"/> is the overload
    /// that puts it somewhere. The SAME walk as
    /// the text overload — LilyPond's named-glyph skyline runs the same freetype
    /// flattening over the glyph outline, and the flattening happens at the
    /// TRANSFORMED size, which is why the size is in the cache key.
    /// Returns an EMPTY pair when the bundled music font cannot be located — the
    /// caller keeps the glyph's designed box.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stencil-integral.cc:535-563 add_named_glyph_segments;
    /// lily/freetype.cc:174-202 ly_FT_add_outline_to_skyline.
    /// </remarks>
    /// <summary>
    /// The same profile, RESOLVED and unplaced — for a caller that merges it straight into a
    /// skyline with <see cref="VerticalSkyline.Merge(IReadOnlyList{SkylineBuilding}, double,
    /// double)"/> rather than taking a placed copy of its own. Empty when the bundled music
    /// font cannot be located.
    /// </summary>
    public static (IReadOnlyList<SkylineBuilding> Up, IReadOnlyList<SkylineBuilding> Down)
        MusicGlyphProfile(char glyph, double fontSize, int design = 0, double horizonPadding = 0.0)
        => ResolvedMusicGlyph(glyph, fontSize, design, horizonPadding);

    /// <param name="design">
    /// The Emmentaler design to walk — 0 (or omitted) for the score's own. A grob that
    /// states a <c>font-size</c> reads ANOTHER design's outline, not this one scaled.
    /// </param>
    public static (VerticalSkyline Up, VerticalSkyline Down) PlaceMusicGlyph(
        char glyph, double fontSize, double x, double y, int design = 0,
        double horizonPadding = 0.0, double extraPad = 0.0)
    {
        var (up, down) = ResolvedMusicGlyph(glyph, fontSize, design, horizonPadding, extraPad);
        return (PlaceResolved(VerticalDirection.Up, up, x, y),
                PlaceResolved(VerticalDirection.Down, down, x, y));
    }

    /// <summary>
    /// One DIRECTION of <see cref="PlaceMusicGlyph"/>'s pair — null when the bundled music
    /// font cannot be located (the caller keeps the glyph's designed box), which is the same
    /// signal the pair's empties carry.
    /// </summary>
    /// <remarks>
    /// The two directions are resolved independently from the same cached entry and neither
    /// reads the other, so taking one is the same skyline as taking both and dropping one —
    /// it just does not allocate the drop. Every consumer of the pair uses one side.
    /// </remarks>
    public static VerticalSkyline? PlaceMusicGlyphSide(
        char glyph, double fontSize, double x, double y, VerticalDirection direction,
        int design = 0, double horizonPadding = 0.0, double extraPad = 0.0)
    {
        var (up, down) = ResolvedMusicGlyph(glyph, fontSize, design, horizonPadding, extraPad);
        var side = direction == VerticalDirection.Up ? up : down;
        // The pair's caller treats "both empty" as "no font"; one empty side on its own is a
        // real answer only when the other is too, so fall back on exactly that condition.
        if (up.Length == 0 && down.Length == 0)
            return null;
        return PlaceResolved(direction, side, x, y);
    }

    private static (SkylineBuilding[] Up, SkylineBuilding[] Down) ResolvedMusicGlyph(
        char glyph, double fontSize, int design, double horizonPadding = 0.0,
        double extraPad = 0.0)
    {
        if (design == 0)
            design = Rendering.EmmentalerFaces.DefaultDesign;
        return MusicProfileCache.GetOrAdd(
            (glyph, fontSize, design, horizonPadding, extraPad), static key =>
        {
            var path = TextFontMetrics.MusicGlyphPath(key.Glyph, key.Design);
            if (path == null || path.IsEmpty)
                return (Array.Empty<SkylineBuilding>(), Array.Empty<SkylineBuilding>());
            var (upQuads, downQuads) = FlattenPath(path, key.Size / 1000.0);
            return (
                Pad(VerticalDirection.Up,
                    Pad(VerticalDirection.Up, Resolve(VerticalDirection.Up, upQuads), key.Pad),
                    key.ExtraPad),
                Pad(VerticalDirection.Down,
                    Pad(VerticalDirection.Down, Resolve(VerticalDirection.Down, downQuads), key.Pad),
                    key.ExtraPad));
        });
    }

    /// <summary>
    /// The horizon padding a grob declares, applied to the RESOLVED profile — LilyPond's
    /// <c>p.pad (skyline-horizontal-padding)</c> half of
    /// <c>Grob::vertical_skylines_from_stencil</c>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/stencil-integral.cc:881-893 Grob::vertical_skylines_from_stencil;
    /// lily/skyline.cc:558-615 Skyline::padded — which
    /// <see cref="VerticalSkyline.Padded"/> ports line for line.</remarks>
    private static SkylineBuilding[] Pad(
        VerticalDirection direction, SkylineBuilding[] resolved, double pad)
        => pad <= 0.0 || resolved.Length == 0
            ? resolved
            : VerticalSkyline.FromResolvedBuildings(direction, resolved)
                .Padded(pad).Buildings.ToArray();

    private static SkylineBuilding[] Resolve(VerticalDirection direction, double[] quads)
        => VerticalSkyline.FromGlyphOutline(direction, quads, StaffSize.FullSize, 0, 0)
            .Buildings.ToArray();

    private static VerticalSkyline PlaceResolved(
        VerticalDirection direction, SkylineBuilding[] resolved, double x, double y)
    {
        var placed = new SkylineBuilding[resolved.Length];
        double raise = (int)direction * y;
        for (int i = 0; i < resolved.Length; i++)
            placed[i] = resolved[i].ShiftedHorizon(x).RaisedBy(raise);
        return VerticalSkyline.FromResolvedBuildings(direction, placed);
    }

    /// <summary>
    /// Flattens the string's path into sign-framed skyline quads ([x1, v1, v2, x2] with
    /// v = sky·y_up), baseline origin, staff spaces at <paramref name="fontSize"/>.
    /// </summary>
    private static (double[] Up, double[] Down) BuildQuads(
        string text, double fontSize, bool sans, FontStyle style)
        => FlattenPath(TextFontMetrics.OutlinePath(text, sans, style), fontSize / 1000.0);

    /// <summary>The walk itself, at path level (units/em·<paramref name="k"/> = staff
    /// spaces). Internal seam so a test can run the identical walk over another face —
    /// used to verify the bundled metric twin against LilyPond's own C059.</summary>
    internal static (double[] Up, double[] Down) FlattenPath(SKPath path, double k)
    {
        // Y-DOWN → Y-up reflection is part of the transform, exactly where LilyPond's
        // Transform sits in Path_interpreter: applied to every point BEFORE lengths are
        // measured or segments classified.
        (double X, double Y) T(SKPoint p) => (p.X * k, -p.Y * k);

        bool ccw = DominantWindingIsCcw(path, T);

        var up = new List<double>();
        var down = new List<double>();

        // LILYPOND-REF: lily/include/lazy-skyline-pair.hh:53-65 add_contour_segment —
        // (x1 > x2) == CCW puts the segment in the UP list, else DOWN. NOT "top half vs
        // bottom half": a counter's edges land by winding, and the resolve picks the
        // right building at each x (see Extract-EmmentalerSkylines.py's classify note).
        void AddContour((double X, double Y) p1, (double X, double Y) p2)
            => AddQuad((p1.X > p2.X) == ccw ? up : down, (p1.X > p2.X) == ccw ? +1 : -1, p1, p2);

        // LILYPOND-REF: lily/include/lazy-skyline-pair.hh:48-51 add_segment — the plain
        // (orientation-free) segment enters BOTH per-direction lists.
        void AddBoth((double X, double Y) p1, (double X, double Y) p2)
        {
            AddQuad(up, +1, p1, p2);
            AddQuad(down, -1, p1, p2);
        }

        var pts = new SKPoint[4];
        var iter = path.CreateRawIterator();
        (double X, double Y) cur = default, start = default;
        SKPathVerb verb;
        while ((verb = iter.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    cur = start = T(pts[0]);
                    break;
                case SKPathVerb.Line:
                    var lineTo = T(pts[1]);
                    AddContour(cur, lineTo);
                    cur = lineTo;
                    break;
                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                {
                    // Degree elevation to the cubic LilyPond's FreeType sees for a CFF
                    // face (declared substitution (2) in the class remarks). A conic's
                    // weight is dropped in the elevation — for the bundled CFF faces
                    // this branch should never run at all.
                    var q0 = cur;
                    var q1 = T(pts[1]);
                    var q2 = T(pts[2]);
                    var c1 = (X: q0.X + 2.0 / 3.0 * (q1.X - q0.X), Y: q0.Y + 2.0 / 3.0 * (q1.Y - q0.Y));
                    var c2 = (X: q2.X + 2.0 / 3.0 * (q1.X - q2.X), Y: q2.Y + 2.0 / 3.0 * (q1.Y - q2.Y));
                    cur = FlattenCubic(cur, c1, c2, q2, AddContour, AddBoth);
                    break;
                }
                case SKPathVerb.Cubic:
                    cur = FlattenCubic(cur, T(pts[1]), T(pts[2]), T(pts[3]), AddContour, AddBoth);
                    break;
                case SKPathVerb.Close:
                    // FreeType's decompose closes every contour back to its first point;
                    // Skia leaves the closing edge implicit in the verb.
                    if (cur != start)
                        AddContour(cur, start);
                    cur = start;
                    break;
            }
        }

        return (up.ToArray(), down.ToArray());
    }

    /// <summary>
    /// LILYPOND-REF: lily/freetype.cc:128-150 <c>Path_interpreter::curve3to</c> —
    /// <c>max(2, int(|end-start|/0.2))</c> steps, intermediate points as contour
    /// segments, the FINAL sub-segment as a both-sides <c>add_segment</c> (:147).
    /// </summary>
    private static (double X, double Y) FlattenCubic(
        (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
        (double X, double Y) p3,
        Action<(double X, double Y), (double X, double Y)> addContour,
        Action<(double X, double Y), (double X, double Y)> addBoth)
    {
        double len = Math.Sqrt((p3.X - p0.X) * (p3.X - p0.X) + (p3.Y - p0.Y) * (p3.Y - p0.Y));
        int quantization = Math.Max(2, (int)(len / 0.2));
        var cur = p0;
        for (int i = 1; i < quantization; i++)
        {
            double t = i / (double)quantization;
            double mt = 1 - t;
            double a = mt * mt * mt, b = 3 * mt * mt * t, c = 3 * mt * t * t, d = t * t * t;
            var pt = (X: a * p0.X + b * c1.X + c * c2.X + d * p3.X,
                      Y: a * p0.Y + b * c1.Y + c * c2.Y + d * p3.Y);
            addContour(cur, pt);
            cur = pt;
        }
        addBoth(cur, p3);
        return p3;
    }

    /// <summary>One sign-framed quad, endpoints normalized left-to-right; degenerate
    /// (Δx = 0) edges are dropped (LILYPOND-REF: lily/skyline.cc:441-453 internal_build_skyline —
    /// the segment ctor swaps each edge to x1 &lt; x2 and only pushes a Building when
    /// x1 &lt; x2 strictly).</summary>
    private static void AddQuad(List<double> quads, int sky,
        (double X, double Y) p1, (double X, double Y) p2)
    {
        var (lo, hi) = p1.X <= p2.X ? (p1, p2) : (p2, p1);
        if (hi.X <= lo.X)
            return;
        quads.Add(lo.X);
        quads.Add(sky * lo.Y);
        quads.Add(sky * hi.Y);
        quads.Add(hi.X);
    }

    /// <summary>
    /// The path's dominant winding in the transformed (Y-up) frame: signed shoelace area
    /// over the curve ENDPOINTS (winding needs no flattening), summed across contours —
    /// outer contours outweigh their counters, so the sign is the face's convention.
    /// Positive = counterclockwise, the PostScript/CFF convention LilyPond assumes for
    /// every non-TrueType face (LILYPOND-REF: lily/freetype.cc:193-195 get_font_format —
    /// "TrueType" picks Orientation::CW, everything else CCW).
    /// </summary>
    private static bool DominantWindingIsCcw(SKPath path, Func<SKPoint, (double X, double Y)> t)
    {
        double area2 = 0;
        var pts = new SKPoint[4];
        var iter = path.CreateRawIterator();
        (double X, double Y) cur = default, start = default;
        SKPathVerb verb;
        while ((verb = iter.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    cur = start = t(pts[0]);
                    break;
                case SKPathVerb.Line:
                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                case SKPathVerb.Cubic:
                {
                    int endIdx = verb == SKPathVerb.Line ? 1 : verb == SKPathVerb.Cubic ? 3 : 2;
                    var end = t(pts[endIdx]);
                    area2 += cur.X * end.Y - end.X * cur.Y;
                    cur = end;
                    break;
                }
                case SKPathVerb.Close:
                    area2 += cur.X * start.Y - start.X * cur.Y;
                    cur = start;
                    break;
            }
        }
        return area2 > 0;
    }
}
