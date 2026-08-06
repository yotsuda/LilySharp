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

namespace LilySharp.Core.Midi;

/// <summary>
/// Exports a LilySharp syntax tree to MIDI format.
/// </summary>
public sealed class MidiExporter
{
    private readonly int _ticksPerQuarter;
    private int _currentTick;
    // Grace notes steal their time from the FOLLOWING note (LilyPond's MIDI
    // convention): the graces sound before the beat's note, and that note's
    // sounding+advance duration is shortened by this many ticks so every later
    // note stays on the metric grid. Accumulated by ProcessGrace, consumed once
    // by the next timed event (note/chord/rest) via ConsumeGraceSteal.
    private int _pendingGraceSteal;
    private int _currentOctave = 4;
    private int _currentNoteName = 0; // c=0, d=1, e=2, f=3, g=4, a=5, b=6
    // Octave mode (mirrors MeasureCollector): false = relative (default), true =
    // `octave absolute` ('/, are offsets from a fixed C4 anchor, no carry).
    private bool _octaveAbsolute;

    // The part's RELATIVE-frame seed: `octave N` > instrument preset > the clef's own
    // octave, exactly as the page resolves it (InstrumentDefaults.AnchorOctave).
    private int _partOctaveAnchor = 4;

    // …and the part's ABSOLUTE-mode base, which is a DIFFERENT rule and must not be folded
    // into the one above: only an explicit `octave N` moves it (OctaveContext: "the clef
    // default is deliberately NOT used here"). One field served both until 2026-08-02, so
    // giving the relative seed its clef step immediately dragged `octave absolute` parts
    // down with it.
    private int _partAbsoluteBase = 4;

    // Preview-synth timbre family of the part currently playing, resolved
    // from its `instrument` property (or the part name itself).
    private int _currentTimbre;

    // Phrase bodies by name; a $reference expands in place (fresh default
    // frame), declarations are silent. _activePhrases guards recursion.
    private Dictionary<string, SyntaxNode>? _phraseBodies;
    private readonly HashSet<string> _activePhrases = new();

    // Structure-driven playback: sections play in `structure { … }` order
    // (with |: :| repeats and volta alternatives), not declaration order.
    // Sections keyed by name. A name maps to a LIST because part-major layout
    // declares the same section name once per part (`part melody { section A … }`,
    // `part bass { section A … }`); a structure reference plays them all.
    private Dictionary<string, List<SectionDeclarationSyntax>>? _sections;
    // section name -> its own header key (a section carrying a `key` but no inline
    // music: section-major, or a standalone part-major header `section A { key g major }`).
    // Applied up front to every part of the section, since it is not walked with the
    // part cell's music.
    private readonly Dictionary<string, KeySignatureSyntax> _sectionHeaderKeys = new();
    private bool _formDriven;
    private bool _formPlayed;

    // Part declarations by name (first-wins), built once in Export so PartTimbre /
    // PartOctaveAnchor / part-transpose lookups are O(1) instead of a full-tree
    // scan per part per section per repeat pass.
    private Dictionary<string, PartDeclarationSyntax> _partDecls = new();
    // Score-wide `transpose` default (a free-standing top-level transpose),
    // computed once; a part's own transpose overrides it.
    private (int step, int alt, int oct)? _scoreTransposeDefault;

    // Per-PART relative-pitch state, carried across sections and repeat
    // passes — matching the collector, which streams one part's blocks
    // sequentially through the whole piece.
    private readonly Dictionary<string, (int NoteName, int Octave, Fraction Dur)> _partPitchLanes = new();

    // Printed-copy ordinal per source position: the k-th onset of a source
    // position corresponds to the k-th PRINTED copy (phrase expansions).
    // Repeat passes whose material is engraved only ONCE (|: :| second pass,
    // percent/tremolo iterations) restore a snapshot so the ordinal replays.
    private Dictionary<int, int> _sourceOrdinals = new();

    private int NextOrdinal(int pos)
    {
        _sourceOrdinals.TryGetValue(pos, out int k);
        _sourceOrdinals[pos] = k + 1;
        return k;
    }
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

    // Phrase-scoped diatonic shift (± scale steps) from a reference's interval
    // argument (Melody'(3) = +2), applied to every written pitch in the WRITTEN
    // key before the chromatic transpose — see WrittenToMidi. Nested references
    // compose additively; saved/restored around each phrase body.
    private int _diatonicShiftSteps;

    /// <summary>Written pitch → MIDI key: the phrase-scoped diatonic shift (modal,
    /// in the written key), then the chromatic transpose, then the clamp. The ONE
    /// funnel every pitched emission uses, so the shift cannot miss a path.</summary>
    private int WrittenToMidi(int step, int alter, int octave)
    {
        if (_diatonicShiftSteps != 0)
            (step, alter, octave) = LilySharp.Core.Music.DiatonicShift.Apply(
                step, alter, octave, _diatonicShiftSteps, _keySharps);
        return Math.Clamp(
            RelativeOctave.StepToMidi(step, alter, octave) + _currentTransposeSemitones, 0, 127);
    }

    // Phrase auto-transpose (movable motif): a phrase written in the score's home
    // key sounds in whatever key is in effect where it is referenced. _ambientTonic
    // tracks the running key (reset to home per section, advanced by key changes);
    // the reference site adds the home→ambient shift to _currentTransposeSemitones.
    private KeyTonic _homeTonic = KeyTonic.CMajor;
    private KeyTonic _ambientTonic = KeyTonic.CMajor;

    // The running WRITTEN key signature (sharps; flats negative) that scale-degree
    // chords (<d 3 5>) stack against. Reset to the score's key per section; the
    // part transpose is applied separately (semitone shift), so this stays written.
    private int _homeKeySharps;
    private int _keySharps;

    /// <summary>Initializes a new <see cref="MidiExporter"/> with the given timing resolution.</summary>
    public MidiExporter(int ticksPerQuarter = MidiFile.DefaultTicksPerQuarter)
    {
        _ticksPerQuarter = ticksPerQuarter;
    }

    private Dictionary<string, DrumInfo>? _drumOverrides; // drummap { } per-score

