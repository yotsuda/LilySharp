using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Represents a candidate tie configuration for scoring.
/// </summary>
public sealed class TieCandidate
{
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double Height { get; set; }
    public bool CurveUp { get; set; }
    public double YOffset { get; set; }
    public double Demerits { get; set; }

    public TieCandidate Clone() => new()
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
/// Solves the tie positioning problem by finding optimal positions that avoid collisions.
/// Based on LilyPond's Tie_formatting_problem from tie-formatting-problem.cc
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tie-formatting-problem.cc:1-1286 Tie_formatting_problem class
/// </remarks>
public sealed class TieFormattingProblem
{
    private readonly TieItem _tie;
    private readonly double _startX;
    private readonly double _startY;
    private readonly double _endX;
    private readonly double _endY;
    private readonly TieDetails _details;
    private readonly IReadOnlyList<TieLayout>? _existingTies;
    private readonly double _staffHeight;

    // Staff line positions (in staff spaces from top line)
    private static readonly double[] StaffLinePositions = { 0, 1, 2, 3, 4 };

    public TieFormattingProblem(
        TieItem tie,
        double startX,
        double startY,
        double endX,
        double endY,
        TieDetails? details = null,
        IReadOnlyList<TieLayout>? existingTies = null,
        double staffHeight = 4.0)
    {
        _tie = tie;
        _startX = startX;
        _startY = startY;
        _endX = endX;
        _endY = endY;
        _details = details ?? TieDetails.Default;
        _existingTies = existingTies;
        _staffHeight = staffHeight;
    }

    /// <summary>
    /// Solves for the optimal tie layout.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:970-1050 solve()
    /// </remarks>
    public TieLayout Solve()
    {
        // Calculate base dimensions
        double width = _endX - _startX;
        if (width < _details.MinLength)
            width = _details.MinLength;

        // Generate candidate configurations
        var candidates = GenerateCandidates(width);

        // Score all candidates
        foreach (var config in candidates)
        {
            ScoreConfiguration(config);
        }

        // Find best configuration (lowest demerits)
        var best = candidates.MinBy(c => c.Demerits) ?? candidates[0];

        // Convert to TieLayout
        return CreateLayout(best);
    }

