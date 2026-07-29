#!/usr/bin/env python3
"""Extract accidental horizontal skylines from Emmentaler and emit a C# partial class.

Reads:  LilySharp.Core/Fonts/emmentaler-20.otf
Writes: LilySharp.Core/Svg/Layout/GlyphSkylinesGenerated.cs

WHY. LilyPond packs the accidentals of a chord with skyline-to-skyline nesting
(lily/accidental-placement.cc:412 `position_apes`,
`ape->horizontal_skylines_[RIGHT].distance(left_skyline, 0.1)`), where each
accidental's skyline is the glyph's REAL outline, not its bounding box. A box
packs sharps at 1.300 and flats at 1.120; LilyPond gets 1.284 and 0.964561,
because the sharp's notches and the flat's narrow lower stem let the neighbour
nest in. This file bakes those outline skylines so Lily# can pack the same way.

HOW LilyPond builds the skyline (verified against a live 2.26.0 dump, see below):
  * `Accidental_interface::horizontal_skylines` (lily/accidental.cc:48) calls
    `skylines_from_stencil(stencil, rotation, Y_AXIS)`.
  * that reaches `add_named_glyph_segments` (lily/stencil-integral.cc:534) ->
    `add_outline_to_skyline` (lily/freetype.cc:175): the glyph's FreeType outline
    is decomposed, cubics flattened to `max(2, len/0.2)` segments
    (freetype.cc:137), each edge classified LEFT/RIGHT by contour orientation
    (lazy-skyline-pair.hh:53). Emmentaler is CFF => Orientation::CCW.
  * the outline is scaled by `bbox / real_bbox` (stencil-integral.cc:557), where
    bbox is the FT-metrics box and real_bbox the outline box; for accidentals
    these coincide, so the effective scale is 1.0 and the skyline is simply the
    outline in staff spaces (1 ss = unitsPerEm/4 = 250 font units). A live dump
    of a flat's LEFT skyline bottoms at x=-0.108 (the OUTLINE left), not the LILC
    grob extent -0.12 — confirming scale 1.0, no LILC rescale.
  * flats and flatflats get one extra RIGHT building at x = stencil.right*0.375
    over the stencil's Y-extent (lily/accidental.cc:65-82) — but ONLY when the
    accidental is NOT parenthesized, so that branch lives at RUNTIME
    (AccidentalPlacement.GlyphSkylinePair), matching LilyPond's placement of it
    in horizontal_skylines rather than in the glyph data. This file bakes RAW
    outlines only.
  * a courtesy (cautionary) accidental's stencil embeds accidentals.leftparen /
    accidentals.rightparen (accidental.cc:33-43 parenthesize, add_at_edge with
    padding 0), and the skyline is built over that combined stencil — so the
    paren glyphs' raw outline skylines are baked here too and composed at
    runtime by shifting each to the accidental's LILC edge.

The classified segments are stored as sign-framed skyline BUILDINGS
(value = sky*x, RIGHT sky=+1, LEFT sky=-1) — the same representation
SkylineBuilding uses — so the C# side only loads numbers.

The live LilyPond dump this was calibrated against is committed and re-runnable:
audit/lp-geometry/probes/accidental-skyline.ly (the flat/sharp horizontal-skylines
plus the column gaps position_apes produces, and the AccidentalCautionary
skylines the runtime paren composition must reproduce).

Run after the bundled Emmentaler font is updated; CI should re-run and assert the
output is unchanged.
"""
from __future__ import annotations

import sys
from pathlib import Path

try:
    from fontTools.ttLib import TTFont
    from fontTools.pens.recordingPen import RecordingPen
    from fontTools.pens.boundsPen import BoundsPen, ControlBoundsPen
except ImportError:
    sys.stderr.write("fontTools not installed. Run: py -3.13 -m pip install fonttools\n")
    sys.exit(2)

STAFF_SPACE_UNITS = 250.0
# Emmentaler is a CFF (PostScript) font: contours wind counter-clockwise.
ORIENTATION_CCW = True

