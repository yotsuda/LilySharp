using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a single music item within a measure.
/// </summary>
public readonly record struct ItemLayout(
    int ItemIndex,
    double X,       // X offset from measure start
    double Width    // Allocated width (may include stretch)
);

/// <summary>
/// Layout information for a single measure.
/// </summary>
public sealed record MeasureLayout(
    int MeasureIndex,
    double X,                              // X position of measure start
    double Width,                          // Total measure width (including barlines)
    ImmutableArray<ItemLayout> Items       // Layout of items within the measure
);

/// <summary>
/// Layout information for a single system (staff line).
/// </summary>
public sealed record SystemLayout(
    int SystemIndex,
    double Y,                              // Y position of system top
    double PrefixWidth,                    // Width of clef + key + time
    ImmutableArray<MeasureLayout> Measures // Measures in this system
);

/// <summary>
/// Complete layout information for a score.
/// </summary>
public sealed record ScoreLayout(
    double Width,
    double Height,
    double HeaderHeight,                     // Space for title/composer
    ImmutableArray<SystemLayout> Systems,
    ImmutableArray<BeamLayout> BeamLayouts,  // Beam layout information
    ImmutableArray<TieLayout> TieLayouts,    // Tie layout information
    ImmutableArray<SlurLayout> SlurLayouts   // Slur layout information
)
{
    /// <summary>Total number of systems.</summary>
    public int SystemCount => Systems.Length;
    
    /// <summary>Creates a ScoreLayout without beam/tie/slur layouts (for backward compatibility).</summary>
    public ScoreLayout(double width, double height, double headerHeight, ImmutableArray<SystemLayout> systems)
        : this(width, height, headerHeight, systems, 
               ImmutableArray<BeamLayout>.Empty, 
               ImmutableArray<TieLayout>.Empty,
               ImmutableArray<SlurLayout>.Empty)
    {
    }
    
    /// <summary>Creates a ScoreLayout without tie/slur layouts (for backward compatibility).</summary>
    public ScoreLayout(double width, double height, double headerHeight, ImmutableArray<SystemLayout> systems, ImmutableArray<BeamLayout> beamLayouts)
        : this(width, height, headerHeight, systems, beamLayouts, 
               ImmutableArray<TieLayout>.Empty,
               ImmutableArray<SlurLayout>.Empty)
    {
    }
    
    /// <summary>Creates a ScoreLayout without slur layouts (for backward compatibility).</summary>
    public ScoreLayout(double width, double height, double headerHeight, ImmutableArray<SystemLayout> systems, ImmutableArray<BeamLayout> beamLayouts, ImmutableArray<TieLayout> tieLayouts)
        : this(width, height, headerHeight, systems, beamLayouts, tieLayouts,
               ImmutableArray<SlurLayout>.Empty)
    {
    }
}