// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Midi;

/// <summary>
/// Exports a LilySharp syntax tree to MIDI format.
/// </summary>
public sealed class MidiExporter
{
    private readonly int _ticksPerQuarter;
    private int _currentTick;
    private int _currentOctave = 4;
    private int _currentNoteName = 0; // c=0, d=1, e=2, f=3, g=4, a=5, b=6
    private Fraction _defaultDuration = Fraction.Quarter;
    private int _tempo = 120;
    private int _velocity = 80;
    private readonly Stack<(int numerator, int denominator)> _tupletStack = new();
    private int _timeNumerator = 4;
    private int _timeDenominator = 4;

    public MidiExporter(int ticksPerQuarter = MidiFile.DefaultTicksPerQuarter)
    {
        _ticksPerQuarter = ticksPerQuarter;
    }

    public MidiFile Export(SyntaxTree tree)
    {
        var midi = new MidiFile { TicksPerQuarterNote = _ticksPerQuarter };

        var conductorTrack = new MidiTrack { Name = "Tempo", Channel = 0 };
        conductorTrack.TempoChanges.Add(new TempoChange(0, BpmToMicroseconds(_tempo)));
        midi.Tracks.Add(conductorTrack);

        var mainTrack = new MidiTrack { Name = "Track 1", Channel = 0 };
        ProcessNode(tree.GetRoot(), mainTrack, conductorTrack);

        // Add initial time signature (may have been updated during processing)
        conductorTrack.TimeSignatures.Insert(0, new TimeSignatureChange(0, _timeNumerator, _timeDenominator));

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

            case TimeSignatureSyntax timeSig:
                ProcessTimeSignature(timeSig, conductorTrack);
                break;

            case TempoDeclarationSyntax tempo:
                ProcessTempo(tempo, conductorTrack);
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

            // Articulations and dynamics are handled within ProcessNote, skip here
            case ArticulationSyntax:
            case DynamicSyntax:
                break;

            case LyricsBlockSyntax lyrics:
                ProcessLyrics(lyrics, track);
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

    /// <summary>
    /// Initializes the relative mode with the base pitch (e.g., c'' sets octave 5 and note name c).
    /// </summary>
    private void InitializeRelativeMode(PitchSyntax basePitch)
    {
        _currentNoteName = GetNoteName(basePitch.BaseName);
        // Base octave: c' = octave 4, c'' = octave 5, etc.
        // OctaveOffset is the number of ' minus the number of ,
        _currentOctave = 3 + basePitch.OctaveOffset;
    }

    /// <summary>
    /// Calculates the MIDI pitch using LilyPond's relative octave algorithm.
    /// Finds the closest octave to the previous note, then applies explicit octave modifiers.
    /// </summary>
    private int CalculateRelativeMidiPitch(PitchSyntax pitch)
    {
        int noteName = GetNoteName(pitch.BaseName);

        // Calculate up and down candidates (LilyPond algorithm)
        int upOctave = _currentOctave;
        if (_currentNoteName > noteName)
            upOctave++;

        int downOctave = _currentOctave;
        if (_currentNoteName < noteName)
            downOctave--;

        // Calculate steps (note name + octave * 7)
        int currentSteps = _currentNoteName + _currentOctave * 7;
        int upSteps = noteName + upOctave * 7;
        int downSteps = noteName + downOctave * 7;

        // Choose the closer octave
        int targetOctave;
        if (Math.Abs(upSteps - currentSteps) < Math.Abs(downSteps - currentSteps))
            targetOctave = upOctave;
        else
            targetOctave = downOctave;

        // Apply explicit octave offset (' or ,)
        targetOctave += pitch.OctaveOffset;

        // Update current state for next note
        _currentNoteName = noteName;
        _currentOctave = targetOctave;

        // Calculate MIDI pitch
        int basePitch = pitch.BaseName switch
        {
            'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5,
            'g' => 7, 'a' => 9, 'b' => 11, _ => 0
        };

        return Math.Clamp(basePitch + pitch.AccidentalOffset + (targetOctave + 1) * 12, 0, 127);
    }

    /// <summary>
    /// Gets the note name index (c=0, d=1, e=2, f=3, g=4, a=5, b=6).
    /// </summary>
    private static int GetNoteName(char baseName)
    {
        return baseName switch
        {
            'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3,
            'g' => 4, 'a' => 5, 'b' => 6, _ => 0
        };
    }

    private void ProcessNote(NoteSyntax note, MidiTrack track)
    {
        int midiPitch = CalculateRelativeMidiPitch(note.Pitch);

        var duration = GetDuration(note.Duration);
        int durationTicks = FractionToTicks(duration);

        // Process articulations and dynamics
        int velocity = _velocity;
        int durationPercent = 100;

        foreach (var child in note.Articulations)
        {
            switch (child)
            {
                case DynamicSyntax dynamic:
                    velocity = dynamic.Velocity;
                    _velocity = velocity; // Update default velocity for subsequent notes
                    break;

                case ArticulationSyntax articulation:
                    (velocity, durationPercent) = ApplyArticulationType(articulation.Type, velocity, durationPercent);
                    break;
            }
        }

        int actualDuration = durationTicks * durationPercent / 100;

        track.Notes.Add(new MidiNote(track.Channel, midiPitch, velocity, _currentTick, actualDuration));
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

        // For chords, calculate all pitches relative to the current state,
        // but only update the state based on the first (lowest) pitch
        bool isFirst = true;
        int savedNoteName = _currentNoteName;
        int savedOctave = _currentOctave;

        foreach (var pitch in pitches)
        {
            if (!isFirst)
            {
                // Restore state for each subsequent pitch in the chord
                _currentNoteName = savedNoteName;
                _currentOctave = savedOctave;
            }

            int midiPitch = CalculateRelativeMidiPitch(pitch);

            if (isFirst)
            {
                // Save the state after processing the first pitch
                savedNoteName = _currentNoteName;
                savedOctave = _currentOctave;
                isFirst = false;
            }

            track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks));
        }

