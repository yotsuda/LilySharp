#!/usr/bin/env python3
"""Extract glyph metrics from Emmentaler font and emit a C# partial class.

Reads:  LilySharp.Core/Fonts/emmentaler-20.otf
Writes: LilySharp.Core/Svg/Layout/GlyphMetricsGenerated.cs

The generated file holds every glyph metric (BBox / advance width) that can
be derived directly from the font binary. Hand-tuned constants — engraving
thicknesses, spacing heuristics, LP grob defaults — stay in GlyphMetrics.cs.

BBoxes come from the font's embedded LILC table, which is where LilyPond itself
reads them (lily/open-type-font.cc:289 load_scheme_table("LILC"), :390-408
get_indexed_char_dimensions); the raw outline is a fallback for fonts without one.
The two differ: the outline is what the curves happen to enclose, LILC is the
dimension METAFONT designed. For noteheads.s0 that is 1.9640 against 1.962000, and
1.962000 is what LilyPond lays out with — so taking the outline made Lily# miss
LilyPond by ~0.002 ss on every measure, in a way no formula could account for.

Glyphs are addressed by their feta NAME, never by code point: Emmentaler's private-use
assignment shifts whenever glyphs are added (2.26.0 moved 73 of the names Lily# uses).
LilyPond addresses them the same way. A name absent from the font is a fatal error here.

Run after the bundled Emmentaler font is updated. CI should re-run this and assert the
output is unchanged (else the font drifted).
"""
from __future__ import annotations

import re
import sys
import zlib
from dataclasses import dataclass
from pathlib import Path

try:
    from fontTools.ttLib import TTFont
    from fontTools.pens.boundsPen import BoundsPen
except ImportError:
    sys.stderr.write("fontTools not installed. Run: pip install fonttools\n")
    sys.exit(2)


# 1 staff space = unitsPerEm / 4 = 250 font units (Emmentaler-20 convention).
STAFF_SPACE_UNITS = 250.0


@dataclass(frozen=True)
class GlyphSpec:
    """Single glyph to extract.

    csharp_name: name of the C# constant (e.g. "NoteheadBlack")
    glyph:       feta glyph name in the font (e.g. "noteheads.s2")
    summary:     XML doc <summary>
    feta_ref:    LILYPOND-REF citation pointing into mf/feta-*.mf
    source:      which dimension LilyPond reads for THIS glyph — "lilc" (the default,
                 the METAFONT-designed bbox that ly:font-get-glyph lays out with) or
                 "outline" (the curves' own bounds, which is what the TEXT path
                 measures). See the dynamics block below for why the two differ.
    """

    csharp_name: str
    glyph: str
    summary: str
    feta_ref: str
    source: str = "lilc"


