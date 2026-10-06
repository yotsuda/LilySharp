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

    // The part whose music is being played, stamped on every note (MidiNote.Part) so the
    // finished stream splits into a track and a channel per part (SplitIntoPartTracks).
    // Null for music no part claims. Set and cleared where _currentTimbre is.
    private string? _currentPart;

    // Phrase bodies by name; a $reference expands in place (fresh default
    // frame), declarations are silent. _activePhrases guards recursion.
    private Dictionary<string, SyntaxNode>? _phraseBodies;
    private readonly HashSet<string> _activePhrases = new();

    // Structure-driven playback: sections play in `structure { … }` order
    // (with |: :| repeats and volta alternatives), not declaration order.
    // Sections keyed by name. A name maps to a LIST because by-part grouping
    // declares the same section name once per part (`part melody { section A … }`,
    // `part bass { section A … }`); a structure reference plays them all.
    private Dictionary<string, List<SectionDeclarationSyntax>>? _sections;
    // The section HEADER registry (Semantics.SectionHeaders, the one spelling): a section's
    // own key / time / partial when it carries the directive but no inline music (by-section,
    // or a standalone by-part header `section A { key g major }`). Applied up front to every
    // part of the section, since it is not walked with the part cell's music. The meter is
    // applied at the same boundary — see Semantics.ScoreHomeMeter: the page reverts the meter
    // at every section boundary and this walk did not, so a mid-section `time 3/4` stayed in
    // the conductor track for the rest of the piece (measured 2026-08-31). The pickup arms
    // every part's first bar, as the page's does (MeasureCollector.Form.cs).
    private Semantics.SectionHeaders _sectionHeaders = Semantics.SectionHeaders.Empty;
    private bool _formDriven;
    private bool _formPlayed;

    // Part declarations by name (first-wins), built once in Export so PartTimbre /
    // PartOctaveAnchor / part-transpose lookups are O(1) instead of a full-tree
    // scan per part per section per repeat pass.
    private Dictionary<string, PartDeclarationSyntax> _partDecls = new();
    // Score-wide `transpose` default (a free-standing top-level transpose),
    // computed once; a part's own transpose overrides it.
    private (int step, int alt, int oct)? _scoreTransposeDefault;
    // Whether the file is written at concert pitch (`pitch concert`), read once per export
    // like the transpose default above — ConcertPitch.FileIsConcert.
    private bool _fileConcert;

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
    /// <summary>The velocity a note sounds at when it carries no dynamic of its own — the
    /// last dynamic written in the PART being played, else <see cref="DefaultVelocity"/>.</summary>
    private int _velocity = DefaultVelocity;
    private const int DefaultVelocity = 80;

    // The running velocity PER PART — the lane twin of _partPitchLanes for dynamics. A part's
    // lane opens at the velocity its last lane closed with (a `@p` in section A still sounds
    // in that part's section B, as the page's mark stands until the next one) and a part
    // never heard before opens at the default. Not cleared at a section boundary: a dynamic
    // is not among the things a section reopens (the octave frame and the note value are).
    // LILYPOND-REF: ly/performer-init.ly:100-103 Dynamic_performer — the performer that turns
    //   dynamics into volume is consisted in the VOICE, so each voice carries its own, and
    //   a `\p` in one staff leaves every other staff's volume where it was.
    // ⚠️ Until 2026-10-02 there was ONE running velocity for the whole export, and the lanes
    // are played one after another: a `@p` in one part leaked into whichever lane played
    // next — the other part of the same section, or the first part of the next section
    // (owner's report; Lab sessions/p748/probes/dyn: both parts at 50 where one was marked).
    private readonly Dictionary<string, int> _partVelocity = new();

    private int LaneVelocity(string part)
        => _partVelocity.TryGetValue(part, out int v) ? v : DefaultVelocity;
    private readonly Stack<(int numerator, int denominator)> _tupletStack = new();

    // The bar's context — the meter in force, the score's home meter, the pickup pending —
    // and its rules (a `time`, a section boundary, a `partial`, the bar line that spends it):
    // Semantics.BarContext, the one spelling the outputs move onto one by one (REFACTOR_PLAN
    // stage C2: this walk first). This walk counts durations, so the notes of a pickup need
    // nothing from it — the readers are MeasureTicks (what an empty `| |` bar, a bare `R` or a
    // chord row's first bar is worth) and PaddingTicks. MEASURED before the pickup arm existed
    // (session 357, scratch/p358/midi): `partial 4 | c'4 …` drew a one-beat spacer on the page
    // and sounded a FULL bar of silence here (first note at tick 1920 against 480 for the
    // `partial 4 s4 |` an author would type), both at the piece's opening and after a
    // mid-piece `partial` — the node fell to ProcessChildren and vanished. No book on disk
    // writes `partial N |` (13825 counted); the pair is kept honest by EmptyMeasureValidatorTests.
    private readonly Semantics.BarContext _bars = new();

    // Tie handling: a tie (~) merges the next same-pitch note into the previous
    // one (one sustained note) instead of re-articulating it.
    // ⚠️ The previous ONSET, not the previous NOTE. A chord is one onset with several
    // notes, so `<c e>2 ~ <c e>2` has to extend BOTH of them; while this was a single
    // index a chord could neither be tied from nor tied into, and every tied chord
    // re-articulated in the MIDI while the page drew the tie and the MusicXML wrote
    // the second chord as a tie-stop (measured 2026-08-17 on 8 of 566 books).
    // ⚠️ ONE MEMORY PER PART, and these fields are the CURRENT part's (SyncTieSlot swaps them).
    // The walk plays a form section by section, every part's block of a section before the
    // next section, so a single slot was overwritten by the other parts between a part's
    // `c~ ||` and its next section's `c` — the tie the page draws over the boundary
    // re-articulated here whenever a book had two parts (measured 2026-09-28: `c''4~ ||` into
    // `c''4` sounded 480 + 480 ticks in a two-part book, 960 in a one-part one).
    private bool _tiePending;
    private readonly List<int> _lastOnset = new();
    private MidiTrack? _lastNoteTrack;
    // The section play (_sectionPlaySerial) the remembered onset sounded in — the section
    // carry rule (Svg.Collector.SectionPlayGraph): a tie is carried into the section PLAYED
    // next, over any repeat sign or ending, and no further (a section the part does not play
    // stands between: silence).
    private int _lastOnsetSerial;
    private string? _tieOwner;
    private readonly Dictionary<string, (bool Pending, int[] Onset, MidiTrack? Track, int Serial)> _tieSlots = new();
    // Counts every section play of the form, in the order they sound (PlaySectionByName).
    private int _sectionPlaySerial;

    /// <summary>Makes the tie fields the current part's, saving the previous part's.</summary>
    private void SyncTieSlot()
    {
        string part = _currentPart ?? "";
        if (part == _tieOwner)
            return;
        if (_tieOwner != null)
            _tieSlots[_tieOwner] = (_tiePending, _lastOnset.ToArray(), _lastNoteTrack, _lastOnsetSerial);
        _lastOnset.Clear();
        if (_tieSlots.TryGetValue(part, out var slot))
        {
            _tiePending = slot.Pending;
            _lastOnset.AddRange(slot.Onset);
            _lastNoteTrack = slot.Track;
            _lastOnsetSerial = slot.Serial;
        }
        else
        {
            _tiePending = false;
            _lastNoteTrack = null;
        }
        _tieOwner = part;
    }

    /// <summary>Sets the current part's pending tie.</summary>
    private void SetTiePending(bool pending)
    {
        SyncTieSlot();
        _tiePending = pending;
    }

    /// <summary>The notes a tie arriving at this onset may extend — the previous
    /// onset's, when a tie is pending on the same track in the same section play or the one
    /// played just before it, and nothing otherwise. The
    /// caller consumes each index it uses, so a chord that sounds one pitch twice
    /// cannot extend one note twice.</summary>
    private List<int> OpenTieTargets(MidiTrack track)
    {
        SyncTieSlot();
        bool carries = _lastOnsetSerial == _sectionPlaySerial || _lastOnsetSerial + 1 == _sectionPlaySerial;
        return _tiePending && carries && _lastNoteTrack == track ? new List<int>(_lastOnset) : new List<int>();
    }

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
        SyncTieSlot();
        _lastOnset.Clear();
        _lastOnset.AddRange(indices);
        _lastNoteTrack = track;
        _tiePending = startsTie;
        _lastOnsetSerial = _sectionPlaySerial;
    }

    /// <summary>A silence (a rest, a slash) ends the tie memory both ways: no tie is pending
    /// past it, and nothing before it can be extended by a tie written after it.</summary>
    private void ForgetOnset()
    {
        SyncTieSlot();
        _tiePending = false;
        _lastOnset.Clear();
        _lastNoteTrack = null;
    }

    // Sounding-pitch transpose for the part currently being played. A part option
    // transpose: shifts every note by the interval's semitones (no respelling).
    private SyntaxNode? _root;

    /// <summary>The capo fret of the score being played (its layout's <c>chordDiagrams … capo N</c>,
    /// <see cref="_playedSpec"/>), 0 for none: a <c>chord(…)</c> item's strings sound that many
    /// semitones higher. Read once per export (<see cref="_capo"/>). Until 2026-09-29 it was the
    /// FIRST score's, whichever score was played — <c>score main "open" { … }</c> with no capo
    /// sounded the main score's capo (HANDOFF §1.0 ⒜, the capo's holes).</summary>
    private int Capo
    {
        get
        {
            if (_capo < 0)
                _capo = _root != null ? Semantics.LayoutPlanReader.ResolveFor(_root, _playedSpec).Chords.Capo : 0;
            return _capo;
        }
    }

    private int _capo = -1;
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
        .Concat(_channelWarnings)
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
    /// Read for THREE things, all "what does this score show": which part a BARE section
    /// belongs to (<see cref="_bareSectionOwner"/>), which chord rows sound
    /// (<see cref="_soundingChordRows"/>) and which parts sound (<see cref="_soundingParts"/>).
    /// Everything else this export does is read from the music itself, so a file with no
    /// <c>score</c> at all plays exactly as before.
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
        // The default tempo at tick 0; a book's own `tempo` replaces it there (SetTempo:
        // one event per moment).
        conductorTrack.SetTempo(0, BpmToMicroseconds(_tempo));
        midi.Tracks.Add(conductorTrack);

        var mainTrack = new MidiTrack { Name = "Track 1", Channel = 0 };
        _root = tree.GetRoot();
        _capo = -1;
        // Every section's voices and canonical bar count (SectionBarCounts, the semantic
        // counter): a part voice that writes fewer bars than its section-mates is padded
        // with silence when its play ends (PaddingTicks), as the page pads its staff.
        _sectionBars = Svg.Collector.SectionBarCounts.BuildSemanticIndex(_root);
        _meterPlan = Svg.Collector.SectionMeterPlan.Build(_root);
        _phraseBodies = new Dictionary<string, SyntaxNode>();
        _sections = new Dictionary<string, List<SectionDeclarationSyntax>>();
        _partDecls = new Dictionary<string, PartDeclarationSyntax>();
        var sectionsInOrder = new List<SectionDeclarationSyntax>();
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
                sectionsInOrder.Add(sd);
            }
            else if (n is PartDeclarationSyntax pd)
                _partDecls.TryAdd(pd.Name.Text, pd); // first-wins, matching the old first-match scans
        }
        _sectionHeaders = Semantics.SectionHeaders.Read(sectionsInOrder);
        _scoreTransposeDefault = PartTranspose.ReadScoreDefault(_root);
        _fileConcert = ConcertPitch.FileIsConcert(_root);
        _homeTonic = ScoreHomeKey.Read(_root);
        _ambientTonic = _homeTonic;
        _homeKeySharps = ScoreHomeKey.Sharps(_root);
        _keySharps = _homeKeySharps;
        var (homeBeats, homeBeatType) = ScoreHomeMeter.Read(_root);
        _bars.HomeMeter = new Semantics.Meter(homeBeats, homeBeatType);
        _bars.SpendPartial();
        _formDriven = _root.DescendantNodes().OfType<FormDeclarationSyntax>().Any();
        _formPlayed = false;
        _playedSpec = RenderSpecParser.PlayedSpec(tree, Score, Form);
        _bareSectionOwner = RenderSpecParser.SingleEngravedPart(tree, Score, Form);
        _soundingChordRows = SoundingChordRows(tree, Score, Form);
        _soundingParts = SoundingParts(_playedSpec);
        _partPitchLanes.Clear();
        _partVelocity.Clear();
        _velocity = DefaultVelocity;
        _sourceOrdinals = new Dictionary<int, int>();
        ProcessNode(_root, mainTrack, conductorTrack);

        // Ensure there is an initial time signature at tick 0. If the score already
        // declared one at the downbeat (ProcessTimeSignature added it), keep that and
        // do NOT insert another: seeding the running (post-processing, i.e. final)
        // value here put a spurious downbeat event on any score whose time signature
        // changes later. Only seed the default when no tick-0 signature exists.
        if (!conductorTrack.TimeSignatures.Any(ts => ts.Tick == 0))
            conductorTrack.SetTimeSignature(0, _bars.Meter.Beats, _bars.Meter.BeatType);

        // The parts the score does not show are stripped HERE, after the whole stream is
        // played and before it is split: every part walked as before, so the timeline (a
        // section's length, a lane's padding, a tie's target, the tempo and meter events) is
        // the one the page has, and only the notes of the unseen parts leave it. Stripping
        // before the split also keeps them off the channel plan.
        if (_soundingParts is { } sounding)
            mainTrack.Notes.RemoveAll(n => n.Part is { } part && !IsChordRowTrack(part) && !sounding.Contains(part));

        // A section-less file's lyric blocks were met on the root stream: sing them now.
        AttachLyrics(mainTrack, null, 0, 0);
        SplitIntoPartTracks(midi, mainTrack);

        return midi;
    }

    // One line per part the channel plan could not give a channel of its own or a same-sounding
    // channel to share (SplitIntoPartTracks), reported through Warnings.
    private readonly List<string> _channelWarnings = new();

    /// <summary>
    /// Splits the finished note stream into a track per part, each on its own channel with its
    /// General MIDI program (HANDOFF §2 F-midi).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stream is played into ONE track first and split afterwards, on purpose: ties,
    /// voices, repeats and forms are resolved against that track while it is being written
    /// (OpenTieTargets indexes its notes), and splitting the finished list leaves all of that
    /// exactly as it was. Each note already knows its part (<see cref="MidiNote.Part"/>).
    /// </para>
    /// <para>
    /// Tracks follow the order in which each part first sounds; music no part claims keeps the
    /// old "Track 1". A pitched part takes the next channel from 0, stepping over 9 (the GM
    /// drum channel); drum notes keep channel 9 inside their part's track. Past the fifteen
    /// pitched channels a part shares the channel of an earlier part with the same program —
    /// a channel carries one sound — and when there is none it shares the first channel and
    /// says so in <see cref="Warnings"/>. A drum-only track sends no program change.
    /// </para>
    /// <para>
    /// LILYSHARP-OWN: LilyPond's performer gives each staff its own channel in the same way
    /// but has no preset table; the sound a part plays is Lily#'s
    /// <see cref="Semantics.PartHeaderDefaults.MidiProgram"/>.
    /// </para>
    /// </remarks>
    private void SplitIntoPartTracks(MidiFile midi, MidiTrack mainTrack)
    {
        _channelWarnings.Clear();
        if (mainTrack.Notes.Count == 0)
            return;

        var order = new List<string?>();
        var byPart = new Dictionary<string, List<MidiNote>>(StringComparer.Ordinal);
        List<MidiNote>? unclaimed = null;
        foreach (var note in mainTrack.Notes)
        {
            if (note.Part is null)
            {
                if (unclaimed == null) { unclaimed = new List<MidiNote>(); order.Add(null); }
                unclaimed.Add(note);
                continue;
            }
            if (!byPart.TryGetValue(note.Part, out var list))
            {
                byPart[note.Part] = list = new List<MidiNote>();
                order.Add(note.Part);
            }
            list.Add(note);
        }

        // The chord rows' tracks (PlayChordRow) come AFTER every part's, whenever they first
        // sound, so a placed row never moves a part off the channel it had before rows sounded.
        order = order.Where(p => p is null || !IsChordRowTrack(p))
            .Concat(order.Where(p => p is not null && IsChordRowTrack(p)))
            .ToList();

        var channelByProgram = new Dictionary<int, int>();
        int nextChannel = 0;
        bool first = true;
        foreach (var part in order)
        {
            var notes = part is null ? unclaimed! : byPart[part];
            int program = part is null ? 0 : Header(part).MidiProgram;
            bool pitched = notes.Any(n => n.Channel != 9);

            int channel = 9;
            if (pitched)
            {
                if (nextChannel == 9)
                    nextChannel++;
                if (nextChannel <= 15)
                {
                    channel = nextChannel++;
                    channelByProgram.TryAdd(program, channel);
                }
                else if (channelByProgram.TryGetValue(program, out int shared))
                {
                    channel = shared;
                }
                else
                {
                    channel = 0;
                    _channelWarnings.Add(
                        $"part '{part ?? mainTrack.Name}' has no MIDI channel of its own: sixteen channels "
                        + $"hold at most fifteen pitched sounds, so it plays on channel 1 with that "
                        + $"channel's sound instead of \"{GeneralMidi.InstrumentNames[program]}\"");
                }
            }

            var track = new MidiTrack
            {
                Name = part ?? mainTrack.Name,
                Channel = channel,
                Program = pitched ? program : null,
            };
            foreach (var note in notes)
                track.Notes.Add(note.Channel == 9 ? note : note with { Channel = channel });
            if (first)
            {
                track.Lyrics.AddRange(mainTrack.Lyrics);
                first = false;
            }
            midi.Tracks.Add(track);
        }
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
                _currentPart = part.Name.Text;
                ProcessChildren(part, track, conductorTrack);
                (_partOctaveAnchor, _partAbsoluteBase) = (4, 4);
                _currentTimbre = 0;
                _currentPart = null;
                break;

            case SectionDeclarationSyntax sectionDecl:
                // With a structure the play order is ITS job; declarations
                // are silent (they used to play in file order regardless).
                if (!_formDriven)
                {
                    _sectionPlaySerial++; // one play, as PlaySectionByName counts them
                    PlaySection(sectionDecl, track, conductorTrack);
                }
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
                _currentTransposeSemitones = PartPlaybackShift(partBlock.Name);
                ProcessChildren(partBlock, track, conductorTrack);
                _currentTransposeSemitones = 0;
                break;

            case OctaveDirectiveSyntax octaveDir:
                // Octave-mode switch (top-level default or mid-stream). MIDI walks
                // in source order, so a file-level directive precedes the notes.
                _octaveAbsolute = octaveDir.IsAbsolute;
                break;

            case ClefDeclarationSyntax:
                // A clef is drawing only: it moves no octave frame (InstrumentDefaults.DefaultAnchorOctave).
                break;

            case CueExpressionSyntax cue:
                // A cue clef is drawing only: the body plays in the running relative frame
                // (InstrumentDefaults.DefaultAnchorOctave).
                ProcessNode(cue.Body, track, conductorTrack);
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
                SetTiePending(true);
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

            case PartialDeclarationSyntax partial:
                // Anacrusis: the bar in progress is a pickup of this length until it closes
                // (Semantics.BarContext.SetPartial has the rule and LilyPond's reference).
                _bars.SetPartial(partial.ToFraction());
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
                // Sung AFTER the section's notes exist, on their onsets (AttachLyrics); the
                // part in force now is what a block inside a part block sings.
                _sectionLyrics.Add((lyrics, _currentPart));
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
                    _pendingGraceSteal = 0; // a lane opens with no grace debt (PlaySection says why)
                    ProcessNode(voice, track, conductorTrack);
                    voicesEndTick = Math.Max(voicesEndTick, _currentTick);
                }
                _pendingGraceSteal = 0;
                _currentTick = voicesEndTick;
                // …and the music AFTER the span reads from the frame the span opened in
                // too: no branch moves it, so which branch was written last cannot matter
                // (MeasureCollector's _parallelSpans carries the same rule for the page).
                // The note-value default is part of that frame (session 398): the LAST
                // branch's last value used to leak out here while the page took voice 0's.
                _currentNoteName = startNoteName;
                _currentOctave = startOctave;
                _defaultDuration = startDuration;
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
                    int? anchorStep = LilySharp.Core.Music.PhraseAnchor.Anchor(phraseBody,
                        n => _phraseBodies!.TryGetValue(n, out var b) ? b : null, out var anchorShape);
                    if (anchorStep == LilySharp.Core.Music.PhraseAnchor.Tonic)
                        anchorStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
                    // A body opening with a chord(…) item: its lowest note, absolute.
                    var shapeAnchor = anchorStep == LilySharp.Core.Music.PhraseAnchor.Shape
                        ? ShapeAnchorOf(anchorShape) : null;
                    if (anchorStep == LilySharp.Core.Music.PhraseAnchor.Shape)
                        anchorStep = null;
                    ProcessNode(phraseBody, track, conductorTrack);
                    _currentTransposeSemitones = savedTranspose;
                    _partAbsoluteBase = savedAbsBase;
                    // Frame hand-off at the phrase's ANCHOR (matches the
                    // collector's ExitPhraseTranspose): the reference is ONE
                    // item, the chord rule — its interior never leaks, and its
                    // own marks shift what propagates, so a note after Melody'
                    // is relative to the shifted anchor. A pitchless body hands
                    // nothing off.
                    if (shapeAnchor is { } sa)
                    {
                        _currentNoteName = sa.Step;
                        _currentOctave = sa.Octave;
                    }
                    else if (anchorStep is { } astep)
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
    /// <c>partName { }</c> block around its music: BY-PART (the section is written
    /// inside <c>part X { … }</c>) and BARE (no block anywhere, the <c>score</c> says
    /// whose it is — see <see cref="_bareSectionOwner"/>). One house, because they arm the
    /// same six things and the by-part one had already grown a comment explaining that
    /// it must arm the sounding shift "too".
    /// <para>
    /// ⚠️ The part's pitch/duration lane is restored on entry and saved on exit, so
    /// concurrently played parts (one <c>PlaySection</c> call each, same structure
    /// reference) keep independent relative-octave chains WITHIN a section instead of
    /// inheriting the previous part's last note.
    /// </para>
    /// </remarks>
    /// <param name="octaveOffset">The shift the section REFERENCE that opened this play
    /// wrote (<c>~B'</c> = +1). It moves BOTH anchors — the relative one a bare letter
    /// resolves nearest to, and the absolute base — because the two octave MODES read
    /// different ones, and a shift that worked in only one would be a silent drop in the
    /// other (the collector's OctaveContext.ResetForSection moves the same pair).</param>
    private void PlayInPart(string partName, Action body, int octaveOffset = 0)
    {
        var (anchor, absBase) = PartOctaveAnchors(partName);
        anchor += octaveOffset;
        absBase += octaveOffset;
        var pitch = _partPitchLanes.TryGetValue(partName, out var saved)
            ? saved
            : (NoteName: 0, Octave: anchor, Dur: Fraction.Quarter);
        _currentNoteName = pitch.NoteName;
        _currentOctave = pitch.Octave;
        _defaultDuration = pitch.Dur;
        _velocity = LaneVelocity(partName);
        _partOctaveAnchor = anchor;
        _partAbsoluteBase = absBase;
        _currentTimbre = PartTimbre(partName);
        var outerPart = _currentPart;
        _currentPart = partName;
        // Music without a PartBlockSyntax around it never passes the arming in ProcessNode,
        // so the part's shift is armed here — otherwise a bass or guitar sounded at
        // written pitch. ⚠️ The SAME shift as the part-block arm, transpose included: until
        // 2026-09-03 this line armed the sounding shift alone, so a by-part book's
        // `transpose` (and, with it, a concert-pitch file's instrument shift) reached the
        // page and not the .mid — measured on `part x { transpose d section A { c1 | } }`,
        // which played C4 where the by-section spelling of the same book played D4.
        _currentTransposeSemitones = PartPlaybackShift(partName);
        body();
        _partPitchLanes[partName] = (_currentNoteName, _currentOctave, _defaultDuration);
        _partVelocity[partName] = _velocity;
        (_partOctaveAnchor, _partAbsoluteBase) = (4, 4);
        _currentTimbre = 0;
        _currentTransposeSemitones = 0;
        _currentPart = outerPart;
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

    /// <summary>
    /// Plays one section: its part blocks run SIMULTANEOUSLY (each from the
    /// section's start tick; the section ends with the longest part), and each
    /// part reopens the octave frame and the note value the section starts at.
    /// </summary>
    private void PlaySection(SectionDeclarationSyntax section, MidiTrack track, MidiTrack conductorTrack,
        int octaveOffset = 0)
    {
        // The lyric blocks met in this section are sung onto its notes once the play is
        // done, whichever of PlaySectionCore's exits it leaves by (AttachLyrics).
        int notesBefore = track.Notes.Count;
        int sectionStart = _currentTick;
        var outerLyrics = _sectionLyrics;
        _sectionLyrics = new List<(LyricsBlockSyntax, string?)>();
        PlaySectionCore(section, track, conductorTrack, octaveOffset);
        AttachLyrics(track, section, notesBefore, sectionStart);
        _sectionLyrics = outerLyrics;
    }

    private void PlaySectionCore(SectionDeclarationSyntax section, MidiTrack track, MidiTrack conductorTrack,
        int octaveOffset)
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

        // A section's own header key — stated beside the part blocks (by-section) or
        // in a standalone by-part header — is not walked with the part cell's music,
        // so apply it up front (overriding the home reset) for every part of the section.
        if (_sectionHeaders.Keys.TryGetValue(section.SectionName, out var headerKey))
        {
            _ambientTonic = KeyTonic.Of(headerKey);
            _keySharps = headerKey.IsCustom ? 0 : KeySpelling.SharpsFor(
                headerKey.Pitch.ToFullString().Trim().ToLowerInvariant(),
                headerKey.Mode.Text.ToLowerInvariant()) ?? 0;
        }

        // ⚠️ THE METER AND THE PICKUP ARE THE SAME QUESTION, asked of the same registry
        // (Semantics.BarContext.OpenSection has the rule): the section's own `time` if it
        // states one, the SCORE meter if it does not — the revert is what this walk was
        // missing: a mid-section change stayed in the conductor track for every later section,
        // so the bar grid a DAW draws parted from the page after the first meter change — and
        // the header's `partial` as every part's first bar's pickup (the page shortens every
        // part's first bar with it, MeasureCollector.Form.cs). The event is written only when
        // the pair actually moves, so a boundary that changes nothing adds none
        // (ProcessTimeSignature is the other writer). Every part walks the section, so the
        // conductor track takes the event ONCE per tick (MidiTrack.SetTimeSignature).
        if (_bars.OpenSection(_sectionHeaders.Times.GetValueOrDefault(section.SectionName),
                _sectionHeaders.Partials.GetValueOrDefault(section.SectionName)))
            conductorTrack.SetTimeSignature(_currentTick, _bars.Meter.Beats, _bars.Meter.BeatType);

        // A by-part CHORD TRACK's section (`chords harmony { section A { … } }`): its
        // entries sound when the score places the row (PlayChordRow), and take their bars'
        // time; a row the score does not place sounds nothing and takes none, as before.
        if (section.Parent is ChordPartBlockSyntax chordTrack)
        {
            if (chordTrack.PartName is { } row && _soundingChordRows.Contains(row))
                _currentTick = PlayChordRow(Svg.Collector.ChordNameCollector.SectionItems(section),
                    row, _currentTick, track);
            return;
        }

        // The section's own chord-track cells (`section A { … chords harmony { C | F | } }`)
        // sound from its start, before any lane is played — whichever of the paths below
        // plays the music (a bare section's owner walks the children and never meets them
        // as cells) — and a row longer than every part (a chords-only section) still takes
        // its bars.
        int rowsEnd = PlaySectionChordRows(section, track);

        // By-part grouping: the section lives INSIDE its part — arm that
        // part's anchor and play the children sequentially.
        for (var p = section.Parent; p != null; p = p.Parent)
        {
            if (p is PartDeclarationSyntax owner)
            {
                PlayInPart(owner.Name.Text, () => ProcessChildren(section, track, conductorTrack),
                    octaveOffset);
                return;
            }
        }

        // A section that declares no part block at all is music the SCORE attributes, not
        // the music (see _bareSectionOwner). Played inside that part exactly as a
        // by-part section is played inside the part that contains it.
        if (_bareSectionOwner is { } bareOwner && !SectionHasPartBlock(section))
        {
            PlayInPart(bareOwner, () => ProcessChildren(section, track, conductorTrack),
                octaveOffset);
            _currentTick = Math.Max(_currentTick, rowsEnd);
            return;
        }

        int sectionStart = _currentTick;
        int sectionEnd = rowsEnd;
        var tickLanes = new Dictionary<string, int>();
        // The section's pickup belongs to EVERY part's first bar, as on the page (each part's
        // MeasureBuilder is armed at the boundary). One lane spends it at its first bar line,
        // so it is re-armed per lane: a second part used to open with a FULL bar — partial.lys
        // (`partial 2`, a melody and an empty `X { | | | }`) ran its section a bar and a half
        // past the page's 4,800 ticks.
        var sectionPickup = _bars.Partial;
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child == null || child is SyntaxTokenNode)
                continue;
            if (child is PartBlockSyntax sectionPart)
            {
                string pname = sectionPart.Name;
                var (anchor, absBase) = PartOctaveAnchors(pname);
                // The reference's marks move both anchors here too — the by-section
                // twin of PlayInPart's line, and the reason it is written twice is that
                // this loop arms one lane PER PART BLOCK rather than one for the section.
                anchor += octaveOffset;
                absBase += octaveOffset;
                var pitch = _partPitchLanes.TryGetValue(pname, out var saved)
                    ? saved
                    : (NoteName: 0, Octave: anchor, Dur: Fraction.Quarter);
                _currentTick = tickLanes.TryGetValue(pname, out int lt) ? lt : sectionStart;
                _currentNoteName = pitch.NoteName;
                _currentOctave = pitch.Octave;
                _defaultDuration = pitch.Dur;
                _velocity = LaneVelocity(pname);
                _partOctaveAnchor = anchor;
                _partAbsoluteBase = absBase;
                _currentTimbre = PartTimbre(pname);
                var outerPart = _currentPart;
                _currentPart = pname;
                // A lane opens with no grace debt: a grace that closed the PREVIOUS lane with
                // nothing after it to steal from used to take its ticks off this lane's first
                // note (MEASURED, sessions/p398/probes/r14/grace-steal-lane: the second part's
                // first crotchet 453 ticks, its whole section 27 short). Such a trailing
                // grace's steal is simply dropped — the note it would have shortened is in
                // another voice, or does not exist.
                _pendingGraceSteal = 0;
                if (!tickLanes.ContainsKey(pname))
                    _bars.Partial = sectionPickup;
                ProcessNode(sectionPart, track, conductorTrack);
                _pendingGraceSteal = 0;
                _currentPart = outerPart;
                _partPitchLanes[pname] = (_currentNoteName, _currentOctave, _defaultDuration);
                _partVelocity[pname] = _velocity;
                _currentTick += PaddingTicks(sectionPart);
                tickLanes[pname] = _currentTick;
                sectionEnd = Math.Max(sectionEnd, _currentTick);
                (_partOctaveAnchor, _partAbsoluteBase) = (4, 4);
            }
            else if (child is ChordPartBlockSyntax)
            {
                // Played above, from the section's start (PlaySectionChordRows).
            }
            else
            {
                ProcessNode(child, track, conductorTrack);
                sectionEnd = Math.Max(sectionEnd, _currentTick);
            }
        }
        _currentTick = sectionEnd;
    }

    private void PlaySectionByName(string name, MidiTrack track, MidiTrack conductorTrack,
        int octaveOffset = 0)
    {
        // A structure reference to a by-part section name plays EVERY part's
        // copy of it concurrently — each from the shared start tick on its own
        // lane — not just the last-declared one (which silently dropped every
        // earlier part, and yielded no notes at all when a chords part was
        // declared last). By-section names map to a single-element list.
        // One section play of the form, whichever parts write it (a tie carries into the
        // next play only — see _sectionPlaySerial).
        _sectionPlaySerial++;
        if (_sections == null || !_sections.TryGetValue(name, out var sections))
            return;
        int start = _currentTick;
        int end = start;
        foreach (var section in sections)
        {
            _currentTick = start;
            _pendingGraceSteal = 0; // each part's copy is a lane of its own (PlaySection says why)
            PlaySection(section, track, conductorTrack, octaveOffset);
            // A by-part cell (or a bare section the score attributes to one part) is one
            // part's voice: pad it to the section's canonical bar count. A by-section
            // declaration pads each of its part blocks inside PlaySection; a chord-track
            // cell sounds nothing and pads nothing — its bars still count toward the
            // canonical length, which is what the parts are padded to.
            if (section.Parent is PartDeclarationSyntax
                || (_bareSectionOwner != null && !SectionHasPartBlock(section)))
                _currentTick += PaddingTicks(section);
            end = Math.Max(end, _currentTick);
        }
        _currentTick = end;
    }

    // The book's section voices (SectionBarCounts.BuildSemanticIndex), read once per Export.
    private Svg.Collector.SectionBarCounts.SemanticIndex _sectionBars = new();

    /// <summary>
    /// The silence that brings one part voice's play of a section up to the section's
    /// canonical bar count, in ticks: one bar of the meter in force per missing bar — the
    /// full-measure spacers the page pads the short staff with (MeasureCollector's section
    /// padding). Without it a one-bar melody A beside a two-bar chord row A played B a bar
    /// early (scratch/ベースタブLy/tooLongChords.lys: B at bar 2, the page at bar 3; MEASURED
    /// 2026-09-10). Part-against-part already aligned through the lanes (the section's end is
    /// the longest lane) — this is what a chord row, which has no lane, adds.
    /// </summary>
    /// <remarks>Each missing bar is worth the score's meter at that bar of the section
    /// (<see cref="Svg.Collector.SectionMeterPlan"/>, HANDOFF §2 F-partmeter ⒜) when another
    /// part's <c>time</c> stands there — a 4/4 bar padding a part short of a 3/4 section ran
    /// the section a quarter past its longest lane.</remarks>
    private int PaddingTicks(SyntaxNode voice)
    {
        int missing = _sectionBars.Missing(voice, out _);
        if (missing <= 0)
            return 0;
        var key = voice is MusicBlockSyntax { Parent: PartBlockSyntax block } ? block : voice;
        if (_meterPlan.IsEmpty || !_sectionBars.ByContainer.TryGetValue(key, out var written))
            return missing * FractionToTicks(_bars.MeterLength);
        int ticks = 0;
        for (int bar = written.Bars; bar < written.Bars + missing; bar++)
            ticks += FractionToTicks(_meterPlan.MeterAt(written.SectionName, bar) is { IsSenzaMisura: false } t
                ? DurationCalculator.ParseTimeSignature(t.Beats, t.BeatType) : _bars.MeterLength);
        return ticks;
    }

    // The meters other parts write into each section's bars (SectionMeterPlan), read once per Export.
    private Svg.Collector.SectionMeterPlan _meterPlan = Svg.Collector.SectionMeterPlan.Empty;

    /// <summary>Sounds every chord-track cell written directly in <paramref name="section"/>
    /// whose row the score places, each from the current tick (the section's start), and
    /// returns where the longest ends — the current tick when there is none. The tick is
    /// left where it was: the lanes start from it.</summary>
    private int PlaySectionChordRows(SectionDeclarationSyntax section, MidiTrack track)
    {
        int start = _currentTick, end = _currentTick;
        for (int i = 0; i < section.SlotCount; i++)
            if (section.GetChild(i) is ChordPartBlockSyntax { HasSections: false, PartName: { } row } cell
                && _soundingChordRows.Contains(row))
                end = Math.Max(end, PlayChordRow(cell.Items, row, start, track));
        return end;
    }

    // The chord rows the exported score PLACES, read once per Export (SoundingChordRows).
    private HashSet<string> _soundingChordRows = new(StringComparer.Ordinal);

    // The score being PLAYED (RenderSpecParser.PlayedSpec: Score, else the one engraving Form,
    // else the file's first), read once per Export; null for a file with no score.
    private RenderSpec? _playedSpec;

    // The parts the exported score SOUNDS, read once per Export (SoundingParts); null when
    // the file declares no score — then every part sounds, as it always did.
    private HashSet<string>? _soundingParts;

    /// <summary>
    /// The parts the score being exported sounds — the ones it engraves and its bare MIDI-only
    /// rows (<see cref="RenderSpec.SoundingPartNames"/>) — or null for a file with no score.
    /// A part the score neither shows nor names is a sketch to it, exactly as a chord row it
    /// does not place is (<see cref="SoundingChordRows"/>): until 2026-09-29 the preview's Play
    /// of <c>score main "p2" { staff p2 }</c> sounded every part of the file, p1 included.
    /// </summary>
    private static HashSet<string>? SoundingParts(RenderSpec? played)
        => played is { } spec
            ? new HashSet<string>(spec.SoundingPartNames, StringComparer.Ordinal)
            : null;

    /// <summary>True when <paramref name="part"/> sounds in this export: every part of a file
    /// with no score, else the ones the score shows or names.</summary>
    private bool PartSounds(string part) => _soundingParts == null || _soundingParts.Contains(part);

    // The track name a chord row's notes carry ("NAME (chords)", PlayChordRow) — what tells
    // them apart from a part's notes in the one stream (SplitIntoPartTracks, the strip).
    private const string ChordRowTrackSuffix = " (chords)";

    private static bool IsChordRowTrack(string part) => part.EndsWith(ChordRowTrackSuffix, StringComparison.Ordinal);

    /// <summary>
    /// The chord tracks the score being exported places — its <c>chords NAME</c> rows and
    /// the <c>staff … with chords NAME</c> attachments. Only these sound (owner decision
    /// 2026-09-25): a chord track no score shows is a sketch, not part of the piece. The
    /// score is <see cref="Score"/>, else the one engraving <see cref="Form"/>, else the
    /// file's first — the one the page draws by default.
    /// </summary>
    private static HashSet<string> SoundingChordRows(SyntaxTree tree, RenderSpec? score,
        FormDeclarationSyntax? form)
        // ONE HOME with the MusicXML's harmonies (RenderSpecParser.PlacedChordRows, 2026-09-29).
        => RenderSpecParser.PlacedChordRows(RenderSpecParser.PlayedSpec(tree, score, form));

    /// <summary>
    /// Sounds one chord row's bars from <paramref name="startTick"/> and returns where they
    /// end. Each bar is split by the SAME grouping the page and the LilyPond twin read
    /// (<see cref="Svg.Collector.ChordNameCollector.SlotGroups"/>), so a symbol sounds exactly
    /// the span the page prints it over; a `.` holds it, a rest (N.C.) is silence, and every
    /// written symbol strikes again — a chord repeated across a bar line sounds twice. The
    /// pitches are the WINDOW voicing (<see cref="Music.ChordVoicing"/>), the one the hover
    /// lists, on a track of the row's own ("NAME (chords)", the default sound) at 70% of the
    /// velocity in force so the row accompanies rather than leads.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (owner decision 2026-09-25, HANDOFF §1.1 第625): LilyPond engraves a
    /// ChordNames context and performs nothing for it. A pickup bar keeps the full meter's
    /// grid and cuts what runs past its end. Not handled: a chord track written flat at the
    /// top level (no book on disk does it).
    /// </remarks>
    private int PlayChordRow(IEnumerable<SyntaxNode> items, string rowName, int startTick, MidiTrack track)
    {
        string part = rowName + ChordRowTrackSuffix;
        int timbre = PartTimbre(part);
        // The row's OWN lane: a row writes no dynamic, so this is the default — not whatever
        // the part lane played before it left in _velocity (the leak the per-part lanes close).
        int velocity = Math.Max(1, LaneVelocity(part) * 7 / 10);
        int barTicks = FractionToTicks(_bars.MeterLength);
        // A section's pickup shortens the row's FIRST bar as it shortens every part's (the
        // page's pickup measure): amazing-grace's row opens with an empty `|` over the
        // one-beat pickup, and a full bar there played every later bar a bar late and ran the
        // section past its parts.
        int thisBarTicks = FractionToTicks(_bars.BarLength);
        int tonicStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
        int barStart = startTick;
        var bar = new List<SyntaxNode>();

        void Sound()
        {
            if (bar.Count == 0)
                return;
            foreach (var (node, timing, duration) in
                     Svg.Collector.ChordNameCollector.SlotGroups(bar, _bars.Meter.Beats, _bars.Meter.BeatType, out _))
            {
                if (node is not ChordEntrySyntax entry
                    || Svg.Collector.ChordNameCollector.StructureOf(entry.SymbolText, tonicStep, _keySharps)
                        is not { } chord)
                    continue;
                int onset = barStart + FractionToTicks(timing);
                int length = Math.Min(FractionToTicks(duration), barStart + thisBarTicks - onset);
                if (length < 1)
                    continue;
                int position = entry.SourceStart;
                int ordinal = NextOrdinal(position);
                foreach (var tone in Music.ChordVoicing.Window(chord))
                    track.Notes.Add(new MidiNote(track.Channel, SoundKey(tone.Midi, position), velocity,
                        onset, length, position, SourceOrdinal: ordinal, Timbre: timbre, Part: part));
            }
            bar.Clear();
        }

        void Close()
        {
            Sound();
            barStart += thisBarTicks;
            thisBarTicks = barTicks;
        }

        // The page's walk of a placed row (ChordNameCollector.CollectPart): every bar line
        // closes a bar — a `|` opening the run closes an EMPTY one — except `|:`, which only
        // closes what is pending and opens the next.
        foreach (var item in items)
        {
            if (item is BarlineSyntax barline)
            {
                if (Svg.Collector.MeasureCollector.ParseBarlineType(barline.BarText) != Svg.Model.BarlineType.RepeatStart
                    || bar.Count > 0)
                    Close();
                continue;
            }
            if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
                bar.Add(item);
        }
        if (bar.Count > 0)
            Close();
        return barStart;
    }

    /// <summary>
    /// Plays sections in structure order. `|: … :|` bodies play twice (or
    /// once per volta alternative); display relabels ("A2") are visual and skipped.
    /// The jump texts (D.C., D.S., al fine, al coda) are FOLLOWED, along the route
    /// <see cref="Semantics.FormRoute"/> reads off the form (2026-10-03; until then they
    /// were visual too): a replayed stretch restores the state the piece had at its target
    /// — the beginning, or the segno — exactly as a one-sided ':|' restores the beginning's.
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
        // The item spellings are read ONCE, by FormWalk (a silent `~Name` is a
        // SectionRef like any other — the bite this method's remark records), and the
        // ROUTE through them once, by FormRoute.
        var items = FormWalk.Read(structure);
        var route = Semantics.FormRoute.Of(items);

        // The state a replay has to put back, keyed by the item it starts from: the
        // beginning's, captured before anything plays, and each segno's, captured as the
        // first pass meets the sign. FormRoute sends a replay only to the beginning or to
        // the item after a segno the first pass has met, so the key is always there.
        var states = new Dictionary<int, PieceState> { [0] = CaptureState() };

        foreach (var stretch in route)
        {
            if (stretch.Replay)
                RestoreState(states[stretch.From]);
            for (int i = stretch.From; i < stretch.To; i++)
            {
                var item = items[i];
                if (stretch.Replay)
                {
                    // The D.C./D.S. pass: a repeat block plays once, on its last pass (the
                    // performer's convention), and a one-sided ':|' rewinds nothing — one
                    // rewind per written sign, as inside RepeatFromTheBeginning's stretch.
                    if (item is FormWalk.Repeat replayed)
                        PlayRepeatBlock(replayed, track, conductorTrack, lastPassOnly: true);
                    else if (item is not FormWalk.LoneRepeatEnd)
                        PlayFormItem(item, track, conductorTrack);
                    continue;
                }
                if (Semantics.FormRoute.IsSegno(item))
                    states[i + 1] = CaptureState();
                // A ':|' written in the form outside any '|: … :|' block: it has no '|:' to
                // pair with, so it repeats FROM THE BEGINNING OF THE PIECE (user decision,
                // 2026-08-15) — the ordinary reading of a one-sided end-repeat, and the one
                // MusicXML already spells (a backward repeat with no forward one). Replay
                // everything before it, once.
                if (item is FormWalk.LoneRepeatEnd)
                    RepeatFromTheBeginning(items, i, track, conductorTrack, states[0]);
                else
                    PlayFormItem(item, track, conductorTrack);
            }
        }
    }

    /// <summary>The state a replay puts back: the same three things PlayRepeatBlock /
    /// ProcessRepeatSpan already restore per pass, plus the per-part pitch lanes, which those
    /// two do not touch because a '|: … :|' body never rewinds past its own start.</summary>
    private sealed record PieceState(
        Dictionary<int, int> Ordinals,
        Dictionary<string, (int, int, Fraction)> PitchLanes,
        Dictionary<string, int> VelocityLanes,
        Fraction Duration,
        int Velocity);

    private PieceState CaptureState() => new(
        new Dictionary<int, int>(_sourceOrdinals),
        new Dictionary<string, (int, int, Fraction)>(_partPitchLanes),
        new Dictionary<string, int>(_partVelocity),
        _defaultDuration,
        _velocity);

    /// <summary>Puts <paramref name="state"/> back, so the replayed stretch sounds as it did
    /// the first time. The replayed music is ENGRAVED once, so its printed copies are the ones
    /// already laid out — the ordinals restart from the snapshot, exactly as a '|: … :|' second
    /// pass does (see PlayRepeatBlock).</summary>
    private void RestoreState(PieceState state)
    {
        _sourceOrdinals = new Dictionary<int, int>(state.Ordinals);
        _partPitchLanes.Clear();
        foreach (var kv in state.PitchLanes)
            _partPitchLanes[kv.Key] = kv.Value;
        _partVelocity.Clear();
        foreach (var kv in state.VelocityLanes)
            _partVelocity[kv.Key] = kv.Value;
        _defaultDuration = state.Duration;
        _velocity = state.Velocity;
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
                PlaySectionByName(s.Name, track, conductorTrack, s.OctaveOffset);
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
                foreach (var s in e.Sections)
                    PlaySectionByName(s.Name, track, conductorTrack, s.OctaveOffset);
                break;
            // A one-sided ':|' only rewinds on the FIRST pass (PlayForm's loop); inside
            // a replayed stretch it does NOT rewind again — that would not terminate.
            // One rewind per written ':|' is what the sign says. Display relabels and
            // anything else are visual and skipped — the navigation marks included: WHERE
            // a jump text sends the walk is PlayForm's (FormRoute), not this switch's, and
            // the signs (segno, coda) are the points it comes back to.
        }
    }

    /// <summary>
    /// Replays form items <c>[0, upTo)</c> — the piece from its beginning up to the
    /// one-sided <c>:|</c> — restoring the state the piece started from so the second pass
    /// sounds like the first.
    /// </summary>
    private void RepeatFromTheBeginning(IReadOnlyList<FormWalk.Item> items, int upTo,
        MidiTrack track, MidiTrack conductorTrack, PieceState piece)
    {
        RestoreState(piece);
        for (int j = 0; j < upTo; j++)
            PlayFormItem(items[j], track, conductorTrack);
    }

    /// <param name="lastPassOnly">True on a D.C./D.S. replay (PlayForm): the block plays once,
    /// on its last pass — the body, then the ending that pass names.</param>
    private void PlayRepeatBlock(FormWalk.Repeat repeatBlock, MidiTrack track, MidiTrack conductorTrack,
        bool lastPassOnly = false)
    {
        // The body carries each reference's OWN octave shift, not just its name: `|: ~A ~A' :|`
        // is two different plays of one section and the list has to keep them apart.
        // A ':|:' INSIDE the block closes one repeat and opens the next (`|: B :|: C :|` is
        // `|: B :| |: C :|` — the page's ProcessRepeatBlock, the twin's AppendRepeatBlock and
        // MusicXML's EmitPlainRepeatBlock all split there), so the children are RUNS, each
        // played as its own repeat with its own endings. MEASURED 2026-09-10 (session 363):
        // this walk read the block as one body and sounded B C B C where the other three
        // readers give B B C C — no book on disk writes the divider inside a block (925
        // scanned), so the observers are FormRepeatBarlineTests' own.
        var body = new List<(string Name, int OctaveOffset)>();
        // One entry per ENDING: the passes its bracket names and the sections it plays in
        // order ([1-2. C D] is {1, 2} and C then D).
        var alternatives = new List<(PassSet Passes, List<(string Name, int OctaveOffset)> Sections)>();
        foreach (var child in repeatBlock.Children)
        {
            switch (child)
            {
                // …a silent reference counts inside a repeat body too. See PlayForm's remark.
                case FormWalk.SectionRef s:
                    body.Add((s.Name, s.OctaveOffset));
                    break;
                case FormWalk.Ending e:
                    alternatives.Add((PassSet.Of(e.Node.Numbers),
                        e.Sections.Select(s => (s.Name, s.OctaveOffset)).ToList()));
                    break;
                case FormWalk.BothBar:
                    PlayRepeatRun(repeatBlock, body, alternatives, track, conductorTrack, lastPassOnly);
                    body = new List<(string Name, int OctaveOffset)>();
                    alternatives = new List<(PassSet, List<(string Name, int OctaveOffset)>)>();
                    break;
            }
        }
        PlayRepeatRun(repeatBlock, body, alternatives, track, conductorTrack, lastPassOnly);
    }

    /// <summary>One <c>|: body [endings] :|</c> run of a form repeat block — the whole block
    /// when it holds no <c>:|:</c>. The written <c>:|*N</c> is the block's and applies to
    /// every run, as the LilyPond twin writes it on each run's close.</summary>
    private void PlayRepeatRun(FormWalk.Repeat repeatBlock,
        List<(string Name, int OctaveOffset)> body,
        List<(PassSet Passes, List<(string Name, int OctaveOffset)> Sections)> alternatives,
        MidiTrack track, MidiTrack conductorTrack, bool lastPassOnly = false)
    {
        if (body.Count == 0 && alternatives.Count == 0)
            return;
        // The SAME rule the music stream plays by (ProcessRepeatSpan, RepeatPasses): an
        // explicit `:|*N` wins, else the highest pass an ending names, else 2 — and on pass p
        // the ending whose numbers name p.
        // ⚠️ THIS ARM USED TO READ NEITHER — it was `Math.Max(2, alternatives.Count)`, so a
        // form's `:|*3` was silently dropped and the piece sounded twice while the same music
        // written inline sounded three times (MEASURED 2026-08-31: 24 note-ons against 16, on
        // one book spelled both ways). The page draws no count either way, and the LilyPond
        // twin has always honoured it (LilyPondExporter.AppendRepeatBlock), so MIDI was the
        // one reader of the four that disagreed — and only about the FORM spelling, which is
        // why it survived: until LYS1034 an author could write the count in the music, where
        // it worked. Nineteen books on disk write `:|*N`.
        // ⚠️ AND UNTIL 2026-09-29 IT READ NO ENDING NUMBER EITHER (HANDOFF 第663 ⑾): the i-th
        // written ending played on pass i and the count was the number of endings, so
        // `|: A [1-2. B] :| [3. C]` sounded A B A C while the same music written inline
        // sounded A B A B A C (FormEndingPassTests).
        var endingPasses = new List<PassSet>(alternatives.Count);
        foreach (var alternative in alternatives)
            endingPasses.Add(alternative.Passes);
        int passes = RepeatPasses.Count(repeatBlock.ExplicitPlayCount, endingPasses);
        // A structure repeat is engraved once (repeat barlines): later passes
        // revisit the same printed BODY copy, so the body's ordinals restart from
        // this snapshot each pass (the highlight re-lights the same printed body).
        var structOrdSnapshot = new Dictionary<int, int>(_sourceOrdinals);
        // On a D.C./D.S. replay the run is heard once, as its LAST pass: the body and the
        // ending that pass names (`|: A [1. B] :| [2. C]` replays A C) — the performer's
        // convention that repeats are not taken after the jump (FormRoute).
        for (int pass = lastPassOnly ? passes : 1; pass <= passes; pass++)
        {
            // A tie is carried to whatever is PLAYED next — back to the body at a new pass,
            // into this pass's ending — as the page draws it (SectionPlayGraph).
            if (pass > 1)
                _sourceOrdinals = new Dictionary<int, int>(structOrdSnapshot);
            foreach (var (name, bodyOctave) in body)
                PlaySectionByName(name, track, conductorTrack, bodyOctave);
            int ending = RepeatPasses.EndingFor(pass, endingPasses);
            if (ending >= 0)
            {
                // Each ENDING, unlike the body, is a distinct printed copy laid out
                // in written order after the body. When the endings reuse the body's
                // section they share its source positions, so the body's per-pass
                // ordinal restart would otherwise map every ending onto the FIRST
                // ending's printed copy (highlighting ending 1 each pass, never
                // ending 2). Advance the body's positions by the ENDING's index — not
                // the pass: a ranged `[1-2. B]` is ONE printed copy played twice — so
                // this pass's ending resolves to its OWN printed copy. Only positions
                // the body just bumped are advanced, leaving intro/outro sections intact.
                if (ending > 0)
                {
                    foreach (var key in new List<int>(_sourceOrdinals.Keys))
                    {
                        structOrdSnapshot.TryGetValue(key, out int before);
                        if (_sourceOrdinals[key] > before)
                            _sourceOrdinals[key] += ending;
                    }
                }
                // An ending's sections play in order, as one stretch of the pass.
                foreach (var (name, endingOctave) in alternatives[ending].Sections)
                    PlaySectionByName(name, track, conductorTrack, endingOctave);
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
        // A part that NAMES its sound — a preset or a `midiInstrument` — takes the preview timbre
        // from the General MIDI program the .mid gives it, so the preview and the file cannot
        // disagree (the substring guess below read `double-bass` and `piano-bass` as a bass
        // guitar). A part that names none keeps the guess from its name: "flute" sounds
        // flute-ish without any property.
        var header = Header(partName);
        if (header.MidiInstrument != null
            || LilySharp.Core.Svg.Model.InstrumentDefaults.GetMidiProgram(header.Preset) != null)
            return GeneralMidi.PreviewTimbreFamily(header.MidiProgram);
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
        // ⚠️ THE REEDS COME BEFORE THE VOICES. These are substring tests read in order,
        // and every saxophone is named after a voice range: 'alto-sax' matched "alto" and
        // played as a CHOIR before this line moved up. The voice test is the general one,
        // so it goes last among the families that can collide with it.
        if (Has("oboe", "bassoon", "sax")) return 2;
        if (Has("voice", "soprano", "alto", "tenor", "bariton", "choir", "vocal", "upper", "lower")) return 8;
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

    /// <summary>
    /// Everything between the letters a part is written with and the pitch the .mid plays:
    /// the part's <c>transpose</c> (its own option, else the file default, with a
    /// concert-pitch file's instrument shift composed in — the same answer as
    /// <c>PartTranspose.Read(root, name)</c>, from the cached declaration and without the
    /// per-call tree scan), plus the instrument's SOUNDING shift (bass 8vb, guitar treble_8,
    /// piccolo 8va, a clarinet's −2). Both move the played pitch, so the .mid sounds what
    /// the instrument really produces — matching the tab.
    /// </summary>
    /// <remarks>
    /// ⚠️ For a concert-pitch file the two halves CANCEL for a chromatic transposer — an
    /// alto saxophone's <c>c'</c> is printed <c>a'</c> (+9) and sounds <c>c'</c> (−9) — and
    /// the cancellation happens exactly once because the instrument shift enters through
    /// the transpose reader and nowhere else (ConcertPitch's remarks).
    /// </remarks>
    private int PartPlaybackShift(string partName)
    {
        var decl = _partDecls.TryGetValue(partName, out var pd) ? pd : null;
        var written = (decl != null ? PartTranspose.Read(decl) : null) ?? _scoreTransposeDefault;
        var transpose = PitchTransposer.Compose(ConcertPitch.InputShift(_fileConcert, decl), written);
        return (transpose is { } t ? PitchTransposer.IntervalSemitones(t.step, t.alt, t.oct) : 0)
            + PartSoundingShift(partName);
    }

    /// <summary>The part's octave anchor, resolved the way the page resolves it, so a bare
    /// <c>c</c> sounds at the octave it prints.</summary>
    /// <remarks>
    /// <c>octave N</c> &gt; preset &gt; 4 — the clef is not a step (user decision 2026-09-15).
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
                bool timePassed = _currentTick > boundaryTick;
                if (pairsHere && _currentTick <= boundaryTick && boundaryClaimed)
                {
                    _currentTick += MeasureTicks();  // the second of a `| |` pair
                }
                // The bar behind this barline is closed (time passed) or was the empty bar
                // just paid for: either way a pending pickup is SPENT and the meter is back
                // in force — the page's MeasureBuilder.ResetPerMeasureState at every measure close.
                // A typed barline on an empty span decorates and closes nothing, so it leaves
                // the pickup pending, as it leaves the bar open on the page.
                if (pairsHere || timePassed)
                    _bars.SpendPartial();
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
            // repeat barline spell one. Since 2026-08-31 a repeat barline in section music is
            // an ERROR anyway (LYS1034, RepeatStructureScopeValidator: repeats are written in
            // the form); this arm only keeps an error book's MIDI whole.
            ProcessNode(items[i], track, conductorTrack);
        }
    }

    /// <summary>
    /// Plays a <c>|: … :|</c> span: the common body N times, selecting the matching
    /// inline volta ending (<c>[1. …] [2. …]</c>) on each pass. N comes from an
    /// explicit <c>:|*N</c>, else the highest volta number, else the default 2
    /// (<see cref="RepeatPasses"/> — the rule the form's readers share since 2026-09-29).
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
        // The rule every reader plays a run by (RepeatPasses): the written `:|*N`, else the
        // highest pass an ending names (at least two), else 2; on pass p the ending naming p.
        var endingPasses = new List<PassSet>(endings.Count);
        foreach (var e in endings)
            endingPasses.Add(PassSet.Of(e.Numbers));
        int count = RepeatPasses.Count(endBar?.HasExplicitRepeatCount == true ? endBar.RepeatCount : null, endingPasses);

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

            int ending = RepeatPasses.EndingFor(pass, endingPasses);
            if (ending >= 0)
                ProcessSequence(endings[ending].Items.ToList(), track, conductorTrack);
        }

        return last;
    }

    private static bool IsRepeatBar(SyntaxNode node, SyntaxKind kind)
        => node is BarlineSyntax b && b.BarToken.Kind == kind;

    /// <summary>One measure of the meter in force, in ticks — what an empty <c>| |</c> bar
    /// costs. The same length <c>MeasureBuilder.EmitEmptyMeasure</c> gives that bar's
    /// spacer, so the two walks agree on what the gap is worth — and, as there, a pending
    /// <c>partial</c> IS the meter in force: the empty pickup <c>partial 4 |</c> is worth
    /// the one beat the page draws, not the whole bar it drew nothing of.</summary>
    private int MeasureTicks()
        => FractionToTicks(_bars.BarLength);

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
        // Bare pitches, degrees, chords and/or rests, with shares — a chord(…) member spread
        // into its notes (the page's MeasureCollector.ProcessArpeggio).
        var members = Music.ArpeggioSpread.Of(arpeggio, ShapeNotesOf);
        if (members.Count == 0)
            return;

        // The group occupies its total (trailing `>>N`, or the inherited running duration);
        // its members split that into shares (one each, plus one per spaced dot). Push the
        // auto-tuplet so every played duration scales like `tuplet num/base { … }`, and
        // force each member's length — its shares of the unit — via _defaultDuration. A
        // member's parts (ArpeggioSubdivision.SpellShares) are ONE sounding note here.
        Fraction total = arpeggio.TotalDuration?.ToFraction() ?? _defaultDuration;
        var sub = ArpeggioSubdivision.Compute(Music.ArpeggioSpread.ShareCount(members), total);
        if (sub.HasTuplet)
            _tupletStack.Push((sub.TupletNum, sub.TupletBase));
        var savedDefault = _defaultDuration;
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
        // The incoming frame, for a group with no pitched member (see the end).
        int frameNameIn = _currentNoteName, frameOctaveIn = _currentOctave;
        bool rootSet = false;
        int anchorOctave = 0;
        int rootStep = 0;
        foreach (var ((member, shares, _, _, _, _), spreadNote) in members)
        {
            _defaultDuration = sub.MemberDisplay * new Fraction(shares);
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
            {
                // A spread note plays as its chord(…) item narrowed to that note (the page's rule).
                _spreadNote = spreadNote is { } sn ? (chord.Green, sn) : null;
                ProcessChord(chord, track, isRoot ? groupOctave : 0);
                _spreadNote = null;
            }
            else
                ProcessNode(member, track, conductorTrack); // rest
            if (!rootSet && spreadNote is { } rootNote)
            {
                rootSet = true;
                anchorOctave = rootNote.Octave;
                rootStep = rootNote.Step;
            }
            else if (!rootSet && letter is { } rl)
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
        // THE GROUP WRITES THE FRAME the way a chord does (user decision, 2026-09-27): the
        // next note is relative to the group's ANCHOR, the root's bare letter (or the tonic)
        // plus the marks after '>>'; a group of rests hands the incoming frame on, shifted
        // by the marks (MeasureCollector.MusicWalk ProcessArpeggio).
        bool anchored = rootSet && !savedAbsolute;
        _currentOctave = anchored ? anchorOctave : frameOctaveIn + groupOctave;
        _currentNoteName = anchored ? rootStep : frameNameIn;
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
        // The member's own dynamic and scripts, read as a note's are (ProcessNote).
        int velocity = _velocity;
        int durationPercent = 100;
        foreach (var child in pitch.Articulations)
        {
            switch (child)
            {
                case DynamicSyntax { Level: not DynamicLevel.None } dynamic:
                    velocity = dynamic.Velocity;
                    _velocity = velocity;
                    break;
                case ArticulationSyntax articulation:
                    (velocity, durationPercent) = ApplyArticulationType(articulation.Type, velocity, durationPercent);
                    break;
            }
        }
        int ticks = FractionToTicks(_defaultDuration);
        int actualTicks = Math.Max(1, ticks * durationPercent / 100);
        track.Notes.Add(new MidiNote(track.Channel, SoundKey(midiPitch, pitch.SourceStart), velocity,
            _currentTick, actualTicks, pitch.SourceStart, QuarterBend: pitch.QuarterOffset,
            SourceOrdinal: NextOrdinal(pitch.SourceStart), Timbre: _currentTimbre, Part: _currentPart));
        // The last member is what a '~' after '>>' ties on from (OpenTieTargets).
        CloseOnset(track, [track.Notes.Count - 1], false);
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
        track.Notes.Add(new MidiNote(track.Channel, SoundKey(midiPitch, degree.SourceStart), _velocity,
            _currentTick, ticks, degree.SourceStart,
            SourceOrdinal: NextOrdinal(degree.SourceStart), Timbre: _currentTimbre, Part: _currentPart));
        CloseOnset(track, [track.Notes.Count - 1], false); // see EmitArpeggioMidiPitch
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
            SetTiePending(false); // like any rest: a tie cannot span it
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
                // A spanner (@cresc/@decresc/@dim) is a DynamicSyntax with no level; letting
                // it through set velocity 0 and silenced the rest of the part (fantasia.lys L65).
                case DynamicSyntax { Level: not DynamicLevel.None } dynamic:
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
        midiPitch = SoundKey(midiPitch, note.SourceStart);

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
            _currentTick, actualDuration, note.SourceStart,
            QuarterBend: note.Pitch.QuarterOffset,
            SourceOrdinal: NextOrdinal(note.SourceStart), Timbre: _currentTimbre, Part: _currentPart));
        CloseOnset(track, [track.Notes.Count - 1], startsTie);
        _currentTick += durationTicks;
    }

    /// <summary>Drum note → GM percussion: channel 10 (0-based 9), pitch =
    /// the drum's GM key, duration/velocity semantics as pitched notes.
    /// Timbre 9 selects the preview's noise-based drum patch.</summary>
    private void ProcessDrumNote(DrumNoteSyntax drum, MidiTrack track)
    {
        SetTiePending(false); // drums do not tie
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
                case DynamicSyntax { Level: not DynamicLevel.None } dynamic:
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
            drum.SourceStart, SourceOrdinal: NextOrdinal(drum.SourceStart), Timbre: 9, Part: _currentPart));
        CloseOnset(track, [track.Notes.Count - 1], startsTie: false);
        _currentTick += durationTicks;
    }

    private void ProcessRest(RestSyntax rest)
    {
        // A rest breaks any pending tie (a tie cannot span a rest) — and forgets the onset
        // before it, so a `~` WRITTEN AFTER the rest (`c4 r4 ~ c4`) has nothing to extend:
        // until session 398 it still pointed at the pre-rest c, which then swallowed the c
        // after the rest into one note sounding through the silence (MEASURED,
        // LilySharp-Lab/sessions/p398/probes/r14/tie-over-rest: one note of 960 ticks
        // where the page draws two crotchets and a rest).
        ForgetOnset();
        // A bare `R` lasts its bar (Music.BarRest) and leaves the running duration alone.
        // ⚠️ `*N` is N of them, as on the page (MeasureCollector.MusicWalk expands the run):
        // until 2026-10-02 this read one, so `R1*3 | c'1` sounded the c a bar after the
        // rest began, where the page draws it after three.
        int durationTicks = (Music.BarRest.IsBare(rest)
            ? MeasureTicks()
            : FractionToTicks(GetDuration(rest.Duration))) * rest.MeasureCount;
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
        ForgetOnset();
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
                case DynamicSyntax { Level: not DynamicLevel.None } dynamic:
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
                    startTick, actualDuration, bare.SourceStart,
                    QuarterBend: sound.QuarterBend,
                    SourceOrdinal: NextOrdinal(bare.SourceStart), Timbre: _currentTimbre, Part: _currentPart));
                CloseOnset(track, [track.Notes.Count - 1], startsTie);
                break;
            }
            case ChordSyntax chord when _resolvedChordNotes.TryGetValue(chord, out var notes):
            {
                // The full chord again - same emission as ProcessChordRepetition,
                // displacement included: a run that reached here through a `q'` repeats
                // the chord where that q left it. Drums are exempt for the same reason.
                var tieTargets = OpenTieTargets(track);
                var onset = new List<int>();
                int ordinal = NextOrdinal(bare.SourceStart);
                int semitones = 12 * Music.BareDurations.DisplacementOf(bare);
                foreach (var n in notes)
                {
                    if (n.IsDrum)
                    {
                        track.Notes.Add(new MidiNote(9, n.MidiPitch, velocity, startTick, actualDuration,
                            bare.SourceStart, SourceOrdinal: ordinal, Timbre: 9, Part: _currentPart));
                        continue;
                    }
                    int pitch = n.MidiPitch + semitones;
                    int tiedInto = ExtendTied(track, tieTargets, pitch, durationTicks);
                    if (tiedInto >= 0) { onset.Add(tiedInto); continue; }
                    track.Notes.Add(new MidiNote(track.Channel, pitch, velocity, startTick, actualDuration,
                        bare.SourceStart, QuarterBend: n.QuarterBend, SourceOrdinal: ordinal, Timbre: _currentTimbre, Part: _currentPart));
                    onset.Add(track.Notes.Count - 1);
                }
                CloseOnset(track, onset, startsTie);
                break;
            }
            case DrumNoteSyntax drum:
            {
                var info = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
                track.Notes.Add(new MidiNote(9, info.GmKey, velocity, startTick, actualDuration,
                    bare.SourceStart, SourceOrdinal: NextOrdinal(bare.SourceStart), Timbre: 9, Part: _currentPart));
                CloseOnset(track, [track.Notes.Count - 1], startsTie: false);
                break;
            }
            default:
                // A slash (silent) or an unresolved repeat (validator reports it):
                // occupy the time, sound nothing, break any pending tie.
                SetTiePending(false);
                break;
        }

        _currentTick = startTick + durationTicks;
    }

    /// <summary>
    /// What ONE member of a chord SOUNDS, and the frame it leaves behind.
    /// </summary>
    /// <remarks>
    /// THE one spelling of the chord's octave rule for this exporter: <see cref="ProcessChord"/>
    /// reads it for the main stream and <c>ProcessGrace</c> for a chord inside a
    /// <c>grace { }</c> body.
    /// <para>
    /// ⚠️ IT WAS TWO SPELLINGS UNTIL SESSION 308, AND THEY DISAGREED BY AN OCTAVE. The grace
    /// arm resolved each member RELATIVE TO THE PREVIOUS ONE, under a comment claiming it
    /// "matches ProcessChord / CreateChordItem" — which it did not. MEASURED
    /// (scratch/p308/ab/d_chordwide against d_mainwide): <c>grace { &lt;c b&gt;16 }</c> sounded
    /// 60 and 59 where <c>&lt;c b&gt;4</c> in the same file sounds 60 and 71, and where the
    /// page and the MusicXML both say B4. It was unobservable while the page drew no grace
    /// chord at all; teaching the page to draw one is what made the two comparable, which is
    /// why <c>Semantics.GraceBodySupport</c> tells whoever touches a grace body to count all
    /// four readers.
    /// </para>
    /// <para>
    /// The first member is the ROOT: its bare LETTER is the chord's ANCHOR; every other member
    /// STACKS above it, so the chord's pitches do not depend on the written order. A
    /// deliberate Lily# divergence from LilyPond, matching <c>MusicXmlExporter</c> and
    /// <c>MeasureCollector</c>.
    /// </para>
    /// <para>
    /// ⚠️ THE CALLER DECIDES THE FRAME THE CHORD LEAVES. This advances the running state to
    /// the root's anchor while the members are placed; the caller then sets the frame to the
    /// chord's anchor (user decision 2026-09-27, <c>MeasureCollector.CreateChordItem</c>; the
    /// twin does the same). Session 394 changed the page and the twin and left this exporter
    /// and the MusicXML one on the old rule for a day — four readers of one sentence, again.
    /// </para>
    /// </remarks>
    private int ResolveChordMemberPitch(
        PitchSyntax pitch, bool isFirst, int chordOctave, int chordShift,
        ref int firstNoteName, ref int firstOctave)
    {
        if (isFirst)
        {
            int midiPitch;
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
                int rootStep = GetNoteName(pitch.BaseName);
                int anchor = RelativeOctave.Resolve(_currentNoteName, _currentOctave, rootStep, 0) + chordOctave;
                midiPitch = WrittenToMidi(rootStep, pitch.AccidentalOffset, anchor + pitch.OctaveOffset);
                _currentNoteName = rootStep;
                _currentOctave = anchor;
                firstOctave = anchor;
            }
            firstNoteName = _currentNoteName;
            return midiPitch;
        }
        if (_octaveAbsolute)
            // Absolute mode: each member is a fixed pitch, no stacking.
            return CalculateRelativeMidiPitch(pitch) + chordShift;
        int step = GetNoteName(pitch.BaseName);
        int octave = firstOctave + (step >= firstNoteName ? 0 : 1) + pitch.OctaveOffset;
        return WrittenToMidi(step, pitch.AccidentalOffset, octave);
    }

    /// <summary>A <c>chord(…)</c> item's strings on the current part's tuning, as written — the
    /// page's reading (Music.ShapeChords); the capo raises the strings (2026-09-29).</summary>
    private System.Collections.Immutable.ImmutableArray<Music.ShapeNote> ShapeNotesOf(ChordSyntax chord)
    {
        // A spread note of a << >> group (ArpeggioSpread) is the item narrowed to that one note.
        if (_spreadNote is { } spread && ReferenceEquals(spread.Item, chord.Green))
            return [spread.Note];
        var header = _currentPart != null ? Header(_currentPart) : Semantics.PartHeaderDefaults.Empty;
        return Music.ShapeChords.Notes(chord, Music.ShapeChords.TuningOf(header),
            header.SoundingShiftSemitones - Capo, _keySharps);
    }

    /// <summary>The <c>chord(…)</c> member of a <c>&lt;&lt; &gt;&gt;</c> group being played one
    /// note at a time and the note it plays now (the page's MeasureCollector._spreadNote).</summary>
    private (Syntax.InternalSyntax.GreenNode Item, Music.ShapeNote Note)? _spreadNote;

    /// <summary>A phrase's outgoing anchor when its body opens with a <c>chord(…)</c> item
    /// (<see cref="Music.PhraseAnchor.Shape"/>): the item's lowest note as written, as
    /// (step, octave) — the page's MeasureCollector.EnterPhraseTranspose; null when the part's
    /// tuning has no shape for it.</summary>
    private (int Step, int Octave)? ShapeAnchorOf(ChordSyntax? shape)
        => shape != null && Music.ShapeChords.Lowest(ShapeNotesOf(shape)) is { } low ? (low.Step, low.Octave) : null;

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
        int chordOrdinal = NextOrdinal(chord.SourceStart);

        // Use the chord's own typed duration, not a descendant scan (which could
        // pick up a duration on an inner pitch if the grammar ever allowed it).
        var duration = chord.Duration is { } cd ? GetDuration(cd) : _defaultDuration;
        int durationTicks = FractionToTicks(duration);
        durationTicks -= ConsumeGraceSteal(durationTicks); // grace notes steal from this chord

        // The chord's own dynamic and scripts, read as a note's are (ProcessNote). Until
        // 2026-09-26 a chord ignored them and `<c e>4@f` played at the running level.
        int velocity = _velocity;
        int durationPercent = 100;
        foreach (var child in chord.Articulations)
        {
            switch (child)
            {
                case DynamicSyntax { Level: not DynamicLevel.None } dynamic:
                    velocity = dynamic.Velocity;
                    _velocity = velocity;
                    break;
                case ArticulationSyntax articulation:
                    (velocity, durationPercent) = ApplyArticulationType(articulation.Type, velocity, durationPercent);
                    break;
            }
        }
        int soundTicks = Math.Max(1, durationTicks * durationPercent / 100);

        // The first member is the ROOT: its bare LETTER is the chord's ANCHOR; every
        // other member STACKS above the anchor — the same octave placement as a
        // scale degree, so a chord's pitches are independent of the order its notes
        // are written (<c e g> == <c 3 5> == <c g e>). Each member's own '/, marks
        // (the root's included) are LOCAL to that one note. A deliberate Lily#
        // divergence from LilyPond, matching the collector and the MusicXML exporter.
        // Octave marks after the closing '>' (<1 3 5>' / <c e g>,,) shift the whole
        // chord; folding it into firstOctave flows through every stacked/degree member,
        // matching the collector and MusicXML exporter.
        // extraOctave is the enclosing arpeggio's group-level shift when this chord is the
        // arpeggio's root (`<< <c e> g >>,`); 0 otherwise.
        int chordOctave = chord.ChordOctaveOffset + extraOctave;
        int chordShift = chordOctave * 12;
        // THE CHORD WRITES THE FRAME (user decision, 2026-09-27): the note after the chord
        // is relative to its ANCHOR — MeasureCollector.CreateChordItem's frame update. The
        // incoming frame is kept for a chord that anchors nothing and for absolute mode.
        int frameNameIn = _currentNoteName, frameOctaveIn = _currentOctave;

        bool isFirst = true;
        int firstNoteName = _currentNoteName;
        int firstOctave = _currentOctave;

        foreach (var pitch in pitches)
        {
            int midiPitch = ResolveChordMemberPitch(
                pitch, isFirst, chordOctave, chordShift, ref firstNoteName, ref firstOctave);
            isFirst = false;
            midiPitch = SoundKey(midiPitch, chord.SourceStart);
            int tiedInto = ExtendTied(track, tieTargets, midiPitch, durationTicks);
            if (tiedInto >= 0)
                onset.Add(tiedInto);
            else
            {
                track.Notes.Add(new MidiNote(track.Channel, midiPitch, velocity, startTick, soundTicks, chord.SourceStart,
                    QuarterBend: pitch.QuarterOffset,
                    SourceOrdinal: chordOrdinal, Timbre: _currentTimbre, Part: _currentPart));
                onset.Add(track.Notes.Count - 1);
            }
            resolved.Add((midiPitch, pitch.QuarterOffset, false));
        }

        // chord(SYMBOL SHAPE): the shape's strings on the part's tuning, written as the part
        // writes a sounding pitch and then played the way every written pitch is — the page's
        // reading (MeasureCollector.CreateChordItem; Music.ShapeChords). No usable shape: the
        // item sounds nothing and keeps its time (a spacer, LYS1040).
        Music.ShapeNote? shapeLowest = null;
        if (chord.IsShapeChord)
        {
            var shapeNotes = ShapeNotesOf(chord);
            shapeLowest = Music.ShapeChords.Lowest(shapeNotes);
            foreach (var sn in shapeNotes)
            {
                int midiPitch = SoundKey(WrittenToMidi(sn.Step, sn.Alter, sn.Octave), chord.SourceStart);
                int tiedInto = ExtendTied(track, tieTargets, midiPitch, durationTicks);
                if (tiedInto >= 0)
                    onset.Add(tiedInto);
                else
                {
                    track.Notes.Add(new MidiNote(track.Channel, midiPitch, velocity, startTick, soundTicks, chord.SourceStart,
                        SourceOrdinal: chordOrdinal, Timbre: _currentTimbre, Part: _currentPart));
                    onset.Add(track.Notes.Count - 1);
                }
                resolved.Add((midiPitch, 0, false));
            }
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
            int midiPitch = SoundKey(WrittenToMidi(step, alter, octave), chord.SourceStart);
            int tiedInto = ExtendTied(track, tieTargets, midiPitch, durationTicks);
            if (tiedInto >= 0)
                onset.Add(tiedInto);
            else
            {
                track.Notes.Add(new MidiNote(track.Channel, midiPitch, velocity, startTick, soundTicks, chord.SourceStart,
                    SourceOrdinal: chordOrdinal, Timbre: _currentTimbre, Part: _currentPart));
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
            track.Notes.Add(new MidiNote(9, dinfo.GmKey, velocity, startTick, soundTicks, chord.SourceStart,
                SourceOrdinal: chordOrdinal, Timbre: 9, Part: _currentPart));
            resolved.Add((dinfo.GmKey, 0, true));
        }
        _resolvedChordNotes[chord] = resolved;
        CloseOnset(track, onset, startsTie);

        // The next note is relative to the chord's ANCHOR (the root's bare letter or the
        // tonic, plus the whole-chord marks); a chord that anchors nothing, and absolute
        // mode, hand the incoming frame on shifted by the marks.
        bool anchored = !_octaveAbsolute && (pitches.Count > 0 || chord.Degrees.Any());
        _currentNoteName = anchored ? firstNoteName : frameNameIn;
        _currentOctave = anchored ? firstOctave : frameOctaveIn + chordOctave;
        // A chord from a shape hands on its lowest sounding note, as written (the page's rule).
        if (shapeLowest is { } low && !_octaveAbsolute)
        {
            _currentNoteName = low.Step;
            _currentOctave = low.Octave;
        }

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
            // q' sounds the chord an octave up, accumulated along the q chain.
            // ⚠️ A DRUM chord is exempt: its "pitch" is an instrument slot on channel 10,
            // not a pitch, so displacing it would silently pick a different instrument.
            int semitones = 12 * ChordRepetitions.DisplacementOf(rep);
            int ordinal = NextOrdinal(rep.SourceStart);
            foreach (var n in notes)
            {
                if (n.IsDrum)
                {
                    track.Notes.Add(new MidiNote(9, n.MidiPitch, _velocity, startTick, durationTicks,
                        rep.SourceStart, SourceOrdinal: ordinal, Timbre: 9, Part: _currentPart));
                    continue;
                }
                int pitch = n.MidiPitch + semitones;
                int tiedInto = ExtendTied(track, tieTargets, pitch, durationTicks);
                if (tiedInto >= 0) { onset.Add(tiedInto); continue; }
                track.Notes.Add(new MidiNote(track.Channel, pitch, _velocity, startTick, durationTicks,
                    rep.SourceStart, QuarterBend: n.QuarterBend, SourceOrdinal: ordinal, Timbre: _currentTimbre, Part: _currentPart));
                onset.Add(track.Notes.Count - 1);
            }
        }
        CloseOnset(track, onset, startsTie);

        _currentTick = startTick + durationTicks;
    }

    private void ProcessTimeSignature(TimeSignatureSyntax timeSig, MidiTrack conductorTrack)
    {
        // `time none` writes NO meta event and leaves the running meter alone
        // (Semantics.BarContext.SetTime has the rule and LilyPond's reference): the bars the
        // MIDI grid draws through a cadenza are the last meter's — the same picture a DAW gets
        // from LilyPond's file. Before session 353 this wrote the 4/4 the syntax falls back to,
        // so a `time none` after a 3/4 stretch flipped the DAW's grid to 4/4 where the page
        // shows no meter.
        if (!_bars.SetTime(timeSig))
            return;
        conductorTrack.SetTimeSignature(_currentTick, _bars.Meter.Beats, _bars.Meter.BeatType);
    }

    private void ProcessTempo(TempoDeclarationSyntax tempo, MidiTrack conductorTrack)
    {
        // The meta event is microseconds per QUARTER, so the bpm is read in the unit the
        // source states (TempoValue.QuarterBpm): `tempo 2 = 60` is 120 crotchets a minute,
        // `tempo 4. = 40` is 60. Until session 398 the unit was ignored and both played at
        // the written figure — half and two-thirds speed.
        if (tempo.Value.QuarterBpm is double quarterBpm)
        {
            _tempo = (int)System.Math.Round(quarterBpm);
            conductorTrack.SetTempo(_currentTick, BpmToMicroseconds(quarterBpm));
        }
    }

    private void ProcessRepeat(RepeatExpressionSyntax repeat, MidiTrack track, MidiTrack conductorTrack)
    {
        int repeatCount = 2;
        if (int.TryParse(repeat.Count.Text, out int count))
            repeatCount = count;

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
        }
        // (Until 2026-09-17 this walk alone played a LilyPond-style `alternative { … }`
        // clause after the body — the page, the twin and the MusicXML dropped it — choosing
        // the LAST ending for every pass beyond the endings' count, where LilyPond's `\volta`
        // -less alternative repeats the FIRST. The clause left the language; HANDOFF §2 R1.)
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

    // Truncated, as the integer division before it was: a rounding here moved the tempo meta of
    // 157 books by one microsecond in the session-398 sweep, for no reader's benefit.
    private static int BpmToMicroseconds(double bpm) => (int)(60_000_000 / Math.Max(1.0, bpm));

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
        // a REST in a grace body SOUNDS here (since 2026-07-10) and is still dropped by the
        // page, which is the half of docs/HANDOFF.md §2 U8 that is open. (A CHORD was on that
        // line until session 308, which taught the page to draw one - and found that this
        // walker had been sounding it at the WRONG OCTAVE all along; see
        // ResolveChordMemberPitch.) That
        // asymmetry is about which GROBS a grace column can hold; a phrase reference names no
        // grob at all, which is why it is the one that could be closed on its own.
        // ⚠️ THIS WALKER'S MAIN STREAM HAS NO SUCH BUDGET, and the asymmetry is real rather
        // than theoretical: MEASURED 2026-08-30 (session 301, scratch/p301/budget) on a
        // doubling phrase DAG (`P(n) = P(n-1) P(n-1)`, 26 source lines), `lysc svg` truncates
        // at the budget and SAYS SO (LYS1033, "the picture is TRUNCATED from here on") while
        // `lysc midi` emits all 1,048,576 notes (9.4 MB) and `lysc xml` all of them again
        // (192 MB), silently. LYS1033's wording is about the PICTURE and, since 2026-09-08
        // (owner's decision: the budget is the page's, a MIDI of a million notes is a correct
        // output), it also SAYS that the other three outputs write the whole expansion.
        // A grace body is budgeted here anyway because the cap has ONE home and this is the
        // shared expander's own parameter — not because this walker counts anywhere else.
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
        Stack<(int Transpose, int AbsBase, int? Anchor, int Offset, (int Step, int Octave)? ShapeAnchor)>? phraseFrames = null;

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
                        : reset.AnchorStep == Music.PhraseAnchor.Shape ? null
                        : reset.AnchorStep;
                    var shapeAnchor = reset.AnchorStep == Music.PhraseAnchor.Shape
                        ? ShapeAnchorOf(reset.AnchorShape) : null;
                    (phraseFrames ??= new()).Push((_currentTransposeSemitones,
                        _partAbsoluteBase, anchor, reset.OctaveOffset, shapeAnchor));
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
                    var (savedTranspose, savedAbsBase, anchor, offset, shapeAnchor) = phraseFrames.Pop();
                    _currentTransposeSemitones = savedTranspose;
                    _partAbsoluteBase = savedAbsBase;
                    // The reference hands the chain back at its ANCHOR — one item, the chord
                    // rule — so a grace note written after it reads the frame it would read
                    // after one in the main stream. A pitchless body hands nothing off.
                    if (shapeAnchor is { } sa)
                    {
                        _currentNoteName = sa.Step;
                        _currentOctave = sa.Octave;
                    }
                    else if (anchor is { } astep)
                    {
                        _currentNoteName = astep;
                        _currentOctave = RelativeOctave.Resolve(
                            0, _partOctaveAnchor + offset, astep, 0);
                    }
                    break;
                }

                // A TUPLET IN A GRACE BODY IS A CONTAINER, AND THIS READER DOES READ ITS
                // RATIO. MEASURED 2026-08-30 (session 302, scratch/p302/lp) on LilyPond's own
                // \midi at division 384 - on the WSL LilyPond, v2.27.3, NOT the canonical
                // 2.26.0 (the pinned Windows binary stalled 13 minutes without writing a byte;
                // docs/RULES.md 5.5). The ticks are therefore QUALITATIVE; what is canonical is
                // the mechanism they agree with, cited on GraceTupletStartMarker and read in
                // the 2.26.0 source. `\grace { d'16 e' f' } c'4` sounds its three grace
                // notes at ticks 0 / 21 / 43 and hands the main note over at 64, while
                // `\grace { \tuplet 3/2 { d'16 e' f' } } c'4` sounds them at 0 / 14 / 29 and
                // hands over at 43 = round(64 * 2/3). LilyPond scales a grace body's tuplet
                // exactly as it scales one in the main stream.
                // ⚠️ THE ARITHMETIC IS THE HOUSE THAT WAS ALREADY HERE, not a second
                // spelling: GraceTicks calls FractionToTicks, which multiplies by every entry
                // on _tupletStack, so pushing the ratio is the whole change. The page reads
                // nothing off it (a grace note is DRAWN from its written duration - measured
                // on LilyPond 2.26.0, session 301) and its bracket and number stay reported.
                case Svg.Collector.GraceTupletStartMarker t:
                    _tupletStack.Push((t.Actual, t.Normal));
                    break;

                // ⚠️ THE POP IS UNGUARDED ON PURPOSE. The pair is emitted or omitted
                // together (GraceBodySupport.Expand pays for a whole entry or none of it), so
                // it cannot underflow - and a `Count > 0` guard would not make it safe if it
                // could: _tupletStack is SHARED with the main stream, so an unpaired close
                // would pop an enclosing tuplet's own entry and the guard would wave it
                // through. Checklist 7.7 calls that shape a fallback that turns a bug green.
                case Svg.Collector.GraceTupletEndMarker:
                    _tupletStack.Pop();
                    break;

                case NoteSyntax note:
                {
                    if (note.Duration != null) written = note.Duration.ToFraction();
                    int g = GraceTicks(written);
                    int midiPitch = SoundKey(CalculateRelativeMidiPitch(note.Pitch), note.SourceStart);
                    track.Notes.Add(new MidiNote(track.Channel, midiPitch, _velocity, _currentTick, g,
                        note.SourceStart, QuarterBend: note.Pitch.QuarterOffset,
                        SourceOrdinal: NextOrdinal(note.SourceStart), Timbre: _currentTimbre, Part: _currentPart,
                        IsGrace: true));
                    _currentTick += g;
                    _pendingGraceSteal += g;
                    break;
                }
                case ChordSyntax chord:
                {
                    if (chord.Duration != null) written = chord.Duration.ToFraction();
                    int g = GraceTicks(written);
                    int chordOrdinal = NextOrdinal(chord.SourceStart);
                    // Within-chord relative octave: the root's LETTER is the chord's ANCHOR
                    // and every other member STACKS above it, and the item AFTER the chord is
                    // relative to that anchor. Asked of ResolveChordMemberPitch, which is what
                    // ProcessChord asks — this arm SPELLED IT OUT until session 308, claimed
                    // in a comment to match, and did not: it resolved each member relative to
                    // the previous one and put `grace { <c b>16 }` an octave under the head
                    // the page draws.
                    bool isFirst = true;
                    int chordShift = chord.ChordOctaveOffset * 12;
                    int frameNameIn = _currentNoteName, frameOctaveIn = _currentOctave;
                    int firstNoteName = _currentNoteName, firstOctave = _currentOctave;
                    foreach (var pitch in chord.Pitches)
                    {
                        int mp = SoundKey(
                            ResolveChordMemberPitch(pitch, isFirst, chord.ChordOctaveOffset,
                                chordShift, ref firstNoteName, ref firstOctave),
                            chord.SourceStart);
                        isFirst = false;
                        track.Notes.Add(new MidiNote(track.Channel, mp, _velocity, _currentTick, g,
                            chord.SourceStart, QuarterBend: pitch.QuarterOffset,
                            SourceOrdinal: chordOrdinal, Timbre: _currentTimbre, Part: _currentPart,
                            IsGrace: true));
                    }
                    // A chord(…) item in a grace body: its strings, as ProcessChord plays them.
                    Music.ShapeNote? graceLowest = null;
                    if (chord.IsShapeChord)
                    {
                        var shapeNotes = ShapeNotesOf(chord);
                        graceLowest = Music.ShapeChords.Lowest(shapeNotes);
                        foreach (var sn in shapeNotes)
                            track.Notes.Add(new MidiNote(track.Channel,
                                SoundKey(WrittenToMidi(sn.Step, sn.Alter, sn.Octave), chord.SourceStart),
                                _velocity, _currentTick, g, chord.SourceStart,
                                SourceOrdinal: chordOrdinal, Timbre: _currentTimbre, Part: _currentPart,
                                IsGrace: true));
                    }
                    // The chord's anchor is the next note's frame — ProcessChord's rule.
                    bool anchored = !_octaveAbsolute && !isFirst;
                    _currentNoteName = anchored ? firstNoteName : frameNameIn;
                    _currentOctave = anchored ? firstOctave : frameOctaveIn + chord.ChordOctaveOffset;
                    if (graceLowest is { } gl && !_octaveAbsolute)
                    {
                        _currentNoteName = gl.Step;
                        _currentOctave = gl.Octave;
                    }
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

    /// <summary>The lyric blocks met while playing the current section, with the part in
    /// force where each was met — sung onto that section's notes once they exist.</summary>
    private List<(LyricsBlockSyntax Block, string? Part)> _sectionLyrics = new();

    /// <summary>
    /// Sings a section's lyric blocks onto the notes the section just sounded: one lyric
    /// meta event per syllable, at the onset it is sung on — the MusicXML's rule
    /// (MusicXmlExporter.AttachLyrics) read on ticks instead of note lists. Syllables
    /// advance onset by onset (a chord is one onset; grace notes, rests and tie
    /// continuations are not sung), a lyric bar line moves to the first onset of the next
    /// bar, and a melisma holds its onset without a syllable.
    /// </summary>
    /// <remarks>
    /// Which notes a block sings: the part block it names (<c>lyrics NAME { … }</c>), else
    /// the part in force where it was met (a block inside a part block), else the section's
    /// FIRST part block — as the MusicXML binds section-level lyrics. Until session 398 the
    /// walk wrote every syllable at the tick the block was met, which was the section's
    /// start — and then wrote nothing at all, because it read the block's children as bare
    /// tokens after the parser had made them syllable nodes (MEASURED,
    /// LilySharp-Lab/sessions/p398/probes/r14/lyrics-midi: no lyric events in the .mid).
    /// ⚠️ Bars are counted at the section's meter at its end and its header pickup; a meter
    /// change INSIDE a section shifts the bar-line sync from there on (the page and the
    /// MusicXML sync on real measures). The events go to the main track and land in the
    /// first part track (SplitIntoPartTracks), wherever the sung part's notes went.
    /// </remarks>
    private void AttachLyrics(MidiTrack track, SectionDeclarationSyntax? section, int notesBefore, int sectionStart)
    {
        if (_sectionLyrics.Count == 0)
            return;
        // A null section is the section-less file (Export plays the root as one stream).
        var partNames = new List<string>();
        for (int i = 0; section != null && i < section.SlotCount; i++)
            if (section.GetChild(i) is PartBlockSyntax pb)
                partNames.Add(pb.Name);
        int firstBar = FractionToTicks(
            section != null && _sectionHeaders.Partials.TryGetValue(section.SectionName, out var hp)
                ? hp.ToFraction()
                : _bars.BarLength);
        int barTicks = FractionToTicks(_bars.MeterLength);

        foreach (var (block, partAtBlock) in _sectionLyrics)
        {
            string? sung = block.VoiceName is { } named && partNames.Contains(named)
                ? named
                : partAtBlock ?? partNames.FirstOrDefault();
            // The words of a part the score does not sound leave with its notes (Export's strip).
            if (sung != null && !PartSounds(sung))
                continue;
            var onsets = track.Notes.Skip(notesBefore)
                .Where(n => !n.IsGrace && (sung == null || n.Part == sung))
                .Select(n => n.StartTick).Distinct().OrderBy(t => t).ToList();
            int bar = 0, oi = 0;
            foreach (var (text, _, _, isBarline, isMelisma) in Svg.Collector.LyricCollector.ParseSyllables(block))
            {
                if (isBarline)
                {
                    bar++;
                    int barStart = sectionStart + firstBar + (bar - 1) * barTicks;
                    while (oi < onsets.Count && onsets[oi] < barStart)
                        oi++;
                    continue;
                }
                if (oi >= onsets.Count)
                    break; // more syllables than onsets — stop quietly, as the MusicXML does
                int tick = onsets[oi++];
                if (isMelisma)
                    continue;
                track.Lyrics.Add(new LyricEvent(tick, text));
            }
        }
        _sectionLyrics.Clear();
    }
}