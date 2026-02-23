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
/// Layout information for a music mark (segno, coda, fine, D.S., D.C., etc.).
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:36-89 Mark_engraver class
/// LILYPOND-REF: define-grobs.scm:3650-3710 RehearsalMark, SegnoMark, CodaMark grobs
/// </remarks>
public readonly record struct MusicMarkLayout(
    int MeasureIndex,       // Measure containing this mark
    double X,               // Absolute X position (staff spaces from score start)
    double Y,               // Y position (staff spaces from staff top, positive = down)
    MusicMarkType MarkType, // Type of mark
    string Text,            // Display text or glyph
    bool IsSymbol,          // True if should use symbol glyph, false for text
    int SourcePosition      // For click-to-source mapping
);

/// <summary>
/// Calculates positions for music marks.
/// Implements LilyPond's mark positioning algorithm with outside-staff-priority stacking.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:46-89 Mark creation
/// LILYPOND-REF: side-position-interface.cc:92-111 axis_aligned_side_helper
/// LILYPOND-REF: axis-group-interface.cc:865-984 skyline_spacing / outside-staff-priority stacking
///
/// LilyPond places marks:
/// - Above staff (direction = UP) for most marks
/// - Below staff (direction = DOWN) for expression marks (rit., accel., etc.)
/// - At beginning of measure for segno/coda
/// - At end of measure for fine/D.S./D.C.
///
/// When multiple marks appear at the same position, they are stacked using
/// outside-staff-priority: lower priority marks are placed closer to the staff,
/// higher priority marks are placed farther away.
/// </remarks>
public static class MusicMarkEngraver
{
    // LILYPOND-REF: define-grobs.scm:3665 padding = 0.5
    private const double Padding = 0.5;

    // Y offset above staff for marks (when no volta brackets present)
    // LILYPOND-REF: define-grobs.scm RehearsalMark padding=0.8
    private const double AboveStaffOffset = -2.0;

    // LILYPOND-REF: axis-group-interface.cc:50 default_outside_staff_padding_ = 0.46
    private const double OutsideStaffPadding = 0.46;

    // Y offset below staff for expression marks
    private const double BelowStaffOffset = 5.5;

    // Gap between stacked marks
    // LILYPOND-REF: axis-group-interface.cc:924 padding between outside-staff elements
    private const double StackGap = 0.3;

