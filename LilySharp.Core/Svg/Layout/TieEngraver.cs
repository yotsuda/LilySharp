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
/// Calculates tie layout including Bezier control points.
/// All calculations are in staff spaces.
/// </summary>
/// <remarks>
/// REFERENCE / NOT IN PRODUCTION PATH. The live tie layout is
/// <see cref="TieFormattingProblem"/>, invoked by <c>ElementCoordinator</c>.
/// (Note: ElementCoordinator holds a <c>_tieEngraver</c> field but never calls
/// it — that field is dead.) This engraver is used only by tests
/// (<c>TieTests</c>); keep as a reference or retire it. See
/// LILYSHARP_STANDALONE_REVIEW.md §1.
/// LILYPOND-REF: lily/tie-formatting-problem.cc:1-1285 Tie_formatting_problem class
/// LILYPOND-REF: lily/bezier-bow.cc:1-132 Bezier_bow class
/// </remarks>
public sealed class TieEngraver
{
    private readonly TieDetails _details;

    public TieEngraver(TieDetails? details = null)
    {
        _details = details ?? TieDetails.Default;
    }

    /// <summary>
    /// Calculates the layout for a tie.
    /// All coordinates are in staff spaces.
    /// </summary>
    public TieLayout CalculateTieLayout(
        TieItem tie,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        // Calculate tie dimensions (all in staff spaces)
        double width = endX - startX;

        // Ensure minimum length
        if (width < _details.MinLength)
            width = _details.MinLength;

        // Calculate height based on width (Lilypond's slur_height algorithm)
        double height = CalculateTieHeight(width, _details.HeightLimit, _details.Ratio);

        // Calculate indent for control points
        double indent = CalculateIndent(width, _details.HeightLimit, _details.Ratio);

        // Apply gap from noteheads
        double adjustedStartX = startX + _details.XGap;
        double adjustedEndX = endX - _details.XGap;
        double adjustedWidth = adjustedEndX - adjustedStartX;

        // Recalculate for adjusted width
        if (adjustedWidth > 0)
        {
            height = CalculateTieHeight(adjustedWidth, _details.HeightLimit, _details.Ratio);
            indent = CalculateIndent(adjustedWidth, _details.HeightLimit, _details.Ratio);
        }

        // Direction: negative height for curve down
        double directedHeight = tie.CurveUp ? -height : height;

        // Calculate control points
        // The tie sits at the staff position, slightly offset
        double yOffset = 0.3;  // staff spaces
        double baseY = tie.CurveUp ? startY - yOffset : startY + yOffset;

        var control1 = (X: adjustedStartX + indent, Y: baseY + directedHeight);
        var control2 = (X: adjustedEndX - indent, Y: baseY + directedHeight);

        return new TieLayout(
            tie,
            adjustedStartX,
            baseY,
            adjustedEndX,
            baseY,
            control1,
            control2);
    }

    /// <summary>
    /// Calculates tie height based on width using LilyPond's atan formula.
    /// F0_1(x) = (2/π) * atan(π*x/2), then h = h_inf * F0_1(w * r_0 / h_inf).
    /// For small w: h ≈ r_0 * w. For large w: h → h_inf.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/bezier-bow.cc:28-38 F0_1() + slur_height()
    /// </remarks>
    private static double CalculateTieHeight(double width, double heightLimit, double ratio)
    {
        if (heightLimit < 0.001)
            return 0;

        double x = width * ratio / heightLimit;
        return heightLimit * (2.0 / Math.PI) * Math.Atan(Math.PI * x / 2.0);
    }

    /// <summary>
    /// Calculates indent for control points.
    /// Based on Lilypond's get_slur_indent_height function.
    /// </summary>
    private double CalculateIndent(double width, double heightLimit, double ratio)
    {
        double maxFraction = 1.0 / 3.1;
        double q = 2 * heightLimit / maxFraction;
        return 2 * heightLimit - q * q * maxFraction / (width + q);
    }

    /// <summary>
    /// Calculates layouts for multiple ties.
    /// </summary>
    public ImmutableArray<TieLayout> CalculateTieLayouts(
        IReadOnlyList<TieItem> ties,
        IReadOnlyList<double> startXPositions,
        IReadOnlyList<double> startYPositions,
        IReadOnlyList<double> endXPositions,
        IReadOnlyList<double> endYPositions)
    {
        var layouts = new List<TieLayout>();

        for (int i = 0; i < ties.Count; i++)
        {
            var layout = CalculateTieLayout(
                ties[i],
                startXPositions[i],
                startYPositions[i],
                endXPositions[i],
                endYPositions[i]);
            layouts.Add(layout);
        }

        return layouts.ToImmutableArray();
    }
}
