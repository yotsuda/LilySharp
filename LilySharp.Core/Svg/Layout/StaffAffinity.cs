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
public static class StaffAffinity
{
    /// <summary>
    /// True iff the staff is spaceable (no <c>staff-affinity</c> set).
    /// LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 Page_layout_problem::is_spaceable
    /// </summary>
    public static bool IsSpaceable(int? staffAffinity) => !staffAffinity.HasValue;

    /// <summary>
    /// Selects the right vertical-spacing spec for the gap between two adjacent
    /// staves (<paramref name="upperAffinity"/> = upper staff, <paramref name="lowerAffinity"/>
    /// = lower staff). Both are <see cref="LilySharp.Core.Svg.Model.Staff.StaffAffinity"/>
    /// values (null = spaceable, otherwise UP/DOWN/CENTER).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:240-252
    /// Decision table (upper × lower):
    /// - spaceable + spaceable          → staff-staff or staffgroup-staff (caller-supplied)
    /// - non-spaceable + non-spaceable  → nonstaff-nonstaff
    /// - one is non-spaceable:
    ///     * affinity points at the spaceable side  → nonstaff-relatedstaff (close)
    ///     * affinity points away                   → nonstaff-unrelatedstaff (far)
    ///     * CENTER                                  → nonstaff-relatedstaff (close, treated as related on both sides)
    /// </remarks>
    public static VerticalSpacingSpec Select(
        int? upperAffinity, int? lowerAffinity,
        VerticalSpacingSpec spaceableSpec, StaffSpacingParameters sp)
    {
        bool upperSpaceable = IsSpaceable(upperAffinity);
        bool lowerSpaceable = IsSpaceable(lowerAffinity);

        if (upperSpaceable && lowerSpaceable)
            return spaceableSpec;

        if (!upperSpaceable && !lowerSpaceable)
            return sp.NonStaffNonStaff;

        // Exactly one is non-spaceable. Identify which way its affinity points.
        // LILYPOND-REF: lily/align-interface.cc:247-260 — direction-aware spec selection
        int? nonStaffAffinity = upperSpaceable ? lowerAffinity : upperAffinity;
        // The spaceable element sits above the non-spaceable iff the upper element is spaceable.
        bool spaceableIsAbove = upperSpaceable;

        // CENTER: treated as related on both sides.
        if (nonStaffAffinity == StaffAffinityDirection.Center)
            return sp.NonStaffRelatedStaff;

        // Affinity points at spaceable iff:
        //   spaceable above + affinity UP, or spaceable below + affinity DOWN
        bool pointsAtSpaceable =
            (spaceableIsAbove && nonStaffAffinity == StaffAffinityDirection.Up) ||
            (!spaceableIsAbove && nonStaffAffinity == StaffAffinityDirection.Down);

        return pointsAtSpaceable ? sp.NonStaffRelatedStaff : sp.NonStaffUnrelatedStaff;
    }
}
