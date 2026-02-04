namespace LilySharp.Core.Svg;

/// <summary>
/// Default metrics for music engraving.
/// All values are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-options.cc:52-63 Spacing_options constructor (defaults)
/// LILYPOND-REF: scm/define-grobs.scm (space-alist for Clef, BarLine, TimeSignature, StaffGrouper)
/// </remarks>
public static class EngravingDefaults
{
    // === Staff and lines ===
    public const double StaffLineThickness = 0.13;
    public const double LegerLineThickness = 0.16;
    public const double LegerLineExtension = 0.4;
    public const double LineThickness = 0.1;

    // === Stems ===
    public const double StemThickness = 0.12;
    public const double IdealStemLength = 3.5;
    public const double MinStemLength = 2.5;
    public const double DefaultStemLength = 3.5;

    // === Beams ===
    public const double BeamThickness = 0.48;
    public const double BeamSpacing = 0.25;
    /// <summary>Distance between beam centers for multiple beams.</summary>
    public const double BeamTranslation = (2.0 + LineThickness - BeamThickness) / 2.0;
    /// <summary>Length of a beamlet (partial beam).</summary>
    public const double BeamletLength = 1.0;

    // === Barlines ===
    public const double ThinBarlineThickness = 0.16;
    public const double ThickBarlineThickness = 0.5;
    public const double BarlineSeparation = 0.4;
    public const double RepeatBarlineDotSeparation = 0.16;

    // === Slurs and ties ===
    public const double SlurEndpointThickness = 0.1;
    public const double SlurMidpointThickness = 0.22;
    public const double TieEndpointThickness = 0.1;
    public const double TieMidpointThickness = 0.22;

    // === Other elements ===
    public const double HairpinThickness = 0.16;
    public const double TupletBracketThickness = 0.16;
    public const double BracketThickness = 0.5;

    // Rest collision avoidance
    /// <summary>Default staff position for rest center (middle line).</summary>
    public const double RestCenterPosition = 4.0;
    /// <summary>Extent of rest collision box in staff positions.</summary>
    public const double RestExtent = 2.0;
    /// <summary>Minimum distance between rest and beam in staff positions.</summary>
    public const double RestBeamMinDistance = 1.0;
    /// <summary>Threshold for applying rest shift (in staff positions).</summary>
    public const double RestShiftThreshold = 0.1;

    // === Flags ===
    /// <summary>Width of a flag glyph (in staff spaces).</summary>
    public const double FlagWidth = 1.2;
    /// <summary>Base height of a flag (eighth note flag, in staff spaces).</summary>
    public const double FlagBaseHeight = 2.5;
    /// <summary>Additional height per beam level (in staff spaces).</summary>
    public const double FlagHeightIncrement = 0.5;

    // === Notehead dimensions ===
    public const double NoteheadWholeWidth = 1.688;
    public const double NoteheadHalfWidth = 1.18;
    public const double NoteheadBlackWidth = 1.18;
    public const double NoteheadDoubleWholeWidth = 2.296;

    // === Stem attachment points ===
    public const double StemUpAttachX = 1.18;
    public const double StemUpAttachY = 0.168;
    public const double StemDownAttachX = 0.0;
    public const double StemDownAttachY = -0.168;


    // === Notehead collision ===
    /// <summary>Half-height of notehead for collision detection (in staff positions).</summary>
    public const double NoteheadHalfHeight = 0.5;
    /// <summary>Height of notehead for skyline calculation (in staff spaces).</summary>
    public const double NoteheadHeight = 1.0;

    // === Rest dimensions ===
    /// <summary>Approximate height of rest glyph for skyline calculation (in staff spaces).</summary>
    public const double RestHeight = 1.0;
    /// <summary>Approximate width of rest glyph for skyline calculation (in staff spaces).</summary>
    public const double RestWidth = 1.0;

    // === Dots ===
    /// <summary>Gap between notehead and augmentation dot (in staff spaces).</summary>
    public const double DotGap = 0.3;

