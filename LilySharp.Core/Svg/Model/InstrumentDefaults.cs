namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Default settings for musical instruments.
/// </summary>
/// <remarks>
/// Provides clef and octave defaults based on instrument type.
/// Used for automatic configuration when a part specifies an instrument.
/// </remarks>
public static class InstrumentDefaults
{
    /// <summary>
    /// Gets the default clef and octave for an instrument.
    /// </summary>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>A tuple of (clef, octave) defaults.</returns>
    public static (ClefType Clef, int Octave) GetDefaults(string instrument)
    {
        return instrument.ToLowerInvariant() switch
        {
            // Strings
            "violin" => (ClefType.Treble, 4),
            "viola" => (ClefType.Alto, 3),
            "cello" => (ClefType.Bass, 3),
            "bass" or "contrabass" or "double-bass" => (ClefType.Bass, 2),
            
            // Piano
            "piano-right" or "piano-treble" => (ClefType.Treble, 4),
            "piano-left" or "piano-bass" => (ClefType.Bass, 3),
            
            // Guitar (written octave higher than sounds)
            "guitar" or "acoustic-guitar" or "electric-guitar" => (ClefType.Treble8Below, 4),
            
            // Woodwinds
            "flute" or "piccolo" => (ClefType.Treble, 5),
            "oboe" => (ClefType.Treble, 4),
            "clarinet" => (ClefType.Treble, 4),
            "bassoon" => (ClefType.Bass, 3),
            
            // Brass
            "trumpet" => (ClefType.Treble, 4),
            "horn" or "french-horn" => (ClefType.Treble, 4),
            "trombone" => (ClefType.Bass, 3),
            "tuba" => (ClefType.Bass, 2),
            
            // Voice
            "soprano" or "voice-soprano" => (ClefType.Treble, 4),
            "alto" or "voice-alto" => (ClefType.Treble, 4),
            "tenor" or "voice-tenor" => (ClefType.Treble8Below, 4),  // treble_8 clef
            "voice-bass" => (ClefType.Bass, 3),
            
            // Default
            _ => (ClefType.Treble, 4)
        };
    }

    /// <summary>
    /// Gets the default octave for a clef type.
    /// Used when no instrument is specified.
    /// </summary>
    /// <param name="clef">The clef type.</param>
    /// <returns>The default starting octave.</returns>
    public static int GetDefaultOctave(ClefType clef)
    {
        return clef switch
        {
            ClefType.Treble => 4,  // Middle C = c'
            ClefType.Bass => 3,   // One octave below middle C
            ClefType.Alto => 3,   // Middle C on middle line
            ClefType.Tenor => 3,  // Middle C on 4th line
            ClefType.Treble8Below => 4,  // Same written pitch as treble
            _ => 4
        };
    }

    /// <summary>
    /// Checks if the given string is a known instrument name.
    /// </summary>
    public static bool IsKnownInstrument(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower is "violin" or "viola" or "cello" or "bass" or "contrabass" or "double-bass"
            or "piano-right" or "piano-treble" or "piano-left" or "piano-bass"
            or "guitar" or "acoustic-guitar" or "electric-guitar"
            or "flute" or "piccolo" or "oboe" or "clarinet" or "bassoon"
            or "trumpet" or "horn" or "french-horn" or "trombone" or "tuba"
            or "soprano" or "voice-soprano" or "alto" or "voice-alto"
            or "tenor" or "voice-tenor" or "voice-bass";
    }
}
