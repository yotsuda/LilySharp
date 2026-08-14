#!/usr/bin/env python3
"""Resolve Emmentaler glyph code points by feta NAME and emit a C# partial class.

Reads:  LilySharp.Core/Fonts/emmentaler-20.otf
Writes: LilySharp.Core/Svg/EmmentalerGlyphs.Generated.cs

WHY THIS EXISTS. Emmentaler's glyphs live in the Unicode private use area, and the
PUA assignment is NOT stable across font builds -- it is just the order feta happened
to emit them in. LilyPond never depends on it: it asks for glyphs by their feta name
("clefs.G", "noteheads.s2"), which lily/clef.cc:29-52 and lily/note-head.cc build as
strings. Lily# used to hard-code the code points with the feta name only in a trailing
comment, so a font update silently repointed every constant at whatever glyph had
drifted into that slot.

That is not hypothetical. LilyPond 2.26.0 inserts 34 glyphs into the range, which moves
73 of the 115 constants below -- U+E085 stops being clefs.G and becomes clefs.varC,
U+E0EA stops being noteheads.s2 and becomes flags.stackedu7. Nothing would have failed
to compile and nothing would have thrown; the score would just have been drawn with the
wrong glyphs. Keying on the name makes that class of bug impossible: an absent name is
an error here, before it can reach a rendered page.

Run after the bundled Emmentaler font is updated. CI should re-run this and assert the
output is unchanged (else the font drifted).
"""
from __future__ import annotations

import sys
from pathlib import Path

# The GPLv3 notice every generated .cs must carry. These files ship in the product,
# so section 4 applies to them exactly as it does to hand-written source; the Emmentaler
# note is here because the numbers are measured from a font that is part of LilyPond.
# Kept identical in the three Extract-Emmentaler*.py generators.
LICENCE_HEADER = [
    "// Copyright (C) 2025-2026 Yoshifumi Tsuda",
    "//",
    "// This program is free software: you can redistribute it and/or modify",
    "// it under the terms of the GNU General Public License as published by",
    "// the Free Software Foundation, either version 3 of the License, or",
    "// (at your option) any later version.",
    "//",
    "// This program is distributed in the hope that it will be useful,",
    "// but WITHOUT ANY WARRANTY; without even the implied warranty of",
    "// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the",
    "// GNU General Public License for more details.",
    "//",
    "// You should have received a copy of the GNU General Public License",
    "// along with this program.  If not, see <https://www.gnu.org/licenses/>.",
    "//",
    "// The numbers below are measured from the Emmentaler music font, which is part of",
    "// LilyPond and is redistributed here under the GNU GPL v3 or later (it is dual",
    "// licensed GPL / SIL OFL). See THIRD-PARTY-NOTICES.md.",
]

try:
    from fontTools.ttLib import TTFont
except ImportError:
    sys.stderr.write("fontTools not installed. Run: pip install fonttools\n")
    sys.exit(2)


