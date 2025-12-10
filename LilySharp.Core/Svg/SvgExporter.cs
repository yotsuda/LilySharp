using System.Text;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg;

/// <summary>
/// Exports a SyntaxTree to SVG format using SMuFL-compliant Bravura font.
/// </summary>
public class SvgExporter
{
    // Layout constants
    private const double StaffSpaces = 4;  // Standard 5-line staff
    private const double SpaceHeight = 10; // Pixels per staff space
    private const double StaffHeight = StaffSpaces * SpaceHeight;
    private const double MarginLeft = 20;
    private const double MarginTop = 60;
    private const double FontSize = 40; // Bravura at 40px ≈ staff height
    
    // Derived from SMuFL defaults (converted to pixels)
    private static double StaffLineThickness => SmuflDefaults.StaffLineThickness * SpaceHeight;
    private static double StemThickness => SmuflDefaults.StemThickness * SpaceHeight;
    private static double ThinBarlineThickness => SmuflDefaults.ThinBarlineThickness * SpaceHeight;
    private static double LegerLineExtension => SmuflDefaults.LegerLineExtension * SpaceHeight;
    private static double LegerLineThickness => SmuflDefaults.LegerLineThickness * SpaceHeight;
    
    // Stem attachment points (from SMuFL, in pixels)
    private static double StemUpAttachX => SmuflDefaults.StemUpAttachX * SpaceHeight;
    private static double StemUpAttachY => SmuflDefaults.StemUpAttachY * SpaceHeight;
    private static double StemDownAttachX => SmuflDefaults.StemDownAttachX * SpaceHeight;
    private static double StemDownAttachY => SmuflDefaults.StemDownAttachY * SpaceHeight;
    
    // Stem length: 3.5 staff spaces = 35px
    private static double StemHeight => EngravingRules.StandardStemLength * SpaceHeight;
    
    // Fixed widths for clef/time signature glyphs (approximate, in pixels)
    private const double ClefWidth = 28;
    private const double TimeSignatureWidth = 20;
    
    // Spacing (converted to pixels)
    private static double MinNoteSpacing => EngravingRules.MinimumNoteSpacing * SpaceHeight;
    private static double SpaceAfterBarline => EngravingRules.SpaceAfterBarline * SpaceHeight;
    private static double SpaceAfterClef => EngravingRules.SpaceAfterClef * SpaceHeight;
    private static double SpaceAfterTimeSignature => EngravingRules.SpaceAfterTimeSignature * SpaceHeight;
    
    /// <summary>
    /// Calculate spacing for a note based on its duration using logarithmic spacing.
    /// Based on LilyPond's implementation of Gourlay's algorithm.
    /// </summary>
    private static double GetNoteSpacing(int noteValue)
    {
        double spacingInStaffSpaces = EngravingRules.GetNoteSpacing(noteValue);
        double spacing = spacingInStaffSpaces * SpaceHeight;
        return Math.Max(spacing, MinNoteSpacing);
    }
    
    private readonly StringBuilder _svg = new();
    private string _sourceText = "";
    private double _currentX;
    private double _currentY;
    private int _currentOctave = 4;
    private Fraction _defaultDuration = Fraction.Quarter;
    private string _currentClef = "treble";
    private int _keySignature = 0; // Number of sharps (+) or flats (-)
    
    // Tablature state
    private bool _isTabMode = false;
    private int[] _tabTuning = Tunings.Guitar;
    private int _tabStringCount = 6;
    
    public string Export(SyntaxTree tree)
    {
        // Reset all state
        _svg.Clear();
        _sourceText = tree.Text;
        _currentX = MarginLeft;
        _currentY = MarginTop;
        _currentOctave = 4;
        _defaultDuration = Fraction.Quarter;
        _currentClef = "treble";
        _keySignature = 0;
        
        // Calculate approximate width needed
        var width = CalculateWidth(tree);
        var height = MarginTop + StaffHeight + 60;
        
        WriteHeader(width, height);
        WriteStaffLines(width - 20);
        
        // Draw clef
        DrawClef();
        
        // Process syntax tree
        ProcessNode(tree.GetRoot());
        
        WriteFooter();
        return _svg.ToString();
    }
    
