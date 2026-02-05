using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Tracks measure boundary alignment status for incremental compilation.
/// </summary>
public record MeasureBoundary(
    int SourcePosition,
    Fraction AccumulatedDuration,
    bool IsExplicit,  // true if there was an explicit barline
    bool IsAligned    // true if duration matches time signature
);

/// <summary>
/// Represents a bar check warning when barline position doesn't match time signature.
/// </summary>
public record BarCheckWarning(
    int SourcePosition,
    Fraction ExpectedDuration,
    Fraction ActualDuration
);

/// <summary>
/// Helper class for building measures from syntax nodes.
/// Supports both explicit barlines and automatic measure detection based on time signature.
/// </summary>
internal sealed class MeasureBuilder
{
    private readonly List<Measure> _measures = new();
    private readonly List<MusicItem> _currentItems = new();
    private readonly List<MeasureBoundary> _boundaries = new();
    private readonly List<BarCheckWarning> _barCheckWarnings = new();

    private readonly Fraction _timeSignature;
    private Fraction _currentDuration = Fraction.Zero;
    private Fraction _defaultDuration = Fraction.Quarter;

    private BarlineType _pendingStartBarline = BarlineType.None;
    private BarlineType _pendingEndBarline = BarlineType.None;
    private bool _pendingBreak = false;
    private string? _sectionLabel;
    private int _measureSourceStart;

    public MeasureBuilder(Fraction timeSignature, int sourceStart = 0)
    {
        _timeSignature = timeSignature;
        _measureSourceStart = sourceStart;
    }

    public IReadOnlyList<MeasureBoundary> Boundaries => _boundaries;
    public IReadOnlyList<BarCheckWarning> BarCheckWarnings => _barCheckWarnings;

    /// <summary>Gets the current accumulated duration within the measure.</summary>
    public Fraction CurrentDuration => _currentDuration;

    /// <summary>Current measure index (completed measures count).</summary>
    public int CurrentMeasureIndex => _measures.Count;

    /// <summary>Current item count within the current measure.</summary>
    public int CurrentItemCount => _currentItems.Count;

    public string? SectionLabel
    {
        get => _sectionLabel;
        set => _sectionLabel = value;
    }

    /// <summary>
    /// Adds a music item and automatically completes the measure if duration is reached.
    /// </summary>
    public void AddItem(MusicItem item)
    {
        _currentItems.Add(item);

        // Track duration
        var itemDuration = GetItemDuration(item);
        _currentDuration += itemDuration;

        // Auto-complete measure if we've reached or exceeded time signature
        if (_currentDuration >= _timeSignature)
        {
            AutoCompleteMeasure(item.SourcePosition + 1);
        }
    }

    /// <summary>
    /// Adds a music item without affecting duration tracking.
    /// Used for tuplet notes where duration is calculated separately.
    /// </summary>
    public void AddItemWithoutDuration(MusicItem item)
    {
        _currentItems.Add(item);

        // Update default duration (for subsequent notes)
        Fraction baseDuration = item switch
        {
            NoteItem note => note.BaseDuration,
            RestItem rest => rest.BaseDuration,
            ChordItem chord => chord.BaseDuration,
            _ => Fraction.Zero
        };
        if (baseDuration != Fraction.Zero)
            _defaultDuration = baseDuration;
    }

    /// <summary>
    /// Adds duration and triggers auto-completion if time signature is reached.
    /// Used after processing tuplet notes with scaled duration.
    /// </summary>
    public void AddDuration(Fraction duration, int sourcePosition)
    {
        _currentDuration += duration;

        if (_currentDuration >= _timeSignature)
        {
            AutoCompleteMeasure(sourcePosition);
        }
    }

    private Fraction GetItemDuration(MusicItem item)
    {
        // Duration already includes dots (BaseDuration.Dotted(Dots))
        Fraction duration = item switch
        {
            NoteItem note => note.Duration,
            RestItem rest => rest.Duration,
            ChordItem chord => chord.Duration,
            _ => Fraction.Zero
        };

        // Update default duration (use base duration without dots)
        Fraction baseDuration = item switch
        {
            NoteItem note => note.BaseDuration,
            RestItem rest => rest.BaseDuration,
            ChordItem chord => chord.BaseDuration,
            _ => Fraction.Zero
        };
        if (baseDuration != Fraction.Zero)
            _defaultDuration = baseDuration;

        return duration;
    }

    private void AutoCompleteMeasure(int sourceEnd)
    {
        // Check if duration aligns with time signature
        bool isAligned = _currentDuration == _timeSignature;

        if (_currentItems.Count > 0)
        {
            // Apply pending break if any
            bool hasBreak = _pendingBreak;
            _pendingBreak = false;

            _measures.Add(new Measure(
                _currentItems.ToImmutableArray(),
                _pendingStartBarline,
                _pendingEndBarline != BarlineType.None ? _pendingEndBarline : BarlineType.Single,
                _sectionLabel,
                _measureSourceStart,
                sourceEnd,
                hasBreakAfter: hasBreak));

            // Record boundary
            _boundaries.Add(new MeasureBoundary(
                sourceEnd,
                _currentDuration,
                IsExplicit: false,
                IsAligned: isAligned));

            _currentItems.Clear();
            _sectionLabel = null;
            _pendingStartBarline = BarlineType.None;
            _pendingEndBarline = BarlineType.None;
            _measureSourceStart = sourceEnd;

            // Handle overflow: if we exceeded time signature, the excess carries over
            if (_currentDuration > _timeSignature)
            {
                // Note: For now we don't handle splitting notes across barlines
                // This would require more complex handling
            }
            _currentDuration = Fraction.Zero;
        }
    }

