namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A building in a horizontal skyline - represents a region with Y extent and sloped edge.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/skyline.cc:32-46, 98-176 Building struct
/// 
/// A building is defined by:
/// - Y range: [YBottom, YTop]
/// - Edge line: x = Slope * y + XIntercept
/// 
/// The X position at any y is: X(y) = Slope * y + XIntercept
/// For vertical buildings (most common), Slope = 0.
/// </remarks>
public readonly struct HorizontalBuilding
{
    public double YBottom { get; }
    public double YTop { get; }
    public double Slope { get; }
    public double XIntercept { get; }
    
    /// <summary>
    /// Creates a sloped building.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:98-105</remarks>
    public HorizontalBuilding(double yBottom, double xAtBottom, double xAtTop, double yTop)
    {
        YBottom = yBottom;
        YTop = yTop;
        
        if (double.IsInfinity(yBottom) || double.IsInfinity(yTop))
        {
            Slope = 0;
            XIntercept = xAtBottom;
        }
        else if (Math.Abs(xAtBottom - xAtTop) < 1e-10)
        {
            Slope = 0;
            XIntercept = xAtBottom;
        }
        else
        {
            double length = yTop - yBottom;
            if (Math.Abs(length) < 1e-10)
            {
                Slope = 0;
                XIntercept = Math.Max(xAtBottom, xAtTop);
            }
            else
            {
                Slope = (xAtTop - xAtBottom) / length;
                if (Math.Abs(Slope) > 1e6)
                {
                    Slope = 0;
                    XIntercept = Math.Max(xAtBottom, xAtTop);
                }
                else
                {
                    XIntercept = xAtBottom - Slope * yBottom;
                }
            }
        }
    }
    
    /// <summary>
    /// Creates a vertical building (constant X).
    /// </summary>
    public HorizontalBuilding(double yBottom, double yTop, double x)
        : this(yBottom, x, x, yTop)
    {
    }
    
    /// <summary>
    /// Creates a building from a bounding box.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:107-113
    /// Uses LilyPond sign convention:
    /// - RIGHT skyline (sky=+1): x = +xRight (positive for rightward)
    /// - LEFT skyline (sky=-1): x = -xLeft (negative for leftward)
    /// </remarks>
    public static HorizontalBuilding FromBox(double yBottom, double yTop, double xLeft, double xRight, HorizontalDirection direction)
    {
        double x = direction == HorizontalDirection.Right ? xRight : -xLeft;
        return new HorizontalBuilding(yBottom, yTop, x);
    }
    
    /// <summary>
    /// Returns the X position at the given Y coordinate.
    /// </summary>
    public double X(double y)
    {
        return double.IsInfinity(y) ? XIntercept : Slope * y + XIntercept;
    }
    
    /// <summary>
    /// Computes the Y coordinate where this building intersects with another.
    /// </summary>
    public double IntersectionY(HorizontalBuilding other)
    {
        double slopeDelta = other.Slope - Slope;
        if (Math.Abs(slopeDelta) < 1e-4)
            return Math.Max(YBottom, other.YBottom);
        return (XIntercept - other.XIntercept) / slopeDelta;
    }
    
    /// <summary>
    /// Returns true if this building is "above" the other at the given Y coordinate.
    /// For RIGHT skyline, "above" means larger X (more to the right).
    /// For LEFT skyline, "above" means smaller X (more to the left, stored as more negative).
    /// </summary>
    public bool Above(HorizontalBuilding other, double y)
    {
        if (double.IsInfinity(XIntercept) || double.IsInfinity(other.XIntercept) || double.IsInfinity(y))
            return XIntercept > other.XIntercept;
        return (Slope - other.Slope) * y + XIntercept > other.XIntercept;
    }
    
    /// <summary>
    /// Creates a copy with modified Y range.
    /// </summary>
    public HorizontalBuilding WithYRange(double newBottom, double newTop)
    {
        double xAtBottom = X(newBottom);
        double xAtTop = X(newTop);
        return new HorizontalBuilding(newBottom, xAtBottom, xAtTop, newTop);
    }
    
    public override string ToString()
        => $"HBuilding[{YBottom:F2}, {YTop:F2}] x = {Slope:F4}y + {XIntercept:F2}";
}

/// <summary>
/// Direction for horizontal skylines.
/// </summary>
/// <remarks>
/// LILYPOND-REF: Uses sky = +1 for RIGHT, -1 for LEFT
/// </remarks>
public enum HorizontalDirection
{
    Right = 1,
    Left = -1
}

/// <summary>
/// A horizontal skyline - the outline of a set of buildings as seen from the right or left.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/skyline.hh:48-100 Skyline class
/// 
/// Used for note spacing collision detection.
/// - RIGHT skyline: rightmost X at each Y position
/// - LEFT skyline: leftmost X at each Y position
/// </remarks>
public sealed class HorizontalSkyline
{
    private readonly List<HorizontalBuilding> _buildings;
    private readonly HorizontalDirection _direction;
    
