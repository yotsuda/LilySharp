using System.Text;

namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Renders brace (curly bracket) for grand staff using bezier curves.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// The brace is drawn using cubic bezier curves to create the classic
/// curly bracket shape used in piano/organ scores.
/// 
/// Brace anatomy:
/// - Top curve: from top endpoint curving inward
/// - Middle point: the sharp tip pointing right
/// - Bottom curve: from middle curving to bottom endpoint
/// </remarks>
public static class BraceRenderer
{
    // Brace shape parameters (in staff spaces)
    private const double BraceWidth = 1.0;           // Horizontal extent
    private const double TipOffset = 0.8;            // How far the tip extends right
    private const double CurveControl = 0.4;         // Control point offset for bezier
    private const double StrokeWidth = 0.15;         // Stroke width in staff spaces
    
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
        
        // Brace shape parameters
        double width = BraceWidth;
        double tipX = x + TipOffset;
        double controlOffset = CurveControl * (height / 8); // Scale with height
        
        // Bezier control points
        // Top curve: from (x, yTop) to (tipX, yMid)
        double topCtrl1X = x + width * 0.2;
        double topCtrl1Y = yTop + height * 0.1;
        double topCtrl2X = tipX;
        double topCtrl2Y = yMid - height * 0.2;
        
        // Bottom curve: from (tipX, yMid) to (x, yBottom)
        double botCtrl1X = tipX;
        double botCtrl1Y = yMid + height * 0.2;
        double botCtrl2X = x + width * 0.2;
        double botCtrl2Y = yBottom - height * 0.1;
        
        var sb = new StringBuilder();
        sb.Append($"<path d=\"");
        
        // Move to top
        sb.Append($"M {x:F2} {yTop:F2} ");
        
        // Top curve to middle
        sb.Append($"C {topCtrl1X:F2} {topCtrl1Y:F2}, {topCtrl2X:F2} {topCtrl2Y:F2}, {tipX:F2} {yMid:F2} ");
        
        // Bottom curve from middle
        sb.Append($"C {botCtrl1X:F2} {botCtrl1Y:F2}, {botCtrl2X:F2} {botCtrl2Y:F2}, {x:F2} {yBottom:F2}");
        
        sb.Append($"\" stroke=\"black\" stroke-width=\"{StrokeWidth:F2}\" fill=\"none\" />");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Calculates the width required for a brace in staff spaces.
    /// </summary>
    public static double GetBraceWidth()
    {
        return TipOffset + 0.5; // Extra padding
    }
}
