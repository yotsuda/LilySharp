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
/// Helper class for building measures from syntax nodes.
/// Supports both explicit barlines and automatic measure detection based on time signature.
/// </summary>
internal sealed class MeasureBuilder
{
    private readonly List<Measure> _measures = new();
    private readonly List<MusicItem> _currentItems = new();
    private readonly List<MeasureBoundary> _boundaries = new();
    
    private readonly Fraction _timeSignature;
    private Fraction _currentDuration = Fraction.Zero;
    private Fraction _defaultDuration = Fraction.Quarter;
    
    private BarlineType _pendingStartBarline = BarlineType.None;
    private bool _pendingBreak = false;
    private string? _sectionLabel;
    private int _measureSourceStart;
    
    public MeasureBuilder(Fraction timeSignature, int sourceStart = 0)
    {
        _timeSignature = timeSignature;
        _measureSourceStart = sourceStart;
    }
    
    public IReadOnlyList<MeasureBoundary> Boundaries => _boundaries;
    
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
    
    private Fraction GetItemDuration(MusicItem item)
    {
        Fraction baseDuration = item switch
        {
            NoteItem note => note.Duration,
            RestItem rest => rest.Duration,
            ChordItem chord => chord.Duration,
            _ => Fraction.Zero
        };
        
        // Apply dots
        int dots = item switch
        {
            NoteItem note => note.Dots,
            RestItem rest => rest.Dots,
            ChordItem chord => chord.Dots,
            _ => 0
        };
        
        var total = baseDuration;
        var dotValue = baseDuration;
        for (int i = 0; i < dots; i++)
        {
            dotValue = new Fraction(dotValue.Numerator, dotValue.Denominator * 2);
            total += dotValue;
        }
        
        // Update default duration
        if (baseDuration != Fraction.Zero)
            _defaultDuration = baseDuration;
        
        return total;
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
                BarlineType.Single,  // Auto-completed measures get single barline
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
    /// Handles an explicit barline, completing the current measure.
    /// </summary>
    public void HandleBarline(BarlineType barType, int position)
    {
        if (barType == BarlineType.RepeatStart)
        {
            if (_currentItems.Count > 0)
                CompleteMeasureExplicit(BarlineType.Single, position);
            _pendingStartBarline = BarlineType.RepeatStart;
            _measureSourceStart = position;
        }
        else if (barType == BarlineType.RepeatEnd && _currentItems.Count == 0 && _measures.Count > 0)
        {
            // :| after section end - modify last measure's end barline
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
            CompleteMeasureExplicit(barType, position);
        }
    }
    
    private void CompleteMeasureExplicit(BarlineType endBarline, int sourceEnd)
    {
        bool isAligned = _currentDuration == _timeSignature;
        
        if (_currentItems.Count > 0 || _pendingStartBarline != BarlineType.None)
        {
            // Apply pending break if any
            bool hasBreak = _pendingBreak;
            _pendingBreak = false;
            
            _measures.Add(new Measure(
                _currentItems.ToImmutableArray(),
                _pendingStartBarline,
                endBarline,
                _sectionLabel,
                _measureSourceStart,
                sourceEnd,
                hasBreakAfter: hasBreak));
            
            // Record boundary with explicit flag
            _boundaries.Add(new MeasureBoundary(
                sourceEnd,
                _currentDuration,
                IsExplicit: true,
                IsAligned: isAligned));
            
            _currentItems.Clear();
            _sectionLabel = null;
            _pendingStartBarline = BarlineType.None;
            _measureSourceStart = sourceEnd;
            _currentDuration = Fraction.Zero;
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
                BarlineType.Single,
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
        
        return new Score(
            voice,
            new TimeSignature(_timeBeats, _timeBeatType),
            new KeySignature(_keySharps),
            _clef,
            _tempo,
            _title,
            _composer);
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
            
            Console.Error.WriteLine($"[CollectMultiStaff] voice={voiceName}, clef={_clef}, octave={_currentOctave}");
            
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
            _composer);
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
            var voiceName = $"voice{voiceNumber}";
            voices.Add(new Voice(voiceName, measures.ToImmutableArray()));
            voiceNumber++;
            
            _currentOctave = savedOctave;
            _lastPitchName = savedPitch;
            _defaultDuration = savedDuration;
        }
        
        return new Score(
            voices.ToImmutableArray(),
            new TimeSignature(_timeBeats, _timeBeatType),
            new KeySignature(_keySharps),
            _clef,
            _tempo,
            _title,
            _composer);
    }
    