    /// <summary>
    /// Generates candidate tie configurations around the default position.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:300-450 generate_configuration()
    /// </remarks>
    private List<TieCandidate> GenerateCandidates(double width)
    {
        var candidates = new List<TieCandidate>();

        // Calculate base height
        double baseHeight = CalculateTieHeight(width);
        bool preferUp = _tie.CurveUp;

        // Y offset variations to try (in staff spaces)
        double[] yOffsets = { 0, 0.15, 0.3, -0.15, -0.3, 0.5, -0.5 };

        // Height variations (percentage of base height)
        double[] heightFactors = { 1.0, 0.85, 1.15, 0.7, 1.3 };

        foreach (var yOffset in yOffsets)
        {
            foreach (var heightFactor in heightFactors)
            {
                var candidate = new TieCandidate
                {
                    StartX = _startX + _details.XGap,
                    StartY = _startY,
                    EndX = _endX - _details.XGap,
                    EndY = _endY,
                    Height = baseHeight * heightFactor,
                    CurveUp = preferUp,
                    YOffset = yOffset,
                    Demerits = 0
                };
                candidates.Add(candidate);

                // Also try opposite direction for near-staff-center ties
                if (Math.Abs(_startY - _staffHeight / 2) < 1.5)
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
    /// Scores a tie configuration against multiple objectives.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:500-900 score_configuration()
    /// </remarks>
    private void ScoreConfiguration(TieCandidate config)
    {
        config.Demerits = 0;

        // Score staff line collision
        ScoreStaffLineCollision(config);

        // Score tie-tie collision
        ScoreTieTieCollision(config);

        // Score intra-space position preference
        ScoreIntraSpacePosition(config);

        // Score height preference (penalize extreme heights)
        ScoreHeightPreference(config);

        // Score direction preference
        ScoreDirectionPreference(config);
    }

    /// <summary>
    /// Penalizes ties that cross staff lines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:600-650 get_staff_line_clearance()
    /// </remarks>
    private void ScoreStaffLineCollision(TieCandidate config)
    {
        double baseY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3
            : config.StartY + config.YOffset + 0.3;
        double peakY = config.CurveUp
            ? baseY - config.Height
            : baseY + config.Height;

        // Check tip clearance from staff lines
        foreach (double lineY in StaffLinePositions)
        {
            double tipDistance = Math.Abs(baseY - lineY);
            if (tipDistance < _details.TipStaffLineClearance)
            {
                config.Demerits += _details.StaffLineCollisionPenalty *
                    (1 - tipDistance / _details.TipStaffLineClearance);
            }

            // Check center/peak clearance
            double centerDistance = Math.Abs(peakY - lineY);
            if (centerDistance < _details.CenterStaffLineClearance)
            {
                config.Demerits += _details.StaffLineCollisionPenalty * 0.5 *
                    (1 - centerDistance / _details.CenterStaffLineClearance);
            }
        }
    }

    /// <summary>
    /// Penalizes ties that overlap with existing ties.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:750-800
    /// </remarks>
    private void ScoreTieTieCollision(TieCandidate config)
    {
        if (_existingTies == null || _existingTies.Count == 0)
            return;

        double configMidX = (config.StartX + config.EndX) / 2;
        double configPeakY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3 - config.Height
            : config.StartY + config.YOffset + 0.3 + config.Height;

        foreach (var existing in _existingTies)
        {
            double existingMidX = (existing.StartX + existing.EndX) / 2;

            // Check if ties overlap horizontally
            bool xOverlap = !(config.EndX < existing.StartX || config.StartX > existing.EndX);
            if (!xOverlap)
                continue;

            // Check vertical distance
            double existingPeakY = (existing.Control1.Y + existing.Control2.Y) / 2;
            double yDistance = Math.Abs(configPeakY - existingPeakY);

            if (yDistance < _details.TieTieCollisionDistance)
            {
                config.Demerits += _details.TieTieCollisionPenalty *
                    (1 - yDistance / _details.TieTieCollisionDistance);
            }
        }
    }

    /// <summary>
    /// Prefers positions between staff lines (intra-space).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:550-600
    /// </remarks>
    private void ScoreIntraSpacePosition(TieCandidate config)
    {
        double baseY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3
            : config.StartY + config.YOffset + 0.3;

        // Calculate distance to nearest staff line
        double minDistance = double.MaxValue;
        foreach (double lineY in StaffLinePositions)
        {
            double distance = Math.Abs(baseY - lineY);
            minDistance = Math.Min(minDistance, distance);
        }

        // Prefer positions that are 0.5 away from staff lines (in the space)
        double idealDistance = 0.5;
        double deviation = Math.Abs(minDistance - idealDistance);
        if (deviation > _details.IntraSpaceThreshold * 0.5)
        {
            config.Demerits += deviation * 2.0;  // Mild penalty
        }
    }

    /// <summary>
    /// Penalizes extreme tie heights.
    /// </summary>
    private void ScoreHeightPreference(TieCandidate config)
    {
        double width = config.EndX - config.StartX;
        double idealHeight = CalculateTieHeight(width);

        double heightRatio = config.Height / idealHeight;
        if (heightRatio < 0.7 || heightRatio > 1.3)
        {
            config.Demerits += Math.Abs(heightRatio - 1.0) * 5.0;
        }
    }

    /// <summary>
    /// Penalizes direction that conflicts with stem direction.
    /// </summary>
    private void ScoreDirectionPreference(TieCandidate config)
    {
        // If the original tie specifies a direction, penalize deviation
        if (config.CurveUp != _tie.CurveUp)
        {
            config.Demerits += _details.WrongDirectionOffsetPenalty;
        }
    }

    /// <summary>
    /// Calculates tie height based on width using LilyPond's algorithm.
    /// </summary>
    private double CalculateTieHeight(double width)
    {
        if (_details.HeightLimit < 0.001)
            return 0;

        double x = _details.Ratio * width / _details.HeightLimit;
        return _details.HeightLimit * Math.Tanh(x);
    }

    /// <summary>
    /// Calculates indent for control points.
    /// </summary>
    private double CalculateIndent(double width)
    {
        double maxFraction = 1.0 / 3.1;
        double q = 2 * _details.HeightLimit / maxFraction;
        return 2 * _details.HeightLimit - q * q * maxFraction / (width + q);
    }

    /// <summary>
    /// Creates a TieLayout from the best candidate configuration.
    /// </summary>
    private TieLayout CreateLayout(TieCandidate config)
    {
        double width = config.EndX - config.StartX;
        double indent = CalculateIndent(width);

        double directedHeight = config.CurveUp ? -config.Height : config.Height;
        double baseY = config.CurveUp
            ? config.StartY - config.YOffset - 0.3
            : config.StartY + config.YOffset + 0.3;

        var control1 = (X: config.StartX + indent, Y: baseY + directedHeight);
        var control2 = (X: config.EndX - indent, Y: baseY + directedHeight);

        return new TieLayout(
            _tie,
            config.StartX,
            baseY,
            config.EndX,
            baseY,
            control1,
            control2);
    }
}
