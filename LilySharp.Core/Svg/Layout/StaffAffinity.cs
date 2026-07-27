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
/// Constants for the <c>staff-affinity</c> direction, matching LilyPond's
/// integer Direction values.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/grob-properties.hh — Direction enum (UP=1, DOWN=-1, CENTER=0)
/// LILYPOND-REF: scm/define-grob-properties.scm:1234-1240 staff-affinity
/// </remarks>
public static class StaffAffinityDirection
{
    /// <summary>Attach to the staff above (UP).</summary>
    public const int Up = 1;

    /// <summary>Attach to the staff below (DOWN).</summary>
    public const int Down = -1;

    /// <summary>Centered between staves (CENTER) — used by DynamicsLine.</summary>
    public const int Center = 0;
}

/// <summary>
/// Helpers for selecting the correct vertical spacing spec between adjacent
/// staves based on their <c>staff-affinity</c>.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:240-252 — staff-affinity-driven spacing selection
/// LILYPOND-REF: lily/page-layout-problem.cc:1173-1183 — is_spaceable check
/// LILYPOND-REF: scm/define-grob-properties.scm:819-841 — non-staff spacing properties
/// </remarks>
internal static class StaffAffinity
{
    /// <summary>
    /// True iff the staff is spaceable (no <c>staff-affinity</c> set).
    /// LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 Page_layout_problem::is_spaceable
    /// </summary>
    public static bool IsSpaceable(int? staffAffinity) => !staffAffinity.HasValue;

    /// <summary>
    /// LilyPond's <c>LARGE_STRETCH</c> — what goes between a non-spaceable line and the
    /// staff its affinity does NOT point at.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-layout-problem.cc:1262.</remarks>
    public const double LargeStretch = 10e5;

    /// <summary>
    /// The spacing spec connecting <paramref name="before"/> (the upper element) to
    /// <paramref name="after"/> (the lower one) — <c>Page_layout_problem::get_spacing_spec</c>,
    /// branch for branch.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1266-1342 <c>get_spacing_spec</c>.
    /// <para>
    /// ⚠️ THE SPEC IS READ OFF A GROB, WHICH IS WHY THE SPECS COME IN PER-CONTEXT SETS
    /// (<see cref="StaffSpacingParameters.NonStaffSpacing"/>): every non-spaceable branch
    /// below asks <c>before</c> or <c>after</c> for ITS OWN <c>nonstaff-*</c> property, and
    /// Lyrics and ChordNames do not declare the same numbers. A single score-wide
    /// <c>NonStaffRelatedStaff</c> — which is what this took until 2026-07-27 — builds the
    /// Lyrics' 5.5-ideal spring under a ChordNames line.
    /// </para>
    /// <para>
    /// ⚠️ THE BOTH-NON-SPACEABLE CASE HAS THREE BRANCHES, NOT ONE (:1313-1337), and the third
    /// was missing: two lines whose affinities point AWAY from each other (the upper one UP,
    /// the lower one DOWN — a lyric line under one staff above a chord row belonging to the
    /// next) take the upper line's <c>nonstaff-unrelatedstaff-spacing</c> with
    /// <see cref="LargeStretch"/>, not <c>nonstaff-nonstaff-spacing</c>. LilyPond warns when
    /// the affinities INCREASE (:1322-1325); Lily# has no surface that can produce that
    /// order, so the warning has nothing to fire on and is not ported.
    /// </para>
    /// <para>
    /// ⚠️ <paramref name="spaceableSpec"/> IS THE CALLER'S because the spaceable/spaceable
    /// branch reads <c>before</c>'s <c>staff-staff-spacing</c>, and WHICH of the three that
    /// is (staff-staff, staffgroup-staff, default-staff-staff) is a grouper question the
    /// caller has already answered — see <c>MultiStaffLayouter.SelectInterGroupSpec</c>.
    /// </para>
    /// <para>
    /// ⚠️ THE STRETCHABILITY IS ADDED, NOT SET (<c>add_stretchability</c>, :1245-1255): it
    /// applies only where the spec DECLARES none, which is why it is spelled as a
    /// <c>with</c> on a null <see cref="VerticalSpacingSpec.Stretchability"/> rather than
    /// unconditionally.
    /// </para>
    /// </remarks>
    public static VerticalSpacingSpec GetSpacingSpec(
        int? beforeAffinity, StaffSpacingParameters.NonStaffSpacing beforeSpecs,
        int? afterAffinity, StaffSpacingParameters.NonStaffSpacing afterSpecs,
        VerticalSpacingSpec spaceableSpec)
    {
        // LILYPOND-REF: :1277-1296 — is_spaceable (before)
        if (IsSpaceable(beforeAffinity))
        {
            // LILYPOND-REF: :1279-1281
            if (IsSpaceable(afterAffinity))
                return spaceableSpec;

            // LILYPOND-REF: :1284-1294 — the AFTER line's own affinity and its own specs.
            return afterAffinity == StaffAffinityDirection.Down
                ? AddStretchability(afterSpecs.UnrelatedStaff, LargeStretch)
                : afterSpecs.RelatedStaff;
        }

        // LILYPOND-REF: :1299-1312
        if (IsSpaceable(afterAffinity))
            return beforeAffinity == StaffAffinityDirection.Up
                ? AddStretchability(beforeSpecs.UnrelatedStaff, LargeStretch)
                : beforeSpecs.RelatedStaff;

        // LILYPOND-REF: :1313-1337 — neither is spaceable. Both readings are the BEFORE
        // line's property; only the last branch differs in which property.
        if (beforeAffinity != StaffAffinityDirection.Up)
            return beforeSpecs.NonStaff;
        if (afterAffinity != StaffAffinityDirection.Down)
            return beforeSpecs.NonStaff;
        return AddStretchability(beforeSpecs.UnrelatedStaff, LargeStretch);
    }

    /// <summary>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1245-1255 <c>add_stretchability</c> — adds
    /// the member only when the spec does not already carry one.
    /// </summary>
    private static VerticalSpacingSpec AddStretchability(VerticalSpacingSpec spec, double stretch)
        => spec.Stretchability.HasValue ? spec : spec with { Stretchability = stretch };
}