# Accidentals to bake: (csharp kind, feta glyph). RAW outlines only — the flat 0.375
# fattening is a RUNTIME branch (accidental.cc:65-82, skipped when parenthesized) and
# lives in AccidentalPlacement.GlyphSkylinePair, not in the glyph data.
ACCIDENTALS = [
    ("sharp",       "accidentals.sharp"),
    ("flat",        "accidentals.flat"),
    ("natural",     "accidentals.natural"),
    ("doubleSharp", "accidentals.doublesharp"),
    ("doubleFlat",  "accidentals.flatflat"),
]

# Parenthesis glyphs a courtesy accidental's stencil embeds
# (accidental.cc:33-43 parenthesize). Baked as raw outlines in their own glyph
# frame; the runtime composes them at the accidental's LILC edges (padding 0).
PARENS = [
    ("leftParen",  "accidentals.leftparen"),
    ("rightParen", "accidentals.rightparen"),
]

# Clefs, baked on the OTHER horizon axis: a clef's VERTICAL skylines.
#
# WHY. The same add_outline_to_skyline builds these (scm/define-grobs.scm:902 declares
# Clef.vertical-skylines from its stencil), and a staff's silhouette is where two staves
# meet. A pair of BOXES makes two facing clefs bind at the sum of their maxima; a pair of
# OUTLINES makes them bind lower, because the G clef's deepest ink is at x=1.84 and its
# highest at x=2.228. LilyPond dumped against the boxed port:
#   dist(upper DOWN, lower UP) = 7.210039  against  3.540000 + 3.776000 = 7.316000
# — a deficit of 0.105961, measured in audit/lp-geometry/probes/skyline-binding.ly.
#
# Keyed by the three glyphs SkylineBuilder.ClefInk names; the percussion clef borrows the
# C clef there and keeps doing so here, which is the same known approximation.
CLEFS = [
    ("G", "clefs.G"),
    ("F", "clefs.F"),
    ("C", "clefs.C"),
]

# fetaText dynamic letters, baked on the vertical axis like the clefs.
#
# WHY. DynamicText declares vertical-skylines from its stencil
# (scm/define-grobs.scm:1446 grob::always-vertical-skylines-from-stencil, and the
# DynamicLineSpanner wraps them via :1412-1413 from-element-stencils), and
# lily/side-position-interface.cc:353-358 measures the support distance POINTWISE
# against that outline (my_dim) — which is why a \f tucks beside a thin stem sliver
# while a \fff binds on it (audit/lp-geometry/probes/dynamic-support.ly, DSQ vs DMF).
# A text stencil's letters sit at pen positions = hmtx advances + GPOS kerns
# (audit/lp-geometry/probes/dynamic-text-x.ly measured every kern pair at the font's
# own sign and magnitude; the per-glyph Pango quantisation of the advance run stays a
# named residual family, NOT fitted), so each letter is baked in its own glyph frame
# and the runtime composes them at those pen positions. The letters are addressed by
# their bare ASCII cmap names, no "dynamics." prefix (same fact recorded in
# Extract-EmmentalerMetrics.py's DynamicLetter* block).
DYNAMICS = [(c, c) for c in "fmnprsz"]


def glyph_outline_scale(glyphset, glyph):
    """LilyPond's `scale` at lily/stencil-integral.cc:551-557 add_named_glyph_segments,
    COMPUTED rather than assumed to be one:

        Box bbox      = get_unscaled_indexed_char_dimensions (gidx);   // FT glyph metrics
        bbox.scale (magnification * design_size / units_per_EM);
        Box real_bbox = get_glyph_outline_bbox (gidx);                 // FT_Outline_Get_BBox
        Real scale    = bbox[X_AXIS].length () / real_bbox[X_AXIS].length ();

    `bbox` is FreeType's glyph metrics read under FT_LOAD_NO_SCALE
    (lily/freetype.cc:51-65 ly_FT_get_unscaled_indexed_char_dimensions reads
    m.horiBearingX / m.width), which for an unhinted CFF glyph FreeType fills from the
    CONTROL box — hence ControlBoundsPen. `real_bbox` is the exact outline box
    (lily/freetype.cc:67-88 uses FT_Outline_Get_BBox) — hence BoundsPen.

    `magnification * design_size / units_per_EM` is the plain unit conversion this file
    already applies as 1/STAFF_SPACE_UNITS, so it is factored out and only the RATIO of
    the two widths is returned: 1.0 when the control box and the outline box agree, which
    is what LilyPond's own comment at :549-550 says to expect and what the clef dumps in
    audit/lp-geometry/probes/skyline-binding.ly confirm to six digits."""
    ctrl = ControlBoundsPen(glyphset)
    exact = BoundsPen(glyphset)
    glyphset[glyph].draw(ctrl)
    glyphset[glyph].draw(exact)
    if ctrl.bounds is None or exact.bounds is None:
        return 1.0
    ctrl_w = ctrl.bounds[2] - ctrl.bounds[0]
    exact_w = exact.bounds[2] - exact.bounds[0]
    if exact_w == 0:
        return 1.0
    return ctrl_w / exact_w