# (C# constant, feta glyph name, parenthetical note for the comment).
# ("#", "Section heading") starts a new section in the generated file.
#
# The feta name is the SOURCE OF TRUTH. Where a constant's old code point and its old
# comment disagreed, the font was consulted and the name chosen deliberately -- see the
# CORRECTED markers, which are the four fermatas and the thumb.
GLYPHS: list[tuple[str, str, str]] = [
    ("#", "Clefs", ""),
    ("GClef", "clefs.G", ""),
    ("GClefChange", "clefs.G_change", "smaller, for clef changes"),
    ("GClef8va", "clefs.G", "same glyph as GClef; the 8 is italic text"),
    ("FClef", "clefs.F", ""),
    ("FClefChange", "clefs.F_change", "smaller, for clef changes"),
    ("FClef8va", "clefs.F", "same glyph as FClef"),
    ("CClef", "clefs.C", ""),
    ("CClefChange", "clefs.C_change", "smaller, for clef changes"),
    ("PercussionClef", "clefs.percussion", ""),
    ("PercussionClefChange", "clefs.percussion_change", ""),
    ("TabClef", "clefs.tab", "6-string TAB"),
    ("TabClefChange", "clefs.tab_change", "smaller"),

    ("#", "Note heads", ""),
    ("NoteheadWhole", "noteheads.s0", ""),
    ("NoteheadHalf", "noteheads.s1", ""),
    ("NoteheadBlack", "noteheads.s2", ""),
    ("NoteheadDoubleWhole", "noteheads.sM1", ""),
    ("NoteheadDiamondWhole", "noteheads.s0diamond", ""),
    ("NoteheadDiamondHalf", "noteheads.s1diamond", ""),
    ("NoteheadDiamondBlack", "noteheads.s2diamond", ""),
    # The old comments said noteheads.s0do -- the Aiken/sacred-harp "do" head, which is
    # a real and DIFFERENT glyph (it sits elsewhere in the font). The code points were
    # always the .triangle family, which is what NoteheadStyle.Triangle means, so the
    # names are corrected to match the drawing rather than the other way round.
    ("NoteheadTriangleWhole", "noteheads.s0triangle", ""),
    ("NoteheadTriangleHalf", "noteheads.s1triangle", ""),
    ("NoteheadTriangleBlack", "noteheads.s2triangle", ""),
    ("NoteheadSlashWhole", "noteheads.s0slash", ""),
    ("NoteheadSlashHalf", "noteheads.s1slash", ""),
    ("NoteheadSlashBlack", "noteheads.s2slash", ""),
    ("NoteheadCrossWhole", "noteheads.s0cross", ""),
    ("NoteheadCrossHalf", "noteheads.s1cross", ""),
    ("NoteheadCrossBlack", "noteheads.s2cross", ""),
    ("NoteheadXCircle", "noteheads.s2xcircle", ""),

    ("#", "Rests", ""),
    ("RestMaxima", "rests.M3", ""),
    ("RestLonga", "rests.M2", ""),
    ("RestDoubleWhole", "rests.M1", ""),
    ("RestWhole", "rests.0", ""),
    ("RestHalf", "rests.1", ""),
    ("RestQuarter", "rests.2", ""),
    ("Rest8th", "rests.3", ""),
    ("Rest16th", "rests.4", ""),
    ("Rest32nd", "rests.5", ""),
    ("Rest64th", "rests.6", ""),
    ("Rest128th", "rests.7", ""),

    ("#", "Accidentals", ""),
    ("AccidentalFlat", "accidentals.flat", ""),
    ("AccidentalNatural", "accidentals.natural", ""),
    ("AccidentalSharp", "accidentals.sharp", ""),
    ("AccidentalDoubleSharp", "accidentals.doublesharp", ""),
    ("AccidentalDoubleFlat", "accidentals.flatflat", ""),
    ("AccidentalQuarterSharp", "accidentals.sharp.slashslash.stem", "quarter sharp"),
    ("AccidentalThreeQuarterSharp", "accidentals.sharp.slashslashslash.stemstem", "three-quarter sharp"),
    ("AccidentalQuarterFlat", "accidentals.flat.slash", "quarter flat"),
    ("AccidentalThreeQuarterFlat", "accidentals.flatflat.slash", "three-quarter flat"),

    ("#", "Accidental parentheses (for courtesy/cautionary accidentals)", ""),
    ("AccidentalLeftParen", "accidentals.leftparen", "ink left of origin, advance 0"),
    ("AccidentalRightParen", "accidentals.rightparen", ""),

    ("#", "Flags", ""),
    ("Flag8thUp", "flags.u3", ""),
    ("Flag8thDown", "flags.d3", ""),
    ("Flag16thUp", "flags.u4", ""),
    ("Flag16thDown", "flags.d4", ""),
    ("Flag32ndUp", "flags.u5", ""),
    ("Flag32ndDown", "flags.d5", ""),
    ("Flag64thUp", "flags.u6", ""),
    ("Flag64thDown", "flags.d6", ""),
    ("Flag128thUp", "flags.u7", ""),
    ("Flag128thDown", "flags.d7", ""),

    ("#", "Augmentation dot", ""),
    ("AugmentationDot", "dots.dot", ""),

    # Meter digits: the PLAIN cut, which is a DIFFERENT GLYPH from the fattened one and not
    # just a different width. A time signature is \number markup, and \number prepends
    # 'font-encoding 'fetaText and no font-features at all
    # (scm/define-markup-commands.scm:3872-3981 define-markup-command for number) -- ss01 is
    # what selects the fattened digits, and nothing here asks for it.
    # ⚠️ THESE WERE fattened.* UNTIL 2026-08-14 (session 164), and the pen was the LAST half
    # of that defect to be found: the metrics moved to the plain cut earlier the same session
    # while this table still pointed the renderer at the fattened codepoints, so Lily#
    # reserved one cut's advance and drew the other's glyph.
    # MEASURED, not deduced, twice over:
    #   · ly:stencil-expr over eight books of scratch/timesig-digit-cut.ly prints the glyph
    #     name for every row, and the string "fattened" appears ZERO times in the whole dump;
    #     the names are one, four, seven ... out of emmentaler-20.otf.
    #   · the two cuts are DIFFERENT OUTLINES for all ten digits, not merely different
    #     advances: same bbox to within a unit or two, but the fattened cut carries 5-14%
    #     more ink area (scratch/plain-vs-fattened-shape.py). So this moves what is drawn on
    #     every digit-path meter, where the widths moved only the 1 and the 7.
    # ⚠️ THE PLAIN DIGITS LIVE AT ASCII U+0030-U+0039, where the fattened ones are in the
    # private use area. That is the same addressing the fetaText DYNAMIC letters already use
    # (f, m, p ... by their bare ASCII names, DynamicLetter* in Extract-EmmentalerMetrics.py).
    ("#", "Meter digits (fetaText, no font-features -- the PLAIN cut, NOT fattened)", ""),
    ("TimeSig0", "zero", ""),
    ("TimeSig1", "one", ""),
    ("TimeSig2", "two", ""),
    ("TimeSig3", "three", ""),
    ("TimeSig4", "four", ""),
    ("TimeSig5", "five", ""),
    ("TimeSig6", "six", ""),
    ("TimeSig7", "seven", ""),
    ("TimeSig8", "eight", ""),
    ("TimeSig9", "nine", ""),
    ("TimeSigCommon", "timesig.C44", ""),
    ("TimeSigCutCommon", "timesig.C22", ""),

    # A Fingering is fetaText as well (scm/define-grobs.scm:1547-1548,
    # ly:text-interface::print over fingering::calc-text) but declares font-features
    # ("cv47" "ss01") -- the figure's three MINUS tnum. Without tabular figures the digits
    # are the PROPORTIONAL fattened.<n>, and cv47 still puts the .alt shapes on 4 and 7 --
    # which is the ONLY thing that keeps these ten from being the ten below.
    # ⚠️ NOT THE SAME CUT AS THE METER, though this comment said so until 2026-08-14: a
    # Fingering asks for ss01 and \number does not, so the meter above is the PLAIN cut.
    # Read off the page, not deduced: ly:stencil-expr printed
    # `fattened.one` for a Fingering where a BassFigure printed
    # `fattened.fixedwidth.one` (audit/lp-geometry/probes/fingering-digit-width.ly).
    ("#", "Fingering digits (fetaText, cv47 + ss01 -- proportional, NOT tabular)", ""),
    ("FingeringDigit0", "fattened.zero", ""),
    ("FingeringDigit1", "fattened.one", ""),
    ("FingeringDigit2", "fattened.two", ""),
    ("FingeringDigit3", "fattened.three", ""),
    ("FingeringDigit4", "fattened.four.alt", "cv47 picks the .alt four"),
    ("FingeringDigit5", "fattened.five", ""),
    ("FingeringDigit6", "fattened.six", ""),
    ("FingeringDigit7", "fattened.seven.alt", "cv47 picks the .alt seven"),
    ("FingeringDigit8", "fattened.eight", ""),
    ("FingeringDigit9", "fattened.nine", ""),

    # A bass figure is `\number` markup (scm/translation-functions.scm:349-470
    # format-bass-figure -> make-number-markup), i.e. the fetaText encoding, and
    # scm/define-grobs.scm:354 declares BassFigure's font-features ("tnum" "cv47" "ss01").
    # Those three OpenType features are SUBSTITUTIONS, so they name the glyph LilyPond
    # actually draws: tnum -> fixedwidth.*, ss01 -> fattened.*, cv47 -> the .alt forms of
    # FOUR and SEVEN. Hence fattened.fixedwidth.<digit>, with .alt on 4 and 7 -- a
    # different digit design from the time signature's (which declares no features at all
    # and so takes the base glyphs).
    ("#", "Figured bass digits (fetaText, tnum + cv47 + ss01 applied)", ""),
    ("FigBassDigit0", "fattened.fixedwidth.zero", ""),
    ("FigBassDigit1", "fattened.fixedwidth.one", ""),
    ("FigBassDigit2", "fattened.fixedwidth.two", ""),
    ("FigBassDigit3", "fattened.fixedwidth.three", ""),
    ("FigBassDigit4", "fattened.fixedwidth.four.alt", "cv47 picks the .alt four"),
    ("FigBassDigit5", "fattened.fixedwidth.five", ""),
    ("FigBassDigit6", "fattened.fixedwidth.six", ""),
    ("FigBassDigit7", "fattened.fixedwidth.seven.alt", "cv47 picks the .alt seven"),
    ("FigBassDigit8", "fattened.fixedwidth.eight", ""),
    ("FigBassDigit9", "fattened.fixedwidth.nine", ""),
    # The alteration of a figure is fetaText too, addressed by the Unicode accidental
    # (scm/translation-functions.scm:338-343 figbass-accidental-alist -> U+266D/266E/266F
    # through make-number-markup), and the font maps those code points to the FIGBASS cuts
    # of the accidentals -- taller and narrower than the notation ones.
    ("FigBassFlat", "accidentals.flat.figbass", "U+266D in figbass-accidental-alist"),
    ("FigBassNatural", "accidentals.natural.figbass", "U+266E"),
    ("FigBassSharp", "accidentals.sharp.figbass", "U+266F"),

    ("#", "Articulations", ""),
    ("FermataAbove", "scripts.ufermata", ""),
    ("FermataBelow", "scripts.dfermata", ""),
    # CORRECTED. These four pointed at the HENZE fermatas while their comments claimed
    # the ordinary short/long ones. LilyPond keeps them as separate articulations --
    # scm/script.scm:356 (shortfermata) and :183 (henzeshortfermata), :220 and :174 for
    # the long pair -- and the font carries both families. Lily# means the ordinary one.
    ("FermataShortAbove", "scripts.ushortfermata", "angled"),
    ("FermataShortBelow", "scripts.dshortfermata", ""),
    ("FermataLongAbove", "scripts.ulongfermata", "square"),
    ("FermataLongBelow", "scripts.dlongfermata", ""),
    ("ArticAccentAbove", "scripts.sforzato", ""),
    ("ArticStaccatoAbove", "scripts.staccato", ""),
    ("ArticTenutoAbove", "scripts.tenuto", ""),
    ("ArticPortatoAbove", "scripts.uportato", ""),
    ("ArticPortatoBelow", "scripts.dportato", ""),
    ("ArticStaccatissimoAbove", "scripts.ustaccatissimo", ""),
    ("ArticStaccatissimoBelow", "scripts.dstaccatissimo", ""),
    # LilyPond 2.26.0 gave the bowing marks a direction pair where 2.24.4 drew one glyph
    # both ways: scm/script.scm:453 is (dupbow . uupbow) and :88 is (ddownbow . udownbow),
    # against 2.24.4's ("upbow" . "upbow") / ("downbow" . "downbow"). The old single glyph
    # is gone from the font, which is what makes this a port rather than a rename.
    ("ArticUpBowAbove", "scripts.uupbow", "V"),
    ("ArticUpBowBelow", "scripts.dupbow", "V, below the staff"),
    ("ArticDownBowAbove", "scripts.udownbow", "frog"),
    ("ArticDownBowBelow", "scripts.ddownbow", "frog, below the staff"),
    ("ArticFlageolet", "scripts.flageolet", "harmonic circle"),
    ("ArticMarcatoAbove", "scripts.umarcato", ""),
    ("ArticMarcatoBelow", "scripts.dmarcato", ""),
    ("ArticStopped", "scripts.stopped", "+"),
    ("PedalHeelUp", "scripts.upedalheel", "U"),
    ("PedalHeelDown", "scripts.dpedalheel", ""),
    ("PedalToeUp", "scripts.upedaltoe", "V"),
    ("PedalToeDown", "scripts.dpedaltoe", ""),
    # CORRECTED. This drew scripts.snappizzicato -- a different articulation entirely --
    # while its comment said thumb. scripts.thumb is its own glyph in the font.
    ("ArticThumb", "scripts.thumb", "cello thumb position"),
    # ...and the glyph that correction displaced, now wanted for ITSELF: the renderer
    # drew @snappizz with hand-drawn circle+line primitives (~0.5 ss taller than the
    # font's glyph) while the engraver reserved the fallback half-space box.
    ("ScriptSnappizzicato", "scripts.snappizzicato", "Bartók pizzicato — ring with rising stem"),

    ("#", "Ornaments", ""),
    ("OrnReverseTurn", "scripts.reverseturn", ""),
    ("OrnTurn", "scripts.turn", ""),
    ("OrnTrill", "scripts.trill", ""),
    # The wavy line's unit: LilyPond DRAWS a trill spanner's line by repeating this glyph
    # (lily/line-interface.cc:48-108 make_trill_line), so the renderer places copies of it
    # rather than stroking a curve of its own.
    ("OrnTrillElement", "scripts.trill_element", "the trill line's repeating unit"),
    ("MarkSegno", "scripts.segno", ""),
    ("MarkCoda", "scripts.coda", ""),
    ("OrnPrall", "scripts.prall", ""),
    ("OrnMordent", "scripts.mordent", ""),
    ("OrnPrallPrall", "scripts.prallprall", ""),

    # The wiggle's unit, and the same shape of fact as the trill element above: LilyPond's
    # arpeggio stencil is this ONE glyph stacked upward until the pile covers the chord
    # (lily/arpeggio.cc:34-41 get_squiggle, :180-183 add_at_edge), so the engraver places
    # whole copies rather than stroking a wave of its own. The glyph is one staff space tall
    # and 0.8 wide by design (mf/feta-scripts.mf:1892-1905 set_char_box (0, width#, 0,
    # height#) with height# = staff_space# and width# = 0.8 * height#), which is why an
    # arpeggio's drawn length always comes out a whole number of spaces.
    ("#", "Arpeggio", ""),
    ("Arpeggio", "scripts.arpeggio", "one staff space tall; the stencil stacks whole copies"),

    ("#", "Breathing signs", ""),
    ("BreathComma", "scripts.rcomma", "\\breathe"),
    ("CaesuraStraight", "scripts.caesura.straight", "\\caesura"),

    ("#", "Metronome (regular noteheads)", ""),
    ("MetNoteDoubleWhole", "noteheads.sM1", ""),
    ("MetNoteWhole", "noteheads.s0", ""),
    ("MetNoteHalfUp", "noteheads.s1", ""),
    ("MetNoteQuarterUp", "noteheads.s2", ""),
    ("MetNote8thUp", "noteheads.s2", ""),
    ("MetNote16thUp", "noteheads.s2", ""),

    ("#", "Repeat dots", ""),
    ("RepeatDots", "dots.dot", ""),

    # lily/system-start-delimiter.cc:36-66 staff_bracket asks the font for these
    # two by name and hangs one off each end of the vertical stroke. They are the
    # bracket's shape -- drawing a stand-in serif is inventing a second Emmentaler.
    ("#", "System-start bracket tips", ""),
    ("BracketTipUp", "brackettips.up", "top end of a SystemStartBracket"),
    ("BracketTipDown", "brackettips.down", "bottom end of a SystemStartBracket"),
]


