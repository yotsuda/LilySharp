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

using System.Text;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

// A using directive does not import NESTED namespaces, so the green layer needs its own name
// here. Only the form walk touches it, to build the two nodes the source never wrote:
// the `|:` / `:|` a form's repeat block spells with bare tokens, and the ending node
// EmitInlineRepeat groups (AppendRepeatBlock / CreateEnding).
using InternalSyntax = LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.LilyPond;

/// <summary>
/// Exports a Lily# syntax tree back to a standalone LilyPond <c>.ly</c> source.
///
/// This is a TRANSPILER, not a re-derivation: pitch tokens (letter, accidental,
/// and the <c>'</c>/<c>,</c> octave marks) are copied through VERBATIM so the
/// octave the author wrote in the <c>.lys</c> is preserved byte-for-byte. To keep
/// those verbatim marks pitch-correct in real LilyPond, the music is wrapped in a
/// reference that matches Lily#'s own octave convention:
///   • <c>octave absolute</c> input → <c>\fixed c' { … }</c> (bare <c>c</c> = middle C;
///     absolute mode is anchored at middle C whatever the clef — OctaveContext's
///     "clef default is deliberately NOT used here")
///   • relative input (the default) → <c>\relative</c> at THE PART'S OWN anchor, which is
///     not always <c>c'</c>: an explicit <c>octave N</c> part property, else the
///     <c>instrument</c> preset's octave, else 4 (InstrumentDefaults.AnchorOctave). The clef
///     is NOT a step: a Lily# clef is drawing only, like LilyPond's (user decision
///     2026-09-15). ⚠️ This file used to write <c>\relative c'</c> unconditionally, which
///     made every part with an octave-3 preset export AN OCTAVE HIGH.
///
/// It reproduces the MUSIC and the staff/tab structure the score declares; it does
/// NOT reconstruct anything the <c>.lys</c> does not hold (a hand <c>.ly</c>'s
/// comments, multiple <c>\book</c> variants, custom definitions).
///
/// A preset the <c>.lys</c> DOES hold but LilyPond has no spelling for is EXPANDED, not
/// dropped: <c>instrument bass</c> becomes the clef, the relative anchor, the string
/// tuning and the sounding transposition it stands for (<see cref="PartClefWord"/>), the
/// way a degree chord becomes its pitches. Expanding what the source says is transpiling;
/// inventing what it never said is the re-derivation this file refuses.
/// </summary>
public sealed partial class LilyPondExporter
{
    private const string LilyPondVersion = "2.26.0";

    private readonly StringBuilder _sb = new();
    private readonly List<string> _warnings;   // _shared.Warnings — see SharedState
    private bool _octaveAbsolute; // false = relative (Lily#'s default)

    /// <summary>
    /// The printed label each part's staff carries, by part name — the twin's
    /// <c>instrumentName</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ ASKED OF THE PAGE'S OWN READER, not re-derived. Which label a staff shows is a
    /// four-step precedence — a per-score <c>staff X "…"</c> override, then the part's inline
    /// display name, then the <c>instrument</c> preset's, then (only once two or more plain
    /// staves exist) the capitalised part name, with <c>staff ~X</c> suppressing it — and
    /// re-implementing that here would be a second spelling of a rule the twin exists to
    /// compare against. <see cref="Svg.Collector.RenderSpecParser"/> answers it.
    /// <para>
    /// ⚠️ WITHOUT THIS THE TWIN CARRIED NO NAME AT ALL, so nothing about instrument names
    /// could be measured against LilyPond — the same shape as `lysc ly` dropping lyrics,
    /// which is what left showcase/08-chorale's "verified against LilyPond" comment resting
    /// on a twin that had none.
    /// </para>
    /// <para>
    /// ⚠️ KEYED BY PART, so a score putting ONE part on two staves under two different labels
    /// keeps only the last. Two such files exist in the tree and neither is a fixture (see
    /// DuplicatePartStaffTests); a positional match would be worse, because the exporter's
    /// walk over render syntax and the spec's item list are not the same list.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, string> _instrumentNames = new(StringComparer.Ordinal);


    /// <summary>
    /// The octave the part being emitted anchors its relative pitches to — Lily#'s
    /// "default octave": 4 unless the part's <c>octave N</c> or <c>instrument</c> says otherwise.
    /// </summary>
    /// <remarks>
    /// It is state rather than a parameter because the two places that spell an anchor are
    /// not adjacent: the part's own wrapper (<see cref="EmitPartVariable"/>) and every
    /// nested <c>\relative</c> a phrase reference opens (<see cref="ReferencePitch"/>).
    /// Both have to move together or a bass part's phrases land an octave off its own
    /// wrapper, which is worse than both being wrong the same way.
    /// ⚠️ Sub-exporters must inherit it — see the phrase-body buffer.
    /// </remarks>
    private int _anchorOctave = InstrumentDefaults.DefaultAnchorOctave;

    /// <summary>
    /// The running WRITTEN key signature (sharps positive, flats negative) and the tonic an
    /// omitted chord root anchors on — the two things a scale-degree member resolves against.
    /// </summary>
    /// <remarks>
    /// Seeded from the score's top-level key (<see cref="ScoreHomeKey"/>, the same reading the
    /// collector and the MIDI / MusicXML exporters take) and advanced by every <c>\key</c> this
    /// exporter WRITES, in emission order — which is the collector's order too, because
    /// <see cref="SectionHeaderMusic"/> already emits a section's own key where the collector
    /// applies it. A custom/atonal key has no tonic (<c>Valid</c> false), and the collector
    /// falls back to C there, so this does the same.
    /// ⚠️ It is the written key, NOT the sounding one, and it is RIGHT to be: a
    /// <c>transpose</c> moves the key and the pitches together, and the twin says so by
    /// wrapping the whole variable in <c>\transpose c X</c> (see <see cref="TransposeTarget"/>)
    /// rather than by writing moved keys and moved pitches itself. LilyPond moves both inside
    /// the wrapper, so everything here stays the source's own spelling — which is what a
    /// transpiler owes its reader. ⚠️ This used to read "this exporter writes neither — a
    /// standing gate of its own, with one fixture in it"; the gate closed on 2026-08-17 and
    /// the fixtures were four, not one. ⚠️ It is NOT the <c>instrument</c> gate, which is also
    /// closed: see <see cref="PartClefWord"/>.
    /// </remarks>
    private int _keySharps;
    private KeyTonic _tonic = KeyTonic.CMajor;
    private int _homeKeySharps;
    private KeyTonic _homeTonic = KeyTonic.CMajor;
    // The home key's DECLARATION node (null = default C major): re-emitted verbatim
    // when a section boundary restores the score key, so mode/spelling come from the
    // source (see EmitSectionPlay).
    private KeySignatureSyntax? _homeKeyNode;

    // The running METER, the score meter a section boundary reverts it to, `time none` in
    // force and the pickup pending — Semantics.BarContext, the one spelling of those rules
    // (REFACTOR_PLAN stage C4; the MIDI and the MusicXML moved first). Held as the WRITTEN
    // pair, not a Fraction, so 4/4 and 2/2 stay distinct (they engrave differently and \time
    // takes the pair). The home node is re-emitted verbatim on a restore so a `C` written in
    // the source stays `C` (see ScoreHomeMeter). ⚠️ Each nested-body exporter has a context of
    // its own: the meter half rides the frames (PartFrame's home, StreamFrame's running
    // state), the pickup does not — a body opens with none pending, as before.
    private readonly Semantics.BarContext _bars = new();
    private TimeSignatureSyntax? _homeTimeNode;
    // The part header's own key (part p { key bes major … } outside its sections), null when it
    // writes none, and the key sharps a section boundary restores — the part header's, else the
    // file's (HANDOFF §2 R12⒞; the page: MeasureCollector.GetPartDefaults). A part header cannot
    // state a meter (LYS1026), so the meter needs no twin of this. Set per part by ArmPartHome
    // and after EmitScoreSettings.
    private KeySignatureSyntax? _partHeaderKeyNode;
    private int _restoreKeySharps;

