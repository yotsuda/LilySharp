using System.Collections.Immutable;
using LilySharp.Core.Semantics.BoundTree;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Builds a Score from a BoundScore.
/// This is the bridge between semantic analysis and layout/rendering.
/// </summary>
public sealed class ScoreBuilder
{
    private readonly string _clef;
    
    public ScoreBuilder(string clef = "treble")
    {
        _clef = clef;
    }
    
    /// <summary>
    /// Builds a Score from a BoundScore.
    /// </summary>
    public Score Build(BoundScore boundScore)
    {
        var voices = boundScore.Voices
            .Select(BuildVoice)
            .ToImmutableArray();
        
        if (voices.Length == 0)
        {
            voices = ImmutableArray.Create(new Voice("", ImmutableArray<Measure>.Empty));
        }
        
        return new Score(
            voices,
            boundScore.Metadata.TimeSignature,
            boundScore.Metadata.KeySignature,
            boundScore.Metadata.Clef,
            boundScore.Metadata.Tempo,
            boundScore.Metadata.Title,
            boundScore.Metadata.Composer);
    }
    
    private Voice BuildVoice(BoundVoice boundVoice)
    {
        var measures = boundVoice.Measures
            .Select(BuildMeasure)
            .ToImmutableArray();
        
        return new Voice(boundVoice.Name, measures);
    }
    
    private Measure BuildMeasure(BoundMeasure boundMeasure)
    {
        var items = boundMeasure.Items
            .Select(BuildMusicItem)
            .ToImmutableArray();
        
        int sourceStart = boundMeasure.Syntax?.Position ?? 0;
        int sourceEnd = boundMeasure.Syntax != null 
            ? boundMeasure.Syntax.Position + boundMeasure.Syntax.FullWidth 
            : 0;
        
        return new Measure(
            items,
            boundMeasure.StartBarline,
            boundMeasure.EndBarline,
            boundMeasure.SectionLabel,
            sourceStart,
            sourceEnd);
    }
    
    private MusicItem BuildMusicItem(BoundMusic boundMusic)
    {
        return boundMusic switch
        {
            BoundNote note => BuildNoteItem(note),
            BoundRest rest => BuildRestItem(rest),
            BoundChord chord => BuildChordItem(chord),
            _ => throw new InvalidOperationException($"Unknown bound music type: {boundMusic.GetType().Name}")
        };
    }
    
    private NoteItem BuildNoteItem(BoundNote note)
    {
        var staffPosition = CalculateStaffPosition(note.Pitch);
        bool needsLedger = staffPosition <= -6 || staffPosition >= 6;
        
        string? accidental = note.Pitch.Alteration switch
        {
            2 => "doubleSharp",
            1 => "sharp",
            -1 => "flat",
            -2 => "doubleFlat",
            _ => null
        };
        
        return new NoteItem(
            staffPosition,
            note.BaseDuration,
            note.Dots,
            accidental,
            needsLedger,
            note.SourcePosition);
    }
    
    private RestItem BuildRestItem(BoundRest rest)
    {
        return new RestItem(rest.BaseDuration, rest.Dots, rest.SourcePosition);
    }
    
    private ChordItem BuildChordItem(BoundChord chord)
    {
        var notes = chord.Notes
            .Select(n => {
                var staffPosition = CalculateStaffPosition(n.Pitch);
                bool needsLedger = staffPosition <= -6 || staffPosition >= 6;
                string? accidental = n.Pitch.Alteration switch
                {
                    2 => "doubleSharp",
                    1 => "sharp",
                    -1 => "flat",
                    -2 => "doubleFlat",
                    _ => null
                };
                return new ChordNoteInfo(staffPosition, accidental, needsLedger);
            })
            .ToImmutableArray();
        
        return new ChordItem(notes, chord.BaseDuration, chord.Dots, chord.SourcePosition);
    }
    
    private int CalculateStaffPosition(Pitch pitch)
    {
        // Staff position calculation depends on clef
        // For treble clef: B4 = 0, middle C (C4) = -6
        // For bass clef: D3 = 0, middle C (C4) = 6
        return _clef switch
        {
            "treble" => pitch.Step - 6 + (pitch.Octave - 4) * 7, // B4 = position 0
            "bass" => pitch.Step - 1 + (pitch.Octave - 3) * 7,   // D3 = position 0
            "alto" => pitch.Step - 0 + (pitch.Octave - 4) * 7,   // C4 = position 0
            "tenor" => pitch.Step - 5 + (pitch.Octave - 3) * 7,  // A3 = position 0
            _ => pitch.Step - 6 + (pitch.Octave - 4) * 7         // Default to treble
        };
    }
}
