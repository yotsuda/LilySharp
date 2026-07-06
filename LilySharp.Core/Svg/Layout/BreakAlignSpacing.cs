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
/// Break-align symbol types for system prefix element ordering and spacing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm break-align-orders
/// LILYPOND-REF: lily/break-alignment-interface.cc
/// </remarks>
public enum BreakAlignSymbol
{
    /// <summary>Left edge of the system prefix (the starting reference point).</summary>
    LeftEdge,
    /// <summary>Pitch-range indicator (ambitus) shown at the start of the staff.</summary>
    Ambitus,
    /// <summary>Breath mark positioned among the prefix items.</summary>
    BreathingSign,
    /// <summary>The clef at the start of the line.</summary>
    Clef,
    /// <summary>Naturals cancelling the outgoing key signature.</summary>
    KeyCancellation,
    /// <summary>The key signature.</summary>
    KeySignature,
    /// <summary>The time signature.</summary>
    TimeSignature,
    /// <summary>The barline at the break point.</summary>
    StaffBar,
    /// <summary>The cue clef opening a cue passage.</summary>
    CueClef,
    /// <summary>The clef restoring the main part after a cue passage.</summary>
    CueEndClef,
    /// <summary>The first note column following the prefix.</summary>
    FirstNote,
    /// <summary>Right edge of the system prefix.</summary>
    RightEdge
}

/// <summary>
/// Spacing style types from LilyPond's space-alist.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/break-alignment-interface.cc:108-150 get_break_align_spacing()
/// Each style determines how the space value is interpreted:
/// - ExtraSpace: distance = extent(right) + value (space added to right extent)
/// - MinimumSpace: distance = max(extent(right) + min_pad, value)
/// - FixedSpace: distance = extent(left_right_edge) + value
/// - MinimumFixedSpace: distance = max(extent(left_right_edge) + value, min_from_left_edge)
/// - SemiFixedSpace: distance = extent(left_right_edge) + value/2 + natural/2
/// </remarks>
public enum SpacingStyle
{
    /// <summary>Adds the value to the right item's extent: <c>distance = extent(right) + value</c>.</summary>
    ExtraSpace,
    /// <summary>At least the value from the left item: <c>distance = max(extent(right) + min_pad, value)</c>.</summary>
    MinimumSpace,
    /// <summary>Fixed distance from the left/right edge: <c>distance = extent(left_right_edge) + value</c>.</summary>
    FixedSpace,
    /// <summary>Fixed spacing floored at a minimum: <c>distance = max(extent(left_right_edge) + value, min_from_left_edge)</c>.</summary>
    MinimumFixedSpace,
    /// <summary>Half natural, half fixed: <c>distance = extent(left_right_edge) + value/2 + natural/2</c>.</summary>
    SemiFixedSpace,
    /// <summary>Mostly fixed but slightly compressible, allowing the spacing to shrink under compression.</summary>
    SemiShrinkSpace
}

/// <summary>
/// A spacing entry from LilyPond's space-alist: (spacing-style . value).
/// </summary>
public readonly record struct SpacingEntry(SpacingStyle Style, double Value);

/// <summary>
/// Implements LilyPond's break-alignment-interface for system prefix spacing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/break-alignment-interface.cc:1-200
/// LILYPOND-REF: scm/define-grobs.scm break-align-orders, Clef.space-alist,
///               KeySignature.space-alist, TimeSignature.space-alist
///
/// The break-alignment system controls ordering and spacing of "breakable" items
/// (clef, key signature, time signature) at the start of each system line.
/// Each item type has a space-alist that maps adjacent item types to spacing values.
/// </remarks>
internal static class BreakAlignSpacing
{
    /// <summary>
    /// Default break-align order for start-of-line (index 2 in LP's break-align-orders vector).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BreakAlignment.break-align-orders
    /// Start-of-line: left-edge ambitus breathing-sign clef key-cancellation
    ///                key-signature time-signature staff-bar cue-clef cue-end-clef
    /// </remarks>
    public static readonly BreakAlignSymbol[] StartOfLineOrder = new[]
    {
        BreakAlignSymbol.LeftEdge,
        BreakAlignSymbol.Ambitus,
        BreakAlignSymbol.BreathingSign,
        BreakAlignSymbol.Clef,
        BreakAlignSymbol.KeyCancellation,
        BreakAlignSymbol.KeySignature,
        BreakAlignSymbol.TimeSignature,
        BreakAlignSymbol.StaffBar,
        BreakAlignSymbol.CueClef,
        BreakAlignSymbol.CueEndClef,
    };