    // The running meter AS WRITTEN (TimeText, "\cadenzaOn" aside): what a section head's
    // `time` is compared with — the beats alone cannot tell 3+2/8 from 5/8.
    private string TimeTextInForce => TimeText(_bars.Meter);
    // A section play's head (the twin of MeasureBuilder.SectionHead): what was in force before
    // its restores, and the restores themselves, HELD until the head is over — a `time` or
    // `key` that states the held value again cancels its restore and writes nothing.
    private (string TimeText, bool Senza, int KeySharps, KeyTonic Tonic)? _sectionHead;
    private string? _heldTimeRestore;
    private string? _heldKeyRestore;
    private string? _heldMark;
    // `time none` in force (_bars.SenzaMisura) — LilyPond's \cadenzaOn (Timing.timing = ##f).
    // The running flag decides two spellings: a metered `time` after it writes \cadenzaOff
    // first (or LilyPond keeps not counting, draws no bar and numbers nothing), and a written
    // `|` inside it is `\bar "|"` — under \cadenzaOn LilyPond's `|` is only a bar CHECK and
    // draws nothing, while Lily#'s `|` is the boundary and draws the bar
    // (MeasureBuilder.HandleBarline). MEASURED (2.26.0, scratch/p354/lp/senza-misura.ly
    // against senza-fixed.ly, 2026-09-08): with plain `|` the cadenza drew no bar line and the
    // bars after \time 4/4 none either.
    // True while the cadenza in force opened INSIDE a bar — music had sounded since the last
    // bar line when `time none` arrived. LilyPond's measurePosition froze at that reading and
    // nothing in the cadenza resets it (not its `\bar "|"`, not \cadenzaOff), so the returning
    // \time is "mid-measure" to LilyPond: the twin writes `\partial <bar>` after it, which
    // sets measurePosition = measureLength − dur = 0 (lily/timing-translator.cc:149-160
    // pre_process_music, the mid-piece branch) and is exactly the page's rule — a written `|`
    // closed the cadenza's bar, and the next bar starts fresh. MEASURED (2.26.0,
    // scratch/p359/lp/midbar-8th.ly against midbar-8th-fix.ly, 2026-09-09): without it,
    // "mid-measure time signature without \partial", "bar check failed at: 1/4" and an
    // automatic bar line INSIDE the `c1` (bars 40.893 / 50.795); with `\partial 1` after
    // `\time 4/4`, no warning and bars 40.893 / 52.295 — the same for a 3/4 return with
    // `\partial 2.` (midbar-8th-34-fixA.ly), in either order of \time and \partial (fixB).
    private bool _cadenzaOpenedMidBar;
    // EmitMusicStream's clock for the `| |` rule — has music sounded since the last bar line?
    // A field rather than a local so EmitTime can read it when `time none` arrives.
    private bool _timeSinceBoundary;
    // Standalone music a note leaves for AFTER its sibling post-events — `\breathe`,
    // `\caesura` (SplitAttachments) — written by EmitMusicStream before the next event.
    private readonly StringBuilder _trailingMusic = new();
    // (The pickup in force — a `partial` read and not yet closed by a bar — is _bars.Partial,
    // for the two readers that need it here: the spacer an empty `| |` bar stands for and a
    // bare `R`. The same BarContext.Partial the page's MeasureBuilder reads, spent the same way:
    // at the first bar that closes after it.)

    /// <summary>
    /// The relative-octave frame, TWICE: where Lily# stands, and where the text this exporter
    /// has written puts LilyPond. Both are absolute octave numbers (4 = the octave of middle C).
    /// </summary>
    /// <remarks>
    /// They are the same number everywhere the transpiler is exact, and a chord is what parts
    /// them: the next event is relative to the chord's ANCHOR in Lily# (the root's bare letter,
    /// or the key's tonic when the root is omitted — MeasureCollector.ItemFactory) and to the
    /// chord's FIRST MEMBER in LilyPond (lily/music-sequence.cc:213-219, <c>ret_first</c>).
    /// A degree chord written <c>&lt;1' 3 5&gt;</c> sounds C5 E4 G4 and leaves Lily# on C4 —
    /// LilyPond, reading the C5 this exporter had to write first, would be an octave up.
    /// <para>
    /// The difference is carried, not warned about: the next pitch's marks absorb it
    /// (<see cref="EmitMusicPitch"/>), which puts both frames back on the same note. Only a
    /// degree chord can open the gap, so in every book without one the correction is 0 and
    /// every pitch token is still the source's, byte for byte.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="_frameTracked"/> goes false where this exporter hands pitches to a
    /// sub-exporter whose frame it does not model (a grace body, a phrase reference's nested
    /// <c>\relative</c>, a voice span). A degree chord after that point is reported rather
    /// than trusted.
    /// </para>
    /// </remarks>
    private int _lysStep, _lysOctave;
    private int _lyStep, _lyOctave;
    private bool _frameTracked = true;

    /// <summary>
    /// The parts whose music is drum-kit music, and whether the variable being written now is
    /// one of them. A drum note (<c>hh8 bd4</c>) is a NAME, not a pitch, and LilyPond only
    /// reads those names inside <c>\drummode</c> — so the part is wrapped in that instead of
    /// <c>\relative</c>, and its staff is a <c>DrumStaff</c>.
    /// </summary>
    /// <remarks>
    /// The vocabulary itself needs no translation: Lily#'s drum names and aliases ARE
    /// LilyPond's (DrumNameRegistry cites ly/drumpitch-init.ly drumPitchNames), so the token
    /// goes through verbatim like any other. Before this, all 24 of them in the corpus were
    /// dropped with a warning and test/drum-groove's twin was a bar-check failure.
    /// </remarks>
    private readonly HashSet<string> _drumParts = new(StringComparer.Ordinal);
    private bool _drumMode;

    /// <summary>
    /// The part being written is played ONLY by a <c>combinedStaff</c>, so a <c>voice { } { }</c>
    /// span in it is one Voice's simultaneous music (<see cref="EmitParallel"/>).
    /// </summary>
    /// <remarks>
    /// The page reads a span two ways, by the staff that plays it: on a plain or condensed staff
    /// each block is a voice with <c>\voiceOne</c>/<c>\voiceTwo</c>
    /// (<c>MeasureCollector.ResolveVoiceStemDirections</c>), and in a combined part the blocks are
    /// one Voice's events, among which the combiner chooses a silence
    /// (<c>PartCombiner.ChooseSilenceWithinPart</c>, the port of
    /// <c>input/regression/part-combine-silence-mixed.ly</c>'s <c>&lt;&lt; R1 s1 s4 &gt;&gt;</c>).
    /// MEASURED (LilyPond 2.26.0, scratch/p387/voice): the twin of audit/lpreg/pcsm-probe.lys
    /// written with <c>\\</c> inside <c>\partCombine</c> warns "too many colliding rests"; the
    /// one-Voice spelling does not.
    /// A part a score ALSO plays on another staff keeps <c>\\</c> and is reported: one variable
    /// cannot be both spellings.
    /// </remarks>
    private bool _combinedPart;

