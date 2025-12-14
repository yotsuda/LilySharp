using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Calculates slur layout including Bezier control points.
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
    /// </summary>
    public SlurLayout CalculateSlurLayout(
        SlurItem slur,
        double startX,
        double startY,
        double endX,
        double endY,
        double staffSpaceSize)
    {
        // Calculate slur dimensions
        double width = endX - startX;
        double heightDiff = endY - startY;
        
        // Calculate arc height based on width (Lilypond's slur_height algorithm)
        double heightLimit = _parameters.HeightLimit * staffSpaceSize;
        double ratio = _parameters.Ratio;
        double arcHeight = CalculateSlurHeight(width, heightLimit, ratio);
        
        // Calculate indent for control points
        double indent = CalculateIndent(width, heightLimit, ratio);
        
        // Apply gap from noteheads
        double xGap = _parameters.FreeHeadDistance * staffSpaceSize;
        double adjustedStartX = startX + xGap;
        double adjustedEndX = endX - xGap;
        double adjustedWidth = adjustedEndX - adjustedStartX;
        
        // Recalculate for adjusted width
        if (adjustedWidth > 0)
        {
            arcHeight = CalculateSlurHeight(adjustedWidth, heightLimit, ratio);
            indent = CalculateIndent(adjustedWidth, heightLimit, ratio);
        }
        
        // Direction: negative height for curve down
        double directedHeight = slur.CurveUp ? -arcHeight : arcHeight;
        
        // Calculate base Y positions with offset from noteheads
        double offset = staffSpaceSize * 0.4;
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
        IReadOnlyList<double> endYPositions,
        double staffSpaceSize)
    {
        var layouts = new List<SlurLayout>();
        
        for (int i = 0; i < slurs.Count; i++)
        {
            var layout = CalculateSlurLayout(
                slurs[i],
                startXPositions[i],
                startYPositions[i],
                endXPositions[i],
                endYPositions[i],
                staffSpaceSize);
            layouts.Add(layout);
        }
        
        return layouts.ToImmutableArray();
    }
}