def bezier_pt(p0, p1, p2, p3, t):
    mt = 1 - t
    a = mt * mt * mt
    b = 3 * mt * mt * t
    c = 3 * mt * t * t
    d = t * t * t
    return (a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0],
            a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1])


def outline_segments(glyphset, glyph, scale=1.0):
    """The glyph outline as (p1,p2) edges in staff spaces, cubics flattened exactly
    as lily/freetype.cc:128-149 does (max(2, len/0.2) steps, length in the target
    frame = staff spaces). Returns (contour_edges, both_side_edges); the final
    sub-segment of every cubic is a plain add_segment that feeds BOTH skylines
    (freetype.cc:147), matching LilyPond."""
    pen = RecordingPen()
    glyphset[glyph].draw(pen)
    contour = []       # classified by orientation
    both = []          # add_segment: contributes to both LEFT and RIGHT
    cur = None
    start = None

    def ss(pt):
        # LilyPond scales the TRANSFORM before decomposing (stencil-integral.cc:559-562
        # `local.scale (scale, scale)`), so the ratio is inside the frame the cubic
        # flattening measures its lengths in — it must be applied here, not afterwards.
        return (pt[0] * scale / STAFF_SPACE_UNITS, pt[1] * scale / STAFF_SPACE_UNITS)

    for cmd, pts in pen.value:
        if cmd == "moveTo":
            cur = ss(pts[0])
            start = cur
        elif cmd == "lineTo":
            nxt = ss(pts[0])
            contour.append((cur, nxt))
            cur = nxt
        elif cmd == "curveTo":
            # cubic bezier chain; fontTools splits chains so pts == (c1, c2, end)
            c1, c2, end = (ss(p) for p in pts)
            p0 = cur
            length = ((end[0] - p0[0]) ** 2 + (end[1] - p0[1]) ** 2) ** 0.5
            q = max(2, int(length / 0.2))
            prev = p0
            for i in range(1, q):
                pt = bezier_pt(p0, c1, c2, end, i / q)
                contour.append((prev, pt))
                prev = pt
            both.append((prev, end))  # freetype.cc:147 add_segment -> both sides
            cur = end
        elif cmd == "qCurveTo":
            # LilyPond substitutes a line for a conic (freetype.cc:122-127)
            nxt = ss(pts[-1])
            contour.append((cur, nxt))
            cur = nxt
        elif cmd == "closePath":
            if start is not None and cur != start:
                contour.append((cur, start))
            cur = start
    return contour, both


