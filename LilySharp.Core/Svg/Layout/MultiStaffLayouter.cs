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
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Handles layout calculations specific to multi-staff scores.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/system.cc
/// LILYPOND-REF: lily/staff-spacing.cc
/// LILYPOND-REF: lily/align-interface.cc — internal_get_minimum_translations()
/// LILYPOND-REF: lily/staff-grouper-interface.cc — staff-staff-spacing, staffgroup-staff-spacing
///
/// Skyline-based staff spacing (align-interface.cc:217-268):
///   when measure layouts are provided, uses skyline distance between staves for collision avoidance;
///   falls back to fixed formula (BasicDistance - staffHeight) when skylines are unavailable
/// IMPLEMENTED — pure height estimation (axis-group-interface.cc:138-173) via CalculatePureSystemHeight
/// IMPLEMENTED — staff-affinity for non-spaceable staves (align-interface.cc:240-252) via StaffAffinity.Select
/// IMPLEMENTED — hara-kiri auto-hide empty staves (hara-kiri-group-spanner.cc)
/// IMPLEMENTED — alignment-distances manual override (StaffSpacingParameters.ApplyOverrides)
/// Outside-staff-priority stacking: implemented in OutsideStaffStacker.cs (axis-group-interface.cc:359-474)
/// IMPLEMENTED — brace collapse-height (system-start-delimiter.cc:127-129)
/// IMPLEMENTED — ChoirStaff/bracket delimiter variants (system-start-delimiter.cc)
/// </remarks>
internal sealed class MultiStaffLayouter
{
    private readonly LayoutOptions _options;
    private readonly MeasureLayouter _measureLayouter;

    /// <summary>Clef→first-note gap (staff spaces) on an ALL-TAB line. The compact "TAB"
    /// clef does not warrant the wide 5.0 space-alist gap an engraved clef reserves
    /// (BreakAlignSpacing.FirstNoteSpring), so the notes begin close to it — a Lily#
    /// choice, since a tab line carries no key/time between the clef and the first
    /// fret.</summary>
    private const double TabClefToFirstNoteSpace = 1.5;

    /// <summary>
    /// Current indent for the system being laid out (in staff spaces).
    /// Set by LayoutEngine before each system's layout calls.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly — indent / short-indent
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset uses indent
    /// </remarks>
    internal double CurrentIndent { get; set; }

    public MultiStaffLayouter(LayoutOptions options, MeasureLayouter measureLayouter)
    {
        _options = options;
        _measureLayouter = measureLayouter;
    }

    /// <summary>
    /// Calculates the total height of a multi-staff system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
    /// Uses StaffSpacingParameters for intra-group and inter-group spacing.
    /// </remarks>
    public double CalculateSystemHeight(MultiStaffScore score)
    {
        double height = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                // Sum each staff's ACTUAL height plus one intra-group gap per pair. A
                // grand staff is usually two normal staves (piano/harp), but may hold
                // three-plus (organ) or an ossia/tab staff; the old `staffHeight * 2`
                // hardcoded exactly two normal staves and under-counted anything else.
                // For the common two-normal-staff case this still equals
                // `staffHeight*2 + (BasicDistance - staffHeight)`, and it mirrors LP's
                // align-interface (sum of real staff extents + inter-staff spacing).
                // LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
                foreach (var staff in group.Staves)
                    height += GetStaffHeight(staff);
                if (group.StaffCount > 1)
                    height += (sp.StaffStaff.BasicDistance - staffHeight) * (group.StaffCount - 1);
            }
            else
            {
                foreach (var staff in group.Staves)
                {
                    height += GetStaffHeight(staff);
                }
                if (group.StaffCount > 1)
                {
                    // Intra-group spacing for each pair
                    double intraSpacing = sp.StaffStaff.BasicDistance - staffHeight;
                    height += Math.Max(0, intraSpacing) * (group.StaffCount - 1);
                }
            }