def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    font_path = repo / "LilySharp.Core" / "Fonts" / "emmentaler-20.otf"
    out_path = repo / "LilySharp.Core" / "Svg" / "EmmentalerGlyphs.Generated.cs"

    if not font_path.exists():
        sys.stderr.write(f"Font not found: {font_path}\n")
        return 2

    font = TTFont(str(font_path))
    by_name: dict[str, int] = {}
    for codepoint, glyph in font.getBestCmap().items():
        # First mapping wins; feta names are unique in this font.
        by_name.setdefault(glyph, codepoint)

    missing = [feta for name, feta, _ in GLYPHS if name != "#" and feta not in by_name]
    if missing:
        # An absent name is fatal. This is the guard the old code-point table lacked:
        # it stops a font swap before it can silently repoint a constant.
        for feta in missing:
            sys.stderr.write(f"ERROR: glyph name not in font: {feta}\n")
        return 1

    lines: list[str] = []
    lines.append("// Lily# - Music notation compiler")
    # The generated file SHIPS, so it needs the notice GPLv3 s4 requires. It was missing
    # for as long as this generator existed; LicenceHeaderTests now fails if it goes again.
    lines.extend(LICENCE_HEADER)
    lines.append("// AUTO-GENERATED by audit/scripts/Extract-EmmentalerGlyphs.py — DO NOT EDIT MANUALLY.")
    lines.append("// Re-run the script after the bundled Emmentaler font is updated.")
    lines.append("// Source font: LilySharp.Core/Fonts/emmentaler-20.otf")
    lines.append("//")
    lines.append("// Every constant is resolved from the glyph's feta NAME, the way LilyPond asks")
    lines.append("// for glyphs (lily/clef.cc:29-52, lily/note-head.cc). The code points themselves")
    lines.append("// are private-use and NOT stable across font builds — LilyPond 2.26.0 moves 73 of")
    lines.append("// them — so the name is what carries meaning and the number is just today's slot.")
    lines.append("")
    lines.append("namespace LilySharp.Core.Svg;")
    lines.append("")
    lines.append("internal static partial class EmmentalerGlyphs")
    lines.append("{")

    first = True
    for name, feta, note in GLYPHS:
        if name == "#":
            if not first:
                lines.append("")
            lines.append(f"    // === {feta} ===")
            first = False
            continue
        codepoint = by_name[feta]
        suffix = f" ({note})" if note else ""
        lines.append(f"    /// <summary>{feta}{suffix}</summary>")
        lines.append(f"    public const char {name} = '\\u{codepoint:04X}';")
        first = False

    lines.append("}")
    lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8")
    count = sum(1 for n, _, _ in GLYPHS if n != "#")
    print(f"Wrote {out_path} ({count} glyph constants)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
