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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Represents a candidate slur configuration for scoring.
/// </summary>
internal sealed class SlurCandidate : IScorableConfig
{
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double Height { get; set; }
    public bool CurveUp { get; set; }
    public double Demerits { get; set; }

    /// <summary>
    /// Scorer progress for lazy evaluation (priority-queue optimization).
    /// LILYPOND-REF: lily/include/slur-configuration.hh Slur_scorers enum
    /// </summary>
    public int NextScorerTodo { get; set; } = 1; // Start after INITIAL_SCORE
    public bool IsDone => NextScorerTodo >= 5; // NUM_SCORERS
}

/// <summary>
/// Represents an obstacle that a slur should avoid.
/// </summary>
internal readonly record struct SlurObstacle(
    double X,
    double TopY,
    double BottomY,
    SlurObstacleType Type);

/// <summary>
/// Types of obstacles for slur avoidance.
/// </summary>
internal enum SlurObstacleType
{
    NoteHead,
    Stem,
    Accidental,
    Articulation
}

/// <summary>
/// Solves the slur positioning problem using LilyPond's priority-queue
/// scoring approach with lazy scorer evaluation.
/// Scorers (in LilyPond order):
///   1. SLOPE - slope penalties
///   2. ENCOMPASS - note head/stem encompass + variance
///   3. EXTRA_ENCOMPASS - staff lines, accidentals, ties
///   4. EDGES - edge attraction
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/slur-scoring.cc:1-906 Slur_scoring class
/// LILYPOND-REF: lily/slur-configuration.cc:1-558 Slur_configuration class
/// LILYPOND-REF: lily/misc.cc:39-46 peak_around()
/// </remarks>
/// <summary>
/// The slur-edge note facts the scorer needs, per endpoint.
/// LILYPOND-REF: lily/slur-scoring.cc Slur_score_state — extremes_[d].stem_ / stem_dir_,
/// edge_has_beams_, and Stem::get_beaming(stem, -d) (beamed on the INNER side, toward the
/// other endpoint).
/// </summary>
internal readonly record struct SlurEdgeInfo(bool HasStem, bool StemUp, bool BeamedInner, bool Beamed);

internal sealed class SlurScoringProblem
{
    private readonly SlurItem _slur;
    private readonly double _startX;
    private readonly double _startY;
    private readonly double _endX;
    private readonly double _endY;
    private readonly SlurScoreParameters _parameters;
    private readonly IReadOnlyList<SlurObstacle>? _obstacles;
    private readonly IReadOnlyList<SlurLayout>? _existingSlurs;
    private readonly bool _isBrokenLeft;
    private readonly bool _isBrokenRight;
    private readonly SlurEdgeInfo _leftEdge;
    private readonly SlurEdgeInfo _rightEdge;
    private readonly bool _edgeHasBeams;

    // Musical dy: pitch difference in staff spaces
    private readonly double _musicalDy;

    // The five staff lines in this scorer's Y-up frame (yUp = -yDevice, absolute
    // page). LilyPond avoids staff lines in STAFF-RELATIVE positions
    // (slur-configuration.cc on_staff_line); our config Y is absolute-page, so the
    // lines must be placed absolutely too — a fixed {0,-1,-2,-3,-4} was only right
    // for a staff whose middle sits at device Y=2 (single top staff), silently
    // disabling the penalty on every other staff. Built from staffMiddleY below.
    private readonly double[] _staffLinePositions;

