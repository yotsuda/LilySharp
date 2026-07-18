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
/// Parameters for tie formatting.
/// Values from define-grobs.scm (Tie.details) override C++ defaults in tie-details.cc.
/// Staff-line clearances are stored in staff spaces (LilyPond stores in half-spaces,
/// divide by 2 for staff-space equivalent).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tie-details.cc:37-93 Tie_details::from_grob()
/// LILYPOND-REF: scm/define-grobs.scm:3866-3909 Tie grob definition
/// </remarks>
public sealed record TieDetails
{
    /// <summary>Default parameters matching LilyPond effective defaults.</summary>
    public static TieDetails Default { get; } = new();

    // --- Shape parameters ---

    /// <summary>
    /// Maximum height of tie arc (staff spaces).
    /// LILYPOND-REF: define-grobs.scm: height-limit = 1.0
    /// </summary>
    public double HeightLimit { get; init; } = 1.0;

    /// <summary>
    /// Ratio for determining tie height based on length.
    /// LILYPOND-REF: define-grobs.scm: ratio = 0.333
    /// </summary>
    public double Ratio { get; init; } = 0.333;

    /// <summary>
    /// Gap between tie endpoint and notehead (staff spaces).
    /// LILYPOND-REF: define-grobs.scm: note-head-gap = 0.2
    /// </summary>
    public double XGap { get; init; } = 0.2;

    /// <summary>
    /// Gap between tie and stem (staff spaces).
    /// LILYPOND-REF: define-grobs.scm: stem-gap = 0.35
    /// </summary>
    public double StemGap { get; init; } = 0.35;

    /// <summary>
    /// Minimum tie length (staff spaces).
    /// LILYPOND-REF: tie-details.cc: min-length default = 1.0
    /// </summary>
    public double MinLength { get; init; } = 1.0;

    /// <summary>
    /// Length limit for ties between notes (staff spaces).
    /// LILYPOND-REF: define-grobs.scm: between-length-limit = 1.0
    /// </summary>
    public double BetweenLengthLimit { get; init; } = 1.0;

    // --- Staff line collision ---

    /// <summary>
    /// Clearance from staff lines at tip (staff spaces).
    /// LilyPond: 0.45 half-spaces / 2 = 0.225 staff spaces.
    /// LILYPOND-REF: define-grobs.scm: tip-staff-line-clearance = 0.45 (half-spaces)
    /// </summary>
    public double TipStaffLineClearance { get; init; } = 0.225;

    /// <summary>
    /// Clearance from staff lines at center (staff spaces).
    /// LilyPond: 0.6 half-spaces / 2 = 0.3 staff spaces.
    /// LILYPOND-REF: define-grobs.scm: center-staff-line-clearance = 0.6 (half-spaces)
    /// </summary>
    public double CenterStaffLineClearance { get; init; } = 0.3;

    /// <summary>
    /// Penalty for collision with staff line.
    /// LILYPOND-REF: tie-details.cc: staff-line-collision-penalty default = 5
    /// </summary>
    public double StaffLineCollisionPenalty { get; init; } = 5.0;

    // --- Dot collision ---

    /// <summary>
    /// Clearance from dots (staff spaces).
    /// LILYPOND-REF: tie-details.cc: dot-collision-clearance default = 0.25
    /// </summary>
    public double DotCollisionClearance { get; init; } = 0.25;

    /// <summary>
    /// Penalty for collision with dots.
    /// LILYPOND-REF: tie-details.cc: dot-collision-penalty default = 0.25
    /// </summary>
    public double DotCollisionPenalty { get; init; } = 0.25;

    // --- Direction penalties ---

    /// <summary>
    /// Penalty for tie going in wrong direction.
    /// LILYPOND-REF: tie-details.cc: wrong-direction-offset-penalty default = 10
    /// </summary>
    public double WrongDirectionOffsetPenalty { get; init; } = 10.0;

