using System.Collections.Immutable;
using LilySharp.Core.Semantics;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Time signature (e.g., 3/4, 4/4, 6/8).
/// </summary>
public readonly record struct TimeSignature(int Beats, int BeatType)
{
    /// <summary>Duration of one full measure.</summary>
    public Fraction MeasureDuration => new(Beats, BeatType);
    
    public override string ToString() => $"{Beats}/{BeatType}";
}

/// <summary>
/// Key signature (number of sharps or flats).
/// </summary>
public readonly record struct KeySignature(int Sharps)
{
    /// <summary>Positive for sharps, negative for flats.</summary>
    public bool IsSharps => Sharps > 0;
    public bool IsFlats => Sharps < 0;
    public int Count => Math.Abs(Sharps);
    
    public static readonly KeySignature CMajor = new(0);
}

/// <summary>
/// A voice (part) containing a sequence of measures.
/// </summary>
public sealed record Voice(
    string Name,
    ImmutableArray<Measure> Measures
);

/// <summary>
/// A complete musical score ready for layout.
/// </summary>
/// <remarks>
/// Score is the output of the collection phase and input to the layout phase.
/// It contains all musical content in a structured, measure-based format.
/// </remarks>
public sealed record Score
{
    /// <summary>The voice to render (single-staff scores have one voice).</summary>
    public Voice Voice { get; }
    
    /// <summary>Time signature for the score.</summary>
    public TimeSignature TimeSignature { get; }
    
    /// <summary>Key signature for the score.</summary>
    public KeySignature KeySignature { get; }
    
    /// <summary>Clef type ("treble", "bass", "alto", "tenor").</summary>
    public string Clef { get; }
    
    /// <summary>Tempo in BPM (optional).</summary>
    public int? Tempo { get; }
    
    /// <summary>Title (optional).</summary>
    public string? Title { get; }
    
    /// <summary>Composer (optional).</summary>
    public string? Composer { get; }

    public Score(
        Voice voice,
        TimeSignature timeSignature,
        KeySignature keySignature,
        string clef,
        int? tempo = null,
        string? title = null,
        string? composer = null)
    {
        Voice = voice;
        TimeSignature = timeSignature;
        KeySignature = keySignature;
        Clef = clef;
        Tempo = tempo;
        Title = title;
        Composer = composer;
    }
    
    /// <summary>Total number of measures in the score.</summary>
    public int MeasureCount => Voice.Measures.Length;
}