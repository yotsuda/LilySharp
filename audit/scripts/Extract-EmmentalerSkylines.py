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


def bezier_pt(p0, p1, p2, p3, t):
    mt = 1 - t
    a = mt * mt * mt
    b = 3 * mt * mt * t
    c = 3 * mt * t * t
    d = t * t * t
    return (a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0],
            a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1])


def outline_segments(glyphset, glyph):
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
        return (pt[0] / STAFF_SPACE_UNITS, pt[1] / STAFF_SPACE_UNITS)

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


def classify(contour, both):
    """Split edges into LEFT/RIGHT skyline building lists, sign-framed.
    LILYPOND-REF: lazy-skyline-pair.hh:53-65 add_contour_segment (horizon = Y).
    Each edge -> (start=yLo, startValue, endValue, end=yHi) with value = sky*x.
    Degenerate (Δy == 0) edges are dropped (skyline.cc:449 x1 < x2)."""
    left = []   # sky = -1
    right = []  # sky = +1

    def building(edge, sky):
        (p1, p2) = edge
        (lo, hi) = (p1, p2) if p1[1] <= p2[1] else (p2, p1)
        if hi[1] <= lo[1]:
            return None
        return (lo[1], sky * lo[0], sky * hi[0], hi[1])

    for edge in contour:
        (p1, p2) = edge
        cond = (p1[1] > p2[1]) == ORIENTATION_CCW
        if cond:
            b = building(edge, -1)
            if b:
                left.append(b)
        else:
            b = building(edge, +1)
            if b:
                right.append(b)
    for edge in both:
        bl = building(edge, -1)
        if bl:
            left.append(bl)
        br = building(edge, +1)
        if br:
            right.append(br)
    return left, right


def build(glyphset, glyph):
    contour, both = outline_segments(glyphset, glyph)
    return classify(contour, both)


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
    L.append("// Each array is a flat list of skyline BUILDINGS, four doubles apiece:")
    L.append("//   start (yLow), startValue (sky*x at yLow), endValue (sky*x at yHigh), end (yHigh)")
    L.append("// with sky = +1 for RIGHT, -1 for LEFT — the sign-framed form SkylineBuilding takes.")
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
    for kind, cap in kinds:
        L.append(f"    private static readonly (HorizontalSkyline Left, HorizontalSkyline Right) AccSkyPair{cap} =")
        L.append(f"        (HorizontalSkyline.FromSignedBuildings(HorizontalDirection.Left, AccSky{cap}L),")
        L.append(f"         HorizontalSkyline.FromSignedBuildings(HorizontalDirection.Right, AccSky{cap}R));")
    L.append("}")
    L.append("")

    out_path.write_text("\n".join(L), encoding="utf-8")
    print(f"Wrote {out_path} ({len(ACCIDENTALS)} accidentals + {len(PARENS)} parens)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
