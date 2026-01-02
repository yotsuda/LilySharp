using System.Text;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Renders a Score with its ScoreLayout to SVG.
/// </summary>
public sealed class SvgRenderer
{
    // SVG output scale: pixels per staff space for the width/height attributes
    // (viewBox uses staff spaces directly; this is only for the outer dimensions)
    private const double SpaceHeight = 10;
    
    // Layout constants (in staff spaces)
    private const double StaffHeight = 4;  // 4 staff spaces between top and bottom staff lines
    private const double FontSize = 4;  // 4 staff spaces for music glyphs
    
    // Derived from SMuFL defaults (all in staff spaces)
    private static double StaffLineThickness => EngravingDefaults.StaffLineThickness;
    private static double StemThickness => EngravingDefaults.StemThickness;
    private static double ThinBarlineThickness => EngravingDefaults.ThinBarlineThickness;
    private static double LegerLineExtension => EngravingDefaults.LegerLineExtension;
    private static double LegerLineThickness => EngravingDefaults.LegerLineThickness;
    
    // Stem attachment points (in staff spaces)
    private static double StemUpAttachX => EngravingDefaults.StemUpAttachX;
    private static double StemUpAttachY => EngravingDefaults.StemUpAttachY;
    private static double StemDownAttachX => EngravingDefaults.StemDownAttachX;
    private static double StemDownAttachY => EngravingDefaults.StemDownAttachY;
    private static double StemHeight => EngravingRules.StandardStemLength;
    
    /// <summary>
    /// Converts staff spaces to pixels for SVG width/height attributes only.
    /// All internal coordinates use staff spaces directly via viewBox.
    /// </summary>
    private static double Px(double staffSpaces) => staffSpaces * SpaceHeight;
    
    private readonly StringBuilder _svg = new();
    private readonly LayoutOptions _layoutOptions;
    private readonly SvgRenderOptions _renderOptions;
    private Dictionary<MusicItem, double> _beamedStemEndYs = new();
    private Dictionary<MusicItem, bool> _beamedStemUp = new();
    
    public SvgRenderer(LayoutOptions? layoutOptions = null, SvgRenderOptions? renderOptions = null)
    {
        _layoutOptions = layoutOptions ?? LayoutOptions.Default;
        _renderOptions = renderOptions ?? SvgRenderOptions.Default;
    }
    
