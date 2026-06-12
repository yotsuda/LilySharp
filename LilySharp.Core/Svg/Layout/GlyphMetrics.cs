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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Glyph metrics for the Emmentaler music font.
/// All values are in staff spaces (the distance between two staff lines).
/// </summary>
/// <remarks>
/// This file holds <b>hand-tuned</b> constants — engraving thicknesses, spacing
/// heuristics, and LilyPond grob defaults. Values that can be derived directly
/// from the font binary (BBoxes, advance widths) live in
/// <c>GlyphMetricsGenerated.cs</c>, which is produced by
/// <c>audit/scripts/Extract-EmmentalerMetrics.py</c>.
///
/// Coordinate system:
/// - Origin (0, 0) is at the glyph's left edge on the baseline
/// - X increases to the right
/// - Y increases upward
/// - Bounding box is defined by SW (south-west) and NE (north-east) corners
/// </remarks>
public static partial class GlyphMetrics
{
    /// <summary>
    /// Bounding box for a glyph, in staff spaces.
    /// </summary>
    public readonly record struct BBox(double Left, double Bottom, double Right, double Top)
    {
        public double Width => Right - Left;
        public double Height => Top - Bottom;
        public double CenterX => (Left + Right) / 2;
        public double CenterY => (Bottom + Top) / 2;
    }

    /// <summary>
    /// Anchor point for stem attachment, in staff spaces relative to glyph origin.
    /// </summary>
    public readonly record struct Anchor(double X, double Y);

    // ========== Stem Anchors ==========
    // Stem attachment uses the notehead's advance width on the X axis (LP convention)
    // and a small vertical offset to account for the notehead curve. The Y offset is
    // hand-tuned (font does not expose stem-attach anchors via OTF tables).
    // LILYPOND-REF: lily/stem.cc — stem attaches at notehead.extent(X_AXIS)[RIGHT]

    /// <summary>Stem attachment point for upward stem (right side of filled notehead).</summary>
    /// <remarks>X = NoteheadBlackAdvance, Y from Emmentaler stem anchor convention.</remarks>
    public static readonly Anchor StemUpSE = new(NoteheadBlackAdvance, 0.168);

    /// <summary>Stem attachment point for downward stem (left side of notehead).</summary>
    public static readonly Anchor StemDownNW = new(0, -0.168);

    // ========== Accidental parenthesis ==========

    /// <summary>
    /// Combined ink width both parens add around a parenthesized accidental:
    /// parenthesize() juxtaposes stencil EXTENTS with zero padding, so the
    /// added width is the two paren glyphs' bounding-box widths — NOT their
    /// advances (leftparen draws behind its origin with advance 0).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mf/feta-parenthesis.mf — accidentals.leftparen/rightparen
    /// LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize() adds parens with 0 padding
    /// </remarks>
    /// (Computed property: static-field initialization order across partial
    /// class files is unspecified, and the BBoxes live in the generated file.)
    public static double AccidentalParensInkWidth =>
        AccidentalLeftParen.Width + AccidentalRightParen.Width;

    // ========== Engraving line/stroke thicknesses ==========
    // Line-family thicknesses live in EngravingDefaults, derived from
    // LilyPond's line-thickness (scm/paper.scm). The SMuFL/Bravura duplicates
    // that used to sit here (staff 0.13 / stem 0.12 / ledger 0.16) were
    // unused and contradicted LilyPond's values.

    /// <summary>Thickness of thin barlines.</summary>
    public const double ThinBarlineThickness = 0.16;

    // ========== Spacing heuristics ==========

    /// <summary>
    /// Minimum gap between accidental and notehead, in staff spaces.
    /// This is the optical separation, not edge-to-edge distance.
    /// </summary>
    public const double AccidentalNoteGap = 0.2;

    /// <summary>
    /// Minimum gap between adjacent items (note-to-note), in staff spaces.
    /// </summary>
    public const double MinItemGap = 0.4;

    /// <summary>
    /// Padding between barline and adjacent item, in staff spaces.
    /// </summary>
    public const double BarlinePadding = 0.8;

    // ========== Clef widths (advance) and change-clef variants ==========
    // Full clef advance widths come from the font (GlyphMetricsGenerated). Change
    // (mid-measure) clef variants are 75% of full size — LP draws them at reduced
    // font-size rather than as separate glyphs.
    // LILYPOND-REF: lily/clef.cc:29-52 — "_change" suffix glyphs are ~75% of full size

    /// <summary>G clef advance width (alias for GClefAdvance, kept for source compat).</summary>
    public const double GClefWidth = GClefAdvance;

    /// <summary>F clef advance width (alias for FClefAdvance).</summary>
    public const double FClefWidth = FClefAdvance;

    /// <summary>C clef advance width (alias for CClefAdvance).</summary>
    public const double CClefWidth = CClefAdvance;

    /// <summary>G clef change width (approximately 75% of full G clef).</summary>
    public const double GClefChangeWidth = GClefWidth * 0.75;

    /// <summary>F clef change width (approximately 75% of full F clef).</summary>
    public const double FClefChangeWidth = FClefWidth * 0.75;

    /// <summary>
    /// C clef change width (approximately 75% of full C clef).
    /// LILYPOND-REF: C clef has no separate _change glyph in Emmentaler; drawn at reduced font-size.
    /// </summary>
    public const double CClefChangeWidth = CClefWidth * 0.75;

    /// <summary>
    /// Padding before and after a mid-measure clef change.
    /// LILYPOND-REF: scm/define-grobs.scm:800-834 — Clef space-alist
    /// </summary>
    public const double ClefChangePadding = 0.5;