    public void SetBreak()
    {
        if (_currentItems.Count == 0 && _measures.Count > 0)
        {
            // At measure boundary - apply break to previous measure
            var last = _measures[^1];
            _measures[^1] = new Measure(
                last.Items,
                last.StartBarline,
                last.EndBarline,
                last.SectionLabel,
                last.SourceStart,
                last.SourceEnd,
                hasBreakAfter: true);
        }
        else
        {
            // Mid-measure break - defer to next measure boundary
            _pendingBreak = true;
        }
    }

    /// <summary>
    /// Handles an explicit barline as a bar check (LilyPond style).
    /// Does not create measures - measures are created automatically based on time signature.
    /// Emits warnings if barline position doesn't match expected measure boundary.
    /// </summary>
    public void HandleBarline(BarlineType barType, int position)
    {
        // Bar check: verify current position is at a measure boundary
        bool isAligned = _currentDuration == Fraction.Zero || _currentDuration == _timeSignature;

        if (!isAligned)
        {
            // Emit warning: barline position doesn't match time signature
            _barCheckWarnings.Add(new BarCheckWarning(
                position,
                _timeSignature,
                _currentDuration));
        }

        // Handle special barlines by setting pending barline types
        switch (barType)
        {
            case BarlineType.RepeatStart:
                _pendingStartBarline = BarlineType.RepeatStart;
                break;

            case BarlineType.RepeatEnd:
                if (_currentDuration == Fraction.Zero && _measures.Count > 0)
                {
                    // :| at measure boundary - modify last measure's end barline
                    var lastMeasure = _measures[^1];
                    _measures[^1] = new Measure(
                        lastMeasure.Items,
                        lastMeasure.StartBarline,
                        BarlineType.RepeatEnd,
                        lastMeasure.SectionLabel,
                        lastMeasure.SourceStart,
                        lastMeasure.SourceEnd);
                }
                else
                {
                    // :| in middle of measure - set pending end barline
                    _pendingEndBarline = BarlineType.RepeatEnd;
                }
                break;

            case BarlineType.Double:
                _pendingEndBarline = BarlineType.Double;
                break;

            case BarlineType.Final:
                _pendingEndBarline = BarlineType.Final;
                break;

            // Single barline is just a check, no action needed
            case BarlineType.Single:
            default:
                break;
        }
    }


    public List<Measure> FinalizeMeasures()
    {
        // Handle any remaining items as the final measure
        if (_currentItems.Count > 0)
        {
            bool isAligned = _currentDuration == _timeSignature;

            _measures.Add(new Measure(
                _currentItems.ToImmutableArray(),
                _pendingStartBarline,
                _pendingEndBarline != BarlineType.None ? _pendingEndBarline : BarlineType.Single,
                _sectionLabel,
                _measureSourceStart,
                _measureSourceStart));  // End position same as start for incomplete

            _boundaries.Add(new MeasureBoundary(
                _measureSourceStart,
                _currentDuration,
                IsExplicit: false,
                IsAligned: isAligned));
        }
        return _measures;
    }
}

/// <summary>
/// Collects measures from a syntax tree.
/// </summary>
public sealed class MeasureCollector
{
    private readonly Dictionary<string, SectionDeclarationSyntax> _sections = new();
    private readonly Dictionary<string, SyntaxNode> _variables = new();
    private StructureDeclarationSyntax? _structure;
    private string? _voiceName;
    private SyntaxNode? _root;

    // State for relative pitch mode
    private int _currentOctave = 4;
    private char _lastPitchName = 'c';

    // Dynamic markings
    private readonly List<DynamicItem> _dynamics = new();
    // Articulation marks
    private readonly List<ArticulationItem> _articulations = new();
    // Grace notes
    private readonly List<GraceNoteItem> _graceNotes = new();
    // Lyrics
    private readonly List<LyricItem> _lyrics = new();
    // Music marks (segno, coda, fine, D.S., D.C., etc.)
    private readonly List<MusicMarkItem> _musicMarks = new();
    // Custom text annotations
    private readonly List<CustomTextItem> _customTexts = new();
    // Volta brackets (first/second ending)
    private readonly List<VoltaBracketItem> _voltaBrackets = new();
    // Tuplet brackets
    private readonly List<TupletBracketItem> _tupletBrackets = new();
    // Pending grace notes to attach to the next main note
    private GraceExpressionSyntax? _pendingGrace = null;
    // Default duration
    private Fraction _defaultDuration = Fraction.Quarter;

    // Metadata
    private string? _title;
    private string? _composer;
    private int? _tempo;
    private int _timeBeats = 4;
    private int _timeBeatType = 4;
    private int _keySharps = 0;
    private string _clef = "treble";

    /// <summary>
    /// Gets the time signature as a Fraction.
    /// </summary>
    private Fraction TimeSignatureFraction => new(_timeBeats, _timeBeatType);