    /// <summary>
    /// The parts whose music carries a <c>\N</c> string number. LilyPond's Staff prints a
    /// StringNumber grob for every one of them — a circled digit beside the note — and
    /// Lily#'s notation staff draws none: the number steers the TAB's string choice
    /// (<c>MeasureCollector.ExtractStringNumber</c>) and nothing else. So the twin's Staff
    /// asks LilyPond not to print them, which is what the hand-written corpus books say in
    /// as many words (<c>\new Staff \with { \omit StringNumber }</c>).
    /// </summary>
    /// <remarks>
    /// The same switch as <c>\tabFullNotation</c> on the TabStaff, the other way round:
    /// the two engines' defaults differ, and the twin declares what Lily# draws. Measured
    /// (2026-09-05, session 335, scratch/p335): without this the twin of "Le Freak" bars
    /// 1-57 printed the digits over bars 10-11 of the notation staff, widened them, and
    /// tied the two 11-line breakings of its A1 the other way from the hand-written book
    /// (6 + 10 against 4 + 12, at the same system count).
    /// </remarks>
    private readonly HashSet<string> _stringNumberParts = new(StringComparer.Ordinal);

    /// <summary>
    /// The parts that write a right-hand finger (<c>@pluck(p|i|m|a)</c>), whose staves must
    /// <c>\set strokeFingerOrientations = #'(down)</c>: LilyPond's default puts the letter to
    /// the RIGHT of the head, a side the page never draws. The page's own side is the
    /// stem-opposite one, like a Script's (ArticulationEngraver: <c>forceAbove || !stemUp</c>
    /// over the collector's initial below), which no orientation list can say — <c>'(down)</c>
    /// is the collector's initial and the low-note half of what the page draws; the twin's
    /// letter over a stem-down note sits below where the page's sits above (self-acknowledged,
    /// measured on test/tab-technique-letters: b and e' take m and a above on the page).
    /// Written only for a part that plucks at all, so every other twin is unchanged — the
    /// same shape as <see cref="_pedalParts"/>.
    /// LILYPOND-REF: ly/engraver-init.ly, the Staff context (line 909) — strokeFingerOrientations
    ///   = #'(right), the default.
    /// LILYPOND-REF: lily/new-fingering-engraver.cc:372-375 position_scripts — the orientations
    ///   list read for the StrokeFinger grobs.
    /// </summary>
    private readonly HashSet<string> _strokeFingerParts = new(StringComparer.Ordinal);

    /// <summary>
    /// The parts that use a piano pedal, each with the <c>pedalSustainStyle</c> its staff
    /// must be given — LilyPond's default is <c>'text</c> ("Ped. *") where Lily#'s
    /// <see cref="Svg.Model.PedalStyle"/> defaults to Bracket, so a twin that says nothing
    /// draws different pedal music. Written only for a part that pedals at all, so every
    /// other twin is unchanged.
    /// LILYPOND-REF: ly/engraver-init.ly, the Staff context (line 895) — pedalSustainStyle =
    ///   #'text; lily/piano-pedal-engraver.cc — the style read per pedal type.
    /// </summary>
    private readonly Dictionary<string, string> _pedalParts = new(StringComparer.Ordinal);

    /// <summary>
    /// The parts that write an ottava. Their staff opens with LilyPond's ORDINAL ottavation —
    /// "8va" / "8vb" / "15ma" / "15mb", the strings the page draws — because LilyPond 2.26's
    /// default is the bare number ("8"), and a twin that says nothing prints a different
    /// label. Written only for a part with an ottava, so every other twin is unchanged.
    /// </summary>
    /// <remarks>
    /// The page's strings are the owner's decision (2026-08-02, audit/lp-geometry/probes/
    /// ottava-floor.ly: Lily# keeps "8va"); this makes the twin say the same (session 734).
    /// LILYPOND-REF: ly/engraver-init.ly, the Staff context — ottavationMarkups = #ottavation-numbers, the default.
    /// LILYPOND-REF: scm/translation-functions.scm:1178 ottavation-simple-ordinals — "8va" and its siblings.
    /// </remarks>
    private readonly HashSet<string> _ottavaParts = new(StringComparer.Ordinal);

    /// <summary>The <c>\set</c> an ottava part's staff opens with, or "" (<see cref="_ottavaParts"/>).</summary>
    private string OttavaStyleSet(string? partName)
        => partName != null && _ottavaParts.Contains(partName)
            ? "\\set Staff.ottavationMarkups = #ottavation-simple-ordinals "
            : "";

    /// <summary>Whether the twin currently has <c>\improvisationOn</c> open — the
    /// LilyPond spelling of a slash-note run. Opened by the first slash, closed
    /// by the next pitched event.</summary>
    private bool _improvisationOpen;

    /// <summary>The note value Lily# would give an event that writes no duration — its own
    /// rule, not LilyPond's. See <see cref="EmitEventDuration"/>.</summary>
    private string _lastWrittenValue = "4";

    /// <summary>The dots that go with <see cref="_lastWrittenValue"/> — Lily# carries them
    /// too (MeasureCollector.ItemFactory: an undurated note takes <c>_defaultDots</c>), and an
    /// arpeggio's total is that value with its dots.</summary>
    private int _lastWrittenDots;

    /// <summary>Set when the next event must write its duration out because LilyPond would
    /// otherwise infer a different one. See <see cref="EmitEventDuration"/>.</summary>
    private bool _forceNextDuration;

    /// <summary>
    /// What this exporter and every nested-body exporter it opens (<see cref="OpenNested"/>)
    /// hold IN COMMON — one instance, handed over by reference, never copied: the phrase
    /// table, the cycle guard, the marks the page's streams already carry, the tree, and the
    /// one warning list every body reports into.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME, BY CONSTRUCTION. This used to be a field-by-field hand-over at the
    /// nested-exporter sites, and every table added to the exporter had to be added there
    /// too: the phrase table was missing from four of the six sites (MEASURED 2026-08-17:
    /// samples/canon-in-d.lys, whose header advertises a ground "written ONCE and cycled 13
    /// times", emitted <c>\repeat unfold 13 {  }</c> — 53 bars of continuo on the page against
    /// 1 in the twin, so every LilyPond comparison taken through that book compared different
    /// music), and the chord / figure mark registries from all six (2026-10-02: every
    /// <c>@chord</c> in a phrase was reported "dropped" while the twin printed it). A table
    /// declared here reaches every body, because a body is constructed WITH it.
    /// <para>
    /// ⚠️ <see cref="ActivePhrases"/> in particular must be SHARED, not copied: recursion has
    /// to be caught through a container as well (<c>phrase A { tuplet 3/2 { A } }</c>), and
    /// a copy would let the inner reference open the phrase again and expand forever.
    /// </para>
    /// </remarks>
    private sealed class SharedState
    {
        /// <summary>Phrase (and variable) bodies by name, so a bare reference in a section
        /// can be expanded in place.</summary>
        public readonly Dictionary<string, SyntaxNode> Phrases = new(StringComparer.Ordinal);

        /// <summary>References being expanded right now — the cycle guard, the same one
        /// MusicXmlExporter and MidiExporter keep for the same reason.</summary>
        public readonly HashSet<string> ActivePhrases = new(StringComparer.Ordinal);

        /// <summary>The source position of every <c>@chord</c> mark a ChordNames stream
        /// carries (<see cref="EmitInlineChordTracks"/>), and of every <c>@figuredBass</c>
        /// mark a FiguredBass stream carries (<see cref="EmitFiguredBassTracks"/>): what tells
        /// <see cref="EmitMark"/> the symbol is already on the page.</summary>
        public readonly HashSet<int> InlineChordMarks = new();
        public readonly HashSet<int> FigureMarks = new();

        /// <summary>The grace skips every voice owes LilyPond's grace synchronisation
        /// (<see cref="CollectGraceSync"/>): by a written event's source position, one entry per
        /// time the page plays it, in the page's order — the length of the skip to write in
        /// front of it (zero where none is owed). <see cref="GraceSyncSeen"/> counts the
        /// occurrences written so far, across every nested body.</summary>
        public readonly Dictionary<int, List<Fraction>> GraceSyncPads = new();
        public readonly Dictionary<int, int> GraceSyncSeen = new();