    /// <summary>
    /// Renders a score with its layout to SVG.
    /// </summary>
    public string Render(Score score, ScoreLayout layout)
    {
        _svg.Clear();
        
        // Build measure to system mapping for beam processing
        var measureToSystem = new Dictionary<int, SystemLayout>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystem[measure.MeasureIndex] = system;
            }
        }
        
        // Calculate stem end Y positions for beamed notes (all in staff spaces)
        _beamedStemEndYs.Clear();
        _beamedStemUp.Clear();
        foreach (var beamLayout in layout.BeamLayouts)
        {
            if (!measureToSystem.TryGetValue(beamLayout.Group.MeasureIndex, out var system))
                continue;
            
            var group = beamLayout.Group;
            double staffMiddleY = system.Y + StaffHeight / 2;
            
            // Stem X offset from note center
            var noteheadBBox = GlyphMetrics.GetNoteheadBBox(3);
            double noteheadCenterX = noteheadBBox.CenterX;
            double stemOffsetX = group.StemUp 
                ? StemUpAttachX - noteheadCenterX 
                : StemDownAttachX - noteheadCenterX;
            
            // Calculate beam endpoints at stem X positions
            double leftStemX = beamLayout.LeftX + stemOffsetX;
            double rightStemX = beamLayout.RightX + stemOffsetX;
            double leftBeamCenterY = staffMiddleY - beamLayout.LeftY / 2;  // staff positions to staff spaces
            double rightBeamCenterY = staffMiddleY - beamLayout.RightY / 2;  // staff positions to staff spaces
            
            // Beam slope
            double beamSpanX = rightStemX - leftStemX;
            double slope = beamSpanX > 0.001 ? (rightBeamCenterY - leftBeamCenterY) / beamSpanX : 0;
            
            // Beam thickness
            double beamThickness = BeamThickness;  // already in staff spaces
            
            for (int i = 0; i < group.Members.Length; i++)
            {
                var member = group.Members[i];
                
                // This note's stem X position
                double noteCenterX = beamLayout.MemberXPositions[i];
                double stemX = noteCenterX + stemOffsetX;
                
                // Primary beam Y at this stem X (center of beam)
                double primaryBeamCenterY = leftBeamCenterY + slope * (stemX - leftStemX);
                
                // Stem extends to the far edge of the primary beam (away from notehead)
                double stemEndY;
                if (group.StemUp)
                {
                    // Stem goes up, extends to top edge of primary beam (smallest Y)
                    stemEndY = primaryBeamCenterY - beamThickness / 2;
                }
                else
                {
                    // Stem goes down, extends to bottom edge of primary beam (largest Y)
                    stemEndY = primaryBeamCenterY + beamThickness / 2;
                }
                
                _beamedStemEndYs[member.Item] = stemEndY;
                _beamedStemUp[member.Item] = group.StemUp;
            }
        }
        WriteHeader(layout.Width, layout.Height);
        
        // Draw header (title/composer)
        if (score.Title != null || score.Composer != null)
            DrawHeader(score, layout);
        
        // Draw each system
        for (int sysIdx = 0; sysIdx < layout.Systems.Length; sysIdx++)
        {
            var system = layout.Systems[sysIdx];
            bool isFirstSystem = sysIdx == 0;
            
            DrawSystem(score, layout, system, isFirstSystem);
        }
        
        // Draw beams
        DrawBeams(layout);
        
        // Draw ties
        DrawTies(layout);
        
        // Draw slurs
        DrawSlurs(layout);
        
        // Draw dynamics
        DrawDynamics(layout);
        
        // Draw articulations
        DrawArticulations(layout);
        
        // Draw grace notes
        DrawGraceNotes(layout);
        
        WriteFooter();
        
        return _svg.ToString();
    }
    
    /// <summary>
    /// Renders a multi-staff score with its layout to SVG.
    /// </summary>
    public string Render(MultiStaffScore score, ScoreLayout layout)
    {
        _svg.Clear();
        
        // Calculate stem end Y positions for beamed notes
        _beamedStemEndYs.Clear();
        _beamedStemUp.Clear();
        CalculateMultiStaffBeamStemPositions(score, layout);
        
        WriteHeader(layout.Width, layout.Height);
        
        // Draw header (title/composer)
        if (score.Title != null || score.Composer != null)
            DrawMultiStaffHeader(score, layout);
        
        // Draw each system
        for (int sysIdx = 0; sysIdx < layout.Systems.Length; sysIdx++)
        {
            var system = layout.Systems[sysIdx];
            bool isFirstSystem = sysIdx == 0;
            
            DrawMultiStaffSystem(score, layout, system, isFirstSystem);
        }
        
        // Draw beams
        DrawMultiStaffBeams(score, layout);

        WriteFooter();
        
        return _svg.ToString();
    }
    
    private void DrawMultiStaffHeader(MultiStaffScore score, ScoreLayout layout)
    {
        double centerX = layout.Width / 2;
        double y = _layoutOptions.MarginTop;
        
        if (score.Title != null)
        {
            _svg.AppendLine($"""  <text class="title" x="{centerX}" y="{y}" text-anchor="middle" font-size="2.5">{EscapeXml(score.Title)}</text>""");
            y += 3;  // 3 staff spaces
        }
        
        if (score.Composer != null)
        {
            _svg.AppendLine($"""  <text class="composer" x="{centerX}" y="{y}" text-anchor="middle" font-size="1.5">{EscapeXml(score.Composer)}</text>""");
        }
    }
    
    private void DrawMultiStaffSystem(MultiStaffScore score, ScoreLayout scoreLayout, SystemLayout system, bool isFirstSystem)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return;
        
        double startX = _layoutOptions.MarginLeft;
        
        // Calculate the actual end of the system (right edge of last measure)
        double endX;
        if (system.Measures.Length > 0)
        {
            var lastMeasure = system.Measures[^1];
            endX = lastMeasure.X + lastMeasure.Width;
        }
        else
        {
            endX = _layoutOptions.PageWidth - _layoutOptions.MarginRight;
        }
        
        // Draw each staff group
        foreach (var staffGroup in system.StaffGroups)
        {
            DrawStaffGroup(score, scoreLayout, system, staffGroup, startX, endX, isFirstSystem);
        }
        
        // Draw system barlines (connecting all staves)
        DrawSystemBarlines(system, scoreLayout, startX, endX);
    }
    
    private void DrawStaffGroup(
        MultiStaffScore score,
        ScoreLayout scoreLayout,
        SystemLayout system,
        StaffGroupLayout staffGroup,
        double startX,
        double endX,
        bool isFirstSystem)
    {
        // Draw brace if this is a grand staff
        if (staffGroup.IsGrandStaff && staffGroup.GrandStaffLayout != null)
        {
            var grandStaff = staffGroup.GrandStaffLayout;
            // Adjust Y positions relative to system
            double braceTop = system.Y + grandStaff.BraceTop;
            double braceBottom = system.Y + grandStaff.BraceBottom;
            var braceSvg = BraceRenderer.RenderBrace(grandStaff.BraceX, braceTop, braceBottom);
            _svg.AppendLine($"  {braceSvg}");
        }
        
        // Draw each staff in the group
        foreach (var staffLayout in staffGroup.Staves)
        {
            double staffY = system.Y + staffLayout.Y;
            DrawStaff(score, scoreLayout, system, staffLayout, staffY, startX, endX, isFirstSystem);
        }
    }
    
    private void DrawStaff(
        MultiStaffScore score,
        ScoreLayout scoreLayout,
        SystemLayout system,
        StaffLayout staffLayout,
        double staffY,
        double startX,
        double endX,
        bool isFirstSystem)
    {
        // Draw 5 staff lines
        for (int i = 0; i < 5; i++)
        {
            double lineY = staffY + i;  // 1 staff space between lines
            _svg.AppendLine($"""  <line class="staff" x1="{startX}" y1="{lineY}" x2="{endX}" y2="{lineY}"/>""");
        }
        
        // Draw clef
        double currentX = startX;
        char clefGlyph = staffLayout.Clef switch
        {
            ClefType.Bass => EmmentalerGlyphs.FClef,
            ClefType.Alto => EmmentalerGlyphs.CClef,
            ClefType.Tenor => EmmentalerGlyphs.CClef,
            _ => EmmentalerGlyphs.GClef
        };
        double clefY = staffLayout.Clef switch
        {
            ClefType.Bass => staffY + 1,
            ClefType.Alto => staffY + 2,
            ClefType.Tenor => staffY + 1,
            _ => staffY + 3
        };
        double clefWidth = staffLayout.Clef switch
        {
            ClefType.Bass => GlyphMetrics.FClefWidth,
            ClefType.Alto or ClefType.Tenor => GlyphMetrics.CClefWidth,
            _ => GlyphMetrics.GClefWidth
        };
        DrawGlyph(clefGlyph, currentX, clefY);
        double clefRightEdge = currentX + clefWidth;
        
        // Draw key signature
        string clefName = staffLayout.Clef switch
        {
            ClefType.Bass => "bass",
            ClefType.Alto => "alto",
            ClefType.Tenor => "tenor",
            _ => "treble"
        };
        bool hasKeySignature = score.KeySignature.Count > 0;
        if (hasKeySignature)
        {
            currentX = clefRightEdge + (GlyphMetrics.ClefToKeySignatureSpace - clefWidth);
            currentX = DrawKeySignature(score.KeySignature, clefName, currentX, staffY);
        }
        
        // Draw time signature (first system only)
        if (isFirstSystem)
        {
            if (hasKeySignature)
            {
                currentX += GlyphMetrics.KeySignatureToTimeSignatureSpace;
            }
            else
            {
                currentX = clefRightEdge + (GlyphMetrics.ClefToTimeSignatureSpace - clefWidth);
            }
            DrawTimeSignature(score.TimeSignature, currentX, staffY);
        }
        
        // Find the matching staff and voice in the score
        var matchingStaff = FindStaffForLayout(score, staffLayout.StaffIndex);
        if (matchingStaff == null)
            return;
        
        // Draw measures for this staff's voices
        foreach (var measureLayout in system.Measures)
        {
            foreach (var voice in matchingStaff.Voices)
            {
                if (measureLayout.MeasureIndex < voice.Measures.Length)
                {
                    var measure = voice.Measures[measureLayout.MeasureIndex];
                    // For multi-staff, use auto stem direction based on clef
                    bool? forcedStemUp = null;
                    DrawMeasure(measure, measureLayout, measureLayout.MeasureIndex, 1, staffY, scoreLayout, forcedStemUp, isFirstVoice: true, skipBarlines: true);
                }
            }
        }
    }
    
    private Staff? FindStaffForLayout(MultiStaffScore score, int staffIndex)
    {
        int currentIndex = 0;
        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                if (currentIndex == staffIndex)
                    return staff;
                currentIndex++;
            }
        }
        return null;
    }
    
    private void DrawSystemBarlines(SystemLayout system, ScoreLayout scoreLayout, double startX, double endX)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return;
        
        // Get the Y range for all staves
        double topY = double.MaxValue;
        double bottomY = double.MinValue;
        
        foreach (var staffGroup in system.StaffGroups)
        {
            foreach (var staff in staffGroup.Staves)
            {
                double staffTop = system.Y + staff.Y;
                double staffBottom = staffTop + StaffHeight;
                topY = Math.Min(topY, staffTop);
                bottomY = Math.Max(bottomY, staffBottom);
            }
        }
        
        // Draw start barline (connecting all staves)
        _svg.AppendLine($"""  <line class="barline" x1="{startX}" y1="{topY}" x2="{startX}" y2="{bottomY}"/>""");
        
        // Draw barlines at measure boundaries
        foreach (var measureLayout in system.Measures)
        {
            double barlineX = measureLayout.X + measureLayout.Width;
            _svg.AppendLine($"""  <line class="barline" x1="{barlineX}" y1="{topY}" x2="{barlineX}" y2="{bottomY}"/>""");
        }
    }

    
    private void WriteHeader(double width, double height)
    {
        _svg.AppendLine($"""<?xml version="1.0" encoding="UTF-8"?>""");
        _svg.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{Px(width)}" height="{Px(height)}" viewBox="0 0 {width} {height}">""");
        _svg.AppendLine("<style>");
        
        // Font face - either embedded or referenced by path
        var fontFace = GetFontFaceRule();
        _svg.AppendLine($"  {fontFace}");
        
        _svg.AppendLine("  .music { font-family: 'Emmentaler', serif; }");
        _svg.AppendLine($"  .staff {{ stroke: black; stroke-width: {StaffLineThickness:F2}; }}");
        _svg.AppendLine($"  .stem {{ stroke: black; stroke-width: {StemThickness:F2}; }}");
        _svg.AppendLine($"  .barline {{ stroke: black; stroke-width: {ThinBarlineThickness:F2}; }}");
        _svg.AppendLine($"  .ledger {{ stroke: black; stroke-width: {LegerLineThickness:F2}; }}");
        _svg.AppendLine("  .title { font-family: serif; font-size: 0.6; font-weight: bold; }");
        _svg.AppendLine("  .composer { font-family: serif; font-size: 0.4; font-style: italic; }");
        _svg.AppendLine("  .tempo { font-family: serif; font-size: 0.35; }");
        _svg.AppendLine("  .section-label { font-family: serif; font-size: 0.4; font-weight: bold; }");
        _svg.AppendLine("</style>");
    }
    
    private string GetFontFaceRule()
    {
        // Preview mode: omit @font-face (font defined externally in HTML)
        if (_renderOptions.OmitFontFace)
        {
            return "";
        }
        
        var sb = new StringBuilder();
        
        // Export mode: embed fonts as Base64
        if (_renderOptions.EmbedFont)
        {
            // Embed Emmentaler (music notation) font
            var musicFontPath = FindFontFile("emmentaler-20.woff2");
            if (musicFontPath != null && File.Exists(musicFontPath))
            {
                var fontBytes = File.ReadAllBytes(musicFontPath);
                var base64 = Convert.ToBase64String(fontBytes);
                sb.AppendLine($"@font-face {{ font-family: 'Emmentaler'; src: url('data:font/woff2;base64,{base64}') format('woff2'); }}");
            }
            
            // Embed Emmentaler-Brace font
            var braceFontPath = FindFontFile("emmentaler-brace.woff");
            if (braceFontPath != null && File.Exists(braceFontPath))
            {
                var fontBytes = File.ReadAllBytes(braceFontPath);
                var base64 = Convert.ToBase64String(fontBytes);
                sb.AppendLine($"  @font-face {{ font-family: 'Emmentaler-Brace'; src: url('data:font/woff;base64,{base64}') format('woff'); }}");
            }
            
            if (sb.Length > 0)
                return sb.ToString().TrimEnd();
        }
        
        // Default: reference fonts by name (requires fonts installed on system)
        return "@font-face { font-family: 'Emmentaler'; src: local('Emmentaler'); }\n  @font-face { font-family: 'Emmentaler-Brace'; src: local('Emmentaler-Brace'); }";
    }
    
    private string? FindFontFile(string fontFileName)
    {
        // Check specified directory first
        if (!string.IsNullOrEmpty(_renderOptions.FontDirectory))
        {
            var specifiedPath = Path.Combine(_renderOptions.FontDirectory, fontFileName);
            if (File.Exists(specifiedPath))
                return specifiedPath;
        }
        
        // Search in common locations
        var candidates = new[]
        {
            fontFileName,
            $"fonts/{fontFileName}",
            $"../fonts/{fontFileName}",
            Path.Combine(AppContext.BaseDirectory, "fonts", fontFileName),
            Path.Combine(AppContext.BaseDirectory, fontFileName)
        };
        
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }
        
        return null;
    }
    
    private void WriteFooter()
    {
        _svg.AppendLine("</svg>");
    }
    
    private void DrawHeader(Score score, ScoreLayout layout)
    {
        double centerX = layout.Width / 2;
        double y = _layoutOptions.MarginTop;
        
        if (score.Title != null)
        {
            _svg.AppendLine($"""  <text class="title" x="{centerX}" y="{y}" text-anchor="middle" font-size="2.5">{EscapeXml(score.Title)}</text>""");
            y += 2.5;  // 2.5 staff spaces
        }
        
        if (score.Composer != null)
        {
            _svg.AppendLine($"""  <text class="composer" x="{centerX}" y="{y}" text-anchor="middle" font-size="1.5">{EscapeXml(score.Composer)}</text>""");
        }
    }
    
    private void DrawSystem(Score score, ScoreLayout scoreLayout, SystemLayout system, bool isFirstSystem)
    {
        double y = system.Y;
        double startX = _layoutOptions.MarginLeft;
        
        // Calculate the actual end of the system (right edge of last measure)
        double endX;
        if (system.Measures.Length > 0)
        {
            var lastMeasure = system.Measures[^1];
            endX = lastMeasure.X + lastMeasure.Width;
        }
        else
        {
            endX = _layoutOptions.PageWidth - _layoutOptions.MarginRight;
        }
        
        // Draw staff lines
        for (int i = 0; i < 5; i++)
        {
            double lineY = y + i;  // 1 staff space between lines
            _svg.AppendLine($"""  <line class="staff" x1="{startX}" y1="{lineY}" x2="{endX}" y2="{lineY}"/>""");
        }
        
        // Draw clef
        double currentX = startX;
        char clefGlyph = score.Clef switch
        {
            "bass" => EmmentalerGlyphs.FClef,
            "alto" => EmmentalerGlyphs.CClef,
            "tenor" => EmmentalerGlyphs.CClef,
            _ => EmmentalerGlyphs.GClef
        };
        double clefY = score.Clef switch
        {
            "bass" => y + 1,
            "alto" => y + 2,
            "tenor" => y + 1,
            _ => y + 3
        };
        double clefWidth = score.Clef switch
        {
            "bass" => GlyphMetrics.FClefWidth,
            "alto" or "tenor" => GlyphMetrics.CClefWidth,
            _ => GlyphMetrics.GClefWidth
        };
        DrawGlyph(clefGlyph, currentX, clefY);
        double clefRightEdge = currentX + clefWidth;
        
        // Draw key signature
        bool hasKeySignature = score.KeySignature.Count > 0;
        if (hasKeySignature)
        {
            currentX = clefRightEdge + (GlyphMetrics.ClefToKeySignatureSpace - clefWidth);
            currentX = DrawKeySignature(score.KeySignature, score.Clef, currentX, y);
        }
        
        // Draw time signature (first system only)
        if (isFirstSystem)
        {
            if (hasKeySignature)
            {
                // Key signature already positioned currentX
                currentX += GlyphMetrics.KeySignatureToTimeSignatureSpace;
            }
            else
            {
                currentX = clefRightEdge + (GlyphMetrics.ClefToTimeSignatureSpace - clefWidth);
            }
            DrawTimeSignature(score.TimeSignature, currentX, y);
            currentX += GlyphMetrics.TimeSignatureWidth + GlyphMetrics.TimeSignatureToFirstNoteSpace;
        }
        else
        {
            // Subsequent systems: no time signature, just spacing after clef/key
            if (!hasKeySignature)
            {
                currentX = clefRightEdge + (GlyphMetrics.ClefToFirstNoteSpace - clefWidth);
            }
            else
            {
                currentX += GlyphMetrics.KeySignatureToFirstNoteSpace;
            }
        }
        
        // Draw tempo marking (first system only)
        if (isFirstSystem && score.Tempo.HasValue)
        {
            DrawTempoMarking(score.Tempo.Value, startX, y);
        }
        
        // Draw measures (all voices)
        foreach (var measureLayout in system.Measures)
        {
            // Draw each voice
            for (int voiceIdx = 0; voiceIdx < score.Voices.Length; voiceIdx++)
            {
                var voice = score.Voices[voiceIdx];
                if (measureLayout.MeasureIndex < voice.Measures.Length)
                {
                    var measure = voice.Measures[measureLayout.MeasureIndex];
                    int voiceNumber = voiceIdx + 1;
                    bool? forcedStemUp = VoiceDefaults.GetDefaultStemUp(voiceNumber);
                    DrawMeasure(measure, measureLayout, measureLayout.MeasureIndex, voiceNumber, y, scoreLayout, forcedStemUp, isFirstVoice: voiceIdx == 0);
                }
            }
        }
    }
    
    private void DrawMeasure(Measure measure, MeasureLayout layout, int measureIndex, int voiceNumber, double systemY, ScoreLayout scoreLayout, bool? forcedStemUp = null, bool isFirstVoice = true, bool skipBarlines = false)
    {
        double x = layout.X;
        double staffBottom = systemY + StaffHeight;
        
        // Draw section label if present (first voice only to avoid duplicates)
        if (isFirstVoice && measure.SectionLabel != null)
        {
            DrawSectionLabel(measure.SectionLabel, x, systemY);
        }
        
        // Draw start barline (first voice only to avoid duplicates, skip for multi-staff)
        if (isFirstVoice && !skipBarlines && measure.StartBarline != BarlineType.None)
        {
            DrawBarline(measure.StartBarline, x, systemY);
        }
        
        // Draw items using column-based timing for multi-staff alignment
        var currentTiming = Fraction.Zero;
        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            
            // Calculate X position using timing-based columns
            double itemX;
            if (!layout.Columns.IsDefaultOrEmpty && layout.Columns.Length > 0)
            {
                // Use column-based positioning for multi-staff scores
                itemX = x + layout.GetXForTiming(currentTiming);
            }
            else if (i < layout.Items.Length)
            {
                // Fallback to direct item layout for single-staff scores
                itemX = x + layout.Items[i].X;
            }
            else
            {
                // Should not happen, but provide a reasonable fallback
                itemX = x;
            }
            
            // Get voice collision offset
            double voiceOffset = scoreLayout.GetVoiceOffset(measureIndex, voiceNumber, i);
            itemX += voiceOffset;
            
            switch (item)
            {
                case NoteItem note:
                    DrawNote(note, itemX, systemY, forcedStemUp);
                    break;
                case RestItem rest:
                    double restShift = scoreLayout.GetRestShift(measureIndex, i);
                    DrawRest(rest, itemX, systemY, restShift);
                    break;
                case ChordItem chord:
                    DrawChord(chord, itemX, systemY, forcedStemUp);
                    break;
            }
            
            currentTiming += item.Duration;
        }
        
        // Draw end barline at the right edge of the measure (first voice only, skip for multi-staff)
        if (isFirstVoice && !skipBarlines)
        {
            double endX = x + layout.Width;
            DrawBarline(measure.EndBarline, endX, systemY);
        }
    }
    
    private void DrawNote(NoteItem note, double x, double systemY, bool? forcedStemUp = null)
    {
        // x is the reference point (center of notehead in Spring-Rod model)
        double noteY = systemY + StaffHeight / 2 - (note.StaffPosition / 2.0);
        int noteValue = GetNoteValue(note.BaseDuration);
        
        // Get notehead metrics from GlyphMetrics
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadWidth = noteheadBBox.Width;
        double noteheadCenterX = noteheadBBox.CenterX;
        
        // Convert reference point to notehead left edge (SMuFL glyphs are drawn from left edge)
        double noteheadLeftX = x - noteheadCenterX;
        
        // Draw accidental (to the left of notehead)
        if (note.Accidental != null)
        {
            char accGlyph = note.Accidental switch
            {
                "doubleSharp" => EmmentalerGlyphs.AccidentalDoubleSharp,
                "sharp" => EmmentalerGlyphs.AccidentalSharp,
                "flat" => EmmentalerGlyphs.AccidentalFlat,
                "doubleFlat" => EmmentalerGlyphs.AccidentalDoubleFlat,
                _ => EmmentalerGlyphs.AccidentalNatural
            };
            
            // Get accidental metrics
            var accBBox = GlyphMetrics.GetAccidentalBBox(note.Accidental);
            double accWidth = accBBox.Width;
            double accNoteGap = GlyphMetrics.AccidentalNoteGap;
            
            // Accidental is drawn to the left of notehead with a gap
            double accidentalX = noteheadLeftX - accWidth - accNoteGap;
            DrawGlyph(accGlyph, accidentalX, noteY);
        }
        
        // Draw ledger lines
        if (note.NeedsLedgerLines)
        {
            DrawLedgerLines(note.StaffPosition, noteheadLeftX, noteheadWidth, systemY);
        }
        
        // Draw notehead
        char notehead = EmmentalerGlyphs.GetNotehead(noteValue);
        DrawGlyph(notehead, noteheadLeftX, noteY, note.SourcePosition);
        
        // Draw stem using GlyphMetrics anchor points
        if (noteValue >= 2)
        {
            // Priority: beam direction > forced (voice) direction > note's own direction
            bool stemUp = _beamedStemUp.TryGetValue(note, out bool beamStemUp) 
                ? beamStemUp 
                : forcedStemUp ?? note.StemUp;
            var stemAnchor = stemUp ? GlyphMetrics.StemUpSE : GlyphMetrics.StemDownNW;
            double stemX = noteheadLeftX + stemAnchor.X;
            double stemAttachY = noteY - stemAnchor.Y;
            
            // Use beam-calculated stem end if part of a beam group, otherwise fixed length
            double stemEndY;
            if (_beamedStemEndYs.TryGetValue(note, out double beamStemEndY))
            {
                stemEndY = beamStemEndY;
            }
            else
            {
                stemEndY = stemUp ? stemAttachY - StemHeight : stemAttachY + StemHeight;
            }
            
            _svg.AppendLine($"""  <line class="stem" x1="{stemX:F1}" y1="{stemAttachY:F1}" x2="{stemX:F1}" y2="{stemEndY:F1}"/>""");
            
            // Draw flag (only if not beamed)
            if (!_beamedStemEndYs.ContainsKey(note))
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
                if (flag.HasValue)
                {
                    DrawGlyph(flag.Value, stemX, stemEndY);
                }
            
            // Draw tremolo (if present)
            if (note.HasTremolo)
            {
                DrawTremolo(stemX, stemAttachY, stemEndY, stemUp, note.TremoloBeams);
            }
            }
        }
        
        // Draw dots (to the right of notehead)
        var dotBBox = GlyphMetrics.AugmentationDot;
        double dotWidth = dotBBox.Width;
        double dotGap = EngravingDefaults.DotGap;  // Gap between notehead and first dot
        for (int d = 0; d < note.Dots; d++)
        {
            double dotX = noteheadLeftX + noteheadWidth + dotGap + d * (dotWidth + dotGap);
            
            // Dots must avoid staff lines
            // If note is on a line (StaffPosition is even), shift dot up by half a space
            double dotYOffset = 0;
            if (note.StaffPosition % 2 == 0)
            {
                // On a staff line - shift dot up to sit in the space above
                dotYOffset = -0.5;
            }
            
            double dotY = noteY + dotYOffset;
            DrawGlyph(EmmentalerGlyphs.AugmentationDot, dotX, dotY);
        }
    }
    
    private void DrawRest(RestItem rest, double x, double systemY, double shiftStaffPositions = 0)
    {
        int noteValue = GetNoteValue(rest.BaseDuration);
        double restY = systemY + 2;
        
        if (noteValue == 1)
            restY = systemY + 1;
        else if (noteValue == 2)
            restY = systemY + 2;
        
        // Apply shift (positive shift = move down in staff positions, which is up in Y)
        // Staff position increases upward, but Y increases downward
        restY -= shiftStaffPositions / 2;
        
        char restGlyph = EmmentalerGlyphs.GetRest(noteValue);
        DrawGlyph(restGlyph, x, restY, rest.SourcePosition);
    }
    
    private void DrawChord(ChordItem chord, double x, double systemY, bool? forcedStemUp = null)
    {
        int noteValue = GetNoteValue(chord.BaseDuration);
        double noteheadWidth = (noteValue == 1 ? EngravingDefaults.NoteheadWholeWidth : EngravingDefaults.NoteheadBlackWidth);
        char notehead = EmmentalerGlyphs.GetNotehead(noteValue);
        
        // Calculate accidental positions
        var accidentalPlacement = new AccidentalPlacement();
        var accidentalLayouts = accidentalPlacement.CalculatePositions(chord.Notes);
        var accidentalMap = accidentalLayouts.ToDictionary(a => a.StaffPosition);
        
        foreach (var note in chord.Notes)
        {
            double noteY = systemY + StaffHeight / 2 - (note.StaffPosition / 2.0);
            
            // Draw accidental with calculated position
            if (note.Accidental != null && accidentalMap.TryGetValue(note.StaffPosition, out var accLayout))
            {
                char accGlyph = note.Accidental switch
                {
                    "doubleSharp" => EmmentalerGlyphs.AccidentalDoubleSharp,
                    "sharp" => EmmentalerGlyphs.AccidentalSharp,
                    "flat" => EmmentalerGlyphs.AccidentalFlat,
                    "doubleFlat" => EmmentalerGlyphs.AccidentalDoubleFlat,
                    _ => EmmentalerGlyphs.AccidentalNatural
                };
                DrawGlyph(accGlyph, x + accLayout.XOffset, noteY);
            }
            
            // Draw ledger lines
            if (note.NeedsLedgerLines)
            {
                DrawLedgerLines(note.StaffPosition, x, noteheadWidth, systemY);
            }
            
            // Draw notehead
            DrawGlyph(notehead, x, noteY, chord.SourcePosition);
        }
        
        // Draw single stem for chord
        if (noteValue >= 2 && chord.Notes.Length > 0)
        {
            // Priority: beam direction > forced (voice) direction > chord's own direction
            bool stemUp = _beamedStemUp.TryGetValue(chord, out bool beamStemUp) 
                ? beamStemUp 
                : forcedStemUp ?? chord.StemUp;
            
            int stemNotePos = stemUp
                ? chord.Notes.Min(n => n.StaffPosition)
                : chord.Notes.Max(n => n.StaffPosition);
            double stemNoteY = systemY + StaffHeight / 2 - (stemNotePos / 2.0);
            
            double stemX = stemUp ? x + StemUpAttachX : x + StemDownAttachX;
            double stemAttachY = stemUp ? stemNoteY - StemUpAttachY : stemNoteY - StemDownAttachY;
            
            // Use beam-calculated stem end if part of a beam group, otherwise fixed length
            double stemEndY;
            if (_beamedStemEndYs.TryGetValue(chord, out double beamStemEndY))
            {
                stemEndY = beamStemEndY;
            }
            else
            {
                stemEndY = stemUp ? stemAttachY - StemHeight : stemAttachY + StemHeight;
            }
            
            _svg.AppendLine($"""  <line class="stem" x1="{stemX:F1}" y1="{stemAttachY:F1}" x2="{stemX:F1}" y2="{stemEndY:F1}"/>""");
            
            // Draw flag (only if not beamed)
            if (!_beamedStemEndYs.ContainsKey(chord))
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
                if (flag.HasValue)
                {
                    DrawGlyph(flag.Value, stemX, stemEndY);
                }
            
            // Draw tremolo (if present)
            if (chord.HasTremolo)
            {
                DrawTremolo(stemX, stemAttachY, stemEndY, stemUp, chord.TremoloBeams);
            }
            }
        }
    }
    
    private void DrawBarline(BarlineType type, double x, double systemY)
    {
        if (type == BarlineType.None) return;
        
        double yTop = systemY;
        double yBottom = systemY + StaffHeight;
        double height = yBottom - yTop;
        
        double thinWidth = EngravingDefaults.ThinBarlineThickness;
        double thickWidth = EngravingDefaults.ThickBarlineThickness;
        double separation = EngravingDefaults.BarlineSeparation;
        double dotSep = EngravingDefaults.RepeatBarlineDotSeparation;
        
        switch (type)
        {
            case BarlineType.Single:
                DrawBarlineRect(x, yTop, thinWidth, height);
                break;
                
            case BarlineType.Double:
                DrawBarlineRect(x, yTop, thinWidth, height);
                DrawBarlineRect(x + thinWidth + separation, yTop, thinWidth, height);
                break;
                
            case BarlineType.Final:
                DrawBarlineRect(x, yTop, thinWidth, height);
                DrawBarlineRect(x + thinWidth + separation, yTop, thickWidth, height);
                break;
                
            case BarlineType.RepeatStart:
                DrawBarlineRect(x, yTop, thickWidth, height);
                DrawBarlineRect(x + thickWidth + separation, yTop, thinWidth, height);
                DrawRepeatDots(x + thickWidth + separation + thinWidth + dotSep, systemY);
                break;
                
            case BarlineType.RepeatEnd:
                DrawRepeatDots(x, systemY);
                double afterDots = x + EngravingDefaults.RepeatDotsOffset;
                DrawBarlineRect(afterDots, yTop, thinWidth, height);
                DrawBarlineRect(afterDots + thinWidth + separation, yTop, thickWidth, height);
                break;
                
            case BarlineType.RepeatBoth:
                DrawRepeatDots(x, systemY);
                double pos = x + EngravingDefaults.RepeatDotsOffset;
                DrawBarlineRect(pos, yTop, thinWidth, height);
                DrawBarlineRect(pos + thinWidth + separation, yTop, thickWidth, height);
                DrawBarlineRect(pos + thinWidth + separation + thickWidth + separation, yTop, thinWidth, height);
                DrawRepeatDots(pos + thinWidth + separation + thickWidth + separation + thinWidth + dotSep, systemY);
                break;
        }
    }
    
    private void DrawBarlineRect(double x, double y, double width, double height)
    {
        _svg.AppendLine($"""  <rect x="{x:F2}" y="{y:F2}" width="{width:F2}" height="{height:F2}" fill="black"/>""");
    }
    
    private void DrawRepeatDots(double x, double systemY)
    {
        double r = EngravingDefaults.RepeatDotRadius;
        double dot1Y = systemY + EngravingDefaults.RepeatDotPosition1;
        double dot2Y = systemY + EngravingDefaults.RepeatDotPosition2;
        _svg.AppendLine($"""  <circle cx="{x + r:F2}" cy="{dot1Y:F2}" r="{r:F2}" fill="black"/>""");
        _svg.AppendLine($"""  <circle cx="{x + r:F2}" cy="{dot2Y:F2}" r="{r:F2}" fill="black"/>""");
    }
    
    private void DrawSectionLabel(string label, double x, double systemY)
    {
        double labelY = systemY - 1.5;  // 1.5 staff spaces above
        double padding = 0.4;  // staff spaces
        double boxWidth = label.Length * 0.6 + padding * 2;  // rough estimate in staff spaces
        double boxHeight = 2;  // staff spaces
        
        _svg.AppendLine($"""  <rect x="{x - padding}" y="{labelY - boxHeight + 0.5}" width="{boxWidth}" height="{boxHeight}" fill="none" stroke="black" stroke-width="0.1"/>""");
        _svg.AppendLine($"""  <text class="section-label" font-size="1.5" x="{x}" y="{labelY}">{EscapeXml(label)}</text>""");
    }
    
    private double DrawKeySignature(KeySignature keySig, string clef, double x, double systemY)
    {
        if (keySig.Count == 0) return x;
        
        // LilyPond key signature position calculation
        // Based on output-lib.scm: key-signature-interface::alteration-position
        //
        // c0-position: position of middle C for each clef
        // - treble: -6 (C4 is one ledger line below staff)
        // - bass: 6 (C4 is one ledger line above staff)
        // - alto: 0 (C4 is on middle line)
        // - tenor: 2 (C4 is on fourth line)
        int c0Position = clef switch
        {
            "bass" => 6,
            "alto" => 0,
            "tenor" => 2,
            _ => -6  // treble
        };
        
        // LilyPond sharp-positions and flat-positions from define-grobs.scm
        // These are indexed by (modulo c0-position 7)
        int[] sharpPositions = [4, 5, 4, 2, 3, 2, 3];
        int[] flatPositions = [2, 3, 4, 2, 1, 2, 1];
        
        // Order of accidentals in key signature:
        // Sharps: F#, C#, G#, D#, A#, E#, B# → steps: 3, 0, 4, 1, 5, 2, 6
        // Flats:  Bb, Eb, Ab, Db, Gb, Cb, Fb → steps: 6, 2, 5, 1, 4, 0, 3
        int[] sharpSteps = [3, 0, 4, 1, 5, 2, 6];  // F, C, G, D, A, E, B
        int[] flatSteps = [6, 2, 5, 1, 4, 0, 3];   // B, E, A, D, G, C, F
        
        char glyph = keySig.IsSharps ? EmmentalerGlyphs.AccidentalSharp : EmmentalerGlyphs.AccidentalFlat;
        int[] positions = keySig.IsSharps ? sharpPositions : flatPositions;
        int[] steps = keySig.IsSharps ? sharpSteps : flatSteps;
        
        // c-pos: normalized position of C within octave (0-6)
        int cPos = ((c0Position % 7) + 7) % 7;  // ensure positive modulo
        int hi = positions[cPos];  // highest position for this clef
        
        for (int i = 0; i < keySig.Count; i++)
        {
            int step = steps[i];
            // LilyPond formula: hi - modulo(hi - (c-pos + step), 7)
            int diff = hi - (cPos + step);
            int modDiff = ((diff % 7) + 7) % 7;  // ensure positive modulo
            int staffPosition = hi - modDiff;
            
            // Convert staff position to Y coordinate
            // position 0 = middle line (systemY + 2)
            // Each position unit = 0.5 staff spaces
            double accY = systemY + StaffHeight / 2 - (staffPosition * 0.5);
            DrawGlyph(glyph, x, accY);
            x += GlyphMetrics.KeySignatureAccidentalWidth;
        }
        
        // Return the right edge of the key signature (spacing is handled by caller)
        return x;
    }
    
    private void DrawTimeSignature(TimeSignature timeSig, double x, double y)
    {
        char topGlyph = GetTimeNumberGlyph(timeSig.Beats);
        char bottomGlyph = GetTimeNumberGlyph(timeSig.BeatType);
        
        // Emmentaler time sig glyphs have baseline at bottom
        // Top number spans lines 1-3, so baseline at line 3
        // Bottom number spans lines 3-5, so baseline at line 5
        DrawGlyph(topGlyph, x, y + 2);
        DrawGlyph(bottomGlyph, x, y + 4);
    }
    
    private void DrawTempoMarking(int tempo, double x, double systemY)
    {
        double tempoY = systemY - 2.5;  // 2.5 staff spaces above staff
        string tempoText = $"♩ = {tempo}";
        _svg.AppendLine($"""  <text class="tempo" font-size="1.2" x="{x}" y="{tempoY}">{tempoText}</text>""");
    }
    
    private void DrawLedgerLines(int staffPosition, double x, double noteheadWidth, double systemY)
    {
        double extension = LegerLineExtension;
        double ledgerX1 = x - extension;
        double ledgerX2 = x + noteheadWidth + extension;
        
        // Lines above staff
        if (staffPosition >= 6)
        {
            for (int pos = 6; pos <= staffPosition; pos += 2)
            {
                double ledgerY = systemY + StaffHeight / 2 - (pos / 2.0);
                _svg.AppendLine($"""  <line class="ledger" x1="{ledgerX1:F1}" y1="{ledgerY:F1}" x2="{ledgerX2:F1}" y2="{ledgerY:F1}"/>""");
            }
        }
        
        // Lines below staff
        if (staffPosition <= -6)
        {
            for (int pos = -6; pos >= staffPosition; pos -= 2)
            {
                double ledgerY = systemY + StaffHeight / 2 - (pos / 2.0);
                _svg.AppendLine($"""  <line class="ledger" x1="{ledgerX1:F1}" y1="{ledgerY:F1}" x2="{ledgerX2:F1}" y2="{ledgerY:F1}"/>""");
            }
        }
    }
    
    private void DrawGlyph(char glyph, double x, double y, int? sourcePosition = null)
    {
        string dataAttr = sourcePosition.HasValue ? $" data-pos=\"{sourcePosition}\"" : "";
        _svg.AppendLine($"  <text class=\"music\" x=\"{x:F1}\" y=\"{y:F1}\" font-size=\"{FontSize}\"{dataAttr}>{glyph}</text>");
    }
    
    private static int GetNoteValue(Semantics.Fraction duration)
    {
        // Convert fraction to note value (1=whole, 2=half, 4=quarter, etc.)
        return (int)duration.Denominator;
    }
    
    private static char GetTimeNumberGlyph(int number) => EmmentalerGlyphs.GetTimeSigDigit(number);
    
    
    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
    
    // Beam constants from EngravingDefaults (in staff spaces)
    private static double BeamThickness => EngravingDefaults.BeamThickness;
    private static double BeamTranslation => EngravingDefaults.BeamTranslation;
    private static double BeamletLength => EngravingDefaults.BeamletLength;
    
    private void DrawBeams(ScoreLayout layout)
    {
        if (layout.BeamLayouts.Length == 0)
            return;
        
        // Build measure to system mapping
        var measureToSystem = new Dictionary<int, SystemLayout>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystem[measure.MeasureIndex] = system;
            }
        }
        
        foreach (var beamLayout in layout.BeamLayouts)
        {
            if (!measureToSystem.TryGetValue(beamLayout.Group.MeasureIndex, out var system))
                continue;
            
            DrawBeamGroup(beamLayout, system);
        }
    }
    
    private void DrawBeamGroup(BeamLayout beamLayout, SystemLayout system)
    {
        var group = beamLayout.Group;
        double staffMiddleY = system.Y + StaffHeight / 2;
        
        // Calculate stem X positions for each member using the same logic as DrawNote
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(3);
        double noteheadCenterX = noteheadBBox.CenterX;
        var stemAnchor = group.StemUp ? GlyphMetrics.StemUpSE : GlyphMetrics.StemDownNW;
        
        // stemX = itemX - noteheadCenterX + stemAnchor.X (same as DrawNote)
        double stemOffsetFromRef = -noteheadCenterX + stemAnchor.X;
        
        // DEBUG output
        var memberStemXPositions = new double[group.Members.Length];
        for (int i = 0; i < group.Members.Length; i++)
        {
            memberStemXPositions[i] = beamLayout.MemberXPositions[i] + stemOffsetFromRef;
        }
        
        double leftStemX = memberStemXPositions[0];
        double rightStemX = memberStemXPositions[^1];
        double leftBeamCenterY = staffMiddleY - beamLayout.LeftY / 2;
        double rightBeamCenterY = staffMiddleY - beamLayout.RightY / 2;
        
        int maxBeamCount = 0;
        foreach (var member in group.Members)
        {
            maxBeamCount = Math.Max(maxBeamCount, member.BeamCount);
        }
        
        double beamThickness = BeamThickness;
        double beamTranslation = BeamTranslation;
        
        for (int level = 0; level < maxBeamCount; level++)
        {
            double levelOffset = level * beamTranslation;
            if (!group.StemUp)
                levelOffset = -levelOffset;
            
            double levelLeftY = leftBeamCenterY + levelOffset;
            double levelRightY = rightBeamCenterY + levelOffset;
            
            DrawBeamLevel(beamLayout, level, leftStemX, levelLeftY, rightStemX, levelRightY, beamThickness, memberStemXPositions);
        }
    }
    
    /// <summary>
    /// Calculates stem end Y positions for beamed notes in multi-staff scores.
    /// </summary>
    private void CalculateMultiStaffBeamStemPositions(MultiStaffScore score, ScoreLayout layout)
    {
        if (layout.BeamLayouts.Length == 0)
            return;
        
        // Build measure to system mapping
        var measureToSystem = new Dictionary<int, SystemLayout>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystem[measure.MeasureIndex] = system;
            }
        }
        
        // Build staff Y positions from staff groups
        var staffYPositions = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            if (system.StaffGroups.IsDefaultOrEmpty)
                continue;
            
            foreach (var staffGroup in system.StaffGroups)
            {
                foreach (var staffLayout in staffGroup.Staves)
                {
                    staffYPositions[staffLayout.StaffIndex] = system.Y + staffLayout.Y;
                }
            }
        }
        
        // Get staff index for each beam based on measure index mapping
        // For now, use a simple heuristic: beam index / staves count
        int staffCount = score.EnumerateStaves().Count();
        int beamIndex = 0;
        
        foreach (var beamLayout in layout.BeamLayouts)
        {
            if (!measureToSystem.TryGetValue(beamLayout.Group.MeasureIndex, out var system))
                continue;
            
            // Determine which staff this beam belongs to
            int staffIndex = beamIndex % staffCount;
            double staffY = staffYPositions.TryGetValue(staffIndex, out var y) ? y : system.Y;
            double staffMiddleY = staffY + StaffHeight / 2;
            
            var group = beamLayout.Group;
            
            var noteheadBBox = GlyphMetrics.GetNoteheadBBox(3);
            double noteheadCenterX = noteheadBBox.CenterX;
            double stemOffsetX = group.StemUp 
                ? StemUpAttachX - noteheadCenterX 
                : StemDownAttachX - noteheadCenterX;
            
            double leftStemX = beamLayout.LeftX + stemOffsetX;
            double rightStemX = beamLayout.RightX + stemOffsetX;
            double leftBeamCenterY = staffMiddleY - beamLayout.LeftY / 2;
            double rightBeamCenterY = staffMiddleY - beamLayout.RightY / 2;
            
            double beamSpanX = rightStemX - leftStemX;
            double slope = beamSpanX > 0.001 ? (rightBeamCenterY - leftBeamCenterY) / beamSpanX : 0;
            double beamThickness = BeamThickness;
            
            for (int i = 0; i < group.Members.Length; i++)
            {
                var member = group.Members[i];
                double noteCenterX = beamLayout.MemberXPositions[i];
                double stemX = noteCenterX + stemOffsetX;
                double primaryBeamCenterY = leftBeamCenterY + slope * (stemX - leftStemX);
                
                double stemEndY = group.StemUp
                    ? primaryBeamCenterY - beamThickness / 2
                    : primaryBeamCenterY + beamThickness / 2;
                
                _beamedStemEndYs[member.Item] = stemEndY;
                _beamedStemUp[member.Item] = group.StemUp;
            }        }
    }
    
    /// <summary>
    /// Draws beams for multi-staff scores.
    /// </summary>
    private void DrawMultiStaffBeams(MultiStaffScore score, ScoreLayout layout)
    {
        if (layout.BeamLayouts.Length == 0)
            return;
        
        // Build measure to system mapping
        var measureToSystem = new Dictionary<int, SystemLayout>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystem[measure.MeasureIndex] = system;
            }
        }
        
        // Build staff Y positions from staff groups
        var staffYPositions = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            if (system.StaffGroups.IsDefaultOrEmpty)
                continue;
            
            foreach (var staffGroup in system.StaffGroups)
            {
                foreach (var staffLayout in staffGroup.Staves)
                {
                    staffYPositions[staffLayout.StaffIndex] = system.Y + staffLayout.Y;
                }
            }
        }
        
        
        foreach (var beamLayout in layout.BeamLayouts)
        {
            if (!measureToSystem.TryGetValue(beamLayout.Group.MeasureIndex, out var system))
                continue;
            
            // Use staff index from BeamLayout
            double staffY = staffYPositions.TryGetValue(beamLayout.StaffIndex, out var y) ? y : system.Y;
            DrawBeamGroupAtStaffY(beamLayout, staffY);
        }
    }
    
    private void DrawBeamGroupAtStaffY(BeamLayout beamLayout, double staffY)
    {
        var group = beamLayout.Group;
        double staffMiddleY = staffY + StaffHeight / 2;
        
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(3);
        double noteheadCenterX = noteheadBBox.CenterX;
        var stemAnchor = group.StemUp ? GlyphMetrics.StemUpSE : GlyphMetrics.StemDownNW;
        
        double stemOffsetFromRef = -noteheadCenterX + stemAnchor.X;
        var memberStemXPositions = new double[group.Members.Length];
        for (int i = 0; i < group.Members.Length; i++)
        {
            memberStemXPositions[i] = beamLayout.MemberXPositions[i] + stemOffsetFromRef;
        }
        
        double leftStemX = memberStemXPositions[0];
        double rightStemX = memberStemXPositions[^1];
        double leftBeamCenterY = staffMiddleY - beamLayout.LeftY / 2;
        double rightBeamCenterY = staffMiddleY - beamLayout.RightY / 2;
        
        int maxBeamCount = 0;
        foreach (var member in group.Members)
        {
            maxBeamCount = Math.Max(maxBeamCount, member.BeamCount);
        }
        
        double beamThickness = BeamThickness;
        double beamTranslation = BeamTranslation;
        
        for (int level = 0; level < maxBeamCount; level++)
        {
            double levelOffset = level * beamTranslation;
            if (!group.StemUp)
                levelOffset = -levelOffset;
            
            double levelLeftY = leftBeamCenterY + levelOffset;
            double levelRightY = rightBeamCenterY + levelOffset;
            
            DrawBeamLevel(beamLayout, level, leftStemX, levelLeftY, rightStemX, levelRightY, beamThickness, memberStemXPositions);
        }
    }
    
    private void DrawBeamLevel(BeamLayout beamLayout, int level, double leftX, double leftY, double rightX, double rightY, double thickness, double[] memberStemXPositions)
    {
        var members = beamLayout.Group.Members;
        
        int i = 0;
        while (i < members.Length)
        {
            while (i < members.Length && members[i].BeamCount <= level)
                i++;
            
            if (i >= members.Length)
                break;
            
            int segmentStart = i;
            
            while (i < members.Length && members[i].BeamCount > level)
                i++;
            
            int segmentEnd = i - 1;
            
            if (segmentStart <= segmentEnd)
            {
                DrawBeamSegment(segmentStart, segmentEnd, leftX, leftY, rightX, rightY, thickness, memberStemXPositions);
            }
        }
    }
    
    private void DrawBeamSegment(int startIdx, int endIdx, double leftX, double leftY, double rightX, double rightY, double thickness, double[] memberStemXPositions)
    {
        double segLeftX, segRightX, segLeftY, segRightY;
        
        if (startIdx == endIdx)
        {
            // Single-note beamlet
            double memberStemX = memberStemXPositions[startIdx];
            
            bool extendLeft = startIdx > 0;
            double beamletLength = BeamletLength;
            
            if (extendLeft)
            {
                segLeftX = memberStemX - beamletLength;
                segRightX = memberStemX;
            }
            else
            {
                segLeftX = memberStemX;
                segRightX = memberStemX + beamletLength;
            }
            
            // Clip beamlet to stay within primary beam bounds
            segLeftX = Math.Max(segLeftX, leftX);
            segRightX = Math.Min(segRightX, rightX);
        }
        else
        {
            // Multi-note beam segment - use actual stem positions
            segLeftX = memberStemXPositions[startIdx];
            segRightX = memberStemXPositions[endIdx];
        }
        
        // Interpolate Y positions
        double slope = (rightX - leftX) > 0.001 ? (rightY - leftY) / (rightX - leftX) : 0;
        segLeftY = leftY + slope * (segLeftX - leftX);
        segRightY = leftY + slope * (segRightX - leftX);
        
        // Draw beam as polygon
        double halfThickness = thickness / 2;
        double x1 = segLeftX, y1 = segLeftY - halfThickness;
        double x2 = segRightX, y2 = segRightY - halfThickness;
        double x3 = segRightX, y3 = segRightY + halfThickness;
        double x4 = segLeftX, y4 = segLeftY + halfThickness;
        
        _svg.AppendLine($"  <polygon points=\"{x1:F1},{y1:F1} {x2:F1},{y2:F1} {x3:F1},{y3:F1} {x4:F1},{y4:F1}\" fill=\"black\"/>");
    }
    private void DrawTies(ScoreLayout layout)
    {
        if (layout.TieLayouts.Length == 0)
            return;
        
        foreach (var tieLayout in layout.TieLayouts)
        {
            DrawTie(tieLayout);
        }
    }
    
    private void DrawTie(TieLayout tieLayout)
    {
        // Draw tie as a cubic Bezier curve
        // SVG path: M startX,startY C control1X,control1Y control2X,control2Y endX,endY
        
        double startX = tieLayout.StartX;
        double startY = tieLayout.StartY;
        double endX = tieLayout.EndX;
        double endY = tieLayout.EndY;
        double c1x = tieLayout.Control1.X;
        double c1y = tieLayout.Control1.Y;
        double c2x = tieLayout.Control2.X;
        double c2y = tieLayout.Control2.Y;
        
        // Draw the tie as a filled shape (two Bezier curves)
        // Outer curve and inner curve to create thickness
        double thickness = EngravingDefaults.TieRenderThickness; // Tie thickness
        
        // Offset for inner curve (direction depends on curve direction)
        double offsetY = tieLayout.CurveUp ? thickness : -thickness;
        
        // Outer curve
        string outerPath = $"M {startX:F1},{startY:F1} C {c1x:F1},{c1y:F1} {c2x:F1},{c2y:F1} {endX:F1},{endY:F1}";
        
        // Inner curve (reversed, with offset)
        double innerC1y = c1y + offsetY;
        double innerC2y = c2y + offsetY;
        string innerPath = $"C {c2x:F1},{innerC2y:F1} {c1x:F1},{innerC1y:F1} {startX:F1},{startY:F1}";
        
        // Combined path (closed shape)
        string fullPath = $"{outerPath} {innerPath} Z";
        
        _svg.AppendLine($"  <path d=\"{fullPath}\" fill=\"black\"/>");
    }
    
    private void DrawSlurs(ScoreLayout layout)
    {
        if (layout.SlurLayouts.Length == 0)
            return;
        
        foreach (var slurLayout in layout.SlurLayouts)
        {
            DrawSlur(slurLayout);
        }
    }
    
    private void DrawSlur(SlurLayout slurLayout)
    {
        // Draw slur as a cubic Bezier curve
        // SVG path: M startX,startY C control1X,control1Y control2X,control2Y endX,endY
        
        double startX = slurLayout.StartX;
        double startY = slurLayout.StartY;
        double endX = slurLayout.EndX;
        double endY = slurLayout.EndY;
        double c1x = slurLayout.Control1.X;
        double c1y = slurLayout.Control1.Y;
        double c2x = slurLayout.Control2.X;
        double c2y = slurLayout.Control2.Y;
        
        // Draw the slur as a filled shape (two Bezier curves)
        // Outer curve and inner curve to create thickness
        double thickness = EngravingDefaults.SlurRenderThickness; // Slur thickness (slightly thicker than tie)
        
        // Offset for inner curve (direction depends on curve direction)
        double offsetY = slurLayout.CurveUp ? thickness : -thickness;
        
        // Outer curve
        string outerPath = $"M {startX:F1},{startY:F1} C {c1x:F1},{c1y:F1} {c2x:F1},{c2y:F1} {endX:F1},{endY:F1}";
        
        // Inner curve (reversed, with offset)
        double innerC1y = c1y + offsetY;
        double innerC2y = c2y + offsetY;
        string innerPath = $"C {c2x:F1},{innerC2y:F1} {c1x:F1},{innerC1y:F1} {startX:F1},{startY:F1}";
        
        // Combined path (closed shape)
        string fullPath = $"{outerPath} {innerPath} Z";
        
        _svg.AppendLine($"  <path d=\"{fullPath}\" fill=\"black\"/>");
    }
    
    /// <summary>
    /// Draws all dynamic markings.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm:1298-1327 DynamicText grob
    /// LILYPOND-REF: output-lib.scm:348-365 dynamic glyph rendering
    /// Dynamics are rendered using SMuFL dynamic glyphs (U+E520-U+E52F).
    /// </remarks>
    private void DrawDynamics(ScoreLayout layout)
    {
        if (layout.DynamicLayouts.IsDefaultOrEmpty)
            return;
        
        foreach (var dynamicLayout in layout.DynamicLayouts)
        {
            DrawDynamic(dynamicLayout);
        }
    }
    
    /// <summary>
    /// Draws a single dynamic marking.
    /// </summary>
    private void DrawDynamic(DynamicLayout dynamicLayout)
    {
        // SMuFL dynamic glyphs
        // LILYPOND-REF: define-grobs.scm:1317 font-encoding = fetaText
        string glyph = GetDynamicGlyph(dynamicLayout.Text);
        
        double x = dynamicLayout.X;
        double y = dynamicLayout.Y;
        
        // Center the dynamic horizontally
        // LILYPOND-REF: define-grobs.scm:1311 self-alignment-X = CENTER
        double glyphWidth = EstimateDynamicWidth(dynamicLayout.Text);
        x -= glyphWidth / 2;
        
        // Dynamic font size is slightly smaller than regular music glyphs
        // LILYPOND-REF: define-grobs.scm:1317 Y-offset = (scale-by-font-size -0.6)
        double fontSize = FontSize * 0.85;
        
        _svg.AppendLine($"  <text x=\"{x:F2}\" y=\"{y:F2}\" font-family=\"Emmentaler\" font-size=\"{fontSize:F1}\" fill=\"black\" data-pos=\"{dynamicLayout.SourcePosition}\">{glyph}</text>");
    }
    
    /// <summary>
    /// Gets the SMuFL glyph string for a dynamic marking.
    /// </summary>
    private static string GetDynamicGlyph(string text) => text switch
    {
        // SMuFL dynamic glyphs (U+E520-U+E52F)
        "ppp" => "\uE52A",  // dynamicPPP
        "pp" => "\uE52B",   // dynamicPP
        "p" => "\uE520",    // dynamicPiano
        "mp" => "\uE52C",   // dynamicMP
        "mf" => "\uE52D",   // dynamicMF
        "f" => "\uE522",    // dynamicForte
        "ff" => "\uE52F",   // dynamicFF
        "fff" => "\uE530",  // dynamicFFF
        _ => text           // Fallback to text
    };
    
    /// <summary>
    /// Estimates the width of a dynamic marking for centering.
    /// </summary>
    private static double EstimateDynamicWidth(string text) => text switch
    {
        "ppp" => 2.5,
        "pp" => 1.8,
        "p" => 0.9,
        "mp" => 1.8,
        "mf" => 1.8,
        "f" => 0.9,
        "ff" => 1.8,
        "fff" => 2.5,
        _ => text.Length * 0.8
    };
    
    /// <summary>
    /// Draws all articulation marks.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm:2268-2310 Script grob
    /// LILYPOND-REF: output-lib.scm:305-320 script glyph rendering
    /// </remarks>
    private void DrawArticulations(ScoreLayout layout)
    {
        if (layout.ArticulationLayouts.IsDefaultOrEmpty)
            return;
        
        foreach (var articulationLayout in layout.ArticulationLayouts)
        {
            DrawArticulation(articulationLayout);
        }
    }
    
    /// <summary>
    /// Draws a single articulation mark.
    /// </summary>
    private void DrawArticulation(ArticulationLayout articulationLayout)
    {
        double x = articulationLayout.X;
        double y = articulationLayout.Y;
        string glyph = articulationLayout.Glyph;
        
        if (string.IsNullOrEmpty(glyph))
            return;
        
        // Center the articulation horizontally
        // LILYPOND-REF: define-grobs.scm:2289 self-alignment-X = CENTER
        double glyphWidth = 0.6; // Approximate width
        x -= glyphWidth / 2;
        
        // Articulation font size (slightly smaller than notes)
        double fontSize = FontSize * 0.9;
        
        _svg.AppendLine($"  <text x=\"{x:F2}\" y=\"{y:F2}\" font-family=\"Emmentaler\" font-size=\"{fontSize:F1}\" fill=\"black\" data-pos=\"{articulationLayout.SourcePosition}\">{glyph}</text>");
    }
    
    /// <summary>
    /// <summary>
    /// Draws tremolo beams on a stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: stem-tremolo.cc:129-150 Stem_tremolo::raw_stencil
    /// Tremolo beams are short, angled strokes across the stem.
    /// </remarks>
    private void DrawTremolo(double stemX, double stemAttachY, double stemEndY, bool stemUp, int beamCount)
    {
        if (beamCount <= 0)
            return;
        
        // Tremolo parameters (in staff spaces)
        // LILYPOND-REF: define-grobs.scm:2780-2790 beam-width, beam-gap, slope
        const double beamWidth = 1.2;
        const double beamThickness = 0.48;
        const double beamGap = 0.8;
        const double slope = 0.25;
        
        // Position tremolo at center of stem
        double stemMidY = (stemAttachY + stemEndY) / 2;
        
        // Adjust position based on number of beams
        double totalHeight = beamCount * beamThickness + (beamCount - 1) * beamGap;
        double startY = stemMidY - totalHeight / 2 + beamThickness / 2;
        
        for (int i = 0; i < beamCount; i++)
        {
            double y = startY + i * (beamThickness + beamGap);
            double halfWidth = beamWidth / 2;
            
            // Calculate sloped endpoints
            double dy = halfWidth * slope;
            double x1 = stemX - halfWidth;
            double x2 = stemX + halfWidth;
            double y1 = stemUp ? y + dy : y - dy;
            double y2 = stemUp ? y - dy : y + dy;
            
            // Draw as a thick line (tremolo beam)
            _svg.AppendLine($"""  <line class="tremolo" x1="{x1:F2}" y1="{y1:F2}" x2="{x2:F2}" y2="{y2:F2}" stroke="black" stroke-width="{beamThickness:F2}"/>""");
        }
    }
    
    /// Draws all grace notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: define-grobs.scm:1358-1402 GraceSpacing grob
    /// LILYPOND-REF: note-head.cc grace note rendering
    /// Grace notes are rendered at 65% of normal size.
    /// Acciaccaturas have a diagonal slash through the stem.
    /// </remarks>
    private void DrawGraceNotes(ScoreLayout layout)
    {
        if (layout.GraceNoteLayouts.IsDefaultOrEmpty)
            return;
        
        foreach (var graceLayout in layout.GraceNoteLayouts)
        {
            DrawGraceNoteGroup(graceLayout);
        }
    }
    
    /// <summary>
    /// Draws a group of grace notes.
    /// </summary>
    private void DrawGraceNoteGroup(GraceNoteLayout graceLayout)
    {
        double x = graceLayout.X;
        double scale = graceLayout.Scale;
        double scaledFontSize = FontSize * scale;
        double noteSpacing = 1.2 * scale;
        
        // Find the system Y offset for this measure
        // For now, use a default offset (this should be calculated from layout)
        double systemY = 0;
        
        foreach (var noteInfo in graceLayout.Notes)
        {
            // Calculate Y position from staff position
            double y = systemY + noteInfo.StaffPosition * 0.5;
            
            // Draw the notehead (quarter note head for grace notes)
            string noteGlyph = "\uE0A4"; // noteheadBlack
            _svg.AppendLine($"  <text x=\"{x:F2}\" y=\"{y:F2}\" font-family=\"Emmentaler\" font-size=\"{scaledFontSize:F1}\" fill=\"black\" data-pos=\"{graceLayout.SourcePosition}\">{noteGlyph}</text>");
            
            // Draw stem
            double stemX = x + 0.5 * scale;
            double stemStartY = y;
            double stemEndY = y - 3.5 * scale; // Stem goes up
            _svg.AppendLine($"  <line x1=\"{stemX:F2}\" y1=\"{stemStartY:F2}\" x2=\"{stemX:F2}\" y2=\"{stemEndY:F2}\" stroke=\"black\" stroke-width=\"{StemThickness:F2}\"/>");
            
            // Draw flag for grace notes
            string flagGlyph = "\uE240"; // flag8thUp
            _svg.AppendLine($"  <text x=\"{stemX:F2}\" y=\"{stemEndY:F2}\" font-family=\"Emmentaler\" font-size=\"{scaledFontSize:F1}\" fill=\"black\">{flagGlyph}</text>");
            
            // Draw slash for acciaccatura
            if (graceLayout.Type == GraceNoteType.Acciaccatura)
            {
                double slashStartX = stemX - 0.3 * scale;
                double slashStartY = stemStartY - 1.5 * scale;
                double slashEndX = stemX + 0.5 * scale;
                double slashEndY = stemStartY - 2.5 * scale;
                _svg.AppendLine($"  <line x1=\"{slashStartX:F2}\" y1=\"{slashStartY:F2}\" x2=\"{slashEndX:F2}\" y2=\"{slashEndY:F2}\" stroke=\"black\" stroke-width=\"{StaffLineThickness:F2}\"/>");
            }
            
            // Draw accidental if present
            if (noteInfo.Accidental != null)
            {
                string accidentalGlyph = noteInfo.Accidental switch
                {
                    "sharp" => "\uE262",
                    "flat" => "\uE260",
                    "natural" => "\uE261",
                    "doubleSharp" => "\uE263",
                    "doubleFlat" => "\uE264",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(accidentalGlyph))
                {
                    double accX = x - 0.8 * scale;
                    _svg.AppendLine($"  <text x=\"{accX:F2}\" y=\"{y:F2}\" font-family=\"Emmentaler\" font-size=\"{scaledFontSize:F1}\" fill=\"black\">{accidentalGlyph}</text>");
                }
            }
            
            x += noteSpacing;
        }
    }
}

























