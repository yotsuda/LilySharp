namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a tie, including Bezier control points.
/// A tie is drawn as a cubic Bezier curve with 4 control points.
/// </summary>
public sealed record TieLayout
{
    /// <summary>The tie model.</summary>
    public Model.TieItem Tie { get; }

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
    public bool CurveUp => Tie.CurveUp;

    public TieLayout(
        Model.TieItem tie,
        double startX,
        double startY,
        double endX,
        double endY,
        (double X, double Y) control1,
        (double X, double Y) control2)
    {
        Tie = tie;
        StartX = startX;
        StartY = startY;
        EndX = endX;
        EndY = endY;
        Control1 = control1;
        Control2 = control2;
    }
}