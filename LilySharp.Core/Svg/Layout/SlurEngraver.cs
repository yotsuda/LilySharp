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

        // Calculate midpoint Y for control point height
        double midY = (baseStartY + baseEndY) / 2;

        // Control points
        var control1 = (X: adjustedStartX + indent, Y: midY + directedHeight);
        var control2 = (X: adjustedEndX - indent, Y: midY + directedHeight);

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
    /// Calculates slur arc height based on width.
    /// Based on Lilypond's slur_height function in bezier-bow.cc
    /// </summary>
    private double CalculateSlurHeight(double width, double heightLimit, double ratio)
    {
        if (heightLimit < 0.001)
            return 0;

        double x = ratio * width / heightLimit;
        return heightLimit * Math.Tanh(x);
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
