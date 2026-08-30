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
/// Layout information for a slur, including Bezier control points.
/// A slur is drawn as a cubic Bezier curve with 4 control points.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/slur.cc — Slur grob (Bezier control points)
/// LILYPOND-REF: lily/slur-scoring.cc — scoring/optimization of slur shape
/// Control points are computed by SlurScoringProblem; this record stores the result.
/// </remarks>
internal sealed record SlurLayout : BowLayout
{
    /// <summary>The slur model.</summary>
    public Model.SlurItem Slur { get; init; }

    /// <summary>Direction: true = curve up, false = curve down.</summary>
    public override bool CurveUp => Slur.CurveUp;

    public SlurLayout(
        Model.SlurItem slur,
        double startX,
        double startY,
        double endX,
        double endY,
        (double X, double Y) control1,
        (double X, double Y) control2,
        bool isBrokenLeft = false,
        bool isBrokenRight = false)
        : base(startX, startY, endX, endY, control1, control2, isBrokenLeft, isBrokenRight)
    {
        Slur = slur;
    }
}