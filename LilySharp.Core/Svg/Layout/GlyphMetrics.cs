namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// SMuFL glyph metrics from Bravura font metadata.
/// All values are in staff spaces (the distance between two staff lines).
/// </summary>
/// <remarks>
/// Source: https://github.com/steinbergmedia/bravura/blob/master/redist/bravura_metadata.json
///
/// Coordinate system:
/// - Origin (0, 0) is at the glyph's left edge on the baseline
/// - X increases to the right
/// - Y increases upward
/// - Bounding box is defined by SW (south-west) and NE (north-east) corners
/// </remarks>
public static class GlyphMetrics
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

    // ========== Noteheads ==========

    /// <summary>Black (filled) notehead - quarter note and shorter</summary>
    public static readonly BBox NoteheadBlack = new(0, -0.5, 1.18, 0.5);

    /// <summary>Half (hollow) notehead</summary>
    public static readonly BBox NoteheadHalf = new(0, -0.5, 1.18, 0.5);

    /// <summary>Whole notehead</summary>
    public static readonly BBox NoteheadWhole = new(0, -0.5, 1.688, 0.5);

    // ========== Stem Anchors ==========

    /// <summary>Stem attachment point for upward stem (right side of notehead)</summary>
    public static readonly Anchor StemUpSE = new(1.18, 0.168);

    /// <summary>Stem attachment point for downward stem (left side of notehead)</summary>
    public static readonly Anchor StemDownNW = new(0, -0.168);

    // ========== Accidentals ==========

    /// <summary>Sharp accidental</summary>
    public static readonly BBox AccidentalSharp = new(0, -1.392, 0.996, 1.4);

    /// <summary>Flat accidental</summary>
    public static readonly BBox AccidentalFlat = new(0, -0.7, 0.904, 1.756);

    /// <summary>Natural accidental</summary>
    public static readonly BBox AccidentalNatural = new(0, -1.34, 0.672, 1.364);

    /// <summary>Double sharp accidental</summary>
    public static readonly BBox AccidentalDoubleSharp = new(0, -0.5, 0.988, 0.508);

    /// <summary>Double flat accidental</summary>
    public static readonly BBox AccidentalDoubleFlat = new(0, -0.7, 1.644, 1.748);

    // ========== Articulations ==========
    // LILYPOND-REF: mf/feta-scripts.mf glyph definitions with set_char_box

    /// <summary>Staccato dot. Symmetric circle, radius = 0.20 staff spaces.</summary>
    /// <remarks>LILYPOND-REF: feta-scripts.mf:628-637 radius# = 0.20 * staff_space#</remarks>
    public static readonly BBox ArticStaccato = new(-0.20, -0.20, 0.20, 0.20);

    /// <summary>Accent/Sforzato. Symmetric V shape.</summary>
    /// <remarks>LILYPOND-REF: feta-scripts.mf:607-615 set_char_box(0.75, 0.75, 0.42, 0.42)</remarks>
    public static readonly BBox ArticAccent = new(-0.75, -0.42, 0.75, 0.42);

    /// <summary>Tenuto. Horizontal line, thick = 1.6 * linethickness.</summary>
    /// <remarks>LILYPOND-REF: feta-scripts.mf:665-674 set_char_box(.6, .6, thick#/2, thick#/2)</remarks>
    public static readonly BBox ArticTenuto = new(-0.60, -0.10, 0.60, 0.10);

    /// <summary>Marcato above (upward V). Reference point at bottom tip.</summary>
    /// <remarks>LILYPOND-REF: feta-scripts.mf:704-746 set_char_box(0.5, 0.5, 0, 1.1)</remarks>
    public static readonly BBox ArticMarcatoAbove = new(-0.50, 0.0, 0.50, 1.10);

    /// <summary>Marcato below (downward V). Reference point at top tip.</summary>
    /// <remarks>LILYPOND-REF: feta-scripts.mf:761-764 xy_mirror_char</remarks>
    public static readonly BBox ArticMarcatoBelow = new(-0.50, -1.10, 0.50, 0.0);

    // ========== Other Glyphs ==========

    /// <summary>Augmentation dot</summary>
    public static readonly BBox AugmentationDot = new(0, -0.2, 0.4, 0.2);

    // ========== Engraving Defaults ==========

    /// <summary>Extension of ledger lines beyond notehead on each side</summary>
    public const double LegerLineExtension = 0.4;

    /// <summary>Thickness of ledger lines</summary>
    public const double LegerLineThickness = 0.16;

    /// <summary>Thickness of staff lines</summary>
    public const double StaffLineThickness = 0.13;

    /// <summary>Thickness of stems</summary>
    public const double StemThickness = 0.12;

    /// <summary>Thickness of thin barlines</summary>
    public const double ThinBarlineThickness = 0.16;

    // ========== Spacing Defaults ==========

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

    // ========== Clef and Signature Spacing ==========
    // LILYPOND-REF: scm/define-grobs.scm:810-819 Clef space-alist
    // LILYPOND-REF: scm/define-grobs.scm:1832-1839 KeySignature space-alist
    // LILYPOND-REF: scm/define-grobs.scm:3596-3602 TimeSignature space-alist

    /// <summary>
    /// G clef approximate width in staff spaces.
    /// </summary>
    public const double GClefWidth = 2.8;

    /// <summary>
    /// F clef approximate width in staff spaces.
    /// </summary>
    public const double FClefWidth = 2.5;

    /// <summary>
    /// C clef approximate width in staff spaces.
    /// </summary>
    public const double CClefWidth = 2.3;

    // LILYPOND-REF: scm/define-grobs.scm:815 (key-signature . (minimum-space . 3.5))
    /// <summary>
    /// Minimum space from clef to key signature.
    /// </summary>
    public const double ClefToKeySignatureSpace = 3.5;

    // LILYPOND-REF: scm/define-grobs.scm:816 (time-signature . (minimum-space . 4.2))
    /// <summary>
    /// Minimum space from clef to time signature.
    /// </summary>
    public const double ClefToTimeSignatureSpace = 4.2;

    // LILYPOND-REF: scm/define-grobs.scm:817 (first-note . (minimum-fixed-space . 5.0))
    /// <summary>
    /// Minimum space from clef to first note.
    /// </summary>
    public const double ClefToFirstNoteSpace = 5.0;

    // LILYPOND-REF: scm/define-grobs.scm:1834 (time-signature . (extra-space . 1.15))
    /// <summary>
    /// Extra space from key signature to time signature.
    /// </summary>
    public const double KeySignatureToTimeSignatureSpace = 1.15;

    // LILYPOND-REF: scm/define-grobs.scm:1839 (first-note . (fixed-space . 2.5))
    /// <summary>
    /// Fixed space from key signature to first note.
    /// </summary>
    public const double KeySignatureToFirstNoteSpace = 2.5;

    // LILYPOND-REF: scm/define-grobs.scm:3599 (first-note . (fixed-space . 2.0))
    /// <summary>
    /// Fixed space from time signature to first note.
    /// </summary>
    public const double TimeSignatureToFirstNoteSpace = 2.0;

    // LILYPOND-REF: scm/define-grobs.scm:1842 (extra-spacing-width . (0.0 . 1.0))
    /// <summary>
    /// Width of a single accidental in key signature including spacing.
    /// </summary>
    public const double KeySignatureAccidentalWidth = 1.0;

    /// <summary>
    /// Time signature number width (approximate).
    /// </summary>
    public const double TimeSignatureWidth = 1.8;

    // ========== Helper Methods ==========

    /// <summary>
    /// Gets the bounding box for an accidental by name.
    /// </summary>
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
    // ========== Flags ==========

    /// <summary>8th note flag (upward stem)</summary>
    public static readonly BBox Flag8thUp = new(0, -3.241, 1.056, 0.035);

    /// <summary>8th note flag (downward stem)</summary>
    public static readonly BBox Flag8thDown = new(0, -0.058, 1.224, 3.233);

    /// <summary>16th note flag (upward stem)</summary>
    public static readonly BBox Flag16thUp = new(0, -3.252, 1.116, 0.008);

    /// <summary>16th note flag (downward stem)</summary>
    public static readonly BBox Flag16thDown = new(0, -0.036, 1.164, 3.248);

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