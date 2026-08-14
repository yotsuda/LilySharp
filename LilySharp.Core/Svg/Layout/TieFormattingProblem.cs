// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/tie-formatting-problem.cc
//     Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
//   lily/tie-configuration.cc
//     Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One tie of a column, with everything its two bounds hand the scorer — LilyPond's
/// <c>Tie_specification</c>.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/tie-specification.hh:26-44 Tie_specification — a note head per
/// side, the tie's own staff position, and the manual direction/position a user may have imposed.
/// </remarks>
internal sealed class TieSpecification
{
    public required TieItem Tie { get; init; }

    /// <summary>
    /// The FIXED anchor each bound falls back to when it is not a note column at all — a piece
    /// broken at a system edge, or a tab digit. A bound that HAS a column reads its attachment
    /// off the outline instead and never touches these.
    /// </summary>
    public required double StartX { get; init; }

    /// <inheritdoc cref="StartX"/>
    public required double EndX { get; init; }

    /// <summary>
    /// This tie's vertical anchor, device Y: the page Y its two endpoints would share at staff
    /// position <see cref="Position"/>. The middle line is <c>Y + Position * 0.5</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE COLUMN CAN CARRY SEVERAL OF THESE. On a notation staff every tie of a column
    /// resolves to the SAME middle line, which is LilyPond's single
    /// <c>staff_symbol_referencer_</c>; on a TAB each tie hangs off its own string line, which
    /// is a Lily#-own placement (see ElementCoordinator's tab branch). Keeping the anchor per
    /// specification is what lets both share this scorer.
    /// </remarks>
    public required double Y { get; init; }

    public TieColumnParts? StartColumn { get; init; }
    public TieColumnParts? EndColumn { get; init; }
    public int StartDots { get; init; }
    public bool IsBrokenLeft { get; init; }
    public bool IsBrokenRight { get; init; }

    /// <summary>The two bound STEMS' directions (true = up). Null on a side that has none.</summary>
    /// <remarks>
    /// LilyPond skips exactly the same sides: a stem enters score_aptitude only through
    /// <c>Stem::is_normal_stem</c>, which a whole note has none of (:690-691).
    /// </remarks>
    public bool? StartStemUp { get; init; }

    /// <inheritdoc cref="StartStemUp"/>
    public bool? EndStemUp { get; init; }

    /// <summary>The tied head's staff position — LilyPond's <c>Tie_specification::position_</c>.</summary>
    public int Position => Tie.StaffPosition;

    /// <summary>
    /// A direction IMPOSED on this tie, or null — LilyPond's <c>has_manual_dir_</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONLY A REAL GROB PROPERTY REACHES THIS. A chord's bottom-DOWN / top-UP distribution
    /// is NOT one: it is <see cref="TieFormattingProblem.SetTiesConfigStandardDirections"/>,
    /// which seeds a configuration the search may still overturn. What arrives here is
    /// \voiceOne / \voiceTwo (ly/engraver-init.ly) and Lily#'s own tab rule.
    /// </remarks>
    public bool? ManualDir => Tie.ForcedCurveUp;
}

/// <summary>
/// Represents a candidate tie configuration for scoring.
/// </summary>
internal sealed class TieCandidate
{
    /// <summary>Which <see cref="TieSpecification"/> of the column this is a configuration OF.</summary>
    public int SpecIndex { get; init; }

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

    /// <summary>+1 = curve up, -1 = curve down. LilyPond's <c>dir_</c>.</summary>
    public int Dir { get; init; }

    public bool CurveUp => Dir > 0;

    /// <summary>Staff position (half-space integer) for quantized placement.</summary>
    public int Position { get; init; }

    /// <summary>Small delta offset from quantized position (staff spaces).</summary>
    public double DeltaY { get; set; }

    /// <summary>
    /// What this configuration costs ON ITS OWN — LilyPond's <c>Tie_configuration::score_</c>,
    /// which is charged once and then carried by every <c>Ties_configuration</c> the
    /// configuration appears in.
    /// </summary>
    public double Demerits { get; set; }

    public bool IsScored { get; set; }
}

/// <summary>
/// Solves a tie COLUMN's positioning: the whole set of ties is varied together and scored as
/// one <c>Ties_configuration</c>, so what a tie costs depends on where its neighbours went.
/// Faithfully ports LilyPond's scoring algorithm including peak_around/convex_amplifier
/// penalty functions, staff-line/dot/tie-tie collision scoring, and multi-tie
/// monotonicity/symmetry penalties.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tie-formatting-problem.cc:1-1285 Tie_formatting_problem class
/// LILYPOND-REF: lily/tie-configuration.cc Tie_configuration / Ties_configuration
/// LILYPOND-REF: lily/misc.cc:39-56 peak_around(), convex_amplifier()
/// <para>
/// ⚠️ THIS USED TO SOLVE ONE TIE AT A TIME, against the finished layouts of the column's
/// others, and the difference was a measured one rather than an argued one: a chord's FRONT
/// tie was fixed before the rest of the column existed, so it could not answer two different
/// numbers to two columns whose front is identical. LilyPond does
/// (audit/lp-geometry <c>tie.y.seconds.lower</c> = -3.750000 against
/// <c>tie.y.triad.lower</c> = -4.000000, the same c at head position -6 in a two-tie and a
/// three-tie column). Both readings are the pair that names this class.
/// </para>
/// </remarks>
internal sealed class TieFormattingProblem
{
    private readonly IReadOnlyList<TieSpecification> _specs;