    public SlurScoringProblem(
        SlurItem slur,
        double startX,
        double startY,
        double endX,
        double endY,
        double staffMiddleY,
        SlurScoreParameters? parameters = null,
        IReadOnlyList<SlurObstacle>? obstacles = null,
        IReadOnlyList<SlurLayout>? existingSlurs = null,
        bool isBrokenLeft = false,
        bool isBrokenRight = false,
        SlurEdgeInfo leftEdge = default,
        SlurEdgeInfo rightEdge = default)
    {
        // Internal vertical frame: LilyPond's native Y-up. We obtain it from
        // the device frame (Y-down) by exact negation (yUp = -yDevice), so the
        // scorers below read sign-for-sign against lily/slur-configuration.cc
        // (dir = up ? +1 : -1, peak = mid + height, encompass dir*(slur - head)
        // < 0, ...). Negation is exact in IEEE, so the round-trip is
        // byte-neutral; CreateLayout negates the result back to device.
        _slur = slur;
        _startX = startX;
        _startY = -startY;
        _endX = endX;
        _endY = -endY;
        _parameters = parameters ?? SlurScoreParameters.Default;
        _existingSlurs = existingSlurs;
        _isBrokenLeft = isBrokenLeft;
        _isBrokenRight = isBrokenRight;
        // A broken edge is an artificial break point — no real stem/beam there.
        _leftEdge = isBrokenLeft ? default : leftEdge;
        _rightEdge = isBrokenRight ? default : rightEdge;
        _edgeHasBeams = _leftEdge.Beamed || _rightEdge.Beamed;

        // Reflect obstacle extents into the Y-up frame (negate both edges; the
        // TopY field stays the visual top edge, now the numerically larger one).
        if (obstacles != null)
        {
            var reflected = new List<SlurObstacle>(obstacles.Count);
            foreach (var o in obstacles)
                reflected.Add(new SlurObstacle(o.X, -o.TopY, -o.BottomY, o.Type));
            _obstacles = reflected;
        }
        else
        {
            _obstacles = null;
        }

        // Musical dy in the Y-up frame: higher pitch = larger Y.
        // LILYPOND-REF: lily/slur-scoring.cc:334-341
        _musicalDy = (slur.EndStaffPosition - slur.StartStaffPosition) / 2.0;

        // The 5 staff lines in this scorer's frame (yUp = -yDevice). A line at
        // staff position p sits at device staffMiddleY - p/2, i.e. at -(that) here.
        // Lines are at p in {4,2,0,-2,-4} -> offsets {2,1,0,-1,-2} ss above middle.
        _staffLinePositions = new double[5];
        for (int i = 0; i < 5; i++)
            _staffLinePositions[i] = -staffMiddleY + (2.0 - i);
    }

    // ---------------------------------------------------------------
    // Helper functions
    // ---------------------------------------------------------------

    // Bow arc height / control-point indent: the shared bezier-bow math, bound to
    // this slur's height-limit and ratio. See BezierBow (LilyPond bezier-bow.cc).
    private double CalculateSlurHeight(double width) =>
        BezierBow.Height(_parameters.HeightLimit, _parameters.Ratio, width);

    private double CalculateIndent(double width) =>
        BezierBow.Indent(_parameters.HeightLimit, width);

    /// <summary>
    /// Interpolates slur Y at parameter t using parabolic arc.
    /// </summary>
    private static double InterpolateSlurY(double startY, double endY, double height, bool curveUp, double t)
    {
        double linearY = startY + t * (endY - startY);
        double arc = 4 * height * t * (1 - t);
        // Y-up: an up slur peaks ABOVE the chord line (larger Y).
        return curveUp ? linearY + arc : linearY - arc;
    }

    // ---------------------------------------------------------------
    // Solving (priority-queue with lazy evaluation)
    // ---------------------------------------------------------------

    /// <summary>
    /// Solves for the optimal slur layout using LilyPond's priority-queue approach.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:438-459 get_best_curve()
    /// </remarks>
    public SlurLayout Solve()
    {
        double width = _endX - _startX;
        if (width < 1.0)
            width = 1.0;

        var candidates = GenerateCandidates(width);

        // Priority queue: lazy evaluation of scorers
        // LILYPOND-REF: lily/slur-scoring.cc:438-459
        var best = BestFirstScorer.Solve(candidates, RunNextScorer);

        return CreateLayout(best);
    }

    /// <summary>
    /// Runs the next scorer on a configuration.
    /// Scorer order matches LilyPond (cheap before expensive):
    /// SLOPE → EDGES → EXTRA_ENCOMPASS → ENCOMPASS.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-configuration.cc:531-549 run_next_scorer()
    /// LILYPOND-REF: lily/include/slur-configuration.hh:43-51 Slur_scorers enum
    /// Order: INITIAL_SCORE, SLOPE, EDGES, EXTRA_ENCOMPASS, ENCOMPASS
    /// </remarks>
    private void RunNextScorer(SlurCandidate config)
    {
        switch (config.NextScorerTodo)
        {
            case 1: // SLOPE
                ScoreSlopes(config);
                break;
            case 2: // EDGES
                ScoreEdges(config);
                break;
            case 3: // EXTRA_ENCOMPASS
                ScoreExtraEncompass(config);
                break;
            case 4: // ENCOMPASS
                ScoreEncompass(config);
                break;
        }
        config.NextScorerTodo++;
    }

