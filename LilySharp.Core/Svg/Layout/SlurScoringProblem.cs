using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Represents a candidate slur configuration for scoring.
/// </summary>
public sealed class SlurCandidate
{
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double Height { get; set; }
    public bool CurveUp { get; set; }
    public double YOffset { get; set; }
    public double Demerits { get; set; }

    public SlurCandidate Clone() => new()
    {
        StartX = StartX,
        StartY = StartY,
        EndX = EndX,
        EndY = EndY,
        Height = Height,
        CurveUp = CurveUp,
        YOffset = YOffset,
        Demerits = Demerits
    };
}

/// <summary>
/// Represents an obstacle that a slur should avoid.
/// </summary>
public readonly record struct SlurObstacle(
    double X,
    double TopY,
    double BottomY,
    SlurObstacleType Type);

/// <summary>
/// Types of obstacles for slur avoidance.
/// </summary>
public enum SlurObstacleType
{
    NoteHead,
    Stem,
    Accidental,
    Articulation
}

/// <summary>
/// Solves the slur positioning problem by finding optimal positions that avoid collisions.
/// Based on LilyPond's Slur_scoring from slur-scoring.cc
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/slur-scoring.cc:1-906 Slur_scoring class
/// </remarks>
public sealed class SlurScoringProblem
{
    private readonly SlurItem _slur;
    private readonly double _startX;
    private readonly double _startY;
    private readonly double _endX;
    private readonly double _endY;
    private readonly SlurScoreParameters _parameters;
    private readonly IReadOnlyList<SlurObstacle>? _obstacles;
    private readonly IReadOnlyList<SlurLayout>? _existingSlurs;
    private readonly double _staffHeight;

    // Staff line positions (in staff spaces from top line)
    private static readonly double[] StaffLinePositions = { 0, 1, 2, 3, 4 };

    public SlurScoringProblem(
        SlurItem slur,
        double startX,
        double startY,
        double endX,
        double endY,
        SlurScoreParameters? parameters = null,
        IReadOnlyList<SlurObstacle>? obstacles = null,
        IReadOnlyList<SlurLayout>? existingSlurs = null,
        double staffHeight = 4.0)
    {
        _slur = slur;
        _startX = startX;
        _startY = startY;
        _endX = endX;
        _endY = endY;
        _parameters = parameters ?? SlurScoreParameters.Default;
        _obstacles = obstacles;
        _existingSlurs = existingSlurs;
        _staffHeight = staffHeight;
    }

    /// <summary>
    /// Solves for the optimal slur layout.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:700-800 score_encompass()
    /// </remarks>
    public SlurLayout Solve()
    {
        // Calculate base dimensions
        double width = _endX - _startX;
        if (width < 1.0)
            width = 1.0;

        // Generate candidate configurations
        var candidates = GenerateCandidates(width);

        // Score all candidates
        foreach (var config in candidates)
        {
            ScoreConfiguration(config);
        }

        // Find best configuration (lowest demerits)
        var best = candidates.MinBy(c => c.Demerits) ?? candidates[0];

        // Convert to SlurLayout
        return CreateLayout(best);
    }

    /// <summary>
    /// Generates candidate slur configurations.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:200-350 generate_curves()
    /// </remarks>
    private List<SlurCandidate> GenerateCandidates(double width)
    {
        var candidates = new List<SlurCandidate>();

        // Calculate base height
        double baseHeight = CalculateSlurHeight(width);
        bool preferUp = _slur.CurveUp;

        // Y offset variations to try (in staff spaces)
        double[] yOffsets = { 0, 0.2, 0.4, -0.2, -0.4, 0.6, -0.6, 0.8, -0.8 };

        // Height variations (percentage of base height)
        double[] heightFactors = { 1.0, 0.8, 1.2, 0.6, 1.4, 0.5, 1.6 };

        foreach (var yOffset in yOffsets)
        {
            foreach (var heightFactor in heightFactors)
            {
                var candidate = new SlurCandidate
                {
                    StartX = _startX + _parameters.FreeHeadDistance,
                    StartY = _startY,
                    EndX = _endX - _parameters.FreeHeadDistance,
                    EndY = _endY,
                    Height = baseHeight * heightFactor,
                    CurveUp = preferUp,
                    YOffset = yOffset,
                    Demerits = 0
                };
                candidates.Add(candidate);

                // Also try opposite direction for ambiguous cases
                if (Math.Abs(_startY - _staffHeight / 2) < 1.5 &&
                    Math.Abs(_endY - _staffHeight / 2) < 1.5)
                {
                    var opposite = candidate.Clone();
                    opposite.CurveUp = !preferUp;
                    opposite.YOffset = -yOffset;
                    candidates.Add(opposite);
                }
            }
        }

        return candidates;
    }

