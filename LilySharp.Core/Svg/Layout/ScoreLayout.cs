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
    double Y,                              // Y position of system top (relative to page)
    double PrefixWidth,                    // Width of clef + key + time
    ImmutableArray<MeasureLayout> Measures // Measures in this system
);

/// <summary>
/// Layout information for a single page.
/// </summary>
public sealed record PageLayout(
    int PageIndex,
    double Width,
    double Height,
    double HeaderHeight,                   // Space for title/composer (first page only)
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
/// </summary>
public sealed record ScoreLayout(
    ImmutableArray<PageLayout> Pages,
    ImmutableArray<SystemLayout> AllSystems,
    ImmutableArray<BeamLayout> BeamLayouts,
    ImmutableArray<TieLayout> TieLayouts,
    ImmutableArray<SlurLayout> SlurLayouts,
    ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
    ImmutableDictionary<RestShiftKey, double> RestShifts
)
{
    /// <summary>Total number of pages.</summary>
    public int PageCount => Pages.Length;
    
    /// <summary>Total number of systems across all pages.</summary>
    public int SystemCount => AllSystems.Length;
    
    /// <summary>Width of the first page (for compatibility).</summary>
    public double Width => Pages.Length > 0 ? Pages[0].Width : 0;
    
    /// <summary>Height of the first page (for compatibility).</summary>
    public double Height => Pages.Length > 0 ? Pages[0].Height : 0;
    
    /// <summary>Header height of the first page (for compatibility).</summary>
    public double HeaderHeight => Pages.Length > 0 ? Pages[0].HeaderHeight : 0;
    
    /// <summary>All systems from all pages (pre-computed for performance).</summary>
    public ImmutableArray<SystemLayout> Systems => AllSystems;
    
    /// <summary>
    /// Gets the X offset for a specific voice item due to collision handling.
    /// Returns 0 if no offset is needed.
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