    // ---------------------------------------------------------------
    // Candidate generation
    // ---------------------------------------------------------------

    /// <summary>
    /// Generates candidate slur configurations on a grid.
    /// LilyPond enumerates attachment points at half-staff-space intervals
    /// within a region.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:722-804 enumerate_attachments()
    /// </remarks>
    private List<SlurCandidate> GenerateCandidates(double width)
    {
        var candidates = new List<SlurCandidate>();

        double baseHeight = CalculateSlurHeight(width);
        bool preferUp = _slur.CurveUp;
        // Y-up: an up slur sits ABOVE its notes, so attachments move to larger Y.
        int dir = preferUp ? 1 : -1;

        // Base attachment offset from notes
        double offset = 0.3; // staff spaces
        double baseStartY = preferUp ? _startY + offset : _startY - offset;
        double baseEndY = preferUp ? _endY + offset : _endY - offset;

        // Generate grid: regionSize steps at 0.5 staff-space intervals
        // LILYPOND-REF: lily/slur-scoring.cc:739-740
        int regionSize = _parameters.RegionSize;
        double step = 0.5; // half staff space

        for (int i = 0; i <= regionSize; i++)
        {
            double leftY = baseStartY + i * dir * step;

            for (int j = 0; j <= regionSize; j++)
            {
                double rightY = baseEndY + j * dir * step;

                double adjustedStartX = _startX + _parameters.FreeHeadDistance;
                double adjustedEndX = _endX - _parameters.FreeHeadDistance;
                double dz_x = adjustedEndX - adjustedStartX;
                double dz_y = rightY - leftY;

                // Skip if too short or too steep
                // LILYPOND-REF: lily/slur-scoring.cc:770-775
                if (dz_x < 1.0)
                    continue;
                if (Math.Abs(dz_y / dz_x) > _parameters.MaxSlope)
                    continue;

                var candidate = new SlurCandidate
                {
                    StartX = adjustedStartX,
                    StartY = leftY,
                    EndX = adjustedEndX,
                    EndY = rightY,
                    Height = baseHeight,
                    CurveUp = preferUp,
                    Demerits = 0,
                    NextScorerTodo = 1
                };
                candidates.Add(candidate);
            }
        }

        // Always include default configuration
        if (candidates.Count == 0)
        {
            candidates.Add(new SlurCandidate
            {
                StartX = _startX + _parameters.FreeHeadDistance,
                StartY = baseStartY,
                EndX = _endX - _parameters.FreeHeadDistance,
                EndY = baseEndY,
                Height = baseHeight,
                CurveUp = preferUp,
                Demerits = 0,
                NextScorerTodo = 1
            });
        }

        return candidates;
    }

    // ---------------------------------------------------------------
    // Scorer 1: SLOPE
    // ---------------------------------------------------------------

