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
    // Bow arc height / control-point indent: the shared bezier-bow math, bound to
    // this tie's height-limit and ratio. See BezierBow (LilyPond bezier-bow.cc).
    private double CalculateTieHeight(double width) =>
        BezierBow.Height(_details.HeightLimit, _details.Ratio, width);

    private double CalculateIndent(double width) =>
        BezierBow.Indent(_details.HeightLimit, width);

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
        // LILYPOND-REF: lily/tie-formatting-problem.cc:1120-1151
        // generate_single_tie_variations — the base configuration sits AT the
        // note's staff position with the tie's default direction; variations
        // walk outward one half-space at a time, in BOTH directions, with the
        // walk direction doubling as the candidate's curve direction.
        int notePos = _tie.StaffPosition;
        double staffMiddleY = _startY + notePos * 0.5; // page Y of the middle line
        int defaultDir = _tie.CurveUp ? +1 : -1;       // LP convention: up = +1

        var candidates = new List<TieCandidate>
        {
            GenerateConfiguration(notePos, defaultDir, notePos, staffMiddleY, width),
        };

        int regionSize = (_existingTies != null && _existingTies.Count > 0)
            ? _details.MultiTieRegionSize
            : _details.SingleTieRegionSize;

        for (int i = 0; i < regionSize; i++)
        {
            foreach (int d in new[] { -1, +1 })
            {
                if (i == 0 && d == defaultDir)
                    continue;
                candidates.Add(GenerateConfiguration(notePos + i * d, d, notePos, staffMiddleY, width));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Builds one tie configuration: nominal Y from the staff position plus
    /// the delta-y tuning LilyPond applies before scoring. All math is in
    /// LilyPond convention (staff positions / staff spaces, UP positive,
    /// relative to the middle line); the result converts to page Y at the end.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:476-611
    /// generate_configuration:
    /// - a dot at the tie position pushes the tie a quarter space dirwards
    ///   and disables further tuning (:489-493)
    /// - a tie in the half-space just OUTSIDE the head hugs the head's outer
    ///   edge plus outer-tie-vertical-gap (:495-505)
    /// - small ties: on a line, nudge by the tip clearance; in a staff space,
    ///   center the curve vertically on the position (:526-543 +
    ///   tie-configuration.cc:36-45 center_tie_vertically)
    /// - tall ties: keep the curve TOP clear of real staff lines (:544-560,
    ///   note LilyPond ASSIGNS delta_y there rather than adding)
    /// </remarks>
    private TieCandidate GenerateConfiguration(int pos, int dir, int notePos, double staffMiddleY, double width)
    {
        double y = pos * 0.5;   // sp from middle line, up+
        double deltaY = 0;      // sp, up+
        double height = CalculateTieHeight(width);
        bool yTune = true;

        // Dot avoidance.
        if (_startDots > 0)
        {
            int dotPos = notePos % 2 == 0 ? notePos + 1 : notePos;
            if (dotPos == pos)
            {
                deltaY += dir * 0.25;
                yTune = false;
            }
        }

        // Head-edge hug: the head spans one half-space either side of its
        // position; a tie in the adjacent half-space (and not on a line)
        // snaps to the head's outer edge + outer-tie-vertical-gap.
        double headEdgeY = (notePos + dir) * 0.5;
        if (yTune && Math.Abs(headEdgeY - y) < 0.25 && pos % 2 != 0)
        {
            deltaY = (headEdgeY - y) + dir * _details.OuterTieVerticalGap;
        }

        if (yTune)
        {
            // staff_span widened by -1: positions -3..3; head positions at
            // the columns reduce to the note position for single notes.
            bool withinStaff = Math.Abs(pos) <= 3;
            bool nearHeads = pos == notePos;
            if (nearHeads || withinStaff)
            {
                if (height < _details.IntraSpaceThreshold * 0.5)
                {
                    if (pos % 2 == 0)
                    {
                        // TipStaffLineClearance is stored in staff spaces
                        // (= LP's half-space value x 0.5 already).
                        deltaY += dir * _details.TipStaffLineClearance;
                    }
                    else if (withinStaff)
                    {
                        // center_tie_vertically: untransformed curve spans
                        // 0..height, center = height/2.
                        deltaY = -dir * height / 2;
                    }
                }
                else
                {
                    double topY = y + deltaY + dir * height;
                    double topPos = topY / 0.5;
                    int roundPos = (int)Math.Floor(topPos + 0.5); // round_halfway_up
                    // Clearance compared in half-space units (LP raw value).
                    double clearanceHs = _details.CenterStaffLineClearance * 2;
                    bool onRealStaffLine = roundPos % 2 == 0 && Math.Abs(roundPos) <= 4;
                    if (Math.Abs(topPos - roundPos) < clearanceHs && onRealStaffLine)
                    {
                        double newY = (roundPos + clearanceHs * dir) * 0.5;
                        deltaY = newY - topY; // LP assigns (see remarks)
                    }
                }
            }
        }

        double curveYFromMiddle = y + deltaY;        // sp, up+
        double attachmentY = staffMiddleY - curveYFromMiddle; // page Y, down+

        return new TieCandidate
        {
            StartX = _startX + _details.XGap,
            StartY = _startY,
            EndX = _endX - _details.XGap,
            EndY = _endY,
            Height = height,
            CurveUp = dir > 0,
            AttachmentY = attachmentY,
            Position = pos,
            DeltaY = deltaY,
            Demerits = 0,
            IsScored = false
        };
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
        double lengthPenalty = BezierBow.PeakAround(
            0.33 * _details.MinLength, _details.MinLength, length);
        config.Demerits += _details.MinLengthPenaltyFactor * lengthPenalty;

        // --- Staff line collisions, in LilyPond position units ---
        // LILYPOND-REF: tie-formatting-problem.cc:754-792
        int dir = config.CurveUp ? +1 : -1;
        double tipPos = config.Position + config.DeltaY / 0.5;
        double topPos = tipPos + dir * config.Height / 0.5;

        // Curve top vs a REAL staff line, only when the top is below the
        // staff's top line (:762-774).
        int roundTopPos = (int)Math.Round(topPos);
        if (roundTopPos % 2 == 0 && Math.Abs(roundTopPos) <= 4
            && topPos * 0.5 < 2.0)
        {
            double clearanceHs = _details.CenterStaffLineClearance * 2;
            config.Demerits += _details.StaffLineCollisionPenalty
                * BezierBow.PeakAround(0.1 * clearanceHs, clearanceHs,
                    Math.Abs(topPos - roundTopPos));
        }

        // Tie tips vs LINE positions, gated to the heads' positions or the
        // inner staff (:776-792).
        int roundTipPos = (int)Math.Round(tipPos);
        if (roundTipPos % 2 == 0
            && (roundTipPos == _tie.StaffPosition || Math.Abs(roundTipPos) <= 3))
        {
            double clearanceHs = _details.TipStaffLineClearance * 2;
            config.Demerits += _details.StaffLineCollisionPenalty
                * BezierBow.PeakAround(0.1 * clearanceHs, clearanceHs,
                    Math.Abs(tipPos - roundTipPos));
        }

        // --- Dot collision ---
        // LILYPOND-REF: tie-formatting-problem.cc:795-818
        ScoreDotCollision(config);

        config.IsScored = true;
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
        // LilyPond convention: staff spaces from the middle line, up+.
        int dir = config.CurveUp ? +1 : -1;
        double curveY = config.Position * 0.5 + config.DeltaY;
        double tieY = _tie.StaffPosition * 0.5;

        // --- Direction penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:642-653 —
        // Direction(curve_y - tie_y) must equal the tie's direction.
        if (Math.Sign(curveY - tieY) != dir)
        {
            config.Demerits += _details.WrongDirectionOffsetPenalty;
        }

        // --- Vertical distance penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:655-663
        {
            double relevantDist = Math.Max(Math.Abs(curveY - tieY) - 0.5, 0.0);
            double p = _details.VerticalDistancePenaltyFactor
                       * BezierBow.ConvexAmplifier(1.0, 0.9, relevantDist);
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
                * BezierBow.PeakAround(
                    0.1 * _details.TieTieCollisionDistance,
                    _details.TieTieCollisionDistance,
                    Math.Abs(configCenterY - existingCenterY));

            // Edge-edge collision
            // LILYPOND-REF: tie-formatting-problem.cc:881-886
            config.Demerits += _details.TieTieCollisionPenalty
                * BezierBow.PeakAround(
                    0.1 * _details.TieTieCollisionDistance,
                    _details.TieTieCollisionDistance,
                    Math.Abs(configEdgeY - existingEdgeY));

            // Monotonicity: edges and centers must be ordered. Ties are emitted
            // bottom→top, so each new tie must sit strictly ABOVE the previous
            // one. LP (Y-up) penalizes `edge <= last_edge`; our Y is device
            // (down-positive), where "above" is the SMALLER value — so the
            // violated order here is >=, not <= (<= penalized exactly the
            // correct stacking and rewarded the inverted one).
            // LILYPOND-REF: tie-formatting-problem.cc:868-873
            if (configEdgeY >= existingEdgeY)
                config.Demerits += _details.TieColumnMonotonicityPenalty;
            if (configCenterY >= existingCenterY)
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
