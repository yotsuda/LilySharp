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
    // …and the share of it the document's <transpose> states: the instrument's alone, the
    // header clef's octave being in the pitches (MusicXmlPart.PitchOctaveShift).
    private int _partTransposeWritten;
    private bool _initialOctaveAbsolute; // file-level default, restored per part
    private bool _tieToNextNote;      // a tie was seen; the next note/chord ends it (gets tie-stop)
    private Fraction _defaultDuration = Fraction.Quarter;

    // Active tuplet nesting: (actual, normal) = "actual notes in the time of normal"
    // (a triplet is (3, 2)). Scales note durations and drives <time-modification>.
    private readonly Stack<(int Actual, int Normal)> _tupletStack = new();
    private int _measureNumber = 1;
    // Anacrusis (partial) state: the pickup pending is _bars.Partial (Semantics.BarContext —
    // the page's rule: a `partial` arms it, the bar line that closes a bar spends it); while
    // one is open, accumulate the duration written into it and auto-close the implicit
    // measure once it reaches the declared length (the page's MeasureBuilder.AddItem
    // auto-complete). _justAutoClosedPickup absorbs a written barline that immediately
    // follows the auto-close, so no empty measure is emitted. Until 2026-10-03 (p755,
    // owner's decision A2) the pickup was two fields of this exporter's own and a bar line
    // did NOT spend it: a short pickup (`partial 2  c4 | d4 e f g |`) ran on across the `|`
    // and closed after the d4, where the page, the MIDI and the twin close it at the `|`.
    private Fraction _pickupAccumulated = Fraction.Zero;
    private bool _justAutoClosedPickup;
    // A block just closed its last bar and handed back an EMPTY measure: a `voice { } { }`
    // span (ProcessParallelVoices), or a repeat pass that ended on a full bar
    // (CloseFullBarAtPassEnd). The `|` written after the block closes that bar — time has
    // passed since the boundary — so it must not read the empty measure as the second half
    // of a `| |` pair: every `voice { … } { … } |` wrote a blank bar of rest after it (a
    // two-bar repro exported four measures). Cleared at the next barline and by
    // FlushCurrentMeasure.
    private bool _barClosedByBlock;
    // Whether a bar line has been met since this scope (part / section) opened — a `|:`
    // that opens the scope closes nothing, one met later pairs like a bare `|`
    // (MidiExporter.ProcessSequence's atScopeStart, MeasureBuilder._atScopeStart).
    private bool _barSeenInScope;
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
    // The meter in force (the pair, the additive numerator's text, `time none`) and the score's
    // home meter a section reverts to: Semantics.BarContext, the one spelling of those rules
    // (REFACTOR_PLAN stage C3; the MIDI moved first). The pickup is still this exporter's own —
    // it spends it by the duration written into the implicit measure (MaybeClosePickup), a
    // different rule from the MIDI's and the page's bar-line spending (see the type's remarks).
    private readonly Semantics.BarContext _bars = new();
    // The meter as the document states it — one tuple, so a written <time> can be told from a repeat.
    private (int Beats, string? BeatsText, int BeatType, bool Senza) RunningTime
        => (_bars.Meter.Beats, _bars.Meter.BeatsText, _bars.Meter.BeatType, _bars.SenzaMisura);
    private int _keyFifths = 0;
    private string _keyMode = "major";
    private string _clefSign = "G";
    private int _clefLine = 2;
    private int? _clefOctaveChange; // ±1 for the _8 / ^8 clefs

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
    // ⚠️ PER PART (2026-09-29): the three above are the part being written's, and
    // EnsurePart swaps them in and out here. Until then one triple served every part, so the
    // SECOND part of a section that states `time 3/4` saw "3/4 already written" — by the
    // first part — and got no <time> at all, while each part's opening bar repeated its clef
    // because the other part's clef was the last one "written" (MEASURED, Lab
    // sessions/p684/probes/silentmeter2: bass B and C with no <time>, both parts' C with a
    // second <clef>).
    private readonly Dictionary<string, ((int Fifths, string? Mode, string? Custom)? Key,
        (int Beats, string? BeatsText, int BeatType, bool Senza)? Time,
        (string Sign, int Line, int? OctaveChange)? Clef)> _writtenByPart = new();
    // The score's own signature, captured after the metadata pass. (Its own METER is captured
    // at the same spot and reverted at the same boundary — _bars.CaptureHome / RevertToHome.
    // See ScoreHomeMeter: the collector reverts both at a section boundary, and this exporter
    // reverted neither until 2026-08-31 — a mid-section `time 3/4` leaked into every following
    // section of the exported document.)
    private (int Fifths, string Mode, string? Custom)? _homeKey;
    // A change seen after the bar had started. The measure carries ONE attributes slot,
    // rendered at its head, so writing it here would move the change a bar early; it
    // waits for the next measure instead.
    private bool _attributesDirty;
    private string? _keyCustomXml;  // non-traditional key (encoded pairs)
    private string? _noteFrameSpec; // @diagram(...) on the note being written
    private int[] _partTuning = Tablature.Tunings.Guitar; // what a symbol-less @chord(x32010) is named on
    private TuningType? _partFrettedTuning;  // the part's fretted instrument: a diagram's tuning when the layout names none
    private (bool Read, string? Word, bool All, Music.ChordShapeTable? Table, int Capo) _diagramsWord;  // the score's chordDiagrams word as written (its `all`, its shape table, its capo), read once
    private MusicXmlNote? _lastPitchedNote; // hammer-on/pull-off start anchor
    private string? _pendingLineStop;       // "glissando" | "slide": stop lands on the NEXT note
    private string? _chordArpeggio;         // "arpeggiate" | "non-arpeggiate" for the chord being written
    private readonly List<MusicXmlNote> _chordMembers = new(); // members of the chord being written
    private readonly List<MusicXmlNote> _lastEmittedNotes = new(); // last note (1) or chord (N) — a following '~' node ties all of them
    // The notes carrying a tie-start that nothing has ended yet. Kept as the NOTES and
    // not as a flag, because the pair is decided at the NEXT onset: a start is written
    // before its continuation is known, so an unmatched one has to be taken back.
    private readonly List<MusicXmlNote> _tieOpen = new();

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

    /// <summary>
    /// The <c>layout { }</c> switches of the score being written — its layout reference over
    /// the file's default, the page's reading (<see cref="Semantics.LayoutPlanReader"/>) — or,
    /// with no score chosen, the file's first score's. Read once.
    /// </summary>
    private Semantics.LayoutPlan ScoreLayoutPlan()
    {
        if (_scoreLayoutPlan is { } read)
            return read;
        var plan = Semantics.LayoutPlan.Default;
        if (_root != null)
        {
            if (Score is { } score)
            {
                var file = Semantics.LayoutPlanReader.FileDefault(_root) is { } f
                    ? Semantics.LayoutPlanReader.Read(f, out _)
                    : Semantics.LayoutPlan.Default;
                plan = score.LayoutRef is { } layoutRef
                    ? Semantics.LayoutPlanReader.ReadReference(_root, layoutRef, file)
                    : file;
            }
            else
                plan = Semantics.LayoutPlanReader.Resolve(_root,
                    _root.DescendantNodes().OfType<RenderDeclarationSyntax>().FirstOrDefault());
        }
        return (_scoreLayoutPlan = plan);
    }

    private Semantics.LayoutPlan? _scoreLayoutPlan;

    /// <summary>
    /// The tuning an <c>@chord</c>'s diagram draws on in the exported score (owner's decisions
    /// 2026-09-28): the score's <c>layout { chordDiagrams T }</c> — the <see cref="Score"/> being
    /// written (its layout reference over the file's default), else the file's first score, the
    /// page's reading (<see cref="Semantics.LayoutPlanReader"/>) — else the part's fretted
    /// instrument, else the guitar; null under <c>chordDiagrams none</c>.
    /// </summary>
    private TuningType? DiagramTuning()
    {
        if (!_diagramsWord.Read && _root != null)
        {
            string? word;
            bool all;
            Music.ChordShapeTable? table;
            int capo;
            if (Score is { } score)
            {
                var file = Semantics.LayoutPlanReader.FileDefault(_root) is { } f
                    ? Semantics.LayoutPlanReader.Read(f, out _)
                    : Semantics.LayoutPlan.Default;
                var plan = score.LayoutRef is { } layoutRef
                    ? Semantics.LayoutPlanReader.ReadReference(_root, layoutRef, file)
                    : file;
                (word, all, table, capo) = (plan.ChordDiagrams, plan.ChordDiagramsAll, plan.ChordDiagramTable, plan.Chords.Capo);
            }
            else
            {
                var first = Semantics.ChordDiagramScores.Of(_root).FirstOrDefault();
                (word, all, table, capo) = (first?.LayoutWord, first?.All ?? false, first?.Table, first?.Capo ?? 0);
            }
            _diagramsWord = (true, word, all, table, capo);
        }
        return Semantics.ChordDiagramsKey.Resolve(_diagramsWord.Word, _partFrettedTuning);
    }

    /// <summary>The exported score's capo fret (read with <see cref="DiagramTuning"/>), 0 for
    /// none: a diagram is the pressed shape, a <c>chord(…)</c> item's strings sound that many
    /// semitones higher. The <c>&lt;harmony&gt;</c> stays the sounding chord (data).</summary>
    private int DiagramCapo
    {
        get
        {
            DiagramTuning();
            return _diagramsWord.Capo;
        }
    }

    /// <summary>The exported score's <c>chordDiagrams … all</c> (read with
    /// <see cref="DiagramTuning"/>, which is always asked first): every chord name draws a
    /// diagram, its written shape else the default.</summary>
    private bool DiagramsAll => _diagramsWord.All;

    /// <summary>The exported score's layout shape table (read with <see cref="DiagramTuning"/>):
    /// the chords that draw wherever they are named, or null.</summary>
    private Music.ChordShapeTable? DiagramTable => _diagramsWord.Table;

    internal MusicXmlDocument Export(SyntaxTree tree)
    {
        _diagramsWord = default;
        _scoreLayoutPlan = null;
        _document = new MusicXmlDocument();
        _pageModel = null;
        _tempoStated = false;

        var root = tree.GetRoot();
        _root = root;
        // Every section's voices and canonical bar count (SectionBarCounts, the semantic
        // counter): a part voice that writes fewer bars than its section-mates is padded
        // with silent measures when its span ends (PadVoice), as the page pads its staff.
        _sectionBars = Svg.Collector.SectionBarCounts.BuildSemanticIndex(root);
        _meterPlan = Svg.Collector.SectionMeterPlan.Build(root);
        _bareSectionOwner = RenderSpecParser.SingleEngravedPart(tree, Score, Form);
        _playedSpec = RenderSpecParser.PlayedSpec(tree, Score, Form);
        _placedChordRows = RenderSpecParser.PlacedChordRows(_playedSpec);
        ReadCarryRefusals(tree);
        _pendingChordRows.Clear();
        _playStartMeasure.Clear();
        _writtenByPart.Clear();
        (_writtenKey, _writtenTime, _writtenClef) = (null, null, null);
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
            _bars.CaptureHome();
            BuildSectionHeaderRegistry(root);
            ProcessSections(root);
            // The chord rows, once every part has written its bars (their measures are the
            // rows' homes). A flat top-level chord track is not handled, as the MIDI's is not.
            EmitPendingChordRows();
        }
        WriteCapo();
        WriteBeams(tree, hasSections);
        WriteTab(tree);
        WriteStaffLines();
        WriteTrackLyrics(tree);
        if (hasSections)
            WriteSectionLabels(tree);
        WritePartNames();
        MergeGrandStaves();
        WritePartGroups();

        return _document;
    }

    /// <summary>
    /// The score's staff groups as <c>&lt;part-group&gt;</c>s (<see cref="MusicXmlDocument.PartGroups"/>),
    /// read off the played score as the page draws them: every <c>grandStaff</c>,
    /// <c>staffGroup</c> and <c>choirStaff</c>, at any depth, over the parts its staves are —
    /// a grand staff merged into one part (MergeGrandStaves) is a part, not a group. Until
    /// 2026-09-30 a <c>staffGroup</c> or <c>choirStaff</c> wrote nothing and a reader drew
    /// its staves ungrouped.
    /// </summary>
    private void WritePartGroups()
    {
        if (_playedSpec == null)
            return;
        void Walk(IEnumerable<Svg.Collector.RenderItemSpec> items)
        {
            foreach (var item in items)
            {
                if (item is not Svg.Collector.GrandStaffRenderSpec g)
                    continue;
                var indices = PartsOf(g.GrandStaff.Members)
                    .Select(p => Document.Parts.IndexOf(p)).Where(i => i >= 0).Distinct().Order().ToList();
                // A group over one part says nothing; a group whose parts are not a run cannot be
                // written as one.
                if (indices.Count >= 2 && indices[^1] - indices[0] == indices.Count - 1)
                {
                    var (symbol, barline) = g.GrandStaff.Type switch
                    {
                        Svg.Model.StaffGroupType.GrandStaff => ("brace", true),
                        Svg.Model.StaffGroupType.ChoirStaff => ("bracket", false),
                        _ => ("bracket", true),
                    };
                    Document.PartGroups.Add((Document.Parts[indices[0]], Document.Parts[indices[^1]], symbol, barline));
                }
                Walk(g.GrandStaff.Members);
            }
        }
        Walk(_playedSpec.Items);
    }

    /// <summary>The exported parts a group's staves show, a merged grand staff's lower part
    /// answering as the one part it became.</summary>
    private IEnumerable<MusicXmlPart> PartsOf(IEnumerable<Svg.Collector.RenderItemSpec> members)
    {
        foreach (var name in StaffNames(members))
            if (_partsByName.TryGetValue(name, out var part))
                yield return Document.Parts.Contains(part) ? part
                    : _mergedInto.GetValueOrDefault(part) ?? part;
    }

    private static IEnumerable<string> StaffNames(IEnumerable<Svg.Collector.RenderItemSpec> members)
    {
        foreach (var member in members)
            switch (member)
            {
                case Svg.Collector.SingleStaffSpec s: yield return s.Staff.VoiceName; break;
                case Svg.Collector.TabStaffSpec t: yield return t.Staff.VoiceName; break;
                case Svg.Collector.CondensedStaffSpec c: foreach (var n in c.PartNames) yield return n; break;
                case Svg.Collector.CombinedStaffSpec c: foreach (var n in c.PartNames) yield return n; break;
                case Svg.Collector.GrandStaffRenderSpec g: foreach (var n in StaffNames(g.GrandStaff.Members)) yield return n; break;
            }
    }

    /// <summary>A merged grand staff's lower part → the upper part it was merged into.</summary>
    private readonly Dictionary<MusicXmlPart, MusicXmlPart> _mergedInto = new();

    /// <summary>The played score's staff items, grand-staff members included, in score order.</summary>
    private static IEnumerable<Svg.Collector.RenderItemSpec> FlatItems(IEnumerable<Svg.Collector.RenderItemSpec> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item is Svg.Collector.GrandStaffRenderSpec g)
                foreach (var member in FlatItems(g.GrandStaff.Members))
                    yield return member;
        }
    }

    /// <summary>
    /// Each part's <c>&lt;part-name&gt;</c> is the label the page gives its staff — the score's
    /// <c>staff rh "Right"</c>, else the part's <c>part vo "Vocal"</c>, else its instrument's
    /// (<c>StaffSpec.InstrumentName</c>, RenderSpecParser's one reading) — and the id only when
    /// the page gives none. Until 2026-09-30 it was always the id (LilySharp-Omr
    /// docs/repro/musicxml-exporter-bugs.md #5).
    /// </summary>
    private void WritePartNames()
    {
        if (_playedSpec == null)
            return;
        foreach (var item in FlatItems(_playedSpec.Items))
        {
            var staff = item switch
            {
                Svg.Collector.SingleStaffSpec s => s.Staff,
                Svg.Collector.TabStaffSpec t => t.Staff,
                _ => null,
            };
            if (staff is { InstrumentName: { } label } && _partsByName.TryGetValue(staff.VoiceName, out var part))
                part.DisplayName ??= label;
        }
    }

    /// <summary>
    /// A section's label as the page draws it at the section's first bar — a boxed (or, under
    /// <c>layout { sectionLabels plain }</c>, a bare) label, and none under <c>none</c> — as a
    /// <c>&lt;rehearsal&gt;</c> direction on the first part's bar. The labels are the page's
    /// own (<c>Measure.SectionLabel</c>: the section-label rule, repeats and all). Until
    /// 2026-09-30 the file had none (LilySharp-Omr docs/repro/musicxml-exporter-bugs.md #4).
    /// </summary>
    private void WriteSectionLabels(SyntaxTree tree)
    {
        if (_playedSpec is not { } spec || Document.Parts.Count == 0)
            return;
        var page = PageModel(tree, spec);
        var style = page.LayoutPlan.SectionLabels;
        if (style == Semantics.SectionLabelStyle.None)
            return;
        var staff = page.StaffGroups.SelectMany(g => g.Staves).FirstOrDefault(s => !s.IsTextRow);
        if (staff == null)
            return;
        var measures = Document.Parts[0].Measures;
        var pageMeasures = staff.PrimaryVoice.Measures;
        for (int i = 0; i < pageMeasures.Length && i < measures.Count; i++)
            if (pageMeasures[i].SectionLabel is { Length: > 0 } label)
                measures[i].Directions.Insert(0, new MusicXmlDirection
                {
                    Rehearsal = label,
                    RehearsalEnclosure = style == Semantics.SectionLabelStyle.Plain ? "none" : "square",
                });
    }

    /// <summary>
    /// A <c>grandStaff { staff rh  staff lh }</c> is ONE part on two staves, as a piano part is
    /// written: the upper part's notes on staff 1, then a <c>&lt;backup&gt;</c> and the lower
    /// part's on staff 2 (voices 5–8, slurs numbered 2), with one <c>&lt;staves&gt;2</c> and a
    /// numbered clef for each. Until 2026-09-30 it was two unrelated parts (LilySharp-Omr
    /// docs/repro/musicxml-exporter-bugs.md #6); the importer splits it back
    /// (MusicXmlReader.SplitByStaff).
    /// </summary>
    /// <remarks>
    /// Only the plain piano shape is merged: a brace (<c>grandStaff</c>, not a bracket group)
    /// holding exactly two notation staves of two different parts with the same bar count and
    /// no tab. Anything else stays as separate parts, as before — and a pair whose staves are
    /// labelled apart stays two parts joined by a brace <c>&lt;part-group&gt;</c>, so both
    /// labels survive.
    /// </remarks>
    private void MergeGrandStaves()
    {
        _mergedInto.Clear();
        if (_playedSpec == null)
            return;
        foreach (var group in FlatItems(_playedSpec.Items).OfType<Svg.Collector.GrandStaffRenderSpec>())
        {
            var g = group.GrandStaff;
            if (g.Type != Svg.Model.StaffGroupType.GrandStaff || g.Members.Length != 2
                || g.Members[0] is not Svg.Collector.SingleStaffSpec upperSpec
                || g.Members[1] is not Svg.Collector.SingleStaffSpec lowerSpec
                || upperSpec.Staff.VoiceName == lowerSpec.Staff.VoiceName
                || !_partsByName.TryGetValue(upperSpec.Staff.VoiceName, out var upper)
                || !_partsByName.TryGetValue(lowerSpec.Staff.VoiceName, out var lower)
                || !Document.Parts.Contains(upper) || !Document.Parts.Contains(lower)
                || upper.Tab != null || lower.Tab != null
                || upper.Measures.Count != lower.Measures.Count || upper.Measures.Count == 0)
                continue;

            // Two staves labelled apart (`staff rh "Right"  staff lh "Left"`) keep their two
            // labels, as the page prints them: one merged part has one name, so they stay two
            // parts under a brace — MusicXML's other spelling of a grand staff. A member of a
            // grand staff gets no ensemble default (RenderSpecParser.ApplyEnsembleDefault), so
            // two labels here are the writer's.
            if (upper.DisplayName is { } upperLabel && lower.DisplayName is { } lowerLabel
                && upperLabel != lowerLabel)
                continue;   // WritePartGroups writes their brace

            for (int i = 0; i < upper.Measures.Count; i++)
            {
                var u = upper.Measures[i];
                var l = lower.Measures[i];
                foreach (var n in u.Notes)
                    if (n.RawElement == null && !n.IsBackup)
                        n.Staff = 1;

                if (u.Attributes != null)
                    u.Attributes.ClefNumber = 1;
                if (i == 0 || l.Attributes?.ClefSign != null)
                {
                    u.Attributes ??= new MusicXmlAttributes { Divisions = null };
                    u.Attributes.ClefNumber = 1;
                    if (i == 0)
                        u.Attributes.Staves = 2;
                    if (l.Attributes?.ClefSign is { } sign)
                        u.Attributes.Staff2Clef = (sign, l.Attributes.ClefLine, l.Attributes.ClefOctaveChange);
                }

                foreach (var d in l.Directions)
                {
                    d.Staff = 2;
                    d.Number = 2;
                    u.Directions.Add(d);
                }

                if (l.Notes.Count == 0)
                    continue;
                int at = 0;
                foreach (var n in u.Notes)
                    if (n.RawElement == null && !n.IsChord && !n.IsGrace)
                        at += n.IsBackup ? -n.Duration : n.Duration;
                if (at > 0)
                    u.Notes.Add(new MusicXmlNote { IsBackup = true, Duration = at });
                foreach (var n in l.Notes)
                {
                    if (n.RawElement == null && !n.IsBackup)
                    {
                        n.Staff = 2;
                        n.Voice = (n.Voice ?? 1) + 4;
                        n.SlurNumber = 2;
                    }
                    u.Notes.Add(n);
                }
            }
            // The one part answers to the brace's label, which may be written on either staff.
            upper.DisplayName ??= lower.DisplayName;
            Document.Parts.Remove(lower);
            _mergedInto[lower] = upper;
        }
    }

    /// <summary>
    /// The score's tab staves: each part the score shows as <c>tab</c> gets its TAB staff
    /// (<see cref="MusicXmlTab"/>) and every note the string and fret the PAGE's tab staff
    /// prints for it — the page's own string choice (<c>TabResolver.ResolveTabStrings</c>:
    /// written <c>\N</c>, the bar-long reuse, the fingering planner), read off the page's model
    /// and matched to the exported notes as <see cref="WriteBeams"/> matches its beams.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-30 a <c>tab gt</c> was not in the file at all — no TAB staff, no string,
    /// no fret (LilySharp-Omr docs/repro/musicxml-exporter-bugs.md #1).
    /// <para>
    /// A chord's members are paired with the exported chord's notes by pitch. A note below the
    /// fretboard, which the page hides on its tab staff, carries no fret.
    /// </para>
    /// </remarks>
    private void WriteStaffLines()
    {
        // A staff the score draws with other than five lines (`as lines N`) says so, or a
        // reader opens a one-line percussion staff with five (LilySharp-Omr feedback #18).
        // A part merged with its grand-staff partner has two staves and is left alone.
        void Walk(Svg.Collector.RenderItemSpec item)
        {
            switch (item)
            {
                case Svg.Collector.SingleStaffSpec s when s.Staff.Lines != 5:
                    if (_partsByName.TryGetValue(s.Staff.VoiceName, out var part)
                        && Document.Parts.Contains(part) && !_mergedInto.ContainsValue(part))
                        part.StaffLines = s.Staff.Lines;
                    break;
                case Svg.Collector.GrandStaffRenderSpec g:
                    foreach (var m in g.GrandStaff.Members) Walk(m);
                    break;
            }
        }
        if (_playedSpec is { } spec)
            foreach (var item in spec.Items)
                Walk(item);
    }

    private void WriteTab(SyntaxTree tree)
    {
        var spec = _playedSpec;
        if (spec == null)
            return;
        var tabSpecs = new List<Svg.Collector.TabStaffSpec>();
        var notationParts = new HashSet<string>(StringComparer.Ordinal);
        void Walk(Svg.Collector.RenderItemSpec item)
        {
            switch (item)
            {
                case Svg.Collector.TabStaffSpec t: tabSpecs.Add(t); break;
                case Svg.Collector.SingleStaffSpec s: notationParts.Add(s.Staff.VoiceName); break;
                case Svg.Collector.GrandStaffRenderSpec g:
                    foreach (var m in g.GrandStaff.Members) Walk(m);
                    break;
            }
        }
        foreach (var item in spec.Items)
            Walk(item);
        if (tabSpecs.Count == 0)
            return;

        var page = PageModel(tree, spec);
        var tabStaves = page.StaffGroups.SelectMany(g => g.Staves).Where(s => s.IsTab && s.Tuning.HasValue).ToList();
        var byPart = Document.Parts.ToDictionary(p => p, PrintingsBySource);

        for (int t = 0; t < tabStaves.Count && t < tabSpecs.Count; t++)
        {
            var staff = tabStaves[t];
            int[] tuning = Tablature.Tunings.GetTuning(staff.Tuning!.Value);
            int shift = Tablature.Tunings.SoundingShift(staff.TabSourceClef, staff.Transposition);
            var items = staff.Voices
                .SelectMany(v => v.Measures.SelectMany(m => m.Items))
                .Where(i => i is Svg.Model.NoteItem or Svg.Model.ChordItem && i.SourcePosition >= 0)
                .ToList();

            // The part whose notes these are: the one that wrote the most of them.
            var part = Document.Parts
                .Select(p => (Part: p, Hits: items.Count(i => byPart[p].ContainsKey(i.SourcePosition))))
                .Where(x => x.Hits > 0)
                .OrderByDescending(x => x.Hits)
                .Select(x => x.Part)
                .FirstOrDefault();
            if (part == null || part.Tab != null)
                continue;
            part.Tab = new MusicXmlTab(tuning.ToArray(), notationParts.Contains(tabSpecs[t].Staff.VoiceName));

            var bySource = byPart[part];
            var seen = new Dictionary<int, int>();
            var lastDrawn = new Dictionary<int, List<MusicXmlNote>>();
            foreach (var item in items)
            {
                seen.TryGetValue(item.SourcePosition, out int k);
                seen[item.SourcePosition] = k + 1;
                if (!bySource.TryGetValue(item.SourcePosition, out var printings) || k >= printings.Count)
                    continue;
                var notes = printings[k];
                lastDrawn[item.SourcePosition] = notes;
                switch (item)
                {
                    case Svg.Model.NoteItem note when !note.TabBelowRange:
                        notes[0].Tab = Fretted(note.Midi, note.StringNumber);
                        break;
                    case Svg.Model.ChordItem chordItem:
                        var free = chordItem.Notes.ToList();
                        foreach (var xmlNote in notes)
                        {
                            int midi = WrittenMidi(xmlNote);
                            int at = free.FindIndex(cn => cn.Midi == midi);
                            if (at < 0)
                                continue;
                            xmlNote.Tab = Fretted(free[at].Midi, free[at].StringNumber);
                            free.RemoveAt(at);
                        }
                        break;
                }
            }

            // A printing the page does not draw — a percent repeat's repetitions, which the
            // file writes out and the page shows as the sign — plays the same notes on the
            // same strings as the last one it does.
            foreach (var (position, drawn) in lastDrawn)
                foreach (var printing in bySource[position].Skip(seen[position]))
                    for (int i = 0; i < printing.Count && i < drawn.Count; i++)
                        printing[i].Tab ??= drawn[i].Tab;

            // A grace note is no page item: the page frets it from its own pitch and written
            // \N alone (SharedRenderer.GraceNotes' tab digits), and so does the file.
            foreach (var measure in part.Measures)
                foreach (var note in measure.Notes)
                    if (note.IsGrace && !note.IsRest && !note.IsUnpitched && note.Step != null && note.Tab == null)
                    {
                        int? written = note.Technicals.FirstOrDefault(t => t.Name.LocalName == "string") is { } s
                                       && int.TryParse(s.Value, out int n) ? n : null;
                        note.Tab = Fretted(WrittenMidi(note), written);
                    }

            (int, int)? Fretted(int writtenMidi, int? stringNumber)
            {
                var (s, fret) = Tablature.Tunings.CalculateFret(writtenMidi + shift, tuning, stringNumber ?? 0);
                return fret < 0 ? null : (s, fret);
            }
        }
    }

    /// <summary>The page's model of the exported score — the one the SVG renders, with the
    /// score's own fonts / paper / layout references (SvgGenerator.CollectFor) — collected once
    /// for the passes that read the page (<see cref="WriteTab"/>, <see cref="WriteTrackLyrics"/>).</summary>
    private Svg.Model.MultiStaffScore PageModel(SyntaxTree tree, Svg.Collector.RenderSpec spec)
        => _pageModel ??= new Svg.Collector.MeasureCollector
        {
            FontsOverride = spec.FontsRef,
            PaperOverride = spec.PaperRef,
            LayoutOverride = spec.LayoutRef,
        }.CollectMultiStaff(tree, spec);

    private Svg.Model.MultiStaffScore? _pageModel;

    /// <summary>A part's exported pitched notes by where they were written, k-th printing by
    /// k-th printing — the side of the page match (<see cref="MusicXmlNote.SourcePosition"/>) a
    /// page item's own printing count indexes. A chord is its first note and the members that
    /// follow it.</summary>
    private static Dictionary<int, List<List<MusicXmlNote>>> PrintingsBySource(MusicXmlPart part)
    {
        var bySource = new Dictionary<int, List<List<MusicXmlNote>>>();
        List<MusicXmlNote>? chord = null;
        foreach (var measure in part.Measures)
            foreach (var note in measure.Notes)
            {
                if (note.IsChord && chord != null)
                {
                    chord.Add(note);
                    continue;
                }
                chord = null;
                if (note.SourcePosition < 0 || note.IsRest || note.IsGrace || note.IsBackup
                    || note.RawElement != null || note.IsUnpitched)
                    continue;
                if (!bySource.TryGetValue(note.SourcePosition, out var printings))
                    bySource[note.SourcePosition] = printings = new List<List<MusicXmlNote>>();
                printings.Add(chord = new List<MusicXmlNote> { note });
            }
        return bySource;
    }

    /// <summary>
    /// The syllables of a TOP-LEVEL lyrics track (<c>lyrics words sings vo { … }</c> placed by
    /// the score's <c>lyrics words</c>) as <c>&lt;lyric&gt;</c>: the page binds each syllable
    /// to a note (LyricCollector — the row folds under the staff it sings), and the syllable
    /// is written on the exported note that stands for that page note.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-30 only lyrics written INSIDE a section or part block reached the file
    /// (<see cref="AttachLyrics"/>, which still writes those); a top-level track was dropped
    /// (LilySharp-Omr docs/repro/musicxml-exporter-bugs.md #2). A track the score does not
    /// place is not on the page, and is not in the file either. An unbound row (no melody —
    /// the even-spread lead-sheet row) names no note and is left out.
    /// </remarks>
    private void WriteTrackLyrics(SyntaxTree tree)
    {
        var spec = _playedSpec;
        if (spec == null)
            return;
        var tracks = tree.GetRoot().ChildNodes().OfType<LyricsBlockSyntax>().Select(b => b.Span).ToList();
        if (tracks.Count == 0)
            return;
        var page = PageModel(tree, spec);
        var staves = page.StaffGroups.SelectMany(g => g.Staves).ToList();
        var byPart = Document.Parts.ToDictionary(p => p, PrintingsBySource);

        // Each staff's items' printings, counted as the exported notes' are.
        var printing = new Dictionary<Svg.Model.MusicItem, int>(ReferenceEqualityComparer.Instance);
        foreach (var staff in staves)
        {
            var seen = new Dictionary<int, int>();
            foreach (var voice in staff.Voices)
                foreach (var measure in voice.Measures)
                    foreach (var item in measure.Items)
                        if (item is Svg.Model.NoteItem or Svg.Model.ChordItem && item.SourcePosition >= 0)
                        {
                            seen.TryGetValue(item.SourcePosition, out int k);
                            printing[item] = k;
                            seen[item.SourcePosition] = k + 1;
                        }
        }

        var hyphenBefore = new Dictionary<(int Staff, int Voice, int Verse), bool>();
        foreach (var lyric in page.Lyrics
                     .Where(l => !l.IsLyricsRow && tracks.Any(t => l.SourcePosition >= t.Start && l.SourcePosition < t.End))
                     .OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing))
        {
            if (lyric.StaffIndex >= staves.Count || SungItem(staves[lyric.StaffIndex], lyric) is not { } item
                || !printing.TryGetValue(item, out int k))
                continue;
            var note = byPart.Values
                .Select(bySource => bySource.TryGetValue(item.SourcePosition, out var p) && k < p.Count ? p[k][0] : null)
                .FirstOrDefault(n => n != null);
            if (note == null || note.Lyrics.Any(l => l.Verse == lyric.VerseNumber))
                continue;
            var key = (lyric.StaffIndex, lyric.VoiceId, lyric.VerseNumber);
            bool prevHyphen = hyphenBefore.GetValueOrDefault(key);
            bool hyphen = lyric.ConnectorType == Svg.Model.LyricConnectorType.Hyphen;
            note.Lyrics.Add((lyric.VerseNumber, lyric.Text,
                prevHyphen ? (hyphen ? "middle" : "end") : (hyphen ? "begin" : "single"),
                lyric.ConnectorType == Svg.Model.LyricConnectorType.Extender));
            hyphenBefore[key] = hyphen;
        }

        // The note a syllable is under: the primary voice's item it indexes, or — a bound
        // voice's syllable — the note of another voice starting at its moment.
        static Svg.Model.MusicItem? SungItem(Svg.Model.Staff staff, Svg.Model.LyricItem lyric)
        {
            if (lyric.VoiceId == 0)
            {
                var items = staff.PrimaryVoice.Measures is var ms && lyric.MeasureIndex < ms.Length
                    ? ms[lyric.MeasureIndex].Items
                    : default;
                return !items.IsDefault && lyric.ItemIndex >= 0 && lyric.ItemIndex < items.Length
                    ? items[lyric.ItemIndex]
                    : null;
            }
            foreach (var voice in staff.Voices.Skip(1))
            {
                if (lyric.MeasureIndex >= voice.Measures.Length)
                    continue;
                var at = Fraction.Zero;
                foreach (var item in voice.Measures[lyric.MeasureIndex].Items)
                {
                    if (at == lyric.Timing && item is Svg.Model.NoteItem or Svg.Model.ChordItem)
                        return item;
                    at += item.Duration;
                }
            }
            return null;
        }
    }

    /// <summary>An exported note's written pitch as a MIDI number.</summary>
    private static int WrittenMidi(MusicXmlNote note)
    {
        int pc = note.Step switch
        {
            "C" => 0, "D" => 2, "E" => 4, "F" => 5, "G" => 7, "A" => 9, "B" => 11, _ => 0,
        };
        return ((note.Octave ?? 4) + 1) * 12 + pc + (int)Math.Round(note.Alter ?? 0);
    }

    /// <summary>
    /// The page's beams as <c>&lt;beam&gt;</c> elements: each part is collected the way the page
    /// collects it and its beam groups detected by the page's own <see cref="Svg.Collector.BeamDetector"/>
    /// (automatic beaming by the meter, written <c>[ ]</c>, the tuplet and voice bounds), and every
    /// member's levels are written on the exported note that stands for it.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-30 the file carried no <c>&lt;beam&gt;</c> at all, so a reader beamed by its own
    /// rules: MuseScore joined nine eighths of a 7/4 bar under one beam where the page beams by the
    /// beat (LilySharp-Omr docs/repro/musicxml-exporter-bugs.md #11, beams.lys).
    /// <para>
    /// THE MATCH: an exported note and a page item are the same written note when they share the
    /// syntax node's <c>SourceStart</c> (<see cref="MusicXmlNote.SourcePosition"/>) and are its
    /// k-th printing in the part, counted in measure order on both sides — a section printed twice
    /// is two sets of notes in both. A chord's <c>&lt;beam&gt;</c> rides its first note.
    /// </para>
    /// <para>
    /// THE LEVELS: a member with <c>BeamCount</c> beams, <c>BeamCountLeft</c> reaching its left
    /// neighbour and <c>BeamCountRight</c> its right, writes one element per level: a level both
    /// neighbours share on a side is joined there (<c>begin</c> / <c>continue</c> / <c>end</c>), a
    /// level joined on neither side is a beamlet (<c>forward hook</c> when it points right,
    /// <c>backward hook</c> otherwise) — the counts the renderer's segments are built from
    /// (BeamSubdivision.CalcBeamSegments).
    /// </para>
    /// </remarks>
    private void WriteBeams(SyntaxTree tree, bool hasSections)
    {
        var detector = new Svg.Collector.BeamDetector();
        foreach (var part in Document.Parts)
        {
            // Where each written note falls in the part, k-th printing by k-th printing.
            var xmlBySource = new Dictionary<int, List<MusicXmlNote>>();
            foreach (var measure in part.Measures)
                foreach (var note in measure.Notes)
                    if (note.SourcePosition >= 0 && !note.IsChord && !note.IsRest && !note.IsGrace)
                    {
                        if (!xmlBySource.TryGetValue(note.SourcePosition, out var list))
                            xmlBySource[note.SourcePosition] = list = new List<MusicXmlNote>();
                        list.Add(note);
                    }
            if (xmlBySource.Count == 0)
                continue;

            var score = new Svg.Collector.MeasureCollector().Collect(tree, hasSections ? part.Name : null);
            // The same count on the page: each item's printing, per voice, in measure order.
            var printing = new Dictionary<Svg.Model.MusicItem, int>(ReferenceEqualityComparer.Instance);
            foreach (var voice in score.Voices)
            {
                var seen = new Dictionary<int, int>();
                foreach (var measure in voice.Measures)
                    foreach (var item in measure.Items)
                        if (item is Svg.Model.NoteItem or Svg.Model.ChordItem && item.SourcePosition >= 0)
                        {
                            seen.TryGetValue(item.SourcePosition, out int k);
                            printing[item] = k;
                            seen[item.SourcePosition] = k + 1;
                        }
            }

            foreach (var group in detector.DetectBeamGroups(score))
            {
                var members = group.Members;
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i];
                    var item = m.DetectedItem;
                    if (!printing.TryGetValue(item, out int k)
                        || !xmlBySource.TryGetValue(item.SourcePosition, out var notes) || k >= notes.Count)
                        continue;
                    var xmlNote = notes[k];
                    xmlNote.Beams.Clear();
                    for (int level = 1; level <= m.BeamCount; level++)
                    {
                        bool left = i > 0 && level <= m.BeamCountLeft && level <= members[i - 1].BeamCountRight;
                        bool right = i < members.Length - 1 && level <= m.BeamCountRight
                                     && level <= members[i + 1].BeamCountLeft;
                        string value = (left, right) switch
                        {
                            (true, true) => "continue",
                            (false, true) => "begin",
                            (true, false) => "end",
                            _ => level <= m.BeamCountRight && i < members.Length - 1 ? "forward hook" : "backward hook",
                        };
                        xmlNote.Beams.Add((level, value));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The score's capo (<c>chordDiagrams … capo N</c>) as <c>&lt;staff-details&gt;&lt;capo&gt;</c>
    /// in the opening attributes of every part whose harmonies carry a <c>&lt;frame&gt;</c>:
    /// the frames are the shapes PRESSED above the capo (HANDOFF §2 K2), and without this a
    /// reader fingers them at the nut. A part with no frame — a bass under the row, a part
    /// naming chords alone — says nothing, its <c>&lt;harmony&gt;</c> being the sounding chord.
    /// </summary>
    /// <remarks>Left open by 第665 (HANDOFF §1.0 K5); written last, once every part has its
    /// harmonies, the rows' included (<see cref="EmitPendingChordRows"/>).</remarks>
    private void WriteCapo()
    {
        int capo = DiagramCapo;
        if (capo <= 0)
            return;
        foreach (var part in Document.Parts)
        {
            if (part.Measures.Count == 0
                || !part.Measures.Any(m => m.Notes.Any(n => n.RawElement is { } raw && raw.Descendants("frame").Any())))
                continue;
            var first = part.Measures[0];
            first.Attributes ??= new MusicXmlAttributes { Divisions = null };
            first.Attributes.Capo = capo;
        }
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
            {
                BeginPrintedPlay();
                EmitSection(section);
            }
            FinishCarriedTies();
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
        // The marks after the last play (`… B dc al fine`) are that play's MarksAfter.
        if (_xmlPendingMarks.Count > 0 && _printedPlays.Count > 0)
            _printedPlays[^1] = _printedPlays[^1] with { MarksAfter = Svg.Collector.NavMarkStamp.Encode(_xmlPendingMarks) };
        _xmlPendingMarks.Clear();
        FinishCarriedTies();
    }

    // ---- ties carried over a section boundary (the section carry rule) ----------------
    //
    // A tie open at the end of a part's block is carried to the first note of EVERY play that
    // follows that play in the PLAYED order (Svg.Collector.PlayedOrder — the order MIDI plays):
    // the printed next one, back to a `|:`, into a later ending. The music is printed ONCE, so
    // each such note gets the tie's stop and the tied note keeps its start; a start nothing
    // stops is retracted (a start with no stop is a broken pair — CloseTies' remark).
    // LILYSHARP-OWN: a stop on a note the tied note does not PRINT before (the first note of
    // a repeat's body, of a later ending) is how this exporter writes LilyPond's \repeatTie —
    // MusicXML has no repeat-tie element; `<tie type="stop"/>` + `<tied type="stop"/>` with no
    // start before it on the page is the spelling that plays right when the repeat is honoured.

    // The printed plays, in emission order, with what each is to a form repeat.
    private readonly List<Svg.Collector.PrintedPlay> _printedPlays = new();
    // What the next printed play is to a form repeat (set by the form walk).
    private Svg.Model.SectionRepeatRole _xmlRole;
    private bool _xmlRunStart;
    private int _xmlRunCount;
    private bool _xmlRewind;
    // The passes the next printed play's ending bracket names ([1-2. B] is {1, 2}).
    private PassSet _xmlEndingPasses;
    // Per (part, printed play): the tie starts open at the block's end, and the block's first onset.
    private readonly Dictionary<(string Part, int Play), List<MusicXmlNote>> _openAtEnd = new();
    private readonly Dictionary<(string Part, int Play), List<MusicXmlNote>> _firstOnset = new();
    private string? _currentPartName;

    /// <summary>The printed play now being emitted (1-based; 0 before the first).</summary>
    private int _xmlPlaySerial => _printedPlays.Count;

    /// <summary>The slur and phrasing-slur marks the section carry rule refuses, by (the host
    /// note's source position, 0-based play, whether it is the close) — the page draws no
    /// slur for them (LYS4023), and neither does this document.</summary>
    private readonly HashSet<(int Position, int Play, bool Close)> _refusedSlurs = new();

    /// <summary>The hairpins the carry rule cuts at their own section's end, by (the mark's
    /// source position, 0-based play): the wedge stops where the section's play ends
    /// (<see cref="CloseCutWedge"/>), not at the next dynamic.</summary>
    private readonly HashSet<(int Position, int Play)> _cutHairpins = new();

    /// <summary>True while the open wedge is one the rule cuts at the block's end.</summary>
    private bool _wedgeCut;

    /// <summary>The source position of the note, chord or slash the last emitted onset came
    /// from (-1 when unknown) — what a slur walked as a SIBLING after it is refused by.</summary>
    private int _lastEmittedHost = -1;

    /// <summary>
    /// Reads the carry rule's refusals off the page's own collect of the exported score, so
    /// this document draws the spans the page draws and no other. ⚠️ Until 2026-09-29
    /// (HANDOFF §1.1 第663 ⒂) the exporter wrote every slur and hairpin as written — a slur the
    /// page refuses (carried into the next section and not closed there, or over a repeat) was
    /// a <c>&lt;slur type="start"&gt;</c> with its stop a section or a repeat away, and a
    /// hairpin the page cuts at its section's end ran on to the next dynamic.
    /// </summary>
    /// <remarks>
    /// The rule is <see cref="Svg.Collector.SectionPlayCursor"/>'s and the findings are the
    /// collect's (<see cref="MeasureCollector.SectionCarryWarnings"/>), for the reason
    /// <c>SectionCarryValidator</c> gives: one rule, read from one place. Keyed by PLAY as well
    /// as position: <c>form { C D C E }</c> refuses the second C's slur and draws the first's.
    /// A book with no section has no boundary and pays for no collect.
    /// </remarks>
    private void ReadCarryRefusals(SyntaxTree tree)
    {
        _refusedSlurs.Clear();
        _cutHairpins.Clear();
        _wedgeCut = false;
        if (!tree.GetRoot().DescendantNodes().OfType<SectionDeclarationSyntax>().Any())
            return;
        var collector = Semantics.SemanticValidation.TryCollect(tree, _playedSpec);
        if (collector == null)
            return;
        foreach (var w in collector.SectionCarryWarnings)
        {
            switch (w.Kind)
            {
                case Svg.Collector.SectionSpanKind.Slur:
                case Svg.Collector.SectionSpanKind.PhrasingSlur:
                    _refusedSlurs.Add((w.SourcePosition, w.Play, w.AtClose));
                    break;
                case Svg.Collector.SectionSpanKind.Hairpin:
                    _cutHairpins.Add((w.SourcePosition, w.Play));
                    break;
            }
        }
    }

    /// <summary>Whether the slur mark on the onset from <paramref name="host"/> — its open, or
    /// its close — is one the page refuses in the play being written.</summary>
    private bool RefusedSlur(int host, bool close)
        => host >= 0 && _refusedSlurs.Contains((host, _xmlPlaySerial - 1, close));

    /// <summary>Stops the open wedge the rule cuts, at the end of the last written bar of the
    /// block — where the page cuts the hairpin (HairpinEngraver.DetectHairpins: at the start
    /// of the play that follows).</summary>
    private void CloseCutWedge()
    {
        if (!_wedgeOpen || !_wedgeCut)
            return;
        _wedgeOpen = false;
        _wedgeCut = false;
        var measure = _currentMeasure is { Notes.Count: > 0 } m ? m
            : _currentPart is { Measures.Count: > 0 } p ? p.Measures[^1]
            : null;
        if (measure == null)
            return;
        measure.Directions.Add(new MusicXmlDirection
        {
            WedgeType = "stop",
            Placement = "below",
            Offset = CurrentMeasurePosition(measure),
        });
    }

    /// <summary>Opens the next printed play with the repeat role the form walk set.</summary>
    private void BeginPrintedPlay()
    {
        _printedPlays.Add(new Svg.Collector.PrintedPlay(_xmlRole, _xmlRunStart,
            _xmlRunStart ? _xmlRunCount : 0, _xmlRewind, _xmlEndingPasses,
            Svg.Collector.NavMarkStamp.Encode(_xmlPendingMarks)));
        _xmlPendingMarks.Clear();
        _xmlRunStart = false;
        _xmlRewind = false;
        _xmlEndingPasses = PassSet.None;
    }

    // The form-level navigation marks met since the last printed play — the next play's
    // MarksBefore, or the last play's MarksAfter when the form ends on them (WalkForm): what
    // lets the played order follow the jump texts (Svg.Collector.PlayedOrder).
    private readonly List<NavigationMarkType> _xmlPendingMarks = new();

    /// <summary>Stops every carried tie on the plays that follow its play, retracts the
    /// starts nothing stops, and marks the start no arc leaves as the hanging tie the page
    /// draws there.</summary>
    /// <remarks>
    /// The page's rule (SectionTieCarry): the arc goes to the play printed next when that
    /// play is also played next; every other play that follows gets a repeat tie on its first
    /// note — here a <c>&lt;tie type="stop"/&gt;</c> with no start before it, the spelling
    /// <c>@repeatTie</c> gets — and when NO arc leaves the tied note (back to a <c>|:</c> at
    /// the body's end, into a later ending only) the note carries a hanging tie, the
    /// laissez-vibrer glyph. That last one is <c>&lt;tied type="let-ring"/&gt;</c>, the
    /// spelling <c>@laissezVibrer</c> gets (ApplyHalfTies), beside the sounding
    /// <c>&lt;tie type="start"/&gt;</c> the stop on the repeat's first note pairs with.
    /// ⚠️ Until 2026-09-29 (HANDOFF §1.1 第663 ⑽) the hanging tie never reached the document:
    /// the start stood alone, and a reader drew whatever it draws for a start with its stop
    /// earlier in the part.
    /// </remarks>
    private void FinishCarriedTies()
    {
        var successors = Svg.Collector.PlayedOrder.Successors(_printedPlays);
        foreach (var ((part, play), open) in _openAtEnd)
        {
            var matched = new HashSet<MusicXmlNote>();
            bool inRange = play >= 1 && play <= successors.Length;
            // The play printed next (1-based play + 1 is 0-based play) is played next: the arc.
            bool arc = inRange && successors[play - 1].Contains(play);
            if (inRange)
                foreach (int s in successors[play - 1])
                {
                    if (!_firstOnset.TryGetValue((part, s + 1), out var targets))
                        continue;
                    foreach (var t in targets)
                        if (open.FirstOrDefault(n => SameNotehead(n, t)) is { } from)
                        {
                            t.TieStop = true;
                            matched.Add(from);
                        }
                }
            foreach (var n in open)
            {
                if (!matched.Contains(n))
                    n.TieStart = false;
                else if (!arc)
                    LetRing(n);
            }
        }
        _openAtEnd.Clear();
        _pendingRepeatStop.Clear();
        _firstOnset.Clear();
    }

    /// <summary>The first onset of the notes emitted into <paramref name="part"/> from measure
    /// <paramref name="fromMeasure"/> on: the first sounding note and its chord members, or
    /// nothing when the block opens with a rest.</summary>
    private static List<MusicXmlNote> FirstOnset(MusicXmlPart part, int fromMeasure)
    {
        var onset = new List<MusicXmlNote>();
        for (int m = fromMeasure; m < part.Measures.Count; m++)
            foreach (var n in part.Measures[m].Notes)
            {
                if (n.RawElement != null || n.IsBackup || n.IsGrace)
                    continue;
                if (onset.Count == 0)
                {
                    if (n.IsRest)
                        return onset;
                    onset.Add(n);
                }
                else if (n.IsChord)
                    onset.Add(n);
                else
                    return onset;
            }
        return onset;
    }

    // The section's OWN key, if it states one beside its part blocks
    // (`section A { key d major  melody { … } }`). ⚠️ It sits outside every part block, so
    // the per-part music walk never reaches it — the collector says the same thing in the
    // same words (MeasureCollector.Form.cs, "NOT reached by the per-part music walk") and
    // applies it from there. Until 2026-08-17 this exporter did not, so a section key
    // changed neither the signature nor the phrase auto-transpose baseline: measured on
    // `test/keysig-treble`, whose three sections all came out in one key.
    private KeySignatureSyntax? _sectionKey;

    /// <summary>The section's HEADER meter — the twin of <see cref="_sectionKey"/>, and it
    /// answers the same boundary question: a section that states its own <c>time</c> opens
    /// at THAT meter, and one that does not reverts to the score's (see
    /// <see cref="Semantics.ScoreHomeMeter"/>).</summary>
    private TimeSignatureSyntax? _sectionTime;

    /// <summary>The section's HEADER pickup — the third of the family (<see cref="_sectionKey"/>,
    /// <see cref="_sectionTime"/>): <see cref="EmitPartMusic"/> arms it on the first bar of
    /// EVERY part's play of the section, the way the page shortens every part's first measure
    /// with it (MeasureCollector.Form.cs, <c>_sectionHeaderPartials</c>).</summary>
    private PartialDeclarationSyntax? _sectionPartial;

    /// <summary>The section's HEADER tempo — the fourth of the family: the piece's opening
    /// tempo when the part's first bar is this section's, a metronome direction at the
    /// section's start otherwise (the page's ProcessSectionPrologue draws the same line:
    /// "at the very first timestep the section tempo IS the piece's opening tempo").</summary>
    private TempoDeclarationSyntax? _sectionTempo;

    // The section HEADER registry, keyed by NAME — the reading the page, the MIDI and the
    // LilyPond twin all take (MeasureCollector.Definitions.cs, MidiExporter's
    // _sectionHeaderKeys / _sectionHeaderTimes / _sectionHeaderPartials, the twin's
    // BuildSectionHeaderRegistry): every declaration of a name that holds no inline music
    // contributes its first direct `key` / `time` / `tempo` / `partial`, first declaration
    // wins.
    // ⚠️ Keyed by name and not read off the declaration being played, because in a
    // by-part book the header is a DIFFERENT declaration from the cell: `section A
    // { partial 8 }` beside `part m { section A { … } }`. Until 2026-09-17 this exporter
    // scanned only the declaration in hand, so the standalone header's key, time and
    // pickup never reached the cell (MEASURED on LilySharp-Lab/sessions/p398/probes/
    // xml-partial: `key d major time 3/4` in the header exported fifths 0 in 4/4, and the
    // pickup of `test/chord-flag` exported as a full bar 1), while the header declaration
    // itself was emitted as music — under "Part 1" when no single engraved part owned it,
    // an EMPTY <part/> the schema forbids and the importer cannot read back.
    private Semantics.SectionHeaders _sectionHeaders = Semantics.SectionHeaders.Empty;

    /// <summary>Reads the header registry off every section declaration of the file (see the
    /// field's remarks): <see cref="Semantics.SectionHeaders"/>, the one spelling.</summary>
    private void BuildSectionHeaderRegistry(SyntaxNode root)
        => _sectionHeaders = Semantics.SectionHeaders.Read(root);

    /// <summary>True when the section is a HEADER and nothing else: a TOP-LEVEL declaration
    /// with no part, chord or lyrics block and no inline music — only directives
    /// (<c>section A { partial 8 }</c> in a by-part book). Its directives reach every play
    /// of the name through the registry, so the declaration itself has no music to emit.
    /// ⚠️ Top-level only, the line the LilyPond twin draws ("a header is exactly what it
    /// turns away", its section walk): a directives-only cell INSIDE a part —
    /// <c>part bl { section Body { } }</c> — is that part's play of the section, empty, and
    /// still opens the part (the bass corpus has one such book, an empty chord chart).</summary>
    private static bool IsHeaderOnly(SectionDeclarationSyntax section)
        => section.Parent is CompilationUnitSyntax
           && !Svg.Collector.MeasureCollector.SectionHasInlineMusic(section)
           && !DirectChildren(section).Any(c => c is PartBlockSyntax or ChordPartBlockSyntax or LyricsBlockSyntax);

    /// <summary>The octave shift written on the section REFERENCE currently being played
    /// (<c>~B'</c> = +1), read by <see cref="EmitPartMusic"/> when it arms the frame. It is
    /// a field rather than a parameter because the two things it moves are armed one level
    /// down, once per part block; <see cref="EmitSection"/> sets it for every section it
    /// emits — including the form-less path, which passes 0 — so it can never be left over
    /// from the previous play.</summary>
    private int _sectionOctaveOffset;

    private void EmitSection(SectionDeclarationSyntax section, int octaveOffset = 0)
    {
        _sectionOctaveOffset = octaveOffset;
        // A section is self-contained: its phrase auto-transpose baseline reverts
        // to the score's home key (a mid-section modulation cannot leak out).
        _ambientTonic = _homeTonic;
        // The section's header, by NAME (see the registry's remarks): the same header for the
        // by-section declaration that carries it and for a by-part cell whose header
        // is a standalone declaration.
        // ⚠️ ONLY A SECTION THAT WRAPS ITS MUSIC IN PART BLOCKS HAS A HEADER.
        // A by-part section holds its music INLINE, so a `key` or `time` written in the
        // MIDDLE of that music is also a direct child of the section node — and a scan of
        // the declaration read it as the section's header, applying it at the section's
        // FIRST bar. Measured 2026-08-31 on `section A { c'4 d e f | key g major g a b c | }`:
        // the page turns G major on at bar 2, the export claimed it from bar 1, and the
        // same book written by-section placed it correctly. The registry draws the
        // same line in the same words (MeasureCollector.SectionHasInlineMusic, which the
        // LilyPond exporter's BuildSectionHeaderRegistry already consults: "a declaration
        // with inline music walks its own directives as music and registers NOTHING").
        _sectionKey = _sectionHeaders.Keys.TryGetValue(section.SectionName, out var headerKey) ? headerKey : null;
        _sectionTime = _sectionHeaders.Times.TryGetValue(section.SectionName, out var headerTime) ? headerTime : null;
        _sectionTempo = _sectionHeaders.Tempos.TryGetValue(section.SectionName, out var headerTempo) ? headerTempo : null;
        _sectionPartial = _sectionHeaders.Partials.TryGetValue(section.SectionName, out var headerPartial) ? headerPartial : null;

        // The section's chord rows (owner's decisions 2026-09-29, EmitPendingChordRows): a
        // by-part chord track's section (`chords prog { section A { … } }`) holds a placed
        // row's bars and no part's music — remembered for this play, and nothing else of it
        // is written (until then it was walked as inline music under the bare-section owner
        // and wrote nothing but, on a lead sheet, an empty "Part 1"). A by-section cell
        // (`section A { … chords prog { C | F | } }`) is remembered beside its parts.
        if (section.Parent is ChordPartBlockSyntax byPartTrack)
        {
            if (byPartTrack.PartName is { } trackRow && _placedChordRows.Contains(trackRow))
                RememberChordRow(trackRow, Svg.Collector.ChordNameCollector.SectionItems(section));
            return;
        }
        for (int i = 0; i < section.SlotCount; i++)
            if (section.GetChild(i) is ChordPartBlockSyntax { HasSections: false, PartName: { } cellRow } cell
                && _placedChordRows.Contains(cellRow))
                RememberChordRow(cellRow, cell.Items);

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
        else if (IsHeaderOnly(section)
                 || (DirectChildren(section).Any() && DirectChildren(section).All(c => c is ChordPartBlockSyntax or LyricsBlockSyntax)))
        {
            // A standalone header (`section A { partial 8 }` beside the parts' cells): its
            // directives have already reached the registry, and it holds no music — emitting
            // it opened a part for it ("Part 1" when no single engraved part owned it) and
            // wrote nothing into it, an empty <part/>. A lead sheet's section — chord cells and
            // lyrics, no notes — is the same empty part (2026-09-29): its rows are remembered
            // above and get a part of their own (EmitRowAsItsOwnPart).
        }
        else
        {
            // No nested part blocks: the section holds its music INLINE — a by-part
            // `part m { section A { … } }` cell, or a standalone section (default part).
            // Emit it under the enclosing part's name; its lyrics map onto those notes.
            EmitGroupedByPartSection(section);
        }
    }

    private void EmitSectionByName(Dictionary<string, List<SectionDeclarationSyntax>> byName, string name,
        int octaveOffset = 0)
    {
        BeginPrintedPlay(); // one section play, whichever parts write it
        if (byName.TryGetValue(name, out var list))
        {
            foreach (var section in list)
                EmitSection(section, octaveOffset);
            PadPartsSilentInThisPlay(name, octaveOffset);
        }
    }

    /// <summary>
    /// The engraved parts that wrote NOTHING in the play just emitted get the section's bars
    /// as silence — the canonical count (<see cref="Svg.Collector.SectionBarCounts"/>) of empty
    /// <c>|</c> bars under the section's own header, the way the page pads their staves and
    /// the twin their voices — so every part's measure N stays the same bar, and a placed
    /// chord row over such a part has bars to stand in.
    /// </summary>
    /// <remarks>
    /// MEASURED 2026-09-29 (HANDOFF §1.1 第668's hole, Lab sessions/p684/probes): a bass with
    /// no block for section B exported B's two bars NOWHERE — its C followed its A, three
    /// measures against the melody's five (the twin and the MIDI had this right since
    /// 2026-09-29 / 2026-09-10) — and a row over a melody that wrote nothing in B lost B's
    /// harmonies, EmitPendingChordRows having no measure of that play to put them in.
    /// <para>
    /// Written through <see cref="EmitPartMusic"/> with the bars as its children, so the
    /// silent play is the same play as a written one to everything downstream: the part's
    /// header, the section's meter, key and pickup (the first bar is the pickup's length,
    /// as the twin's <c>s4 | s2. |</c>), the measure numbering, and the
    /// <c>_playStartMeasure</c> record the rows are placed by. A part with no <c>part</c>
    /// declaration (a lead sheet's rows) is not engraved and gets nothing.
    /// </para>
    /// </remarks>
    private void PadPartsSilentInThisPlay(string sectionName, int octaveOffset)
    {
        if (_playedSpec is not { } spec
            || !_sectionBars.Canonical.TryGetValue(sectionName, out int bars) || bars <= 0)
            return;
        foreach (var partName in spec.EngravedPartNames)
        {
            if (partName.Length == 0 || _playStartMeasure.ContainsKey((partName, _xmlPlaySerial)))
                continue;
            _sectionOctaveOffset = octaveOffset;
            // The bare bar line the author's own `| |` would be: ProcessNode's empty-bar rule
            // gives each a bar of silence (AddSilentBar), as PadVoice's padding does.
            var bar = new BarlineSyntax(new Syntax.InternalSyntax.BarlineGreen(
                new Syntax.InternalSyntax.SyntaxToken(SyntaxKind.Bar, "|"), null, null), null, 0);
            // Each bar in the score's meter there: another part's `time` at a bar's start is
            // written before it (SectionMeterPlan, HANDOFF §2 F-partmeter ⒜).
            var items = new List<SyntaxNode>(bars);
            for (int i = 0; i < bars; i++)
            {
                if (_meterPlan.ForeignChangeAt(sectionName, i, null) is { } time)
                    items.Add(time);
                items.Add(bar);
            }
            EmitPartMusic(partName, items);
        }
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
        _xmlPendingMarks.Clear();
        foreach (var item in FormWalk.Read(container))
        {
            switch (item)
            {
                // A plain and a silent (~) reference are the same emission — this
                // exporter writes no section label, so the tilde has nothing to hide.
                case FormWalk.SectionRef s:
                    EmitWithPendingTargets(() => EmitSectionByName(byName, s.Name, s.OctaveOffset));
                    break;
                case FormWalk.Repeat rb:
                    EmitWithPendingTargets(() => EmitRepeatBlock(rb, byName));
                    break;
                case FormWalk.Ending alt:
                    EmitWithPendingTargets(() =>
                    {
                        foreach (var s in alt.Sections)
                            EmitSectionByName(byName, s.Name, s.OctaveOffset);
                    });
                    break;
                case FormWalk.Other { Node: NavigationMarkSyntax nav }:
                    _xmlPendingMarks.Add(nav.MarkType);
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
                    _xmlRewind = true; // the piece so far plays again (PlayedOrder)
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
        // The block's plays are a repeat run: its body plays and endings (PlayedOrder).
        _xmlRunStart = true;
        _xmlRunCount = rb.ExplicitPlayCount ?? 0;
        if (rb.Children.Any(c => c is FormWalk.Ending))
            EmitVoltaRepeatBlock(rb, byName);
        else
            EmitPlainRepeatBlock(rb, byName);
        _xmlRole = Svg.Model.SectionRepeatRole.None;
        _xmlRunStart = false;
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
            _xmlRunStart = true; // a ':|:' divides two repeats
            _xmlRole = Svg.Model.SectionRepeatRole.Body;
            var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
            foreach (var item in run)
                EmitSectionByName(byName, item.Name, item.OctaveOffset);
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
    /// forward repeat; each ending gets a &lt;ending&gt; start/stop bracket — "stop" for a
    /// hooked end (<c>]</c>), "discontinue" for an open one (<c>-]</c>) or one its
    /// <c>voltaBracket N</c> cuts short; the backward repeat sits on the last measure before
    /// the <c>:|</c>.
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
        // The last ending the block closes on returns when it plays a pass before the last
        // (RepeatPasses.EndingReturns): `[1,3. B] :| [2. C]` ends C in a backward repeat no ':|'
        // was written for. Until 2026-09-30 the file had none there.
        int highestPass = rb.Children.OfType<FormWalk.Ending>().Select(e => e.Node.Numbers.DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max();
        int closingEnding = -1;
        for (int k = 0; k < rb.Children.Count; k++)
            if (rb.Children[k] is FormWalk.Ending)
                closingEnding = k;
            else if (rb.Children[k] is FormWalk.RepeatEnd)
                closingEnding = -1;

        for (int k = 0; k < rb.Children.Count; k++)
        {
            var child = rb.Children[k];
            if (child is FormWalk.Ending { Node: var alt } ending)
            {
                _xmlRole = Svg.Model.SectionRepeatRole.Ending;
                _xmlEndingPasses = PassSet.Of(alt.Numbers);
                var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
                // One <ending> across all of the ending's sections: start on the first
                // measure of the first, stop on the last measure of the last.
                foreach (var s in ending.Sections)
                {
                    EmitSectionByName(byName, s.Name, s.OctaveOffset);
                    _xmlRole = Svg.Model.SectionRepeatRole.EndingContinued;
                }
                string num = EndingNumbers(alt);
                // END SHAPE and LENGTH (owner's design 2026-09-28, the page's reading in
                // MeasureCollector.EndingBracket): `]` stops with a hook ("stop"), `-]` without
                // one ("discontinue"); `voltaBracket N` puts the end at the ending's Nth bar,
                // always "discontinue" there — a cut bracket is not the ending's end.
                // ⚠️ `voltaBracket line` has no reading here: a MusicXML file carries no
                // system breaks of the page's, so the ending is written whole.
                var length = alt.LengthUnder(ScoreLayoutPlan().VoltaBracket);
                foreach (var p in Document.Parts)
                {
                    int first = startIdx.GetValueOrDefault(p);
                    if (p.Measures.Count <= first) continue;
                    int last = p.Measures.Count - 1;
                    int stopAt = length.LastBar(first, last);
                    p.Measures[first].EndingStartNumbers = num;
                    p.Measures[stopAt].EndingStopNumbers = num;
                    p.Measures[stopAt].EndingStopType = alt.EndsHooked && stopAt == last ? "stop" : "discontinue";
                    if (forwardPending) p.Measures[first].RepeatForward = true;
                    if (k == closingEnding && RepeatPasses.EndingReturns(alt.Numbers, highestPass))
                        p.Measures[last].RepeatBackward = true;
                }
                forwardPending = false;
            }
            else if (child is FormWalk.SectionRef s)
            {
                _xmlRole = Svg.Model.SectionRepeatRole.Body;
                var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
                EmitSectionByName(byName, s.Name, s.OctaveOffset);
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
                foreach (var p in Document.Parts)
                    if (p.Measures.Count > 0)
                        p.Measures[^1].RepeatBackward = true;
            }
        }
    }

    /// <summary>The MusicXML <c>&lt;ending number&gt;</c> list for a volta: "1", a
    /// range <c>[1-3.]</c> → "1,2,3", a list <c>[1,3,5.]</c> → "1,3,5" — the passes every
    /// reader plays it by (<see cref="FormAlternativeSyntax.Numbers"/>).</summary>
    private static string EndingNumbers(FormAlternativeSyntax alt)
    {
        var passes = alt.Numbers.ToList();
        return passes.Count == 0 ? alt.AlternativeNumber.ToString() : string.Join(",", passes);
    }

    private void ProcessPartBlock(PartBlockSyntax partBlock)
    {
        // The score's meter (SectionMeterPlan): another part's `time` at a bar's start is this
        // part's <time> there too.
        IEnumerable<SyntaxNode> children = DirectChildren(partBlock);
        if (!_meterPlan.IsEmpty && partBlock.Parent is SectionDeclarationSyntax section
            && partBlock.ChildNodes().OfType<MusicBlockSyntax>().FirstOrDefault() is { } body)
        {
            var items = body.Items.ToList();
            var withChanges = _meterPlan.WithForeignChanges(section.SectionName, partBlock, items);
            if (withChanges.Count != items.Count)
                children = withChanges;
        }
        EmitPartMusic(partBlock.Name, children);
        PadVoice(partBlock);
    }

    // The book's section voices (SectionBarCounts.BuildSemanticIndex), read once per Export.
    private Svg.Collector.SectionBarCounts.SemanticIndex _sectionBars = new();

    /// <summary>
    /// The silent measures that bring one part voice's span of a section up to the section's
    /// canonical bar count — what the page pads the short staff with (MeasureCollector's
    /// section padding), so every part's measure N is the same bar: without it a one-bar
    /// melody A beside a two-bar bass A exported melody with one measure fewer and its B
    /// beside bass's A (scratch/p363/pm-two-parts.lys: P1 2 measures, P2 3; MEASURED
    /// 2026-09-10). Written as the bare bar lines the author's own <c>| |</c> would be —
    /// <see cref="ProcessNode"/>'s empty-bar rule gives each a bar of silence
    /// (<see cref="AddSilentBar"/>) — with one extra when the voice's last bar is still open
    /// (that first bar line only closes it; the index says). The count is the SEMANTIC one
    /// (<c>R1*4</c> is four bars, a repeat its played length).
    /// </summary>
    private void PadVoice(SyntaxNode voice)
    {
        int missing = _sectionBars.Missing(voice, out bool open);
        if (missing <= 0)
            return;
        // Silence follows the part's last note: no tie is carried over it.
        if (_currentPartName != null && _openAtEnd.Remove((_currentPartName, _xmlPlaySerial), out var hanging))
            foreach (var n in hanging) n.TieStart = false;
        if (_currentMeasure == null)
        {
            // The number follows the part's last written bar: the empty measure the block's
            // closing bar line opened took a number and was dropped unwritten by
            // FlushCurrentMeasure, so the padding bar was numbered one too high (measured
            // 2026-09-29 on SectionVoicePaddingExportTests' ChordRowLonger: 1, 3, 3).
            _measureNumber = _currentPart is { Measures.Count: > 0 } p ? p.Measures[^1].Number + 1 : 1;
            StartNewMeasure();
        }
        var bar = new BarlineSyntax(new Syntax.InternalSyntax.BarlineGreen(
            new Syntax.InternalSyntax.SyntaxToken(SyntaxKind.Bar, "|"), null, null), null, voice.Position);
        var key = voice is MusicBlockSyntax { Parent: PartBlockSyntax block } ? block : voice;
        var written = _sectionBars.ByContainer.GetValueOrDefault(key);
        for (int i = 0; i < missing + (open ? 1 : 0); i++)
        {
            // The bar this bar line writes empty is in the score's meter there
            // (SectionMeterPlan, HANDOFF §2 F-partmeter ⒜): another part's `time` at its start
            // is written here too, as PadPartsSilentInThisPlay does.
            int at = (written?.Bars ?? 0) + i - (open ? 1 : 0);
            if (written != null && at >= written.Bars
                && _meterPlan.ForeignChangeAt(written.SectionName, at, key) is { } time)
                ProcessNode(time);
            ProcessNode(bar);
        }
    }

    // The meters other parts write into each section's bars (SectionMeterPlan), read once per Export.
    private Svg.Collector.SectionMeterPlan _meterPlan = Svg.Collector.SectionMeterPlan.Empty;

    /// <summary>A by-part section (<c>part m { section A { … } }</c>) holds its music
    /// INLINE — not in a nested part block — so it is emitted here under the ENCLOSING
    /// part's name (and clef/transpose), exactly like the by-section
    /// <c>section A { m { … } }</c> form. Without this the inline notes hit
    /// <see cref="ProcessNode"/>'s skip-declarations case and the part exported EMPTY.</summary>
    /// <remarks>
    /// ⚠️ A section inside NO part declaration is a BARE section, and the name it is emitted
    /// under decides which header it reads. It used to fall straight to "Part 1" — a name no
    /// declaration answers — so the music came out at the default anchor with no sounding
    /// shift, whatever the part it is drawn on says. The score is what says whose it is
    /// (see <see cref="_bareSectionOwner"/>); "Part 1" remains for a file that names nobody.
    /// </remarks>
    private void EmitGroupedByPartSection(SectionDeclarationSyntax section)
    {
        IEnumerable<SyntaxNode> children = DirectChildren(section);
        if (!_meterPlan.IsEmpty && section.Parent is PartDeclarationSyntax)
            children = _meterPlan.WithForeignChanges(section.SectionName, section, children.ToList());
        EmitPartMusic(
            EnclosingPartName(section) ?? _bareSectionOwner ?? "Part 1",
            children);
        PadVoice(section);
    }

    /// <summary>The non-token child nodes of a container, in order.</summary>
    private static IEnumerable<SyntaxNode> DirectChildren(SyntaxNode node)
    {
        for (int i = 0; i < node.SlotCount; i++)
            if (node.GetChild(i) is { } child && child is not SyntaxTokenNode)
                yield return child;
    }

    /// <summary>The name of the <c>part</c> declaration a node sits inside, or null when it
    /// is not by-part (a standalone section maps to the default "Part 1").</summary>
    private static string? EnclosingPartName(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartDeclarationSyntax pd)
                return pd.Name.Text;
        return null;
    }

    /// <summary>Emits one span of a part's music (a by-section part block, or a
    /// by-part section's inline body) under <paramref name="partName"/>: sets up the
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
        // ⚠️ …moved by whatever the section REFERENCE that opened this play asked for
        // (`~B'` = +1). Both anchors move, because the two octave modes read different ones:
        // relative resolves nearest to _currentOctave, absolute measures from _octaveAnchor.
        // ApplyPartHeader re-arms _octaveAnchor on every call, so this adds and never
        // accumulates (OctaveContext.ResetForSection is the collector's spelling of it).
        _currentOctave = _partAnchorOctave + _sectionOctaveOffset;
        _octaveAnchor += _sectionOctaveOffset;
        _currentStep = 0;
        _ambientTonic = _homeTonic; // each voice starts at the score's home key
        // ... and so does its SIGNATURE, before the section states one of its own. Both
        // halves of "a section is self-contained" have to be here: reverting the baseline
        // and not the fifths would let one section's key print over the next one's.
        // ⚠️ No measure is open across this point — the change belongs to the bar
        // StartNewMeasure is about to open, not to whatever the previous part left behind.
        _currentMeasure = null;
        if (_homeKey is { } hk) (_keyFifths, _keyMode, _keyCustomXml) = hk;
        // ⚠️ THE METER REVERTS HERE TOO — the third half of "a section is self-contained".
        // A section that states no `time` of its own opens at the SCORE meter
        // (MeasureCollector.ProcessSectionPrologue; ScoreHomeMeter carries the rule), and
        // this exporter kept the previous section's mid-music change instead: measured
        // 2026-08-31, `section A { … time 3/4 … } section B { … }` exported every one of
        // B's bars in 3/4 while the page draws them in 4/4.
        // The bar the play BEFORE this one closed in — a bar full under its own meter leaves
        // nothing for this play's first bar to complete (ContinueSplitBar).
        int barTicksBefore = _bars.SenzaMisura ? 0 : 4 * DivisionsPerQuarter * _bars.Meter.Beats / _bars.Meter.BeatType;
        if (_sectionTime is null)
            _bars.RevertToHome();
        // ⚠️ AND A RESTORE HAS TO BE *WRITTEN*, not merely held. Both reverts above only
        // moved the running state; nothing marked the document dirty, so the next
        // StartNewMeasure's `else if (_attributesDirty)` arm never ran and the revert was
        // silent. SyncAttributes compares against _writtenKey/_writtenTime, so arming it
        // unconditionally emits exactly the boundaries that actually changed something.
        _attributesDirty = true;
        if (_sectionTime is { } st) ProcessTimeSignature(st);
        if (_sectionKey is { } sk) ProcessKeySignature(sk);
        _octaveAbsolute = _initialOctaveAbsolute; // restore file-level octave mode
        // THE SECTION CARRY RULE, for the tie: the open starts a part leaves at the end of a
        // block are matched with the first notes of the plays that follow it in the PLAYED
        // order once the whole document is written (FinishCarriedTies). Until 2026-09-28 they
        // were forgotten here unretracted, so a `c~ ||` into the next section's `c`, which the
        // page draws, was written as a tie start with no stop. A second block of the same part
        // in the SAME play continues it in the flow, as it always did.
        _tieToNextNote = false;
        _tieOpen.Clear();
        _currentPartName = partName;
        if (_openAtEnd.Remove((partName, _xmlPlaySerial), out var samePlay))
        {
            _tieOpen.AddRange(samePlay);
            _tieToNextNote = true;
        }
        _defaultDuration = Fraction.Quarter;

        // If this is the first measure for this part, add attributes
        bool isFirst = _currentPart!.Measures.Count == 0;
        // The section's header tempo, in the collector's order (time, tempo, key, partial):
        // read before the part's opening measure so that measure's direction carries it as
        // the piece's opening tempo, and after any later measure is opened so ProcessTempo
        // writes it as a metronome direction at the section's start. Until 2026-09-17 it
        // reached the document only because the header declaration was walked as music
        // (`section A { tempo 110 }` in the bass corpus's test.lys — 110 became 120 the
        // moment the header stopped being played).
        if (_sectionTempo is { } sectionTempo && isFirst)
            ProcessTempo(sectionTempo);
        // The bar the play opens in — what a split bar's two halves must add up to.
        int barTicksAtStart = _bars.SenzaMisura ? 0 : 4 * DivisionsPerQuarter * _bars.Meter.Beats / _bars.Meter.BeatType;
        StartNewMeasure(addAttributes: isFirst);
        if (_sectionTempo is { } laterSectionTempo && !isFirst)
            ProcessTempo(laterSectionTempo);
        // The section's header pickup shortens this part's first bar of the section — the
        // page applies its registry at the same spot, after the section meter, so the pickup
        // restores to the section's own time when it closes (MeasureCollector.Form.cs).
        if (_sectionPartial is { } sectionPartial)
            ArmPickup(sectionPartial);

        // Process the music; lyrics blocks are collected and mapped onto the emitted
        // notes afterwards.
        int measuresBefore = _currentPart!.Measures.Count;
        // Where this play's bars of the part start — the home of a chord row's harmonies.
        _playStartMeasure.TryAdd((partName, _xmlPlaySerial), measuresBefore);
        var lyricsBlocks = new List<LyricsBlockSyntax>();
        foreach (var child in children)
        {
            if (child is LyricsBlockSyntax lb)
                lyricsBlocks.Add(lb);
            else
                ProcessNode(child);
        }

        // A hairpin the carry rule cuts stops at this play's end, before the bar is flushed.
        CloseCutWedge();
        FlushCurrentMeasure();
        ContinueSplitBar(measuresBefore, barTicksAtStart, barTicksBefore);
        AttachLyrics(_currentPart!, measuresBefore, lyricsBlocks);

        // The block's first onset (what a tie carried into this play stops on), and what it
        // leaves open (FinishCarriedTies matches them once every play is written).
        _firstOnset.TryAdd((partName, _xmlPlaySerial), FirstOnset(_currentPart!, measuresBefore));
        if (_tieToNextNote && _tieOpen.Count > 0)
            _openAtEnd[(partName, _xmlPlaySerial)] = _tieOpen.ToList();
        _tieOpen.Clear();
        _tieToNextNote = false;
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
        _barSeenInScope = false;
        // The written-attributes record is the part's own (its remark says why).
        if (_currentPart is { Name: { } leaving })
            _writtenByPart[leaving] = (_writtenKey, _writtenTime, _writtenClef);
        (_writtenKey, _writtenTime, _writtenClef) = _writtenByPart.TryGetValue(name, out var written)
            ? written
            : (null, null, null);
        if (_partsByName.TryGetValue(name, out var existing))
        {
            _currentPart = existing;
            // The number follows the part's LAST WRITTEN bar, as PadVoice's does — not the
            // count of its bars. The two differ by one whenever the part opened with a
            // pickup (bar 0): counted, `0 | 1 … 8` is nine bars, and the next play's first bar
            // was numbered 10 — `Greensleeves` skipped 9 and `I'm Your Man`, whose first
            // section is the pickup alone, went 0, 2, 3 (measured 2026-10-03, p756, over the
            // 998-book corpus: every by-section book with a header pickup skipped a number
            // at its second section).
            _measureNumber = existing.Measures.Count > 0 ? existing.Measures[^1].Number + 1 : 1;
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
        else if (_currentMeasure is { } dropped)
        {
            // A measure dropped UNWRITTEN hands its number back: the empty bar a block's
            // closing bar line opened took the next number, and the music after the block
            // was numbered past it — `voice { c'2 d | e2 f | } { … }` then a third bar went
            // 1, 2, 4 (test/multi-voice, and nine more books of the corpus, 2026-10-03,
            // p757). PadVoice and EnsurePart resume from the last written bar for the same
            // reason; this is the one spelling under them. Bar 0 (an implicit pickup never
            // written) hands nothing back — the count after it is 1 either way.
            if (dropped.Number >= 1)
                _measureNumber = dropped.Number;
            // A percent run that ends its block leaves its STOP on the empty bar its closing
            // bar line opened, which is not written: the stop moves to the part's next bar, in
            // the next play (StartNewMeasure), so the reader's repeat does not run on to the end.
            if (dropped.Attributes is { MeasureRepeat: "stop" } && _currentPartName != null)
                _pendingRepeatStop[_currentPartName] = dropped.Attributes.MeasureRepeatBars;
        }
        _currentMeasure = null;
        _bars.SpendPartial();
        _justAutoClosedPickup = false;
        _barClosedByBlock = false;
        _barSeenInScope = false;
    }

    /// <summary>
    /// The bar an empty <c>| |</c> stands for: one bar of silence — the pickup's length while
    /// a <c>partial</c> is pending, the meter's otherwise — written as the rest the author's
    /// own <c>s1</c> would have produced (<see cref="ProcessRest"/>), so the two spellings
    /// stay one document (EmptyBarExportTests).
    /// </summary>
    /// <remarks>
    /// Owner's decision 2026-08-28: the page fills the bar with a full-measure spacer
    /// (MeasureBuilder.EmitEmptyMeasure) and the MIDI counts it (MidiExporter.MeasureTicks);
    /// this walk reused the empty measure and wrote NO bar, so <c>c1 | | e1</c> exported two
    /// measures where the page draws three, and an empty pickup <c>partial 4 | c4 …</c> pulled
    /// the <c>c4</c> into measure 0 (MEASURED 2026-09-09, scratch/p358/midi). Under
    /// <c>time none</c> the last metered length stands, as it does on the page.
    /// </remarks>
    private void AddSilentBar()
    {
        if (_currentMeasure == null) return;
        var length = _bars.BarLength;
        var (type, dots) = GetNoteType(length);
        _currentMeasure.Notes.Add(new MusicXmlNote
        {
            IsRest = true,
            Duration = FractionToTicks(length),
            Type = type,
            Dots = dots
        });
        _lastPitchedNote = null;
        _lastEmittedNotes.Clear();
        // The pickup, if one was pending, is this bar: spent.
        _bars.SpendPartial();
    }

    /// <summary>
    /// Anacrusis: the measure currently being built is a pickup of the declared length. Mark
    /// it implicit and arm the duration-based auto-close (no written barline required) — the
    /// ONE arm for a <c>partial</c> written in the music and for a section header's
    /// (<see cref="EmitPartMusic"/>). A LEADING pickup — the part's first measure — is bar 0,
    /// so the first FULL measure becomes 1; a pickup later in the part keeps the number it
    /// was dealt and the count runs on, which is how the page numbers it (Measure.IsPickup:
    /// "a LEADING pickup (index 0) is bar 0"; LayoutEngine.Annotations shifts the numbers by
    /// one only when measures[0] is the pickup). Until 2026-09-17 every pickup restarted the
    /// count at 0 — a mid-piece <c>partial</c> numbered the rest of the part from 1 again.
    /// LILYPOND-REF: ly/music-functions-init.ly:1697-1705 partial = context-spec-music 'Timing
    /// </summary>
    private void ArmPickup(PartialDeclarationSyntax partial)
    {
        if (_currentMeasure == null || _currentMeasure.Notes.Count != 0)
            return;
        _currentMeasure.Implicit = true;
        if (_currentPart != null && _currentPart.Measures.Count == 0)
        {
            _currentMeasure.Number = 0;
            _measureNumber = 1;
        }
        _bars.SetPartial(partial.ToFraction());
        _pickupAccumulated = Fraction.Zero;
    }

    /// <summary>
    /// While a 'partial' pickup is pending, accumulate the duration written into it and
    /// auto-close the implicit measure once it reaches the declared length — even
    /// with no written barline — mirroring MeasureBuilder.AddItem so MusicXML and SVG
    /// split the pickup identically. A bar line before that closes the bar and spends
    /// the pickup short (the bar-line arm of <see cref="ProcessNode"/>), as the page does.
    /// </summary>
    private void MaybeClosePickup(Fraction added)
    {
        if (_bars.Partial is not { } pickup)
            return;
        _pickupAccumulated += added;
        if (_pickupAccumulated >= pickup)
        {
            _bars.SpendPartial();
            if (_currentMeasure != null && _currentPart != null && _currentMeasure.Notes.Count > 0)
            {
                _currentPart.Measures.Add(_currentMeasure);
                StartNewMeasure();
                _justAutoClosedPickup = true;
            }
        }
    }

    /// <summary>
    /// Closes the open bar when a repeat pass ends on a FULL bar with no bar line written
    /// after it — <c>repeat percent 4 { r2. | r2. }</c> — as the page does (MeasureBuilder
    /// completes a bar when it reaches the time signature). Without it each pass ran on into
    /// the next: that body exported as <c>r | r r | r …</c>, two rests to a 3/4 bar.
    /// </summary>
    private void CloseFullBarAtPassEnd()
    {
        if (_currentMeasure is not { Notes.Count: > 0 } open || _currentPart == null
            || _bars.SenzaMisura || _bars.Partial is not null)
            return;
        int barTicks = 4 * DivisionsPerQuarter * _bars.Meter.Beats / _bars.Meter.BeatType;
        if (ElapsedTicks(open) < barTicks)
            return;
        _currentPart.Measures.Add(open);
        StartNewMeasure();
        _barClosedByBlock = true;
    }

    // The time a measure's notes fill: the furthest point any voice reaches (a <backup>
    // rewinds for the next voice; chord members and grace notes take no time; raw elements
    // — directions, harmony — none either).
    /// <summary>
    /// The bar a section boundary splits is ONE bar for the numbering: a play whose first bar
    /// is the rest of the bar the play before it left short — the two together exactly one
    /// bar of the meter the play opens in, the bar before it no pickup — takes the number of
    /// that bar and is implicit (a reader displays no number on it), and the play's later
    /// bars follow from there. The page's rule (MeasureCollector.SectionBoundaryContinuations,
    /// BarNumberEngraver.NumberMeasures: <c>ContinuesBar</c>): written that way when a repeat
    /// sign or a volta bracket stands mid-bar — <c>|: A [1. B] :| [2. C]</c> where A ends on
    /// the half bar and every ending opens with the other half. The play's declared
    /// <c>partial</c> completes the bar before it the same way (the upbeat finishes the
    /// incomplete bar it follows, LilyPond's mid-piece <c>\partial</c>; until session 795 it
    /// was a pickup bar of its own, 1, 2, 3i, 4 for the page's 1 2 3 4 — now 1, 2, 2i, 3 for
    /// 1 2 2 3, the owner's decision for musical validity). A second ending's first bar
    /// follows the FIRST ending's last bar in this document (the body is written once, under
    /// repeat bar lines), which is a full bar, so it is numbered on — as the page numbers it
    /// ("bar numbers continue through alternatives"). Until 2026-10-03 (p759, owner's GO) the
    /// MusicXML numbered both halves: 1, 2, 3 where the page prints 1, 2, 2 and the twin's
    /// LilyPond 1, 2, 3.
    /// </summary>
    private void ContinueSplitBar(int firstIndex, int barTicks, int prevBarTicks)
    {
        var measures = _currentPart!.Measures;
        if (barTicks <= 0 || prevBarTicks <= 0 || firstIndex <= 0 || firstIndex >= measures.Count)
            return;
        var prev = measures[firstIndex - 1];
        var first = measures[firstIndex];
        if (prev.Implicit)
            return;
        int head = ElapsedTicks(prev), tail = ElapsedTicks(first);
        // The bar before is short under ITS meter (a 2/4 bar before a play in 4/4 is full,
        // whatever the halves add up to), and the halves make one bar of THIS play's.
        if (head <= 0 || tail <= 0 || head >= prevBarTicks || head >= barTicks || head + tail != barTicks)
            return;
        first.Implicit = true;
        for (int i = firstIndex; i < measures.Count; i++)
            measures[i].Number -= 1;
        _measureNumber -= 1;
    }

    private static int ElapsedTicks(MusicXmlMeasure measure)
    {
        int at = 0, furthest = 0;
        foreach (var n in measure.Notes)
        {
            if (n.RawElement != null || n.IsChord || n.IsGrace)
                continue;
            at += n.IsBackup ? -n.Duration : n.Duration;
            furthest = Math.Max(furthest, at);
        }
        return furthest;
    }

    /// <summary>The key signature as the DOCUMENT must spell it: the written fifths plus
    /// whatever the part's instrument transpose adds. One house, so the opening attributes
    /// and a later change cannot disagree about it.</summary>
    private int EffectiveKeyFifths()
        => _currentTranspose is { } trk
            ? _keyFifths + PitchTransposer.KeySignatureFifthsShift(trk.step, trk.alt)
            : _keyFifths;

    /// <summary>
    /// True while the part is on a percussion clef, which carries no key signature: the page
    /// draws none on a drum staff (MEASURED 2026-09-30: `key d major` and `key c major` over
    /// the same drum part render the same SVG), and a reader given one drew it — MuseScore put
    /// ♯s on the drum staff and a ♮ on the bass drum (LilySharp-Omr
    /// docs/repro/musicxml-exporter-bugs.md #9).
    /// </summary>
    private bool OnPercussionStaff => _clefSign == "percussion";

    private void StartNewMeasure(bool addAttributes = false)
    {
        _currentMeasure = new MusicXmlMeasure { Number = _measureNumber++ };

        if (addAttributes)
        {
            _currentMeasure.Attributes = new MusicXmlAttributes
            {
                Divisions = DivisionsPerQuarter,
                TimeBeats = _bars.Meter.Beats,
                TimeBeatsText = _bars.Meter.BeatsText,
                TimeSenzaMisura = _bars.SenzaMisura,
                TimeBeatType = _bars.Meter.BeatType,
                KeyFifths = OnPercussionStaff ? null : EffectiveKeyFifths(),
                KeyCustom = OnPercussionStaff ? null : _keyCustomXml,
                KeyMode = OnPercussionStaff ? null : _keyMode,
                ClefSign = _clefSign,
                ClefLine = _clefLine > 0 ? _clefLine : null,
                ClefOctaveChange = _clefOctaveChange,
                TransposeSemitones = _partTransposeWritten != 0
                    ? _partTransposeWritten
                    : null
            };

            SetOpeningTempo(_currentMeasure);
            RecordWrittenAttributes();
            _attributesDirty = false;
        }
        else if (_attributesDirty)
        {
            SyncAttributes();
        }

        if (_currentPartName != null && _pendingRepeatStop.Remove(_currentPartName, out int stopBars))
            MarkMeasureRepeat(_currentMeasure, "stop", stopBars);
    }

    /// <summary>A percent run's STOP whose bar was never written, waiting for the part's next
    /// bar (<see cref="FlushCurrentMeasure"/>).</summary>
    private readonly Dictionary<string, int> _pendingRepeatStop = new();

    /// <summary>Remember what the measure just written says, so the next change is a change.</summary>
    private void RecordWrittenAttributes()
    {
        _writtenKey = (EffectiveKeyFifths(), _keyMode, _keyCustomXml);
        _writtenTime = RunningTime;
        _writtenClef = (_clefSign, _clefLine, _clefOctaveChange);
    }

    /// <summary>`key!` / `time!` / `clef!` seen since the last attributes were written: the
    /// page draws them though they change nothing (owner's decision 2026-10-02), so the
    /// document states them again. Held with <see cref="_attributesDirty"/> when they arrive
    /// after notes.</summary>
    private bool _forcedKey, _forcedTime, _forcedClef;

    /// <summary>Write an <c>&lt;attributes&gt;</c> for whatever the walk has changed since
    /// the document last said it — a key change, a meter change, a clef change — or forced
    /// with a `!` (<see cref="_forcedKey"/>). A restatement that changes nothing writes
    /// nothing, as the page draws nothing.</summary>
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
        var time = RunningTime;
        var clef = (_clefSign, _clefLine, _clefOctaveChange);
        bool keyChanged = _forcedKey || _writtenKey is null || !_writtenKey.Value.Equals(key);
        bool timeChanged = _forcedTime || _writtenTime is null || !_writtenTime.Value.Equals(time);
        bool clefChanged = _forcedClef || _writtenClef is null || !_writtenClef.Value.Equals(clef);
        _attributesDirty = false;
        _forcedKey = _forcedTime = _forcedClef = false;
        if (!keyChanged && !timeChanged && !clefChanged) return;

        // Merge into this measure's own attributes when it already has one (the part's
        // opening bar, whose section states a key of its own): two <attributes> in one
        // measure would be a reader's coin toss.
        var attrs = _currentMeasure.Attributes;
        if (attrs == null)
            _currentMeasure.Attributes = attrs = new MusicXmlAttributes { Divisions = null };

        if (keyChanged && !OnPercussionStaff)
        {
            attrs.KeyFifths = key.Item1;
            attrs.KeyMode = _keyMode;
            attrs.KeyCustom = _keyCustomXml;
        }
        if (timeChanged)
        {
            attrs.TimeBeats = time.Beats;
            attrs.TimeBeatsText = time.BeatsText;
            attrs.TimeBeatType = time.BeatType;
            attrs.TimeSenzaMisura = time.Senza;
        }
        if (clefChanged)
        {
            attrs.ClefSign = _clefSign;
            attrs.ClefLine = _clefLine > 0 ? _clefLine : null;
            attrs.ClefOctaveChange = _clefOctaveChange;
        }
        RecordWrittenAttributes();
    }

    /// <summary>
    /// Runs the handler of one written note-like item and stamps the notes it added with the
    /// item's <c>SourceStart</c> (<see cref="MusicXmlNote.SourcePosition"/>) — the offset the
    /// page's items carry, so <see cref="WriteBeams"/> can find each exported note's place in
    /// the page's beam groups. Grace notes, backups and raw pseudo-entries are not the item's.
    /// A handler that starts a new measure leaves its notes in two lists; both are stamped.
    /// </summary>
    private void Stamped(SyntaxNode item, System.Action handle)
    {
        var before = _currentMeasure;
        int count = before?.Notes.Count ?? 0;
        handle();
        int source = item.SourceStart;
        if (before != null)
            StampFrom(before, count);
        if (_currentMeasure != null && !ReferenceEquals(_currentMeasure, before))
            StampFrom(_currentMeasure, 0);

        void StampFrom(MusicXmlMeasure measure, int from)
        {
            for (int i = from; i < measure.Notes.Count; i++)
            {
                var n = measure.Notes[i];
                if (n.SourcePosition < 0 && !n.IsGrace && !n.IsBackup && n.RawElement == null)
                    n.SourcePosition = source;
            }
        }
    }

    /// <summary>
    /// Puts a <c>&lt;measure-repeat&gt;</c> on a measure, INTO the attributes it already has.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-30 both ends were set with <c>Attributes ??= new …</c>, so a measure that
    /// already stated a clef, key or time — the measure after a section's percent run, which a
    /// section opening restates — kept its attributes and lost the mark: the file had
    /// <c>start</c>s and no <c>stop</c> (LilySharp-Omr docs/repro/musicxml-exporter-bugs.md #3).
    /// </remarks>
    private static void MarkMeasureRepeat(MusicXmlMeasure measure, string type, int bars)
    {
        measure.Attributes ??= new MusicXmlAttributes { Divisions = DivisionsPerQuarter };
        measure.Attributes.MeasureRepeat = type;
        measure.Attributes.MeasureRepeatBars = bars;
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
                Stamped(note, () => ProcessNote(note));
                break;
            case DrumNoteSyntax drumNote:
                Stamped(drumNote, () => ProcessDrumNote(drumNote));
                break;

            case ChordSyntax chord:
                Stamped(chord, () => ProcessChord(chord));
                break;

            case ChordRepetitionSyntax rep:
                Stamped(rep, () => ProcessChordRepetition(rep));
                break;

            case SlashNoteSyntax slash:
                Stamped(slash, () => ProcessSlashNote(slash));
                break;

            case BareDurationSyntax bare:
                Stamped(bare, () => ProcessBareDuration(bare));
                break;

            case ArpeggioSyntax arpeggio:
                ProcessArpeggio(arpeggio);
                break;

            case RestSyntax rest:
                ProcessRest(rest);
                break;

            case PartialDeclarationSyntax partial:
                ArmPickup(partial);
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
                    // A bare `|` — or a `|:` that does not open the scope — with no time
                    // since the last boundary is an EMPTY BAR, not a redundant bar line:
                    // write the bar of silence it stands for before closing it (the rule
                    // MidiExporter.ProcessSequence and MeasureBuilder.HandleBarline share).
                    // ⚠️ A chord ROW's bar lines reach here too — the walk has no arm for
                    // ChordPartBlockSyntax, so the default arm visits its children — and
                    // they are the ROW's grid, not this part's bars: `Am | |` over a two-bar
                    // melody would have written two silent bars INTO the melody (measured on
                    // test/volta-chord-row the day this landed). The row's bars are read by
                    // nobody here; only its `|:` / `:|` flags below were ever reached.
                    bool inChordRow = false;
                    for (var anc = barline.Parent; anc != null; anc = anc.Parent)
                        if (anc is ChordPartBlockSyntax) { inChordRow = true; break; }
                    bool pairsHere = !inChordRow
                        && (barText == "|" || (barText == "|:" && _barSeenInScope));
                    if (!inChordRow)
                        _barSeenInScope = true;
                    // Right after a voice span the bar behind this line is the span's last,
                    // already written: this line closes IT (see _barClosedByBlock).
                    bool closesSpan = _barClosedByBlock && !inChordRow
                        && _currentMeasure is { Notes.Count: 0 } && _currentPart is { Measures.Count: > 0 };
                    if (!inChordRow)
                        _barClosedByBlock = false;
                    bool timePassed = closesSpan || _currentMeasure is { Notes.Count: > 0 };
                    if (pairsHere && !closesSpan && _currentMeasure != null && _currentPart != null
                        && _currentMeasure.Notes.Count == 0)
                        AddSilentBar();   // worth the pickup while one is pending — read before it is spent
                    // The line closes a bar — one with time in it, the span's last, or the
                    // empty bar a bare `|` stands for — so a pending pickup is SPENT, short or
                    // not (the page's MeasureBuilder.ResetPerMeasureState at every measure close;
                    // MidiExporter.ProcessSequence's `pairsHere || timePassed`). A typed bar line
                    // on an empty span decorates and closes nothing, and leaves it pending.
                    if (!inChordRow && (pairsHere || timePassed))
                        _bars.SpendPartial();
                    var closing = closesSpan ? _currentPart!.Measures[^1] : _currentMeasure;
                    if (closing != null)
                    {
                        // Closing side: repeat sign / double / final / dashed.
                        if (barText is ":|" or ":|:")
                            closing.RepeatBackward = true;
                        else if (barText == "||")
                            closing.BarStyle = "light-light";
                        else if (barText == "|.")
                            closing.BarStyle = "light-heavy";
                        else if (barText == "!")
                            closing.BarStyle = "dashed";
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
                HandleDynamicText(dynamic.DynamicToken.Text, dynamic.SourceStart);
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
                    // …and a TWO-measure body its sign too (%%, the page's DoublePercentRepeat):
                    // MusicXML's measure-repeat names the measures one repetition spans.
                    int percentBars = repeat.Body.Items.Count(i => i is BarlineSyntax);
                    bool oneMeasurePercent = repeat.RepeatType.Text == "percent"
                        && percentBars is 1 or 2
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
                            int passStart = _currentPart!.Measures.Count;
                            ProcessNode(repeat.Body);
                            // The START rides the FIRST measure of the first repetition (the pass
                            // flushes it, so it is the first one added during the pass).
                            if (rep == 1 && _currentPart.Measures.Count > passStart)
                                MarkMeasureRepeat(_currentPart.Measures[passStart], "start", percentBars);
                        }
                        // The STOP rides the measure AFTER the last repetition — "the first
                        // measure where the repeats are no longer displayed" (MusicXML 4.0) —
                        // the one the body's closing bar line opened. When nothing follows it
                        // stays empty and is not written, and the sign runs to the part's end.
                        if (repCount > 1 && _currentMeasure != null)
                            MarkMeasureRepeat(_currentMeasure, "stop", percentBars);
                        break;
                    }
                    for (int rep = 0; rep < repCount; rep++)
                    {
                        if (rep > 0)
                            (_currentOctave, _currentStep, _defaultDuration) = frame;
                        ProcessNode(repeat.Body);
                        CloseFullBarAtPassEnd();
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
                    // ⚠️ …and the anchor is the SECTION's, not the part's (2026-08-31): a
                    // section quoted `~B'` is an octave up, phrase bodies inside it included.
                    // _sectionOctaveOffset is 0 for every play written without marks, and the
                    // collector says the same in one line (OctaveContext.ResetToInitial).
                    _currentOctave = _partAnchorOctave + _sectionOctaveOffset + varRef.OctaveOffset;
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
                    int? anchorStep = LilySharp.Core.Music.PhraseAnchor.Anchor(varBody,
                        n => _variables.TryGetValue(n, out var b) ? b : null, out var anchorShape);
                    if (anchorStep == LilySharp.Core.Music.PhraseAnchor.Tonic)
                        anchorStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
                    // A body opening with a chord(…) item: its lowest note, absolute.
                    var shapeAnchor = anchorStep == LilySharp.Core.Music.PhraseAnchor.Shape
                        ? ShapeAnchorOf(anchorShape) : null;
                    if (anchorStep == LilySharp.Core.Music.PhraseAnchor.Shape)
                        anchorStep = null;
                    ProcessNode(varBody);
                    _currentTranspose = savedTranspose;
                    _octaveAnchor = savedAnchor;
                    // Frame hand-off at the phrase's ANCHOR (matches the
                    // collector's ExitPhraseTranspose): the reference is ONE
                    // item, the chord rule — its interior never leaks, and its
                    // own marks shift what propagates, so a note after Melody'
                    // is relative to the shifted anchor. A pitchless body hands
                    // nothing off.
                    if (shapeAnchor is { } sa)
                    {
                        _currentStep = sa.Step;
                        _currentOctave = sa.Octave;
                    }
                    else if (anchorStep is { } astep)
                    {
                        int oct = RelativeOctave.Resolve(
                            0, _partAnchorOctave + _sectionOctaveOffset + varRef.OctaveOffset,
                            astep, 0);
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
                // A cue clef is drawing only: the body is read in the running relative frame
                // (InstrumentDefaults.DefaultAnchorOctave).
                ProcessNode(cue.Body);
                break;

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
        // …and the note-value default is part of that frame (session 398, the same rule in
        // MeasureCollector.MusicWalk): every voice opens at it, and so does the music after.
        var spanDefault = _defaultDuration;

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
            // The DURATION default is the span's too (BuildExtraVoiceTracks reads the
            // recorded span's value; until session 398 both sides reset it to a quarter,
            // which drew `c8 voice { d e } { f g }`'s f g as crotchets).
            _defaultDuration = spanDefault;
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
        _barClosedByBlock = true;
        // ... and in the frame the span opened in, which is where the page reads the next
        // note from. Leaving voice 1's end here read `d` two octaves off in the probe.
        _currentOctave = spanOctave;
        _currentStep = spanStep;
        _defaultDuration = spanDefault;
    }

    private void ProcessTimeSignature(TimeSignatureSyntax timeSig)
    {
        // A metered `time` is the meter in force; `time none` keeps the last metered pair and
        // says senza misura (Semantics.BarContext.SetTime has the rule).
        _bars.SetTime(timeSig);
        _forcedTime |= timeSig.IsForced;
        _attributesDirty = true;
        SyncAttributes();
    }

    private void ProcessTempo(TempoDeclarationSyntax tempo)
    {
        var value = tempo.Value;
        if (value.Bpm is not int bpm)
            return;
        _tempo = bpm;
        _tempoStated = true;
        _tempoBeatUnit = value.BeatUnit ?? 4;
        _tempoBeatDots = value.BeatDots;
        // A mid-piece tempo change emits a metronome direction at this point; the
        // initial tempo is carried by the first measure's attributes direction.
        if (_currentMeasure != null && (_currentMeasure.Notes.Count > 0 || _currentMeasure.Number > 1))
            AddDirection(TempoDirection());
    }

    // The beat unit the running _tempo counts in (session 398): `tempo 2 = 60` is sixty
    // minims a minute, and the document writes that unit in its <metronome> and the
    // crotchet rate in <sound tempo>. Until then every metronome said "quarter".
    private int _tempoBeatUnit = 4;
    private int _tempoBeatDots;

    /// <summary>The running tempo as a direction — the ONE place the three numbers meet.</summary>
    private MusicXmlDirection TempoDirection()
        => new() { Tempo = _tempo, TempoBeatUnit = _tempoBeatUnit, TempoBeatDots = _tempoBeatDots };

    /// <summary>True once the source has stated a tempo. Until then the running
    /// <see cref="_tempo"/> is only the playback default, which the page does not print.</summary>
    private bool _tempoStated;

    /// <summary>
    /// A part's opening tempo: the stated one as its metronome direction, or — no tempo
    /// stated — the playback default as a bare <c>&lt;sound tempo&gt;</c>, which sets the speed
    /// and prints nothing.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-30 the default was a full direction, so every part of a piece with no
    /// tempo opened with a ♩ = 120 the page never drew (LilySharp-Omr
    /// docs/repro/musicxml-exporter-bugs.md #7).
    /// </remarks>
    private void SetOpeningTempo(MusicXmlMeasure measure)
    {
        if (_tempoStated)
            measure.Direction = TempoDirection();
        else
            measure.SoundTempo = _tempo;
    }

    private void ProcessMetadata(MetadataDeclarationSyntax metadata)
    {
        if (_document == null) return;

        var keyword = metadata.Keyword.ToLowerInvariant();

        if (keyword == "title" && metadata.StringValue is string title)
            _document.Title = title;
        else if (keyword == "composer" && metadata.StringValue is string composer)
            _document.Composer = composer;
        else if (keyword == "subtitle" && metadata.StringValue is string subtitle)
            _document.Subtitle = subtitle;
        else if (keyword == "poet" && metadata.StringValue is string poet)
            _document.Poet = poet;
    }

    private void ProcessKeySignature(KeySignatureSyntax key)
    {
        _forcedKey |= key.IsForced;
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

        _partAnchorOctave = header.AnchorOctave;
        _octaveAnchor = header.AbsoluteBaseOctave;
        // The strings a symbol-less @chord(x32010) is named on (the page's reading, the same header).
        _partTuning = Tablature.Tunings.GetTuning(header.Tuning);
        // …and the fretted instrument its chord diagrams draw on when the layout names none.
        _partFrettedTuning = header.FrettedTuning;

        // The part's General MIDI sound, the one the .mid gives it (HANDOFF §2 F-midi).
        if (_currentPart != null)
            _currentPart.MidiProgram = header.MidiProgram;

        // The whole written→sounding distance, which the chord shapes are fretted through…
        _partTransposeSemitones = header.SoundingShiftSemitones;
        // …but the document splits it: the header clef's octave goes INTO the pitches
        // (MusicXML reads a <pitch> under its clef, <clef-octave-change> included — a
        // `treble_8` staff's middle line is B3, which is what the page draws there and what it
        // sounds), and <transpose> states only the instrument's share. From 2026-08-17 to
        // 2026-10-05 the pitches stayed as written on the plain clef and <transpose> carried the
        // clef's octave too: the sound was right, but a reader that honours the clef (MuseScore)
        // drew the staff and fretted the TAB an octave high (LilySharp-Omr feedback #17).
        // ⚠️ The importer undoes the same split — MusicXmlReader.ReadPart.
        _partTransposeWritten = header.TranspositionSemitones;
        if (_currentPart != null)
            _currentPart.PitchOctaveShift = header.ClefOctaveSemitones / 12;
    }

    private void ProcessClef(ClefDeclarationSyntax clef)
    {
        var word = clef.ClefName?.Text.ToLower();
        SetClef(word);
        _forcedClef |= clef.IsForced;
        // A clef is drawing only: it moves no relative frame (InstrumentDefaults.DefaultAnchorOctave).
        // ⚠️ Only the IN-MUSIC clef syncs. A header clef reaches the document through the
        // part's opening attributes, and syncing there would write a change on the bar a
        // second part happens to be starting.
        _attributesDirty = true;
        SyncAttributes();
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
                && mark.Name.Equals("notehead", StringComparison.Ordinal)
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
        AddDrumMark(xmlNote, info);
        _currentMeasure.Notes.Add(xmlNote);
        MaybeClosePickup(duration);
    }

    /// <summary>
    /// The mark a drum carries of itself (<see cref="DrumInfo.Mark"/>, LilyPond's style
    /// table, which the page draws — MeasureCollector's drum walk): <c>+</c> on the closed
    /// hi-hat and the muted hand drums as <c>&lt;technical&gt;&lt;stopped/&gt;</c>, <c>○</c> on
    /// the open ones as <c>&lt;technical&gt;&lt;open/&gt;</c>, and the guiros' staccato / tenuto.
    /// Until 2026-09-30 the file had neither, so <c>hho</c> and <c>hhc</c> read as one
    /// instrument (LilySharp-Omr docs/repro/musicxml-exporter-bugs.md #10).
    /// </summary>
    private static void AddDrumMark(MusicXmlNote note, DrumInfo info)
    {
        switch (info.Mark)
        {
            case "open" or "stopped":
                note.Technicals.Add(new System.Xml.Linq.XElement(info.Mark));
                break;
            case "staccato" or "tenuto":
                note.Articulations.Add(info.Mark);
                break;
        }
    }

    private void ProcessNote(NoteSyntax note)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;
        _lastEmittedHost = note.SourceStart;

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
        ProcessArticulations(note.Articulations, xmlNote, host: note.SourceStart);

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
                && named.NameToken.Text is "glissando" or "slide")
            {
                string el = named.NameToken.Text.Equals("slide", StringComparison.Ordinal)
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
        // A chord(…) member spread into its notes (the page's MeasureCollector.ProcessArpeggio).
        var members = Music.ArpeggioSpread.Of(arpeggio, ShapeNotesOf);
        if (members.Count == 0)
            return;

        // The group occupies its total (trailing `>>N`, or the inherited running duration);
        // its members split that into shares. An auto-tuplet fits the shares into the
        // P-note frame; a member's shares are written as ArpeggioSubdivision.SpellShares
        // spells them — one note, or tied notes.
        Fraction total = arpeggio.TotalDuration?.ToFraction() ?? _defaultDuration;
        var sub = ArpeggioSubdivision.Compute(Music.ArpeggioSpread.ShareCount(members), total);
        var tupletMeasure = _currentMeasure;
        int tupletFrom = _currentMeasure?.Notes.Count ?? 0;
        int tupletNumber = 0;
        if (sub.HasTuplet)
        {
            _tupletStack.Push((sub.TupletNum, sub.TupletBase));
            tupletNumber = _tupletStack.Count;
        }
        var savedDefault = _defaultDuration;
        // Octave marks after '>>' shift the whole group (like a chord's '<c e g>,'): applied
        // to the ROOT, inherited by the stacked members / degrees via the anchor octave.
        int groupOctave = arpeggio.OctaveOffset;

        // A dynamic on the group (`<< c e g >>@f`) sounds at its start: route it
        // through the shared dynamic/wedge funnel and emit before the first member.
        foreach (var a in arpeggio.Articulations)
            if (a is DynamicSyntax dyn)
                HandleDynamicText(dyn.DynamicToken.Text);
        EmitPendingDynamic();
        // A string number on the group (`>>4\3`) is every pitched / degree member's that
        // names none of its own — the collector's groupString (a chord member inside the
        // group keeps to the chord's own pairing, as on the page).
        int? groupString = arpeggio.Articulations.OfType<StringNumberAnnotationSyntax>()
            .FirstOrDefault()?.StringNumber;

        // The root is the first PITCHED member (leading rests just advance time); it
        // resolves relatively and anchors the group. Subsequent PITCHED members stack above
        // it (absolute mode with the anchored octave), order-independently; rests keep the
        // normal frame; degrees stack on the root by diatonic steps in the key.
        bool savedAbsolute = _octaveAbsolute;
        int savedAnchor = _octaveAnchor;
        // The incoming frame, for a group with no pitched member (see the end).
        int frameStepIn = _currentStep, frameOctaveIn = _currentOctave;
        bool rootSet = false;
        int anchorOctave = 0;
        int rootStep = 0;
        foreach (var ((member, shares, slurStart, slurEnd, _, _), spreadNote) in members)
        {
            var parts = sub.SpellShares(shares);
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
                EmitArpeggioXmlDegree(degree, rootStep, anchorOctave, parts, slurStart, slurEnd, groupString);
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
                EmitArpeggioXmlPitch(pitch, isRoot ? groupOctave : 0, parts, slurStart, slurEnd, groupString);
            else if (member is ChordSyntax chord)
            {
                // A second part re-reads the chord, so the frame is put back to what the
                // first part read (the root chord folds the group's marks in on every read).
                var frame = (_octaveAbsolute, _octaveAnchor, _currentStep, _currentOctave);
                // A spread note plays as its chord(…) item narrowed to that note (the page's rule).
                _spreadNote = spreadNote is { } sn ? (chord.Green, sn) : null;
                for (int k = 0; k < parts.Count; k++)
                {
                    bool first = k == 0, last = k == parts.Count - 1;
                    if (!first)
                        (_octaveAbsolute, _octaveAnchor, _currentStep, _currentOctave) = frame;
                    _defaultDuration = Fraction.FromNoteValue(parts[k].Value).Dotted(parts[k].Dots);
                    Stamped(chord, () => ProcessChord(chord, isRoot ? groupOctave : 0));
                    if (first && slurStart)
                        foreach (var n in _chordMembers) n.SlurStart = true;
                    if (last && slurEnd)
                        foreach (var n in _chordMembers) n.SlurStop = true;
                    if (!last)
                        OpenTies(_chordMembers);
                }
                _spreadNote = null;
            }
            else
            {
                foreach (var part in parts) // a rest: one per part, no tie
                {
                    _defaultDuration = Fraction.FromNoteValue(part.Value).Dotted(part.Dots);
                    ProcessNode(member);
                }
            }
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
        _octaveAnchor = savedAnchor;
        // Acts like one note: a trailing `>>N` carries N as the running duration.
        _defaultDuration = arpeggio.TotalDuration?.ToFraction() ?? savedDefault;
        // THE GROUP WRITES THE FRAME the way a chord does (user decision, 2026-09-27): the
        // next note is relative to the group's ANCHOR, the root's bare letter (or the tonic)
        // plus the marks after '>>'; a group of rests hands the incoming frame on, shifted
        // by the marks (MeasureCollector.MusicWalk ProcessArpeggio).
        bool anchored = rootSet && !savedAbsolute;
        _currentOctave = anchored ? anchorOctave : frameOctaveIn + groupOctave;
        _currentStep = anchored ? rootStep : frameStepIn;

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

    /// <summary>A bare arpeggio pitch → one sequential note per written part (tied to one
    /// another when its shares take more than one), resolved once through the octave frame
    /// the caller set up. The member's own post-events and slur marks ride the first part;
    /// the last part is what a marker after <c>&gt;&gt;</c> hangs on (_lastEmittedNotes).
    /// The string number (the member's own, else <paramref name="groupString"/>) rides
    /// EVERY part, as it does on the page's items — each part is a note of its own.</summary>
    private void EmitArpeggioXmlPitch(PitchSyntax pitch, int octaveShift,
        IReadOnlyList<(int Value, int Dots)> parts, bool slurStart, bool slurEnd, int? groupString)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        int? stringNumber = pitch.Articulations.OfType<StringNumberAnnotationSyntax>()
            .FirstOrDefault()?.StringNumber ?? groupString;
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
        var (tupletActual, tupletNormal) = CurrentTupletRatio();

        for (int k = 0; k < parts.Count; k++)
        {
            bool first = k == 0, last = k == parts.Count - 1;
            var duration = Fraction.FromNoteValue(parts[k].Value).Dotted(parts[k].Dots);
            int durationTicks = FractionToTicks(duration);
            var (type, dots) = GetNoteType(duration);
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
                Notehead = NoteheadFromMarks(pitch.Articulations),
                ActualNotes = tupletActual,
                NormalNotes = tupletNormal,
                // The page's arpeggio note carries its MEMBER's offset (BuildArpeggioNoteItems),
                // not the group's — the beams find it by this (WriteBeams).
                SourcePosition = pitch.SourceStart,
            };
            if (first)
                ProcessArticulations(
                    pitch.Articulations.Where(a => a is not StringNumberAnnotationSyntax), xmlNote);
            if (stringNumber is { } s)
                xmlNote.Technicals.Add(new System.Xml.Linq.XElement("string", s));
            if (first && slurStart) xmlNote.SlurStart = true;
            if (last && slurEnd) xmlNote.SlurStop = true;
            // A tie into the group, or from the previous part, ends here; a part that is not
            // the last ties on to the next.
            CloseTies([xmlNote]);
            if (!last) OpenTies([xmlNote]);
            _currentMeasure.Notes.Add(xmlNote);
            _lastPitchedNote = xmlNote;
            _lastEmittedNotes.Clear();
            _lastEmittedNotes.Add(xmlNote);
            MaybeClosePickup(duration);
        }
    }

    /// <summary>A scale-degree arpeggio member → one sequential note, stacked on the group's
    /// anchor (the root, or the key tonic when no pitched member precedes — the caller
    /// resolves it) by diatonic steps in the WRITTEN key, then transposed like a pitch.</summary>
    private void EmitArpeggioXmlDegree(ScaleDegreeSyntax degree, int rootStep, int anchorOctave,
        IReadOnlyList<(int Value, int Dots)> parts, bool slurStart, bool slurEnd, int? groupString)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var (dstep, dalter, doctave) = ChordDegrees.Resolve(
            rootStep, anchorOctave, degree.Number, degree.Alteration, degree.OctaveOffset, _keyFifths);
        (dstep, dalter, doctave) = ApplyWrittenTransforms(dstep, dalter, doctave);
        var (tupletActual, tupletNormal) = CurrentTupletRatio();

        for (int k = 0; k < parts.Count; k++)
        {
            bool first = k == 0, last = k == parts.Count - 1;
            var duration = Fraction.FromNoteValue(parts[k].Value).Dotted(parts[k].Dots);
            int durationTicks = FractionToTicks(duration);
            var (type, dots) = GetNoteType(duration);
            var xmlNote = new MusicXmlNote
            {
                Step = "CDEFGAB"[dstep].ToString(),
                Alter = dalter,
                Octave = doctave,
                Duration = durationTicks,
                Type = type,
                Dots = dots,
                ActualNotes = tupletActual,
                NormalNotes = tupletNormal,
                SourcePosition = degree.SourceStart,   // the page's EmitArpeggioDegree item's
            };
            if (groupString is { } s)
                xmlNote.Technicals.Add(new System.Xml.Linq.XElement("string", s));
            if (first && slurStart) xmlNote.SlurStart = true;
            if (last && slurEnd) xmlNote.SlurStop = true;
            CloseTies([xmlNote]);
            if (!last) OpenTies([xmlNote]);
            _currentMeasure.Notes.Add(xmlNote);
            _lastPitchedNote = xmlNote;
            _lastEmittedNotes.Clear();
            _lastEmittedNotes.Add(xmlNote);
            MaybeClosePickup(duration);
        }
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
        _lastEmittedHost = slash.SourceStart;

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
        ProcessArticulations(slash.Articulations, xmlNote, host: slash.SourceStart);
        // A slash ties to the next slash exactly as a note ties to the next note (`/8~ | /4`,
        // the page's CreateSlashNoteItem reads the same `~`). ⚠️ Until 2026-09-29 (HANDOFF
        // §1.1 第662 ⑻) this arm paired no tie at all: the `~` on a slash wrote a start with
        // no stop, and once starts were retracted for want of a stop (2026-09-28) the tie
        // vanished from the document altogether. The pairing is SameNotehead's — an unpitched
        // slash joins only an unpitched slash.
        CloseTies([xmlNote]);
        if (slash.Articulations.OfType<TieSyntax>().Any()) OpenTies([xmlNote]);
        _currentMeasure.Notes.Add(xmlNote);
        _lastPitchedNote = null;
        // The onset just written, so a `~` walked as a SIBLING after the slash
        // (ApplyMarkerToLastEmitted) opens its tie here rather than on nothing.
        _lastEmittedNotes.Clear();
        _lastEmittedNotes.Add(xmlNote);
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
        _lastEmittedHost = bare.SourceStart;

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
                ProcessArticulations(bare.Articulations, xmlNote, host: bare.SourceStart);
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
                // A run that reached here through a `q'` repeats the chord where that
                // q left it, the same as a following q would.
                int bareDisplacement = Music.BareDurations.DisplacementOf(bare);
                bool isFirst = true;
                foreach (var m in members)
                {
                    var xmlNote = new MusicXmlNote
                    {
                        Step = m.Step,
                        Alter = m.Alter,
                        Octave = m.Octave + bareDisplacement,
                        Duration = durationTicks,
                        Type = type,
                        Dots = dots,
                        IsChord = !isFirst,
                        ActualNotes = tupletActual,
                        NormalNotes = tupletNormal
                    };
                    if (isFirst)
                    {
                        ProcessArticulations(bare.Articulations, xmlNote, host: bare.SourceStart);
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
                var drumNote = new MusicXmlNote
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
                AddDrumMark(drumNote, info);
                _currentMeasure.Notes.Add(drumNote);
                _lastPitchedNote = null;
                _lastEmittedNotes.Clear();
                MaybeClosePickup(duration);
                return;
            }
            case SlashNoteSyntax:
            {
                EmitPendingDynamic();
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
                // The repeated slash ties as ProcessSlashNote's does (`/2~ | 4`): the copy
                // ends the tie the slash opened, and its own `~` opens the next.
                ProcessArticulations(bare.Articulations, xmlNote, host: bare.SourceStart);
                CloseTies([xmlNote]);
                if (bare.Articulations.OfType<TieSyntax>().Any()) OpenTies([xmlNote]);
                _currentMeasure.Notes.Add(xmlNote);
                _lastPitchedNote = null;
                _lastEmittedNotes.Clear();
                _lastEmittedNotes.Add(xmlNote);
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
                // Slur follows a note — mark start/stop on the last note. Read off the SAME
                // record as the tie arm above: the notes just emitted, which outlive the
                // measure they were written into. Reading the current measure's last note
                // instead lost every slur opened on a pickup note — MaybeClosePickup closes
                // the pickup the moment its length is filled, BEFORE the `(` after the note
                // is walked, so the marker met an empty measure (MEASURED 2026-09-17 on
                // `partial 4  g'4( | c'4 d' e' f') |`: no slur start, one slur stop; the bass
                // corpus writes it under a header pickup, `dis,4( | d2.)`).
                var slurred = _lastEmittedNotes.Count > 0 ? _lastEmittedNotes[^1]
                    : _currentMeasure is { Notes.Count: > 0 } m ? m.Notes[^1]
                    : null;
                if (slurred != null && !RefusedSlur(_lastEmittedHost, close: !slur.IsOpen))
                {
                    if (slur.IsOpen)
                        slurred.SlurStart = true;
                    else
                        slurred.SlurStop = true;
                }
                break;
        }
    }

    /// <summary>
    /// Where ONE member of a chord sounds, and the frame it leaves behind.
    /// </summary>
    /// <remarks>
    /// THE one spelling of the chord's octave rule for this exporter: <c>ProcessChord</c>
    /// reads it for the main stream and <c>ProcessGraceNotes</c> for a chord inside a
    /// <c>grace { }</c> body (session 308). It was inline in the first when the second was
    /// written, and copying it would have made a fifth answer to a question this repository
    /// has already had to unify four times (see <c>ProcessGraceNotes</c>' remark on the grace
    /// group's own duration memory).
    /// <para>
    /// The first member is the ROOT: its bare LETTER is the chord's ANCHOR; every other member
    /// STACKS above the anchor — the same octave placement as a scale degree, so the chord's
    /// pitches are independent of the written order (<c>&lt;c e g&gt;</c> ==
    /// <c>&lt;c 3 5&gt;</c> == <c>&lt;c g e&gt;</c>). Each member's own <c>'</c>/<c>,</c> marks
    /// (the root's included) are LOCAL to that one note. A deliberate Lily# divergence from
    /// LilyPond, matching <c>MidiExporter</c> and <c>MeasureCollector</c>.
    /// </para>
    /// <para>
    /// ⚠️ THE CALLER DECIDES THE FRAME THE CHORD LEAVES. This advances the running state to
    /// the root's anchor while the members are placed; the caller then sets the frame to the
    /// chord's anchor (user decision 2026-09-27, <c>MeasureCollector.CreateChordItem</c>; the
    /// twin does the same). Session 394 changed the page and the twin and left this exporter
    /// and the MIDI one on the old rule for a day — four readers of one sentence, again.
    /// </para>
    /// </remarks>
    private (string Step, int Alter, int Octave) ResolveChordMemberPitch(
        PitchSyntax pitch, bool isFirst, int chordOctave,
        ref int firstStep, ref int firstOctave)
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
        return ApplyTranspose(pitch, step, alter, targetOctave);
    }

    /// <summary>A <c>chord(…)</c> item's strings on the part's tuning (Music.ShapeChords — the
    /// page's reading); the capo raises the strings (2026-09-29).</summary>
    private System.Collections.Immutable.ImmutableArray<Music.ShapeNote> ShapeNotesOf(ChordSyntax chord)
        // A spread note of a << >> group (ArpeggioSpread) is the item narrowed to that one note.
        => _spreadNote is { } spread && ReferenceEquals(spread.Item, chord.Green)
            ? [spread.Note]
            : Music.ShapeChords.Notes(chord, _partFrettedTuning ?? TuningType.Guitar,
                _partTransposeSemitones - DiagramCapo, _keyFifths);

    /// <summary>The <c>chord(…)</c> member of a <c>&lt;&lt; &gt;&gt;</c> group being written one
    /// note at a time and the note it writes now (the page's MeasureCollector._spreadNote).</summary>
    private (Syntax.InternalSyntax.GreenNode Item, Music.ShapeNote Note)? _spreadNote;

    /// <summary>A phrase's outgoing anchor when its body opens with a <c>chord(…)</c> item
    /// (<see cref="Music.PhraseAnchor.Shape"/>): the item's lowest note as written, as
    /// (step, octave) — the page's MeasureCollector.EnterPhraseTranspose; null when the part's
    /// tuning has no shape for it.</summary>
    private (int Step, int Octave)? ShapeAnchorOf(ChordSyntax? shape)
        => shape != null && Music.ShapeChords.Lowest(ShapeNotesOf(shape)) is { } low ? (low.Step, low.Octave) : null;

    private void ProcessChord(ChordSyntax chord, int extraOctave = 0)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;
        _lastEmittedHost = chord.SourceStart;

        var pitches = chord.Pitches.ToList();
        // chord(SYMBOL SHAPE): the shape's strings on the part's tuning (Music.ShapeChords —
        // the page's reading), each with its <technical><string>. No usable shape: a rest-
        // shaped silence keeps the time (the page's spacer, LYS1040).
        var shapeNotes = chord.IsShapeChord ? ShapeNotesOf(chord) : [];
        if (chord.IsShapeChord && shapeNotes.IsEmpty)
        {
            var spacerDuration = GetDuration(chord.Duration);
            var (spacerType, spacerDots) = GetNoteType(spacerDuration);
            _lastPitchedNote = null;
            _lastEmittedNotes.Clear();
            _currentMeasure.Notes.Add(new MusicXmlNote
            {
                IsRest = true,
                Duration = FractionToTicks(spacerDuration),
                Type = spacerType,
                Dots = spacerDots,
            });
            MaybeClosePickup(spacerDuration);
            return;
        }
        if (pitches.Count == 0 && !chord.Degrees.Any() && !chord.IsShapeChord)
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
        // included) are LOCAL to that one note. A deliberate Lily# divergence from
        // LilyPond, matching MidiExporter and MeasureCollector.
        // Octave marks after the closing '>' (<1 3 5>' / <c e g>,,) shift the whole
        // chord; folding it into firstOctave flows through every stacked/degree member,
        // matching MidiExporter and MeasureCollector. extraOctave is the enclosing
        // arpeggio's group shift when this chord is its root.
        int chordOctave = chord.ChordOctaveOffset + extraOctave;
        // THE CHORD WRITES THE FRAME (user decision, 2026-09-27): the note after the chord
        // is relative to its ANCHOR — MeasureCollector.CreateChordItem's frame update. The
        // incoming frame is kept for a chord that anchors nothing and for absolute mode.
        int frameStepIn = _currentStep, frameOctaveIn = _currentOctave;

        int firstStep = _currentStep, firstOctave = _currentOctave;
        var (tupletActual, tupletNormal) = CurrentTupletRatio();

        // String numbers OUTSIDE the brackets (<e dis'>\5\4) pair with the members in
        // written order: a member's own \N wins, each member without one takes the next
        // outside one, and the last outside one repeats once the list is exhausted —
        // the collector's rule (CreateChordItem), so <e dis'>\5\4 == <e\5 dis'\4>.
        // LILYPOND-REF: lily/articulations.cc:38-80 articulation_list — per note
        //   event, the note's own articulation wins; else articulation_events[j],
        //   j advancing only while more remain.
        List<int>? chordStrings = null;
        foreach (var a in chord.Articulations)
            if (a is StringNumberAnnotationSyntax sn)
                (chordStrings ??= new()).Add(sn.StringNumber);
        int nextChordString = 0;

        bool isFirst = true;
        foreach (var pitch in pitches)
        {
            var (step, alter, targetOctave) = ResolveChordMemberPitch(
                pitch, isFirst, chordOctave, ref firstStep, ref firstOctave);
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

            // The member's own <technical> marks: its fingering, and its string number
            // (own, else paired from the outside list).
            int? memberString = null;
            foreach (var a in pitch.Articulations)
            {
                if (a is MusicMarkSyntax mm && Semantics.AnnotationValues.Finger(mm) is { } finger)
                    xmlNote.Technicals.Add(new System.Xml.Linq.XElement("fingering", finger));
                else if (a is StringNumberAnnotationSyntax own)
                    memberString ??= own.StringNumber;
            }
            if (memberString is null && chordStrings != null)
            {
                memberString = chordStrings[nextChordString];
                if (nextChordString + 1 < chordStrings.Count)
                    nextChordString++;
            }
            if (memberString is { } memberStringNumber)
                xmlNote.Technicals.Add(new System.Xml.Linq.XElement("string", memberStringNumber));
            // A member-level half-tie ties its own head (LP `<d\laissezVibrer g>`); the
            // chord-level one is written on every member below.
            ApplyHalfTies(pitch.Articulations, xmlNote);
            // A slur mark written on this member (<c e( g>) is this note's <slur>, as the page
            // binds it to this head; the chord-level list below skips it.
            foreach (var (member, mark) in chord.MemberSlurs)
                if (member.SourceStart == pitch.SourceStart)
                {
                    if (mark.IsOpen)
                        xmlNote.SlurStart = !RefusedSlur(chord.SourceStart, close: false);
                    else
                        xmlNote.SlurStop = !RefusedSlur(chord.SourceStart, close: true);
                }

            // Add articulations + tie pairing only on the first note of the chord.
            if (isFirst)
            {
                bool hasArp = chord.Articulations.Any(a2 =>
                    a2 is ArticulationSyntax { Type: ArticulationType.None } na
                    && na.NameToken.Text.Equals("arpeggio", StringComparison.Ordinal));
                bool hasBracket = chord.Articulations.Any(a2 =>
                    a2 is MusicMarkSyntax mm
                    && Semantics.AnnotationValues.IsArpeggioBracket(mm));
                if (hasArp)
                    _chordArpeggio = "arpeggiate";
                else if (hasBracket)
                    _chordArpeggio = "non-arpeggiate";
                // The outside string numbers were paired above, member by member.
                var memberMarks = chord.MemberSlurs.Select(ms => ms.Mark.SourceStart).ToHashSet();
                ProcessArticulations(
                    chord.Articulations.Where(a => a is not StringNumberAnnotationSyntax
                        && !(a is SlurSyntax s && memberMarks.Contains(s.SourceStart))), xmlNote,
                    host: chord.SourceStart);
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

        // chord(SYMBOL SHAPE): lowest sounding first, each note's string in <technical>.
        foreach (var sn in Music.ShapeChords.Ascending(shapeNotes))
        {
            var (sstep, salter, soctave) = ApplyWrittenTransforms(sn.Step, sn.Alter, sn.Octave);
            string stepName = "CDEFGAB"[sstep].ToString();
            resolved.Add((stepName, salter, soctave));
            var xmlNote = new MusicXmlNote
            {
                Step = stepName,
                Alter = salter,
                Octave = soctave,
                Duration = durationTicks,
                Type = type,
                Dots = dots,
                IsChord = !isFirst,
                ActualNotes = tupletActual,
                NormalNotes = tupletNormal
            };
            xmlNote.Technicals.Add(new System.Xml.Linq.XElement("string", sn.StringNumber));
            if (isFirst)
            {
                if (chord.Articulations.Any(a2 => a2 is ArticulationSyntax { Type: ArticulationType.None } na
                        && na.NameToken.Text.Equals("arpeggio", StringComparison.Ordinal)))
                    _chordArpeggio = "arpeggiate";
                else if (chord.Articulations.Any(a2 => a2 is MusicMarkSyntax mm
                             && Semantics.AnnotationValues.IsArpeggioBracket(mm)))
                    _chordArpeggio = "non-arpeggiate";
                // Every note carries its own string: an outside \N has nothing left to pair with.
                ProcessArticulations(
                    chord.Articulations.Where(a => a is not StringNumberAnnotationSyntax), xmlNote);
                isFirst = false;
            }
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

        // A chord-level half-tie ties EVERY head (the page's SemiTiesOf); ProcessArticulations
        // wrote it on the first member only.
        foreach (var member in _chordMembers)
            ApplyHalfTies(chord.Articulations, member);

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

        // The next note is relative to the chord's ANCHOR (the root's bare letter or the
        // tonic, plus the whole-chord marks); a chord that anchors nothing, and absolute
        // mode, hand the incoming frame on shifted by the marks.
        bool anchored = !_octaveAbsolute && (pitches.Count > 0 || chord.Degrees.Any());
        _currentStep = anchored ? firstStep : frameStepIn;
        _currentOctave = anchored ? firstOctave : frameOctaveIn + chordOctave;
        // A chord from a shape hands on its lowest sounding note, as written (the page's rule).
        if (Music.ShapeChords.Lowest(shapeNotes) is { } low && !_octaveAbsolute)
        {
            _currentStep = low.Step;
            _currentOctave = low.Octave;
        }
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
        _lastEmittedHost = rep.SourceStart;

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
        // q' repeats the chord an octave up, accumulated along the q chain. MusicXML
        // spells the octave as a number, so the displacement is simply added to it —
        // the step and the alter do not move, which is why the spelling is preserved.
        int displacement = ChordRepetitions.DisplacementOf(rep);
        bool isFirst = true;
        foreach (var m in members)
        {
            var xmlNote = new MusicXmlNote
            {
                Step = m.Step,
                Alter = m.Alter,
                Octave = m.Octave + displacement,
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
                ProcessArticulations(rep.Articulations, xmlNote, host: rep.SourceStart);
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
        => a.IsUnpitched || b.IsUnpitched
            // The one unpitched head that ties is the slash (ProcessSlashNote): two slashes
            // are the same head; a slash and a pitched B4 — the slash's display place — are
            // not, and a drum member never is.
            ? a.IsUnpitched && b.IsUnpitched && a.Notehead == "slash" && b.Notehead == "slash"
            : a.Step != null && b.Step != null && a.Step == b.Step && a.Octave == b.Octave
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

        // A bare `R` lasts its bar (Music.BarRest) and leaves the running duration alone. A bar
        // no single note value spells (5/4) writes no <type>, as a whole-measure rest may.
        bool bare = Music.BarRest.IsBare(rest);
        var duration = bare ? _bars.BarLength : GetDuration(rest.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = bare && Music.BarRest.Shape(duration).Scale != 1
            ? ((string?)null, 0) : GetNoteType(duration);
        int bars = rest.MeasureCount;
        // A rest inside a tuplet plays its share like a note (the pitched rest above stamps
        // the same): without the ratio a reader sees a plain eighth where a triplet eighth
        // stands, and a bracket opening on the rest has no ratio to open with.
        var (tupletActual, tupletNormal) = CurrentTupletRatio();

        var xmlNote = new MusicXmlNote
        {
            IsRest = true,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            ActualNotes = tupletActual,
            NormalNotes = tupletNormal,
            // `R1*N` is N whole-measure rests (<rest measure="yes"/>), one a bar; a plain
            // `r1` is a rest at beat one, and the two are told apart by every reader.
            // `R1` alone is a whole-measure rest as well — it was written as `r1` until
            // 2026-09-30, so it came back from an import as one.
            IsMeasureRest = rest.RestText == "R",
            // A spacer holds its time and prints nothing: MusicXML has no spacer, so it is a
            // rest that is not printed. Until 2026-09-29 (HANDOFF §1.1 第662 ⑷) it was an
            // ordinary <rest/> — a rest drawn where the page draws nothing.
            PrintObject = rest.RestText != "s",
        };

        // The rest's post-events are read by the reader every note's are, so `r2@fermata`,
        // `R1*4@p`, `r1@chord(C x32010)` and `s1@chord(G)` all reach the document: the
        // <harmony> (with its <frame>) stands before the rest, at its moment (the page draws
        // it there since 2026-09-28 — owner's decision: a chord symbol belongs to the beat);
        // the fermata is the rest's <notations>; the dynamic a <direction> at its onset.
        // ⚠️ Until 2026-09-29 only the chord family was read here (第662 ⑷): the fermata and
        // the dynamic on a rest were dropped in silence while the page drew them.
        ProcessArticulations(rest.Articulations, xmlNote);

        _currentMeasure.Notes.Add(xmlNote);
        MaybeClosePickup(duration);
        if (bars <= 1)
            return;

        // LILYPOND-REF: lily/parser.yy:3117-3120 MULTI_MEASURE_REST — R<dur>*N is ONE event
        // spanning N measures. MusicXML says the same with <measure-style><multiple-rest>N
        // on the first of the N measures, and a whole-measure rest in each; the reader
        // (MeasureCollector.MusicWalk's RestSyntax arm) expands the same N. Until 2026-09-29
        // this arm wrote ONE rest for the N bars — a 17-bar part came out 13 (第662 ⑷) — and
        // the bar count no longer matched the page, the MIDI or the LilyPond twin.
        // The measures between the copies are closed here: the written bar line that
        // follows the rest closes only the LAST of them (the BarlineSyntax arm).
        if (_currentPart == null || _currentMeasure == null)
            return;
        var first = _currentMeasure.Notes.Count > 0 && _currentMeasure.Notes[^1] == xmlNote
            ? _currentMeasure
            : _currentPart.Measures.Count > 0 ? _currentPart.Measures[^1] : null;
        if (first != null)
        {
            // A mid-piece measure carries no <attributes> unless something changed; the
            // multiple-rest is such a change, and a change block repeats no <divisions>.
            first.Attributes ??= new MusicXmlAttributes { Divisions = null };
            first.Attributes.MultipleRest = bars;
        }
        // The bars after a bare `R*N`'s first are whole bars of the meter, a pickup's or not.
        var meter = _bars.MeterLength;
        if (bare && duration != meter)
        {
            duration = meter;
            durationTicks = FractionToTicks(duration);
            (type, dots) = Music.BarRest.Shape(duration).Scale != 1 ? ((string?)null, 0) : GetNoteType(duration);
        }
        for (int i = 1; i < bars; i++)
        {
            if (_currentMeasure.Notes.Count > 0)
            {
                _currentPart.Measures.Add(_currentMeasure);
                StartNewMeasure();
            }
            _currentMeasure!.Notes.Add(new MusicXmlNote
            {
                IsRest = true,
                IsMeasureRest = true,
                PrintObject = xmlNote.PrintObject,
                Duration = durationTicks,
                Type = type,
                Dots = dots,
            });
            MaybeClosePickup(duration);
        }
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
        // ⚠️ THIS WALKER'S MAIN STREAM HAS NO SUCH BUDGET either — see the same remark on
        // MidiExporter.ProcessGrace. MEASURED 2026-08-30 (scratch/p301/budget): on a doubling
        // phrase DAG the page truncates at the budget and says so (LYS1033) while this
        // exporter writes all 1,048,576 <note> elements, 192 MB, from 26 lines of source —
        // by decision (2026-09-08) the budget is the page's alone, and LYS1033 names this.
        int expansionBudget = Svg.Collector.MeasureCollector.DefaultExpansionBudgetCap;
        // What a phrase reference borrows and must give back — only what THIS reader reads.
        // ⚠️ ALLOCATED ONLY IF A REFERENCE IS ACTUALLY WRITTEN: a grace body naming a phrase
        // is rare (2 books in the whole 1754-book sweep) and this runs once per grace.
        Stack<((int step, int alt, int oct)? Transpose, int Anchor,
            int? AnchorStep, int Offset, (int Step, int Octave)? ShapeAnchor)>? phraseFrames = null;
        // The one slur a grace group carries: a `(` on its LAST element, closed on the main
        // note (`grace { d16( } e4)`) — the page engraves no other (LYS4020). A `(` followed
        // by another grace element is not that slur, so the next element clears it. Until
        // 2026-09-30 the `(` was not read here at all: the XML carried the main note's stop
        // with no start, and the import came back with a stray `)`.
        MusicXmlNote? lastGrace = null, graceSlurOn = null;

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
                        : reset.AnchorStep == Music.PhraseAnchor.Shape ? null
                        : reset.AnchorStep;
                    var shapeAnchor = reset.AnchorStep == Music.PhraseAnchor.Shape
                        ? ShapeAnchorOf(reset.AnchorShape) : null;
                    (phraseFrames ??= new()).Push((_currentTranspose, _octaveAnchor,
                        anchorStep, reset.OctaveOffset, shapeAnchor));
                    // Section shift included, like the reference arm above.
                    _currentOctave = _partAnchorOctave + _sectionOctaveOffset + reset.OctaveOffset;
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
                    var (savedTranspose, savedAnchor, anchorStep, offset, shapeAnchor) = phraseFrames.Pop();
                    _currentTranspose = savedTranspose;
                    _octaveAnchor = savedAnchor;
                    // Hand-off at the phrase's ANCHOR — the reference is ONE item, the chord
                    // rule, so its interior never leaks into what follows.
                    if (shapeAnchor is { } sa)
                    {
                        _currentStep = sa.Step;
                        _currentOctave = sa.Octave;
                    }
                    else if (anchorStep is { } astep)
                    {
                        _currentStep = astep;
                        _currentOctave = RelativeOctave.Resolve(
                            0, _partAnchorOctave + offset, astep, 0);
                    }
                    break;
                }

                // A TUPLET IN A GRACE BODY IS A CONTAINER, AND THIS READER READS ITS RATIO
                // AS <time-modification> - the same stack the main stream's tuplet arm
                // pushes, read by the same CurrentTupletRatio(), so nothing is spelled twice.
                // MEASURED 2026-08-30 (session 302, scratch/p302/lp) on LilyPond's own \midi
                // (the WSL v2.27.3 binary, not the canonical 2.26.0 - see the note on
                // GraceTupletStartMarker; the ticks are qualitative and the MECHANISM is what
                // carries the weight): a grace body's tuplet scales the played length by
                // normal/actual exactly as one in the main stream does, so the ratio is a fact
                // about the music rather than about the page.
                // ⚠️ THE <notations><tuplet> BRACKET IS DELIBERATELY NOT WRITTEN, and this is
                // the one narrowing decision this arm makes. That element asks the reading
                // program to DRAW a bracket and a number, and those are exactly the two grobs
                // a grace column still cannot hold - GraceBodyValidator reports them as a
                // GraceDropKind.Bracket in the same book. Writing them here would put the two
                // readers that place grobs into disagreement and make the warning false for
                // one of them. The time is exported because MIDI exports it too; the ink is
                // not, because the page does not. ⇒ The day CollectGraceNotes learns the
                // bracket, this arm does what ProcessNode's TupletExpressionSyntax arm does:
                // remember _currentMeasure.Notes.Count at the open and, at the close, hang
                // TupletNotation("start") / ("stop") on the first and last note of that range.
                // (There is no shared helper to call: that arm is written inline, and pulling
                // it out is part of that trip rather than this one.) The drop kind goes with it.
                // ⚠️ THE SENTENCE ABOVE NAMED A HELPER THAT DOES NOT EXIST for one commit
                // (`StampTupletBracket`, session 302). A forward instruction is read by
                // somebody who cannot yet tell it from a real symbol, and no ratchet checks
                // C# names in prose the way LpReferenceCitationTests checks LilyPond ones.
                case Svg.Collector.GraceTupletStartMarker t:
                    _tupletStack.Push((t.Actual, t.Normal));
                    break;

                // ⚠️ UNGUARDED ON PURPOSE: the pair is emitted or omitted together
                // (GraceBodySupport.Expand pays for a whole entry or none of it), and since
                // _tupletStack is SHARED with the main stream a `Count > 0` guard could not
                // make an unpaired close safe anyway - it would pop an enclosing tuplet's
                // entry and report success. Checklist 7.7's "fallback that turns a bug green".
                case Svg.Collector.GraceTupletEndMarker:
                    _tupletStack.Pop();
                    break;

                case NoteSyntax note:
                {
                    var (step, alter) = ParsePitch(note.Pitch);
                    int targetOctave = ResolveRelativeOctave(note.Pitch);

                    if (note.Duration != null)
                        graceDuration = note.Duration.ToFraction();
                    var (type, _) = GetNoteType(graceDuration);
                    var (tupletActual, tupletNormal) = CurrentTupletRatio();

                    var xmlNote = new MusicXmlNote
                    {
                        IsGrace = true,
                        IsSlash = isAcciaccatura,
                        Step = step,
                        Alter = alter,
                        Octave = targetOctave,
                        Type = type,
                        ActualNotes = tupletActual,
                        NormalNotes = tupletNormal
                    };
                    // A grace's written \N, which the page frets it on (GraceHeadInfo.StringNumber);
                    // until 2026-09-30 only a grace CHORD's member carried it here.
                    foreach (var stringNumber in note.Articulations.OfType<StringNumberAnnotationSyntax>())
                        xmlNote.Technicals.Add(new System.Xml.Linq.XElement("string", stringNumber.StringNumber));

                    _currentMeasure.Notes.Add(xmlNote);
                    (lastGrace, graceSlurOn) = (xmlNote, null);
                    break;
                }

                case SlurSyntax { IsOpen: true }:
                    graceSlurOn = lastGrace;
                    break;

                // A CHORD IN A GRACE BODY IS ONE COLUMN WITH N HEADS (session 308), and this
                // reader writes it the way it writes any chord: one <note> per member, with
                // <chord/> on every member but the first. The octave rule is
                // ResolveChordMemberPitch, the SAME statement ProcessChord reads — a grace
                // chord's pitches must not depend on which walk found them.
                // ⚠️ THE DURATION IS THE GRACE GROUP'S, not the main stream's. That is this
                // walker's own memory (see the remark above and what sharing it once cost),
                // and a chord threads it exactly as a note does.
                case ChordSyntax chord when Semantics.GraceBodySupport.CarriedChord(chord) != null:
                {
                    if (chord.Duration != null)
                        graceDuration = chord.Duration.ToFraction();
                    var (chordType, _) = GetNoteType(graceDuration);
                    var (chordActual, chordNormal) = CurrentTupletRatio();
                    int graceChordAt = _currentMeasure.Notes.Count;
                    int chordOctave = chord.ChordOctaveOffset;
                    int frameStepIn = _currentStep, frameOctaveIn = _currentOctave;
                    int firstStep = _currentStep, firstOctave = _currentOctave;
                    bool firstMember = true;
                    foreach (var pitch in chord.Pitches)
                    {
                        var (cstep, calter, coctave) = ResolveChordMemberPitch(
                            pitch, firstMember, chordOctave, ref firstStep, ref firstOctave);
                        _currentMeasure.Notes.Add(new MusicXmlNote
                        {
                            IsGrace = true,
                            IsSlash = isAcciaccatura,
                            IsChord = !firstMember,
                            Step = cstep,
                            Alter = calter,
                            Octave = coctave,
                            Type = chordType,
                            ActualNotes = chordActual,
                            NormalNotes = chordNormal
                        });
                        firstMember = false;
                    }
                    // Scale-degree members (`grace { <d 3 5>16 }`): the same stacking the main
                    // stream's chord arm applies, so a degree chord written in a grace body
                    // sounds what the page draws.
                    if (chord.Pitches.Any() is false && chord.Degrees.Any())
                    {
                        int tonicStep = _ambientTonic.Valid ? _ambientTonic.Step : 0;
                        firstOctave = RelativeOctave.Resolve(
                            _currentStep, _currentOctave, tonicStep, 0) + chordOctave;
                        firstStep = tonicStep;
                        _currentStep = tonicStep;
                        _currentOctave = firstOctave;
                    }
                    foreach (var degree in chord.Degrees)
                    {
                        var (dstep, dalter, doctave) = ChordDegrees.Resolve(
                            firstStep, firstOctave, degree.Number, degree.Alteration,
                            degree.OctaveOffset, _keyFifths);
                        (dstep, dalter, doctave) = ApplyWrittenTransforms(dstep, dalter, doctave);
                        _currentMeasure.Notes.Add(new MusicXmlNote
                        {
                            IsGrace = true,
                            IsSlash = isAcciaccatura,
                            IsChord = !firstMember,
                            Step = "CDEFGAB"[dstep].ToString(),
                            Alter = dalter,
                            Octave = doctave,
                            Type = chordType,
                            ActualNotes = chordActual,
                            NormalNotes = chordNormal
                        });
                        firstMember = false;
                    }
                    // A chord(…) item in a grace body: its strings, as ProcessChord writes them.
                    var graceShape = chord.IsShapeChord ? ShapeNotesOf(chord) : [];
                    foreach (var sn in Music.ShapeChords.Ascending(graceShape))
                    {
                        var (sstep, salter, soctave) = ApplyWrittenTransforms(sn.Step, sn.Alter, sn.Octave);
                        var graceNote = new MusicXmlNote
                        {
                            IsGrace = true,
                            IsSlash = isAcciaccatura,
                            IsChord = !firstMember,
                            Step = "CDEFGAB"[sstep].ToString(),
                            Alter = salter,
                            Octave = soctave,
                            Type = chordType,
                            ActualNotes = chordActual,
                            NormalNotes = chordNormal
                        };
                        graceNote.Technicals.Add(new System.Xml.Linq.XElement("string", sn.StringNumber));
                        _currentMeasure.Notes.Add(graceNote);
                        firstMember = false;
                    }
                    // The chord's anchor is the next note's frame — ProcessChord's rule.
                    bool anchored = !_octaveAbsolute && (chord.Pitches.Any() || chord.Degrees.Any());
                    _currentStep = anchored ? firstStep : frameStepIn;
                    _currentOctave = anchored ? firstOctave : frameOctaveIn + chordOctave;
                    if (Music.ShapeChords.Lowest(graceShape) is { } graceLow && !_octaveAbsolute)
                    {
                        _currentStep = graceLow.Step;
                        _currentOctave = graceLow.Octave;
                    }
                    // The slur rides the chord's first <note>, as a main-stream chord's does.
                    if (_currentMeasure.Notes.Count > graceChordAt)
                        (lastGrace, graceSlurOn) = (_currentMeasure.Notes[graceChordAt], null);
                    break;
                }

                // A REST IN A GRACE BODY IS A COLUMN WITH NO HEAD (session 308), and this
                // reader writes it as a grace rest. It has SOUNDED since 2026-07-10 (the MIDI
                // walker gives it grace time and emits no note) and the page draws it now, so
                // this was the last of the four still dropping it.
                // ⚠️ A SPACER (`s`) IS STILL WRITTEN, as `<rest/>` with no ink implied: this
                // format has no separate spacer, and the page's own answer - hold the column,
                // draw nothing - has no MusicXML spelling either. The alternative is to omit
                // it, which would make the grace group's written rhythm wrong.
                case RestSyntax rest when Semantics.GraceBodySupport.CarriedRest(rest) != null:
                {
                    if (rest.Duration != null)
                        graceDuration = rest.Duration.ToFraction();
                    var (restType, _) = GetNoteType(graceDuration);
                    var (restActual, restNormal) = CurrentTupletRatio();
                    var graceRest = new MusicXmlNote
                    {
                        IsGrace = true,
                        IsSlash = isAcciaccatura,
                        IsRest = true,
                        Type = restType,
                        ActualNotes = restActual,
                        NormalNotes = restNormal
                    };
                    _currentMeasure.Notes.Add(graceRest);
                    (lastGrace, graceSlurOn) = (graceRest, null);
                    break;
                }
            }
        }
        if (graceSlurOn != null)
            graceSlurOn.SlurStart = true;
    }

    /// <param name="host">The source position of the note, chord or slash the marks ride —
    /// what the carry rule's refusals of its slurs are keyed on (<see cref="RefusedSlur"/>);
    /// -1 where none can be refused.</param>
    private void ProcessArticulations(IEnumerable<SyntaxNode> articulations, MusicXmlNote xmlNote, int host = -1)
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

        ApplyHalfTies(articulations, xmlNote);

        foreach (var artic in articulations)
        {
            if (artic is ArticulationSyntax articulation)
            {
                // Single-word direction marks (@sustain, @sostenuto, @ottava, @loco)
                // parse as name-only articulations, not compound marks.
                if (articulation.Type == ArticulationType.None)
                    ProcessDirectionName(articulation.NameToken.Text);
                // MusicXML has no phrasing-slur element: a phrasing slur is a <slur> with a
                // number of its own, so it can overlap the ordinary slurs (number 1).
                if (Semantics.AnnotationValues.IsPhrasingSlurName(articulation.NameToken.Text)
                    && !RefusedSlur(articulation.SourceStart, close: false))
                {
                    var start = PhrasingSlurNotation("start");
                    if (articulation.ForcedAbove is { } above)
                        start.Add(new System.Xml.Linq.XAttribute("placement", above ? "above" : "below"));
                    xmlNote.ExtraNotations.Add(start);
                }

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
                HandleDynamicText(dynamic.DynamicToken.Text, dynamic.SourceStart);
            }
            else if (artic is StringNumberAnnotationSyntax stringNumber)
            {
                // A written \N is the note's <technical><string>. Until 2026-09-08 this
                // reader had no arm for the node, so no note carried a string — the
                // page and the twin had read the same annotation since 2026-08-09.
                xmlNote.Technicals.Add(new System.Xml.Linq.XElement("string", stringNumber.StringNumber));
            }
            else if (artic is MusicMarkSyntax mark)
            {
                // @finger(N) is the note's <technical><fingering>; the direction-family
                // marks (pedal, ottava, chord symbol) go on to their own reader.
                if (Semantics.AnnotationValues.Finger(mark) is { } finger)
                    xmlNote.Technicals.Add(new System.Xml.Linq.XElement("fingering", finger));
                if (mark.IsSpanEnd && Semantics.AnnotationValues.IsPhrasingSlurName(mark.Name)
                    && !RefusedSlur(mark.SourceStart, close: true))
                    xmlNote.ExtraNotations.Add(PhrasingSlurNotation("stop"));
                ProcessDirectionMark(mark);
            }
            else if (artic is SlurSyntax slur)
            {
                // A slur the carry rule refuses in this play is not written (the page draws
                // none, LYS4023) — its open, or its close.
                if (slur.IsOpen)
                    xmlNote.SlurStart = !RefusedSlur(host, close: false);
                else
                    xmlNote.SlurStop = !RefusedSlur(host, close: true);
            }
        }
    }

    /// <summary>
    /// The half-ties written on a note or chord member: <c>@laissezVibrer</c> is
    /// <c>&lt;tied type="let-ring"/&gt;</c>, <c>@repeatTie</c> a tie STOP with no start before
    /// it on the page — the spelling this exporter already writes for the repeat tie a tie
    /// carried back over a repeat sign draws (FinishCarriedTies' remark). Idempotent, so a
    /// chord-level and a member-level annotation on the same head write it once.
    /// </summary>
    /// <remarks>
    /// Until 2026-09-28 neither annotation reached the MusicXML at all (every part and voice);
    /// the page and the LilyPond twin (<c>\laissezVibrer</c> / <c>\repeatTie</c>) had them.
    /// MusicXML 4.0 tied-type: start / stop / continue / let-ring.
    /// </remarks>
    private static void ApplyHalfTies(IEnumerable<SyntaxNode> articulations, MusicXmlNote xmlNote)
    {
        foreach (var a in articulations)
        {
            if (a is not ArticulationSyntax { Type: ArticulationType.None } named)
                continue;
            switch (named.NameToken.Text)
            {
                case "laissezVibrer":
                    LetRing(xmlNote);
                    break;
                case "repeatTie":
                    xmlNote.TieStop = true;
                    break;
            }
        }
    }

    /// <summary>The laissez-vibrer half-tie, <c>&lt;tied type="let-ring"/&gt;</c>, once —
    /// written for <c>@laissezVibrer</c> and for the hanging tie a carried tie leaves
    /// (FinishCarriedTies).</summary>
    private static void LetRing(MusicXmlNote xmlNote)
    {
        if (!xmlNote.ExtraNotations.Any(e => e.Name.LocalName == "tied"
                && (string?)e.Attribute("type") == "let-ring"))
            xmlNote.ExtraNotations.Add(new System.Xml.Linq.XElement("tied",
                new System.Xml.Linq.XAttribute("type", "let-ring")));
    }

    /// <summary>A phrasing slur's <c>&lt;slur&gt;</c> end: number 2, the ordinary slurs
    /// being number 1 (MusicXmlNote), so the two may overlap as they do on the page.</summary>
    private static System.Xml.Linq.XElement PhrasingSlurNotation(string type)
        => new("slur", new System.Xml.Linq.XAttribute("type", type),
            new System.Xml.Linq.XAttribute("number", 2));

    /// <summary>Whether a crescendo/diminuendo wedge is open (closed by the
    /// next level dynamic).</summary>
    private bool _wedgeOpen;

    /// <summary>A dynamic word: cresc/decresc/dim OPEN a &lt;wedge&gt; (they
    /// used to leak into &lt;dynamics&gt; as invalid &lt;cresc/&gt;); a level
    /// mark closes any open wedge, then emits as a dynamics direction.</summary>
    private void HandleDynamicText(string text, int position = -1)
    {
        if (text == "niente")
        {
            HandleNiente();
            return;
        }
        if (text is "cresc" or "decresc" or "dim")
        {
            var wedge = new MusicXmlDirection
            {
                WedgeType = text == "cresc" ? "crescendo" : "diminuendo",
                Placement = "below",
            };
            // A crescendo from a niente on the same note grows from nothing: the circle is the
            // wedge's own niente="yes", and the <n/> written for it goes.
            if (text == "cresc" && _niente is { } n && n.Measure == _currentMeasure
                && n.Offset == CurrentMeasurePosition(_currentMeasure!))
            {
                wedge.WedgeNiente = true;
                if (n.Word != null)
                    _currentMeasure!.Directions.Remove(n.Word);
                _niente = null;
            }
            AddDirection(wedge);
            _openWedge = wedge;
            _wedgeOpen = true;
            // The rule cuts this one at its section's end (CloseCutWedge), whatever dynamic
            // follows in the next section.
            _wedgeCut = position >= 0 && _cutHairpins.Contains((position, _xmlPlaySerial - 1));
            return;
        }
        if (_wedgeOpen)
        {
            AddDirection(new MusicXmlDirection
            {
                WedgeType = "stop",
                Placement = "below",
            });
            _wedgeOpen = false;
        }
        // Written where it stands — its note's onset (a note's marks are read before the note
        // is added) — rather than held for the next note: a dynamic on a bar's last note used
        // to be written at the next bar's head, and one on a part's last note never (2026-09-29).
        AddDirection(new MusicXmlDirection { DynamicType = text, Placement = "below" });
    }

    /// <summary>The open wedge's start direction, while <see cref="_wedgeOpen"/>.</summary>
    private MusicXmlDirection? _openWedge;

    /// <summary>The last niente seen — its measure and offset, and the <c>&lt;n/&gt;</c> written
    /// for it if one was — so a crescendo starting on the same note takes it as its circle.</summary>
    private (MusicXmlMeasure Measure, int Offset, MusicXmlDirection? Word)? _niente;

    /// <summary>
    /// A <c>@niente</c>, as the page reads it (HairpinEngraver.DetectHairpins): it ends an open
    /// wedge — a diminuendo AL NIENTE, whose stop carries <c>niente="yes"</c> and stands for the
    /// niente; a crescendo just started on this same note grows FROM it (dal niente, the
    /// start's <c>niente="yes"</c>); otherwise it is the dynamic <c>&lt;n/&gt;</c>.
    /// </summary>
    private void HandleNiente()
    {
        if (_currentMeasure == null)
            return;
        int offset = CurrentMeasurePosition(_currentMeasure);
        if (_wedgeOpen && _openWedge is { WedgeType: "crescendo" } started
            && _currentMeasure.Directions.Contains(started) && started.Offset == offset)
        {
            started.WedgeNiente = true;
            return;
        }
        bool consumed = false;
        if (_wedgeOpen)
        {
            bool alNiente = _openWedge?.WedgeType == "diminuendo";
            AddDirection(new MusicXmlDirection { WedgeType = "stop", Placement = "below", WedgeNiente = alNiente });
            _wedgeOpen = false;
            _openWedge = null;
            consumed = alNiente;
        }
        MusicXmlDirection? word = null;
        if (!consumed)
        {
            word = new MusicXmlDirection { DynamicType = "n", Placement = "below" };
            AddDirection(word);
        }
        _niente = (_currentMeasure, offset, word);
    }

    /// <summary>
    /// Adds a direction to the current measure at the position the stream has reached — the
    /// onset of the note about to be written, since a note's marks are read before it is
    /// added (<see cref="ProcessArticulations"/> precedes every <c>Notes.Add</c>). The measure
    /// writes its directions at its head with this as their <c>&lt;offset&gt;</c>
    /// (<see cref="MusicXmlDirection.Offset"/>): until 2026-09-29 every direction of a bar
    /// stood at its first beat (HANDOFF §1.1 第662 ⑹).
    /// </summary>
    private void AddDirection(MusicXmlDirection direction)
    {
        if (_currentMeasure == null)
            return;
        direction.Offset = CurrentMeasurePosition(_currentMeasure);
        _currentMeasure.Directions.Add(direction);
    }

    /// <summary>The position the measure's note stream has reached, in divisions from the
    /// bar's head: the durations of its notes and rests, chord members and grace notes not
    /// counted, a backup subtracted.</summary>
    private static int CurrentMeasurePosition(MusicXmlMeasure measure)
    {
        int position = 0;
        foreach (var n in measure.Notes)
        {
            if (n.RawElement != null || n.IsGrace || n.IsChord)
                continue;
            position += n.IsBackup ? -n.Duration : n.Duration;
        }
        return position;
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
        // ⚠️ ChordSpelling.Canonical, deliberately: a <harmony> carries the chord as DATA and
        // BuildHarmony reads Lily#'s CANONICAL symbol back to build it, so a score that wrote
        // nothing at all — the default is LilyPond's symbols since 2026-09-12 — must not hand
        // this a "C°", which spells no
        // quality the parser knows. The rule `sectionLabels` and `partCombineText` keep: a
        // display switch moves the page, never MIDI and never MusicXML.
        // ⚠️ Only the TEXT is taken: a <harmony> has no typography to carry, so the raised
        // run the symbol would print with on the page (ChordSymbolText.SuperFrom) is the
        // page's and stops here, exactly as the spelling does.
        // ★ The chord DIAGRAM (owner's decisions 2026-09-28): the shape the page draws — one
        // WRITTEN for the tuning, or in a `chordDiagrams … all` score the default (DiagramTuning:
        // the layout's, else the part's instrument, else the guitar; none under `chordDiagrams
        // none`), ChordAnnotation.Drawn — nested in
        // the <harmony> as its <frame>. A shape with no symbol
        // (`@chord(x32010)`) is named from its notes as the page names it; if they name
        // nothing there is no <harmony> for the <frame> to live in (MusicXML has no
        // free-standing frame) and nothing is written.
        if (_currentMeasure != null
            && LilySharp.Core.Semantics.ChordAnnotation.Of(mark) is { } words)
        {
            var diagramTuning = DiagramTuning();
            LilySharp.Core.Music.ChordStructure? derived = null;
            string? chordText =
                LilySharp.Core.Semantics.AnnotationValues.Chord(
                    mark, LilySharp.Core.Semantics.ChordSpelling.Canonical, out _)
                    is { Text.Length: > 0 } chordSymbol
                    ? chordSymbol.Text
                    : words.NamingShape(diagramTuning, _partTuning) is { } naming
                      && (derived = LilySharp.Core.Semantics.ChordAnnotation.NameFromFrets(
                          naming.Frets, naming.Tuning, _keyFifths)) != null
                        ? derived.PrintedSymbol(LilySharp.Core.Semantics.ChordSpelling.Canonical).Text
                        : null;
            // In a `chordDiagrams … all` score every name draws: the written shape, else the
            // default of its chord (a symbol-less shape's, the one its frets name); a chord the
            // layout's shape table lists draws the table's shape (per the section the mark is in).
            var diagram = diagramTuning is { } dt
                ? words.Drawn(dt, DiagramsAll, derived, DiagramTable,
                    DiagramTable != null ? Semantics.ChordDiagramScores.SectionNameOf(mark) : null, DiagramCapo)
                : null;
            if (chordText != null)
            {
                if (BuildHarmony(chordText) is { } harmony)
                {
                    // The @chord's own diagram, else a @diagram on the same note — both nest
                    // inside the harmony (MusicXML <frame> is a harmony child).
                    string? fspec = diagram != null
                        ? diagram.FrameSpec
                        : _noteFrameSpec;
                    if (fspec != null && BuildFrame(fspec) is { } frameEl)
                        harmony.Add(frameEl);
                    _currentMeasure.Notes.Add(new MusicXmlNote { RawElement = harmony });
                }
                return;
            }
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

        // Free text and the rehearsal mark ON A NOTE (2026-09-29, HANDOFF §1.1 第672's hole:
        // both fell through to the one-word table below, whose names they are not, and
        // were dropped in silence). Each is read from the ANNOTATION like the chord and the
        // figures above, by the same reader the page draws from
        // (MeasureCollector.Annotations: `@text` is a TextScript, below unless `.up`;
        // `@mark` a RehearsalMark, above). A direction at the note's own offset, as the
        // dynamics are. On a multi-measure rest the text stands on the run's first bar,
        // which is the bar the page prints it in.
        if (_currentMeasure != null
            && LilySharp.Core.Semantics.AnnotationValues.Text(mark) is { } freeText)
        {
            AddDirection(new MusicXmlDirection
            {
                Words = freeText,
                Placement = mark.ForcedAbove == true ? "above" : "below",
            });
            return;
        }
        if (_currentMeasure != null
            && LilySharp.Core.Semantics.AnnotationValues.Rehearsal(mark, out _) is { } rehearsal)
        {
            AddDirection(new MusicXmlDirection { Rehearsal = rehearsal });
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
                        AddDirection(new MusicXmlDirection { OctaveShiftType = "stop", OctaveShiftSize = _octaveShiftSize });
                        break;
                    // MusicXML has ONE pedal stop for the sustain and the sostenuto.
                    case Svg.Model.MusicMarkType.SustainOff:
                    case Svg.Model.MusicMarkType.SostenutoOff:
                        AddDirection(new MusicXmlDirection { PedalType = "stop", Placement = "below" });
                        break;
                    // The una corda is no MusicXML pedal: its release is the words a score
                    // prints, as its start is (ProcessDirectionName; 2026-09-29 — it used to
                    // be a <pedal type="stop"> with no start before it).
                    case Svg.Model.MusicMarkType.UnaCordaOff:
                        AddDirection(new MusicXmlDirection { Words = "tre corde", Placement = "below" });
                        break;
                    // The text spanner has no stop spelling here — the silence '@rit' meets.
                }
            return;
        }

        ProcessDirectionName(mark.MarkName);
    }

    private void ProcessDirectionName(string name)
    {
        if (_currentMeasure == null) return;
        // Matched as written: names are case-sensitive (owner's decision 2026-09-27).
        switch (name)
        {
            case "sustain":
                AddDirection(new MusicXmlDirection { PedalType = "start", Placement = "below" });
                break;
            case "sostenuto":
                AddDirection(new MusicXmlDirection { PedalType = "sostenuto", Placement = "below" });
                break;
            // The una corda (2026-09-29, HANDOFF §1.1 第662 ⑴ — it wrote nothing): MusicXML
            // has no pedal type for it, so it is the words a score prints, and '@treCorde' —
            // its release written as a word rather than as '@!' — the same words as '@!unaCorda'.
            case "unaCorda":
                AddDirection(new MusicXmlDirection { Words = "una corda", Placement = "below" });
                break;
            case "treCorde":
                AddDirection(new MusicXmlDirection { Words = "tre corde", Placement = "below" });
                break;
            case "ottava":
                // 8va above: MusicXML octave-shift "down" (written an octave
                // below the sounding pitch).
                _octaveShiftSize = 8;
                AddDirection(new MusicXmlDirection { OctaveShiftType = "down" });
                break;
            case "ottava.bassa":
                _octaveShiftSize = 8;
                AddDirection(new MusicXmlDirection { OctaveShiftType = "up", Placement = "below" });
                break;
            // The quindicesima (2026-09-29, 第662 ⑴): the same line two octaves wide — size 15,
            // on its stop too.
            case "quindicesima":
                _octaveShiftSize = 15;
                AddDirection(new MusicXmlDirection { OctaveShiftType = "down", OctaveShiftSize = 15 });
                break;
            case "quindicesima.bassa":
                _octaveShiftSize = 15;
                AddDirection(new MusicXmlDirection { OctaveShiftType = "up", OctaveShiftSize = 15, Placement = "below" });
                break;
        }
    }

    /// <summary>The size of the octave line running (8, or 15 for a quindicesima): what its
    /// <c>@!ottava</c> stop is written with.</summary>
    private int _octaveShiftSize = 8;

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
    /// <remarks>
    /// The fret is read by the page's one reader (<c>FretFrameGeometry.FretAt</c>), so the
    /// frets 10–15 a chosen voicing can use (spelled a–f inside the spec) come out as numbers.
    /// <c>frame-frets</c> is the rows the page draws and <c>first-fret</c> the fret they start
    /// at when the shape is shifted (the page's "Nfr" label) — MusicXML's own spelling of both.
    /// A predefined shape's detail (the spec's suffix, <c>FretFrameGeometry.Detailed</c>) is
    /// data the frame carries whole: each fingered note's <c>&lt;fingering&gt;</c>, and a barre's
    /// <c>&lt;barre type="start"&gt;</c> on its lowest-pitched string (the highest string
    /// number) and <c>stop</c> on its highest — MusicXML's frame-note children in schema order.
    /// </remarks>
    private static System.Xml.Linq.XElement? BuildFrame(string spec)
    {
        int strings = Svg.Layout.FretFrameGeometry.Strings(spec);
        if (strings < 4) return null;
        var frame = new System.Xml.Linq.XElement("frame",
            new System.Xml.Linq.XElement("frame-strings", strings),
            new System.Xml.Linq.XElement("frame-frets", Svg.Layout.FretFrameGeometry.RowCount(spec)));
        if (Svg.Layout.FretFrameGeometry.BaseFret(spec) is > 1 and var firstFret)
            frame.Add(new System.Xml.Linq.XElement("first-fret", firstFret));
        var barres = Svg.Layout.FretFrameGeometry.Barres(spec);
        for (int i = 0; i < strings; i++)
        {
            int fret = Svg.Layout.FretFrameGeometry.FretAt(spec, i);
            if (fret < 0) continue;
            var note = new System.Xml.Linq.XElement("frame-note",
                new System.Xml.Linq.XElement("string", strings - i),
                new System.Xml.Linq.XElement("fret", fret));
            if (fret > 0 && Svg.Layout.FretFrameGeometry.FingerAt(spec, i) is > 0 and var finger)
                note.Add(new System.Xml.Linq.XElement("fingering", finger));
            foreach (var b in barres)
                if (b.From == i || b.To == i)
                    note.Add(new System.Xml.Linq.XElement("barre",
                        new System.Xml.Linq.XAttribute("type", b.From == i ? "start" : "stop")));
            frame.Add(note);
        }
        return frame;
    }

    // ---- chord rows as <harmony> (owner's decisions 2026-09-29, HANDOFF §2 K5 ①) ------------
    //
    // MusicXML has no chord-only part: a <harmony> lives in a part's <measure> at a musical
    // position. So a placed `chords NAME` row is written into the part of the staff DIRECTLY
    // UNDER it in the score's order (the next staff after the row, else the score's first —
    // the page's own rule, "a row directly above a staff aligns over it"), each symbol at its
    // slot's <offset> from the bar's head; a lead sheet with no staff at all gets a part of
    // whole rests named "ROW (chords)" (the MIDI's track name) to hold them. An @chord of the
    // SAME chord at the SAME moment is dropped (the page joins the two names on one line); a
    // different one stands beside the row's. A rest (N.C.) and a leading '.' write nothing,
    // as a rest carrying no symbol writes no harmony. Every written symbol is written again
    // (`C | C |` is two harmonies), as the MIDI strikes it again. The harmony is the SOUNDING
    // chord (a degree resolved in the key at its bar, a capo score's written name); the
    // <frame> is the shape the page draws under the symbol (ChordShapes.Drawn on the target
    // staff's tuning — pressed, under a capo), fingers and barre included.
    // LILYSHARP-OWN: LilyPond's ChordNames context is engraved and exports nothing; MusicXML's
    // own lead sheets carry their harmonies in the melody's part, which is what this mirrors.

    /// <summary>One placed chord row's bars in one printed play, with the section state its
    /// symbols are read in (meter for the slot grid, key for a degree, the pickup that cuts
    /// its first bar).</summary>
    private sealed record PendingChordRow(int Play, string Row, List<SyntaxNode> Items,
        int Beats, int BeatType, Fraction? Pickup, int TonicStep, int Sharps,
        (int Fifths, string Mode, string? Custom) Key);

    private readonly List<PendingChordRow> _pendingChordRows = new();
    /// <summary>The measure index a part's bars of a printed play begin at.</summary>
    private readonly Dictionary<(string Part, int Play), int> _playStartMeasure = new();
    private HashSet<string> _placedChordRows = new(StringComparer.Ordinal);
    private RenderSpec? _playedSpec;

    /// <summary>Remembers a placed row's bars of the section being emitted, with the section's
    /// meter, key and pickup — the state <see cref="EmitPartMusic"/> applies to its parts
    /// (the section's header, else the score's home).</summary>
    private void RememberChordRow(string row, IEnumerable<SyntaxNode> items)
    {
        int beats = _bars.Meter.Beats, beatType = _bars.Meter.BeatType;
        if (_sectionTime is { } st)
        {
            if (!st.IsSenzaMisura)
                (beats, beatType) = (st.Beats, st.BeatType);
        }
        else if (!_bars.HomeSenzaMisura)
            (beats, beatType) = (_bars.HomeMeter.Beats, _bars.HomeMeter.BeatType);
        var (fifths, mode, custom) = _homeKey ?? (_keyFifths, _keyMode, _keyCustomXml);
        var tonic = _homeTonic;
        if (_sectionKey is { } sk)
        {
            tonic = KeyTonic.Of(sk);
            if (sk.IsCustom)
                (fifths, custom) = (0, LilySharp.Core.Svg.Model.KeySignature.EncodeCustom(sk.CustomAlterations));
            else
            {
                mode = sk.Mode.Text.ToLowerInvariant();
                fifths = KeySpelling.SharpsFor(sk.Pitch?.ToFullString().Trim().ToLower() ?? "", mode) ?? 0;
                custom = null;
            }
        }
        _pendingChordRows.Add(new PendingChordRow(_xmlPlaySerial, row, items.ToList(), beats, beatType,
            _sectionPartial?.ToFraction(), tonic.Valid ? tonic.Step : 0, fifths, (fifths, mode, custom)));
    }

    /// <summary>A row's items split into bars, as the page and the MIDI split them
    /// (<c>MidiExporter.PlayChordRow</c>): every bar line closes a bar — a <c>|</c> opening the
    /// run closes an EMPTY one — except <c>|:</c>, which only closes what is pending.</summary>
    private static List<List<SyntaxNode>> RowBars(IReadOnlyList<SyntaxNode> items)
    {
        var bars = new List<List<SyntaxNode>>();
        var bar = new List<SyntaxNode>();
        foreach (var item in items)
        {
            if (item is BarlineSyntax barline)
            {
                if (Svg.Collector.MeasureCollector.ParseBarlineType(barline.BarText) != Svg.Model.BarlineType.RepeatStart
                    || bar.Count > 0)
                {
                    bars.Add(bar);
                    bar = new List<SyntaxNode>();
                }
                continue;
            }
            if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
                bar.Add(item);
        }
        if (bar.Count > 0)
            bars.Add(bar);
        return bars;
    }

    /// <summary>
    /// The part a row's harmonies go to: the part of a staff that names the row as its attached
    /// chords, else the first staff AFTER the row in the score's order, else the score's first
    /// staff; null when the score engraves no staff at all (<paramref name="scoreHasStaff"/>
    /// false: a lead sheet, whose row gets a part of its own).
    /// </summary>
    private string? RowTargetPart(string row, out bool scoreHasStaff)
    {
        scoreHasStaff = false;
        if (_playedSpec is not { } spec)
            return null;
        var items = spec.Items;
        int rowAt = -1;
        for (int i = 0; i < items.Length; i++)
            if (items[i] is ChordRowSpec r && r.PartName == row)
            {
                rowAt = i;
                break;
            }
        string? first = null, after = null;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is SingleStaffSpec { Staff.WithChords: { } attached } s && attached == row)
                return s.Staff.VoiceName;
            if (FirstEngravedPart(items[i]) is not { } name)
                continue;
            first ??= name;
            if (i > rowAt)
                after ??= name;
        }
        scoreHasStaff = first != null;
        return after ?? first;
    }

    /// <summary>The first part a score item engraves on a staff, or null (a row, an ossia).</summary>
    private static string? FirstEngravedPart(RenderItemSpec item) => item switch
    {
        SingleStaffSpec s => s.Staff.VoiceName,
        TabStaffSpec t => t.Staff.VoiceName,
        GrandStaffRenderSpec g => g.GrandStaff.Members.Select(FirstEngravedPart).FirstOrDefault(n => n != null),
        CondensedStaffSpec c => c.PartNames.FirstOrDefault(),
        CombinedStaffSpec cb => cb.PartNames.FirstOrDefault(),
        _ => null,
    };

    /// <summary>Writes every remembered row into its target part's measures of its play, or
    /// into a part of its own on a lead sheet.</summary>
    private void EmitPendingChordRows()
    {
        if (_pendingChordRows.Count == 0)
            return;
        DiagramTuning();   // reads the score's chordDiagrams word (the tuning is per target part)
        foreach (var pending in _pendingChordRows)
        {
            var bars = RowBars(pending.Items);
            string? target = RowTargetPart(pending.Row, out bool scoreHasStaff);
            if (target != null)
            {
                // The part wrote nothing in this play: no bar of its holds the harmony.
                if (!_partsByName.TryGetValue(target, out var part)
                    || !_playStartMeasure.TryGetValue((target, pending.Play), out int start))
                    continue;
                var header = PartHeaderDefaults.Read(
                    _root?.DescendantNodes().OfType<PartDeclarationSyntax>().FirstOrDefault(pd => pd.Name.Text == target));
                var tuning = Semantics.ChordDiagramsKey.Resolve(_diagramsWord.Word, header.FrettedTuning);
                for (int k = 0; k < bars.Count && start + k < part.Measures.Count; k++)
                {
                    var measure = part.Measures[start + k];
                    AddRowHarmonies(measure, bars[k], pending, tuning, k == 0 && measure.Implicit ? pending.Pickup : null);
                }
            }
            else if (!scoreHasStaff)
                EmitRowAsItsOwnPart(pending, bars);
        }
        _pendingChordRows.Clear();
    }

    /// <summary>
    /// One bar's symbols as harmonies at the head of <paramref name="measure"/>'s stream, each
    /// with its slot's offset — the grid every reader of a row shares
    /// (<see cref="Svg.Collector.ChordNameCollector.SlotGroups"/>) — dropping an @chord of the
    /// same chord at the same moment. A slot past a pickup's end (<paramref name="cut"/>) is
    /// not written, as the MIDI does not play it.
    /// </summary>
    private void AddRowHarmonies(MusicXmlMeasure measure, List<SyntaxNode> bar, PendingChordRow pending,
        TuningType? tuning, Fraction? cut)
    {
        if (bar.Count == 0)
            return;
        var existing = HarmoniesWithOnsets(measure);
        int insertAt = 0;
        foreach (var (node, timing, _) in Svg.Collector.ChordNameCollector.SlotGroups(bar, pending.Beats, pending.BeatType, out _))
        {
            if (node is not ChordEntrySyntax entry)
                continue;   // N.C. and a bar-head '.' write nothing
            if (cut is { } pickup && timing >= pickup)
                continue;
            if (Svg.Collector.ChordNameCollector.StructureOf(entry.SymbolText, pending.TonicStep, pending.Sharps) is not { } chord)
                continue;
            if (BuildHarmony(chord.PrintedSymbol(LilySharp.Core.Semantics.ChordSpelling.Canonical).Text) is not { } harmony)
                continue;
            if (tuning is { } t)
            {
                var shapes = Semantics.ChordDiagramScores.ShapesOf(entry).Shapes;
                var drawn = Music.ChordShapes.Drawn(t, shapes, DiagramsAll, chord, DiagramTable,
                    DiagramTable != null ? Semantics.ChordDiagramScores.SectionNameOf(entry) : null, DiagramCapo);
                if (drawn != null && BuildFrame(drawn.FrameSpec) is { } frame)
                    harmony.Add(frame);
            }
            int offset = FractionToTicks(timing);
            if (offset > 0)
                harmony.Add(new System.Xml.Linq.XElement("offset", offset));
            foreach (var (note, onset) in existing)
                if (onset == offset && note.RawElement is { } other && SameChord(other, harmony))
                    measure.Notes.Remove(note);
            measure.Notes.Insert(insertAt++, new MusicXmlNote { RawElement = harmony });
        }
    }

    /// <summary>The measure's harmonies with the position (divisions from the bar's head) each
    /// stands at: its place in the stream, plus its own offset.</summary>
    private static List<(MusicXmlNote Note, int Onset)> HarmoniesWithOnsets(MusicXmlMeasure measure)
    {
        var list = new List<(MusicXmlNote, int)>();
        int position = 0;
        foreach (var n in measure.Notes)
        {
            if (n.RawElement is { } raw)
            {
                if (raw.Name.LocalName == "harmony")
                    list.Add((n, position + ((int?)raw.Element("offset") ?? 0)));
                continue;
            }
            if (n.IsBackup)
            {
                position -= n.Duration;
                continue;
            }
            if (n.IsGrace || n.IsChord)
                continue;
            position += n.Duration;
        }
        return list;
    }

    /// <summary>Two harmonies naming one chord: the same root, kind and bass (the frame and the
    /// offset are not the chord).</summary>
    private static bool SameChord(System.Xml.Linq.XElement a, System.Xml.Linq.XElement b)
    {
        static bool Same(System.Xml.Linq.XElement? x, System.Xml.Linq.XElement? y)
            => x == null ? y == null : y != null && System.Xml.Linq.XNode.DeepEquals(x, y);
        return Same(a.Element("root"), b.Element("root"))
            && Same(a.Element("kind"), b.Element("kind"))
            && Same(a.Element("bass"), b.Element("bass"));
    }

    /// <summary>A lead sheet's row (a score with no staff): a part named "ROW (chords)" — the
    /// MIDI's track name — of one whole rest a bar (the pickup's length for a pickup bar), the
    /// harmonies at their offsets, in the treble clef, the section's key and meter.</summary>
    private void EmitRowAsItsOwnPart(PendingChordRow pending, List<List<SyntaxNode>> bars)
    {
        EnsurePart(pending.Row + " (chords)");
        _currentTranspose = null;
        _partTransposeSemitones = 0;
        _partTransposeWritten = 0;
        SetClef("treble");
        _keyFifths = pending.Key.Fifths;
        _keyMode = pending.Key.Mode;
        _keyCustomXml = pending.Key.Custom;
        _bars.SetMeter(new Semantics.Meter(pending.Beats, pending.BeatType));
        _attributesDirty = true;
        var tuning = Semantics.ChordDiagramsKey.Resolve(_diagramsWord.Word, null);
        for (int k = 0; k < bars.Count; k++)
        {
            bool first = _currentPart!.Measures.Count == 0;
            StartNewMeasure(addAttributes: first);
            var length = new Fraction(pending.Beats, pending.BeatType);
            Fraction? cut = null;
            if (k == 0 && pending.Pickup is { } pickup && pickup < length)
            {
                length = pickup;
                cut = pickup;
                _currentMeasure!.Implicit = true;
                if (first)
                {
                    _currentMeasure.Number = 0;
                    _measureNumber = 1;
                }
            }
            var (type, dots) = GetNoteType(length);
            _currentMeasure!.Notes.Add(new MusicXmlNote { IsRest = true, Duration = FractionToTicks(length), Type = type, Dots = dots });
            AddRowHarmonies(_currentMeasure, bars[k], pending, tuning, cut);
            _currentPart.Measures.Add(_currentMeasure);
            _currentMeasure = null;
        }
    }

    /// <summary>Nothing to emit since 2026-09-29: <see cref="HandleDynamicText"/> writes a
    /// dynamic where it stands (<see cref="AddDirection"/>). Kept as the one line every
    /// note-writing site calls before its note, so the order "direction, then note" stays
    /// stated where the note is written.</summary>
    private static void EmitPendingDynamic()
    {
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
