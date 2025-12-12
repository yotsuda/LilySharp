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
/// Key for voice-specific layout offsets in multi-voice scores.
/// </summary>
public readonly record struct VoiceItemKey(int MeasureIndex, int VoiceId, int ItemIndex);

/// <summary>
/// Complete layout information for a score.
/// </summary>
public sealed record ScoreLayout(
    double Width,
    double Height,
    double HeaderHeight,                     // Space for title/composer
    ImmutableArray<SystemLayout> Systems,
    ImmutableArray<BeamLayout> BeamLayouts,
    ImmutableArray<TieLayout> TieLayouts,
    ImmutableArray<SlurLayout> SlurLayouts,
    ImmutableDictionary<VoiceItemKey, double> VoiceOffsets
)
{
    /// <summary>Total number of systems.</summary>
    public int SystemCount => Systems.Length;
    
    /// <summary>
    /// Gets the X offset for a specific voice item due to collision handling.
    /// Returns 0 if no offset is needed.
    /// </summary>
    public double GetVoiceOffset(int measureIndex, int voiceId, int itemIndex)
    {
        var key = new VoiceItemKey(measureIndex, voiceId, itemIndex);
        return VoiceOffsets.TryGetValue(key, out var offset) ? offset : 0;
    }
}