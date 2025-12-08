using Lilysharp.Core.Semantics;
using Lilysharp.Core.Syntax;

namespace Lilysharp.Core.Midi;

/// <summary>
/// Exports a Lilysharp syntax tree to MIDI format.
/// </summary>
public class MidiExporter
{
    private readonly int _ticksPerQuarter;
    private int _currentTick;
    private int _currentOctave = 4;
    private Fraction _defaultDuration = Fraction.Quarter;
    private int _tempo = 120;
    private int _velocity = 80;
    private readonly Stack<(int numerator, int denominator)> _tupletStack = new();
    
    public MidiExporter(int ticksPerQuarter = MidiFile.DefaultTicksPerQuarter)
    {
        _ticksPerQuarter = ticksPerQuarter;
    }
    
    public MidiFile Export(SyntaxTree tree)
    {
        var midi = new MidiFile { TicksPerQuarterNote = _ticksPerQuarter };
        
        var conductorTrack = new MidiTrack { Name = "Tempo", Channel = 0 };
        conductorTrack.TempoChanges.Add(new TempoChange(0, BpmToMicroseconds(_tempo)));
        conductorTrack.TimeSignatures.Add(new TimeSignatureChange(0, 4, 4));
        midi.Tracks.Add(conductorTrack);
        
        var mainTrack = new MidiTrack { Name = "Track 1", Channel = 0 };
        ProcessNode(tree.GetRoot(), mainTrack, conductorTrack);
        
        if (mainTrack.Notes.Count > 0)
            midi.Tracks.Add(mainTrack);
        
        return midi;
    }
    
    private void ProcessNode(SyntaxNode node, MidiTrack track, MidiTrack conductorTrack)
    {
        switch (node)
        {
            case CompilationUnitSyntax cu:
                foreach (var member in cu.Members)
                    ProcessNode(member, track, conductorTrack);
                break;
                
            case ScoreDeclarationSyntax score:
                ProcessChildren(score, track, conductorTrack);
                break;
                
            case PartDeclarationSyntax part:
                ProcessChildren(part, track, conductorTrack);
                break;
                
            case StaffDeclarationSyntax staff:
                ProcessChildren(staff, track, conductorTrack);
                break;
                
            case RelativeExpressionSyntax relative:
                var (_, baseOctave) = ParsePitch(relative.BasePitch);
                _currentOctave = baseOctave;
                ProcessNode(relative.Body, track, conductorTrack);
                break;
                
            case MusicBlockSyntax block:
                foreach (var item in block.Items)
                    ProcessNode(item, track, conductorTrack);
                break;
                
            case NoteSyntax note:
                ProcessNote(note, track);
                break;
                
            case RestSyntax rest:
                ProcessRest(rest);
                break;
                
            case ChordSyntax chord:
                ProcessChord(chord, track);
                break;
                
            case MetadataDeclarationSyntax metadata:
                ProcessMetadata(metadata, conductorTrack);
                break;
                
            case RepeatExpressionSyntax repeat:
                ProcessRepeat(repeat, track, conductorTrack);
                break;
                
            case TupletExpressionSyntax tuplet:
                _tupletStack.Push((tuplet.TupletRatio, tuplet.BaseDivision));
                ProcessNode(tuplet.Body, track, conductorTrack);
                _tupletStack.Pop();
                break;
            case GraceExpressionSyntax grace:
                ProcessGrace(grace, track);
                break;
                
            case ParallelExpressionSyntax parallel:
                var voices = parallel.Voices.ToList();
                if (voices.Count > 0)
                    ProcessNode(voices[0], track, conductorTrack);
                break;
                
            default:
                // Process any other nodes by visiting their children
                ProcessChildren(node, track, conductorTrack);
                break;
        }
    }
    
