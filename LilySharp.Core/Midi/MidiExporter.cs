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

    // Part header `octave N` anchor: the base octave for bare letters in
    // absolute mode and the seed of the relative frame (matches the
    // collector, where `part lh { octave 3 }` roots the left hand at C3).
    private int _partOctaveAnchor = 4;

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
                _partOctaveAnchor = PartOctaveAnchor(part.Name.Text);
                _currentTimbre = PartTimbre(part.Name.Text);
                ProcessChildren(part, track, conductorTrack);
                _partOctaveAnchor = 4;
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
                    // top of any part transpose; restored after the body.
                    int savedTranspose = _currentTransposeSemitones;
                    _currentTransposeSemitones += PhraseTransposeSemitones();
                    ProcessNode(phraseBody, track, conductorTrack);
                    _currentTransposeSemitones = savedTranspose;
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
                int anchor = PartOctaveAnchor(pname);
                var pitch = _partPitchLanes.TryGetValue(pname, out var saved)
                    ? saved
                    : (NoteName: 0, Octave: anchor, Dur: Fraction.Quarter);
                _currentNoteName = pitch.NoteName;
                _currentOctave = pitch.Octave;
                _defaultDuration = pitch.Dur;
                _partOctaveAnchor = anchor;
                _currentTimbre = PartTimbre(pname);
                // Part-major music plays inside its own part declaration (no
                // PartBlockSyntax), so arm the instrument's sounding shift here too —
                // otherwise a bass/guitar section-in-part sounded at written pitch.
                _currentTransposeSemitones = PartSoundingShift(pname);
                ProcessChildren(section, track, conductorTrack);
                _partPitchLanes[pname] = (_currentNoteName, _currentOctave, _defaultDuration);
                _partOctaveAnchor = 4;
                _currentTimbre = 0;
                _currentTransposeSemitones = 0;
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
                int anchor = PartOctaveAnchor(pname);
                var pitch = _partPitchLanes.TryGetValue(pname, out var saved)
                    ? saved
                    : (NoteName: 0, Octave: anchor, Dur: Fraction.Quarter);
                _currentTick = tickLanes.TryGetValue(pname, out int lt) ? lt : sectionStart;
                _currentNoteName = pitch.NoteName;
                _currentOctave = pitch.Octave;
                _defaultDuration = pitch.Dur;
                _partOctaveAnchor = anchor;
                _currentTimbre = PartTimbre(pname);
                ProcessNode(sectionPart, track, conductorTrack);
                _partPitchLanes[pname] = (_currentNoteName, _currentOctave, _defaultDuration);
                tickLanes[pname] = _currentTick;
                sectionEnd = Math.Max(sectionEnd, _currentTick);
                _partOctaveAnchor = 4;
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
                case FormRepeatBlockSyntax repeatBlock:
                    PlayRepeatBlock(repeatBlock, track, conductorTrack);
                    break;
            }
        }
    }

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
    private int PartSoundingShift(string partName)
    {
        if (!_partDecls.TryGetValue(partName, out var partDecl))
            return 0;

        string? clefText = null, transText = null, tuningText = null;
        string? instrument = null;
        foreach (var prop in partDecl.Properties)
        {
            var name = prop.NameToken.Text.ToLowerInvariant();
            string Joined()
            {
                var sb = new System.Text.StringBuilder();
                for (int vi = 2; vi < prop.SlotCount; vi++)
                    if (prop.GetChild(vi) is SyntaxTokenNode vt) sb.Append(vt.Text);
                return sb.ToString();
            }
            switch (name)
            {
                case "clef": clefText = (prop.GetChild(2) as SyntaxTokenNode)?.Text.ToLowerInvariant(); break;
                case "transposition": transText = (prop.GetChild(2) as SyntaxTokenNode)?.Text; break;
                case "tuning": tuningText = Joined().ToLowerInvariant(); break;
                case "instrument":
                    instrument = LilySharp.Core.Svg.Model.InstrumentDefaults
                        .SplitInstrument(new[] { Joined() }.Where(s => s.Length > 0)).Preset.ToLowerInvariant();
                    break;
            }
        }

        // Clef octave: explicit clef, else the instrument preset's default clef.
        var clef = clefText != null ? ClefFromText(clefText)
            : instrument != null
                ? LilySharp.Core.Svg.Model.InstrumentDefaults.GetDefaults(instrument).Clef
                : Svg.Model.ClefType.Treble;
        int clefShift = Tablature.Tunings.ClefOctaveShift(clef);

        // Transposition: explicit `transposition` > instrument preset > tuning default.
        int transposition;
        if (transText != null &&
            LilySharp.Core.Svg.Model.InstrumentDefaults.ParseTranspositionSemitones(transText) is int ex)
            transposition = ex;
        else if (instrument != null)
            transposition = LilySharp.Core.Svg.Model.InstrumentDefaults.GetTransposition(instrument);
        else if (tuningText != null)
            transposition = Tablature.Tunings.TuningTransposition(TuningFromText(tuningText));
        else
            transposition = 0;

        return clefShift + transposition;
    }

    private static Svg.Model.ClefType ClefFromText(string clef) => clef switch
    {
        "bass" => Svg.Model.ClefType.Bass,
        "alto" => Svg.Model.ClefType.Alto,
        "tenor" => Svg.Model.ClefType.Tenor,
        "treble_8" => Svg.Model.ClefType.Treble8Below,
        "treble^8" => Svg.Model.ClefType.Treble8Above,
        "bass_8" => Svg.Model.ClefType.Bass8Below,
        _ => Svg.Model.ClefType.Treble,
    };

    private static TuningType TuningFromText(string tuning) => tuning switch
    {
        "bass" => TuningType.Bass,
        "bass5" => TuningType.Bass5,
        "bass6" => TuningType.Bass6,
        "ukulele" or "uke" => TuningType.Ukulele,
        _ => TuningType.Guitar,
    };

    /// <summary>The part's octave anchor: an explicit <c>octave N</c> &gt; the
    /// instrument preset's default octave (bass = 3) &gt; 4. Mirrors the notation
    /// collector's priority so a bare <c>c</c> sounds at the same octave it prints.</summary>
    private int PartOctaveAnchor(string partName)
    {
        if (_partDecls.TryGetValue(partName, out var partDecl))
        {
            string? instrument = null;
            foreach (var prop in partDecl.Properties)
            {
                var name = prop.NameToken.Text.ToLowerInvariant();
                if (name == "octave"
                    && prop.GetChild(2) is SyntaxTokenNode v
                    && int.TryParse(v.Text, out int oct))
                    return oct; // explicit wins
                if (name == "instrument")
                {
                    var texts = new System.Collections.Generic.List<string>();
                    for (int vi = 2; vi < prop.SlotCount; vi++)
                        if (prop.GetChild(vi) is SyntaxTokenNode vt) texts.Add(vt.Text);
                    instrument = LilySharp.Core.Svg.Model.InstrumentDefaults.SplitInstrument(texts).Preset;
                }
            }
            if (instrument != null)
                return LilySharp.Core.Svg.Model.InstrumentDefaults.GetDefaults(instrument).Octave;
        }
        return 4;
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
            ? _partOctaveAnchor + pitch.OctaveOffset
            : RelativeOctave.Resolve(
                _currentNoteName, _currentOctave, noteName, pitch.OctaveOffset);

        // Update current state for next note
        _currentNoteName = noteName;
        _currentOctave = targetOctave;

        // Calculate MIDI pitch (shared step→MIDI formula), then transpose + clamp.
        return Math.Clamp(
            RelativeOctave.StepToMidi(
                RelativeOctave.StepIndex(pitch.BaseName), pitch.AccidentalOffset, targetOctave)
            + _currentTransposeSemitones,
            0, 127);
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
    /// Plays an arpeggio (<c>&lt;&lt; c e g &gt;&gt;</c>) as SEQUENTIAL notes whose octaves
    /// anchor to the first note — the chord rule: the octave reference stays frozen on the
    /// first note while the pitch names flow.
    /// </summary>
    private void ProcessArpeggio(ArpeggioSyntax arpeggio, MidiTrack track, MidiTrack conductorTrack)
    {
        var members = arpeggio.Members.ToList(); // notes and/or nested chords, in order
        if (members.Count == 0)
            return;
        // The first member is the ROOT (normal relative resolution); it anchors the group.
        ProcessNode(members[0], track, conductorTrack);
        int anchorOctave = _currentOctave;
        char rootLetter = FirstPitchLetter(members[0]) ?? 'c';
        int rootStep = RelativeOctave.StepIndex(rootLetter);

        // Every other member STACKS above the root — the same octave placement as a
        // `<c e g>` chord member, so the pitches are order-independent. Absolute mode makes
        // each member's octave = anchor + (step >= root ? 0 : 1) + its own '/, marks.
        bool savedAbsolute = _octaveAbsolute;
        int savedAnchor = _partOctaveAnchor;
        for (int i = 1; i < members.Count; i++)
        {
            int step = RelativeOctave.StepIndex(FirstPitchLetter(members[i]) ?? rootLetter);
            _octaveAbsolute = true;
            _partOctaveAnchor = anchorOctave + (step >= rootStep ? 0 : 1);
            ProcessNode(members[i], track, conductorTrack);
        }
        _octaveAbsolute = savedAbsolute;
        _partOctaveAnchor = savedAnchor;
        // After the group the running reference is the root (chord-after behavior).
        _currentOctave = anchorOctave;
        _currentNoteName = rootStep;
    }

    /// <summary>The letter of a member's root pitch — a note's letter, or a chord's root
    /// (first pitch) — used to stack the arpeggio's members above the first.</summary>
    private static char? FirstPitchLetter(SyntaxNode member) => member switch
    {
        NoteSyntax n => n.Pitch.PitchName.ToLowerInvariant()[0],
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

    private void ProcessChord(ChordSyntax chord, MidiTrack track)
    {
        // A chord breaks any pending tie chain. Chords are not currently tie
        // targets, and leaving stale _tiePending/_lastNoteIndex from the
        // previous note would let a later same-pitch note wrongly merge across
        // the chord.
        _tiePending = false;
        _lastNoteIndex = -1;

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

        // The first member is the ROOT (resolved relatively); every other member
        // STACKS above it — the same octave placement as a scale degree, so a
        // chord's pitches are independent of the order its notes are written
        // (<c e g> == <c 3 5> == <c g e>). The note AFTER the chord is relative to
        // the root. A deliberate Lily# divergence from LilyPond, matching the
        // collector and the MusicXML exporter.
        // Octave marks after the closing '>' (<1 3 5>' / <c e g>,,) shift the whole
        // chord; folding it into firstOctave flows through every stacked/degree member
        // and the following note, matching the collector and MusicXML exporter.
        int chordOctave = chord.ChordOctaveOffset;
        int chordShift = chordOctave * 12;

        bool isFirst = true;
        int firstNoteName = _currentNoteName;
        int firstOctave = _currentOctave;

        foreach (var pitch in pitches)
        {
            int midiPitch;
            if (isFirst)
            {
                midiPitch = System.Math.Clamp(CalculateRelativeMidiPitch(pitch) + chordShift, 0, 127); // advances state
                firstNoteName = _currentNoteName;
                firstOctave = _currentOctave + chordOctave;
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
                midiPitch = System.Math.Clamp(
                    RelativeOctave.StepToMidi(step, pitch.AccidentalOffset, octave) + _currentTransposeSemitones,
                    0, 127);
            }
            track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks, chord.Position,
                QuarterBend: pitch.QuarterOffset,
                SourceOrdinal: chordOrdinal, Timbre: _currentTimbre));
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
            int midiPitch = System.Math.Clamp(
                RelativeOctave.StepToMidi(step, alter, octave) + _currentTransposeSemitones, 0, 127);
            track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks, chord.Position,
                SourceOrdinal: chordOrdinal, Timbre: _currentTimbre));
        }

        // Drum chord members: GM percussion alongside any pitched members.
        foreach (var drum in chord.DrumNames)
        {
            var dinfo = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
            track.Notes.Add(new MidiNote(9, dinfo.GmKey, _velocity, startTick, durationTicks, chord.Position,
                SourceOrdinal: chordOrdinal, Timbre: 9));
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
        // unwritten leading grace falls back to a 1/32 note. The main stream's
        // _defaultDuration is untouched — the grace has its own local memory.
        //
        // Sounding time is 9/40 of the grace's NOTATED duration, LilyPond's
        // built-in MIDI behavior. LILYPOND-REF: ly/articulate.ly
        // ac:defaultGraceFactor = 9/40 ("though the notation reference says 1/4").
        Fraction? written = null;
        // Sounding time is 9/40 of the NOTATED duration for both a written value
        // and the unwritten 1/32 fallback (the fallback previously emitted a full
        // 1/32, ~4× too long, skipping the 9/40 scaling every other path applies).
        int GraceTicks(Fraction? w)
        {
            long notatedTicks = w is { } d ? FractionToTicks(d) : _ticksPerQuarter / 8;
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

    private (int velocity, int durationPercent) ApplyArticulationType(
        ArticulationType type, int velocity, int durationPercent)
    {
        return type switch
        {
            ArticulationType.Staccato => (velocity, 50),                      // Half duration
            ArticulationType.Staccatissimo => (velocity, 30),                 // Even shorter
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