    // === Repeat dots ===
    /// <summary>Radius of repeat barline dots (in staff spaces).</summary>
    public const double RepeatDotRadius = 0.2;
    /// <summary>Y position of upper repeat dot (in staff spaces from top).</summary>
    public const double RepeatDotPosition1 = 1.5;
    /// <summary>Y position of lower repeat dot (in staff spaces from top).</summary>
    public const double RepeatDotPosition2 = 2.5;

    // === Spacing (Lilypond-compatible) ===
    // See: lily/spacing-options.cc, lily/spacing-spanner.cc

    /// <summary>
    /// Spacing increment, approximately notehead width.
    /// Lilypond default: 1.2 staff spaces.
    /// </summary>
    public const double SpacingIncrement = 1.2;
    // === Element spacing (from scm/define-grobs.scm) ===
    // Clef space-alist
    /// <summary>Space from clef to time-signature.</summary>
    public const double ClefToTimeSignatureSpace = 4.2;
    /// <summary>Space from clef to first-note.</summary>
    public const double ClefToFirstNoteSpace = 5.0;
    /// <summary>Space from clef to next-note.</summary>
    public const double ClefToNextNoteSpace = 1.0;

    // TimeSignature space-alist
    /// <summary>Space from time-signature to first-note.</summary>
    public const double TimeSignatureToFirstNoteSpace = 2.0;
    /// <summary>Space from time-signature to right-edge.</summary>
    public const double TimeSignatureToRightEdgeSpace = 0.5;

    // BarLine space-alist
    /// <summary>Space from bar-line to first-note.</summary>
    public const double BarLineToFirstNoteSpace = 1.3;
    /// <summary>Space from bar-line to clef.</summary>
    public const double BarLineToClefSpace = 1.0;

    // === Staff spacing (from scm/define-grobs.scm StaffGrouper) ===
    /// <summary>Basic distance between staves in a group (center to center).</summary>
    public const double StaffStaffBasicDistance = 9.0;
    /// <summary>Minimum distance between staves in a group.</summary>
    public const double StaffStaffMinimumDistance = 7.0;
    /// <summary>Padding between staves.</summary>
    public const double StaffStaffPadding = 1.0;


    /// <summary>
    /// Space for shortest duration.
    /// Lilypond default: 2.0 (so shortest note gets 2.0 * increment space).
    /// </summary>
    public const double ShortestDurationSpace = 2.0;

    /// <summary>
    /// Base shortest duration as fraction (1/8 = eighth note).
    /// Notes shorter than this use linear spacing instead of logarithmic.
    /// </summary>
    public const double BaseShortestDuration = 0.125; // 1/8

    /// <summary>Maximum stiffness for zero-duration items.</summary>
    public const double MaxStiffness = 10.0;

    // === Barline rendering ===
    /// <summary>Space after repeat dots before barline (in staff spaces).</summary>
    public const double RepeatDotsOffset = 0.6;

    // === Tie/Slur rendering ===
    /// <summary>Tie thickness multiplier for rendering (deprecated, use TieMidThickness).</summary>
    public const double TieRenderThickness = 0.12;
    /// <summary>Slur thickness multiplier for rendering (deprecated, use SlurMidThickness).</summary>
    public const double SlurRenderThickness = 0.15;

    // LilyPond-style variable thickness parameters
    // Reference: LilyPond's 'thickness' property (distance between arcs at thickest point)
    // and 'line-thickness' property (diameter of virtual pen at endpoints)

    /// <summary>Maximum thickness of tie at the middle (in staff spaces). Endpoints are thin.</summary>
    public const double TieMidThickness = 0.25;

    /// <summary>Maximum thickness of slur at the middle (in staff spaces). Endpoints are thin.</summary>
    public const double SlurMidThickness = 0.30;


    // === Conversion helpers ===

    /// <summary>Converts staff spaces to staff positions (1 space = 2 positions).</summary>
    public static double ToStaffPositions(double staffSpaces) => staffSpaces * 2;

    /// <summary>Converts staff positions to staff spaces (1 position = 0.5 spaces).</summary>
    public static double ToStaffSpaces(double staffPositions) => staffPositions / 2;
}