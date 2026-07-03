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

using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.MusicXml;

/// <summary>
/// Exports a syntax tree to MusicXML format.
/// Supports multi-section/multi-part scores, ties, slurs, grace notes,
/// dynamics, and ornaments.
/// </summary>
public sealed class MusicXmlExporter
{
    // Divisions per quarter note. 24 is divisible by 2/3/4/6/8/12, so triplets
    // (and other tuplets) and notes down to 32nds get exact integer <duration>
    // values — 4 truncated a triplet eighth to 1 and a 32nd to 0.
    private const int DivisionsPerQuarter = 24;

    private int _currentOctave = 4;
    private int _currentStep = 0;     // c=0..b=6, for LilyPond relative-octave resolution (mirrors MidiExporter)
    // Octave mode (mirrors MeasureCollector): false = relative (default), true =
    // `octave absolute` ('/, are offsets from a fixed C4 anchor, no carry).
    private bool _octaveAbsolute;
    private bool _initialOctaveAbsolute; // file-level default, restored per part
    private bool _tieToNextNote;      // a tie was seen; the next note/chord ends it (gets tie-stop)
    private Fraction _defaultDuration = Fraction.Quarter;

    // Active tuplet nesting: (actual, normal) = "actual notes in the time of normal"
    // (a triplet is (3, 2)). Scales note durations and drives <time-modification>.
    private readonly Stack<(int Actual, int Normal)> _tupletStack = new();
    private int _measureNumber = 1;
    // Anacrusis (partial) state: while a pickup is open, accumulate its duration
    // and auto-close the implicit measure once it reaches the declared length
    // (mirrors MeasureCollector). _justAutoClosedPickup absorbs a written barline
    // that immediately follows the auto-close, so no empty measure is emitted.
    private bool _pendingPickup;
    private Fraction _pickupLength = Fraction.Zero;
    private Fraction _pickupAccumulated = Fraction.Zero;
    private bool _justAutoClosedPickup;
    private MusicXmlMeasure? _currentMeasure;
    private MusicXmlPart? _currentPart;
    private MusicXmlDocument? _document;

    private int _tempo = 120;
    private int _timeNumerator = 4;
    private int _timeDenominator = 4;
    private int _keyFifths = 0;
    private string _keyMode = "major";
    private string _clefSign = "G";
    private int _clefLine = 2;
    private string? _pendingDynamic;

    // Track parts across sections for multi-section support
    private readonly Dictionary<string, MusicXmlPart> _partsByName = new();

    // Part-option transpose for the part being written: the WRITTEN pitch is
    // respelled and the key signature shifts with it.
    private SyntaxNode? _root;
    private (int step, int alt, int oct)? _currentTranspose;

    // Variable/phrase resolution
    private readonly Dictionary<string, SyntaxNode> _variables = new();

    public MusicXmlDocument Export(SyntaxTree tree)
    {
        _document = new MusicXmlDocument();

        var root = tree.GetRoot();
        _root = root;

        // Check if there are section declarations (multi-part)
        var hasSections = root.DescendantNodes().OfType<SectionDeclarationSyntax>().Any();

        if (!hasSections)
        {
            // Simple single-part mode — collect metadata first, then process music
            CollectMetadata(root);
            _currentPart = new MusicXmlPart { Name = "Part 1" };
            _document.Parts.Add(_currentPart);
            StartNewMeasure(addAttributes: true);
            ProcessNode(root);
            FlushCurrentMeasure();
        }
        else
        {
            // Multi-section mode: collect metadata first, then process sections
            CollectMetadata(root);
            ProcessSections(root);
        }

        return _document;
    }

