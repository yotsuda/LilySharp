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
/// Shared geometry for a bow-shaped grob (a slur or a tie), drawn as a cubic
/// Bezier with four control points. <see cref="SlurLayout"/> and
/// <see cref="TieLayout"/> add their respective model payload; every coordinate
/// field lives here so the two never drift apart.
/// </summary>
internal abstract record BowLayout
{
    /// <summary>Global staff index this bow belongs to (-1 = unknown, e.g.
    /// direct unit-test construction). The renderer uses it to shrink bows on
    /// ossia staves with the rest of the staff's notation.</summary>
    public int StaffIndex { get; init; } = -1;

    /// <summary>Measure index used to decide which PAGE this (possibly broken) piece
    /// renders on. For a cross-system break the piece's geometry is page-local to ITS
    /// own system, so the page-membership test must key off a measure on that segment's
    /// system — NOT the original spanner's start measure, which may sit on an earlier
    /// page and would drag the continuation onto the wrong page. -1 falls back to the
    /// model's own start measure (single-system bows).</summary>
    public int RenderMeasureIndex { get; init; } = -1;

    // COORDINATE FRAME: every Y here is Y-up — up-positive, the exact negation of device Y
    // (yUp = -yDevice), the frame both bow scorers reason in (SlurScoringProblem/
    // slur-scoring.cc and TieFormattingProblem/tie-formatting-problem.cc, sign-for-sign), so
    // each CreateLayout stores the scored Y verbatim with no exit negation — and ITS ORIGIN
    // IS THE BOW'S OWN STAFF'S TOP LINE (session 1116): the scorers run with no within-system
    // offset (ElementCoordinator.LayoutTies' staffY 0, LayoutSlurs' staffMiddleDown
    // StaffHeight/2), so a bow depends on nothing but its staff's own music and the
    // preliminary pass and the page agree bit for bit. A reader puts it on the page by adding
    // the staff's top ONCE: page Y-up = LayoutUtilities.FindStaffYInSystem(system, StaffIndex)
    // + YUp (SharedRenderer.DrawTies/DrawSlurs), within-system Y-up =
    // StaffOffsetInSystemUp(system, StaffIndex) + YUp (the paging skylines), and a script's
    // frame about the staff middle = YUp + StaffMiddle (ArticulationEngraver). The
    // Control1/Control2 tuples' .Y members are in the same frame.
    // ⚠️ Until session 1115 the within-system offset was baked in as an additive base, and a
    // bow carried from the preliminary pass onto paged systems was moved by the offset's
    // delta (LayoutEngine.BowShiftsOf / ReanchorBows) — 5,552 bows a keystroke, 89 of them
    // one ulp off the page's own answer. Scoring in the staff's frame retired that.

    /// <summary>X coordinate of the start point.</summary>
    public double StartX { get; }

    /// <summary>Y of the start point in the page Y-up frame (up-positive = -device).</summary>
    public double StartYUp { get; }

    /// <summary>X coordinate of the end point.</summary>
    public double EndX { get; }

    /// <summary>Y of the end point in the page Y-up frame (up-positive = -device).</summary>
    public double EndYUp { get; }

    /// <summary>First control point (near start); .Y is page Y-up.</summary>
    public (double X, double Y) Control1 { get; }

    /// <summary>Second control point (near end); .Y is page Y-up.</summary>
    public (double X, double Y) Control2 { get; }

    /// <summary>Direction: true = curve up, false = curve down.</summary>
    public abstract bool CurveUp { get; }

    /// <summary>
    /// True if this layout is the right-side piece of a bow split at a system break
    /// (the left bound has been reattached to the system's left edge).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/item.cc:127-135 — break_status_dir == LEFT broken piece.</remarks>
    public bool IsBrokenLeft { get; }

    /// <summary>
    /// True if this layout is the left-side piece of a bow split at a system break
    /// (the right bound has been reattached to the system's right edge).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/item.cc:127-135 — break_status_dir == RIGHT broken piece.</remarks>
    public bool IsBrokenRight { get; }

    /// <summary>
    /// This bow moved <paramref name="dyUp"/> staff-spaces UP, everything else — X, the
    /// direction, the broken-piece flags, the model it carries — unchanged.
    /// </summary>
    /// <remarks>
    /// For a tie whose notes <c>@cross</c> moved onto another staff: it was scored about its
    /// voice's staff and stands about the other, so it moves by the distance between the two
    /// (<c>LayoutEngine.TiesOnTheirNotesStaff</c>). It no longer moves a bow carried from the
    /// preliminary pass — a bow is scored in its staff's own frame (the frame remark above),
    /// so the two passes' answers are one.
    /// </remarks>
    internal abstract BowLayout ShiftedUp(double dyUp);

    /// <summary>
    /// The four Y-up coordinates <see cref="ShiftedUp"/> produces.
    /// </summary>
    /// <remarks>
    /// ⚠️ In ONE place on purpose: the slur's override and the tie's rebuild different
    /// records, and "shift a bow" spelled twice is the second-model drift this repository
    /// keeps finding (RULES §7.7). Each override decides only how to REBUILD itself.
    /// </remarks>
    protected (double StartYUp, double EndYUp, (double X, double Y) Control1,
               (double X, double Y) Control2) ShiftedParts(double dyUp)
        => (StartYUp + dyUp, EndYUp + dyUp,
            (Control1.X, Control1.Y + dyUp), (Control2.X, Control2.Y + dyUp));

    protected BowLayout(
        double startX,
        double startYUp,
        double endX,
        double endYUp,
        (double X, double Y) control1,
        (double X, double Y) control2,
        bool isBrokenLeft,
        bool isBrokenRight)
    {
        StartX = startX;
        StartYUp = startYUp;
        EndX = endX;
        EndYUp = endYUp;
        Control1 = control1;
        Control2 = control2;
        IsBrokenLeft = isBrokenLeft;
        IsBrokenRight = isBrokenRight;
    }
}
