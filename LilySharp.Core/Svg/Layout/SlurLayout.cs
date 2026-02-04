namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a slur, including Bezier control points.
/// A slur is drawn as a cubic Bezier curve with 4 control points.
/// </summary>
public sealed record SlurLayout
{
    /// <summary>The slur model.</summary>
    public Model.SlurItem Slur { get; }

    /// <summary>X coordinate of the start point.</summary>
    public double StartX { get; }

    /// <summary>Y coordinate of the start point.</summary>
    public double StartY { get; }

    /// <summary>X coordinate of the end point.</summary>
    public double EndX { get; }

    /// <summary>Y coordinate of the end point.</summary>
    public double EndY { get; }

    /// <summary>First control point (near start).</summary>
    public (double X, double Y) Control1 { get; }

    /// <summary>Second control point (near end).</summary>
    public (double X, double Y) Control2 { get; }

    /// <summary>Direction: true = curve up, false = curve down.</summary>
    public bool CurveUp => Slur.CurveUp;

    public SlurLayout(
        Model.SlurItem slur,
        double startX,
        double startY,
        double endX,
        double endY,
        (double X, double Y) control1,
        (double X, double Y) control2)
    {
        Slur = slur;
        StartX = startX;
        StartY = startY;
        EndX = endX;
        EndY = endY;
        Control1 = control1;
        Control2 = control2;
    }
}