        /// <summary>The same for a bar of silence the twin writes as one spacer, by part and
        /// <see cref="EmptyBarKey"/>; and which section each padding bar line the form walk
        /// makes stands for (<see cref="AppendSilentPlay"/>, <see cref="PaddingBars"/>), by its green.</summary>
        public readonly Dictionary<(string Part, string Key), List<Fraction>> GraceSyncEmptyBars = new();
        public readonly Dictionary<(string Part, string Key), int> GraceSyncEmptyBarsSeen = new();
        public readonly Dictionary<object, string> PaddingBarSections = new(ReferenceEqualityComparer.Instance);

        /// <summary>What the page pairs with a <c>@niente</c> (<see cref="CollectNiente"/>), by
        /// source position: the hairpin marks whose tip is circled, the nientes such a circle
        /// stands for, and the nientes that end a hairpin.</summary>
        public readonly HashSet<int> CircledHairpinMarks = new();
        public readonly HashSet<int> CircledNientes = new();
        public readonly HashSet<int> NienteEnds = new();

        /// <summary>Diagnostics collected while exporting (constructs dropped because they
        /// are deprecated or out of scope, and the like). Not fatal.</summary>
        public readonly List<string> Warnings = new();

        /// <summary>The exported file's root, for the tuning a chord diagram or a shape chord
        /// draws on (<see cref="Semantics.ChordDiagramScores"/>); null before an export.</summary>
        public SyntaxNode? Root;
    }

    private readonly SharedState _shared;

    public LilyPondExporter() : this(new SharedState()) { }

    /// <summary>A nested-body exporter: the same tables as the exporter opening it.</summary>
    private LilyPondExporter(SharedState shared)
    {
        _shared = shared;
        _warnings = shared.Warnings;
    }

    /// <summary>
    /// The section-header registry keyed by section NAME (<see cref="Semantics.SectionHeaders"/>,
    /// the collector's rule in its one spelling): any declaration of the name WITHOUT inline
    /// music registers its direct-child directives, first declaration wins per directive, and
    /// they apply to every play of that name. Keyed by name because a section reaches the
    /// form in SPLIT declarations too — <c>section A { partial 8 }</c> beside
    /// <c>part melody { section A { … } }</c> — and the played declaration is not the one
    /// holding the header. Reading the header off the chosen declaration alone
    /// (SectionHeaderMusic) lost that pickup: the twin of
    /// scratch/ベースタブLy/blogger2.lys carried no <c>\partial</c> at all (第99 handoff ③).
    /// </summary>
    private Semantics.SectionHeaders _sectionHeaders = Semantics.SectionHeaders.Empty;

    /// <summary>Sections standing in for the single-part shorthand, so
    /// <see cref="ContainerMusic"/> knows to take only their LOOSE music and leave any other
    /// part's cell alone. Identity, not name: the same section can be a container here and a
    /// mere holder of somebody else's cell for the next part.</summary>
    private readonly HashSet<SyntaxNode> _looseSections = new();

    /// <summary>True while a CHORD track is being flattened and written: the form walk
    /// then appends a section's chord bars instead of its music, and the item emitter
    /// writes <c>\chordmode</c> bars (see <see cref="EmitChordTracks"/>).</summary>
    private bool _chordTrack;

    /// <summary>True once a stream was found to open with a REWIND — a repeat body with no
    /// written <c>|:</c> at moment 0 (<see cref="EmitRewindRepeat"/>) — so the score's
    /// <c>printInitialRepeatBar</c> is written off, as the page draws no opener there.</summary>
    private bool _rewindOpensThePiece;

    /// <summary>The <c>\chordmode</c> variable written for each chord part a row names
    /// (part name → variable), filled by <see cref="EmitChordTracks"/> and read by
    /// <see cref="EmitScore"/>.</summary>
    private readonly Dictionary<string, string> _chordVars = new(StringComparer.Ordinal);

    /// <summary>Chord symbols already warned about (one warning per spelling, not per bar).</summary>
    private readonly HashSet<string> _chordWarned = new(StringComparer.Ordinal);

    /// <summary>The <c>\chordmode</c> variable written for each part's INLINE <c>@chord</c>
    /// marks (part name → variable), filled by <see cref="EmitInlineChordTracks"/>; the
    /// parts whose ChordNames row <see cref="EmitScore"/> has already placed.</summary>
    private readonly Dictionary<string, string> _inlineChordVars = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inlineChordPlaced = new(StringComparer.Ordinal);

    /// <summary>The part whose music variable is being written — what tells
    /// <see cref="EmitMark"/> whether this part's <c>@chord</c> marks reach the twin.</summary>
    private string? _currentPartName;

    /// <summary>Diagnostics collected while exporting (e.g. constructs dropped
    /// because they are deprecated or out of scope). Not fatal.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// The <c>form</c> this twin renders, or null for the default
    /// (<see cref="LilySharp.Core.Semantics.ScoreForms.Primary"/>).
    /// </summary>
    /// <remarks>
    /// The twin writes one <c>\score</c>, so a file with several movements takes one export
    /// per movement (<c>lysc ly --score</c> / <c>--all</c>). LilyPond can hold several
    /// <c>\score</c> blocks in one file; Lily# writes one file per score instead, which is
    /// what <c>lysc svg --all</c> already does and keeps the twin comparable score for score.
    /// </remarks>
    public FormDeclarationSyntax? Form { get; init; }

    /// <summary>
    /// The <c>score</c> declaration this twin writes — its staves, its <c>fonts</c> and
    /// <c>layout</c> plans, its instrument names, and (when <see cref="Form"/> is null) its
    /// form. Null for the default: the file's first <c>score</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <see cref="Form"/> alone cannot say which score: a book of <c>score</c>,
    /// <c>score both { staff … tab … }</c> and <c>score tab { tab … }</c>
    /// names ONE form three times. Until 2026-09-25 the twin read the file's first
    /// <c>score</c> whatever it was asked for, so <c>lysc ly --all</c> wrote that book's
    /// staff-only score three times over — the tab score's twin had no TabStaff (found
    /// while comparing the tab corpus with its twins, HANDOFF §2 T7).
    /// </remarks>
    public RenderDeclarationSyntax? Score { get; init; }

    /// <summary>
    /// Write a <c>\paper</c> block pinning the twin's serif and sans faces to LilyPond's
    /// bundled ones (<c>lysc ly --pin-fonts</c>), and turn LilyPond's tagline off — the one
    /// footer a measured page would carry that no Lily# page has (session 851). Off by
    /// default: the twin is a control
    /// laid out on LilyPond's own paper, and the corpus's twins must not all move for a
    /// measuring convenience.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly — property-defaults.fonts.serif and
    /// property-defaults.fonts.sans (the Fonts block, ly:get-option 'backend): under the
    /// svg backend ONLY, LilyPond 2.26 sets them
    /// to the generic names "serif" / "sans", which fontconfig resolves to whatever the
    /// machine prefers (Noto, DejaVu, Verdana …); every other backend gets "LilyPond Serif"
    /// / "LilyPond Sans Serif". A twin measured through <c>-dbackend=svg</c> therefore
    /// reads machine-dependent text widths (a chord "Am" 4.336 against the canonical 3.926,
    /// a title baseline 0.21 off — sessions 350 and 367 both mistook that for a residual)
    /// unless the two lines the LP-fidelity probes carry are written. Owner's decision
    /// 2026-09-11 (session 368): opt-in on the CLI, never the default.
    /// </remarks>
    public bool PinFonts { get; init; }

