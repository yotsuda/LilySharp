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
/// LILYPOND-REF: scm/define-grobs.scm:3040-3054 StaffGrouper
/// LILYPOND-REF: lily/staff-grouper-interface.cc
/// </remarks>
internal sealed record StaffSpacingParameters
{
    public static StaffSpacingParameters Default { get; } = new();

    /// <summary>
    /// Spacing between consecutive staves within the same group (e.g., piano grand staff).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3042-3045
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
    /// LILYPOND-REF: scm/define-grobs.scm:3046-3049
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
    /// Spacing for non-spaceable (loose) staves that use staff-affinity.
    /// Kept for backward compatibility; equivalent to <see cref="NonStaffRelatedStaff"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:240-252 staff-affinity
    /// </remarks>
    public VerticalSpacingSpec NonStaff { get; init; } = new()
    {
        BasicDistance = 5,
        MinimumDistance = 3,
        Padding = 0.5,
        Stretchability = 2
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
    /// LILYPOND-REF: lily/page-layout-problem.cc:656-717 alignment_distances
    /// LILYPOND-REF: lily/staff-grouper-interface.cc — staff-staff-spacing, staffgroup-staff-spacing
    ///
    /// LilyPond syntax:
    ///   \override StaffGrouper.staff-staff-spacing.basic-distance = #10
    ///   \override StaffGrouper.staffgroup-staff-spacing.padding = #2
    ///
    /// Supports all 4 sub-properties: basic-distance, minimum-distance, padding, stretchability
    /// for both staff-staff-spacing and staffgroup-staff-spacing.
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
