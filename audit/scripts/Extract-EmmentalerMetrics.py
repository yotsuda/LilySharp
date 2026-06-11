#!/usr/bin/env python3
"""
Extract glyph metrics from Emmentaler font and emit a C# partial class.

Reads:  editors/vscode/server/Fonts/emmentaler-20.otf
Writes: LilySharp.Core/Svg/Layout/GlyphMetricsGenerated.cs

The generated file holds every glyph metric (BBox / advance width) that can
be derived directly from the font binary. Hand-tuned constants — engraving
thicknesses, spacing heuristics, LP grob defaults — stay in GlyphMetrics.cs.

Run after Emmentaler font is updated. CI should re-run this and assert the
output is unchanged (else the font drifted).
"""
from __future__ import annotations

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
    # NOTE: U+E047/U+E048 resolve to scripts.thumb / scripts.sforzato in this
    # font's cmap, NOT segno/coda — do not extract metrics for them here.
    # Rests (ink extents; used to place augmentation dots after the glyph)
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


def get_bbox(glyphSet, hmtx, codepoint: int) -> tuple[float, float, float, float, float] | None:
    """Return (left, bottom, right, top, advance) in staff spaces, or None if missing."""
    cmap = glyphSet.glyfTable.font.getBestCmap() if hasattr(glyphSet, "glyfTable") else None
    # cmap fallback via the font passed in via glyphSet doesn't exist; resolve in caller.
    raise NotImplementedError


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

    def fmt(v: float) -> str:
        # Round to 4 decimal places to keep diffs small without losing precision.
        return f"{v:.4f}"

    lines: list[str] = []
    lines.append("// Lily# - Music notation compiler")
    lines.append("// AUTO-GENERATED by audit/scripts/Extract-EmmentalerMetrics.py — DO NOT EDIT MANUALLY.")
    lines.append("// Re-run the script after the bundled Emmentaler font is updated.")
    lines.append("// Source font: editors/vscode/server/Fonts/emmentaler-20.otf")
    lines.append(f"// 1 staff space = unitsPerEm / 4 = {STAFF_SPACE_UNITS:.0f} font units")
    lines.append("//")
    lines.append("// Hand-tuned constants (engraving thicknesses, spacing heuristics, LP grob")
    lines.append("// defaults) live in GlyphMetrics.cs — this file holds only values that can")
    lines.append("// be derived directly from the font binary.")
    lines.append("")
    lines.append("namespace LilySharp.Core.Svg.Layout;")
    lines.append("")
    lines.append("public static partial class GlyphMetrics")
    lines.append("{")

    # BBox glyphs (also emit advance width as a separate constant)
    lines.append("    // ========== BBox glyphs (extracted from font outlines) ==========")
    lines.append("    // BBox = true visual extent (use for collision / skyline). For horizontal")
    lines.append("    // positioning of the next glyph use the corresponding ...Advance constant —")
    lines.append("    // notehead glyphs have decorative serifs that overhang the advance width.")
    lines.append("")
    for spec in BBOX_GLYPHS:
        if spec.codepoint not in cmap:
            sys.stderr.write(f"ERROR: U+{spec.codepoint:04X} ({spec.csharp_name}) not in cmap\n")
            return 1
        gname = cmap[spec.codepoint]
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
