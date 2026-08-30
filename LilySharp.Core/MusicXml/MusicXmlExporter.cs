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
    // `octave absolute` ('/, are offsets from a fixed anchor, no carry). The anchor is
    // normally C4 but is set per-member while stacking an arpeggio (`<< … >>`).
    private bool _octaveAbsolute;
    private int _octaveAnchor = 4;

    // The part's RELATIVE-frame anchor (its clef's or preset's octave), and the instrument's
    // written→sounding shift for <transpose>. Both come from the part header; see
    // ApplyPartHeader. ⚠️ _octaveAnchor above is the ABSOLUTE-mode base and is a DIFFERENT
    // rule — only an explicit `octave N` moves it.
    private int _partAnchorOctave = 4;
    private int _partTransposeSemitones;
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

    /// <summary>The document under construction. It is created at the top of the
    /// build and stays non-null for the whole emit phase, so the emit helpers reach
    /// it through this checked accessor: a violated invariant throws a clear error
    /// instead of a bare <see cref="System.NullReferenceException"/>, and the
    /// nullable analysis no longer needs a scattering of null-forgiving <c>!</c>.</summary>
    private MusicXmlDocument Document =>
        _document ?? throw new System.InvalidOperationException(
            "MusicXmlExporter: the document was accessed before the build created it.");

    private int _tempo = 120;
    private int _timeNumerator = 4;
    private string? _timeNumeratorText; // additive meters ("3+2")
    private int _timeDenominator = 4;
    private int _keyFifths = 0;
    private string _keyMode = "major";
    private string _clefSign = "G";
    private int _clefLine = 2;
    private int? _clefOctaveChange; // ±1 for the _8 / ^8 clefs
    // The clef the walk reads in, as a TYPE — the sign/line pair above cannot tell
    // `treble` from `treble_8`, and the frame rule turns on exactly that difference.
    private LilySharp.Core.Svg.Model.ClefType _currentClefType = LilySharp.Core.Svg.Model.ClefType.Treble;

    // What the document has already SAID about key / time / clef, so a change can be
    // told from a repeat. Until 2026-08-17 nothing here was written twice: the opening
    // measure carried an <attributes> and every later key, time and clef change was
    // dropped, which is how a 3/4 bar came out declared 4/4.
    // ⚠️ THIS IS A SECOND SPELLING of what the measures already hold (RULES §7.7), kept
    // deliberately: the document's own copy is spread over the part's measures, and
    // finding the last one that stated a key means walking back through them on every
    // change. It goes away the day a measure can carry its attributes as a delta of the
    // one before it. Observed by MusicXmlAttributeChangeTests, whose last case fails if
    // this record is ever ahead of or behind what the measures say.
    private (int Fifths, string? Mode, string? Custom)? _writtenKey;
    private (int Beats, string? BeatsText, int BeatType, bool Senza)? _writtenTime;
    private (string Sign, int Line, int? OctaveChange)? _writtenClef;
    // The score's own signature, captured after the metadata pass.
    private (int Fifths, string Mode, string? Custom)? _homeKey;
    // A change seen after the bar had started. The measure carries ONE attributes slot,
    // rendered at its head, so writing it here would move the change a bar early; it
    // waits for the next measure instead.
    private bool _attributesDirty;
    private bool _timeSenzaMisura;  // time none
    private string? _keyCustomXml;  // non-traditional key (encoded pairs)
    private string? _noteFrameSpec; // @frame(...) on the note being written
    private MusicXmlNote? _lastPitchedNote; // hammer-on/pull-off start anchor
    private string? _pendingLineStop;       // "glissando" | "slide": stop lands on the NEXT note
    private string? _chordArpeggio;         // "arpeggiate" | "non-arpeggiate" for the chord being written
    private readonly List<MusicXmlNote> _chordMembers = new(); // members of the chord being written
    private readonly List<MusicXmlNote> _lastEmittedNotes = new(); // last note (1) or chord (N) — a following '~' node ties all of them
    // The notes carrying a tie-start that nothing has ended yet. Kept as the NOTES and
    // not as a flag, because the pair is decided at the NEXT onset: a start is written
    // before its continuation is known, so an unmatched one has to be taken back.
    private readonly List<MusicXmlNote> _tieOpen = new();
    private string? _pendingDynamic;

    // Track parts across sections for multi-section support
    private readonly Dictionary<string, MusicXmlPart> _partsByName = new();

    // Part-option transpose for the part being written: the WRITTEN pitch is
    // respelled and the key signature shifts with it.
    private SyntaxNode? _root;
    private (int step, int alt, int oct)? _currentTranspose;

    /// <summary>Applies the chromatic transpose to an already-absolute written pitch —
    /// the ONE funnel for notes and degree members alike. (A phrase-scoped DIATONIC
    /// shift was applied here first, in the written key, until the reference interval
    /// argument that armed it was removed 2026-08-28.)</summary>
    private (int step, int alter, int octave) ApplyWrittenTransforms(int step, int alter, int octave)
    {
        if (_currentTranspose is { } tr)
            (step, alter, octave) = PitchTransposer.Transpose(step, alter, octave, tr.step, tr.alt, tr.oct);
        return (step, alter, octave);
    }

    // Phrase auto-transpose (movable motif): a phrase written in the score's home
    // key is respelled into whatever key is in effect where it is referenced.
    // _ambientTonic tracks the running key (reset to home per voice and section,
    // advanced by key changes); the reference composes the home→ambient interval
    // onto _currentTranspose for the phrase body.
    private KeyTonic _homeTonic = KeyTonic.CMajor;
    private KeyTonic _ambientTonic = KeyTonic.CMajor;

    // Variable/phrase resolution
    private readonly Dictionary<string, SyntaxNode> _variables = new();
    // Phrases open on the current expansion chain — a reference to one already
    // active is a cycle (x -> y -> x, or longer) and is NOT re-expanded, so a
    // recursive phrase renders its acyclic prefix instead of overflowing the stack.
    // The cycle itself is reported once by PhraseCycleValidator. Matches the MIDI
    // exporter's _activePhrases and the collector's ExpandVariable guard.
    private readonly HashSet<string> _activePhrases = new();

    // drummap { } per-score overrides, built lazily off the root.
    private Dictionary<string, DrumInfo>? _drumOverridesCache;
    private bool _drumOverridesBuilt;
    private Dictionary<string, DrumInfo>? DrumOverridesMap
    {
        get
        {
            if (!_drumOverridesBuilt && _root != null)
            {
                _drumOverridesCache = DrumOverrides.Build(_root);
                _drumOverridesBuilt = true;
            }
            return _drumOverridesCache;
        }
    }

    /// <summary>
    /// The <c>form</c> to write, or null for the default (<see cref="ScoreForms.Primary"/>).
    /// </summary>
    /// <remarks>
    /// One document carries one arrangement, the way one <c>.mid</c> does, so a file with
    /// several movements takes one export per movement (<c>lysc xml --score</c> /
    /// <c>--all</c>).
    /// </remarks>
    public FormDeclarationSyntax? Form { get; init; }

    /// <summary>
    /// The <c>score</c> being written, or null to resolve it from the tree (the one whose
    /// form is being written, else the first).
    /// </summary>
    public RenderSpec? Score { get; init; }

    /// <summary>
    /// The part a section that declares no <c>partName { }</c> block belongs to: the one
    /// part the score engraves, or null when the score names none or several.
    /// </summary>
    /// <remarks>
    /// The same reading the MIDI takes, and for the same reason — a bare section is music no
    /// block claims, and <c>score main { staff bl }</c> is the only statement of whose it is.
    /// ⚠️ Only when the score names exactly ONE part: two parts means the page draws the same
    /// music in two registers and a single stream cannot be both.
    /// </remarks>
    private string? _bareSectionOwner;

    /// <summary>Exports the tree to a MusicXML file at <paramref name="path"/> and
    /// returns a summary (part / measure counts). The intermediate document model is
    /// an implementation detail.</summary>
    public (int Parts, int Measures) ExportToFile(SyntaxTree tree, string path)
    {
        var doc = Export(tree);
        doc.Save(path);
        return (doc.Parts.Count, doc.Parts.Sum(p => p.Measures.Count));
    }

    internal MusicXmlDocument Export(SyntaxTree tree)
    {
        _document = new MusicXmlDocument();

        var root = tree.GetRoot();
        _root = root;
        _bareSectionOwner = RenderSpecParser.SingleEngravedPart(tree, Score, Form);
        _homeTonic = ScoreHomeKey.Read(root);
        _ambientTonic = _homeTonic;

        // Check if there are section declarations (multi-part)
        var hasSections = root.DescendantNodes().OfType<SectionDeclarationSyntax>().Any();

        if (!hasSections)
        {
            // Simple single-part mode — collect metadata first, then process music
            CollectMetadata(root);
            _ambientTonic = _homeTonic; // CollectMetadata walked every key; re-arm
            _currentPart = new MusicXmlPart { Name = "Part 1" };
            Document.Parts.Add(_currentPart);
            StartNewMeasure(addAttributes: true);
            ProcessNode(root);
            FlushCurrentMeasure();
        }
        else
        {
            // Multi-section mode: collect metadata first, then process sections
            CollectMetadata(root);
            _ambientTonic = _homeTonic; // CollectMetadata walked every key; re-arm
            // The score's own signature, now that the pass takes only the top-level ones.
            // A section reverts to it before stating a key of its own, the way its
            // auto-transpose baseline reverts to _homeTonic just above.
            _homeKey = (_keyFifths, _keyMode, _keyCustomXml);
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
                // ⚠️ ONLY THE TOP-LEVEL ONES. This pass runs to the end of the file before a
                // single note is written, so an unguarded case left the document's opening
                // <attributes> holding the LAST value in the source rather than the first:
                // `test/keysig-treble` (D major, then G, then F) opened in F, and
                // `test/section-meter-resets-to-global` declared 4/4 over a 3/4 bar.
                // A key / time / clef inside a section, phrase or part block is a CHANGE,
                // written from its own position by the walk (SyncAttributes) — the same
                // split the collector makes, and for the same reason.
                case TimeSignatureSyntax timeSig:
                    if (!IsInsideMusicContent(timeSig)) ProcessTimeSignature(timeSig);
                    break;
                case TempoDeclarationSyntax tempo:
                    if (!IsInsideMusicContent(tempo)) ProcessTempo(tempo);
                    break;
                case KeySignatureSyntax key:
                    if (!IsInsideMusicContent(key)) ProcessKeySignature(key);
                    break;
                case ClefDeclarationSyntax clef:
                    if (!IsInsideMusicContent(clef)) ProcessClef(clef);
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
        var sectionDecls = root.DescendantNodes().OfType<SectionDeclarationSyntax>().ToList();
        // The form the caller asked for, else the primary one (`main`, else the first).
        var structure = Form ?? LilySharp.Core.Semantics.ScoreForms.Primary(root);

        if (structure == null)
        {
            // No structure block: emit the sections in declaration order.
            foreach (var section in sectionDecls)
                EmitSection(section);
            return;
        }

        // A structure block carries the real playing ORDER and the REPEATS. Emit the
        // sections it names, in its order (so replays reappear), and bracket each
        // repeat span with forward/backward repeat barlines. Without this the MusicXML
        // was just the raw sections in declaration order, with no repeats at all.
        var byName = new Dictionary<string, List<SectionDeclarationSyntax>>(StringComparer.Ordinal);
        foreach (var s in sectionDecls)
        {
            if (!byName.TryGetValue(s.SectionName, out var list))
                byName[s.SectionName] = list = new List<SectionDeclarationSyntax>();
            list.Add(s);
        }
        WalkForm(structure, byName);
    }

    // The section's OWN key, if it states one beside its part blocks
    // (`section A { key d major  melody { … } }`). ⚠️ It sits outside every part block, so
    // the per-part music walk never reaches it — the collector says the same thing in the
    // same words (MeasureCollector.Form.cs, "NOT reached by the per-part music walk") and
    // applies it from there. Until 2026-08-17 this exporter did not, so a section key
    // changed neither the signature nor the phrase auto-transpose baseline: measured on
    // `test/keysig-treble`, whose three sections all came out in one key.
    private KeySignatureSyntax? _sectionKey;

    private void EmitSection(SectionDeclarationSyntax section)
    {
        // A section is self-contained: its phrase auto-transpose baseline reverts
        // to the score's home key (a mid-section modulation cannot leak out).
        _ambientTonic = _homeTonic;
        _sectionKey = null;
        for (int i = 0; i < section.SlotCount; i++)
            if (section.GetChild(i) is KeySignatureSyntax sk)
                _sectionKey = sk;

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
            // No nested part blocks: the section holds its music INLINE — a part-major
            // `part m { section A { … } }` cell, or a standalone section (default part).
            // Emit it under the enclosing part's name; its lyrics map onto those notes.
            EmitPartMajorSection(section);
        }
    }

    private void EmitSectionByName(Dictionary<string, List<SectionDeclarationSyntax>> byName, string name)
    {
        if (byName.TryGetValue(name, out var list))
            foreach (var section in list)
                EmitSection(section);
    }

    // Segno / coda jump TARGETS wait here for the next section, whose first
    // measure they open (they mark where a jump lands).
    private readonly List<System.Xml.Linq.XElement> _pendingTargetDirections = new();

    /// <summary>Emits the structure's items in order: a section reference plays its
    /// section, a repeat block brackets its span with repeat barlines, a volta writes
    /// its <c>&lt;ending&gt;</c> brackets (<see cref="EmitVoltaRepeatBlock"/>) and a nav
    /// mark its <c>&lt;segno&gt;</c> / <c>&lt;coda&gt;</c> / <c>&lt;words&gt;</c>
    /// direction (<see cref="ApplyNavMark"/>, all ten <see cref="NavigationMarkType"/>),
    /// and a <c>_"text"</c> directive its own <c>&lt;words&gt;</c>
    /// (<see cref="ApplyCustomText"/>).</summary>
    private void WalkForm(SyntaxNode container, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        _pendingTargetDirections.Clear();
        foreach (var item in FormWalk.Read(container))
        {
            switch (item)
            {
                // A plain and a silent (~) reference are the same emission — this
                // exporter writes no section label, so the tilde has nothing to hide.
                case FormWalk.SectionRef s:
                    EmitWithPendingTargets(() => EmitSectionByName(byName, s.Name));
                    break;
                case FormWalk.Repeat rb:
                    EmitWithPendingTargets(() => EmitRepeatBlock(rb, byName));
                    break;
                case FormWalk.Ending alt:
                    EmitWithPendingTargets(() => EmitSectionByName(byName, alt.Node.SectionName.Text));
                    break;
                case FormWalk.Other { Node: NavigationMarkSyntax nav }:
                    ApplyNavMark(nav.MarkType);
                    break;
                case FormWalk.Other { Node: CustomTextSyntax custom }:
                    ApplyCustomText(custom.Text);
                    break;
                // A ':|' written in the form itself, outside any '|: … :|' block. It caps
                // the section just played, on every part — the barline is a score-level
                // object (MeasureCollector.SynchronizeBarlines), so it is not one part's.
                // A backward repeat with no matching forward one is MusicXML's own spelling
                // for "repeat from the beginning", which is the reading this grammar gives
                // a one-sided ':|', so nothing extra has to be written to say it.
                case FormWalk.LoneRepeatEnd:
                    foreach (var p in Document.Parts)
                        if (p.Measures.Count > 0)
                            p.Measures[^1].RepeatBackward = true;
                    break;
            }
        }
    }

    /// <summary>Runs <paramref name="emit"/>, then opens each pending jump target
    /// (segno / coda) on the FIRST measure it produced.</summary>
    private void EmitWithPendingTargets(System.Action emit)
    {
        if (_pendingTargetDirections.Count == 0)
        {
            emit();
            return;
        }
        var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
        emit();
        foreach (var p in Document.Parts)
        {
            int si = startIdx.GetValueOrDefault(p);
            if (p.Measures.Count <= si)
                continue;
            for (int k = _pendingTargetDirections.Count - 1; k >= 0; k--)
                p.Measures[si].Notes.Insert(0,
                    new MusicXmlNote { RawElement = new System.Xml.Linq.XElement(_pendingTargetDirections[k]) });
        }
        _pendingTargetDirections.Clear();
    }

    /// <summary>Places a structure navigation mark. Targets (segno / coda) are held
    /// for the next section's start; jump-from instructions (fine, to coda, D.C.,
    /// D.S. …) attach to the end of the section just played.</summary>
    private void ApplyNavMark(NavigationMarkType type)
    {
        var (dir, isTarget) = BuildNavDirection(type);
        if (dir == null)
            return;
        if (isTarget)
        {
            _pendingTargetDirections.Add(dir);
        }
        else
        {
            foreach (var p in Document.Parts)
                if (p.Measures.Count > 0)
                    p.Measures[^1].Notes.Add(
                        new MusicXmlNote { RawElement = new System.Xml.Linq.XElement(dir) });
        }
    }

    /// <summary>Places a form-level <c>_"text"</c> directive as a plain
    /// <c>&lt;words&gt;</c> direction, BELOW the staff, on the last measure emitted.</summary>
    /// <remarks>
    /// Neither the measure nor the side is chosen here; both are read off the engine that
    /// already draws this node.
    /// <list type="bullet">
    /// <item>MEASURE — MeasureCollector states the rule in as many words: free text between
    /// sections, "engraved like the jump-from navigation text at the END of the section just
    /// played", and it gives the item the same measure index those marks get
    /// (<c>CurrentMeasureIndex - 1</c>). That is what <see cref="ApplyNavMark"/>'s non-target
    /// branch already does here, so the attachment is shared rather than reinvented.</item>
    /// <item>SIDE — NOT shared, which is worth saying precisely because of the resemblance
    /// above. CustomTextEngraver's baseline is <c>2.0 - 5.5</c> Y-up from the staff middle,
    /// i.e. BELOW the staff; MusicMarkEngraver's is <c>2.0 - (-2.0)</c>, ABOVE it. The nav
    /// marks are rightly <c>placement="above"</c> and this one is <c>"below"</c>. Copying
    /// the whole of ApplyNavMark put it on the wrong side of the staff in the first draft,
    /// and nothing else in the suite reads placement.</item>
    /// </list>
    /// <para>
    /// No <c>&lt;sound&gt;</c>: unlike the ten navigation marks this carries no playback
    /// meaning, and a <c>&lt;sound&gt;</c> attribute would assert a jump nobody wrote. No
    /// empty-text guard either, for the same reason the side is not decided here — the
    /// collector adds the item whatever the string is, and a guard would make the two
    /// engines disagree about <c>_""</c>.
    /// </para>
    /// <para>
    /// ⚠️ The importer does not read <c>&lt;words&gt;</c> at all, so this does not round
    /// trip — but that is the state it was already in for the eight <c>&lt;words&gt;</c>
    /// navigation marks, not something this mapping introduces.
    /// </para>
    /// </remarks>
    private void ApplyCustomText(string text)
    {
        var dir = new System.Xml.Linq.XElement("direction",
            new System.Xml.Linq.XAttribute("placement", "below"),
            new System.Xml.Linq.XElement("direction-type",
                new System.Xml.Linq.XElement("words", text)));
        foreach (var p in Document.Parts)
            if (p.Measures.Count > 0)
                p.Measures[^1].Notes.Add(
                    new MusicXmlNote { RawElement = new System.Xml.Linq.XElement(dir) });
    }

    /// <summary>The MusicXML &lt;direction&gt; for a navigation mark, and whether it
    /// is a jump TARGET (segno / coda) rather than a jump-from instruction. Signs use
    /// &lt;segno&gt;/&lt;coda&gt;; the rest are &lt;words&gt;, each with the matching
    /// &lt;sound&gt; playback attribute so importers can follow the jumps.</summary>
    private static (System.Xml.Linq.XElement? dir, bool isTarget) BuildNavDirection(NavigationMarkType type)
    {
        static System.Xml.Linq.XElement Wrap(System.Xml.Linq.XElement inner, System.Xml.Linq.XElement sound)
            => new("direction", new System.Xml.Linq.XAttribute("placement", "above"),
                new System.Xml.Linq.XElement("direction-type", inner), sound);
        static System.Xml.Linq.XElement Words(string t) => new("words", t);
        static System.Xml.Linq.XElement Sound(string a, string v)
            => new("sound", new System.Xml.Linq.XAttribute(a, v));

        return type switch
        {
            NavigationMarkType.Segno => (Wrap(new("segno"), Sound("segno", "segno")), true),
            NavigationMarkType.Coda => (Wrap(new("coda"), Sound("coda", "coda")), true),
            NavigationMarkType.Fine => (Wrap(Words("Fine"), Sound("fine", "yes")), false),
            NavigationMarkType.ToCoda => (Wrap(Words("To Coda"), Sound("tocoda", "coda")), false),
            NavigationMarkType.DaCapo => (Wrap(Words("D.C."), Sound("dacapo", "yes")), false),
            NavigationMarkType.DaCapoAlFine => (Wrap(Words("D.C. al Fine"), Sound("dacapo", "yes")), false),
            NavigationMarkType.DaCapoAlCoda => (Wrap(Words("D.C. al Coda"), Sound("dacapo", "yes")), false),
            NavigationMarkType.DalSegno => (Wrap(Words("D.S."), Sound("dalsegno", "segno")), false),
            NavigationMarkType.DalSegnoAlFine => (Wrap(Words("D.S. al Fine"), Sound("dalsegno", "segno")), false),
            NavigationMarkType.DalSegnoAlCoda => (Wrap(Words("D.S. al Coda"), Sound("dalsegno", "segno")), false),
            _ => (null, false),
        };
    }

    /// <summary>A <c>|: … :|</c> repeat block. A <c>:|:</c> divider splits it into
    /// back-to-back repeat spans (each <c>:| |:</c>); every span is bracketed with a
    /// forward repeat on its first measure and a backward repeat on its last, per
    /// part — mirroring the inline-barline handling.</summary>
    private void EmitRepeatBlock(FormWalk.Repeat rb, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        if (rb.Children.Any(c => c is FormWalk.Ending))
            EmitVoltaRepeatBlock(rb, byName);
        else
            EmitPlainRepeatBlock(rb, byName);
    }

    private void EmitPlainRepeatBlock(FormWalk.Repeat rb, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        var runs = new List<List<FormWalk.SectionRef>>();
        var cur = new List<FormWalk.SectionRef>();
        foreach (var child in rb.Children)
        {
            if (child is FormWalk.SectionRef s)
                cur.Add(s);
            else if (child is FormWalk.BothBar)
            {
                runs.Add(cur);
                cur = new List<FormWalk.SectionRef>();
            }
        }
        runs.Add(cur);

        foreach (var run in runs)
        {
            if (run.Count == 0)
                continue;
            var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
            foreach (var item in run)
                EmitSectionByName(byName, item.Name);
            foreach (var p in Document.Parts)
            {
                if (p.Measures.Count > startIdx.GetValueOrDefault(p))
                {
                    p.Measures[startIdx.GetValueOrDefault(p)].RepeatForward = true;
                    p.Measures[^1].RepeatBackward = true;
                }
            }
        }
    }

    /// <summary>A <c>|: BODY [1. E1] :| [2. E2]</c> volta repeat. The body opens the
    /// forward repeat; each ending gets a &lt;ending&gt; start/stop bracket; the
    /// backward repeat sits on the last measure before the <c>:|</c>; endings AFTER
    /// the <c>:|</c> are final (type "discontinue", no repeat).
    /// <para>
    /// A silent <c>~</c> ending is INDISTINGUISHABLE HERE, and that is the correct answer
    /// rather than a gap: the tilde binds to the section name and hides the section LABEL,
    /// and this exporter writes no section label at all — <c>EmitSectionByName</c> takes a
    /// name and emits music, so <c>~B</c> and <c>B</c> already reach it as the same call.
    /// Its ending bracket is emitted like any other.
    /// ⚠️ UNTIL 2026-08-25 THIS SUPPRESSED THE BRACKET, "as the engraving does" — and the
    /// engraving was wrong. The citation is what carried the defect across the output
    /// boundary; the quantity has three outputs and only one of them was ever right.
    /// </para></summary>
    private void EmitVoltaRepeatBlock(FormWalk.Repeat rb, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        bool forwardPending = true;
        bool afterEndBar = false;

        foreach (var child in rb.Children)
        {
            if (child is FormWalk.Ending { Node: var alt })
            {
                var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
                EmitSectionByName(byName, alt.SectionName.Text);
                string num = EndingNumbers(alt);
                string stopType = afterEndBar ? "discontinue" : "stop";
                foreach (var p in Document.Parts)
                {
                    if (p.Measures.Count <= startIdx.GetValueOrDefault(p)) continue;
                    p.Measures[startIdx.GetValueOrDefault(p)].EndingStartNumbers = num;
                    p.Measures[^1].EndingStopNumbers = num;
                    p.Measures[^1].EndingStopType = stopType;
                    if (forwardPending) p.Measures[startIdx.GetValueOrDefault(p)].RepeatForward = true;
                }
                forwardPending = false;
            }
            else if (child is FormWalk.SectionRef s)
            {
                var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
                EmitSectionByName(byName, s.Name);
                if (forwardPending)
                {
                    foreach (var p in Document.Parts)
                        if (p.Measures.Count > startIdx.GetValueOrDefault(p))
                            p.Measures[startIdx.GetValueOrDefault(p)].RepeatForward = true;
                    forwardPending = false;
                }
            }
            else if (child is FormWalk.RepeatEnd)
            {
                // The :| repeats back to the |:; it caps the ending just played.
                afterEndBar = true;
                foreach (var p in Document.Parts)
                    if (p.Measures.Count > 0)
                        p.Measures[^1].RepeatBackward = true;
            }
        }
    }

    /// <summary>The MusicXML <c>&lt;ending number&gt;</c> list for a volta: "1", a
    /// range <c>[1-3.]</c> → "1,2,3", a list <c>[1,3.]</c> → "1,3".</summary>
    private static string EndingNumbers(FormAlternativeSyntax alt)
    {
        int n = alt.AlternativeNumber;
        if (alt.Separator is not { } sep || alt.EndNumber is not { } endTok
            || !int.TryParse(endTok.Text, out int e))
            return n.ToString();
        if (sep.Text == "-")
            return string.Join(",", System.Linq.Enumerable.Range(n, System.Math.Max(1, e - n + 1)));
        return $"{n},{e}";
    }

    private void ProcessPartBlock(PartBlockSyntax partBlock)
        => EmitPartMusic(partBlock.Name, DirectChildren(partBlock));

    /// <summary>A part-major section (<c>part m { section A { … } }</c>) holds its music
    /// INLINE — not in a nested part block — so it is emitted here under the ENCLOSING
    /// part's name (and clef/transpose), exactly like the section-major
    /// <c>section A { m { … } }</c> form. Without this the inline notes hit
    /// <see cref="ProcessNode"/>'s skip-declarations case and the part exported EMPTY.</summary>
    /// <remarks>
    /// ⚠️ A section inside NO part declaration is a BARE section, and the name it is emitted
    /// under decides which header it reads. It used to fall straight to "Part 1" — a name no
    /// declaration answers — so the music came out at the default anchor with no sounding
    /// shift, whatever the part it is drawn on says. The score is what says whose it is
    /// (see <see cref="_bareSectionOwner"/>); "Part 1" remains for a file that names nobody.
    /// </remarks>
    private void EmitPartMajorSection(SectionDeclarationSyntax section)
        => EmitPartMusic(
            EnclosingPartName(section) ?? _bareSectionOwner ?? "Part 1",
            DirectChildren(section));

    /// <summary>The non-token child nodes of a container, in order.</summary>
    private static IEnumerable<SyntaxNode> DirectChildren(SyntaxNode node)
    {
        for (int i = 0; i < node.SlotCount; i++)
            if (node.GetChild(i) is { } child && child is not SyntaxTokenNode)
                yield return child;
    }

    /// <summary>The name of the <c>part</c> declaration a node sits inside, or null when it
    /// is not part-major (a standalone section maps to the default "Part 1").</summary>
    private static string? EnclosingPartName(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartDeclarationSyntax pd)
                return pd.Name.Text;
        return null;
    }

    /// <summary>Emits one span of a part's music (a section-major part block, or a
    /// part-major section's inline body) under <paramref name="partName"/>: sets up the
    /// part (clef / transpose / fresh frame), processes the music children, and maps any
    /// lyrics onto the notes just emitted.</summary>
    private void EmitPartMusic(string partName, IEnumerable<SyntaxNode> children)
    {
        EnsurePart(partName);
        _currentTranspose = _root != null ? PartTranspose.Read(_root, partName) : null;
        ApplyPartHeader(partName);
        _lastPitchedNote = null; // ho/po never pairs across parts

        // Reset state for this part's continuation. The relative frame starts at the part's
        // own anchor — the octave it PRINTS — not at a fixed middle C.
        _currentOctave = _partAnchorOctave;
        _currentStep = 0;
        _ambientTonic = _homeTonic; // each voice starts at the score's home key
        // ... and so does its SIGNATURE, before the section states one of its own. Both
        // halves of "a section is self-contained" have to be here: reverting the baseline
        // and not the fifths would let one section's key print over the next one's.
        // ⚠️ No measure is open across this point — the change belongs to the bar
        // StartNewMeasure is about to open, not to whatever the previous part left behind.
        _currentMeasure = null;
        if (_homeKey is { } hk) (_keyFifths, _keyMode, _keyCustomXml) = hk;
        if (_sectionKey is { } sk) ProcessKeySignature(sk);
        _octaveAbsolute = _initialOctaveAbsolute; // restore file-level octave mode
        _tieToNextNote = false;
        // Forget the open starts WITHOUT retracting them: a tie left hanging at a part
        // boundary is a separate question from an unmatched member, and this line only
        // stops the next part's first onset from being paired against another part's note.
        _tieOpen.Clear();
        _defaultDuration = Fraction.Quarter;
        _pendingDynamic = null;

        // If this is the first measure for this part, add attributes
        bool isFirst = _currentPart!.Measures.Count == 0;
        StartNewMeasure(addAttributes: isFirst);

        // Process the music; lyrics blocks are collected and mapped onto the emitted
        // notes afterwards.
        int measuresBefore = _currentPart!.Measures.Count;
        var lyricsBlocks = new List<LyricsBlockSyntax>();
        foreach (var child in children)
        {
            if (child is LyricsBlockSyntax lb)
                lyricsBlocks.Add(lb);
            else
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
                        // RawElement pseudo-entries (<harmony>, <figured-bass>) sit in
                        // the note stream but are not sung — skip them, else a chord
                        // symbol before the first note steals its syllable (and, being
                        // serialized verbatim, drops it).
                        if (!n.IsRest && !n.IsChord && !n.IsGrace && !n.TieStop && n.RawElement == null)
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
            Document.Parts.Add(_currentPart);
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

    /// <summary>The key signature as the DOCUMENT must spell it: the written fifths plus
    /// whatever the part's instrument transpose adds. One house, so the opening attributes
    /// and a later change cannot disagree about it.</summary>
    private int EffectiveKeyFifths()
        => _currentTranspose is { } trk
            ? _keyFifths + PitchTransposer.KeySignatureFifthsShift(trk.step, trk.alt)
            : _keyFifths;

    private void StartNewMeasure(bool addAttributes = false)
    {
        _currentMeasure = new MusicXmlMeasure { Number = _measureNumber++ };

        if (addAttributes)
        {
            _currentMeasure.Attributes = new MusicXmlAttributes
            {
                Divisions = DivisionsPerQuarter,
                TimeBeats = _timeNumerator,
                TimeBeatsText = _timeNumeratorText,
                TimeSenzaMisura = _timeSenzaMisura,
                TimeBeatType = _timeDenominator,
                KeyFifths = EffectiveKeyFifths(),
                KeyCustom = _keyCustomXml,
                KeyMode = _keyMode,
                ClefSign = _clefSign,
                ClefLine = _clefLine > 0 ? _clefLine : null,
                ClefOctaveChange = _clefOctaveChange,
                TransposeSemitones = _partTransposeSemitones != 0
                    ? _partTransposeSemitones
                    : null
            };

            _currentMeasure.Direction = new MusicXmlDirection { Tempo = _tempo };
            RecordWrittenAttributes();
            _attributesDirty = false;
        }
        else if (_attributesDirty)
        {
            SyncAttributes();
        }
    }

    /// <summary>Remember what the measure just written says, so the next change is a change.</summary>
    private void RecordWrittenAttributes()
    {
        _writtenKey = (EffectiveKeyFifths(), _keyMode, _keyCustomXml);
        _writtenTime = (_timeNumerator, _timeNumeratorText, _timeDenominator, _timeSenzaMisura);
        _writtenClef = (_clefSign, _clefLine, _clefOctaveChange);
    }

    /// <summary>Write an <c>&lt;attributes&gt;</c> for whatever the walk has changed since
    /// the document last said it — a key change, a meter change, a clef change.</summary>
    /// <remarks>
    /// ⚠️ A measure carries ONE attributes slot and renders it at the bar's head, so a change
    /// seen after notes have been written would sound a bar early. Such a change is held
    /// (<see cref="_attributesDirty"/>) and written by the next <see cref="StartNewMeasure"/>
    /// instead — which is where a <c>time</c> or <c>key</c> at a bar line belongs anyway.
    /// ⚠️ Divisions are NOT repeated: the change block says only what changed, and a reader
    /// that saw <c>&lt;divisions&gt;</c> once keeps it.
    /// </remarks>
    private void SyncAttributes()
    {
        if (_currentMeasure == null || _currentPart == null) return;
        if (_currentMeasure.Notes.Count > 0) { _attributesDirty = true; return; }

        var key = (EffectiveKeyFifths(), _keyMode, _keyCustomXml);
        var time = (_timeNumerator, _timeNumeratorText, _timeDenominator, _timeSenzaMisura);
        var clef = (_clefSign, _clefLine, _clefOctaveChange);
        bool keyChanged = _writtenKey is null || !_writtenKey.Value.Equals(key);
        bool timeChanged = _writtenTime is null || !_writtenTime.Value.Equals(time);
        bool clefChanged = _writtenClef is null || !_writtenClef.Value.Equals(clef);
        _attributesDirty = false;
        if (!keyChanged && !timeChanged && !clefChanged) return;

        // Merge into this measure's own attributes when it already has one (the part's
        // opening bar, whose section states a key of its own): two <attributes> in one
        // measure would be a reader's coin toss.
        var attrs = _currentMeasure.Attributes;
        if (attrs == null)
            _currentMeasure.Attributes = attrs = new MusicXmlAttributes { Divisions = null };

        if (keyChanged)
        {
            attrs.KeyFifths = key.Item1;
            attrs.KeyMode = _keyMode;
            attrs.KeyCustom = _keyCustomXml;
        }
        if (timeChanged)
        {
            attrs.TimeBeats = _timeNumerator;
            attrs.TimeBeatsText = _timeNumeratorText;
            attrs.TimeBeatType = _timeDenominator;
            attrs.TimeSenzaMisura = _timeSenzaMisura;
        }
        if (clefChanged)
        {
            attrs.ClefSign = _clefSign;
            attrs.ClefLine = _clefLine > 0 ? _clefLine : null;
            attrs.ClefOctaveChange = _clefOctaveChange;
        }
        RecordWrittenAttributes();
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
            case DrumNoteSyntax drumNote:
                ProcessDrumNote(drumNote);
                break;

            case ChordSyntax chord:
                ProcessChord(chord);
                break;

            case ChordRepetitionSyntax rep:
                ProcessChordRepetition(rep);
                break;

            case SlashNoteSyntax slash:
                ProcessSlashNote(slash);
                break;

            case BareDurationSyntax bare:
                ProcessBareDuration(bare);
                break;

            case ArpeggioSyntax arpeggio:
                ProcessArpeggio(arpeggio);
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

            case BarlineSyntax barline:
                {
                    // A barline immediately after a pickup auto-close is redundant —
                    // the pickup measure already closed, so swallow it (no empty bar).
                    if (_justAutoClosedPickup)
                    {
                        _justAutoClosedPickup = false;
                        break;
                    }
                    string barText = (barline.GetChild(0) as SyntaxTokenNode)?.Text ?? "|";
                    if (_currentMeasure != null)
                    {
                        // Closing side: repeat sign / double / final / dashed.
                        if (barText is ":|" or ":|:")
                            _currentMeasure.RepeatBackward = true;
                        else if (barText == "||")
                            _currentMeasure.BarStyle = "light-light";
                        else if (barText == "|.")
                            _currentMeasure.BarStyle = "light-heavy";
                        else if (barText == "!")
                            _currentMeasure.BarStyle = "dashed";
                    }
                    if (_currentMeasure != null && _currentPart != null)
                    {
                        if (_currentMeasure.Notes.Count > 0)
                        {
                            // Close the current measure and open the next one.
                            _currentPart.Measures.Add(_currentMeasure);
                            StartNewMeasure();
                        }
                        // else: an empty current measure (e.g. a leading '|:' before
                        // any notes) — reuse it instead of emitting a blank bar.
                        if (barText is "|:" or ":|:")
                            _currentMeasure!.RepeatForward = true;
                    }
                }
                break;

            case DynamicSyntax dynamic:
                HandleDynamicText(dynamic.DynamicToken.Text);
                break;

            case TieSyntax:
            case SlurSyntax:
                ApplyMarkerToLastEmitted(node);
                break;

            case GraceExpressionSyntax grace:
                ProcessGraceNotes(grace);
                break;

            case ParallelExpressionSyntax parallel:
                ProcessParallelVoices(parallel);
                break;

            case RepeatExpressionSyntax repeat:
                {
                    int repCount = int.TryParse(repeat.Count.Text, out int rc) ? Math.Max(1, rc) : 2;
                    // A one-measure percent body exports the SIGN: the source
                    // measure once, then empty measures under a
                    // <measure-style><measure-repeat> run (importers play the
                    // repeat and print %). Multi-measure bodies and the other
                    // repeat types stay unfolded (metrically correct).
                    bool oneMeasurePercent = repeat.RepeatType.Text == "percent"
                        && repeat.Body.Items.Count(i => i is BarlineSyntax) == 1
                        && repeat.Body.Items.LastOrDefault() is BarlineSyntax
                        && _currentPart != null;
                    // A percent sign and a tremolo are ENGRAVED ONCE, so every pass has to
                    // be that one copy — which means re-entering the body in the frame it
                    // opened in, not the one the previous pass left. Without it a body that
                    // moves the frame climbs, and unlike the MIDI (which clamps at key 127)
                    // this file has no ceiling to hide it: MEASURED 2026-08-17 on
                    // `repeat tremolo 32 { g''64 a }`, whose page is one G5-A5 pair, the
                    // export ran G5 A5 G7 A7 G9 ... up to OCTAVE 67.
                    // ⚠️ `unfold` re-enters its own frame TOO, though it is written out in
                    // full: N copies of one piece of music is what "play this N times" means
                    // (decided 2026-08-17, HANDOFF §3), and it is LilyPond's reading as well.
                    var frame = (_currentOctave, _currentStep, _defaultDuration);
                    if (oneMeasurePercent)
                    {
                        // Repeated measures carry their REAL notes under the
                        // measure-style (importers hide them behind the % and
                        // strict ones see full bars), like MuseScore exports.
                        for (int rep = 0; rep < repCount; rep++)
                        {
                            // The body ends with its own barline, which flushes
                            // the measure and opens the next (flushing HERE
                            // nulls the open measure and drops later passes).
                            if (rep > 0)
                                (_currentOctave, _currentStep, _defaultDuration) = frame;
                            ProcessNode(repeat.Body);
                            if (rep == 1 && _currentPart!.Measures.Count > 0)
                            {
                                var m = _currentPart.Measures[^1];
                                m.Attributes ??= new MusicXmlAttributes
                                {
                                    Divisions = DivisionsPerQuarter,
                                    MeasureRepeat = "start",
                                };
                            }
                        }
                        if (_currentMeasure != null)
                            _currentMeasure.Attributes ??= new MusicXmlAttributes
                            {
                                Divisions = DivisionsPerQuarter,
                                MeasureRepeat = "stop",
                            };
                        break;
                    }
                    for (int rep = 0; rep < repCount; rep++)
                    {
                        if (rep > 0)
                            (_currentOctave, _currentStep, _defaultDuration) = frame;
                        ProcessNode(repeat.Body);
                    }
                }
                break;

            case TupletExpressionSyntax tuplet:
                // A tuplet plays TupletRatio notes in the time of BaseDivision
                // (triplet = 3 in 2). Scale durations and tag time-modification for
                // the body's notes; nested tuplets multiply.
                _tupletStack.Push((tuplet.TupletRatio, tuplet.BaseDivision));
                int tupletNumber = _tupletStack.Count;
                var tupletMeasure = _currentMeasure;
                int tupletFrom = _currentMeasure?.Notes.Count ?? 0;
                ProcessNode(tuplet.Body);
                _tupletStack.Pop();
                // Add the <tuplet> notation bracket (start on the body's first note,
                // stop on its last) alongside the <time-modification> already stamped.
                // Skipped when the body crossed a barline (rare) — no bracket beats a
                // wrong one.
                if (_currentMeasure != null && ReferenceEquals(_currentMeasure, tupletMeasure))
                {
                    var body = _currentMeasure.Notes;
                    int firstIdx = -1, lastIdx = -1;
                    for (int k = tupletFrom; k < body.Count; k++)
                    {
                        if (body[k].IsChord || body[k].RawElement != null)
                            continue; // chord members / <harmony> / <figured-bass> are not the tuplet's notes
                        if (firstIdx < 0) firstIdx = k;
                        lastIdx = k;
                    }
                    if (firstIdx >= 0)
                    {
                        body[firstIdx].ExtraNotations.Add(TupletNotation("start", tupletNumber));
                        body[lastIdx].ExtraNotations.Add(TupletNotation("stop", tupletNumber));
                    }
                }
                break;

            case VariableReferenceSyntax varRef:
                if (_variables.TryGetValue(varRef.Name.Text, out var varBody)
                    && _activePhrases.Add(varRef.Name.Text))
                {
                    // Phrase bodies evaluate in a fresh relative frame so a
                    // $phrase means the same pitches at every call site
                    // (matches MeasureCollector's RelativeResetMarker). Trailing
                    // marks (Chorus' / Chorus,) shift that frame up or down.
                    // ⚠️ FRESH IS THE PART'S ANCHOR, NOT MIDDLE C — the same octave
                    // EmitPartMusic arms above, and what the collector's ResetToInitial
                    // means by "initial" (OctaveContext.InitialOctave is the voice's armed
                    // octave, which the clef sets). A literal 4 here put every phrase in a
                    // bass part an octave above its own page: MEASURED 2026-08-17 on a
                    // `part lh { clef bass }` whose music is one phrase — page and MIDI read
                    // C3 E3 C3 G3, this wrote C4 E4 C4 G4. The MIDI walk had it right all
                    // along (_partOctaveAnchor + varRef.OctaveOffset), which is why the
                    // disagreement needed two outputs side by side to see.
                    _currentOctave = _partAnchorOctave + varRef.OctaveOffset;
                    _currentStep = 0;
                    _defaultDuration = Fraction.Quarter;
                    // Auto-transpose the movable phrase from the home key to the
                    // ambient key here (respelled), composed under any part
                    // transpose; restored after the body.
                    var savedTranspose = _currentTranspose;
                    _currentTranspose = PitchTransposer.Compose(PhraseTransposeTarget(), savedTranspose);
                    // The same marks in ABSOLUTE mode: there is no running frame to
                    // move, so the shift lands on the absolute anchor instead — the
                    // collector's OctaveBase, this walker's _octaveAnchor.
                    int savedAnchor = _octaveAnchor;
                    _octaveAnchor += varRef.OctaveOffset;
                    // The phrase's outgoing ANCHOR — its first note's bare
                    // letter resolved in the fresh frame above, the ambient
                    // tonic for a degree-opened body — captured before the
                    // body runs (a mid-body key change must not move it).
                    int? anchorStep = LilySharp.Core.Music.PhraseAnchor.AnchorStep(varBody,
                        n => _variables.TryGetValue(n, out var b) ? b : null);
                    if (anchorStep == LilySharp.Core.Music.PhraseAnchor.Tonic)
                        anchorStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
                    ProcessNode(varBody);
                    _currentTranspose = savedTranspose;
                    _octaveAnchor = savedAnchor;
                    // Frame hand-off at the phrase's ANCHOR (matches the
                    // collector's ExitPhraseTranspose): the reference is ONE
                    // item, the chord rule — its interior never leaks, and its
                    // own marks shift what propagates, so a note after Melody'
                    // is relative to the shifted anchor. A pitchless body hands
                    // nothing off.
                    if (anchorStep is { } astep)
                    {
                        int oct = RelativeOctave.Resolve(
                            0, _partAnchorOctave + varRef.OctaveOffset, astep, 0);
                        _currentStep = astep;
                        _currentOctave = oct;
                    }
                    _activePhrases.Remove(varRef.Name.Text);
                }
                break;

            case PhraseDeclarationSyntax:
            case VariableDeclarationSyntax:
                // Skip declarations — a phrase is written where it is REFERENCED.
                break;

            case CueExpressionSyntax cue:
            {
                // `cue bass { … }` reads its body in the cue clef and hands the staff's own
                // clef back at the end. BOTH EDGES ARE UNCONDITIONAL, unlike a `clef`
                // declaration — the collector resets whether or not the cue clef differs
                // (MeasureCollector.MusicWalk.ProcessCueRegion), and the page is the rule.
                // `audit/lp-regression/lys/cue-clef-manually` writes the compensation into
                // its own margin, so its four cue notes were exported an octave high here.
                var outerClef = _currentClefType;
                if (cue.ClefKeyword is { } cueClef)
                    SetFrameToClef(LilySharp.Core.Svg.Collector.MeasureCollector.ParseClefType(
                        cueClef.Text.ToLowerInvariant()));
                ProcessNode(cue.Body);
                if (cue.ClefKeyword != null) SetFrameToClef(outerClef);
                break;
            }

            case PartDeclarationSyntax:
            case SectionDeclarationSyntax:
            case FormDeclarationSyntax:
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

    /// <summary>
    /// Multi-voice: voice 1 leads the measure stream; each further voice
    /// renders into a SCRATCH part and merges into the same measures behind a
    /// &lt;backup&gt; cursor rewind, tagged with its voice number — the
    /// MusicXML shape importers expect (the walk used to serialize voices
    /// SEQUENTIALLY, doubling the measure count).
    /// <para>
    /// ⚠️ IT REOPENS THE STREAM ON THE WAY OUT. Closing a measure here is right — the
    /// block is bar-aligned and every voice must merge into the SAME bars — but
    /// <see cref="FlushCurrentMeasure"/> also nulls the cursor, and every emitter in this
    /// file opens with <c>if (_currentMeasure == null) return;</c>. So until 2026-08-17 the
    /// music written AFTER a <c>voice { } { }</c> block was dropped in silence: measured on
    /// <c>test/multi-voice</c>, the page drew 3 bars and the MIDI sounded 14 notes while the
    /// MusicXML carried 2 bars and 8. The same book was a different piece depending on which
    /// output was asked.
    /// </para>
    /// </summary>
    private void ProcessParallelVoices(ParallelExpressionSyntax parallel)
    {
        var voices = parallel.Voices.ToList();
        if (voices.Count == 0) return;
        if (_currentPart == null)
        {
            foreach (var v in voices) ProcessNode(v);
            return;
        }

        // The frame the span OPENS in. Every voice reads from it, and so does the music
        // after the span: simultaneous music does not move the relative frame. That rule is
        // the collector's, stated where it is enforced (MeasureCollector.MusicWalk, the
        // ParallelExpressionSyntax case: "the frame at the span's OPENING is what every
        // voice reads from, and what the music after the span reads from"), and this walk
        // is a second reader of it — not a second rule.
        int spanOctave = _currentOctave, spanStep = _currentStep;

        int startMeasure = _currentPart.Measures.Count;
        ProcessNode(voices[0]);
        FlushCurrentMeasure(); // voice blocks are bar-aligned; settle voice 1
        int endMeasure = _currentPart.Measures.Count;

        for (int v = 1; v < voices.Count; v++)
        {
            var savedPart = _currentPart;
            var savedMeasure = _currentMeasure;
            int savedMeasureNumber = _measureNumber;
            var savedOctave = _currentOctave;
            var savedStep = _currentStep;
            var savedDefault = _defaultDuration;
            var savedTie = _tieToNextNote;
            var savedTieOpen = _tieOpen.ToList(); // the open starts belong to the OUTER stream

            var temp = new MusicXmlPart { Name = "voice-temp" };
            _currentPart = temp;
            _currentMeasure = null;
            StartNewMeasure(); // scratch stream needs an open measure for its notes
            // ⚠️ THE SPAN'S FRAME, not the part's default C4. Resetting here made every
            // sub-voice read its first bare letter from middle C, so `c'2 c' | voice { … }
            // { b, c, }` put the second voice an octave below the page's answer — and the
            // page's answer is the one the twin and the MIDI both give (measured
            // 2026-08-17: page/MIDI/LilyPond all read B3 C3 where this wrote B2 C2).
            _currentOctave = spanOctave;
            _currentStep = spanStep;
            // The DURATION default does reset — the collector resets it too
            // (BuildExtraVoiceTracks: `_defaultDuration = Fraction.Quarter`), so this line
            // agrees with the page and only the octave above was a second rule.
            _defaultDuration = Fraction.Quarter;
            _tieToNextNote = false;
            _tieOpen.Clear();
            ProcessNode(voices[v]);
            FlushCurrentMeasure();

            _currentPart = savedPart;
            _currentMeasure = savedMeasure;
            _measureNumber = savedMeasureNumber;
            _currentOctave = savedOctave;
            _currentStep = savedStep;
            _defaultDuration = savedDefault;
            _tieToNextNote = savedTie;
            _tieOpen.Clear();
            _tieOpen.AddRange(savedTieOpen);

            for (int i = 0; i < temp.Measures.Count && startMeasure + i < endMeasure; i++)
            {
                var target = _currentPart.Measures[startMeasure + i];
                // Back up by the CURRENT cursor offset from the bar start, i.e. the
                // net forward advance already in this measure (forward notes minus
                // the backups already emitted). Summing every forward note would,
                // from the third voice on, rewind past the bar start because earlier
                // voices' notes are already merged in.
                int written = target.Notes
                        .Where(n => !n.IsChord && !n.IsGrace && !n.IsBackup)
                        .Sum(n => n.Duration)
                    - target.Notes.Where(n => n.IsBackup).Sum(n => n.Duration);
                foreach (var n in target.Notes)
                    if (!n.IsBackup)
                        n.Voice ??= 1;
                target.Notes.Add(new MusicXmlNote { IsBackup = true, Duration = written });
                foreach (var n in temp.Measures[i].Notes)
                {
                    n.Voice = v + 1;
                    target.Notes.Add(n);
                }
            }
        }

        // Hand the stream back OPEN, so whatever is written after the block still has a
        // measure to land in (see the remark above). An empty one costs nothing: a measure
        // with no notes is never added by FlushCurrentMeasure, and a following barline
        // reuses it rather than emitting a blank bar.
        StartNewMeasure();
        // ... and in the frame the span opened in, which is where the page reads the next
        // note from. Leaving voice 1's end here read `d` two octaves off in the probe.
        _currentOctave = spanOctave;
        _currentStep = spanStep;
    }

    private void ProcessTimeSignature(TimeSignatureSyntax timeSig)
    {
        _timeSenzaMisura = timeSig.IsSenzaMisura;
        if (!_timeSenzaMisura)
        {
            _timeNumerator = timeSig.Beats;
            _timeNumeratorText = timeSig.BeatsText;
            _timeDenominator = timeSig.BeatType;
        }
        _attributesDirty = true;
        SyncAttributes();
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
        if (key.IsCustom)
        {
            _keyCustomXml = LilySharp.Core.Svg.Model.KeySignature.EncodeCustom(key.CustomAlterations);
            _keyFifths = 0;
            _attributesDirty = true;
            SyncAttributes();
            return;
        }
        _keyCustomXml = null;
        var pitch = key.Pitch?.ToFullString().Trim().ToLower();
        // MusicXML's <mode> takes the church-mode names directly.
        var mode = key.Mode.Text.ToLowerInvariant();

        // Delegate to KeySpelling (the single source of truth for tonic -> fifths);
        // an unrecognized tonic falls back to 0 (C), as before.
        _keyFifths = KeySpelling.SharpsFor(pitch ?? "", mode) ?? 0;
        _keyMode = mode;

        // Advance the phrase auto-transpose baseline to this key's (written) tonic.
        _ambientTonic = KeyTonic.Of(key);
        _attributesDirty = true;
        SyncAttributes();
    }

    /// <summary>
    /// Applies everything the part's HEADER says about pitch: the clef it reads in, the
    /// octave its bare letters anchor to, and its written→sounding transposition.
    /// </summary>
    /// <remarks>
    /// The walk only sees IN-MUSIC clef changes, so a header-only clef (the normal case)
    /// has to be applied here.
    /// <para>
    /// ⚠️ It used to read the <c>clef</c> PROPERTY and nothing else, which left this
    /// exporter answering for a header it had barely read: <c>instrument bass</c> exported a
    /// treble clef, every part exported at octave 4 whatever it printed, and no part ever
    /// carried a <c>transpose</c>. All three come off one reading
    /// (<see cref="PartHeaderDefaults"/>), the same one the MIDI exporter takes.
    /// </para>
    /// </remarks>
    private void ApplyPartHeader(string partName)
    {
        var header = PartHeaderDefaults.Read(
            _root?.DescendantNodes().OfType<PartDeclarationSyntax>()
                 .FirstOrDefault(pd => pd.Name.Text == partName));

        if (header.ClefWord != null)
            SetClef(header.ClefWord);
        // A mid-music `clef` is a change FROM this one, and an unchanged clef changes
        // nothing — so the walk has to know what the part is already reading in.
        _currentClefType = header.Clef;

        _partAnchorOctave = header.AnchorOctave;
        _octaveAnchor = header.AbsoluteBaseOctave;

        // ⚠️ THE WHOLE written→sounding distance goes in <transpose>, the clef's octave
        // included. MusicXML's <pitch> is the WRITTEN pitch and <transpose> is what turns it
        // into the sounding one; <clef-octave-change> is notation — it says where the written
        // pitch is DRAWN — so a reader that plays the document has nothing else to read.
        // Until 2026-08-17 only the instrument's share went here, and a guitar part sounded
        // an octave high in every program but this one (44 books; decided, HANDOFF §3).
        // ⚠️ The importer subtracts the clef's share again on the way back in, because the
        // clef WORD it writes carries it (`clef treble_8`). Both halves move together or a
        // guitar drops two octaves — MusicXmlReader.ReadPart.
        _partTransposeSemitones = header.SoundingShiftSemitones;
    }

    private void ProcessClef(ClefDeclarationSyntax clef)
    {
        var word = clef.ClefName?.Text.ToLower();
        var next = LilySharp.Core.Svg.Collector.MeasureCollector.ParseClefType(word ?? "");
        bool changed = next != _currentClefType;
        SetClef(word);
        // ⚠️ A CHANGED clef reopens the relative frame at its own octave — the OCTAVE only,
        // not the note name — so `clef bass c,4` is a low C without extra commas. That is
        // Lily#'s rule rather than LilyPond's (whose `\relative` never looks at a clef),
        // and it is the page's: `MeasureCollector.MusicWalk`'s clef branch does exactly
        // this. Until 2026-08-17 only the page did, so `g4 a clef bass c,4 d` printed
        // C3 D3 and exported C5 D5. Decided 2026-08-17, HANDOFF §3.
        // ⚠️ An UNCHANGED clef changes nothing — it engraves no grob (LilyPond makes one
        // only when glyph/position/transposition differ) and must not reset the frame.
        if (changed) SetFrameToClef(next);
        // ⚠️ Only the IN-MUSIC clef syncs. A header clef reaches the document through the
        // part's opening attributes, and syncing there would write a change on the bar a
        // second part happens to be starting.
        _attributesDirty = true;
        SyncAttributes();
    }

    /// <summary>Reopen the relative frame in <paramref name="clef"/> — the octave only, so
    /// the frame keeps reading from the letter it last saw.</summary>
    private void SetFrameToClef(LilySharp.Core.Svg.Model.ClefType clef)
    {
        _currentClefType = clef;
        _currentOctave = LilySharp.Core.Svg.Model.InstrumentDefaults.GetDefaultOctave(clef);
    }

    private void SetClef(string? clefName)
    {
        (_clefSign, _clefLine) = clefName switch
        {
            "treble" => ("G", 2),
            "treble_8" => ("G", 2),
            "treble^8" => ("G", 2),
            "bass" => ("F", 4),
            "bass_8" => ("F", 4),
            "alto" => ("C", 3),
            "tenor" => ("C", 4),
            "soprano" => ("C", 1),
            "mezzosoprano" => ("C", 2),
            "baritone" => ("C", 5),
            "percussion" => ("percussion", 0),
            _ => ("G", 2)
        };
        _clefOctaveChange = clefName switch
        {
            "treble_8" or "bass_8" => -1,
            "treble^8" => 1,
            _ => null,
        };
    }

    // Respells a written pitch for a transposed part (no-op otherwise). The
    // relative octave is resolved on the ORIGINAL pitch by the caller; this only
    // moves the printed step / alter / octave.
    /// <summary>
    /// The home→ambient interval for a movable phrase at the current reference
    /// site (nearest octave), or null when there is nothing to do — ambient
    /// equals home, or either key is custom/atonal.
    /// </summary>
    private (int step, int alt, int oct)? PhraseTransposeTarget()
        => _homeTonic.Valid && _ambientTonic.Valid
            ? PitchTransposer.MovableInterval(
                _homeTonic.Step, _homeTonic.Alter, _ambientTonic.Step, _ambientTonic.Alter)
            : null;

    private (string step, int alter, int octave) ApplyTranspose(
        PitchSyntax pitch, string step, int alter, int octave)
    {
        if (_currentTranspose is null)
            return (step, alter, octave);
        var (ns, na, no) = ApplyWrittenTransforms(
            RelativeOctave.StepIndex(pitch.BaseName), pitch.AccidentalOffset, octave);
        return ("CDEFGAB"[ns].ToString(), na, no);
    }

    /// <summary>MusicXML notehead value from a @notehead(...) mark, or the
    /// drum table style. XCircle serializes as "circle-x".</summary>
    private static string? NoteheadName(Svg.Model.NoteheadStyle style) => style switch
    {
        Svg.Model.NoteheadStyle.Cross => "x",
        Svg.Model.NoteheadStyle.Diamond => "diamond",
        Svg.Model.NoteheadStyle.Triangle => "triangle",
        Svg.Model.NoteheadStyle.Slash => "slash",
        Svg.Model.NoteheadStyle.XCircle => "circle-x",
        _ => null,
    };

    private static string? NoteheadFromMarks(IEnumerable<SyntaxNode> articulations)
    {
        foreach (var art in articulations)
            if (art is MusicMarkSyntax mark
                && mark.Name.Equals("notehead", StringComparison.OrdinalIgnoreCase)
                && mark.HasArgumentList)
                return Semantics.AnnotationValues.Notehead(mark) switch
                {
                    "x" or "cross" => "x",
                    "diamond" => "diamond",
                    "triangle" => "triangle",
                    "slash" => "slash",
                    "xcircle" => "circle-x",
                    _ => null,
                };
        return null;
    }

    /// <summary>Drum note → &lt;unpitched&gt; note: display position from the
    /// drums-style staff position mapped onto treble letters (middle line =
    /// B4), notehead from the same table.</summary>
    /// <remarks>LILYPOND-REF: ly/drumpitch-init.ly drums-style.</remarks>
    private void ProcessDrumNote(DrumNoteSyntax drum)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;
        var info = DrumOverrides.Resolve(DrumOverridesMap, drum.DrumName);

        // Staff position → display step/octave (B4 = middle line).
        int idx = 6 + info.StaffPosition;
        int oct = 4 + (int)Math.Floor(idx / 7.0);
        string step = "CDEFGAB"[((idx % 7) + 7) % 7].ToString();

        var duration = GetDuration(drum.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);
        EmitPendingDynamic();

        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        var xmlNote = new MusicXmlNote
        {
            IsUnpitched = true,
            Step = step,
            Octave = oct,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            ActualNotes = tupletActual,
            NormalNotes = tupletNormal,
            Notehead = NoteheadName(info.Notehead),
        };
        _currentMeasure.Notes.Add(xmlNote);
        MaybeClosePickup(duration);
    }

    private void ProcessNote(NoteSyntax note)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var (step, alter) = ParsePitch(note.Pitch);
        int targetOctave = ResolveRelativeOctave(note.Pitch);
        (step, alter, targetOctave) = ApplyTranspose(note.Pitch, step, alter, targetOctave);
        // Quarter tones: half-integer alter + an explicit accidental name.
        int quarter = note.Pitch.QuarterOffset;

        var duration = GetDuration(note.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        // `a4@rest` is a REST placed by a written pitch. It leaves here rather than earlier
        // because the four lines above are what it must still do: resolve the relative
        // octave (moving the frame on), apply the part's transpose — the page moves the rest
        // with it, MEASURED — and carry the duration. What follows is note work, and all of
        // it is wrong for a rest: until 2026-08-17 this method ran the lot and emitted
        // <pitch>, so `a'4@rest` was a SOUNDING NOTE in the MusicXML of a page that draws a
        // rest.
        if (Semantics.PitchedRest.Is(note))
        {
            EmitPitchedRest(step, alter, targetOctave, durationTicks, type, dots, duration);
            return;
        }

        // What a following bare duration copies (same contract as
        // _resolvedChordXmlNotes): the spelling this walk resolved.
        _resolvedNoteXml[note] = (step, alter, targetOctave, quarter);

        // Emit pending dynamic as direction before the note
        EmitPendingDynamic();

        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        var xmlNote = new MusicXmlNote
        {
            Step = step,
            Alter = quarter == 0 ? alter : alter + 0.5 * quarter,
            Octave = targetOctave,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            AccidentalName = (alter, quarter) switch
            {
                (0, 1) => "quarter-sharp",
                (1, 1) => "three-quarters-sharp",
                (0, -1) => "quarter-flat",
                (-1, -1) => "three-quarters-flat",
                _ => null,
            },
            Notehead = NoteheadFromMarks(note.Articulations),
            ActualNotes = tupletActual,
            NormalNotes = tupletNormal
        };

        // Process articulations and slurs
        ProcessArticulations(note.Articulations, xmlNote);

        // Tie pairing: a preceding '~' ends on this note (tie-stop); a '~' on
        // this note (sibling or articulation) starts a tie to the next note.
        CloseTies([xmlNote]);
        if (note.Articulations.OfType<TieSyntax>().Any()) OpenTies([xmlNote]);

        // Glissando / slide lines pair start (this note) with stop (next).
        if (_pendingLineStop is { } lineKind)
        {
            xmlNote.ExtraNotations.Add(new System.Xml.Linq.XElement(lineKind,
                new System.Xml.Linq.XAttribute("type", "stop"),
                new System.Xml.Linq.XAttribute("number", 1)));
            _pendingLineStop = null;
        }
        foreach (var art in note.Articulations)
        {
            if (art is ArticulationSyntax { Type: ArticulationType.None } named
                && named.NameToken.Text.ToLowerInvariant() is "glissando" or "slide")
            {
                string el = named.NameToken.Text.Equals("slide", StringComparison.OrdinalIgnoreCase)
                    ? "slide" : "glissando";
                xmlNote.ExtraNotations.Add(new System.Xml.Linq.XElement(el,
                    new System.Xml.Linq.XAttribute("type", "start"),
                    new System.Xml.Linq.XAttribute("number", 1),
                    new System.Xml.Linq.XAttribute("line-type", el == "slide" ? "solid" : "wavy")));
                _pendingLineStop = el;
                break;
            }
        }

        _currentMeasure.Notes.Add(xmlNote);
        _lastPitchedNote = xmlNote;
        _lastEmittedNotes.Clear();
        _lastEmittedNotes.Add(xmlNote);
        MaybeClosePickup(duration);
    }

    /// <summary>
    /// Emits an arpeggio (<c>&lt;&lt; c e g &gt;&gt;</c>) — a written-out broken chord — as
    /// SEQUENTIAL notes that EQUALLY SUBDIVIDE the group's total (an auto-tuplet, with
    /// time-modification + bracket, when the share is not a plain note value). The octaves
    /// stack above the first pitched member (the chord rule) and scale degrees
    /// (<c>&lt;&lt; c 3 5 &gt;&gt;</c>) resolve against the root and the key — mirroring
    /// MidiExporter / the collector.
    /// </summary>
    private void ProcessArpeggio(ArpeggioSyntax arpeggio)
    {
        var members = arpeggio.Members.ToList();
        if (members.Count == 0)
            return;

        // The group occupies its total (trailing `>>N`, or the inherited running duration);
        // its members split that equally. An auto-tuplet fits M members into the P-note frame.
        Fraction total = arpeggio.TotalDuration?.ToFraction() ?? _defaultDuration;
        var sub = ArpeggioSubdivision.Compute(members.Count, total);
        var tupletMeasure = _currentMeasure;
        int tupletFrom = _currentMeasure?.Notes.Count ?? 0;
        int tupletNumber = 0;
        if (sub.HasTuplet)
        {
            _tupletStack.Push((sub.TupletNum, sub.TupletBase));
            tupletNumber = _tupletStack.Count;
        }
        var savedDefault = _defaultDuration;
        _defaultDuration = sub.MemberDisplay; // forced member value/dots
        // Octave marks after '>>' shift the whole group (like a chord's '<c e g>,'): applied
        // to the ROOT, inherited by the stacked members / degrees via the anchor octave.
        int groupOctave = arpeggio.OctaveOffset;

        // A dynamic on the group (`<< c e g >>@f`) sounds at its start: route it
        // through the shared dynamic/wedge funnel and emit before the first member.
        foreach (var a in arpeggio.Articulations)
            if (a is DynamicSyntax dyn)
                HandleDynamicText(dyn.DynamicToken.Text);
        EmitPendingDynamic();

        // The root is the first PITCHED member (leading rests just advance time); it
        // resolves relatively and anchors the group. Subsequent PITCHED members stack above
        // it (absolute mode with the anchored octave), order-independently; rests keep the
        // normal frame; degrees stack on the root by diatonic steps in the key.
        bool savedAbsolute = _octaveAbsolute;
        int savedAnchor = _octaveAnchor;
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
                    anchorOctave = RelativeOctave.Resolve(_currentStep, _currentOctave, rootStep, 0) + groupOctave;
                }
                EmitArpeggioXmlDegree(degree, rootStep, anchorOctave);
                continue;
            }

            char? letter = RelativeOctave.FirstPitchLetter(member);
            // The group octave shift applies to the ROOT member only; the stacked members
            // inherit it via the anchor octave the shifted root sets.
            bool isRoot = !rootSet && letter is not null;
            if (rootSet && letter is { } l)
            {
                _octaveAbsolute = true;
                _octaveAnchor = anchorOctave + (RelativeOctave.StepIndex(l) >= rootStep ? 0 : 1);
            }
            else
            {
                _octaveAbsolute = savedAbsolute; // the root, and any rest
            }
            if (member is PitchSyntax pitch)
                EmitArpeggioXmlPitch(pitch, isRoot ? groupOctave : 0);
            else if (member is ChordSyntax chord)
                ProcessChord(chord, isRoot ? groupOctave : 0);
            else
                ProcessNode(member); // rest
            if (!rootSet && letter is { } rl)
            {
                rootSet = true;
                anchorOctave = _currentOctave;
                rootStep = RelativeOctave.StepIndex(rl);
            }
        }
        _octaveAbsolute = savedAbsolute;
        _octaveAnchor = savedAnchor;
        // Acts like one note: a trailing `>>N` carries N as the running duration.
        _defaultDuration = arpeggio.TotalDuration?.ToFraction() ?? savedDefault;
        // After the group the running reference is the root (chord-after behavior).
        if (rootSet)
        {
            _currentOctave = anchorOctave;
            _currentStep = rootStep;
        }

        if (sub.HasTuplet)
        {
            _tupletStack.Pop();
            if (_currentMeasure != null && ReferenceEquals(_currentMeasure, tupletMeasure))
            {
                var body = _currentMeasure.Notes;
                int firstIdx = -1, lastIdx = -1;
                for (int k = tupletFrom; k < body.Count; k++)
                {
                    if (body[k].IsChord || body[k].RawElement != null) continue;
                    if (firstIdx < 0) firstIdx = k;
                    lastIdx = k;
                }
                if (firstIdx >= 0)
                {
                    body[firstIdx].ExtraNotations.Add(TupletNotation("start", tupletNumber));
                    body[lastIdx].ExtraNotations.Add(TupletNotation("stop", tupletNumber));
                }
            }
        }
    }

    /// <summary>A bare arpeggio pitch → one sequential note at the forced member duration,
    /// resolved through the octave frame the caller set up.</summary>
    private void EmitArpeggioXmlPitch(PitchSyntax pitch, int octaveShift)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var (step, alter) = ParsePitch(pitch);
        // Stacked members arrive in forced-absolute mode (plain path). The ROOT, in
        // relative mode, anchors on its bare LETTER: its own '/, marks are LOCAL to
        // its sounding pitch and do not move the anchor the group propagates.
        int targetOctave;
        if (_octaveAbsolute)
        {
            targetOctave = ResolveRelativeOctave(pitch) + octaveShift;
            _currentOctave += octaveShift; // so the anchor octave carries the group shift
        }
        else
        {
            int stepIdx = RelativeOctave.StepIndex(pitch.BaseName);
            int anchor = RelativeOctave.Resolve(_currentStep, _currentOctave, stepIdx, 0) + octaveShift;
            targetOctave = anchor + pitch.OctaveOffset;
            _currentStep = stepIdx;
            _currentOctave = anchor;
        }
        (step, alter, targetOctave) = ApplyTranspose(pitch, step, alter, targetOctave);
        int quarter = pitch.QuarterOffset;

        var duration = _defaultDuration;
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);
        var (tupletActual, tupletNormal) = CurrentTupletRatio();

        _currentMeasure.Notes.Add(new MusicXmlNote
        {
            Step = step,
            Alter = quarter == 0 ? alter : alter + 0.5 * quarter,
            Octave = targetOctave,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            AccidentalName = (alter, quarter) switch
            {
                (0, 1) => "quarter-sharp",
                (1, 1) => "three-quarters-sharp",
                (0, -1) => "quarter-flat",
                (-1, -1) => "three-quarters-flat",
                _ => null,
            },
            ActualNotes = tupletActual,
            NormalNotes = tupletNormal,
        });
        MaybeClosePickup(duration);
    }

    /// <summary>A scale-degree arpeggio member → one sequential note, stacked on the group's
    /// anchor (the root, or the key tonic when no pitched member precedes — the caller
    /// resolves it) by diatonic steps in the WRITTEN key, then transposed like a pitch.</summary>
    private void EmitArpeggioXmlDegree(ScaleDegreeSyntax degree, int rootStep, int anchorOctave)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var (dstep, dalter, doctave) = ChordDegrees.Resolve(
            rootStep, anchorOctave, degree.Number, degree.Alteration, degree.OctaveOffset, _keyFifths);
        (dstep, dalter, doctave) = ApplyWrittenTransforms(dstep, dalter, doctave);

        var duration = _defaultDuration;
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);
        var (tupletActual, tupletNormal) = CurrentTupletRatio();

        _currentMeasure.Notes.Add(new MusicXmlNote
        {
            Step = "CDEFGAB"[dstep].ToString(),
            Alter = dalter,
            Octave = doctave,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            ActualNotes = tupletActual,
            NormalNotes = tupletNormal,
        });
        MaybeClosePickup(duration);
    }


    /// <summary>The written notes of every chord this walk has emitted, keyed by
    /// node — what a following <c>q</c> copies (post-transpose spelling; LP
    /// expands repetitions after \relative, so a q never re-reads the frame).</summary>
    private readonly Dictionary<ChordSyntax, List<(string Step, int Alter, int Octave)>> _resolvedChordXmlNotes = new();

    /// <summary>The resolved spelling of every pitched note this walk has
    /// emitted - what a following bare duration copies. Same contract as
    /// <see cref="_resolvedChordXmlNotes"/>.</summary>
    private readonly Dictionary<NoteSyntax, (string Step, int Alter, int Octave, int Quarter)> _resolvedNoteXml = new();

    /// <summary>A slash note: MusicXML's own reading is an UNPITCHED note with a
    /// slash head displayed on the middle line (B4 in every staff's display
    /// space), the same shape a drum note takes.</summary>
    private void ProcessSlashNote(SlashNoteSyntax slash)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var duration = GetDuration(slash.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);
        EmitPendingDynamic();

        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        var xmlNote = new MusicXmlNote
        {
            IsUnpitched = true,
            Step = "B",
            Octave = 4,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            ActualNotes = tupletActual,
            NormalNotes = tupletNormal,
            Notehead = "slash",
        };
        ProcessArticulations(slash.Articulations, xmlNote);
        _currentMeasure.Notes.Add(xmlNote);
        _lastPitchedNote = null;
        _lastEmittedNotes.Clear();
        MaybeClosePickup(duration);
    }

    /// <summary>A bare duration - the previous note, chord or slash again at the
    /// written length (LILYPOND-REF: lily/parser.yy music_embedded). The shape
    /// mirrors <see cref="ProcessChordRepetition"/>: resolved spellings recorded
    /// by this walk, the repetition's own post-events only.</summary>
    private void ProcessBareDuration(BareDurationSyntax bare)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var duration = GetDuration(bare.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);
        var (tupletActual, tupletNormal) = CurrentTupletRatio();

        switch (Music.BareDurations.OriginalOf(bare))
        {
            case NoteSyntax note when _resolvedNoteXml.TryGetValue(note, out var m):
            {
                EmitPendingDynamic();
                var xmlNote = new MusicXmlNote
                {
                    Step = m.Step,
                    Alter = m.Quarter == 0 ? m.Alter : m.Alter + 0.5 * m.Quarter,
                    Octave = m.Octave,
                    Duration = durationTicks,
                    Type = type,
                    Dots = dots,
                    ActualNotes = tupletActual,
                    NormalNotes = tupletNormal
                };
                ProcessArticulations(bare.Articulations, xmlNote);
                CloseTies([xmlNote]);
                if (bare.Articulations.OfType<TieSyntax>().Any()) OpenTies([xmlNote]);
                _currentMeasure.Notes.Add(xmlNote);
                _lastPitchedNote = xmlNote;
                _lastEmittedNotes.Clear();
                _lastEmittedNotes.Add(xmlNote);
                MaybeClosePickup(duration);
                return;
            }
            case ChordSyntax chord when _resolvedChordXmlNotes.TryGetValue(chord, out var members)
                && members.Count > 0:
            {
                EmitPendingDynamic();
                bool isFirst = true;
                foreach (var m in members)
                {
                    var xmlNote = new MusicXmlNote
                    {
                        Step = m.Step,
                        Alter = m.Alter,
                        Octave = m.Octave,
                        Duration = durationTicks,
                        Type = type,
                        Dots = dots,
                        IsChord = !isFirst,
                        ActualNotes = tupletActual,
                        NormalNotes = tupletNormal
                    };
                    if (isFirst)
                    {
                        ProcessArticulations(bare.Articulations, xmlNote);
                        isFirst = false;
                    }
                    _currentMeasure.Notes.Add(xmlNote);
                    _chordMembers.Add(xmlNote);
                }
                CloseTies(_chordMembers);
                if (bare.Articulations.OfType<TieSyntax>().Any()) OpenTies(_chordMembers);
                _lastEmittedNotes.Clear();
                _lastEmittedNotes.AddRange(_chordMembers);
                _chordMembers.Clear();
                MaybeClosePickup(duration);
                return;
            }
            case DrumNoteSyntax drum:
            {
                var info = DrumOverrides.Resolve(DrumOverridesMap, drum.DrumName);
                int idx = 6 + info.StaffPosition;
                int oct = 4 + (int)Math.Floor(idx / 7.0);
                string step = "CDEFGAB"[((idx % 7) + 7) % 7].ToString();
                EmitPendingDynamic();
                _currentMeasure.Notes.Add(new MusicXmlNote
                {
                    IsUnpitched = true,
                    Step = step,
                    Octave = oct,
                    Duration = durationTicks,
                    Type = type,
                    Dots = dots,
                    ActualNotes = tupletActual,
                    NormalNotes = tupletNormal,
                    Notehead = NoteheadName(info.Notehead),
                });
                _lastPitchedNote = null;
                _lastEmittedNotes.Clear();
                MaybeClosePickup(duration);
                return;
            }
            case SlashNoteSyntax:
            {
                EmitPendingDynamic();
                _currentMeasure.Notes.Add(new MusicXmlNote
                {
                    IsUnpitched = true,
                    Step = "B",
                    Octave = 4,
                    Duration = durationTicks,
                    Type = type,
                    Dots = dots,
                    ActualNotes = tupletActual,
                    NormalNotes = tupletNormal,
                    Notehead = "slash",
                });
                _lastPitchedNote = null;
                _lastEmittedNotes.Clear();
                MaybeClosePickup(duration);
                return;
            }
            default:
                // Nothing to repeat (the validator reports it): keep the time.
                _lastPitchedNote = null;
                _lastEmittedNotes.Clear();
                _currentMeasure.Notes.Add(new MusicXmlNote
                {
                    IsRest = true,
                    Duration = durationTicks,
                    Type = type,
                    Dots = dots
                });
                MaybeClosePickup(duration);
                return;
        }
    }

    /// <summary>
    /// Applies one tie/slur marker to the notes already emitted — the reading that says a
    /// post-event belongs to the music BEFORE it.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME, BECAUSE THE MARKER CAN ARRIVE FROM TWO PLACES. The parser keeps the
    /// tree in the order the characters were typed (HANDOFF §2F ⑺), so a marker written
    /// before another post-event — <c>&lt;&gt;)@text("sul D")</c> — is a child of its host
    /// and reaches this through the host's own walk, while one written last is the next
    /// item and reaches it through the sequence switch. Spelling the rule twice is how the
    /// empty chord lost its slur stop: MEASURED 2026-08-30 on
    /// audit/lp-regression/lys/empty-chord.lys, whose MusicXML dropped
    /// <c>&lt;slur type="stop"/&gt;</c> because <see cref="ProcessChord"/> returns before
    /// reading post-events and the sequence arm no longer saw the <c>)</c>.
    /// </remarks>
    private void ApplyMarkerToLastEmitted(SyntaxNode marker)
    {
        switch (marker)
        {
            case TieSyntax:
                // Tie follows a note or chord — mark EVERY note just emitted as a
                // tie start (a chord ties all its members), and flag the next
                // note/chord so it emits the matching tie-stop.
                if (_lastEmittedNotes.Count > 0)
                    OpenTies(_lastEmittedNotes);
                else if (_currentMeasure != null && _currentMeasure.Notes.Count > 0)
                    OpenTies([_currentMeasure.Notes[^1]]);
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
        }
    }

    private void ProcessChord(ChordSyntax chord, int extraOctave = 0)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var pitches = chord.Pitches.ToList();
        if (pitches.Count == 0 && !chord.Degrees.Any())
        {
            // An EMPTY chord emits no note of its own, so nothing downstream will read
            // its post-events — but a marker written on it still belongs to the music
            // before it (<>) closes the slur that opened two notes back).
            foreach (var postEvent in chord.Articulations)
                ApplyMarkerToLastEmitted(postEvent);
            return;
        }
        var resolved = new List<(string Step, int Alter, int Octave)>();

        var duration = GetDuration(chord.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        // Emit pending dynamic as direction before the chord
        EmitPendingDynamic();

        // The first member is the ROOT: its bare LETTER is the chord's ANCHOR; every
        // other member STACKS above the anchor — the same octave placement as a
        // scale degree, so the chord's pitches are independent of the written order
        // (<c e g> == <c 3 5> == <c g e>). Each member's own '/, marks (the root's
        // included) are LOCAL to that one note; the note after the chord is relative
        // to the anchor. A deliberate Lily# divergence from LilyPond, matching
        // MidiExporter and MeasureCollector.
        // Octave marks after the closing '>' (<1 3 5>' / <c e g>,,) shift the whole
        // chord; folding it into firstOctave flows through every stacked/degree member
        // and the following note, matching MidiExporter and MeasureCollector. extraOctave
        // is the enclosing arpeggio's group shift when this chord is its root.
        int chordOctave = chord.ChordOctaveOffset + extraOctave;

        int firstStep = _currentStep, firstOctave = _currentOctave;
        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        bool isFirst = true;
        foreach (var pitch in pitches)
        {
            var (step, alter) = ParsePitch(pitch);
            int targetOctave;
            if (isFirst)
            {
                if (_octaveAbsolute)
                {
                    targetOctave = ResolveRelativeOctave(pitch) + chordOctave; // advances state
                    firstOctave = _currentOctave + chordOctave;
                }
                else
                {
                    // The root's LETTER resolved bare = the chord's ANCHOR; its own
                    // '/, marks are LOCAL to its sounding pitch (<c' e g> = C5 E4 G4,
                    // and the next note stays relative to C4).
                    int stepIdx = RelativeOctave.StepIndex(pitch.BaseName);
                    int anchor = RelativeOctave.Resolve(_currentStep, _currentOctave, stepIdx, 0) + chordOctave;
                    targetOctave = anchor + pitch.OctaveOffset;
                    _currentStep = stepIdx;
                    _currentOctave = anchor;
                    firstOctave = anchor;
                }
                firstStep = _currentStep;
            }
            else if (_octaveAbsolute)
            {
                // Absolute mode: each member is a fixed pitch, no stacking.
                targetOctave = ResolveRelativeOctave(pitch) + chordOctave;
            }
            else
            {
                int stepIdx = RelativeOctave.StepIndex(pitch.BaseName);
                targetOctave = firstOctave + (stepIdx >= firstStep ? 0 : 1) + pitch.OctaveOffset;
            }
            (step, alter, targetOctave) = ApplyTranspose(pitch, step, alter, targetOctave);
            resolved.Add((step, alter, targetOctave));

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
                bool hasArp = chord.Articulations.Any(a2 =>
                    a2 is ArticulationSyntax { Type: ArticulationType.None } na
                    && na.NameToken.Text.Equals("arpeggio", StringComparison.OrdinalIgnoreCase));
                bool hasBracket = chord.Articulations.Any(a2 =>
                    a2 is MusicMarkSyntax mm
                    && Semantics.AnnotationValues.IsArpeggioBracket(mm));
                if (hasArp)
                    _chordArpeggio = "arpeggiate";
                else if (hasBracket)
                    _chordArpeggio = "non-arpeggiate";
                ProcessArticulations(chord.Articulations, xmlNote);
                isFirst = false;
            }

            // Arpeggio marks: <arpeggiate> on EVERY member; the bracket form
            // puts <non-arpeggiate> on the two OUTER members only.
            if (_chordArpeggio == "arpeggiate")
                xmlNote.ExtraNotations.Add(new System.Xml.Linq.XElement("arpeggiate",
                    new System.Xml.Linq.XAttribute("number", 1)));

            _currentMeasure.Notes.Add(xmlNote);
            _chordMembers.Add(xmlNote);
        }

        // Omitted root (<1 3 5> / <3 5>): anchor the degrees on the key's tonic
        // (degree 1 = tonic), resolved relatively like a written root.
        if (pitches.Count == 0 && chord.Degrees.Any())
        {
            int tonicStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
            firstOctave = RelativeOctave.Resolve(_currentStep, _currentOctave, tonicStep, 0) + chordOctave;
            firstStep = tonicStep;
            _currentStep = tonicStep;
            _currentOctave = firstOctave;
        }

        // Scale-degree members (<d 3 5 7,>): stack on the root by diatonic steps in
        // the (written) key, then apply the part transpose like any pitch. When the
        // root is omitted the FIRST degree is the chord's onset (no <chord/>).
        bool needsOnset = pitches.Count == 0;
        foreach (var degree in chord.Degrees)
        {
            var (dstep, dalter, doctave) = ChordDegrees.Resolve(
                firstStep, firstOctave, degree.Number, degree.Alteration,
                degree.OctaveOffset, _keyFifths);
            (dstep, dalter, doctave) = ApplyWrittenTransforms(dstep, dalter, doctave);
            resolved.Add(("CDEFGAB"[dstep].ToString(), dalter, doctave));
            var xmlNote = new MusicXmlNote
            {
                Step = "CDEFGAB"[dstep].ToString(),
                Alter = dalter,
                Octave = doctave,
                Duration = durationTicks,
                Type = type,
                Dots = dots,
                IsChord = !needsOnset,
                ActualNotes = tupletActual,
                NormalNotes = tupletNormal,
            };
            if (_chordArpeggio == "arpeggiate")
                xmlNote.ExtraNotations.Add(new System.Xml.Linq.XElement("arpeggiate",
                    new System.Xml.Linq.XAttribute("number", 1)));
            _currentMeasure.Notes.Add(xmlNote);
            _chordMembers.Add(xmlNote);
            needsOnset = false;
        }

        if (_chordArpeggio == "non-arpeggiate" && _chordMembers.Count >= 2)
        {
            _chordMembers[0].ExtraNotations.Add(new System.Xml.Linq.XElement("non-arpeggiate",
                new System.Xml.Linq.XAttribute("type", "bottom"),
                new System.Xml.Linq.XAttribute("number", 1)));
            _chordMembers[^1].ExtraNotations.Add(new System.Xml.Linq.XElement("non-arpeggiate",
                new System.Xml.Linq.XAttribute("type", "top"),
                new System.Xml.Linq.XAttribute("number", 1)));
        }
        _resolvedChordXmlNotes[chord] = resolved;

        // Ties apply to EVERY member of the chord: <c e g>~ <c e g> ties all
        // voices, so tagging only the first note (the old behavior) dropped the
        // rest. Pair the stop from a preceding tie across all members too.
        CloseTies(_chordMembers);
        if (chord.Articulations.OfType<TieSyntax>().Any()) OpenTies(_chordMembers);

        // Remember this chord's members so a following standalone '~' node ties
        // all of them (a chord tie), not just the last member.
        _lastEmittedNotes.Clear();
        _lastEmittedNotes.AddRange(_chordMembers);

        _chordArpeggio = null;
        _chordMembers.Clear();

        // Continue from the first chord note (LilyPond: next note is relative to
        // the chord's first pitch).
        _currentStep = firstStep;
        _currentOctave = firstOctave;
        MaybeClosePickup(duration);
    }

    /// <summary>A <c>q</c> chord repetition: the ORIGINAL chord's written notes at
    /// the repetition's own duration, with the repetition's own post-events. The
    /// octave frame is NOT touched — LP expands q after \relative resolution. A
    /// bad repetition (no chord before it) emits a rest of the written duration
    /// so the measure stays honest; the validator reports it.</summary>
    /// <remarks>LILYPOND-REF: scm/music-functions.scm:854-946 copy-repeat-chord + expand-repeat-chords!</remarks>
    private void ProcessChordRepetition(ChordRepetitionSyntax rep)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var duration = GetDuration(rep.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        if (ChordRepetitions.OriginalOf(rep) is not { } original
            || !_resolvedChordXmlNotes.TryGetValue(original, out var members)
            || members.Count == 0)
        {
            _lastPitchedNote = null;
            _lastEmittedNotes.Clear();
            _currentMeasure.Notes.Add(new MusicXmlNote
            {
                IsRest = true,
                Duration = durationTicks,
                Type = type,
                Dots = dots
            });
            MaybeClosePickup(duration);
            return;
        }

        EmitPendingDynamic();
        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        bool isFirst = true;
        foreach (var m in members)
        {
            var xmlNote = new MusicXmlNote
            {
                Step = m.Step,
                Alter = m.Alter,
                Octave = m.Octave,
                Duration = durationTicks,
                Type = type,
                Dots = dots,
                IsChord = !isFirst,
                ActualNotes = tupletActual,
                NormalNotes = tupletNormal
            };
            if (isFirst)
            {
                // The repetition's OWN post-events only — LP copies note events,
                // not the original's articulations.
                ProcessArticulations(rep.Articulations, xmlNote);
                isFirst = false;
            }
            _currentMeasure.Notes.Add(xmlNote);
            _chordMembers.Add(xmlNote);
        }

        CloseTies(_chordMembers);
        if (rep.Articulations.OfType<TieSyntax>().Any()) OpenTies(_chordMembers);

        _lastEmittedNotes.Clear();
        _lastEmittedNotes.AddRange(_chordMembers);
        _chordMembers.Clear();
        MaybeClosePickup(duration);
    }

    /// <summary>Written pitch identity — what a tie joins. Two notes are the same notehead
    /// when the step, the accidental and the octave agree; an unpitched member (a drum in a
    /// chord) is never the same as anything, so it cannot be tied.</summary>
    private static bool SameNotehead(MusicXmlNote a, MusicXmlNote b)
        => a.Step != null && b.Step != null && a.Step == b.Step && a.Octave == b.Octave
           && (int)System.Math.Round(a.Alter ?? 0) == (int)System.Math.Round(b.Alter ?? 0);

    /// <summary>Start a tie on every member of the onset just written.</summary>
    private void OpenTies(IReadOnlyList<MusicXmlNote> from)
    {
        foreach (var n in from) { n.TieStart = true; _tieOpen.Add(n); }
        _tieToNextNote = true;
    }

    /// <summary>End the open ties on the onset now arriving, PAIRED BY PITCH.</summary>
    /// <remarks>
    /// ⚠️ A tie joins two noteheads of the same pitch, so `&lt;c f g c&gt;~ &lt;c e g c&gt;`
    /// sustains the c, g and c, ENDS the f and ATTACKS the e — the corpus states the rule in
    /// `test/feature-tour` (「一部不一致なら共通分のみ」) and the MIDI walk plays it. Marking
    /// every arriving member as a stop instead wrote `&lt;tie type="stop"/&gt;` on a note with
    /// no start to stop, which is not a MusicXML document any importer can read as intended.
    /// ⚠️ THE START IS RETRACTED, not just left unmatched: it was written one onset ago, when
    /// what followed was still unknown, and a start with no stop is the same broken pair seen
    /// from the other end.
    /// </remarks>
    private void CloseTies(IReadOnlyList<MusicXmlNote> arriving)
    {
        if (!_tieToNextNote) return;
        _tieToNextNote = false;
        foreach (var m in arriving)
        {
            int k = _tieOpen.FindIndex(n => SameNotehead(n, m));
            if (k < 0) continue;
            m.TieStop = true;
            _tieOpen.RemoveAt(k);
        }
        foreach (var n in _tieOpen) n.TieStart = false; // nothing continued it
        _tieOpen.Clear();
    }

    /// <summary>
    /// The rest half of <see cref="ProcessNote"/>, for <c>a4@rest</c>: the resolved pitch
    /// becomes display-step / display-octave inside the <c>&lt;rest&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The state it clears is <see cref="ProcessRest"/>'s, for <see cref="ProcessRest"/>'s
    /// reason — a rest breaks a hammer-on/pull-off pair and cannot be tied, so a following
    /// <c>~</c> must not tie the note before it.
    /// </remarks>
    private void EmitPitchedRest(string step, int alter, int octave,
        int durationTicks, string? type, int dots, Fraction duration)
    {
        if (_currentMeasure == null) return;
        _lastPitchedNote = null;
        _lastEmittedNotes.Clear();

        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        _currentMeasure.Notes.Add(new MusicXmlNote
        {
            IsRest = true,
            RestHasDisplayPitch = true,
            Step = step,
            Octave = octave,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            ActualNotes = tupletActual,
            NormalNotes = tupletNormal,
        });
        MaybeClosePickup(duration);
    }

    private void ProcessRest(RestSyntax rest)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;
        // A rest breaks a hammer-on/pull-off pair (no note is held into it) and
        // cannot be tied, so a following '~' must not tie the pre-rest note.
        _lastPitchedNote = null;
        _lastEmittedNotes.Clear();

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

    /// <summary>
    /// Writes the grace notes of a <c>grace { … }</c> body.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE BODY IS READ THROUGH THE STATEMENT THE PAGE READS
    /// (<see cref="Semantics.GraceBodySupport"/>), so a phrase named in a grace body is
    /// expanded here exactly as <c>MeasureCollector.CollectGraceNotes</c> expands it. This
    /// walk used to take <c>grace.Body.Items</c> itself: MEASURED 2026-08-30 (session 301,
    /// scratch/p301/ab) <c>grace { G } c'4 c'2.</c> wrote NO <c>&lt;grace/&gt;</c> element at
    /// all where the inline spelling wrote two, on a page that engraves the two.
    /// <para>
    /// ⚠️ THE GRACE GROUP HAS ITS OWN DURATION MEMORY, opening at an EIGHTH. That is the
    /// LAYOUT's rule and it is read from there (<c>CollectGraceNotes</c>'
    /// <c>graceDefaultDuration</c>; LilyPond has no grace-specific default at all). This
    /// walker used to share <see cref="_defaultDuration"/> with the main stream, which made
    /// it the FOURTH answer to a question 2026-08-01 declared to have one home — MEASURED:
    /// <c>grace { c' } d'4</c> wrote <c>&lt;type&gt;quarter&lt;/type&gt;</c> where the page,
    /// the MIDI and the <c>.ly</c> twin all say an eighth. The main stream's memory is now
    /// left alone, the way the other three readers leave it alone.
    /// </para>
    /// </remarks>
    private void ProcessGraceNotes(GraceExpressionSyntax grace)
    {
        if (_currentMeasure == null) return;

        bool isAcciaccatura = grace.IsAcciaccatura;
        // The grace group's own memory. Written duration threads WITHIN the group and the
        // main stream's is untouched, so `grace { d16 e } c4` leaves the c a quarter.
        Fraction graceDuration = Fraction.Eighth;
        int expansionBudget = Svg.Collector.MeasureCollector.DefaultExpansionBudgetCap;
        // What a phrase reference borrows and must give back — only what THIS reader reads.
        // ⚠️ ALLOCATED ONLY IF A REFERENCE IS ACTUALLY WRITTEN: a grace body naming a phrase
        // is rare (2 books in the whole 1754-book sweep) and this runs once per grace.
        Stack<((int step, int alt, int oct)? Transpose, int Anchor,
            int? AnchorStep, int Offset)>? phraseFrames = null;

        foreach (var (item, _) in Semantics.GraceBodySupport.BodyElements(
                     grace,
                     name => _variables.TryGetValue(name, out var body) ? body : null,
                     () => expansionBudget-- > 0))
        {
            switch (item)
            {
                // The same fresh frame the main stream's reference opens (ProcessNode's
                // VariableReferenceSyntax arm), armed off the MARKER: the expander has
                // already read the octave marks and the anchor, and reading them a second
                // time here is how the two walks would drift apart.
                // ⚠️ IT IS THE SECOND SPELLING OF THAT ARM and cannot be folded into it (that
                // one takes a reference node and recurses; this one takes a flattened
                // marker), so per checklist 7.7 the pair carries a DIFFERENTIAL net:
                // MusicXmlExportShapeTests.APhraseInAGraceBody_HandsTheExportedChainBackAtItsAnchor
                // asks both spellings for the note after the same phrase and demands one answer.
                case Svg.Collector.RelativeResetMarker reset:
                {
                    int? anchorStep = reset.AnchorStep == Music.PhraseAnchor.Tonic
                        ? (_ambientTonic.Valid ? _ambientTonic.Step : 0)
                        : reset.AnchorStep;
                    (phraseFrames ??= new()).Push((_currentTranspose, _octaveAnchor,
                        anchorStep, reset.OctaveOffset));
                    _currentOctave = _partAnchorOctave + reset.OctaveOffset;
                    _currentStep = 0;
                    _currentTranspose =
                        PitchTransposer.Compose(PhraseTransposeTarget(), _currentTranspose);
                    _octaveAnchor += reset.OctaveOffset;
                    // `grace { c'16 G }` gives G's undurated first note the group's eighth,
                    // the same note `grace { G }` gives it — the boundary restores what this
                    // reader reads, and a duration is one of those things.
                    graceDuration = Fraction.Eighth;
                    break;
                }

                case Svg.Collector.PhraseEndMarker:
                {
                    // The pair is emitted or omitted together (GraceBodySupport.Expand pays
                    // for a whole entry or none of it), so this cannot underflow. ⚠️ The
                    // guard is the PAGE's own shape rather than a fallback invented here —
                    // MeasureCollector.ExitPhraseTranspose guards all three of its saves with
                    // the same `Count > 0`.
                    if (phraseFrames is not { Count: > 0 })
                        break;
                    var (savedTranspose, savedAnchor, anchorStep, offset) = phraseFrames.Pop();
                    _currentTranspose = savedTranspose;
                    _octaveAnchor = savedAnchor;
                    // Hand-off at the phrase's ANCHOR — the reference is ONE item, the chord
                    // rule, so its interior never leaks into what follows.
                    if (anchorStep is { } astep)
                    {
                        _currentStep = astep;
                        _currentOctave = RelativeOctave.Resolve(
                            0, _partAnchorOctave + offset, astep, 0);
                    }
                    break;
                }

                case NoteSyntax note:
                {
                    var (step, alter) = ParsePitch(note.Pitch);
                    int targetOctave = ResolveRelativeOctave(note.Pitch);

                    if (note.Duration != null)
                        graceDuration = note.Duration.ToFraction();
                    var (type, _) = GetNoteType(graceDuration);

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
                    break;
                }
            }
        }
    }

    private void ProcessArticulations(IEnumerable<SyntaxNode> articulations, MusicXmlNote xmlNote)
    {
        // Pre-scan the frame spec so a chord symbol on the same note can
        // embed it, whichever order the marks were written in.
        // ⚠️ This read used to take whatever followed "frame." with NO gate, so a spec
        // Lily# refuses to draw still reached the XML. It asks the one reader now.
        _noteFrameSpec = null;
        foreach (var artic in articulations)
            if (artic is MusicMarkSyntax fm
                && Semantics.AnnotationValues.Frame(fm) is { } spec)
                _noteFrameSpec = spec;

        foreach (var artic in articulations)
        {
            if (artic is ArticulationSyntax articulation)
            {
                // Single-word direction marks (@sustain, @sostenuto, @ottava, @loco)
                // parse as name-only articulations, not compound marks.
                if (articulation.Type == ArticulationType.None)
                    ProcessDirectionName(articulation.NameToken.Text.ToLowerInvariant());

                // Guitar/TAB techniques → <technical> children. Hammer-on /
                // pull-off are exported as text technicals (the paired
                // start/stop form needs both notes; the letter is what TAB
                // readers print anyway).
                switch (articulation.Type)
                {
                    case ArticulationType.Tap:
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("tap"));
                        break;
                    case ArticulationType.SnapPizz:
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("snap-pizzicato"));
                        break;
                    case ArticulationType.Thumb:
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("thumb-position"));
                        break;
                    case ArticulationType.Heel:
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("heel"));
                        break;
                    case ArticulationType.Toe:
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("toe"));
                        break;
                    case ArticulationType.Stopped:
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("stopped"));
                        break;
                    case ArticulationType.HammerOn:
                        // Proper paired form: start on the PREVIOUS note (the
                        // one struck), stop on this one.
                        _lastPitchedNote?.Technicals.Add(new System.Xml.Linq.XElement("hammer-on",
                            new System.Xml.Linq.XAttribute("type", "start"),
                            new System.Xml.Linq.XAttribute("number", 1), "H"));
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("hammer-on",
                            new System.Xml.Linq.XAttribute("type", "stop"),
                            new System.Xml.Linq.XAttribute("number", 1)));
                        break;
                    case ArticulationType.PullOff:
                        _lastPitchedNote?.Technicals.Add(new System.Xml.Linq.XElement("pull-off",
                            new System.Xml.Linq.XAttribute("type", "start"),
                            new System.Xml.Linq.XAttribute("number", 1), "P"));
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("pull-off",
                            new System.Xml.Linq.XAttribute("type", "stop"),
                            new System.Xml.Linq.XAttribute("number", 1)));
                        break;
                }

                var articName = MapArticulation(articulation.Type);
                if (articName != null)
                    xmlNote.Articulations.Add(articName);

                var ornamentName = MapOrnament(articulation.Type);
                if (ornamentName != null)
                    xmlNote.Ornaments.Add(ornamentName);
            }
            else if (artic is DynamicSyntax dynamic)
            {
                HandleDynamicText(dynamic.DynamicToken.Text);
            }
            else if (artic is MusicMarkSyntax mark)
            {
                ProcessDirectionMark(mark);
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

    /// <summary>Whether a crescendo/diminuendo wedge is open (closed by the
    /// next level dynamic).</summary>
    private bool _wedgeOpen;

    /// <summary>A dynamic word: cresc/decresc/dim OPEN a &lt;wedge&gt; (they
    /// used to leak into &lt;dynamics&gt; as invalid &lt;cresc/&gt;); a level
    /// mark closes any open wedge, then emits as a dynamics direction.</summary>
    private void HandleDynamicText(string text)
    {
        if (text is "cresc" or "decresc" or "dim")
        {
            _currentMeasure?.Directions.Add(new MusicXmlDirection
            {
                WedgeType = text == "cresc" ? "crescendo" : "diminuendo",
                Placement = "below",
            });
            _wedgeOpen = true;
            return;
        }
        if (_wedgeOpen)
        {
            _currentMeasure?.Directions.Add(new MusicXmlDirection
            {
                WedgeType = "stop",
                Placement = "below",
            });
            _wedgeOpen = false;
        }
        _pendingDynamic = text;
    }

    /// <summary>Direction-family compound marks attached to a note:
    /// pedal (@sustain / @!sustain / @sostenuto / @!sostenuto), ottava lines
    /// (@ottava / @ottava.bassa / @loco) and chord symbols (@chord(...)).
    /// Everything else stays with its specialized consumer.</summary>
    private void ProcessDirectionMark(MusicMarkSyntax mark)
    {
        // The chord symbol is read from the ANNOTATION, not from its dotted name:
        // its argument is a sub-language whose written text the argument node already
        // holds (VALUE_SITE_AUDIT §9.5.3 ⑴). A bare '@chord' names nothing HERE — the
        // symbol it derives from its notes is the collector's, not the exporter's —
        // and the empty string it answers with keeps it out of the <harmony> below,
        // exactly as `StartsWith("chord.")` did.
        if (_currentMeasure != null
            && LilySharp.Core.Semantics.AnnotationValues.Chord(mark, out _) is { Length: > 0 } chordText)
        {
            if (BuildHarmony(chordText) is { } harmony)
            {
                // A @frame on the same note nests inside the harmony (MusicXML
                // <frame> is a harmony child).
                if (_noteFrameSpec is { } fspec && BuildFrame(fspec) is { } frameEl)
                    harmony.Add(frameEl);
                _currentMeasure.Notes.Add(new MusicXmlNote { RawElement = harmony });
            }
            return;
        }

        // Figured bass is read from the annotation too, and for the same reason: it is a
        // sub-language, so it parses the argument TOKENS (§9.5.3 ⑴). This used to sit in
        // the default arm of the dotted-name switch below, which is now left with the
        // marks that really are named by a dotted name.
        if (_currentMeasure != null
            && LilySharp.Core.Semantics.AnnotationValues.Figures(mark) is { } figures
            && BuildFiguredBass(figures) is { } figuredBass)
        {
            // <figured-bass> sits before its bass note, like <harmony>.
            _currentMeasure.Notes.Add(new MusicXmlNote { RawElement = figuredBass });
            return;
        }

        // '@!X' is a TERMINATOR, and its NAME is the name of what it ENDS — so handing it to
        // the table below would emit that thing's START: '@!ottava' would OPEN an octave
        // shift. The families are told apart here instead.
        if (mark.IsSpanEnd)
        {
            // ⚠️ EVERY TERMINATOR MUSICXML HAS A SPELLING FOR MUST BE ANSWERED HERE, because
            // the moment a family gains '@!' its stop stops arriving at the table below. Both
            // of these used to be ordinary marks the table answered — '@loco' and
            // '@!sustain' — and both would have been dropped in silence by an early return.
            if (_currentMeasure != null)
                switch (Svg.Model.MusicMarkItem.ParseSpanEndName(mark.Name))
                {
                    case Svg.Model.MusicMarkType.OttavaStop:
                        _currentMeasure.Directions.Add(
                            new MusicXmlDirection { OctaveShiftType = "stop" });
                        break;
                    // MusicXML has ONE pedal stop for all three pedals.
                    case Svg.Model.MusicMarkType.SustainOff:
                    case Svg.Model.MusicMarkType.SostenutoOff:
                    case Svg.Model.MusicMarkType.UnaCordaOff:
                        _currentMeasure.Directions.Add(
                            new MusicXmlDirection { PedalType = "stop", Placement = "below" });
                        break;
                    // The text spanner has no stop spelling here — the silence '@rit' meets.
                }
            return;
        }

        ProcessDirectionName(mark.MarkName);
    }

    private void ProcessDirectionName(string rawName)
    {
        if (_currentMeasure == null) return;
        var name = rawName.ToLowerInvariant();
        switch (name)
        {
            case "sustain":
                _currentMeasure.Directions.Add(new MusicXmlDirection { PedalType = "start", Placement = "below" });
                break;
            // '@treCorde' is the una corda's release written as a word rather than as '@!';
            // it is the same mark, so it is the same <pedal type="stop">.
            case "trecorde":
                _currentMeasure.Directions.Add(new MusicXmlDirection { PedalType = "stop", Placement = "below" });
                break;
            case "sostenuto":
                _currentMeasure.Directions.Add(new MusicXmlDirection { PedalType = "sostenuto", Placement = "below" });
                break;
            case "ottava":
                // 8va above: MusicXML octave-shift "down" (written an octave
                // below the sounding pitch).
                _currentMeasure.Directions.Add(new MusicXmlDirection { OctaveShiftType = "down" });
                break;
            case "ottava.bassa":
                _currentMeasure.Directions.Add(new MusicXmlDirection { OctaveShiftType = "up", Placement = "below" });
                break;
        }
    }

    /// <summary>A &lt;figured-bass&gt; from a parsed continuo figure group. Each
    /// figure emits a &lt;figure-number&gt; with the accidental as a &lt;suffix&gt;
    /// (6♯), a bare accidental as a &lt;prefix&gt;, and a held figure as
    /// &lt;extend&gt;. Element order follows the MusicXML DTD: prefix, number,
    /// suffix, extend.</summary>
    private static System.Xml.Linq.XElement? BuildFiguredBass(
        System.Collections.Immutable.ImmutableArray<LilySharp.Core.Svg.Model.FiguredBassFigure> figures)
    {
        if (figures.IsDefaultOrEmpty)
            return null;

        var fb = new System.Xml.Linq.XElement("figured-bass");
        foreach (var f in figures)
        {
            var figure = new System.Xml.Linq.XElement("figure");
            string? acc = f.Alteration switch
            {
                1 => "sharp",
                -1 => "flat",
                2 => "natural",
                _ => null,
            };
            if (f.Held)
                figure.Add(new System.Xml.Linq.XElement("extend",
                    new System.Xml.Linq.XAttribute("type", "continue")));
            else if (f.Number > 0)
            {
                figure.Add(new System.Xml.Linq.XElement("figure-number", f.Number));
                if (acc != null)
                    figure.Add(new System.Xml.Linq.XElement("suffix", acc));
            }
            else if (acc != null)
                figure.Add(new System.Xml.Linq.XElement("prefix", acc));
            else
                continue;

            fb.Add(figure);
        }
        return fb.HasElements ? fb : null;
    }

    /// <summary>&lt;harmony&gt; from a chord display text ("Cm7", "B♭maj7",
    /// "C/E"): root step + alter, a kind from the common-suffix map (unknown
    /// suffixes keep kind "other" with the original text), optional bass.</summary>
    private static System.Xml.Linq.XElement? BuildHarmony(string chordText)
    {
        string text = chordText;
        string? bass = null;
        int slash = text.IndexOf('/');
        if (slash > 0)
        {
            bass = text[(slash + 1)..];
            text = text[..slash];
        }
        if (text.Length == 0 || text[0] < 'A' || text[0] > 'G')
            return null;
        string rootStep = text[..1];
        int rootAlter = 0;
        int qi = 1;
        if (text.Length > 1 && (text[1] == '♭' || text[1] == 'b')) { rootAlter = -1; qi = 2; }
        else if (text.Length > 1 && (text[1] == '♯' || text[1] == '#')) { rootAlter = 1; qi = 2; }
        string suffix = text[qi..];

        string kind = suffix switch
        {
            "" => "major",
            "m" => "minor",
            "7" => "dominant",
            "m7" => "minor-seventh",
            "maj7" => "major-seventh",
            "dim" => "diminished",
            "dim7" => "diminished-seventh",
            "aug" => "augmented",
            "sus4" => "suspended-fourth",
            "sus2" => "suspended-second",
            "6" => "major-sixth",
            "m6" => "minor-sixth",
            "9" => "dominant-ninth",
            "maj9" => "major-ninth",
            "m9" => "minor-ninth",
            "mmaj7" => "major-minor",
            _ => "other",
        };

        var root = new System.Xml.Linq.XElement("root",
            new System.Xml.Linq.XElement("root-step", rootStep));
        if (rootAlter != 0)
            root.Add(new System.Xml.Linq.XElement("root-alter", rootAlter));

        var kindEl = new System.Xml.Linq.XElement("kind", kind);
        if (kind == "other")
            kindEl.Add(new System.Xml.Linq.XAttribute("text", suffix));

        var harmony = new System.Xml.Linq.XElement("harmony", root, kindEl);
        if (bass is { Length: > 0 } && bass[0] >= 'A' && bass[0] <= 'G')
        {
            var bassEl = new System.Xml.Linq.XElement("bass",
                new System.Xml.Linq.XElement("bass-step", bass[..1]));
            if (bass.Length > 1 && (bass[1] == '♭' || bass[1] == 'b'))
                bassEl.Add(new System.Xml.Linq.XElement("bass-alter", -1));
            else if (bass.Length > 1 && (bass[1] == '♯' || bass[1] == '#'))
                bassEl.Add(new System.Xml.Linq.XElement("bass-alter", 1));
            harmony.Add(bassEl);
        }
        return harmony;
    }

    /// <summary>&lt;frame&gt; from a diagram spec ("x32010", LOW string
    /// first): frame-note per sounding string (string 1 = highest pitch),
    /// muted strings omitted per the schema.</summary>
    private static System.Xml.Linq.XElement? BuildFrame(string spec)
    {
        int strings = spec.Length;
        if (strings < 4) return null;
        var frame = new System.Xml.Linq.XElement("frame",
            new System.Xml.Linq.XElement("frame-strings", strings),
            new System.Xml.Linq.XElement("frame-frets", 4));
        for (int i = 0; i < strings; i++)
        {
            char ch = spec[i];
            if (ch == 'x') continue;
            int fret = ch is >= '1' and <= '9' ? ch - '0' : 0;
            frame.Add(new System.Xml.Linq.XElement("frame-note",
                new System.Xml.Linq.XElement("string", strings - i),
                new System.Xml.Linq.XElement("fret", fret)));
        }
        return frame;
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
            ArticulationType.Scoop => "scoop",
            ArticulationType.Plop => "plop",
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
            ? _octaveAnchor + pitch.OctaveOffset
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

    /// <summary>A &lt;tuplet&gt; notation bracket (start / stop) for the visual
    /// bracket + ratio number, alongside the note's &lt;time-modification&gt;.</summary>
    private static System.Xml.Linq.XElement TupletNotation(string type, int number)
        => new("tuplet",
            new System.Xml.Linq.XAttribute("type", type),
            new System.Xml.Linq.XAttribute("number", number));

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

        // A breve (2/1) or longa (4/1) has denominator 1 and numerator >= 2. Without
        // this they collapse to baseDenom 1 => "whole" with double/quadruple ticks;
        // the switch's "breve" arm is otherwise unreachable (Denominator is never 0).
        if (duration.Denominator == 1 && duration.Numerator >= 2)
            return (duration.Numerator >= 4 ? "long" : "breve", 0);

        string type = baseDenom switch
        {
            0 => "breve",
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