    /// <summary>
    /// Collects a Score from a syntax tree.
    /// </summary>
    public Score Collect(SyntaxTree tree, string? voiceName = null)
    {
        _voiceName = voiceName;
        Reset();

        // Phase 1: Collect definitions
        CollectDefinitions(tree.GetRoot());

        // Phase 1.5: If voiceName specified, look up clef from part definition
        if (voiceName != null)
        {
            var partClef = GetPartClef(tree.GetRoot(), voiceName);
            if (partClef != null)
                _clef = partClef;
        }

        // Set initial octave based on clef (bass clef starts at octave 3)
        _currentOctave = _clef == "bass" ? 3 : 4;

        // Phase 2: Check for parallel expression (multi-voice)
        var parallelExpr = tree.GetRoot().DescendantNodes()
            .OfType<ParallelExpressionSyntax>()
            .FirstOrDefault();

        if (parallelExpr != null)
        {
            return CollectMultiVoiceScore(parallelExpr);
        }

        // Single voice
        var measures = CollectMeasures();
        var voice = new Voice(_voiceName ?? "default", measures.ToImmutableArray());

        // Collect lyrics
        CollectLyrics(tree.GetRoot(), measures);

        return new Score(
            voice,
            new TimeSignature(_timeBeats, _timeBeatType),
            new KeySignature(_keySharps),
            _clef,
            _tempo,
            _title,
            _composer,
            _dynamics.ToImmutableArray(),
            _articulations.ToImmutableArray(),
            _graceNotes.ToImmutableArray(),
            lyrics: _lyrics.ToImmutableArray(),
            musicMarks: _musicMarks.ToImmutableArray(),
            customTexts: _customTexts.ToImmutableArray(),
            voltaBrackets: _voltaBrackets.ToImmutableArray(),
            tupletBrackets: _tupletBrackets.ToImmutableArray());
    }

    /// <summary>
    /// Collects a MultiStaffScore from a syntax tree based on a render specification.
    /// </summary>
    public MultiStaffScore CollectMultiStaff(SyntaxTree tree, RenderSpec renderSpec)
    {
        Reset();

        // Phase 1: Collect definitions
        CollectDefinitions(tree.GetRoot());

        // Phase 2: Build voice dictionary
        var voiceDict = new Dictionary<string, Voice>();
        foreach (var voiceName in renderSpec.GetVoiceNames())
        {
            _voiceName = voiceName;
            _lastPitchName = 'c';
            _defaultDuration = Fraction.Quarter;

            // Set clef for this voice from part definition
            var partClef = GetPartClef(tree.GetRoot(), voiceName);
            _clef = partClef ?? "treble";

            // Set initial octave based on clef (bass clef starts at octave 3)
            _currentOctave = _clef == "bass" ? 3 : 4;


            var measures = CollectMeasuresForVoice(voiceName);
            voiceDict[voiceName] = new Voice(voiceName, measures.ToImmutableArray());
        }

        // Phase 3: Build staff groups from render spec
        var staffGroups = renderSpec.ToStaffGroups(name =>
            voiceDict.TryGetValue(name, out var v) ? v : new Voice(name, ImmutableArray<Measure>.Empty))
            .ToImmutableArray();

        return new MultiStaffScore(
            staffGroups,
            new TimeSignature(_timeBeats, _timeBeatType),
            new KeySignature(_keySharps),
            _tempo,
            _title,
            _composer,
            lyrics: _lyrics.ToImmutableArray(),
            musicMarks: _musicMarks.ToImmutableArray(),
            customTexts: _customTexts.ToImmutableArray(),
            voltaBrackets: _voltaBrackets.ToImmutableArray(),
            tupletBrackets: _tupletBrackets.ToImmutableArray());
    }

    private List<Measure> CollectMeasuresForVoice(string voiceName)
    {
        // 1. Check variables first
        if (_variables.TryGetValue(voiceName, out var variable))
            return CollectMeasuresFromNode(variable);

        // 2. Search for PartBlock with matching name in all sections
        foreach (var section in _sections.Values)
        {
            var partBlock = section.DescendantNodes<PartBlockSyntax>()
                .FirstOrDefault(p => p.Name == voiceName);
            if (partBlock != null)
                return CollectMeasuresFromNode(partBlock);
        }

        return [];
    }

    private Score CollectMultiVoiceScore(ParallelExpressionSyntax parallelExpr)
    {
        var voices = new List<Voice>();
        List<Measure>? firstVoiceMeasures = null;
        int voiceNumber = 1;

        foreach (var voiceNode in parallelExpr.Voices)
        {
            // Save and reset state for each voice
            var savedOctave = _currentOctave;
            var savedPitch = _lastPitchName;
            var savedDuration = _defaultDuration;

            _currentOctave = 4;
            _lastPitchName = 'c';
            _defaultDuration = Fraction.Quarter;

            var measures = CollectMeasuresFromNode(voiceNode);
            if (firstVoiceMeasures == null)
                firstVoiceMeasures = measures;

            var voiceName = $"voice{voiceNumber}";
            voices.Add(new Voice(voiceName, measures.ToImmutableArray()));
            voiceNumber++;

            _currentOctave = savedOctave;
            _lastPitchName = savedPitch;
            _defaultDuration = savedDuration;
        }

        // Collect lyrics (aligned with first voice)
        if (firstVoiceMeasures != null)
            CollectLyrics(parallelExpr, firstVoiceMeasures);

        return new Score(
            voices.ToImmutableArray(),
            new TimeSignature(_timeBeats, _timeBeatType),
            new KeySignature(_keySharps),
            _clef,
            _tempo,
            _title,
            _composer,
            _dynamics.ToImmutableArray(),
            _articulations.ToImmutableArray(),
            _graceNotes.ToImmutableArray(),
            lyrics: _lyrics.ToImmutableArray(),
            musicMarks: _musicMarks.ToImmutableArray(),
            customTexts: _customTexts.ToImmutableArray(),
            voltaBrackets: _voltaBrackets.ToImmutableArray(),
            tupletBrackets: _tupletBrackets.ToImmutableArray());
    }