    /// <summary>
    /// Penalizes non-horizontal slurs, steep slopes, and slope direction mismatches.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-configuration.cc:490-529 score_slopes()
    /// </remarks>
    private void ScoreSlopes(SlurCandidate config)
    {
        double slurDx = config.EndX - config.StartX;
        if (slurDx < 0.001)
            return;

        double slurDy = config.EndY - config.StartY;
        double demerit = 0.0;

        // Max slope penalty
        // LILYPOND-REF: slur-configuration.cc:499-501
        demerit += Math.Max(Math.Abs(slurDy / slurDx) - _parameters.MaxSlope, 0.0)
                   * _parameters.MaxSlopeFactor;

        // Broken slurs (split at a line break) skip the musical-slope penalties:
        // their attachment points are artificial at the break edge.
        // LILYPOND-REF: slur-configuration.cc:505-521 (!state.is_broken_ gates)
        bool isBroken = _isBrokenLeft || _isBrokenRight;

        // Steeper than musical indication
        // LILYPOND-REF: slur-configuration.cc:501-507
        double maxDy = Math.Abs(_musicalDy) + 0.2; // 0.2: account for staffline offset
        // LILYPOND-REF: slur-configuration.cc:502-503 — a beamed edge lets the slur be one
        // more staff-space steeper before the steepness penalty bites.
        if (_edgeHasBeams)
            maxDy += 1.0;
        if (!isBroken)
            demerit += _parameters.SteeperSlopeFactor
                       * Math.Max(Math.Abs(slurDy) - maxDy, 0.0);

        // Max slope penalty (applied twice in LilyPond)
        // LILYPOND-REF: slur-configuration.cc:509-513
        demerit += Math.Max(Math.Abs(slurDy / slurDx) - _parameters.MaxSlope, 0.0)
                   * _parameters.MaxSlopeFactor;

        // Non-horizontal penalty: if notes are at same pitch but slur is tilted
        // LILYPOND-REF: slur-configuration.cc:515-518
        if (Math.Abs(_musicalDy) < 0.01 && Math.Abs(slurDy) > 0.01 && !isBroken)
            demerit += _parameters.NonHorizontalPenalty;

        // Same direction penalty: slur slopes opposite to note movement
        // LILYPOND-REF: slur-configuration.cc:519-523 — a beamed edge softens this to 1/10
        // (the beam already constrains the endpoint, so the slope is largely forced).
        if (Math.Abs(_musicalDy) > 0.01 && Math.Abs(slurDy) > 0.01 && !isBroken
            && Math.Sign(slurDy) != Math.Sign(_musicalDy))
        {
            demerit += _edgeHasBeams
                ? _parameters.SameSlopePenalty / 10.0
                : _parameters.SameSlopePenalty;
        }

        config.Demerits += demerit;
    }

    // ---------------------------------------------------------------
    // Scorer 2: EDGES
    // ---------------------------------------------------------------

    /// <summary>
    /// Penalizes attachment points far from base positions,
    /// with exponential slope factor.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-configuration.cc:464-488 score_edges()
    /// </remarks>
    private void ScoreEdges(SlurCandidate config)
    {
        double slurDx = config.EndX - config.StartX;
        if (slurDx < 0.001)
            return;

        double slope = (config.EndY - config.StartY) / slurDx;
        double factor = _parameters.EdgeAttractionFactor;
        int dir = config.CurveUp ? 1 : -1;

        // Left edge
        {
            double dy = Math.Abs(config.StartY - _startY);
            double demerit = factor * dy;
            // LILYPOND-REF: slur-configuration.cc:473-477 — when the edge note's stem points
            // the SAME way as the slur and is not beamed on its inner side, the endpoint can
            // slide freely along the stem, so the attraction penalty is 5x weaker.
            if (_leftEdge.HasStem && _leftEdge.StemUp == config.CurveUp && !_leftEdge.BeamedInner)
                demerit /= 5.0;
            // Exponential slope factor
            // LILYPOND-REF: slur-configuration.cc:478-479
            demerit *= Math.Exp(dir * (-1) * slope * _parameters.EdgeSlopeExponent);
            config.Demerits += demerit;
        }

        // Right edge
        {
            double dy = Math.Abs(config.EndY - _endY);
            double demerit = factor * dy;
            // LILYPOND-REF: slur-configuration.cc:473-477 (stem slurward, not inner-beamed).
            if (_rightEdge.HasStem && _rightEdge.StemUp == config.CurveUp && !_rightEdge.BeamedInner)
                demerit /= 5.0;
            demerit *= Math.Exp(dir * 1 * slope * _parameters.EdgeSlopeExponent);
            config.Demerits += demerit;
        }
    }

    // ---------------------------------------------------------------
    // Scorer 3: EXTRA_ENCOMPASS (staff lines, extra objects)
    // ---------------------------------------------------------------

