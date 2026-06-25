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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout result for a single chord name.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm - ChordName grob properties
/// </remarks>
public readonly record struct ChordNameLayout(
    int MeasureIndex,
    double X,                // X position (staff spaces from page left)
    double Y,                // Y position (staff spaces from page top, above staff)
    string ChordText,        // Display text (e.g., "Cm7", "B♭7")
    int SourcePosition
);

/// <summary>
/// Calculates layout positions for chord name symbols.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/chord-name.cc - ChordName::after_line_breaking
/// LILYPOND-REF: scm/define-grobs.scm - ChordName: font-family=sans, font-size=1.5
/// LILYPOND-REF: ly/engraver-init.ly:571-592 - ChordNames context
///
/// Chord names are positioned above the staff with padding.
/// In LilyPond, ChordNames is a separate context above the staff.
/// </remarks>
public static class ChordNameEngraver
{
    /// <summary>Distance from the associated staff's top line up to the chord-name baseline.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:703-723 - ChordNames context:
    ///   staff-affinity = DOWN, nonstaff-relatedstaff-spacing.padding = 0.5
    /// The ChordNames context has staff-affinity = DOWN, so it is spaced relative to the
    /// staff BELOW it (i.e. it sits just above its associated staff), NOT floated high above.
    /// LilyPond places the chord-name baseline ~0.6 staff-spaces above that staff's top line
    /// (relatedstaff-spacing padding 0.5 plus the glyph's skyline clearance; measured 0.587
    /// against LilyPond 2.24.4 for both solo and top-of-system lead sheets).
    ///
    /// NOTE: an earlier value of 5.5 was the basic-distance of the LYRICS/DYNAMICS contexts
    /// (engraver-init.ly:650/692), mis-attributed to ChordNames. It floated single-staff chords
    /// far too high and, on a lower staff, shoved the chord up into the staff above it.
    ///
    /// Known simplification: like the other annotation engravers, this uses a fixed offset from
    /// the staff's top LINE rather than the staff's full skyline, so notes/ledger lines poking
    /// above the staff are not yet cleared (LilyPond would skyline-space the ChordNames line).
    /// </remarks>
    private const double StaffPadding = 0.6;

    /// <summary>
    /// Calculates chord name layouts from collected items.
    /// </summary>
    public static ImmutableArray<ChordNameLayout> Calculate(
        ImmutableArray<ChordNameItem> chordNames,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Dictionary<int, double>? staffYByIndex = null)
    {
        if (chordNames.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<ChordNameLayout>.Empty;

        var results = ImmutableArray.CreateBuilder<ChordNameLayout>(chordNames.Length);

        foreach (var chord in chordNames)
        {
            if (chord.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[chord.MeasureIndex];

            // Resolve this chord name's OWN staff (multi-staff): its measures (X)
            // and the staff's vertical offset, so it sits above its own staff.
            var cnMeasures = measuresByStaff != null
                && measuresByStaff.TryGetValue(chord.StaffIndex, out var mm) ? mm : measures;
            double staffOffset = staffYByIndex != null
                && staffYByIndex.TryGetValue(chord.StaffIndex, out var so) ? so : 0;

            // Find item X position (Items/Columns-aware)
            double x = ml.X + LayoutUtilities.GetItemXOffset(
                cnMeasures, chord.MeasureIndex, chord.ItemIndex, ml);

            // Y position: above the staff (negative = upward), offset to own staff
            double y = -StaffPadding + staffOffset;

            results.Add(new ChordNameLayout(
                chord.MeasureIndex, x, y, chord.ChordText, chord.SourcePosition));
        }

        return results.ToImmutable();
    }
}
