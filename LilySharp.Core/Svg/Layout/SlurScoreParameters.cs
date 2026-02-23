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
/// Parameters for slur scoring.
/// Values from layout-slur.scm (default-slur-details) define all scoring parameters.
/// Shape parameters (height-limit, ratio) come from the Slur grob definition.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/slur-score-parameters.hh:26-56 Slur_score_parameters struct
/// LILYPOND-REF: scm/layout-slur.scm:19-45 default-slur-details
/// </remarks>
public sealed record SlurScoreParameters
{
    /// <summary>Default parameters matching LilyPond defaults.</summary>
    public static SlurScoreParameters Default { get; } = new();

    // --- Shape parameters (from Slur grob, not details) ---

    /// <summary>
    /// Maximum height limit (staff spaces).
    /// LILYPOND-REF: define-grobs.scm Slur.height-limit = 2.0
    /// </summary>
    public double HeightLimit { get; init; } = 2.0;

    /// <summary>
    /// Height ratio for slur shape.
    /// LILYPOND-REF: define-grobs.scm Slur.ratio = 0.25
    /// Note: PhrasingSlur uses 0.333
    /// </summary>
    public double Ratio { get; init; } = 0.25;

    // --- Search parameters ---

    /// <summary>
    /// Search region size (grid points in each direction).
    /// LILYPOND-REF: layout-slur.scm: region-size = 4
    /// </summary>
    public int RegionSize { get; init; } = 4;

    // --- Encompass penalties ---

    /// <summary>
    /// Penalty for encompassing note heads.
    /// LILYPOND-REF: layout-slur.scm: head-encompass-penalty = 1000.0
    /// </summary>
    public double HeadEncompassPenalty { get; init; } = 1000.0;

    /// <summary>
    /// Penalty for encompassing stems.
    /// LILYPOND-REF: layout-slur.scm: stem-encompass-penalty = 30.0
    /// </summary>
    public double StemEncompassPenalty { get; init; } = 30.0;

    // --- Edge parameters ---

    /// <summary>
    /// Factor for edge attraction.
    /// LILYPOND-REF: layout-slur.scm: edge-attraction-factor = 4
    /// </summary>
    public double EdgeAttractionFactor { get; init; } = 4.0;

    /// <summary>
    /// Exponent for edge slope effect on edge attraction.
    /// LILYPOND-REF: layout-slur.scm: edge-slope-exponent = 1.7
    /// </summary>
    public double EdgeSlopeExponent { get; init; } = 1.7;

    /// <summary>
    /// Length threshold for close-to-edge detection.
    /// LILYPOND-REF: layout-slur.scm: close-to-edge-length = 2.5
    /// </summary>
    public double CloseToEdgeLength { get; init; } = 2.5;

    // --- Slope penalties ---

    /// <summary>
    /// Penalty for same slope as staff (musical direction mismatch).
    /// LILYPOND-REF: layout-slur.scm: same-slope-penalty = 20
    /// </summary>
    public double SameSlopePenalty { get; init; } = 20.0;

    /// <summary>
    /// Factor for steeper slopes than musical indication.
    /// LILYPOND-REF: layout-slur.scm: steeper-slope-factor = 50
    /// </summary>
    public double SteeperSlopeFactor { get; init; } = 50.0;

    /// <summary>
    /// Penalty for non-horizontal slurs when notes are at same pitch.
    /// LILYPOND-REF: layout-slur.scm: non-horizontal-penalty = 15
    /// </summary>
    public double NonHorizontalPenalty { get; init; } = 15.0;

    /// <summary>
    /// Maximum allowed slope.
    /// LILYPOND-REF: layout-slur.scm: max-slope = 1.1
    /// </summary>
    public double MaxSlope { get; init; } = 1.1;

    /// <summary>
    /// Factor for max slope penalty.
    /// LILYPOND-REF: layout-slur.scm: max-slope-factor = 10
    /// </summary>
    public double MaxSlopeFactor { get; init; } = 10.0;

    // --- Extra object collision ---

    /// <summary>
    /// Penalty for collision with extra objects (accidentals, articulations, etc.).
    /// LILYPOND-REF: layout-slur.scm: extra-object-collision-penalty = 50
    /// </summary>
    public double ExtraObjectCollisionPenalty { get; init; } = 50.0;

    /// <summary>
    /// Penalty for accidental collision.
    /// LILYPOND-REF: layout-slur.scm: accidental-collision = 3
    /// </summary>
    public double AccidentalCollision { get; init; } = 3.0;

    /// <summary>
    /// Free distance for extra encompass collision detection.
    /// LILYPOND-REF: layout-slur.scm: extra-encompass-free-distance = 0.3
    /// </summary>
    public double ExtraEncompassFreeDistance { get; init; } = 0.3;

    /// <summary>
    /// Collision distance for extra encompass objects.
    /// LILYPOND-REF: layout-slur.scm: extra-encompass-collision-distance = 0.8
    /// </summary>
    public double ExtraEncompassCollisionDistance { get; init; } = 0.8;

    // --- Distance parameters ---

    /// <summary>
    /// Free distance for slur positioning.
    /// LILYPOND-REF: layout-slur.scm: free-slur-distance = 0.8
    /// </summary>
    public double FreeSlurDistance { get; init; } = 0.8;

    /// <summary>
    /// Free distance from note heads.
    /// LILYPOND-REF: layout-slur.scm: free-head-distance = 0.3
    /// </summary>
    public double FreeHeadDistance { get; init; } = 0.3;

    /// <summary>
    /// Maximum ratio for head-slur distance variance penalty.
    /// LILYPOND-REF: layout-slur.scm: head-slur-distance-max-ratio = 3
    /// </summary>
    public double HeadSlurDistanceMaxRatio { get; init; } = 3.0;

    /// <summary>
    /// Factor for head-slur distance penalty.
    /// LILYPOND-REF: layout-slur.scm: head-slur-distance-factor = 10
    /// </summary>
    public double HeadSlurDistanceFactor { get; init; } = 10.0;

    /// <summary>
    /// Measure for absolute closeness.
    /// LILYPOND-REF: layout-slur.scm: absolute-closeness-measure = 0.3
    /// </summary>
    public double AbsoluteClosenessMeasure { get; init; } = 0.3;

    // --- Staff line gaps ---

    /// <summary>
    /// Gap to staff line (inside, for endpoints).
    /// LILYPOND-REF: layout-slur.scm: gap-to-staffline-inside = 0.2
    /// </summary>
    public double GapToStafflineInside { get; init; } = 0.2;

    /// <summary>
    /// Gap to staff line (outside, for slur body).
    /// LILYPOND-REF: layout-slur.scm: gap-to-staffline-outside = 0.1
    /// </summary>
    public double GapToStafflineOutside { get; init; } = 0.1;

    // --- Miscellaneous ---

    /// <summary>
    /// Range overshoot for encompass object detection.
    /// LILYPOND-REF: layout-slur.scm: encompass-object-range-overshoot = 0.5
    /// </summary>
    public double EncompassObjectRangeOvershoot { get; init; } = 0.5;

    /// <summary>
    /// Minimum distance from slur extrema to tie attachment.
    /// LILYPOND-REF: layout-slur.scm: slur-tie-extrema-min-distance = 0.2
    /// </summary>
    public double SlurTieExtremaMinDistance { get; init; } = 0.2;

    /// <summary>
    /// Penalty when slur extrema too close to tie.
    /// LILYPOND-REF: layout-slur.scm: slur-tie-extrema-min-distance-penalty = 2
    /// </summary>
    public double SlurTieExtremaMinDistancePenalty { get; init; } = 2.0;
}