    private const double NegativeInfinity = double.NegativeInfinity;
    private const double PositiveInfinity = double.PositiveInfinity;
    
    public HorizontalSkyline(HorizontalDirection direction)
    {
        _buildings = new List<HorizontalBuilding>();
        _direction = direction;
    }
    
    private HorizontalSkyline(List<HorizontalBuilding> buildings, HorizontalDirection direction)
    {
        _buildings = buildings;
        _direction = direction;
    }
    
    public HorizontalDirection Direction => _direction;
    public bool IsEmpty => _buildings.Count == 0;
    public IReadOnlyList<HorizontalBuilding> Buildings => _buildings;
    
    /// <summary>
    /// Creates a skyline from a single bounding box.
    /// </summary>
    public static HorizontalSkyline FromBox(double yBottom, double yTop, double xLeft, double xRight, HorizontalDirection direction)
    {
        var building = HorizontalBuilding.FromBox(yBottom, yTop, xLeft, xRight, direction);
        var skyline = new HorizontalSkyline(direction);
        skyline._buildings.Add(building);
        return skyline;
    }
    
    /// <summary>
    /// Creates a skyline from multiple bounding boxes.
    /// </summary>
    public static HorizontalSkyline FromBoxes(IEnumerable<(double YBottom, double YTop, double XLeft, double XRight)> boxes, HorizontalDirection direction)
    {
        var skyline = new HorizontalSkyline(direction);
        foreach (var (yBottom, yTop, xLeft, xRight) in boxes)
        {
            var building = HorizontalBuilding.FromBox(yBottom, yTop, xLeft, xRight, direction);
            skyline._buildings.Add(building);
        }
        return skyline;
    }
    
    /// <summary>
    /// Creates a skyline from a sloped region (e.g., beam edge).
    /// </summary>
    public static HorizontalSkyline FromSlope(double yBottom, double xBottom, double yTop, double xTop, HorizontalDirection direction)
    {
        int sky = (int)direction;
        return new HorizontalSkyline(new List<HorizontalBuilding>
        {
            new HorizontalBuilding(yBottom, sky * xBottom, sky * xTop, yTop)
        }, direction);
    }
    
    /// <summary>
    /// Merges another skyline into this one.
    /// </summary>
    public void Merge(HorizontalSkyline other)
    {
        if (other._direction != _direction)
            throw new ArgumentException("Cannot merge skylines with different directions");
        
        if (other.IsEmpty) return;
        
        if (IsEmpty)
        {
            _buildings.AddRange(other._buildings);
            return;
        }
        
        _buildings.AddRange(other._buildings);
    }
    
    /// <summary>
    /// Calculates the distance between this skyline and another of opposite direction.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/skyline.cc:617-649 internal_distance()</remarks>
    public double Distance(HorizontalSkyline other)
    {
        if (_direction == other._direction)
            throw new ArgumentException("Distance requires skylines with opposite directions");
        
        if (IsEmpty || other.IsEmpty)
            return NegativeInfinity;
        
        double maxDistance = NegativeInfinity;
        
        foreach (var b1 in _buildings)
        {
            foreach (var b2 in other._buildings)
            {
                double overlapBottom = Math.Max(b1.YBottom, b2.YBottom);
                double overlapTop = Math.Min(b1.YTop, b2.YTop);
                
                if (overlapBottom < overlapTop)
                {
                    double[] ySamples = { overlapBottom, overlapTop, (overlapBottom + overlapTop) / 2 };
                    
                    foreach (double y in ySamples)
                    {
                        if (y >= overlapBottom && y <= overlapTop)
                        {
                            double x1 = b1.X(y);
                            double x2 = b2.X(y);
                            double dist = x1 + x2;
                            maxDistance = Math.Max(maxDistance, dist);
                        }
                    }
                    
                    if (Math.Abs(b1.Slope - b2.Slope) > 1e-6)
                    {
                        double iy = b1.IntersectionY(b2);
                        if (iy > overlapBottom && iy < overlapTop)
                        {
                            maxDistance = Math.Max(maxDistance, b1.X(iy) + b2.X(iy));
                        }
                    }
                }
            }
        }
        
        return maxDistance;
    }
    
    /// <summary>
    /// Shifts the skyline horizontally.
    /// </summary>
    public void Shift(double dx)
    {
        int sky = (int)_direction;
        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            double newIntercept = b.XIntercept + sky * dx;
            _buildings[i] = new HorizontalBuilding(b.YBottom, b.YTop, newIntercept);
        }
    }
    
    /// <summary>
    /// Returns the X position at a specific Y coordinate in real coordinates.
    /// </summary>
    public double X(double y)
    {
        foreach (var b in _buildings)
        {
            if (y >= b.YBottom && y <= b.YTop)
            {
                int sky = (int)_direction;
                return sky * b.X(y);
            }
        }
        return _direction == HorizontalDirection.Right ? NegativeInfinity : PositiveInfinity;
    }
}
