using System.Collections.Immutable;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Clef type enumeration.
/// </summary>
public enum ClefType
{
    Treble,
    Bass,
    Alto,
    Tenor,
    Treble8Below
}

/// <summary>
/// A single staff with its own clef and voices.
/// </summary>
/// <remarks>
/// In a grand staff (piano), there are typically two staves:
/// - Upper staff: treble clef, right hand
/// - Lower staff: bass clef, left hand
/// </remarks>
public sealed record Staff(
    ClefType Clef,
    ImmutableArray<Voice> Voices
)
{
    /// <summary>The primary voice (first voice).</summary>
    public Voice PrimaryVoice => Voices[0];

    /// <summary>Whether this staff has multiple voices.</summary>
    public bool IsMultiVoice => Voices.Length > 1;

    /// <summary>Number of measures (from primary voice).</summary>
    public int MeasureCount => PrimaryVoice.Measures.Length;

    /// <summary>
    /// Creates a single-voice staff.
    /// </summary>
    public static Staff Create(ClefType clef, Voice voice)
        => new(clef, ImmutableArray.Create(voice));

    /// <summary>
    /// Parses a clef string to ClefType.
    /// </summary>
    public static ClefType ParseClef(string clef) => clef.ToLowerInvariant() switch
    {
        "treble" => ClefType.Treble,
        "bass" => ClefType.Bass,
        "alto" => ClefType.Alto,
        "tenor" => ClefType.Tenor,
        "treble_8" => ClefType.Treble8Below,
        _ => ClefType.Treble
    };

    /// <summary>
    /// Parses a SyntaxKind to ClefType.
    /// </summary>
    public static ClefType ParseClef(SyntaxKind kind) => kind switch
    {
        SyntaxKind.TrebleKeyword => ClefType.Treble,
        SyntaxKind.BassKeyword => ClefType.Bass,
        SyntaxKind.AltoKeyword => ClefType.Alto,
        SyntaxKind.TenorKeyword => ClefType.Tenor,
        SyntaxKind.Treble8Keyword => ClefType.Treble8Below,
        _ => ClefType.Treble
    };
}