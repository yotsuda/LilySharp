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
    /// <summary>Distance from staff center to ChordNames context reference point.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:588 - nonstaff-relatedstaff-spacing.padding = 0.5
    /// LILYPOND-REF: scm/define-grobs.scm - VerticalAxisGroup nonstaff-relatedstaff-spacing
    ///   basic-distance = 5.5 (center-to-center from staff)
    /// ChordNames is a separate non-staff context above the staff; positioned using
    /// basic-distance from the staff center (StaffHeight/2 = 2.0).
    /// Y offset from staff top = StaffHeight/2 + basic-distance = 2.0 + 5.5 = 7.5
    /// but adjusted for text being positioned at baseline, not center.
    /// </remarks>
    private const double StaffPadding = 5.5;

    /// <summary>
    /// Calculates chord name layouts from collected items.
    /// </summary>
    public static ImmutableArray<ChordNameLayout> Calculate(
        ImmutableArray<ChordNameItem> chordNames,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default)
    {
        if (chordNames.IsDefaultOrEmpty || systems.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return ImmutableArray<ChordNameLayout>.Empty;

        var results = ImmutableArray.CreateBuilder<ChordNameLayout>(chordNames.Length);

        foreach (var chord in chordNames)
        {
            if (chord.MeasureIndex >= measureLayouts.Length)
                continue;

            var ml = measureLayouts[chord.MeasureIndex];

            // Find item X position (Items/Columns-aware)
            double x = ml.X + LayoutUtilities.GetItemXOffset(
                measures, chord.MeasureIndex, chord.ItemIndex, ml);

            // Y position: above the staff (negative = upward)
            double y = -StaffPadding;

            results.Add(new ChordNameLayout(
                chord.MeasureIndex, x, y, chord.ChordText, chord.SourcePosition));
        }

        return results.ToImmutable();
    }
}