    /// <summary>
    /// Scores a slur configuration against multiple objectives.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:400-700 score_configuration()
    /// </remarks>
    private void ScoreConfiguration(SlurCandidate config)
    {
        config.Demerits = 0;

        // Score staff line clearance
        ScoreStaffLineClearance(config);

        // Score slope (prefer horizontal)
        ScoreSlope(config);

        // Score height preference
        ScoreHeightPreference(config);

        // Score direction preference
        ScoreDirectionPreference(config);

        // Score edge attraction
        ScoreEdgeAttraction(config);

        // Score obstacle avoidance (note heads, stems, etc.)
        ScoreObstacleAvoidance(config);

        // Score slur-slur collision
        ScoreSlurSlurCollision(config);
    }

    /// <summary>
    /// Penalizes slurs that cross staff lines at tips.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:500-550 score_extra_encompass()
    /// </remarks>
    private void ScoreStaffLineClearance(SlurCandidate config)
    {
        double baseStartY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3
            : config.StartY + config.YOffset + 0.3;
        double baseEndY = config.CurveUp
            ? config.EndY - config.YOffset - 0.3
            : config.EndY + config.YOffset + 0.3;

        double gapInside = _parameters.GapToStafflineInside;
        double gapOutside = _parameters.GapToStafflineOutside;

        // Check both endpoints
        foreach (double lineY in StaffLinePositions)
        {
            // Start point clearance
            double startDist = Math.Abs(baseStartY - lineY);
            if (startDist < gapInside)
            {
                config.Demerits += _parameters.ExtraObjectCollisionPenalty *
                    (1 - startDist / gapInside);
            }

            // End point clearance
            double endDist = Math.Abs(baseEndY - lineY);
            if (endDist < gapInside)
            {
                config.Demerits += _parameters.ExtraObjectCollisionPenalty *
                    (1 - endDist / gapInside);
            }
        }

        // Also check peak of the slur
        double peakY = config.CurveUp
            ? (baseStartY + baseEndY) / 2 - config.Height
            : (baseStartY + baseEndY) / 2 + config.Height;

        foreach (double lineY in StaffLinePositions)
        {
            double peakDist = Math.Abs(peakY - lineY);
            if (peakDist < gapOutside)
            {
                config.Demerits += _parameters.ExtraObjectCollisionPenalty * 0.5 *
                    (1 - peakDist / gapOutside);
            }
        }
    }

    /// <summary>
    /// Penalizes non-horizontal slurs and steep slopes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:600-650
    /// </remarks>
    private void ScoreSlope(SlurCandidate config)
    {
        double width = config.EndX - config.StartX;
        if (width < 0.001)
            return;

        double slope = (config.EndY - config.StartY) / width;
        double absSlope = Math.Abs(slope);

        // Penalty for non-horizontal slurs
        if (absSlope > 0.01)
        {
            config.Demerits += _parameters.NonHorizontalPenalty * absSlope;
        }

        // Additional penalty for very steep slopes
        if (absSlope > _parameters.MaxSlope)
        {
            config.Demerits += _parameters.MaxSlopeFactor *
                (absSlope - _parameters.MaxSlope);
        }

        // Penalty if slur and notehead slope match (looks unnatural)
        double noteSlope = (_slur.EndStaffPosition - _slur.StartStaffPosition) /
            (2.0 * width);
        if (Math.Abs(slope - noteSlope) < 0.1)
        {
            config.Demerits += _parameters.SameSlopePenalty * 0.5;
        }
    }

    /// <summary>
    /// Penalizes extreme heights.
    /// </summary>
    private void ScoreHeightPreference(SlurCandidate config)
    {
        double width = config.EndX - config.StartX;
        double idealHeight = CalculateSlurHeight(width);

        double heightRatio = config.Height / idealHeight;

        // Penalize heights that are too small or too large
        if (heightRatio < 0.5)
        {
            config.Demerits += (0.5 - heightRatio) * 20.0;
        }
        else if (heightRatio > 1.5)
        {
            config.Demerits += (heightRatio - 1.5) * 15.0;
        }
    }

    /// <summary>
    /// Penalizes direction that conflicts with stem direction.
    /// </summary>
    private void ScoreDirectionPreference(SlurCandidate config)
    {
        if (config.CurveUp != _slur.CurveUp)
        {
            config.Demerits += _parameters.NonHorizontalPenalty;
        }
    }

    /// <summary>
    /// Rewards configurations where endpoints are close to noteheads.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:450-500 edge_attraction
    /// </remarks>
    private void ScoreEdgeAttraction(SlurCandidate config)
    {
        double baseStartY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3
            : config.StartY + config.YOffset + 0.3;
        double baseEndY = config.CurveUp
            ? config.EndY - config.YOffset - 0.3
            : config.EndY + config.YOffset + 0.3;

        // Penalize endpoints that are too far from original note positions
        double startDistance = Math.Abs(baseStartY - _startY);
        double endDistance = Math.Abs(baseEndY - _endY);

        if (startDistance > _parameters.FreeSlurDistance)
        {
            config.Demerits += _parameters.EdgeAttractionFactor *
                (startDistance - _parameters.FreeSlurDistance);
        }

        if (endDistance > _parameters.FreeSlurDistance)
        {
            config.Demerits += _parameters.EdgeAttractionFactor *
                (endDistance - _parameters.FreeSlurDistance);
        }
    }