    private void ProcessChildren(SyntaxNode node, MidiTrack track, MidiTrack conductorTrack)
    {
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && child is not SyntaxTokenNode)
                ProcessNode(child, track, conductorTrack);
        }
    }
    
    private void ProcessNote(NoteSyntax note, MidiTrack track)
    {
        var (basePitch, _) = ParsePitch(note.Pitch);
        int octaveChange = note.Pitch.OctaveOffset;
        int targetOctave = _currentOctave + octaveChange;
        int midiPitch = Math.Clamp(basePitch + (targetOctave + 1) * 12, 0, 127);
        _currentOctave = targetOctave;
        
        var duration = GetDuration(note.Duration);
        int durationTicks = FractionToTicks(duration);
        
        track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, _currentTick, durationTicks));
        _currentTick += durationTicks;
    }
    
    private void ProcessRest(RestSyntax rest)
    {
        var duration = GetDuration(rest.Duration);
        _currentTick += FractionToTicks(duration);
    }
    
    private void ProcessChord(ChordSyntax chord, MidiTrack track)
    {
        int startTick = _currentTick;
        var pitches = chord.Pitches.ToList();
        
        var durationNode = chord.DescendantNodes<DurationSyntax>().FirstOrDefault();
        var duration = durationNode != null ? GetDuration(durationNode) : _defaultDuration;
        int durationTicks = FractionToTicks(duration);
        
        foreach (var pitch in pitches)
        {
            var (basePitch, _) = ParsePitch(pitch);
            int octaveChange = pitch.OctaveOffset;
            int targetOctave = _currentOctave + octaveChange;
            int midiPitch = Math.Clamp(basePitch + (targetOctave + 1) * 12, 0, 127);
            
            track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks));
        }
        
        _currentTick = startTick + durationTicks;
    }
    
    private void ProcessMetadata(MetadataDeclarationSyntax metadata, MidiTrack conductorTrack)
    {
        string keyword = metadata.KeywordToken.Text.ToLowerInvariant();
        
        // Get value from child nodes
        SyntaxTokenNode? valueToken = null;
        for (int i = 1; i < metadata.SlotCount; i++)
        {
            if (metadata.GetChild(i) is SyntaxTokenNode token && 
                token.Kind == SyntaxKind.DurationNumber)
            {
                valueToken = token;
                break;
            }
        }
        
        if (valueToken == null) return;
        
        if (keyword == "tempo" && int.TryParse(valueToken.Text, out int bpm))
        {
            _tempo = bpm;
            conductorTrack.TempoChanges.Add(new TempoChange(_currentTick, BpmToMicroseconds(bpm)));
        }
    }
    
    private void ProcessRepeat(RepeatExpressionSyntax repeat, MidiTrack track, MidiTrack conductorTrack)
    {
        int repeatCount = 2;
        if (int.TryParse(repeat.Count.Text, out int count))
            repeatCount = count;
        
        var alternatives = repeat.Alternative?.Alternatives.ToList();
        
        for (int i = 0; i < repeatCount; i++)
        {
            ProcessNode(repeat.Body, track, conductorTrack);
            
            if (alternatives != null && alternatives.Count > 0)
            {
                int altIndex = Math.Min(i, alternatives.Count - 1);
                ProcessNode(alternatives[altIndex], track, conductorTrack);
            }
        }
    }
    
    private (int basePitch, int octave) ParsePitch(PitchSyntax pitch)
    {
        string text = pitch.PitchName.ToLowerInvariant();
        
        char noteName = text[0];
        int basePitch = noteName switch
        {
            'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5,
            'g' => 7, 'a' => 9, 'b' => 11, _ => 0
        };
        
        int accidental = 0;
        if (text.Contains("isis")) accidental = 2;
        else if (text.Contains("eses")) accidental = -2;
        else if (text.Contains("is")) accidental = 1;
        else if (text.Contains("es") || text.Contains("as")) accidental = -1;
        
        return (basePitch + accidental, 4 + pitch.OctaveOffset);
    }
    
    private Fraction GetDuration(DurationSyntax? duration)
    {
        if (duration == null) return _defaultDuration;
        
        var baseDuration = Fraction.FromNoteValue(duration.Value);
        _defaultDuration = baseDuration.Dotted(duration.DotCount);
        return _defaultDuration;
    }
    
    private int FractionToTicks(Fraction duration)
    {
        int baseTicks = (int)(duration.Numerator * 4 * _ticksPerQuarter / duration.Denominator);
        
        // Apply tuplet scaling: each note plays in (denominator/numerator) of normal time
        foreach (var (numerator, denominator) in _tupletStack)
        {
            baseTicks = baseTicks * denominator / numerator;
        }
        
        return baseTicks;
    }
    
    private static int BpmToMicroseconds(int bpm) => 60_000_000 / bpm;
    
    private void ProcessGrace(GraceExpressionSyntax grace, MidiTrack track)
    {
        // Grace notes steal time from the following note
        // For now, use a fixed short duration (1/32 note per grace note)
        int graceDuration = _ticksPerQuarter / 8; // 1/32 note
        
        // Collect all notes in the grace expression
        var graceNotes = grace.Body.Items.OfType<NoteSyntax>().ToList();
        
        // Temporarily set shorter duration for grace notes
        var savedDefaultDuration = _defaultDuration;
        _defaultDuration = new Fraction(1, 32);
        
        foreach (var note in graceNotes)
        {
            var (pitchClass, octave) = ParsePitch(note.Pitch);
            int midiPitch = (octave + 1) * 12 + pitchClass;
            
            track.Notes.Add(new MidiNote(
                track.Channel,
                midiPitch,
                _velocity,
                _currentTick,
                graceDuration
            ));
            
            _currentTick += graceDuration;
        }
        
        _defaultDuration = savedDefaultDuration;
    }
}