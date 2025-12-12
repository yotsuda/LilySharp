using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

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
    /// Collects a Score from a syntax tree.
    /// </summary>
    public Score Collect(SyntaxTree tree, string? voiceName = null)
    {
        _voiceName = voiceName;
        Reset();
        
        // Phase 1: Collect definitions
        CollectDefinitions(tree.GetRoot());
        
        // Phase 2: Collect measures
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
        var measures = new List<Measure>();
        var currentItems = new List<MusicItem>();
        BarlineType pendingStartBarline = BarlineType.None;
        string? sectionLabel = null;
        int measureSourceStart = 0;
        
        void CompleteMeasure(BarlineType endBarline, int sourceEnd)
        {
            if (currentItems.Count > 0 || pendingStartBarline != BarlineType.None)
            {
                measures.Add(new Measure(
                    currentItems.ToImmutableArray(),
                    pendingStartBarline,
                    endBarline,
                    sectionLabel,
                    measureSourceStart,
                    sourceEnd));
                
                currentItems.Clear();
                sectionLabel = null;
                pendingStartBarline = BarlineType.None;
                measureSourceStart = sourceEnd;
            }
        }
        
        void ProcessNodes(IEnumerable<SyntaxNode> nodes)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case RelativeExpressionSyntax relative:
                        InitializeRelativeMode(relative.BasePitch);
                        break;
                        
                    case NoteSyntax note:
                        currentItems.Add(CreateNoteItem(note));
                        break;
                        
                    case RestSyntax rest:
                        currentItems.Add(CreateRestItem(rest));
                        break;
                        
                    case ChordSyntax chord:
                        currentItems.Add(CreateChordItem(chord));
                        break;
                        
                    case BarlineSyntax barline:
                        var barType = ParseBarlineType(barline.BarToken.Text);
                        
                        if (barType == BarlineType.RepeatStart)
                        {
                            // |: starts a new measure
                            if (currentItems.Count > 0)
                                CompleteMeasure(BarlineType.Single, barline.Position);
                            pendingStartBarline = BarlineType.RepeatStart;
                            measureSourceStart = barline.Position;
                        }
                        else if (barType == BarlineType.RepeatEnd && currentItems.Count == 0 && measures.Count > 0)
                        {
                            // :| after section end - modify last measure's end barline
                            var lastMeasure = measures[measures.Count - 1];
                            measures[measures.Count - 1] = new Measure(
                                lastMeasure.Items,
                                lastMeasure.StartBarline,
                                BarlineType.RepeatEnd,
                                lastMeasure.SectionLabel,
                                lastMeasure.SourceStart,
                                lastMeasure.SourceEnd);
                        }
                        else
                        {
                            CompleteMeasure(barType, barline.Position);
                        }
                        break;
                }
            }
        }
        
        // Process based on structure or sections
        if (_structure != null)
        {
            ProcessStructure(ProcessNodes, ref sectionLabel);
        }
        else if (_sections.Count > 0)
        {
            foreach (var section in _sections.Values)
            {
                sectionLabel = section.SectionName;
                ProcessSection(section, ProcessNodes);
            }
        }
        else if (_root != null)
        {
            // Fallback: process music nodes directly from root (for simple files)
            var musicNodes = _root.DescendantNodes()
                .Where(n => n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or RelativeExpressionSyntax);
            ProcessNodes(musicNodes);
        }
        
        // Handle final measure without trailing barline
        if (currentItems.Count > 0)
        {
            measures.Add(new Measure(
                currentItems.ToImmutableArray(),
                pendingStartBarline,
                BarlineType.Single,
                sectionLabel,
                measureSourceStart,
                measureSourceStart));
        }
        
        return measures;
    }
    
    private void ProcessStructure(Action<IEnumerable<SyntaxNode>> processNodes, ref string? sectionLabel)
    {
        foreach (var child in _structure!.DescendantNodes())
        {
            switch (child)
            {
                case SectionReferenceSyntax reference:
                    if (_sections.TryGetValue(reference.SectionName, out var section))
                    {
                        sectionLabel = reference.SectionName;
                        ProcessSection(section, processNodes);
                    }
                    break;
                    
                case StructureRepeatBlockSyntax repeat:
                    ProcessRepeatBlock(repeat, processNodes, ref sectionLabel);
                    break;
            }
        }
    }
    
    private void ProcessRepeatBlock(StructureRepeatBlockSyntax repeat, Action<IEnumerable<SyntaxNode>> processNodes, ref string? sectionLabel)
    {
        bool afterRepeatStart = false;
        
        for (int i = 0; i < repeat.SlotCount; i++)
        {
            var child = repeat.GetChild(i);
            
            if (child is SyntaxTokenNode token)
            {
                if (token.Text == "|:")
                {
                    // Process as a repeat start barline
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
                        sectionLabel = reference.SectionName;
                        ProcessSection(section, processNodes);
                    }
                }
                else if (child is StructureAlternativeSyntax alt)
                {
                    string altSectionName = alt.SectionName.Text;
                    if (_sections.TryGetValue(altSectionName, out var section))
                    {
                        sectionLabel = altSectionName;
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
                case RelativeExpressionSyntax:
                    musicNodes.Add(node);
                    break;
                    
                case VariableReferenceSyntax varRef:
                    // Expand variable reference (handles both 'use name' and bare identifier)
                    ExpandVariable(varRef.Name.Text, musicNodes);
                    break;
                    
                // Note: SyntaxTokenNode with Identifier kind is NOT processed here
                // because it's already wrapped in VariableReferenceSyntax by the parser
            }
        }
        
        processNodes(musicNodes);
    }
    
    private void ExpandVariable(string name, List<SyntaxNode> musicNodes)
    {
        if (!_variables.TryGetValue(name, out var expression))
            return;
        
        // Include expression itself if it is a music node (e.g., RelativeExpressionSyntax)
        if (expression is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or RelativeExpressionSyntax)
        {
            musicNodes.Add(expression);
        }
        
        // Get music nodes from the variable expression descendants
        var nodes = expression.DescendantNodes()
            .Where(n => n is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax or RelativeExpressionSyntax);
        
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