using Lilysharp.Core.Syntax;
using Lilysharp.Core.Semantics;

namespace Lilysharp.Core.MusicXml;

/// <summary>
/// Exports a syntax tree to MusicXML format.
/// </summary>
public sealed class MusicXmlExporter
{
    private const int DivisionsPerQuarter = 4;
    
    private int _currentOctave = 4;
    private Fraction _defaultDuration = Fraction.Quarter;
    private int _measureNumber = 1;
    private MusicXmlMeasure? _currentMeasure;
    private MusicXmlPart? _currentPart;
    private MusicXmlDocument? _document;
    
    private int _tempo = 120;
    private int _timeNumerator = 4;
    private int _timeDenominator = 4;
    private int _keyFifths = 0;
    private string _keyMode = "major";
    private string _clefSign = "G";
    private int _clefLine = 2;
    private string? _currentDynamic;

    public MusicXmlDocument Export(SyntaxTree tree)
    {
        _document = new MusicXmlDocument();
        _currentPart = new MusicXmlPart { Name = "Part 1" };
        _document.Parts.Add(_currentPart);
        
        StartNewMeasure();
        
        var root = tree.GetRoot();
        ProcessNode(root);
        
        // Ensure last measure is added
        if (_currentMeasure != null && _currentMeasure.Notes.Count > 0)
        {
            _currentPart.Measures.Add(_currentMeasure);
        }
        
        return _document;
    }