def classify(contour, both, horizon_axis=1):
    """Split edges into two sign-framed skyline building lists.
    LILYPOND-REF: lazy-skyline-pair.hh:53-65 add_contour_segment.

    `horizon_axis` is LilyPond's `a_`: 1 (Y) gives HORIZONTAL skylines and returns
    (LEFT sky=-1, RIGHT sky=+1); 0 (X) gives VERTICAL skylines and returns
    (DOWN sky=-1, UP sky=+1).  The classifier is one line of LilyPond either way —
    `(seg[LEFT][a_] > seg[RIGHT][a_]) == (orientation == CCW)` — but the SIDE it
    selects flips with the axis: for X that branch is UP (+1) and for Y it is
    LEFT (-1), which is why `first` below is not always the negative one.

    Each edge -> (start=hLo, startValue, endValue, end=hHi) where h is the horizon
    coordinate and value = sky * (the other coordinate).
    Degenerate (Δhorizon == 0) edges are dropped (skyline.cc:449 x1 < x2).

    ⚠️ THE TWO LISTS ARE NOT "THE TOP HALF" AND "THE BOTTOM HALF" OF THE GLYPH, AND THIS
    IS NOT A BUG TO FIX. The branch above sorts by CONTOUR DIRECTION, so an edge lands in
    the DOWN list because the contour runs left-to-right there, not because it is low. A
    glyph with inner contours (counters) or with two separate arms therefore puts edges
    with POSITIVE y into the DOWN list and edges with NEGATIVE y into the UP list -- the C
    clef does both, its DOWN list carrying buildings at y = +1.96. LilyPond produces
    exactly the same mixture (lily/include/lazy-skyline-pair.hh:53-65 add_contour_segment
    is the one line this reproduces), and the skyline RESOLVE is what picks the right
    building at each horizon coordinate afterwards. Filtering by sign here would delete
    real silhouette and would not match LilyPond.
    ⚠️ VERIFIED, not argued: audit/lp-geometry/probes/skyline-binding.ly dumps each clef's
    own vertical-skylines with ly:skyline->points, and the resolved profiles agree with
    these buildings to six digits on all three clefs."""
    h = horizon_axis          # horizon coordinate index
    o = 1 - horizon_axis      # the coordinate a building's value carries
    # (sky of the branch the classifier's TRUE selects, sky of the FALSE branch)
    true_sky, false_sky = (+1, -1) if horizon_axis == 0 else (-1, +1)
    true_side = []
    false_side = []

    def building(edge, sky):
        (p1, p2) = edge
        (lo, hi) = (p1, p2) if p1[h] <= p2[h] else (p2, p1)
        if hi[h] <= lo[h]:
            return None
        return (lo[h], sky * lo[o], sky * hi[o], hi[h])

    for edge in contour:
        (p1, p2) = edge
        cond = (p1[h] > p2[h]) == ORIENTATION_CCW
        if cond:
            b = building(edge, true_sky)
            if b:
                true_side.append(b)
        else:
            b = building(edge, false_sky)
            if b:
                false_side.append(b)
    for edge in both:
        bt = building(edge, true_sky)
        if bt:
            true_side.append(bt)
        bf = building(edge, false_sky)
        if bf:
            false_side.append(bf)
    # Return in (negative sky, positive sky) order regardless of axis, i.e.
    # (LEFT, RIGHT) for horizon Y and (DOWN, UP) for horizon X.
    return (false_side, true_side) if horizon_axis == 0 else (true_side, false_side)


def build(glyphset, glyph, horizon_axis=1):
    scale = glyph_outline_scale(glyphset, glyph)
    # Reported rather than silently applied: if a glyph ever comes out off 1.0, the number
    # LilyPond spaces it by is not the outline in staff spaces any more, and every dump
    # this file was calibrated against would have to be re-read.
    if abs(scale - 1.0) > 1e-12:
        sys.stderr.write(f"NOTE: {glyph} outline->stencil scale = {scale:.9f} (not 1)\n")
    contour, both = outline_segments(glyphset, glyph, scale)
    return classify(contour, both, horizon_axis)


