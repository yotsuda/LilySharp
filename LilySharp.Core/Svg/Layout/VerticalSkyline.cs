namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A building in a vertical skyline - represents a rectangular region with X extent and Y height.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/skyline.cc:32-46 Building struct
/// LilyPond's Building supports sloped roofs, but for typical music notation,
/// horizontal buildings (constant height) are sufficient.
/// </remarks>
public readonly struct Building
{
    public double XLeft { get; }
    public double XRight { get; }
    public double Height { get; }
    
    public Building(double xLeft, double xRight, double height)
    {
        XLeft = xLeft;
        XRight = xRight;
        Height = height;
    }
    
    /// <summary>
    /// Creates a building from a bounding box.
    /// </summary>
    public static Building FromBox(double xLeft, double xRight, double yBottom, double yTop, VerticalDirection direction)
    {
        // LILYPOND-REF: lily/skyline.cc:107-113 Building::Building(Box const &b, ...)
        // For UP skyline: height = yTop (positive = higher)
        // For DOWN skyline: height = -yBottom (positive = lower, stored as negative)
        double height = direction == VerticalDirection.Up ? yTop : -yBottom;
        return new Building(xLeft, xRight, height);
    }
}

/// <summary>
/// Direction for vertical skylines.
/// </summary>
public enum VerticalDirection
{
    Up = 1,
    Down = -1
}

/// <summary>
/// A vertical skyline - the outline of a set of rectangles as seen from above (UP) or below (DOWN).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/skyline.hh:48-100 Skyline class
/// LILYPOND-REF: lily/skyline.cc:1-700 Skyline implementation
/// </remarks>
public sealed class VerticalSkyline
{
    private readonly List<Building> _buildings;
    private readonly VerticalDirection _direction;
    
    public VerticalSkyline(VerticalDirection direction)
    {
        _buildings = new List<Building>();
        _direction = direction;
    }
    
    private VerticalSkyline(List<Building> buildings, VerticalDirection direction)
    {
        _buildings = buildings;
        _direction = direction;
    }
    
    public VerticalDirection Direction => _direction;
    public bool IsEmpty => _buildings.Count == 0;
    
    public static VerticalSkyline FromBox(double xLeft, double xRight, double yBottom, double yTop, VerticalDirection direction)
    {
        var building = Building.FromBox(xLeft, xRight, yBottom, yTop, direction);
        return new VerticalSkyline(new List<Building> { building }, direction);
    }
    
    public void Merge(VerticalSkyline other)
    {
        if (other._direction != _direction)
            throw new ArgumentException("Cannot merge skylines with different directions");
        
        if (other.IsEmpty) return;
        
        if (IsEmpty) { _buildings.AddRange(other._buildings); return; }
        
        _buildings.AddRange(other._buildings);
        var merged = MergeBuildings(_buildings, _direction);
        _buildings.Clear();
        _buildings.AddRange(merged);
    }
    
    public void Raise(double amount)
    {
        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            _buildings[i] = new Building(b.XLeft, b.XRight, b.Height + (int)_direction * amount);
        }
    }
    
    public void Shift(double amount)
    {
        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            _buildings[i] = new Building(b.XLeft + amount, b.XRight + amount, b.Height);
        }
    }
    
    public void SetMinimumHeight(double height)
    {
        double targetHeight = (int)_direction * height;
        
        if (_buildings.Count == 0)
        {
            _buildings.Add(new Building(double.NegativeInfinity, double.PositiveInfinity, targetHeight));
            return;
        }
        
        for (int i = 0; i < _buildings.Count; i++)
        {
            var b = _buildings[i];
            if (_direction == VerticalDirection.Up)
            {
                if (b.Height < targetHeight)
                    _buildings[i] = new Building(b.XLeft, b.XRight, targetHeight);
            }
            else
            {
                if (b.Height > targetHeight)
                    _buildings[i] = new Building(b.XLeft, b.XRight, targetHeight);
            }
        }
    }
    
    public double Distance(VerticalSkyline other)
    {
        if (_direction == other._direction)
            throw new ArgumentException("Distance requires skylines with opposite directions");
        
        if (IsEmpty || other.IsEmpty)
            return double.NegativeInfinity;
        
        double maxDistance = double.NegativeInfinity;
        
        foreach (var b1 in _buildings)
        {
            foreach (var b2 in other._buildings)
            {
                double overlapLeft = Math.Max(b1.XLeft, b2.XLeft);
                double overlapRight = Math.Min(b1.XRight, b2.XRight);
                
                if (overlapLeft < overlapRight)
                {
                    double heightSum = b1.Height + b2.Height;
                    maxDistance = Math.Max(maxDistance, heightSum);
                }
            }
        }
        
        return maxDistance;
    }
    
    /// <summary>
    /// Returns the extreme height of this skyline.
    /// For UP skyline: returns the smallest Y coordinate (topmost point in SVG coordinates)
    /// For DOWN skyline: returns the largest Y coordinate (bottommost point in SVG coordinates)
    /// </summary>
    /// <remarks>
    /// SVG coordinate system: Y increases downward, so "up" is smaller Y values.
    /// </remarks>
    public double MaxHeight()
    {
        if (IsEmpty) return _direction == VerticalDirection.Up ? double.PositiveInfinity : double.NegativeInfinity;
        
        if (_direction == VerticalDirection.Up)
        {
            // UP skyline: find minimum Y (topmost in SVG)
            double minY = double.PositiveInfinity;
            foreach (var b in _buildings)
                minY = Math.Min(minY, b.Height);
            return minY;
        }
        else
        {
            // DOWN skyline: find maximum Y (bottommost in SVG)
            // Note: DOWN skyline stores -yBottom, so we need to negate
            double maxNegY = double.NegativeInfinity;
            foreach (var b in _buildings)
                maxNegY = Math.Max(maxNegY, b.Height);
            return -maxNegY;  // Convert back to actual Y coordinate
        }
    }
    
    private static List<Building> MergeBuildings(List<Building> buildings, VerticalDirection direction)
    {
        if (buildings.Count <= 1) return buildings;
        
        var sorted = buildings.OrderBy(b => b.XLeft).ToList();
        var result = new List<Building>();
        
        foreach (var building in sorted)
        {
            if (result.Count == 0) { result.Add(building); continue; }
            
            var last = result[^1];
            if (building.XLeft <= last.XRight)
            {
                double height = direction == VerticalDirection.Up
                    ? Math.Min(last.Height, building.Height)  // UP: keep topmost (smallest Y)
                    : Math.Min(last.Height, building.Height); // DOWN: keep bottommost (most negative -Y)
                
                result[^1] = new Building(
                    Math.Min(last.XLeft, building.XLeft),
                    Math.Max(last.XRight, building.XRight),
                    height);
            }
            else
            {
                result.Add(building);
            }
        }
        
        return result;
    }
}