    private void StartNewMeasure()
    {
        _currentMeasure = new MusicXmlMeasure { Number = _measureNumber++ };
        
        // Add attributes on first measure
        if (_measureNumber == 2)
        {
            _currentMeasure.Attributes = new MusicXmlAttributes
            {
                Divisions = DivisionsPerQuarter,
                TimeBeats = _timeNumerator,
                TimeBeatType = _timeDenominator,
                KeyFifths = _keyFifths,
                KeyMode = _keyMode,
                ClefSign = _clefSign,
                ClefLine = _clefLine
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
                
            case MetadataDeclarationSyntax metadata:
                ProcessMetadata(metadata);
                break;
                
            case KeySignatureSyntax key:
                ProcessKeySignature(key);
                break;
                
            case ClefDeclarationSyntax clef:
                ProcessClef(clef);
                break;
                
            case RelativeExpressionSyntax relative:
                ProcessRelative(relative);
                break;
                
            case MusicBlockSyntax block:
                foreach (var item in block.Items)
                    ProcessNode(item);
                break;
                
            case NoteSyntax note:
                ProcessNote(note);
                break;
                
            case ChordSyntax chord:
                ProcessChord(chord);
                break;
                
            case RestSyntax rest:
                ProcessRest(rest);
                break;
                
            case BarlineSyntax:
                if (_currentMeasure != null && _currentPart != null)
                {
                    _currentPart.Measures.Add(_currentMeasure);
                    StartNewMeasure();
                }
                break;
                
            case DynamicSyntax dynamic:
                _currentDynamic = dynamic.DynamicToken.Text;
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

    private void ProcessMetadata(MetadataDeclarationSyntax metadata)
    {
        if (_document == null) return;
        
        var keyword = metadata.KeywordToken.Text.ToLowerInvariant();
        
        // Get string value if present
        for (int i = 1; i < metadata.SlotCount; i++)
        {
            if (metadata.GetChild(i) is SyntaxTokenNode token)
            {
                if (token.Kind == SyntaxKind.StringLiteral)
                {
                    var value = token.Text.Trim('"');
                    if (keyword == "title")
                        _document.Title = value;
                    else if (keyword == "composer")
                        _document.Composer = value;
                }
                else if (token.Kind == SyntaxKind.DurationNumber || token.Kind == SyntaxKind.IntegerLiteral)
                {
                    if (keyword == "tempo" && int.TryParse(token.Text, out var bpm))
                        _tempo = bpm;
                }
            }
        }
        
        // Handle time signature: time 4/4
        if (keyword == "time")
        {
            for (int i = 1; i < metadata.SlotCount; i++)
            {
                if (metadata.GetChild(i) is SyntaxTokenNode token && 
                    (token.Kind == SyntaxKind.DurationNumber || token.Kind == SyntaxKind.IntegerLiteral))
                {
                    if (int.TryParse(token.Text, out var num))
                    {
                        if (_timeNumerator == 4 && _timeDenominator == 4) // first number
                            _timeNumerator = num;
                        else
                            _timeDenominator = num;
                    }
                }
            }
        }
    }

    private void ProcessKeySignature(KeySignatureSyntax key)
    {
        var pitch = key.Pitch?.ToFullString().Trim().ToLower();
        var isMajor = key.IsMajor;
        
        _keyFifths = pitch switch
        {
            "c" => isMajor ? 0 : -3,
            "g" => isMajor ? 1 : -2,
            "d" => isMajor ? 2 : -1,
            "a" => isMajor ? 3 : 0,
            "e" => isMajor ? 4 : 1,
            "b" => isMajor ? 5 : 2,
            "fis" => isMajor ? 6 : 3,
            "f" => isMajor ? -1 : -4,
            "bes" => isMajor ? -2 : -5,
            "ees" => isMajor ? -3 : -6,
            "aes" => isMajor ? -4 : -7,
            "des" => isMajor ? -5 : -8,
            "ges" => isMajor ? -6 : -9,
            _ => 0
        };
        _keyMode = isMajor ? "major" : "minor";
    }

    private void ProcessClef(ClefDeclarationSyntax clef)
    {
        var clefName = clef.ClefName?.Text.ToLower();
        (_clefSign, _clefLine) = clefName switch
        {
            "treble" => ("G", 2),
            "bass" => ("F", 4),
            "alto" => ("C", 3),
            "tenor" => ("C", 4),
            _ => ("G", 2)
        };
    }

    private void ProcessRelative(RelativeExpressionSyntax relative)
    {
        var pitchText = relative.BasePitch.ToFullString();
        var upCount = pitchText.Count(c => c == '\'');
        var downCount = pitchText.Count(c => c == ',');
        _currentOctave = 4 + upCount - downCount;
        
        ProcessNode(relative.Body);
    }

    private void ProcessNote(NoteSyntax note)
    {
        if (_currentMeasure == null) return;
        
        var (step, alter) = ParsePitch(note.Pitch);
        int octaveChange = note.Pitch.OctaveOffset;
        int targetOctave = _currentOctave + octaveChange;
        _currentOctave = targetOctave;
        
        var duration = GetDuration(note.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);
        
        var xmlNote = new MusicXmlNote
        {
            Step = step,
            Alter = alter,
            Octave = targetOctave,
            Duration = durationTicks,
            Type = type,
            Dots = dots,
            Dynamic = _currentDynamic
        };
        
        foreach (var artic in note.Articulations)
        {
            if (artic is ArticulationSyntax articulation)
            {
                var articName = articulation.Type switch
                {
                    ArticulationType.Staccato => "staccato",
                    ArticulationType.Accent => "accent",
                    ArticulationType.Tenuto => "tenuto",
                    ArticulationType.Marcato => "strong-accent",
                    ArticulationType.Fermata => "fermata",
                    _ => null
                };
                if (articName != null)
                    xmlNote.Articulations.Add(articName);
            }
            else if (artic is DynamicSyntax dynamic)
            {
                _currentDynamic = dynamic.DynamicToken.Text;
            }
        }
        
        _currentMeasure.Notes.Add(xmlNote);
    }

    private void ProcessChord(ChordSyntax chord)
    {
        if (_currentMeasure == null) return;
        
        var pitches = chord.Pitches.ToList();
        if (pitches.Count == 0) return;
        
        var duration = GetDuration(chord.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);
        
        bool isFirst = true;
        foreach (var pitch in pitches)
        {
            var (step, alter) = ParsePitch(pitch);
            int octaveChange = pitch.OctaveOffset;
            int targetOctave = _currentOctave + octaveChange;
            
            var xmlNote = new MusicXmlNote
            {
                Step = step,
                Alter = alter,
                Octave = targetOctave,
                Duration = durationTicks,
                Type = type,
                Dots = dots,
                IsChord = !isFirst,
                Dynamic = isFirst ? _currentDynamic : null
            };
            
            // Add articulations only to the first note
            if (isFirst)
            {
                foreach (var artic in chord.Articulations)
                {
                    if (artic is ArticulationSyntax articulation)
                    {
                        var articName = articulation.Type switch
                        {
                            ArticulationType.Staccato => "staccato",
                            ArticulationType.Accent => "accent",
                            ArticulationType.Tenuto => "tenuto",
                            ArticulationType.Marcato => "strong-accent",
                            ArticulationType.Fermata => "fermata",
                            _ => null
                        };
                        if (articName != null)
                            xmlNote.Articulations.Add(articName);
                    }
                    else if (artic is DynamicSyntax dynamic)
                    {
                        _currentDynamic = dynamic.DynamicToken.Text;
                    }
                }
            }
            
            _currentMeasure.Notes.Add(xmlNote);
            
            if (isFirst)
            {
                _currentOctave = targetOctave;
                isFirst = false;
            }
        }
    }

    private void ProcessRest(RestSyntax rest)
    {
        if (_currentMeasure == null) return;
        
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
    }

    private (string step, int alter) ParsePitch(PitchSyntax pitch)
    {
        var text = pitch.PitchName.ToLowerInvariant();
        
        char basePitch = text[0];
        string step = basePitch.ToString().ToUpper();
        
        int alter = 0;
        if (text.Contains("isis")) alter = 2;
        else if (text.Contains("eses")) alter = -2;
        else if (text.Contains("is")) alter = 1;
        else if (text.Contains("es") || text.Contains("as")) alter = -1;
        
        return (step, alter);
    }

    private Fraction GetDuration(DurationSyntax? duration)
    {
        if (duration == null) return _defaultDuration;
        
        var baseDuration = Fraction.FromNoteValue(duration.Value);
        _defaultDuration = baseDuration.Dotted(duration.DotCount);
        return _defaultDuration;
    }

    private int FractionToTicks(Fraction frac)
    {
        return (int)(frac.Numerator * DivisionsPerQuarter * 4 / frac.Denominator);
    }

    private (string type, int dots) GetNoteType(Fraction duration)
    {
        int dots = 0;
        int baseDenom = (int)duration.Denominator;
        
        // Check for dotted notes
        if (duration.Numerator == 3 && duration.Denominator % 2 == 0)
        {
            dots = 1;
            baseDenom = (int)(duration.Denominator / 2);
        }
        
        string type = baseDenom switch
        {
            1 => "whole",
            2 => "half",
            4 => "quarter",
            8 => "eighth",
            16 => "16th",
            32 => "32nd",
            64 => "64th",
            _ => "quarter"
        };
        
        return (type, dots);
    }
}