    /// <summary>
    /// Penalizes configurations that encompass obstacles (noteheads, stems).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:550-600 score_encompass()
    /// </remarks>
    private void ScoreObstacleAvoidance(SlurCandidate config)
    {
        if (_obstacles == null || _obstacles.Count == 0)
            return;

        double baseStartY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3
            : config.StartY + config.YOffset + 0.3;
        double baseEndY = config.CurveUp
            ? config.EndY - config.YOffset - 0.3
            : config.EndY + config.YOffset + 0.3;

        double width = config.EndX - config.StartX;

        foreach (var obstacle in _obstacles)
        {
            // Check if obstacle is within slur X range
            if (obstacle.X < config.StartX || obstacle.X > config.EndX)
                continue;

            // Calculate slur Y at obstacle X
            double t = (obstacle.X - config.StartX) / width;
            double slurY = InterpolateSlurY(baseStartY, baseEndY, config.Height, config.CurveUp, t);

            // Check for collision
            bool collision = config.CurveUp
                ? slurY > obstacle.TopY  // For up curve, slur Y should be less than obstacle top
                : slurY < obstacle.BottomY;  // For down curve, slur Y should be more than obstacle bottom

            if (collision)
            {
                double penalty = obstacle.Type switch
                {
                    SlurObstacleType.NoteHead => _parameters.HeadEncompassPenalty,
                    SlurObstacleType.Stem => _parameters.StemEncompassPenalty,
                    SlurObstacleType.Accidental => _parameters.AccidentalCollision,
                    SlurObstacleType.Articulation => _parameters.ExtraObjectCollisionPenalty,
                    _ => 10.0
                };
                config.Demerits += penalty;
            }
        }
    }

    /// <summary>
    /// Penalizes slurs that overlap with existing slurs.
    /// </summary>
    private void ScoreSlurSlurCollision(SlurCandidate config)
    {
        if (_existingSlurs == null || _existingSlurs.Count == 0)
            return;

        double configMidX = (config.StartX + config.EndX) / 2;
        double baseY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3
            : config.StartY + config.YOffset + 0.3;
        double configPeakY = config.CurveUp
            ? baseY - config.Height
            : baseY + config.Height;

        foreach (var existing in _existingSlurs)
        {
            // Check if slurs overlap horizontally
            bool xOverlap = !(config.EndX < existing.StartX || config.StartX > existing.EndX);
            if (!xOverlap)
                continue;

            // Check vertical distance at peaks
            double existingPeakY = (existing.Control1.Y + existing.Control2.Y) / 2;
            double yDistance = Math.Abs(configPeakY - existingPeakY);

            double minDistance = _parameters.FreeSlurDistance;
            if (yDistance < minDistance)
            {
                config.Demerits += _parameters.ExtraObjectCollisionPenalty *
                    (1 - yDistance / minDistance);
            }
        }
    }

    /// <summary>
    /// Interpolates the Y position along the slur curve.
    /// Uses a simple parabolic approximation for the Bezier curve.
    /// </summary>
    private double InterpolateSlurY(double startY, double endY, double height, bool curveUp, double t)
    {
        // Linear interpolation for baseline
        double linearY = startY + t * (endY - startY);

        // Parabolic arc: 4*h*t*(1-t) peaks at t=0.5 with value h
        double arc = 4 * height * t * (1 - t);

        return curveUp ? linearY - arc : linearY + arc;
    }

    /// <summary>
    /// Calculates slur arc height based on width.
    /// Based on Lilypond's slur_height function in bezier-bow.cc
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/bezier-bow.cc:50-70 slur_height()
    /// </remarks>
    private double CalculateSlurHeight(double width)
    {
        if (_parameters.HeightLimit < 0.001)
            return 0;

        double x = _parameters.Ratio * width / _parameters.HeightLimit;
        return _parameters.HeightLimit * Math.Tanh(x);
    }

    /// <summary>
    /// Calculates indent for control points.
    /// </summary>
    private double CalculateIndent(double width)
    {
        double maxFraction = 1.0 / 3.1;
        double q = 2 * _parameters.HeightLimit / maxFraction;
        return 2 * _parameters.HeightLimit - q * q * maxFraction / (width + q);
    }

    /// <summary>
    /// Creates a SlurLayout from the best candidate configuration.
    /// </summary>
    private SlurLayout CreateLayout(SlurCandidate config)
    {
        double width = config.EndX - config.StartX;
        double indent = CalculateIndent(width);

        double directedHeight = config.CurveUp ? -config.Height : config.Height;
        double baseStartY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3
            : config.StartY + config.YOffset + 0.3;
        double baseEndY = config.CurveUp
            ? config.EndY - config.YOffset - 0.3
            : config.EndY + config.YOffset + 0.3;

        double midY = (baseStartY + baseEndY) / 2;

        var control1 = (X: config.StartX + indent, Y: midY + directedHeight);
        var control2 = (X: config.EndX - indent, Y: midY + directedHeight);

        return new SlurLayout(
            _slur,
            config.StartX,
            baseStartY,
            config.EndX,
            baseEndY,
            control1,
            control2);
    }
}