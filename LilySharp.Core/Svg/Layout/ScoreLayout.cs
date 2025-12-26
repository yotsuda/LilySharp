using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for a single music item within a measure.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct ItemLayout(
    int ItemIndex,
    double X,       // X offset from measure start (staff spaces)
    double Width    // Allocated width (staff spaces)
);

/// <summary>
/// Layout information for a timing column within a measure.
/// A column represents a vertical slice of time where items may be placed.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct ColumnLayout(
    Fraction Timing,  // Start time of this column (from measure start)
    double X,         // X offset from measure start (staff spaces)
    double Width      // Allocated width (staff spaces)
);

/// <summary>
/// Layout information for a single measure.
/// All coordinates are in staff spaces.
/// </summary>
public sealed record MeasureLayout
{
    public int MeasureIndex { get; }
    public double X { get; }                              // X position of measure start (staff spaces)
    public double Width { get; }                          // Total measure width (staff spaces)
    public ImmutableArray<ItemLayout> Items { get; }      // Layout of items within the measure (for single-staff)
    public ImmutableArray<ColumnLayout> Columns { get; }  // Timing-based columns (for multi-staff)
    
    /// <summary>
    /// Creates a MeasureLayout with item-based positioning (for single-staff scores).
    /// </summary>
    public MeasureLayout(int measureIndex, double x, double width, ImmutableArray<ItemLayout> items)
    {
        MeasureIndex = measureIndex;
        X = x;
        Width = width;
        Items = items;
        Columns = ImmutableArray<ColumnLayout>.Empty;
    }
    
    /// <summary>
    /// Creates a MeasureLayout with both item-based and column-based positioning (for multi-staff scores).
    /// </summary>
    public MeasureLayout(int measureIndex, double x, double width, ImmutableArray<ItemLayout> items, ImmutableArray<ColumnLayout> columns)
    {
        MeasureIndex = measureIndex;
        X = x;
        Width = width;
        Items = items;
        Columns = columns;
    }
    
    /// <summary>
    /// Gets the X coordinate for a given timing within this measure.
    /// Uses column information if available, otherwise interpolates.
    /// </summary>
    public double GetXForTiming(Fraction timing)
    {
        if (Columns.IsDefaultOrEmpty || Columns.Length == 0)
        {
            // No column info - return start of measure as fallback
            return 0;
        }
        
        // Find the column with exact or closest timing
        for (int i = 0; i < Columns.Length; i++)
        {
            if (Columns[i].Timing == timing)
                return Columns[i].X;
            
            if (Columns[i].Timing > timing)
            {
                // Timing is between previous column and this one - interpolate
                if (i == 0)
                    return Columns[0].X;
                
                var prev = Columns[i - 1];
                var curr = Columns[i];
                var ratio = (timing - prev.Timing).ToDouble() / (curr.Timing - prev.Timing).ToDouble();
                return prev.X + ratio * (curr.X - prev.X);
            }
        }
        
        // Timing is after all columns - return last column position
        return Columns[^1].X;
    }
}

/// <summary>
/// Layout information for a single system (staff line).
/// All coordinates are in staff spaces.
/// </summary>
public sealed record SystemLayout(
    int SystemIndex,
    double Y,                              // Y position of system top (staff spaces from page top)
    double PrefixWidth,                    // Width of clef + key + time (staff spaces)
    ImmutableArray<MeasureLayout> Measures, // Measures in this system
    ImmutableArray<StaffGroupLayout> StaffGroups = default  // Staff groups (optional, for multi-staff)
)
{
    /// <summary>Whether this system has multiple staff groups.</summary>
    public bool HasMultipleStaffGroups => !StaffGroups.IsDefaultOrEmpty && StaffGroups.Length > 1;
    
    /// <summary>Whether this system has a grand staff.</summary>
    public bool HasGrandStaff => !StaffGroups.IsDefaultOrEmpty && 
        StaffGroups.Any(g => g.Type == StaffGroupType.GrandStaff);
    
    /// <summary>Total height of all staff groups (staff spaces).</summary>
    public double TotalStaffHeight => StaffGroups.IsDefaultOrEmpty ? 0 : StaffGroups.Sum(g => g.Height);
}

/// <summary>
/// Layout information for a single page.
/// All coordinates are in staff spaces.
/// </summary>
public sealed record PageLayout(
    int PageIndex,
    double Width,                          // Page width (staff spaces)
    double Height,                         // Page height (staff spaces)
    double HeaderHeight,                   // Space for title/composer (staff spaces)
    ImmutableArray<SystemLayout> Systems   // Systems on this page
);

/// <summary>
/// Key for voice-specific layout offsets in multi-voice scores.
/// </summary>
public readonly record struct VoiceItemKey(int MeasureIndex, int VoiceId, int ItemIndex);

/// <summary>
/// Key for rest shift due to beam collision.
/// </summary>
public readonly record struct RestShiftKey(int MeasureIndex, int ItemIndex);

/// <summary>
/// Complete layout information for a score.
/// All coordinates are in staff spaces unless otherwise noted.
/// </summary>
public sealed record ScoreLayout(
    ImmutableArray<PageLayout> Pages,
    ImmutableArray<SystemLayout> AllSystems,
    ImmutableArray<BeamLayout> BeamLayouts,
    ImmutableArray<TieLayout> TieLayouts,
    ImmutableArray<SlurLayout> SlurLayouts,
    ImmutableArray<DynamicLayout> DynamicLayouts,
    ImmutableArray<ArticulationLayout> ArticulationLayouts,
    ImmutableArray<GraceNoteLayout> GraceNoteLayouts,
    ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
    ImmutableDictionary<RestShiftKey, double> RestShifts
)
{
    /// <summary>Total number of pages.</summary>
    public int PageCount => Pages.Length;
    
    /// <summary>Total number of systems across all pages.</summary>
    public int SystemCount => AllSystems.Length;
    
    /// <summary>Width of the first page (staff spaces).</summary>
    public double Width => Pages.Length > 0 ? Pages[0].Width : 0;
    
    /// <summary>Height of the first page (staff spaces).</summary>
    public double Height => Pages.Length > 0 ? Pages[0].Height : 0;
    
    /// <summary>Header height of the first page (staff spaces).</summary>
    public double HeaderHeight => Pages.Length > 0 ? Pages[0].HeaderHeight : 0;
    
    /// <summary>All systems from all pages (pre-computed for performance).</summary>
    public ImmutableArray<SystemLayout> Systems => AllSystems;
    
    /// <summary>
    /// Gets the X offset for a specific voice item due to collision handling.
    /// Returns 0 if no offset is needed. Value is in staff spaces.
    /// </summary>
    public double GetVoiceOffset(int measureIndex, int voiceId, int itemIndex)
    {
        var key = new VoiceItemKey(measureIndex, voiceId, itemIndex);
        return VoiceOffsets.TryGetValue(key, out var offset) ? offset : 0;
    }
    
    /// <summary>
    /// Gets the Y shift for a rest due to beam collision.
    /// Returns 0 if no shift is needed. Value is in staff positions.
    /// </summary>
    public double GetRestShift(int measureIndex, int itemIndex)
    {
        var key = new RestShiftKey(measureIndex, itemIndex);
        return RestShifts.TryGetValue(key, out var shift) ? shift : 0;
    }
}
