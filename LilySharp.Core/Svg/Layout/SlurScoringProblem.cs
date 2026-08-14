// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/slur-scoring.cc
//     Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>;
//     Jan Nieuwenhuizen <janneke@gnu.org>
//   lily/slur-configuration.cc
//     Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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
    /// The candidate's generated curve (Y-up frame) — LilyPond's
    /// <c>Slur_configuration::curve_</c>; every scorer that needs "the slur's Y
    /// at x" evaluates THIS, not a parabolic stand-in.
    /// </summary>
    public Bezier Curve { get; set; }

    /// <summary>
    /// Scorer progress for lazy evaluation (priority-queue optimization).
    /// LILYPOND-REF: lily/include/slur-configuration.hh Slur_scorers enum
    /// </summary>
    public int NextScorerTodo { get; set; } = 1; // Start after INITIAL_SCORE
    public bool IsDone => NextScorerTodo >= 5; // NUM_SCORERS
}

/// <summary>
/// One encompassed note column the slur scores its curve over — LilyPond's
/// <c>Encompass_info</c>: the head box on the slur's path plus, when the
/// column's stem points WITH the slur, the stem's reach (<see cref="StemY"/>).
/// Device coordinates (Y down; TopY &lt; BottomY).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/slur-scoring.cc:111-161 get_encompass_info — one info per
/// note column carrying <c>x_</c>, <c>head_</c> and <c>stem_</c>. <c>stem_</c>
/// is the stem's Y extent on the slur's side plus half the beam's thickness
/// when beamed (:146-150), and falls back to <c>head_</c> when the stem points
/// away or is absent (:157-158) — spelled here as <see cref="StemY"/> = NaN.
/// The head/stem values are ONE info; a separate stem entry in the list would
/// corrupt the scorer's first/last-column edge flags
/// (slur-configuration.cc:247-248), which index note columns, not grobs.
/// </remarks>
internal readonly record struct SlurObstacle(
    double X,
    double TopY,
    double BottomY,
    double StemY = double.NaN);

/// <summary>
/// How an extra-encompass object wants the slur to treat it — LilyPond's
/// <c>avoid-slur</c> values that reach the scorer ('outside objects never enter
/// the slur's own scoring; they ride the finished curve via side-position).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/slur-configuration.cc:438-448 score_extra_encompass —
/// 'around uses the full-box distance, 'inside the directed overshoot.
/// </remarks>
internal enum SlurAvoidType
{
    Inside,
    Around
}

/// <summary>
/// One extra-encompass object (augmentation dots, ...) the slur scores its curve
/// against, as its extent box in DEVICE coordinates (Y down; TopY &lt; BottomY).
/// The box arrives already widened the way LilyPond widens it at collection time
/// (dots +0.2 vertically, then thickness*0.5 vertically / thickness*1.0
/// horizontally) so the scorer stays shape-agnostic.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/slur-scoring.cc:850-884 get_extra_encompass_infos — the
/// non-slur branch: extents plus the dots-interface and thickness widens, penalty
/// per grob kind, <c>avoid-slur</c> read into <c>type_</c>.
/// </remarks>
internal readonly record struct SlurExtraObject(
    double LeftX,
    double RightX,
    double TopY,
    double BottomY,
    SlurAvoidType Type,
    double Penalty);

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
/// <remarks>
/// <paramref name="HeadWidth"/> is the endpoint notehead's ink width in staff spaces
/// (0 for a rest or a broken edge): LilyPond reads it as
/// <c>extremes_[d].slur_head_x_extent_</c> both for the tilt X shift
/// (slur-scoring.cc:780-793 slur_head_x_extent_) and for the extra-encompass
/// edge check (slur-configuration.cc:405-425 slur_head_x_extent_).
/// </remarks>
/// <param name="StemXLo">Left edge of the stem's X extent UNITED WITH ITS FLAG
/// (device) — LP's <c>extremes_[d].stem_extent_[X_AXIS]</c> is
/// <c>stem-&gt;extent ∪ flag-&gt;extent</c> (slur-scoring.cc:188-203
/// get_bound_info), so a flagged 8th-or-shorter unbeamed stem reaches to the
/// flag's ink, not the bare line. NaN when the edge has no stem or the caller
/// did not resolve one.</param>
/// <param name="StemXHi">Right edge of the same united extent (the flag hangs
/// on the stem's right in both directions, so only this side moves).</param>
/// <param name="StemTipY">Device Y of the stem's tip (the far end, on the
/// quanted beam for a beamed stem). The flag never reaches past the tip, so
/// the union leaves it. NaN when unresolved.</param>
/// <param name="StemBeginY">Device Y of the head-side end of the united
/// extent (the head the stem hangs off; pushed further only by a flag longer
/// than its stem). NaN when unresolved.</param>
internal readonly record struct SlurEdgeInfo(
    bool HasStem, bool StemUp, bool BeamedInner, bool Beamed, double HeadWidth = 0.0,
    double StemXLo = double.NaN, double StemXHi = double.NaN,
    double StemTipY = double.NaN, double StemBeginY = double.NaN);

