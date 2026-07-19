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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Common utility methods for layout calculations.
/// </summary>
internal static class LayoutUtilities
{
    /// <summary>
    /// Gets note value (1=whole, 2=half, 4=quarter, 8=eighth) from duration fraction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:600 Stem::duration_log
    /// Duration log: 0=whole, 1=half, 2=quarter, 3=eighth, etc.
    /// Note value: 1=whole, 2=half, 4=quarter, 8=eighth, etc.
    /// </remarks>
    public static int GetNoteValueFromFraction(Fraction duration)
    {
        // duration = 1/1 for whole, 1/2 for half, 1/4 for quarter, 1/8 for eighth, etc.
        if (duration.Numerator == 0) return 4; // Default to quarter
        return (int)(duration.Denominator / duration.Numerator);
    }

    /// <summary>
    /// Calculates the flag height based on note value.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/flag.cc:80-95 Flag::internal_print
    /// Flag height increases with shorter note values (more beams/flags).
    /// </remarks>
    public static double CalculateFlagHeight(int noteValue)
    {
        double height = EngravingDefaults.FlagBaseHeight;
        if (noteValue >= 16) height += EngravingDefaults.FlagHeightIncrement;
        if (noteValue >= 32) height += EngravingDefaults.FlagHeightIncrement;
        return height;
    }

    /// <summary>
    /// The measures a per-staff annotation is positioned against: the annotation's own
    /// staff measures when a multi-staff map is supplied and contains the staff, else
    /// the fallback (the single- or primary-voice measures). Shared by the annotation
    /// engravers, which all repeated this ternary.
    /// </summary>
    public static ImmutableArray<Measure> ResolveStaffMeasures(
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff, int staffIndex,
        ImmutableArray<Measure> fallback)
        => measuresByStaff != null && measuresByStaff.TryGetValue(staffIndex, out var mm)
            ? mm : fallback;

    /// <summary>
    /// Builds a map from measure index to (system, measureLayout) for quick lookup.
    /// </summary>
    public static Dictionary<int, (SystemLayout System, MeasureLayout Measure)> BuildMeasureMap(
        ImmutableArray<SystemLayout> systems)
    {
        var map = new Dictionary<int, (SystemLayout, MeasureLayout)>();
        foreach (var system in systems)
        {
            foreach (var measureLayout in system.Measures)
            {
                map[measureLayout.MeasureIndex] = (system, measureLayout);
            }
        }
        return map;
    }

    /// <summary>
    /// Builds a map from measure index to measureLayout for quick lookup.
    /// </summary>
    public static Dictionary<int, MeasureLayout> BuildMeasureLayoutMap(
        ImmutableArray<SystemLayout> systems)
    {
        var map = new Dictionary<int, MeasureLayout>();
        foreach (var system in systems)
        {
            foreach (var measureLayout in system.Measures)
            {
                map[measureLayout.MeasureIndex] = measureLayout;
            }
        }
        return map;
    }

    /// <summary>
    /// Calculates the upward extent of a system skyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:622-626
    /// MaxHeight() returns the topmost Y-up (positive for notes above the staff top).
    /// It is already the positive extent above the staff top.
    /// </remarks>
    public static double CalculateUpExtent(VerticalSkyline upSkyline)
    {
        return upSkyline.IsEmpty ? 0 : Math.Max(0, upSkyline.MaxHeight());
    }

    /// <summary>
    /// Calculates the downward extent of a system skyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:667-680 Skyline::max_height()
    /// DOWN skyline's MaxHeight() returns the bottommost Y-up (negative below the
    /// staff top). The staff bottom line sits at Y-up = -staffHeight, so the extent
    /// below it is (-MaxHeight) - staffHeight.
    /// </remarks>
    public static double CalculateDownExtent(VerticalSkyline downSkyline, double staffHeight)
    {
        return downSkyline.IsEmpty ? 0 : Math.Max(0, -downSkyline.MaxHeight() - staffHeight);
    }

