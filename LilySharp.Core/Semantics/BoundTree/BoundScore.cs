using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.BoundTree;

/// <summary>
/// Metadata for a bound score.
/// </summary>
public sealed record BoundScoreMetadata(
    string? Title,
    string? Composer,
    int? Tempo,
    TimeSignature TimeSignature,
    KeySignature KeySignature,
    string Clef)
{
    /// <summary>Default metadata.</summary>
    public static BoundScoreMetadata Default => new(
        null, null, null,
        new TimeSignature(4, 4),
        KeySignature.CMajor,
        "treble");
}

/// <summary>
/// A bound voice (part) containing resolved measures.
/// </summary>
public sealed record BoundVoice(
    string Name,
    ImmutableArray<BoundMeasure> Measures);

/// <summary>
/// A complete bound score after semantic analysis.
/// </summary>
/// <remarks>
/// BoundScore is the output of the Binder and input to the layout phase.
/// It contains all music with:
/// - Resolved symbol references
/// - Expanded structures (repeats, alternatives)
/// - Resolved relative pitches
/// </remarks>
public sealed record BoundScore
{
    /// <summary>
    /// Creates a bound score.
    /// </summary>
    public BoundScore(
        ImmutableArray<BoundVoice> voices,
        BoundScoreMetadata metadata,
        ImmutableArray<SemanticDiagnostic> diagnostics)
    {
        Voices = voices;
        Metadata = metadata;
        Diagnostics = diagnostics;
    }

    /// <summary>All voices in the score.</summary>
    public ImmutableArray<BoundVoice> Voices { get; }

    /// <summary>The primary voice (first voice).</summary>
    public BoundVoice PrimaryVoice => Voices.Length > 0 ? Voices[0] : new BoundVoice("", ImmutableArray<BoundMeasure>.Empty);

    /// <summary>Score metadata (title, tempo, etc.).</summary>
    public BoundScoreMetadata Metadata { get; }

    /// <summary>Diagnostic messages from semantic analysis.</summary>
    public ImmutableArray<SemanticDiagnostic> Diagnostics { get; }

    /// <summary>Whether binding succeeded without errors.</summary>
    public bool Success => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>Creates an empty score.</summary>
    public static BoundScore Empty => new(
        ImmutableArray<BoundVoice>.Empty,
        BoundScoreMetadata.Default,
        ImmutableArray<SemanticDiagnostic>.Empty);
}
