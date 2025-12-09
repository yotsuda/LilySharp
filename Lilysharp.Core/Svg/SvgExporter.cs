using System.Text;
using Lilysharp.Core.Syntax;
using Lilysharp.Core.Semantics;

namespace Lilysharp.Core.Svg;

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
    private double _currentX;
    private double _currentY;
    private int _currentOctave = 4;
    private Fraction _defaultDuration = Fraction.Quarter;
    private string _currentClef = "treble";
    private int _keySignature = 0; // Number of sharps (+) or flats (-)
    
    public string Export(SyntaxTree tree)
    {
        _svg.Clear();
        _currentX = MarginLeft;
        _currentY = MarginTop;
        
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
    
    private void DrawGlyph(char glyph, double x, double y)
    {
        _svg.AppendLine($"""  <text class="music" x="{x:F1}" y="{y:F1}">{glyph}</text>""");
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
        DrawGlyph(notehead, _currentX, noteY);
        
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
        DrawGlyph(restGlyph, _currentX, restY);
        
        _currentX += GetNoteSpacing(noteValue);
    }
    
    private void DrawChord(ChordSyntax chord)
    {
        var duration = chord.Duration?.ToFraction() ?? _defaultDuration;
        if (chord.Duration != null)
            _defaultDuration = duration;
        
        int noteValue = (int)duration.Denominator;
        char notehead = SmuflGlyphs.GetNotehead(noteValue);
        double noteheadWidth = (noteValue == 1 ? SmuflDefaults.NoteheadWholeWidth : SmuflDefaults.NoteheadBlackWidth) * SpaceHeight;
        
        var pitches = chord.Pitches.ToList();
        if (pitches.Count == 0)
        {
            _currentX += GetNoteSpacing(noteValue);
            return;
        }
        
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
            DrawGlyph(notehead, _currentX, noteY);
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
}