    // Each bound column's CHORD OUTLINE — the skyline every attachment is read off (see
    // TieChordOutline). Null on a side that is not a note column at all: a piece broken at a
    // system edge, or a tab digit. Those fall back to the specification's fixed anchor, which
    // is where the caller reattached the bound.
    private readonly TieChordOutline?[] _startOutlines;
    private readonly TieChordOutline?[] _endOutlines;

    // Every dot position in the COLUMN, not just each tie's own — LilyPond's dot_positions_
    // (:242-247, filled from every Dots grob it is handed). A chord's dots all belong to one
    // chord, so for a column of one this set holds exactly the tie's own dot and the reading
    // is unchanged; for a chord it is what generate_collision_variations asks.
    private readonly HashSet<int> _dotPositions = [];

    private readonly TieDetails _details;

    // LilyPond's possibilities_: one Tie_configuration per (tie, position, direction), built
    // on demand and REUSED, so its own score is charged once however many Ties_configurations
    // it turns up in. :455-472 get_configuration.
    private readonly Dictionary<(int Spec, int Pos, int Dir), TieCandidate> _possibilities = [];

    /// <summary>
    /// Builds the problem for one tie COLUMN, bottom tie first — the order LilyPond's
    /// <c>front ()</c>/<c>back ()</c> and its monotonicity terms are written in.
    /// </summary>
    public TieFormattingProblem(IReadOnlyList<TieSpecification> specs, TieDetails? details = null)
    {
        _specs = specs;
        _details = details ?? TieDetails.Default;

        _startOutlines = new TieChordOutline?[specs.Count];
        _endOutlines = new TieChordOutline?[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            // set_column_chord_outline runs here, as it does in LilyPond's own constructor path
            // (from_ties -> set_chord_outline -> set_column_chord_outline), so the problem owns
            // its outlines and the caller only says what the column HAS.
            _startOutlines[i] = specs[i].StartColumn is { } sc
                ? TieChordOutline.Build(sc, isLeftBound: true, _details.SkylinePadding)
                : null;
            _endOutlines[i] = specs[i].EndColumn is { } ec
                ? TieChordOutline.Build(ec, isLeftBound: false, _details.SkylinePadding)
                : null;

            if (specs[i].StartDots > 0)
            {
                int pos = specs[i].Position;
                _dotPositions.Add(pos % 2 == 0 ? pos + 1 : pos);
            }
        }
    }

    /// <summary>A column of ONE, which is every tie that is not a chord's.</summary>
    public TieFormattingProblem(
        TieItem tie,
        double startX,
        double endX,
        double y,
        TieDetails? details = null,
        int startDots = 0,
        bool isBrokenLeft = false,
        bool isBrokenRight = false,
        TieColumnParts? startColumn = null,
        TieColumnParts? endColumn = null,
        bool? startStemUp = null,
        bool? endStemUp = null)
        : this(
            [new TieSpecification
            {
                Tie = tie,
                StartX = startX,
                EndX = endX,
                Y = y,
                StartDots = startDots,
                IsBrokenLeft = isBrokenLeft,
                IsBrokenRight = isBrokenRight,
                StartColumn = startColumn,
                EndColumn = endColumn,
                StartStemUp = startStemUp,
                EndStemUp = endStemUp,
            }],
            details)
    {
    }

