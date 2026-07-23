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
internal sealed class TieCandidate
{
    public double StartX { get; set; }
    public double EndX { get; set; }
    public double Height { get; set; }
    public bool CurveUp { get; set; }

    /// <summary>
    /// Y position of the tie attachment in the page Y-up frame (up-positive =
    /// -device), staff spaces. For CurveUp this is above the note (larger Y-up);
    /// for CurveDown, below.
    /// </summary>
    public double AttachmentY { get; set; }

    /// <summary>Staff position (half-space integer) for quantized placement.</summary>
    public int Position { get; set; }

    /// <summary>Small delta offset from quantized position (staff spaces).</summary>
    public double DeltaY { get; set; }

    public double Demerits { get; set; }
    public bool IsScored { get; set; }
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
/// LILYPOND-REF: lily/misc.cc:39-56 peak_around(), convex_amplifier()
/// </remarks>
internal sealed class TieFormattingProblem
{
    private readonly TieItem _tie;
    // Left/right attachment X at the head's INNER EDGE (right edge of the left head past
    // its dots / left edge of the right head) and at the head CENTRE (dots ignored). Which
    // one the tie attaches to depends on whether its scored endpoint Y clears the head's
    // one-staff-space box — LilyPond reads the chord-outline skyline at that Y. See GetAttachment.
    private readonly double _startX;
    private readonly double _endX;
    private readonly double _startCenterX;
    private readonly double _endCenterX;
    // A tie has a single vertical anchor (its endpoints share one Y — the page Y
    // of the staff's middle line); the scorer walks half-spaces out from there.
    private readonly double _y;
    private readonly TieDetails _details;
    private readonly IReadOnlyList<TieLayout>? _existingTies;
    private readonly int _startDots;
    private readonly bool _isBrokenLeft;
    private readonly bool _isBrokenRight;