# --- BBox glyphs (full bounds extracted) ---
BBOX_GLYPHS: list[GlyphSpec] = [
    # Noteheads
    GlyphSpec("NoteheadWhole",       "noteheads.s0", "Whole notehead",         "mf/feta-noteheads.mf — noteheads.s0"),
    GlyphSpec("NoteheadHalf",        "noteheads.s1", "Half (hollow) notehead", "mf/feta-noteheads.mf — noteheads.s1"),
    GlyphSpec("NoteheadBlack",       "noteheads.s2", "Black (filled) notehead", "mf/feta-noteheads.mf — noteheads.s2"),
    # Accidentals
    GlyphSpec("AccidentalSharp",       "accidentals.sharp", "Sharp accidental",        "mf/feta-accidentals.mf — accidentals.sharp"),
    GlyphSpec("AccidentalFlat",        "accidentals.flat", "Flat accidental",         "mf/feta-flats.mf — accidentals.flat"),
    GlyphSpec("AccidentalNatural",     "accidentals.natural", "Natural accidental",      "mf/feta-accidentals.mf — accidentals.natural"),
    GlyphSpec("AccidentalDoubleSharp", "accidentals.doublesharp", "Double sharp accidental", "mf/feta-accidentals.mf — accidentals.doublesharp"),
    GlyphSpec("AccidentalDoubleFlat",  "accidentals.flatflat", "Double flat accidental",  "mf/feta-flats.mf — accidentals.flatflat"),
    # Accidental parentheses: ink-extent glyphs designed for extent
    # juxtaposition (leftparen draws BEHIND its origin with advance 0)
    GlyphSpec("AccidentalLeftParen",  "accidentals.leftparen", "Left accidental parenthesis (ink left of origin, advance 0)",  "mf/feta-parenthesis.mf — accidentals.leftparen"),
    GlyphSpec("AccidentalRightParen", "accidentals.rightparen", "Right accidental parenthesis", "mf/feta-parenthesis.mf — accidentals.rightparen"),
    # Flags
    GlyphSpec("Flag8thUp",    "flags.u3", "8th note flag (upward stem)",     "mf/feta-flags.mf — flags.u3"),
    GlyphSpec("Flag8thDown",  "flags.d3", "8th note flag (downward stem)",   "mf/feta-flags.mf — flags.d3"),
    GlyphSpec("Flag16thUp",   "flags.u4", "16th note flag (upward stem)",    "mf/feta-flags.mf — flags.u4"),
    GlyphSpec("Flag16thDown", "flags.d4", "16th note flag (downward stem)",  "mf/feta-flags.mf — flags.d4"),
    # Augmentation dot
    GlyphSpec("AugmentationDot", "dots.dot", "Augmentation dot", "mf/feta-noteheads.mf — dots.dot"),
    # Ornament / mark glyphs (ink extents for outside-staff stacking)
    GlyphSpec("OrnTrillGlyph", "scripts.trill", "Trill ornament", "mf/feta-scripts.mf — scripts.trill"),
    GlyphSpec("OrnTurnGlyph", "scripts.turn", "Turn ornament", "mf/feta-scripts.mf — scripts.turn"),
    GlyphSpec("OrnReverseTurnGlyph", "scripts.reverseturn", "Inverted (reverse) turn ornament", "mf/feta-scripts.mf — scripts.reverseturn"),
    GlyphSpec("OrnPrallGlyph", "scripts.prall", "Prall (upper mordent) ornament", "mf/feta-scripts.mf — scripts.prall"),
    GlyphSpec("OrnMordentGlyph", "scripts.mordent", "Mordent (lower mordent) ornament", "mf/feta-scripts.mf — scripts.mordent"),
    GlyphSpec("OrnPrallPrallGlyph", "scripts.prallprall", "Prall-prall / prall-triller ornament", "mf/feta-scripts.mf — scripts.prallprall"),
    GlyphSpec("MarkSegno", "scripts.segno", "Segno mark", "mf/feta-scripts.mf — scripts.segno"),
    GlyphSpec("MarkCoda", "scripts.coda", "Coda mark", "mf/feta-scripts.mf — scripts.coda"),
    # Clefs (ink extents; prefix glyphs seed the outside-staff occupancy).
    GlyphSpec("ClefG", "clefs.G", "G (treble) clef", "mf/feta-clefs.mf — clefs.G"),
    GlyphSpec("ClefF", "clefs.F", "F (bass) clef", "mf/feta-clefs.mf — clefs.F"),
    GlyphSpec("ClefC", "clefs.C", "C (alto/tenor) clef", "mf/feta-clefs.mf — clefs.C"),
    # Change clefs are their OWN glyphs, not the full clef scaled — see Clef::calc_glyph_name
    # appending "_change". Their width sets the gap after a mid-measure clef change, so it
    # has to be a real metric rather than a fraction of the full clef's.
    GlyphSpec("ClefGChange", "clefs.G_change", "G (treble) change clef", "mf/feta-clefs.mf — clefs.G_change"),
    GlyphSpec("ClefFChange", "clefs.F_change", "F (bass) change clef", "mf/feta-clefs.mf — clefs.F_change"),
    GlyphSpec("ClefCChange", "clefs.C_change", "C (alto/tenor) change clef", "mf/feta-clefs.mf — clefs.C_change"),
    # Rests (ink extents; used to place augmentation dots after the glyph
    # and to centre the church-rest combination of a multi-measure rest)
    GlyphSpec("RestLonga",       "rests.M2", "Longa (4-measure) rest",      "mf/feta-rests.mf — rests.M2"),
    GlyphSpec("RestDoubleWhole", "rests.M1", "Double-whole (breve) rest",   "mf/feta-rests.mf — rests.M1"),
    GlyphSpec("RestWhole",   "rests.0", "Whole rest",   "mf/feta-rests.mf — rests.0"),
    GlyphSpec("RestHalf",    "rests.1", "Half rest",    "mf/feta-rests.mf — rests.1"),
    GlyphSpec("RestQuarter", "rests.2", "Quarter rest", "mf/feta-rests.mf — rests.2"),
    GlyphSpec("Rest8th",     "rests.3", "8th rest",     "mf/feta-rests.mf — rests.3"),
    GlyphSpec("Rest16th",    "rests.4", "16th rest",    "mf/feta-rests.mf — rests.4"),
    GlyphSpec("Rest32nd",    "rests.5", "32nd rest",    "mf/feta-rests.mf — rests.5"),
    GlyphSpec("Rest64th",    "rests.6", "64th rest",    "mf/feta-rests.mf — rests.6"),
    GlyphSpec("Rest128th",   "rests.7", "128th rest",   "mf/feta-rests.mf — rests.7"),
    # Articulations
    GlyphSpec("ArticStaccato",      "scripts.staccato", "Staccato dot articulation",       "mf/feta-scripts.mf — scripts.staccato"),
    GlyphSpec("ArticAccent",        "scripts.sforzato", "Accent / sforzato articulation",  "mf/feta-scripts.mf — scripts.sforzato"),
    GlyphSpec("ArticTenuto",        "scripts.tenuto", "Tenuto articulation",             "mf/feta-scripts.mf — scripts.tenuto"),
    GlyphSpec("ArticMarcatoAbove",  "scripts.umarcato", "Marcato above (upward V)",        "mf/feta-scripts.mf — scripts.umarcato"),
    GlyphSpec("ArticMarcatoBelow",  "scripts.dmarcato", "Marcato below (downward V)",      "mf/feta-scripts.mf — scripts.dmarcato"),
    GlyphSpec("FermataAboveGlyph",  "scripts.ufermata", "Fermata above",                   "mf/feta-scripts.mf — scripts.ufermata"),
    GlyphSpec("FermataBelowGlyph",  "scripts.dfermata", "Fermata below",                   "mf/feta-scripts.mf — scripts.dfermata"),
    GlyphSpec("ArticStaccatissimoAboveGlyph", "scripts.ustaccatissimo", "Staccatissimo above (dagger)", "mf/feta-scripts.mf — scripts.ustaccatissimo"),
    GlyphSpec("ArticStaccatissimoBelowGlyph", "scripts.dstaccatissimo", "Staccatissimo below (dagger)", "mf/feta-scripts.mf — scripts.dstaccatissimo"),
    # LilyPond 2.26.0 split the bowing marks into a direction pair (scm/script.scm:453,
    # :88); 2.24.4 drew one glyph both ways and the single glyph is gone from the font.
    GlyphSpec("ArticUpBowAboveGlyph",   "scripts.uupbow", "Up-bow above (V)",   "mf/feta-scripts.mf — scripts.uupbow"),
    GlyphSpec("ArticUpBowBelowGlyph",   "scripts.dupbow", "Up-bow below (V)",   "mf/feta-scripts.mf — scripts.dupbow"),
    GlyphSpec("ArticDownBowAboveGlyph", "scripts.udownbow", "Down-bow above (frog)", "mf/feta-scripts.mf — scripts.udownbow"),
    GlyphSpec("ArticDownBowBelowGlyph", "scripts.ddownbow", "Down-bow below (frog)", "mf/feta-scripts.mf — scripts.ddownbow"),
    GlyphSpec("ArticFlageoletGlyph", "scripts.flageolet", "Flageolet / harmonic circle",    "mf/feta-scripts.mf — scripts.flageolet"),
    # --- Dynamic letters (fetaText encoding) ---
    # A DynamicText grob is TEXT, not a glyph lookup: scm/define-grobs.scm:1438 gives it
    # (font-encoding . fetaText) and :1445 (stencil . ly:text-interface::print), so its
    # stencil is built by Modified_font_metric::text_stencil (lily/modified-font-metric.cc
    # :125-143) and measured by Pango over the FreeType outline. LILC is read ONLY by
    # get_indexed_char_dimensions (lily/open-type-font.cc:372-409), which is the GLYPH
    # path; the text path never calls it. So the source here follows from which function
    # LilyPond runs, not from which number happens to fit — and the two differ far past
    # rounding:
    #
    #     glyph   LILC bbox Y          outline bbox Y      LilyPond 2.26.0 reports
    #     f       (-0.5834 . 2.0066)   (-0.692 . 1.896)    (-0.692002 . 1.896021)
    #     p       (-0.5834 . 1.1666)   (-0.584 . 1.168)    (-0.584004 . 1.168008)
    #     m       ( 0.0    . 1.1666)   (-0.028 . 1.196)    (see mp below)
    #
    # Confirmed (not derived) by asking the grob itself on three independent letter sets:
    # \mp reports (-0.584004 . 1.196016), the union of the OUTLINE p and m, unreachable
    # from LILC. A multi-letter dynamic is the union of its letters' boxes.
    # The residual +2e-5 is Pango's own quantisation of the outline and stays named rather
    # than fitted (HANDOFF 5.2.1 (5)); Lily# has no Pango.
    #
    # These are the seven letters the fetaText encoding draws dynamics from; they are
    # addressed by the bare ASCII name, not a "dynamics." prefix.
    GlyphSpec("DynamicLetterF", "f", "Dynamic letter 'f' (fetaText)", "mf/feta-dynamics.mf — f", source="outline"),
    GlyphSpec("DynamicLetterM", "m", "Dynamic letter 'm' (fetaText)", "mf/feta-dynamics.mf — m", source="outline"),
    GlyphSpec("DynamicLetterN", "n", "Dynamic letter 'n' (fetaText)", "mf/feta-dynamics.mf — n", source="outline"),
    GlyphSpec("DynamicLetterP", "p", "Dynamic letter 'p' (fetaText)", "mf/feta-dynamics.mf — p", source="outline"),
    GlyphSpec("DynamicLetterR", "r", "Dynamic letter 'r' (fetaText)", "mf/feta-dynamics.mf — r", source="outline"),
    GlyphSpec("DynamicLetterS", "s", "Dynamic letter 's' (fetaText)", "mf/feta-dynamics.mf — s", source="outline"),
    GlyphSpec("DynamicLetterZ", "z", "Dynamic letter 'z' (fetaText)", "mf/feta-dynamics.mf — z", source="outline"),
]

