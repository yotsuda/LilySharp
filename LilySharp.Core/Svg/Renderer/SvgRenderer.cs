using System.Text;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Renderer;

/// <summary>
/// Renders a Score with its ScoreLayout to SVG.
/// </summary>
public sealed class SvgRenderer
{
    // Layout constants
    private const double SpaceHeight = 10;
    private const double StaffHeight = 4 * SpaceHeight;
    private const double FontSize = 40;
    
    // Derived from SMuFL defaults
    private static double StaffLineThickness => SmuflDefaults.StaffLineThickness * SpaceHeight;
    private static double StemThickness => SmuflDefaults.StemThickness * SpaceHeight;
    private static double ThinBarlineThickness => SmuflDefaults.ThinBarlineThickness * SpaceHeight;
    private static double LegerLineExtension => SmuflDefaults.LegerLineExtension * SpaceHeight;
    private static double LegerLineThickness => SmuflDefaults.LegerLineThickness * SpaceHeight;
    
    // Stem attachment points
    private static double StemUpAttachX => SmuflDefaults.StemUpAttachX * SpaceHeight;
    private static double StemUpAttachY => SmuflDefaults.StemUpAttachY * SpaceHeight;
    private static double StemDownAttachX => SmuflDefaults.StemDownAttachX * SpaceHeight;
    private static double StemDownAttachY => SmuflDefaults.StemDownAttachY * SpaceHeight;
    private static double StemHeight => 3.5 * SpaceHeight;
    
    private readonly StringBuilder _svg = new();
    private readonly LayoutOptions _layoutOptions;
    
    public SvgRenderer(LayoutOptions? options = null)
    {
        _layoutOptions = options ?? LayoutOptions.Default;
    }
    
    /// <summary>
    /// Renders a score with its layout to SVG.
    /// </summary>
    public string Render(Score score, ScoreLayout layout)
    {
        _svg.Clear();
        
        WriteHeader(layout.Width, layout.Height);
        
        // Draw header (title/composer)
        if (score.Title != null || score.Composer != null)
            DrawHeader(score, layout);
        
        // Draw each system
        for (int sysIdx = 0; sysIdx < layout.Systems.Length; sysIdx++)
        {
            var system = layout.Systems[sysIdx];
            bool isFirstSystem = sysIdx == 0;
            
            DrawSystem(score, system, isFirstSystem);
        }
        
        // Draw beams
        DrawBeams(layout);
        
        // Draw ties
        DrawTies(layout);
        WriteFooter();
        
        return _svg.ToString();
    }
    