    public TieFormattingProblem(
        TieItem tie,
        double startX,
        double endX,
        double startCenterX,
        double endCenterX,
        double y,
        TieDetails? details = null,
        IReadOnlyList<TieLayout>? existingTies = null,
        int startDots = 0,
        bool isBrokenLeft = false,
        bool isBrokenRight = false)
    {
        _isBrokenLeft = isBrokenLeft;
        _isBrokenRight = isBrokenRight;
        _tie = tie;
        _startX = startX;
        _endX = endX;
        _startCenterX = startCenterX;
        _endCenterX = endCenterX;
        _y = y;
        _details = details ?? TieDetails.Default;
        _existingTies = existingTies;
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

    /// <summary>
    /// The tie's attachment interval — the drawn endpoints — for a candidate whose scored
    /// endpoint sits at <paramref name="curveYFromMiddle"/> staff spaces above the middle line.
    /// </summary>
    /// <remarks>
    /// LilyPond builds a per-column chord-outline skyline and reads the attachment X off it at
    /// the tie's Y (get_attachment(y + delta_y)), then insets each end by the note-head gap
    /// (attachment_x_.widen(-x_gap)). Within the head's one-staff-space box the outline stands
    /// at the head's inner EDGE; beyond the box the up/down boxes recede it to the head CENTRE —
    /// x[-dir] = b[X].linear_combination(-dir/2), where -dir/2 is INTEGER division on the ±1
    /// Direction, so the argument is 0 and the interval collapses to its midpoint (not the
    /// three-quarter point). Measured across whole/half/quarter/dotted notes: within the box the
    /// span is c2c - headW - 2*x_gap, beyond it c2c - 2*x_gap, the step falling exactly at the
    /// head-box edge (|delta| = 0.5). Both bounds share one anchor Y (the tie's pitch), so the
    /// two ends switch together.
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:96-287 set_column_chord_outline
    ///   (:243-258 updowndir boxes, :251 linear_combination(-dir/2)), :73-87 get_attachment,
    ///   :563-581 attachment_x_ = get_attachment(...) then widen(-x_gap);
    ///   lily/skyline.cc:104-110 Building(Box, ...); lily/interval.hh linear_combination.
    /// </remarks>
    private (double StartX, double EndX) GetAttachment(double curveYFromMiddle)
    {
        // The head box is one staff space tall, centred on the tied note's staff position.
        double noteCenterY = _tie.StaffPosition * 0.5;
        bool clearsHead = Math.Abs(curveYFromMiddle - noteCenterY) > 0.5;
        double leftAttach = clearsHead ? _startCenterX : _startX;
        double rightAttach = clearsHead ? _endCenterX : _endX;
        return (leftAttach + _details.XGap, rightAttach - _details.XGap);
    }

    /// <summary>
    /// The width fed to the arc-height / min-length math for a candidate at
    /// <paramref name="curveYFromMiddle"/>: the attachment span (post widen), floored at
    /// the minimum tie length. LILYPOND-REF: tie-formatting-problem.cc:747-757.
    /// </summary>
    private double WidthAt(double curveYFromMiddle)
    {
        var (startX, endX) = GetAttachment(curveYFromMiddle);
        double width = endX - startX;
        return width < _details.MinLength ? _details.MinLength : width;
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
        // Generate candidate configurations at quantized positions. Each candidate computes its
        // own attachment (and hence width/height) from its scored endpoint Y, the way LilyPond
        // recomputes attachment_x_ per configuration — the attachment is Y-dependent, so it
        // cannot be a single width fixed up front.
        var candidates = GenerateCandidates();

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
    private List<TieCandidate> GenerateCandidates()
    {
        // LILYPOND-REF: lily/tie-formatting-problem.cc:1120-1151
        // generate_single_tie_variations — the base configuration sits AT the
        // note's staff position with the tie's default direction; variations
        // walk outward one half-space at a time, in BOTH directions, with the
        // walk direction doubling as the candidate's curve direction.
        int notePos = _tie.StaffPosition;
        double staffMiddleY = _y + notePos * 0.5; // page Y of the middle line
        int defaultDir = _tie.CurveUp ? +1 : -1;       // LP convention: up = +1

        var candidates = new List<TieCandidate>
        {
            GenerateConfiguration(notePos, defaultDir, notePos, staffMiddleY),
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
                candidates.Add(GenerateConfiguration(notePos + i * d, d, notePos, staffMiddleY));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Builds one tie configuration: nominal Y from the staff position plus
    /// the delta-y tuning LilyPond applies before scoring. All math is in
    /// LilyPond convention (staff positions / staff spaces, UP positive,
    /// relative to the middle line); the result is expressed in the native page
    /// Y-up frame at the end (a single subtraction from the middle line).
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
    private TieCandidate GenerateConfiguration(int pos, int dir, int notePos, double staffMiddleY)
    {
        double y = pos * 0.5;   // sp from middle line, up+
        double deltaY = 0;      // sp, up+
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
            // Provisional attachment/height at the post-hug endpoint drives the small-vs-tall
            // branch below, the way LilyPond computes attachment_x_ = get_attachment(y+delta_y)
            // and h = height() before the staff-line tuning. The attachment is Y-dependent
            // (edge vs centre — see GetAttachment), so this must be read at the current delta_y,
            // not from a width fixed up front. LILYPOND-REF: tie-formatting-problem.cc:507-511.
            double height = CalculateTieHeight(WidthAt(y + deltaY));

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
                        // center_tie_vertically: center = (edge + middle)/2 where
                        // edge = curve_point(0) = 0 and middle = curve_point(0.5).
                        // Our control points sit at ±height (slur_shape control_[1..2]),
                        // so the bezier midpoint is 0.75*height, NOT height itself.
                        // LILYPOND-REF: lily/tie-configuration.cc:37-44 center_tie_vertically;
                        //               lily/bezier-bow.cc:127-130 slur_shape.
                        double middleY = 0.75 * height;
                        deltaY = -dir * middleY / 2.0;
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

        // Final attachment (and hence width/height) at the tuned endpoint Y — LilyPond
        // recomputes attachment_x_ = get_attachment(y + delta_y) here, after the tuning.
        // LILYPOND-REF: tie-formatting-problem.cc:563-581.
        double curveYFromMiddle = y + deltaY;        // sp, up+ from the middle line
        var (attachStartX, attachEndX) = GetAttachment(curveYFromMiddle);
        double finalHeight = CalculateTieHeight(WidthAt(curveYFromMiddle));
        // Native page Y-up attachment. The whole vertical model is Y-up (= -device);
        // the middle line sits at page-Y-up -staffMiddleY (staffMiddleY is the middle
        // line's device Y, reconstructed from the caller's device anchor), and the
        // attachment is curveYFromMiddle staff-spaces ABOVE it. So page-Y-up =
        // curveYFromMiddle - staffMiddleY. Written as one subtraction rather than
        // negating a device value; DrawBow performs the lone flip to device downstream.
        double attachmentY = curveYFromMiddle - staffMiddleY; // page Y, up+

        return new TieCandidate
        {
            StartX = attachStartX,
            EndX = attachEndX,
            Height = finalHeight,
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
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:854-888 score_ties_configuration()
    ///
    /// FRAME: native page Y-up (up-positive), sign-for-sign with LP. The config's
    /// edge is its attachment; its center (the transformed bezier's midpoint,
    /// LP <c>curve_point(0.5)</c>) sits ABOVE the edge for an UP tie (+Height) and
    /// below for a DOWN tie (-Height). Existing ties are already stored page Y-up
    /// (<see cref="BowLayout"/>), so their edge/center are read directly — no
    /// reflection. The monotonicity tests then read LP's <c>&lt;=</c> verbatim: ties
    /// are emitted bottom→top, so each new tie must sit strictly ABOVE (larger Y-up
    /// than) the previous one, and <c>edge &lt;= last_edge</c> is the violation.
    /// </remarks>
    private void ScoreTieTieCollision(TieCandidate config)
    {
        if (_existingTies == null || _existingTies.Count == 0)
            return;

        // Edge = attachment; center = bezier midpoint = LP curve_point(0.5). With
        // control points at ±Height (slur_shape), the midpoint is 0.75*Height off
        // the edge — using the full Height overstated the center whenever stacked
        // ties differ in height. LILYPOND-REF: lily/tie-formatting-problem.cc:860.
        double configEdgeY = config.AttachmentY;
        double configCenterY = config.CurveUp
            ? config.AttachmentY + 0.75 * config.Height
            : config.AttachmentY - 0.75 * config.Height;

        foreach (var existing in _existingTies)
        {
            // Check horizontal overlap
            bool xOverlap = !(config.EndX < existing.StartX || config.StartX > existing.EndX);
            if (!xOverlap)
                continue;

            // Existing ties are stored page Y-up (up-positive), the same frame this
            // scorer works in, so read their edge/center directly (no reflection).
            double existingEdgeY = existing.StartYUp;
            // Bezier midpoint of the existing tie (curve_point(0.5)): the edge plus
            // 0.75 of its control-point height, matching configCenterY's frame.
            double existingCenterY = existing.StartYUp
                + 0.75 * (existing.Control1.Y - existing.StartYUp);

            // Center-center collision
            // LILYPOND-REF: tie-formatting-problem.cc:872-877
            config.Demerits += _details.TieTieCollisionPenalty
                * BezierBow.PeakAround(
                    0.1 * _details.TieTieCollisionDistance,
                    _details.TieTieCollisionDistance,
                    Math.Abs(configCenterY - existingCenterY));

            // Edge-edge collision
            // LILYPOND-REF: tie-formatting-problem.cc:878-883
            config.Demerits += _details.TieTieCollisionPenalty
                * BezierBow.PeakAround(
                    0.1 * _details.TieTieCollisionDistance,
                    _details.TieTieCollisionDistance,
                    Math.Abs(configEdgeY - existingEdgeY));

            // Monotonicity: edges and centers must be ordered. Ties are emitted
            // bottom→top, so each new tie must sit strictly ABOVE the previous one
            // = LARGER Y-up. LP penalizes `edge <= last_edge` in its native Y-up
            // frame, which we now read verbatim.
            // LILYPOND-REF: tie-formatting-problem.cc:865-870
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

        // The config is already in the page Y-up frame (up-positive = -device), the
        // frame BowLayout keeps, so its values pass straight through — no exit
        // negation. An up curve's control sits ABOVE the attachment (larger Y-up),
        // so directedHeight is +Height up / -Height down. DrawBow flips to device once.
        double baseYUp = config.AttachmentY;
        double directedHeightUp = config.CurveUp ? config.Height : -config.Height;

        var control1 = (X: config.StartX + indent, Y: baseYUp + directedHeightUp);
        var control2 = (X: config.EndX - indent, Y: baseYUp + directedHeightUp);

        return new TieLayout(
            _tie,
            config.StartX,
            baseYUp,
            config.EndX,
            baseYUp,
            control1,
            control2,
            isBrokenLeft: _isBrokenLeft,
            isBrokenRight: _isBrokenRight);
    }
}