    /// <summary>
    /// Calculates layout for all music marks in a score, including section labels.
    /// Section labels from measures are merged with explicit music marks and
    /// stacked using outside-staff-priority when they overlap.
    /// </summary>
    public static ImmutableArray<MusicMarkLayout> Calculate(
        Score? score,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        ImmutableArray<Measure> measures = default,
        ImmutableArray<VoltaBracketLayout> voltaBrackets = default)
    {
        // Merge section labels from measures into the mark list
        var allMarks = MergeSectionLabels(musicMarks, measures);

        if (allMarks.Length == 0)
            return ImmutableArray<MusicMarkLayout>.Empty;

        // Calculate X positions and group marks that need stacking
        var markEntries = new List<(MusicMarkItem Mark, double X)>();
        foreach (var mark in allMarks)
        {
            if (mark.MeasureIndex >= measureLayouts.Length)
                continue;

            var measureLayout = measureLayouts[mark.MeasureIndex];
            double x = CalculateXPosition(mark, measureLayout);
            markEntries.Add((mark, x));
        }

        // LILYPOND-REF: axis-group-interface.cc:865-984 skyline_spacing
        // Group by (MeasureIndex, Position) for collision stacking.
        // Marks at the same measure+position are sorted by outside-staff-priority
        // and stacked outward from the staff.

        // Build volta bracket coverage: measure indices that have a volta bracket above
        // LILYPOND-REF: define-grobs.scm:3943 VoltaBracketSpanner outside-staff-priority=600
        var voltaMeasures = new HashSet<int>();
        double voltaTopY = 0;
        if (!voltaBrackets.IsDefaultOrEmpty)
        {
            foreach (var vb in voltaBrackets)
            {
                for (int mi = vb.StartMeasureIndex; mi <= vb.EndMeasureIndex; mi++)
                    voltaMeasures.Add(mi);
                // Track the highest (most negative) volta Y
                if (vb.Y < voltaTopY)
                    voltaTopY = vb.Y;
            }
        }

        var groups = markEntries
            .GroupBy(e => (e.Mark.MeasureIndex, e.Mark.Position))
            .ToList();

        var layouts = ImmutableArray.CreateBuilder<MusicMarkLayout>();

        foreach (var group in groups)
        {
            // Separate above-staff and below-staff marks
            var aboveMarks = group
                .Where(e => e.Mark.Vertical == MusicMarkVertical.Above)
                .OrderBy(e => GetOutsideStaffPriority(e.Mark.Type))
                .ToList();

            var belowMarks = group
                .Where(e => e.Mark.Vertical == MusicMarkVertical.Below)
                .OrderBy(e => GetOutsideStaffPriority(e.Mark.Type))
                .ToList();

            // Check if any mark in this group overlaps with a volta bracket
            bool hasVoltaOverlap = aboveMarks.Any(e => voltaMeasures.Contains(e.Mark.MeasureIndex));

            // LILYPOND-REF: axis-group-interface.cc:652-681 avoid_outside_staff_collisions
            // Marks with priority > 600 (VoltaBracketSpanner) must be placed above volta.
            // Base Y for above-staff stacking: if volta present, start above volta top.
            double baseAboveY = AboveStaffOffset;
            if (hasVoltaOverlap)
            {
                // Place marks above volta bracket with outside-staff padding
                baseAboveY = voltaTopY - OutsideStaffPadding;
            }

            // Stack above-staff marks (lower priority = closer to staff)
            double stackTopY = baseAboveY;
            for (int i = 0; i < aboveMarks.Count; i++)
            {
                var (mark, x) = aboveMarks[i];
                double halfExtent = GetMarkHalfExtent(mark.Type);

                double y;
                if (i == 0)
                {
                    y = baseAboveY - Padding;
                    stackTopY = y - halfExtent;
                }
                else
                {
                    // Subsequent marks: stack above previous
                    y = stackTopY - StackGap - halfExtent;
                    stackTopY = y - halfExtent;
                }

                layouts.Add(new MusicMarkLayout(
                    mark.MeasureIndex, x, y, mark.Type, mark.Text,
                    mark.IsSymbol, mark.SourcePosition));
            }

            // Stack below-staff marks (lower priority = closer to staff)
            double stackBottomY = BelowStaffOffset;
            for (int i = 0; i < belowMarks.Count; i++)
            {
                var (mark, x) = belowMarks[i];
                double halfExtent = GetMarkHalfExtent(mark.Type);

                double y;
                if (i == 0)
                {
                    y = BelowStaffOffset + Padding;
                    stackBottomY = y + halfExtent;
                }
                else
                {
                    y = stackBottomY + StackGap + halfExtent;
                    stackBottomY = y + halfExtent;
                }

                layouts.Add(new MusicMarkLayout(
                    mark.MeasureIndex, x, y, mark.Type, mark.Text,
                    mark.IsSymbol, mark.SourcePosition));
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Merges section labels from measures into the music marks list.
    /// Section labels become MusicMarkType.SectionLabel entries.
    /// </summary>
    private static ImmutableArray<MusicMarkItem> MergeSectionLabels(
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<Measure> measures)
    {
        if (measures.IsDefaultOrEmpty)
            return musicMarks.IsDefaultOrEmpty ? ImmutableArray<MusicMarkItem>.Empty : musicMarks;

        // Collect section labels from measures
        var sectionLabels = new List<MusicMarkItem>();
        for (int i = 0; i < measures.Length; i++)
        {
            var measure = measures[i];
            if (measure.SectionLabel != null)
            {
                sectionLabels.Add(new MusicMarkItem(
                    MusicMarkType.SectionLabel, measure.SectionLabel, i, measure.SourceStart));
            }
        }

        if (sectionLabels.Count == 0)
            return musicMarks.IsDefaultOrEmpty ? ImmutableArray<MusicMarkItem>.Empty : musicMarks;

        // Merge: existing marks + section labels
        var builder = ImmutableArray.CreateBuilder<MusicMarkItem>();
        if (!musicMarks.IsDefaultOrEmpty)
            builder.AddRange(musicMarks);
        builder.AddRange(sectionLabels);
        return builder.ToImmutable();
    }

    /// <summary>
    /// Gets the outside-staff-priority for a mark type.
    /// Lower values are placed closer to the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm
    /// - SectionLabel: outside-staff-priority = 1450
    /// - RehearsalMark: outside-staff-priority = 1500
    /// - SegnoMark/CodaMark: outside-staff-priority = 1500
    /// </remarks>
    private static int GetOutsideStaffPriority(MusicMarkType type) => type switch
    {
        MusicMarkType.SectionLabel => 1450,
        MusicMarkType.Rehearsal => 1500,
        MusicMarkType.Segno => 1500,
        MusicMarkType.Coda => 1500,
        _ => 1500
    };

    /// <summary>
    /// Gets the approximate half-height of a mark's visual extent in staff spaces.
    /// Used for collision avoidance stacking between marks.
    /// </summary>
    /// <remarks>
    /// These values match the rendering sizes in SvgRenderer:
    /// - Boxed marks (Rehearsal/SectionLabel): (fontSize + boxPadding*2) / 2
    ///   where boxPadding = 0.2 (LILYPOND-REF: define-markup-commands.scm)
    /// - Symbol marks (Segno/Coda): symbol glyph height / 2
    /// - Text marks (D.S./Fine/etc.): fontSize / 2
    /// </remarks>
    private static double GetMarkHalfExtent(MusicMarkType type) => type switch
    {
        MusicMarkType.Rehearsal => 1.4,       // (FontSize*0.6 + 0.2*2) / 2 = (2.4+0.4)/2
        MusicMarkType.SectionLabel => 1.3,    // (FontSize*0.55 + 0.2*2) / 2 = (2.2+0.4)/2
        MusicMarkType.Segno or MusicMarkType.Coda => 2.0,
        _ => 1.0
    };

    /// <summary>
    /// Calculates X position for a mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mark-engraver.cc:75-80 break-align-symbol
    /// - Beginning marks (segno, coda): align to start of measure
    /// - End marks (fine, D.S., D.C.): align to end of measure
    /// </remarks>
    private static double CalculateXPosition(MusicMarkItem mark, MeasureLayout measureLayout)
    {
        return mark.Position switch
        {
            MusicMarkPosition.Beginning => measureLayout.X + 0.5, // Small offset from barline
            MusicMarkPosition.End => measureLayout.X + measureLayout.Width - 0.5, // Before end barline
            _ => measureLayout.X + measureLayout.Width / 2 // Center (fallback)
        };
    }
}
