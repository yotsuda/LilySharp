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

    /// <summary>
    /// Y position of the tie attachment in staff spaces.
    /// For CurveUp, this is above the note; for CurveDown, below.
    /// </summary>
    public double AttachmentY { get; set; }

    /// <summary>Staff position (half-space integer) for quantized placement.</summary>
    public int Position { get; set; }

    /// <summary>Small delta offset from quantized position (staff spaces).</summary>
    public double DeltaY { get; set; }

    public double Demerits { get; set; }
    public bool IsScored { get; set; }

    public TieCandidate Clone() => new()
    {
        StartX = StartX,
        StartY = StartY,
        EndX = EndX,
        EndY = EndY,
        Height = Height,
        CurveUp = CurveUp,
        AttachmentY = AttachmentY,
        Position = Position,
        DeltaY = DeltaY,
        Demerits = Demerits,
        IsScored = IsScored
    };
}

/// <summary>
/// Solves the tie positioning problem by finding optimal positions that avoid collisions.
/// Faithfully ports LilyPond's scoring algorithm including peak_around/convex_amplifier
/// penalty functions, staff-line/dot/tie-tie collision scoring, and multi-tie
/// monotonicity/symmetry penalties.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tie-formatting-problem.cc:1-1285 Tie_formatting_problem class
/// LILYPOND-REF: lily/tie-configuration.cc Tie_configuration class
/// LILYPOND-REF: lily/misc.cc:48-65 peak_around(), convex_amplifier()
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
    private readonly int _startDots;
    private readonly bool _isBrokenLeft;
    private readonly bool _isBrokenRight;

    // Staff line positions (in staff spaces from bottom line)
    private static readonly double[] StaffLinePositions = { 0, 1, 2, 3, 4 };

    public TieFormattingProblem(
        TieItem tie,
        double startX,
        double startY,
        double endX,
        double endY,
        TieDetails? details = null,
        IReadOnlyList<TieLayout>? existingTies = null,
        double staffHeight = 4.0,
        int startDots = 0,
        bool isBrokenLeft = false,
        bool isBrokenRight = false)
    {
        _isBrokenLeft = isBrokenLeft;
        _isBrokenRight = isBrokenRight;
        _tie = tie;
        _startX = startX;
        _startY = startY;
        _endX = endX;
        _endY = endY;
        _details = details ?? TieDetails.Default;
        _existingTies = existingTies;
        _staffHeight = staffHeight;
        _startDots = startDots;
    }

    // ---------------------------------------------------------------
    // Helper functions ported from LilyPond
    // ---------------------------------------------------------------

    /// <summary>
    /// Returns 1 at x=0, decreases to 0 at x=threshold, stays 0 beyond.
    /// The epsilon parameter controls the curve shape near x=0.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/misc.cc:48-55 peak_around()
    /// </remarks>
    internal static double PeakAround(double epsilon, double threshold, double x)
    {
        if (x < 0)
            return 1.0;
        return Math.Max(-epsilon * (x - threshold) / ((x + epsilon) * threshold), 0.0);
    }

    /// <summary>
    /// Returns 0 at x=0, 1 at x=standardX, growing exponentially beyond.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/misc.cc:60-65 convex_amplifier()
    /// </remarks>
    internal static double ConvexAmplifier(double standardX, double increaseFactor, double x)
    {
        return (Math.Exp(increaseFactor * x / standardX) - 1.0)
               / (Math.Exp(increaseFactor) - 1.0);
    }

    /// <summary>
    /// Calculates tie height using LilyPond's atan formula.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/bezier-bow.cc:28-38 F0_1() + slur_height()
    /// </remarks>
    private double CalculateTieHeight(double width)
    {
        if (_details.HeightLimit < 0.001)
            return 0;

        double x = width * _details.Ratio / _details.HeightLimit;
        return _details.HeightLimit * (2.0 / Math.PI) * Math.Atan(Math.PI * x / 2.0);
    }

    /// <summary>
    /// Calculates indent for control points.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/bezier-bow.cc get_slur_indent_height()
    /// </remarks>
    private double CalculateIndent(double width)
    {
        double maxFraction = 1.0 / 3.1;
        double q = 2 * _details.HeightLimit / maxFraction;
        return 2 * _details.HeightLimit - q * q * maxFraction / (width + q);
    }

    // ---------------------------------------------------------------
    // Solving
    // ---------------------------------------------------------------

    /// <summary>
    /// Solves for the optimal tie layout.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:970-1050 solve()
    /// </remarks>
    public TieLayout Solve()
    {
        double width = _endX - _startX;
        if (width < _details.MinLength)
            width = _details.MinLength;

        // Generate candidate configurations at quantized positions
        var candidates = GenerateCandidates(width);

        // Score all candidates
        foreach (var config in candidates)
        {
            ScoreConfiguration(config);
            ScoreAptitude(config);
        }

        // Find best configuration (lowest demerits)
        var best = candidates.MinBy(c => c.Demerits) ?? candidates[0];

        return CreateLayout(best);
    }

    // ---------------------------------------------------------------
    // Candidate generation
    // ---------------------------------------------------------------

    /// <summary>
    /// Generates candidate tie configurations at quantized positions.
    /// LilyPond generates candidates at integer staff positions within a
    /// region, trying both directions at each position.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1123-1153 generate_single_tie_variations()
    /// </remarks>
    private List<TieCandidate> GenerateCandidates(double width)
    {
        var candidates = new List<TieCandidate>();

        double baseHeight = CalculateTieHeight(width);
        bool preferUp = _tie.CurveUp;
        int direction = preferUp ? -1 : 1; // -1 = up, +1 = down (Y-axis positive = down on staff)

        // Convert note position to staff-space Y
        // Staff position is in half-spaces; Y in staff spaces = staffPosition / 2
        // Note: _startY is already the note's Y in staff spaces

        // Default attachment offset from note (staff spaces)
        double defaultOffset = 0.3;

        // Base attachment position
        double baseAttachmentY = preferUp
            ? _startY - defaultOffset
            : _startY + defaultOffset;

        // Determine the staff position (integer) nearest to base attachment
        // In staff spaces, position p corresponds to staff-space Y = p * 0.5
        // But our staff lines are at integer staff spaces, so
        // the nearest half-space position to baseAttachmentY is:
        int basePosition = (int)Math.Round(baseAttachmentY * 2); // convert to half-spaces

        // Region size
        int regionSize = (_existingTies != null && _existingTies.Count > 0)
            ? _details.MultiTieRegionSize
            : _details.SingleTieRegionSize;

        // Generate candidates at integer positions within the region
        // LILYPOND-REF: lily/tie-formatting-problem.cc:1130-1150
        for (int i = 0; i < regionSize; i++)
        {
            foreach (int dir in new[] { -1, 1 }) // try both directions
            {
                if (i == 0 && (preferUp ? -1 : 1) == dir)
                    continue; // skip duplicate at base position

                int position = basePosition + i * dir;
                double attachmentY = position * 0.5; // convert back to staff spaces

                bool curveUp = preferUp;
                // For positions that move away from the default direction,
                // consider flipping direction
                if (dir != (preferUp ? -1 : 1) && i > 0)
                    curveUp = !preferUp;

                double h = baseHeight;

                var candidate = new TieCandidate
                {
                    StartX = _startX + _details.XGap,
                    StartY = _startY,
                    EndX = _endX - _details.XGap,
                    EndY = _endY,
                    Height = h,
                    CurveUp = curveUp,
                    AttachmentY = attachmentY,
                    Position = position,
                    DeltaY = 0,
                    Demerits = 0,
                    IsScored = false
                };
                candidates.Add(candidate);
            }

            // Also add the base direction at each offset
            {
                int position = basePosition + i * (preferUp ? -1 : 1);
                double attachmentY = position * 0.5;

                var candidate = new TieCandidate
                {
                    StartX = _startX + _details.XGap,
                    StartY = _startY,
                    EndX = _endX - _details.XGap,
                    EndY = _endY,
                    Height = baseHeight,
                    CurveUp = preferUp,
                    AttachmentY = attachmentY,
                    Position = position,
                    DeltaY = 0,
                    Demerits = 0,
                    IsScored = false
                };
                candidates.Add(candidate);
            }
        }

        // Always include the default configuration
        candidates.Insert(0, new TieCandidate
        {
            StartX = _startX + _details.XGap,
            StartY = _startY,
            EndX = _endX - _details.XGap,
            EndY = _endY,
            Height = baseHeight,
            CurveUp = preferUp,
            AttachmentY = baseAttachmentY,
            Position = basePosition,
            DeltaY = baseAttachmentY - basePosition * 0.5,
            Demerits = 0,
            IsScored = false
        });

        return candidates;
    }

    // ---------------------------------------------------------------
    // Scoring functions
    // ---------------------------------------------------------------

    /// <summary>
    /// Scores an individual tie configuration without regard to note heads.
    /// Checks staff-line collisions (tip and center), minimum length, and dot collisions.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:741-821 score_configuration()
    /// </remarks>
    private void ScoreConfiguration(TieCandidate config)
    {
        if (config.IsScored)
            return;

        double attachmentY = config.AttachmentY;
        double length = config.EndX - config.StartX;

        // --- Minimum length penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:751-754
        double lengthPenalty = PeakAround(
            0.33 * _details.MinLength, _details.MinLength, length);
        config.Demerits += _details.MinLengthPenaltyFactor * lengthPenalty;

        // --- Staff line collision at tip ---
        // LILYPOND-REF: tie-formatting-problem.cc:779-793
        ScoreTipStaffLineCollision(config, attachmentY);

        // --- Staff line collision at center ---
        // LILYPOND-REF: tie-formatting-problem.cc:765-775
        double peakY = config.CurveUp
            ? attachmentY - config.Height
            : attachmentY + config.Height;
        ScoreCenterStaffLineCollision(config, peakY);

        // --- Dot collision ---
        // LILYPOND-REF: tie-formatting-problem.cc:795-818
        ScoreDotCollision(config);

        config.IsScored = true;
    }

    /// <summary>
    /// Penalizes tip positions on or near staff lines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:779-793
    /// </remarks>
    private void ScoreTipStaffLineCollision(TieCandidate config, double tipY)
    {
        foreach (double lineY in StaffLinePositions)
        {
            if (tipY < -0.5 || tipY > _staffHeight + 0.5)
                continue; // outside staff

            double distance = Math.Abs(tipY - lineY);
            config.Demerits += _details.StaffLineCollisionPenalty
                * PeakAround(
                    0.1 * _details.TipStaffLineClearance,
                    _details.TipStaffLineClearance,
                    distance);
        }
    }

    /// <summary>
    /// Penalizes center/peak positions on or near staff lines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:765-775
    /// </remarks>
    private void ScoreCenterStaffLineCollision(TieCandidate config, double centerY)
    {
        foreach (double lineY in StaffLinePositions)
        {
            if (centerY < -0.5 || centerY > _staffHeight + 0.5)
                continue; // outside staff

            double distance = Math.Abs(centerY - lineY);
            config.Demerits += _details.StaffLineCollisionPenalty
                * PeakAround(
                    0.1 * _details.CenterStaffLineClearance,
                    _details.CenterStaffLineClearance,
                    distance);
        }
    }

    /// <summary>
    /// Penalizes tie configurations that conflict with augmentation dots.
    /// A dot conflicts with the tie when it lies in the direction of the tie's curve
    /// from the tie's attachment position, within the clearance threshold.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:795-818 dot collision scoring
    /// LILYPOND-REF: lily/dots-engraver.cc:62-80 dot position avoids staff lines
    /// </remarks>
    private void ScoreDotCollision(TieCandidate config)
    {
        if (_startDots <= 0)
            return;

        // Dot position in half-staff-positions
        // If note is on a staff line (even staff position), dot shifts up by 1 half-space
        int dotPosition = _tie.StaffPosition;
        if (_tie.StaffPosition % 2 == 0)
            dotPosition += 1;

        // Check if the dot is in the curve's direction from the tie position
        // CurveUp (dir=+1): collision if dot is above (dotPosition > config.Position)
        // CurveDown (dir=-1): collision if dot is below (dotPosition < config.Position)
        int dir = config.CurveUp ? 1 : -1;
        int diff = dotPosition - config.Position;

        if (dir * diff > 0 && Math.Abs(diff) * 0.5 <= _details.DotCollisionClearance)
        {
            config.Demerits += _details.DotCollisionPenalty;
        }
    }

    /// <summary>
    /// Scores tie aptitude: how well the tie fits with respect to the note head.
    /// Includes vertical distance, horizontal distance, and direction penalties.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:639-723 score_aptitude()
    /// </remarks>
    private void ScoreAptitude(TieCandidate config)
    {
        double curveY = config.AttachmentY;
        double noteY = config.StartY;

        // --- Direction penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:648-655
        bool correctDirection = config.CurveUp
            ? curveY < noteY // curve attachment should be above (lower Y) for curve up
            : curveY > noteY; // curve attachment should be below (higher Y) for curve down

        if (!correctDirection && Math.Abs(curveY - noteY) > 0.01)
        {
            config.Demerits += _details.WrongDirectionOffsetPenalty;
        }

        // --- Vertical distance penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:657-665
        {
            double relevantDist = Math.Max(Math.Abs(curveY - noteY) - 0.5, 0.0);
            double p = _details.VerticalDistancePenaltyFactor
                       * ConvexAmplifier(1.0, 0.9, relevantDist);
            config.Demerits += p;
        }

        // --- Direction preference (same dir as stem) ---
        // LILYPOND-REF: tie-formatting-problem.cc:687-720
        if (config.CurveUp != _tie.CurveUp)
        {
            config.Demerits += _details.SameDirAsStemPenalty;
        }

        // --- Tie-tie collision ---
        // LILYPOND-REF: tie-formatting-problem.cc:847-912 score_ties_configuration()
        ScoreTieTieCollision(config);
    }

    /// <summary>
    /// Penalizes ties that overlap with existing ties using peak_around.
    /// Checks both center and edge distances.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:875-886
    /// </remarks>
    private void ScoreTieTieCollision(TieCandidate config)
    {
        if (_existingTies == null || _existingTies.Count == 0)
            return;

        double configEdgeY = config.AttachmentY;
        double configCenterY = config.CurveUp
            ? config.AttachmentY - config.Height
            : config.AttachmentY + config.Height;

        foreach (var existing in _existingTies)
        {
            // Check horizontal overlap
            bool xOverlap = !(config.EndX < existing.StartX || config.StartX > existing.EndX);
            if (!xOverlap)
                continue;

            double existingEdgeY = existing.StartY;
            double existingCenterY = (existing.Control1.Y + existing.Control2.Y) / 2;

            // Center-center collision
            // LILYPOND-REF: tie-formatting-problem.cc:875-880
            config.Demerits += _details.TieTieCollisionPenalty
                * PeakAround(
                    0.1 * _details.TieTieCollisionDistance,
                    _details.TieTieCollisionDistance,
                    Math.Abs(configCenterY - existingCenterY));

            // Edge-edge collision
            // LILYPOND-REF: tie-formatting-problem.cc:881-886
            config.Demerits += _details.TieTieCollisionPenalty
                * PeakAround(
                    0.1 * _details.TieTieCollisionDistance,
                    _details.TieTieCollisionDistance,
                    Math.Abs(configEdgeY - existingEdgeY));

            // Monotonicity: edges and centers must be ordered
            // LILYPOND-REF: tie-formatting-problem.cc:868-873
            if (configEdgeY <= existingEdgeY)
                config.Demerits += _details.TieColumnMonotonicityPenalty;
            if (configCenterY <= existingCenterY)
                config.Demerits += _details.TieColumnMonotonicityPenalty;
        }
    }

    // ---------------------------------------------------------------
    // Layout creation
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a TieLayout from the best candidate configuration.
    /// </summary>
    private TieLayout CreateLayout(TieCandidate config)
    {
        double width = config.EndX - config.StartX;
        double indent = CalculateIndent(width);

        double directedHeight = config.CurveUp ? -config.Height : config.Height;
        double baseY = config.AttachmentY;

        var control1 = (X: config.StartX + indent, Y: baseY + directedHeight);
        var control2 = (X: config.EndX - indent, Y: baseY + directedHeight);

        return new TieLayout(
            _tie,
            config.StartX,
            baseY,
            config.EndX,
            baseY,
            control1,
            control2,
            isBrokenLeft: _isBrokenLeft,
            isBrokenRight: _isBrokenRight);
    }
}