def dynamic_kern_pairs(font, letters):
    """GPOS pair kerning among the fetaText dynamic letters, in staff spaces.

    The Emmentaler dynamic letters are addressed by their bare cmap names, so the
    glyph names ARE the letters. Pango applies these adjustments when it shapes a
    dynamic string (measured: audit/lp-geometry/probes/dynamic-text-x.ly — every
    kern pair lands with the font's sign and magnitude, plus the shaping
    quantisation the probe header names). Walks PairPos format 1 and 2, unwrapping
    extension lookups; only pairs where BOTH glyphs are dynamic letters are kept."""
    pairs = {}
    if "GPOS" not in font:
        return pairs
    glyphs = set(letters)
    for lookup in font["GPOS"].table.LookupList.Lookup:
        for st in lookup.SubTable:
            t = st.ExtSubTable if st.LookupType == 9 else st
            if t.LookupType != 2:
                continue
            if t.Format == 1:
                for gi, g1 in enumerate(t.Coverage.glyphs):
                    if g1 not in glyphs:
                        continue
                    for pvr in t.PairSet[gi].PairValueRecord:
                        if pvr.SecondGlyph not in glyphs:
                            continue
                        xa = getattr(pvr.Value1, "XAdvance", 0) if pvr.Value1 else 0
                        if xa:
                            pairs[(g1, pvr.SecondGlyph)] = xa / STAFF_SPACE_UNITS
            elif t.Format == 2:
                c1 = t.ClassDef1.classDefs
                c2 = t.ClassDef2.classDefs
                cov = set(t.Coverage.glyphs)
                for g1 in sorted(glyphs & cov):
                    for g2 in sorted(glyphs):
                        rec = t.Class1Record[c1.get(g1, 0)].Class2Record[c2.get(g2, 0)]
                        xa = getattr(rec.Value1, "XAdvance", 0) if rec.Value1 else 0
                        if xa:
                            pairs[(g1, g2)] = xa / STAFF_SPACE_UNITS
    return pairs


def fmt(v):
    return f"{v + 0.0:.6f}"


def emit_side(name, buildings):
    lines = [f"    private static readonly double[] {name} =", "    {"]
    # four numbers per building: start, startValue, endValue, end
    row = []
    for b in buildings:
        row.append("        " + ", ".join(fmt(x) for x in b) + ",")
    lines.extend(row)
    lines.append("    };")
    return lines


