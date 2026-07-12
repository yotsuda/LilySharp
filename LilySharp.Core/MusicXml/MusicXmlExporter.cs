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
    private bool _timeSenzaMisura;  // time none
    private string? _keyCustomXml;  // non-traditional key (encoded pairs)
    private string? _noteFrameSpec; // @frame(...) on the note being written
    private MusicXmlNote? _lastPitchedNote; // hammer-on/pull-off start anchor
    private string? _pendingLineStop;       // "glissando" | "slide": stop lands on the NEXT note
    private string? _chordArpeggio;         // "arpeggiate" | "non-arpeggiate" for the chord being written
    private readonly List<MusicXmlNote> _chordMembers = new(); // members of the chord being written
    private readonly List<MusicXmlNote> _lastEmittedNotes = new(); // last note (1) or chord (N) — a following '~' node ties all of them
    private string? _pendingDynamic;

    // Track parts across sections for multi-section support
    private readonly Dictionary<string, MusicXmlPart> _partsByName = new();

    // Part-option transpose for the part being written: the WRITTEN pitch is
    // respelled and the key signature shifts with it.
    private SyntaxNode? _root;
    private (int step, int alt, int oct)? _currentTranspose;

    // Phrase auto-transpose (movable motif): a phrase written in the score's home
    // key is respelled into whatever key is in effect where it is referenced.
    // _ambientTonic tracks the running key (reset to home per voice and section,
    // advanced by key changes); the reference composes the home→ambient interval
    // onto _currentTranspose for the phrase body.
    private KeyTonic _homeTonic = KeyTonic.CMajor;
    private KeyTonic _ambientTonic = KeyTonic.CMajor;

    // Variable/phrase resolution
    private readonly Dictionary<string, SyntaxNode> _variables = new();

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
        var sectionDecls = root.DescendantNodes().OfType<SectionDeclarationSyntax>().ToList();
        // Export the PRIMARY form (`main`, else the first declared).
        var forms = root.DescendantNodes().OfType<FormDeclarationSyntax>().ToList();
        var structure = forms.FirstOrDefault(f => f.NameText == "main") ?? forms.FirstOrDefault();

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

    private void EmitSection(SectionDeclarationSyntax section)
    {
        // A section is self-contained: its phrase auto-transpose baseline reverts
        // to the score's home key (a mid-section modulation cannot leak out).
        _ambientTonic = _homeTonic;

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

    private void EmitSectionByName(Dictionary<string, List<SectionDeclarationSyntax>> byName, string name)
    {
        if (byName.TryGetValue(name, out var list))
            foreach (var section in list)
                EmitSection(section);
    }

    /// <summary>Emits the structure's items in order: a section reference plays its
    /// section, a repeat block brackets its span with repeat barlines. Nav marks,
    /// custom text and volta ENDING brackets are not yet mapped to MusicXML (the
    /// alternative's music is still emitted so nothing is lost).</summary>
    // Segno / coda jump TARGETS wait here for the next section, whose first
    // measure they open (they mark where a jump lands).
    private readonly List<System.Xml.Linq.XElement> _pendingTargetDirections = new();

    private void WalkForm(SyntaxNode container, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        _pendingTargetDirections.Clear();
        for (int i = 0; i < container.SlotCount; i++)
        {
            switch (container.GetChild(i))
            {
                case SectionReferenceSyntax r:
                    EmitWithPendingTargets(() => EmitSectionByName(byName, r.SectionName));
                    break;
                case FormRepeatBlockSyntax rb:
                    EmitWithPendingTargets(() => EmitRepeatBlock(rb, byName));
                    break;
                case FormAlternativeSyntax alt:
                    EmitWithPendingTargets(() => EmitSectionByName(byName, alt.SectionName.Text));
                    break;
                case { Kind: SyntaxKind.SilentSectionReference } silent
                        when silent.GetChild(1) is SyntaxTokenNode nm:
                    EmitWithPendingTargets(() => EmitSectionByName(byName, nm.Text));
                    break;
                case NavigationMarkSyntax nav:
                    ApplyNavMark(nav.MarkType);
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
    private void EmitRepeatBlock(FormRepeatBlockSyntax rb, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        bool hasEndings = false;
        for (int i = 0; i < rb.SlotCount; i++)
            if (rb.GetChild(i) is FormAlternativeSyntax) { hasEndings = true; break; }

        if (hasEndings)
            EmitVoltaRepeatBlock(rb, byName);
        else
            EmitPlainRepeatBlock(rb, byName);
    }

    private void EmitPlainRepeatBlock(FormRepeatBlockSyntax rb, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        var runs = new List<List<SyntaxNode>>();
        var cur = new List<SyntaxNode>();
        for (int i = 0; i < rb.SlotCount; i++)
        {
            var child = rb.GetChild(i);
            if (child is SectionReferenceSyntax or { Kind: SyntaxKind.SilentSectionReference })
                cur.Add(child);
            else if (child is SyntaxTokenNode { Kind: SyntaxKind.RepeatBothBar })
            {
                runs.Add(cur);
                cur = new List<SyntaxNode>();
            }
        }
        runs.Add(cur);

        foreach (var run in runs)
        {
            if (run.Count == 0)
                continue;
            var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
            foreach (var item in run)
                EmitFormItem(item, byName);
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
    /// the <c>:|</c> are final (type "discontinue", no repeat). A silent <c>~</c>
    /// ending suppresses its bracket (as the engraving does) but still plays.</summary>
    private void EmitVoltaRepeatBlock(FormRepeatBlockSyntax rb, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        bool forwardPending = true;
        bool afterEndBar = false;

        for (int i = 0; i < rb.SlotCount; i++)
        {
            var child = rb.GetChild(i);
            if (child is FormAlternativeSyntax alt)
            {
                var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
                EmitSectionByName(byName, alt.SectionName.Text);
                string num = EndingNumbers(alt);
                string stopType = afterEndBar ? "discontinue" : "stop";
                foreach (var p in Document.Parts)
                {
                    if (p.Measures.Count <= startIdx.GetValueOrDefault(p)) continue;
                    if (!alt.IsSilent)
                    {
                        p.Measures[startIdx.GetValueOrDefault(p)].EndingStartNumbers = num;
                        p.Measures[^1].EndingStopNumbers = num;
                        p.Measures[^1].EndingStopType = stopType;
                    }
                    if (forwardPending) p.Measures[startIdx.GetValueOrDefault(p)].RepeatForward = true;
                }
                forwardPending = false;
            }
            else if (child is SectionReferenceSyntax or { Kind: SyntaxKind.SilentSectionReference })
            {
                var startIdx = Document.Parts.ToDictionary(p => p, p => p.Measures.Count);
                EmitFormItem(child, byName);
                if (forwardPending)
                {
                    foreach (var p in Document.Parts)
                        if (p.Measures.Count > startIdx.GetValueOrDefault(p))
                            p.Measures[startIdx.GetValueOrDefault(p)].RepeatForward = true;
                    forwardPending = false;
                }
            }
            else if (child is SyntaxTokenNode { Kind: SyntaxKind.RepeatEndBar })
            {
                // The :| repeats back to the |:; it caps the ending just played.
                afterEndBar = true;
                foreach (var p in Document.Parts)
                    if (p.Measures.Count > 0)
                        p.Measures[^1].RepeatBackward = true;
            }
        }
    }

    private void EmitFormItem(SyntaxNode item, Dictionary<string, List<SectionDeclarationSyntax>> byName)
    {
        switch (item)
        {
            case SectionReferenceSyntax r: EmitSectionByName(byName, r.SectionName); break;
            case FormAlternativeSyntax alt: EmitSectionByName(byName, alt.SectionName.Text); break;
            case { Kind: SyntaxKind.SilentSectionReference } silent
                    when silent.GetChild(1) is SyntaxTokenNode nm:
                EmitSectionByName(byName, nm.Text);
                break;
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
    {
        var partName = partBlock.Name;
        EnsurePart(partName);
        _currentTranspose = _root != null ? PartTranspose.Read(_root, partName) : null;
        ApplyPartHeaderClef(partName);
        _lastPitchedNote = null; // ho/po never pairs across parts

        // Reset state for this part's continuation
        _currentOctave = 4;
        _currentStep = 0;
        _ambientTonic = _homeTonic; // each voice starts at the score's home key
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
                KeyFifths = _currentTranspose is { } trk
                    ? _keyFifths + PitchTransposer.KeySignatureFifthsShift(trk.step, trk.alt)
                    : _keyFifths,
                KeyCustom = _keyCustomXml,
                KeyMode = _keyMode,
                ClefSign = _clefSign,
                ClefLine = _clefLine > 0 ? _clefLine : null,
                ClefOctaveChange = _clefOctaveChange
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
            case DrumNoteSyntax drumNote:
                ProcessDrumNote(drumNote);
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
                // Tie follows a note or chord — mark EVERY note just emitted as a
                // tie start (a chord ties all its members), and flag the next
                // note/chord so it emits the matching tie-stop.
                if (_lastEmittedNotes.Count > 0)
                    foreach (var n in _lastEmittedNotes) n.TieStart = true;
                else if (_currentMeasure != null && _currentMeasure.Notes.Count > 0)
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
                        ProcessNode(repeat.Body);
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
                if (_variables.TryGetValue(varRef.Name.Text, out var varBody))
                {
                    // Phrase bodies evaluate in a fresh relative frame so a
                    // $phrase means the same pitches at every call site
                    // (matches MeasureCollector's RelativeResetMarker). Trailing
                    // marks (Chorus' / Chorus,) shift that frame up or down.
                    _currentOctave = 4 + varRef.OctaveOffset;
                    _currentStep = 0;
                    _defaultDuration = Fraction.Quarter;
                    // Auto-transpose the movable phrase from the home key to the
                    // ambient key here (respelled), composed under any part
                    // transpose; restored after the body.
                    var savedTranspose = _currentTranspose;
                    _currentTranspose = PitchTransposer.Compose(PhraseTransposeTarget(), savedTranspose);
                    ProcessNode(varBody);
                    _currentTranspose = savedTranspose;
                }
                break;

            case PhraseDeclarationSyntax:
            case VariableDeclarationSyntax:
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

            var temp = new MusicXmlPart { Name = "voice-temp" };
            _currentPart = temp;
            _currentMeasure = null;
            StartNewMeasure(); // scratch stream needs an open measure for its notes
            _currentOctave = 4;
            _currentStep = 0;
            _defaultDuration = Fraction.Quarter;
            _tieToNextNote = false;
            ProcessNode(voices[v]);
            FlushCurrentMeasure();

            _currentPart = savedPart;
            _currentMeasure = savedMeasure;
            _measureNumber = savedMeasureNumber;
            _currentOctave = savedOctave;
            _currentStep = savedStep;
            _defaultDuration = savedDefault;
            _tieToNextNote = savedTie;

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
    }

    private void ProcessTimeSignature(TimeSignatureSyntax timeSig)
    {
        _timeSenzaMisura = timeSig.IsSenzaMisura;
        if (_timeSenzaMisura) return;
        _timeNumerator = timeSig.Beats;
        _timeNumeratorText = timeSig.BeatsText;
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
        if (key.IsCustom)
        {
            _keyCustomXml = LilySharp.Core.Svg.Model.KeySignature.EncodeCustom(key.CustomAlterations);
            _keyFifths = 0;
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
    }

    /// <summary>The part declaration's `clef` property (if any) sets the
    /// part's opening clef — the walk only sees IN-MUSIC clef changes, so a
    /// header-only clef (the normal case) used to export as G.</summary>
    private void ApplyPartHeaderClef(string partName)
    {
        if (_root == null) return;
        foreach (var pd in _root.DescendantNodes().OfType<PartDeclarationSyntax>())
        {
            if (pd.Name.Text != partName) continue;
            foreach (var prop in pd.Properties)
            {
                if (prop.NameToken.Text.Equals("clef", StringComparison.OrdinalIgnoreCase)
                    && prop.GetChild(2) is SyntaxTokenNode v)
                {
                    SetClef(v.Text.ToLowerInvariant());
                    return;
                }
            }
            return;
        }
    }

    private void ProcessClef(ClefDeclarationSyntax clef)
    {
        SetClef(clef.ClefName?.Text.ToLower());
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
        if (_currentTranspose is not { } tr)
            return (step, alter, octave);
        var (ns, na, no) = PitchTransposer.Transpose(
            RelativeOctave.StepIndex(pitch.BaseName), pitch.AccidentalOffset, octave,
            tr.step, tr.alt, tr.oct);
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
                && mark.MarkName.StartsWith("notehead.", StringComparison.OrdinalIgnoreCase))
                return mark.MarkName[9..].ToLowerInvariant() switch
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
        if (_tieToNextNote) { xmlNote.TieStop = true; _tieToNextNote = false; }
        if (note.Articulations.OfType<TieSyntax>().Any()) { xmlNote.TieStart = true; _tieToNextNote = true; }

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

    private void ProcessChord(ChordSyntax chord)
    {
        if (_currentMeasure == null) return;
        _justAutoClosedPickup = false;

        var pitches = chord.Pitches.ToList();
        if (pitches.Count == 0 && !chord.Degrees.Any()) return;

        var duration = GetDuration(chord.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        // Emit pending dynamic as direction before the chord
        EmitPendingDynamic();

        // The first member is the ROOT (resolved relatively); every other member
        // STACKS above it — the same octave placement as a scale degree, so the
        // chord's pitches are independent of the written order (<c e g> == <c 3 5>
        // == <c g e>); the note after the chord is relative to the root. A
        // deliberate Lily# divergence from LilyPond, matching MidiExporter and
        // MeasureCollector.
        int firstStep = _currentStep, firstOctave = _currentOctave;
        var (tupletActual, tupletNormal) = CurrentTupletRatio();
        bool isFirst = true;
        foreach (var pitch in pitches)
        {
            var (step, alter) = ParsePitch(pitch);
            int targetOctave;
            if (isFirst)
            {
                targetOctave = ResolveRelativeOctave(pitch); // root: relative, advances state
                firstStep = _currentStep;
                firstOctave = _currentOctave;
            }
            else if (_octaveAbsolute)
            {
                // Absolute mode: each member is a fixed pitch, no stacking.
                targetOctave = ResolveRelativeOctave(pitch);
            }
            else
            {
                int stepIdx = RelativeOctave.StepIndex(pitch.BaseName);
                targetOctave = firstOctave + (stepIdx >= firstStep ? 0 : 1) + pitch.OctaveOffset;
            }
            (step, alter, targetOctave) = ApplyTranspose(pitch, step, alter, targetOctave);

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
                    && mm.MarkName.Equals("arpeggio.bracket", StringComparison.OrdinalIgnoreCase));
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
            firstOctave = RelativeOctave.Resolve(_currentStep, _currentOctave, tonicStep, 0);
            // The first degree is the chord's root; its octave mark sets the whole
            // chord's register (<1' 3 5> == <c' 3 5>). Fold it into the root octave
            // and skip it on that degree below.
            firstOctave += chord.Degrees.First().OctaveOffset;
            firstStep = tonicStep;
            _currentStep = tonicStep;
            _currentOctave = firstOctave;
        }

        // Scale-degree members (<d 3 5 7,>): stack on the root by diatonic steps in
        // the (written) key, then apply the part transpose like any pitch. When the
        // root is omitted the FIRST degree is the chord's onset (no <chord/>).
        bool needsOnset = pitches.Count == 0;
        bool firstDeg = true;
        foreach (var degree in chord.Degrees)
        {
            int degOctaveMarks = needsOnset && firstDeg ? 0 : degree.OctaveOffset;
            firstDeg = false;
            var (dstep, dalter, doctave) = ChordDegrees.Resolve(
                firstStep, firstOctave, degree.Number, degree.Alteration,
                degOctaveMarks, _keyFifths);
            if (_currentTranspose is { } tr)
                (dstep, dalter, doctave) = PitchTransposer.Transpose(
                    dstep, dalter, doctave, tr.step, tr.alt, tr.oct);
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
        // Ties apply to EVERY member of the chord: <c e g>~ <c e g> ties all
        // voices, so tagging only the first note (the old behavior) dropped the
        // rest. Pair the stop from a preceding tie across all members too.
        if (_tieToNextNote) { foreach (var m in _chordMembers) m.TieStop = true; _tieToNextNote = false; }
        if (chord.Articulations.OfType<TieSyntax>().Any())
        { foreach (var m in _chordMembers) m.TieStart = true; _tieToNextNote = true; }

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
        // Pre-scan the frame spec so a chord symbol on the same note can
        // embed it, whichever order the marks were written in.
        _noteFrameSpec = null;
        foreach (var artic in articulations)
            if (artic is MusicMarkSyntax fm
                && fm.MarkName.StartsWith("frame.", StringComparison.OrdinalIgnoreCase))
                _noteFrameSpec = fm.MarkName[6..].ToLowerInvariant();

        foreach (var artic in articulations)
        {
            if (artic is ArticulationSyntax articulation)
            {
                // Single-word direction marks (@ped, @sost, @ottava, @loco)
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
    /// pedal (@ped / @ped.off / @sost / @sost.off), ottava lines
    /// (@ottava / @ottava.bassa / @loco) and chord symbols (@chord(...)).
    /// Everything else stays with its specialized consumer.</summary>
    private void ProcessDirectionMark(MusicMarkSyntax mark)
        => ProcessDirectionName(mark.MarkName);

    private void ProcessDirectionName(string rawName)
    {
        if (_currentMeasure == null) return;
        var name = rawName.ToLowerInvariant(); // chord TEXT keeps its case below
        switch (name)
        {
            case "ped":
                _currentMeasure.Directions.Add(new MusicXmlDirection { PedalType = "start", Placement = "below" });
                break;
            case "ped.off":
            case "sost.off":
                _currentMeasure.Directions.Add(new MusicXmlDirection { PedalType = "stop", Placement = "below" });
                break;
            case "sost":
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
            case "loco":
                _currentMeasure.Directions.Add(new MusicXmlDirection { OctaveShiftType = "stop" });
                break;
            default:
                if (name.StartsWith("chord.", StringComparison.Ordinal)
                    && LilySharp.Core.Svg.Model.ChordNameItem.ParseChordName(rawName) is { } chordText)
                {
                    var harmony = BuildHarmony(chordText);
                    if (harmony != null)
                    {
                        // A @frame on the same note nests inside the harmony
                        // (MusicXML <frame> is a harmony child).
                        if (_noteFrameSpec is { } fspec && BuildFrame(fspec) is { } frameEl)
                            harmony.Add(frameEl);
                        _currentMeasure.Notes.Add(new MusicXmlNote { RawElement = harmony });
                    }
                }
                else if (name.StartsWith("fig.", StringComparison.Ordinal)
                    && LilySharp.Core.Svg.Model.FiguredBassItem.ParseFigures(rawName) is { } figures
                    && BuildFiguredBass(figures) is { } figuredBass)
                {
                    // <figured-bass> sits before its bass note, like <harmony>.
                    _currentMeasure.Notes.Add(new MusicXmlNote { RawElement = figuredBass });
                }
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
