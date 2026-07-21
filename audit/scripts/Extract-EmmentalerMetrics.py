#!/usr/bin/env python3
"""
Extract glyph metrics from Emmentaler font and emit a C# partial class.

Reads:  editors/vscode/server/Fonts/emmentaler-20.otf
Writes: LilySharp.Core/Svg/Layout/GlyphMetricsGenerated.cs

The generated file holds every glyph metric (BBox / advance width) that can
be derived directly from the font binary. Hand-tuned constants — engraving
thicknesses, spacing heuristics, LP grob defaults — stay in GlyphMetrics.cs.

BBoxes come from the font's embedded LILC table, which is where LilyPond itself
reads them (lily/open-type-font.cc:288 load_scheme_table("LILC"), :389-407
get_indexed_char_dimensions); the raw outline is a fallback for fonts without one.
The two differ: the outline is what the curves happen to enclose, LILC is the
dimension METAFONT designed. For noteheads.s0 that is 1.9640 against 1.962002, and
1.962002 is what LilyPond lays out with — so taking the outline made Lily# miss
LilyPond by ~0.002 ss on every measure, in a way no formula could account for.

Run after Emmentaler font is updated. CI should re-run this and assert the
output is unchanged (else the font drifted).
"""
from __future__ import annotations

import re
import sys
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
    codepoint:   Unicode codepoint in the font
    summary:     XML doc <summary>
    feta_ref:    LILYPOND-REF citation pointing into mf/feta-*.mf
    """

    csharp_name: str
    codepoint: int
    summary: str
    feta_ref: str


# --- BBox glyphs (full bounds extracted) ---
BBOX_GLYPHS: list[GlyphSpec] = [
    # Noteheads
    GlyphSpec("NoteheadWhole",       0xE0E8, "Whole notehead",                "mf/feta-noteheads.mf — noteheads.s0"),
    GlyphSpec("NoteheadHalf",        0xE0E9, "Half (hollow) notehead",        "mf/feta-noteheads.mf — noteheads.s1"),
    GlyphSpec("NoteheadBlack",       0xE0EA, "Black (filled) notehead",       "mf/feta-noteheads.mf — noteheads.s2"),
    # Accidentals
    GlyphSpec("AccidentalSharp",       0xE013, "Sharp accidental",        "mf/feta-accidentals.mf — accidentals.sharp"),
    GlyphSpec("AccidentalFlat",        0xE021, "Flat accidental",         "mf/feta-flats.mf — accidentals.flat"),
    GlyphSpec("AccidentalNatural",     0xE01D, "Natural accidental",      "mf/feta-accidentals.mf — accidentals.natural"),
    GlyphSpec("AccidentalDoubleSharp", 0xE01C, "Double sharp accidental", "mf/feta-accidentals.mf — accidentals.doublesharp"),
    GlyphSpec("AccidentalDoubleFlat",  0xE02A, "Double flat accidental",  "mf/feta-flats.mf — accidentals.flatflat"),
    # Accidental parentheses: ink-extent glyphs designed for extent
    # juxtaposition (leftparen draws BEHIND its origin with advance 0)
    GlyphSpec("AccidentalLeftParen",  0xE02F, "Left accidental parenthesis (ink left of origin, advance 0)",  "mf/feta-parenthesis.mf — accidentals.leftparen"),
    GlyphSpec("AccidentalRightParen", 0xE02E, "Right accidental parenthesis", "mf/feta-parenthesis.mf — accidentals.rightparen"),
    # Flags
    GlyphSpec("Flag8thUp",    0xE0D2, "8th note flag (upward stem)",     "mf/feta-flags.mf — flags.u3"),
    GlyphSpec("Flag8thDown",  0xE0DA, "8th note flag (downward stem)",   "mf/feta-flags.mf — flags.d3"),
    GlyphSpec("Flag16thUp",   0xE0D3, "16th note flag (upward stem)",    "mf/feta-flags.mf — flags.u4"),
    GlyphSpec("Flag16thDown", 0xE0DB, "16th note flag (downward stem)",  "mf/feta-flags.mf — flags.d4"),
    # Augmentation dot
    GlyphSpec("AugmentationDot", 0xE038, "Augmentation dot", "mf/feta-noteheads.mf — dots.dot"),
    # Ornament / mark glyphs (ink extents for outside-staff stacking)
    GlyphSpec("OrnTrillGlyph", 0xE05C, "Trill ornament", "mf/feta-scripts.mf — scripts.trill"),
    GlyphSpec("OrnTurnGlyph", 0xE059, "Turn ornament", "mf/feta-scripts.mf — scripts.turn"),
    GlyphSpec("OrnReverseTurnGlyph", 0xE058, "Inverted (reverse) turn ornament", "mf/feta-scripts.mf — scripts.reverseturn"),
    GlyphSpec("OrnPrallGlyph", 0xE070, "Prall (upper mordent) ornament", "mf/feta-scripts.mf — scripts.prall"),
    GlyphSpec("OrnMordentGlyph", 0xE071, "Mordent (lower mordent) ornament", "mf/feta-scripts.mf — scripts.mordent"),
    GlyphSpec("OrnPrallPrallGlyph", 0xE072, "Prall-prall / prall-triller ornament", "mf/feta-scripts.mf — scripts.prallprall"),
    # NOTE: in this font's cmap segno/coda live at U+E062/U+E064;
    # U+E047/U+E048 are scripts.thumb / scripts.sforzato.
    GlyphSpec("MarkSegno", 0xE062, "Segno mark", "mf/feta-scripts.mf — scripts.segno"),
    GlyphSpec("MarkCoda", 0xE064, "Coda mark", "mf/feta-scripts.mf — scripts.coda"),
    # Clefs (ink extents; prefix glyphs seed the outside-staff occupancy).
    # NOTE: this font's cmap puts the clefs at E085/E083/E07F (the SMuFL
    # E050-range codepoints map to script glyphs here).
    GlyphSpec("ClefG", 0xE085, "G (treble) clef", "mf/feta-clefs.mf — clefs.G"),
    GlyphSpec("ClefF", 0xE083, "F (bass) clef", "mf/feta-clefs.mf — clefs.F"),
    GlyphSpec("ClefC", 0xE07F, "C (alto/tenor) clef", "mf/feta-clefs.mf — clefs.C"),
    # Change clefs are their OWN glyphs, not the full clef scaled — see Clef::calc_glyph_name
    # appending "_change". Their width sets the gap after a mid-measure clef change, so it
    # has to be a real metric rather than a fraction of the full clef's.
    GlyphSpec("ClefGChange", 0xE086, "G (treble) change clef", "mf/feta-clefs.mf — clefs.G_change"),
    GlyphSpec("ClefFChange", 0xE084, "F (bass) change clef", "mf/feta-clefs.mf — clefs.F_change"),
    GlyphSpec("ClefCChange", 0xE080, "C (alto/tenor) change clef", "mf/feta-clefs.mf — clefs.C_change"),
    # Rests (ink extents; used to place augmentation dots after the glyph
    # and to centre the church-rest combination of a multi-measure rest)
    GlyphSpec("RestLonga",       0xE005, "Longa (4-measure) rest",      "mf/feta-rests.mf — rests.M2"),
    GlyphSpec("RestDoubleWhole", 0xE006, "Double-whole (breve) rest",   "mf/feta-rests.mf — rests.M1"),
    GlyphSpec("RestWhole",   0xE000, "Whole rest",   "mf/feta-rests.mf — rests.0"),
    GlyphSpec("RestHalf",    0xE001, "Half rest",    "mf/feta-rests.mf — rests.1"),
    GlyphSpec("RestQuarter", 0xE008, "Quarter rest", "mf/feta-rests.mf — rests.2"),
    GlyphSpec("Rest8th",     0xE00B, "8th rest",     "mf/feta-rests.mf — rests.3"),
    GlyphSpec("Rest16th",    0xE00C, "16th rest",    "mf/feta-rests.mf — rests.4"),
    GlyphSpec("Rest32nd",    0xE00D, "32nd rest",    "mf/feta-rests.mf — rests.5"),
    GlyphSpec("Rest64th",    0xE00E, "64th rest",    "mf/feta-rests.mf — rests.6"),
    GlyphSpec("Rest128th",   0xE00F, "128th rest",   "mf/feta-rests.mf — rests.7"),
    # Articulations
    GlyphSpec("ArticStaccato",      0xE04A, "Staccato dot articulation",       "mf/feta-scripts.mf — scripts.staccato"),
    GlyphSpec("ArticAccent",        0xE048, "Accent / sforzato articulation",  "mf/feta-scripts.mf — scripts.sforzato"),
    GlyphSpec("ArticTenuto",        0xE04D, "Tenuto articulation",             "mf/feta-scripts.mf — scripts.tenuto"),
    GlyphSpec("ArticMarcatoAbove",  0xE050, "Marcato above (upward V)",        "mf/feta-scripts.mf — scripts.umarcato"),
    GlyphSpec("ArticMarcatoBelow",  0xE051, "Marcato below (downward V)",      "mf/feta-scripts.mf — scripts.dmarcato"),
    GlyphSpec("FermataAboveGlyph",  0xE039, "Fermata above",                   "mf/feta-scripts.mf — scripts.ufermata"),
    GlyphSpec("FermataBelowGlyph",  0xE03A, "Fermata below",                   "mf/feta-scripts.mf — scripts.dfermata"),
    GlyphSpec("ArticStaccatissimoAboveGlyph", 0xE04B, "Staccatissimo above (dagger)", "mf/feta-scripts.mf — scripts.ustaccatissimo"),
    GlyphSpec("ArticStaccatissimoBelowGlyph", 0xE04C, "Staccatissimo below (dagger)", "mf/feta-scripts.mf — scripts.dstaccatissimo"),
    GlyphSpec("ArticUpBowGlyph",    0xE056, "Up-bow (V)",                      "mf/feta-scripts.mf — scripts.upbow"),
    GlyphSpec("ArticDownBowGlyph",  0xE057, "Down-bow (frog)",                 "mf/feta-scripts.mf — scripts.downbow"),
    GlyphSpec("ArticFlageoletGlyph", 0xE061, "Flageolet / harmonic circle",    "mf/feta-scripts.mf — scripts.flageolet"),
]

# --- Advance-only glyphs (just the horizontal advance width) ---
ADVANCE_GLYPHS: list[GlyphSpec] = [
    # Clefs
    GlyphSpec("GClefAdvance",     0xE085, "G (treble) clef advance width",  "mf/feta-clefs.mf — clefs.G"),
    GlyphSpec("FClefAdvance",     0xE083, "F (bass) clef advance width",    "mf/feta-clefs.mf — clefs.F"),
    GlyphSpec("CClefAdvance",     0xE07F, "C (alto/tenor) clef advance width", "mf/feta-clefs.mf — clefs.C"),
    # Time signature digits (fattened)
    GlyphSpec("TimeSigDigit0Advance", 0xE0B4, "Time signature '0' digit advance width", "mf/feta-numbers.mf — fattened.zero"),
    GlyphSpec("TimeSigDigit1Advance", 0xE0B5, "Time signature '1' digit advance width", "mf/feta-numbers.mf — fattened.one"),
    GlyphSpec("TimeSigDigit2Advance", 0xE0B6, "Time signature '2' digit advance width", "mf/feta-numbers.mf — fattened.two"),
    GlyphSpec("TimeSigDigit3Advance", 0xE0B7, "Time signature '3' digit advance width", "mf/feta-numbers.mf — fattened.three"),
    GlyphSpec("TimeSigDigit4Advance", 0xE0B8, "Time signature '4' digit advance width", "mf/feta-numbers.mf — fattened.four"),
    GlyphSpec("TimeSigDigit5Advance", 0xE0BA, "Time signature '5' digit advance width", "mf/feta-numbers.mf — fattened.five"),
    GlyphSpec("TimeSigDigit6Advance", 0xE0BB, "Time signature '6' digit advance width", "mf/feta-numbers.mf — fattened.six"),
    GlyphSpec("TimeSigDigit7Advance", 0xE0BC, "Time signature '7' digit advance width", "mf/feta-numbers.mf — fattened.seven"),
    GlyphSpec("TimeSigDigit8Advance", 0xE0BE, "Time signature '8' digit advance width", "mf/feta-numbers.mf — fattened.eight"),
    GlyphSpec("TimeSigDigit9Advance", 0xE0BF, "Time signature '9' digit advance width", "mf/feta-numbers.mf — fattened.nine"),
    # Accidental parenthesis
]


def load_lilc_bboxes(font) -> tuple[dict[str, tuple[float, float, float, float]], float]:
    """Parse the font's LILC table into {glyph_name: (left, bottom, right, top)} in staff
    spaces. This is the SAME per-glyph metric LilyPond itself reads — see
    lily/open-type-font.cc:288 load_scheme_table("LILC") and :389-407
    get_indexed_char_dimensions, which returns this stored bbox in preference to the raw
    glyph outline. The LILC values are in the feta design unit; the LILY table's staff_space
    (5 for emmentaler-20 = design_size/4) converts them to staff spaces. Empty if no LILC."""
    keys = font.reader.keys()
    if "LILC" not in keys or "LILY" not in keys:
        return {}, 0.0
    lily = font.getTableData("LILY").decode("latin-1")
    m = re.search(r"staff_space\s*\.\s*([0-9]+(?:\.[0-9]+)?)", lily)
    staff_space = float(m.group(1)) if m else 5.0
    lilc = font.getTableData("LILC").decode("latin-1")
    pat = re.compile(r"\(([^\s()]+)\s*\.\s*\(\(bbox\s*\.\s*\(([-0-9.eE ]+)\)", re.S)
    out: dict[str, tuple[float, float, float, float]] = {}
    for gm in pat.finditer(lilc):
        vals = [float(x) / staff_space for x in gm.group(2).split()]
        if len(vals) == 4:
            out[gm.group(1)] = (vals[0], vals[1], vals[2], vals[3])
    return out, staff_space


def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    font_path = repo / "editors" / "vscode" / "server" / "Fonts" / "emmentaler-20.otf"
    out_path = repo / "LilySharp.Core" / "Svg" / "Layout" / "GlyphMetricsGenerated.cs"

    if not font_path.exists():
        sys.stderr.write(f"Font not found: {font_path}\n")
        return 2

    font = TTFont(str(font_path))
    upem = font["head"].unitsPerEm
    if upem != 1000:
        sys.stderr.write(f"WARNING: unitsPerEm={upem}, expected 1000 (Emmentaler convention)\n")
    cmap = font.getBestCmap()
    glyphSet = font.getGlyphSet()
    hmtx = font["hmtx"]
    # LP reads glyph bboxes from the font's LILC table, not the raw outline.
    lilc_bboxes, _staff_space = load_lilc_bboxes(font)
    if not lilc_bboxes:
        sys.stderr.write("WARNING: font has no LILC table; falling back to glyph outlines\n")

    def fmt(v: float) -> str:
        # SIX decimals. Four was enough while these values were only ever compared with each
        # other, but the LP fidelity corpus holds them against LilyPond, which speaks in six
        # (a note head is 1.304212, not 1.3040) — and rounding to four put a residual of
        # 2e-4..2e-3 under several ledger entries with no defect behind it.
        # `+ 0.0` folds LILC's signed zero: it stores -0.000000 for a left edge on the
        # origin, which is the same number but reads like a defect in the output.
        return f"{v + 0.0:.6f}"

    lines: list[str] = []
    lines.append("// Lily# - Music notation compiler")
    lines.append("// AUTO-GENERATED by audit/scripts/Extract-EmmentalerMetrics.py — DO NOT EDIT MANUALLY.")
    lines.append("// Re-run the script after the bundled Emmentaler font is updated.")
    lines.append("// Source font: editors/vscode/server/Fonts/emmentaler-20.otf")
    lines.append(f"// 1 staff space = unitsPerEm / 4 = {STAFF_SPACE_UNITS:.0f} font units")
    lines.append("//")
    lines.append("// BBoxes come from the font's LILC table, which is what LilyPond reads")
    lines.append("// (lily/open-type-font.cc:288, :389-407); advances come from hmtx.")
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
    lines.append("    // dimension LilyPond lays out with (lily/open-type-font.cc:389-407). It is NOT")
    lines.append("    // the outline's bounding box, which differs by ~0.002 ss on a note head.")
    lines.append("    // For horizontal positioning of the next glyph use the corresponding")
    lines.append("    // ...Advance constant, taken from hmtx as LilyPond takes it.")
    lines.append("")
    for spec in BBOX_GLYPHS:
        if spec.codepoint not in cmap:
            sys.stderr.write(f"ERROR: U+{spec.codepoint:04X} ({spec.csharp_name}) not in cmap\n")
            return 1
        gname = cmap[spec.codepoint]
        if gname in lilc_bboxes:
            L, B, R, T = lilc_bboxes[gname]
        else:
            # No LILC entry (a font without the table, or a glyph feta never sized).
            # Fall back to the outline and say so, rather than silently mixing sources.
            sys.stderr.write(f"note: {gname} has no LILC bbox; using the outline\n")
            pen = BoundsPen(glyphSet)
            glyphSet[gname].draw(pen)
            if pen.bounds is None:
                sys.stderr.write(f"ERROR: glyph {gname} has empty outline\n")
                return 1
            xMin, yMin, xMax, yMax = pen.bounds
            L = xMin / STAFF_SPACE_UNITS
            B = yMin / STAFF_SPACE_UNITS
            R = xMax / STAFF_SPACE_UNITS
            T = yMax / STAFF_SPACE_UNITS
        adv, _ = hmtx[gname]
        adv_ss = adv / STAFF_SPACE_UNITS
        lines.append(f"    /// <summary>{spec.summary} — BBox.</summary>")
        lines.append(f"    /// <remarks>LILYPOND-REF: {spec.feta_ref} (U+{spec.codepoint:04X} = {gname})</remarks>")
        lines.append(f"    public static readonly BBox {spec.csharp_name} = new({fmt(L)}, {fmt(B)}, {fmt(R)}, {fmt(T)});")
        lines.append(f"    /// <summary>{spec.summary} — advance width (next-glyph horizontal feed).</summary>")
        lines.append(f"    public const double {spec.csharp_name}Advance = {fmt(adv_ss)};")
        lines.append("")

    # Advance-only glyphs
    lines.append("    // ========== Advance widths (extracted from hmtx table) ==========")
    lines.append("")
    for spec in ADVANCE_GLYPHS:
        if spec.codepoint not in cmap:
            sys.stderr.write(f"ERROR: U+{spec.codepoint:04X} ({spec.csharp_name}) not in cmap\n")
            return 1
        gname = cmap[spec.codepoint]
        adv, lsb = hmtx[gname]
        adv_ss = adv / STAFF_SPACE_UNITS
        lines.append(f"    /// <summary>{spec.summary}</summary>")
        lines.append(f"    /// <remarks>LILYPOND-REF: {spec.feta_ref} (U+{spec.codepoint:04X} = {gname})</remarks>")
        lines.append(f"    public const double {spec.csharp_name} = {fmt(adv_ss)};")
        lines.append("")

    lines.append("}")
    lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {out_path} ({len(BBOX_GLYPHS)} BBox + {len(ADVANCE_GLYPHS)} advance entries)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
