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

    /// <summary>X coordinate of the start point.</summary>
    public double StartX { get; }

    /// <summary>Y coordinate of the start point.</summary>
    public double StartY { get; }

    /// <summary>X coordinate of the end point.</summary>
    public double EndX { get; }

    /// <summary>Y coordinate of the end point.</summary>
    public double EndY { get; }

    /// <summary>First control point (near start).</summary>
    public (double X, double Y) Control1 { get; }

    /// <summary>Second control point (near end).</summary>
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
        double startY,
        double endX,
        double endY,
        (double X, double Y) control1,
        (double X, double Y) control2,
        bool isBrokenLeft,
        bool isBrokenRight)
    {
        StartX = startX;
        StartY = startY;
        EndX = endX;
        EndY = endY;
        Control1 = control1;
        Control2 = control2;
        IsBrokenLeft = isBrokenLeft;
        IsBrokenRight = isBrokenRight;
    }
}