    /// <summary>Exports the tree and returns the complete <c>.ly</c> text.</summary>
    public string Export(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        _rePedals = RePedalStarts(root);

        // Every section's voices and canonical bar count (SectionBarCounts, the semantic
        // counter): a voice that writes fewer bars than its section-mates is padded with
        // silent bars when its play is appended (PaddingBars), as the page pads its staff.
        _sectionBars = Svg.Collector.SectionBarCounts.BuildSemanticIndex(root);
        _phraseBodies = Svg.Collector.SectionBarCounts.PhraseBodies(root);
        // The meters other parts write into each section's bars (HANDOFF §2 F-partmeter ⒜):
        // a voice's silent bars are written in them (AppendSilentPlay, PaddingBars).
        _meterPlan = Svg.Collector.SectionMeterPlan.Build(root, _phraseBodies);

        // Octave mode is a file-level directive; default is relative (Lily#'s default).
        var octaveDir = root.DescendantNodes<OctaveDirectiveSyntax>().FirstOrDefault();
        _octaveAbsolute = octaveDir?.IsAbsolute ?? false;

        // The key a part starts in, before any section header or mid-stream change.
        _homeTonic = ScoreHomeKey.Read(root);
        _homeKeySharps = ScoreHomeKey.Sharps(root);
        _homeKeyNode = ScoreHomeKey.Declaration(root);

        // The file's top-level `clef` — the clef of every part that names none (PartClefWord).
        _fileClef = null;
        foreach (var child in root.ChildNodes())
            if (child is ClefDeclarationSyntax fileClef)
                _fileClef = fileClef.ClefName.Text;

        // …and the meter the same boundary reverts to, read the same way (with the numerator
        // as the home node writes it, so a `3+2/8` home is compared and restored as written).
        var (homeBeats, homeBeatType) = ScoreHomeMeter.Read(root);
        _homeTimeNode = ScoreHomeMeter.Declaration(root);
        _bars.HomeMeter = new Semantics.Meter(homeBeats, homeBeatType,
            _homeTimeNode is { IsSenzaMisura: false } homeTime ? homeTime.BeatsText : null);
        _bars.HomeSenzaMisura = _homeTimeNode?.IsSenzaMisura ?? false;
        _bars.RevertToHome();

        CollectPhrases(root);

        var render = Score ?? root.DescendantNodes<RenderDeclarationSyntax>().FirstOrDefault();
        _scoreForm = Score is { } score ? Svg.Collector.RenderSpecParser.Parse(score)?.Form : null;
        // The score's resolved fonts plan — for the SIZE and STYLE attributes the twin
        // writes as overrides (EmitFontOverrides); the faces stay unwritten (EmitHeader).
        _fontPlan = ResolveFontPlan(tree, root, render);
        // The score's resolved layout plan — the bar-number policy the twin writes into
        // its \layout block, and the `markTempo beside` it can only warn about.
        _layoutPlan = ResolveLayoutPlan(root, render);
        // …and what a chord diagram's tuning is read against (the parts' instruments, the
        // staff a row stands over — ChordDiagramScores).
        _shared.Root = root;
        _renderSpec = render != null ? Svg.Collector.RenderSpecParser.Parse(render) : null;

        EmitHeader(root);

        var parts = root.DescendantNodes<PartDeclarationSyntax>().ToList();
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().ToList();
        var form = PrimaryForm(root);
        CollectInstrumentNames(tree);
        // Before the part variables: EmitMark asks whether a part's @chord marks have a
        // ChordNames stream of their own while it writes that part's music.
        EmitInlineChordTracks(tree, render);
        EmitFiguredBassTracks(tree, render);
        EmitLyricTracks(tree, render);
        EmitLeadSheetTiming(tree, render);
        // Before the part variables: every event a grace on another voice stands beside owes
        // a grace skip, which the music walk writes in front of it (EmitItemCore).
        CollectGraceSync(tree, render);
        // Before the part variables too: a niente's hairpin is written with its circled tip.
        CollectNiente(tree, render);

        // One music variable per part. A by-part score keeps its sections inside
        // the part block; the form orders them.
        var partVars = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = PartNames(parts, render);
        // Which parts a combinedStaff plays, and which any other staff does: a span in a part
        // only the former plays is written as one Voice's music (_combinedPart).
        var combinedNames = new HashSet<string>(StringComparer.Ordinal);
        var otherNames = new HashSet<string>(StringComparer.Ordinal);
        if (render != null)
        {
            foreach (var node in render.DescendantNodes())
            {
                switch (node)
                {
                    case CombinedStaffRenderSyntax c:
                        combinedNames.UnionWith(SharedStaffPartNames(c.PartNames).OfType<string>());
                        break;
                    case CondensedStaffRenderSyntax c:
                        otherNames.UnionWith(SharedStaffPartNames(c.PartNames).OfType<string>());
                        break;
                    case StaffRenderSyntax or TabRenderSyntax:
                        if (RenderPartName(node) is { } n)
                            otherNames.Add(n);
                        break;
                }
            }
        }
        if (names.Count > 0)
        {
            foreach (string name in names)
            {
                _currentPartName = name;
                _combinedPart = combinedNames.Contains(name) && !otherNames.Contains(name);
                if (combinedNames.Contains(name) && otherNames.Contains(name))
                    _warnings.Add(
                        $"part '{name}' is played by a combinedStaff and by another staff — a voice {{ }}"
                        + " span in it is written with \\\\ (separate voices), while the combined staff"
                        + " reads it as one voice's simultaneous music");
                var part = parts.FirstOrDefault(p => p.Name.Text == name);
                ArmPartHome(part);
                string varName = VarName(name);
                partVars[name] = varName;
                // An undeclared part has no `octave` or `instrument` to anchor to, so it takes
                // the same default the collector gives it.
                _anchorOctave = part != null
                    ? AnchorOctaveOf(part)
                    : InstrumentDefaults.DefaultAnchorOctave;
                // The clef a slash note's middle-line pitch is spelled against.
                // Read from the part's own `clef` property (the same source
                // AnchorOctaveOf reads); a preset-implied or staff-level clef is
                // not seen here, which only moves which LINE the twin's slash
                // sits on - Lily#'s page pins it to the middle regardless.
                _lysClef = part != null && PartProperty(part, "clef") is { } partClefWord
                    ? Svg.Collector.MeasureCollector.ParseClefType(partClefWord.ToLowerInvariant())
                    : ClefType.Treble;
                // The RELATIVE anchor above and the ABSOLUTE one here answer different
                // questions and resolve differently: the relative one follows the clef and
                // the instrument preset, the absolute one is middle C unless the part states
                // an `octave N`. Same split the collector makes in GetPartDefaults, and
                // reading them off one value is how the twin came to be an octave out.
                _absoluteBaseOctave = InstrumentDefaults.AbsoluteBaseOctave(
                    part != null ? ExplicitPartOctave(part) : null);
                var music = OrderedMusic(name, part, form, sections);
                if (IsDrumPart(name, music))
                {
                    _drumParts.Add(name);
                    // A drummap block re-tables position / notehead / MIDI key for the score
                    // (DrumOverrides). LilyPond spells that as drumPitchTable and
                    // drumStyleTable overrides, which this transpiler does not write — so the
                    // twin plays the DEFAULT kit and is a different page wherever the map bit.
                    if (root.DescendantNodes<DrummapDeclarationSyntax>().Any())
                        _warnings.Add(
                            "drummap { } is not exported — the twin uses LilyPond's default "
                            + "drum table, so any remapped position, notehead or MIDI key differs");
                }
                if (HasStringNumbers(music))
                    _stringNumberParts.Add(name);
                if (HasStrokeFingers(music))
                    _strokeFingerParts.Add(name);
                if (HasOttavaMarks(music))
                    _ottavaParts.Add(name);
                if (HasPedalMarks(music))
                    _pedalParts[name] = (part != null ? PartProperty(part, "pedal") : null)?.ToLowerInvariant() switch
                    {
                        "text" => "text",
                        "mixed" => "mixed",
                        _ => "bracket",   // Staff.ParsePedalStyle's default
                    };
                _drumMode = _drumParts.Contains(name);
                _partTranspose = EffectiveTranspose(root, name, render);
                EmitPartVariable(varName, music, root);
                _drumMode = false;
            }
        }
        else
        {
            // No part at all — neither declared nor named by a score: treat the whole
            // file's music stream as one voice.
            partVars["music"] = "music";
            ArmPartHome(null);
            _lysClef = ClefType.Treble;
            _partTranspose = EffectiveTranspose(root, "music", render);
            EmitPartVariable("music", TopLevelMusic(root), root);
        }

        EmitChordTracks(root, render, form, sections);
        EmitChordList();
        EmitScore(render, parts, partVars);
        // `markTempo beside` is a Lily#-own arrangement (Semantics.MarkArrangement): LilyPond
        // stacks a RehearsalMark over a MetronomeMark and has no chart pair, so the twin
        // keeps LilyPond's picture and says so — the `fonts … size` rule, not a silent drop.
        // Read the way the page reads it (the score's own reference, else the file's default).
        if (_layoutPlan.MarksBeside)
            _warnings.Add("markTempo beside is not exported: the twin stacks the section label over "
                          + "the tempo mark, as LilyPond does — the arrangement has no LilyPond spelling");
        return _sb.ToString();
    }