    /// <summary>
    /// Scores collision with staff lines and extra objects (accidentals, etc.)
    /// using peak_around for smooth penalties.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-configuration.cc:351-462 score_extra_encompass()
    /// </remarks>
    private void ScoreExtraEncompass(SlurCandidate config)
    {
        double demerit = 0.0;

        // Staff line collision at endpoints and peak
        double gapInside = _parameters.GapToStafflineInside;
        double gapOutside = _parameters.GapToStafflineOutside;

        // Check endpoints against staff lines
        foreach (double lineY in _staffLinePositions)
        {
            double startDist = Math.Abs(config.StartY - lineY);
            if (startDist < gapInside)
            {
                demerit += _parameters.ExtraObjectCollisionPenalty
                           * BezierBow.PeakAround(0.1 * gapInside, gapInside, startDist);
            }

            double endDist = Math.Abs(config.EndY - lineY);
            if (endDist < gapInside)
            {
                demerit += _parameters.ExtraObjectCollisionPenalty
                           * BezierBow.PeakAround(0.1 * gapInside, gapInside, endDist);
            }
        }

        // Check peak against staff lines
        double midY = (config.StartY + config.EndY) / 2;
        double peakY = config.CurveUp ? midY + config.Height : midY - config.Height;

        foreach (double lineY in _staffLinePositions)
        {
            double peakDist = Math.Abs(peakY - lineY);
            if (peakDist < gapOutside)
            {
                demerit += _parameters.ExtraObjectCollisionPenalty * 0.5
                           * BezierBow.PeakAround(0.1 * gapOutside, gapOutside, peakDist);
            }
        }

        // Slur-slur collision
        if (_existingSlurs != null)
        {
            foreach (var existing in _existingSlurs)
            {
                bool xOverlap = !(config.EndX < existing.StartX || config.StartX > existing.EndX);
                if (!xOverlap)
                    continue;

                // Existing slurs are now stored in the same page Y-up frame this
                // scorer works in, so use their control Y directly (no reflection).
                double existingPeakY = (existing.Control1.Y + existing.Control2.Y) / 2;
                double dist = Math.Abs(peakY - existingPeakY);

                demerit += _parameters.ExtraObjectCollisionPenalty
                           * BezierBow.PeakAround(
                               0.1 * _parameters.ExtraEncompassFreeDistance,
                               _parameters.ExtraEncompassFreeDistance,
                               dist);
            }
        }

        config.Demerits += demerit;
    }

    // ---------------------------------------------------------------
    // Scorer 4: ENCOMPASS (note heads and stems)
    // ---------------------------------------------------------------

