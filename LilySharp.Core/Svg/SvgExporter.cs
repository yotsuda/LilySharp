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
    
    // Page layout constants
    private const double PageWidth = 800;
    private const double MarginRight = 20;
    private const double SystemSpacing = 80;  // Vertical space between systems (staves)
    
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
    
    private StringBuilder _svg = new();
    private StringBuilder _content = new();  // Content builder for line breaking support
    private string _sourceText = "";
    private double _currentX;
    private double _currentY;
    private int _currentOctave = 4;
    private int _currentNoteName = 0; // c=0, d=1, e=2, f=3, g=4, a=5, b=6
    private Fraction _defaultDuration = Fraction.Quarter;
    private string _currentClef = "treble";
    private int _keySignature = 0; // Number of sharps (+) or flats (-)
    
    // Tablature state
    private bool _isTabMode = false;
    private int[] _tabTuning = Tunings.Guitar;
    private int _tabStringCount = 6;
    
    // Line breaking state
    private int _systemCount = 1;
    private double _systemStartY;
    private double _previousSystemY;  // Y coordinate of previous system (for drawing :| on previous line)
    private double _previousLineEndX;  // X coordinate at end of previous line (for drawing :| on previous line)
    private bool _lineJustBroken;  // True if line break just occurred
    private bool _suppressTrailingBarline;  // True to skip last barline in section (before :|)
    private readonly double _lineBreakThreshold = PageWidth - MarginRight - 50; // Leave some margin
    
    // Metadata
    private string? _title;
    private string? _composer;
    private int? _tempo;
    private int _timeBeats = 4;
    private int _timeBeatType = 4;
    
    // Structural definitions (collected before rendering)
    private Dictionary<string, SectionDeclarationSyntax> _sections = new();
    private StructureDeclarationSyntax? _structure;
    private string? _renderPartName;  // Current part being rendered (e.g., "rightHand")
    private SyntaxNode? _root;  // Root node for fallback processing
    
    public string Export(SyntaxTree tree)
    {
        // Phase 1: Reset all state
        ResetState(tree);
        
        // Phase 2: Collect all definitions (sections, structure, metadata)
        CollectDefinitions(tree.GetRoot());
        
        // Phase 3: Setup rendering
        var originalSvg = _svg;
        _svg = _content;
        
        double headerHeight = (_title != null || _composer != null) ? 50 : 0;
        _currentY = MarginTop + headerHeight;
        _systemStartY = _currentY;
        _currentX = MarginLeft;
        
        // Draw header elements
        if (_title != null || _composer != null)
            DrawTitleAndComposer();
        
        WriteStaffLines(PageWidth - MarginRight);
        DrawClef();
        
        if (_keySignature != 0)
            DrawKeySignature();
        
        DrawTimeSignature(_timeBeats, _timeBeatType);
        
        if (_tempo.HasValue)
            DrawTempoMarking();
        
        // Phase 4: Render content based on structure
        RenderByStructure();
        
        // Phase 5: Finalize SVG
        _svg = originalSvg;
        _svg.Clear();
        
        var height = MarginTop + headerHeight + (_systemCount * (StaffHeight + SystemSpacing));
        WriteHeader(PageWidth, height);
        _svg.Append(_content);
        WriteFooter();
        
        return _svg.ToString();
    }
    
    private void ResetState(SyntaxTree tree)
    {
        _content = new StringBuilder();
        _sourceText = tree.Text;
        _currentOctave = 4;
        _currentNoteName = 0;
        _defaultDuration = Fraction.Quarter;
        _currentClef = "treble";
        _keySignature = 0;
        _systemCount = 1;
        _previousSystemY = 0;
        _previousLineEndX = 0;
        _lineJustBroken = false;
        _suppressTrailingBarline = false;
        _title = null;
        _composer = null;
        _tempo = null;
        _timeBeats = 4;
        _timeBeatType = 4;
        _sections = new();
        _structure = null;
        _renderPartName = null;
    }
    
    /// <summary>
    /// Collect all definitions from the syntax tree before rendering.
    /// This includes metadata, sections, structure, and render blocks.
    /// </summary>
    private void CollectDefinitions(SyntaxNode root)
    {
        _root = root;  // Save for fallback processing
        
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MetadataDeclarationSyntax metadata:
                    CollectMetadataItem(metadata);
                    break;
                    
                case TempoDeclarationSyntax tempoDecl:
                    var tempoValues = tempoDecl.Values.ToList();
                    if (tempoValues.Count > 0 && tempoValues[0] is SyntaxTokenNode tempoToken)
                        if (int.TryParse(tempoToken.Text, out int tempo))
                            _tempo = tempo;
                    break;
                    
                case TimeSignatureSyntax timeSig:
                    _timeBeats = timeSig.Beats;
                    _timeBeatType = timeSig.BeatType;
                    break;
                    
                case KeySignatureSyntax key:
                    _keySignature = CalculateKeySignature(key);
                    break;
                    
                case ClefDeclarationSyntax clef:
                    _currentClef = clef.ClefName.Text.ToLowerInvariant();
                    break;
                    
                case SectionDeclarationSyntax section:
                    _sections[section.SectionName] = section;
                    break;
                    
                case StructureDeclarationSyntax structure:
                    _structure = structure;
                    break;
                    
                case RenderDeclarationSyntax render:
                    // Extract the first staff's part name for rendering
                    ExtractRenderPartName(render);
                    break;
            }
        }
    }
    
    private void CollectMetadataItem(MetadataDeclarationSyntax metadata)
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
    
    private void ExtractRenderPartName(RenderDeclarationSyntax render)
    {
        // Find the first staff render and extract its part name
        // StaffRender structure: staff [clef] { partName }
        foreach (var child in render.DescendantNodes())
        {
            if (child is StaffRenderSyntax staff)
            {
                // Part name is either at index 2 (with clef) or index 1 (without clef)
                // staff treble { rightHand } -> [staff, treble, {, rightHand, }]
                // staff { rightHand } -> [staff, {, rightHand, }]
                for (int i = 0; i < staff.SlotCount; i++)
                {
                    var slot = staff.GetChild(i);
                    if (slot is SyntaxTokenNode token && 
                        token.Kind == SyntaxKind.Identifier &&
                        token.Text != "staff" && token.Text != "treble" && 
                        token.Text != "bass" && token.Text != "alto" && token.Text != "tenor")
                    {
                        _renderPartName = token.Text;
                        return;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Render content based on structure block, or fall back to section order.
    /// </summary>
    private void RenderByStructure()
    {
        if (_structure != null)
        {
            // Render according to structure
            for (int i = 0; i < _structure.SlotCount; i++)
            {
                var child = _structure.GetChild(i);
                if (child != null && child is not SyntaxTokenNode)
                    ProcessStructureItem(child);
            }
        }
        else if (_sections.Count > 0)
        {
            // Fallback: render sections in definition order
            foreach (var section in _sections.Values)
            {
                DrawSectionLabel(section.SectionName);
                RenderSectionContent(section);
            }
        }
        else
        {
            // No structure, no sections: fall back to legacy behavior
            // Process all nodes directly (for simple files without section/structure)
            ProcessNode(_root!);

        }
    }
    
    /// <summary>
    /// Process a single item within the structure block.
    /// </summary>
    private void ProcessStructureItem(SyntaxNode item)
    {
        switch (item)
        {
            case StructureRepeatBlockSyntax repeat:
                ProcessRepeatBlock(repeat);
                break;
                
            case SectionReferenceSyntax reference:
                RenderSectionReference(reference.SectionName);
                break;
                
            case StructureAlternativeSyntax alt:
                RenderAlternative(alt);
                break;
                
            case BarlineSyntax barline:
                DrawBarline(barline.BarToken.Text);
                break;
        }
    }
    
    /// <summary>
    /// Process a repeat block: |: ... :| or |: ... | 1. A :| 2. B
    /// </summary>
    private void ProcessRepeatBlock(StructureRepeatBlockSyntax repeat)
    {
        bool afterRepeatStart = false;
        bool hasAlternatives = false;
        
        // Pre-scan for alternatives to determine if this is a simple repeat or has volta brackets
        for (int i = 0; i < repeat.SlotCount; i++)
        {
            if (repeat.GetChild(i) is StructureAlternativeSyntax)
            {
                hasAlternatives = true;
                break;
            }
        }
        
        for (int i = 0; i < repeat.SlotCount; i++)
        {
            var child = repeat.GetChild(i);
            
            // Look ahead: check what follows this element
            bool nextIsRepeatEnd = false;
            bool nextIsAlternative = false;
            for (int j = i + 1; j < repeat.SlotCount; j++)
            {
                var next = repeat.GetChild(j);
                if (next is SyntaxTokenNode nextToken)
                {
                    if (nextToken.Text == ":|")
                        nextIsRepeatEnd = true;
                    else if (nextToken.Text == "|" && hasAlternatives)
                        continue;  // Skip separator, look further
                    break;
                }
                else if (next is StructureAlternativeSyntax)
                {
                    nextIsAlternative = true;
                    break;
                }
                else if (next != null)
                {
                    break;
                }
            }
            
            if (child is SyntaxTokenNode token)
            {
                var text = token.Text;
                
                if (text == "|:")
                {
                    DrawBarline("|:");
                    afterRepeatStart = true;
                }
                else if (text == ":|")
                {
                    // Draw repeat end on current line, then check for line break
                    DrawBarline(":|", checkLineBreak: false);
                    CheckLineBreak();
                }
                else if (text == "|" && afterRepeatStart && hasAlternatives)
                {
                    // This is the alternatives separator - don't draw as regular barline
                    // It marks the start of volta brackets (will implement volta later)
                    // Skip drawing
                }
            }
            else if (child is SectionReferenceSyntax reference && afterRepeatStart)
            {
                // Suppress trailing barline in section if :| follows directly
                _suppressTrailingBarline = nextIsRepeatEnd;
                // Don't check line break if followed by :| or alternatives
                bool suppressLineBreak = nextIsRepeatEnd || nextIsAlternative;
                RenderSectionReference(reference.SectionName, checkLineBreak: !suppressLineBreak);
                _suppressTrailingBarline = false;
            }
            else if (child is StructureAlternativeSyntax alt && afterRepeatStart)
            {
                // Suppress trailing barline if :| follows directly
                _suppressTrailingBarline = nextIsRepeatEnd;
                // Alternatives: don't check line break if :| follows
                RenderAlternative(alt, checkLineBreak: !nextIsRepeatEnd);
                _suppressTrailingBarline = false;
            }
            else if (child != null && afterRepeatStart)
            {
                ProcessStructureItem(child);
            }
        }
    }
    
    /// <summary>
    /// Render a section reference with label and content.
    private void RenderSectionReference(string sectionName, bool checkLineBreak = true)
    {
        if (_sections.TryGetValue(sectionName, out var section))
        {
            DrawSectionLabel(sectionName);
            RenderSectionContent(section);
            
            // Check for line break after section content
            if (checkLineBreak)
                CheckLineBreak();
        }
    }
    
    /// <summary>
    /// Render an alternative ending (1. A, 2. B).
    /// </summary>
    private void RenderAlternative(StructureAlternativeSyntax alt, bool checkLineBreak = true)
    {
        var sectionName = alt.SectionName.Text;
        if (_sections.TryGetValue(sectionName, out var section))
        {
            // TODO: Draw volta bracket with alternative number
            DrawSectionLabel(sectionName);
            RenderSectionContent(section);
            
            if (checkLineBreak)
                CheckLineBreak();
        }
    }
    
    /// <summary>
    /// Render the musical content of a section.
    /// </summary>
    private void RenderSectionContent(SectionDeclarationSyntax section)
    {
        // Find the part that matches _renderPartName
        foreach (var child in section.DescendantNodes())
        {
            if (child is PartBlockSyntax partBlock)
            {
                // If no specific part is requested, or this is the requested part
                if (_renderPartName == null || partBlock.Name == _renderPartName)
                {
                    // Collect nodes and find the last barline
                    var nodes = partBlock.DescendantNodes().ToList();
                    int lastBarlineIndex = -1;
                    for (int i = nodes.Count - 1; i >= 0; i--)
                    {
                        if (nodes[i] is BarlineSyntax)
                        {
                            lastBarlineIndex = i;
                            break;
                        }
                    }
                    
                    // Render all music content, skipping last barline if suppressed
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        if (i == lastBarlineIndex && _suppressTrailingBarline)
                            continue;  // Skip trailing barline before :|
                        ProcessMusicNode(nodes[i]);
                    }
                    
                    // Only render one part per section
                    if (_renderPartName != null)
                        return;
                }
            }
        }
    }
    
    /// <summary>
    /// Process a music node (note, rest, chord, barline, etc.)
    /// </summary>
    private void ProcessMusicNode(SyntaxNode node)
    {
        switch (node)
        {
            case RelativeExpressionSyntax relative:
                InitializeRelativeMode(relative.BasePitch);
                // Don't recurse here - DescendantNodes will handle children
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
                
            case BarlineSyntax barline:
                // Enable line break check for barlines in section content
                DrawBarline(barline.BarToken.Text);
                break;
        }
    }
    
    /// <summary>
    /// Draw title and composer at the top of the page.
    /// </summary>
    private void DrawTitleAndComposer()
    {
        double centerX = PageWidth / 2;
        
        if (_title != null)
        {
            // Title: centered, large font
            _svg.AppendLine($"""  <text x="{centerX:F1}" y="30" font-family="serif" font-size="24" font-weight="bold" text-anchor="middle">{EscapeXml(_title)}</text>""");
        }
        
        if (_composer != null)
        {
            // Composer: right-aligned, smaller font
            double composerX = PageWidth - MarginRight;
            _svg.AppendLine($"""  <text x="{composerX:F1}" y="50" font-family="serif" font-size="14" font-style="italic" text-anchor="end">{EscapeXml(_composer)}</text>""");
        }
    }
    
    /// <summary>
    /// Draw tempo marking above the staff.
    /// </summary>
    private void DrawTempoMarking()
    {
        double tempoX = MarginLeft;
        // LilyPond places tempo marks above staff with padding
        // Position about 2.5 staff-spaces above the top staff line
        double tempoY = _currentY - (SpaceHeight * 2.5);
        
        // Draw quarter note at smaller size (60% of normal music font)
        // LilyPond uses padding of 0.8 staff-space between elements
        double noteSize = FontSize * 0.6;  // Smaller note for tempo marking
        double padding = SpaceHeight * 0.8;  // LilyPond standard padding
        
        // Quarter note glyph (smaller size)
        _svg.AppendLine($"""  <text style="font-family: 'Bravura', 'Bravura Text'; font-size: {noteSize:F0}px;" x="{tempoX:F1}" y="{tempoY:F1}">{SmuflGlyphs.MetNoteQuarterUp}</text>""");
        
        // LilyPond style: note - space - equals - space - number
        // Each element separated by equal padding for balanced appearance
        double textSize = noteSize * 0.5;
        double noteWidth = noteSize * 0.4;  // Approximate width of quarter note glyph
        double equalsPadding = padding * 0.5;  // Padding on each side of equals sign
        
        // Position equals sign with equal spacing on both sides
        double equalsX = tempoX + noteWidth + equalsPadding;
        _svg.AppendLine($"""  <text x="{equalsX:F1}" y="{tempoY:F1}" font-family="Times New Roman, serif" font-size="{textSize:F0}" font-weight="bold">=</text>""");
        
        // Position number after equals with same padding
        double numberX = equalsX + textSize * 0.6 + equalsPadding;  // equals width + padding
        _svg.AppendLine($"""  <text x="{numberX:F1}" y="{tempoY:F1}" font-family="Times New Roman, serif" font-size="{textSize:F0}" font-weight="bold">{_tempo}</text>""");
    }
    
    /// <summary>
    /// Draw key signature (sharps or flats) after the clef.
    /// </summary>
    private void DrawKeySignature()
    {
        if (_keySignature == 0) return;
        
        bool isSharps = _keySignature > 0;
        int count = Math.Abs(_keySignature);
        char glyph = isSharps ? SmuflGlyphs.AccidentalSharp : SmuflGlyphs.AccidentalFlat;
        
        // Sharp positions (F, C, G, D, A, E, B) - line/space from top
        // In treble clef: F5, C5, G5, D5, A4, E5, B4 
        int[] sharpPositions = [8, 5, 9, 6, 3, 7, 4];  // Staff positions from bottom (0 = middle C ledger)
        // Flat positions (B, E, A, D, G, C, F)
        int[] flatPositions = [4, 7, 3, 6, 2, 5, 1];
        
        int[] positions = isSharps ? sharpPositions : flatPositions;
        
        for (int i = 0; i < count && i < positions.Length; i++)
        {
            int pos = positions[i];
            double y = _currentY + StaffHeight - (pos * SpaceHeight / 2);
            DrawGlyph(glyph, _currentX, y);
            _currentX += 10;
        }
        
        _currentX += 5; // Space after key signature
    }
    
    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
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
    
    /// <summary>
    /// Start a new system (staff line) when line break is needed.
    /// </summary>
    private void StartNewSystem()
    {
        _systemCount++;
        _previousSystemY = _currentY;  // Save Y before moving to new line
        _previousLineEndX = _currentX;  // Save X at end of previous line
        _lineJustBroken = true;  // Mark that we just broke the line
        _currentY += StaffHeight + SystemSpacing;
        _currentX = MarginLeft;
        _systemStartY = _currentY;
        
        // Draw new staff lines
        WriteStaffLines(PageWidth - MarginRight);
        
        // Draw clef at the beginning of new system
        DrawClef();
        
        // Draw key signature if set
        if (_keySignature != 0)
        {
            DrawKeySignature();
        }
    }
    

    /// <summary>
    /// Draw section label in a box above the staff.
    /// </summary>
    private void DrawSectionLabel(string sectionName)
    {
        string text = sectionName;
        
        // Position: above the staff, at current X position
        double boxX = _currentX;
        double boxY = _currentY - 25;  // Above staff
        double padding = 4;
        double textWidth = text.Length * 7; // Approximate width
        double boxWidth = textWidth + padding * 2;
        double boxHeight = 16;
        
        // Draw box
        _svg.AppendLine($"""  <rect x="{boxX:F1}" y="{boxY:F1}" width="{boxWidth:F1}" height="{boxHeight:F1}" fill="transparent" stroke="black" stroke-width="1"/>""");
        
        // Draw text
        double textX = boxX + padding;
        double textY = boxY + boxHeight - 4;
        _svg.AppendLine($"""  <text x="{textX:F1}" y="{textY:F1}" font-family="Arial, sans-serif" font-size="12">{text}</text>""");
    }
    /// <summary>
    /// Check if line break is needed and perform it if necessary.
    /// Called after drawing a barline.
    /// </summary>
    private void CheckLineBreak()
    {
        if (_currentX >= _lineBreakThreshold)
        {
            StartNewSystem();
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
                InitializeRelativeMode(relative.BasePitch);
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
                
            case BarlineSyntax barline:
                DrawBarline(barline.BarToken.Text);
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
                
            case SectionDeclarationSyntax section:
                DrawSectionLabel(section.SectionName);
                // Process children (part blocks)
                for (int i = 0; i < section.SlotCount; i++)
                {
                    var child = section.GetChild(i);
                    if (child != null && child is not SyntaxTokenNode)
                        ProcessNode(child);
                }
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
    
    private void DrawBarline(string barType = "|", bool checkLineBreak = true)
    {
        double x = _currentX;
        // SMuFL barline glyphs are positioned at the bottom staff line
        double y = _currentY + StaffHeight;
        
        // Special handling for repeat end: if we just broke the line, 
        // draw on previous line instead
        if (barType == ":|" && _lineJustBroken && _previousSystemY > 0)
        {
            x = _previousLineEndX;
            y = _previousSystemY + StaffHeight;
            DrawGlyph(SmuflGlyphs.RepeatRight, x, y);
            _lineJustBroken = false;  // Reset flag
            return;  // Don't advance _currentX on current line
        }
        
        switch (barType)
        {
            case "|:":  // Repeat start
                DrawGlyph(SmuflGlyphs.RepeatLeft, x, y);
                _currentX += 20 + SpaceAfterBarline;
                _lineJustBroken = false;
                break;
                
            case ":|":  // Repeat end
                DrawGlyph(SmuflGlyphs.RepeatRight, x, y);
                _currentX += 20 + SpaceAfterBarline;
                _lineJustBroken = false;
                break;
                
            case ":|:":  // Repeat both
                DrawGlyph(SmuflGlyphs.RepeatRightLeft, x, y);
                _currentX += 24 + SpaceAfterBarline;
                _lineJustBroken = false;
                break;
                
            case "||":  // Double bar
                DrawGlyph(SmuflGlyphs.BarlineDouble, x, y);
                _currentX += 8 + SpaceAfterBarline;
                _lineJustBroken = false;
                break;
                
            case "|.":  // Final bar
                DrawGlyph(SmuflGlyphs.BarlineFinal, x, y);
                _currentX += 10 + SpaceAfterBarline;
                _lineJustBroken = false;
                break;
                
            default:  // Single bar |
                DrawGlyph(SmuflGlyphs.BarlineSingle, x, y);
                _currentX += SpaceAfterBarline;
                _lineJustBroken = false;
                break;
        }
        
        // Check for line break after barline (except final bar and repeat start)
        if (checkLineBreak && barType != "|." && barType != "|:")
        {
            CheckLineBreak();
        }
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
        int noteName = GetNoteName(pitch.BaseName);
        
        // LilyPond relative octave algorithm: find closest octave
        int upOctave = _currentOctave;
        if (_currentNoteName > noteName)
            upOctave++;
        
        int downOctave = _currentOctave;
        if (_currentNoteName < noteName)
            downOctave--;
        
        // Calculate steps (note name + octave * 7)
        int currentSteps = _currentNoteName + _currentOctave * 7;
        int upSteps = noteName + upOctave * 7;
        int downSteps = noteName + downOctave * 7;
        
        // Choose the closer octave
        int targetOctave;
        if (Math.Abs(upSteps - currentSteps) < Math.Abs(downSteps - currentSteps))
            targetOctave = upOctave;
        else
            targetOctave = downOctave;
        
        // Apply explicit octave offset (' or ,)
        targetOctave += pitch.OctaveOffset;
        
        // Update current state for next note
        _currentNoteName = noteName;
        _currentOctave = targetOctave;
        
        // Position on staff (0 = middle C ledger line in treble clef)
        // Treble clef: E4 = line 0 (bottom), so C4 = -2
        int staffPosition = noteName + ((targetOctave - 4) * 7) - 2;
        
        return (staffPosition, targetOctave);
    }
    
    private void InitializeRelativeMode(PitchSyntax basePitch)
    {
        _currentNoteName = GetNoteName(basePitch.BaseName);
        // Base octave: c' = octave 4, c'' = octave 5, etc.
        _currentOctave = 3 + basePitch.OctaveOffset;
    }
    
    private static int GetNoteName(char baseName)
    {
        return baseName switch
        {
            'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3,
            'g' => 4, 'a' => 5, 'b' => 6, _ => 0
        };
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