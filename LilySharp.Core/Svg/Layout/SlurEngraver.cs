using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Calculates slur layout including Bezier control points.
/// All calculations are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/slur-scoring.cc:1-906 Slur_scoring class
/// LILYPOND-REF: lily/bezier-bow.cc:1-132 Bezier_bow class
/// </remarks>
public sealed class SlurEngraver
{
    private readonly SlurScoreParameters _parameters;

    public SlurEngraver(SlurScoreParameters? parameters = null)
    {
        _parameters = parameters ?? SlurScoreParameters.Default;
    }

    /// <summary>
    /// Calculates the layout for a slur.
    /// All coordinates are in staff spaces.
    /// </summary>
    public SlurLayout CalculateSlurLayout(
        SlurItem slur,
        double startX,
        double startY,
        double endX,
        double endY)
    {
        // Calculate slur dimensions (all in staff spaces)
        double width = endX - startX;
        double heightDiff = endY - startY;

        // Calculate arc height based on width (Lilypond's slur_height algorithm)
        double arcHeight = CalculateSlurHeight(width, _parameters.HeightLimit, _parameters.Ratio);

        // Calculate indent for control points
        double indent = CalculateIndent(width, _parameters.HeightLimit, _parameters.Ratio);

        // Apply gap from noteheads
        double adjustedStartX = startX + _parameters.FreeHeadDistance;
        double adjustedEndX = endX - _parameters.FreeHeadDistance;
        double adjustedWidth = adjustedEndX - adjustedStartX;

        // Recalculate for adjusted width
        if (adjustedWidth > 0)
        {
            arcHeight = CalculateSlurHeight(adjustedWidth, _parameters.HeightLimit, _parameters.Ratio);
            indent = CalculateIndent(adjustedWidth, _parameters.HeightLimit, _parameters.Ratio);
        }

        // Direction: negative height for curve down
        double directedHeight = slur.CurveUp ? -arcHeight : arcHeight;

        // Calculate base Y positions with offset from noteheads
        double offset = 0.4;  // staff spaces
        double baseStartY = slur.CurveUp ? startY - offset : startY + offset;
        double baseEndY = slur.CurveUp ? endY - offset : endY + offset;

        // Control points: Y follows the start-end line with arc height offset
        // LILYPOND-REF: lily/bezier-bow.cc creates flat bow then applies shear for dy
        double cpT1 = (adjustedWidth > 0) ? indent / adjustedWidth : 0;
        double cpT2 = (adjustedWidth > 0) ? 1 - indent / adjustedWidth : 1;
        var control1 = (X: adjustedStartX + indent, Y: baseStartY + cpT1 * (baseEndY - baseStartY) + directedHeight);
        var control2 = (X: adjustedEndX - indent, Y: baseStartY + cpT2 * (baseEndY - baseStartY) + directedHeight);

        return new SlurLayout(
            slur,
            adjustedStartX,
            baseStartY,
            adjustedEndX,
            baseEndY,
            control1,
            control2);
    }

    /// <summary>
    /// Calculates slur arc height using LilyPond's atan formula.
    /// F0_1(x) = (2/π) * atan(π*x/2), then h = h_inf * F0_1(w * r_0 / h_inf).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/bezier-bow.cc:28-38 F0_1() + slur_height()
    /// </remarks>
    private static double CalculateSlurHeight(double width, double heightLimit, double ratio)
    {
        if (heightLimit < 0.001)
            return 0;

        double x = width * ratio / heightLimit;
        return heightLimit * (2.0 / Math.PI) * Math.Atan(Math.PI * x / 2.0);
    }

    /// <summary>
    /// Calculates indent for control points.
    /// </summary>
    private double CalculateIndent(double width, double heightLimit, double ratio)
    {
        double maxFraction = 1.0 / 3.1;
        double q = 2 * heightLimit / maxFraction;
        return 2 * heightLimit - q * q * maxFraction / (width + q);
    }

    /// <summary>
    /// Calculates layouts for multiple slurs.
    /// </summary>
    public ImmutableArray<SlurLayout> CalculateSlurLayouts(
        IReadOnlyList<SlurItem> slurs,
        IReadOnlyList<double> startXPositions,
        IReadOnlyList<double> startYPositions,
        IReadOnlyList<double> endXPositions,
        IReadOnlyList<double> endYPositions)
    {
        var layouts = new List<SlurLayout>();

        for (int i = 0; i < slurs.Count; i++)
        {
            var layout = CalculateSlurLayout(
                slurs[i],
                startXPositions[i],
                startYPositions[i],
                endXPositions[i],
                endYPositions[i]);
            layouts.Add(layout);
        }

        return layouts.ToImmutableArray();
    }
}