    // ========== LP grob spacing defaults ==========
    // LILYPOND-REF: scm/define-grobs.scm — Clef / KeySignature / TimeSignature space-alist

    /// <summary>Minimum space from clef to key signature.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:815 (key-signature . (minimum-space . 3.5))</remarks>
    public const double ClefToKeySignatureSpace = 3.5;

    /// <summary>Minimum space from clef to time signature.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:816 (time-signature . (minimum-space . 4.2))</remarks>
    public const double ClefToTimeSignatureSpace = 4.2;

    /// <summary>Minimum space from clef to first note.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:817 (first-note . (minimum-fixed-space . 5.0))</remarks>
    public const double ClefToFirstNoteSpace = 5.0;

    /// <summary>Extra space from key signature to time signature.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1834 (time-signature . (extra-space . 1.15))</remarks>
    public const double KeySignatureToTimeSignatureSpace = 1.15;

    /// <summary>Fixed space from key signature to first note.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1839 (first-note . (fixed-space . 2.5))</remarks>
    public const double KeySignatureToFirstNoteSpace = 2.5;

    /// <summary>Fixed space from time signature to first note.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3599 (first-note . (fixed-space . 2.0))</remarks>
    public const double TimeSignatureToFirstNoteSpace = 2.0;

    // ========== Key signature accidental widths ==========
    // LP key-signature-interface.cc uses add_at_edge(padding=0) which places
    // accidentals edge-to-edge based on stencil (=advance) width.

    /// <summary>Width of a sharp accidental in key signature.</summary>
    public const double KeySignatureSharpWidth = AccidentalSharpAdvance;

    /// <summary>Width of a flat accidental in key signature.</summary>
    public const double KeySignatureFlatWidth = AccidentalFlatAdvance;

    /// <summary>Width of a natural accidental in key signature (used for cancellation).</summary>
    public const double KeySignatureNaturalWidth = AccidentalNaturalAdvance;

    /// <summary>Gets the per-accidental width for a key signature based on accidental type.</summary>
    public static double GetKeySignatureAccidentalWidth(bool isSharps) =>
        isSharps ? KeySignatureSharpWidth : KeySignatureFlatWidth;

    // ========== Time signature widths ==========

    /// <summary>Gets the width of a time signature based on its digit widths.</summary>
    public static double GetTimeSigWidth(int beats, int beatType) =>
        System.Math.Max(GetTimeSigDigitWidth(beats), GetTimeSigDigitWidth(beatType));

    /// <summary>Gets the advance width of a single time signature digit.</summary>
    public static double GetTimeSigDigitWidth(int digit) => digit switch
    {
        0 => TimeSigDigit0Advance,
        1 => TimeSigDigit1Advance,
        2 => TimeSigDigit2Advance,
        3 => TimeSigDigit3Advance,
        4 => TimeSigDigit4Advance,
        5 => TimeSigDigit5Advance,
        6 => TimeSigDigit6Advance,
        7 => TimeSigDigit7Advance,
        8 => TimeSigDigit8Advance,
        9 => TimeSigDigit9Advance,
        _ => TimeSigDigit0Advance, // fallback to widest common digit
    };

    // ========== Helper methods ==========

    /// <summary>Gets the rest glyph bounding box for a given note value.</summary>
    public static BBox GetRestBBox(int noteValue) => noteValue switch
    {
        1 => RestWhole,
        2 => RestHalf,
        4 => RestQuarter,
        8 => Rest8th,
        16 => Rest16th,
        32 => Rest32nd,
        64 => Rest64th,
        128 => Rest128th,
        _ => RestQuarter
    };

    /// <summary>Gets the bounding box for an accidental by name.</summary>
    public static BBox GetAccidentalBBox(string? accidental) => accidental switch
    {
        "sharp" => AccidentalSharp,
        "flat" => AccidentalFlat,
        "natural" => AccidentalNatural,
        "doubleSharp" => AccidentalDoubleSharp,
        "doubleFlat" => AccidentalDoubleFlat,
        _ => default
    };

    /// <summary>
    /// Gets the notehead bounding box for a given note value.
    /// </summary>
    /// <param name="noteValue">1=whole, 2=half, 4=quarter, etc.</param>
    public static BBox GetNoteheadBBox(int noteValue) => noteValue switch
    {
        1 => NoteheadWhole,
        2 => NoteheadHalf,
        _ => NoteheadBlack
    };

    /// <summary>Gets the notehead advance width for a given note value.</summary>
    public static double GetNoteheadAdvance(int noteValue) => noteValue switch
    {
        1 => NoteheadWholeAdvance,
        2 => NoteheadHalfAdvance,
        _ => NoteheadBlackAdvance,
    };

    /// <summary>
    /// Gets the flag bounding box for a given note value and stem direction.
    /// </summary>
    /// <param name="noteValue">8=eighth, 16=sixteenth, etc.</param>
    /// <param name="stemUp">True if stem points upward</param>
    /// <returns>Flag bounding box, or default if no flag needed</returns>
    public static BBox GetFlagBBox(int noteValue, bool stemUp) => (noteValue, stemUp) switch
    {
        (8, true) => Flag8thUp,
        (8, false) => Flag8thDown,
        (16, true) => Flag16thUp,
        (16, false) => Flag16thDown,
        // For 32nd, 64th etc., use 16th as approximation (they're similar width)
        (>= 32, true) => Flag16thUp,
        (>= 32, false) => Flag16thDown,
        _ => default
    };
}