    // ---- Fonts: size and style ------------------------------------------------

    /// <summary>The plan the exported score resolves its text through — the page's own
    /// (<see cref="PageModel"/>, so the twin and the page cannot read two plans), or the
    /// file's unnamed default when the file has no score block.</summary>
    private Rendering.TextFontPlan _fontPlan = Rendering.TextFontPlan.Default;

    /// <summary>The <c>layout { }</c> plan the exported score resolves to.</summary>
    /// <remarks>
    /// Read from the SOURCE (<see cref="Semantics.LayoutPlanReader.Resolve"/>) rather than
    /// from <see cref="PageModel"/> as the fonts plan is, because it needs no collect: the
    /// reader IS the collector's reading (the collector calls the same one), so the two
    /// cannot disagree either way.
    /// ⚠️ It had to be this way while <c>PageModel</c> collected without the per-score
    /// references — a hole the same session closed (see PageModel's own remark), and the
    /// reason a score's <c>fonts NAME</c> had been invisible to the twin.
    /// </remarks>
    private Semantics.LayoutPlan _layoutPlan = Semantics.LayoutPlan.Default;

    /// <summary>The exported score's spec, for the tuning a chord diagram draws on
    /// (<see cref="Semantics.ChordDiagramScores"/>, with <see cref="SharedState.Root"/>); null
    /// before an export.</summary>
    private Svg.Collector.RenderSpec? _renderSpec;

    /// <summary>
    /// The state a nested body is written AGAINST and cannot move — the PART's: the octave
    /// mode and its anchors, the drum / combined flags, the part's name, and the home key and
    /// meter a section boundary restores. Copied INTO a body's exporter
    /// (<see cref="OpenNested"/>) and never back.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN: carrying AbsoluteBaseOctave is correct by construction. A degree
    /// chord's two uses of it CANCEL — the anchor is base + rootOffset and the written mark is
    /// octave − base — so no nesting of one can see it. The use that does NOT cancel is the
    /// nested <c>\fixed</c> a marked phrase reference emits, which is exactly what a nested
    /// body can do; the observer is AMarkedReference_MovesTheAnchor_WithANestedFixed reached
    /// through a container. SectionOctaveOffset rides with it for the same reason: a nested
    /// body written inside a <c>~B'</c> play sounds where the play sounds.
    /// The part's name and the tree (<see cref="SharedState.Root"/>) are for a chord(…) item
    /// in the body: its notes are on the PART's strings (EmitShapeChord). Measured 2026-09-28:
    /// without them a grace or tuplet body wrote the item on the guitar at sounding pitch, an
    /// octave under the page.
    /// </remarks>
    private readonly record struct PartFrame(
        bool OctaveAbsolute, int AnchorOctave, int AbsoluteBaseOctave, int SectionOctaveOffset,
        bool DrumMode, string? CurrentPartName, bool CombinedPart,
        int HomeKeySharps, KeyTonic HomeTonic,
        Semantics.Meter HomeMeter, bool HomeSenzaMisura, TimeSignatureSyntax? HomeTimeNode,
        KeySignatureSyntax? PartHeaderKeyNode, int RestoreKeySharps);

    /// <summary>
    /// The state a body ADVANCES — the STREAM's: the two octave frames, the running key and
    /// meter, the clef, the improvisation switch, and the note-value memory. Copied into a
    /// body's exporter and, for a body that is plain sequential music on both sides, back out
    /// of it (<see cref="CarryFrameBack"/>) — the same record both ways, so what goes in cannot
    /// be left behind on the way out.
    /// </summary>
    /// <remarks>
    /// The note-value memory goes in with the frame (session 398): a tuplet, cue or repeat
    /// body is sequential music on both sides, so its first bare note reads what the stream
    /// last wrote — and reads it the same way on both sides only if the buffer knows it. A
    /// fresh buffer knew "4" and forced nothing, so <c>c8 tuplet 3/2 { d e f }</c> wrote
    /// <c>d e f</c> bare and LilyPond read them as quavers by luck of the lexical carry, while
    /// a site that had to force a value (a voice branch) then forced the WRONG one after its
    /// tuplet. Sites whose body opens its own memory (a grace at an eighth, a voice branch at
    /// the span's value, a phrase body) overwrite the three after <see cref="OpenNested"/>.
    /// </remarks>
    private readonly record struct StreamFrame(
        bool ImprovisationOpen, ClefType LysClef,
        int LysStep, int LysOctave, int LyStep, int LyOctave, bool FrameTracked,
        int KeySharps, KeyTonic Tonic,
        Semantics.BarContext.MeterState Time,
        string LastWrittenValue, int LastWrittenDots, bool ForceNextDuration);

    private PartFrame CapturePart() => new(
        _octaveAbsolute, _anchorOctave, _absoluteBaseOctave, _sectionOctaveOffset,
        _drumMode, _currentPartName, _combinedPart,
        _homeKeySharps, _homeTonic,
        _bars.HomeMeter, _bars.HomeSenzaMisura, _homeTimeNode,
        _partHeaderKeyNode, _restoreKeySharps);

    private void ApplyPart(in PartFrame f)
    {
        (_octaveAbsolute, _anchorOctave, _absoluteBaseOctave, _sectionOctaveOffset,
         _drumMode, _currentPartName, _combinedPart,
         _homeKeySharps, _homeTonic,
         _, _, _homeTimeNode,
         _partHeaderKeyNode, _restoreKeySharps) = f;
        _bars.HomeMeter = f.HomeMeter;
        _bars.HomeSenzaMisura = f.HomeSenzaMisura;
    }

    private StreamFrame CaptureStream() => new(
        _improvisationOpen, _lysClef,
        _lysStep, _lysOctave, _lyStep, _lyOctave, _frameTracked,
        _keySharps, _tonic,
        _bars.Save(),
        _lastWrittenValue, _lastWrittenDots, _forceNextDuration);

    private void ApplyStream(in StreamFrame f)
    {
        (_improvisationOpen, _lysClef,
         _lysStep, _lysOctave, _lyStep, _lyOctave, _frameTracked,
         _keySharps, _tonic,
         _,
         _lastWrittenValue, _lastWrittenDots, _forceNextDuration) = f;
        _bars.Restore(f.Time);
    }

