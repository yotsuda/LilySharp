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
using LilySharp.Core.Svg.Collector;
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

    // Per-PART relative-pitch state WITHIN one section, so a part whose music is split
    // over more than one block picks its own chain up again instead of inheriting the
    // block that ran before it.
    // ⚠️ IT DOES NOT CROSS A SECTION BOUNDARY. It used to, under a comment saying that
    // matched the collector; it did not. `test/section-octave-reset` states the rule the
    // page and the MusicXML both follow — "octave resets to default at section
    // boundaries" — and "default" is the PART's own anchor, so a bass part reopens at
    // octave 3 (measured 2026-08-17: page C3, MIDI C4). The note VALUE rides the same
    // lane and had the same defect: `section A { c2 d }` then four bare letters played
    // four HALF notes against the page's four quarters.
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
    // ⚠️ The previous ONSET, not the previous NOTE. A chord is one onset with several
    // notes, so `<c e>2 ~ <c e>2` has to extend BOTH of them; while this was a single
    // index a chord could neither be tied from nor tied into, and every tied chord
    // re-articulated in the MIDI while the page drew the tie and the MusicXML wrote
    // the second chord as a tie-stop (measured 2026-08-17 on 8 of 566 books).
    private bool _tiePending;
    private readonly List<int> _lastOnset = new();
    private MidiTrack? _lastNoteTrack;

    /// <summary>The notes a tie arriving at this onset may extend — the previous
    /// onset's, when a tie is pending on the same track, and nothing otherwise. The
    /// caller consumes each index it uses, so a chord that sounds one pitch twice
    /// cannot extend one note twice.</summary>
    private List<int> OpenTieTargets(MidiTrack track)
        => _tiePending && _lastNoteTrack == track ? new List<int>(_lastOnset) : new List<int>();

    /// <summary>Extend the tied-from note of <paramref name="midiPitch"/> and return its
    /// index, or -1 when nothing ties into it. A tie joins noteheads of the SAME pitch —
    /// `&lt;c e&gt;~ &lt;c g&gt;` sustains the c and articulates the g — so an unmatched
    /// member starts its own note rather than silently borrowing a neighbour's.</summary>
    /// <remarks>The extension is the full written <paramref name="durationTicks"/>, not an
    /// articulation-shortened length: a staccato note that is tied-FROM gains full length
    /// on the merge. Rare (tie-over-staccato); kept deliberately, matching the intent of a
    /// sustained tied note.</remarks>
    private int ExtendTied(MidiTrack track, List<int> targets, int midiPitch, int durationTicks)
    {
        for (int k = 0; k < targets.Count; k++)
        {
            int i = targets[k];
            if (i < 0 || i >= track.Notes.Count || track.Notes[i].Pitch != midiPitch) continue;
            track.Notes[i] = track.Notes[i] with
            {
                DurationTicks = track.Notes[i].DurationTicks + durationTicks,
            };
            targets.RemoveAt(k);
            return i;
        }
        return -1;
    }

    /// <summary>Record what this onset sounded, so the next tie knows what to extend.</summary>
    private void CloseOnset(MidiTrack track, List<int> indices, bool startsTie)
    {
        _lastOnset.Clear();
        _lastOnset.AddRange(indices);
        _lastNoteTrack = track;
        _tiePending = startsTie;
    }

    // Sounding-pitch transpose for the part currently being played. A part option
    // transpose: shifts every note by the interval's semitones (no respelling).
    private SyntaxNode? _root;
    private int _currentTransposeSemitones;

    /// <summary>Written pitch → MIDI key: the chromatic transpose. The ONE funnel every
    /// pitched emission uses, so the transpose cannot miss a path. (A phrase-scoped
    /// DIATONIC shift was applied here first, in the written key, until the reference
    /// interval argument that armed it was removed 2026-08-28.)</summary>
    /// <remarks>
    /// ⚠️ THE RESULT IS NOT CLAMPED — <see cref="SoundKey"/> does that, once, where the note
    /// is emitted. It used to clamp here, and three callers then added a chord or arpeggio
    /// octave to the CLAMPED value and clamped again: <c>&lt;c e g&gt;,</c> on a chord already over the
    /// ceiling came out an octave below where the arithmetic says, because the shift was
    /// applied to 127 rather than to the pitch. Keeping the range out of the arithmetic also
    /// gives the warning something true to say about how far outside a note fell.
    /// </remarks>
    private int WrittenToMidi(int step, int alter, int octave)
    {
        return RelativeOctave.StepToMidi(step, alter, octave) + _currentTransposeSemitones;
    }

    /// <summary>The written key a note actually SOUNDS at: MIDI has 128 keys, so anything
    /// outside 0-127 is pinned to the edge — and said so, because the page and the MusicXML
    /// keep the octave the source wrote and only this output silently loses it.</summary>
    /// <remarks>
    /// ⚠️ THIS IS THE ONLY PLACE THE RANGE IS APPLIED. A book that runs off the top does it
    /// by its own spelling — `a'' a'' a''` in relative mode climbs two octaves a note — and
    /// the result was a run of identical 127s that no output, log or net mentioned
    /// (measured 2026-08-17: `audit/lpreg/fermata-b-obs-probe` pins 2 of 4 notes, and
    /// `test/section-meter-resets-to-global` 9 of 14, both in silence).
    /// </remarks>
    private int SoundKey(int writtenKey, int position)
    {
        if (writtenKey is >= 0 and <= 127) return writtenKey;
        _outOfRange.Add((position, writtenKey));
        return Math.Clamp(writtenKey, 0, 127);
    }

    // Every note this export could not sound where it was written: (source offset, the key
    // the arithmetic asked for). Reported rather than dropped — HANDOFF §2F, "if you drop
    // something, say so in Warnings".
    private readonly List<(int Position, int Key)> _outOfRange = new();

    /// <summary>One line per pitch this export had to pin to the edge of the MIDI range.</summary>
    public IReadOnlyList<string> Warnings => _outOfRange
        .Select(o => $"pitch out of MIDI range at offset {o.Position}: key {o.Key} "
            + $"sounds as {Math.Clamp(o.Key, 0, 127)} (the page and the MusicXML keep the written octave)")
        .ToList();

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

    /// <summary>
    /// The <c>form</c> to play, or null for the default (<see cref="ScoreForms.Primary"/>).
    /// </summary>
    /// <remarks>
    /// One <c>.mid</c> carries one arrangement, so a file with several movements needs one
    /// export per movement — which is what <c>lysc midi --score</c> / <c>--all</c> ask for.
    /// LilyPond answers the same way: two <c>\score</c> blocks with <c>\midi { }</c> write
    /// two files, <c>ts.mid</c> and <c>ts-1.mid</c> (2.26.0, measured).
    /// </remarks>
    public FormDeclarationSyntax? Form { get; init; }

    /// <summary>
    /// The <c>score</c> being written, or null to resolve it from the tree (the one whose
    /// form is being played, else the first).
    /// </summary>
    /// <remarks>
    /// Read for ONE thing: which part a BARE section belongs to (see
    /// <see cref="_bareSectionOwner"/>). Everything else this export does is still read
    /// from the music itself, so a file with no <c>score</c> at all plays exactly as before.
    /// </remarks>
    public RenderSpec? Score { get; init; }

    /// <summary>
    /// The part a section that declares no <c>partName { }</c> block belongs to: the one
    /// part the score engraves, or null when the score names none or several.
    /// </summary>
    /// <remarks>
    /// ⚠️ WHY THE SCORE IS THE ONE WHO KNOWS. <c>section A { c4 … }</c> written outside any
    /// part block is music no block claims, and the only statement in the file that says
    /// whose it is is <c>score main { staff bl }</c>. The page has always read it that way
    /// (RenderSpecParser → GetPartDefaults); this export did not read <c>score</c> at all,
    /// so a bare section got no part header, no anchor, no sounding shift and no
    /// section-boundary reset. MEASURED 2026-08-17 on
    /// <c>part bl { clef bass } section A { c'4 d e f } section B { g'4 f e d }</c>: the
    /// page and the MusicXML read C5 D5 E5 F5 / G4 F4 E4 D4, the MIDI played G6 for that
    /// second <c>g'</c>. Put the same music inside <c>bl { }</c> blocks and the difference
    /// is gone — which is the pair that says this is attribution and nothing else.
    /// <para>
    /// ⚠️ ONLY WHEN THE SCORE NAMES EXACTLY ONE PART. Two parts means the page draws the
    /// same music twice, in two registers, and one MIDI line cannot be both; that case is
    /// left as it was and warned about. It has NO instance in the corpus — 23 of the 566
    /// books write bare sections, 19 of them declare a non-default part, and not one gives
    /// a bare section to more than one part (measured 2026-08-17, HANDOFF §2F ⒢).
    /// </para>
    /// </remarks>
    private string? _bareSectionOwner;

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
        _bareSectionOwner = RenderSpecParser.SingleEngravedPart(tree, Score, Form);
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
                _currentClef = Header(part.Name.Text).Clef;
                _currentTimbre = PartTimbre(part.Name.Text);
                ProcessChildren(part, track, conductorTrack);
                (_partOctaveAnchor, _partAbsoluteBase) = (4, 4);
                _currentClef = Svg.Model.ClefType.Treble;
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

            case ClefDeclarationSyntax clefDecl:
                ApplyClefChange(clefDecl.ClefName.Text);
                break;

            case CueExpressionSyntax cue:
            {
                // `cue bass { … }` reads its body in the cue clef and hands the staff's own
                // clef back at the end — both edges move the frame, and the collector does
                // exactly this (MeasureCollector.MusicWalk.ProcessCueRegion). The fixture
                // that measures it says so in its own margin: `cue-clef-manually` writes
                // "cue の bass clef は相対 anchor を octave 3 に引く" and compensates with a
                // leading c'. This walk did not, so those four cue notes sounded an octave
                // above the page.
                // ⚠️ BOTH EDGES ARE UNCONDITIONAL, unlike a `clef` declaration: the collector
                // resets on the way in and on the way out whether or not the cue clef differs
                // from the staff's, and the page is the rule here.
                var outer = _currentClef;
                if (cue.ClefKeyword is { } cueClef)
                    SetFrameToClef(Svg.Collector.MeasureCollector.ParseClefType(
                        cueClef.Text.ToLowerInvariant()));
                ProcessNode(cue.Body, track, conductorTrack);
                if (cue.ClefKeyword != null) SetFrameToClef(outer);
                break;
            }

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

            case SlashNoteSyntax slash:
                ProcessSlashNote(slash);
                break;

            case BareDurationSyntax bare:
                ProcessBareDuration(bare, track);
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
                    // top of any part transpose; restored after the body.
                    int savedTranspose = _currentTransposeSemitones;
                    _currentTransposeSemitones += PhraseTransposeSemitones();
                    // The same marks in ABSOLUTE mode: there is no running frame to
                    // move, so the shift lands on the absolute anchor instead — the
                    // collector's OctaveBase, this walker's _partAbsoluteBase.
                    int savedAbsBase = _partAbsoluteBase;
                    _partAbsoluteBase += varRef.OctaveOffset;
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
                    _partAbsoluteBase = savedAbsBase;
                    // Frame hand-off at the phrase's ANCHOR (matches the
                    // collector's ExitPhraseTranspose): the reference is ONE
                    // item, the chord rule — its interior never leaks, and its
                    // own marks shift what propagates, so a note after Melody'
                    // is relative to the shifted anchor. A pitchless body hands
                    // nothing off.
                    if (anchorStep is { } astep)
                    {
                        int oct = RelativeOctave.Resolve(
                            0, _partOctaveAnchor + varRef.OctaveOffset, astep, 0);
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

    /// <summary>True when <paramref name="form"/> is the one to play: <see cref="Form"/>
    /// when the caller named one, else the primary (<see cref="ScoreForms.Primary"/>).</summary>
    private bool IsPrimaryForm(FormDeclarationSyntax form)
        => ReferenceEquals(form, Form ?? ScoreForms.Primary(_root!));

    /// <summary>
    /// Plays <paramref name="body"/> as the music of <paramref name="partName"/>: its
    /// octave anchors, its clef, its timbre and its written→sounding shift, restored after.
    /// </summary>
    /// <remarks>
    /// The two callers are the two ways a section can belong to a part without a
    /// <c>partName { }</c> block around its music: PART-MAJOR (the section is written
    /// inside <c>part X { … }</c>) and BARE (no block anywhere, the <c>score</c> says
    /// whose it is — see <see cref="_bareSectionOwner"/>). One house, because they arm the
    /// same six things and the part-major one had already grown a comment explaining that
    /// it must arm the sounding shift "too".
    /// <para>
    /// ⚠️ The part's pitch/duration lane is restored on entry and saved on exit, so
    /// concurrently played parts (one <c>PlaySection</c> call each, same structure
    /// reference) keep independent relative-octave chains WITHIN a section instead of
    /// inheriting the previous part's last note.
    /// </para>
    /// </remarks>
    private void PlayInPart(string partName, Action body)
    {
        var (anchor, absBase) = PartOctaveAnchors(partName);
        var pitch = _partPitchLanes.TryGetValue(partName, out var saved)
            ? saved
            : (NoteName: 0, Octave: anchor, Dur: Fraction.Quarter);
        _currentNoteName = pitch.NoteName;
        _currentOctave = pitch.Octave;
        _defaultDuration = pitch.Dur;
        _partOctaveAnchor = anchor;
        _partAbsoluteBase = absBase;
        _currentClef = Header(partName).Clef;  // a mid-music `clef` is a change FROM this
        _currentTimbre = PartTimbre(partName);
        // Music without a PartBlockSyntax around it never passes the arming in ProcessNode,
        // so the instrument's sounding shift is armed here — otherwise a bass or guitar
        // sounded at written pitch.
        _currentTransposeSemitones = PartSoundingShift(partName);
        body();
        _partPitchLanes[partName] = (_currentNoteName, _currentOctave, _defaultDuration);
        (_partOctaveAnchor, _partAbsoluteBase) = (4, 4);
        _currentTimbre = 0;
        _currentTransposeSemitones = 0;
    }

    /// <summary>True when the section declares at least one <c>partName { }</c> block —
    /// i.e. its music says for itself whose it is.</summary>
    private static bool SectionHasPartBlock(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
            if (section.GetChild(i) is PartBlockSyntax)
                return true;
        return false;
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
    /// as opposed to only directives and part / chord / lyric blocks — i.e. its own
    /// <c>key</c> is walked as music, not a header. THE one spelling lives with the
    /// collector (MeasureCollector.SectionHasInlineMusic): this file's own copy was the
    /// drifted one — it did not exclude override/revert/once, so a header carrying a
    /// page directive beside its key lost the key here and the .mid played the phrase
    /// in the home key while the page engraved the section's.</summary>
    private static bool SectionHasInlineMusic(SectionDeclarationSyntax section)
        => Svg.Collector.MeasureCollector.SectionHasInlineMusic(section);

    /// <summary>
    /// Plays one section: its part blocks run SIMULTANEOUSLY (each from the
    /// section's start tick; the section ends with the longest part), and each
    /// part reopens the octave frame and the note value the section starts at.
    /// </summary>
    private void PlaySection(SectionDeclarationSyntax section, MidiTrack track, MidiTrack conductorTrack)
    {
        // A section is self-contained: its phrase auto-transpose baseline and the
        // scale-degree key both revert to the score's home key (a mid-section
        // modulation cannot leak out).
        _ambientTonic = _homeTonic;
        _keySharps = _homeKeySharps;
        // ... and so do the two running defaults a bare letter reads. The lanes below
        // hold a part's chain BETWEEN its blocks inside this section, not across the
        // boundary into it — that is the whole of what "self-contained" means for pitch,
        // and it is the rule `test/section-octave-reset` names.
        _partPitchLanes.Clear();

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
                PlayInPart(owner.Name.Text, () => ProcessChildren(section, track, conductorTrack));
                return;
            }
        }

        // A section that declares no part block at all is music the SCORE attributes, not
        // the music (see _bareSectionOwner). Played inside that part exactly as a
        // part-major section is played inside the part that contains it.
        if (_bareSectionOwner is { } bareOwner && !SectionHasPartBlock(section))
        {
            PlayInPart(bareOwner, () => ProcessChildren(section, track, conductorTrack));
            return;
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
                _currentClef = Header(pname).Clef; // a mid-music `clef` is a change FROM this
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
        // The state a from-the-beginning repeat has to put back, captured before anything
        // plays. The same three things PlayRepeatBlock / ProcessRepeatSpan already restore
        // per pass, plus the per-part pitch lanes, which those two do not touch because a
        // '|: … :|' body never rewinds past its own start.
        var pieceOrdinals = new Dictionary<int, int>(_sourceOrdinals);
        var piecePitchLanes = new Dictionary<string, (int, int, Fraction)>(_partPitchLanes);
        var pieceDuration = _defaultDuration;
        int pieceVelocity = _velocity;

        // The item spellings are read ONCE, by FormWalk (a silent `~Name` is a
        // SectionRef like any other — the bite this method's remark records).
        var items = FormWalk.Read(structure);
        for (int i = 0; i < items.Count; i++)
        {
            // A ':|' written in the form outside any '|: … :|' block: it has no '|:' to
            // pair with, so it repeats FROM THE BEGINNING OF THE PIECE (user decision,
            // 2026-08-15) — the ordinary reading of a one-sided end-repeat, and the one
            // MusicXML already spells (a backward repeat with no forward one). Replay
            // everything before it, once.
            if (items[i] is FormWalk.LoneRepeatEnd)
                RepeatFromTheBeginning(items, i, track, conductorTrack,
                    pieceOrdinals, piecePitchLanes, pieceDuration, pieceVelocity);
            else
                PlayFormItem(items[i], track, conductorTrack);
        }
    }

    /// <summary>Plays one form item. The one switch both the first pass and a
    /// from-the-beginning replay run — they used to be two hand-synced switches,
    /// and a shape handled in one and not the other would make <c>A [1. B] :|</c>
    /// play B once and then not at all.</summary>
    private void PlayFormItem(FormWalk.Item item, MidiTrack track, MidiTrack conductorTrack)
    {
        switch (item)
        {
            case FormWalk.SectionRef s:
                PlaySectionByName(s.Name, track, conductorTrack);
                break;
            case FormWalk.Repeat r:
                PlayRepeatBlock(r, track, conductorTrack);
                break;
            // A volta ending no repeat block opened — `form main { A [1. B] }`. There is
            // nothing for it to be an alternative TO, so it sounds as its plain section,
            // once.
            // LILYPOND-REF: lily/alternative-sequence-iterator.cc:83-84 — Alternative_sequence_iterator::analyze defaults repeat-count to 1
            // with no enclosing repeat, which is exactly "played once". Confirmed on
            // 2.26.0 (a `\volta 1` alternative with no `\repeat` in front renders
            // byte-identically to the bare music), and both
            // MusicXmlExporter and LilyPondExporter already read it this way; MIDI and
            // the page were the two walks that dropped it. Saying so to the author is
            // the other half of the repair and lives in FormDeclarationValidator.
            case FormWalk.Ending e:
                PlaySectionByName(e.Node.SectionName.Text, track, conductorTrack);
                break;
            // A one-sided ':|' only rewinds on the FIRST pass (PlayForm's loop); inside
            // a replayed stretch it does NOT rewind again — that would not terminate.
            // One rewind per written ':|' is what the sign says. Display relabels,
            // navigation text and anything else are visual and skipped.
        }
    }

    /// <summary>
    /// Replays form items <c>[0, upTo)</c> — the piece from its beginning up to the
    /// one-sided <c>:|</c> — restoring the state the piece started from so the second pass
    /// sounds like the first.
    /// </summary>
    private void RepeatFromTheBeginning(IReadOnlyList<FormWalk.Item> items, int upTo,
        MidiTrack track, MidiTrack conductorTrack,
        Dictionary<int, int> pieceOrdinals,
        Dictionary<string, (int, int, Fraction)> piecePitchLanes,
        Fraction pieceDuration, int pieceVelocity)
    {
        // The replayed music is ENGRAVED once, so its printed copies are the ones already
        // laid out — the ordinals restart from the snapshot, exactly as a '|: … :|' second
        // pass does (see PlayRepeatBlock).
        _sourceOrdinals = new Dictionary<int, int>(pieceOrdinals);
        _partPitchLanes.Clear();
        foreach (var kv in piecePitchLanes)
            _partPitchLanes[kv.Key] = kv.Value;
        _defaultDuration = pieceDuration;
        _velocity = pieceVelocity;

        for (int j = 0; j < upTo; j++)
            PlayFormItem(items[j], track, conductorTrack);
    }

    private void PlayRepeatBlock(FormWalk.Repeat repeatBlock, MidiTrack track, MidiTrack conductorTrack)
    {
        var body = new List<string>();
        var alternatives = new List<string>();
        foreach (var child in repeatBlock.Children)
        {
            switch (child)
            {
                // …a silent reference counts inside a repeat body too. See PlayForm's remark.
                case FormWalk.SectionRef s:
                    body.Add(s.Name);
                    break;
                case FormWalk.Ending e:
                    alternatives.Add(e.Node.SectionName.Text);
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

    // The clef the walk is reading in, so an UNCHANGED `clef` can be told from a change.
    private LilySharp.Core.Svg.Model.ClefType _currentClef = LilySharp.Core.Svg.Model.ClefType.Treble;

    /// <summary>A mid-music <c>clef</c>: the relative frame reopens at the new clef's own
    /// octave, so `clef bass c,4` is a low C without extra commas.</summary>
    /// <remarks>
    /// ⚠️ THE OCTAVE ONLY, not the note name — the frame keeps reading from the letter it
    /// last saw, and only the register it counts from moves. That is what
    /// <c>MeasureCollector.MusicWalk</c>'s clef branch does, and this walk exists so the
    /// two agree: until 2026-08-17 the page re-anchored and the MIDI did not, so
    /// `g4 a clef bass c,4 d` printed C3 D3 and played C5 D5 (measured, `test/clef-change`).
    /// ⚠️ AN UNCHANGED CLEF CHANGES NOTHING. LilyPond makes a Clef grob only when the
    /// resolved glyph/position/transposition differ, so a redundant `clef treble` must not
    /// reset the frame either — the collector's branch says the same and cites
    /// lily/clef-engraver.cc:139-166 inspect_clef_properties for it.
    /// ⚠️ This is a DELIBERATE divergence from LilyPond, whose `\relative` never looks at a
    /// clef. It is Lily#'s rule because a part header's clef already sets the anchor
    /// (PartHeaderDefaults.AnchorOctave), and one word means one thing; the LilyPond twin
    /// therefore cannot spell it and writes corrected octave marks instead, the way it
    /// already does for `transpose`. Decided 2026-08-17, HANDOFF §3.
    /// </remarks>
    private void ApplyClefChange(string? clefWord)
    {
        var next = Svg.Collector.MeasureCollector.ParseClefType((clefWord ?? "").ToLowerInvariant());
        if (next == _currentClef) return;
        SetFrameToClef(next);
    }

    /// <summary>Reopen the relative frame in <paramref name="clef"/> — the octave only.</summary>
    private void SetFrameToClef(LilySharp.Core.Svg.Model.ClefType clef)
    {
        _currentClef = clef;
        _currentOctave = LilySharp.Core.Svg.Model.InstrumentDefaults.GetDefaultOctave(clef);
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
        // An empty `| |` bar has to COST TIME here, or a gap written in one part pulls
        // everything after it a whole bar early against the others. The engraver never had
        // that problem — it walks BARS, so an item-less placeholder still held its slot —
        // and this walk counts DURATIONS, which is why the defect was audible only.
        // MEASURED before the repair (two staves, `c'1 | | e'1` against `c1 | g1 | c1`):
        // the upper part's third bar sounded at tick 1920 where the lower part's did at
        // 3840. Owner's decision 2026-08-28; the engraving half is
        // MeasureBuilder.EmitEmptyMeasure, which fills the same bar with a spacer.
        // ⚠️ THIS IS A SECOND SPELLING OF THE BARE-BARLINE RULE and it is admitted as one:
        // the collector's copy is MeasureBuilder.HandleBarline and the validators' is
        // MeasureModel.Split, and neither is reachable from here (this walk sees a SIBLING
        // LIST, not a measure stream). What keeps the three from drifting is not hope but
        // EmptyMeasureValidatorTests' identity pair — `| |` against the `s1` an author
        // would type by hand — which fails the moment any one of them answers differently.
        // The rule in this walk's terms: a bare `|` CLOSES the bar before it when time has
        // passed since the last boundary; otherwise it opens an EMPTY measure worth one
        // bar of silence. A typed barline (`||`, `:|`, `|.`) decorates a boundary and
        // never opens one.
        // ⚠️ THE SCOPE START COUNTS AS CLAIMED (owner's decision, 2026-08-28). It did not
        // until that day — a `|` opening the sequence found the boundary unclaimed and was
        // absorbed, so `section A { | | | | }` sounded three bars of silence where the page
        // now draws four. The engraving half is MeasureBuilder._confirmableBoundary; the
        // two halves are kept honest by EmptyMeasureValidatorTests' identity pair, which
        // plays `| |` against the `s1` an author would type by hand.
        int boundaryTick = _currentTick;
        bool boundaryClaimed = true;
        // A `|:` OPENING the scope closes nothing, so it leaves no silent bar behind it —
        // the one place `|` and `|:` part company (MeasureBuilder._atScopeStart is twin).
        bool atScopeStart = true;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is BarlineSyntax bar)
            {
                // `|:` pairs like a bare `|` (it OPENS the next bar rather than
                // decorating the one behind it), so `… | |: …` is a gap here too —
                // MeasureBuilder.HandleBarline owns the rule.
                bool pairsHere = bar.BarToken.Kind is SyntaxKind.Bar
                    || (bar.BarToken.Kind is SyntaxKind.RepeatStartBar && !atScopeStart);
                atScopeStart = false;
                if (pairsHere && _currentTick <= boundaryTick && boundaryClaimed)
                {
                    _currentTick += MeasureTicks();  // the second of a `| |` pair
                }
                boundaryTick = _currentTick;
                boundaryClaimed = true;
                // …and fall through: the repeat spans below key on the repeat barlines,
                // and every other node type still takes its ordinary path.
            }
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
            // ⚠️ A ':|' reached HERE is NOT necessarily one-sided, and THIS WALK CANNOT TELL.
            // It sees one SECTION's music, and a '|:' opened in another section pairs with a
            // ':|' in this one — the collector's flattened measure stream is where that
            // becomes visible, which is exactly why LYS4017 is decided there and not here.
            // MEASURED, and it is why this arm does nothing: a first version treated it as
            // one-sided and rewound the piece, which changed 3 correctly written books in the
            // author's library (ABC, Automatic, Beat It — each spelling '|:' in one section
            // and '] :|' in a later one) by replaying the whole song.
            // Only the FORM-level ':|' gets the from-the-beginning meaning, where PlayForm
            // sees the whole piece and the parser guarantees a form repeat block closes at
            // form level. A one-sided ':|' written inside section music is therefore still
            // not played — measured 2026-08-15: ZERO of the 133 books on disk that contain a
            // repeat barline spell one.
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

    /// <summary>One measure of the meter in force, in ticks — what an empty <c>| |</c> bar
    /// costs. The same length <c>MeasureBuilder.EmitEmptyMeasure</c> gives that bar's
    /// spacer, so the two walks agree on what the gap is worth.</summary>
    private int MeasureTicks()
        => FractionToTicks(new Fraction(_timeNumerator, _timeDenominator));

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

            char? letter = RelativeOctave.FirstPitchLetter(member);
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
            midiPitch = CalculateRelativeMidiPitch(pitch) + octaveShift * 12;
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
        track.Notes.Add(new MidiNote(track.Channel, SoundKey(midiPitch, pitch.Position), _velocity,
            _currentTick, ticks, pitch.Position, QuarterBend: pitch.QuarterOffset,
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
        track.Notes.Add(new MidiNote(track.Channel, SoundKey(midiPitch, degree.Position), _velocity,
            _currentTick, ticks, degree.Position,
            SourceOrdinal: NextOrdinal(degree.Position), Timbre: _currentTimbre));
        _currentTick += ticks;
    }


    private void ProcessNote(NoteSyntax note, MidiTrack track)
    {
        int midiPitch = CalculateRelativeMidiPitch(note.Pitch);

        var duration = GetDuration(note.Duration);
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks); // grace notes steal from this note

        // `a4@rest` is a REST that takes its staff position from a written pitch, so it is
        // SILENT — MeasureCollector.CreatePitchedRestItem's remark says "must not sound in
        // MIDI", and until 2026-08-17 it did: the probe `a'4@rest c'4 r4 g'4@rest` played 3
        // note-ons against the control's 1. It leaves here rather than earlier because the
        // two lines above are exactly what it must still do — move the relative-octave frame
        // on (CalculateRelativeMidiPitch) and carry the duration to the next item — which is
        // the whole of what the note it replaces contributes.
        if (Semantics.PitchedRest.Is(note))
        {
            _tiePending = false; // like any rest: a tie cannot span it
            _currentTick += durationTicks;
            return;
        }

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
        // ⚠️ Pinned to the range BEFORE the tie is matched: the notes already in the track
        // hold sounding keys, so comparing a written 134 against a stored 127 would refuse
        // to merge a tie the page draws.
        midiPitch = SoundKey(midiPitch, note.Position);

        // What a following bare duration copies (same contract as
        // _resolvedChordNotes): the SOUNDING key, resolved by this walk.
        _resolvedNoteSound[note] = (midiPitch, note.Pitch.QuarterOffset);

        // If the previous onset tied into this one (same pitch on the same track),
        // extend that note instead of emitting a new note-on/off pair.
        var targets = OpenTieTargets(track);
        int tiedInto = ExtendTied(track, targets, midiPitch, durationTicks);
        if (tiedInto >= 0)
        {
            CloseOnset(track, [tiedInto], startsTie); // continue a tie chain (c~ c~ c)
            _currentTick += durationTicks;
            return;
        }

        track.Notes.Add(new MidiNote(track.Channel, midiPitch, velocity,
            _currentTick, actualDuration, note.Position,
            QuarterBend: note.Pitch.QuarterOffset,
            SourceOrdinal: NextOrdinal(note.Position), Timbre: _currentTimbre));
        CloseOnset(track, [track.Notes.Count - 1], startsTie);
        _currentTick += durationTicks;
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
        CloseOnset(track, [track.Notes.Count - 1], startsTie: false);
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

    /// <summary>The sounding key of every pitched note this walk has emitted -
    /// what a following bare duration copies. Same contract as
    /// <see cref="_resolvedChordNotes"/>.</summary>
    private readonly Dictionary<NoteSyntax, (int MidiPitch, int QuarterBend)> _resolvedNoteSound = new();

    /// <summary>A slash note is SILENT - it has no pitch to sound - and, like a
    /// rest, breaks any pending tie. It still occupies its time and carries the
    /// running duration forward.</summary>
    private void ProcessSlashNote(SlashNoteSyntax slash)
    {
        _tiePending = false;
        var duration = GetDuration(slash.Duration);
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks);
        _currentTick += durationTicks;
    }

    /// <summary>A bare duration - the previous note, chord or slash again at the
    /// written length (LILYPOND-REF: lily/parser.yy music_embedded). Pitches come
    /// from this walk's recorded resolutions, exactly as a <c>q</c>'s do; the
    /// repetition's OWN post-events (dynamics, tie) apply, the original's do not.</summary>
    private void ProcessBareDuration(BareDurationSyntax bare, MidiTrack track)
    {
        var duration = GetDuration(bare.Duration);
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks);
        int startTick = _currentTick;

        int velocity = _velocity;
        int durationPercent = 100;
        foreach (var child in bare.Articulations)
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
        bool startsTie = bare.Articulations.OfType<TieSyntax>().Any();

        switch (Music.BareDurations.OriginalOf(bare))
        {
            case NoteSyntax note when _resolvedNoteSound.TryGetValue(note, out var sound):
            {
                var targets = OpenTieTargets(track);
                int tiedInto = ExtendTied(track, targets, sound.MidiPitch, durationTicks);
                if (tiedInto >= 0)
                {
                    CloseOnset(track, [tiedInto], startsTie);
                    break;
                }
                track.Notes.Add(new MidiNote(track.Channel, sound.MidiPitch, velocity,
                    startTick, actualDuration, bare.Position,
                    QuarterBend: sound.QuarterBend,
                    SourceOrdinal: NextOrdinal(bare.Position), Timbre: _currentTimbre));
                CloseOnset(track, [track.Notes.Count - 1], startsTie);
                break;
            }
            case ChordSyntax chord when _resolvedChordNotes.TryGetValue(chord, out var notes):
            {
                // The full chord again - same emission as ProcessChordRepetition.
                var tieTargets = OpenTieTargets(track);
                var onset = new List<int>();
                int ordinal = NextOrdinal(bare.Position);
                foreach (var n in notes)
                {
                    if (n.IsDrum)
                    {
                        track.Notes.Add(new MidiNote(9, n.MidiPitch, velocity, startTick, actualDuration,
                            bare.Position, SourceOrdinal: ordinal, Timbre: 9));
                        continue;
                    }
                    int tiedInto = ExtendTied(track, tieTargets, n.MidiPitch, durationTicks);
                    if (tiedInto >= 0) { onset.Add(tiedInto); continue; }
                    track.Notes.Add(new MidiNote(track.Channel, n.MidiPitch, velocity, startTick, actualDuration,
                        bare.Position, QuarterBend: n.QuarterBend, SourceOrdinal: ordinal, Timbre: _currentTimbre));
                    onset.Add(track.Notes.Count - 1);
                }
                CloseOnset(track, onset, startsTie);
                break;
            }
            case DrumNoteSyntax drum:
            {
                var info = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
                track.Notes.Add(new MidiNote(9, info.GmKey, velocity, startTick, actualDuration,
                    bare.Position, SourceOrdinal: NextOrdinal(bare.Position), Timbre: 9));
                CloseOnset(track, [track.Notes.Count - 1], startsTie: false);
                break;
            }
            default:
                // A slash (silent) or an unresolved repeat (validator reports it):
                // occupy the time, sound nothing, break any pending tie.
                _tiePending = false;
                break;
        }

        _currentTick = startTick + durationTicks;
    }

    private void ProcessChord(ChordSyntax chord, MidiTrack track, int extraOctave = 0)
    {
        // A chord is ONE onset: a tie arriving here extends every member the previous
        // onset also sounded, and a member it did not sound articulates. The other two
        // outputs already state the same rule from their own side — the MusicXML
        // exporter's "Ties apply to EVERY member of the chord", and the collector's
        // HasTieAfter, which draws one tie per head.
        var tieTargets = OpenTieTargets(track);
        bool startsTie = chord.Articulations.OfType<TieSyntax>().Any();
        var onset = new List<int>();
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
                    midiPitch = CalculateRelativeMidiPitch(pitch) + chordShift; // advances state
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
                midiPitch = CalculateRelativeMidiPitch(pitch) + chordShift;
            }
            else
            {
                int step = GetNoteName(pitch.BaseName);
                int octave = firstOctave + (step >= firstNoteName ? 0 : 1) + pitch.OctaveOffset;
                midiPitch = WrittenToMidi(step, pitch.AccidentalOffset, octave);
            }
            midiPitch = SoundKey(midiPitch, chord.Position);
            int tiedInto = ExtendTied(track, tieTargets, midiPitch, durationTicks);
            if (tiedInto >= 0)
                onset.Add(tiedInto);
            else
            {
                track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks, chord.Position,
                    QuarterBend: pitch.QuarterOffset,
                    SourceOrdinal: chordOrdinal, Timbre: _currentTimbre));
                onset.Add(track.Notes.Count - 1);
            }
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
            int midiPitch = SoundKey(WrittenToMidi(step, alter, octave), chord.Position);
            int tiedInto = ExtendTied(track, tieTargets, midiPitch, durationTicks);
            if (tiedInto >= 0)
                onset.Add(tiedInto);
            else
            {
                track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, startTick, durationTicks, chord.Position,
                    SourceOrdinal: chordOrdinal, Timbre: _currentTimbre));
                onset.Add(track.Notes.Count - 1);
            }
            resolved.Add((midiPitch, 0, false));
        }

        // Drum chord members: GM percussion alongside any pitched members. They are
        // deliberately left OUT of the onset: drums do not tie (ProcessDrumNote), so a
        // tie must not find one and sustain a cymbal.
        foreach (var drum in chord.DrumNames)
        {
            var dinfo = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
            track.Notes.Add(new MidiNote(9, dinfo.GmKey, _velocity, startTick, durationTicks, chord.Position,
                SourceOrdinal: chordOrdinal, Timbre: 9));
            resolved.Add((dinfo.GmKey, 0, true));
        }
        _resolvedChordNotes[chord] = resolved;
        CloseOnset(track, onset, startsTie);

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
        // A q is an onset like the chord it copies, so it ties like one — `<c e>2~ q`
        // sustains, `q~ q` chains. The MusicXML exporter already reads `rep.Articulations`
        // for the same tie; leaving this walk out would have kept the third spelling of
        // one rule broken after the first two were fixed.
        var tieTargets = OpenTieTargets(track);
        bool startsTie = rep.Articulations.OfType<TieSyntax>().Any();
        var onset = new List<int>();

        int startTick = _currentTick;
        var duration = rep.Duration is { } rd ? GetDuration(rd) : _defaultDuration;
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks);

        if (ChordRepetitions.OriginalOf(rep) is { } original
            && _resolvedChordNotes.TryGetValue(original, out var notes))
        {
            int ordinal = NextOrdinal(rep.Position);
            foreach (var n in notes)
            {
                if (n.IsDrum)
                {
                    track.Notes.Add(new MidiNote(9, n.MidiPitch, _velocity, startTick, durationTicks,
                        rep.Position, SourceOrdinal: ordinal, Timbre: 9));
                    continue;
                }
                int tiedInto = ExtendTied(track, tieTargets, n.MidiPitch, durationTicks);
                if (tiedInto >= 0) { onset.Add(tiedInto); continue; }
                track.Notes.Add(new MidiNote(track.Channel, n.MidiPitch, _velocity, startTick, durationTicks,
                    rep.Position, QuarterBend: n.QuarterBend, SourceOrdinal: ordinal, Timbre: _currentTimbre));
                onset.Add(track.Notes.Count - 1);
            }
        }
        CloseOnset(track, onset, startsTie);

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

        // ⚠️ AND SO IS THE PITCH FRAME, for the same reason and it is the same fact: the
        // page draws ONE copy, so every iteration has to sound that copy. Without this the
        // walk re-entered the body with the frame the previous iteration left, and a body
        // that moves the frame climbed. MEASURED 2026-08-17 on audit/lpreg/chord-tremolo-whole
        // (`repeat tremolo 32 { g''64 a }`, a page of one G5-A5 pair): the MIDI played
        // 79 81 103 105 and then 127 sixty times — a rising figure pinned against the top of
        // the MIDI range, where the page, the MusicXML and LilyPond all have thirty-two G5-A5
        // pairs. The duration default rides along because it carries the same way: the second
        // iteration of `{ c4 d }` must be two quarters, not whatever the last note left.
        // ⚠️ `unfold` RESTARTS THE FRAME TOO, though it is engraved in full — the two facts
        // are separate. It prints N copies of one piece of music, and "play this N times"
        // is what the word was decided to mean (2026-08-17, HANDOFF §3); it is also
        // LilyPond's reading, which resolves the relative chain once and copies the RESULT.
        // Until then the page climbed and the MIDI climbed with it, so the two agreed on
        // the wrong piece: `repeat unfold 4 { g''8 a }` sounded four pairs an octave apart
        // and ran off the top of the range.
        var frame = (_currentOctave, _currentNoteName, _defaultDuration);

        for (int i = 0; i < repeatCount; i++)
        {
            if (i > 0 && ordSnapshot != null)
                _sourceOrdinals = new Dictionary<int, int>(ordSnapshot);
            if (i > 0)
                (_currentOctave, _currentNoteName, _defaultDuration) = frame;
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

        // ⚠️ THE BODY IS READ THROUGH THE STATEMENT THE PAGE READS. A phrase named in a
        // grace body is a CONTAINER, so it is expanded here exactly as it is in
        // MeasureCollector.CollectGraceNotes — the two used to disagree, and the page won:
        // MEASURED 2026-08-30 (session 301, scratch/p301/ab), `grace { G } c'4 c'2.` against
        // the same music written inline, `octave absolute` so the frames match — the SVG is
        // byte-identical with data-pos masked, and this walker's MIDI was byte-identical to
        // the book WITH NO GRACE IN IT (91 bytes against the inline book's 107). Session 300
        // taught the page and the report to expand a reference and left the sound behind, and
        // the comment on GraceBodySupport says "written once and read TWICE" while there are
        // four readers: this one, the page, GraceBodyValidator and MusicXmlExporter.
        // ⚠️ THE NARROWING BELOW IS STILL WIDER THAN THE PAGE'S, on purpose and separately:
        // a chord and a rest in a grace body SOUND here (since 2026-07-10) and are still
        // dropped by the page, which is the half of docs/HANDOFF.md §2 U8 that is open. That
        // asymmetry is about which GROBS a grace column can hold; a phrase reference names no
        // grob at all, which is why it is the one that could be closed on its own.
        int expansionBudget = Svg.Collector.MeasureCollector.DefaultExpansionBudgetCap;
        // The frame a phrase reference opens, kept per expansion so a nested one restores in
        // order. ⚠️ Only what THIS reader reads is saved: the sounding transpose, the
        // absolute-octave base, and the marks/anchor the hand-off needs. The rule is the
        // page's (docs/HANDOFF.md §1 session 300, "the boundary restores what that reader
        // reads") — the grace's own duration memory is RESET on entry and deliberately not
        // restored on exit, because CollectGraceNotes does neither.
        // ⚠️ ALLOCATED ONLY IF A REFERENCE IS ACTUALLY WRITTEN: a grace body naming a phrase
        // is rare (2 books in the whole 1754-book sweep), and this method runs once per grace
        // in the piece.
        Stack<(int Transpose, int AbsBase, int? Anchor, int Offset)>? phraseFrames = null;

        foreach (var (item, _) in Semantics.GraceBodySupport.BodyElements(
                     grace,
                     name => _phraseBodies is { } table
                             && table.TryGetValue(name, out var body) ? body : null,
                     () => expansionBudget-- > 0))
        {
            switch (item)
            {
                case Svg.Collector.RelativeResetMarker reset:
                {
                    // The same fresh frame the main stream's $reference opens (ProcessNode's
                    // VariableReferenceSyntax arm), armed from the MARKER rather than from
                    // the reference node — the expander has already read the marks and the
                    // anchor off it, and reading them twice is how the two walks would drift.
                    // ⚠️ IT IS THE SECOND SPELLING OF THAT ARM, and it cannot be folded into
                    // it: that one takes a reference node and recurses through ProcessNode,
                    // this one takes an already-flattened marker. Checklist 7.7's answer for
                    // a pair that cannot be folded is a DIFFERENTIAL net, and it is
                    // GraceNoteMidiTests.APhraseInAGraceBody_HandsThePlayedChainBackAtItsAnchor:
                    // it asks both spellings for the note after the same phrase and demands
                    // one answer.
                    int? anchor = reset.AnchorStep == Music.PhraseAnchor.Tonic
                        ? (_ambientTonic.Valid ? _ambientTonic.Step : 0)
                        : reset.AnchorStep;
                    (phraseFrames ??= new()).Push((_currentTransposeSemitones,
                        _partAbsoluteBase, anchor, reset.OctaveOffset));
                    _currentNoteName = 0;
                    _currentOctave = _partOctaveAnchor + reset.OctaveOffset;
                    _currentTransposeSemitones += PhraseTransposeSemitones();
                    _partAbsoluteBase += reset.OctaveOffset;
                    // `grace { c'16 G }` gives G's first undurated note the group's EIGHTH,
                    // not the sixteenth written before the reference — the same answer
                    // `grace { G }` gives it, so a reference cannot be told from the music
                    // it names by what it does to the next note's length.
                    written = null;
                    break;
                }

                case Svg.Collector.PhraseEndMarker:
                {
                    // A marker pair is emitted or omitted together (GraceBodySupport.Expand
                    // pays for the whole entry or none of it), so the stack cannot underflow.
                    // ⚠️ THE GUARD IS THE PAGE'S OWN SHAPE, not a fallback invented here:
                    // MeasureCollector.ExitPhraseTranspose guards all three of its saves with
                    // the same `Count > 0`. Reading the same markers with a stricter rule
                    // than the walk they were designed for is how two readers start
                    // disagreeing about a malformed book.
                    if (phraseFrames is not { Count: > 0 })
                        break;
                    var (savedTranspose, savedAbsBase, anchor, offset) = phraseFrames.Pop();
                    _currentTransposeSemitones = savedTranspose;
                    _partAbsoluteBase = savedAbsBase;
                    // The reference hands the chain back at its ANCHOR — one item, the chord
                    // rule — so a grace note written after it reads the frame it would read
                    // after one in the main stream. A pitchless body hands nothing off.
                    if (anchor is { } astep)
                    {
                        _currentNoteName = astep;
                        _currentOctave = RelativeOctave.Resolve(
                            0, _partOctaveAnchor + offset, astep, 0);
                    }
                    break;
                }

                case NoteSyntax note:
                {
                    if (note.Duration != null) written = note.Duration.ToFraction();
                    int g = GraceTicks(written);
                    int midiPitch = SoundKey(CalculateRelativeMidiPitch(note.Pitch), note.Position);
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
                        int mp = SoundKey(CalculateRelativeMidiPitch(pitch), chord.Position);
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