    /// <summary>Exports the given syntax tree to a <see cref="MidiFile"/>.</summary>
    public MidiFile Export(SyntaxTree tree)
    {
        _drumOverrides = DrumOverrides.Build(tree.GetRoot());
        var midi = new MidiFile { TicksPerQuarterNote = _ticksPerQuarter };

        var conductorTrack = new MidiTrack { Name = "Tempo", Channel = 0 };
        conductorTrack.TempoChanges.Add(new TempoChange(0, BpmToMicroseconds(_tempo)));
        midi.Tracks.Add(conductorTrack);

        var mainTrack = new MidiTrack { Name = "Track 1", Channel = 0 };
        _root = tree.GetRoot();
        _phraseBodies = new Dictionary<string, SyntaxNode>();
        _sections = new Dictionary<string, List<SectionDeclarationSyntax>>();
        _partDecls = new Dictionary<string, PartDeclarationSyntax>();
        foreach (var n in _root.DescendantNodes())
        {
            if (n is PhraseDeclarationSyntax ph)
                _phraseBodies[ph.Name.Text] = ph.Body;
            else if (n is VariableDeclarationSyntax vd)
                _phraseBodies[vd.Name.Text] = vd.Expression;
            else if (n is SectionDeclarationSyntax sd)
            {
                if (!_sections.TryGetValue(sd.Name.Text, out var sameName))
                    _sections[sd.Name.Text] = sameName = new List<SectionDeclarationSyntax>();
                sameName.Add(sd);
                if (!SectionHasInlineMusic(sd) && FirstDirectKey(sd) is { } hk)
                    _sectionHeaderKeys.TryAdd(sd.Name.Text, hk);
            }
            else if (n is PartDeclarationSyntax pd)
                _partDecls.TryAdd(pd.Name.Text, pd); // first-wins, matching the old first-match scans
        }
        _scoreTransposeDefault = PartTranspose.ReadScoreDefault(_root);
        _homeTonic = ScoreHomeKey.Read(_root);
        _ambientTonic = _homeTonic;
        _homeKeySharps = ScoreHomeKey.Sharps(_root);
        _keySharps = _homeKeySharps;
        _formDriven = _root.DescendantNodes().OfType<FormDeclarationSyntax>().Any();
        _formPlayed = false;
        _partPitchLanes.Clear();
        _sourceOrdinals = new Dictionary<int, int>();
        ProcessNode(_root, mainTrack, conductorTrack);

        // Ensure there is an initial time signature at tick 0. If the score already
        // declared one at the downbeat (ProcessTimeSignature added it), keep that and
        // do NOT insert another: seeding the running (post-processing, i.e. final)
        // value here put a spurious downbeat event on any score whose time signature
        // changes later. Only seed the default when no tick-0 signature exists.
        if (!conductorTrack.TimeSignatures.Any(ts => ts.Tick == 0))
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
                (_partOctaveAnchor, _partAbsoluteBase) = PartOctaveAnchors(part.Name.Text);
                _currentTimbre = PartTimbre(part.Name.Text);
                ProcessChildren(part, track, conductorTrack);
                (_partOctaveAnchor, _partAbsoluteBase) = (4, 4);
                _currentTimbre = 0;
                break;

            case SectionDeclarationSyntax sectionDecl:
                // With a structure the play order is ITS job; declarations
                // are silent (they used to play in file order regardless).
                if (!_formDriven)
                    PlaySection(sectionDecl, track, conductorTrack);
                break;

            case FormDeclarationSyntax formDecl:
                // A file may declare several named forms; MIDI plays the PRIMARY
                // one (`main`, else the first declared) so the .mid matches the
                // canonical arrangement.
                if (!_formPlayed && IsPrimaryForm(formDecl))
                {
                    _formPlayed = true;
                    PlayForm(formDecl, track, conductorTrack);
                }
                break;

            case PartBlockSyntax partBlock:
                // A section's `partName { ... }` block: arm the part's transpose
                // (sounding-pitch shift) for the notes inside, then disarm it.
                // Part's own transpose (from the cached declaration) overrides the
                // score-wide default — same result as PartTranspose.Read(root, name),
                // without the per-call tree scan.
                var transpose = (_partDecls.TryGetValue(partBlock.Name, out var tpd)
                    ? PartTranspose.Read(tpd) : null) ?? _scoreTransposeDefault;
                // The instrument's SOUNDING shift (bass 8vb, guitar treble_8, piccolo
                // 8va) composes on top of any `transpose` option: both move the played
                // pitch, so the .mid sounds what the instrument really produces —
                // matching the tab. The clef octave + `transposition` property.
                _currentTransposeSemitones = (transpose is { } t
                    ? PitchTransposer.IntervalSemitones(t.step, t.alt, t.oct)
                    : 0) + PartSoundingShift(partBlock.Name);
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

            case DrumNoteSyntax drumNote:
                ProcessDrumNote(drumNote, track);
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

            case ChordRepetitionSyntax rep:
                ProcessChordRepetition(rep, track);
                break;

            case ArpeggioSyntax arpeggio:
                ProcessArpeggio(arpeggio, track, conductorTrack);
                break;

            case TimeSignatureSyntax timeSig:
                ProcessTimeSignature(timeSig, conductorTrack);
                break;

            case KeySignatureSyntax keySig:
                // MIDI pitches are absolute, so a key change emits nothing — but it
                // advances the phrase auto-transpose baseline (where a later phrase
                // reference lands) and the scale-degree key that <d 3 5> stacks on.
                _ambientTonic = KeyTonic.Of(keySig);
                _keySharps = keySig.IsCustom ? 0 : KeySpelling.SharpsFor(
                    keySig.Pitch.ToFullString().Trim().ToLowerInvariant(),
                    keySig.Mode.Text.ToLowerInvariant()) ?? 0;
                break;

            case TempoDeclarationSyntax tempo:
                ProcessTempo(tempo, conductorTrack);
                break;

            case MetadataDeclarationSyntax:
                // title/composer only; nothing that affects MIDI
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
                // Simultaneous voices (<< v1 \\ v2 >>): every voice sounds, each
                // starting at the block's tick — not just voices[0]. Notes carry
                // absolute start ticks, so we rewind to the block start before each
                // voice, then advance past the LONGEST one. Each voice also restarts
                // the relative-octave / default-duration state from the pre-block
                // value, so voice 2 is not skewed by voice 1's ending pitch.
                var voices = parallel.Voices;
                int blockStartTick = _currentTick;
                int startNoteName = _currentNoteName;
                int startOctave = _currentOctave;
                Fraction startDuration = _defaultDuration;
                int voicesEndTick = blockStartTick;
                foreach (var voice in voices)
                {
                    _currentTick = blockStartTick;
                    _currentNoteName = startNoteName;
                    _currentOctave = startOctave;
                    _defaultDuration = startDuration;
                    ProcessNode(voice, track, conductorTrack);
                    voicesEndTick = Math.Max(voicesEndTick, _currentTick);
                }
                _currentTick = voicesEndTick;
                // …and the music AFTER the span reads from the frame the span opened in
                // too: no branch moves it, so which branch was written last cannot matter
                // (MeasureCollector's _parallelSpans carries the same rule for the page).
                _currentNoteName = startNoteName;
                _currentOctave = startOctave;
                break;

            case PhraseDeclarationSyntax:
            case VariableDeclarationSyntax:
                // Declarations are SILENT — bodies play where referenced.
                // Playing them here put every phrase once at tick 0 in a C4
                // frame, and $references then played nothing.
                break;

            case VariableReferenceSyntax varRef:
            {
                // $name expands in place, in a fresh default frame anchored at
                // the part's octave (the collector's RelativeResetMarker).
                string phName = varRef.Name.Text;
                if (_phraseBodies != null
                    && _phraseBodies.TryGetValue(phName, out var phraseBody)
                    && _activePhrases.Add(phName))
                {
                    _currentNoteName = 0;
                    // Trailing marks on the reference (Chorus' / Chorus,) raise or
                    // lower the movable phrase, matching the SVG collector's frame shift.
                    _currentOctave = _partOctaveAnchor + varRef.OctaveOffset;
                    _defaultDuration = Fraction.Quarter;
                    // Auto-transpose the movable phrase from the home key to the
                    // ambient key here (sounds an octave/interval up or down), on
                    // top of any part transpose; restored after the body. The
                    // reference's interval argument (Melody'(3)) adds a diatonic
                    // scale-step shift on top (nested references compose).
                    int savedTranspose = _currentTransposeSemitones;
                    int savedDiatonic = _diatonicShiftSteps;
                    _currentTransposeSemitones += PhraseTransposeSemitones();
                    _diatonicShiftSteps += varRef.DiatonicShiftSteps;
                    // The phrase's outgoing ANCHOR — its first note's bare
                    // letter resolved in the fresh frame above, the ambient
                    // tonic for a degree-opened body — captured before the
                    // body runs (a mid-body key change must not move it).
                    int? anchorStep = LilySharp.Core.Music.PhraseAnchor.AnchorStep(phraseBody,
                        n => _phraseBodies!.TryGetValue(n, out var b) ? b : null);
                    if (anchorStep == LilySharp.Core.Music.PhraseAnchor.Tonic)
                        anchorStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
                    ProcessNode(phraseBody, track, conductorTrack);
                    _currentTransposeSemitones = savedTranspose;
                    _diatonicShiftSteps = savedDiatonic;
                    // Frame hand-off at the phrase's ANCHOR (matches the
                    // collector's ExitPhraseTranspose): the reference is ONE
                    // item, the chord rule — its interior never leaks, and its
                    // own marks ('(N) included) shift what propagates, so a
                    // note after Melody'(3) is relative to the shifted anchor
                    // and '(8) == '. A pitchless body hands nothing off.
                    if (anchorStep is { } astep)
                    {
                        int oct = RelativeOctave.Resolve(
                            0, _partOctaveAnchor + varRef.OctaveOffset, astep, 0);
                        if (varRef.DiatonicShiftSteps != 0)
                            (astep, _, oct) = LilySharp.Core.Music.DiatonicShift.Apply(
                                astep, 0, oct, varRef.DiatonicShiftSteps, _keySharps);
                        _currentNoteName = astep;
                        _currentOctave = oct;
                    }
                    _activePhrases.Remove(phName);
                }
                break;
            }

            default:
                // Process any other nodes by visiting their children
                ProcessChildren(node, track, conductorTrack);
                break;
        }
    }

    /// <summary>True when <paramref name="form"/> is the PRIMARY form to play: the
    /// one named <c>main</c> (case-sensitive), or the first declared if none is.</summary>
    private bool IsPrimaryForm(FormDeclarationSyntax form)
    {
        var forms = _root!.DescendantNodes().OfType<FormDeclarationSyntax>().ToList();
        var primary = forms.FirstOrDefault(f => f.NameText == "main") ?? forms.FirstOrDefault();
        return ReferenceEquals(form, primary);
    }

    /// <summary>The first <c>key</c> that is a DIRECT child of the section, or null.</summary>
    private static KeySignatureSyntax? FirstDirectKey(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
            if (section.GetChild(i) is KeySignatureSyntax k)
                return k;
        return null;
    }

    /// <summary>True when the section has a direct-child MUSIC node (note / phrase / …),
    /// as opposed to only directives (<c>key</c> / <c>time</c> / …) and part / chord /
    /// lyric blocks — i.e. its own <c>key</c> is walked as music, not a header.</summary>
    private static bool SectionHasInlineMusic(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child is null or SyntaxTokenNode)
                continue;
            if (child is PartBlockSyntax or ChordPartBlockSyntax or LyricsBlockSyntax)
                continue;
            if (child is KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
                or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax)
                continue;
            return true; // a music node
        }
        return false;
    }

    /// <summary>
    /// Plays one section: its part blocks run SIMULTANEOUSLY (each from the
    /// section's start tick; the section ends with the longest part), and each
    /// part's relative-pitch state persists across sections/repeat passes via
    /// _partPitchLanes — the collector streams a part the same way.
    /// </summary>
    private void PlaySection(SectionDeclarationSyntax section, MidiTrack track, MidiTrack conductorTrack)
    {
        // A section is self-contained: its phrase auto-transpose baseline and the
        // scale-degree key both revert to the score's home key (a mid-section
        // modulation cannot leak out).
        _ambientTonic = _homeTonic;
        _keySharps = _homeKeySharps;

        // A section's own header key — stated beside the part blocks (section-major) or
        // in a standalone part-major header — is not walked with the part cell's music,
        // so apply it up front (overriding the home reset) for every part of the section.
        if (_sectionHeaderKeys.TryGetValue(section.SectionName, out var headerKey))
        {
            _ambientTonic = KeyTonic.Of(headerKey);
            _keySharps = headerKey.IsCustom ? 0 : KeySpelling.SharpsFor(
                headerKey.Pitch.ToFullString().Trim().ToLowerInvariant(),
                headerKey.Mode.Text.ToLowerInvariant()) ?? 0;
        }

        // Part-major layout: the section lives INSIDE its part — arm that
        // part's anchor and play the children sequentially.
        for (var p = section.Parent; p != null; p = p.Parent)
        {
            if (p is PartDeclarationSyntax owner)
            {
                // Restore this part's own pitch/duration lane so concurrently
                // played parts (one PlaySection call each, same structure
                // reference) keep independent relative-octave chains across their
                // sections instead of inheriting the previous part's last note.
                string pname = owner.Name.Text;
                var (anchor, absBase) = PartOctaveAnchors(pname);
                var pitch = _partPitchLanes.TryGetValue(pname, out var saved)
                    ? saved
                    : (NoteName: 0, Octave: anchor, Dur: Fraction.Quarter);
                _currentNoteName = pitch.NoteName;
                _currentOctave = pitch.Octave;
                _defaultDuration = pitch.Dur;
                _partOctaveAnchor = anchor;
                _partAbsoluteBase = absBase;
                _currentTimbre = PartTimbre(pname);
                // Part-major music plays inside its own part declaration (no
                // PartBlockSyntax), so arm the instrument's sounding shift here too —
                // otherwise a bass/guitar section-in-part sounded at written pitch.
                _currentTransposeSemitones = PartSoundingShift(pname);
                ProcessChildren(section, track, conductorTrack);
                _partPitchLanes[pname] = (_currentNoteName, _currentOctave, _defaultDuration);
                (_partOctaveAnchor, _partAbsoluteBase) = (4, 4);
                _currentTimbre = 0;
                _currentTransposeSemitones = 0;
                _diatonicShiftSteps = 0;
                return;
            }
        }

        int sectionStart = _currentTick;
        int sectionEnd = _currentTick;
        var tickLanes = new Dictionary<string, int>();
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child == null || child is SyntaxTokenNode)
                continue;
            if (child is PartBlockSyntax sectionPart)
            {
                string pname = sectionPart.Name;
                var (anchor, absBase) = PartOctaveAnchors(pname);
                var pitch = _partPitchLanes.TryGetValue(pname, out var saved)
                    ? saved
                    : (NoteName: 0, Octave: anchor, Dur: Fraction.Quarter);
                _currentTick = tickLanes.TryGetValue(pname, out int lt) ? lt : sectionStart;
                _currentNoteName = pitch.NoteName;
                _currentOctave = pitch.Octave;
                _defaultDuration = pitch.Dur;
                _partOctaveAnchor = anchor;
                _partAbsoluteBase = absBase;
                _currentTimbre = PartTimbre(pname);
                ProcessNode(sectionPart, track, conductorTrack);
                _partPitchLanes[pname] = (_currentNoteName, _currentOctave, _defaultDuration);
                tickLanes[pname] = _currentTick;
                sectionEnd = Math.Max(sectionEnd, _currentTick);
                (_partOctaveAnchor, _partAbsoluteBase) = (4, 4);
            }
            else
            {
                ProcessNode(child, track, conductorTrack);
                sectionEnd = Math.Max(sectionEnd, _currentTick);
            }
        }
        _currentTick = sectionEnd;
    }

    private void PlaySectionByName(string name, MidiTrack track, MidiTrack conductorTrack)
    {
        // A structure reference to a part-major section name plays EVERY part's
        // copy of it concurrently — each from the shared start tick on its own
        // lane — not just the last-declared one (which silently dropped every
        // earlier part, and yielded no notes at all when a chords part was
        // declared last). Section-major names map to a single-element list.
        if (_sections == null || !_sections.TryGetValue(name, out var sections))
            return;
        int start = _currentTick;
        int end = start;
        foreach (var section in sections)
        {
            _currentTick = start;
            PlaySection(section, track, conductorTrack);
            end = Math.Max(end, _currentTick);
        }
        _currentTick = end;
    }

    /// <summary>
    /// Plays sections in structure order. `|: … :|` bodies play twice (or
    /// once per volta alternative); display relabels ("A2") and navigation
    /// text are visual and skipped. D.C./D.S. jump SEMANTICS are not yet
    /// honored (they would need segno/fine targets in time).
    /// </summary>
    /// <remarks>
    /// ⚠️ A '~' REFERENCE HIDES A LABEL, NOT THE MUSIC. <c>~Name</c> is the same section
    /// reference with its rehearsal label suppressed (Parser.Form.cs ParseSilentSectionReference),
    /// so it has to play. Matching only <see cref="SectionReferenceSyntax"/> here silenced
    /// the whole section: <c>form main { ~Main }</c> engraved correctly and exported ZERO
    /// notes, while the same book with the '~' dropped exported eight.
    /// The engraver has already been bitten by this once, in its repeat-block walk
    /// (MeasureCollector.Form.cs, "without this the section's measures were dropped entirely,
    /// not just its label") — a silent reference must be answered EVERYWHERE a plain one is.
    /// </remarks>
    private void PlayForm(FormDeclarationSyntax structure, MidiTrack track, MidiTrack conductorTrack)
    {
        for (int i = 0; i < structure.SlotCount; i++)
        {
            var child = structure.GetChild(i);
            switch (child)
            {
                case SectionReferenceSyntax reference:
                    PlaySectionByName(reference.SectionName, track, conductorTrack);
                    break;
                case { Kind: SyntaxKind.SilentSectionReference }
                    when SilentSectionName(child) is { } silentName:
                    PlaySectionByName(silentName, track, conductorTrack);
                    break;
                case FormRepeatBlockSyntax repeatBlock:
                    PlayRepeatBlock(repeatBlock, track, conductorTrack);
                    break;
            }
        }
    }

    /// <summary>
    /// The section name inside a <c>~Name</c> reference. It has no red-node class of its own,
    /// so the name is read off slot 1 — the same way every other consumer reads it
    /// (MeasureCollector.Form.cs, MusicXmlExporter, SectionReferenceFinder).
    /// </summary>
    private static string? SilentSectionName(SyntaxNode? node)
        => node?.GetChild(1) is SyntaxTokenNode name ? name.Text : null;

    private void PlayRepeatBlock(FormRepeatBlockSyntax repeatBlock, MidiTrack track, MidiTrack conductorTrack)
    {
        var body = new List<string>();
        var alternatives = new List<string>();
        for (int i = 0; i < repeatBlock.SlotCount; i++)
        {
            switch (repeatBlock.GetChild(i))
            {
                case SectionReferenceSyntax reference:
                    body.Add(reference.SectionName);
                    break;
                // …and inside a repeat body too. See the remark on PlayForm.
                case { Kind: SyntaxKind.SilentSectionReference } silent
                    when SilentSectionName(silent) is { } silentName:
                    body.Add(silentName);
                    break;
                case FormAlternativeSyntax alt:
                    alternatives.Add(alt.SectionName.Text);
                    break;
            }
        }
        int passes = Math.Max(2, alternatives.Count);
        // A structure repeat is engraved once (repeat barlines): later passes
        // revisit the same printed BODY copy, so the body's ordinals restart from
        // this snapshot each pass (the highlight re-lights the same printed body).
        var structOrdSnapshot = new Dictionary<int, int>(_sourceOrdinals);
        for (int pass = 0; pass < passes; pass++)
        {
            if (pass > 0)
                _sourceOrdinals = new Dictionary<int, int>(structOrdSnapshot);
            foreach (var name in body)
                PlaySectionByName(name, track, conductorTrack);
            if (pass < alternatives.Count)
            {
                // Each ENDING, unlike the body, is a distinct printed copy laid out
                // in pass order after the body. When the endings reuse the body's
                // section they share its source positions, so the body's per-pass
                // ordinal restart would otherwise map every ending onto the FIRST
                // ending's printed copy (highlighting ending 1 each pass, never
                // ending 2). Advance the body's positions by `pass` so this pass's
                // ending resolves to its OWN printed copy. Only positions the body
                // just bumped are advanced, leaving intro/outro sections intact.
                if (pass > 0)
                {
                    foreach (var key in new List<int>(_sourceOrdinals.Keys))
                    {
                        structOrdSnapshot.TryGetValue(key, out int before);
                        if (_sourceOrdinals[key] > before)
                            _sourceOrdinals[key] += pass;
                    }
                }
                PlaySectionByName(alternatives[pass], track, conductorTrack);
            }
        }
    }

    /// <summary>
    /// Timbre family for the preview synth: the part's `instrument` property
    /// wins, else the part NAME itself is matched (a part called "flute"
    /// sounds flute-ish without any property).
    /// </summary>
    private int PartTimbre(string partName)
    {
        string? source = null;
        if (_partDecls.TryGetValue(partName, out var partDecl))
        {
            foreach (var prop in partDecl.Properties)
            {
                if (prop.NameToken.Text.ToLowerInvariant() == "instrument")
                {
                    // MIDI timbre follows the preset (the bare word), not a
                    // trailing "…" display label: instrument violin "1st Violin".
                    var texts = new System.Collections.Generic.List<string>();
                    for (int vi = 2; vi < prop.SlotCount; vi++)
                        if (prop.GetChild(vi) is SyntaxTokenNode vt)
                            texts.Add(vt.Text);
                    source = LilySharp.Core.Svg.Model.InstrumentDefaults.SplitInstrument(texts).Preset;
                    break;
                }
            }
        }
        return TimbreFamily(source ?? partName);
    }

    private static int TimbreFamily(string name)
    {
        string s = name.ToLowerInvariant();
        bool Has(params string[] keys) => keys.Any(s.Contains);
        if (Has("flute", "piccolo", "recorder", "fife")) return 1;
        if (Has("clarinet")) return 2;
        if (Has("violin", "viola", "cello", "string", "contrabass", "fiddle")) return 3;
        if (Has("guitar", "banjo", "mandolin", "ukulele", "lute")) return Has("bass") ? 5 : 4;
        if (Has("bass")) return 5;
        if (Has("trumpet", "horn", "trombone", "tuba", "brass", "cornet")) return 6;
        if (Has("organ", "harmonium", "accordion")) return 7;
        if (Has("voice", "soprano", "alto", "tenor", "bariton", "choir", "vocal", "upper", "lower")) return 8;
        if (Has("oboe", "bassoon", "sax")) return 2;
        return 0; // piano-ish default
    }

    /// <summary>
    /// The part's SOUNDING shift (semitones) for playback: the octave the clef
    /// carries (treble_8 → −12) plus the resolved <c>transposition</c> (explicit
    /// property &gt; instrument preset &gt; tuning default). A bass sounds an octave
    /// below its bass-clef notation, a guitar an octave below its treble_8, a piccolo
    /// an octave above — so the .mid plays what the instrument really sounds, matching
    /// the tab. Shares the same resolution as the tab's fret shift.
    /// </summary>
    private int PartSoundingShift(string partName) => Header(partName).SoundingShiftSemitones;

    /// <summary>The part's octave anchor, resolved the way the page resolves it, so a bare
    /// <c>c</c> sounds at the octave it prints.</summary>
    /// <remarks>
    /// ⚠️ The CLEF step is not optional and used to be missing here: this read
    /// <c>octave N</c> &gt; preset &gt; 4 and stopped, so <c>part m { clef bass }</c> printed
    /// C3 and played C4 while <c>instrument bass</c> — whose preset fills the octave — was
    /// right. MEASURED across six part headers before and after; see HANDOFF §1 ⑤.
    /// The chain itself lives in <see cref="LilySharp.Core.Svg.Model.InstrumentDefaults.AnchorOctave"/>.
    /// </remarks>
    private (int Relative, int Absolute) PartOctaveAnchors(string partName)
    {
        var header = Header(partName);
        return (header.AnchorOctave, header.AbsoluteBaseOctave);
    }

    /// <summary>What this part's header says about pitch, read once per lookup.</summary>
    private Semantics.PartHeaderDefaults Header(string partName)
        => _partDecls.TryGetValue(partName, out var pd)
            ? Semantics.PartHeaderDefaults.Read(pd)
            : Semantics.PartHeaderDefaults.Empty;

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
    /// <remarks>LILYPOND-REF: scm/music-functions.scm (unfold-repeats); lily/volta-repeat-iterator.cc — repeated music is performed N times.</remarks>
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
    /// <remarks>LILYPOND-REF: scm/music-functions.scm (unfold-repeats); lily/volta-repeat-iterator.cc — body performed N times, i-th ending per pass.</remarks>
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
        // The repeat span is ENGRAVED once: later passes replay the same
        // printed copies, so their ordinals restart from this snapshot.
        var ordinalSnapshot = new Dictionary<int, int>(_sourceOrdinals);
        for (int pass = 1; pass <= count; pass++)
        {
            _currentNoteName = savedName;
            _currentOctave = savedOctave;
            _velocity = savedVelocity;
            _defaultDuration = savedDuration;
            if (pass > 1)
                _sourceOrdinals = new Dictionary<int, int>(ordinalSnapshot);

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
            ? _partAbsoluteBase + pitch.OctaveOffset
            : RelativeOctave.Resolve(
                _currentNoteName, _currentOctave, noteName, pitch.OctaveOffset);

        // Update current state for next note
        _currentNoteName = noteName;
        _currentOctave = targetOctave;

        // Calculate MIDI pitch (shared step→MIDI formula), then diatonic shift +
        // transpose + clamp (WrittenToMidi).
        return WrittenToMidi(
            RelativeOctave.StepIndex(pitch.BaseName), pitch.AccidentalOffset, targetOctave);
    }

    /// <summary>
    /// The sounding shift, in semitones, that moves a movable phrase from the
    /// score's home key to the current ambient key (nearest octave). 0 when there
    /// is nothing to do — ambient equals home, or either key is custom/atonal.
    /// </summary>
    private int PhraseTransposeSemitones()
    {
        if (!_homeTonic.Valid || !_ambientTonic.Valid)
            return 0;
        var target = PitchTransposer.MovableInterval(
            _homeTonic.Step, _homeTonic.Alter, _ambientTonic.Step, _ambientTonic.Alter);
        return target is { } t
            ? PitchTransposer.IntervalSemitones(t.step, t.alt, t.oct)
            : 0;
    }

    /// <summary>
    /// Gets the note name index (c=0, d=1, e=2, f=3, g=4, a=5, b=6).
    /// </summary>
    private static int GetNoteName(char baseName) => RelativeOctave.StepIndex(baseName);

    /// <summary>
    /// Plays an arpeggio (<c>&lt;&lt; c e g &gt;&gt;</c>) — a written-out broken chord — as
    /// SEQUENTIAL notes that EQUALLY SUBDIVIDE the group's total (an auto-tuplet when the
    /// share is not a plain note value: 3 in a beat = a triplet, 5 = a quintuplet). The
    /// octaves anchor to the first pitched member (the chord rule) and scale degrees
    /// (<c>&lt;&lt; c 3 5 &gt;&gt;</c>) resolve against the root and the key.
    /// </summary>
    private void ProcessArpeggio(ArpeggioSyntax arpeggio, MidiTrack track, MidiTrack conductorTrack)
    {
        var members = arpeggio.Members.ToList(); // bare pitches, degrees, chords and/or rests
        if (members.Count == 0)
            return;

        // The group occupies its total (trailing `>>N`, or the inherited running duration);
        // its members split that equally. Push the auto-tuplet so every played duration
        // scales like `tuplet num/base { … }`, and force the member value via _defaultDuration.
        Fraction total = arpeggio.TotalDuration?.ToFraction() ?? _defaultDuration;
        var sub = ArpeggioSubdivision.Compute(members.Count, total);
        if (sub.HasTuplet)
            _tupletStack.Push((sub.TupletNum, sub.TupletBase));
        var savedDefault = _defaultDuration;
        _defaultDuration = sub.MemberDisplay;
        // Octave marks after '>>' shift the whole group (like a chord's '<c e g>,'): applied
        // to the ROOT, inherited by the stacked members / degrees via the anchor octave.
        int groupOctave = arpeggio.OctaveOffset;

        // A dynamic on the group (`<< c e g >>@f`) takes effect at its start,
        // exactly as if written on the first member (running state thereafter).
        foreach (var a in arpeggio.Articulations)
            if (a is DynamicSyntax { Level: not DynamicLevel.None } dyn)
                _velocity = dyn.Velocity;

        // The ROOT is the first PITCHED member (leading rests just advance time); it
        // resolves relatively and anchors the group. Every later PITCHED member STACKS
        // above it — the same octave placement as a `<c e g>` chord member, so the pitches
        // are order-independent — while rests keep the normal frame. Absolute mode makes
        // each stacked member's octave = anchor + (step >= root ? 0 : 1) + its own '/, marks.
        bool savedAbsolute = _octaveAbsolute;
        // ⚠️ The ABSOLUTE base, because the stacking below switches absolute mode ON and
        // spells each member's octave itself. The relative seed is untouched.
        int savedAnchor = _partAbsoluteBase;
        bool rootSet = false;
        int anchorOctave = 0;
        int rootStep = 0;
        foreach (var member in members)
        {
            if (member is ScaleDegreeSyntax degree)
            {
                // Degrees anchor on the root — or, before any pitched member, on the
                // KEY TONIC (like an omitted-root degree chord), which then becomes
                // the group's anchor and outgoing reference. A custom/atonal key has
                // no tonic, so fall back to C.
                if (!rootSet)
                {
                    rootSet = true;
                    rootStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
                    anchorOctave = RelativeOctave.Resolve(_currentNoteName, _currentOctave, rootStep, 0) + groupOctave;
                }
                EmitArpeggioMidiDegree(degree, track, rootStep, anchorOctave);
                continue;
            }

            char? letter = FirstPitchLetter(member);
            // The group octave shift applies to the ROOT member only; the stacked members
            // inherit it via the anchor octave the shifted root sets.
            bool isRoot = !rootSet && letter is not null;
            if (rootSet && letter is { } l)
            {
                _octaveAbsolute = true;
                _partAbsoluteBase = anchorOctave + (RelativeOctave.StepIndex(l) >= rootStep ? 0 : 1);
            }
            else
            {
                _octaveAbsolute = savedAbsolute; // the root, and any rest
            }
            if (member is PitchSyntax pitch)
                EmitArpeggioMidiPitch(pitch, track, isRoot ? groupOctave : 0);
            else if (member is ChordSyntax chord)
                ProcessChord(chord, track, isRoot ? groupOctave : 0);
            else
                ProcessNode(member, track, conductorTrack); // rest
            if (!rootSet && letter is { } rl)
            {
                rootSet = true;
                anchorOctave = _currentOctave;
                rootStep = RelativeOctave.StepIndex(rl);
            }
        }
        _octaveAbsolute = savedAbsolute;
        _partAbsoluteBase = savedAnchor;
        if (sub.HasTuplet)
            _tupletStack.Pop();
        // Acts like one note: a trailing `>>N` carries N as the running duration; an inherited
        // total leaves it unchanged.
        _defaultDuration = arpeggio.TotalDuration?.ToFraction() ?? savedDefault;
        // After the group the running reference is the root (chord-after behavior).
        if (rootSet)
        {
            _currentOctave = anchorOctave;
            _currentNoteName = rootStep;
        }
    }

    /// <summary>Play one bare arpeggio pitch at the forced member duration, resolved through
    /// the octave frame the caller set up (root relative, later members stacked absolute).
    /// <paramref name="octaveShift"/> is the group-level octave mark, applied to the root (0
    /// for stacked members, which inherit it via the anchor).</summary>
    private void EmitArpeggioMidiPitch(PitchSyntax pitch, MidiTrack track, int octaveShift)
    {
        // Stacked members arrive in forced-absolute mode (plain path). The ROOT, in
        // relative mode, anchors on its bare LETTER: its own '/, marks are LOCAL to
        // its sounding pitch and do not move the anchor the group propagates.
        int midiPitch;
        if (_octaveAbsolute)
        {
            midiPitch = System.Math.Clamp(CalculateRelativeMidiPitch(pitch) + octaveShift * 12, 0, 127);
            _currentOctave += octaveShift; // so the anchor octave carries the group shift
        }
        else
        {
            int step = GetNoteName(pitch.BaseName);
            int anchor = RelativeOctave.Resolve(_currentNoteName, _currentOctave, step, 0) + octaveShift;
            midiPitch = WrittenToMidi(step, pitch.AccidentalOffset, anchor + pitch.OctaveOffset);
            _currentNoteName = step;
            _currentOctave = anchor;
        }
        int ticks = FractionToTicks(_defaultDuration);
        track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, _currentTick, ticks,
            pitch.Position, QuarterBend: pitch.QuarterOffset,
            SourceOrdinal: NextOrdinal(pitch.Position), Timbre: _currentTimbre));
        _currentTick += ticks;
    }

    /// <summary>Play one scale-degree arpeggio member, stacked on the group's anchor (the
    /// root, or the key tonic when no pitched member precedes — the caller resolves it) by
    /// diatonic steps in the key, then transposed like a pitch.</summary>
    private void EmitArpeggioMidiDegree(ScaleDegreeSyntax degree, MidiTrack track, int rootStep, int anchorOctave)
    {
        var (step, alter, octave) = ChordDegrees.Resolve(
            rootStep, anchorOctave, degree.Number, degree.Alteration, degree.OctaveOffset, _keySharps);
        int midiPitch = WrittenToMidi(step, alter, octave);
        int ticks = FractionToTicks(_defaultDuration);
        track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, _currentTick, ticks,
            degree.Position, SourceOrdinal: NextOrdinal(degree.Position), Timbre: _currentTimbre));
        _currentTick += ticks;
    }

    /// <summary>The letter of a member's root pitch — a bare pitch's letter, or a chord's
    /// root (first pitch) — used to stack the arpeggio's members above the first. Degrees
    /// and rests return null (they do not anchor the frame).</summary>
    private static char? FirstPitchLetter(SyntaxNode member) => member switch
    {
        PitchSyntax p => p.PitchName.ToLowerInvariant()[0],
        ChordSyntax c => c.Root?.PitchName.ToLowerInvariant()[0],
        _ => null,
    };

    private void ProcessNote(NoteSyntax note, MidiTrack track)
    {
        int midiPitch = CalculateRelativeMidiPitch(note.Pitch);

        var duration = GetDuration(note.Duration);
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks); // grace notes steal from this note

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

        // A sounding note keeps at least one tick: if a short value times a
        // duration-shortening articulation (e.g. staccato) rounds to 0, its NoteOff
        // would land on the same tick as its NoteOn and — because NoteOff sorts before
        // NoteOn — be emitted first, leaving a stuck note.
        int actualDuration = Math.Max(1, durationTicks * durationPercent / 100);

        bool startsTie = note.Articulations.OfType<TieSyntax>().Any();

        // If the previous note tied into this one (same pitch on the same track),
        // extend that note instead of emitting a new note-on/off pair.
        if (_tiePending && _lastNoteTrack == track
            && _lastNoteIndex >= 0 && _lastNoteIndex < track.Notes.Count
            && track.Notes[_lastNoteIndex].Pitch == midiPitch)
        {
            var prev = track.Notes[_lastNoteIndex];
            // The tie extends prev by the full written durationTicks, not the
            // articulation-shortened actualDuration — so a staccato note that is
            // tied-FROM gains full length on the merge. Rare (tie-over-staccato);
            // kept deliberately, matching a sustained tied note's intent.
            track.Notes[_lastNoteIndex] = prev with { DurationTicks = prev.DurationTicks + durationTicks };
            _currentTick += durationTicks;
            _tiePending = startsTie; // continue a tie chain (c~ c~ c)
            return;
        }

        track.Notes.Add(new MidiNote(track.Channel, midiPitch, velocity, _currentTick, actualDuration, note.Position,
            QuarterBend: note.Pitch.QuarterOffset,
            SourceOrdinal: NextOrdinal(note.Position), Timbre: _currentTimbre));
        _lastNoteIndex = track.Notes.Count - 1;
        _lastNoteTrack = track;
        _currentTick += durationTicks;
        _tiePending = startsTie;
    }

    /// <summary>Drum note → GM percussion: channel 10 (0-based 9), pitch =
    /// the drum's GM key, duration/velocity semantics as pitched notes.
    /// Timbre 9 selects the preview's noise-based drum patch.</summary>
    private void ProcessDrumNote(DrumNoteSyntax drum, MidiTrack track)
    {
        _tiePending = false; // drums do not tie
        var info = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
        var duration = GetDuration(drum.Duration);
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks);

        int velocity = _velocity;
        int durationPercent = 100;
        foreach (var child in drum.Articulations)
        {
            switch (child)
            {
                case DynamicSyntax dynamic:
                    velocity = dynamic.Velocity;
                    _velocity = velocity;
                    break;
                case ArticulationSyntax articulation:
                    (velocity, durationPercent) = ApplyArticulationType(articulation.Type, velocity, durationPercent);
                    break;
            }
        }

        int actualDuration = Math.Max(1, durationTicks * durationPercent / 100);
        track.Notes.Add(new MidiNote(9, info.GmKey, velocity, _currentTick, actualDuration,
            drum.Position, SourceOrdinal: NextOrdinal(drum.Position), Timbre: 9));
        _lastNoteIndex = track.Notes.Count - 1;
        _lastNoteTrack = track;
        _currentTick += durationTicks;
    }

    private void ProcessRest(RestSyntax rest)
    {
        // A rest breaks any pending tie (a tie cannot span a rest).
        _tiePending = false;
        var duration = GetDuration(rest.Duration);
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks); // grace notes steal from this rest
        _currentTick += durationTicks;
    }

    /// <summary>The sounding notes of every chord this walk has emitted, keyed by
    /// node — what a following <c>q</c> copies (resolved ABSOLUTE pitches; LP
    /// expands repetitions after \relative, so a q never re-reads the frame).</summary>
    private readonly Dictionary<ChordSyntax, List<(int MidiPitch, int QuarterBend, bool IsDrum)>> _resolvedChordNotes = new();

    private void ProcessChord(ChordSyntax chord, MidiTrack track, int extraOctave = 0)
    {
        // A chord breaks any pending tie chain. Chords are not currently tie
        // targets, and leaving stale _tiePending/_lastNoteIndex from the
        // previous note would let a later same-pitch note wrongly merge across
        // the chord.
        _tiePending = false;
        _lastNoteIndex = -1;
        var resolved = new List<(int MidiPitch, int QuarterBend, bool IsDrum)>();

        int startTick = _currentTick;
        var pitches = chord.Pitches.ToList();
        // One ordinal per chord ONSET — every head shares the chord's source
        // position and must map to the same printed copy.
        int chordOrdinal = NextOrdinal(chord.Position);

        // Use the chord's own typed duration, not a descendant scan (which could
        // pick up a duration on an inner pitch if the grammar ever allowed it).
        var duration = chord.Duration is { } cd ? GetDuration(cd) : _defaultDuration;
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks); // grace notes steal from this chord

        // The first member is the ROOT: its bare LETTER is the chord's ANCHOR; every
        // other member STACKS above the anchor — the same octave placement as a
        // scale degree, so a chord's pitches are independent of the order its notes
        // are written (<c e g> == <c 3 5> == <c g e>). Each member's own '/, marks
        // (the root's included) are LOCAL to that one note. The note AFTER the chord
        // is relative to the anchor. A deliberate Lily# divergence from LilyPond,
        // matching the collector and the MusicXML exporter.
        // Octave marks after the closing '>' (<1 3 5>' / <c e g>,,) shift the whole
        // chord; folding it into firstOctave flows through every stacked/degree member
        // and the following note, matching the collector and MusicXML exporter.
        // extraOctave is the enclosing arpeggio's group-level shift when this chord is the
        // arpeggio's root (`<< <c e> g >>,`); 0 otherwise.
        int chordOctave = chord.ChordOctaveOffset + extraOctave;
        int chordShift = chordOctave * 12;

        bool isFirst = true;
        int firstNoteName = _currentNoteName;
        int firstOctave = _currentOctave;

        foreach (var pitch in pitches)
        {
            int midiPitch;
            if (isFirst)
            {
                if (_octaveAbsolute)
                {
                    midiPitch = System.Math.Clamp(CalculateRelativeMidiPitch(pitch) + chordShift, 0, 127); // advances state
                    firstOctave = _currentOctave + chordOctave;
                }
                else
                {
                    // The root's LETTER resolved bare = the chord's ANCHOR; its own
                    // '/, marks are LOCAL to its sounding pitch (<c' e g> = C5 E4 G4,
                    // and the next note stays relative to C4).
                    int step = GetNoteName(pitch.BaseName);
                    int anchor = RelativeOctave.Resolve(_currentNoteName, _currentOctave, step, 0) + chordOctave;
                    midiPitch = WrittenToMidi(step, pitch.AccidentalOffset, anchor + pitch.OctaveOffset);
                    _currentNoteName = step;
                    _currentOctave = anchor;
                    firstOctave = anchor;
                }
                firstNoteName = _currentNoteName;
                isFirst = false;
            }
            else if (_octaveAbsolute)
            {
                // Absolute mode: each member is a fixed pitch, no stacking.
                midiPitch = System.Math.Clamp(CalculateRelativeMidiPitch(pitch) + chordShift, 0, 127);
            }
            else
            {
                int step = GetNoteName(pitch.BaseName);
                int octave = firstOctave + (step >= firstNoteName ? 0 : 1) + pitch.OctaveOffset;
                midiPitch = WrittenToMidi(step, pitch.AccidentalOffset, octave);
            }
            track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks, chord.Position,
                QuarterBend: pitch.QuarterOffset,
                SourceOrdinal: chordOrdinal, Timbre: _currentTimbre));
            resolved.Add((midiPitch, pitch.QuarterOffset, false));
        }

        // Omitted root (<1 3 5> / <3 5>): anchor the degrees on the key's tonic
        // (degree 1 = tonic), resolved relatively like a written root.
        if (pitches.Count == 0 && chord.Degrees.Any())
        {
            int tonicStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
            firstOctave = RelativeOctave.Resolve(_currentNoteName, _currentOctave, tonicStep, 0) + chordOctave;
            firstNoteName = tonicStep;
            _currentNoteName = tonicStep;
            _currentOctave = firstOctave;
        }

        // Scale-degree members (<d 3 5 7,>): stack on the root by diatonic steps in
        // the (written) key, then add the part transpose like any pitch.
        foreach (var degree in chord.Degrees)
        {
            var (step, alter, octave) = ChordDegrees.Resolve(
                firstNoteName, firstOctave, degree.Number, degree.Alteration,
                degree.OctaveOffset, _keySharps);
            int midiPitch = WrittenToMidi(step, alter, octave);
            track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks, chord.Position,
                SourceOrdinal: chordOrdinal, Timbre: _currentTimbre));
            resolved.Add((midiPitch, 0, false));
        }

        // Drum chord members: GM percussion alongside any pitched members.
        foreach (var drum in chord.DrumNames)
        {
            var dinfo = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
            track.Notes.Add(new MidiNote(9, dinfo.GmKey, _velocity, startTick, durationTicks, chord.Position,
                SourceOrdinal: chordOrdinal, Timbre: 9));
            resolved.Add((dinfo.GmKey, 0, true));
        }
        _resolvedChordNotes[chord] = resolved;

        // Next note is relative to the chord's first pitch.
        _currentNoteName = firstNoteName;
        _currentOctave = firstOctave;

        _currentTick = startTick + durationTicks;
    }

    /// <summary>A <c>q</c> chord repetition: the ORIGINAL chord's resolved notes
    /// at the repetition's own duration. The octave frame is NOT touched — LP
    /// expands q after \relative resolution, so a q is transparent to the frame.
    /// A bad repetition (no chord before it) still advances time silently; the
    /// validator reports it.</summary>
    /// <remarks>LILYPOND-REF: scm/music-functions.scm:854-946 copy-repeat-chord + expand-repeat-chords!</remarks>
    private void ProcessChordRepetition(ChordRepetitionSyntax rep, MidiTrack track)
    {
        // Same tie-chain break as a written chord.
        _tiePending = false;
        _lastNoteIndex = -1;

        int startTick = _currentTick;
        var duration = rep.Duration is { } rd ? GetDuration(rd) : _defaultDuration;
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks);

        if (ChordRepetitions.OriginalOf(rep) is { } original
            && _resolvedChordNotes.TryGetValue(original, out var notes))
        {
            int ordinal = NextOrdinal(rep.Position);
            foreach (var n in notes)
                track.Notes.Add(n.IsDrum
                    ? new MidiNote(9, n.MidiPitch, _velocity, startTick, durationTicks, rep.Position,
                        SourceOrdinal: ordinal, Timbre: 9)
                    : new MidiNote(track.Channel, n.MidiPitch, _velocity, startTick, durationTicks, rep.Position,
                        QuarterBend: n.QuarterBend, SourceOrdinal: ordinal, Timbre: _currentTimbre));
        }

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

    private void ProcessRepeat(RepeatExpressionSyntax repeat, MidiTrack track, MidiTrack conductorTrack)
    {
        int repeatCount = 2;
        if (int.TryParse(repeat.Count.Text, out int count))
            repeatCount = count;

        var alternatives = repeat.Alternative?.Alternatives.ToList();

        // percent (％ signs) and tremolo (one slashed note) are engraved
        // ONCE; unfold is printed in full, so its ordinals keep counting.
        string repType = repeat.RepeatType.Text;
        bool engravedOnce = repType is "percent" or "tremolo";
        var ordSnapshot = engravedOnce ? new Dictionary<int, int>(_sourceOrdinals) : null;

        for (int i = 0; i < repeatCount; i++)
        {
            if (i > 0 && ordSnapshot != null)
                _sourceOrdinals = new Dictionary<int, int>(ordSnapshot);
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
        // Round rather than truncate: independent flooring of each duration biases
        // nested tuplets progressively earlier. Rounding is identical for the common
        // power-of-two durations and only differs on awkward tuplet remainders.
        long baseTicks = RoundedDiv(duration.Numerator * 4 * _ticksPerQuarter, duration.Denominator);

        // Apply tuplet scaling: each note plays in (denominator/numerator) of normal time
        foreach (var (numerator, denominator) in _tupletStack)
        {
            baseTicks = RoundedDiv(baseTicks * denominator, numerator);
        }

        return (int)baseTicks;
    }

    /// <summary>Nearest-integer division for non-negative operands (a &gt;= 0, b &gt; 0).</summary>
    private static long RoundedDiv(long a, long b) => b <= 0 ? 0 : (a + b / 2) / b;

    private static int BpmToMicroseconds(int bpm) => 60_000_000 / Math.Max(1, bpm);

    private void ProcessGrace(GraceExpressionSyntax grace, MidiTrack track)
    {
        // Grace notes/chords steal time from the following note (see
        // _pendingGraceSteal), so the beat after the grace pair stays on the
        // metric grid. Duration threads WITHIN the grace group: a written value
        // (grace c16) is honored and carried to later unwritten items; an
        // unwritten leading grace falls back to an EIGHTH. The main stream's
        // _defaultDuration is untouched — the grace has its own local memory.
        //
        // ⚠️ That eighth is the LAYOUT's rule, and it has to be read from there:
        // MeasureCollector.CollectGraceNotes' graceDefaultDuration = Fraction.Eighth.
        // LilyPond has no grace-specific default at all (a bare note takes the
        // previous written duration), so this is LILYSHARP-OWN and Lily# used to
        // answer it in three places with three answers — 1/8 on the page, 1/32
        // here, and a quarter in the .ly twin. The page is the one that decides;
        // fixed 2026-08-01 (docs/HANDOFF.md §1).
        //
        // Sounding time is 9/40 of the grace's NOTATED duration, LilyPond's
        // built-in MIDI behavior. LILYPOND-REF: ly/articulate.ly
        // ac:defaultGraceFactor = 9/40 ("though the notation reference says 1/4").
        Fraction? written = null;
        int GraceTicks(Fraction? w)
        {
            long notatedTicks = FractionToTicks(w ?? Fraction.Eighth);
            return (int)RoundedDiv(notatedTicks * 9, 40);
        }

        foreach (var item in grace.Body.Items)
        {
            switch (item)
            {
                case NoteSyntax note:
                {
                    if (note.Duration != null) written = note.Duration.ToFraction();
                    int g = GraceTicks(written);
                    int midiPitch = CalculateRelativeMidiPitch(note.Pitch);
                    track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, _currentTick, g,
                        note.Position, QuarterBend: note.Pitch.QuarterOffset,
                        SourceOrdinal: NextOrdinal(note.Position), Timbre: _currentTimbre));
                    _currentTick += g;
                    _pendingGraceSteal += g;
                    break;
                }
                case ChordSyntax chord:
                {
                    if (chord.Duration != null) written = chord.Duration.ToFraction();
                    int g = GraceTicks(written);
                    int chordOrdinal = NextOrdinal(chord.Position);
                    // Within-chord relative octave: each pitch is relative to the
                    // previous; the item AFTER the chord is relative to the chord's
                    // FIRST pitch (matches ProcessChord / CreateChordItem).
                    bool isFirst = true;
                    int firstNoteName = _currentNoteName, firstOctave = _currentOctave;
                    foreach (var pitch in chord.Pitches)
                    {
                        int mp = CalculateRelativeMidiPitch(pitch);
                        if (isFirst) { firstNoteName = _currentNoteName; firstOctave = _currentOctave; isFirst = false; }
                        track.Notes.Add(new MidiNote(track.Channel, mp, _velocity, _currentTick, g,
                            chord.Position, QuarterBend: pitch.QuarterOffset,
                            SourceOrdinal: chordOrdinal, Timbre: _currentTimbre));
                    }
                    _currentNoteName = firstNoteName;
                    _currentOctave = firstOctave;
                    _currentTick += g;
                    _pendingGraceSteal += g;
                    break;
                }
                case RestSyntax rest:
                {
                    if (rest.Duration != null) written = rest.Duration.ToFraction();
                    int g = GraceTicks(written);
                    // A grace rest is a silent spacer: it consumes grace time (the
                    // following note steals it) but emits no note.
                    _currentTick += g;
                    _pendingGraceSteal += g;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The number of ticks the next timed event must give up to the grace notes
    /// that preceded it, clamped so the event keeps at least one tick. Resets the
    /// pending steal once consumed.
    /// </summary>
    private int ConsumeGraceSteal(int durationTicks)
    {
        if (_pendingGraceSteal <= 0)
            return 0;
        int steal = Math.Min(_pendingGraceSteal, Math.Max(0, durationTicks - 1));
        _pendingGraceSteal = 0;
        return steal;
    }

    // LILYPOND-REF: ly/articulate.ly — duration factors: ac:staccatoFactor (1 . 2) = 50%,
    // ac:portatoFactor (3 . 4) = 75%, ac:tenutoFactor (1 . 1) = 100%,
    // ac:staccatissimoFactor (1 . 4) = 25%.
    private (int velocity, int durationPercent) ApplyArticulationType(
        ArticulationType type, int velocity, int durationPercent)
    {
        return type switch
        {
            ArticulationType.Staccato => (velocity, 50),                      // Half duration
            ArticulationType.Staccatissimo => (velocity, 25),                 // Even shorter (LP 1/4)
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