    /// <summary>
    /// Opens the exporter a nested body — a phrase reference, a tuplet, a voice branch, a
    /// grace, a cue, a repeat — is written into: the same shared tables as this one
    /// (<see cref="SharedState"/>), the part's frame, and the stream's frame as it stands
    /// here. The ONE way to make one; a site whose body opens its own memory sets those
    /// fields after this call.
    /// </summary>
    private LilyPondExporter OpenNested()
    {
        var buf = new LilyPondExporter(_shared);
        buf.ApplyPart(CapturePart());
        buf.ApplyStream(CaptureStream());
        return buf;
    }

    /// <summary>What a nested body's exporter wrote, as one line.</summary>
    private static string NestedText(LilyPondExporter buf)
        => buf._sb.ToString().Replace("\n", " ").Trim();

    /// <summary>
    /// Takes the stream's frame back out of a body that is plain sequential music on BOTH
    /// sides (a tuplet, a cue, a repeat) — the stream continues where the body left off, and
    /// the note after it reads the body's last value on both sides (the page walks the body
    /// inline; LilyPond's parser carries the last value written). Bodies whose frame the two
    /// engines hand over differently (a voice span, a phrase reference) do not call this and
    /// clear <see cref="_frameTracked"/> or set the frame by hand instead; the grace site
    /// calls it and then puts the stream's own note value back, because Lily# does not carry
    /// a grace's out.
    /// </summary>
    private void CarryFrameBack(LilyPondExporter buf) => ApplyStream(buf.CaptureStream());

    private static void AppendToken(StringBuilder line, string tok, string indent)
    {
        if (line.Length > indent.Length) line.Append(' ');
        line.Append(tok);
    }

    private void FlushLine(StringBuilder line, string indent)
    {
        if (line.Length > indent.Length)
        {
            _sb.Append(line.ToString().TrimEnd()).Append('\n');
        }
        line.Clear();
        line.Append(indent);
    }

    /// <summary>
    /// A LilyPond variable name for <paramref name="name"/>, unique within this export.
    /// </summary>
    /// <remarks>
    /// LilyPond variable names are letters only, so each digit is spelled as a word
    /// (<c>fl1</c> → <c>flOne</c>) and any other character is dropped.
    /// ⚠️ Digits used to be DROPPED, so <c>part fl1</c> and <c>part fl2</c> both became
    /// <c>\fl</c>: the twin defined the variable twice, LilyPond kept the last definition,
    /// and every staff played the last part's music — with no warning. 14 of the 599 tracked
    /// books had such a pair (<c>test/instrument-names</c>' violin1/violin2, the part-combine
    /// probes' fl1/fl2…; session 381). The used-name set catches what spelling alone cannot
    /// (<c>a_b</c> and <c>ab</c>, or a part really named <c>flOne</c> beside <c>fl1</c>).
    /// </remarks>
    private string VarName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsLetter(c)) sb.Append(c);
            else if (c is >= '0' and <= '9') sb.Append(DigitWords[c - '0']);
        }
        string baseName = sb.Length == 0 ? "music" : sb.ToString();
        string candidate = baseName;
        for (int n = 2; IsPitchName(candidate) || !_usedVarNames.Add(candidate); n++)
            candidate = baseName + "Var" + NumberWord(n);
        return candidate;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a note name of LilyPond's default (nederlands) input
    /// language — <c>b</c>, <c>fis</c>, <c>es</c>, <c>aes</c>, <c>ceh</c> — which the lexer reads
    /// as a pitch even at the top level, so it cannot be a variable: a part named <c>b</c> gave
    /// `b = \fixed c' { … }` and LilyPond refused it ("syntax error, unexpected NOTENAME_PITCH";
    /// found exporting a two-staff probe, session 832).
    /// The table is exactly each of <c>a</c>…<c>g</c> bare or with <c>is isis es eses ih eh isih
    /// eseh</c>, plus the short <c>as ases es eses</c> — so <c>bs</c> is a free name.
    /// LILYPOND-REF: scm/define-note-names.scm — the nederlands pitch names.
    /// </summary>
    private static bool IsPitchName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            name, "^(?:[a-g](?:is|isis|es|eses|ih|eh|isih|eseh)?|as|ases|es|eses)$");

    private static readonly string[] DigitWords =
        ["Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"];

    /// <summary>Every variable name this export has handed out (<see cref="VarName"/>),
    /// seeded with the names a variable must never take (<see cref="ReservedLilyPondNames"/>).</summary>
    private readonly HashSet<string> _usedVarNames = new(ReservedLilyPondNames, StringComparer.Ordinal);

    /// <summary>
    /// Names a twin's variable must not take, so <see cref="VarName"/> steps past them the way
    /// it steps past a name already used (<c>drums</c> → <c>drumsVarTwo</c>).
    /// </summary>
    /// <remarks>
    /// Two kinds. LilyPond's KEYWORDS cannot be variables at all — `\drums` is the parser's
    /// DRUMS token, so a part named <c>drums</c> gave `drums = \drummode { … }` and `{ \drums }`,
    /// which LilyPond refused ("syntax error, unexpected '}'"; found exporting a big-band probe,
    /// 2026-09-26). And the COMMANDS this exporter writes: a variable of that name would
    /// shadow the command for the rest of the file (a part named <c>bar</c> and the twin's own
    /// `\bar "|."`). Lily# reserves some of these words itself; the rest are ordinary part names.
    /// LILYPOND-REF: lily/lily-lexer.cc:51-95 the_key_tab (the keywords).
    /// </remarks>
    private static readonly string[] ReservedLilyPondNames =
    [
        // lily/lily-lexer.cc the_key_tab
        "accepts", "addlyrics", "alias", "alternative", "book", "bookpart", "change", "chordmode", "chords",
        "consists", "context", "default", "defaultchild", "denies", "description", "drummode",
        "drums", "etc", "figuremode", "figures", "header", "layout", "lyricmode", "lyrics",
        "lyricsto", "markup", "markuplist", "midi", "name", "new", "notemode", "override", "paper",
        "remove", "repeat", "rest", "revert", "score", "sequential", "set", "simultaneous",
        "tempo", "type", "unset", "with",
        // commands and music functions this exporter writes
        "accent", "acciaccatura", "accidentalStyle", "appoggiatura", "arpeggio", "bar",
        "bendAfter", "bold", "box", "break", "breathe", "cadenzaOff", "cadenzaOn", "caesura",
        "clef", "codaMark", "concat", "cueClef", "cueClefUnset", "deadNote", "downbow", "fermata",
        "fixed", "flageolet", "fontsize", "glissando", "grace", "hspace", "improvisationOff",
        "improvisationOn", "italic", "jump", "key", "laissezVibrer", "major", "marcato", "mark",
        "mordent", "noBreak", "nonArpeggiato", "noPageBreak", "note", "omit", "once", "ottava",
        "pageBreak", "partCombine", "partial", "portato", "prall", "prallprall", "relative",
        "repeatTie", "reverseturn", "rhythm", "rightHandFinger", "segnoMark", "skip", "smaller",
        "snappizzicato", "sostenutoOff", "sostenutoOn", "staccatissimo", "staccato",
        "startTextSpan", "startTrillSpan", "stopTextSpan", "stopTrillSpan", "sustainOff",
        "sustainOn", "tabFullNotation", "tenuto", "time", "transpose", "treCorde", "trill",
        "tuplet", "turn", "tweak", "unaCorda", "upbow", "version",
        // the dynamics it writes by name
        "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "sf", "sfz", "fp", "rfz", "fz",
    ];

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