    private List<Measure> CollectMeasuresFromNode(SyntaxNode voiceNode)
    {
        var builder = new MeasureBuilder(TimeSignatureFraction, voiceNode.Position);

        // Collect all music nodes, expanding variable references
        var musicNodes = new List<SyntaxNode>();

        foreach (var node in voiceNode.DescendantNodes())
        {
            // Skip nodes that are inside a tuplet (they'll be processed by the tuplet)
            if (IsInsideTuplet(node))
                continue;

            switch (node)
            {
                case NoteSyntax:
                case RestSyntax:
                case ChordSyntax:
                case BarlineSyntax:
                case BreakSyntax:
                case TieSyntax:
                case SlurSyntax:
                case TupletExpressionSyntax:
                    musicNodes.Add(node);
                    break;

                case VariableReferenceSyntax varRef:
                    ExpandVariable(varRef.Name.Text, musicNodes);
                    break;
            }
        }

        // Process collected music nodes (with lookahead for ties/slurs)
        for (int i = 0; i < musicNodes.Count; i++)
        {
            var node = musicNodes[i];
            // Check if next node is a tie or slur
            bool hasTieAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is TieSyntax;
            bool hasSlurStartAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is SlurSyntax slurS && slurS.IsOpen;
            bool hasSlurEndAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is SlurSyntax slurE && !slurE.IsOpen;
            ProcessMusicNode(node, builder, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter);
        }

        return builder.FinalizeMeasures();
    }

    private void ProcessMusicNode(SyntaxNode node, MeasureBuilder builder, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false)
    {
        switch (node)
        {
            case GraceExpressionSyntax grace:
                // Store grace expression to attach to the next note
                _pendingGrace = grace;
                break;

            case NoteSyntax note:
                {
                    int measureIndex = builder.CurrentMeasureIndex;
                    int itemIndex = builder.CurrentItemCount;
                    var noteItem = CreateNoteItem(note, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter);
                    builder.AddItem(noteItem);
                    CollectDynamics(note, measureIndex, itemIndex);
                    CollectArticulations(note, measureIndex, itemIndex, noteItem.StemUp);
                    // Attach any pending grace notes
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                }
                break;

            case RestSyntax rest:
                builder.AddItem(CreateRestItem(rest));
                break;

            case ChordSyntax chord:
                {
                    int measureIndex = builder.CurrentMeasureIndex;
                    int itemIndex = builder.CurrentItemCount;
                    var chordItem = CreateChordItem(chord);
                    builder.AddItem(chordItem);
                    CollectDynamics(chord, measureIndex, itemIndex);
                    // Use chord stem direction for articulation placement
                    CollectArticulations(chord, measureIndex, itemIndex, chordItem.StemUp);
                    // Attach any pending grace notes
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                }
                break;

            case BarlineSyntax barline:
                var barType = ParseBarlineType(barline.BarToken.Text);
                builder.HandleBarline(barType, barline.Position);
                break;

            case BreakSyntax:
                // 'break' keyword triggers line break
                builder.SetBreak();
                break;

            case TieSyntax:
            case SlurSyntax:
                // Already processed with the preceding note
                break;

            case TupletExpressionSyntax tuplet:
                // LILYPOND-REF: lily/tuplet-engraver.cc - process tuplet as a unit
                ProcessTuplet(tuplet, builder);
                break;
        }
    }

    /// <summary>
    /// Processes a tuplet expression, collecting notes and creating a bracket item.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-engraver.cc - Tuplet_engraver class
    /// Processes notes within a tuplet expression and creates a bracket item.
    /// </remarks>
    private void ProcessTuplet(TupletExpressionSyntax tuplet, MeasureBuilder builder)
    {
        int measureIndex = builder.CurrentMeasureIndex;
        int startNoteIndex = builder.CurrentItemCount;

        // Track written duration of all items in the tuplet
        Fraction writtenDuration = Fraction.Zero;
        int lastSourcePosition = tuplet.Position;

        // Process all notes inside the tuplet body using Items property
        // (not DescendantNodes which includes all nested nodes)
        // Use AddItemWithoutDuration to avoid incorrect auto-completion
        foreach (var item in tuplet.Body.Items)
        {
            if (item is NoteSyntax note)
            {
                var noteItem = CreateNoteItem(note, false, false, false);
                builder.AddItemWithoutDuration(noteItem);
                writtenDuration += noteItem.Duration;
                lastSourcePosition = note.Position;
            }
            else if (item is RestSyntax rest)
            {
                var restItem = CreateRestItem(rest);
                builder.AddItemWithoutDuration(restItem);
                writtenDuration += restItem.Duration;
                lastSourcePosition = rest.Position;
            }
            else if (item is ChordSyntax chord)
            {
                var chordItem = CreateChordItem(chord);
                builder.AddItemWithoutDuration(chordItem);
                writtenDuration += chordItem.Duration;
                lastSourcePosition = chord.Position;
            }
        }

        // Calculate actual duration: written × base / ratio
        // e.g., tuplet 3/2: 3 quarters (3/4) → actual 2/4
        // LILYPOND-REF: lily/tuplet-bracket.cc - tuplet duration scaling
        int ratio = tuplet.TupletRatio;   // e.g., 3 (play 3 notes)
        int @base = tuplet.BaseDivision;  // e.g., 2 (in time of 2)
        Fraction actualDuration = new Fraction(
            writtenDuration.Numerator * @base,
            writtenDuration.Denominator * ratio);

        // Add the actual duration (may trigger auto-completion)
        builder.AddDuration(actualDuration, lastSourcePosition + 1);

        int endNoteIndex = builder.CurrentItemCount - 1;

        // Only add bracket if we have at least 2 notes
        if (endNoteIndex >= startNoteIndex)
        {
            _tupletBrackets.Add(new TupletBracketItem(
                tuplet.TupletRatio,
                tuplet.BaseDivision,
                startNoteIndex,
                endNoteIndex,
                measureIndex,
                tuplet.Position
            ));
        }
    }