    /// <summary>
    /// Calculates the initial Y position for the first system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:477-478, 984-985
    /// The staff Y is positioned to leave room for: header + system extent + padding.
    /// </remarks>
    public static double CalculateFirstSystemY(double headerBottom, double systemUpExtent, double topSystemPadding)
    {
        return headerBottom + systemUpExtent + topSystemPadding;
    }

    /// <summary>
    /// Calculates the actual header height based on title and composer presence.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:435
    /// header_height_ = head ? head->extent(Y_AXIS).length() : 0;
    ///
    /// SVG text coordinates specify the baseline, which is approximately
    /// the bottom of the text (excluding descenders). Therefore:
    /// - Title at y=MarginTop has its bottom at MarginTop
    /// - Composer follows with spacing from title baseline
    /// - headerBottom = MarginTop + (vertical extent of all header elements)
    /// </remarks>
    // Mirror of SharedRenderer.DrawHeader: the title BASELINE sits at
    // MarginTop (fs 3.49) and the composer baseline TitleFontSize below it
    // (fs 2.2). Header HEIGHT is the ink below MarginTop — the old model
    // pretended the title had no descender (and used a stale 3.0 for the
    // composer step), so a first system with no tall content of its own
    // (a lyrics/chords ROW score) started inside the title's descender ink.
    private const double HeaderTitleFontSize = 3.49;
    private const double HeaderComposerFontSize = 2.2;
    private const double DescentEm = 0.22; // serif descender depth per em

    public static double CalculateHeaderHeight(string? title, string? composer)
    {
        if (title != null && composer != null)
            return HeaderTitleFontSize + HeaderComposerFontSize * DescentEm;
        if (title != null)
            return HeaderTitleFontSize * DescentEm;
        if (composer != null)
            return HeaderComposerFontSize * DescentEm;
        return 0;
    }