    private List<Measure> CollectMeasuresFromNode(SyntaxNode voiceNode)
    {
        var builder = new MeasureBuilder(TimeSignatureFraction, voiceNode.Position);
        
        // Collect all music nodes, expanding variable references
        var musicNodes = new List<SyntaxNode>();
        
        foreach (var node in voiceNode.DescendantNodes())
        {
            switch (node)
            {
                case NoteSyntax:
                case RestSyntax:
                case ChordSyntax:
                case BarlineSyntax:
                case BreakSyntax:
                    musicNodes.Add(node);
                    break;
                    
                case VariableReferenceSyntax varRef:
                    ExpandVariable(varRef.Name.Text, musicNodes);
                    break;
            }
        }
        
        // Process collected music nodes
        foreach (var node in musicNodes)
        {
            ProcessMusicNode(node, builder);
        }
        
        return builder.FinalizeMeasures();
    }
    
    private void ProcessMusicNode(SyntaxNode node, MeasureBuilder builder)
    {
        switch (node)
        {
            case NoteSyntax note:
                builder.AddItem(CreateNoteItem(note));
                break;
                
            case RestSyntax rest:
                builder.AddItem(CreateRestItem(rest));
                break;
                
            case ChordSyntax chord:
                builder.AddItem(CreateChordItem(chord));
                break;
                
            case BarlineSyntax barline:
                var barType = ParseBarlineType(barline.BarToken.Text);
                builder.HandleBarline(barType, barline.Position);
                break;
                
            case BreakSyntax:
                // 'break' keyword triggers line break
                builder.SetBreak();
                break;
        }
    }
    
    private void Reset()
    {
        _sections.Clear();
        _variables.Clear();
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
            foreach (var node in nodes)
            {
                ProcessMusicNode(node, builder);
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
                .Where(n => n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or BreakSyntax);
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
    
    private void ProcessRepeatBlock(StructureRepeatBlockSyntax repeat, Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
    {
        bool afterRepeatStart = false;
        
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
                        builder.SectionLabel = altSectionName;
                        ProcessSection(section, processNodes);
                    }
                }
            }
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
            switch (node)
            {
                case NoteSyntax:
                case RestSyntax:
                case ChordSyntax:
                case BarlineSyntax:
                case BreakSyntax:
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
        if (expression is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax)
        {
            musicNodes.Add(expression);
        }
        
        // Get music nodes from the variable expression descendants
        var nodes = expression.DescendantNodes()
            .Where(n => n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax);
        
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
    
    private NoteItem CreateNoteItem(NoteSyntax note)
    {
        var (staffPosition, octave) = CalculateStaffPosition(note.Pitch);
        Console.Error.WriteLine($"[CreateNoteItem] pitch={note.Pitch.PitchName}, octaveOffset={note.Pitch.OctaveOffset}, lastPitch={_lastPitchName}, prevOctave={_currentOctave} -> newOctave={octave}, staffPos={staffPosition}");
        _currentOctave = octave;
        
        int noteValue = note.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (note.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);
        
        int dots = note.Duration?.DotCount ?? 0;
        bool needsLedger = staffPosition <= -6 || staffPosition >= 6;
        
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
            note.Position);
    }
    
    private RestItem CreateRestItem(RestSyntax rest)
    {
        int noteValue = rest.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (rest.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);
        
        int dots = rest.Duration?.DotCount ?? 0;
        
        return new RestItem(Fraction.FromNoteValue(noteValue), dots, rest.Position);
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
        
        return new ChordItem(notes.ToImmutableArray(), Fraction.FromNoteValue(noteValue), dots, chord.Position);
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
}




