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
using LilySharp.Core.Svg.Collector;
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
    // Lays slurs out in the staff's own frame for the per-staff skyline (see
    // StaffSlurLayouts); cheap to construct (options only) and stateless per call.
    private readonly ElementCoordinator _elementCoordinator;

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
        _elementCoordinator = new ElementCoordinator(options);
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
        int globalStaffIndex = 0;

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
                height += NoteBoundLyricExtraGap(score, globalStaffIndex, globalStaffIndex + group.StaffCount);
            }

            globalStaffIndex += group.StaffCount;
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

    /// <summary>
    /// Extra inter-group gap so a note-bound (<c>with lyrics</c>) line's SECOND-and-later
    /// verses, which sit below a non-last group, clear the staff beneath. Verse 1 fits
    /// in the ordinary staff-staff distance (so single-verse layouts are unchanged); each
    /// further verse adds one <see cref="TextRowVerseSpacing"/> — the same step the
    /// LyricEngraver stacks verses by. 0 when the group's staves carry no such lyrics.
    /// LILYPOND-REF: axis-group-interface.cc skyline_spacing grows the gap by the
    /// outside-staff line's extent.
    /// </summary>
    private static double NoteBoundLyricExtraGap(MultiStaffScore score, int firstStaffIndex, int endStaffIndex)
    {
        if (score.Lyrics.IsDefaultOrEmpty) return 0;
        int maxVerse = 0;
        foreach (var ly in score.Lyrics)
            if (!ly.IsLyricsRow && ly.StaffIndex >= firstStaffIndex && ly.StaffIndex < endStaffIndex)
                maxVerse = Math.Max(maxVerse, ly.VerseNumber);
        return maxVerse <= 1 ? 0 : (maxVerse - 1) * TextRowVerseSpacing;
    }

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
    public ImmutableArray<StaffGroupLayout> LayoutStaffGroups(MultiStaffScore score)
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
                currentY -= layout.Height;
            }
            else if (group.HasDelimiter)
            {
                var layout = LayoutBracketGroup(group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY -= layout.Height;
            }
            else
            {
                var layout = LayoutSingleStaffGroup(group, currentY, staffHeight, sp.StaffStaff, globalStaffIndex);
                builder.Add(layout);
                currentY -= layout.Height;
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
                currentY -= interGroupGap;
                // Room for this group's `with lyrics` 2nd+ verses (verse 1 fits the gap).
                currentY -= NoteBoundLyricExtraGap(score, globalStaffIndex, globalStaffIndex + group.StaffCount);
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
        MultiStaffScore score,
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
                currentY -= layout.Height;
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
                    currentY -= interGroupGap;
                    // Room for this group's `with lyrics` 2nd+ verses (verse 1 fits the gap).
                    currentY -= NoteBoundLyricExtraGap(score, globalStaffIndex, globalStaffIndex + group.StaffCount);
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
                    currentY -= thisStaffHeight + Math.Max(0, staffSpacing);

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

        double totalHeight = y - currentY + LastVisibleStaffHeight(staffLayouts);
        double braceX = CurrentIndent - SystemStartBracePadding;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y - totalHeight);

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
            : y - currentY + lastVisibleHeight;

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

        double totalHeight = y - currentY + LastVisibleStaffHeight(staffLayouts);
        double bracketX = CurrentIndent - SystemStartBracketPadding;

        var delimiterLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: bracketX,
            BraceTop: y,
            BraceBottom: y - totalHeight,
            DelimiterType: SystemStartDelimiterType.Bracket);

        return StaffGroupLayout.CreateBracketGroup(
            group.Type,
            staffLayouts.ToImmutable(), y, totalHeight, delimiterLayout);
    }

    /// <summary>
    /// Layouts a grand staff group (piano/organ style with brace).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3352-3355 staff-staff-spacing
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
                currentY -= staffHeight + Math.Max(0, staffSpacing);
        }

        double totalHeight = y - currentY + staffHeight;
        double braceX = CurrentIndent - SystemStartBracePadding;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y - totalHeight);

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
    /// LILYPOND-REF: scm/define-grobs.scm:3352-3355 staff-staff-spacing
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
                currentY -= thisStaffHeight + Math.Max(0, staffSpacing);
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = group.StaffCount == 1
            ? lastStaffHeight
            : y - currentY + lastStaffHeight;

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
                currentY -= thisStaffHeight + Math.Max(0, staffSpacing);
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = y - currentY + lastStaffHeight;
        double bracketX = CurrentIndent - SystemStartBracketPadding;

        var delimiterLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: bracketX,
            BraceTop: y,
            BraceBottom: y - totalHeight,
            DelimiterType: SystemStartDelimiterType.Bracket);

        return StaffGroupLayout.CreateBracketGroup(
            group.Type,
            staffLayouts.ToImmutable(),
            y,
            totalHeight,
            delimiterLayout);
    }

    /// <summary>
    /// The line-start spring a system's opening measure gets: its bar-line spring 0
    /// REPLACED by the prefix→first-note spring, floored at LilyPond's 0.3 + min_dist.
    /// The ONE implementation of that spring, shared by the actual layout (LayoutMeasures)
    /// and the line-break gate (SystemBreaker.ComputeMultiStaffSpringData), so section 5.4's
    /// "the two spring systems and the break gate must agree" holds at the line start by
    /// construction rather than by a re-derivation that can drift.
    /// </summary>
    /// <remarks>
    /// Self-contained from the score and the line's first measure so the gate can call it
    /// per measure: it recomputes the prefix reservation (key ink, whether the prefix
    /// carries the meter, the break-align columns) that LayoutMeasures also computes for the
    /// prefix width — the SAME functions on the SAME inputs, so the numbers are identical and
    /// substituting this call for the inline block is output-invariant for the layout.
    /// LILYPOND-REF: lily/spacing-spanner.cc:219-224 — LilyPond generates the spacing for the
    ///   PREBROKEN pieces of a column, so a candidate line start is priced with the spring it
    ///   would really get. The spring itself is staff-spacing.cc:210-220 via
    ///   <see cref="LineStartColumn.SpringWithMinimumDistanceFloor"/>.
    /// </remarks>
    /// <param name="isFirstSystem">systemIndex == 0. The first line carries the opening meter
    /// in its prefix; a continuation hoists only a meter CHANGE that opens it.</param>
    /// <param name="measureSpring0">The opening measure's own spring 0 — the bar-line spring
    /// the substitution replaces, whose minimum still floors the FIXED distance (the leading
    /// grace / lyric widths Lily# prices here that LilyPond puts in separate paper columns;
    /// MEASURED 2026-07-25: dropping it moves 21 snapshots — grace-notes, lyric-break-pricing,
    /// lead-sheet-lyrics, chorale, ornaments — and is inert on a plain line start).</param>
    internal static Spring LineStartSpringForLine(
        MultiStaffScore score, int startMeasureIndex, bool isFirstSystem, Spring measureSpring0)
    {
        var primaryVoice = score.PrimaryContentStaff.PrimaryVoice;
        double activeKeyInk = SpacingRules.WidestActiveKeyInk(score, startMeasureIndex);

        // A meter change that OPENS a continuation system is drawn in the prefix (clef, key,
        // THEN time); the first system carries the initial meter. A tab prefix draws no
        // meter, so an all-tab score hoists none. Mirrors LayoutMeasures.
        TimeSignatureChangeItem? leadingTimeChange = null;
        if (!score.AllStavesTab && !isFirstSystem && startMeasureIndex < primaryVoice.Measures.Length)
            foreach (var item in primaryVoice.Measures[startMeasureIndex].Items)
            {
                if (item is TimeSignatureChangeItem tc) { leadingTimeChange = tc; break; }
                if (item.Duration > Fraction.Zero) break;
            }

        // …and no meter is booked when NO row engraves one (a chords / lyrics-only system):
        // SpacingRules.AnyStaffEngravesTime. Mirrors LayoutMeasures.
        bool prefixHasTime = !score.AllStavesTab && SpacingRules.AnyStaffEngravesTime(score)
                             && (isFirstSystem || leadingTimeChange != null);
        // The break-align GROUP's width places the shared meter column; each staff's own clef
        // ink inside that group is what its own first-note wish is measured from.
        double maxClefWidth = SpacingRules.MaxClefWidth(score);
        int prefixBeats = leadingTimeChange?.NewTime.LayoutBeats ?? score.TimeSignature.LayoutBeats;
        int prefixBeatType = leadingTimeChange?.NewTime.BeatType ?? score.TimeSignature.BeatType;
        // The break-align table itself, not just its right edge: the min_dist needs every
        // column's X to place the prefatory boxes (staff-spacing.cc:210).
        var prefixColumns = BreakAlignSpacing.SolvePrefixColumns(
            maxClefWidth, activeKeyInk, prefixHasTime, prefixBeats, prefixBeatType);

        // When the opening meter change is hoisted into the prefix, its hang-left width is no
        // longer reserved in the measure, so the bare space-alist fixed distance holds;
        // otherwise the measure's own spring-0 minimum still floors it (min_dist does not
        // cover the leading grace / lyric widths — LilyPond puts those in their own paper
        // columns; an accidental on the first note DOES reach min_dist, probe TKA +1.55).
        double? ownFixedFloor = leadingTimeChange != null ? null : measureSpring0.MinDistance;

        // ONE Staff_spacing wish per staff, merged — spacing-spanner.cc:492-517. The staves
        // do NOT agree: a tab staff ends its prefix on the TAB clef (minimum-fixed-space 5.0)
        // where its notation neighbour ends on the meter (semi-shrink-space 2.0), and
        // merge_springs averages the two ideals.
        return LineStartColumn.LineStartSpring(
            score, prefixColumns, SpacingRules.ClefGroupInkLeft(score),
            prefixHasTime ? GlyphMetrics.GetTimeSigWidth(prefixBeats, prefixBeatType) : 0.0,
            startMeasureIndex, ownFixedFloor);
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
        // mid-piece change in force before this system. Reserve the KeySignature
        // break-align group's own extent — the union of the signatures the system's staves
        // ENGRAVE (break-alignment-interface.cc:141-142), so a transposed part's wider
        // signature governs the shared column while a staff that prints none (a tab row,
        // whose TabStaff has no Key_engraver) books nothing however wide its key would be.
        double activeKeyInk = SpacingRules.WidestActiveKeyInk(score, startMeasureIndex);

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

        // A chords / lyrics-only system engraves no meter either — neither Lyrics nor
        // ChordNames consists a Time_signature_engraver (ly/engraver-init.ly:632-649,:703-725)
        // — so it books none, exactly as it books no key and (now) no clef.
        bool prefixHasTime = !score.AllStavesTab && SpacingRules.AnyStaffEngravesTime(score)
                             && (systemIndex == 0 || leadingTimeChange != null);
        // The widest clef in the system governs where every staff's meter and first note
        // sit — a bass/alto/C clef reserves more than the treble G (ledger defect-3). The
        // SAME width threads into FirstNoteSpring below so the clef-only case still cancels.
        double maxClefWidth = SpacingRules.MaxClefWidth(score);
        int prefixBeats = leadingTimeChange?.NewTime.LayoutBeats ?? score.TimeSignature.LayoutBeats;
        int prefixBeatType = leadingTimeChange?.NewTime.BeatType ?? score.TimeSignature.BeatType;
        // The break-align table itself, not just its right edge: the line-start spring's
        // min_dist needs every column's X to place the prefatory boxes (staff-spacing.cc:210).
        var prefixColumns = BreakAlignSpacing.SolvePrefixColumns(
            maxClefWidth, activeKeyInk, prefixHasTime, prefixBeats, prefixBeatType);
        double prefixWidth = prefixColumns.Right;
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

        // First pass: collect timing springs and barline widths per measure
        var measureSprings = new List<ImmutableArray<Spring>>();
        var measureTimings = new List<List<Fraction>>();
        var measureAllMeasures = new List<List<Measure>>();
        var measureBarlineWidths = new List<double>();
        double totalBarlineWidth = 0;
        // Per measure, per column: how far the CENTRED ink on that column reaches beyond it —
        // LilyPond's keep_inside_line_, which is symmetric here because the only grobs Lily#
        // centres on a column are text (see the rod block below).
        var measureColumnOverhangs = new List<double[]>();

        // A compressed multi-measure rest is ONE bar between two bar-line columns,
        // so the measures a run swallows contribute neither springs nor bar lines;
        // the run-opening measure carries the whole bar and takes the run rod below.
        var runMap = MmrRunMap.Build(MultiMeasureRestEngraver.FindRuns(score));

        for (int i = startMeasureIndex; i < endMeasureIndex; i++)
        {
            var primaryMeasure = primaryVoice.Measures[i];
            var allTimings = CollectAllTimingsForMeasure(score, i);
            var allMeasures = CollectAllMeasuresAtIndex(score, i);

            if (runMap.IsInterior(i))
            {
                measureSprings.Add(ImmutableArray<Spring>.Empty);
                measureTimings.Add(allTimings);
                measureAllMeasures.Add(allMeasures);
                measureBarlineWidths.Add(0);
                measureColumnOverhangs.Add(System.Array.Empty<double>());
                continue;
            }

            // The next measure is passed so a clef change opening it can be charged to
            // THIS measure's closing spring — LilyPond draws it before the shared bar
            // line. SystemBreaker mirrors this; the two must agree (SpacingInvariantTests).
            var nextMeasure = i + 1 < primaryVoice.Measures.Length
                ? primaryVoice.Measures[i + 1] : null;
            var springs = _measureLayouter.CreateTimingSprings(
                primaryMeasure, allTimings, baseShortestDuration, allMeasures, nextMeasure);

            // An empty placeholder measure (`| |`) has no timing springs at all —
            // without a floor it collapses to its barlines and reads as a double
            // barline. Give it one RIGID spring at the empty-bar slot width, so it
            // renders as a visible measure (matching the ideal-width floor the line
            // breaker uses — see SpacingRules.EmptyPlaceholderContentWidth).
            if (springs.Length == 0 && primaryMeasure.IsEmptyPlaceholder)
            {
                double slot = SpacingRules.EmptyPlaceholderContentWidth();
                springs = ImmutableArray.Create(new Spring(slot, slot, 0));
            }

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
                    : LyricSpacing.ApplyLyricSpacing(springs, primaryMeasure, allTimings, i, score.Lyrics);
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
            // Each column's own centred ink, for the keep-inside-line rods below. Measured
            // BEFORE spring 0 is replaced: the extents do not depend on the spring, but the
            // by-item/by-column choice reads springs.Length, exactly as the lyric reservation
            // above does.
            var lyricHalf = LyricSpacing.CentredHalfWidthPerColumn(
                springs, primaryMeasure, allTimings, i, score.Lyrics, score.IsLeadSheet);
            var chordHalf = SpacingRules.ChordCentredHalfWidthPerColumn(
                allTimings, i, score.ChordNames, includeAttached: !score.IsLeadSheet);
            var overhangs = new double[allTimings.Count];
            for (int c = 0; c < overhangs.Length; c++)
                overhangs[c] = Math.Max(
                    c < lyricHalf.Length ? lyricHalf[c] : 0.0,
                    c < chordHalf.Length ? chordHalf[c] : 0.0);
            measureColumnOverhangs.Add(overhangs);

            if (i == startMeasureIndex && springs.Length > 0)
            {
                // The bar-line spring 0 becomes the prefix→first-note spring, floored at
                // LilyPond's 0.3 + min_dist. ONE implementation (LineStartSpringForLine),
                // shared with the break gate so both price a line start identically
                // (section 5.4). systemIndex == 0 is the first line, which carries the meter.
                springs = springs.SetItem(0, LineStartSpringForLine(
                    score, startMeasureIndex, isFirstSystem: systemIndex == 0, springs[0]));
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

        // The system's rods, all fed through the one Simple_spacer::add_rod port
        // (SpringSolver.ApplyRods, blocking-force propagation included).
        var rods = new List<(int Left, int Right, double Distance)>();

        // KEEP-INSIDE-LINE: no column may push its ink into either margin.
        // LILYPOND-REF: lily/simple-spacer.cc:431-432 — every column but the line starter is
        //   given `keep_inside_line_ = col->extent (col, X_AXIS)` (the property is #t by
        //   default on PaperColumn scm/define-grobs.scm:2742 and NonMusicalPaperColumn :2525,
        //   and means "this column cannot have objects sticking into the margin",
        //   scm/define-grob-properties.scm:637) — and :556-560
        //     spacer.add_rod (i, cols.size (), cols[i].keep_inside_line_[RIGHT]);
        //     spacer.add_rod (0, i, -cols[i].keep_inside_line_[LEFT]);
        //   NO padding and no spring term: each rod is the bare overhang, which is why
        //   LilyPond's answer for a lead sheet is the overhang itself and not overhang + 0.5.
        // MEASURED (audit/lp-geometry/probes/staffless-system.ly, scores CL/CLX/CLL): a
        //   syllable reaching 2.312540 left of its column puts that column on 2.312539
        //   instead of the 0.500000 `min_dist + 0.5` alone gives, moving every column of the
        //   line by the same amount and none of the column-to-column distances; take the
        //   reach away (CLL) and the column drops straight back to 0.500000.
        // The rod is a MINIMUM, so it is inert wherever the springs already clear it — which
        // is everywhere but the first column and the last, since the springs in between
        // accumulate. That is why generalising this from the first column to all of them
        // moves nothing: the constraint was already met.
        // ⚠️ THE INPUT IS INCOMPLETE, and knowingly so. LilyPond's keep_inside_line_ is the
        //   column's WHOLE ink extent; what is fed in here is only the CENTRED TEXT on it —
        //   chord symbols and lyric syllables, which Lily# draws with text-anchor="middle"
        //   and which therefore hang half their width to the left. A MUSICAL column can also
        //   reach left: SpacingRules.MusicalColumnLeftReach is CalculateLeftExtent + esw, and
        //   probe TKT read 1.234272 for a note carrying an accidental against 0.100000 for a
        //   plain one — so an accidental reaches ~1.13 ss past its column and is NOT in this
        //   rod. It is inert today (the line-start spring's own min_dist already carries an
        //   opening accidental — probe TKA, +1.55 — and the springs between absorb any
        //   mid-line column's reach), but "no observable difference" is not a reason to skip
        //   a literal port. Doing it properly needs the item-to-timing-column mapping this
        //   loop does not have; see docs/HANDOFF.md section 2.
        // ⚠️ Two LilyPond quantities are unported and would change what this rod measures:
        //   ChordName declares no X-offset and no self-alignment-interface at all
        //   (scm/define-grobs.scm:837-855) so LilyPond's chord ink starts AT its column, and
        //   a LyricText is centred not on the column but on the PaperColumn placeholder
        //   X-alignment-extent = (0 . 1.35), i.e. at -w/2 + 0.675
        //   (self-alignment-interface.cc:117-176, define-grobs.scm:2749-2750). The ledger
        //   point staffless.line-start.chords-vs-staff (-0.438600) is the first of these.
        {
            int columnOffset = 0;
            for (int m = 0; m < measureColumnOverhangs.Count; m++)
            {
                var overhangs = measureColumnOverhangs[m];
                for (int c = 0; c < overhangs.Length; c++)
                {
                    if (overhangs[c] <= 0.0)
                        continue;
                    // Spring j spans column j → column j+1, so measure m's column c is the
                    // right end of spring columnOffset + c.
                    int column = columnOffset + c + 1;
                    if (column >= 1 && column <= allSprings.Length)
                        rods.Add((0, column, overhangs[c]));
                    // A rod from the LINE's last column to itself is the degenerate one
                    // LilyPond's own `add_rod (i, cols.size (), …)` reduces to; skip it.
                    if (column < allSprings.Length)
                        rods.Add((column, allSprings.Length, overhangs[c]));
                }
                columnOffset += measureSprings[m].Length;
            }
        }

        // Multi-measure rest runs: LilyPond's run-level rod across the springs of the
        // run-opening measure (the run's single column pair).
        // LILYPOND-REF: lily/multi-measure-rest.cc:341-391 calculate_spacing_rods →
        // Rod::add_to_cols → lily/simple-spacer.cc:90-128 Simple_spacer::add_rod.
        int springOffset = 0;
        for (int i = 0; i < measureSprings.Count; i++)
        {
            int measureIndex = startMeasureIndex + i;
            int springCount = measureSprings[i].Length;
            if (springCount > 0 && runMap.TryGetRunStartingAt(measureIndex, out var run))
            {
                // LP's Paper_column::minimum_distance (li, ri) between the bounding
                // bar-line columns: a skyline distance over the LEFT column's break-aligned
                // grobs (bar line + any leading key/time change). This used to sum the run
                // measure's spring MinDistances, which has no LilyPond counterpart: it is an
                // accumulation of spacing minima, not a geometric column distance, and it
                // inflated every run (an R1*5 run by ~3.4 ss).
                var runStartMeasure = primaryVoice.Measures[measureIndex];
                double minimumDistance = SpacingRules.MmrRodMinimumDistance(
                    SpacingRules.RunLeftBoundBarline(primaryVoice.Measures, measureIndex),
                    runStartMeasure.Items);

                var measureLength = Fraction.Zero;
                foreach (var item in runStartMeasure.Items)
                    measureLength += item.Duration;

                // Bar lines this measure adds to its width below — subtracted from the
                // rod so the run's CONTENT span, plus these bar lines, equals LilyPond's
                // li->ri column distance. See MmrRodDistance.
                double runBarlineWidth =
                    SpacingRules.GetBarlineWidth(runStartMeasure.StartBarline)
                    + SpacingRules.GetBarlineWidth(runStartMeasure.EndBarline);

                rods.Add((springOffset, springOffset + springCount,
                    SpacingRules.MmrRodDistance(
                        run.Count, measureLength, minimumDistance, runBarlineWidth)));
            }
            springOffset += springCount;
        }

        if (rods.Count > 0)
        {
            allSprings = SpringSolver.ApplyRods(allSprings, rods);
            // ApplyRods preserves order and count, so slice the adjusted springs back
            // into their measures — per-measure widths below read from these lists.
            int offset = 0;
            for (int i = 0; i < measureSprings.Count; i++)
            {
                int n = measureSprings[i].Length;
                if (n > 0)
                    measureSprings[i] = ImmutableArray.Create(allSprings.AsSpan(offset, n).ToArray());
                offset += n;
            }
        }

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

            var columnLayouts = _measureLayouter.LayoutColumns(
                primaryMeasure, measureWidth, measureTimings[i],
                baseShortestDuration, measureAllMeasures[i],
                measureSprings[i], force);

            // Derive the item slots FROM the solved columns so Items[i].X == the
            // column-grid X the renderer draws the notehead at — the raw-slot readers
            // (Hairpin / TextSpanner / TrillSpanner / TieVariant) then stay on the
            // notehead instead of drifting when a bar opens with a mid-piece meter/clef
            // change. Fall back to the item-spring layout only for a degenerate measure
            // with no timing columns (all zero-duration items).
            var itemLayouts = MeasureLayouter.LayoutItemsFromColumns(primaryMeasure, columnLayouts, measureWidth);
            if (itemLayouts.IsDefaultOrEmpty && primaryMeasure.Items.Length > 0)
                itemLayouts = _measureLayouter.LayoutItems(primaryMeasure, measureWidth);

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
                height += NoteBoundLyricExtraGap(score, globalStaffIdx, globalStaffIdx + staffCount);
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
        MultiStaffScore score,
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
                currentY -= layout.Height;
            }
            else if (group.HasDelimiter)
            {
                var layout = LayoutBracketGroupWithSkylines(
                    group, currentY, sp.StaffStaff, globalStaffIndex,
                    staffSkylines);
                builder.Add(layout);
                currentY -= layout.Height;
            }
            else
            {
                var layout = LayoutSingleStaffGroupWithSkylines(
                    group, currentY, sp.StaffStaff, globalStaffIndex,
                    staffSkylines);
                builder.Add(layout);
                currentY -= layout.Height;
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
                currentY -= interGroupGap;
                // Room for this group's `with lyrics` 2nd+ verses (verse 1 fits the gap).
                currentY -= NoteBoundLyricExtraGap(score, globalStaffIndex, globalStaffIndex + group.StaffCount);
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
                currentY -= staffHeight + gap;
            }
        }

        double totalHeight = y - currentY + staffHeight;
        double braceX = CurrentIndent - SystemStartBracePadding;

        var grandStaffLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: braceX,
            BraceTop: y,
            BraceBottom: y - totalHeight);

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
        StaffGroup group, double y, VerticalSpacingSpec staffSpec,
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
                currentY -= thisStaffHeight + gap;
            }
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = group.StaffCount == 1
            ? lastStaffHeight
            : y - currentY + lastStaffHeight;

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
        StaffGroup group, double y, VerticalSpacingSpec staffSpec,
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
                currentY -= thisStaffHeight + gap;
            }
        }

        double lastStaffHeight = GetStaffHeight(group.Staves[^1]);
        double totalHeight = y - currentY + lastStaffHeight;
        double bracketX = CurrentIndent - SystemStartBracketPadding;

        var delimiterLayout = new GrandStaffLayout(
            Staves: staffLayouts.ToImmutable(),
            BraceX: bracketX,
            BraceTop: y,
            BraceBottom: y - totalHeight,
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
                var tupletBrackets = StaffTupletBracketLayouts(score, staff, thisStaff, measureLayouts);
                var slurs = StaffSlurLayouts(score, staff, thisStaff, measureLayouts);
                var ties = StaffTieLayouts(score, staff, thisStaff, measureLayouts);
                var beams = StaffBeamLayouts(score, staff, thisStaff, measureLayouts);
                var sky = skylineBuilder.BuildStaffSkylines(
                    staff, measureLayouts, dynamics, tabArticulations, tupletBrackets, slurs, ties, beams);

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

    /// <summary>
    /// This staff's own tuplet brackets, positioned in the staff's own frame, so the
    /// skyline can reserve them.
    /// </summary>
    /// <remarks>
    /// The engraver is the SAME one the annotation pass runs; it is not re-implemented
    /// here. Two arguments make its answer staff-local rather than system-relative:
    /// <c>staffYAt</c> is null, so no staff offset is baked in and <c>*YUp</c> comes back
    /// measured from this staff's top line — exactly the skyline's own origin — and the
    /// per-staff dictionaries scope it to this staff's voices and measures.
    /// <para>
    /// The beam groups matter and cannot be skipped: a fully beamed tuplet draws no
    /// bracket (<c>bracket-visibility = if-no-beam</c>), and without them every beamed
    /// tuplet in the corpus would reserve a bracket that is never drawn. They are detected
    /// from the model alone (<see cref="BeamDetector"/> reads voices, time signature and
    /// tuplet spans), so they are available this early; the beam LAYOUTS are not, which
    /// only affects where a suppressed tuplet's NUMBER sits, and the number is not seeded.
    /// </para>
    /// </remarks>
    private ImmutableArray<TupletBracketLayout> StaffTupletBracketLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (score.TupletBrackets.IsDefaultOrEmpty)
            return ImmutableArray<TupletBracketLayout>.Empty;
        var staffTuplets = score.TupletBrackets
            .Where(t => t.StaffIndex == staffIndex)
            .ToImmutableArray();
        if (staffTuplets.IsEmpty)
            return ImmutableArray<TupletBracketLayout>.Empty;

        var detector = new BeamDetector();
        var beamGroups = ImmutableArray.CreateBuilder<BeamGroup>();
        for (int v = 0; v < staff.Voices.Length; v++)
        {
            // A tuplet bounds beaming only inside its OWN voice — the same filter
            // BeamDetector.DetectBeamGroups(Score) applies, for the same reason.
            var voiceTuplets = staffTuplets.Where(t => t.VoiceIndex == v).ToImmutableArray();
            beamGroups.AddRange(detector.DetectBeamGroups(
                staff.Voices[v], score.TimeSignature, voiceTuplets,
                voiceIndex: v, forceStemUp: VoiceDefaults.GetDefaultStemUp(v + 1)));
        }

        return TupletBracketEngraver.Calculate(
            staffTuplets, measureLayouts, staff.PrimaryVoice.Measures,
            beamGroups.ToImmutable(), beamLayouts: default,
            forceStemUp: staff.IsMultiVoice,
            measuresByStaff: new Dictionary<int, ImmutableArray<Measure>>
                { [staffIndex] = staff.PrimaryVoice.Measures },
            voicesByStaff: new Dictionary<int, ImmutableArray<Voice>> { [staffIndex] = staff.Voices },
            staffYAt: null,
            staffByIndex: new Dictionary<int, Staff> { [staffIndex] = staff });
    }

    /// <summary>
    /// This staff's own slurs, laid out in the staff's own frame so the skyline can
    /// reserve their bows. The mirror of <see cref="StaffTupletBracketLayouts"/>.
    /// </summary>
    /// <remarks>
    /// Slurs are scored against <see cref="SystemLayout"/>s, which do not exist yet when
    /// staves are being spaced — so build a TRIVIAL one-staff system with this staff at
    /// offset 0. The slur scorer's <c>staffMiddleDown</c> then carries no system offset
    /// (<c>StaffOffsetInSystemDown</c> of the sole staff is 0), and every returned
    /// <c>*YUp</c> is measured from the staff's top line, exactly the per-staff skyline's
    /// own origin — the same frame <see cref="StaffTupletBracketLayouts"/> produces. This
    /// reuses <see cref="ElementCoordinator.LayoutSlurs"/> whole rather than a second copy
    /// of its scoring. Slur geometry is independent of inter-staff spacing (it is fixed by
    /// note X and pitch), so computing it before the spacing is decided is sound.
    /// <para>
    /// Beams are not passed (default): they only shift a slur's ENDPOINT attachment to a
    /// beamed stem tip, never the peak that binds the gap, and the beam layouts are not
    /// available this early — the same trade <see cref="StaffTupletBracketLayouts"/> makes
    /// for the tuplet number.
    /// </para>
    /// </remarks>
    private ImmutableArray<SlurLayout> StaffSlurLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        var staffLayout = new StaffLayout(0, staff.Clef, Y: 0, Height: _options.StaffHeight);
        var group = StaffGroupLayout.CreateSingle(staffLayout, 0, _options.StaffHeight);
        var system = new SystemLayout(
            SystemIndex: 0, Y: 0,
            Width: _options.ContentWidth,
            PrefixWidth: 0,
            Measures: measureLayouts,
            StaffGroups: ImmutableArray.Create(group),
            Indent: 0);
        var staffScore = new Score(
            staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer);
        return _elementCoordinator.LayoutSlurs(
            staffScore, ImmutableArray.Create(system), staffIndex: 0, staff, score.GraceNotes);
    }

    /// <summary>
    /// This staff's own ties, laid out in the staff's own frame so the skyline can reserve
    /// their bows — the tie analogue of <see cref="StaffSlurLayouts"/>. Same trivial
    /// one-staff-at-offset-0 system, reusing <see cref="ElementCoordinator.LayoutTies"/>
    /// whole; tie geometry is fixed by note X and pitch, so it is sound to compute before
    /// the inter-staff spacing is decided.
    /// </summary>
    private ImmutableArray<TieLayout> StaffTieLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        var staffLayout = new StaffLayout(0, staff.Clef, Y: 0, Height: _options.StaffHeight);
        var group = StaffGroupLayout.CreateSingle(staffLayout, 0, _options.StaffHeight);
        var system = new SystemLayout(
            SystemIndex: 0, Y: 0,
            Width: _options.ContentWidth,
            PrefixWidth: 0,
            Measures: measureLayouts,
            StaffGroups: ImmutableArray.Create(group),
            Indent: 0);
        var staffScore = new Score(
            staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer);
        return _elementCoordinator.LayoutTies(
            staffScore, ImmutableArray.Create(system), staffIndex: 0, staff);
    }

    /// <summary>
    /// This staff's own beams, laid out in the staff's own frame so the skyline can reserve
    /// their outer edges — the beam analogue of <see cref="StaffSlurLayouts"/>. Reuses
    /// <see cref="ElementCoordinator.LayoutBeams"/> whole on the same trivial one-staff system,
    /// exactly as the final pass computes beams (<c>LayoutEngine.LayoutAllSpanners</c>).
    /// </summary>
    /// <remarks>
    /// A beam's Y is measured from the staff MIDDLE (half-space positions) and its X from the
    /// measure layout — both independent of the inter-staff spacing — so computing it before
    /// that spacing is decided is sound, and it matches the drawn beam.
    /// <para>
    /// The score MUST expose all voices, as the final pass does: auto-beaming runs per voice, so
    /// a second-voice beam — the shape audit/lp-geometry BMD/BMU measure — never forms on a
    /// primary-voice-only score. Tuplet spans are passed because auto beams break at tuplet
    /// boundaries (<see cref="BeamDetector"/>), so the beams here are the ones the renderer draws.
    /// </para>
    /// <para>
    /// Internal so <c>LayoutEngine</c> can compute the SAME per-staff beams for the
    /// system silhouette (<c>SkylineBuilder.BuildSystemSkylines</c>) — one beam model
    /// for both skylines, the way <c>BuildStaffSkylines</c> and this pass already share
    /// one. audit/lp-geometry system.beam-{under,over}-notes.
    /// </para>
    /// </remarks>
    internal ImmutableArray<BeamLayout> StaffBeamLayouts(
        MultiStaffScore score, Staff staff, int staffIndex,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        var staffLayout = new StaffLayout(0, staff.Clef, Y: 0, Height: _options.StaffHeight);
        var group = StaffGroupLayout.CreateSingle(staffLayout, 0, _options.StaffHeight);
        var system = new SystemLayout(
            SystemIndex: 0, Y: 0,
            Width: _options.ContentWidth,
            PrefixWidth: 0,
            Measures: measureLayouts,
            StaffGroups: ImmutableArray.Create(group),
            Indent: 0);
        var staffTuplets = score.TupletBrackets.IsDefaultOrEmpty
            ? ImmutableArray<TupletBracketItem>.Empty
            : score.TupletBrackets.Where(t => t.StaffIndex == staffIndex).ToImmutableArray();
        var staffScore = staff.Voices.Length > 1
            ? new Score(
                staff.Voices, score.TimeSignature, score.KeySignature,
                LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: staffTuplets)
            : new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                LayoutEngine.ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: staffTuplets);
        return _elementCoordinator.LayoutBeams(staffScore, ImmutableArray.Create(system), staffIndex: 0);
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
        double bandTop = ChordRowStaffPadding + protrusion + ChordSymbolCapHeight; // Y-up above the top line
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
