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

        // Auto-set final barline on the last measure (music convention)
        if (_measures.Count > 0)
        {
            var last = _measures[^1];
            if (last.EndBarline == BarlineType.Single)
            {
                _measures[^1] = new Measure(
                    last.Items, last.StartBarline, BarlineType.Final,
                    last.SectionLabel, last.SourceStart, last.SourceEnd, last.HasBreakAfter);
            }
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
    private int _initialOctave = 4;  // Reset target for section boundaries
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
    // Arpeggio markings
    private readonly List<ArpeggioItem> _arpeggios = new();
    // Figured bass
    private readonly List<FiguredBassItem> _figuredBasses = new();
    // Chord names
    private readonly List<ChordNameItem> _chordNames = new();
    // Percent repeats
    private readonly List<PercentRepeatItem> _percentRepeats = new();
    // Cross-staff items
    private readonly List<CrossStaffItem> _crossStaffItems = new();
    // Grob property overrides and reverts
    private readonly List<GrobOverride> _grobOverrides = new();
    private readonly List<GrobRevert> _grobReverts = new();
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
    private string _initialClef = "treble"; // Preserved for Score.Clef (not mutated by mid-measure clef changes)

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

        // Phase 1.5: If voiceName specified, look up clef and octave from part definition
        if (voiceName != null)
        {
            var (partClef, partOctave) = GetPartDefaults(tree.GetRoot(), voiceName);
            if (partClef != null)
                _clef = partClef;
            _currentOctave = partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
        }
        else
        {
            _currentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
        }
        _initialOctave = _currentOctave;
        _initialClef = _clef; // Preserve initial clef before music processing

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
            _initialClef, // Use initial clef, not the final state after clef changes
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
            tupletBrackets: _tupletBrackets.ToImmutableArray(),
            arpeggios: _arpeggios.ToImmutableArray(),
            figuredBasses: _figuredBasses.ToImmutableArray(),
            chordNames: _chordNames.ToImmutableArray(),
            percentRepeats: _percentRepeats.ToImmutableArray(),
            crossStaffItems: _crossStaffItems.ToImmutableArray(),
            grobOverrides: _grobOverrides.ToImmutableArray(),
            grobReverts: _grobReverts.ToImmutableArray());
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

            // Set clef and octave for this voice from part definition
            var (partClef, partOctave) = GetPartDefaults(tree.GetRoot(), voiceName);
            _clef = partClef ?? "treble";

            // Set initial octave: explicit > instrument default > clef default
            _currentOctave = partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
            _initialOctave = _currentOctave;

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
            tupletBrackets: _tupletBrackets.ToImmutableArray(),
            dynamics: _dynamics.ToImmutableArray(),
            articulations: _articulations.ToImmutableArray(),
            graceNotes: _graceNotes.ToImmutableArray(),
            arpeggios: _arpeggios.ToImmutableArray(),
            figuredBasses: _figuredBasses.ToImmutableArray(),
            chordNames: _chordNames.ToImmutableArray(),
            percentRepeats: _percentRepeats.ToImmutableArray(),
            crossStaffItems: _crossStaffItems.ToImmutableArray());
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
            _initialClef, // Use initial clef, not the final state after clef changes
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
            tupletBrackets: _tupletBrackets.ToImmutableArray(),
            arpeggios: _arpeggios.ToImmutableArray(),
            figuredBasses: _figuredBasses.ToImmutableArray(),
            grobOverrides: _grobOverrides.ToImmutableArray(),
            grobReverts: _grobReverts.ToImmutableArray());
    }

    private List<Measure> CollectMeasuresFromNode(SyntaxNode voiceNode)
    {
        var builder = new MeasureBuilder(TimeSignatureFraction, voiceNode.Position);

        // Collect all music nodes, expanding variable references
        var musicNodes = new List<SyntaxNode>();

        foreach (var node in voiceNode.DescendantNodes())
        {
            // Skip nodes that are inside a tuplet, repeat, grace, or once (they'll be processed by those handlers)
            if (IsInsideTuplet(node) || IsInsideRepeat(node) || IsInsideOnce(node) || IsInsideGrace(node))
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
                case BeamMarkerSyntax:
                case GraceExpressionSyntax:
                case TupletExpressionSyntax:
                case RepeatExpressionSyntax:
                case MusicMarkSyntax:
                case OverrideDeclarationSyntax:
                case RevertDeclarationSyntax:
                case OnceModifierSyntax:
                case ClefDeclarationSyntax:
                    musicNodes.Add(node);
                    break;

                case VariableReferenceSyntax varRef:
                    ExpandVariable(varRef.Name.Text, musicNodes);
                    break;
            }
        }

        // Process collected music nodes (with lookahead for ties/slurs/beams)
        for (int i = 0; i < musicNodes.Count; i++)
        {
            var node = musicNodes[i];
            // Check if next node is a tie or slur
            bool hasTieAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is TieSyntax;
            bool hasSlurStartAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is SlurSyntax slurS && slurS.IsOpen;
            bool hasSlurEndAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is SlurSyntax slurE && !slurE.IsOpen;
            bool hasBeamStartAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is BeamMarkerSyntax beamS && beamS.IsStart;
            bool hasBeamEndAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is BeamMarkerSyntax beamE && !beamE.IsStart;
            ProcessMusicNode(node, builder, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
        }

        return builder.FinalizeMeasures();
    }

    private void ProcessMusicNode(SyntaxNode node, MeasureBuilder builder, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false)
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
                    // Process grace notes BEFORE the main note so they get correct octave context
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool hasGliss = HasGlissandoArticulation(note);
                    int featherDir = GetFeatherDirection(note);
                    var noteItem = CreateNoteItem(note, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter, hasGliss, featherDir);
                    builder.AddItem(noteItem);
                    CollectDynamics(note, measureIndex, itemIndex);
                    CollectArticulations(note, measureIndex, itemIndex, noteItem.StemUp);
                    CollectFiguredBass(note, measureIndex, itemIndex);
                    CollectChordNames(note, measureIndex, itemIndex);
                    CollectCrossStaff(note, measureIndex, itemIndex);
                }
                break;

            case RestSyntax rest:
                builder.AddItem(CreateRestItem(rest));
                break;

            case ChordSyntax chord:
                {
                    int measureIndex = builder.CurrentMeasureIndex;
                    int itemIndex = builder.CurrentItemCount;
                    // Process grace notes BEFORE the main chord so they get correct octave context
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool hasArpeggio = HasArpeggioArticulation(chord);
                    var chordItem = CreateChordItem(chord, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio);
                    builder.AddItem(chordItem);
                    CollectDynamics(chord, measureIndex, itemIndex);
                    // Use chord stem direction for articulation placement
                    CollectArticulations(chord, measureIndex, itemIndex, chordItem.StemUp);
                    CollectFiguredBass(chord, measureIndex, itemIndex);
                    CollectChordNames(chord, measureIndex, itemIndex);
                    CollectCrossStaff(chord, measureIndex, itemIndex);
                    // Collect arpeggio if present
                    if (hasArpeggio && chordItem.Notes.Length > 0)
                    {
                        int minPos = chordItem.Notes.Min(n => n.StaffPosition);
                        int maxPos = chordItem.Notes.Max(n => n.StaffPosition);
                        _arpeggios.Add(new ArpeggioItem(measureIndex, itemIndex, minPos, maxPos, chord.Position));
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

            case MusicMarkSyntax mark:
                {
                    var markType = MusicMarkItem.ParseMarkName(mark.MarkName);
                    if (markType != null)
                    {
                        if (markType.Value == MusicMarkType.Rehearsal)
                        {
                            string text = MusicMarkItem.ParseRehearsalText(mark.MarkName);
                            _musicMarks.Add(new MusicMarkItem(MusicMarkType.Rehearsal, text, builder.CurrentMeasureIndex, mark.Position));
                        }
                        else
                        {
                            _musicMarks.Add(new MusicMarkItem(markType.Value, builder.CurrentMeasureIndex, mark.Position));
                        }
                    }
                }
                break;

            case ClefDeclarationSyntax clefDecl:
                {
                    // Mid-measure clef change
                    // LILYPOND-REF: lily/clef-engraver.cc — inspect_clef_properties()
                    string newClef = clefDecl.ClefName.Text.ToLowerInvariant();
                    _clef = newClef;
                    _currentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
                    var clefChange = new ClefChangeItem(ParseClefType(newClef), clefDecl.Position);
                    builder.AddItem(clefChange);
                }
                break;

            case TieSyntax:
            case SlurSyntax:
            case BeamMarkerSyntax:
                // Already processed with the preceding note
                break;

            case TupletExpressionSyntax tuplet:
                // LILYPOND-REF: lily/tuplet-engraver.cc - process tuplet as a unit
                ProcessTuplet(tuplet, builder, nestingDepth: 0);
                break;

            case RepeatExpressionSyntax repeat:
                // LILYPOND-REF: lily/percent-repeat-engraver.cc - percent repeat handling
                ProcessRepeatExpression(repeat, builder);
                break;

            case OverrideDeclarationSyntax overrideDecl:
                CollectOverride(overrideDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: false);
                break;

            case RevertDeclarationSyntax revertDecl:
                CollectRevert(revertDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount);
                break;

            case OnceModifierSyntax onceModifier:
                if (onceModifier.Command is OverrideDeclarationSyntax innerOverride)
                    CollectOverride(innerOverride, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: true);
                else if (onceModifier.Command is RevertDeclarationSyntax innerRevert)
                    CollectRevert(innerRevert, builder.CurrentMeasureIndex, builder.CurrentItemCount);
                break;
        }
    }

    /// <summary>
    /// Processes a tuplet expression, collecting notes and creating a bracket item.
    /// Supports nested tuplets via recursive calls with increasing nesting depth.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-engraver.cc - Tuplet_engraver class
    /// LILYPOND-REF: lily/tuplet-bracket.cc:400-500 - nested bracket stacking
    ///
    /// For nested tuplets, duration scaling compounds:
    /// outer 3/2 containing inner 3/2 { e8 f g } →
    /// inner actual = 3/8 * 2/3 = 1/4, then outer scales again.
    /// Only the top-level tuplet (nestingDepth=0) adds duration to the measure.
    /// </remarks>
    /// <returns>The actual (scaled) duration of this tuplet.</returns>
    private Fraction ProcessTuplet(TupletExpressionSyntax tuplet, MeasureBuilder builder, int nestingDepth)
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
            else if (item is TupletExpressionSyntax nestedTuplet)
            {
                // LILYPOND-REF: lily/tuplet-bracket.cc - nested tuplet processing
                // Recursively process nested tuplet; its actual duration
                // counts as "written" duration for this outer tuplet
                Fraction nestedActualDuration = ProcessTuplet(nestedTuplet, builder, nestingDepth + 1);
                writtenDuration += nestedActualDuration;
                lastSourcePosition = nestedTuplet.Position;
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

        // Only add duration to the measure at the top level
        // Nested tuplets return their duration to the parent for compounding
        if (nestingDepth == 0)
        {
            builder.AddDuration(actualDuration, lastSourcePosition + 1);
        }

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
                tuplet.Position,
                nestingDepth
            ));
        }

        return actualDuration;
    }

    private void Reset()
    {
        _sections.Clear();
        _variables.Clear();
        _dynamics.Clear();
        _articulations.Clear();
        _graceNotes.Clear();
        _arpeggios.Clear();
        _figuredBasses.Clear();
        _chordNames.Clear();
        _percentRepeats.Clear();
        _crossStaffItems.Clear();
        _grobOverrides.Clear();
        _grobReverts.Clear();
        _structure = null;
        _root = null;
        _currentOctave = 4;
        _initialOctave = 4;
        _lastPitchName = 'c';
        _defaultDuration = Fraction.Quarter;
        _title = null;
        _composer = null;
        _tempo = null;
        _timeBeats = 4;
        _timeBeatType = 4;
        _keySharps = 0;
        _clef = "treble";
        _initialClef = "treble";
    }

    /// <summary>
    /// Looks up clef and octave defaults from a part definition by name.
    /// Priority: explicit attributes > instrument defaults > clef-based defaults.
    /// </summary>
    private static (string? clef, int? octave) GetPartDefaults(SyntaxNode root, string partName)
    {
        foreach (var partDecl in root.DescendantNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;

            string? clef = null;
            string? instrument = null;
            int? octave = null;

            // Check properties for clef, instrument, and octave
            foreach (var prop in partDecl.Properties)
            {
                var propName = prop.NameToken.Text.ToLowerInvariant();
                var valueToken = prop.GetChild(2) as SyntaxTokenNode;
                if (valueToken == null) continue;

                if (propName == "clef")
                    clef = valueToken.Text.ToLowerInvariant();
                else if (propName == "instrument")
                    instrument = valueToken.Text.ToLowerInvariant();
                else if (propName == "octave" && int.TryParse(valueToken.Text, out var oct))
                    octave = oct;
            }

            // Resolve clef: explicit > instrument > null
            string? resolvedClef = clef;
            int? resolvedOctave = octave;

            if (instrument != null)
            {
                var (defaultClef, defaultOctave) = InstrumentDefaults.GetDefaults(instrument);
                resolvedClef ??= defaultClef switch
                {
                    ClefType.Treble => "treble",
                    ClefType.Bass => "bass",
                    ClefType.Alto => "alto",
                    ClefType.Tenor => "tenor",
                    ClefType.Treble8Below => "treble_8",
                    _ => "treble"
                };
                resolvedOctave ??= defaultOctave;
            }

            return (resolvedClef, resolvedOctave);
        }

        return (null, null);
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

    // LILYPOND-REF: lily/accidental-engraver.cc
    // Sharp order: F C G D A E B (steps 3,0,4,1,5,2,6)
    // Flat order:  B E A D G C F (steps 6,2,5,1,4,0,3)
    private static readonly int[] SharpOrder = { 3, 0, 4, 1, 5, 2, 6 };
    private static readonly int[] FlatOrder = { 6, 2, 5, 1, 4, 0, 3 };

    /// <summary>
    /// Gets the expected alteration for a pitch step based on the current key signature.
    /// </summary>
    private int GetKeySignatureAlteration(int step)
    {
        if (_keySharps > 0)
        {
            for (int i = 0; i < _keySharps && i < SharpOrder.Length; i++)
                if (SharpOrder[i] == step) return 1;
        }
        else if (_keySharps < 0)
        {
            int flatCount = -_keySharps;
            for (int i = 0; i < flatCount && i < FlatOrder.Length; i++)
                if (FlatOrder[i] == step) return -1;
        }
        return 0;
    }

    /// <summary>
    /// Determines the displayed accidental for a pitch, considering the key signature.
    /// Returns null if the pitch's alteration matches the key signature (no accidental needed).
    /// </summary>
    private string? GetDisplayAccidental(PitchSyntax pitch)
    {
        int step = PitchNameToStep(pitch.BaseName);
        int expected = GetKeySignatureAlteration(step);
        int actual = pitch.AccidentalOffset;

        if (actual == expected)
            return null;

        return actual switch
        {
            2 => "doubleSharp",
            1 => "sharp",
            0 => "natural",
            -1 => "flat",
            -2 => "doubleFlat",
            _ => null
        };
    }

    private static int PitchNameToStep(char name) => char.ToLower(name) switch
    {
        'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3, 'g' => 4, 'a' => 5, 'b' => 6,
        _ => 0
    };

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
                // Check if next node is a tie, slur, or beam marker
                bool hasTieAfter = i + 1 < nodeList.Count && nodeList[i + 1] is TieSyntax;
                bool hasSlurStartAfter = i + 1 < nodeList.Count && nodeList[i + 1] is SlurSyntax slurS && slurS.IsOpen;
                bool hasSlurEndAfter = i + 1 < nodeList.Count && nodeList[i + 1] is SlurSyntax slurE && !slurE.IsOpen;
                bool hasBeamStartAfter = i + 1 < nodeList.Count && nodeList[i + 1] is BeamMarkerSyntax beamS && beamS.IsStart;
                bool hasBeamEndAfter = i + 1 < nodeList.Count && nodeList[i + 1] is BeamMarkerSyntax beamE && !beamE.IsStart;
                ProcessMusicNode(node, builder, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
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
                .Where(n => !IsInsideTuplet(n) && !IsInsideRepeat(n) && !IsInsideOnce(n) && !IsInsideGrace(n) && n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or BreakSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax or GraceExpressionSyntax or TupletExpressionSyntax or RepeatExpressionSyntax or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax);
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
    /// Top-level TupletExpressionSyntax nodes pass through (processed by main loop).
    /// Nested TupletExpressionSyntax nodes are filtered (processed recursively by ProcessTuplet).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc - notes inside tuplets are processed together
    /// </remarks>
    private static bool IsInsideTuplet(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is TupletExpressionSyntax)
                return true;
            parent = parent.Parent;
        }
        return false;
    }

    /// <summary>
    /// Checks if a node is inside an OnceModifierSyntax.
    /// Prevents double-processing of inner override/revert in once modifier.
    /// </summary>
    private static bool IsInsideOnce(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is OnceModifierSyntax)
                return true;
            parent = parent.Parent;
        }
        return false;
    }

    /// <summary>
    /// Checks if a node is inside a RepeatExpressionSyntax.
    /// Prevents double-processing of notes inside repeat expressions.
    /// </summary>
    /// <summary>
    /// Checks if a node is inside a GraceExpressionSyntax.
    /// Prevents double-processing of notes inside grace expressions.
    /// </summary>
    private static bool IsInsideGrace(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is GraceExpressionSyntax)
                return true;
            parent = parent.Parent;
        }
        return false;
    }

    private static bool IsInsideRepeat(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is RepeatExpressionSyntax)
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
                        // endMeasureIndex is exclusive (one-past-end); convert to inclusive
                        // for VoltaBracketItem which stores the last measure index
                        if (alt.HasBracket && !alt.IsSilent)
                        {
                            int lastMeasure = Math.Max(startMeasureIndex, endMeasureIndex - 1);
                            pendingVoltaBrackets.Add((startMeasureIndex, lastMeasure, alt.VoltaText, alt.Position));
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
        // Reset octave to initial value at each section boundary
        _currentOctave = _initialOctave;
        _lastPitchName = 'c';

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
            // Skip nodes inside tuplets, repeats, or grace expressions (they'll be processed by those handlers)
            if (IsInsideTuplet(node) || IsInsideRepeat(node) || IsInsideGrace(node))
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
                case BeamMarkerSyntax:
                case GraceExpressionSyntax:
                case TupletExpressionSyntax:
                case RepeatExpressionSyntax:
                case MusicMarkSyntax:
                case ClefDeclarationSyntax:
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
        if (expression is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax
            or GraceExpressionSyntax or TupletExpressionSyntax or RepeatExpressionSyntax
            or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax or MusicMarkSyntax or BreakSyntax
            or ClefDeclarationSyntax)
        {
            musicNodes.Add(expression);
        }

        // Get music nodes from the variable expression descendants
        // Skip nodes inside containers (grace, tuplet, repeat, once) - they'll be processed by those handlers
        var nodes = expression.DescendantNodes()
            .Where(n => !IsInsideGrace(n) && !IsInsideTuplet(n) && !IsInsideRepeat(n) && !IsInsideOnce(n)
                && n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax
                or GraceExpressionSyntax or TupletExpressionSyntax or RepeatExpressionSyntax
                or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax or MusicMarkSyntax or BreakSyntax
                or ClefDeclarationSyntax);

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
                else
                {
                    // @cresc, @decresc, @dim — parsed as DynamicSyntax but Level=None
                    // Collect as MusicMark for hairpin detection
                    var markName = dynamicSyntax.DynamicToken.Text;
                    var markType = MusicMarkItem.ParseMarkName(markName);
                    if (markType != null)
                    {
                        _musicMarks.Add(new MusicMarkItem(markType.Value, measureIndex, dynamicSyntax.Position));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a chord has an @arpeggio articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc - arpeggio marking
    /// </remarks>
    private static bool HasArpeggioArticulation(SyntaxNode node)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var art in articulations)
        {
            if (art is ArticulationSyntax artSyntax &&
                artSyntax.NameToken.Text == "arpeggio")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a note or chord has a @gliss articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/glissando-engraver.cc - Glissando_engraver::listen_glissando
    /// </remarks>
    private static bool HasGlissandoArticulation(SyntaxNode node)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var art in articulations)
        {
            if (art is ArticulationSyntax artSyntax &&
                artSyntax.NameToken.Text == "gliss")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Processes a repeat expression (volta, unfold, percent, tremolo).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/percent-repeat-engraver.cc - percent repeat handling
    /// LILYPOND-REF: lily/percent-repeat-iterator.cc - type determination
    ///
    /// For percent repeats: body is unfolded N times, iterations 2+ marked with PercentRepeatItem.
    /// For volta/unfold: body is simply unfolded N times (basic implementation).
    /// </remarks>
    private void ProcessRepeatExpression(RepeatExpressionSyntax repeat, MeasureBuilder builder)
    {
        string type = repeat.RepeatType.Text;
        int count = int.TryParse(repeat.Count.Text, out int c) ? c : 2;

        if (type == "percent")
        {
            // First iteration: process body normally
            int startMeasure = builder.CurrentMeasureIndex;
            foreach (var item in repeat.Body.Items)
                ProcessMusicNode(item, builder);
            int bodyMeasureCount = builder.CurrentMeasureIndex - startMeasure;

            // Additional iterations: process body again but mark as percent repeat
            for (int iter = 1; iter < count; iter++)
            {
                int iterStart = builder.CurrentMeasureIndex;
                foreach (var item in repeat.Body.Items)
                    ProcessMusicNode(item, builder);

                // Mark all measures in this iteration as percent repeats
                for (int m = 0; m < bodyMeasureCount; m++)
                {
                    _percentRepeats.Add(new PercentRepeatItem(
                        iterStart + m,
                        repeat.Position));
                }
            }
        }
        else
        {
            // For volta/unfold/tremolo: unfold body count times (basic implementation)
            for (int i = 0; i < count; i++)
            {
                foreach (var item in repeat.Body.Items)
                    ProcessMusicNode(item, builder);
            }
        }
    }

    /// <summary>
    /// Collects figured bass annotations from note/chord modifiers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc - listen_bass_figure
    /// Syntax: @fig.6 (single), @fig.6.4 (two figures), @fig.6.s (with sharp)
    /// </remarks>
    private void CollectFiguredBass(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var child in articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var figures = FiguredBassItem.ParseFigures(markSyntax.MarkName);
                if (figures != null)
                {
                    _figuredBasses.Add(new FiguredBassItem(
                        figures.Value,
                        measureIndex,
                        itemIndex,
                        markSyntax.Position));
                }
            }
        }
    }

    /// <summary>
    /// Collects chord name annotations (@chord.TEXT) from a note or chord's articulations.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm:1309 - Current_chord_text_engraver
    /// Syntax: @chord.Cm7, @chord.Bb7, @chord.Am
    /// </remarks>
    private void CollectChordNames(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var child in articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var chordText = ChordNameItem.ParseChordName(markSyntax.MarkName);
                if (chordText != null)
                {
                    _chordNames.Add(new ChordNameItem(
                        chordText,
                        measureIndex,
                        itemIndex,
                        markSyntax.Position));
                }
            }
        }
    }

    /// <summary>
    /// Detects @cross annotation on a note or chord for cross-staff rendering.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:1451-1459 - cross-staff detection
    /// Syntax: @cross marks a note for rendering on the other staff in a grand staff.
    ///
    /// In a grand staff context:
    /// - If voice is on staff 0 (treble), @cross moves to staff 1 (bass)
    /// - If voice is on staff 1 (bass), @cross moves to staff 0 (treble)
    /// The TargetStaffIndex is resolved later during layout based on voice assignment.
    /// Here we use 0 as a placeholder (actual target resolved by layout engine).
    /// </remarks>
    private void CollectCrossStaff(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var child in articulations)
        {
            // @cross is parsed as ArticulationSyntax (single Identifier, no dot)
            if (child is ArticulationSyntax artSyntax && artSyntax.NameToken.Text == "cross")
            {
                _crossStaffItems.Add(new CrossStaffItem(
                    measureIndex,
                    itemIndex,
                    0,
                    artSyntax.Position));
                return;
            }
        }
    }

    /// <summary>
    /// Collects a grob property override from an OverrideDeclarationSyntax.
    /// LILYPOND-REF: lily/context-property.cc (push)
    /// </summary>
    private void CollectOverride(OverrideDeclarationSyntax node, int measureIndex, int itemIndex, bool isOnce)
    {
        string grobType = node.GrobName.Text;
        string propertyName = node.PropertyName.Text;
        string value = node.ValueToken.Text;
        _grobOverrides.Add(new GrobOverride(grobType, propertyName, value, measureIndex, itemIndex, isOnce));
    }

    /// <summary>
    /// Collects a grob property revert from a RevertDeclarationSyntax.
    /// LILYPOND-REF: lily/context-property.cc (pop)
    /// </summary>
    private void CollectRevert(RevertDeclarationSyntax node, int measureIndex, int itemIndex)
    {
        string grobType = node.GrobName.Text;
        string propertyName = node.PropertyName.Text;
        _grobReverts.Add(new GrobRevert(grobType, propertyName, measureIndex, itemIndex));
    }

    /// <summary>
    /// Gets the feathered beam direction from a note's articulations.
    /// Returns 0 (none), 1 (right/accel), or -1 (left/rit).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1039-1082 grow-direction
    /// Syntax: @feather.right (accelerando) or @feather.left (ritardando)
    /// </remarks>
    private static int GetFeatherDirection(SyntaxNode node)
    {
        if (node is not NoteSyntax note)
            return 0;

        foreach (var child in note.Articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var name = markSyntax.MarkName.ToLowerInvariant();
                if (name == "feather.right" || name == "feather.accel")
                    return 1;
                if (name == "feather.left" || name == "feather.rit")
                    return -1;
            }
        }
        return 0;
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
                else
                {
                    // Check if this articulation is a MusicMark (cresc, rit, mark.A, ottava, ped, etc.)
                    var markName = articulationSyntax.NameToken.Text;
                    var markType = MusicMarkItem.ParseMarkName(markName);
                    if (markType != null)
                    {
                        if (markType.Value == MusicMarkType.Rehearsal)
                        {
                            string text = MusicMarkItem.ParseRehearsalText(markName);
                            _musicMarks.Add(new MusicMarkItem(MusicMarkType.Rehearsal, text, measureIndex, articulationSyntax.Position));
                        }
                        else
                        {
                            _musicMarks.Add(new MusicMarkItem(markType.Value, measureIndex, articulationSyntax.Position));
                        }
                    }
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
                string? accidental = GetDisplayAccidental(note.Pitch);

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

    private NoteItem CreateNoteItem(NoteSyntax note, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasGlissando = false, int featherDirection = 0)
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

        string? accidental = GetDisplayAccidental(note.Pitch);

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
            hasSlurEnd: hasSlurEndAfter,
            hasBeamStart: hasBeamStartAfter,
            hasBeamEnd: hasBeamEndAfter,
            hasGlissando: hasGlissando,
            featherDirection: featherDirection);
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

    private ChordItem CreateChordItem(ChordSyntax chord, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasArpeggio = false)
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

            string? accidental = GetDisplayAccidental(pitch);

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

        return new ChordItem(notes.ToImmutableArray(), Fraction.FromNoteValue(noteValue), dots, chord.Position, tremoloBeams, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio);
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
            "treble" or "treble_8" => GetPitchIndex(pitchName) - GetPitchIndex('b') + (actualOctave - 4) * 7,
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

    private static ClefType ParseClefType(string clef) => clef switch
    {
        "bass" => ClefType.Bass,
        "alto" => ClefType.Alto,
        "tenor" => ClefType.Tenor,
        "treble_8" => ClefType.Treble8Below,
        _ => ClefType.Treble
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