    private void WriteHeader(double width, double height)
    {
        _svg.AppendLine($"""<?xml version="1.0" encoding="UTF-8"?>""");
        _svg.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">""");
        _svg.AppendLine("<style>");
        _svg.AppendLine("  @font-face { font-family: 'Bravura'; src: url('https://cdn.jsdelivr.net/npm/bravura-font@1.0.0/fonts/bravura/Bravura.woff2') format('woff2'); }");
        _svg.AppendLine("  .music { font-family: 'Bravura', serif; }");
        _svg.AppendLine($"  .staff {{ stroke: black; stroke-width: {StaffLineThickness:F2}; }}");
        _svg.AppendLine($"  .stem {{ stroke: black; stroke-width: {StemThickness:F2}; }}");
        _svg.AppendLine($"  .barline {{ stroke: black; stroke-width: {ThinBarlineThickness:F2}; }}");
        _svg.AppendLine($"  .ledger {{ stroke: black; stroke-width: {LegerLineThickness:F2}; }}");
        _svg.AppendLine("  .title { font-family: serif; font-size: 24px; font-weight: bold; }");
        _svg.AppendLine("  .composer { font-family: serif; font-size: 16px; font-style: italic; }");
        _svg.AppendLine("  .tempo { font-family: serif; font-size: 14px; }");
        _svg.AppendLine("  .section-label { font-family: serif; font-size: 16px; font-weight: bold; }");
        _svg.AppendLine("</style>");
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
            _svg.AppendLine($"""  <text class="title" x="{centerX}" y="{y}" text-anchor="middle">{EscapeXml(score.Title)}</text>""");
            y += 25;
        }
        
        if (score.Composer != null)
        {
            _svg.AppendLine($"""  <text class="composer" x="{centerX}" y="{y}" text-anchor="middle">{EscapeXml(score.Composer)}</text>""");
        }
    }
    
    private void DrawSystem(Score score, SystemLayout system, bool isFirstSystem)
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
            double lineY = y + i * SpaceHeight;
            _svg.AppendLine($"""  <line class="staff" x1="{startX}" y1="{lineY}" x2="{endX}" y2="{lineY}"/>""");
        }
        
        // Draw clef
        double currentX = startX;
        char clefGlyph = score.Clef switch
        {
            "bass" => SmuflGlyphs.FClef,
            "alto" => SmuflGlyphs.CClef,
            "tenor" => SmuflGlyphs.CClef,
            _ => SmuflGlyphs.GClef
        };
        double clefY = score.Clef switch
        {
            "bass" => y + SpaceHeight,
            "alto" => y + 2 * SpaceHeight,
            "tenor" => y + SpaceHeight,
            _ => y + 3 * SpaceHeight
        };
        DrawGlyph(clefGlyph, currentX, clefY);
        currentX += 30;
        
        // Draw key signature
        if (score.KeySignature.Count > 0)
        {
            currentX = DrawKeySignature(score.KeySignature, score.Clef, currentX, y);
        }
        
        // Draw time signature (first system only)
        if (isFirstSystem)
        {
            DrawTimeSignature(score.TimeSignature, currentX, y);
            currentX += 25;
        }
        
        // Draw tempo marking (first system only)
        if (isFirstSystem && score.Tempo.HasValue)
        {
            DrawTempoMarking(score.Tempo.Value, startX, y);
        }
        
        // Draw measures
        foreach (var measureLayout in system.Measures)
        {
            var measure = score.Voice.Measures[measureLayout.MeasureIndex];
            DrawMeasure(measure, measureLayout, y);
        }
    }
    
    private void DrawMeasure(Measure measure, MeasureLayout layout, double systemY)
    {
        double x = layout.X;
        double staffBottom = systemY + StaffHeight;
        
        // Draw section label if present
        if (measure.SectionLabel != null)
        {
            DrawSectionLabel(measure.SectionLabel, x, systemY);
        }
        
        // Draw start barline
        if (measure.StartBarline != BarlineType.None)
        {
            DrawBarline(measure.StartBarline, x, systemY);
        }
        
        // Draw items
        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            var itemLayout = layout.Items[i];
            double itemX = x + itemLayout.X;
            
            switch (item)
            {
                case NoteItem note:
                    DrawNote(note, itemX, systemY);
                    break;
                case RestItem rest:
                    DrawRest(rest, itemX, systemY);
                    break;
                case ChordItem chord:
                    DrawChord(chord, itemX, systemY);
                    break;
            }
        }
        
        // Draw end barline at the right edge of the measure
        double endX = x + layout.Width;
        DrawBarline(measure.EndBarline, endX, systemY);
    }
    
    private void DrawNote(NoteItem note, double x, double systemY)
    {
        // x is the reference point (center of notehead in Spring-Rod model)
        double noteY = systemY + StaffHeight - (note.StaffPosition * SpaceHeight / 2);
        int noteValue = GetNoteValue(note.BaseDuration);
        
        // Get notehead metrics from GlyphMetrics
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double noteheadWidth = GlyphMetrics.ToPixels(noteheadBBox.Width);
        double noteheadCenterX = GlyphMetrics.ToPixels(noteheadBBox.CenterX);
        
        // Convert reference point to notehead left edge (SMuFL glyphs are drawn from left edge)
        double noteheadLeftX = x - noteheadCenterX;
        
        // Draw accidental (to the left of notehead)
        if (note.Accidental != null)
        {
            char accGlyph = note.Accidental switch
            {
                "doubleSharp" => SmuflGlyphs.AccidentalDoubleSharp,
                "sharp" => SmuflGlyphs.AccidentalSharp,
                "flat" => SmuflGlyphs.AccidentalFlat,
                "doubleFlat" => SmuflGlyphs.AccidentalDoubleFlat,
                _ => SmuflGlyphs.AccidentalNatural
            };
            
            // Get accidental metrics
            var accBBox = GlyphMetrics.GetAccidentalBBox(note.Accidental);
            double accWidth = GlyphMetrics.ToPixels(accBBox.Width);
            double accNoteGap = GlyphMetrics.ToPixels(GlyphMetrics.AccidentalNoteGap);
            
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
        char notehead = SmuflGlyphs.GetNotehead(noteValue);
        DrawGlyph(notehead, noteheadLeftX, noteY, note.SourcePosition);
        
        // Draw stem using GlyphMetrics anchor points
        if (noteValue >= 2)
        {
            var stemAnchor = note.StemUp ? GlyphMetrics.StemUpSE : GlyphMetrics.StemDownNW;
            double stemX = noteheadLeftX + GlyphMetrics.ToPixels(stemAnchor.X);
            double stemAttachY = noteY - GlyphMetrics.ToPixels(stemAnchor.Y);
            double stemEndY = note.StemUp ? stemAttachY - StemHeight : stemAttachY + StemHeight;
            
            _svg.AppendLine($"""  <line class="stem" x1="{stemX:F1}" y1="{stemAttachY:F1}" x2="{stemX:F1}" y2="{stemEndY:F1}"/>""");
            
            // Draw flag
            var flag = SmuflGlyphs.GetFlag(noteValue, note.StemUp);
            if (flag.HasValue)
            {
                DrawGlyph(flag.Value, stemX, stemEndY);
            }
        }
        
        // Draw dots (to the right of notehead)
        var dotBBox = GlyphMetrics.AugmentationDot;
        double dotWidth = GlyphMetrics.ToPixels(dotBBox.Width);
        double dotGap = GlyphMetrics.ToPixels(0.3);  // Gap between notehead and first dot
        for (int d = 0; d < note.Dots; d++)
        {
            double dotX = noteheadLeftX + noteheadWidth + dotGap + d * (dotWidth + dotGap);
            
            // Dots must avoid staff lines
            // If note is on a line (StaffPosition is even), shift dot up by half a space
            double dotYOffset = 0;
            if (note.StaffPosition % 2 == 0)
            {
                // On a staff line - shift dot up to sit in the space above
                dotYOffset = -SpaceHeight / 2;
            }
            
            double dotY = noteY + dotYOffset;
            DrawGlyph(SmuflGlyphs.AugmentationDot, dotX, dotY);
        }
    }
    
    private void DrawRest(RestItem rest, double x, double systemY)
    {
        int noteValue = GetNoteValue(rest.BaseDuration);
        double restY = systemY + 2 * SpaceHeight;
        
        if (noteValue == 1)
            restY = systemY + SpaceHeight;
        else if (noteValue == 2)
            restY = systemY + 2 * SpaceHeight;
        
        char restGlyph = SmuflGlyphs.GetRest(noteValue);
        DrawGlyph(restGlyph, x, restY, rest.SourcePosition);
    }
    
    private void DrawChord(ChordItem chord, double x, double systemY)
    {
        int noteValue = GetNoteValue(chord.BaseDuration);
        double noteheadWidth = (noteValue == 1 ? SmuflDefaults.NoteheadWholeWidth : SmuflDefaults.NoteheadBlackWidth) * SpaceHeight;
        char notehead = SmuflGlyphs.GetNotehead(noteValue);
        
        foreach (var note in chord.Notes)
        {
            double noteY = systemY + StaffHeight - (note.StaffPosition * SpaceHeight / 2);
            
            // Draw accidental
            if (note.Accidental != null)
            {
                char accGlyph = note.Accidental switch
                {
                    "doubleSharp" => SmuflGlyphs.AccidentalDoubleSharp,
                    "sharp" => SmuflGlyphs.AccidentalSharp,
                    "flat" => SmuflGlyphs.AccidentalFlat,
                    "doubleFlat" => SmuflGlyphs.AccidentalDoubleFlat,
                    _ => SmuflGlyphs.AccidentalNatural
                };
                DrawGlyph(accGlyph, x - 12, noteY);
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
            int stemNotePos = chord.StemUp
                ? chord.Notes.Min(n => n.StaffPosition)
                : chord.Notes.Max(n => n.StaffPosition);
            double stemNoteY = systemY + StaffHeight - (stemNotePos * SpaceHeight / 2);
            
            double stemX = chord.StemUp ? x + StemUpAttachX : x + StemDownAttachX;
            double stemAttachY = chord.StemUp ? stemNoteY - StemUpAttachY : stemNoteY - StemDownAttachY;
            double stemEndY = chord.StemUp ? stemAttachY - StemHeight : stemAttachY + StemHeight;
            
            _svg.AppendLine($"""  <line class="stem" x1="{stemX:F1}" y1="{stemAttachY:F1}" x2="{stemX:F1}" y2="{stemEndY:F1}"/>""");
            
            // Draw flag
            var flag = SmuflGlyphs.GetFlag(noteValue, chord.StemUp);
            if (flag.HasValue)
            {
                DrawGlyph(flag.Value, stemX, stemEndY);
            }
        }
    }
    
    private void DrawBarline(BarlineType type, double x, double systemY)
    {
        if (type == BarlineType.None) return;
        
        double y = systemY + StaffHeight;
        char glyph = type switch
        {
            BarlineType.RepeatStart => SmuflGlyphs.RepeatLeft,
            BarlineType.RepeatEnd => SmuflGlyphs.RepeatRight,
            BarlineType.RepeatBoth => SmuflGlyphs.RepeatRightLeft,
            BarlineType.Double => SmuflGlyphs.BarlineDouble,
            BarlineType.Final => SmuflGlyphs.BarlineFinal,
            _ => SmuflGlyphs.BarlineSingle
        };
        
        DrawGlyph(glyph, x, y);
    }
    
    private void DrawSectionLabel(string label, double x, double systemY)
    {
        double labelY = systemY - 15;
        double padding = 4;
        double boxWidth = label.Length * 10 + padding * 2;
        double boxHeight = 20;
        
        _svg.AppendLine($"""  <rect x="{x - padding}" y="{labelY - boxHeight + 5}" width="{boxWidth}" height="{boxHeight}" fill="none" stroke="black" stroke-width="1"/>""");
        _svg.AppendLine($"""  <text class="section-label" x="{x}" y="{labelY}">{EscapeXml(label)}</text>""");
    }
    
    private double DrawKeySignature(KeySignature keySig, string clef, double x, double systemY)
    {
        if (keySig.Count == 0) return x;
        
        int[] sharpPositions = clef switch
        {
            "bass" => [4, 1, 5, 2, 6, 3, 0],
            _ => [6, 3, 7, 4, 1, 5, 2]
        };
        
        int[] flatPositions = clef switch
        {
            "bass" => [0, 3, -1, 2, 5, 1, 4],
            _ => [2, 5, 1, 4, 0, 3, -1]
        };
        
        char glyph = keySig.IsSharps ? SmuflGlyphs.AccidentalSharp : SmuflGlyphs.AccidentalFlat;
        int[] positions = keySig.IsSharps ? sharpPositions : flatPositions;
        
        for (int i = 0; i < keySig.Count; i++)
        {
            double accY = systemY + StaffHeight - (positions[i] * SpaceHeight / 2);
            DrawGlyph(glyph, x, accY);
            x += 10;
        }
        
        return x + 5;
    }
    
    private void DrawTimeSignature(TimeSignature timeSig, double x, double y)
    {
        char topGlyph = GetTimeNumberGlyph(timeSig.Beats);
        char bottomGlyph = GetTimeNumberGlyph(timeSig.BeatType);
        
        DrawGlyph(topGlyph, x, y + SpaceHeight);
        DrawGlyph(bottomGlyph, x, y + 3 * SpaceHeight);
    }
    
    private void DrawTempoMarking(int tempo, double x, double systemY)
    {
        double tempoY = systemY - 25;
        string tempoText = $"♩ = {tempo}";
        _svg.AppendLine($"""  <text class="tempo" x="{x}" y="{tempoY}">{tempoText}</text>""");
    }
    
    private void DrawLedgerLines(int staffPosition, double x, double noteheadWidth, double systemY)
    {
        double extension = LegerLineExtension;
        double ledgerX1 = x - extension;
        double ledgerX2 = x + noteheadWidth + extension;
        
        // Lines above staff
        if (staffPosition >= 10)
        {
            for (int pos = 10; pos <= staffPosition; pos += 2)
            {
                double ledgerY = systemY + StaffHeight - (pos * SpaceHeight / 2);
                _svg.AppendLine($"""  <line class="ledger" x1="{ledgerX1:F1}" y1="{ledgerY:F1}" x2="{ledgerX2:F1}" y2="{ledgerY:F1}"/>""");
            }
        }
        
        // Lines below staff
        if (staffPosition <= -2)
        {
            for (int pos = -2; pos >= staffPosition; pos -= 2)
            {
                double ledgerY = systemY + StaffHeight - (pos * SpaceHeight / 2);
                _svg.AppendLine($"""  <line class="ledger" x1="{ledgerX1:F1}" y1="{ledgerY:F1}" x2="{ledgerX2:F1}" y2="{ledgerY:F1}"/>""");
            }
        }
    }
    
    private void DrawGlyph(char glyph, double x, double y, int? sourcePosition = null)
    {
        string dataAttr = sourcePosition.HasValue ? $" data-source=\"{sourcePosition}\"" : "";
        _svg.AppendLine($"  <text class=\"music\" x=\"{x:F1}\" y=\"{y:F1}\" font-size=\"{FontSize}\"{dataAttr}>{glyph}</text>");
    }
    
    private static int GetNoteValue(Semantics.Fraction duration)
    {
        // Convert fraction to note value (1=whole, 2=half, 4=quarter, etc.)
        return (int)duration.Denominator;
    }
    
    private static char GetTimeNumberGlyph(int number) => SmuflGlyphs.GetTimeSigDigit(number);
    
    
    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
    
    // Beam constants (matching Lilypond defaults)
    private const double BeamThickness = 0.48; // staff spaces
    private const double BeamTranslation = 0.58; // distance between multiple beams in staff spaces
    
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
        
        // System Y is at the top staff line
        // Staff position 0 = middle line = 4th space from top = StaffHeight/2 from top
        double staffMiddleY = system.Y + StaffHeight / 2;
        
        // Convert staff space coordinates to pixels
        // In staff coordinates: positive Y goes down (lower pitches)
        double leftY = staffMiddleY - beamLayout.LeftY * SpaceHeight / 2;
        double rightY = staffMiddleY - beamLayout.RightY * SpaceHeight / 2;
        double leftX = beamLayout.LeftX;
        double rightX = beamLayout.RightX;
        
        // Draw beam segments for each beam level
        // Level 0 = main beam (8th notes), Level 1 = secondary beam (16th notes), etc.
        int maxBeamCount = 0;
        foreach (var member in group.Members)
        {
            maxBeamCount = Math.Max(maxBeamCount, member.BeamCount);
        }
        
        double beamThicknessPx = BeamThickness * SpaceHeight;
        double beamTranslationPx = BeamTranslation * SpaceHeight;
        
        for (int level = 0; level < maxBeamCount; level++)
        {
            // Offset for this beam level
            double levelOffset = level * beamTranslationPx;
            if (group.StemUp)
                levelOffset = -levelOffset; // Beams stack downward for stem-up
            // For stem-down, beams stack upward (positive offset)
            
            DrawBeamLevel(beamLayout, level, leftX, leftY + levelOffset, rightX, rightY + levelOffset, beamThicknessPx);
        }
    }
    
    private void DrawBeamLevel(BeamLayout beamLayout, int level, double leftX, double leftY, double rightX, double rightY, double thickness)
    {
        var group = beamLayout.Group;
        var members = group.Members;
        
        // Find continuous segments at this beam level
        int i = 0;
        while (i < members.Length)
        {
            // Find start of a segment
            while (i < members.Length && members[i].BeamCount <= level)
                i++;
            
            if (i >= members.Length)
                break;
            
            int segmentStart = i;
            
            // Find end of segment
            while (i < members.Length && members[i].BeamCount > level)
                i++;
            
            int segmentEnd = i - 1;
            
            // Draw this segment
            if (segmentStart <= segmentEnd)
            {
                DrawBeamSegment(beamLayout, segmentStart, segmentEnd, leftX, leftY, rightX, rightY, thickness);
            }
        }
    }
    
    private void DrawBeamSegment(BeamLayout beamLayout, int startIdx, int endIdx, double leftX, double leftY, double rightX, double rightY, double thickness)
    {
        var group = beamLayout.Group;
        var members = group.Members;
        
        // Calculate X positions for segment start and end
        double segLeftX, segRightX, segLeftY, segRightY;
        
        if (startIdx == endIdx)
        {
            // Single-note beamlet
            var member = members[startIdx];
            double memberX = beamLayout.LeftX + (beamLayout.RightX - beamLayout.LeftX) * startIdx / Math.Max(1, members.Length - 1);
            
            // Determine beamlet direction based on neighbors
            bool extendLeft = startIdx > 0 && members[startIdx - 1].BeamCount > members[startIdx].BeamCount;
            double beamletLength = SpaceHeight * 1.0; // 1 staff space
            
            if (extendLeft)
            {
                segLeftX = memberX - beamletLength;
                segRightX = memberX;
            }
            else
            {
                segLeftX = memberX;
                segRightX = memberX + beamletLength;
            }
        }
        else
        {
            // Multi-note beam segment
            double span = beamLayout.RightX - beamLayout.LeftX;
            double tStart = members.Length > 1 ? (double)startIdx / (members.Length - 1) : 0;
            double tEnd = members.Length > 1 ? (double)endIdx / (members.Length - 1) : 0;
            
            segLeftX = beamLayout.LeftX + span * tStart;
            segRightX = beamLayout.LeftX + span * tEnd;
        }
        
        // Interpolate Y positions
        double slope = (rightX - leftX) > 0.001 ? (rightY - leftY) / (rightX - leftX) : 0;
        segLeftY = leftY + slope * (segLeftX - leftX);
        segRightY = leftY + slope * (segRightX - leftX);
        
        // Draw beam as a polygon (quadrilateral)
        double halfThickness = thickness / 2;
        
        // Four corners of the beam
        double x1 = segLeftX, y1 = segLeftY - halfThickness;  // top-left
        double x2 = segRightX, y2 = segRightY - halfThickness; // top-right
        double x3 = segRightX, y3 = segRightY + halfThickness; // bottom-right
        double x4 = segLeftX, y4 = segLeftY + halfThickness;  // bottom-left
        
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
        double thickness = 0.12 * SpaceHeight; // Tie thickness
        
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
}