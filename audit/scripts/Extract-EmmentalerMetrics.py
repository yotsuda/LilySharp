#!/usr/bin/env python3
"""Extract glyph metrics from the Emmentaler fonts and emit a C# partial class.

Reads:  LilySharp.Core/Fonts/emmentaler-{11,13,14,16,18,20,23,26}.otf
Writes: LilySharp.Core/Svg/Layout/GlyphMetricsGenerated.cs

The generated file holds every glyph metric (BBox / advance width) that can
be derived directly from the font binary. Hand-tuned constants — engraving
thicknesses, spacing heuristics, LP grob defaults — stay in GlyphMetrics.cs.

EVERY DESIGN IS EXTRACTED, not just the 20. Emmentaler is optically sized: the
designs are not scales of each other, so a glyph asked for at a smaller size is a
DIFFERENT outline with a different box (the black notehead's right edge runs
1.289478 in the 11 design against 1.304200 in the 20, in each design's OWN staff
spaces). LilyPond picks the file by lily/font-select.cc:41-70's ratio rule —
ported as EmmentalerDesignSize — and then scales it, so a metric is read as
designTable[chosen] * magstep(step). The 20 design keeps the flat top-level
constants it always had; the other seven are emitted as tables beside it.

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


# 1 staff space = unitsPerEm / 4 = 250 font units (Emmentaler convention). Every design
# normalises to the same em — the optical difference is in the outlines, not in the scale —
# so this divisor is the same in all eight files (asserted per font below).
STAFF_SPACE_UNITS = 250.0

# The eight designs LilyPond ships, by the rounded size in the file name.
# LILYPOND-REF: scm/lily-library.scm:1702-1710 feta-design-size-mapping.
DESIGNS: list[int] = [11, 13, 14, 16, 18, 20, 23, 26]

# The design whose metrics are ALSO emitted as the flat top-level constants: Lily#'s staff is
# LilyPond's default 20pt, so an unscaled grob reads this one.
BASE_DESIGN = 20


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
    # The wavy line's UNIT. LilyPond builds a trill spanner's line by repeating this one
    # glyph (lily/line-interface.cc:48-108 make_trill_line), and BOTH boxes matter there:
    # the LILC bbox is the repetition STEP (elt_len = elt.extent(X_AXIS).length()) while
    # the OUTLINE span is the first element's own length (elt_true_len, taken from the
    # stencil's horizontal skylines), and the difference is the overhang by which two
    # neighbours blend — LilyPond's own comment at :72-74. So the run's total length is
    # elt_true_len + n * elt_len for as many whole elements as fit, which is why a trill
    # line stops SHORT of its right bound.
    GlyphSpec("OrnTrillElementGlyph", "scripts.trill_element", "Trill line element (the wave's repeating unit)", "mf/feta-scripts.mf — scripts.trill_element"),
    # The ARPEGGIO's unit, the same kind of fact as the trill element above: LilyPond's
    # arpeggio stencil is this one glyph stacked upward while the pile is shorter than the
    # chord asks for (lily/arpeggio.cc:34-41 get_squiggle, :180-183 add_at_edge), and the
    # grob's X-extent is declared to BE this glyph's extent (:313-319 Arpeggio::width,
    # scm/define-grobs.scm:218). So both of an arpeggio's own dimensions are here: the box is
    # (0, 0, 0.8, 1.0) by design — mf/feta-scripts.mf:1892-1905 sets height# = staff_space#
    # and width# = 0.8 * height# — which is why a wiggle is 0.800000 wide and its length is
    # quantised to whole staff spaces. MEASURED against real LilyPond on
    # audit/lp-geometry/probes/arpeggio.ly: width 0.800000, and a chord spanning two spaces
    # draws 3.000000, i.e. three whole copies.
    GlyphSpec("Arpeggio", "scripts.arpeggio", "Arpeggio wiggle (the stencil's repeating unit)", "mf/feta-scripts.mf — scripts.arpeggio"),
    GlyphSpec("OrnTurnGlyph", "scripts.turn", "Turn ornament", "mf/feta-scripts.mf — scripts.turn"),
    GlyphSpec("OrnReverseTurnGlyph", "scripts.reverseturn", "Inverted (reverse) turn ornament", "mf/feta-scripts.mf — scripts.reverseturn"),
    GlyphSpec("OrnPrallGlyph", "scripts.prall", "Prall (upper mordent) ornament", "mf/feta-scripts.mf — scripts.prall"),
    GlyphSpec("OrnMordentGlyph", "scripts.mordent", "Mordent (lower mordent) ornament", "mf/feta-scripts.mf — scripts.mordent"),
    GlyphSpec("ScriptSnappizzicato", "scripts.snappizzicato", "Bartók (snap) pizzicato — ring with rising stem", "mf/feta-scripts.mf — scripts.snappizzicato"),
    GlyphSpec("OrnPrallPrallGlyph", "scripts.prallprall", "Prall-prall / prall-triller ornament", "mf/feta-scripts.mf — scripts.prallprall"),
    GlyphSpec("MarkSegno", "scripts.segno", "Segno mark", "mf/feta-scripts.mf — scripts.segno"),
    GlyphSpec("MarkCoda", "scripts.coda", "Coda mark", "mf/feta-scripts.mf — scripts.coda"),
    # Clefs (ink extents; prefix glyphs seed the outside-staff occupancy).
    GlyphSpec("ClefG", "clefs.G", "G (treble) clef", "mf/feta-clefs.mf — clefs.G"),
    GlyphSpec("ClefF", "clefs.F", "F (bass) clef", "mf/feta-clefs.mf — clefs.F"),
    GlyphSpec("ClefC", "clefs.C", "C (alto/tenor) clef", "mf/feta-clefs.mf — clefs.C"),
    # Percussion clef — unlike the pitched clefs its ink starts RIGHT of the grob origin
    # (LILC bbox left ~0.67, not 0), so its ink width (Right - Left) and its draw origin both
    # differ from the pitched clefs; see GlyphMetrics.LineStartClefWidth / SharedRenderer DrawClef.
    GlyphSpec("ClefPercussion", "clefs.percussion", "Percussion clef", "mf/feta-clefs.mf — clefs.percussion"),
    # TAB clef. LilyPond puts it in the SAME Clef break-align group as the pitched clefs and
    # it is WIDER than the G clef (origin-to-ink-right 2.8 against 2.565), so on a
    # notation+tab score it governs where every staff's meter and first note sit --
    # audit/lp-geometry/probes/line-start-mindist.ly measures the 0.235 difference between
    # TKC's and SKC's min_dist and it is exactly this. Like the percussion clef its ink
    # starts right of the grob origin (LILC left 0.2, not 0).
    GlyphSpec("ClefTab", "clefs.tab", "TAB clef", "mf/feta-clefs.mf — clefs.tab"),
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
    # Common / cut-common time signatures. LilyPond's DEFAULT style prints 4/4 and 2/2 as
    # these GLYPHS (scm/time-signature-settings.scm:954-964 make-c-time-signature-markup ->
    # timesig.C44 / timesig.C22, glyph path = LILC ink); every other fraction takes the
    # numbered markup (Pango) path the TimeSigDigit*Advance table serves.
    GlyphSpec("TimeSigCommon",    "timesig.C44", "Common-time (C) signature",          "mf/feta-timesignatures.mf — timesig.C44"),
    GlyphSpec("TimeSigCutCommon", "timesig.C22", "Cut-common (alla breve) signature",  "mf/feta-timesignatures.mf — timesig.C22"),

    # These are the seven letters the fetaText encoding draws dynamics from; they are
    # addressed by the bare ASCII name, not a "dynamics." prefix.
    GlyphSpec("DynamicLetterF", "f", "Dynamic letter 'f' (fetaText)", "mf/feta-dynamics.mf — f", source="outline"),
    GlyphSpec("DynamicLetterM", "m", "Dynamic letter 'm' (fetaText)", "mf/feta-dynamics.mf — m", source="outline"),
    GlyphSpec("DynamicLetterN", "n", "Dynamic letter 'n' (fetaText)", "mf/feta-dynamics.mf — n", source="outline"),
    GlyphSpec("DynamicLetterP", "p", "Dynamic letter 'p' (fetaText)", "mf/feta-dynamics.mf — p", source="outline"),
    GlyphSpec("DynamicLetterR", "r", "Dynamic letter 'r' (fetaText)", "mf/feta-dynamics.mf — r", source="outline"),
    GlyphSpec("DynamicLetterS", "s", "Dynamic letter 's' (fetaText)", "mf/feta-dynamics.mf — s", source="outline"),
    GlyphSpec("DynamicLetterZ", "z", "Dynamic letter 'z' (fetaText)", "mf/feta-dynamics.mf — z", source="outline"),

    # --- Figured-bass digits and alterations (fetaText, like the dynamics above) ---
    # A BassFigure's stencil is ly:text-interface::print over `\number` markup
    # (scm/define-grobs.scm:352-356, scm/translation-functions.scm:349-470
    # format-bass-figure), so it is the TEXT path and the outline is the box LilyPond
    # measures -- the same argument as the dynamic letters, and the reason source is
    # "outline" here too. WHICH glyph is decided by BassFigure's font-features
    # ("tnum" "cv47" "ss01") = fixedwidth + .alt(4,7) + fattened.
    # ⚠️ THE SIZE IS NOT IN THIS FILE. These boxes are per EM at the font's design size;
    # a figure is set at font-size -5 (translation-functions.scm:468-470's
    # make-fontsize-markup), i.e. 4 ss * magstep(-5) -- see
    # EngravingDefaults.FiguredBassFontSize, which is what scales them.
    GlyphSpec("FigBassDigit0", "fattened.fixedwidth.zero",  "Figured-bass digit 0",  "mf/feta-numbers.mf — fattened.fixedwidth.zero", source="outline"),
    GlyphSpec("FigBassDigit1", "fattened.fixedwidth.one",   "Figured-bass digit 1",  "mf/feta-numbers.mf — fattened.fixedwidth.one", source="outline"),
    GlyphSpec("FigBassDigit2", "fattened.fixedwidth.two",   "Figured-bass digit 2",  "mf/feta-numbers.mf — fattened.fixedwidth.two", source="outline"),
    GlyphSpec("FigBassDigit3", "fattened.fixedwidth.three", "Figured-bass digit 3",  "mf/feta-numbers.mf — fattened.fixedwidth.three", source="outline"),
    GlyphSpec("FigBassDigit4", "fattened.fixedwidth.four.alt", "Figured-bass digit 4 (cv47 variant)", "mf/feta-numbers.mf — fattened.fixedwidth.four.alt", source="outline"),
    GlyphSpec("FigBassDigit5", "fattened.fixedwidth.five",  "Figured-bass digit 5",  "mf/feta-numbers.mf — fattened.fixedwidth.five", source="outline"),
    GlyphSpec("FigBassDigit6", "fattened.fixedwidth.six",   "Figured-bass digit 6",  "mf/feta-numbers.mf — fattened.fixedwidth.six", source="outline"),
    GlyphSpec("FigBassDigit7", "fattened.fixedwidth.seven.alt", "Figured-bass digit 7 (cv47 variant)", "mf/feta-numbers.mf — fattened.fixedwidth.seven.alt", source="outline"),
    GlyphSpec("FigBassDigit8", "fattened.fixedwidth.eight", "Figured-bass digit 8",  "mf/feta-numbers.mf — fattened.fixedwidth.eight", source="outline"),
    GlyphSpec("FigBassDigit9", "fattened.fixedwidth.nine",  "Figured-bass digit 9",  "mf/feta-numbers.mf — fattened.fixedwidth.nine", source="outline"),
    GlyphSpec("FigBassFlat",    "accidentals.flat.figbass",    "Figured-bass flat (U+266D)",    "mf/feta-flats.mf — flat.figbass", source="outline"),
    GlyphSpec("FigBassNatural", "accidentals.natural.figbass", "Figured-bass natural (U+266E)", "mf/feta-naturals.mf — natural.figbass", source="outline"),
    GlyphSpec("FigBassSharp",   "accidentals.sharp.figbass",   "Figured-bass sharp (U+266F)",   "mf/feta-sharps.mf — sharp.figbass", source="outline"),
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


def load_lilc_attachments(font) -> dict[str, tuple[float, float]]:
    """Parse the font's LILC `attachment` points into {glyph: (x, y)} in staff spaces —
    the UP-stem attachment. This is the same per-glyph datum LilyPond's
    Font_metric::attachment_point serves to Note_head::get_stem_attachment
    (lily/note-head.cc:164-196), which \\note-by-number turns back into the stem's
    lower-end coordinate (define-markup-commands.scm attach-off). Empty if no LILC."""
    keys = font.reader.keys()
    if "LILC" not in keys or "LILY" not in keys:
        return {}
    lily = font.getTableData("LILY").decode("latin-1")
    m = re.search(r"staff_space\s*\.\s*([0-9]+(?:\.[0-9]+)?)", lily)
    staff_space = float(m.group(1)) if m else 5.0
    raw = font.getTableData("LILC")
    try:
        raw = zlib.decompress(raw)
    except zlib.error:
        pass
    lilc = raw.decode("latin-1")
    pat = re.compile(
        r"\(([^\s()]+)\s*\.\s*\(\(bbox\s*\.\s*\([^)]*\)\)\s*"
        r"\(attachment\s*\.\s*\(([-0-9.eE]+)\s*\.\s*([-0-9.eE]+)\)\)",
        re.S)
    out: dict[str, tuple[float, float]] = {}
    for gm in pat.finditer(lilc):
        out[gm.group(1)] = (float(gm.group(2)) / staff_space,
                            float(gm.group(3)) / staff_space)
    return out


# Noteheads whose UP-stem attachment point is emitted (the stemless whole needs none).
ATTACHMENT_GLYPHS: list[tuple[str, str]] = [
    ("NoteheadHalfStemAttachment",  "noteheads.s1"),
    ("NoteheadBlackStemAttachment", "noteheads.s2"),
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


def load_design_size(font) -> float:
    """The design size the file really carries, from its LILY table — 14.14 for
    emmentaler-14.otf, not 14. This is the same number
    scm/lily-library.scm:1702-1710 feta-design-size-mapping lists against the rounded
    size in the file name, read from the font instead of copied, so that the ported
    table (EmmentalerDesignSize.Designs) has something to be checked against."""
    lily = font.getTableData("LILY").decode("latin-1")
    m = re.search(r"design_size\s*\.\s*([0-9]+(?:\.[0-9]+)?)", lily)
    if m is None:
        raise ValueError("font has no design_size in its LILY table")
    return float(m.group(1))


@dataclass(frozen=True)
class DesignMetrics:
    """Every extracted metric of ONE design, in that design's own staff spaces."""

    rounded: int
    design_size: float
    boxes: dict[str, tuple[float, float, float, float]]       # by C# name
    outlines: dict[str, tuple[float, float, float, float]]    # by C# name
    advances: dict[str, float]                                # by C# name
    attachments: dict[str, tuple[float, float]]               # by C# name
    codepoints: dict[str, int]                                # by feta glyph name


