using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Calculates tie layout including Bezier control points.
/// All calculations are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tie-formatting-problem.cc:1-1286 Tie_formatting_problem class
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
