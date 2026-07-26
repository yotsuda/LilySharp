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
/// Parameters for spacing between staves within a system.
/// Controls both intra-group (staff-staff) and inter-group (staffgroup-staff) spacing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3350-3364 StaffGrouper
/// LILYPOND-REF: lily/staff-grouper-interface.cc
/// </remarks>
internal sealed record StaffSpacingParameters
{
    public static StaffSpacingParameters Default { get; } = new();

    /// <summary>
    /// Spacing between consecutive staves within the same group (e.g., piano grand staff).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3352-3355
    /// (staff-staff-spacing . ((basic-distance . 9)
    ///                         (minimum-distance . 7)
    ///                         (padding . 1)
    ///                         (stretchability . 5)))
    /// </remarks>
    public VerticalSpacingSpec StaffStaff { get; init; } = new()
    {
        BasicDistance = 9,
        MinimumDistance = 7,
        Padding = 1,
        Stretchability = 5
    };

    /// <summary>
    /// Spacing between the last staff of one group and the first staff of the next.
    /// Larger gap to visually separate groups.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3356-3359
    /// (staffgroup-staff-spacing . ((basic-distance . 10.5)
    ///                              (minimum-distance . 8)
    ///                              (padding . 1)
    ///                              (stretchability . 9)))
    /// </remarks>
    public VerticalSpacingSpec StaffGroupStaff { get; init; } = new()
    {
        BasicDistance = 10.5,
        MinimumDistance = 8,
        Padding = 1,
        Stretchability = 9
    };

    /// <summary>
    /// The gap below a staff that belongs to NO group at all — LilyPond's third branch,
    /// which Lily# did not have.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:1008-1027
    /// <c>calc_maybe_pure_staff_staff_spacing</c> — the spec is read off the UPPER staff of
    /// the pair (<c>get_spacing_spec</c> asks <c>before</c>, page-layout-problem.cc:1280-1281)
    /// and describes the gap BELOW it, and it has three branches, not two: no
    /// <c>staff-grouper</c> at all falls through to the staff's own
    /// <c>default-staff-staff-spacing</c>, and only a staff that HAS a grouper and is its
    /// last live spaceable member (<c>Staff_grouper_interface::maybe_pure_within_group</c>)
    /// takes <c>staffgroup-staff-spacing</c>.
    /// <para>
    /// LILYPOND-REF: scm/define-grobs.scm:4237-4239
    /// (default-staff-staff-spacing . ((basic-distance . 9)
    ///                                 (minimum-distance . 8)
    ///                                 (padding . 1)))
    /// ⚠️ NO <c>stretchability</c> MEMBER, so <c>set_default_strength</c> makes the inverse
    /// stretch strength the ideal itself, 9 — which is what
    /// <c>staffgroup-staff-spacing</c> declares outright, so the two springs stretch alike
    /// and only the basic-distance separates them. The COMPRESS strengths do differ
    /// (<c>ideal - minimum-distance</c> = 1 here against 2.5), which a compressed page would
    /// see.
    /// </para>
    /// </remarks>
    public VerticalSpacingSpec DefaultStaffStaff { get; init; } = new()
    {
        BasicDistance = 9,
        MinimumDistance = 8,
        Padding = 1,
        Stretchability = 9
    };

    /// <summary>
    /// Spacing between a non-staff line and the nearest staff in the direction of its
    /// <c>staff-affinity</c> ("close" / related direction).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grob-properties.scm:826-833 nonstaff-relatedstaff-spacing
    /// LILYPOND-REF: ly/engraver-init.ly:649-652 Lyrics defaults
    /// (basic-distance . 5.5) (padding . 0.5) (stretchability . 1)
    /// </remarks>
    public VerticalSpacingSpec NonStaffRelatedStaff { get; init; } = new()
    {
        BasicDistance = 5.5,
        MinimumDistance = 0,
        Padding = 0.5,
        Stretchability = 1
    };