# --- Advance-only glyphs (just the horizontal advance width) ---
ADVANCE_GLYPHS: list[GlyphSpec] = [
    # Clefs
    GlyphSpec("GClefAdvance",     "clefs.G", "G (treble) clef advance width",  "mf/feta-clefs.mf — clefs.G"),
    GlyphSpec("FClefAdvance",     "clefs.F", "F (bass) clef advance width",    "mf/feta-clefs.mf — clefs.F"),
    GlyphSpec("CClefAdvance",     "clefs.C", "C (alto/tenor) clef advance width", "mf/feta-clefs.mf — clefs.C"),
    # Time signature digits (fattened)
    GlyphSpec("TimeSigDigit0Advance", "fattened.zero", "Time signature '0' digit advance width", "mf/feta-numbers.mf — fattened.zero"),
    GlyphSpec("TimeSigDigit1Advance", "fattened.one", "Time signature '1' digit advance width", "mf/feta-numbers.mf — fattened.one"),
    GlyphSpec("TimeSigDigit2Advance", "fattened.two", "Time signature '2' digit advance width", "mf/feta-numbers.mf — fattened.two"),
    GlyphSpec("TimeSigDigit3Advance", "fattened.three", "Time signature '3' digit advance width", "mf/feta-numbers.mf — fattened.three"),
    GlyphSpec("TimeSigDigit4Advance", "fattened.four", "Time signature '4' digit advance width", "mf/feta-numbers.mf — fattened.four"),
    GlyphSpec("TimeSigDigit5Advance", "fattened.five", "Time signature '5' digit advance width", "mf/feta-numbers.mf — fattened.five"),
    GlyphSpec("TimeSigDigit6Advance", "fattened.six", "Time signature '6' digit advance width", "mf/feta-numbers.mf — fattened.six"),
    GlyphSpec("TimeSigDigit7Advance", "fattened.seven", "Time signature '7' digit advance width", "mf/feta-numbers.mf — fattened.seven"),
    GlyphSpec("TimeSigDigit8Advance", "fattened.eight", "Time signature '8' digit advance width", "mf/feta-numbers.mf — fattened.eight"),
    GlyphSpec("TimeSigDigit9Advance", "fattened.nine", "Time signature '9' digit advance width", "mf/feta-numbers.mf — fattened.nine"),
]