    private void CollectMetadata(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MetadataDeclarationSyntax metadata:
                    ProcessMetadata(metadata);
                    break;
                case TimeSignatureSyntax timeSig:
                    ProcessTimeSignature(timeSig);
                    break;
                case TempoDeclarationSyntax tempo:
                    ProcessTempo(tempo);
                    break;
                case KeySignatureSyntax key:
                    ProcessKeySignature(key);
                    break;
                case ClefDeclarationSyntax clef:
                    ProcessClef(clef);
                    break;
                case OctaveDirectiveSyntax octaveDir:
                    // Top-level `octave absolute/relative` sets the file default.
                    if (!IsInsideMusicContent(octaveDir))
                    {
                        _octaveAbsolute = octaveDir.IsAbsolute;
                        _initialOctaveAbsolute = octaveDir.IsAbsolute;
                    }
                    break;
                case PhraseDeclarationSyntax phrase:
                    _variables[phrase.Name.Text] = phrase.Body;
                    break;
                case VariableDeclarationSyntax varDecl:
                    _variables[varDecl.Name.Text] = varDecl.Expression;
                    break;
            }
        }
    }

    private void ProcessSections(SyntaxNode root)
    {
        foreach (var section in root.DescendantNodes().OfType<SectionDeclarationSyntax>())
        {
            // Each section may contain part blocks
            var partBlocks = section.DescendantNodes().OfType<PartBlockSyntax>().ToList();

            if (partBlocks.Count > 0)
            {
                // Section-level lyrics (siblings of the part blocks) sing the
                // FIRST part's melody, like the engraving binds them.
                var sectionLyrics = new List<LyricsBlockSyntax>();
                for (int i = 0; i < section.SlotCount; i++)
                    if (section.GetChild(i) is LyricsBlockSyntax slb)
                        sectionLyrics.Add(slb);

                MusicXmlPart? firstPart = null;
                int firstBefore = 0;
                foreach (var partBlock in partBlocks)
                {
                    if (firstPart == null)
                    {
                        firstBefore = _partsByName.TryGetValue(partBlock.Name, out var fp)
                            ? fp.Measures.Count
                            : 0;
                    }
                    ProcessPartBlock(partBlock);
                    firstPart ??= _partsByName[partBlock.Name];
                }
                if (firstPart != null && sectionLyrics.Count > 0)
                    AttachLyrics(firstPart, firstBefore, sectionLyrics);
            }
            else
            {
                // Section without named parts → treat as default part; its
                // lyrics blocks map onto the notes just emitted.
                EnsurePart("Part 1");
                int before = _currentPart!.Measures.Count;
                var blocks = new List<LyricsBlockSyntax>();
                for (int i = 0; i < section.SlotCount; i++)
                    if (section.GetChild(i) is LyricsBlockSyntax lb2)
                        blocks.Add(lb2);
                ProcessNode(section);
                FlushCurrentMeasure();
                AttachLyrics(_currentPart!, before, blocks);
            }
        }
    }

    private void ProcessPartBlock(PartBlockSyntax partBlock)
    {
        var partName = partBlock.Name;
        EnsurePart(partName);
        _currentTranspose = _root != null ? PartTranspose.Read(_root, partName) : null;

        // Reset state for this part's continuation
        _currentOctave = 4;
        _currentStep = 0;
        _octaveAbsolute = _initialOctaveAbsolute; // restore file-level octave mode
        _tieToNextNote = false;
        _defaultDuration = Fraction.Quarter;
        _pendingDynamic = null;

        // If this is the first measure for this part, add attributes
        bool isFirst = _currentPart!.Measures.Count == 0;
        StartNewMeasure(addAttributes: isFirst);

        // Process the content inside the part block; lyrics blocks are
        // collected and mapped onto the emitted notes afterwards.
        int measuresBefore = _currentPart!.Measures.Count;
        var lyricsBlocks = new List<LyricsBlockSyntax>();
        for (int i = 0; i < partBlock.SlotCount; i++)
        {
            var child = partBlock.GetChild(i);
            if (child is LyricsBlockSyntax lb)
            {
                lyricsBlocks.Add(lb);
                continue;
            }
            if (child != null && child is not SyntaxTokenNode)
                ProcessNode(child);
        }

        FlushCurrentMeasure();
        AttachLyrics(_currentPart!, measuresBefore, lyricsBlocks);
    }

    /// <summary>
    /// Maps a part block's lyrics onto the notes it just emitted, verse by
    /// verse: syllables advance note-by-note (rests, chord members, grace
    /// notes and tie continuations are not sung), a lyric barline syncs to
    /// the next measure, hyphens become syllabic begin/middle/end, extenders
    /// and melisma marks hold notes without new syllables. Vocal editors
    /// (VOCALOID, Synthesizer V, CeVIO, NEUTRINO) read these on import.
    /// </summary>
    private static void AttachLyrics(MusicXmlPart part, int measuresBefore, List<LyricsBlockSyntax> lyricsBlocks)
    {
        if (lyricsBlocks.Count == 0)
            return;
        var measures = part.Measures.Skip(measuresBefore).ToList();
        for (int verse = 0; verse < lyricsBlocks.Count; verse++)
        {
            var syllables = Svg.Collector.LyricCollector.ParseSyllables(lyricsBlocks[verse]);
            int mi = 0, ni = 0;
            bool prevHyphen = false;

            MusicXmlNote? NextSingable()
            {
                while (mi < measures.Count)
                {
                    var notes = measures[mi].Notes;
                    while (ni < notes.Count)
                    {
                        var n = notes[ni++];
                        if (!n.IsRest && !n.IsChord && !n.IsGrace && !n.TieStop)
                            return n;
                    }
                    mi++;
                    ni = 0;
                }
                return null;
            }

            foreach (var (text, connector, _, isBarline, isMelisma) in syllables)
            {
                if (isBarline)
                {
                    // Lyric bar = measure sync: jump to the next measure's notes.
                    mi++;
                    ni = 0;
                    continue;
                }
                if (isMelisma)
                {
                    NextSingable(); // held note, no new syllable
                    continue;
                }
                var target = NextSingable();
                if (target == null)
                    return; // more syllables than notes — stop quietly
                bool hyphen = connector == Svg.Model.LyricConnectorType.Hyphen;
                string syllabic = prevHyphen
                    ? (hyphen ? "middle" : "end")
                    : (hyphen ? "begin" : "single");
                target.Lyrics.Add((verse + 1, text, syllabic,
                    connector == Svg.Model.LyricConnectorType.Extender));
                prevHyphen = hyphen;
            }
        }
    }

    private void EnsurePart(string name)
    {
        if (_partsByName.TryGetValue(name, out var existing))
        {
            _currentPart = existing;
            _measureNumber = existing.Measures.Count + 1;
        }
        else
        {
            _currentPart = new MusicXmlPart { Name = name };
            _document!.Parts.Add(_currentPart);
            _partsByName[name] = _currentPart;
            _measureNumber = 1;
        }
    }

    private void FlushCurrentMeasure()
    {
        if (_currentMeasure != null && _currentMeasure.Notes.Count > 0 && _currentPart != null)
        {
            _currentPart.Measures.Add(_currentMeasure);
        }
        _currentMeasure = null;
        _pendingPickup = false;
        _justAutoClosedPickup = false;
    }

    /// <summary>
    /// While a leading 'partial' pickup is open, accumulate its duration and
    /// auto-close the implicit measure once it reaches the declared length — even
    /// with no written barline — mirroring MeasureCollector so MusicXML and SVG
    /// split the pickup identically.
    /// </summary>
    private void MaybeClosePickup(Fraction added)
    {
        if (!_pendingPickup)
            return;
        _pickupAccumulated += added;
        if (_pickupAccumulated >= _pickupLength)
        {
            _pendingPickup = false;
            if (_currentMeasure != null && _currentPart != null && _currentMeasure.Notes.Count > 0)
            {
                _currentPart.Measures.Add(_currentMeasure);
                StartNewMeasure();
                _justAutoClosedPickup = true;
            }
        }
    }

    private void StartNewMeasure(bool addAttributes = false)
    {
        _currentMeasure = new MusicXmlMeasure { Number = _measureNumber++ };

        if (addAttributes)
        {
            _currentMeasure.Attributes = new MusicXmlAttributes
            {
                Divisions = DivisionsPerQuarter,
                TimeBeats = _timeNumerator,
                TimeBeatType = _timeDenominator,
                KeyFifths = _currentTranspose is { } trk
                    ? _keyFifths + PitchTransposer.KeySignatureFifthsShift(trk.step, trk.alt)
                    : _keyFifths,
                KeyMode = _keyMode,
                ClefSign = _clefSign,
                ClefLine = _clefLine
            };

            _currentMeasure.Direction = new MusicXmlDirection { Tempo = _tempo };
        }
    }

    private void ProcessNode(SyntaxNode node)
    {
        switch (node)
        {
            case CompilationUnitSyntax unit:
                foreach (var member in unit.Members)
                    ProcessNode(member);
                break;

            case LyricsBlockSyntax:
                // Handled AFTER the notes exist (AttachLyrics maps syllables
                // onto the emitted notes); walking it here would do nothing
                // useful and the default recursion could misfire.
                break;

            case TimeSignatureSyntax timeSig:
                ProcessTimeSignature(timeSig);
                break;

            case TempoDeclarationSyntax tempo:
                ProcessTempo(tempo);
                break;

            case MetadataDeclarationSyntax metadata:
                ProcessMetadata(metadata);
                break;

            case KeySignatureSyntax key:
                ProcessKeySignature(key);
                break;

            case ClefDeclarationSyntax clef:
                ProcessClef(clef);
                break;

            case OctaveDirectiveSyntax octaveDir:
                // Mid-stream octave-mode switch (affects subsequent pitches only).
                _octaveAbsolute = octaveDir.IsAbsolute;
                break;

            case MusicBlockSyntax block:
                foreach (var item in block.Items)
                    ProcessNode(item);
                break;

            case NoteSyntax note:
                ProcessNote(note);
                break;

            case ChordSyntax chord:
                ProcessChord(chord);
                break;

            case RestSyntax rest:
                ProcessRest(rest);
                break;

            case PartialDeclarationSyntax partial:
                // Anacrusis: the measure currently being built is a pickup. Mark it
                // implicit and number it 0, so the first FULL measure becomes 1, and
                // arm the duration-based auto-close (no written barline required).
                // LILYPOND-REF: ly/music-functions-init.ly:1670-1678 \partial.
                if (_currentMeasure != null && _currentMeasure.Notes.Count == 0)
                {
                    _currentMeasure.Implicit = true;
                    _currentMeasure.Number = 0;
                    _measureNumber = 1;
                    _pendingPickup = true;
                    _pickupLength = partial.ToFraction();
                    _pickupAccumulated = Fraction.Zero;
                }
                break;

            case BarlineSyntax:
                // A barline immediately after a pickup auto-close is redundant —
                // the pickup measure already closed, so swallow it (no empty bar).
                if (_justAutoClosedPickup)
                {
                    _justAutoClosedPickup = false;
                    break;
                }
                if (_currentMeasure != null && _currentPart != null)
                {
                    _currentPart.Measures.Add(_currentMeasure);
                    StartNewMeasure();
                }
                break;

            case DynamicSyntax dynamic:
                _pendingDynamic = dynamic.DynamicToken.Text;
                break;

            case TieSyntax:
                // Tie follows a note — mark the last note as tie start, and flag
                // the next note/chord so it emits the matching tie-stop.
                if (_currentMeasure != null && _currentMeasure.Notes.Count > 0)
                    _currentMeasure.Notes[^1].TieStart = true;
                _tieToNextNote = true;
                break;

            case SlurSyntax slur:
                // Slur follows a note — mark start/stop on the last note
                if (_currentMeasure != null && _currentMeasure.Notes.Count > 0)
                {
                    if (slur.IsOpen)
                        _currentMeasure.Notes[^1].SlurStart = true;
                    else
                        _currentMeasure.Notes[^1].SlurStop = true;
                }
                break;

            case GraceExpressionSyntax grace:
                ProcessGraceNotes(grace);
                break;

            case TupletExpressionSyntax tuplet:
                // A tuplet plays TupletRatio notes in the time of BaseDivision
                // (triplet = 3 in 2). Scale durations and tag time-modification for
                // the body's notes; nested tuplets multiply.
                _tupletStack.Push((tuplet.TupletRatio, tuplet.BaseDivision));
                ProcessNode(tuplet.Body);
                _tupletStack.Pop();
                break;

            case VariableReferenceSyntax varRef:
                if (_variables.TryGetValue(varRef.Name.Text, out var varBody))
                {
                    // Phrase bodies evaluate in a fresh relative frame so a
                    // $phrase means the same pitches at every call site
                    // (matches MeasureCollector's RelativeResetMarker).
                    _currentOctave = 4;
                    _currentStep = 0;
                    _defaultDuration = Fraction.Quarter;
                    ProcessNode(varBody);
                }
                break;

            case PhraseDeclarationSyntax:
            case VariableDeclarationSyntax:
            case PartDeclarationSyntax:
            case SectionDeclarationSyntax:
            case StructureDeclarationSyntax:
                // Skip declarations — they're handled elsewhere
                break;

            default:
                for (int i = 0; i < node.SlotCount; i++)
                {
                    var child = node.GetChild(i);
                    if (child != null && child is not SyntaxTokenNode)
                        ProcessNode(child);
                }
                break;
        }
    }

    private void ProcessTimeSignature(TimeSignatureSyntax timeSig)
    {
        _timeNumerator = timeSig.Beats;
        _timeDenominator = timeSig.BeatType;
    }

    private void ProcessTempo(TempoDeclarationSyntax tempo)
    {
        if (tempo.Bpm is not int bpm)
            return;
        _tempo = bpm;
        // A mid-piece tempo change emits a metronome direction at this point; the
        // initial tempo is carried by the first measure's attributes direction.
        if (_currentMeasure != null && (_currentMeasure.Notes.Count > 0 || _currentMeasure.Number > 1))
            _currentMeasure.Directions.Add(new MusicXmlDirection { Tempo = bpm });
    }

    private void ProcessMetadata(MetadataDeclarationSyntax metadata)
    {
        if (_document == null) return;

        var keyword = metadata.Keyword.ToLowerInvariant();

        if (keyword == "title" && metadata.StringValue is string title)
            _document.Title = title;
        else if (keyword == "composer" && metadata.StringValue is string composer)
            _document.Composer = composer;
    }

    private void ProcessKeySignature(KeySignatureSyntax key)
    {
        var pitch = key.Pitch?.ToFullString().Trim().ToLower();
        // MusicXML's <mode> takes the church-mode names directly.
        var mode = key.Mode.Text.ToLowerInvariant();

        // Delegate to KeySpelling (the single source of truth for tonic -> fifths);
        // an unrecognized tonic falls back to 0 (C), as before.
        _keyFifths = KeySpelling.SharpsFor(pitch ?? "", mode) ?? 0;
        _keyMode = mode;
    }

    private void ProcessClef(ClefDeclarationSyntax clef)
    {
        var clefName = clef.ClefName?.Text.ToLower();
        (_clefSign, _clefLine) = clefName switch
        {
            "treble" => ("G", 2),
            "bass" => ("F", 4),
            "alto" => ("C", 3),
            "tenor" => ("C", 4),
            _ => ("G", 2)
        };
    }

    // Respells a written pitch for a transposed part (no-op otherwise). The
    // relative octave is resolved on the ORIGINAL pitch by the caller; this only
    // moves the printed step / alter / octave.
    private (string step, int alter, int octave) ApplyTranspose(
        PitchSyntax pitch, string step, int alter, int octave)
    {
        if (_currentTranspose is not { } tr)
            return (step, alter, octave);
        var (ns, na, no) = PitchTransposer.Transpose(
            RelativeOctave.StepIndex(pitch.BaseName), pitch.AccidentalOffset, octave,
            tr.step, tr.alt, tr.oct);
        return ("CDEFGAB"[ns].ToString(), na, no);
    }

    private void ProcessNote(NoteSyntax note)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var (step, alter) = ParsePitch(note.Pitch);
        int targetOctave = ResolveRelativeOctave(note.Pitch);
        (step, alter, targetOctave) = ApplyTranspose(note.Pitch, step, alter, targetOctave);

        var duration = GetDuration(note.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        // Emit pending dynamic as direction before the note
        EmitPendingDynamic();

        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        var xmlNote = new MusicXmlNote
        {
            Step = step,
            Alter = alter,
            Octave = targetOctave,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            ActualNotes = tupletActual,
            NormalNotes = tupletNormal
        };

        // Process articulations and slurs
        ProcessArticulations(note.Articulations, xmlNote);

        // Tie pairing: a preceding '~' ends on this note (tie-stop); a '~' on
        // this note (sibling or articulation) starts a tie to the next note.
        if (_tieToNextNote) { xmlNote.TieStop = true; _tieToNextNote = false; }
        if (note.Articulations.OfType<TieSyntax>().Any()) { xmlNote.TieStart = true; _tieToNextNote = true; }

        _currentMeasure.Notes.Add(xmlNote);
        MaybeClosePickup(duration);
    }

    private void ProcessChord(ChordSyntax chord)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var pitches = chord.Pitches.ToList();
        if (pitches.Count == 0) return;

        var duration = GetDuration(chord.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        // Emit pending dynamic as direction before the chord
        EmitPendingDynamic();

        // LilyPond relative chords: each note is relative to the PREVIOUS note in
        // the chord (state advances per pitch); the note after the chord is relative
        // to the FIRST pitch. Matches MidiExporter and MeasureCollector.
        int firstStep = _currentStep, firstOctave = _currentOctave;
        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        bool isFirst = true;
        foreach (var pitch in pitches)
        {
            var (step, alter) = ParsePitch(pitch);
            int targetOctave = ResolveRelativeOctave(pitch); // advances state per pitch
            (step, alter, targetOctave) = ApplyTranspose(pitch, step, alter, targetOctave);

            if (isFirst) { firstStep = _currentStep; firstOctave = _currentOctave; }

            var xmlNote = new MusicXmlNote
            {
                Step = step,
                Alter = alter,
                Octave = targetOctave,
                Duration = durationTicks,
                Type = type,
                Dots = dots,
                IsChord = !isFirst,
                ActualNotes = tupletActual,
                NormalNotes = tupletNormal
            };

            // Add articulations + tie pairing only on the first note of the chord.
            if (isFirst)
            {
                ProcessArticulations(chord.Articulations, xmlNote);
                if (_tieToNextNote) { xmlNote.TieStop = true; _tieToNextNote = false; }
                if (chord.Articulations.OfType<TieSyntax>().Any()) { xmlNote.TieStart = true; _tieToNextNote = true; }
                isFirst = false;
            }

            _currentMeasure.Notes.Add(xmlNote);
        }

        // Continue from the first chord note (LilyPond: next note is relative to
        // the chord's first pitch).
        _currentStep = firstStep;
        _currentOctave = firstOctave;
        MaybeClosePickup(duration);
    }

    private void ProcessRest(RestSyntax rest)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var duration = GetDuration(rest.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        var xmlNote = new MusicXmlNote
        {
            IsRest = true,
            Duration = durationTicks,
            Type = type,
            Dots = dots
        };

        _currentMeasure.Notes.Add(xmlNote);
        MaybeClosePickup(duration);
    }

    private void ProcessGraceNotes(GraceExpressionSyntax grace)
    {
        if (_currentMeasure == null) return;

        bool isAcciaccatura = grace.IsAcciaccatura;

        foreach (var item in grace.Body.Items)
        {
            if (item is NoteSyntax note)
            {
                var (step, alter) = ParsePitch(note.Pitch);
                int targetOctave = ResolveRelativeOctave(note.Pitch);

                var duration = GetDuration(note.Duration);
                var (type, _) = GetNoteType(duration);

                var xmlNote = new MusicXmlNote
                {
                    IsGrace = true,
                    IsSlash = isAcciaccatura,
                    Step = step,
                    Alter = alter,
                    Octave = targetOctave,
                    Type = type
                };

                _currentMeasure.Notes.Add(xmlNote);
            }
        }
    }

    private void ProcessArticulations(IEnumerable<SyntaxNode> articulations, MusicXmlNote xmlNote)
    {
        foreach (var artic in articulations)
        {
            if (artic is ArticulationSyntax articulation)
            {
                var articName = MapArticulation(articulation.Type);
                if (articName != null)
                    xmlNote.Articulations.Add(articName);

                var ornamentName = MapOrnament(articulation.Type);
                if (ornamentName != null)
                    xmlNote.Ornaments.Add(ornamentName);
            }
            else if (artic is DynamicSyntax dynamic)
            {
                _pendingDynamic = dynamic.DynamicToken.Text;
            }
            else if (artic is SlurSyntax slur)
            {
                if (slur.IsOpen)
                    xmlNote.SlurStart = true;
                else
                    xmlNote.SlurStop = true;
            }
        }
    }

    private void EmitPendingDynamic()
    {
        if (_pendingDynamic != null && _currentMeasure != null)
        {
            _currentMeasure.Directions.Add(new MusicXmlDirection
            {
                DynamicType = _pendingDynamic,
                Placement = "below"
            });
            _pendingDynamic = null;
        }
    }

    private static string? MapArticulation(ArticulationType type)
    {
        return type switch
        {
            ArticulationType.Staccato => "staccato",
            ArticulationType.Staccatissimo => "staccatissimo",
            ArticulationType.Accent => "accent",
            ArticulationType.Tenuto => "tenuto",
            ArticulationType.Marcato => "strong-accent",
            ArticulationType.Fermata => "fermata",
            ArticulationType.Portato => "detached-legato",
            _ => null
        };
    }

    private static string? MapOrnament(ArticulationType type)
    {
        return type switch
        {
            ArticulationType.Trill => "trill-mark",
            ArticulationType.Mordent => "mordent",
            ArticulationType.Prall => "inverted-mordent",
            ArticulationType.Turn => "turn",
            ArticulationType.InvertedTurn => "inverted-turn",
            ArticulationType.PrallTriller => "inverted-mordent",
            _ => null
        };
    }

    private (string step, int alter) ParsePitch(PitchSyntax pitch)
    {
        string step = char.ToUpper(pitch.BaseName).ToString();
        return (step, pitch.AccidentalOffset);
    }

    /// <summary>
    /// Resolves the absolute octave of a pitch using LilyPond's relative-octave
    /// rule (nearest octave to the previous pitch, within a fourth), then applies
    /// the explicit ' / , offset. Mirrors <c>MidiExporter.CalculateRelativeMidiPitch</c>
    /// so MIDI and MusicXML octaves agree. Updates the running step/octave state.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/pitch.cc — relative octave (closest interval).</remarks>
    private int ResolveRelativeOctave(PitchSyntax pitch)
    {
        int noteName = StepIndex(pitch.BaseName);

        // Absolute mode: '/, are offsets from a fixed C4 anchor (bare c = C4),
        // stateless. Relative mode (default): closest-octave rule + '/, offset,
        // shared with the collector and the MIDI exporter (RelativeOctave is the
        // single source of truth). Matches MeasureCollector exactly.
        int targetOctave = _octaveAbsolute
            ? 4 + pitch.OctaveOffset
            : RelativeOctave.Resolve(
                _currentStep, _currentOctave, noteName, pitch.OctaveOffset);

        _currentStep = noteName;
        _currentOctave = targetOctave;
        return targetOctave;
    }

    /// <summary>True when <paramref name="node"/> is nested inside a phrase /
    /// section / part body (music content) rather than a top-level declaration.</summary>
    private static bool IsInsideMusicContent(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PhraseDeclarationSyntax or SectionDeclarationSyntax
                or VariableDeclarationSyntax or PartBlockSyntax)
                return true;
        return false;
    }

    private static int StepIndex(char baseName) => RelativeOctave.StepIndex(baseName);

    private Fraction GetDuration(DurationSyntax? duration)
    {
        if (duration == null) return _defaultDuration;
        _defaultDuration = duration.ToFraction();
        return _defaultDuration;
    }

    private int FractionToTicks(Fraction frac)
    {
        long ticks = (long)frac.Numerator * DivisionsPerQuarter * 4 / frac.Denominator;
        // Each enclosing tuplet shrinks the played duration to normal/actual.
        foreach (var (actual, normal) in _tupletStack)
            ticks = ticks * normal / actual;
        return (int)ticks;
    }

    /// <summary>
    /// The cumulative tuplet ratio to stamp on a note as &lt;time-modification&gt;:
    /// the product of actual/normal across all enclosing tuplets (null when none).
    /// </summary>
    private (int? Actual, int? Normal) CurrentTupletRatio()
    {
        if (_tupletStack.Count == 0)
            return (null, null);
        int actual = 1, normal = 1;
        foreach (var (a, n) in _tupletStack) { actual *= a; normal *= n; }
        return (actual, normal);
    }

    private (string type, int dots) GetNoteType(Fraction duration)
    {
        int dots = 0;
        int baseDenom = (int)duration.Denominator;

        // A k-dotted note reduces to numerator (2^(k+1) - 1) — 3, 7, 15, 31, … — over
        // the base value's denominator scaled by 2^k (e.g. dotted quarter 3/8, double
        // 7/16, triple 15/32). Recover the dot count from that pattern; previously only
        // single/double dots were special-cased, so a triple-dotted note mis-exported as
        // an undotted shorter value (15/64 -> "64th" instead of a triple-dotted eighth).
        for (int k = 1; k <= 8; k++)
        {
            if (duration.Numerator == (1L << (k + 1)) - 1 && duration.Denominator % (1L << k) == 0)
            {
                dots = k;
                baseDenom = (int)(duration.Denominator >> k);
                break;
            }
        }

        string type = baseDenom switch
        {
            1 => "whole",
            2 => "half",
            4 => "quarter",
            8 => "eighth",
            16 => "16th",
            32 => "32nd",
            64 => "64th",
            128 => "128th",
            _ => "quarter"
        };

        return (type, dots);
    }
}