    // ---------------------------------------------------------------
    // Helper functions ported from LilyPond
    // ---------------------------------------------------------------

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
    /// Where one tie's two columns' outlines stand at <paramref name="curveYFromMiddle"/> staff
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
    private (double Left, double Right) GetAttachment(int specIdx, double curveYFromMiddle)
        => (_startOutlines[specIdx]?.Attachment(curveYFromMiddle) ?? _specs[specIdx].StartX,
            _endOutlines[specIdx]?.Attachment(curveYFromMiddle) ?? _specs[specIdx].EndX);

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
    private (double Left, double Right) FinalAttachment(int specIdx, double curveYFromMiddle, int dir)
    {
        var att = GetAttachment(specIdx, curveYFromMiddle);

        // A short tie is more vertical, so where it would attach a little further out still
        // constrains it; a long one is flat enough that LilyPond does not ask. :565-579.
        if (BowHeight(att) < _details.IntraSpaceThreshold * 0.5)
        {
            var closeBy = GetAttachment(
                specIdx, curveYFromMiddle + dir * _details.IntraSpaceThreshold * 0.25);
            att = (Math.Max(att.Left, closeBy.Left), Math.Min(att.Right, closeBy.Right));
        }

        // widen (-x_gap): note-head-gap off each end. :581.
        att = (att.Left + _details.XGap, att.Right - _details.XGap);

        // Avoid the stems we attach to. LilyPond skips this for a semi-tie, whose two column
        // ranks are the same one (column_span_length () == 0); the counterpart here is a bound
        // that is not a column at all, which has no stem extent to read either.
        // :583-609, stem-gap 0.35.
        if (_startOutlines[specIdx]?.StemBox is { } ls && ls.Down <= curveYFromMiddle && curveYFromMiddle <= ls.Up)
            att.Left = Math.Max(att.Left, ls.Right + _details.StemGap);
        if (_endOutlines[specIdx]?.StemBox is { } rs && rs.Down <= curveYFromMiddle && curveYFromMiddle <= rs.Up)
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

    /// <summary>
    /// The page Y-up (up-positive = -device) height of one specification's MIDDLE LINE.
    /// </summary>
    private double StaffMiddleY(int specIdx)
        => _specs[specIdx].Y + _specs[specIdx].Position * 0.5;

    /// <summary>
    /// A candidate's attachment height in the page Y-up frame — LilyPond's
    /// <c>get_transformed_bezier (…).curve_point (0.0)[Y_AXIS]</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-configuration.cc:46-56 get_transformed_bezier — the bow is
    /// translated to <c>delta_y_ + staff_space * 0.5 * position_</c>, so its edge IS that sum
    /// in LilyPond's staff frame. Here it is read through the SPECIFICATION'S OWN middle line.
    /// <para>
    /// ⚠️ LILYSHARP-OWN, AND ONLY ON A TAB. LilyPond has one <c>staff_symbol_referencer_</c>
    /// per problem, so <c>0.5 * position_</c> is measured from ONE middle line and its
    /// cross-tie terms (monotonicity, tie-tie, and the collision variations' centres) compare
    /// raw sums. Every notation column here resolves to one middle line too, so subtracting it
    /// shifts all of them by the same constant and every DIFFERENCE those terms take is
    /// LilyPond's own number. A TAB column does not: Lily# hangs each tie off its own string
    /// line (see <see cref="TieSpecification.Y"/>), and the subtraction is what makes the terms
    /// compare heights on one page rather than positions on staves that are not the same staff.
    ///   departs from: :53-54, which has one <c>staff_space_ * 0.5 * position_</c> and no
    ///     per-tie origin to subtract, because LilyPond's tab tie IS at its notation position.
    ///   goes away when: the tab tie stops being a Lily#-own placement — the same decision
    ///     named at ElementCoordinator's tab branch and at TieSpecification.Y.
    ///   observed by: NOTHING on the tab side. audit/lp-geometry tie.y.* pin the notation side,
    ///     where the two readings are equal by construction; test/tab-chord-tie holds the
    ///     drawing only.
    /// </para>
    /// </remarks>
    private double EdgeYUp(TieCandidate c)
        => c.Position * 0.5 + c.DeltaY - StaffMiddleY(c.SpecIndex);

    /// <summary>
    /// The bezier MIDPOINT's height, page Y-up — <c>curve_point (0.5)[Y_AXIS]</c>, which is
    /// the edge plus the directed <see cref="TieCandidate.Height"/>.
    /// </summary>
    private double CenterYUp(TieCandidate c) => EdgeYUp(c) + c.Dir * c.Height;

    // ---------------------------------------------------------------
    // Solving
    // ---------------------------------------------------------------

    /// <summary>
    /// Solves the column, bottom tie first — one layout per specification.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1004-1023 generate_optimal_configuration
    /// </remarks>
    public IReadOnlyList<TieLayout> Solve()
    {
        var baseConfig = GenerateBaseChordConfiguration();
        double baseScore = ScoreTies(baseConfig);

        var vars = _specs.Count > 1
            ? GenerateCollisionVariations(baseConfig)
            : GenerateSingleTieVariations(baseConfig);
        var (best, bestScore) = FindBestVariation(baseConfig, baseScore, vars);

        if (_specs.Count > 1)
        {
            vars = GenerateExtremalTieVariations(best);
            (best, _) = FindBestVariation(best, bestScore, vars);
        }

        var layouts = new TieLayout[best.Length];
        for (int i = 0; i < best.Length; i++)
            layouts[i] = CreateLayout(best[i]);
        return layouts;
    }

    /// <summary>
    /// 1-opt: every variation is applied to the BASE on its own, and the best survivor is
    /// returned. A variation must score STRICTLY better to displace what is there, so the base
    /// wins every tie — which is the search's tie-break, not an accident.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:978-998 find_best_variation. audit/lp-geometry
    /// <c>tie.direction.beam-opposes-stem</c> is decided by 0.02, so the rule is legible there.
    /// </remarks>
    private (TieCandidate[] Best, double Score) FindBestVariation(
        TieCandidate[] baseConfig, double baseScore,
        List<List<(int Index, TieCandidate Config)>> vars)
    {
        var best = baseConfig;
        double bestScore = baseScore;

        foreach (var variation in vars)
        {
            var variant = (TieCandidate[])baseConfig.Clone();
            foreach (var (index, config) in variation)
                variant[index] = config;

            double score = ScoreTies(variant);
            if (score < bestScore)
            {
                best = variant;
                bestScore = score;
            }
        }

        return (best, bestScore);
    }

    // ---------------------------------------------------------------
    // Candidate generation
    // ---------------------------------------------------------------

    /// <summary>
    /// The column's starting point: standard directions distributed over the whole column, and
    /// every tie stepped one half-space DIRWARDS off its own note.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:938-971 generate_base_chord_configuration.
    /// <para>
    /// ⚠️ THE BASE DOES NOT SIT AT THE NOTE'S OWN POSITION: <c>position_ += dir_</c> runs once
    /// the standard directions are in (:964-966), and every variation is measured from THERE.
    /// </para>
    /// <para>
    /// ⚠️ LilyPond's manual-POSITION branch (\tieDown-style <c>Tie.details</c> overrides fed
    /// through <c>set_manual_tie_configuration</c>) has no counterpart here: nothing in Lily#
    /// sets one, so <c>has_manual_position_</c> is false throughout and the branches that
    /// read it are named where they are skipped rather than written out.
    /// </para>
    /// </remarks>
    private TieCandidate[] GenerateBaseChordConfiguration()
    {
        var dirs = SetTiesConfigStandardDirections();

        var config = new TieCandidate[_specs.Count];
        for (int i = 0; i < _specs.Count; i++)
            config[i] = GetConfiguration(i, _specs[i].Position + dirs[i], dirs[i]);
        return config;
    }

    /// <summary>
    /// LilyPond's standard direction distribution over a whole column: an imposed direction
    /// stands, the FRONT goes DOWN and the BACK goes UP, adjacent seconds split outward, and
    /// whatever is left follows the sign of its own position (the middle line → DOWN).
    /// A column of ONE takes a different first branch — the sign of its position, then
    /// <c>neutral-direction</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1025-1084 set_ties_config_standard_directions;
    /// lily/tie-details.cc:43-46 reads <c>neutral-direction</c>, which the Tie declares UP
    /// (scm/define-grobs.scm:3899).
    /// <para>
    /// ⚠️ NO STEM IS READ HERE, and that is the point of the port. The stems reach the answer
    /// only as a PENALTY, in <see cref="ScoreAptitude"/>, and only for a column of one.
    /// </para>
    /// <para>
    /// ⚠️ THE SECONDS BRANCH READS ONLY THE <c>fabs (diff) &lt;= 1</c> SIDE of :1058-1073. The
    /// other side needs <c>column_span ()</c> to DIFFER between two ties of one column — a tie
    /// spanning a different pair of note columns than its neighbour — and every tie of a Lily#
    /// column runs between the same two chords, so <c>span_diff</c> is zero throughout.
    ///   departs from: :1055-1063, the <c>span_diff != 0.0</c> branch (<c>column_span</c>).
    ///   goes away when: a tie column can hold ties of unequal span (LilyPond gets those from
    ///     partially-tied chords across a repeat or a broken bound).
    ///   observed by: NOTHING — there is no such column to observe.
    /// </para>
    /// <para>
    /// ⚠️ THIS USED TO LIVE IN TieDetector.EmitChordTies, which handed the result over as an
    /// IMPOSED direction. It is not one: LilyPond writes it into the base configuration and
    /// then lets <see cref="GenerateCollisionVariations"/> flip it. Only \voiceOne/\voiceTwo
    /// (and Lily#'s own tab rule) still arrive as <see cref="TieSpecification.ManualDir"/>.
    /// </para>
    /// </remarks>
    private int[] SetTiesConfigStandardDirections()
    {
        int n = _specs.Count;
        var dirs = new int?[n];
        for (int i = 0; i < n; i++)
            if (_specs[i].ManualDir is { } m)
                dirs[i] = m ? +1 : -1;

        if (n == 0)
            return [];

        if (dirs[0] is null)
        {
            if (n == 1)
            {
                int bySign = Math.Sign(_specs[0].Position);
                if (bySign != 0)
                    dirs[0] = bySign;
            }
            dirs[0] ??= n > 1 ? -1 : (_details.NeutralDirectionUp ? +1 : -1);
        }

        dirs[n - 1] ??= +1;

        // Seconds: adjacent ties within one staff position split outward.
        for (int i = 1; i < n; i++)
        {
            if (Math.Abs(_specs[i].Position - _specs[i - 1].Position) <= 1)
            {
                dirs[i - 1] ??= -1;
                dirs[i] ??= +1;
            }
        }

        // Whatever is left: the sign of its own position, the middle line counting as DOWN.
        var result = new int[n];
        for (int i = 0; i < n; i++)
        {
            int d = dirs[i] ?? Math.Sign(_specs[i].Position);
            result[i] = d != 0 ? d : -1;
        }
        return result;
    }

    /// <summary>
    /// One tie's configuration at a given position and direction, built once and reused —
    /// LilyPond's <c>possibilities_</c> cache, which is what makes a configuration's own score
    /// a charge it pays once however many whole-column configurations it appears in.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:455-472 get_configuration.
    /// <para>
    /// ⚠️ LilyPond's <c>tune_dy</c> argument is <c>!has_manual_delta_y_</c> and nothing in
    /// Lily# sets a manual delta-y, so it is always true and is not carried here.
    /// </para>
    /// <para>
    /// ⚠️ LILYPOND COPIES THESE INTO EACH <c>Ties_configuration</c> (<c>copy.push_back (*ptr)</c>,
    /// :920) AND THIS SHARES THE OBJECT. The arithmetic is the same either way:
    /// <c>score_configuration</c> is a pure function of the configuration, so a copy that
    /// re-scores and a shared object that scores once reach the same number — LilyPond's own
    /// <c>is_scored</c> flag is there to skip the recomputation, not to change it. Sharing is
    /// safe only because nothing mutates a configuration after it is built; if that ever stops
    /// being true, this has to become a copy.
    /// </para>
    /// </remarks>
    private TieCandidate GetConfiguration(int specIdx, int pos, int dir)
    {
        var key = (specIdx, pos, dir);
        if (_possibilities.TryGetValue(key, out var cached))
            return cached;

        var conf = GenerateConfiguration(specIdx, pos, dir);
        _possibilities[key] = conf;
        return conf;
    }

    /// <summary>
    /// Builds one tie configuration: nominal Y from the staff position plus
    /// the delta-y tuning LilyPond applies before scoring. All math is in
    /// LilyPond convention (staff positions / staff spaces, UP positive,
    /// relative to the middle line).
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
    private TieCandidate GenerateConfiguration(int specIdx, int pos, int dir)
    {
        double y = pos * 0.5;   // sp from middle line, up+
        double deltaY = 0;      // sp, up+
        bool yTune = true;

        // Dot avoidance — any dot of the COLUMN, which for a column of one is this tie's own.
        if (_dotPositions.Contains(pos))
        {
            deltaY += dir * 0.25;
            yTune = false;
        }

        // Head-edge hug: a tie in the half-space just outside the heads (and not on a line)
        // snaps to their outer edge + outer-tie-vertical-gap.
        // ⚠️ THE TEST READS BOTH BOUNDS AND THE ASSIGNMENT READS ONLY THE LEFT ONE, which is
        //   LilyPond's own asymmetry (:496-504), and the extent is the union over ALL the
        //   column's TIED heads — so a middle tie of a chord never hugs.
        double leftHeadEdge = HeadExtentAt(_startOutlines[specIdx], dir);
        double rightHeadEdge = HeadExtentAt(_endOutlines[specIdx], dir);
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
            double height = BowHeight(GetAttachment(specIdx, y + deltaY));

            // staff_span widened by -1: positions -3..3.
            bool withinStaff = Math.Abs(pos) <= 3;
            bool nearHeads = ContainsPosition(_startOutlines[specIdx], pos)
                             || ContainsPosition(_endOutlines[specIdx], pos);
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
        var (attachStartX, attachEndX) = FinalAttachment(specIdx, curveYFromMiddle, dir);
        double finalWidth = Math.Max(attachEndX - attachStartX, _details.MinLength);

        return new TieCandidate
        {
            SpecIndex = specIdx,
            StartX = attachStartX,
            EndX = attachEndX,
            Height = CalculateTieHeight(finalWidth),
            ControlHeight = CalculateControlHeight(finalWidth),
            Dir = dir,
            Position = pos,
            DeltaY = deltaY,
            Demerits = 0,
            IsScored = false
        };
    }

    /// <summary>
    /// A lone tie's variations: every half-space of the single-tie region, in both directions,
    /// substituted for the base one at a time.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1120-1151 generate_single_tie_variations —
    /// the walk direction doubles as the candidate's curve direction, and a direction imposed
    /// on the tie admits only its own (<c>has_manual_dir_</c>, :1138-1139).
    /// </remarks>
    private List<List<(int Index, TieCandidate Config)>> GenerateSingleTieVariations(TieCandidate[] ties)
    {
        var vars = new List<List<(int, TieCandidate)>>();
        int sz = _details.SingleTieRegionSize;

        for (int i = 0; i < sz; i++)
        {
            foreach (int d in new[] { -1, +1 })
            {
                if (i == 0 && ties[0].Dir == d)
                    continue;
                if (_specs[0].ManualDir is { } forced && d != (forced ? +1 : -1))
                    continue;

                vars.Add([(0, GetConfiguration(0, ties[0].Position + i * d, d))]);
            }
        }

        return vars;
    }

    /// <summary>
    /// A chord's variations: wherever two neighbouring ties' bezier midpoints come within a
    /// quarter space of each other, offer to flip EITHER of them; and where a tie sits on one
    /// of the column's dots, offer to step it one half-space further out.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1153-1237 generate_collision_variations.
    /// <para>
    /// ⚠️ TWO OF LILYPOND'S FIVE BRANCHES ARE NOT WRITTEN OUT, and both are named rather than
    /// silently dropped. The <c>has_manual_position_</c> gates are always open here (nothing in
    /// Lily# sets a manual position), and the <c>i == ties.size ()</c> branch at :1209-1219 is
    /// UNREACHABLE IN LILYPOND ITSELF — the loop it sits in is bounded by
    /// <c>i &lt; ties.size ()</c>, so the test can never hold.
    /// </para>
    /// </remarks>
    private List<List<(int Index, TieCandidate Config)>> GenerateCollisionVariations(TieCandidate[] ties)
    {
        const double centerDistanceTolerance = 0.25;

        var vars = new List<List<(int, TieCandidate)>>();
        double lastCenter = 0.0;

        for (int i = 0; i < ties.Length; i++)
        {
            double center = CenterYUp(ties[i]);

            if (i > 0)
            {
                if (center <= lastCenter + centerDistanceTolerance)
                {
                    if (_specs[i].ManualDir is null)
                        vars.Add([(i, GetConfiguration(i, _specs[i].Position - ties[i].Dir, -ties[i].Dir))]);

                    if (_specs[i - 1].ManualDir is null)
                        vars.Add([(i - 1, GetConfiguration(i - 1, _specs[i - 1].Position - ties[i - 1].Dir, -ties[i - 1].Dir))]);

                    if (i == 1 && ties[i - 1].Dir < 0)
                        vars.Add([(i - 1, GetConfiguration(i - 1, _specs[i - 1].Position - 1, -1))]);
                }
                else if (_dotPositions.Contains(ties[i].Position))
                {
                    vars.Add([(i, GetConfiguration(i, ties[i].Position + ties[i].Dir, ties[i].Dir))]);
                }
            }

            lastCenter = center;
        }

        return vars;
    }

    /// <summary>
    /// The column's OUTER ties pushed further out, one half-space at a time — each on its own,
    /// and then both together.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1086-1118 generate_extremal_tie_variations,
    /// run on the winner of the first pass (:1013-1018).
    /// <para>
    /// ⚠️ THIS IS THE STEP THE GREEDY SOLVE COULD NOT HAVE: it moves the FRONT tie for reasons
    /// that live in the rest of the column. audit/lp-geometry <c>tie.y.triad.lower</c> is the
    /// reading — the same c at head position -6 takes the base -7 in a two-tie column and this
    /// variation's -8 in a three-tie one.
    /// </para>
    /// </remarks>
    private List<List<(int Index, TieCandidate Config)>> GenerateExtremalTieVariations(TieCandidate[] ties)
    {
        var vars = new List<List<(int, TieCandidate)>>();

        for (int i = 1; i <= _details.MultiTieRegionSize; i++)
        {
            TieCandidate? down = null;
            TieCandidate? up = null;

            foreach (int d in new[] { -1, +1 })
            {
                int index = d < 0 ? 0 : ties.Length - 1;
                var config = ties[index];
                if (config.Dir != d)
                    continue;

                var moved = GetConfiguration(index, config.Position + d * i, d);
                if (d < 0)
                    down = moved;
                else
                    up = moved;
                vars.Add([(index, moved)]);
            }

            if (down is not null && up is not null)
                vars.Add([(0, down), (ties.Length - 1, up)]);
        }

        return vars;
    }

    // ---------------------------------------------------------------
    // Scoring functions
    // ---------------------------------------------------------------

    /// <summary>
    /// What a whole column configuration costs: each tie's own charge, then the terms that
    /// only exist BETWEEN ties, then each tie's aptitude for the note it belongs to.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:833-841 score_ties, :843-912
    /// score_ties_configuration, :819-831 score_ties_aptitude.
    /// </remarks>
    private double ScoreTies(TieCandidate[] ties)
    {
        double score = 0;

        for (int i = 0; i < ties.Length; i++)
            score += ScoreConfiguration(ties[i]);

        // Monotonicity and tie-tie collision, ADJACENT PAIRS ONLY — LilyPond carries a single
        // last_edge/last_center down the column (:854-888), so a three-tie column never scores
        // its bottom tie against its top one. Ties are emitted bottom→top and page Y-up grows
        // upward, so `edge <= last_edge` is the monotonicity violation, verbatim.
        double lastEdge = 0.0;
        double lastCenter = 0.0;
        for (int i = 0; i < ties.Length; i++)
        {
            double edge = EdgeYUp(ties[i]);
            double center = CenterYUp(ties[i]);

            if (i > 0)
            {
                if (edge <= lastEdge)
                    score += _details.TieColumnMonotonicityPenalty;
                if (center <= lastCenter)
                    score += _details.TieColumnMonotonicityPenalty;

                score += _details.TieTieCollisionPenalty
                    * BezierBow.PeakAround(
                        0.1 * _details.TieTieCollisionDistance,
                        _details.TieTieCollisionDistance,
                        Math.Abs(center - lastCenter));
                score += _details.TieTieCollisionPenalty
                    * BezierBow.PeakAround(
                        0.1 * _details.TieTieCollisionDistance,
                        _details.TieTieCollisionDistance,
                        Math.Abs(edge - lastEdge));
            }

            lastEdge = edge;
            lastCenter = center;
        }

        // What the column's OUTER ties pay for disagreeing with each other — in LENGTH, and in
        // how far each sits from its own note. Both read only front() and back(), so a middle
        // tie of a three-note chord is in neither. :890-908.
        if (ties.Length > 1)
        {
            var front = ties[0];
            var back = ties[^1];

            score += _details.OuterTieLengthSymmetryPenaltyFactor
                * Math.Abs((front.EndX - front.StartX) - (back.EndX - back.StartX));

            double frontDistance = Math.Abs(
                _specs[front.SpecIndex].Position * 0.5 - (front.Position * 0.5 + front.DeltaY));
            double backDistance = Math.Abs(
                _specs[back.SpecIndex].Position * 0.5 - (back.Position * 0.5 + back.DeltaY));
            score += _details.OuterTieVerticalDistanceSymmetryPenaltyFactor
                * Math.Abs(frontDistance - backDistance);
        }

        bool loneTie = ties.Length == 1;
        for (int i = 0; i < ties.Length; i++)
            score += ScoreAptitude(ties[i], loneTie);

        return score;
    }

    /// <summary>
    /// Scores an individual tie configuration without regard to note heads.
    /// Checks staff-line collisions (tip and center), minimum length, and dot collisions.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:741-821 score_configuration() — charged
    /// ONCE per configuration (<c>is_scored</c>), which is why the cache in
    /// <see cref="GetConfiguration"/> matters to the arithmetic and not only to the speed.
    /// </remarks>
    private double ScoreConfiguration(TieCandidate config)
    {
        if (config.IsScored)
            return config.Demerits;

        double demerits = 0;
        double length = config.EndX - config.StartX;

        // --- Minimum length penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:751-754
        double lengthPenalty = BezierBow.PeakAround(
            0.33 * _details.MinLength, _details.MinLength, length);
        demerits += _details.MinLengthPenaltyFactor * lengthPenalty;

        // --- Staff line collisions, in LilyPond position units ---
        // LILYPOND-REF: tie-formatting-problem.cc:754-792
        int dir = config.Dir;
        double tipPos = config.Position + config.DeltaY / 0.5;
        double topPos = tipPos + dir * config.Height / 0.5;

        // Curve top vs a REAL staff line, only when the top is below the
        // staff's top line (:762-774).
        int roundTopPos = (int)Math.Round(topPos);
        if (roundTopPos % 2 == 0 && Math.Abs(roundTopPos) <= 4
            && topPos * 0.5 < 2.0)
        {
            double clearanceHs = _details.CenterStaffLineClearance * 2;
            demerits += _details.StaffLineCollisionPenalty
                * BezierBow.PeakAround(0.1 * clearanceHs, clearanceHs,
                    Math.Abs(topPos - roundTopPos));
        }

        // Tie tips vs LINE positions, gated to the heads' positions or the
        // inner staff (:776-792).
        // ⚠️ THE GATE IS THE COLUMN'S HEAD SLICE, NOT THIS TIE'S OWN POSITION. It used to read
        // `roundTipPos == spec.Position`, which is the same predicate for a lone tie (a column
        // of one has a one-position slice) and NARROWER for a chord, whose slice spans every
        // head on the stem. GenerateConfiguration was already spelling it the other way, one
        // ContainsPosition call away in the same file — the two spellings of one LilyPond line.
        int roundTipPos = (int)Math.Round(tipPos);
        if (roundTipPos % 2 == 0
            && (ContainsPosition(_startOutlines[config.SpecIndex], roundTipPos)
                || ContainsPosition(_endOutlines[config.SpecIndex], roundTipPos)
                || Math.Abs(roundTipPos) <= 3))
        {
            double clearanceHs = _details.TipStaffLineClearance * 2;
            demerits += _details.StaffLineCollisionPenalty
                * BezierBow.PeakAround(0.1 * clearanceHs, clearanceHs,
                    Math.Abs(tipPos - roundTipPos));
        }

        // --- Dot collision ---
        // LILYPOND-REF: tie-formatting-problem.cc:795-818
        demerits += ScoreDotCollision(config);

        config.Demerits = demerits;
        config.IsScored = true;
        return demerits;
    }

    /// <summary>
    /// Penalizes tie configurations that conflict with augmentation dots.
    /// A dot conflicts with the tie when it lies in the direction of the tie's curve
    /// from the tie's attachment position, within the clearance threshold.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:794-813 dot_x_ / dot_positions_ collision
    /// LILYPOND-REF: lily/dots-engraver.cc:62-80 dot position avoids staff lines
    /// <para>
    /// ⚠️ NOT PORTED — THE ARITHMETIC; THE CITATION IS THE RULE'S SHAPE ONLY. LP has the
    /// quantity (the bow evaluation below), so the flat penalty is an unported
    /// simplification and not a Lily#-own one (§5.2 audit, session 158).
    /// LilyPond evaluates the DRAWN BOW over the dots' X and charges <c>peak_around</c> on the
    /// distance from EVERY dot position in the column to that height (:794-813: <c>dot_x_
    /// .center ()</c>, <c>b.get_other_coordinate (X_AXIS, x)</c>). This asks only whether the
    /// dot lies dirwards of the tie's POSITION within the clearance, and charges a flat
    /// penalty — no bow is evaluated and the dots' X is never read.
    ///   departs from: :796-813, the transformed-bezier reading.
    ///   goes away when: the problem carries the dots' X extent, which
    ///     <see cref="TieColumnParts"/> already boxes for the outline, and evaluates the
    ///     candidate's bow at it.
    ///   observed by: NOTHING. No ledger point measures a dotted tie
    ///     (tie.direction.beam-opposes-stem's dot is on the tie's END note), and no fixture or
    ///     sample in the repo ties a dotted note at all — grepped, 0 hits. The unit fixtures in
    ///     TieFormattingProblemTests reach the branch but assert a valid layout, not a number.
    /// </para>
    /// </remarks>
    private double ScoreDotCollision(TieCandidate config)
    {
        var spec = _specs[config.SpecIndex];
        if (spec.StartDots <= 0)
            return 0;

        // Dot position in half-staff-positions
        // If note is on a staff line (even staff position), dot shifts up by 1 half-space
        int dotPosition = spec.Position;
        if (spec.Position % 2 == 0)
            dotPosition += 1;

        // Check if the dot is in the curve's direction from the tie position
        // CurveUp (dir=+1): collision if dot is above (dotPosition > config.Position)
        // CurveDown (dir=-1): collision if dot is below (dotPosition < config.Position)
        int diff = dotPosition - config.Position;

        if (config.Dir * diff > 0 && Math.Abs(diff) * 0.5 <= _details.DotCollisionClearance)
            return _details.DotCollisionPenalty;

        return 0;
    }

    /// <summary>
    /// Scores tie aptitude: how well the tie fits with respect to the note head.
    /// Includes vertical distance, horizontal distance, and direction penalties.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:639-723 score_aptitude()
    /// </remarks>
    private double ScoreAptitude(TieCandidate config, bool loneTie)
    {
        var spec = _specs[config.SpecIndex];

        // LilyPond convention: staff spaces from the middle line, up+.
        int dir = config.Dir;
        double curveY = config.Position * 0.5 + config.DeltaY;
        double tieY = spec.Position * 0.5;

        double demerits = 0;

        // --- Direction penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:642-653 —
        // Direction(curve_y - tie_y) must equal the tie's direction.
        if (Math.Sign(curveY - tieY) != dir)
            demerits += _details.WrongDirectionOffsetPenalty;

        // --- Vertical distance penalty ---
        // LILYPOND-REF: tie-formatting-problem.cc:655-663
        {
            double relevantDist = Math.Max(Math.Abs(curveY - tieY) - 0.5, 0.0);
            demerits += _details.VerticalDistancePenaltyFactor
                        * BezierBow.ConvexAmplifier(1.0, 0.9, relevantDist);
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
        demerits += HorizontalDistancePenalty(_startOutlines[config.SpecIndex]?.HeadX, config.StartX);
        demerits += HorizontalDistancePenalty(_endOutlines[config.SpecIndex]?.HeadX, config.EndX);

        // --- Direction preference (same dir as stem) ---
        if (loneTie)
            demerits += ScoreDirectionAgainstStems(spec, dir);

        return demerits;
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
    /// ⚠️ THE GATE IS LILYPOND'S OWN — <c>ties_conf-&gt;size () == 1</c> (:685) — now that the
    /// problem is handed the whole column. A tie with an IMPOSED direction still reaches this,
    /// exactly as in LilyPond, and the term is then the same constant on every candidate (they
    /// all carry that one direction), so it cannot move the winner.
    /// </para>
    /// </remarks>
    private double ScoreDirectionAgainstStems(TieSpecification spec, int dir)
    {
        int? left = spec.StartStemUp is { } l ? (l ? +1 : -1) : null;
        int? right = spec.EndStemUp is { } r ? (r ? +1 : -1) : null;

        bool stemDirOk = true;
        bool positionDirOk = true;
        if (left is { } lv && right is null)
            stemDirOk = dir != lv;
        else if (left is null && right is { } rv)
            stemDirOk = dir != rv;
        else if (left is { } lv2 && right is { } rv2 && lv2 == rv2)
            stemDirOk = dir != lv2;
        else if (spec.Position != 0)
            positionDirOk = dir == Math.Sign(spec.Position);

        double demerits = 0;
        if (!stemDirOk)
            demerits += _details.SameDirAsStemPenalty;
        if (!positionDirOk)
            demerits += _details.SameDirAsStemPenalty;
        return demerits;
    }

    // ---------------------------------------------------------------
    // Layout creation
    // ---------------------------------------------------------------

    /// <summary>
    /// Creates a TieLayout from one solved configuration.
    /// </summary>
    private TieLayout CreateLayout(TieCandidate config)
    {
        var spec = _specs[config.SpecIndex];

        double width = config.EndX - config.StartX;
        double indent = CalculateIndent(width);

        // Native page Y-up attachment. The whole vertical model is Y-up (= -device); the
        // middle line sits at page-Y-up -StaffMiddleY, and the attachment is
        // (position/2 + delta_y) staff-spaces ABOVE it. Written as one subtraction rather
        // than negating a device value; DrawBow performs the lone flip to device downstream.
        double baseYUp = EdgeYUp(config);
        // The CONTROL height here — this builds the drawn bezier, and slur_shape's
        // control_[1..2] is what a Tie's stencil is made of (lily/tie.cc:154-188
        // get_default_control_points -> get_transformed_bezier).
        double directedHeightUp = config.CurveUp ? config.ControlHeight : -config.ControlHeight;

        var control1 = (X: config.StartX + indent, Y: baseYUp + directedHeightUp);
        var control2 = (X: config.EndX - indent, Y: baseYUp + directedHeightUp);

        return new TieLayout(
            spec.Tie,
            config.StartX,
            baseYUp,
            config.EndX,
            baseYUp,
            control1,
            control2,
            curveUp: config.CurveUp,
            isBrokenLeft: spec.IsBrokenLeft,
            isBrokenRight: spec.IsBrokenRight);
    }
}