/// <summary>
/// Zero-width sentinel the form expansion plants at each section PLAY — the twin's
/// section boundary. Carries what the collector decides at the same spot
/// (MeasureCollector's reference arms): the occurrence's display label (null =
/// silent/suppressed — no mark) and whether a section-header key follows (which
/// overrides the score-key restore, the collector's else-if).
/// <c>LilyPondExporter.EmitSectionPlay</c> is the one consumer. Same shape as the
/// collector's <c>RelativeResetMarker</c>: a synthetic red over a zero-width green,
/// so it can travel a <c>List&lt;SyntaxNode&gt;</c> stream.
/// ⚠️ The DATA lives on the green (<see cref="SectionPlayGreen"/>), not the red:
/// <c>CreateEnding</c> rebuilds a volta ending's items as GREENS, and the red the
/// rebuilt tree hands back is a plain <c>GenericSyntaxNode</c> — the emitter matches
/// the green's type, which survives the rebuild, where a red-held payload silently
/// did not (the endings' marks and score-key restore were the exporter's last
/// silent-drop hole).
/// </summary>
internal sealed class SectionPlayMarker : SyntaxNode
{
    public SectionPlayMarker(string? markLabel, bool hasHeaderKey, bool hasHeaderTime,
        int octaveOffset = 0)
        : base(new SectionPlayGreen(markLabel, hasHeaderKey, hasHeaderTime, octaveOffset),
            parent: null, position: 0)
    {
    }
}

/// <summary>
/// One bar of a chord track, pre-spelled as <c>\chordmode</c> entries
/// (<c>LilyPondExporter.ChordBars</c>). A stream item like <see cref="SectionPlayMarker"/>,
/// carried on its green for the same reason: a form ending rebuilds its items as greens.
/// </summary>
internal sealed class ChordBarMarker : SyntaxNode
{
    public ChordBarMarker(string text)
        : base(new ChordBarGreen(text), parent: null, position: 0)
    {
    }
}

/// <summary>
/// "The voice's last bar is closed here" — a stream item that writes nothing and resets the
/// empty-bar rule's clock (<c>LilyPondExporter.PaddingBars</c>). Matched by its green, like
/// <see cref="SectionPlayMarker"/>, so it survives a form ending's green rebuild.
/// </summary>
internal sealed class ClosedBarMarker : SyntaxNode
{
    public ClosedBarMarker()
        : base(new ClosedBarGreen(), parent: null, position: 0)
    {
    }
}

/// <summary>
/// "The next note or chord takes \repeatTie" — a tie carried over a repeat reaches it from a
/// play not printed before it (<c>LilyPondExporter.RepeatTiePlays</c>). Matched by its green,
/// like <see cref="SectionPlayMarker"/>, so it survives a form ending's green rebuild.
/// </summary>
internal sealed class RepeatTieMarker : SyntaxNode
{
    public RepeatTieMarker()
        : base(new RepeatTieGreen(), parent: null, position: 0)
    {
    }
}

/// <summary>The repeat-tie marker's green (see <see cref="RepeatTieMarker"/>).</summary>
internal sealed class RepeatTieGreen : InternalSyntax.GreenNode
{
    public RepeatTieGreen()
        : base(SyntaxKind.None, fullWidth: 0)
    {
    }
}

/// <summary>The closed-bar marker's green (see <see cref="ClosedBarMarker"/>).</summary>
internal sealed class ClosedBarGreen : InternalSyntax.GreenNode
{
    public ClosedBarGreen()
        : base(SyntaxKind.None, fullWidth: 0)
    {
    }
}

/// <summary>
/// "Give the volta bracket starting here this end shape and length" — the overrides a form
/// ending's <c>-]</c> / <c>voltaBracket</c> asks of LilyPond (<c>LilyPondExporter.VoltaShapeFor</c>).
/// Matched by its green, like <see cref="SectionPlayMarker"/>, so it survives the ending's
/// green rebuild; written where the alternative's music starts.
/// </summary>
internal sealed class VoltaShapeMarker : SyntaxNode
{
    public VoltaShapeMarker(VoltaShapeGreen green)
        : base(green, parent: null, position: 0)
    {
    }
}

/// <summary>The volta-shape marker's green (see <see cref="VoltaShapeMarker"/>).</summary>
internal sealed class VoltaShapeGreen : InternalSyntax.GreenNode
{
    /// <summary>The right end hooks down (<c>]</c>, not cut short).</summary>
    public bool Hooked { get; }
    /// <summary>No <c>:|</c> follows the ending (LilyPond's bar there does not hook).</summary>
    public bool LastEnding { get; }
    /// <summary><c>voltaBracket line</c>.</summary>
    public bool FirstSystemOnly { get; }
    /// <summary><c>voltaBracket N</c> when N bars stop before the ending's end, else 0.</summary>
    public int CutBars { get; }
    /// <summary>The ending's sections, in order — whose first <see cref="CutBars"/> bars are
    /// measured for LilyPond's <c>musical-length</c> (<c>LilyPondExporter.FirstBarsLength</c>).</summary>
    public IReadOnlyList<string> Sections { get; }

    public VoltaShapeGreen(bool hooked, bool lastEnding, bool firstSystemOnly, int cutBars,
        IReadOnlyList<string> sections)
        : base(SyntaxKind.None, fullWidth: 0)
    {
        Hooked = hooked;
        LastEnding = lastEnding;
        FirstSystemOnly = firstSystemOnly;
        CutBars = cutBars;
        Sections = sections;
    }
}

/// <summary>The chord bar's green — the spelled entries ride here (see <see cref="ChordBarMarker"/>).</summary>
internal sealed class ChordBarGreen : InternalSyntax.GreenNode
{
    /// <summary>The bar's entries, space-separated, without the bar line.</summary>
    public string Entries { get; }

    public ChordBarGreen(string entries)
        : base(SyntaxKind.None, fullWidth: 0)
    {
        Entries = entries;
    }
}

/// <summary>The section-play sentinel's green — the payload rides here so it survives
/// <c>CreateEnding</c>'s green rebuild (see <see cref="SectionPlayMarker"/>).</summary>
internal sealed class SectionPlayGreen : InternalSyntax.GreenNode
{
    /// <summary>The boxed label to engrave, or null for no mark (a silent
    /// <c>~Section</c> play, or an occurrence label written <c>""</c>).</summary>
    public string? MarkLabel { get; }

    /// <summary>True when the play's header registry carries a key — the boundary
    /// then takes THAT key and the score-key restore stays silent.</summary>
    public bool HasHeaderKey { get; }

    /// <summary>True when the play's header registry carries a <c>time</c> — the boundary
    /// then takes THAT meter and the score-meter restore stays silent. The twin of
    /// <see cref="HasHeaderKey"/>: the collector asks both questions at the same spot
    /// (MeasureCollector.ProcessSectionPrologue's header-time / header-key arms), and this
    /// carrier answered only the key until 2026-08-31.</summary>
    public bool HasHeaderTime { get; }

    /// <summary>The net octave shift written on the REFERENCE that opened this play
    /// (<c>~B'</c> = +1). The third thing this carrier had to be told: it was built for the
    /// key, taught the meter on 2026-08-31, and taught this on the same day — each time
    /// because the collector decides something at this spot that the twin could not
    /// re-derive from the flattened node list, which no longer holds the reference.</summary>
    public int OctaveOffset { get; }

    public SectionPlayGreen(string? markLabel, bool hasHeaderKey, bool hasHeaderTime,
        int octaveOffset = 0)
        : base(SyntaxKind.None, fullWidth: 0)
    {
        MarkLabel = markLabel;
        HasHeaderKey = hasHeaderKey;
        HasHeaderTime = hasHeaderTime;
        OctaveOffset = octaveOffset;
    }
}