    private void Reset()
    {
        _sections.Clear();
        _variables.Clear();
        _dynamics.Clear();
        _articulations.Clear();
        _graceNotes.Clear();
        _structure = null;
        _root = null;
        _currentOctave = 4;
        _lastPitchName = 'c';
        _defaultDuration = Fraction.Quarter;
        _title = null;
        _composer = null;
        _tempo = null;
        _timeBeats = 4;
        _timeBeatType = 4;
        _keySharps = 0;
        _clef = "treble";
    }

    /// <summary>
    /// Looks up the clef from a part definition by name.
    /// </summary>
    private static string? GetPartClef(SyntaxNode root, string partName)
    {
        foreach (var partDecl in root.DescendantNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;

            // Check properties for clef
            foreach (var prop in partDecl.Properties)
            {
                if (prop.NameToken.Text.ToLowerInvariant() == "clef")
                {
                    // Value is at index 2 (after name and colon)
                    var valueToken = prop.GetChild(2) as SyntaxTokenNode;
                    if (valueToken == null) continue;

                    return valueToken.Text.ToLowerInvariant();
                }
            }
        }

        return null;
    }

    private void CollectDefinitions(SyntaxNode root)
    {
        _root = root;

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MetadataDeclarationSyntax metadata:
                    CollectMetadata(metadata);
                    break;

                case TempoDeclarationSyntax tempoDecl:
                    CollectTempo(tempoDecl);
                    break;

                case TimeSignatureSyntax timeSig:
                    _timeBeats = timeSig.Beats;
                    _timeBeatType = timeSig.BeatType;
                    break;

                case KeySignatureSyntax key:
                    _keySharps = CalculateKeySharps(key);
                    break;

                case ClefDeclarationSyntax clef:
                    _clef = clef.ClefName.Text.ToLowerInvariant();
                    break;

                case SectionDeclarationSyntax section:
                    _sections[section.SectionName] = section;
                    break;

                case StructureDeclarationSyntax structure:
                    _structure = structure;
                    break;

                case VariableDeclarationSyntax varDecl:
                    _variables[varDecl.Name.Text] = varDecl.Expression;
                    break;

                case PhraseDeclarationSyntax phraseDecl:
                    _variables[phraseDecl.Name.Text] = phraseDecl.Body;
                    break;

                case RenderDeclarationSyntax render:
                    ExtractVoiceName(render);
                    break;
            }
        }
    }

    private void CollectMetadata(MetadataDeclarationSyntax metadata)
    {
        var keyword = metadata.Keyword.ToLowerInvariant();
        var values = metadata.Values.ToList();

        switch (keyword)
        {
            case "title":
                if (values.Count > 0 && values[0] is SyntaxTokenNode titleToken)
                    _title = titleToken.Text.Trim('"');
                break;
            case "composer":
                if (values.Count > 0 && values[0] is SyntaxTokenNode composerToken)
                    _composer = composerToken.Text.Trim('"');
                break;
        }
    }

    private void CollectTempo(TempoDeclarationSyntax tempoDecl)
    {
        var values = tempoDecl.Values.ToList();
        if (values.Count > 0 && values[0] is SyntaxTokenNode token && int.TryParse(token.Text, out int tempo))
            _tempo = tempo;
    }

    private int CalculateKeySharps(KeySignatureSyntax key)
    {
        string pitchName = key.Pitch.PitchName.ToLowerInvariant();
        string mode = key.Mode.Text.ToLowerInvariant();

        var majorKeys = new Dictionary<string, int>
        {
            ["c"] = 0, ["g"] = 1, ["d"] = 2, ["a"] = 3, ["e"] = 4, ["b"] = 5,
            ["f"] = -1, ["bes"] = -2, ["ees"] = -3, ["aes"] = -4, ["des"] = -5, ["ges"] = -6
        };

        string keyName = pitchName;
        if (key.Pitch.AccidentalOffset > 0) keyName += "is";
        else if (key.Pitch.AccidentalOffset < 0) keyName += "es";

        if (majorKeys.TryGetValue(keyName, out int sharps))
        {
            if (mode == "minor") sharps -= 3;
            return sharps;
        }

        return 0;
    }

    private void ExtractVoiceName(RenderDeclarationSyntax render)
    {
        if (render.GetChild(1) is not SyntaxTokenNode outputType || outputType.Text != "score")
            return;

        foreach (var child in render.DescendantNodes())
        {
            if (child is StaffRenderSyntax staff)
            {
                for (int i = 0; i < staff.SlotCount; i++)
                {
                    if (staff.GetChild(i) is SyntaxTokenNode token &&
                        token.Kind == SyntaxKind.Identifier &&
                        token.Text != "staff" && token.Text != "treble" &&
                        token.Text != "bass" && token.Text != "alto" && token.Text != "tenor")
                    {
                        _voiceName = token.Text;
                        return;
                    }
                }
            }
        }
    }

    private List<Measure> CollectMeasures()
    {
        var builder = new MeasureBuilder(TimeSignatureFraction);

        void ProcessNodes(IEnumerable<SyntaxNode> nodes)
        {
            var nodeList = nodes.ToList();
            for (int i = 0; i < nodeList.Count; i++)
            {
                var node = nodeList[i];
                // Check if next node is a tie or slur
                bool hasTieAfter = i + 1 < nodeList.Count && nodeList[i + 1] is TieSyntax;
                bool hasSlurStartAfter = i + 1 < nodeList.Count && nodeList[i + 1] is SlurSyntax slurS && slurS.IsOpen;
                bool hasSlurEndAfter = i + 1 < nodeList.Count && nodeList[i + 1] is SlurSyntax slurE && !slurE.IsOpen;
                ProcessMusicNode(node, builder, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter);
            }
        }

        // Process based on structure or sections
        if (_structure != null)
        {
            ProcessStructure(ProcessNodes, builder);
        }
        else if (_sections.Count > 0)
        {
            foreach (var section in _sections.Values)
            {
                builder.SectionLabel = section.SectionName;
                ProcessSection(section, ProcessNodes);
            }
        }
        else if (_root != null)
        {
            var musicNodes = _root.DescendantNodes()
                .Where(n => !IsInsideTuplet(n) && n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or BreakSyntax or TieSyntax or SlurSyntax or TupletExpressionSyntax);
            ProcessNodes(musicNodes);
        }

        return builder.FinalizeMeasures();
    }

    private void ProcessStructure(Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
    {
        foreach (var child in _structure!.DescendantNodes())
        {
            switch (child)
            {
                case SectionReferenceSyntax reference:
                    // Skip if inside a repeat block (will be handled by ProcessRepeatBlock)
                    if (IsInsideRepeatBlock(reference))
                        break;
                    if (_sections.TryGetValue(reference.SectionName, out var section))
                    {
                        builder.SectionLabel = reference.SectionName;
                        ProcessSection(section, processNodes);
                    }
                    break;

                case StructureRepeatBlockSyntax repeat:
                    ProcessRepeatBlock(repeat, processNodes, builder);
                    break;
            }
        }
    }

    private static bool IsInsideRepeatBlock(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is StructureRepeatBlockSyntax)
                return true;
            parent = parent.Parent;
        }
        return false;
    }

    /// <summary>
    /// Checks if a node is inside a TupletExpression (to avoid double-counting).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc - notes inside tuplets are processed together
    /// </remarks>
    private static bool IsInsideTuplet(SyntaxNode node)
    {
        // TupletExpressionSyntax itself is not "inside" a tuplet
        if (node is TupletExpressionSyntax)
            return false;

        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is TupletExpressionSyntax)
                return true;
            parent = parent.Parent;
        }
        return false;
    }

    private void ProcessRepeatBlock(StructureRepeatBlockSyntax repeat, Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
    {
        bool afterRepeatStart = false;
        var pendingVoltaBrackets = new List<(int startMeasure, int endMeasure, string voltaText, int sourcePosition)>();

        for (int i = 0; i < repeat.SlotCount; i++)
        {
            var child = repeat.GetChild(i);

            if (child is SyntaxTokenNode token)
            {
                if (token.Text == "|:")
                {
                    processNodes(new[] { CreateBarlineSyntax(token.Text, token.Position) });
                    afterRepeatStart = true;
                }
                else if (token.Text == ":|")
                {
                    processNodes(new[] { CreateBarlineSyntax(token.Text, token.Position) });
                }
            }
            else if (afterRepeatStart)
            {
                if (child is SectionReferenceSyntax reference)
                {
                    if (_sections.TryGetValue(reference.SectionName, out var section))
                    {
                        builder.SectionLabel = reference.SectionName;
                        ProcessSection(section, processNodes);
                    }
                }
                else if (child is StructureAlternativeSyntax alt)
                {
                    string altSectionName = alt.SectionName.Text;
                    if (_sections.TryGetValue(altSectionName, out var section))
                    {
                        // Track measure index before processing this alternative
                        int startMeasureIndex = builder.CurrentMeasureIndex;

                        builder.SectionLabel = altSectionName;
                        ProcessSection(section, processNodes);

                        // Track measure index after processing
                        int endMeasureIndex = builder.CurrentMeasureIndex;
                        // If we're mid-measure, include that measure
                        if (builder.CurrentItemCount > 0)
                            endMeasureIndex++;

                        // Collect volta bracket info if bracket style
                        if (alt.HasBracket && !alt.IsSilent)
                        {
                            pendingVoltaBrackets.Add((startMeasureIndex, endMeasureIndex, alt.VoltaText, alt.Position));
                        }
                    }
                }
            }
        }

        // Add all volta brackets - last one is closed, others are open
        for (int i = 0; i < pendingVoltaBrackets.Count; i++)
        {
            var (startMeasure, endMeasure, voltaText, sourcePosition) = pendingVoltaBrackets[i];
            bool isClosed = (i == pendingVoltaBrackets.Count - 1);
            _voltaBrackets.Add(new VoltaBracketItem(startMeasure, endMeasure, voltaText, isClosed, sourcePosition));
        }
    }

    private void ProcessSection(SectionDeclarationSyntax section, Action<IEnumerable<SyntaxNode>> processNodes)
    {
        foreach (var child in section.DescendantNodes())
        {
            if (child is PartBlockSyntax partBlock)
            {
                if (_voiceName == null || partBlock.Name == _voiceName)
                {
                    ProcessPartBlock(partBlock, processNodes);

                    if (_voiceName != null) return;
                }
            }
        }
    }

    private void ProcessPartBlock(PartBlockSyntax partBlock, Action<IEnumerable<SyntaxNode>> processNodes)
    {
        // Collect all music nodes, expanding variable references
        var musicNodes = new List<SyntaxNode>();

        foreach (var node in partBlock.DescendantNodes())
        {
            // Skip nodes inside tuplets (they'll be processed by the tuplet)
            if (IsInsideTuplet(node))
                continue;

            switch (node)
            {
                case NoteSyntax:
                case RestSyntax:
                case ChordSyntax:
                case BarlineSyntax:
                case BreakSyntax:
                case TieSyntax:
                case SlurSyntax:
                case TupletExpressionSyntax:
                    musicNodes.Add(node);
                    break;

                case VariableReferenceSyntax varRef:
                    ExpandVariable(varRef.Name.Text, musicNodes);
                    break;
            }
        }

        processNodes(musicNodes);
    }

    private void ExpandVariable(string name, List<SyntaxNode> musicNodes)
    {
        if (!_variables.TryGetValue(name, out var expression))
            return;

        // Include expression itself if it is a music node
        if (expression is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or TieSyntax or SlurSyntax)
        {
            musicNodes.Add(expression);
        }

        // Get music nodes from the variable expression descendants
        var nodes = expression.DescendantNodes()
            .Where(n => n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or TieSyntax or SlurSyntax);

        musicNodes.AddRange(nodes);
    }

    private static BarlineSyntax CreateBarlineSyntax(string barText, int position)
    {
        var kind = barText switch
        {
            "|:" => SyntaxKind.RepeatStartBar,
            ":|" => SyntaxKind.RepeatEndBar,
            "||" => SyntaxKind.DoubleBar,
            "|." => SyntaxKind.FinalBar,
            _ => SyntaxKind.Bar
        };

        var token = new LilySharp.Core.Syntax.InternalSyntax.SyntaxToken(kind, barText);
        var green = new LilySharp.Core.Syntax.InternalSyntax.BarlineGreen(token);
        return new BarlineSyntax(green, null, position);
    }

    private void InitializeRelativeMode(PitchSyntax basePitch)
    {
        _lastPitchName = basePitch.PitchName[0];
        _currentOctave = 4 + basePitch.OctaveOffset;
    }

    /// <summary>
    /// Collects dynamic markings from note/chord modifiers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: dynamic-engraver.cc:36-61 Dynamic_engraver::listen_dynamic
    /// </remarks>
    private void CollectDynamics(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var articulation in articulations)
        {
            if (articulation is DynamicSyntax dynamicSyntax)
            {
                var level = dynamicSyntax.Level;
                if (level != DynamicLevel.None)
                {
                    _dynamics.Add(new DynamicItem(level, measureIndex, itemIndex, dynamicSyntax.Position));
                }
            }
        }
    }

    /// <summary>
    /// Collects articulation marks from note/chord modifiers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: script-engraver.cc:92-125 Script_engraver::acknowledge_note_head
    /// </remarks>
    private void CollectArticulations(SyntaxNode node, int measureIndex, int itemIndex, bool stemUp)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var articulation in articulations)
        {
            if (articulation is ArticulationSyntax articulationSyntax)
            {
                var type = articulationSyntax.Type;
                if (type != ArticulationType.None)
                {
                    // LILYPOND-REF: script-interface.cc:23-45 direction calculation
                    // Articulations go opposite to stem direction by default
                    bool isAbove = !stemUp;

                    // Fermata and ornaments always go above
                    // LILYPOND-REF: define-grobs.scm:1365 fermata: direction = UP
                    // LILYPOND-REF: define-grobs.scm:2175 ornaments: direction = UP
                    if (type == ArticulationType.Fermata ||
                        type == ArticulationType.Trill ||
                        type == ArticulationType.Mordent ||
                        type == ArticulationType.Prall ||
                        type == ArticulationType.Turn ||
                        type == ArticulationType.InvertedTurn ||
                        type == ArticulationType.PrallTriller)
                    {
                        isAbove = true;
                    }

                    _articulations.Add(new ArticulationItem(type, measureIndex, itemIndex, isAbove, articulationSyntax.Position));
                }
            }
        }
    }

    /// <summary>
    /// Collects grace notes from a grace expression.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: grace-engraver.cc:36-80 Grace_engraver class
    /// </remarks>
    private void CollectGraceNotes(GraceExpressionSyntax grace, int measureIndex, int mainNoteItemIndex)
    {
        var type = grace.IsAcciaccatura ? GraceNoteType.Acciaccatura
                 : grace.IsAppoggiatura ? GraceNoteType.Appoggiatura
                 : GraceNoteType.Grace;

        // Collect notes from the grace body
        var graceNoteInfos = new List<GraceNoteInfo>();

        foreach (var item in grace.Body.Items)
        {
            if (item is NoteSyntax note)
            {
                var (staffPosition, octave) = CalculateStaffPosition(note.Pitch);
                _currentOctave = octave;

                bool needsLedger = staffPosition <= -6 || staffPosition >= 6;
                string? accidental = note.Pitch.AccidentalOffset switch
                {
                    2 => "doubleSharp",
                    1 => "sharp",
                    -1 => "flat",
                    -2 => "doubleFlat",
                    _ => null
                };

                graceNoteInfos.Add(new GraceNoteInfo(staffPosition, accidental, needsLedger));
            }
        }

        if (graceNoteInfos.Count > 0)
        {
            _graceNotes.Add(new GraceNoteItem(
                type,
                graceNoteInfos.ToImmutableArray(),
                measureIndex,
                mainNoteItemIndex,
                grace.Position));
        }
    }

    private NoteItem CreateNoteItem(NoteSyntax note, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false)
    {
        var (staffPosition, octave) = CalculateStaffPosition(note.Pitch);
        _currentOctave = octave;

        int noteValue = note.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (note.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = note.Duration?.DotCount ?? 0;
        bool needsLedger = staffPosition <= -6 || staffPosition >= 6;

        // Parse tremolo suffix (:8 = 1 beam, :16 = 2 beams, :32 = 3 beams)
        int tremoloBeams = ParseTremoloBeams(note.Tremolo);

        string? accidental = note.Pitch.AccidentalOffset switch
        {
            2 => "doubleSharp",
            1 => "sharp",
            -1 => "flat",
            -2 => "doubleFlat",
            _ => null
        };

        return new NoteItem(
            staffPosition,
            Fraction.FromNoteValue(noteValue),
            dots,
            accidental,
            needsLedger,
            note.Position,
            tremoloBeams,
            hasTieStart: hasTieAfter,
            hasSlurStart: hasSlurStartAfter,
            hasSlurEnd: hasSlurEndAfter);
    }

    private RestItem CreateRestItem(RestSyntax rest)
    {
        int noteValue = rest.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (rest.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = rest.Duration?.DotCount ?? 0;

        return new RestItem(Fraction.FromNoteValue(noteValue), dots, rest.Position);
    }

    /// <summary>
    /// Parses tremolo suffix into beam count.
    /// :8 = 1 beam, :16 = 2 beams, :32 = 3 beams
    /// </summary>
    private static int ParseTremoloBeams(SyntaxTokenNode? tremolo)
    {
        if (tremolo == null)
            return 0;

        // Tremolo text is ":8", ":16", or ":32"
        var text = tremolo.Text;
        if (text.Length < 2 || text[0] != ':')
            return 0;

        return text[1..] switch
        {
            "8" => 1,
            "16" => 2,
            "32" => 3,
            _ => 0
        };
    }

    private ChordItem CreateChordItem(ChordSyntax chord)
    {
        var notes = new List<ChordNoteInfo>();

        // Track first note's state for subsequent chord/note relative calculation
        int firstOctave = _currentOctave;
        char firstPitchName = _lastPitchName;

        foreach (var pitch in chord.Pitches)
        {
            var (staffPosition, octave) = CalculateStaffPosition(pitch);
            _currentOctave = octave;

            // Remember first pitch's state
            if (notes.Count == 0)
            {
                firstOctave = octave;
                firstPitchName = pitch.PitchName.ToLowerInvariant()[0];
            }

            string? accidental = pitch.AccidentalOffset switch
            {
                2 => "doubleSharp",
                1 => "sharp",
                -1 => "flat",
                -2 => "doubleFlat",
                _ => null
            };

            bool needsLedger = staffPosition <= -6 || staffPosition >= 6;
            notes.Add(new ChordNoteInfo(staffPosition, accidental, needsLedger));
        }

        // Next chord/note is relative to first pitch of this chord (Lilypond spec)
        _currentOctave = firstOctave;
        _lastPitchName = firstPitchName;

        int noteValue = chord.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (chord.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = chord.Duration?.DotCount ?? 0;
        int tremoloBeams = ParseTremoloBeams(chord.Tremolo);

        return new ChordItem(notes.ToImmutableArray(), Fraction.FromNoteValue(noteValue), dots, chord.Position, tremoloBeams);
    }

    private (int staffPosition, int octave) CalculateStaffPosition(PitchSyntax pitch)
    {
        char pitchName = pitch.PitchName.ToLowerInvariant()[0];

        // Calculate base octave from interval (without OctaveOffset)
        int interval = GetPitchIndex(pitchName) - GetPitchIndex(_lastPitchName);
        int baseOctave = _currentOctave;
        if (interval > 3) baseOctave--;
        else if (interval < -3) baseOctave++;

        // Apply OctaveOffset for this note only (does not affect next note)
        int actualOctave = baseOctave + pitch.OctaveOffset;

        // Staff position 0 = middle line of the staff
        // Treble clef: B4 = staff position 0
        // Bass clef: D3 = staff position 0
        int basePosition = _clef switch
        {
            "treble" => GetPitchIndex(pitchName) - GetPitchIndex('b') + (actualOctave - 4) * 7,
            "bass" => GetPitchIndex(pitchName) - GetPitchIndex('d') + (actualOctave - 3) * 7,
            _ => GetPitchIndex(pitchName) - GetPitchIndex('b') + (actualOctave - 4) * 7
        };

        _lastPitchName = pitchName;

        // Return actualOctave - next note is calculated relative to actual pitch
        return (basePosition, actualOctave);
    }

    private static int GetPitchIndex(char pitch) => pitch switch
    {
        'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3, 'g' => 4, 'a' => 5, 'b' => 6,
        _ => 0
    };

    private static BarlineType ParseBarlineType(string text) => text switch
    {
        "|:" => BarlineType.RepeatStart,
        ":|" => BarlineType.RepeatEnd,
        ":|:" => BarlineType.RepeatBoth,
        "||" => BarlineType.Double,
        "|." => BarlineType.Final,
        _ => BarlineType.Single
    };

    /// <summary>
    /// Collects lyrics from LyricsBlockSyntax nodes and associates them with notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:60-88 process_music
    /// LILYPOND-REF: lily/lyric-combine-music-iterator.cc:100-150 note association
    /// </remarks>
    private void CollectLyrics(SyntaxNode root, List<Measure> measures)
    {
        // Find all LyricsBlockSyntax nodes
        var lyricsBlocks = root.DescendantNodes()
            .OfType<LyricsBlockSyntax>()
            .ToList();

        if (lyricsBlocks.Count == 0)
            return;

        // Build note indices: (measureIndex, itemIndex) for each note/chord
        var noteIndices = new List<(int MeasureIndex, int ItemIndex)>();
        for (int m = 0; m < measures.Count; m++)
        {
            var measure = measures[m];
            for (int i = 0; i < measure.Items.Length; i++)
            {
                var item = measure.Items[i];
                // Only notes and chords get lyrics (not rests)
                if (item is NoteItem or ChordItem)
                {
                    noteIndices.Add((m, i));
                }
            }
        }

        // Collect lyrics from each block
        var lyricCollector = new LyricCollector();
        int verseNumber = 1;
        foreach (var lyricsBlock in lyricsBlocks)
        {
            var lyrics = lyricCollector.Collect(lyricsBlock, noteIndices, voiceId: 0, verseNumber);
            _lyrics.AddRange(lyrics);
            verseNumber++;
        }
    }
}
