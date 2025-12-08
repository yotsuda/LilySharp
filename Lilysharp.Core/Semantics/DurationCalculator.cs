using Lilysharp.Core.Syntax;

namespace Lilysharp.Core.Semantics;

/// <summary>
/// Calculates durations from syntax nodes.
/// </summary>
public static class DurationCalculator
{
    /// <summary>
    /// Gets the duration of a note or rest.
    /// </summary>
    public static Fraction GetDuration(NoteSyntax note, Fraction defaultDuration)
    {
        if (note.Duration == null)
            return defaultDuration;
        return GetDuration(note.Duration);
    }

    /// <summary>
    /// Gets the duration of a rest.
    /// </summary>
    public static Fraction GetDuration(RestSyntax rest, Fraction defaultDuration)
    {
        if (rest.Duration == null)
            return defaultDuration;
        return GetDuration(rest.Duration);
    }

    /// <summary>
    /// Gets the duration from a DurationSyntax node.
    /// </summary>
    public static Fraction GetDuration(DurationSyntax duration)
    {
        int noteValue = duration.Value;
        int dots = duration.DotCount;
        
        var baseDuration = Fraction.FromNoteValue(noteValue);
        return baseDuration.Dotted(dots);
    }

    /// <summary>
    /// Parses a time signature like "4/4" or "3/4".
    /// </summary>
    public static Fraction ParseTimeSignature(int beats, int beatUnit)
    {
        // Time signature 4/4 means 4 quarter notes = 4 * 1/4 = 1
        // Time signature 3/4 means 3 quarter notes = 3 * 1/4 = 3/4
        return new Fraction(beats, beatUnit);
    }

    /// <summary>
    /// Calculates the total duration of items in a music block.
    /// </summary>
    public static Fraction CalculateMeasureDuration(MusicBlockSyntax block, Fraction defaultDuration)
    {
        var total = Fraction.Zero;
        var currentDefault = defaultDuration;

        foreach (var item in block.Items)
        {
            switch (item)
            {
                case NoteSyntax note:
                    var noteDuration = GetDuration(note, currentDefault);
                    if (note.Duration != null)
                        currentDefault = noteDuration; // Update default
                    total += noteDuration;
                    break;

                case RestSyntax rest:
                    var restDuration = GetDuration(rest, currentDefault);
                    if (rest.Duration != null)
                        currentDefault = restDuration;
                    total += restDuration;
                    break;

                case ChordSyntax chord:
                    // Chord duration - get from first pitch or default
                    // TODO: proper chord duration handling
                    total += currentDefault;
                    break;

                case BarlineSyntax:
                    // Barlines don't affect duration
                    break;

                case TieSyntax:
                case SlurSyntax:
                    // Ties and slurs don't add duration
                    break;
            }
        }

        return total;
    }
}