        // Restore the state from the first pitch for the next note
        _currentNoteName = savedNoteName;
        _currentOctave = savedOctave;

        _currentTick = startTick + durationTicks;
    }

    private void ProcessTimeSignature(TimeSignatureSyntax timeSig, MidiTrack conductorTrack)
    {
        _timeNumerator = timeSig.Beats;
        _timeDenominator = timeSig.BeatType;
        conductorTrack.TimeSignatures.Add(new TimeSignatureChange(_currentTick, _timeNumerator, _timeDenominator));
    }

    private void ProcessTempo(TempoDeclarationSyntax tempo, MidiTrack conductorTrack)
    {
        if (tempo.Bpm is int bpm)
        {
            _tempo = bpm;
            conductorTrack.TempoChanges.Add(new TempoChange(_currentTick, BpmToMicroseconds(bpm)));
        }
    }

    private void ProcessMetadata(MetadataDeclarationSyntax metadata, MidiTrack conductorTrack)
    {
        // MetadataDeclarationSyntax now only handles title/composer which don't affect MIDI
        // Tempo and time signatures have their own syntax nodes
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

    private Fraction GetDuration(DurationSyntax? duration)
    {
        if (duration == null) return _defaultDuration;
        _defaultDuration = duration.ToFraction();
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
        // Use a fixed short duration (1/32 note per grace note)
        int graceDuration = _ticksPerQuarter / 8; // 1/32 note

        foreach (var note in grace.Body.Items.OfType<NoteSyntax>())
        {
            int midiPitch = CalculateRelativeMidiPitch(note.Pitch);

            track.Notes.Add(new MidiNote(
                track.Channel,
                midiPitch,
                _velocity,
                _currentTick,
                graceDuration
            ));

            _currentTick += graceDuration;
        }
    }

    private (int velocity, int durationPercent) ApplyArticulationType(
        ArticulationType type, int velocity, int durationPercent)
    {
        return type switch
        {
            ArticulationType.Staccato => (velocity, 50),                      // Half duration
            ArticulationType.Accent => (Math.Min(127, velocity + 20), durationPercent),
            ArticulationType.Tenuto => (velocity, 100),                       // Full duration
            ArticulationType.Marcato => (Math.Min(127, velocity + 30), 80),   // Louder, slightly shorter
            ArticulationType.Portato => (velocity, 75),                       // Slightly shorter
            ArticulationType.Fermata => (velocity, 150),                      // Extended
            _ => (velocity, durationPercent)
        };
    }

    private void ProcessLyrics(LyricsBlockSyntax lyrics, MidiTrack track)
    {
        // For now, we add each syllable as a lyric event at the current tick
        // A more sophisticated implementation would sync lyrics with notes
        foreach (var syllable in lyrics.Syllables)
        {
            if (syllable is SyntaxTokenNode token)
            {
                var text = token.Text.Trim();
                if (!string.IsNullOrEmpty(text) && text != "--")
                {
                    // Add lyric event at current tick
                    track.Lyrics.Add(new LyricEvent(_currentTick, text));
                }
            }
        }
    }
}