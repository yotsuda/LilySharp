using System.Text;

namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Renders barlines that connect multiple staves in a system.
/// </summary>
/// <remarks>
/// In a grand staff or staff group, barlines extend from the top
/// of the upper staff to the bottom of the lower staff, visually
/// connecting them as a unit.
/// </remarks>
public static class SystemBarlineRenderer
{
    /// <summary>
    /// Renders a system barline connecting multiple staves.
    /// </summary>
    /// <param name="x">X position of the barline</param>
    /// <param name="yTop">Top of the uppermost staff (top staff line)</param>
    /// <param name="yBottom">Bottom of the lowermost staff (bottom staff line)</param>
    /// <param name="thickness">Line thickness in staff spaces</param>
    /// <returns>SVG line element string</returns>
    public static string RenderSystemBarline(double x, double yTop, double yBottom, double thickness = 1.0)
    {
        return $"""<line x1="{x:F2}" y1="{yTop:F2}" x2="{x:F2}" y2="{yBottom:F2}" stroke="black" stroke-width="{thickness:F2}" />""";
    }
    
    /// <summary>
    /// Renders a double barline (end of section/piece).
    /// </summary>
    public static string RenderDoubleBarline(double x, double yTop, double yBottom, double thickness = 1.0, double spacing = 3.0)
    {
        var sb = new StringBuilder();
        sb.AppendLine(RenderSystemBarline(x, yTop, yBottom, thickness));
        sb.AppendLine(RenderSystemBarline(x + spacing, yTop, yBottom, thickness * 2));
        return sb.ToString();
    }
    
    /// <summary>
    /// Renders a repeat barline (with dots).
    /// </summary>
    public static string RenderRepeatBarline(double x, double yTop, double yBottom, 
        double staffSpace, bool dotsOnLeft = false, double thickness = 1.0)
    {
        var sb = new StringBuilder();
        double midY = (yTop + yBottom) / 2;
        double dotRadius = staffSpace * 0.2;
        double dotSpacing = staffSpace * 0.5;
        
        // Barlines
        if (dotsOnLeft)
        {
            // End repeat: dots, thin, thick
            double dotX = x - staffSpace;
            sb.AppendLine($"""<circle cx="{dotX:F2}" cy="{midY - dotSpacing:F2}" r="{dotRadius:F2}" fill="black" />""");
            sb.AppendLine($"""<circle cx="{dotX:F2}" cy="{midY + dotSpacing:F2}" r="{dotRadius:F2}" fill="black" />""");
            sb.AppendLine(RenderSystemBarline(x, yTop, yBottom, thickness));
            sb.AppendLine(RenderSystemBarline(x + 3, yTop, yBottom, thickness * 2));
        }
        else
        {
            // Start repeat: thick, thin, dots
            sb.AppendLine(RenderSystemBarline(x, yTop, yBottom, thickness * 2));
            sb.AppendLine(RenderSystemBarline(x + 3, yTop, yBottom, thickness));
            double dotX = x + 3 + staffSpace;
            sb.AppendLine($"""<circle cx="{dotX:F2}" cy="{midY - dotSpacing:F2}" r="{dotRadius:F2}" fill="black" />""");
            sb.AppendLine($"""<circle cx="{dotX:F2}" cy="{midY + dotSpacing:F2}" r="{dotRadius:F2}" fill="black" />""");
        }
        
        return sb.ToString();
    }
}