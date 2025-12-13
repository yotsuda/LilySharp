namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Options for score layout.
/// </summary>
public sealed record LayoutOptions
{
    /// <summary>Page width in pixels.</summary>
    public double PageWidth { get; init; } = 800;
    
    /// <summary>Left margin in pixels.</summary>
    public double MarginLeft { get; init; } = 20;
    
    /// <summary>Right margin in pixels.</summary>
    public double MarginRight { get; init; } = 20;
    
    /// <summary>Top margin in pixels.</summary>
    public double MarginTop { get; init; } = 50;
    
    /// <summary>Staff height (distance from top to bottom line) in pixels.</summary>
    public double StaffHeight { get; init; } = 40;
    
    /// <summary>Space between staff lines in pixels (staff space size).</summary>
    public double SpaceHeight { get; init; } = 10;
    
    /// <summary>Staff space size - alias for SpaceHeight for clarity.</summary>
    public double StaffSpaceSize => SpaceHeight;
    
    /// <summary>Vertical spacing between systems in pixels.</summary>
    public double SystemSpacing { get; init; } = 80;
    
    /// <summary>Header height when title or composer is present, in pixels.</summary>
    public double HeaderHeight { get; init; } = 50;
    
    /// <summary>Spacing multiplier between staves in a grand staff.</summary>
    public double GrandStaffSpacingMultiplier { get; init; } = 3;
    
    /// <summary>Spacing multiplier between staff groups.</summary>
    public double StaffGroupSpacingMultiplier { get; init; } = 5;
    
    /// <summary>Horizontal padding for collision detection, in pixels.</summary>
    public double CollisionXPadding { get; init; } = 20;
    
    /// <summary>Maximum stretch per measure during justification, in pixels.</summary>
    public double MaxStretchPerMeasure { get; init; } = 50;
    
    /// <summary>Available width for music content.</summary>
    public double ContentWidth => PageWidth - MarginLeft - MarginRight;
    
    /// <summary>Default options for standard layout.</summary>
    public static LayoutOptions Default { get; } = new();
}