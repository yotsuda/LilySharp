using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Calculates tie layout including Bezier control points.
/// Based on Lilypond's tie-formatting-problem.cc and bezier-bow.cc
/// </summary>
public sealed class TieEngraver
{
    private readonly TieDetails _details;
    
    public TieEngraver(TieDetails? details = null)
    {
        _details = details ?? TieDetails.Default;
    }
    
    /// <summary>
    /// Calculates the layout for a tie.
    /// </summary>
    public TieLayout CalculateTieLayout(
        TieItem tie,
        double startX,
        double startY,
        double endX,
        double endY,
        double staffSpaceSize)
    {
        // Calculate tie dimensions
        double width = endX - startX;
        
        // Ensure minimum length
        double minLengthPx = _details.MinLength * staffSpaceSize;
        if (width < minLengthPx)
            width = minLengthPx;
        
        // Calculate height based on width (Lilypond's slur_height algorithm)
        double heightLimit = _details.HeightLimit * staffSpaceSize;
        double ratio = _details.Ratio;
        double height = CalculateTieHeight(width, heightLimit, ratio);
        
        // Calculate indent for control points
        double indent = CalculateIndent(width, heightLimit, ratio);
        
        // Apply gap from noteheads
        double xGap = _details.XGap * staffSpaceSize;
        double adjustedStartX = startX + xGap;
        double adjustedEndX = endX - xGap;
        double adjustedWidth = adjustedEndX - adjustedStartX;
        
        // Recalculate for adjusted width
        if (adjustedWidth > 0)
        {
            height = CalculateTieHeight(adjustedWidth, heightLimit, ratio);
            indent = CalculateIndent(adjustedWidth, heightLimit, ratio);
        }
        
        // Direction: negative height for curve down
        double directedHeight = tie.CurveUp ? -height : height;
        
        // Calculate control points
        // The tie sits at the staff position, slightly offset
        double baseY = tie.CurveUp ? startY - staffSpaceSize * 0.3 : startY + staffSpaceSize * 0.3;
        
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
    /// Calculates tie height based on width.
    /// Based on Lilypond's slur_height function in bezier-bow.cc
    /// </summary>
    private double CalculateTieHeight(double width, double heightLimit, double ratio)
    {
        // h = h_inf * tanh(r * w / h_inf)
        // For small w: h ≈ r * w
        // For large w: h → h_inf
        
        if (heightLimit < 0.001)
            return 0;
        
        double x = ratio * width / heightLimit;
        return heightLimit * Math.Tanh(x);
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
        IReadOnlyList<double> endYPositions,
        double staffSpaceSize)
    {
        var layouts = new List<TieLayout>();
        
        for (int i = 0; i < ties.Count; i++)
        {
            var layout = CalculateTieLayout(
                ties[i],
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