def load_lilc_bboxes(font) -> tuple[dict[str, tuple[float, float, float, float]], float]:
    """Parse the font's LILC table into {glyph_name: (left, bottom, right, top)} in staff
    spaces. This is the SAME per-glyph metric LilyPond itself reads — see
    lily/open-type-font.cc:289 load_scheme_table("LILC") and :390-408
    get_indexed_char_dimensions, which returns this stored bbox in preference to the raw
    glyph outline. The LILC values are in the feta design unit; the LILY table's staff_space
    (5 for emmentaler-20 = design_size/4) converts them to staff spaces. Empty if no LILC.

    LilyPond 2.26.0 stores the table zlib-COMPRESSED — it more than halves the font — and
    open-type-font.cc:78-123 inflates it, falling back to the raw bytes when inflate says
    the data was not compressed. Doing the same here matters more than it looks: reading a
    2.26.0 font without inflating finds no bboxes at all, and this script would quietly drop
    to the outline fallback, which is exactly the non-LilyPond metric the LILC switch was
    made to get rid of."""
    keys = font.reader.keys()
    if "LILC" not in keys or "LILY" not in keys:
        return {}, 0.0
    lily = font.getTableData("LILY").decode("latin-1")
    m = re.search(r"staff_space\s*\.\s*([0-9]+(?:\.[0-9]+)?)", lily)
    staff_space = float(m.group(1)) if m else 5.0
    raw = font.getTableData("LILC")
    try:
        raw = zlib.decompress(raw)
    except zlib.error:
        pass  # not compressed — LilyPond treats that as legal too
    lilc = raw.decode("latin-1")
    pat = re.compile(r"\(([^\s()]+)\s*\.\s*\(\(bbox\s*\.\s*\(([-0-9.eE ]+)\)", re.S)
    out: dict[str, tuple[float, float, float, float]] = {}
    for gm in pat.finditer(lilc):
        vals = [float(x) / staff_space for x in gm.group(2).split()]
        if len(vals) == 4:
            out[gm.group(1)] = (vals[0], vals[1], vals[2], vals[3])
    return out, staff_space