    /// <summary>
    /// Looks up the spacing between two adjacent break-align symbols.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:108-150
    /// Each break-aligned grob has a space-alist that maps the next symbol
    /// to a (spacing-style . value) pair.
    /// </remarks>
    public static SpacingEntry GetSpacing(BreakAlignSymbol left, BreakAlignSymbol right)
    {
        return left switch
        {
            BreakAlignSymbol.Clef => GetClefSpacing(right),
            BreakAlignSymbol.KeyCancellation => GetKeyCancellationSpacing(right),
            BreakAlignSymbol.KeySignature => GetKeySignatureSpacing(right),
            BreakAlignSymbol.TimeSignature => GetTimeSignatureSpacing(right),
            BreakAlignSymbol.LeftEdge => new SpacingEntry(SpacingStyle.ExtraSpace, 0.0),
            BreakAlignSymbol.StaffBar => GetStaffBarSpacing(right),
            _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
        };
    }

    /// <summary>
    /// Clef.space-alist from LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:800-834 Clef
    /// </remarks>
    private static SpacingEntry GetClefSpacing(BreakAlignSymbol right) => right switch
    {
        // (key-cancellation . (minimum-space . 3.5))
        BreakAlignSymbol.KeyCancellation =>
            new SpacingEntry(SpacingStyle.MinimumSpace, 3.5),
        // (key-signature . (minimum-space . 3.5))
        BreakAlignSymbol.KeySignature =>
            new SpacingEntry(SpacingStyle.MinimumSpace, 3.5),
        // (time-signature . (minimum-space . 4.2))
        BreakAlignSymbol.TimeSignature =>
            new SpacingEntry(SpacingStyle.MinimumSpace, 4.2),
        // (first-note . (minimum-fixed-space . 5.0))
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.MinimumFixedSpace, 5.0),
        // (right-edge . (extra-space . 0.5))
        BreakAlignSymbol.RightEdge =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.5),
        // (staff-bar . (minimum-space . 3.7))
        BreakAlignSymbol.StaffBar =>
            new SpacingEntry(SpacingStyle.MinimumSpace, 3.7),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
    };

    /// <summary>
    /// KeyCancellation.space-alist from LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1800-1825 KeyCancellation
    /// </remarks>
    private static SpacingEntry GetKeyCancellationSpacing(BreakAlignSymbol right) => right switch
    {
        // (key-signature . (extra-space . 0.3))
        BreakAlignSymbol.KeySignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.3),
        // (time-signature . (extra-space . 1.15))
        BreakAlignSymbol.TimeSignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.15),
        // (first-note . (fixed-space . 2.5))
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.FixedSpace, 2.5),
        // (staff-bar . (extra-space . 0.6))
        BreakAlignSymbol.StaffBar =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.6),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 0.5)
    };

    /// <summary>
    /// KeySignature.space-alist from LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1830-1845 KeySignature
    /// </remarks>
    private static SpacingEntry GetKeySignatureSpacing(BreakAlignSymbol right) => right switch
    {
        // (time-signature . (extra-space . 1.15))
        BreakAlignSymbol.TimeSignature =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.15),
        // (first-note . (fixed-space . 2.5))
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.FixedSpace, 2.5),
        // (staff-bar . (extra-space . 1.1))
        BreakAlignSymbol.StaffBar =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 1.1),
        // (right-edge . (extra-space . 0.5))
        BreakAlignSymbol.RightEdge =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.5),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
    };

    /// <summary>
    /// TimeSignature.space-alist from LilyPond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3595-3610 TimeSignature
    /// </remarks>
    private static SpacingEntry GetTimeSignatureSpacing(BreakAlignSymbol right) => right switch
    {
        // (first-note . (fixed-space . 2.0))
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.FixedSpace, 2.0),
        // (right-edge . (extra-space . 0.5))
        BreakAlignSymbol.RightEdge =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.5),
        // (staff-bar . (minimum-space . 2.0))
        BreakAlignSymbol.StaffBar =>
            new SpacingEntry(SpacingStyle.MinimumSpace, 2.0),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
    };

    /// <summary>
    /// StaffBar (barline at break) space-alist.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarLine.space-alist (used at break points)
    /// </remarks>
    private static SpacingEntry GetStaffBarSpacing(BreakAlignSymbol right) => right switch
    {
        BreakAlignSymbol.FirstNote =>
            new SpacingEntry(SpacingStyle.SemiFixedSpace, 1.3),
        BreakAlignSymbol.RightEdge =>
            new SpacingEntry(SpacingStyle.ExtraSpace, 0.0),
        _ => new SpacingEntry(SpacingStyle.ExtraSpace, 1.0)
    };

    /// <summary>
    /// Calculates the effective distance for a spacing entry, considering item extents.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc:108-150
    /// The interpretation depends on the spacing style:
    /// - ExtraSpace: rightExtent + value
    /// - MinimumSpace: max(value, rightExtent + minPad)
    /// - FixedSpace: leftItemRightExtent + value
    /// - MinimumFixedSpace: max(leftItemRightExtent + value, value from left edge)
    /// - SemiFixedSpace: (leftItemRightExtent + value) / 2 + naturalDistance / 2
    /// </remarks>
    public static double CalculateDistance(SpacingEntry entry,
        double leftItemRightExtent, double rightItemLeftExtent)
    {
        const double minPad = 0.1;  // minimum padding between items

        return entry.Style switch
        {
            // LILYPOND-REF: extra-space: adds value to the right extent of left item
            SpacingStyle.ExtraSpace =>
                leftItemRightExtent + entry.Value,

            // LILYPOND-REF: minimum-space: at least 'value' from left edge of left item
            SpacingStyle.MinimumSpace =>
                Math.Max(entry.Value, leftItemRightExtent + minPad),

            // LILYPOND-REF: fixed-space: fixed distance from right edge of left item
            SpacingStyle.FixedSpace =>
                leftItemRightExtent + entry.Value,

            // LILYPOND-REF: minimum-fixed-space: at least 'value' from left edge (like minimum-space)
            // but also ensures fixed spacing from right edge when item is wider than value
            SpacingStyle.MinimumFixedSpace =>
                Math.Max(entry.Value, leftItemRightExtent + minPad),

            // LILYPOND-REF: semi-fixed-space: half natural, half fixed
            SpacingStyle.SemiFixedSpace =>
                (leftItemRightExtent + entry.Value) / 2.0 +
                (leftItemRightExtent + rightItemLeftExtent + minPad) / 2.0,

            // LILYPOND-REF: semi-shrink-space: mostly fixed, slightly compressible
            SpacingStyle.SemiShrinkSpace =>
                Math.Max(leftItemRightExtent + entry.Value * 0.8, entry.Value * 0.6),

            _ => leftItemRightExtent + entry.Value
        };
    }

    /// <summary>
    /// Calculates the system prefix width using break-alignment spacing rules.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/break-alignment-interface.cc
    /// Iterates through the break-align-orders for start-of-line,
    /// querying space-alist for each pair of adjacent present items.
    /// </remarks>
    public static double CalculatePrefixWidth(
        double clefWidth,
        int keyAccidentalCount, bool keySharps,
        bool includeTimeSignature, int timeSigBeats = 4, int timeSigBeatType = 4)
    {
        // Build the list of present items in break-align order
        // At minimum we have Clef → FirstNote
        // Optionally: Clef → KeySignature → TimeSignature → FirstNote

        double distance = 0;
        var currentSymbol = BreakAlignSymbol.Clef;
        double currentRightExtent = clefWidth;

        // Clef → KeySignature (if present)
        if (keyAccidentalCount > 0)
        {
            var entry = GetSpacing(currentSymbol, BreakAlignSymbol.KeySignature);
            double keyLeftExtent = 0;  // key signature starts at left edge
            distance += CalculateDistance(entry, currentRightExtent, keyLeftExtent);

            // Key signature width
            double accWidth = GlyphMetrics.GetKeySignatureAccidentalWidth(keySharps);
            currentRightExtent = keyAccidentalCount * accWidth;
            currentSymbol = BreakAlignSymbol.KeySignature;
        }

        // → TimeSignature (if present, first system only)
        if (includeTimeSignature)
        {
            var entry = GetSpacing(currentSymbol, BreakAlignSymbol.TimeSignature);
            double timeSigLeftExtent = 0;
            distance += CalculateDistance(entry, currentRightExtent, timeSigLeftExtent);

            double timeSigWidth = GlyphMetrics.GetTimeSigWidth(timeSigBeats, timeSigBeatType);
            currentRightExtent = timeSigWidth;
            currentSymbol = BreakAlignSymbol.TimeSignature;
        }

        // The prefix ends at the last item's INK. The prefix→first-note
        // distance is NOT part of the prefix: it is carried by the first
        // measure's leading spring (see FirstNoteSpring) so it can take part
        // in spring solving with the proper minimum — adding it here AND in
        // the spring double-counted the gap and line-start measures came out
        // ~3x wider than LilyPond's.
        return distance + currentRightExtent;
    }

    /// <summary>
    /// Ideal/minimum distance from the END of the line-start prefix to the
    /// first note column, per the LAST prefix item's space-alist entry.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm space-alist (first-note . ...):
    ///   Clef           (minimum-fixed-space . 5.0)  — rigid
    ///   KeySignature   (shrink-space . 2.5)         — compressible
    ///   TimeSignature  (semi-shrink-space . 2.0)    — compressible to half
    /// LILYPOND-REF: lily/staff-spacing.cc Staff_spacing::get_spacing —
    ///   the style decides how much of the ideal survives compression.
    /// </remarks>
    public static (double Ideal, double Min) FirstNoteSpring(
        int keyAccidentalCount, bool includeTimeSignature)
    {
        if (includeTimeSignature)
            return (2.0, 1.0);   // semi-shrink: fixed = d/2
        if (keyAccidentalCount > 0)
            return (2.5, 1.25);  // shrink-space: generously compressible
        return (5.0, 5.0);       // minimum-fixed: rigid
    }
}