            if (i < score.StaffGroups.Length - 1)
            {
                // LILYPOND-REF: lily/align-interface.cc:240-252 — direction-aware staff-affinity spec selection.
                var nextGroup = score.StaffGroups[i + 1];
                var spec = SelectInterGroupSpec(group, nextGroup, sp);

                bool nextIsOssia = nextGroup.Staves.Any(s => s.IsOssia);
                bool currentIsOssia = group.Staves.Any(s => s.IsOssia);
                // An ossia joins the SAME vertical alignment as the staves in LP
                // (vertical-align-engraver.cc inserts its grob among them), so
                // the ossia/staff pair gets ordinary staff-staff-spacing — not
                // the wider between-groups spacing — scaled with the ossia.
                if (nextIsOssia || currentIsOssia)
                    spec = sp.StaffStaff;
                bool textRowPair = group.Staves[^1].IsTextRow && nextGroup.Staves[0].IsTextRow;
                double interGroupGap = textRowPair
                    ? TextRowPairGap
                    : spec.BasicDistance - staffHeight;
                if (nextIsOssia || currentIsOssia)
                    interGroupGap *= OssiaScaleFactor;
                height += interGroupGap;
            }
        }

        return height;
    }

    /// <summary>
    /// Picks the right vertical-spacing spec for the gap between two adjacent staff groups,
    /// honouring the boundary staves' <c>staff-affinity</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:240-252 — direction-aware non-staff spacing.
    /// The boundary is between the LAST staff of <paramref name="upper"/> and the FIRST
    /// staff of <paramref name="lower"/>. When both are spaceable, falls back to
    /// staffgroup-staff-spacing.
    /// </remarks>
    private static VerticalSpacingSpec SelectInterGroupSpec(
        StaffGroup upper, StaffGroup lower, StaffSpacingParameters sp)
    {
        if (upper.Staves.IsDefaultOrEmpty || lower.Staves.IsDefaultOrEmpty)
            return sp.StaffGroupStaff;

        int? upperAffinity = upper.Staves[^1].StaffAffinity;
        int? lowerAffinity = lower.Staves[0].StaffAffinity;
        return StaffAffinity.Select(upperAffinity, lowerAffinity, sp.StaffGroupStaff, sp);
    }

    /// <summary>
    /// Estimates pure system height including content-dependent loose line extents.
    /// Used for page breaking optimization before full layout.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:138-173 pure_height
    ///
    /// Pure height = base staff spacing + estimated loose line heights (lyrics, dynamics, etc.)
    /// This allows the page breaker to account for variable system heights without
    /// requiring full layout. The base height comes from CalculateSystemHeight;
    /// the loose line extents are provided by the caller (from LayoutEngine.EstimateLooseLineExtents).
    /// </remarks>
    public double CalculatePureSystemHeight(MultiStaffScore score, double looseDownExtent, double looseUpExtent)
    {
        double baseHeight = CalculateSystemHeight(score);
        return baseHeight + looseDownExtent + looseUpExtent;
    }

    /// <summary>
    /// Scale factor for ossia staves: magstep(-3) = 2^(-3/6) ≈ 0.707.
    /// LILYPOND-REF: ly/engraver-init.ly — ossia staves typically use fontSize = #-3
    /// with \override StaffSymbol.staff-space = #(magstep -3).
    /// Shared with the renderer via EngravingDefaults so reserved heights
    /// match the drawn size exactly.
    /// </summary>
    private const double OssiaScaleFactor = EngravingDefaults.OssiaScale;

    /// <summary>
    /// Gets the height of a staff in staff spaces.
    /// Standard staves have 4 staff spaces (5 lines).
    /// Tab staves have (stringCount - 1) staff spaces.
    /// Ossia staves are scaled down.
    /// </summary>
    private double GetStaffHeight(Staff staff)
    {
        if (staff.IsTab && staff.Tuning.HasValue)
        {
            int stringCount = Tunings.GetStringCount(staff.Tuning.Value);
            // Tab lines are spaced wider than a normal staff (TabStringSpace).
            return (stringCount - 1) * EngravingDefaults.TabStringSpace(stringCount); // Bass: 3 → 4.5
        }
        if (staff.IsTextRow)
            // A LYRIC row is "a staff with the lines removed": a full
            // staff-height band (plus one verse-spacing per extra verse), so
            // its barlines, spacing and neighbours behave exactly as around a
            // real staff. A chord row keeps the compact band its symbols
            // hang on.
            return staff.IsLyricsTextRow
                ? _options.StaffHeight + (staff.TextRowVerses - 1) * TextRowVerseSpacing
                : TextRowHeight;
        if (staff.IsOssia)
            return _options.StaffHeight * OssiaScaleFactor;
        return _options.StaffHeight;
    }

    /// <summary>Reserved vertical band (staff spaces) for an independent text row
    /// (chords / lyrics): a line of text (~1.5 ss tall) plus a little breathing room.</summary>
    private const double TextRowHeight = 2.5;

    /// <summary>Extra band height per additional lyrics verse. MUST match
    /// LyricEngraver's VerseSpacing, or verse 2+ leak out of the reserved
    /// band (they did, at the stale 1.8).</summary>
    private const double TextRowVerseSpacing = 3.2;

    /// <summary>Gap between two adjacent TEXT rows (chord row above a lyric
    /// row): the chord symbols sit just above the lyric band, like chord
    /// names above a staff — not a whole staff-distance away.</summary>
    private const double TextRowPairGap = 0.6;

    /// <summary>How far left of the staff a system-start BRACE sits.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm SystemStartBrace (padding . 0.3)</remarks>
    private const double SystemStartBracePadding = 0.3;

    /// <summary>How far left of the staff a system-start BRACKET sits.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm SystemStartBracket (padding . 0.8)</remarks>
    private const double SystemStartBracketPadding = 0.8;

    /// <summary>
    /// Layouts all staff groups within a system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
    /// Uses StaffSpacingParameters for intra-group and inter-group spacing.
    /// </remarks>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(MultiStaffScore score, double systemY)
    {
        var builder = ImmutableArray.CreateBuilder<StaffGroupLayout>();
        double currentY = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;
        int globalStaffIndex = 0;

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                var layout = LayoutGrandStaffGroup(group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height;
            }
            else if (group.HasDelimiter)
            {
                var layout = LayoutBracketGroup(group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height;
            }
            else
            {
                var layout = LayoutSingleStaffGroup(group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY += layout.Height;
            }

            if (i < score.StaffGroups.Length - 1)
            {
                // LILYPOND-REF: lily/align-interface.cc:240-252 — staff-affinity-aware spec selection.
                var nextGroup = score.StaffGroups[i + 1];
                var spec = SelectInterGroupSpec(group, nextGroup, sp);
                bool nextIsOssia = nextGroup.Staves.Any(s => s.IsOssia);
                bool currentIsOssia = group.Staves.Any(s => s.IsOssia);
                // Ossia/staff pairs share one alignment in LP → ordinary
                // staff-staff-spacing, scaled (see CalculateSystemHeight).
                if (nextIsOssia || currentIsOssia)
                    spec = sp.StaffStaff;
                bool textRowPair = group.Staves[^1].IsTextRow && nextGroup.Staves[0].IsTextRow;
                double interGroupGap = textRowPair
                    ? TextRowPairGap
                    : spec.BasicDistance - staffHeight;
                if (nextIsOssia || currentIsOssia)
                    interGroupGap *= OssiaScaleFactor;
                currentY += interGroupGap;
            }

            globalStaffIndex += group.StaffCount;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Layouts all staff groups with hara-kiri support (empty staff auto-hiding).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hara-kiri-group-spanner.cc — consider_suicide()
    /// LILYPOND-REF: lily/align-interface.cc internal_get_minimum_translations()
    ///
    /// Checks each staff's content in the given measure range. Staves with
    /// RemoveEmpty=true that contain no notes/chords are hidden (Height=0, IsHidden=true).
    /// Hidden staves contribute no height or inter-group spacing.
    /// </remarks>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(
        MultiStaffScore score, double systemY,
        int startMeasure, int endMeasure, bool isFirstSystem)
    {
        var builder = ImmutableArray.CreateBuilder<StaffGroupLayout>();
        double currentY = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;
        int globalStaffIndex = 0;

        // Track which groups are entirely hidden for inter-group spacing
        int lastVisibleGroupIndex = -1;

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];
            StaffGroupLayout layout;

            if (group.IsGrandStaff)
            {
                layout = LayoutGrandStaffGroupWithHaraKiri(
                    group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex,
                    startMeasure, endMeasure, isFirstSystem);
            }
            else if (group.HasDelimiter)
            {
                layout = LayoutBracketGroupWithHaraKiri(
                    group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex,
                    startMeasure, endMeasure, isFirstSystem);
            }
            else
            {
                layout = LayoutSingleStaffGroupWithHaraKiri(
                    group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex,
                    startMeasure, endMeasure, isFirstSystem);
            }

            builder.Add(layout);

            bool groupIsHidden = layout.Staves.All(s => s.IsHidden);
            if (!groupIsHidden)
            {
                currentY += layout.Height;
                lastVisibleGroupIndex = i;
            }

            // Inter-group gap: only add between visible groups
            if (i < score.StaffGroups.Length - 1 && !groupIsHidden)
            {
                // Check if next visible group exists
                bool nextGroupVisible = false;
                for (int j = i + 1; j < score.StaffGroups.Length; j++)
                {
                    bool allHidden = true;
                    foreach (var staff in score.StaffGroups[j].Staves)
                    {
                        if (!HaraKiri.ShouldHideStaff(staff, startMeasure, endMeasure, isFirstSystem))
                        {
                            allHidden = false;
                            break;
                        }
                    }
                    if (!allHidden) { nextGroupVisible = true; break; }
                }

                if (nextGroupVisible)
                {
                    // LILYPOND-REF: lily/align-interface.cc:240-252 — staff-affinity-aware spec selection.
                    var nextGroup = score.StaffGroups[i + 1];
                    var spec = SelectInterGroupSpec(group, nextGroup, sp);
                    bool nextIsOssia = nextGroup.Staves.Any(s => s.IsOssia);
                    bool currentIsOssia = group.Staves.Any(s => s.IsOssia);
                    // Ossia/staff pairs share one alignment in LP → ordinary
                    // staff-staff-spacing, scaled (see CalculateSystemHeight).
                    if (nextIsOssia || currentIsOssia)
                        spec = sp.StaffStaff;
                    bool textRowPair = group.Staves[^1].IsTextRow && nextGroup.Staves[0].IsTextRow;
                    double interGroupGap = textRowPair
                        ? TextRowPairGap
                        : spec.BasicDistance - staffHeight;
                    if (nextIsOssia || currentIsOssia)
                        interGroupGap *= OssiaScaleFactor;
                    currentY += interGroupGap;
                }
            }

            globalStaffIndex += group.StaffCount;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The shared hara-kiri staff-stacking loop for a group: places each staff at its
    /// real height (<see cref="GetStaffHeight"/> — a tab/ossia staff differs from the
    /// nominal staffHeight), hiding hara-kiri staves at zero height. Returns the
    /// builder plus the running bottom Y (<paramref name="currentY"/>) and whether any
    /// staff is visible (<paramref name="anyVisible"/>). The grand/single/bracket
    /// hara-kiri helpers share this and differ only in their delimiter tail.
    /// </summary>
    private ImmutableArray<StaffLayout>.Builder StackHaraKiriStaves(
        StaffGroup group, double y, double staffSpacing,
        int startIndex, int startMeasure, int endMeasure, bool isFirstSystem,
        out double currentY, out bool anyVisible)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        currentY = y;
        anyVisible = false;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            bool hidden = HaraKiri.ShouldHideStaff(staff, startMeasure, endMeasure, isFirstSystem);
            double thisStaffHeight = GetStaffHeight(staff);

            if (hidden)
            {
                staffLayouts.Add(new StaffLayout(
                    StaffIndex: startIndex + i,
                    Clef: staff.Clef,
                    Y: currentY,
                    Height: 0,
                    Tuning: staff.Tuning,
                    InstrumentName: staff.InstrumentName,
                    IsOssia: staff.IsOssia,
                    IsHidden: true));
            }
            else
            {
                if (anyVisible)
                    currentY += thisStaffHeight + Math.Max(0, staffSpacing);

                staffLayouts.Add(new StaffLayout(
                    StaffIndex: startIndex + i,
                    Clef: staff.Clef,
                    Y: currentY,
                    Height: thisStaffHeight,
                    Tuning: staff.Tuning,
                    InstrumentName: staff.InstrumentName,
                    IsOssia: staff.IsOssia));
                anyVisible = true;
            }
        }

        return staffLayouts;
    }

    /// <summary>Height of the last visible staff in a stacked group (0 if none).</summary>
    private static double LastVisibleStaffHeight(ImmutableArray<StaffLayout>.Builder staffLayouts)
    {
        for (int i = staffLayouts.Count - 1; i >= 0; i--)
            if (!staffLayouts[i].IsHidden)
                return staffLayouts[i].Height;
        return 0;
    }

    /// <summary>
    /// Layouts a grand staff group with hara-kiri support.
    /// </summary>
    private StaffGroupLayout LayoutGrandStaffGroupWithHaraKiri(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec,
        int startIndex, int startMeasure, int endMeasure, bool isFirstSystem)
    {
        double staffSpacing = staffSpec.BasicDistance - staffHeight;
        var staffLayouts = StackHaraKiriStaves(
            group, y, staffSpacing, startIndex, startMeasure, endMeasure, isFirstSystem,
            out double currentY, out bool anyVisible);

        if (!anyVisible)
        {
            // All staves hidden — zero-height group
            return StaffGroupLayout.CreateGrandStaff(
                staffLayouts.ToImmutable(), y, 0,
                new GrandStaffLayout(staffLayouts.ToImmutable(), 0, 0, 0));
        }

        double totalHeight = currentY + LastVisibleStaffHeight(staffLayouts) - y;
        double braceX = CurrentIndent - SystemStartBracePadding;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y + totalHeight);

        return StaffGroupLayout.CreateGrandStaff(
            staffLayouts.ToImmutable(), y, totalHeight, grandStaffLayout);
    }

    /// <summary>
    /// Layouts a single staff group with hara-kiri support.
    /// </summary>
    private StaffGroupLayout LayoutSingleStaffGroupWithHaraKiri(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec,
        int startIndex, int startMeasure, int endMeasure, bool isFirstSystem)
    {
        double staffSpacing = staffSpec.BasicDistance - staffHeight;
        var staffLayouts = StackHaraKiriStaves(
            group, y, staffSpacing, startIndex, startMeasure, endMeasure, isFirstSystem,
            out double currentY, out bool anyVisible);

        if (!anyVisible)
        {
            // All staves hidden — zero-height group
            return StaffGroupLayout.CreateSingle(
                staffLayouts[0], y, 0);
        }

        double lastVisibleHeight = LastVisibleStaffHeight(staffLayouts);
        double totalHeight = group.StaffCount == 1
            ? lastVisibleHeight
            : currentY + lastVisibleHeight - y;

        return StaffGroupLayout.CreateSingle(
            staffLayouts[0], y, totalHeight);
    }

    /// <summary>
    /// Layouts a bracket group with hara-kiri support.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc — bracket rendering with collapse-height
    /// </remarks>
    private StaffGroupLayout LayoutBracketGroupWithHaraKiri(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec,
        int startIndex, int startMeasure, int endMeasure, bool isFirstSystem)
    {
        double staffSpacing = staffSpec.BasicDistance - staffHeight;
        var staffLayouts = StackHaraKiriStaves(
            group, y, staffSpacing, startIndex, startMeasure, endMeasure, isFirstSystem,
            out double currentY, out bool anyVisible);

        if (!anyVisible)
        {
            return StaffGroupLayout.CreateBracketGroup(
                group.Type,
                staffLayouts.ToImmutable(), y, 0,
                new GrandStaffLayout(staffLayouts.ToImmutable(), 0, 0, 0, SystemStartDelimiterType.Bracket));
        }

        double totalHeight = currentY + LastVisibleStaffHeight(staffLayouts) - y;
        double bracketX = CurrentIndent - SystemStartBracketPadding;

        var delimiterLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: bracketX,
            BraceTop: y,
            BraceBottom: y + totalHeight,
            DelimiterType: SystemStartDelimiterType.Bracket);

        return StaffGroupLayout.CreateBracketGroup(
            group.Type,
            staffLayouts.ToImmutable(), y, totalHeight, delimiterLayout);
    }

    /// <summary>
    /// Layouts a grand staff group (piano/organ style with brace).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3042-3045 staff-staff-spacing
    /// </remarks>
    private StaffGroupLayout LayoutGrandStaffGroup(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        // Intra-group spacing: staff-staff basic distance is center-to-center
        double staffSpacing = staffSpec.BasicDistance - staffHeight;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: staffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName));

            if (i < group.Staves.Length - 1)
                currentY += staffHeight + Math.Max(0, staffSpacing);
        }

        double totalHeight = currentY + staffHeight - y;
        double braceX = CurrentIndent - SystemStartBracePadding;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y + totalHeight);

        return StaffGroupLayout.CreateGrandStaff(
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            grandStaffLayout);
    }

    /// <summary>
    /// Layouts a single staff or bracket group.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3042-3045 staff-staff-spacing
    /// </remarks>
    private StaffGroupLayout LayoutSingleStaffGroup(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        // Intra-group spacing: staff-staff basic distance is center-to-center
        double staffSpacing = staffSpec.BasicDistance - staffHeight;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            double thisStaffHeight = GetStaffHeight(staff);
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: thisStaffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                IsOssia: staff.IsOssia));

            if (i < group.Staves.Length - 1)
                currentY += thisStaffHeight + Math.Max(0, staffSpacing);
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = group.StaffCount == 1
            ? lastStaffHeight
            : currentY + lastStaffHeight - y;

        return StaffGroupLayout.CreateSingle(
            staffLayouts[0],
            y,
            totalHeight);
    }

    /// <summary>
    /// Layouts a bracket group (StaffGroup or ChoirStaff with bracket delimiter).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc — bracket rendering
    /// LILYPOND-REF: ly/engraver-init.ly — StaffGroup/ChoirStaff use SystemStartBracket
    /// </remarks>
    private StaffGroupLayout LayoutBracketGroup(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec, int startIndex)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;
        double staffSpacing = staffSpec.BasicDistance - staffHeight;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            double thisStaffHeight = GetStaffHeight(staff);
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: thisStaffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                IsOssia: staff.IsOssia));

            if (i < group.Staves.Length - 1)
                currentY += thisStaffHeight + Math.Max(0, staffSpacing);
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = currentY + lastStaffHeight - y;
        double bracketX = CurrentIndent - SystemStartBracketPadding;

        var delimiterLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: bracketX,
            BraceTop: y,
            BraceBottom: y + totalHeight,
            DelimiterType: SystemStartDelimiterType.Bracket);

        return StaffGroupLayout.CreateBracketGroup(
            group.Type,
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            delimiterLayout);
    }

    /// <summary>
    /// Layouts measures for multi-staff scores with timing-based column information.
    /// Supports measure ranges for system breaking and proportional justification.
    /// </summary>
    public ImmutableArray<MeasureLayout> LayoutMeasures(
        MultiStaffScore score, int systemIndex,
        int startMeasureIndex = 0, int? measureCount = null,
        bool isLastSystem = false,
        double? baseShortestDuration = null)
    {
        var primaryVoice = score.PrimaryContentStaff.PrimaryVoice;
        int endMeasureIndex = measureCount.HasValue
            ? startMeasureIndex + measureCount.Value
            : primaryVoice.Measures.Length;

        // The key signature is reprinted at every system head, reflecting any
        // mid-piece change in force before this system. Reserve width for the
        // WIDEST staff's active key: a transposed part (Staff.PerStaffKeySignature)
        // may carry more accidentals than the primary, and its signature must not
        // overrun the shared first-note column.
        // Tab staves never print a key signature. When EVERY staff is a tab, no key is
        // drawn at the system head, so reserve none of its width — the notes spread into
        // the reclaimed space (there is no notation staff to align against). A score with
        // any notation staff keeps the existing widest-key reservation unchanged.
        int activeKeySharps = score.AllStavesTab ? 0 : score.KeySignature.Sharps;
        int widestAccidentals = -1;
        if (!score.AllStavesTab)
            foreach (var staffGroup in score.StaffGroups)
                foreach (var staff in staffGroup.Staves)
                {
                    int sharps = (staff.PerStaffKeySignature ?? score.KeySignature).Sharps;
                    var pv = staff.PrimaryVoice;
                    for (int m = 0; m < startMeasureIndex && m < pv.Measures.Length; m++)
                        foreach (var item in pv.Measures[m].Items)
                            if (item is KeySignatureChangeItem kc)
                                sharps = kc.NewKey.Sharps;
                    if (Math.Abs(sharps) > widestAccidentals)
                    {
                        widestAccidentals = Math.Abs(sharps);
                        activeKeySharps = sharps;
                    }
                }

        // A meter change that OPENS this system's first measure (i.e. a change
        // landing exactly at the line break) is drawn in the prefix — clef, key,
        // THEN time — like LilyPond, instead of hanging off the first note column.
        // Reserve the time-signature width in the prefix (not the measure spring).
        // Only on a non-first system; the first system carries the initial meter.
        // A tab staff draws no time signature (DrawTabStaff skips the prefix meter), so
        // an all-tab score reserves none of its width — like the key signature, matching
        // what is drawn — and no meter change is hoisted into a prefix it does not have.
        TimeSignatureChangeItem? leadingTimeChange = null;
        if (!score.AllStavesTab && systemIndex > 0 && startMeasureIndex < primaryVoice.Measures.Length)
            foreach (var item in primaryVoice.Measures[startMeasureIndex].Items)
            {
                if (item is TimeSignatureChangeItem tc) { leadingTimeChange = tc; break; }
                if (item.Duration > Fraction.Zero) break;
            }

        bool prefixHasTime = !score.AllStavesTab && (systemIndex == 0 || leadingTimeChange != null);
        double prefixWidth = SpacingRules.CalculatePrefixWidth(activeKeySharps, prefixHasTime,
            leadingTimeChange?.NewTime.LayoutBeats ?? score.TimeSignature.LayoutBeats,
            leadingTimeChange?.NewTime.BeatType ?? score.TimeSignature.BeatType);
        // LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
        // System-internal coordinates are LINE-RELATIVE (0 = line start); the page
        // places the whole line at MarginLeft once, via the renderer's margin
        // translate. So startX must NOT include MarginLeft — baking it in here AND
        // translating again double-counts the left margin, shoving the music right
        // until the final barline hits the page edge (no right margin).
        double startX = CurrentIndent + prefixWidth;
        double availableWidth = _options.PageWidth - _options.MarginLeft - _options.MarginRight - CurrentIndent - prefixWidth;

        // End-of-line courtesy: the NEXT measure (first of the next system)
        // opening with a key change reserves room after this line's final
        // barline for the cancellation + new signature.
        // LILYPOND-REF: explicitKeySignatureVisibility default all-visible.
        if (endMeasureIndex < primaryVoice.Measures.Length)
        {
            foreach (var lead in primaryVoice.Measures[endMeasureIndex].Items)
            {
                if (lead is KeySignatureChangeItem kcNext)
                {
                    availableWidth -= SpacingRules.KeyCourtesySuffixWidth(
                        kcNext.PreviousKey.Sharps, kcNext.NewKey.Sharps);
                    break;
                }
                if (lead.Duration > Fraction.Zero)
                    break;
            }
        }

        // LILYPOND-REF: lily/spacing-spanner.cc — collect springs from ALL columns across
        // the entire system, then solve with a single SpringSolver for uniform force.
        // This matches SystemLayouter's approach for single-staff scores.

        // First pass: collect timing springs and barline widths per measure
        var measureSprings = new List<ImmutableArray<Spring>>();
        var measureTimings = new List<List<Fraction>>();
        var measureAllMeasures = new List<List<Measure>>();
        var measureBarlineWidths = new List<double>();
        double totalBarlineWidth = 0;

        for (int i = startMeasureIndex; i < endMeasureIndex; i++)
        {
            var primaryMeasure = primaryVoice.Measures[i];
            var allTimings = CollectAllTimingsForMeasure(score, i);
            var allMeasures = CollectAllMeasuresAtIndex(score, i);

            var springs = _measureLayouter.CreateTimingSprings(primaryMeasure, allTimings, baseShortestDuration, allMeasures);

            // Reserve room for lyric syllables so they don't collide. Only acts
            // on single-voice measures (timing columns == note items); a no-lyric
            // score leaves the chain untouched. Applied before the FirstNoteSpring
            // tweak below, which Math.Max-preserves the widened minimum.
            if (!score.Lyrics.IsDefaultOrEmpty)
            {
                // On a lead sheet the chords change at most every quarter note, so
                // the chord (primary) row has far fewer columns than the syllable
                // grid; reserving lyric width against IT under-counts and the
                // syllables crowd. Reserve against the DENSEST row at this bar (the
                // lyrics), whose item count matches the timing columns the springs
                // were built from. Staff-backed scores keep the primary measure.
                springs = score.IsLeadSheet
                    // The union timing columns don't match the syllable count on a lead sheet
                    // (chords and lyrics subdivide the bar differently), so reserve by column.
                    ? LyricSpacing.ApplyLeadSheetLyricSpacing(springs, allTimings, i, score.Lyrics)
                    : LyricSpacing.ApplyLyricSpacing(springs, primaryMeasure, i, score.Lyrics);
            }
            if (score.IsLeadSheet)
            {
                // Chord symbols reserve their widths on the row columns, and a
                // grid bar never collapses below a readable cell (else a long
                // chords-only chart packs onto one line and never wraps).
                springs = SpacingRules.ApplyChordRowSpacing(springs, allTimings, i, score.ChordNames);
                springs = SpacingRules.EnsureLeadSheetBarWidth(springs);
            }
            else if (!score.ChordNames.IsDefaultOrEmpty)
            {
                // Staff-attached chord symbols price their widths into the
                // columns on EVERY measure, exactly as LilyPond's ChordName
                // item extent (expanded (-0.5 . 0.5)) joins its paper column
                // — most visible over all-rest (R1) bars, which have no other
                // width source and otherwise collapse, overprinting the
                // symbols. LILYPOND-REF: scm/define-grobs.scm ChordName
                // extra-spacing-width.
                springs = SpacingRules.ApplyChordRowSpacing(
                    springs, allTimings, i, score.ChordNames, includeAttached: true);
            }

            // Tab fret digits (a Lily# enlargement of LilyPond's tiny numbers) are
            // wider than note heads and zigzag on chords; reserve their width in the
            // SHARED columns so adjacent digits don't overprint.
            foreach (var tGroup in score.StaffGroups)
                foreach (var tStaff in tGroup.Staves)
                    if (tStaff.IsTab && tStaff.Tuning is { } tabTuning
                        && i < tStaff.PrimaryVoice.Measures.Length)
                        springs = SpacingRules.ApplyTabChordSpacing(
                            springs, allTimings, tStaff.PrimaryVoice.Measures[i],
                            Tunings.GetTuning(tabTuning),
                            Tunings.SoundingShift(tStaff.TabSourceClef, tStaff.Transposition));

            // Reserve a wide script's (fermata / ornament) sideways reach in the
            // SHARED columns, per staff (a script is keyed by its own staff index),
            // so a fermata over one note doesn't crowd the next note's accidental.
            // Y-gated by the skyline: a fermata high above the staff leaves a low
            // following note untouched. Staff enumeration matches the global index
            // scripts are tagged with (see BuildAllStaffSkylines).
            if (!score.Articulations.IsDefaultOrEmpty)
            {
                int artStaffIndex = 0;
                foreach (var aGroup in score.StaffGroups)
                    foreach (var aStaff in aGroup.Staves)
                    {
                        if (i < aStaff.PrimaryVoice.Measures.Length)
                            springs = SpacingRules.ApplyArticulationSpacing(
                                springs, allTimings, aStaff.PrimaryVoice.Measures[i],
                                score.Articulations, i, artStaffIndex);
                        artStaffIndex++;
                    }
            }

            // LINE-START measure: spring 0 is the prefix→first-note spacing
            // (space-alist of the last prefix item), not the mid-line
            // BarLine semi-shrink. The prefix width itself ends at the ink.
            // LILYPOND-REF: scm/define-grobs.scm Clef/KeySignature/
            //   TimeSignature space-alist (first-note . ...).
            if (i == startMeasureIndex && springs.Length > 0)
            {
                // The 5.0 clef→first-note gap is tuned for a wide engraved clef; the
                // compact "TAB" clef needs far less, so an all-tab line starts its notes
                // close to the clef (there is no key/time between them either). Notation
                // staves keep the LilyPond space-alist value.
                var (ideal, min) = score.AllStavesTab
                    ? (TabClefToFirstNoteSpace, TabClefToFirstNoteSpace)
                    : SpacingRules.FirstNoteSpring(activeKeySharps, prefixHasTime);
                var s0 = springs[0];
                // When the opening meter change is hoisted into the prefix, its
                // hang-left width is no longer reserved in the measure — use the
                // bare prefix→first-note spring so the first note doesn't inherit
                // the (now prefix-drawn) time signature's reservation.
                double newMin = leadingTimeChange != null ? min : Math.Max(min, s0.MinDistance);
                springs = springs.SetItem(0, new Spring(
                    Math.Max(ideal, newMin), newMin, inverseStretchStrength: 0));
            }
            measureSprings.Add(springs);
            measureTimings.Add(allTimings);
            measureAllMeasures.Add(allMeasures);

            double barlineWidth = SpacingRules.GetBarlineWidth(primaryMeasure.StartBarline)
                                + SpacingRules.GetBarlineWidth(primaryMeasure.EndBarline);
            measureBarlineWidths.Add(barlineWidth);
            totalBarlineWidth += barlineWidth;
        }

        // Concatenate all springs and solve for a single force across the system
        var allSprings = measureSprings.SelectMany(s => s).ToImmutableArray();
        double springTargetWidth = availableWidth - totalBarlineWidth;

        double force = 0;
        if (allSprings.Length > 0)
        {
            var solver = new SpringSolver(allSprings);

            // LILYPOND-REF: lily/simple-spacer.cc:175-205 Simple_spacer::solve()
            // The last system is justified like the others — LilyPond's `ragged-last`
            // is unset by default and inherits `ragged-right` (= #f for a multi-line
            // score), so the final line fills the full width (verified against
            // LilyPond 2.24 output). Only a global ragged-right leaves lines unfilled.
            //
            // SPECIAL CASE (constrained-breaking.cc:142-148): a score whose
            // ONLY line would be STRETCHED uses ragged spacing — a single
            // sparse system keeps its natural width instead of being pulled
            // across the whole page. Compression still applies.
            bool singleSystemRagged = systemIndex == 0 && isLastSystem
                && new SpringSolver(allSprings).IdealTotalLength < springTargetWidth;
            force = SystemForceSolver.ResolveForce(solver, springTargetWidth, _options.RaggedRight || singleSystemRagged);
        }

        // Second pass: layout measures using the solved force
        var layouts = ImmutableArray.CreateBuilder<MeasureLayout>();
        double currentX = startX;

        for (int i = 0; i < measureSprings.Count; i++)
        {
            int measureIndex = startMeasureIndex + i;
            var primaryMeasure = primaryVoice.Measures[measureIndex];

            // Measure width = barline widths + sum of spring lengths at solved force
            double measureWidth = measureBarlineWidths[i];
            foreach (var spring in measureSprings[i])
            {
                measureWidth += spring.Length(force);
            }

            // For a lyric score, place items with the lyric-widened spring chain
            // (single-voice → columns coincide with items) so syllables sit under
            // their spread-out notes. Without lyrics, re-solve as before so no
            // existing layout shifts.
            var primarySprings = measureSprings[i];
            var itemLayouts = (!score.Lyrics.IsDefaultOrEmpty
                    && primarySprings.Length == primaryMeasure.Items.Length + 1)
                ? _measureLayouter.LayoutItems(primaryMeasure, measureWidth, primarySprings, force)
                : _measureLayouter.LayoutItems(primaryMeasure, measureWidth);
            var columnLayouts = _measureLayouter.LayoutColumns(
                primaryMeasure, measureWidth, measureTimings[i],
                baseShortestDuration, measureAllMeasures[i],
                measureSprings[i], force);

            var measureLayout = new MeasureLayout(measureIndex, currentX, measureWidth, itemLayouts, columnLayouts);
            layouts.Add(measureLayout);
            currentX += measureLayout.Width;
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Collects all unique timings from all voices for a specific measure.
    /// </summary>
    internal static List<Fraction> CollectAllTimingsForMeasure(MultiStaffScore score, int measureIndex)
    {
        var timings = new HashSet<Fraction>();

        foreach (var staffGroup in score.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                foreach (var voice in staff.Voices)
                {
                    if (measureIndex < voice.Measures.Length)
                    {
                        var measure = voice.Measures[measureIndex];
                        var currentTiming = Fraction.Zero;

                        foreach (var item in measure.Items)
                        {
                            timings.Add(currentTiming);
                            currentTiming += item.Duration;
                        }
                    }
                }
            }
        }

        // Timing-placed chord symbols carry their own rhythm and may fall BETWEEN
        // note onsets (e.g. a chord on a beat inside a half note). Give each its own
        // column so the spacing reserves the symbol's width there and the measure
        // widens to fit — otherwise the symbol has no column and overhangs the barline.
        if (!score.ChordNames.IsDefaultOrEmpty)
            foreach (var cn in score.ChordNames)
                if (cn.MeasureIndex == measureIndex && cn.UseTiming)
                    timings.Add(cn.Timing);

        var sortedTimings = timings.ToList();
        sortedTimings.Sort();
        return sortedTimings;
    }

    /// <summary>
    /// Collects all measures from all voices at a specific measure index.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/paper-column.cc — paper columns aggregate grobs from all staves.
    /// Column spacing rods must consider skyline collisions from ALL voices.
    /// </remarks>
    internal static List<Measure> CollectAllMeasuresAtIndex(MultiStaffScore score, int measureIndex)
    {
        var measures = new List<Measure>();

        foreach (var staffGroup in score.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                foreach (var voice in staff.Voices)
                {
                    if (measureIndex < voice.Measures.Length)
                    {
                        measures.Add(voice.Measures[measureIndex]);
                    }
                }
            }
        }

        return measures;
    }

    /// <summary>
    /// The measure with the most items among <paramref name="measures"/> (all rows at
    /// one bar). On a lead sheet this is the lyrics row (one spacer per syllable),
    /// whose item count matches the timing-column grid the springs were built from —
    /// the basis lyric-width reservation needs.
    /// </summary>
    internal static Measure DensestMeasure(IReadOnlyList<Measure> measures)
    {
        var best = measures[0];
        for (int k = 1; k < measures.Count; k++)
            if (measures[k].Items.Length > best.Items.Length)
                best = measures[k];
        return best;
    }

    // --- Skyline-based staff spacing ---

    /// <summary>
    /// Calculates the total height of a multi-staff system using skyline distances.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations()
    /// Uses per-staff skylines to determine actual spacing needed, instead of fixed formula.
    /// </remarks>
    public double CalculateSystemHeight(
        MultiStaffScore score, SkylineBuilder skylineBuilder, ImmutableArray<MeasureLayout> measureLayouts)
    {
        double height = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;

        // Build per-staff skylines
        var staffSkylines = BuildAllStaffSkylines(score, skylineBuilder, measureLayouts);

        int globalStaffIdx = 0;
        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];
            int staffCount = group.StaffCount;

            if (group.IsGrandStaff)
            {
                // Accumulate each staff's ACTUAL height (a tab/ossia staff in a
                // grand staff differs from the nominal staffHeight), matching this
                // method's own non-grand branch and the fixed CalculateSystemHeight
                // overload — the fixed-staffHeight version here mis-measured a
                // grand staff containing a non-normal staff.
                double groupHeight = GetStaffHeight(group.Staves[0]); // first staff
                for (int s = 1; s < group.Staves.Length; s++)
                {
                    int upperIdx = globalStaffIdx + s - 1;
                    int lowerIdx = globalStaffIdx + s;
                    double gap = CalculateStaffGapWithSkylines(
                        sp.StaffStaff, staffHeight, staffSkylines, upperIdx, lowerIdx);
                    groupHeight += gap + GetStaffHeight(group.Staves[s]);
                }
                height += groupHeight;
            }
            else
            {
                for (int s = 0; s < group.Staves.Length; s++)
                {
                    height += GetStaffHeight(group.Staves[s]);
                    if (s < group.Staves.Length - 1)
                    {
                        int upperIdx = globalStaffIdx + s;
                        int lowerIdx = globalStaffIdx + s + 1;
                        double gap = CalculateStaffGapWithSkylines(
                            sp.StaffStaff, staffHeight, staffSkylines, upperIdx, lowerIdx);
                        height += gap;
                    }
                }
            }

            if (i < score.StaffGroups.Length - 1)
            {
                int lastOfGroup = globalStaffIdx + staffCount - 1;
                int firstOfNext = globalStaffIdx + staffCount;
                // LILYPOND-REF: lily/align-interface.cc:240-252 — staff-affinity-aware spec selection.
                var nextGroup = score.StaffGroups[i + 1];
                var spec = SelectInterGroupSpec(group, nextGroup, sp);
                bool nextIsOssia = nextGroup.Staves.Any(s => s.IsOssia);
                bool currentIsOssia = group.Staves.Any(s => s.IsOssia);
                // Ossia/staff pairs share one alignment in LP → ordinary
                // staff-staff-spacing, scaled (see CalculateSystemHeight).
                if (nextIsOssia || currentIsOssia)
                    spec = sp.StaffStaff;
                bool textRowPair = group.Staves[^1].IsTextRow && nextGroup.Staves[0].IsTextRow;
                double interGroupGap = textRowPair
                    ? TextRowPairGap
                    : CalculateStaffGapWithSkylines(
                        spec, staffHeight, staffSkylines, lastOfGroup, firstOfNext);
                if (nextIsOssia || currentIsOssia)
                    interGroupGap *= OssiaScaleFactor;
                height += interGroupGap;
            }

            globalStaffIdx += staffCount;
        }

        return height;
    }

    /// <summary>
    /// Layouts all staff groups using skyline-based spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations()
    /// </remarks>
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(
        MultiStaffScore score, double systemY,
        SkylineBuilder skylineBuilder, ImmutableArray<MeasureLayout> measureLayouts)
    {
        var builder = ImmutableArray.CreateBuilder<StaffGroupLayout>();
        double currentY = 0;
        double staffHeight = _options.StaffHeight;
        var sp = _options.StaffSpacing;
        int globalStaffIndex = 0;

        var staffSkylines = BuildAllStaffSkylines(score, skylineBuilder, measureLayouts);

        for (int i = 0; i < score.StaffGroups.Length; i++)
        {
            var group = score.StaffGroups[i];

            if (group.IsGrandStaff)
            {
                var layout = LayoutGrandStaffGroupWithSkylines(
                    group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex,
                    staffSkylines);
                builder.Add(layout);
                currentY += layout.Height;
            }
            else if (group.HasDelimiter)
            {
                var layout = LayoutBracketGroupWithSkylines(
                    group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex,
                    staffSkylines);
                builder.Add(layout);
                currentY += layout.Height;
            }
            else
            {
                var layout = LayoutSingleStaffGroupWithSkylines(
                    group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex,
                    staffSkylines);
                builder.Add(layout);
                currentY += layout.Height;
            }

            if (i < score.StaffGroups.Length - 1)
            {
                int lastOfGroup = globalStaffIndex + group.StaffCount - 1;
                int firstOfNext = globalStaffIndex + group.StaffCount;
                // LILYPOND-REF: lily/align-interface.cc:240-252 — staff-affinity-aware spec selection.
                var nextGroup = score.StaffGroups[i + 1];
                var spec = SelectInterGroupSpec(group, nextGroup, sp);
                bool nextIsOssia = nextGroup.Staves.Any(s => s.IsOssia);
                bool currentIsOssia = group.Staves.Any(s => s.IsOssia);
                // Ossia/staff pairs share one alignment in LP → ordinary
                // staff-staff-spacing, scaled (see CalculateSystemHeight).
                if (nextIsOssia || currentIsOssia)
                    spec = sp.StaffStaff;
                bool textRowPair = group.Staves[^1].IsTextRow && nextGroup.Staves[0].IsTextRow;
                double interGroupGap = textRowPair
                    ? TextRowPairGap
                    : CalculateStaffGapWithSkylines(
                    spec, staffHeight, staffSkylines, lastOfGroup, firstOfNext);
                if (nextIsOssia || currentIsOssia)
                    interGroupGap *= OssiaScaleFactor;
                currentY += interGroupGap;
            }

            globalStaffIndex += group.StaffCount;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Layouts a grand staff group using skyline-based spacing.
    /// </summary>
    private StaffGroupLayout LayoutGrandStaffGroupWithSkylines(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec,
        int startIndex, List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: staffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName));

            if (i < group.Staves.Length - 1)
            {
                int upperIdx = startIndex + i;
                int lowerIdx = startIndex + i + 1;
                double gap = CalculateStaffGapWithSkylines(
                    staffSpec, staffHeight, staffSkylines, upperIdx, lowerIdx);
                currentY += staffHeight + gap;
            }
        }

        double totalHeight = currentY + staffHeight - y;
        double braceX = CurrentIndent - SystemStartBracePadding;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y + totalHeight);

        return StaffGroupLayout.CreateGrandStaff(
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            grandStaffLayout);
    }

    /// <summary>
    /// Layouts a single staff group using skyline-based spacing.
    /// </summary>
    private StaffGroupLayout LayoutSingleStaffGroupWithSkylines(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec,
        int startIndex, List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            double thisStaffHeight = GetStaffHeight(staff);
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: thisStaffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                IsOssia: staff.IsOssia));

            if (i < group.Staves.Length - 1)
            {
                int upperIdx = startIndex + i;
                int lowerIdx = startIndex + i + 1;
                double gap = CalculateStaffGapWithSkylines(
                    staffSpec, thisStaffHeight, staffSkylines, upperIdx, lowerIdx);
                currentY += thisStaffHeight + gap;
            }
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = group.StaffCount == 1
            ? lastStaffHeight
            : currentY + lastStaffHeight - y;

        return StaffGroupLayout.CreateSingle(
            staffLayouts[0],
            y,
            totalHeight);
    }

    /// <summary>
    /// Layouts a bracket group using skyline-based spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc — bracket rendering
    /// </remarks>
    private StaffGroupLayout LayoutBracketGroupWithSkylines(
        StaffGroup group, double y, double staffHeight, VerticalSpacingSpec staffSpec,
        int startIndex, List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines)
    {
        var staffLayouts = ImmutableArray.CreateBuilder<StaffLayout>();
        double currentY = y;

        for (int i = 0; i < group.Staves.Length; i++)
        {
            var staff = group.Staves[i];
            double thisStaffHeight = GetStaffHeight(staff);
            staffLayouts.Add(new StaffLayout(
                StaffIndex: startIndex + i,
                Clef: staff.Clef,
                Y: currentY,
                Height: thisStaffHeight,
                Tuning: staff.Tuning,
                InstrumentName: staff.InstrumentName,
                IsOssia: staff.IsOssia));

            if (i < group.Staves.Length - 1)
            {
                int upperIdx = startIndex + i;
                int lowerIdx = startIndex + i + 1;
                double gap = CalculateStaffGapWithSkylines(
                    staffSpec, thisStaffHeight, staffSkylines, upperIdx, lowerIdx);
                currentY += thisStaffHeight + gap;
            }
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = currentY + lastStaffHeight - y;
        double bracketX = CurrentIndent - SystemStartBracketPadding;

        var delimiterLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: bracketX,
            BraceTop: y,
            BraceBottom: y + totalHeight,
            DelimiterType: SystemStartDelimiterType.Bracket);

        return StaffGroupLayout.CreateBracketGroup(
            group.Type,
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            delimiterLayout);
    }

    /// <summary>
    /// Builds UP/DOWN skylines for every staff in the score.
    /// Returns a list indexed by global staff index.
    /// </summary>
    private List<(VerticalSkyline Up, VerticalSkyline Down)> BuildAllStaffSkylines(
        MultiStaffScore score, SkylineBuilder skylineBuilder,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        var result = new List<(VerticalSkyline Up, VerticalSkyline Down)>();

        // Each staff's own dynamics (tagged by StaffIndex) hang below it and must
        // widen the gap to the staff below; filter so a staff reserves room only
        // for its dynamics, cleared against its own voices.
        int staffIndex = 0;
        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                int thisStaff = staffIndex;
                var dynamics = score.Dynamics.IsDefaultOrEmpty
                    ? ImmutableArray<DynamicItem>.Empty
                    : score.Dynamics.Where(d => d.StaffIndex == thisStaff).ToImmutableArray();
                // A tab staff's forced-above Scripts (fermata/flageolet) drop into
                // the gap and hit the low noteheads of the staff above; reserve their
                // staff-local extent so the inter-staff gap widens to clear them.
                var tabArticulations = ArticulationEngraver.CalculateTabStaffLocal(
                    staff, thisStaff, score.Articulations, measureLayouts);
                var sky = skylineBuilder.BuildStaffSkylines(staff, measureLayouts, dynamics, tabArticulations);

                // A staff carrying associated chord names (`staff X with chords ...`)
                // shows a chord-symbol row just above it. The row shares one baseline
                // per system, raised to clear THIS staff's own high notes, so it can
                // rise well above the top line — reserve it in the UP skyline or a low
                // note in the staff ABOVE overprints the chord symbols. (An independent
                // chord GRID row, IsChordRow, is its own staff and reserves its own band.)
                if (!score.ChordNames.IsDefaultOrEmpty
                    && score.ChordNames.Any(c => c.StaffIndex == thisStaff && !c.IsChordRow))
                    ReserveChordRowBand(sky.Up, measureLayouts);

                result.Add(sky);
                staffIndex++;
            }
        }

        return result;
    }

    /// <summary>Half-height (ss) of a bold sans chord symbol's cap above its
    /// baseline — the renderer draws chord names at font 2.6 (FontSize 4.0 × 0.65),
    /// cap height ≈ 0.72 × 2.6.</summary>
    private const double ChordSymbolCapHeight = 1.9;

    /// <summary>Chord-name baseline distance above the staff top line — mirrors
    /// <c>ChordNameEngraver.StaffPadding</c>.</summary>
    private const double ChordRowStaffPadding = 0.6;

    /// <summary>
    /// Extends a staff's UP skyline to cover its associated chord-name row, so the
    /// gap to the staff above reserves room for the symbols. The row's baseline sits
    /// <see cref="ChordRowStaffPadding"/> + note-protrusion above the top line (see
    /// <see cref="ChordNameEngraver"/>), and the symbol reaches ~cap-height higher;
    /// reserve a flat band to that top across the drawn width.
    /// </summary>
    private static void ReserveChordRowBand(
        VerticalSkyline up, ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (up.IsEmpty || measureLayouts.IsDefaultOrEmpty)
            return;
        double xLeft = double.PositiveInfinity, xRight = double.NegativeInfinity;
        foreach (var ml in measureLayouts)
        {
            xLeft = Math.Min(xLeft, ml.X);
            xRight = Math.Max(xRight, ml.X + ml.Width);
        }
        if (xRight <= xLeft)
            return;

        // Note ink already in the skyline sets where the shared-baseline row floats;
        // the symbol top clears the top line by padding + that protrusion + cap.
        double protrusion = up.MaxProtrusionInRange(xLeft, xRight);
        double bandTop = -(ChordRowStaffPadding + protrusion + ChordSymbolCapHeight);
        up.Merge(VerticalSkyline.FromBox(xLeft, xRight, 0.0, bandTop, VerticalDirection.Up));
    }

    /// <summary>
    /// Calculates the gap between two adjacent staves using skyline distances.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations()
    ///
    /// Formula: gap = max(skyline_distance + padding, minimum_distance, basic_distance) - upper_staff_height
    ///
    /// The skyline distance gives the minimum center-to-center distance needed to avoid collisions.
    /// We then take the maximum of that (with padding), the minimum-distance, and basic-distance,
    /// then subtract the upper staff height to get the gap between bottom of upper and top of lower.
    /// </remarks>
    private static double CalculateStaffGapWithSkylines(
        VerticalSpacingSpec spec, double upperStaffHeight,
        List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines,
        int upperStaffIndex, int lowerStaffIndex)
    {
        if (upperStaffIndex >= staffSkylines.Count || lowerStaffIndex >= staffSkylines.Count)
            return Math.Max(0, spec.BasicDistance - upperStaffHeight);

        var upperDown = staffSkylines[upperStaffIndex].Down;
        var lowerUp = staffSkylines[lowerStaffIndex].Up;

        if (upperDown.IsEmpty || lowerUp.IsEmpty)
            return Math.Max(0, spec.BasicDistance - upperStaffHeight);

        double skyDistance = upperDown.Distance(lowerUp);

        // LILYPOND-REF: lily/align-interface.cc:247-260
        // center_to_center = max(skyline_distance + padding, minimum_distance, basic_distance)
        double centerToCenter = Math.Max(
            Math.Max(skyDistance + spec.Padding, spec.MinimumDistance),
            spec.BasicDistance);

        return Math.Max(0, centerToCenter - upperStaffHeight);
    }
}