def main():
    repo = Path(__file__).resolve().parents[2]
    font_path = repo / "LilySharp.Core" / "Fonts" / "emmentaler-20.otf"
    out_path = repo / "LilySharp.Core" / "Svg" / "Layout" / "GlyphSkylinesGenerated.cs"
    if not font_path.exists():
        sys.stderr.write(f"Font not found: {font_path}\n")
        return 2

    font = TTFont(str(font_path))
    upem = font["head"].unitsPerEm
    if upem != 1000:
        sys.stderr.write(f"WARNING: unitsPerEm={upem}, expected 1000\n")
    glyphset = font.getGlyphSet()
    order = set(font.getGlyphOrder())

    L = []
    L.append("// Lily# - Music notation compiler")
    L.append("// AUTO-GENERATED by audit/scripts/Extract-EmmentalerSkylines.py — DO NOT EDIT MANUALLY.")
    L.append("// Re-run the script after the bundled Emmentaler font is updated.")
    L.append("// Source font: LilySharp.Core/Fonts/emmentaler-20.otf")
    L.append("//")
    L.append("// Accidental horizontal skylines, taken from each glyph's REAL outline the way")
    L.append("// LilyPond builds them: lily/accidental.cc:48 horizontal_skylines ->")
    L.append("// skylines_from_stencil -> add_outline_to_skyline (lily/freetype.cc:175), cubics")
    L.append("// flattened to max(2, len/0.2) segments, classified by contour orientation")
    L.append("// (Emmentaler is CFF => CCW). The effective outline->stencil scale is 1.0 for")
    L.append("// accidentals, so a skyline coordinate is just the outline in staff spaces")
    L.append("// (1 ss = unitsPerEm/4 = 250 font units). These are RAW outlines: the flat")
    L.append("// 0.375 fattening (accidental.cc:65-82, skipped when parenthesized) and the")
    L.append("// courtesy paren composition (accidental.cc:33-43 parenthesize) are runtime")
    L.append("// branches in AccidentalPlacement.GlyphSkylinePair, where LilyPond has them.")
    L.append("//")
    L.append("//")
    L.append("// Clef VERTICAL skylines, from the same outline flattening on the other horizon")
    L.append("// axis: scm/define-grobs.scm:902-927 gives Clef grob::always-vertical-skylines-from-stencil.")
    L.append("//")
    L.append("// The outline->stencil ratio lily/stencil-integral.cc:551-557 divides by,")
    L.append("//     scale = bbox[X_AXIS].length () / real_bbox[X_AXIS].length ()")
    L.append("// is COMPUTED here rather than assumed (glyph_outline_scale): bbox is FreeType's")
    L.append("// glyph metrics under FT_LOAD_NO_SCALE (freetype.cc:51-65")
    L.append("// ly_FT_get_unscaled_indexed_char_dimensions), i.e. the CONTROL box, and real_bbox")
    L.append("// is the exact outline box (freetype.cc:67-88 uses FT_Outline_Get_BBox). It comes")
    L.append("// out at exactly 1.0 for every glyph baked here -- which is what LilyPond's own")
    L.append("// comment at :549-550 predicts -- but it is a division in LilyPond, so it is a")
    L.append("// division here, and the generator writes a NOTE to stderr if any glyph ever")
    L.append("// leaves 1.0. Writing 1 instead would be folding an evaluated result.")
    L.append("// A pair of BOXES makes two facing clefs bind at the sum of their maxima; a pair of")
    L.append("// OUTLINES binds lower, because the G clef's deepest ink is at x=1.84 and its highest")
    L.append("// at x=2.228. Measured off LilyPond in audit/lp-geometry/probes/skyline-binding.ly:")
    L.append("// dist(upper DOWN, lower UP) = 7.210039 against 3.540000 + 3.776000 = 7.316000.")
    L.append("//")
    L.append("// fetaText dynamic letter VERTICAL skylines + GPOS kerns, for DynamicText's own")
    L.append("// profile (my_dim): scm/define-grobs.scm:1446 declares vertical-skylines from the")
    L.append("// stencil, and side-position-interface.cc:353-358 measures the support distance")
    L.append("// POINTWISE against it — a \\f tucks beside a thin stem sliver, a \\fff binds on it")
    L.append("// (audit/lp-geometry/probes/dynamic-support.ly DSQ vs DMF). Letters compose at pen")
    L.append("// positions = hmtx advance (DynamicLetter*Advance, Extract-EmmentalerMetrics.py) +")
    L.append("// DynamicLetterKern below; the composition X model and every kern pair are measured")
    L.append("// in audit/lp-geometry/probes/dynamic-text-x.ly, whose header also names the")
    L.append("// per-glyph Pango shaping quantisation (<= 0.0167 ss, both signs) that stays a")
    L.append("// residual family rather than being fitted.")
    L.append("//")
    L.append("// Each array is a flat list of skyline BUILDINGS, four doubles apiece:")
    L.append("//   start (horizon low), startValue (sky*other at horizon low),")
    L.append("//   endValue (sky*other at horizon high), end (horizon high)")
    L.append("// — the sign-framed form SkylineBuilding takes. The horizon is Y for the accidental")
    L.append("// pairs (sky = -1 LEFT, +1 RIGHT) and X for the clefs (sky = -1 DOWN, +1 UP).")
    L.append("//")
    L.append("// ⚠️ A LIST IS NOT ONE SIDE OF THE GLYPH, AND MIXED SIGNS ARE NOT A BUG. Edges are")
    L.append("// sorted by CONTOUR DIRECTION, not by which half of the glyph they sit in")
    L.append("// (lily/include/lazy-skyline-pair.hh:53-65 add_contour_segment), so a glyph with")
    L.append("// counters or with two arms puts edges with POSITIVE y into its DOWN array and")
    L.append("// edges with NEGATIVE y into its UP array. ClefSkyCD really does carry buildings")
    L.append("// at y = +1.96, and LilyPond's own lists carry the same ones; the skyline RESOLVE")
    L.append("// picks the right building at each x. Dropping them by sign would delete real")
    L.append("// silhouette. Checked against LilyPond's ly:skyline->points dump for all three")
    L.append("// clefs (audit/lp-geometry/probes/skyline-binding.ly): six digits, every vertex.")
    L.append("")
    L.append("namespace LilySharp.Core.Svg.Layout;")
    L.append("")
    L.append("internal static partial class GlyphMetrics")
    L.append("{")

    kinds = []
    for kind, glyph in ACCIDENTALS + PARENS:
        if glyph not in order:
            sys.stderr.write(f"ERROR: glyph name not in font: {glyph}\n")
            return 1
        left, right = build(glyphset, glyph)
        cap = kind[0].upper() + kind[1:]
        L.append(f"    // ===== {kind} ({glyph}): {len(left)} LEFT + {len(right)} RIGHT buildings =====")
        L.extend(emit_side(f"AccSky{cap}L", left))
        L.extend(emit_side(f"AccSky{cap}R", right))
        L.append("")
        kinds.append((kind, cap))

    clef_kinds = []
    for kind, glyph in CLEFS:
        if glyph not in order:
            sys.stderr.write(f"ERROR: glyph name not in font: {glyph}\n")
            return 1
        down, up = build(glyphset, glyph, horizon_axis=0)
        L.append(f"    // ===== clef {kind} ({glyph}): {len(down)} DOWN + {len(up)} UP buildings =====")
        L.extend(emit_side(f"ClefSky{kind}D", down))
        L.extend(emit_side(f"ClefSky{kind}U", up))
        L.append("")
        clef_kinds.append(kind)

    dyn_kinds = []
    for kind, glyph in DYNAMICS:
        if glyph not in order:
            sys.stderr.write(f"ERROR: glyph name not in font: {glyph}\n")
            return 1
        down, up = build(glyphset, glyph, horizon_axis=0)
        cap = kind.upper()
        L.append(f"    // ===== dynamic letter '{kind}' ({glyph}): {len(down)} DOWN + {len(up)} UP buildings =====")
        L.extend(emit_side(f"DynSky{cap}D", down))
        L.extend(emit_side(f"DynSky{cap}U", up))
        L.append("")
        dyn_kinds.append((kind, cap))

    kerns = dynamic_kern_pairs(font, [g for _, g in DYNAMICS])

    acc_kinds = [(k, c) for (k, c) in kinds if k not in ("leftParen", "rightParen")]

    # Loader: build the HorizontalSkyline pair for a kind, cached.
    L.append("    /// <summary>The (LEFT, RIGHT) horizontal skyline pair for an accidental kind,")
    L.append("    /// in the glyph's own frame (X from the glyph origin, Y centred on the note).</summary>")
    L.append("    public static (HorizontalSkyline Left, HorizontalSkyline Right) AccidentalSkylinePair(string kind) => kind switch")
    L.append("    {")
    for kind, cap in acc_kinds:
        L.append(f"        \"{kind}\" => (AccSkyPair{cap}.Left, AccSkyPair{cap}.Right),")
    L.append("        // naturals-as-fallback: an unknown kind draws the natural sign.")
    L.append("        _ => (AccSkyPairNatural.Left, AccSkyPairNatural.Right),")
    L.append("    };")
    L.append("")
    L.append("    /// <summary>The raw outline skyline pair of accidentals.leftparen /")
    L.append("    /// accidentals.rightparen, in each paren glyph's own frame. A courtesy")
    L.append("    /// accidental's stencil embeds these at its LILC edges (padding 0), and the")
    L.append("    /// runtime composition mirrors that placement.")
    L.append("    /// LILYPOND-REF: lily/accidental.cc:33-43 parenthesize.</summary>")
    L.append("    public static (HorizontalSkyline Left, HorizontalSkyline Right) AccidentalParenSkylinePair(bool leftParen) =>")
    L.append("        leftParen ? (AccSkyPairLeftParen.Left, AccSkyPairLeftParen.Right)")
    L.append("                  : (AccSkyPairRightParen.Left, AccSkyPairRightParen.Right);")
    L.append("")
    L.append("    /// <summary>The (DOWN, UP) VERTICAL skyline of a clef glyph, as raw sign-framed")
    L.append("    /// buildings in the glyph's own frame (X from the glyph origin, Y from the line the")
    L.append("    /// glyph sits on). Raw rather than a built skyline because every seat wants it at a")
    L.append("    /// different x, y and staff size, and a <c>VerticalSkyline</c> is mutable.")
    L.append("    /// LILYPOND-REF: scm/define-grobs.scm:902-927 <c>grob::always-vertical-skylines-from-stencil</c>")
    L.append("    /// is what the Clef declares, hence")
    L.append("    /// lily/stencil-integral.cc:562 add_named_glyph_segments and")
    L.append("    /// lily/freetype.cc:174-202 Path_interpreter, run by ly_FT_add_outline_to_skyline.</summary>")
    L.append("    public static (double[] Down, double[] Up) ClefVerticalSkylineQuads(string kind) => kind switch")
    L.append("    {")
    for kind in clef_kinds:
        if kind == "G":
            continue
        L.append(f"        \"{kind}\" => (ClefSky{kind}D, ClefSky{kind}U),")
    L.append("        // The G clef is the fallback for the same reason it is in ClefInk.")
    L.append("        _ => (ClefSkyGD, ClefSkyGU),")
    L.append("    };")
    L.append("")
    L.append("    /// <summary>The (DOWN, UP) VERTICAL skyline of one fetaText dynamic letter, as raw")
    L.append("    /// sign-framed buildings in the glyph's own frame (X from the letter's PEN origin, Y")
    L.append("    /// from the baseline) — default when the char is not one of the seven the encoding")
    L.append("    /// draws dynamics from (the caller falls back, as TryGetDynamicInk does). A label's")
    L.append("    /// profile is these letters composed at pen positions = advance + kern")
    L.append("    /// (<c>DynamicLetter*Advance</c> / <see cref=\"DynamicLetterKern\"/>).")
    L.append("    /// LILYPOND-REF: scm/define-grobs.scm:1446 DynamicText <c>grob::always-vertical-skylines-from-stencil</c>,")
    L.append("    /// :1412-1413 DynamicLineSpanner <c>from-element-stencils</c>; the outline walk is")
    L.append("    /// lily/stencil-integral.cc:562 add_named_glyph_segments and")
    L.append("    /// lily/freetype.cc:174-202 Path_interpreter, as for the clefs above.</summary>")
    L.append("    public static (double[] Down, double[] Up) DynamicLetterVerticalSkylineQuads(char c) => c switch")
    L.append("    {")
    for kind, cap in dyn_kinds:
        L.append(f"        '{kind}' => (DynSky{cap}D, DynSky{cap}U),")
    L.append("        _ => default,")
    L.append("    };")
    L.append("")
    L.append("    /// <summary>GPOS pair kern between two adjacent fetaText dynamic letters, in staff")
    L.append("    /// spaces (negative pulls the second letter in; 0 for every pair the font does not")
    L.append("    /// kern). Pango applies exactly these when it shapes a dynamic string, so a label's")
    L.append("    /// pen positions are advances plus these — measured against LilyPond in")
    L.append("    /// audit/lp-geometry/probes/dynamic-text-x.ly (every pair the font's sign and")
    L.append("    /// magnitude; the per-glyph shaping quantisation stays a named residual family).</summary>")
    L.append("    public static double DynamicLetterKern(char first, char second) => (first, second) switch")
    L.append("    {")
    for (g1, g2), v in sorted(kerns.items()):
        L.append(f"        ('{g1}', '{g2}') => {fmt(v)},")
    L.append("        _ => 0.0,")
    L.append("    };")
    L.append("")
    for kind, cap in kinds:
        L.append(f"    private static readonly (HorizontalSkyline Left, HorizontalSkyline Right) AccSkyPair{cap} =")
        L.append(f"        (HorizontalSkyline.FromSignedBuildings(HorizontalDirection.Left, AccSky{cap}L),")
        L.append(f"         HorizontalSkyline.FromSignedBuildings(HorizontalDirection.Right, AccSky{cap}R));")
    L.append("}")
    L.append("")

    out_path.write_text("\n".join(L), encoding="utf-8")
    print(f"Wrote {out_path} ({len(ACCIDENTALS)} accidentals + {len(PARENS)} parens + "
          f"{len(CLEFS)} clefs + {len(DYNAMICS)} dynamic letters, {len(kerns)} kern pairs)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
