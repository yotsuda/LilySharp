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
    // Octave mode (mirrors MeasureCollector): false = relative (default), true =
    // `octave absolute` ('/, are offsets from a fixed C4 anchor, no carry).
    private bool _octaveAbsolute;
    private Fraction _defaultDuration = Fraction.Quarter;
    private int _tempo = 120;
    private int _velocity = 80;
    private readonly Stack<(int numerator, int denominator)> _tupletStack = new();
    private int _timeNumerator = 4;
    private int _timeDenominator = 4;

    // Tie handling: a tie (~) merges the next same-pitch note into the previous
    // one (one sustained note) instead of re-articulating it.
    private bool _tiePending;
    private int _lastNoteIndex = -1;
    private MidiTrack? _lastNoteTrack;

    // Sounding-pitch transpose for the part currently being played. A part option
    // transpose: shifts every note by the interval's semitones (no respelling).
    private SyntaxNode? _root;
    private int _currentTransposeSemitones;

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
        _root = tree.GetRoot();
        ProcessNode(_root, mainTrack, conductorTrack);

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
                ProcessSequence(cu.Members.ToList(), track, conductorTrack);
                break;

            case PartDeclarationSyntax part:
                ProcessChildren(part, track, conductorTrack);
                break;

            case StaffDeclarationSyntax staff:
                ProcessChildren(staff, track, conductorTrack);
                break;

            case PartBlockSyntax partBlock:
                // A section's `partName { ... }` block: arm the part's transpose
                // (sounding-pitch shift) for the notes inside, then disarm it.
                var transpose = _root != null ? PartTranspose.Read(_root, partBlock.Name) : null;
                _currentTransposeSemitones = transpose is { } t
                    ? PitchTransposer.IntervalSemitones(t.step, t.alt, t.oct)
                    : 0;
                ProcessChildren(partBlock, track, conductorTrack);
                _currentTransposeSemitones = 0;
                break;

            case OctaveDirectiveSyntax octaveDir:
                // Octave-mode switch (top-level default or mid-stream). MIDI walks
                // in source order, so a file-level directive precedes the notes.
                _octaveAbsolute = octaveDir.IsAbsolute;
                break;

            case MusicBlockSyntax block:
                ProcessSequence(block.Items.ToList(), track, conductorTrack);
                break;

            case NoteSyntax note:
                ProcessNote(note, track);
                break;

            case TieSyntax:
                // Tie between two sibling notes — the next same-pitch note extends
                // the previous one rather than re-articulating.
                _tiePending = true;
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

            case PhraseDeclarationSyntax phrase:
                // Phrase bodies evaluate in a fresh relative frame so a phrase
                // means the same pitches wherever it is used (matches
                // MeasureCollector's RelativeResetMarker). MIDI plays phrase
                // declarations in source order, so the reset applies here.
                _currentNoteName = 0;
                _currentOctave = 4;
                _defaultDuration = Fraction.Quarter;
                ProcessChildren(phrase, track, conductorTrack);
                break;

            default:
                // Process any other nodes by visiting their children
                ProcessChildren(node, track, conductorTrack);
                break;
        }
    }

    private void ProcessChildren(SyntaxNode node, MidiTrack track, MidiTrack conductorTrack)
    {
        var children = new List<SyntaxNode>();
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && child is not SyntaxTokenNode)
                children.Add(child);
        }
        ProcessSequence(children, track, conductorTrack);
    }

    /// <summary>
    /// Processes a sibling sequence, expanding symbolic repeats: the span between
    /// a <c>|:</c> and its matching <c>:|</c> is played twice (volta repeat) so
    /// inline <c>|: … :|</c> actually repeats in playback, not just visually.
    /// Each pass restarts from the same relative-octave/duration/dynamic context.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/volta-repeat — repeated music is performed N times.</remarks>
    private void ProcessSequence(List<SyntaxNode> items, MidiTrack track, MidiTrack conductorTrack)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (IsRepeatBar(items[i], SyntaxKind.RepeatStartBar))
            {
                int end = FindMatchingRepeatEnd(items, i);
                if (end > i)
                {
                    int last = ProcessRepeatSpan(items, i, end, track, conductorTrack);
                    i = last; // continue after the repeat (and any trailing endings)
                    continue;
                }
            }
            ProcessNode(items[i], track, conductorTrack);
        }
    }

    /// <summary>
    /// Plays a <c>|: … :|</c> span: the common body N times, selecting the matching
    /// inline volta ending (<c>[1. …] [2. …]</c>) on each pass. N comes from an
    /// explicit <c>:|*N</c>, else the highest volta number, else the default 2.
    /// Returns the index of the last item consumed (the <c>:|</c> or the last
    /// trailing ending) so the caller resumes after it.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/volta-repeat — body performed N times, i-th ending per pass.</remarks>
    private int ProcessRepeatSpan(List<SyntaxNode> items, int start, int end,
        MidiTrack track, MidiTrack conductorTrack)
    {
        // Partition the inner span into the common body and any inline volta endings.
        var body = new List<SyntaxNode>();
        var endings = new List<InlineVoltaSyntax>();
        for (int k = start + 1; k < end; k++)
        {
            if (items[k] is InlineVoltaSyntax ev)
                endings.Add(ev);
            else if (endings.Count == 0)
                body.Add(items[k]);
            // (items after an early ending but before :| are non-standard; ignored.)
        }

        // Trailing endings live after the :| (e.g. |: body [1. …] :| [2. …]).
        int last = end;
        for (int k = end + 1; k < items.Count && items[k] is InlineVoltaSyntax lv; k++)
        {
            endings.Add(lv);
            last = k;
        }

        var endBar = items[end] as BarlineSyntax;
        int count;
        if (endBar?.HasExplicitRepeatCount == true)
            count = endBar.RepeatCount;
        else if (endings.Count > 0)
            count = Math.Max(2, endings.Max(e => e.MaxNumber));
        else
            count = 2;

        int savedName = _currentNoteName, savedOctave = _currentOctave, savedVelocity = _velocity;
        var savedDuration = _defaultDuration;
        for (int pass = 1; pass <= count; pass++)
        {
            _currentNoteName = savedName;
            _currentOctave = savedOctave;
            _velocity = savedVelocity;
            _defaultDuration = savedDuration;

            ProcessSequence(body, track, conductorTrack);

            if (endings.Count > 0)
            {
                var ending = SelectEnding(endings, pass);
                if (ending != null)
                    ProcessSequence(ending.Items.ToList(), track, conductorTrack);
            }
        }

        return last;
    }

    /// <summary>
    /// Picks the inline volta ending for a (1-based) repeat pass: the one whose
    /// number set contains the pass, else the last ending (clamping, mirroring the
    /// keyword path's <c>Math.Min(i, count-1)</c> selection).
    /// </summary>
    private static InlineVoltaSyntax? SelectEnding(List<InlineVoltaSyntax> endings, int pass)
    {
        foreach (var ending in endings)
        {
            if (ending.Matches(pass))
                return ending;
        }
        return endings.Count > 0 ? endings[^1] : null;
    }

    private static bool IsRepeatBar(SyntaxNode node, SyntaxKind kind)
        => node is BarlineSyntax b && b.BarToken.Kind == kind;

    private static int FindMatchingRepeatEnd(List<SyntaxNode> items, int start)
    {
        int depth = 0;
        for (int i = start + 1; i < items.Count; i++)
        {
            if (IsRepeatBar(items[i], SyntaxKind.RepeatStartBar)) depth++;
            else if (IsRepeatBar(items[i], SyntaxKind.RepeatEndBar))
            {
                if (depth == 0) return i;
                depth--;
            }
        }
        return -1; // unmatched |: — caller falls back to normal processing
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

        // Absolute mode: '/, are offsets from a fixed C4 anchor (bare c = C4),
        // stateless. Relative mode (default): closest-octave rule + '/, offset,
        // shared with the collector and the MusicXML exporter (RelativeOctave is
        // the single source of truth). Matches MeasureCollector exactly.
        int targetOctave = _octaveAbsolute
            ? 4 + pitch.OctaveOffset
            : RelativeOctave.Resolve(
                _currentNoteName, _currentOctave, noteName, pitch.OctaveOffset);

        // Update current state for next note
        _currentNoteName = noteName;
        _currentOctave = targetOctave;

        // Calculate MIDI pitch
        int basePitch = pitch.BaseName switch
        {
            'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5,
            'g' => 7, 'a' => 9, 'b' => 11, _ => 0
        };

        return Math.Clamp(
            basePitch + pitch.AccidentalOffset + (targetOctave + 1) * 12 + _currentTransposeSemitones,
            0, 127);
    }

    /// <summary>
    /// Gets the note name index (c=0, d=1, e=2, f=3, g=4, a=5, b=6).
    /// </summary>
    private static int GetNoteName(char baseName) => RelativeOctave.StepIndex(baseName);

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

        bool startsTie = note.Articulations.OfType<TieSyntax>().Any();

        // If the previous note tied into this one (same pitch on the same track),
        // extend that note instead of emitting a new note-on/off pair.
        if (_tiePending && _lastNoteTrack == track
            && _lastNoteIndex >= 0 && _lastNoteIndex < track.Notes.Count
            && track.Notes[_lastNoteIndex].Pitch == midiPitch)
        {
            var prev = track.Notes[_lastNoteIndex];
            track.Notes[_lastNoteIndex] = prev with { DurationTicks = prev.DurationTicks + durationTicks };
            _currentTick += durationTicks;
            _tiePending = startsTie; // continue a tie chain (c~ c~ c)
            return;
        }

        track.Notes.Add(new MidiNote(track.Channel, midiPitch, velocity, _currentTick, actualDuration));
        _lastNoteIndex = track.Notes.Count - 1;
        _lastNoteTrack = track;
        _currentTick += durationTicks;
        _tiePending = startsTie;
    }

    private void ProcessRest(RestSyntax rest)
    {
        // A rest breaks any pending tie (a tie cannot span a rest).
        _tiePending = false;
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

        // LilyPond relative chords: each note is relative to the PREVIOUS note in
        // the chord (so the state advances per pitch); the note AFTER the chord is
        // relative to the chord's FIRST note. Matches MeasureCollector.CreateChordItem.
        // LILYPOND-REF: notation manual — relative octave within chords.
        bool isFirst = true;
        int firstNoteName = _currentNoteName;
        int firstOctave = _currentOctave;

        foreach (var pitch in pitches)
        {
            int midiPitch = CalculateRelativeMidiPitch(pitch); // advances state per pitch
            if (isFirst)
            {
                firstNoteName = _currentNoteName;
                firstOctave = _currentOctave;
                isFirst = false;
            }
            track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks));
        }

        // Next note is relative to the chord's first pitch.
        _currentNoteName = firstNoteName;
        _currentOctave = firstOctave;

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