    /// <summary>
    /// Spacing between a non-staff line and the staff in the direction opposite to
    /// its <c>staff-affinity</c> ("far" / unrelated direction).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grob-properties.scm:837-841 nonstaff-unrelatedstaff-spacing
    /// LILYPOND-REF: scm/define-grobs.scm:4239 VerticalAxisGroup default (padding . 0.5)
    /// LILYPOND-REF: ly/engraver-init.ly:658 Lyrics override padding=1.5
    /// </remarks>
    public VerticalSpacingSpec NonStaffUnrelatedStaff { get; init; } = new()
    {
        BasicDistance = 0,
        MinimumDistance = 0,
        Padding = 1.5,
        Stretchability = 0
    };

    /// <summary>
    /// Spacing between two adjacent non-staff lines (e.g., two consecutive Lyrics lines).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grob-properties.scm:819-823 nonstaff-nonstaff-spacing
    /// LILYPOND-REF: ly/engraver-init.ly:653-657 Lyrics defaults
    /// (basic-distance . 0) (minimum-distance . 2.8) (padding . 0.2) (stretchability . 0)
    /// </remarks>
    public VerticalSpacingSpec NonStaffNonStaff { get; init; } = new()
    {
        BasicDistance = 0,
        MinimumDistance = 2.8,
        Padding = 0.2,
        Stretchability = 0
    };

    /// <summary>
    /// Applies user overrides from \override StaffGrouper.* properties.
    /// Returns a new StaffSpacingParameters with overridden values.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-grouper-interface.cc — staff-staff-spacing, staffgroup-staff-spacing
    ///
    /// LilyPond syntax:
    ///   \override StaffGrouper.staff-staff-spacing.basic-distance = #10
    ///   \override StaffGrouper.staffgroup-staff-spacing.padding = #2
    ///
    /// Supports all 4 sub-properties: basic-distance, minimum-distance, padding, stretchability
    /// for both staff-staff-spacing and staffgroup-staff-spacing.
    ///
    /// ⚠️ THIS IS NOT alignment-distances, and a LILYPOND-REF to
    /// page-layout-problem.cc:656-717 sat here saying it was (removed 2026-07-26 — the
    /// shape HANDOFF 5.2.1 (1) warns about: a REF whose neighbouring code is a different
    /// quantity). What this overrides is the SPEC a staff spring is built from; LilyPond's
    /// alignment-distances is a per-system manual override read out of
    /// line-break-system-details (:706-717, and get_fixed_spacing :1202-1240) that makes
    /// the spring RIGID — set_ideal_distance(dy), set_min_distance(dy),
    /// set_inverse_stretch_strength(0). Lily# has no surface for it and does not port it;
    /// see HANDOFF 2D.
    /// </remarks>
    public StaffSpacingParameters ApplyOverrides(
        System.Collections.Immutable.ImmutableArray<LilySharp.Core.Svg.Model.GrobOverride> overrides)
    {
        if (overrides.IsDefaultOrEmpty)
            return this;

        var staffStaff = StaffStaff;
        var staffGroupStaff = StaffGroupStaff;

        foreach (var ovr in overrides)
        {
            if (ovr.GrobType != "StaffGrouper")
                continue;

            // Parse dotted property names: "staff-staff-spacing.basic-distance"
            var parts = ovr.PropertyName.Split('.', 2);
            if (parts.Length != 2)
                continue;

            string spacingType = parts[0];
            string subProperty = parts[1];

            if (!double.TryParse(ovr.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double value))
                continue;

            if (spacingType == "staff-staff-spacing")
            {
                staffStaff = ApplySubProperty(staffStaff, subProperty, value);
            }
            else if (spacingType == "staffgroup-staff-spacing")
            {
                staffGroupStaff = ApplySubProperty(staffGroupStaff, subProperty, value);
            }
        }

        if (staffStaff == StaffStaff && staffGroupStaff == StaffGroupStaff)
            return this;

        return this with { StaffStaff = staffStaff, StaffGroupStaff = staffGroupStaff };
    }

    private static VerticalSpacingSpec ApplySubProperty(VerticalSpacingSpec spec, string subProperty, double value)
    {
        return subProperty switch
        {
            "basic-distance" => spec with { BasicDistance = value },
            "minimum-distance" => spec with { MinimumDistance = value },
            "padding" => spec with { Padding = value },
            "stretchability" => spec with { Stretchability = value },
            _ => spec
        };
    }
}