    /// <summary>
    /// A staff's WITHIN-SYSTEM vertical offset: the downward distance
    /// (staff-spaces) from the system top to this staff's top line, i.e.
    /// <c>staff.Y</c>. Returns 0 when the staff is not found (single-staff
    /// fallback), so this is exactly <see cref="FindStaffYInSystem"/> minus
    /// <see cref="SystemLayout.Y"/>.
    /// </summary>
    /// <remarks>
    /// This is the frame-INVARIANT part of the staff's vertical position: it is
    /// the offset within the system, independent of where paging places the
    /// system. Engravers that lay an element out relative to its own staff
    /// (ties, slurs, ledger spans, multi-measure rests, outside-staff stacking,
    /// figured bass) resolve against THIS rather than the absolute
    /// <see cref="FindStaffYInSystem"/>, so they stay decoupled from
    /// <see cref="SystemLayout.Y"/> across the Stage-4 W2 origin flip (which
    /// changes <c>system.Y</c> from device Y-down to page Y-up but leaves
    /// <c>staff.Y</c> a downward within-system offset either way).
    /// </remarks>
    public static double StaffOffsetInSystem(SystemLayout system, int staffIndex)
    {
        if (!system.StaffGroups.IsDefaultOrEmpty && staffIndex >= 0)
        {
            foreach (var staffGroup in system.StaffGroups)
            {
                foreach (var staff in staffGroup.Staves)
                {
                    if (staff.StaffIndex == staffIndex)
                        return staff.Y;
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Finds the absolute page-Y-up position of a staff's TOP line within a
    /// specific system (staff-spaces UP from the page bottom). Returns system.Y —
    /// the system top's Y-up — if no matching staff is found (single-staff
    /// fallback). Since <see cref="SystemLayout.Y"/> now stores page Y-up
    /// natively (Stage-4 W2-core) and the staff top sits its within-system
    /// downward offset BELOW the system top, that offset SUBTRACTS.
    /// </summary>
    public static double FindStaffYInSystem(SystemLayout system, int staffIndex)
        => system.Y - StaffOffsetInSystem(system, staffIndex);

    /// <summary>
    /// Absolute page-Y-up of a staff's middle line, the anchor that staff-position
    /// reflections (device = middle − pos/2) measure staff positions from.
    /// Equivalent to <see cref="FindStaffYInSystem"/> LESS half the staff height
    /// (the middle sits half a staff below the top, so in the Y-up frame it
    /// subtracts). Engravers that route an element to its own staff (ties, slurs,
    /// glissandi, multi-measure rests, ledger-line spanners) share this resolution.
    /// </summary>
    public static double ResolveStaffMiddleY(SystemLayout system, int staffIndex, double staffHeight)
        => FindStaffYInSystem(system, staffIndex) - staffHeight / 2.0;

    /// <summary>
    /// Page Y-up of a system's top line — staff-spaces measured UP from the page
    /// bottom. The renderer emits page-Y-up primitives (the single device flip is
    /// the <see cref="Rendering.YFlipDrawingContext"/>), so this is the origin a
    /// system-anchored draw adds its relative Y-up to. Since <see
    /// cref="SystemLayout.Y"/> now stores page Y-up natively (Stage-4 W2-core),
    /// this returns it directly; the <paramref name="pageHeight"/> parameter is
    /// retained for call-site compatibility (it is no longer needed).
    /// </summary>
    public static double SystemTopYUp(SystemLayout system, double pageHeight)
        => system.Y;

    /// <summary>
    /// Page Y-up of a staff's top line within a system. Now that
    /// <see cref="SystemLayout.Y"/> stores page Y-up natively (Stage-4 W2-core),
    /// this is identical to <see cref="FindStaffYInSystem"/>; the
    /// <paramref name="pageHeight"/> parameter is retained for call-site
    /// compatibility (it is no longer needed).
    /// </summary>
    public static double StaffTopYUp(SystemLayout system, int staffIndex, double pageHeight)
        => FindStaffYInSystem(system, staffIndex);

    /// <summary>
    /// Resolves an item's X offset within a measure layout. Single-staff
    /// layouts index <see cref="MeasureLayout.Items"/> directly; multi-staff
    /// layouts use timing-aligned COLUMNS, so the item's timing is computed
    /// from the voice's measures and matched to a column. Engravers that
    /// index Items directly silently shift on the multi-staff path — always
    /// go through this helper.
    /// </summary>
    public static double GetItemXOffset(
        ImmutableArray<Measure> measures, int measureIndex, int itemIndex, MeasureLayout measureLayout)
    {
        // Single-staff path: MeasureLayout.Items aligns with this voice.
        if (measureLayout.Columns.IsDefaultOrEmpty)
        {
            if (itemIndex < measureLayout.Items.Length)
                return measureLayout.Items[itemIndex].X;
            return 0;
        }

        // Multi-staff path: timing → column lookup.
        if (measures.IsDefault || measureIndex < 0 || measureIndex >= measures.Length)
            return 0;
        var measure = measures[measureIndex];
        var timing = Fraction.Zero;
        for (int i = 0; i < itemIndex && i < measure.Items.Length; i++)
            timing = timing + measure.Items[i].Duration;

        return NearestColumnX(measureLayout.Columns, timing);
    }

    /// <summary>
    /// X of the column whose timing matches <paramref name="timing"/> exactly, else the
    /// nearest column by absolute timing distance (0 when there are no columns). This is
    /// the snap-to-onset resolution shared by <see cref="GetItemXOffset"/> and
    /// <see cref="MeasureLayouter.LayoutItemsFromColumns"/>. It is DISTINCT from
    /// <see cref="MeasureLayout.GetXForTiming"/>, which interpolates between the
    /// bracketing columns for a timing that falls BETWEEN onsets — do not fold the two
    /// together. For an exact item onset (the only timings this helper is fed) both agree.
    /// </summary>
    internal static double NearestColumnX(ImmutableArray<ColumnLayout> columns, Fraction timing)
    {
        if (columns.IsDefaultOrEmpty)
            return 0;

        double targetT = timing.ToDouble();
        double bestX = 0;
        double bestDiff = double.MaxValue;
        foreach (var col in columns)
        {
            if (col.Timing == timing)
                return col.X;
            double diff = Math.Abs(col.Timing.ToDouble() - targetT);
            if (diff < bestDiff) { bestX = col.X; bestDiff = diff; }
        }
        return bestX;
    }
}
