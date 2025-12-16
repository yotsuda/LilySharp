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