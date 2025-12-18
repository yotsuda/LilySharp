using System.Text;

namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Renders brace (curly bracket) for grand staff using bezier curves.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// Based on LilyPond's feta-braces.mf design.
/// The brace is drawn using cubic bezier curves to create the classic
/// curly bracket shape used in piano/organ scores.
/// 
/// LILYPOND-REF: mf/feta-braces.mf
/// Brace parameters scale with height to maintain proportions.
/// </remarks>
public static class BraceRenderer
{
    // Brace shape parameters based on LilyPond defaults (in staff spaces)
    // LILYPOND-REF: mf/feta-braces.mf lines 93-96
    private const double MinWidth = 0.5;              // Minimum width for small braces
    private const double MaxWidth = 5.0;              // Maximum width for large braces
    private const double MinThin = 0.05;              // Minimum stroke thickness
    private const double MaxThin = 0.3125;            // Maximum stroke thickness
    
    /// <summary>
    /// Renders a brace SVG path.
    /// All coordinates are in staff spaces.
    /// </summary>
    /// <param name="x">X position of the brace (left edge) in staff spaces</param>
    /// <param name="yTop">Y position of the top of the brace in staff spaces</param>
    /// <param name="yBottom">Y position of the bottom of the brace in staff spaces</param>
    /// <returns>SVG path element string</returns>
    public static string RenderBrace(double x, double yTop, double yBottom)
    {
        double height = yBottom - yTop;
        double yMid = (yTop + yBottom) / 2;
        
        // Scale width and thickness based on height (like LilyPond)
        // LILYPOND-REF: mf/feta-braces.mf line 115
        double heightRatio = Math.Clamp(height / 20.0, 0.0, 1.0);
        double width = MinWidth + (MaxWidth - MinWidth) * heightRatio;
        double strokeWidth = MinThin + (MaxThin - MinThin) * heightRatio;
        
        // LILYPOND-REF: mf/feta-braces.mf lines 44-45
        double thin = 2 * strokeWidth;
        double thick = 0.5 * width;
        
        // Key points
        double tipX = x + width;  // The rightmost point (tip)
        
        // Bezier control points based on LilyPond's algorithm
        // The brace has two symmetric halves meeting at the tip
        
        // Top half: from (x, yTop) curving to (tipX, yMid)
        double topCtrl1X = x + width * 0.2;
        double topCtrl1Y = yTop + height * 0.08;
        double topCtrl2X = tipX - width * 0.1;
        double topCtrl2Y = yMid - height * 0.15;
        
        // Bottom half: from (tipX, yMid) curving to (x, yBottom)
        double botCtrl1X = tipX - width * 0.1;
        double botCtrl1Y = yMid + height * 0.15;
        double botCtrl2X = x + width * 0.2;
        double botCtrl2Y = yBottom - height * 0.08;
        
        var sb = new StringBuilder();
        sb.Append($"<path d=\"");
        
        // Move to top
        sb.Append($"M {x:F2} {yTop:F2} ");
        
        // Top curve to middle tip
        sb.Append($"C {topCtrl1X:F2} {topCtrl1Y:F2}, {topCtrl2X:F2} {topCtrl2Y:F2}, {tipX:F2} {yMid:F2} ");
        
        // Bottom curve from middle tip
        sb.Append($"C {botCtrl1X:F2} {botCtrl1Y:F2}, {botCtrl2X:F2} {botCtrl2Y:F2}, {x:F2} {yBottom:F2}");
        
        sb.Append($"\" stroke=\"black\" stroke-width=\"{strokeWidth:F2}\" fill=\"none\" />");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Calculates the width required for a brace in staff spaces.
    /// </summary>
    /// <param name="height">Height of the brace in staff spaces.</param>
    public static double GetBraceWidth(double height = 11.0)
    {
        // Default height is approximately two staves (4 + 5 + 4 = 13, but usually 11)
        double heightRatio = Math.Clamp(height / 20.0, 0.0, 1.0);
        double width = MinWidth + (MaxWidth - MinWidth) * heightRatio;
        return width + 0.5; // Extra padding
    }
    
    /// <summary>
    /// Calculates the width required for a brace in staff spaces.
    /// </summary>
    public static double GetBraceWidth()
    {
        return GetBraceWidth(11.0);
    }
}
