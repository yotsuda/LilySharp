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

    // COORDINATE FRAME: every Y here is stored in the LilyPond-native PAGE Y-up
    // frame — up-positive, the exact negation of the shared absolute device Y
    // (yUp = -yDevice). This is the frame both bow scorers already reason in
    // (SlurScoringProblem/slur-scoring.cc and TieFormattingProblem/
    // tie-formatting-problem.cc, sign-for-sign), so each CreateLayout stores the
    // scored Y verbatim with no exit negation; the renderer emits these page-Y-up
    // and its YFlipDrawingContext performs the single device flip. The
    // Control1/Control2 tuples' .Y members are page Y-up too.

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