    /// <summary>
    /// Scores note head encompass and stem encompass with LilyPond's
    /// 1/distance head penalty and variance-based uniformity penalty.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-configuration.cc:235-349 score_encompass()
    /// </remarks>
    private void ScoreEncompass(SlurCandidate config)
    {
        if (_obstacles == null || _obstacles.Count == 0)
            return;

        double width = config.EndX - config.StartX;
        if (width < 0.001)
            return;

        double demerit = 0.0;
        var convexHeadDistances = new List<double>();
        int dir = config.CurveUp ? 1 : -1;

        for (int j = 0; j < _obstacles.Count; j++)
        {
            var obstacle = _obstacles[j];

            // Check if obstacle is within slur X range
            if (obstacle.X <= config.StartX || obstacle.X >= config.EndX)
                continue;

            // Calculate slur Y at obstacle X
            double t = (obstacle.X - config.StartX) / width;
            double slurY = InterpolateSlurY(config.StartY, config.EndY, config.Height, config.CurveUp, t);

            bool isEdge = j == 0 || j == _obstacles.Count - 1;

            if (!isEdge && obstacle.Type == SlurObstacleType.NoteHead)
            {
                // Head encompass scoring
                // LILYPOND-REF: slur-configuration.cc:260-291
                double headY = config.CurveUp ? obstacle.TopY : obstacle.BottomY;
                double headDy = slurY - headY;

                if (dir * headDy < 0)
                {
                    // Slur is below head (for up) or above (for down) = encompassed
                    demerit += _parameters.HeadEncompassPenalty;
                    convexHeadDistances.Add(0.0);
                }
                else
                {
                    // 1/distance penalty with free_head_distance threshold
                    double absHeadDy = Math.Abs(headDy);
                    double hd = (absHeadDy > 0.001)
                        ? (1.0 / absHeadDy - 1.0 / _parameters.FreeHeadDistance)
                        : _parameters.HeadEncompassPenalty;
                    hd = Math.Clamp(hd, 0.0, _parameters.HeadEncompassPenalty);
                    demerit += hd;

                    // Track distance for variance calculation
                    double lineY = config.StartY + t * (config.EndY - config.StartY);
                    double closest = config.CurveUp
                        ? Math.Max(obstacle.TopY, lineY)
                        : Math.Min(obstacle.BottomY, lineY);
                    double d = Math.Abs(closest - slurY);
                    convexHeadDistances.Add(d);
                }
            }

            // Stem encompass
            // LILYPOND-REF: slur-configuration.cc:293-306
            if (obstacle.Type == SlurObstacleType.Stem)
            {
                double stemY = config.CurveUp ? obstacle.TopY : obstacle.BottomY;
                if (dir * (slurY - stemY) < 0)
                {
                    double stemDem = _parameters.StemEncompassPenalty;
                    // Reduce only at the edge whose stem points along the slur: the
                    // left edge of an UP slur, the right edge of a DOWN slur — NOT any
                    // edge. LILYPOND-REF: slur-configuration.cc:298 —
                    //   if ((l_edge && dir == UP) || (r_edge && dir == DOWN)) dem /= 5.
                    bool lEdge = j == 0, rEdge = j == _obstacles.Count - 1;
                    if ((lEdge && dir > 0) || (rEdge && dir < 0))
                        stemDem /= 5;
                    demerit += stemDem;
                }
            }

            // Accidental/articulation encompass
            if (obstacle.Type == SlurObstacleType.Accidental)
            {
                double objY = config.CurveUp ? obstacle.TopY : obstacle.BottomY;
                if (dir * (slurY - objY) < 0)
                    demerit += _parameters.AccidentalCollision;
            }
            else if (obstacle.Type == SlurObstacleType.Articulation)
            {
                double objY = config.CurveUp ? obstacle.TopY : obstacle.BottomY;
                if (dir * (slurY - objY) < 0)
                    demerit += _parameters.ExtraObjectCollisionPenalty;
            }
        }

        config.Demerits += demerit;

        // Variance penalty: penalize uneven spacing between slur and heads
        // LILYPOND-REF: slur-configuration.cc:307-349
        int n = convexHeadDistances.Count;
        if (n > 0)
        {
            double avgDistance = 0.0;
            double minDist = double.MaxValue;

            foreach (double d in convexHeadDistances)
            {
                minDist = Math.Min(minDist, d);
                avgDistance += d;
            }

            // For slurs over 3 or 4 heads, add height as smoothing
            // LILYPOND-REF: slur-configuration.cc:326-331
            if (n <= 2)
            {
                avgDistance += config.Height;
                n++;
            }

            avgDistance /= n;

            double variancePenalty = _parameters.HeadSlurDistanceMaxRatio;
            if (minDist > 0.0)
            {
                variancePenalty = Math.Min(
                    avgDistance / (minDist + _parameters.AbsoluteClosenessMeasure) - 1.0,
                    variancePenalty);
            }
            variancePenalty = Math.Max(variancePenalty, 0.0);
            variancePenalty *= _parameters.HeadSlurDistanceFactor;

            config.Demerits += variancePenalty;
        }
    }

    // ---------------------------------------------------------------
    // Layout creation
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a SlurLayout from the best candidate configuration.
    /// </summary>
    private SlurLayout CreateLayout(SlurCandidate config)
    {
        double width = config.EndX - config.StartX;
        double indent = CalculateIndent(width);

        // Y-up: an up slur's control points sit ABOVE the chord line (larger Y).
        double directedHeight = config.CurveUp ? config.Height : -config.Height;
        // Control points follow start-end line with arc height offset
        // LILYPOND-REF: lily/bezier-bow.cc flat bow + shear for dy
        double cpT1 = (width > 0) ? indent / width : 0;
        double cpT2 = (width > 0) ? 1 - indent / width : 1;
        var control1 = (X: config.StartX + indent, Y: config.StartY + cpT1 * (config.EndY - config.StartY) + directedHeight);
        var control2 = (X: config.EndX - indent, Y: config.StartY + cpT2 * (config.EndY - config.StartY) + directedHeight);

        // Store the scored Y verbatim in the page Y-up frame — no exit negation.
        // The scorer already reasons in Y-up (slur-scoring.cc), and BowLayout keeps
        // that frame; DrawBow flips to device once (os.Y(-YUp, …)).
        return new SlurLayout(
            _slur,
            config.StartX,
            config.StartY,
            config.EndX,
            config.EndY,
            (control1.X, control1.Y),
            (control2.X, control2.Y),
            isBrokenLeft: _isBrokenLeft,
            isBrokenRight: _isBrokenRight);
    }
}
