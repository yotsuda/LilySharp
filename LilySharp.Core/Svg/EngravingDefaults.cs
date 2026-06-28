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

    /// <summary>
    /// Base line thickness. LilyPond's layout <c>line-thickness</c> at the
    /// default 20pt staff: calc-line-thickness interpolates to 0.5pt =
    /// 0.10 staff space. Every line-family thickness derives from this,
    /// mirroring LilyPond's structure (stems 1.3×, staff lines 1.0×, …).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:52-66 calc-line-thickness.</remarks>
    public const double LineThickness = 0.1;

    /// <summary>Staff line thickness: 1.0 × line-thickness.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-symbol.cc — StaffSymbol thickness default 1.0
    /// (in line-thickness units).
    /// </remarks>
    public const double StaffLineThickness = 1.0 * LineThickness;

    /// <summary>
    /// Ledger line thickness: StaffSymbol.ledger-line-thickness = (1.0 . 0.1)
    /// → 1.0·line-thickness + 0.1·staff-space.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-symbol.cc:337-344 get_ledger_line_thickness;
    /// scm/define-grobs.scm StaffSymbol (ledger-line-thickness . (1.0 . 0.1)).
    /// </remarks>
    public const double LegerLineThickness = 1.0 * StaffLineThickness + 0.1 * 1.0;

    /// <summary>
    /// Ledger lines extend beyond the notehead by this FRACTION of the head's
    /// own width on each side — proportional, so wide whole/half noteheads get
    /// proportionally longer ledgers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:204-233 —
    /// ledger_extent = head_extent widened by length-fraction·head_width;
    /// (length-fraction . 0.25) per scm/define-grobs.scm LedgerLineSpanner.
    /// </remarks>
    public const double LedgerLengthFraction = 0.25;

    // === Stems ===

    /// <summary>Stem thickness: 1.3 × line-thickness = 0.13 staff space.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm (Stem (thickness . 1.3)).</remarks>
    public const double StemThickness = 1.3 * LineThickness;
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
    // All barline metrics scale with line-thickness, mirroring LilyPond.

    /// <summary>Thin barline: hair-thickness 1.9 × line-thickness = 0.19.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm BarLine (hair-thickness . 1.9).</remarks>
    public const double ThinBarlineThickness = 1.9 * LineThickness;

    /// <summary>Thick barline: thick-thickness 6.0 × line-thickness = 0.6.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm BarLine (thick-thickness . 6.0).</remarks>
    public const double ThickBarlineThickness = 6.0 * LineThickness;

    /// <summary>
    /// Ink gap between the segments of a compound barline:
    /// kern 3.0 × line-thickness = 0.3.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarLine (kern . 3.0);
    /// scm/bar-line.scm:766-801 — compound bars stack their glyph stencils
    /// with spacing <c>kern</c> between every pair.
    /// </remarks>
    public const double BarlineSeparation = 3.0 * LineThickness;

    /// <summary>
    /// Gap between the repeat dots and the adjacent bar segment — the same
    /// kern as between line segments (the colon is just another glyph in the
    /// compound bar).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/bar-line.scm:766-801.</remarks>
    public const double RepeatBarlineDotSeparation = BarlineSeparation;

    // === Other elements ===
    // (Hairpins draw at StaffLineThickness — LP Hairpin (thickness . 1.0)
    //  × line-thickness; the former unused HairpinThickness 0.16 is gone.)

    /// <summary>Tuplet bracket: thickness 1.6 × line-thickness = 0.16.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm TupletBracket (thickness . 1.6).</remarks>
    public const double TupletBracketThickness = 1.6 * LineThickness;

    // Rest collision avoidance
    /// <summary>Default staff position for rest center (middle line).</summary>
    public const double RestCenterPosition = 0.0;
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
    // Aliases to the auto-extracted GlyphMetrics constants (Emmentaler advance widths).
    // Existing call sites use these names; new code should prefer GlyphMetrics directly.
    public const double NoteheadWholeWidth = LilySharp.Core.Svg.Layout.GlyphMetrics.NoteheadWholeAdvance;
    public const double NoteheadHalfWidth = LilySharp.Core.Svg.Layout.GlyphMetrics.NoteheadHalfAdvance;
    public const double NoteheadBlackWidth = LilySharp.Core.Svg.Layout.GlyphMetrics.NoteheadBlackAdvance;
    /// <summary>Double-whole notehead is hand-tuned (no glyph in extracted set).</summary>
    public const double NoteheadDoubleWholeWidth = 2.296;

    // === Stem attachment points ===
    // The stem centre is shifted by -dir*thickness/2 so the stem EDGE (not its
    // centre) sits on the notehead boundary: an up-stem's right edge aligns with
    // the notehead's right edge, a down-stem's left edge with the notehead's left
    // edge. Without this the half-thickness pokes outside the notehead.
    // LILYPOND-REF: lily/stem.cc internal_calc_stem_offset_from_head
    //   (r += -d * rule_thick * 0.5).
    public const double StemUpAttachX = NoteheadBlackWidth - StemThickness / 2;
    public const double StemUpAttachY = 0.168;
    public const double StemDownAttachX = StemThickness / 2;
    public const double StemDownAttachY = -0.168;


    // === Staff geometry (local engraver coordinates) ===
    /// <summary>Device-Y of the staff's middle line in the engravers' local
    /// staff-space frame (top line = 0, bottom line = 4). A notehead's local Y is
    /// <c>StaffMiddle - staffPosition / 2</c> (staffPosition is half-spaces from the
    /// middle line, LilyPond convention). Use this instead of a bare <c>2.0</c>.</summary>
    public const double StaffMiddle = 2.0;

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

    /// <summary>
    /// Tab string-line spacing in staff spaces for a given string count. Wider than
    /// the 1.0 of a normal staff so the larger fret numbers fit (≈1.5× the notation
    /// staff-space, as in LilyPond's TabStaff); tightened a little as strings are
    /// added so a 5- or 6-string staff does not grow excessively tall.
    /// </summary>
    public static double TabStringSpace(int stringCount) => stringCount switch
    {
        >= 6 => 1.3,
        5 => 1.4,
        _ => 1.5,
    };

    // === Barline rendering ===
    /// <summary>
    /// Total width of the repeat-dots block including its kern to the next
    /// bar segment: dot diameter + kern.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/bar-line.scm:766-801 — the colon glyph is
    /// stacked with the same kern as the line segments.</remarks>
    public const double RepeatDotsOffset = 2 * RepeatDotRadius + RepeatBarlineDotSeparation;

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