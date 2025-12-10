namespace LilySharp.Core.Midi;

/// <summary>
/// Represents a MIDI note event.
/// </summary>
public readonly record struct MidiNote(
    int Channel,
    int Pitch,
    int Velocity,
    int StartTick,
    int DurationTicks
);

/// <summary>
/// Represents a tempo change event.
/// </summary>
public readonly record struct TempoChange(
    int Tick,
    int MicrosecondsPerBeat
);

/// <summary>
/// Represents a time signature change.
/// </summary>
public readonly record struct TimeSignatureChange(
    int Tick,
    int Numerator,
    int Denominator
);

/// <summary>
/// Represents a MIDI lyric event (Meta event 0x05).
/// </summary>
public readonly record struct LyricEvent(
    int Tick,
    string Text
);