    private double CalculateWidth(SyntaxTree tree)
    {
        int noteCount = 0;
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is NoteSyntax or RestSyntax or ChordSyntax)
                noteCount++;
        }
        return MarginLeft + ClefWidth + TimeSignatureWidth + (noteCount * GetNoteSpacing(4)) + 40;
    }
    
    private void WriteHeader(double width, double height)
    {
        _svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        _svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width:F0}\" height=\"{height:F0}\" viewBox=\"0 0 {width:F0} {height:F0}\">");
        _svg.AppendLine("  <style>");
        _svg.AppendLine($"    .music {{ font-family: 'Bravura', 'Bravura Text'; font-size: {FontSize}px; }}");
        _svg.AppendLine($"    .staff-line {{ stroke: black; stroke-width: {StaffLineThickness:F2}; }}");
        _svg.AppendLine($"    .ledger-line {{ stroke: black; stroke-width: {LegerLineThickness:F2}; }}");
        _svg.AppendLine($"    .stem {{ stroke: black; stroke-width: {StemThickness:F2}; }}");
        _svg.AppendLine($"    .barline {{ stroke: black; stroke-width: {ThinBarlineThickness:F2}; }}");
        _svg.AppendLine("    .clickable { cursor: pointer; }");
        _svg.AppendLine("    .clickable:hover { fill: #0066cc; }");
        _svg.AppendLine("    .tab-clef { font-family: Arial, sans-serif; font-size: 14px; font-weight: bold; text-anchor: middle; }");
        _svg.AppendLine("    .tab-fret { font-family: Arial, sans-serif; font-size: 12px; text-anchor: middle; dominant-baseline: middle; }");
        _svg.AppendLine("    .tab-line { stroke: black; stroke-width: 0.5; }");
        _svg.AppendLine("  </style>");
    }
    
    private void WriteFooter()
    {
        _svg.AppendLine("</svg>");
    }
    
    private void WriteStaffLines(double width)
    {
        for (int i = 0; i < 5; i++)
        {
            double y = _currentY + (i * SpaceHeight);
            _svg.AppendLine($"""  <line class="staff-line" x1="{MarginLeft}" y1="{y:F1}" x2="{width:F1}" y2="{y:F1}"/>""");
        }
    }
    
    private void DrawClef()
    {
        char clefGlyph = _currentClef switch
        {
            "treble" => SmuflGlyphs.GClef,
            "bass" => SmuflGlyphs.FClef,
            "alto" or "tenor" => SmuflGlyphs.CClef,
            _ => SmuflGlyphs.GClef
        };
        
        // Clef Y position (G clef sits on the G line - 2nd line from bottom)
        double clefY = _currentClef switch
        {
            "treble" => _currentY + (3 * SpaceHeight), // G line
            "bass" => _currentY + (1 * SpaceHeight),   // F line
            _ => _currentY + (2 * SpaceHeight)         // Middle line
        };
        
        DrawGlyph(clefGlyph, _currentX, clefY);
        _currentX += ClefWidth;
    }
    
    private void DrawGlyph(char glyph, double x, double y, int? sourcePosition = null)
    {
        if (sourcePosition.HasValue)
        {
            _svg.AppendLine($"""  <text class="music clickable" x="{x:F1}" y="{y:F1}" data-pos="{sourcePosition.Value}">{glyph}</text>""");
        }
        else
        {
            _svg.AppendLine($"""  <text class="music" x="{x:F1}" y="{y:F1}">{glyph}</text>""");
        }
    }
    
    private void ProcessNode(SyntaxNode node)
    {
        switch (node)
        {
            case RelativeExpressionSyntax relative:
                _currentOctave = GetOctaveFromPitch(relative.BasePitch);
                ProcessNode(relative.Body);
                break;
                
            case MusicBlockSyntax block:
                foreach (var item in block.Items)
                    ProcessNode(item);
                break;
                
            case NoteSyntax note:
                DrawNote(note);
                break;
                
            case RestSyntax rest:
                DrawRest(rest);
                break;
                
            case ChordSyntax chord:
                DrawChord(chord);
                break;
                
            case BarlineSyntax:
                DrawBarline();
                break;
                
            case TimeSignatureSyntax timeSig:
                DrawTimeSignature(timeSig.Beats, timeSig.BeatType);
                break;
                
            case ClefDeclarationSyntax clef:
                _currentClef = clef.ClefName.Text.ToLowerInvariant();
                break;
                
            case KeySignatureSyntax key:
                _keySignature = CalculateKeySignature(key);
                break;
                
            case TabStaffDeclarationSyntax tabStaff:
                ProcessTabStaff(tabStaff);
                break;
                
            default:
                // Process children for container nodes
                for (int i = 0; i < node.SlotCount; i++)
                {
                    var child = node.GetChild(i);
                    if (child != null && child is not SyntaxTokenNode)
                        ProcessNode(child);
                }
                break;
        }
    }
    
    private void DrawNote(NoteSyntax note)
    {
        var duration = note.Duration?.ToFraction() ?? _defaultDuration;
        if (note.Duration != null)
            _defaultDuration = duration;
        
        // Get the base note value from the duration syntax, not the computed fraction
        // This handles dotted notes correctly (e.g., d8. should use noteValue=8, not 1)
        int noteValue = note.Duration?.Value ?? (int)_defaultDuration.Denominator;
        
        // Tab mode: draw fret number instead of notehead
        if (_isTabMode)
        {
            // Calculate octave without updating _currentOctave (avoid accumulation)
            int tabOctave = _currentOctave + note.Pitch.OctaveOffset;
            int midiPitch = CalculateMidiPitch(note.Pitch, tabOctave);
            int preferredString = GetPreferredString(note);
            DrawTabNote(midiPitch, preferredString, FindActualPosition(note.Position));
            _currentX += GetNoteSpacing(noteValue);
            return;
        }
        
        // Calculate pitch position
        var (staffPosition, octave) = CalculateStaffPosition(note.Pitch);
        _currentOctave = octave;
        
        double noteY = _currentY + StaffHeight - (staffPosition * SpaceHeight / 2);
        
        // Get notehead width based on note value (in pixels)
        double noteheadWidth = (noteValue == 1 ? SmuflDefaults.NoteheadWholeWidth : SmuflDefaults.NoteheadBlackWidth) * SpaceHeight;
        
        // Draw accidental if needed
        int accidentalOffset = note.Pitch.AccidentalOffset;
        if (accidentalOffset != 0)
        {
            char accGlyph = accidentalOffset switch
            {
                2 => SmuflGlyphs.AccidentalDoubleSharp,
                1 => SmuflGlyphs.AccidentalSharp,
                -1 => SmuflGlyphs.AccidentalFlat,
                -2 => SmuflGlyphs.AccidentalDoubleFlat,
                _ => SmuflGlyphs.AccidentalNatural
            };
            DrawGlyph(accGlyph, _currentX - 12, noteY);
        }
        
        // Draw ledger lines if needed
        DrawLedgerLines(staffPosition, noteheadWidth);
        
        // Draw notehead
        char notehead = SmuflGlyphs.GetNotehead(noteValue);
        DrawGlyph(notehead, _currentX, noteY, FindActualPosition(note.Position));
        
        // Draw stem (for half notes and shorter)
        if (noteValue >= 2)
        {
            bool stemUp = staffPosition < 4; // Below middle line = stem up
            
            // Calculate stem X position using SMuFL anchor points (already in pixels)
            double stemX = stemUp 
                ? _currentX + StemUpAttachX 
                : _currentX + StemDownAttachX;
            
            // Calculate stem Y attachment point (already in pixels)
            double stemAttachY = stemUp
                ? noteY - StemUpAttachY
                : noteY - StemDownAttachY;
            
            // Draw stem
            double stemEndY = stemUp ? stemAttachY - StemHeight : stemAttachY + StemHeight;
            _svg.AppendLine($"""  <line class="stem" x1="{stemX:F1}" y1="{stemAttachY:F1}" x2="{stemX:F1}" y2="{stemEndY:F1}"/>""");
            
            // Draw flag (for 8th notes and shorter)
            var flag = SmuflGlyphs.GetFlag(noteValue, stemUp);
            if (flag.HasValue)
            {
                // Flag attaches at the end of the stem
                DrawGlyph(flag.Value, stemX, stemEndY);
            }
        }
        
        // Draw dots
        int dots = note.Duration?.DotCount ?? 0;
        for (int d = 0; d < dots; d++)
        {
            DrawGlyph(SmuflGlyphs.AugmentationDot, _currentX + noteheadWidth + 3 + (d * 6), noteY - 2);
        }
        
        _currentX += GetNoteSpacing(noteValue);
    }
    
    private void DrawLedgerLines(int staffPosition, double noteheadWidth)
    {
        // Use SMuFL legerLineExtension for the extension beyond notehead
        double ledgerWidth = noteheadWidth + LegerLineExtension * 2;
        double ledgerX = _currentX - LegerLineExtension;
        
        // Below staff (position < 0)
        if (staffPosition < 0)
        {
            for (int pos = -2; pos >= staffPosition; pos -= 2)
            {
                double y = _currentY + StaffHeight - (pos * SpaceHeight / 2);
                _svg.AppendLine($"""  <line class="ledger-line" x1="{ledgerX:F1}" y1="{y:F1}" x2="{ledgerX + ledgerWidth:F1}" y2="{y:F1}"/>""");
            }
        }
        // Above staff (position > 8)
        else if (staffPosition > 8)
        {
            for (int pos = 10; pos <= staffPosition; pos += 2)
            {
                double y = _currentY + StaffHeight - (pos * SpaceHeight / 2);
                _svg.AppendLine($"""  <line class="ledger-line" x1="{ledgerX:F1}" y1="{y:F1}" x2="{ledgerX + ledgerWidth:F1}" y2="{y:F1}"/>""");
            }
        }
    }
    
    private void DrawRest(RestSyntax rest)
    {
        var duration = rest.Duration?.ToFraction() ?? _defaultDuration;
        if (rest.Duration != null)
            _defaultDuration = duration;
        
        int noteValue = (int)duration.Denominator;
        char restGlyph = SmuflGlyphs.GetRest(noteValue);
        
        // Rest Y position (centered on staff)
        double restY = _currentY + (2 * SpaceHeight);
        DrawGlyph(restGlyph, _currentX, restY, FindActualPosition(rest.Position));
        
        _currentX += GetNoteSpacing(noteValue);
    }
    
    private void DrawChord(ChordSyntax chord)
    {
        var duration = chord.Duration?.ToFraction() ?? _defaultDuration;
        if (chord.Duration != null)
            _defaultDuration = duration;
        
        int noteValue = (int)duration.Denominator;
        
        var pitches = chord.Pitches.ToList();
        if (pitches.Count == 0)
        {
            _currentX += GetNoteSpacing(noteValue);
            return;
        }
        
        // Tab mode: draw fret numbers for all pitches
        if (_isTabMode)
        {
            DrawTabChord(chord, pitches, noteValue);
            return;
        }
        
        char notehead = SmuflGlyphs.GetNotehead(noteValue);
        double noteheadWidth = (noteValue == 1 ? SmuflDefaults.NoteheadWholeWidth : SmuflDefaults.NoteheadBlackWidth) * SpaceHeight;
        

        
        // Calculate positions for all pitches
        var positions = pitches.Select(p => CalculateStaffPosition(p)).ToList();
        int lowestPos = positions.Min(p => p.position);
        int highestPos = positions.Max(p => p.position);
        
        // Draw ledger lines for extreme notes
        DrawLedgerLines(lowestPos, noteheadWidth);
        DrawLedgerLines(highestPos, noteheadWidth);
        
        // Draw noteheads
        foreach (var (pos, octave) in positions)
        {
            double noteY = _currentY + StaffHeight - (pos * SpaceHeight / 2);
            DrawGlyph(notehead, _currentX, noteY, FindActualPosition(chord.Position));
        }
        
        // Draw stem (for half notes and shorter)
        if (noteValue >= 2)
        {
            bool stemUp = (lowestPos + highestPos) / 2 < 4;
            int stemPos = stemUp ? lowestPos : highestPos;
            double noteY = _currentY + StaffHeight - (stemPos * SpaceHeight / 2);
            
            // Calculate stem position using SMuFL anchor points (already in pixels)
            double stemX = stemUp 
                ? _currentX + StemUpAttachX 
                : _currentX + StemDownAttachX;
            
            double stemAttachY = stemUp
                ? noteY - StemUpAttachY
                : noteY - StemDownAttachY;
            
            double stemEndY = stemUp ? stemAttachY - StemHeight : stemAttachY + StemHeight;
            _svg.AppendLine($"""  <line class="stem" x1="{stemX:F1}" y1="{stemAttachY:F1}" x2="{stemX:F1}" y2="{stemEndY:F1}"/>""");
            
            // Draw flag (for 8th notes and shorter)
            var flag = SmuflGlyphs.GetFlag(noteValue, stemUp);
            if (flag.HasValue)
            {
                DrawGlyph(flag.Value, stemX, stemEndY);
            }
        }
        
        _currentX += GetNoteSpacing(noteValue);
    }
    
    private void DrawBarline()
    {
        // Draw barline at current position
        // Note: Space before barline is included in the previous note's spacing
        double x = _currentX;
        _svg.AppendLine($"""  <line class="barline" x1="{x:F1}" y1="{_currentY:F1}" x2="{x:F1}" y2="{_currentY + StaffHeight:F1}"/>""");
        
        // Add space after barline (LilyPond: semi-shrink-space 1.3)
        _currentX += SpaceAfterBarline;
    }
    
    private void DrawTimeSignature(int beats, int beatType)
    {
        // Draw numerator
        string numStr = beats.ToString();
        double numX = _currentX;
        double numY = _currentY + SpaceHeight;
        foreach (char c in numStr)
        {
            DrawGlyph(SmuflGlyphs.GetTimeSigDigit(c - '0'), numX, numY);
            numX += 12;
        }
        
        // Draw denominator
        string denStr = beatType.ToString();
        double denX = _currentX;
        double denY = _currentY + (3 * SpaceHeight);
        foreach (char c in denStr)
        {
            DrawGlyph(SmuflGlyphs.GetTimeSigDigit(c - '0'), denX, denY);
            denX += 12;
        }
        
        _currentX += TimeSignatureWidth;
        
        // Add space after time signature to first note (LilyPond: semi-shrink-space 2.0)
        _currentX += SpaceAfterTimeSignature;
    }
    
    private (int position, int octave) CalculateStaffPosition(PitchSyntax pitch)
    {
        // Base note positions in treble clef (C4 = middle C = position -2)
        int basePosition = pitch.BaseName switch
        {
            'c' => 0,
            'd' => 1,
            'e' => 2,
            'f' => 3,
            'g' => 4,
            'a' => 5,
            'b' => 6,
            _ => 0
        };
        
        // Calculate octave from relative marks
        int octave = _currentOctave + pitch.OctaveOffset;
        
        // Adjust for relative pitch (within a fourth of previous note)
        // For now, simplified implementation
        
        // Position on staff (0 = middle C ledger line in treble clef)
        // Treble clef: E4 = line 0 (bottom), so C4 = -2
        int staffPosition = basePosition + ((octave - 4) * 7) - 2;
        
        return (staffPosition, octave);
    }
    
    private int GetOctaveFromPitch(PitchSyntax pitch)
    {
        // Default octave is 4, adjusted by octave marks
        return 4 + pitch.OctaveOffset;
    }
    
    private int CalculateKeySignature(KeySignatureSyntax key)
    {
        // Calculate sharps/flats based on pitch and mode
        var pitch = key.Pitch;
        bool isMajor = key.IsMajor;
        
        // Circle of fifths lookup
        return (pitch.BaseName, pitch.AccidentalOffset, isMajor) switch
        {
            ('c', 0, true) => 0,
            ('g', 0, true) => 1,
            ('d', 0, true) => 2,
            ('a', 0, true) => 3,
            ('e', 0, true) => 4,
            ('b', 0, true) => 5,
            ('f', 1, true) => 6,   // F#
            ('f', 0, true) => -1,
            ('b', -1, true) => -2, // Bb
            ('e', -1, true) => -3, // Eb
            ('a', -1, true) => -4, // Ab
            ('d', -1, true) => -5, // Db
            // Minor keys (relative)
            ('a', 0, false) => 0,
            ('e', 0, false) => 1,
            ('b', 0, false) => 2,
            ('d', 0, false) => -1,
            ('g', 0, false) => -2,
            ('c', 0, false) => -3,
            _ => 0
        };
    }
    
    /// <summary>
    /// Finds the actual text position by skipping whitespace and comments.
    /// </summary>
    private int FindActualPosition(int startPos)
    {
        if (string.IsNullOrEmpty(_sourceText) || startPos >= _sourceText.Length)
            return startPos;
        
        // Search for the actual text within the node's range
        int pos = startPos;
        while (pos < _sourceText.Length)
        {
            // Skip whitespace
            while (pos < _sourceText.Length && char.IsWhiteSpace(_sourceText[pos]))
                pos++;
            
            // Skip line comments
            if (pos + 1 < _sourceText.Length && _sourceText[pos] == '/' && _sourceText[pos + 1] == '/')
            {
                while (pos < _sourceText.Length && _sourceText[pos] != '\n')
                    pos++;
                continue;
            }
            
            // Skip block comments
            if (pos + 1 < _sourceText.Length && _sourceText[pos] == '/' && _sourceText[pos + 1] == '*')
            {
                pos += 2;
                while (pos + 1 < _sourceText.Length && !(_sourceText[pos] == '*' && _sourceText[pos + 1] == '/'))
                    pos++;
                pos += 2;
                continue;
            }
            
            // Found non-whitespace, non-comment
            break;
        }
        
        return pos;
    }
    
    // ============================================================
    // Tablature Methods
    // ============================================================
    
    private void ProcessTabStaff(TabStaffDeclarationSyntax tabStaff)
    {
        // Save current state
        bool wasTabMode = _isTabMode;
        int[] prevTuning = _tabTuning;
        int prevStringCount = _tabStringCount;
        double savedX = _currentX;
        
        // Enter tab mode
        _isTabMode = true;
        
        // Set tuning if specified
        if (tabStaff.Tuning != null)
        {
            var tuningType = tabStaff.Tuning.Type;
            _tabTuning = Tunings.GetTuning(tuningType);
            _tabStringCount = Tunings.GetStringCount(tuningType);
        }
        else
        {
            _tabTuning = Tunings.Guitar;
            _tabStringCount = 6;
        }
        
        // Draw TAB clef
        DrawTabClef();
        
        // Process body first to determine width
        double startX = _currentX;
        ProcessNode(tabStaff.Body);
        double endX = _currentX + 20;
        
        // Draw tab lines (one per string)
        WriteTabLines(savedX, endX);
        
        // Restore state
        _isTabMode = wasTabMode;
        _tabTuning = prevTuning;
        _tabStringCount = prevStringCount;
    }
    
    private void WriteTabLines(double startX, double endX)
    {
        for (int i = 0; i < _tabStringCount; i++)
        {
            double y = _currentY + (i * SpaceHeight);
            _svg.AppendLine($"""  <line class="tab-line" x1="{startX:F1}" y1="{y:F1}" x2="{endX:F1}" y2="{y:F1}"/>""");
        }
    }
    
    private void DrawTabClef()
    {
        // Draw "TAB" text vertically
        double x = _currentX;
        double centerY = _currentY + ((_tabStringCount - 1) * SpaceHeight) / 2;
        
        _svg.AppendLine($"""  <text class="tab-clef" x="{x:F1}" y="{centerY:F1}">TAB</text>""");
        _currentX += ClefWidth;
    }
    
    private void DrawTabNote(int midiPitch, int preferredString, int? sourcePosition)
    {
        var (stringNum, fret) = Tunings.CalculateFret(midiPitch, _tabTuning, preferredString);
        
        // Calculate Y position (string 1 at top, string N at bottom)
        double y = _currentY + (stringNum - 1) * SpaceHeight;
        
        // Draw fret number
        string fretText = fret.ToString();
        if (sourcePosition.HasValue)
        {
            _svg.AppendLine($"""  <text class="tab-fret clickable" x="{_currentX:F1}" y="{y:F1}" data-pos="{sourcePosition.Value}">{fretText}</text>""");
        }
        else
        {
            _svg.AppendLine($"""  <text class="tab-fret" x="{_currentX:F1}" y="{y:F1}">{fretText}</text>""");
        }
    }
    
    private void DrawTabChord(ChordSyntax chord, List<PitchSyntax> pitches, int noteValue)
    {
        // Calculate MIDI pitches for all notes
        var midiPitches = new List<int>();
        foreach (var pitch in pitches)
        {
            var (_, octave) = CalculateStaffPosition(pitch);
            midiPitches.Add(CalculateMidiPitch(pitch, octave));
        }
        
        // Find best string assignment for each pitch (avoiding duplicates)
        var usedStrings = new HashSet<int>();
        var assignments = new List<(int stringNum, int fret)>();
        
        // Sort by pitch (low to high) for better string assignment
        var sortedPitches = midiPitches.OrderBy(p => p).ToList();
        
        foreach (var midiPitch in sortedPitches)
        {
            // Try to find an unused string
            var (stringNum, fret) = Tunings.CalculateFret(midiPitch, _tabTuning, 0);
            
            // If string already used, try to find another
            if (usedStrings.Contains(stringNum))
            {
                // Try other strings
                for (int s = _tabStringCount; s >= 1; s--)
                {
                    if (!usedStrings.Contains(s))
                    {
                        var (_, testFret) = Tunings.CalculateFret(midiPitch, _tabTuning, s);
                        if (testFret >= 0 && testFret <= 24)
                        {
                            stringNum = s;
                            fret = testFret;
                            break;
                        }
                    }
                }
            }
            
            usedStrings.Add(stringNum);
            assignments.Add((stringNum, fret));
        }
        
        // Draw all fret numbers at current X position
        int? sourcePos = FindActualPosition(chord.Position);
        foreach (var (stringNum, fret) in assignments)
        {
            double y = _currentY + (stringNum - 1) * SpaceHeight;
            string fretText = fret.ToString();
            if (sourcePos.HasValue)
            {
                _svg.AppendLine($"""  <text class="tab-fret clickable" x="{_currentX:F1}" y="{y:F1}" data-pos="{sourcePos.Value}">{fretText}</text>""");
            }
            else
            {
                _svg.AppendLine($"""  <text class="tab-fret" x="{_currentX:F1}" y="{y:F1}">{fretText}</text>""");
            }
        }
        
        _currentX += GetNoteSpacing(noteValue);
    }
    
    private int CalculateMidiPitch(PitchSyntax pitch, int octave)
    {
        // MIDI note numbers: C4 = 60
        int basePitch = pitch.BaseName switch
        {
            'c' => 0,
            'd' => 2,
            'e' => 4,
            'f' => 5,
            'g' => 7,
            'a' => 9,
            'b' => 11,
            _ => 0
        };
        
        return 12 * (octave + 1) + basePitch + pitch.AccidentalOffset;
    }
    
    private int GetPreferredString(NoteSyntax note)
    {
        // Check for string number annotation (\1, \2, etc.)
        // For now, return 0 (auto)
        // TODO: Parse string annotations from note
        return 0;
    }
}