def extract(font_path: Path, rounded: int) -> DesignMetrics | None:
    """Read one design's metrics, or None after writing why it could not be read."""
    if not font_path.exists():
        sys.stderr.write(f"Font not found: {font_path}\n")
        return None

    font = TTFont(str(font_path))
    upem = font["head"].unitsPerEm
    if upem != 1000:
        sys.stderr.write(f"WARNING: {font_path.name}: unitsPerEm={upem}, expected 1000 "
                         "(Emmentaler convention)\n")
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
        sys.stderr.write(f"WARNING: {font_path.name} has no LILC table; "
                         "falling back to glyph outlines\n")

    missing = [s.glyph for s in (*BBOX_GLYPHS, *ADVANCE_GLYPHS) if s.glyph not in order]
    if missing:
        for glyph in missing:
            sys.stderr.write(f"ERROR: {font_path.name}: glyph name not in font: {glyph}\n")
        return None

    def outline_bbox(glyph: str):
        pen = BoundsPen(glyphSet)
        glyphSet[glyph].draw(pen)
        if pen.bounds is None:
            return None
        xMin, yMin, xMax, yMax = pen.bounds
        return (xMin / STAFF_SPACE_UNITS, yMin / STAFF_SPACE_UNITS,
                xMax / STAFF_SPACE_UNITS, yMax / STAFF_SPACE_UNITS)

    boxes: dict[str, tuple[float, float, float, float]] = {}
    outlines: dict[str, tuple[float, float, float, float]] = {}
    advances: dict[str, float] = {}

    for spec in BBOX_GLYPHS:
        if spec.source == "outline":
            # Asked for explicitly: LilyPond measures THIS glyph through the text path,
            # so its LILC entry — if it even has one — is the wrong number. See the
            # dynamics block in BBOX_GLYPHS.
            box = outline_bbox(spec.glyph)
        elif spec.glyph in lilc_bboxes:
            box = lilc_bboxes[spec.glyph]
        else:
            # No LILC entry (a font without the table, or a glyph feta never sized).
            # Fall back to the outline and say so, rather than silently mixing sources.
            sys.stderr.write(f"note: {font_path.name}: {spec.glyph} has no LILC bbox; "
                             "using the outline\n")
            box = outline_bbox(spec.glyph)
        if box is None:
            sys.stderr.write(f"ERROR: {font_path.name}: glyph {spec.glyph} has empty outline\n")
            return None
        boxes[spec.csharp_name] = box
        # ...and the box a SKYLINE is built from, which is a different box.
        skybox = outline_bbox(spec.glyph)
        if skybox is not None:
            outlines[spec.csharp_name] = skybox
        adv, _ = hmtx[spec.glyph]
        advances[spec.csharp_name] = adv / STAFF_SPACE_UNITS

    attachment_points = load_lilc_attachments(font)
    attachments: dict[str, tuple[float, float]] = {}
    for cname, glyph in ATTACHMENT_GLYPHS:
        if glyph not in attachment_points:
            sys.stderr.write(f"ERROR: {font_path.name}: glyph {glyph} has no LILC attachment\n")
            return None
        attachments[cname] = attachment_points[glyph]

    for spec in ADVANCE_GLYPHS:
        adv, _lsb = hmtx[spec.glyph]
        advances[spec.csharp_name] = adv / STAFF_SPACE_UNITS

    return DesignMetrics(rounded=rounded, design_size=load_design_size(font), boxes=boxes,
                         outlines=outlines, advances=advances, attachments=attachments,
                         codepoints=reverse_cmap)