internal sealed class SlurScoringProblem
{
    private readonly SlurItem _slur;
    private readonly double _startX;
    private readonly double _startY;
    private readonly double _endX;
    private readonly double _endY;
    private readonly SlurScoreParameters _parameters;
    private readonly IReadOnlyList<SlurObstacle>? _obstacles;
    private readonly IReadOnlyList<SlurExtraObject>? _extraObjects;
    private readonly IReadOnlyList<SlurLayout>? _existingSlurs;
    private readonly bool _isBrokenLeft;
    private readonly bool _isBrokenRight;
    private readonly SlurEdgeInfo _leftEdge;
    private readonly SlurEdgeInfo _rightEdge;
    private readonly bool _edgeHasBeams;

    // Musical dy: pitch difference in staff spaces
    private readonly double _musicalDy;

    // The staff middle's device-Y offset — the anchor for every staff-line
    // position this scorer reasons about (move_away_from_staffline on the base
    // attachments, avoid_staff_line on the generated curves).
    private readonly double _staffMiddleDown;

    public SlurScoringProblem(
        SlurItem slur,
        double startX,
        double startY,
        double endX,
        double endY,
        double staffMiddleDown,
        SlurScoreParameters? parameters = null,
        IReadOnlyList<SlurObstacle>? obstacles = null,
        IReadOnlyList<SlurLayout>? existingSlurs = null,
        bool isBrokenLeft = false,
        bool isBrokenRight = false,
        SlurEdgeInfo leftEdge = default,
        SlurEdgeInfo rightEdge = default,
        IReadOnlyList<SlurExtraObject>? extraObjects = null)
    {
        // Internal vertical frame: LilyPond's native Y-up. We obtain it from
        // the device frame (Y-down) by exact negation (yUp = -yDevice), so the
        // scorers below read sign-for-sign against lily/slur-configuration.cc
        // (dir = up ? +1 : -1, peak = mid + height, encompass dir*(slur - head)
        // < 0, ...). Negation is exact in IEEE, so the round-trip is
        // byte-neutral; CreateLayout negates the result back to device.
        _slur = slur;
        int slurDir = slur.CurveUp ? 1 : -1;
        // The base attachment steps off a staff line it would sit too close to —
        // BEFORE the grid is enumerated, so every candidate inherits the nudge
        // (0.15 ss slurward when the attachment rounds onto one of the five lines).
        // LILYPOND-REF: lily/slur-scoring.cc:559-616 move_away_from_staffline —
        //   both the real-head (:559) and broken-edge (:616) base attachments
        //   pass through it.
        _startX = startX;
        _startY = MoveAwayFromStaffline(-startY, staffMiddleDown, slurDir);
        _endX = endX;
        _endY = MoveAwayFromStaffline(-endY, staffMiddleDown, slurDir);
        _parameters = parameters ?? SlurScoreParameters.Default;
        _existingSlurs = existingSlurs;
        _isBrokenLeft = isBrokenLeft;
        _isBrokenRight = isBrokenRight;
        // A broken edge is an artificial break point — no real stem/beam there.
        // The edge stem Ys reflect into the same Y-up frame as everything else
        // (NaN survives negation).
        _leftEdge = isBrokenLeft ? default : leftEdge with
        {
            StemTipY = -leftEdge.StemTipY, StemBeginY = -leftEdge.StemBeginY,
        };
        _rightEdge = isBrokenRight ? default : rightEdge with
        {
            StemTipY = -rightEdge.StemTipY, StemBeginY = -rightEdge.StemBeginY,
        };
        _edgeHasBeams = _leftEdge.Beamed || _rightEdge.Beamed;

        // Reflect obstacle extents into the Y-up frame (negate both edges; the
        // TopY field stays the visual top edge, now the numerically larger one).
        if (obstacles != null)
        {
            var reflected = new List<SlurObstacle>(obstacles.Count);
            foreach (var o in obstacles)
                // -NaN is still NaN, so the no-stem marker survives the flip.
                reflected.Add(new SlurObstacle(o.X, -o.TopY, -o.BottomY, -o.StemY));
            _obstacles = reflected;
        }
        else
        {
            _obstacles = null;
        }

        // Extra-encompass objects into the same Y-up frame (negate both edges;
        // TopY stays the visual top, now the numerically larger one).
        if (extraObjects != null)
        {
            var reflected = new List<SlurExtraObject>(extraObjects.Count);
            foreach (var e in extraObjects)
                reflected.Add(e with { TopY = -e.TopY, BottomY = -e.BottomY });
            _extraObjects = reflected;
        }
        else
        {
            _extraObjects = null;
        }

        // Musical dy in the Y-up frame: higher pitch = larger Y.
        // LILYPOND-REF: lily/slur-scoring.cc:334-341
        _musicalDy = (slur.EndStaffPosition - slur.StartStaffPosition) / 2.0;

        _staffMiddleDown = staffMiddleDown;
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
    /// Nudges a base attachment 0.15 ss slurward when it rounds onto one of the
    /// five staff lines (Y-up frame; staff space = 1).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:639-658 move_away_from_staffline —
    /// pos = y * 2 / staff_space; the round is <c>round_halfway_up</c>
    /// (floor(x+0.5)) for the closeness test but <c>rint</c> (half-to-even) for
    /// the line lookup, and on_staff_line means an even position within the five
    /// lines. y += 1.5 * staff_space * dir / 10 when both hold.
    /// </remarks>
    private static double MoveAwayFromStaffline(double yUp, double staffMiddleDown, int dir)
    {
        double pos = (yUp + staffMiddleDown) * 2.0;
        double roundedUp = Math.Floor(pos + 0.5);
        int rint = (int)Math.Round(pos, MidpointRounding.ToEven);
        bool onLine = rint % 2 == 0 && Math.Abs(rint) <= 4;
        if (Math.Abs(pos - roundedUp) < 0.2 && onLine)
            yUp += 1.5 * dir / 10.0;
        return yUp;
    }

    /// <summary>
    /// The points a candidate's curve must clear, in the Y-up frame: interior
    /// note columns (their slurward edge plus free-head-distance) and 'inside
    /// extra objects (their slurward box edge), plus overlapping slurs' midpoints
    /// lifted by free-slur-distance. fit_factor amplifies the curve height until
    /// these fit under (over) it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:660-707 generate_avoid_offsets —
    /// note columns skip the extremes; the per-column point is
    /// <c>max(dir·head_, dir·stem_)</c> (:673), so a slurward stem (a grace
    /// column's forced-up stem under an up slur, \stemUp under an up slur)
    /// pushes the curve off its TIP, not just its head.
    /// </remarks>
    private List<(double X, double Y)> BuildAvoidOffsets(int dir)
    {
        var avoid = new List<(double X, double Y)>();
        if (_obstacles != null)
        {
            for (int i = 1; i + 1 < _obstacles.Count; i++)
            {
                var o = _obstacles[i];
                double edge = dir > 0 ? o.TopY : o.BottomY;
                if (!double.IsNaN(o.StemY))
                    edge = dir > 0 ? Math.Max(edge, o.StemY) : Math.Min(edge, o.StemY);
                avoid.Add((o.X, edge + dir * _parameters.FreeHeadDistance));
            }
        }
        if (_extraObjects != null)
        {
            foreach (var e in _extraObjects)
            {
                if (e.Type != SlurAvoidType.Inside)
                    continue;
                avoid.Add(((e.LeftX + e.RightX) / 2.0, dir > 0 ? e.TopY : e.BottomY));
            }
        }
        if (_existingSlurs != null)
        {
            // LILYPOND-REF: lily/slur-scoring.cc:682-694 free_slur_distance —
            // the small slur's curve midpoint plus that distance.
            foreach (var s in _existingSlurs)
            {
                double midX = (s.StartX + s.EndX) / 2.0;
                double midY = (s.Control1.Y + s.Control2.Y) / 2.0;
                avoid.Add((midX, midY + dir * _parameters.FreeSlurDistance));
            }
        }
        return avoid;
    }

    /// <summary>
    /// How much the flat bow must amplify its height so every avoid point fits on
    /// its slurward side, measured in the chord-aligned frame.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/slur-configuration.cc:93-133 fit_factor.</remarks>
    private static double FitFactor(
        (double X, double Y) dzUnit, (double X, double Y) dzPerp,
        double closeToEdgeLength, Bezier curve, int dir,
        IReadOnlyList<(double X, double Y)> avoid)
    {
        double fit = 0.0;
        double x0X = curve.X0, x0Y = curve.Y0;
        curve.Translate(-x0X, -x0Y);
        curve.Rotate(-Math.Atan2(dzUnit.Y, dzUnit.X) * 180.0 / Math.PI);
        curve.Scale(1, dir);

        double xLo = Math.Min(curve.X0, curve.X3);
        double xHi = Math.Max(curve.X0, curve.X3);

        foreach (var a in avoid)
        {
            double zX = a.X - x0X, zY = a.Y - x0Y;
            double pX = zX * dzUnit.X + zY * dzUnit.Y;
            double pY = dir * (zX * dzPerp.X + zY * dzPerp.Y);

            // Skip points close to either edge: shaping is not adapted for them
            // (they still count in the scoring).
            if (pX - xLo < closeToEdgeLength || xHi - pX < closeToEdgeLength)
                continue;

            // The ±eps window around pX must lie (essentially) fully inside the
            // curve's x range, or the point is skipped.
            const double eps = 0.01;
            double lo = Math.Max(pX - eps, xLo), hi = Math.Min(pX + eps, xHi);
            if (hi < lo || hi - lo <= 1.999 * eps)
                continue;

            double y = curve.GetOtherCoordinate(pX);
            if (y != 0.0)
                fit = Math.Max(fit, pY / y);
        }
        return fit;
    }

    /// <summary>
    /// Bends the curve off a staff line its horizontal point sits too close to:
    /// the middle control points move by the resolution dy, the whole curve by
    /// the remainder.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-configuration.cc:41-91 avoid_staff_line —
    /// thickness = Slur.thickness (1.2) × line-thickness (0.1) and
    /// line_thickness_ is the layout dimension itself. The 0.1 is verified
    /// against LilyPond's own output: the twin SVGs draw staff lines at
    /// stroke-width 0.1000 staff space.
    /// ⚠️ LP gates this on both extremes sharing one staff (its "TODO: handle
    /// case of broken slur") — not ported; every slur this scorer sees lives on
    /// a single staff frame, so the gate is vacuously true here.
    /// </remarks>
    private Bezier AvoidStaffLine(Bezier bez, int dir)
    {
        const double slurThickness = 1.2 * 0.1;
        const double lineThickness = 0.1;

        Span<double> ts = stackalloc double[3];
        int n = bez.SolveHorizontalTangent(ts);
        if (n == 0)
            return bez;

        double t = ts[0]; // the first (usually only) point where slur is horizontal
        double y = bez.CurveY(t);
        // A Bezier curve at t moves 3t-3t² as far as the middle control points.
        double factor = 3.0 * t * (1.0 - t);

        // Y-up frame: the staff middle line sits at -staffMiddleDown.
        double p = 2 * (y + _staffMiddleDown);
        int roundP = (int)Math.Floor(p + 0.5);
        bool OnLine(int pos) => pos % 2 == 0 && Math.Abs(pos) <= 4;
        if (!OnLine(roundP))
            roundP += (p > roundP) ? 1 : -1;
        if (!OnLine(roundP))
            return bez;

        double distance = (p - roundP) / 2.0;
        // Half the slur's thickness at t, plus one basic blot-diameter (half for
        // the slur outline, half for the staff line).
        double minDistance = 0.5 * slurThickness * factor + lineThickness
            + ((dir * distance > 0.0)
                ? _parameters.GapToStafflineInside
                : _parameters.GapToStafflineOutside);
        if (Math.Abs(distance) < minDistance)
        {
            int resolutionDir = distance > 0.0 ? 1 : -1;
            double dy = resolutionDir * (minDistance - Math.Abs(distance));
            // Shape the curve, moving the horizontal point by factor * dy.
            bez.Y1 += dy;
            bez.Y2 += dy;
            // Move the entire curve by the remaining amount.
            bez.Translate(0.0, dy - factor * dy);
        }
        return bez;
    }

    /// <summary>
    /// Generates the candidate's real curve: the flat bow built in the
    /// chord-aligned frame (height PERPENDICULAR to the chord, indent along it),
    /// amplified by fit_factor for the avoid points, then nudged off staff
    /// lines. Sets <see cref="SlurCandidate.Curve"/> and stores the final height
    /// in <see cref="SlurCandidate.Height"/> (LP's <c>height_</c>, the variance
    /// normalizer).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-configuration.cc:135-206 generate_curve —
    /// eccentricity defaults to 0.
    /// </remarks>
    private void GenerateCurve(SlurCandidate config, IReadOnlyList<(double X, double Y)> avoid)
    {
        int dir = config.CurveUp ? 1 : -1;
        double dzX = config.EndX - config.StartX;
        double dzY = config.EndY - config.StartY;
        double len = Math.Sqrt(dzX * dzX + dzY * dzY);
        if (len < 0.001)
            len = 0.001;
        var dzUnit = (X: dzX / len, Y: dzY / len);
        var dzPerp = (X: -dzUnit.Y, Y: dzUnit.X);

        double indent = CalculateIndent(len);
        double height = CalculateSlurHeight(len);

        double maxIndent = len / 3.1;
        indent = Math.Min(indent, maxIndent);

        double a1 = len * len / 3.0;
        double a2 = 0.75 * (indent + len / 3.0) * (indent + len / 3.0);
        double maxH = a1 - a2;
        maxH = maxH < 0 ? len / 3.0 : Math.Sqrt(maxH);

        double x1 = indent;   // eccentricity 0
        double x2 = -indent;

        Bezier Build(double h) => new(
            config.StartX, config.StartY,
            config.StartX + dzPerp.X * h * dir + dzUnit.X * x1,
            config.StartY + dzPerp.Y * h * dir + dzUnit.Y * x1,
            config.EndX + dzPerp.X * h * dir + dzUnit.X * x2,
            config.EndY + dzPerp.Y * h * dir + dzUnit.Y * x2,
            config.EndX, config.EndY);

        double ff = FitFactor(dzUnit, dzPerp, _parameters.CloseToEdgeLength,
            Build(height), dir, avoid);
        height = Math.Max(height, Math.Min(height * ff, maxH));

        config.Curve = AvoidStaffLine(Build(height), dir);
        config.Height = height;
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
    /// The Y the attachment grid may climb to on one side (Y-up frame): at least
    /// region-size staff spaces beyond that side's base, at least one space beyond
    /// the edge note column's slurward extreme, and never short of the OTHER
    /// side's base — the term that lets a slur from a high note stay flat over a
    /// deep drop instead of diving to the far head.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:483-516 get_y_attachment_range —
    /// end_ys[d] = dir * max(max(dir*(base[d] + region_size*dir),
    ///                           dir*(dir + nc_extent[dir])),
    ///                       dir*base[-d]).
    /// The note-column extent is the edge obstacle's head box extended by its
    /// slurward stem when it carries one (LP's column extent covers head AND
    /// stem; a stem pointing away contributes nothing on this side).
    /// ⚠️ LP's slur_head-only branch (:505-508 — a bound with a head but no note
    /// column allows only 0.3 of movement) is not ported: every edge here
    /// carries a note column or a broken-edge stand-in, so the branch has no
    /// caller.
    /// </remarks>
    private double EndYFor(bool left, int dir)
    {
        double baseOwn = left ? _startY : _endY;
        double baseOther = left ? _endY : _startY;
        double range = dir * (baseOwn + _parameters.RegionSize * dir);
        if (_obstacles is { Count: > 0 })
        {
            var edge = left ? _obstacles[0] : _obstacles[^1];
            double ncEdge = dir > 0 ? edge.TopY : edge.BottomY;
            if (!double.IsNaN(edge.StemY))
                ncEdge = dir > 0 ? Math.Max(ncEdge, edge.StemY) : Math.Min(ncEdge, edge.StemY);
            range = Math.Max(range, dir * (dir + ncEdge));
        }
        range = Math.Max(range, dir * baseOther);
        return dir * range;
    }

    /// <summary>
    /// The slur's minimum X length in staff spaces — below it a candidate's X
    /// snaps back to the head centres.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm Slur (minimum-length . 1.5),
    /// consumed at lily/slur-scoring.cc:728-730.</remarks>
    private const double MinimumLength = 1.5;

    /// <summary>
    /// The stem-attachment X rule for one candidate endpoint: when the edge
    /// note's stem points WITH the slur, the X moves onto the stem's inner face
    /// plus 0.3 while the candidate Y lies within the stem's widened Y extent
    /// (returns true = attached), or onto the stem's centre once the candidate
    /// has climbed past the tip. Y-up frame; no-op for a stemless or
    /// counter-stem edge.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:738-760 enumerate_attachments —
    /// stem_y.widen(0.25); contains → x = stem_extent_[X][-d] - d*0.3;
    /// past-tip → x = stem_extent_[X].center(). stem_extent_ is the stem's
    /// extent UNITED WITH ITS FLAG (get_bound_info :188-203), which
    /// <see cref="SlurEdgeInfo.StemXLo"/>/<see cref="SlurEdgeInfo.StemXHi"/>
    /// carry — a flagged left edge attaches past the flag's ink, and its
    /// past-tip centre shifts flagward.
    /// </remarks>
    private bool StemAttachmentX(
        in SlurEdgeInfo edge, double y, bool left, int dir, ref double x)
    {
        if (!edge.HasStem || edge.StemUp != _slur.CurveUp || double.IsNaN(edge.StemXLo))
            return false;
        double lo = Math.Min(edge.StemBeginY, edge.StemTipY) - 0.25;
        double hi = Math.Max(edge.StemBeginY, edge.StemTipY) + 0.25;
        if (y >= lo && y <= hi)
        {
            // stem_extent_[X][-d] - d*0.3: the extent edge FACING the slur's
            // interior (LEFT edge reads [RIGHT], RIGHT edge reads [LEFT]).
            x = left ? edge.StemXHi + 0.3 : edge.StemXLo - 0.3;
            return true;
        }
        if (dir * edge.StemTipY < dir * y)
            x = (edge.StemXLo + edge.StemXHi) / 2.0;
        return false;
    }

    /// <summary>
    /// Generates candidate slur configurations on a grid: every half-staff-space
    /// attachment pair from the base up to the per-side Y range. Nothing is
    /// filtered here — a too-short or too-steep pair is still a candidate (its X
    /// snaps back to the head centre in LilyPond, which is where our X already
    /// is) and the SLOPE scorer prices it; the old skip-and-fallback dropped
    /// every candidate of a deep slur and shipped the raw head-to-head chord.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:722-804 enumerate_attachments() —
    /// the while-loops to end_ys, the stem-attachment X rule (:738-760, see
    /// <see cref="StemAttachmentX"/>), the min-length/max-slope X correction
    /// (:763-778, keeps the candidate), and the tilt X shift (:780-793).
    /// LILYPOND-REF: lily/slur-scoring.cc:290-326 additional_ys — 'inside
    /// extra objects push the range further so the grid can clear them.
    /// </remarks>
    private List<SlurCandidate> GenerateCandidates(double width)
    {
        var candidates = new List<SlurCandidate>();

        bool preferUp = _slur.CurveUp;
        // Y-up: an up slur sits ABOVE its notes, so attachments move to larger Y.
        int dir = preferUp ? 1 : -1;

        // The caller (ElementCoordinator.LayoutSlurs) already supplies LilyPond's base
        // attachment, lifted off the note (head edge + 0.5 ss, or the beam tip + 0.5 ss),
        // so the enumeration begins AT that base and grids outward — it must not add a
        // second lift of its own, which used to push every slur 0.3 ss too far out.
        // LILYPOND-REF: lily/slur-scoring.cc:727 os[LEFT] = base_attachments_[LEFT].
        double baseStartY = _startY;
        double baseEndY = _endY;

        // LILYPOND-REF: lily/slur-scoring.cc:286 get_y_attachment_range
        double endYLeft = EndYFor(left: true, dir);
        double endYRight = EndYFor(left: false, dir);

        // 'inside extra objects (dots, ...) sticking out beyond the straight
        // base-to-range line extend the range so the grid can climb over them.
        // LILYPOND-REF: lily/slur-scoring.cc:290-326 — additional_ys.
        if (_extraObjects != null)
        {
            // ONE extension applied to BOTH sides: LP's inner expression does not
            // depend on the loop's d, and its `(dir_ == LEFT ? 0 : -1)` compares
            // the slur DIRECTION against LEFT (= -1), i.e. down slurs use
            // normalize + 0 and up slurs normalize - 1.
            // ⚠️ LP exempts key-signature / clef / time-signature 'inside grobs
            // from this extension (slur-scoring.cc:302-308) — unwritten here,
            // vacuously: the extra set carries only augmentation dots today.
            // Port the exemption when prefatory grobs join the set.
            double additional = 0.0;
            foreach (var info in _extraObjects)
            {
                if (info.Type != SlurAvoidType.Inside)
                    continue;
                double xc = (info.LeftX + info.RightX) / 2.0;
                // linear_interpolate(xc, base_R.x, base_L.x, end_R, end_L)
                double span = _startX - _endX;
                double norm = Math.Abs(span) < 0.001 ? 0.0 : (xc - _endX) / span;
                double yPlace = endYRight + norm * (endYLeft - endYRight);
                double encompassPlace = dir > 0 ? info.TopY : info.BottomY;
                if (dir * encompassPlace >= dir * yPlace)
                {
                    double mult = norm + (dir == -1 ? 0.0 : -1.0);
                    double ext = dir * (_parameters.EncompassObjectRangeOvershoot
                        + (yPlace - encompassPlace) * mult);
                    additional = dir > 0
                        ? Math.Max(additional, ext)
                        : Math.Min(additional, ext);
                }
            }
            endYLeft += additional;
            endYRight += additional;
        }

        const double step = 0.5; // half staff space
        const double eps = 1e-9;

        // The avoid points every candidate's curve is amplified over.
        // LILYPOND-REF: lily/slur-scoring.cc:709-719 generate_curves.
        var avoid = BuildAvoidOffsets(dir);

        for (double leftY = baseStartY; dir * leftY <= dir * endYLeft + eps; leftY += dir * step)
        {
            for (double rightY = baseEndY; dir * rightY <= dir * endYRight + eps; rightY += dir * step)
            {
                // X starts at the base attachment (the notehead centre; see the
                // caller), then moves to the STEM when the edge stem points WITH
                // the slur: onto its face (+0.3 clear of it) while the candidate Y
                // lies within the widened stem extent, or onto its centre once the
                // candidate has climbed past the tip. This is what parks a
                // voice-two down-slur's ends against the down stems instead of
                // letting the encompass stem term chase the slur away from them.
                // LILYPOND-REF: lily/slur-scoring.cc:738-760.
                double startX = _startX;
                double endX = _endX;
                bool attachLeft = StemAttachmentX(_leftEdge, leftY, left: true, dir, ref startX);
                bool attachRight = StemAttachmentX(_rightEdge, rightY, left: false, dir, ref endX);

                // A too-short or too-steep pair snaps X back to the head centre
                // (= the base X) and is KEPT; score_slopes prices the steepness.
                // LILYPOND-REF: lily/slur-scoring.cc:763-778; minimum-length 1.5
                // (scm/define-grobs.scm Slur), max-slope from the details table.
                double dzX = endX - startX;
                double dzY = rightY - leftY;
                if (dzX < MinimumLength
                    || (dzX > 0.001 && Math.Abs(dzY / dzX) > _parameters.MaxSlope))
                {
                    if (_leftEdge.HeadWidth > 0)
                    {
                        startX = _startX;
                        attachLeft = false;
                    }
                    if (_rightEdge.HeadWidth > 0)
                    {
                        endX = _endX;
                        attachRight = false;
                    }
                }

                // Horizontally move tilted slurs a little, more for bigger tilts:
                // each non-stem-attached end shifts by -dir * head_width * (unit dy) / 3.
                // LILYPOND-REF: lily/slur-scoring.cc:780-793 slur_head_x_extent field,
                //   gated on !attach_to_stem[d] (:783).
                dzX = endX - startX;
                double len = Math.Sqrt(dzX * dzX + dzY * dzY);
                if (len > 0.001)
                {
                    double unitDy = dzY / len;
                    if (!attachLeft)
                        startX -= dir * _leftEdge.HeadWidth * unitDy / 3.0;
                    if (!attachRight)
                        endX -= dir * _rightEdge.HeadWidth * unitDy / 3.0;
                }

                var candidate = new SlurCandidate
                {
                    StartX = startX,
                    StartY = leftY,
                    EndX = endX,
                    EndY = rightY,
                    CurveUp = preferUp,
                    Demerits = 0,
                    NextScorerTodo = 1
                };
                GenerateCurve(candidate, avoid);
                candidates.Add(candidate);
            }
        }

        // Unreachable in practice (the while-bounds always admit the base pair);
        // kept as a defensive floor.
        if (candidates.Count == 0)
        {
            var candidate = new SlurCandidate
            {
                StartX = _startX,
                StartY = baseStartY,
                EndX = _endX,
                EndY = baseEndY,
                CurveUp = preferUp,
                Demerits = 0,
                NextScorerTodo = 1
            };
            GenerateCurve(candidate, avoid);
            candidates.Add(candidate);
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
    /// Scores the curve against the extra-encompass set (dots, other slurs).
    /// LilyPond has NO staff-line penalty here — staff lines are handled by
    /// move_away_from_staffline (base attachments) and avoid_staff_line (the
    /// generated curve); the endpoint/peak-vs-line penalty terms that used to
    /// live in this scorer were LILYSHARP-OWN and are gone.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-configuration.cc:348-459 score_extra_encompass()
    /// </remarks>
    private void ScoreExtraEncompass(SlurCandidate config)
    {
        double demerit = 0.0;

        // Extra-encompass OBJECTS (augmentation dots, ...): the curve is scored
        // against each object's box — 'around by full-box distance, 'inside by the
        // directed overshoot past the box's slurward edge — through the same
        // peak_around ramp. An object over an edge notehead reads the attachment Y
        // itself (the slur-ending can be almost vertical, making the curve Y a bad
        // approximation there); RIGHT wins when both edges match, as LP's
        // unbroken {LEFT, RIGHT} loop leaves the last match in y.
        // LILYPOND-REF: lily/slur-configuration.cc:390-458 score_extra_encompass.
        // ⚠️ The Tie forbidden-attachment term (:352-388) is not ported: ties are
        // not in the extra set yet (their own shelf).
        if (_extraObjects != null)
        {
            int dir = config.CurveUp ? 1 : -1;
            double slurWid = config.EndX - config.StartX;
            foreach (var info in _extraObjects)
            {
                double y = 0.0;
                bool found = false;
                if (_leftEdge.HeadWidth > 0
                    && info.RightX >= _startX - _leftEdge.HeadWidth / 2.0
                    && info.LeftX <= _startX + _leftEdge.HeadWidth / 2.0)
                {
                    y = config.StartY;
                    found = true;
                }
                if (_rightEdge.HeadWidth > 0
                    && info.RightX >= _endX - _rightEdge.HeadWidth / 2.0
                    && info.LeftX <= _endX + _rightEdge.HeadWidth / 2.0)
                {
                    y = config.EndY;
                    found = true;
                }

                if (!found)
                {
                    double x = (info.LeftX + info.RightX) / 2.0;
                    if (x < config.StartX || x > config.EndX || slurWid < 0.001)
                        continue;
                    // The config's REAL curve, not a parabolic stand-in.
                    // LILYPOND-REF: lily/slur-configuration.cc:434 get_other_coordinate.
                    y = config.Curve.GetOtherCoordinate(x);
                }

                double dist;
                if (info.Type == SlurAvoidType.Around)
                {
                    // Interval.distance(y): 0 inside the box, else the gap to it.
                    dist = y > info.TopY ? y - info.TopY
                        : y < info.BottomY ? info.BottomY - y
                        : 0.0;
                }
                else
                {
                    dist = dir * (y - (dir > 0 ? info.TopY : info.BottomY));
                }
                dist = Math.Max(dist, 0.0);

                demerit += info.Penalty
                           * BezierBow.PeakAround(
                               0.1 * _parameters.ExtraEncompassFreeDistance,
                               _parameters.ExtraEncompassFreeDistance,
                               dist);
            }
        }

        // Slur-slur collision. LilyPond scores a slur against the other slurs in its
        // `encompass-objects` and pushes it clear of them (the same extra-encompass term that
        // clears accidentals and scripts). The SET is populated at engrave time by
        // Slur::auxiliary_acknowledge_extra_object, so it holds only slurs whose spans
        // OVERLAP THIS ONE IN TIME -- the caller (ElementCoordinator.LayoutSlurs) supplies
        // exactly that set, so no slur outside this one's musical span reaches here.
        // LILYPOND-REF: lily/slur-scoring.cc:679-682 (Slur members of encompass-objects) and
        //   lily/slur-configuration.cc:349 score_extra_encompass.
        if (_existingSlurs != null)
        {
            // This config's apex, read off the real curve (its horizontal-tangent
            // point; the curve midpoint when the tangent never levels).
            Span<double> ts = stackalloc double[3];
            int nTs = config.Curve.SolveHorizontalTangent(ts);
            double peakY = config.Curve.CurveY(nTs > 0 ? ts[0] : 0.5);

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

            // The config's REAL curve at the obstacle's X.
            // LILYPOND-REF: slur-configuration.cc:254 bez.get_other_coordinate.
            double t = (obstacle.X - config.StartX) / width;
            double slurY = config.Curve.GetOtherCoordinate(obstacle.X);

            bool isEdge = j == 0 || j == _obstacles.Count - 1;

            if (!isEdge)
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

                    // Track distance for variance calculation. The column's point is
                    // Encompass_info::get_point(dir) = the FARTHER of head_ and stem_
                    // on the slur's side — a slurward stem (a grace run's) measures
                    // from its tip, not its head, or the variance reads a phantom gap.
                    // LILYPOND-REF: slur-configuration.cc:283-291; lily/include/
                    //   slur-scoring.hh Encompass_info::get_point.
                    double lineY = config.StartY + t * (config.EndY - config.StartY);
                    double colPoint = headY;
                    if (!double.IsNaN(obstacle.StemY))
                        colPoint = dir > 0
                            ? Math.Max(colPoint, obstacle.StemY)
                            : Math.Min(colPoint, obstacle.StemY);
                    double closest = dir > 0
                        ? Math.Max(colPoint, lineY)
                        : Math.Min(colPoint, lineY);
                    double d = Math.Abs(closest - slurY);
                    convexHeadDistances.Add(d);
                }
            }

            // Stem encompass — runs for EVERY column, edges included. stem_ falls
            // back to the head edge when the column's stem does not point with the
            // slur (get_encompass_info :157-158 ei.stem_ = ei.head_), so the term
            // also prices a curve that dips through a head's own band.
            // LILYPOND-REF: slur-configuration.cc:295-302
            {
                double stemY = double.IsNaN(obstacle.StemY)
                    ? (config.CurveUp ? obstacle.TopY : obstacle.BottomY)
                    : obstacle.StemY;
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
        // The drawn curve IS the scored curve: the winning candidate's generated
        // bezier (chord-aligned bow + fit_factor + avoid_staff_line), verbatim.
        // The old flat-bow-with-vertical-shear reconstruction drew a DIFFERENT
        // curve from the one the scorers had judged.
        // LILYPOND-REF: lily/slur.cc Slur::print consumes the configuration's
        // curve_ unchanged.
        var curve = config.Curve;

        // Store the scored Y verbatim in the page Y-up frame — no exit negation.
        // The scorer already reasons in Y-up (slur-scoring.cc), and BowLayout keeps
        // that frame; the renderer's YFlipDrawingContext performs the single device flip.
        return new SlurLayout(
            _slur,
            curve.X0,
            curve.Y0,
            curve.X3,
            curve.Y3,
            (curve.X1, curve.Y1),
            (curve.X2, curve.Y2),
            isBrokenLeft: _isBrokenLeft,
            isBrokenRight: _isBrokenRight);
    }
}