    /// <summary>
    /// Penalty for tie direction same as stem.
    /// LILYPOND-REF: define-grobs.scm: same-dir-as-stem-penalty = 8
    /// </summary>
    public double SameDirAsStemPenalty { get; init; } = 8.0;

    // --- Length penalties ---

    /// <summary>
    /// Factor for min length penalty.
    /// LILYPOND-REF: define-grobs.scm: min-length-penalty-factor = 26
    /// </summary>
    public double MinLengthPenaltyFactor { get; init; } = 26.0;

    // --- Skyline ---

    /// <summary>
    /// Padding for skyline collision detection.
    /// LILYPOND-REF: tie-details.cc: skyline-padding default = 0.05
    /// </summary>
    public double SkylinePadding { get; init; } = 0.05;

    // --- Tie-tie collision ---

    /// <summary>
    /// Penalty for tie-tie collision.
    /// LILYPOND-REF: define-grobs.scm: tie-tie-collision-penalty = 25.0
    /// </summary>
    public double TieTieCollisionPenalty { get; init; } = 25.0;

    /// <summary>
    /// Distance threshold for tie-tie collision (staff spaces).
    /// LILYPOND-REF: define-grobs.scm: tie-tie-collision-distance = 0.45
    /// </summary>
    public double TieTieCollisionDistance { get; init; } = 0.45;

    // --- Distance penalties ---

    /// <summary>
    /// Penalty factor for horizontal distance.
    /// LILYPOND-REF: define-grobs.scm: horizontal-distance-penalty-factor = 10
    /// </summary>
    public double HorizontalDistancePenaltyFactor { get; init; } = 10.0;

    /// <summary>
    /// Penalty factor for vertical distance.
    /// LILYPOND-REF: define-grobs.scm: vertical-distance-penalty-factor = 7
    /// </summary>
    public double VerticalDistancePenaltyFactor { get; init; } = 7.0;

    // --- Intra-space ---

    /// <summary>
    /// Threshold for intra-space positioning.
    /// LILYPOND-REF: define-grobs.scm: intra-space-threshold = 1.25
    /// </summary>
    public double IntraSpaceThreshold { get; init; } = 1.25;

    // --- Region sizes ---

    /// <summary>
    /// Search region size for single ties.
    /// LILYPOND-REF: define-grobs.scm: single-tie-region-size = 4
    /// </summary>
    public int SingleTieRegionSize { get; init; } = 4;

    /// <summary>
    /// Search region size for multiple ties.
    /// LILYPOND-REF: define-grobs.scm: multi-tie-region-size = 3
    /// </summary>
    public int MultiTieRegionSize { get; init; } = 3;

    // --- Multi-tie scoring ---

    /// <summary>
    /// Penalty for non-monotonic tie column (edges/centers must increase).
    /// LILYPOND-REF: tie-details.cc: tie-column-monotonicity-penalty default = 100
    /// </summary>
    public double TieColumnMonotonicityPenalty { get; init; } = 100.0;

    /// <summary>
    /// Vertical gap between outer ties and notes (staff spaces).
    /// LILYPOND-REF: define-grobs.scm: outer-tie-vertical-gap = 0.25
    /// </summary>
    public double OuterTieVerticalGap { get; init; } = 0.25;

    /// <summary>
    /// Penalty factor for asymmetric outer tie lengths.
    /// LILYPOND-REF: define-grobs.scm: outer-tie-length-symmetry-penalty-factor = 10
    /// </summary>
    public double OuterTieLengthSymmetryPenaltyFactor { get; init; } = 10.0;

    /// <summary>
    /// Penalty factor for asymmetric outer tie vertical distances.
    /// LILYPOND-REF: define-grobs.scm: outer-tie-vertical-distance-symmetry-penalty-factor = 10
    /// </summary>
    public double OuterTieVerticalDistanceSymmetryPenaltyFactor { get; init; } = 10.0;
}
