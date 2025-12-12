using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents multiple voices on a single staff at a specific time point.
/// </summary>
public sealed record VoiceColumn
{
    /// <summary>The voices present at this time point.</summary>
    public ImmutableArray<VoiceEntry> Entries { get; }
    
    /// <summary>Measure index.</summary>
    public int MeasureIndex { get; }
    
    /// <summary>Item index within measure.</summary>
    public int ItemIndex { get; }
    
    public VoiceColumn(ImmutableArray<VoiceEntry> entries, int measureIndex, int itemIndex)
    {
        Entries = entries;
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
    }
}

/// <summary>
/// A single voice's contribution to a voice column.
/// </summary>
public sealed record VoiceEntry
{
    /// <summary>The voice this entry belongs to.</summary>
    public int VoiceId { get; }
    
    /// <summary>The music item at this position.</summary>
    public MusicItem Item { get; }
    
    /// <summary>Forced stem direction (null = use voice default or auto).</summary>
    public bool? ForcedStemUp { get; }
    
    public VoiceEntry(int voiceId, MusicItem item, bool? forcedStemUp = null)
    {
        VoiceId = voiceId;
        Item = item;
        ForcedStemUp = forcedStemUp;
    }
}

/// <summary>
/// Voice direction settings for multi-voice layout.
/// </summary>
public static class VoiceDefaults
{
    /// <summary>
    /// Gets default stem direction for a voice number.
    /// Voice 1 = up, Voice 2 = down, etc.
    /// </summary>
    public static bool? GetDefaultStemUp(int voiceNumber) => voiceNumber switch
    {
        1 => true,   // First voice: stems up
        2 => false,  // Second voice: stems down
        3 => true,   // Third voice: stems up
        4 => false,  // Fourth voice: stems down
        _ => null    // Other voices: auto
    };
}