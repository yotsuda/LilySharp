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
public sealed record StaffSpacingParameters
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
}
