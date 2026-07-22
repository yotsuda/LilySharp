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
    /// Distance DOWN from the paper's top edge to the FIRST system's staff refpoint —
    /// its top staff's MIDDLE line, which is the anchor <c>top-system-spacing</c> is
    /// written against.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:441-444 — the problem opens with
    /// <c>bottom_skyline_</c> AT the top of the printable area, set header_height_ below
    /// it, so the top spring is anchored at the TOP of the header (:471-473 says so in
    /// as many words). The header therefore enters the FLOOR, not the anchor.
    /// LILYPOND-REF: lily/page-layout-problem.cc:625-633 — that floor is the ink the
    /// system carries above its refpoint plus the spec's padding, and it reaches the
    /// spring through <c>Spring::ensure_min_distance</c> (lily/spring.cc:156-159), which
    /// raises the MINIMUM and leaves the ideal alone.
    ///
    /// Two frames meet here and must not be confused (they differ by exactly halfStaff):
    /// <list type="bullet">
    /// <item>Lily#'s system origin and <paramref name="systemUpExtent"/> are the top
    /// staff's TOP LINE and the ink above it.</item>
    /// <item>LilyPond's <c>up_skyline.distance()</c> is measured from the staff REFPOINT
    /// and always contains the staff symbol itself.</item>
    /// </list>
    /// Placing the first system is done in the refpoint frame here and converted back to
    /// the origin frame ONCE, in <see cref="CalculateFirstSystemY"/>.
    /// </remarks>
    public static double CalculateFirstStaffRefpoint(
        double topMargin, double headerHeight, double systemUpExtent,
        double halfStaff, VerticalSpacingSpec topSpec)
        // LILYPOND-REF: lily/simple-spacer.cc:295-305 spring_positions — a system's
        // position is the running sum of its springs' lengths, and for the FIRST system
        // that sum is the top spring alone. At force 0 Spring::length is
        // max(min_distance_, ideal_distance_) (spring.cc:219-237), which is why a system
        // whose ink is smaller than top-system-spacing's basic-distance is not measured by
        // its ink at all.
        => topMargin + CreateTopSystemSpring(headerHeight, systemUpExtent, halfStaff, topSpec)
                       .Length(0);

    /// <summary>
    /// The spring from the top of the printable area down to the first system's staff
    /// refpoint — <c>top-system-spacing</c>, floored by the ink that system carries above
    /// its refpoint.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:511-518 — the first system's spring comes
    /// from top_system_spacing, and :625-633 floors it with the system's own ink through
    /// <c>Spring::ensure_min_distance</c>.
    /// The header is part of that FLOOR, not of the anchor: the problem is built with
    /// <c>bottom_skyline_</c> set header_height_ below the top of the printable area
    /// (:441-444), which is what the comment at :471-473 means by anchoring the spring at
    /// the top of the header.
    /// </remarks>
    public static Spring CreateTopSystemSpring(
        double headerHeight, double systemUpExtent, double halfStaff, VerticalSpacingSpec topSpec)
    {
        // Lily#'s up extent is the ink above the top staff LINE; LilyPond's up_skyline is
        // measured from the staff REFPOINT and always contains the staff symbol itself, so
        // the same quantity is halfStaff more there.
        double inkAboveRefpoint = systemUpExtent + halfStaff;
        return CreateSpring(topSpec, headerHeight + inkAboveRefpoint + topSpec.Padding);
    }

    /// <summary>
    /// Builds one of LilyPond's vertical springs from a spacing spec and the minimum
    /// distance the geometry imposes on it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1345-1358 alter_spring_from_spacing_spec —
    /// basic-distance is the IDEAL and minimum-distance the MIN, then
    /// <c>set_default_strength()</c> makes the inverse stretch equal the ideal
    /// (spring.cc:213-216) and only a <c>stretchability</c> entry overrides it. Lily#'s
    /// spec cannot say "absent", so a Stretchability of 0 is read as LilyPond's absent —
    /// which is what top-system-spacing actually is in ly/paper-defaults-init.ly:78-80.
    ///
    /// The compress strength is fixed at <c>ideal - minimum-distance</c> from the SPEC
    /// (spring.cc:205-210), because <c>ensure_min_distance</c> raises the minimum
    /// afterwards and deliberately does not restrengthen the spring (spring.cc:156-159).
    /// Passing the raised minimum here instead would quietly change every blocking force.
    /// </remarks>
    public static Spring CreateSpring(VerticalSpacingSpec spec, double ensureMinDistance)
    {
        double inverseStretch = spec.Stretchability > 0 ? spec.Stretchability : spec.BasicDistance;
        double inverseCompress = Math.Max(0, spec.BasicDistance - spec.MinimumDistance);
        double minDistance = Math.Max(spec.MinimumDistance, ensureMinDistance);
        return new Spring(spec.BasicDistance, minDistance, inverseStretch, inverseCompress);
    }

    /// <summary>
    /// Distance DOWN from the paper's top edge to the FIRST system's ORIGIN (its top
    /// staff's TOP LINE) — the frame <see cref="SystemLayout.Y"/> is stacked in.
    /// </summary>
    /// <remarks>
    /// The sole seam between the refpoint frame LilyPond's page spacing is written in and
    /// the origin frame Lily# stacks systems in; every caller placing a first system goes
    /// through here so the halfStaff conversion exists in exactly one place.
    /// </remarks>
    public static double CalculateFirstSystemY(
        double topMargin, double headerHeight, double systemUpExtent,
        double halfStaff, VerticalSpacingSpec topSpec)
        => CalculateFirstStaffRefpoint(topMargin, headerHeight, systemUpExtent, halfStaff, topSpec)
           - halfStaff;

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
    public static double StaffOffsetInSystemDown(SystemLayout system, int staffIndex)
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
    /// A staff's WITHIN-SYSTEM vertical offset in LilyPond's frame: staff-spaces
    /// <b>UP</b> from the system top to this staff's top line, so it is NEGATIVE for
    /// every staff below the first and 0 for the first. Exactly
    /// <c>-<see cref="StaffOffsetInSystemDown"/></c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:896-901 — a system's staves are placed
    /// from <c>min_offsets</c>, which <c>Align_interface::get_minimum_translations</c>
    /// produces Y-up (negative going down); :915-917 calls the sign out explicitly
    /// ("this is relative to the system: negative numbers are down").
    ///
    /// This is the boundary shim for the frame move recorded in COORDINATE_AUDIT 2.1.
    /// Callers migrate one island at a time — each migration is arithmetically the
    /// identity (every <c>- offsetDown</c> becomes <c>+ offsetUp</c>), so output must
    /// stay byte-identical and any snapshot that moves is a sign error, not progress.
    ///
    /// ⚠️ <see cref="StaffOffsetInSystemDown"/> does NOT "go away when the last caller is
    /// gone", which an earlier version of this remark predicted. Surveyed 2026-07-22: of
    /// its callers, only the two Y-up skyline passes in <c>LayoutEngine</c> were
    /// migrations; the rest are the boundaries of computations that are DELIBERATELY
    /// device (the tab/arc geometry behind <c>TabStaffGeometry</c>, the slur and
    /// tie-variant scorers, the paging extent pass, <c>SkylineDrop</c>'s floor, and the
    /// stored device Y of ledger spans and multi-measure rests). A device island needs a
    /// reflection at its edge, and this accessor IS that reflection — rewriting those
    /// callers as <c>-StaffOffsetInSystemUp(...)</c> would move the negation inward and
    /// read worse. Down survives on purpose; what remains of the frame work is the
    /// storage flip of <c>StaffLayout.Y</c> itself (see docs/HANDOFF.md 3B).
    /// </remarks>
    public static double StaffOffsetInSystemUp(SystemLayout system, int staffIndex)
        => -StaffOffsetInSystemDown(system, staffIndex);

    /// <summary>
    /// Finds the absolute page-Y-up position of a staff's TOP line within a
    /// specific system (staff-spaces UP from the page bottom). Returns system.Y —
    /// the system top's Y-up — if no matching staff is found (single-staff
    /// fallback). Since <see cref="SystemLayout.Y"/> now stores page Y-up
    /// natively (Stage-4 W2-core) and the staff top sits its within-system
    /// downward offset BELOW the system top, that offset SUBTRACTS.
    /// </summary>
    public static double FindStaffYInSystem(SystemLayout system, int staffIndex)
        => system.Y - StaffOffsetInSystemDown(system, staffIndex);

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
    /// this returns it directly (kept as a named alias at the render seam).
    /// </summary>
    public static double SystemTopYUp(SystemLayout system)
        => system.Y;

    /// <summary>
    /// Page Y-up of a staff's top line within a system. Now that
    /// <see cref="SystemLayout.Y"/> stores page Y-up natively (Stage-4 W2-core),
    /// this is identical to <see cref="FindStaffYInSystem"/> (kept as a named
    /// alias at the render seam).
    /// </summary>
    public static double StaffTopYUp(SystemLayout system, int staffIndex)
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