def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    font_path = repo / "LilySharp.Core" / "Fonts" / "emmentaler-20.otf"
    out_path = repo / "LilySharp.Core" / "Svg" / "Layout" / "GlyphMetricsGenerated.cs"

    if not font_path.exists():
        sys.stderr.write(f"Font not found: {font_path}\n")
        return 2

    font = TTFont(str(font_path))
    upem = font["head"].unitsPerEm
    if upem != 1000:
        sys.stderr.write(f"WARNING: unitsPerEm={upem}, expected 1000 (Emmentaler convention)\n")
    glyphSet = font.getGlyphSet()
    hmtx = font["hmtx"]
    order = set(font.getGlyphOrder())
    # Code points are for documentation only; the glyph name is what selects the glyph.
    reverse_cmap: dict[str, int] = {}
    for codepoint, glyph in font.getBestCmap().items():
        reverse_cmap.setdefault(glyph, codepoint)
    # LP reads glyph bboxes from the font's LILC table, not the raw outline.
    lilc_bboxes, _staff_space = load_lilc_bboxes(font)
    if not lilc_bboxes:
        sys.stderr.write("WARNING: font has no LILC table; falling back to glyph outlines\n")

    missing = [s.glyph for s in (*BBOX_GLYPHS, *ADVANCE_GLYPHS) if s.glyph not in order]
    if missing:
        for glyph in missing:
            sys.stderr.write(f"ERROR: glyph name not in font: {glyph}\n")
        return 1

    def fmt(v: float) -> str:
        # SIX decimals. Four was enough while these values were only ever compared with each
        # other, but the LP fidelity corpus holds them against LilyPond, which speaks in six
        # (a note head is 1.304200, not 1.3040) — and rounding to four put a residual of
        # 2e-4..2e-3 under several ledger entries with no defect behind it.
        # `+ 0.0` folds LILC's signed zero: it stores -0.000000 for a left edge on the
        # origin, which is the same number but reads like a defect in the output.
        return f"{v + 0.0:.6f}"

    def cite(spec: GlyphSpec) -> str:
        codepoint = reverse_cmap.get(spec.glyph)
        where = f"U+{codepoint:04X}" if codepoint is not None else "unmapped"
        return f"{spec.feta_ref} ({spec.glyph} = {where} in this build)"

    lines: list[str] = []
    lines.append("// Lily# - Music notation compiler")
    lines.append("// AUTO-GENERATED by audit/scripts/Extract-EmmentalerMetrics.py — DO NOT EDIT MANUALLY.")
    lines.append("// Re-run the script after the bundled Emmentaler font is updated.")
    lines.append("// Source font: LilySharp.Core/Fonts/emmentaler-20.otf")
    lines.append(f"// 1 staff space = unitsPerEm / 4 = {STAFF_SPACE_UNITS:.0f} font units")
    lines.append("//")
    lines.append("// BBoxes come from the font's LILC table, which is what LilyPond reads")
    lines.append("// (lily/open-type-font.cc:289, :390-408); advances come from hmtx. Glyphs are")
    lines.append("// selected by feta NAME — the code points quoted below are only this build's.")
    lines.append("//")
    lines.append("// Hand-tuned constants (engraving thicknesses, spacing heuristics, LP grob")
    lines.append("// defaults) live in GlyphMetrics.cs — this file holds only values that can")
    lines.append("// be derived directly from the font binary.")
    lines.append("")
    lines.append("namespace LilySharp.Core.Svg.Layout;")
    lines.append("")
    # Must match GlyphMetrics.cs's own declaration — partial parts cannot disagree on
    # accessibility, and this file said `public` while that one says `internal`, so a plain
    # re-run did not compile.
    lines.append("internal static partial class GlyphMetrics")
    lines.append("{")

    # BBox glyphs (also emit advance width as a separate constant)
    lines.append("    // ========== BBox glyphs (from the font's LILC table) ==========")
    lines.append("    // BBox = the glyph's designed extent, read from LILC — the same per-glyph")
    lines.append("    // dimension LilyPond lays out with (lily/open-type-font.cc:390-408). It is NOT")
    lines.append("    // the outline's bounding box, which differs by ~0.002 ss on a note head.")
    lines.append("    // EXCEPTION: entries marked 'outline bbox' below are grobs LilyPond measures")
    lines.append("    // through the TEXT path (Pango over the outline), where LILC is never read.")
    lines.append("    // For horizontal positioning of the next glyph use the corresponding")
    lines.append("    // ...Advance constant, taken from hmtx as LilyPond takes it.")
    lines.append("")
    def outline_bbox(glyph: str):
        pen = BoundsPen(glyphSet)
        glyphSet[glyph].draw(pen)
        if pen.bounds is None:
            return None
        xMin, yMin, xMax, yMax = pen.bounds
        return (xMin / STAFF_SPACE_UNITS, yMin / STAFF_SPACE_UNITS,
                xMax / STAFF_SPACE_UNITS, yMax / STAFF_SPACE_UNITS)

    for spec in BBOX_GLYPHS:
        if spec.source == "outline":
            # Asked for explicitly: LilyPond measures THIS glyph through the text path,
            # so its LILC entry — if it even has one — is the wrong number. See the
            # dynamics block in BBOX_GLYPHS.
            box = outline_bbox(spec.glyph)
            if box is None:
                sys.stderr.write(f"ERROR: glyph {spec.glyph} has empty outline\n")
                return 1
            L, B, R, T = box
        elif spec.glyph in lilc_bboxes:
            L, B, R, T = lilc_bboxes[spec.glyph]
        else:
            # No LILC entry (a font without the table, or a glyph feta never sized).
            # Fall back to the outline and say so, rather than silently mixing sources.
            sys.stderr.write(f"note: {spec.glyph} has no LILC bbox; using the outline\n")
            box = outline_bbox(spec.glyph)
            if box is None:
                sys.stderr.write(f"ERROR: glyph {spec.glyph} has empty outline\n")
                return 1
            L, B, R, T = box
        adv, _ = hmtx[spec.glyph]
        adv_ss = adv / STAFF_SPACE_UNITS
        kind = "outline bbox" if spec.source == "outline" else "LILC bbox"
        lines.append(f"    /// <summary>{spec.summary} — BBox ({kind}).</summary>")
        lines.append(f"    /// <remarks>LILYPOND-REF: {cite(spec)}</remarks>")
        lines.append(f"    public static readonly BBox {spec.csharp_name} = new({fmt(L)}, {fmt(B)}, {fmt(R)}, {fmt(T)});")
        lines.append(f"    /// <summary>{spec.summary} — advance width (next-glyph horizontal feed).</summary>")
        lines.append(f"    public const double {spec.csharp_name}Advance = {fmt(adv_ss)};")
        lines.append("")

    # Advance-only glyphs
    lines.append("    // ========== Advance widths (extracted from hmtx table) ==========")
    lines.append("")
    for spec in ADVANCE_GLYPHS:
        adv, lsb = hmtx[spec.glyph]
        adv_ss = adv / STAFF_SPACE_UNITS
        lines.append(f"    /// <summary>{spec.summary}</summary>")
        lines.append(f"    /// <remarks>LILYPOND-REF: {cite(spec)}</remarks>")
        lines.append(f"    public const double {spec.csharp_name} = {fmt(adv_ss)};")
        lines.append("")

    lines.append("}")
    lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {out_path} ({len(BBOX_GLYPHS)} BBox + {len(ADVANCE_GLYPHS)} advance entries)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
