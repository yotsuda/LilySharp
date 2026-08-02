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
/// Layout information for a tie, including Bezier control points.
/// A tie is drawn as a cubic Bezier curve with 4 control points.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tie.cc — Tie grob (Bezier control points)
/// LILYPOND-REF: lily/tie-formatting-problem.cc — optimization of tie shape
/// Control points are computed by TieFormattingProblem; this record stores the result.
/// </remarks>
internal sealed record TieLayout : BowLayout
{
    /// <summary>The tie model.</summary>
    public Model.TieItem Tie { get; }

    /// <summary>Direction: true = curve up, false = curve down.</summary>
    /// <remarks>
    /// The direction the SEARCH settled on, not one the tie arrived carrying: an ordinary
    /// tie reaches <see cref="TieFormattingProblem"/> with none
    /// (<see cref="Model.TieItem.ForcedCurveUp"/> null) and every candidate is a direction as
    /// much as a position. This used to read <c>Tie.CurveUp</c>, which meant that whenever
    /// the scorer chose the other way the drawn bow and this flag disagreed — and the flag is
    /// what the skyline and the renderer's taper read.
    /// </remarks>
    public override bool CurveUp { get; }

    public TieLayout(
        Model.TieItem tie,
        double startX,
        double startY,
        double endX,
        double endY,
        (double X, double Y) control1,
        (double X, double Y) control2,
        bool curveUp,
        bool isBrokenLeft = false,
        bool isBrokenRight = false)
        : base(startX, startY, endX, endY, control1, control2, isBrokenLeft, isBrokenRight)
    {
        Tie = tie;
        CurveUp = curveUp;
    }
}