def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    fonts_dir = repo / "LilySharp.Core" / "Fonts"
    out_path = repo / "LilySharp.Core" / "Svg" / "Layout" / "GlyphMetricsGenerated.cs"

    designs: list[DesignMetrics] = []
    for rounded in DESIGNS:
        design = extract(fonts_dir / f"emmentaler-{rounded}.otf", rounded)
        if design is None:
            return 1
        designs.append(design)
    base = next(d for d in designs if d.rounded == BASE_DESIGN)

    # A glyph that carries a skyline box in one design has to carry it in all of them, or
    # the tables would not have the same members and a design would silently lose a reader.
    for design in designs:
        if design.outlines.keys() != base.outlines.keys():
            sys.stderr.write(f"ERROR: design {design.rounded} has a different set of glyph "
                             "outlines than the base design\n")
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
        codepoint = base.codepoints.get(spec.glyph)
        where = f"U+{codepoint:04X}" if codepoint is not None else "unmapped"
        return f"{spec.feta_ref} ({spec.glyph} = {where} in this build)"

    lines: list[str] = []
    lines.append("// Lily# - Music notation compiler")
    lines.append("// AUTO-GENERATED by audit/scripts/Extract-EmmentalerMetrics.py — DO NOT EDIT MANUALLY.")
    lines.append("// Re-run the script after the bundled Emmentaler font is updated.")
    lines.append("// Source fonts: LilySharp.Core/Fonts/emmentaler-"
                 f"{{{','.join(str(d) for d in DESIGNS)}}}.otf")
    lines.append(f"// 1 staff space = unitsPerEm / 4 = {STAFF_SPACE_UNITS:.0f} font units")
    lines.append("//")
    lines.append(f"// The flat constants are the {BASE_DESIGN} design — Lily#'s staff is LilyPond's")
    lines.append("// default 20pt, so an unscaled grob reads them. Emmentaler is optically sized, so")
    lines.append("// a grob at another size does NOT read them scaled: it reads the table of the")
    lines.append("// design its size lands on (see the per-design tables at the end of this file).")
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
    lines.append("    //")
    lines.append("    // ...and each glyph ALSO gets a `...Outline` box, which is what a SKYLINE is")
    lines.append("    // built from. LilyPond keeps the two apart: the extent is the designed (LILC)")
    lines.append("    // box and the skyline is the curves' own bounds")
    lines.append("    // (lily/stencil-integral.cc:535-563 add_named_glyph_segments). They coincide")
    lines.append("    // only for a glyph that fills its box — a notehead does (0.001), the G clef")
    lines.append("    // does not (0.024 of slack above), the F clef least of all (0.448 below).")
    lines.append("    // ⚠️ Use the EXTENT for widths and positions, the OUTLINE only for skylines.")
    lines.append("")
    for spec in BBOX_GLYPHS:
        L, B, R, T = base.boxes[spec.csharp_name]
        adv_ss = base.advances[spec.csharp_name]
        kind = "outline bbox" if spec.source == "outline" else "LILC bbox"
        lines.append(f"    /// <summary>{spec.summary} — BBox ({kind}).</summary>")
        lines.append(f"    /// <remarks>LILYPOND-REF: {cite(spec)}</remarks>")
        lines.append(f"    public static readonly BBox {spec.csharp_name} = new({fmt(L)}, {fmt(B)}, {fmt(R)}, {fmt(T)});")
        # ...and the box a SKYLINE is built from, which is a different box.
        # LILYPOND-REF: lily/stencil-integral.cc:535-563 add_named_glyph_segments — the
        # skyline is built from the glyph OUTLINE (get_glyph_outline_bbox), not from the
        # extent, and the two coincide only for a glyph that fills its designed box.
        skybox = base.outlines.get(spec.csharp_name)
        if skybox is not None:
            SL, SB, SR, ST = skybox
            lines.append(f"    /// <summary>{spec.summary} — the box its SKYLINE is built from"
                         " (glyph outline).</summary>")
            lines.append("    /// <remarks>LILYPOND-REF: lily/stencil-integral.cc:535-563"
                         " add_named_glyph_segments.</remarks>")
            lines.append(f"    public static readonly BBox {spec.csharp_name}Outline"
                         f" = new({fmt(SL)}, {fmt(SB)}, {fmt(SR)}, {fmt(ST)});")
        lines.append(f"    /// <summary>{spec.summary} — advance width (next-glyph horizontal feed).</summary>")
        lines.append(f"    public const double {spec.csharp_name}Advance = {fmt(adv_ss)};")
        lines.append("")

    # Stem attachment points (LILC `attachment`)
    lines.append("    // ========== Up-stem attachment points (from the font's LILC table) ==========")
    lines.append("    // The point where an up stem's lower-right corner meets the head — X is the")
    lines.append("    // head's designed right edge, Y the height above the centre line the stem's")
    lines.append("    // lower end starts at. LilyPond serves it via Font_metric::attachment_point to")
    lines.append("    // Note_head::get_stem_attachment (lily/note-head.cc:164-196), and")
    lines.append("    // \\note-by-number turns it back into the stem's lower-end coordinate")
    lines.append("    // (scm/define-markup-commands.scm attach-off).")
    lines.append("")
    for cname, glyph in ATTACHMENT_GLYPHS:
        ax, ay = base.attachments[cname]
        lines.append(f"    /// <summary>{glyph} up-stem attachment point (staff spaces about the")
        lines.append("    /// glyph origin: X from the ink left, Y above the centre line).</summary>")
        lines.append("    /// <remarks>LILYPOND-REF: lily/note-head.cc:164-196 get_stem_attachment,")
        lines.append("    /// attachment_point — the font's LILC attachment entry.</remarks>")
        lines.append(f"    public static readonly (double X, double Y) {cname} = ({fmt(ax)}, {fmt(ay)});")
        lines.append("")

    # Advance-only glyphs
    lines.append("    // ========== Advance widths (extracted from hmtx table) ==========")
    lines.append("")
    for spec in ADVANCE_GLYPHS:
        adv_ss = base.advances[spec.csharp_name]
        lines.append(f"    /// <summary>{spec.summary}</summary>")
        lines.append(f"    /// <remarks>LILYPOND-REF: {cite(spec)}</remarks>")
        lines.append(f"    public const double {spec.csharp_name} = {fmt(adv_ss)};")
        lines.append("")

    # ---- the same metrics, once per design ----
    # kind, C# member name, doc summary, and how to read it out of a DesignMetrics.
    members: list[tuple[str, str, str, object]] = []
    for spec in BBOX_GLYPHS:
        name = spec.csharp_name
        kind = "outline bbox" if spec.source == "outline" else "LILC bbox"
        members.append(("bbox", name, f"{spec.summary} — BBox ({kind}).",
                        lambda d, n=name: d.boxes[n]))
        if name in base.outlines:
            members.append(("bbox", f"{name}Outline",
                            f"{spec.summary} — the box its SKYLINE is built from (glyph outline).",
                            lambda d, n=name: d.outlines[n]))
        members.append(("double", f"{name}Advance",
                        f"{spec.summary} — advance width (next-glyph horizontal feed).",
                        lambda d, n=name: d.advances[n]))
    for cname, glyph in ATTACHMENT_GLYPHS:
        members.append(("attach", cname,
                        f"{glyph} up-stem attachment point (staff spaces about the glyph origin).",
                        lambda d, n=cname: d.attachments[n]))
    for spec in ADVANCE_GLYPHS:
        members.append(("double", spec.csharp_name, spec.summary,
                        lambda d, n=spec.csharp_name: d.advances[n]))

    seen: set[str] = set()
    for _kind, name, _summary, _get in members:
        if name in seen:
            sys.stderr.write(f"ERROR: two metrics want the C# name {name}\n")
            return 1
        seen.add(name)

    csharp_type = {"bbox": "BBox", "double": "double", "attach": "(double X, double Y)"}

    def literal(kind: str, v) -> str:
        if kind == "bbox":
            return f"new({fmt(v[0])}, {fmt(v[1])}, {fmt(v[2])}, {fmt(v[3])})"
        if kind == "attach":
            return f"({fmt(v[0])}, {fmt(v[1])})"
        return fmt(v)

    lines.append("    // ========== The same metrics, per DESIGN ==========")
    lines.append("    // Emmentaler is optically sized: emmentaler-11 is not emmentaler-20 shrunk, it is")
    lines.append("    // a different drawing with different metrics. LilyPond therefore does not scale one")
    lines.append("    // table — it picks the FILE whose design size is closest by ratio to the size asked")
    lines.append("    // for (lily/font-select.cc:41-70 best_rounded_design_size, ported as")
    lines.append("    // EmmentalerDesignSize) and reads that file's metrics, then scales them.")
    lines.append("    //")
    lines.append("    // Each table below is in ITS OWN design's staff spaces, exactly as the flat")
    lines.append("    // constants above are in the 20's. A grob's box on the page is")
    lines.append("    //     designTable[chosen].Glyph * magstep(font-size)")
    lines.append("    // — LilyPond's own requested/actual magnification cancels against the design size")
    lines.append("    // (lily/font-select.cc:185 find_scaled_font with lily/modified-font-metric.cc:62-68")
    lines.append("    // Modified_font_metric::get_indexed_char_dimensions), which is why")
    lines.append("    // the multiplication a caller already does does not change; only the table does.")
    lines.append("")
    lines.append("    /// <summary>")
    lines.append("    /// Every font-derived metric of ONE Emmentaler design, in that design's own staff")
    lines.append("    /// spaces.")
    lines.append("    /// </summary>")
    lines.append("    /// <remarks>")
    lines.append("    /// LILYPOND-REF: lily/open-type-font.cc:390-408 get_indexed_char_dimensions — one")
    lines.append("    ///   loaded font file answers for one design; LilyPond holds as many as the score")
    lines.append("    ///   asks for.")
    lines.append("    /// ⚠️ These are NOT page staff spaces. Scale by the grob's magstep before use.")
    lines.append("    /// </remarks>")
    lines.append("    internal sealed class DesignMetrics")
    lines.append("    {")
    lines.append("        /// <summary>The rounded size in the file name (emmentaler-11.otf is 11).</summary>")
    lines.append("        public int Rounded { get; init; }")
    lines.append("")
    lines.append("        /// <summary>The design size the file really carries — 11.22 for the 11.</summary>")
    lines.append("        /// <remarks>Read from the font's own LILY table; the ported mapping it has to")
    lines.append("        /// agree with is EmmentalerDesignSize.Designs")
    lines.append("        /// (scm/lily-library.scm:1702-1710 feta-design-size-mapping).</remarks>")
    lines.append("        public double DesignSize { get; init; }")
    lines.append("")
    lines.append("        /// <summary>The magnification this table has ALREADY been read at — 1.0 for a")
    lines.append("        /// design's own table, magstep(font-size) for what <see cref=\"AtFontSize\"/>")
    lines.append("        /// hands back.</summary>")
    lines.append("        /// <remarks>A font is a design AND a magnification, not one of the two")
    lines.append("        /// (lily/modified-font-metric.cc:44-56 holds <c>orig_</c> and")
    lines.append("        /// <c>magnification_</c> side by side). Boxes here are scaled already; this is")
    lines.append("        /// for the dimensions that are NOT in this table and have to be read from the")
    lines.append("        /// same face by hand — the glyph OUTLINE skylines")
    lines.append("        /// (GlyphMetrics.AccidentalSkylinePair), which a caller scales itself because")
    lines.append("        /// a skyline is mutable and every seat wants its own copy.</remarks>")
    lines.append("        public double Magnification { get; init; } = 1.0;")
    lines.append("")
    for kind, name, summary, _get in members:
        lines.append(f"        /// <summary>{summary}</summary>")
        lines.append(f"        public {csharp_type[kind]} {name} {{ get; init; }}")
    lines.append("")
    lines.append("        /// <summary>")
    lines.append("        /// Every metric multiplied by <paramref name=\"magnification\"/> — the same table in")
    lines.append("        /// the PAGE's staff spaces instead of this design's.")
    lines.append("        /// </summary>")
    lines.append("        /// <remarks>")
    lines.append("        /// LILYPOND-REF: lily/modified-font-metric.cc:62-68 Modified_font_metric::get_indexed_char_dimensions")
    lines.append("        ///   is")
    lines.append("        ///   <c>Box b = orig_-&gt;get_indexed_char_dimensions (i); b.scale (magnification_);</c>")
    lines.append("        ///   — the scaling happens once, inside the font, so a grob never multiplies and so")
    lines.append("        ///   can never forget to. GlyphMetrics.AtFontSize is that font.")
    lines.append("        /// </remarks>")
    lines.append("        public DesignMetrics Scaled(double magnification) => new()")
    lines.append("        {")
    lines.append("            Rounded = Rounded,")
    lines.append("            DesignSize = DesignSize,")
    lines.append("            Magnification = Magnification * magnification,")
    for kind, name, _summary, _get in members:
        if kind == "bbox":
            lines.append(f"            {name} = new({name}.Left * magnification, {name}.Bottom * magnification,")
            lines.append(f"                {name}.Right * magnification, {name}.Top * magnification),")
        elif kind == "attach":
            lines.append(f"            {name} = ({name}.X * magnification, {name}.Y * magnification),")
        else:
            lines.append(f"            {name} = {name} * magnification,")
    lines.append("        };")
    lines.append("    }")
    lines.append("")

    for design in designs:
        is_base = design.rounded == BASE_DESIGN
        lines.append(f"    /// <summary>emmentaler-{design.rounded}.otf"
                     f" (design size {design.design_size:.2f}).</summary>")
        if is_base:
            lines.append("    /// <remarks>The flat constants above ARE this design, so this table names")
            lines.append("    /// them rather than repeating them — the 20's numbers are written once.</remarks>")
        lines.append(f"    public static readonly DesignMetrics Design{design.rounded} = new()")
        lines.append("    {")
        lines.append(f"        Rounded = {design.rounded},")
        lines.append(f"        DesignSize = {design.design_size:.2f},")
        for kind, name, _summary, get in members:
            value = f"{name}" if is_base else literal(kind, get(design))
            lines.append(f"        {name} = {value},")
        lines.append("    };")
        lines.append("")

    lines.append("    /// <summary>Every design's table, smallest first — LilyPond's own mapping order.")
    lines.append("    /// (EmmentalerDesignSize.Designs is the same eight designs as the SELECTION rule")
    lines.append("    /// sees them; this is their metrics.)</summary>")
    lines.append("    public static readonly DesignMetrics[] AllDesigns =")
    lines.append("    {")
    lines.append("        " + ", ".join(f"Design{d.rounded}" for d in designs) + ",")
    lines.append("    };")
    lines.append("")
    lines.append("    /// <summary>The metrics of <c>emmentaler-&lt;rounded&gt;.otf</c>.</summary>")
    lines.append("    /// <remarks>The argument is the ROUNDED size — what")
    lines.append("    /// EmmentalerDesignSize.BestRounded returns, and the number in the file name.</remarks>")
    lines.append("    public static DesignMetrics ForDesign(int rounded) => rounded switch")
    lines.append("    {")
    for design in designs:
        lines.append(f"        {design.rounded} => Design{design.rounded},")
    lines.append("        _ => throw new System.ArgumentOutOfRangeException(")
    lines.append("            nameof(rounded), rounded, \"not an Emmentaler design size\"),")
    lines.append("    };")
    lines.append("}")
    lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {out_path} ({len(BBOX_GLYPHS)} BBox + {len(ADVANCE_GLYPHS)} advance entries"
          f" x {len(designs)} designs)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
