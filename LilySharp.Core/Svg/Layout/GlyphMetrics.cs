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
internal static partial class GlyphMetrics
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

    /// <summary>
    /// Maxima (8-measure) rest ink width, in staff spaces — the church-rest glyph for
    /// duration-log -3 (<see cref="EmmentalerGlyphs.RestMaxima"/>, rests.M3).
    /// </summary>
    /// <remarks>
    /// This is a font metric and belongs in GlyphMetricsGenerated.cs, but the extractor
    /// does not yet emit rests.M3; move it there when it does. The value is not guessed:
    /// LilyPond 2.24.4 renders `R1*8` as a SINGLE maxima glyph, so the multi-measure
    /// rest's own X-extent is that glyph's width — dumped via ly:grob-extent it is
    /// exactly 1.800. It cross-checks against the run-width model on two further
    /// independent points: N=8 gives 14.190 and N=10 (maxima + breve) gives 16.434,
    /// both matching LilyPond to the last digit.
    /// LILYPOND-REF: mf/feta-rests.mf — rests.M3.
    /// </remarks>
    public const double RestMaximaWidth = 1.8;

    // ========== Engraving line/stroke thicknesses ==========
    // Line-family thicknesses live in EngravingDefaults, derived from
    // LilyPond's line-thickness (scm/paper.scm). The SMuFL/Bravura duplicates
    // that used to sit here (staff 0.13 / stem 0.12 / ledger 0.16, and the
    // thin-barline 0.16) were unused and contradicted LilyPond's values;
    // the thin barline is EngravingDefaults.ThinBarlineThickness (1.9 ×
    // line-thickness = 0.19, LilyPond's hair-thickness).

    // ========== Spacing heuristics ==========

    /// <summary>
    /// Gap between an accidental's ink right edge and its note head's ink left edge.
    /// </summary>
    /// <remarks>
    /// LilyPond's two constants, summed: <c>AccidentalPlacement.padding</c> 0.20 and
    /// <c>right-padding</c> 0.15 (scm/define-grobs.scm AccidentalPlacement;
    /// lily/accidental-placement.cc:397 and :400, applied at :412-416).
    /// <para>
    /// LilyPond adds one more term Lily# does not compute: :412 measures the accidental's
    /// right SKYLINE against the heads' skyline, not their boxes, so a vertically thin glyph
    /// ends up slightly further out. Zeroing both paddings on 2.24.4 leaves exactly that
    /// term — sharp −0.000010, flat −0.000004, double-flat −0.001996, but natural +0.017606
    /// and double-sharp +0.047704. Perturbing each padding by +0.3 moves the gap by +0.3, so
    /// the two are additive and this constant is the whole of the non-skyline part.
    /// </para>
    /// <para>
    /// It was 0.2 — LilyPond's `padding` alone, with `right-padding` missing — which put
    /// every accidental 0.15 too close to its head. See the ledger's
    /// barline.next.accidental-to-notehead.
    /// </para>
    /// </remarks>
    public const double AccidentalNoteGap = 0.35;

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

    // A change clef is its OWN glyph — clefs.G_change / F_change / C_change — not the full
    // clef scaled down, so `full * 0.75` was an approximation of a metric that is available
    // exactly. It ran 4-7% narrow (F: 2.010 against 2.146680), and since the mid-measure gap
    // after a clef is measured from the glyph's right edge, that error landed straight in the
    // spacing. LILYPOND-REF: lily/clef.cc — Clef::calc_glyph_name appends "_change".
    //
    // The right edge, not the advance: Staff_spacing reads last_ext[RIGHT], a stencil extent.

    // ⚠️ PROPERTIES, not `static readonly` fields. These read a BBox declared in the OTHER
    // half of this partial class (GlyphMetricsGenerated.cs), and C# does not define the
    // initialisation order of static fields ACROSS partial parts — as fields these read a
    // default-constructed BBox and came out 0, which silently deleted every change glyph's
    // width from the spacing. A property is evaluated on use, so the order cannot bite.

    /// <summary>G clef change width — <c>clefs.G_change</c> ink right edge.</summary>
    public static double GClefChangeWidth => ClefGChange.Right;

    /// <summary>F clef change width — <c>clefs.F_change</c> ink right edge.</summary>
    public static double FClefChangeWidth => ClefFChange.Right;

    /// <summary>C clef change width — <c>clefs.C_change</c> ink right edge.</summary>
    public static double CClefChangeWidth => ClefCChange.Right;

    // ClefChangePadding (0.5) lived here as "the padding before and after a change item".
    // It was never a LilyPond quantity: 0.5 is Clef.space-alist's `right-edge` entry, the gap
    // to the END of a line, and LilyPond has no single padding that applies to both sides of
    // a change item at all — it prices the left gap through Note_spacing or break alignment
    // and the right gap through the space-alist entry keyed on what follows (clef 1.0, key
    // 2.5, time 2.0). Every caller now goes through SpacingRules.MidMeasureChangeGaps or
    // BoundaryChangePrefix. COORDINATE_AUDIT.md §4.7.2 / §4.7.3.

    // ========== LP grob spacing defaults ==========
    // The Clef/KeySignature/TimeSignature space-alist values (clef->key 3.5,
    // clef->first-note 5.0, key->time 1.15, key->first-note 2.5, time->first-note 2.0)
    // are owned by BreakAlignSpacing (the canonical, unit-tested space-alist home). The
    // copies that were here were dead duplicates and have been removed.

    // ========== Key signature accidental widths ==========
    // LP key-signature-interface.cc uses add_at_edge(padding=0), which butts one STENCIL
    // against the next — so the per-accidental step is the glyph's ink width, not its
    // advance. The old note here called the stencil width "(=advance)"; they are different
    // numbers (a sharp inks 1.100010 and advances 1.100000, a natural inks 0.666666 and
    // advances 0.664000), and LilyPond's A-major signature measures 3.300030 = 3 x 1.100010.

    // Properties for the same reason as the change-clef widths above: cross-partial static
    // field initialisation order is undefined.

    /// <summary>Width of a sharp accidental in key signature.</summary>
    public static double KeySignatureSharpWidth => AccidentalSharp.Width;

    /// <summary>Width of a flat accidental in key signature.</summary>
    public static double KeySignatureFlatWidth => AccidentalFlat.Width;

    /// <summary>Width of a natural accidental in key signature (used for cancellation).</summary>
    public static double KeySignatureNaturalWidth => AccidentalNatural.Width;

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

    /// <summary>
    /// The note-value bucket (1=whole, 2=half, else black) a written base duration
    /// maps to. A non-1 numerator (e.g. a breve 2/1) collapses to a black head as a
    /// safe default. Centralises the formerly inlined
    /// <c>Numerator == 1 ? Denominator : 1</c> idiom.
    /// </summary>
    public static int NoteValueOf(Semantics.Fraction baseDuration) =>
        baseDuration.Numerator == 2 && baseDuration.Denominator == 1
            ? 0 // breve
            : baseDuration.Numerator == 1 ? baseDuration.Denominator : 1;

    /// <summary>Gets the notehead advance width for a given note value.</summary>
    public static double GetNoteheadAdvance(int noteValue) => noteValue switch
    {
        // Breve: the sM1 glyph is the whole head plus its side bars.
        0 => NoteheadWholeAdvance * 1.30,
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
