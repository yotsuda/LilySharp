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

    /// <summary>
    /// The bow's MIDPOINT height — LilyPond's <c>Tie_configuration::height</c>, the quantity
    /// every branch and every score reads. See <see cref="BezierBow.MidpointHeight"/>.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// The bezier CONTROL points' height, which is what the drawn curve is built from
    /// (<c>slur_shape</c>'s <c>control_[1..2]</c>) and is four thirds of
    /// <see cref="Height"/>. Only <see cref="TieFormattingProblem.CreateLayout"/> wants it.
    /// </summary>
    public double ControlHeight { get; set; }

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
    // The two bound columns' CHORD OUTLINES — the skylines every attachment is read off
    // (see TieChordOutline). Null on a side that is not a note column at all: a piece broken
    // at a system edge, or a tab digit. Those fall back to the fixed anchor below, which is
    // where the caller reattached the bound.
    private readonly TieChordOutline? _startOutline;
    private readonly TieChordOutline? _endOutline;
    private readonly double _startX;
    private readonly double _endX;
    // A tie has a single vertical anchor (its endpoints share one Y — the page Y
    // of the staff's middle line); the scorer walks half-spaces out from there.
    private readonly double _y;
    private readonly TieDetails _details;
    private readonly IReadOnlyList<TieLayout>? _existingTies;
    private readonly int _startDots;
    private readonly bool _isBrokenLeft;
    private readonly bool _isBrokenRight;
    // The two bound STEMS' directions (true = up). Null on a side that has none.
    // LilyPond skips exactly the same sides: a stem enters score_aptitude only through
    // Stem::is_normal_stem, which a whole note has none of (:690-691).
    private readonly bool? _startStemUp;
    private readonly bool? _endStemUp;
    // Whether this tie is the BACK of its column -- LilyPond's ties->back (), the one the
    // symmetry terms are charged to. False for a lone tie, which has no column to be
    // symmetric with.
    private readonly bool _isColumnBack;

    public TieFormattingProblem(
        TieItem tie,
        double startX,
        double endX,
        double y,
        TieDetails? details = null,
        IReadOnlyList<TieLayout>? existingTies = null,
        int startDots = 0,
        bool isBrokenLeft = false,
        bool isBrokenRight = false,
        TieColumnParts? startColumn = null,
        TieColumnParts? endColumn = null,
        bool? startStemUp = null,
        bool? endStemUp = null,
        bool isColumnBack = false)
    {
        _isColumnBack = isColumnBack;
        _isBrokenLeft = isBrokenLeft;
        _isBrokenRight = isBrokenRight;
        _tie = tie;
        _startX = startX;
        _endX = endX;
        _y = y;
        _details = details ?? TieDetails.Default;
        _existingTies = existingTies;
        _startDots = startDots;
        // set_column_chord_outline runs here, as it does in LilyPond's own constructor path
        // (from_ties -> set_chord_outline -> set_column_chord_outline), so the problem owns
        // its outlines and the caller only says what the column HAS.
        _startOutline = startColumn is { } sc
            ? TieChordOutline.Build(sc, isLeftBound: true, _details.SkylinePadding)
            : null;
        _endOutline = endColumn is { } ec
            ? TieChordOutline.Build(ec, isLeftBound: false, _details.SkylinePadding)
            : null;
        _startStemUp = startStemUp;
        _endStemUp = endStemUp;
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
    //
    // ⚠️ TWO HEIGHTS, AND LILYPOND MEANS THE FIRST ONE EVERYWHERE BUT THE STENCIL.
    // Tie_configuration::height is the shape evaluated at its MIDDLE (0.75 of the control
    // height); slur_shape's control_[1..2] is what the drawn bezier is built from. This
    // engine had only the second and tested the first against it — see
    // BezierBow.MidpointHeight for what that costs.
    private double CalculateTieHeight(double width) =>
        BezierBow.MidpointHeight(_details.HeightLimit, _details.Ratio, width);

    private double CalculateControlHeight(double width) =>
        BezierBow.Height(_details.HeightLimit, _details.Ratio, width);

    private double CalculateIndent(double width) =>
        BezierBow.Indent(_details.HeightLimit, width);

    /// <summary>
    /// Where the two columns' outlines stand at <paramref name="curveYFromMiddle"/> staff
    /// spaces above the middle line — the RAW attachment, before the note-head gap.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:72-87 get_attachment — one
    /// <c>chord_outlines_[…].height (y)</c> per side. What that outline is built from, and why
    /// a tie clearing its heads comes out a head wider than one alongside them, is
    /// <see cref="TieChordOutline"/>.
    /// <para>
    /// A side with no outline is a bound that is not a note column — a piece broken at a
    /// system edge (reattached to the system's edge X) or a tab digit (hung off the digit's
    /// own edge). Those carry a fixed anchor instead, which is the whole of this engine's
    /// counterpart to LilyPond's break-status branch at :262-270.
    /// </para>
    /// </remarks>
    private (double Left, double Right) GetAttachment(double curveYFromMiddle)
        => (_startOutline?.Attachment(curveYFromMiddle) ?? _startX,
            _endOutline?.Attachment(curveYFromMiddle) ?? _endX);

    /// <summary>
    /// The attachment a candidate is finally DRAWN between: the raw outline reading, narrowed
    /// to what a short tie can also reach a quarter of the intra-space threshold further out,
    /// inset by the note-head gap, and then pulled back off either bound's stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:563-609 generate_configuration's tail, in
    /// that order (get_attachment, then intersect, then widen, then get_stem_extent) — the ORDER is the
    /// content: the height that gates the close-by intersect is measured on the RAW span
    /// (:565), the gap comes off after it (:581), and the stem pull-back comes off after THAT
    /// (:601-607), so it is the only term that can put an endpoint back inside a head.
    /// </remarks>
    private (double Left, double Right) FinalAttachment(double curveYFromMiddle, int dir)
    {
        var att = GetAttachment(curveYFromMiddle);

        // A short tie is more vertical, so where it would attach a little further out still
        // constrains it; a long one is flat enough that LilyPond does not ask. :565-579.
        if (BowHeight(att) < _details.IntraSpaceThreshold * 0.5)
        {
            var closeBy = GetAttachment(
                curveYFromMiddle + dir * _details.IntraSpaceThreshold * 0.25);
            att = (Math.Max(att.Left, closeBy.Left), Math.Min(att.Right, closeBy.Right));
        }

        // widen (-x_gap): note-head-gap off each end. :581.
        att = (att.Left + _details.XGap, att.Right - _details.XGap);

        // Avoid the stems we attach to. LilyPond skips this for a semi-tie, whose two column
        // ranks are the same one (column_span_length () == 0); the counterpart here is a bound
        // that is not a column at all, which has no stem extent to read either.
        // :583-609, stem-gap 0.35.
        if (_startOutline?.StemBox is { } ls && ls.Down <= curveYFromMiddle && curveYFromMiddle <= ls.Up)
            att.Left = Math.Max(att.Left, ls.Right + _details.StemGap);
        if (_endOutline?.StemBox is { } rs && rs.Down <= curveYFromMiddle && curveYFromMiddle <= rs.Up)
            att.Right = Math.Min(att.Right, rs.Left - _details.StemGap);

        return att;
    }

    /// <summary>
    /// The bow's midpoint height over an attachment interval — <c>Tie_configuration::height</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-configuration.cc:62-72 get_untransformed_bezier —
    /// <c>slur_shape (attachment_x_.length (), …)</c>, so the width is whatever the interval
    /// happens to be at the moment it is asked, gap or no gap.
    /// <para>
    /// ⚠️ LILYSHARP-OWN: the MINIMUM-LENGTH FLOOR. LilyPond puts none here — <c>min-length</c>
    /// only ever appears as a PENALTY (:751-754) — and this engine has floored the bow's width
    /// at it for as long as the bow math has existed.
    ///   departs from: :65, <c>Real l = attachment_x_.length ();</c> unconditionally.
    ///   goes away when: a book measures a tie shorter than min-length. Nothing does today,
    ///     so removing the floor here would be an unobserved change to degenerate ties rather
    ///     than a port; it is left where it was found and named instead.
    ///   observed by: NOTHING.
    /// </para>
    /// </remarks>
    private double BowHeight((double Left, double Right) attachment)
        => CalculateTieHeight(Math.Max(attachment.Right - attachment.Left, _details.MinLength));

    /// <summary>
    /// One bound's tied-head Y extent on the <paramref name="dir"/> side —
    /// <c>get_head_extent (columns[d], d, Y_AXIS)[dir]</c>. A bound with no outline returns
    /// the far side of infinity, exactly as LilyPond's empty Interval does, so the
    /// head-edge hug cannot fire on a piece broken at a system edge.
    /// </summary>
    private static double HeadExtentAt(TieChordOutline? outline, int dir)
        => outline is { } o
            ? (dir > 0 ? o.HeadY.Up : o.HeadY.Down)
            : dir * double.NegativeInfinity;

    /// <summary>
    /// Whether one bound's column has a head AT <paramref name="pos"/> — LilyPond's
    /// <c>head_positions_slice (columns[d]).contains (pos)</c> (:526-527), a slice over EVERY
    /// head on the stem and not just the tied ones.
    /// </summary>
    private static bool ContainsPosition(TieChordOutline? outline, int pos)
        => outline?.HeadPositions is { } hp && pos >= hp.Low && pos <= hp.High;

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
        // ⚠️ MinBy KEEPS THE FIRST MINIMUM, and the base configuration is generated first —
        // which is LilyPond's tie-break, not an accident of LINQ. find_best_variation seeds
        // `best` with the base and replaces it only on a STRICTLY smaller score
        // (tie-formatting-problem.cc:978-998), and audit/lp-geometry
        // tie.direction.beam-opposes-stem is decided by 0.02, so the rule is legible there.
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
        // generate_single_tie_variations — variations walk outward from the BASE
        // configuration one half-space at a time, in BOTH directions, with the walk
        // direction doubling as the candidate's curve direction.
        int notePos = _tie.StaffPosition;
        double staffMiddleY = _y + notePos * 0.5; // page Y of the middle line
        int defaultDir = BaseDirection();

        // LILYPOND-REF: lily/tie-formatting-problem.cc:964-966 generate_base_chord_configuration
        // — the base configuration does NOT sit at the note's own position: it steps
        // it one half-space DIRWARDS (position_ += dir_) once the standard directions are
        // in, and every variation is measured from THERE. This engine used to start at the
        // note position, which shifted the whole candidate set by one and left LilyPond's
        // (position + dir, -dir) neighbour -- the one a stem or a dot most often drives the
        // answer onto -- out of the search altogether.
        int basePos = notePos + defaultDir;

        var candidates = new List<TieCandidate>
        {
            GenerateConfiguration(basePos, defaultDir, notePos, staffMiddleY),
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
                // A direction imposed on the tie admits only its own candidates.
                // LILYPOND-REF: tie-formatting-problem.cc:1138-1139 has_manual_dir_ —
                //   !specifications_[0].has_manual_dir_ || d == manual_dir_.
                if (_tie.ForcedCurveUp is { } forced && d != (forced ? +1 : -1))
                    continue;
                candidates.Add(GenerateConfiguration(basePos + i * d, d, notePos, staffMiddleY));
            }
        }

        return candidates;
    }

    /// <summary>
    /// The base configuration's direction: the one imposed on the tie if there is one, else
    /// the sign of its staff position, else <c>neutral-direction</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1026-1045 set_ties_config_standard_directions
    /// — for a column of ONE this is
    /// <c>Direction (sign (front.position_))</c> and then <c>details_.neutral_direction_</c>
    /// when that is zero; lily/tie-details.cc:43-46 reads <c>neutral-direction</c>, which the
    /// Tie declares UP (scm/define-grobs.scm:3899).
    /// <para>
    /// ⚠️ NO STEM IS READ HERE, and that is the point of the port. The stems reach the answer
    /// only as a PENALTY, in <see cref="ScoreAptitude"/>, and only when the two bounds agree
    /// about them.
    /// </para>
    /// <para>
    /// ⚠️ TWO LILYPOND FUNCTIONS FOLDED INTO ONE. There, an imposed direction is already in
    /// the configuration before the standard directions run — <c>generate_base_chord_configuration</c>
    /// copies <c>spec.manual_dir_</c> into it (:944-945) and
    /// <c>set_ties_config_standard_directions</c> then skips it with <c>if (!dir_)</c> — so
    /// the two steps are a write and a fill-in-the-blank. This engine has no configuration to
    /// write into ahead of time (candidates are generated on demand), so the same precedence
    /// is spelt as one chain. TO MAKE IT LITERAL, the candidate set would have to be built
    /// from a seeded base configuration the way LilyPond's is.
    /// ⚠️ AND ONLY THE ONE-TIE BRANCH IS HERE: LilyPond's same function also distributes a
    /// CHORD's directions (front DOWN / back UP / seconds split, :1044-1084), which Lily#
    /// does in <c>TieDetector.EmitChordTies</c> because it solves a column one tie at a time.
    /// That split is named there.
    /// </para>
    /// </remarks>
    private int BaseDirection()
    {
        if (_tie.ForcedCurveUp is { } forced)
            return forced ? +1 : -1;
        int bySign = Math.Sign(_tie.StaffPosition);
        if (bySign != 0)
            return bySign;
        return _details.NeutralDirectionUp ? +1 : -1;
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

        // Head-edge hug: a tie in the half-space just outside the heads (and not on a line)
        // snaps to their outer edge + outer-tie-vertical-gap.
        // ⚠️ THE TEST READS BOTH BOUNDS AND THE ASSIGNMENT READS ONLY THE LEFT ONE, which is
        //   LilyPond's own asymmetry (:496-504), and the extent is the union over ALL the
        //   column's TIED heads — so a middle tie of a chord never hugs.
        double leftHeadEdge = HeadExtentAt(_startOutline, dir);
        double rightHeadEdge = HeadExtentAt(_endOutline, dir);
        if (yTune
            && Math.Max(Math.Abs(leftHeadEdge - y), Math.Abs(rightHeadEdge - y)) < 0.25
            && pos % 2 != 0)
        {
            deltaY = (leftHeadEdge - y) + dir * _details.OuterTieVerticalGap;
        }

        if (yTune)
        {
            // Provisional height at the post-hug endpoint drives the small-vs-tall branch
            // below. ⚠️ IT IS MEASURED ON THE RAW ATTACHMENT, before the note-head gap comes
            // off — LilyPond computes attachment_x_ = get_attachment(y + delta_y) at :509-510
            // and asks for the height at :511, and only widens at :581. Measuring it on the
            // GAPPED span (this engine's former reading) makes every tie 2*note-head-gap
            // narrower here and so a little flatter than the branch it lands in expects.
            double height = BowHeight(GetAttachment(y + deltaY));

            // staff_span widened by -1: positions -3..3.
            bool withinStaff = Math.Abs(pos) <= 3;
            bool nearHeads = ContainsPosition(_startOutline, pos) || ContainsPosition(_endOutline, pos);
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
                        // edge = curve_point(0) = 0 and middle = curve_point(0.5) — which is
                        // exactly `height` here (BezierBow.MidpointHeight).
                        // LILYPOND-REF: lily/tie-configuration.cc:36-45 center_tie_vertically.
                        deltaY = -dir * height / 2.0;
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
        // recomputes attachment_x_ = get_attachment(y + delta_y) here, after the tuning, and
        // then narrows, gaps and stem-avoids it.
        // LILYPOND-REF: tie-formatting-problem.cc:563-609 generate_configuration.
        double curveYFromMiddle = y + deltaY;        // sp, up+ from the middle line
        var (attachStartX, attachEndX) = FinalAttachment(curveYFromMiddle, dir);
        double finalWidth = Math.Max(attachEndX - attachStartX, _details.MinLength);
        double finalHeight = CalculateTieHeight(finalWidth);
        double finalControlHeight = CalculateControlHeight(finalWidth);
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
            ControlHeight = finalControlHeight,
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

        // --- Horizontal distance penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:665-683 score_aptitude — one reading per END: the
        // distance from that bound's NOTE HEAD extent to the attachment LilyPond has just
        // computed for this candidate, amplified by convex_amplifier (1.25, 1.0, d).
        //
        // ⚠️ THIS IS WHAT MAKES THE ATTACHMENT'S Y-DEPENDENCE COST SOMETHING. A candidate
        // inside the head's one-space box attaches at the head's INNER EDGE and is then
        // inset by note-head-gap, which lands it note-head-gap OUTSIDE the head: 1.01 per
        // end at the defaults. One that clears the box attaches at the head CENTRE, which
        // stays inside the head and costs nothing. Without this term every candidate scores
        // the same for it and the whole edge/centre distinction is invisible to the search
        // -- which is how it stood: TieDetails.HorizontalDistancePenaltyFactor was declared
        // (and asserted by a test) and never read. audit/lp-geometry
        // tie.direction.beam-opposes-stem is decided by 2.02 against 2.04 and is the book
        // that says so.
        config.Demerits += HorizontalDistancePenalty(_startOutline?.HeadX, config.StartX);
        config.Demerits += HorizontalDistancePenalty(_endOutline?.HeadX, config.EndX);

        // --- Direction preference (same dir as stem) ---
        ScoreDirectionAgainstStems(config, dir);

        // --- Tie-tie collision, and the column's symmetry ---
        // LILYPOND-REF: tie-formatting-problem.cc:847-912 score_ties_configuration()
        ScoreTieTieCollision(config);
        ScoreColumnSymmetry(config, curveY, tieY);
    }

    /// <summary>
    /// One END's share of the horizontal-distance penalty: how far its attachment lies
    /// OUTSIDE the head it belongs to, amplified. Zero when that bound has no head.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:665-683 score_aptitude —
    /// <c>head_x.distance (conf-&gt;attachment_x_[d])</c> times
    /// <c>convex_amplifier (1.25, 1.0, …)</c>, skipped when <c>note_head_drul_[d]</c> is null.
    /// <para>
    /// ⚠️ <c>Interval::distance</c> IS SPELT OUT HERE rather than called, because this engine
    /// has no Interval type — LilyPond's is a first-class value with <c>distance</c>,
    /// <c>widen</c>, <c>linear_combination</c> and <c>intersect</c> on it (lily/interval.hh),
    /// and the tie code alone uses all four. The arithmetic is that function's, verbatim:
    /// zero inside, otherwise the gap to the nearer end. TO MAKE IT LITERAL, give the layout
    /// an Interval — this, the <c>widen</c> and the <c>intersect</c> in
    /// <see cref="FinalAttachment"/> and the <c>linear_combination</c> in
    /// <see cref="TieChordOutline"/> would then all read as LilyPond's own lines instead of
    /// as open code.
    /// </para>
    /// </remarks>
    private double HorizontalDistancePenalty((double Left, double Right)? head, double attachment)
    {
        if (head is not { } h)
            return 0.0;   // no head on this side (a broken piece, a tab digit)
        double distance = attachment < h.Left ? h.Left - attachment
                        : attachment > h.Right ? attachment - h.Right
                        : 0.0;
        return _details.HorizontalDistancePenaltyFactor
               * BezierBow.ConvexAmplifier(1.25, 1.0, distance);
    }

    /// <summary>
    /// Charges <c>same-dir-as-stem-penalty</c> when a candidate curves the way the tie's
    /// stems point — reading BOTH bounds, and only when they agree about it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:685-718 score_aptitude — the four-branch
    /// chain: one stem present decides alone; two stems decide only when they AGREE
    /// (:705-708); otherwise the tie's own POSITION is asked instead (:709-710), and a tie on
    /// the middle line (position 0) reaches neither, so nothing at all is charged and the
    /// answer falls to the distance terms.
    /// <para>
    /// ⚠️ THE FALL-THROUGH IS NOT AN OVERSIGHT TO TIDY UP, it is the case this port exists
    /// for. <c>d4~ d8.</c> with that eighth beamed to a lower note has the left stem down and
    /// the right one up, and LilyPond charges nothing — so the tie goes DOWN on a 0.02 margin
    /// in a narrow bar and UP in a wide one. audit/lp-geometry
    /// <c>tie.direction.beam-opposes-stem</c> / <c>...beam-agrees-with-stem</c> are the same
    /// music with that beam reversed, and LilyPond answers them oppositely.
    /// </para>
    /// <para>
    /// ⚠️ THE GATE IS NOT LILYPOND'S, and the difference is stated rather than hidden.
    /// LilyPond skips this term when the COLUMN holds more than one tie
    /// (<c>ties_conf-&gt;size () == 1</c>, :685); this asks instead whether a direction was
    /// imposed. The two agree on the OUTCOME — an imposed direction admits only its own
    /// candidates (<see cref="GenerateCandidates"/>), so the term would be the same constant
    /// on all of them and could not move the winner, and a chord's ties are exactly the ties
    /// this engine imposes a direction on. ⚠️ THEY WOULD STOP AGREEING if a one-tie column
    /// ever arrived with an imposed direction AND candidates in both directions; nothing
    /// produces that today. TO MAKE IT LITERAL, the problem would have to be handed the
    /// column (all its ties at once) instead of one tie plus the others' finished layouts —
    /// the same restructuring the chord OUTLINE needs (audit/lp-geometry
    /// <c>tie.width.seconds.upper</c>), which is where it should be done.
    /// </para>
    /// </remarks>
    private void ScoreDirectionAgainstStems(TieCandidate config, int dir)
    {
        if (_tie.ForcedCurveUp is not null)
            return;

        int? left = _startStemUp is { } l ? (l ? +1 : -1) : null;
        int? right = _endStemUp is { } r ? (r ? +1 : -1) : null;

        bool stemDirOk = true;
        bool positionDirOk = true;
        if (left is { } lv && right is null)
            stemDirOk = dir != lv;
        else if (left is null && right is { } rv)
            stemDirOk = dir != rv;
        else if (left is { } lv2 && right is { } rv2 && lv2 == rv2)
            stemDirOk = dir != lv2;
        else if (_tie.StaffPosition != 0)
            positionDirOk = dir == Math.Sign(_tie.StaffPosition);

        if (!stemDirOk)
            config.Demerits += _details.SameDirAsStemPenalty;
        if (!positionDirOk)
            config.Demerits += _details.SameDirAsStemPenalty;
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

        // Edge = attachment; center = bezier midpoint = LP curve_point(0.5), which IS
        // config.Height (BezierBow.MidpointHeight) — three quarters of the control height,
        // and using the control height instead overstated the center whenever stacked ties
        // differ in height. LILYPOND-REF: lily/tie-formatting-problem.cc:858-861 score_ties_configuration
        double configEdgeY = config.AttachmentY;
        double configCenterY = config.CurveUp
            ? config.AttachmentY + config.Height
            : config.AttachmentY - config.Height;

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

    /// <summary>
    /// What a chord's OUTER ties pay for disagreeing with each other — in LENGTH, and in how
    /// far each sits from its own note. Charged once, to the column's TOP tie, against the
    /// bottom one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:890-908 score_ties_configuration — both
    /// terms read only <c>ties->front ()</c> and <c>ties->back ()</c>, so a middle tie of a
    /// three-note chord is not in either of them.
    /// <para>
    /// ⚠️ WITHOUT THIS TERM THE OUTLINE PORT MAKES THE UPPER TIE WORSE, NOT BETTER, and that
    /// is measured rather than argued. On <c>&lt;c d&gt;2 ~ &lt;c d&gt;2</c> the upper tie's own
    /// aptitude prefers the candidate one half-space LOWER (it pays no vertical distance and
    /// 1.01 less horizontal), and it is the length symmetry that overrules it: taking the
    /// lower candidate makes the two ties differ by 1.577 rather than 0.962, which at factor
    /// 10 is 6.2 against an aptitude margin of 0.16. audit/lp-geometry
    /// tie.width.seconds.upper measured -0.760500 with the outline in and this term out.
    /// </para>
    /// <para>
    /// ⚠️ THIS IS GREEDY WHERE LILYPOND IS JOINT, and the difference is stated rather than
    /// hidden. LilyPond scores a whole <c>Ties_configuration</c> and varies the ties TOGETHER
    /// (:915-1001), so its front tie also pays for disagreeing with the back one; this engine
    /// solves a column one tie at a time against the finished layouts of the others, so the
    /// front tie is already fixed by the time the term exists and only the back one pays.
    ///   departs from: :890-908, which is symmetric in front and back.
    ///   goes away when: the problem is handed the whole column at once — the same
    ///     restructuring named at <see cref="ScoreDirectionAgainstStems"/>.
    ///   observed by: audit/lp-geometry tie.width.seconds.{lower,upper}. The pair is what
    ///     makes the approximation visible at all: the LOWER tie is the front, so it is exact
    ///     under both readings, and only the upper one moves.
    /// </para>
    /// </remarks>
    private void ScoreColumnSymmetry(TieCandidate config, double curveY, double tieY)
    {
        if (!_isColumnBack || _existingTies is not { Count: > 0 })
            return;

        var front = _existingTies[0];

        config.Demerits += _details.OuterTieLengthSymmetryPenaltyFactor
            * Math.Abs((front.EndX - front.StartX) - (config.EndX - config.StartX));

        // Both ties of a column hang off ONE middle line, so the front tie's stored page-Y-up
        // edge converts with this tie's own anchor. LILYPOND-REF: :897-907.
        double staffMiddleY = _y + _tie.StaffPosition * 0.5;
        double frontDistance = Math.Abs(
            front.Tie.StaffPosition * 0.5 - (front.StartYUp + staffMiddleY));
        config.Demerits += _details.OuterTieVerticalDistanceSymmetryPenaltyFactor
            * Math.Abs(frontDistance - Math.Abs(tieY - curveY));
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
        // The CONTROL height here — this builds the drawn bezier, and slur_shape's
        // control_[1..2] is what a Tie's stencil is made of (lily/tie.cc:154-188
        // get_default_control_points -> get_transformed_bezier).
        double directedHeightUp = config.CurveUp ? config.ControlHeight : -config.ControlHeight;

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
            curveUp: config.CurveUp,
            isBrokenLeft: _isBrokenLeft,
            isBrokenRight: _isBrokenRight);
    }
}
