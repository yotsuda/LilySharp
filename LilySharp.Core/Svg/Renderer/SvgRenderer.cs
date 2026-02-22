using System.Text;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

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

            // Find the staff Y position for this beam
            double staffY = system.Y;  // Default to system top
            if (!system.StaffGroups.IsDefaultOrEmpty && beamLayout.StaffIndex >= 0)
            {
                // Find the staff layout for this beam
                foreach (var staffGroup in system.StaffGroups)
                {
                    foreach (var staff in staffGroup.Staves)
                    {
                        if (staff.StaffIndex == beamLayout.StaffIndex)
                        {
                            staffY = system.Y + staff.Y;
                            goto foundStaff;
                        }
                    }
                }
                foundStaff:;
            }
            double staffMiddleY = staffY + StaffHeight / 2;
            var group = beamLayout.Group;

            // Stem X offset from note center
            var noteheadBBox = GlyphMetrics.GetNoteheadBBox(3);
            double noteheadCenterX = noteheadBBox.CenterX;
            double stemUpOffsetX = StemUpAttachX - noteheadCenterX;
            double stemDownOffsetX = StemDownAttachX - noteheadCenterX;
            double stemOffsetX = group.StemUp ? stemUpOffsetX : stemDownOffsetX;

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

                // LILYPOND-REF: beam.cc:1425-1448 is_knee
                // For kneed beams, use per-member stem direction
                bool memberUp = group.IsKnee ? member.MemberStemUp : group.StemUp;

                // This note's stem X position
                double noteCenterX = beamLayout.MemberXPositions[i];
                double memberStemOffsetX = memberUp ? stemUpOffsetX : stemDownOffsetX;
                double stemX = noteCenterX + memberStemOffsetX;

                // Primary beam Y at this stem X (center of beam)
                double primaryBeamCenterY = leftBeamCenterY + slope * (stemX - leftStemX);

                // Stem extends to the far edge of the primary beam (away from notehead)
                double stemEndY;
                if (memberUp)
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
                _beamedStemUp[member.Item] = memberUp;
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
        DrawGraceNotes(score, layout);

        // Draw lyrics
        DrawLyrics(layout);

        // Draw music marks (segno, coda, D.S., etc.)
        DrawMusicMarks(layout);

        // Draw custom text
        DrawCustomTexts(layout);

        // Draw volta brackets
        DrawVoltaBrackets(layout);

        // Draw tuplet brackets
        // LILYPOND-REF: lily/tuplet-bracket.cc - tuplet bracket rendering
        DrawTupletBrackets(layout);

        // Draw hairpins
        // LILYPOND-REF: lily/hairpin.cc - hairpin wedge rendering
        DrawHairpins(layout);

        // Draw text spanners
        // LILYPOND-REF: lily/line-spanner.cc - text + dashed line rendering
        DrawTextSpanners(layout);

        // Draw ottava brackets
        // LILYPOND-REF: lily/ottava-bracket.cc - ottava bracket rendering
        DrawOttavaBrackets(layout);

        // Draw glissandos
        // LILYPOND-REF: lily/glissando-engraver.cc - glissando line rendering
        DrawGlissandos(layout);

        // Draw arpeggios
        // LILYPOND-REF: lily/arpeggio.cc - arpeggio wavy line rendering
        DrawArpeggios(layout);

        // Draw pedal brackets
        // LILYPOND-REF: lily/piano-pedal-engraver.cc - pedal bracket rendering
        DrawPedalBrackets(layout);

        // Draw figured bass
        // LILYPOND-REF: lily/figured-bass-engraver.cc - figured bass rendering
        DrawFiguredBass(layout);
        DrawChordNames(layout);
        DrawPercentRepeats(layout);
        DrawPartCombine(layout);

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

        // Draw hairpins
        DrawHairpins(layout);

        // Draw text spanners
        DrawTextSpanners(layout);

        // Draw ottava brackets
        DrawOttavaBrackets(layout);

        // Draw glissandos
        DrawGlissandos(layout);

        // Draw arpeggios
        DrawArpeggios(layout);

        // Draw pedal brackets
        // LILYPOND-REF: lily/piano-pedal-engraver.cc - pedal bracket rendering
        DrawPedalBrackets(layout);

        // Draw figured bass
        // LILYPOND-REF: lily/figured-bass-engraver.cc - figured bass rendering
        DrawFiguredBass(layout);
        DrawChordNames(layout);
        DrawPercentRepeats(layout);
        DrawPartCombine(layout);

        WriteFooter();

        return _svg.ToString();
    }

    private void DrawMultiStaffHeader(MultiStaffScore score, ScoreLayout layout)
    {
        double centerX = layout.Width / 2;
        double rightX = layout.Width - _layoutOptions.MarginLeft;
        double y = _layoutOptions.MarginTop;

        if (score.Title != null)
        {
            // LILYPOND-REF: ly/titling-init.ly:79-108 \huge \larger \larger \bold = font-size +4
            // font-size 4 → base(11pt) * 2^(4/6) ≈ 17.46pt ≈ 3.49 staff spaces
            _svg.AppendLine($"""  <text class="title" x="{centerX}" y="{y}" text-anchor="middle" font-size="3.5">{EscapeXml(score.Title)}</text>""");
            y += 3.5;
        }

        if (score.Composer != null)
        {
            // LILYPOND-REF: ly/titling-init.ly:100 composer has no size modifiers (font-size = 0)
            // font-size 0 → base(11pt) = 2.20 staff spaces
            _svg.AppendLine($"""  <text class="composer" x="{rightX}" y="{y}" text-anchor="end" font-size="2.2">{EscapeXml(score.Composer)}</text>""");
        }
    }

    private void DrawMultiStaffSystem(MultiStaffScore score, ScoreLayout scoreLayout, SystemLayout system, bool isFirstSystem)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return;

        double startX = _layoutOptions.MarginLeft;

        // Staff lines always extend to full system width (LilyPond behavior)
        double staffEndX = startX + system.Width;

        // Calculate the actual end of notated content (for barline positioning)
        double contentEndX;
        if (system.Measures.Length > 0)
        {
            var lastMeasure = system.Measures[^1];
            contentEndX = lastMeasure.X + lastMeasure.Width;
        }
        else
        {
            contentEndX = staffEndX;
        }

        // Draw each staff group (staff lines extend to full system width)
        foreach (var staffGroup in system.StaffGroups)
        {
            DrawStaffGroup(score, scoreLayout, system, staffGroup, startX, staffEndX, isFirstSystem);
        }

        // Draw system barlines at content end (not staff end)
        DrawSystemBarlines(system, scoreLayout, startX, contentEndX);
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
        // Route tab staves to dedicated method
        if (staffLayout.Clef == ClefType.Tab)
        {
            DrawTabStaff(score, scoreLayout, system, staffLayout, staffY, startX, endX);
            return;
        }

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
            _ => EmmentalerGlyphs.GClef  // Treble and Treble8Below both use GClef
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
        // Draw "8" below G-clef for treble_8 clef
        // Cross-ref: LilyPond ClefModifier grob in lilypond-src/scm/define-grobs.scm L836-867
        //   font-shape: italic, font-size: -4 (≈63% of text size)
        //   staff-padding: 0.7, clef-alignments G: (below: -0.2, above: 0.1)
        //   X-offset: self-alignment CENTER with parent-alignment from clef-modifier.cc
        //   Y-offset: side-position-interface::y-aligned-side
        // See also: lilypond-src/scm/translation-functions.scm L82-94 (clef-transposition-markup)
        if (staffLayout.Clef == ClefType.Treble8Below)
        {
            // X: clef center (GClefWidth/2) + LilyPond G-clef alignment (-0.2)
            double eightX = currentX + GlyphMetrics.GClefWidth / 2.0 - 0.2;
            // Y: staff bottom (staffY+4) + staff-padding (0.7) + vcenter offset (~0.5)
            double eightY = staffY + 5.2;
            _svg.AppendLine($"""  <text class="clef8" x="{eightX:F1}" y="{eightY:F1}" text-anchor="middle" font-size="1.4" font-style="italic">8</text>""");
        }
        double clefRightEdge = currentX + clefWidth;

        // Draw key signature
        string clefName = staffLayout.Clef switch
        {
            ClefType.Bass => "bass",
            ClefType.Alto => "alto",
            ClefType.Tenor => "tenor",
            ClefType.Treble8Below => "treble",  // treble_8 uses same key signature positions as treble
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

    // =====================================================
    // Tablature rendering
    // =====================================================

    private void DrawTabStaff(
        MultiStaffScore score,
        ScoreLayout scoreLayout,
        SystemLayout system,
        StaffLayout staffLayout,
        double staffY,
        double startX,
        double endX)
    {
        var tuning = staffLayout.Tuning ?? TuningType.Guitar;
        int stringCount = Tunings.GetStringCount(tuning);
        int[] tuningArray = Tunings.GetTuning(tuning);

        // Draw staff lines (one per string)
        for (int i = 0; i < stringCount; i++)
        {
            double lineY = staffY + i;
            _svg.AppendLine($"""  <line class="staff" x1="{startX}" y1="{lineY}" x2="{endX}" y2="{lineY}"/>""");
        }

        // Draw TAB clef using Emmentaler font glyph (clefs.tab = U+E08F)
        // The glyph is designed for 6-string staves (5ss height at font-size 4).
        // LilyPond always renders it at the same size, centered on the staff.
        // For fewer strings (e.g., 4-string bass), the glyph overflows above/below.
        double tabHeight = stringCount - 1;
        double tabCenterY = staffY + tabHeight / 2.0;
        double tabClefFontSize = FontSize * (5.0 / 5.78);
        DrawGlyph(EmmentalerGlyphs.TabClef, startX, tabCenterY, fontSize: tabClefFontSize);

        // Find the matching staff and voice in the score
        var matchingStaff = FindStaffForLayout(score, staffLayout.StaffIndex);
        if (matchingStaff == null)
            return;

        // Draw measures
        foreach (var measureLayout in system.Measures)
        {
            foreach (var voice in matchingStaff.Voices)
            {
                if (measureLayout.MeasureIndex < voice.Measures.Length)
                {
                    var measure = voice.Measures[measureLayout.MeasureIndex];
                    DrawTabMeasure(measure, measureLayout, staffY, tuningArray, stringCount);
                }
            }
        }
    }

    private void DrawTabMeasure(
        Measure measure,
        MeasureLayout layout,
        double staffY,
        int[] tuning,
        int stringCount)
    {
        double x = layout.X;
        var currentTiming = Fraction.Zero;

        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];

            // Calculate X position using timing-based columns
            double itemX;
            if (!layout.Columns.IsDefaultOrEmpty && layout.Columns.Length > 0)
            {
                itemX = x + layout.GetXForTiming(currentTiming);
            }
            else if (i < layout.Items.Length)
            {
                itemX = x + layout.Items[i].X;
            }
            else
            {
                itemX = x;
            }

            switch (item)
            {
                case NoteItem note:
                    DrawTabNote(note.StaffPosition, note.Accidental, itemX, staffY, tuning, stringCount);
                    break;
                case ChordItem chord:
                    foreach (var chordNote in chord.Notes)
                    {
                        DrawTabNote(chordNote.StaffPosition, chordNote.Accidental, itemX, staffY, tuning, stringCount);
                    }
                    break;
                    // RestItem: no rendering needed on tab staff
            }

            currentTiming += item.Duration;
        }
    }

    private void DrawTabNote(
        int staffPosition,
        string? accidental,
        double x,
        double staffY,
        int[] tuning,
        int stringCount)
    {
        int midiPitch = StaffPositionToMidi(staffPosition, accidental);
        var (stringNum, fret) = Tunings.CalculateFret(midiPitch, tuning);

        // stringNum: 1 = highest pitch string (top line in standard notation, but BOTTOM line in tab)
        // In tab notation: string 1 (highest) is at the top, string N (lowest) at the bottom
        // So string 1 → Y = staffY + 0, string N → Y = staffY + (N-1)
        // Wait - actually in standard tab notation, the highest string is at the TOP
        // String 1 (highest pitch, e.g., high E on guitar) = top line
        // String 6 (lowest pitch, e.g., low E on guitar) = bottom line
        double noteY = staffY + (stringNum - 1);

        // Draw fret number with a white background to occlude the staff line
        string fretText = fret.ToString();
        double fontSize = 1.6;
        double bgWidth = fretText.Length == 1 ? 1.0 : 1.6;
        double bgHeight = 1.1;

        // White background rectangle to hide staff line behind the number
        _svg.AppendLine($"""  <rect class="tab-bg" x="{x - bgWidth / 2:F2}" y="{noteY - bgHeight / 2:F2}" width="{bgWidth:F2}" height="{bgHeight:F2}" fill="white" stroke="none"/>""");
        // Fret number text
        _svg.AppendLine($"""  <text class="tab-fret" x="{x:F1}" y="{noteY + fontSize * 0.32:F1}" text-anchor="middle" font-size="{fontSize:F1}" font-family="serif">{fretText}</text>""");
    }

    /// <summary>
    /// Converts a staff position and accidental back to a MIDI note number.
    /// Staff position 0 = middle C (C4 = MIDI 60).
    /// </summary>
    private static int StaffPositionToMidi(int staffPosition, string? accidental)
    {
        // staffPosition = (Octave - 4) * 7 + Step
        // We need to recover Step and Octave
        int step = ((staffPosition % 7) + 7) % 7;  // Ensure positive modulo
        int octave = 4 + (staffPosition - step) / 7;

        int semitone = step switch
        {
            0 => 0,  // C
            1 => 2,  // D
            2 => 4,  // E
            3 => 5,  // F
            4 => 7,  // G
            5 => 9,  // A
            6 => 11, // B
            _ => 0
        };

        int alteration = accidental switch
        {
            "sharp" => 1,
            "flat" => -1,
            "doubleSharp" => 2,
            "doubleFlat" => -2,
            _ => 0
        };

        return (octave + 1) * 12 + semitone + alteration;
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
                double staffBottom = staffTop + staff.Height;
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
        _svg.AppendLine($"  .barline {{ stroke: black; stroke-width: {ThinBarlineThickness:F2}; }}");
        _svg.AppendLine($"  .ledger {{ stroke: black; stroke-width: {LegerLineThickness:F2}; }}");
        _svg.AppendLine("  .title { font-family: serif; font-weight: bold; }");
        _svg.AppendLine("  .composer { font-family: serif; font-style: italic; }");
        _svg.AppendLine("  .tempo { font-family: serif; }");
        _svg.AppendLine("  .section-label { font-family: serif; font-weight: bold; }");
        // clef8: treble_8 "8" text. Ref: LilyPond ClefModifier font-size:-4 font-shape:italic
        // See: lilypond-src/scm/define-grobs.scm L836-867
        _svg.AppendLine("  .clef8 { font-family: serif; font-size: 1.4; font-style: italic; }");
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
        double rightX = layout.Width - _layoutOptions.MarginLeft;
        double y = _layoutOptions.MarginTop;

        if (score.Title != null)
        {
            // LILYPOND-REF: ly/titling-init.ly:79-108 \huge \larger \larger \bold = font-size +4
            // font-size 4 → base(11pt) * 2^(4/6) ≈ 17.46pt ≈ 3.49 staff spaces
            _svg.AppendLine($"""  <text class="title" x="{centerX}" y="{y}" text-anchor="middle" font-size="3.5">{EscapeXml(score.Title)}</text>""");
            y += 3.5;
        }

        if (score.Composer != null)
        {
            // LILYPOND-REF: ly/titling-init.ly:100 composer has no size modifiers (font-size = 0)
            // font-size 0 → base(11pt) = 2.20 staff spaces
            _svg.AppendLine($"""  <text class="composer" x="{rightX}" y="{y}" text-anchor="end" font-size="2.2">{EscapeXml(score.Composer)}</text>""");
        }
    }

    private void DrawSystem(Score score, ScoreLayout scoreLayout, SystemLayout system, bool isFirstSystem)
    {
        double y = system.Y;
        double startX = _layoutOptions.MarginLeft;

        // Staff lines always extend to full system width (LilyPond behavior)
        double staffEndX = startX + system.Width;

        // Calculate the actual end of notated content (for barline positioning)
        double contentEndX;
        if (system.Measures.Length > 0)
        {
            var lastMeasureLayout = system.Measures[^1];
            contentEndX = lastMeasureLayout.X + lastMeasureLayout.Width;
        }
        else
        {
            contentEndX = staffEndX;
        }

        // Draw staff lines to full system width
        for (int i = 0; i < 5; i++)
        {
            double lineY = y + i;  // 1 staff space between lines
            _svg.AppendLine($"""  <line class="staff" x1="{startX}" y1="{lineY}" x2="{staffEndX}" y2="{lineY}"/>""");
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
        // Draw "8" below G-clef for treble_8 clef
        // Cross-ref: LilyPond ClefModifier grob in lilypond-src/scm/define-grobs.scm L836-867
        // See multi-staff version above for full parameter reference
        if (score.Clef == "treble_8")
        {
            double eightX = currentX + GlyphMetrics.GClefWidth / 2.0 - 0.2;
            double eightY = y + 5.2;
            _svg.AppendLine($"""  <text class="clef8" x="{eightX:F1}" y="{eightY:F1}" text-anchor="middle" font-size="1.4" font-style="italic">8</text>""");
        }
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
                    // Only force stem direction for multi-voice scores
                    bool? forcedStemUp = score.Voices.Length > 1
                        ? VoiceDefaults.GetDefaultStemUp(voiceNumber)
                        : null;
                    DrawMeasure(measure, measureLayout, measureLayout.MeasureIndex, voiceNumber, y, scoreLayout, forcedStemUp, isFirstVoice: voiceIdx == 0);
                }
            }
        }
    }

    private void DrawMeasure(Measure measure, MeasureLayout layout, int measureIndex, int voiceNumber, double systemY, ScoreLayout scoreLayout, bool? forcedStemUp = null, bool isFirstVoice = true, bool skipBarlines = false)
    {
        double x = layout.X;
        double staffBottom = systemY + StaffHeight;

        // Section labels are now routed through MusicMarkEngraver for proper stacking.

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
            // Draw barline so it ENDS at measure boundary (endX), not starts there
            double barlineWidth = GetVisualBarlineWidth(measure.EndBarline);
            DrawBarline(measure.EndBarline, endX - barlineWidth, systemY);
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
            DrawGlyph(accGlyph, accidentalX, noteY, note.SourcePosition);
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

            // Use beam-calculated stem end if part of a beam group, otherwise calculate based on position
            double stemEndY;
            if (_beamedStemEndYs.TryGetValue(note, out double beamStemEndY))
            {
                stemEndY = beamStemEndY;
            }
            else
            {
                int durLog = StemCalculator.GetDurationLog(noteValue);
                stemEndY = CalculateStemEndY(stemAttachY, stemUp, systemY, durLog, note.StaffPosition);
            }

            _svg.AppendLine($"""  <line x1="{stemX:F1}" y1="{stemAttachY:F1}" x2="{stemX:F1}" y2="{stemEndY:F1}" stroke="black" stroke-width="{StemThickness:F2}" data-pos="{note.SourcePosition}"/>""");

            // Draw flag (only if not beamed)
            if (!_beamedStemEndYs.ContainsKey(note))
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
                if (flag.HasValue)
                {
                    DrawGlyph(flag.Value, stemX, stemEndY, note.SourcePosition);
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
            DrawGlyph(EmmentalerGlyphs.AugmentationDot, dotX, dotY, note.SourcePosition);
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
                DrawGlyph(accGlyph, x + accLayout.XOffset, noteY, chord.SourcePosition);
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

            // Use beam-calculated stem end if part of a beam group, otherwise calculate based on position
            double stemEndY;
            if (_beamedStemEndYs.TryGetValue(chord, out double beamStemEndY))
            {
                stemEndY = beamStemEndY;
            }
            else
            {
                int durLog = StemCalculator.GetDurationLog(noteValue);
                stemEndY = CalculateStemEndY(stemAttachY, stemUp, systemY, durLog, stemNotePos);
            }

            _svg.AppendLine($"""  <line x1="{stemX:F1}" y1="{stemAttachY:F1}" x2="{stemX:F1}" y2="{stemEndY:F1}" stroke="black" stroke-width="{StemThickness:F2}" data-pos="{chord.SourcePosition}"/>""");

            // Draw flag (only if not beamed)
            if (!_beamedStemEndYs.ContainsKey(chord))
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, stemUp);
                if (flag.HasValue)
                {
                    DrawGlyph(flag.Value, stemX, stemEndY, chord.SourcePosition);
                }

            // Draw tremolo (if present)
            if (chord.HasTremolo)
            {
                DrawTremolo(stemX, stemAttachY, stemEndY, stemUp, chord.TremoloBeams);
            }
            }
        }
    }

    /// <summary>
    /// Gets the visual (drawn) width of a barline type, as opposed to layout allocation width.
    /// </summary>
    private static double GetVisualBarlineWidth(BarlineType type)
    {
        double thin = EngravingDefaults.ThinBarlineThickness;
        double thick = EngravingDefaults.ThickBarlineThickness;
        double sep = EngravingDefaults.BarlineSeparation;
        double dotsOffset = EngravingDefaults.RepeatDotsOffset;
        
        return type switch
        {
            BarlineType.None => 0,
            BarlineType.Single => thin,
            BarlineType.Double => thin + sep + thin,
            BarlineType.Final => thin + sep + thick,
            BarlineType.RepeatStart => thick + sep + thin + dotsOffset,
            BarlineType.RepeatEnd => dotsOffset + thin + sep + thick,
            BarlineType.RepeatBoth => dotsOffset + thin + sep + thick + sep + thin + dotsOffset,
            _ => thin
        };
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

    /// <summary>
    /// Measures text width using Times New Roman Bold advance widths (per 1000 em units).
    /// This is the standard fallback for CSS <c>font-family: serif; font-weight: bold</c>.
    /// </summary>
    private static double MeasureSerifBoldText(string text, double fontSize)
    {
        double totalAdvance = 0;
        foreach (char c in text)
            totalAdvance += GetSerifBoldAdvanceWidth(c);
        return totalAdvance / 1000.0 * fontSize;
    }

    private static int GetSerifBoldAdvanceWidth(char c) => c switch
    {
        // Times New Roman Bold — advance widths per 1000 em units
        // Uppercase
        'A' => 722, 'B' => 667, 'C' => 722, 'D' => 722, 'E' => 667,
        'F' => 611, 'G' => 778, 'H' => 778, 'I' => 389, 'J' => 500,
        'K' => 778, 'L' => 667, 'M' => 944, 'N' => 722, 'O' => 778,
        'P' => 611, 'Q' => 778, 'R' => 722, 'S' => 556, 'T' => 667,
        'U' => 722, 'V' => 722, 'W' => 1000, 'X' => 722, 'Y' => 722,
        'Z' => 667,
        // Lowercase
        'a' => 500, 'b' => 556, 'c' => 444, 'd' => 556, 'e' => 444,
        'f' => 333, 'g' => 500, 'h' => 556, 'i' => 278, 'j' => 333,
        'k' => 556, 'l' => 278, 'm' => 833, 'n' => 556, 'o' => 500,
        'p' => 556, 'q' => 556, 'r' => 444, 's' => 389, 't' => 333,
        'u' => 556, 'v' => 500, 'w' => 722, 'x' => 500, 'y' => 500,
        'z' => 444,
        // Digits
        '0' => 500, '1' => 500, '2' => 500, '3' => 500, '4' => 500,
        '5' => 500, '6' => 500, '7' => 500, '8' => 500, '9' => 500,
        // Common punctuation
        ' ' => 250, '.' => 250, ',' => 250, ':' => 333, ';' => 333,
        '-' => 333, '\'' => 278, '"' => 500, '(' => 333, ')' => 333,
        '!' => 333, '?' => 500,
        _ => 500, // fallback: median width
    };

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
        // LILYPOND-REF: metronome-engraver.cc / define-grobs.scm MetronomeMark
        // Compose: Emmentaler notehead + stem line + " = NNN" in serif
        double tempoY = systemY - 2.5;  // 2.5 staff spaces above staff
        double noteSize = 1.6;  // Emmentaler font size for metronome notehead
        double textSize = 1.8;  // Serif font size for " = NNN"

        // Draw notehead (filled black noteheads.s2)
        char notehead = EmmentalerGlyphs.NoteheadBlack;
        _svg.AppendLine($"  <text x=\"{x:F2}\" y=\"{tempoY:F2}\" font-family=\"Emmentaler\" font-size=\"{noteSize:F1}\">{notehead}</text>");

        // Draw stem (upward from notehead right edge)
        // LILYPOND-REF: stem.cc default stem-length = 3.5 staff spaces at FontSize 4.0
        // LILYPOND-REF: scm/translation-functions.scm make-smaller-markup scales by magstep(-1) ≈ 0.891
        // Proportional stem: DefaultStemLength * (noteSize / FontSize) = 3.5 * (1.6 / 4.0) = 1.4
        double stemX = x + noteSize * 0.32;  // right side of notehead
        double stemLength = 3.5 * (noteSize / FontSize);  // proportional to LilyPond default
        double stemTop = tempoY - stemLength;
        _svg.AppendLine($"  <line x1=\"{stemX:F2}\" y1=\"{tempoY:F2}\" x2=\"{stemX:F2}\" y2=\"{stemTop:F2}\" stroke=\"black\" stroke-width=\"0.10\"/>");

        // Draw text " = NNN" to the right
        double textX = x + noteSize * 0.5 + 0.3;
        _svg.AppendLine($"""  <text class="tempo" font-size="{textSize:F1}" x="{textX:F2}" y="{tempoY:F2}">= {tempo}</text>""");
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

    private void DrawGlyph(char glyph, double x, double y, int? sourcePosition = null, double? fontSize = null)
    {
        double fs = fontSize ?? FontSize;
        string dataAttr = sourcePosition.HasValue ? $" data-pos=\"{sourcePosition}\"" : "";
        _svg.AppendLine($"  <text class=\"music\" x=\"{x:F1}\" y=\"{y:F1}\" font-size=\"{fs:F2}\"{dataAttr}>{glyph}</text>");
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

        // Find the correct staff Y position for this beam
        double staffY = system.Y;  // Default to system top
        if (!system.StaffGroups.IsDefaultOrEmpty && beamLayout.StaffIndex >= 0)
        {
            // Find the staff layout for this beam
            foreach (var staffGroup in system.StaffGroups)
            {
                foreach (var staff in staffGroup.Staves)
                {
                    if (staff.StaffIndex == beamLayout.StaffIndex)
                    {
                        staffY = system.Y + staff.Y;
                        goto foundStaff;
                    }
                }
            }
            foundStaff:;
        }
        double staffMiddleY = staffY + StaffHeight / 2;

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
        int growDir = group.GrowDirection;

        for (int level = 0; level < maxBeamCount; level++)
        {
            double levelOffset = level * beamTranslation;
            if (!group.StemUp)
                levelOffset = -levelOffset;

            // LILYPOND-REF: beam.cc:1039-1082 feather_factor
            // For feathered beams, apply different offsets at left and right ends
            double leftFeather = growDir == 0 ? 1.0 : (growDir > 0 ? 0.0 : 1.0);
            double rightFeather = growDir == 0 ? 1.0 : (growDir > 0 ? 1.0 : 0.0);

            double levelLeftY = leftBeamCenterY + levelOffset * leftFeather;
            double levelRightY = rightBeamCenterY + levelOffset * rightFeather;

            DrawBeamLevel(beamLayout, level, leftStemX, levelLeftY, rightStemX, levelRightY, beamThickness, memberStemXPositions);
        }
    }

    /// <summary>
    /// Finds the absolute Y position of a staff within a specific system.
    /// </summary>
    private static double FindStaffYInSystem(SystemLayout system, int staffIndex)
        => LayoutUtilities.FindStaffYInSystem(system, staffIndex);

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

        foreach (var beamLayout in layout.BeamLayouts)
        {
            if (!measureToSystem.TryGetValue(beamLayout.Group.MeasureIndex, out var system))
                continue;

            var group = beamLayout.Group;

            if (beamLayout.IsCrossStaff && !beamLayout.MemberStaffIndices.IsDefaultOrEmpty)
            {
                // Cross-staff beam: stem positions computed per-member with different staff Y
                CalculateCrossStaffBeamStemPositions(beamLayout, system);
                continue;
            }

            // Find staff Y within THIS system (not from a flat dictionary)
            double staffY = FindStaffYInSystem(system, beamLayout.StaffIndex);
            double staffMiddleY = staffY + StaffHeight / 2;

            var noteheadBBox = GlyphMetrics.GetNoteheadBBox(3);
            double noteheadCenterX = noteheadBBox.CenterX;
            double stemUpOffsetX = StemUpAttachX - noteheadCenterX;
            double stemDownOffsetX = StemDownAttachX - noteheadCenterX;
            double stemOffsetX = group.StemUp ? stemUpOffsetX : stemDownOffsetX;

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
                bool memberUp = group.IsKnee ? member.MemberStemUp : group.StemUp;
                double memberStemOffsetX = memberUp ? stemUpOffsetX : stemDownOffsetX;
                double noteCenterX = beamLayout.MemberXPositions[i];
                double stemX = noteCenterX + memberStemOffsetX;
                double primaryBeamCenterY = leftBeamCenterY + slope * (stemX - leftStemX);

                double stemEndY = memberUp
                    ? primaryBeamCenterY - beamThickness / 2
                    : primaryBeamCenterY + beamThickness / 2;

                _beamedStemEndYs[member.Item] = stemEndY;
                _beamedStemUp[member.Item] = memberUp;
            }
        }
    }

    /// <summary>
    /// Calculates stem end Y positions for cross-staff beamed notes.
    /// Each member may be on a different staff, so Y positions use system-global coordinates.
    /// </summary>
    private void CalculateCrossStaffBeamStemPositions(BeamLayout beamLayout, SystemLayout system)
    {
        var group = beamLayout.Group;
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(3);
        double noteheadCenterX = noteheadBBox.CenterX;
        double stemUpOffsetX = StemUpAttachX - noteheadCenterX;
        double stemDownOffsetX = StemDownAttachX - noteheadCenterX;
        double stemOffsetX = group.StemUp ? stemUpOffsetX : stemDownOffsetX;

        // Compute beam endpoints in global coordinates
        int leftStaffIdx = beamLayout.MemberStaffIndices[0];
        int rightStaffIdx = beamLayout.MemberStaffIndices[^1];
        double leftStaffMiddleY = FindStaffYInSystem(system, leftStaffIdx) + StaffHeight / 2;
        double rightStaffMiddleY = FindStaffYInSystem(system, rightStaffIdx) + StaffHeight / 2;
        double leftBeamGlobalY = leftStaffMiddleY - beamLayout.LeftY / 2;
        double rightBeamGlobalY = rightStaffMiddleY - beamLayout.RightY / 2;

        double leftStemX = beamLayout.LeftX + stemOffsetX;
        double rightStemX = beamLayout.RightX + stemOffsetX;
        double beamSpanX = rightStemX - leftStemX;
        double slope = beamSpanX > 0.001 ? (rightBeamGlobalY - leftBeamGlobalY) / beamSpanX : 0;
        double beamThickness = BeamThickness;

        for (int i = 0; i < group.Members.Length; i++)
        {
            var member = group.Members[i];
            bool memberUp = group.StemUp;
            double memberStemOffsetX = memberUp ? stemUpOffsetX : stemDownOffsetX;
            double noteCenterX = beamLayout.MemberXPositions[i];
            double stemX = noteCenterX + memberStemOffsetX;
            double primaryBeamCenterY = leftBeamGlobalY + slope * (stemX - leftStemX);

            double stemEndY = memberUp
                ? primaryBeamCenterY - beamThickness / 2
                : primaryBeamCenterY + beamThickness / 2;

            _beamedStemEndYs[member.Item] = stemEndY;
            _beamedStemUp[member.Item] = memberUp;
        }
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

        // Build cross-staff lookup for quick detection
        var crossStaffLookup = !layout.CrossStaffLayouts.IsDefaultOrEmpty
            ? CrossStaffEngraver.BuildCrossStaffLookup(layout.CrossStaffLayouts)
            : new HashSet<(int, int)>();

        foreach (var beamLayout in layout.BeamLayouts)
        {
            if (!measureToSystem.TryGetValue(beamLayout.Group.MeasureIndex, out var system))
                continue;

            if (beamLayout.IsCrossStaff && !beamLayout.MemberStaffIndices.IsDefaultOrEmpty)
            {
                // LILYPOND-REF: beam.cc:1451-1459 - cross-staff beam rendering
                DrawCrossStaffBeamGroup(beamLayout, system);
            }
            else
            {
                // Find staff Y within THIS system (not from a flat dictionary)
                double staffY = FindStaffYInSystem(system, beamLayout.StaffIndex);
                DrawBeamGroupAtStaffY(beamLayout, staffY);
            }
        }
    }

    /// <summary>
    /// Draws a beam group that spans multiple staves (cross-staff beam).
    /// Each member may be on a different staff, so Y positions are computed per-member
    /// in system-global coordinates.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1451-1459 - cross-staff detection
    /// LILYPOND-REF: scm/music-functions.scm:2372-2458 - stem spanning
    ///
    /// Algorithm:
    /// 1. Find each member's staff Y position based on MemberStaffIndices
    /// 2. Compute beam line in global coordinates using first/last member positions
    /// 3. Draw beam line and stems connecting each member to the beam
    /// </remarks>
    private void DrawCrossStaffBeamGroup(BeamLayout beamLayout, SystemLayout system)
    {
        var group = beamLayout.Group;
        var noteheadBBox = GlyphMetrics.GetNoteheadBBox(3);
        double noteheadCenterX = noteheadBBox.CenterX;
        var stemAnchor = group.StemUp ? GlyphMetrics.StemUpSE : GlyphMetrics.StemDownNW;
        double stemOffsetFromRef = -noteheadCenterX + stemAnchor.X;

        // Calculate stem X positions for each member
        var memberStemXPositions = new double[group.Members.Length];
        for (int i = 0; i < group.Members.Length; i++)
        {
            memberStemXPositions[i] = beamLayout.MemberXPositions[i] + stemOffsetFromRef;
        }

        // Use first member's staff as reference for beam Y calculation
        int refStaffIdx = beamLayout.MemberStaffIndices[0];
        double refStaffY = FindStaffYInSystem(system, refStaffIdx);
        double refStaffMiddleY = refStaffY + StaffHeight / 2;

        // Beam Y in global coordinates (relative to reference staff)
        double leftBeamGlobalY = refStaffMiddleY - beamLayout.LeftY / 2;
        double rightStaffIdx = beamLayout.MemberStaffIndices[^1];
        double rightStaffY = FindStaffYInSystem(system, (int)rightStaffIdx);
        double rightStaffMiddleY = rightStaffY + StaffHeight / 2;
        double rightBeamGlobalY = rightStaffMiddleY - beamLayout.RightY / 2;

        double leftStemX = memberStemXPositions[0];
        double rightStemX = memberStemXPositions[^1];

        int maxBeamCount = group.Members.Max(m => m.BeamCount);
        double beamThickness = BeamThickness;
        double beamTranslation = BeamTranslation;

        // Draw beam lines
        for (int level = 0; level < maxBeamCount; level++)
        {
            double levelOffset = level * beamTranslation;
            if (!group.StemUp)
                levelOffset = -levelOffset;

            double levelLeftY = leftBeamGlobalY + levelOffset;
            double levelRightY = rightBeamGlobalY + levelOffset;

            DrawBeamLevel(beamLayout, level, leftStemX, levelLeftY, rightStemX, levelRightY, beamThickness, memberStemXPositions);
        }

        // Draw stems for each member
        for (int i = 0; i < group.Members.Length; i++)
        {
            var member = group.Members[i];
            int memberStaffIdx = beamLayout.MemberStaffIndices[i];
            double memberStaffY = FindStaffYInSystem(system, memberStaffIdx);
            double memberStaffMiddleY = memberStaffY + StaffHeight / 2;

            // Note head Y position (in global coordinates)
            double noteheadY = memberStaffMiddleY - member.StaffPosition / 2.0;

            // Beam Y at this member's X position (interpolated)
            double t = (rightStemX - leftStemX) > 0.001
                ? (memberStemXPositions[i] - leftStemX) / (rightStemX - leftStemX)
                : 0;
            double beamYAtMember = leftBeamGlobalY + t * (rightBeamGlobalY - leftBeamGlobalY);

            // Draw stem from notehead to beam
            double stemTopY = group.StemUp ? beamYAtMember : noteheadY;
            double stemBottomY = group.StemUp ? noteheadY : beamYAtMember;

            _svg.AppendLine($"""  <line x1="{memberStemXPositions[i]:F1}" y1="{stemTopY:F1}" x2="{memberStemXPositions[i]:F1}" y2="{stemBottomY:F1}" stroke="black" stroke-width="{StemThickness:F2}"/>""");
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
        int growDir = group.GrowDirection;

        for (int level = 0; level < maxBeamCount; level++)
        {
            double levelOffset = level * beamTranslation;
            if (!group.StemUp)
                levelOffset = -levelOffset;

            // LILYPOND-REF: beam.cc:1039-1082 feather_factor
            double leftFeather = growDir == 0 ? 1.0 : (growDir > 0 ? 0.0 : 1.0);
            double rightFeather = growDir == 0 ? 1.0 : (growDir > 0 ? 1.0 : 0.0);

            double levelLeftY = leftBeamCenterY + levelOffset * leftFeather;
            double levelRightY = rightBeamCenterY + levelOffset * rightFeather;

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
        // Draw tie using LilyPond-style variable thickness
        // Endpoints are thin (pointed), middle is thickest
        // Reference: LilyPond's thickness and line-thickness properties

        double startX = tieLayout.StartX;
        double startY = tieLayout.StartY;
        double endX = tieLayout.EndX;
        double endY = tieLayout.EndY;
        double c1x = tieLayout.Control1.X;
        double c1y = tieLayout.Control1.Y;
        double c2x = tieLayout.Control2.X;
        double c2y = tieLayout.Control2.Y;

        // LilyPond-style parameters
        double midThickness = EngravingDefaults.TieMidThickness;  // Maximum thickness at middle
        double direction = tieLayout.CurveUp ? -1.0 : 1.0;

        // Inner curve control points are offset toward the curve interior
        // Control points get more offset to create bulge in the middle
        double innerC1x = c1x;
        double innerC1y = c1y + direction * midThickness * 0.9;
        double innerC2x = c2x;
        double innerC2y = c2y + direction * midThickness * 0.9;

        // Outer curve: start → c1,c2 → end
        string outerPath = $"M {startX:F1},{startY:F1} C {c1x:F1},{c1y:F1} {c2x:F1},{c2y:F1} {endX:F1},{endY:F1}";

        // Inner curve: end → c2',c1' → start (reversed direction, endpoints shared)
        string innerPath = $"C {innerC2x:F1},{innerC2y:F1} {innerC1x:F1},{innerC1y:F1} {startX:F1},{startY:F1}";

        // Combined path creates tapered shape (pointed at endpoints)
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
        // Draw slur using LilyPond-style variable thickness
        // Endpoints are thin (pointed), middle is thickest
        // Reference: LilyPond's thickness and line-thickness properties

        double startX = slurLayout.StartX;
        double startY = slurLayout.StartY;
        double endX = slurLayout.EndX;
        double endY = slurLayout.EndY;
        double c1x = slurLayout.Control1.X;
        double c1y = slurLayout.Control1.Y;
        double c2x = slurLayout.Control2.X;
        double c2y = slurLayout.Control2.Y;

        // LilyPond-style parameters
        double midThickness = EngravingDefaults.SlurMidThickness;  // Maximum thickness at middle
        double direction = slurLayout.CurveUp ? -1.0 : 1.0;

        // Inner curve control points are offset toward the curve interior
        // Control points get more offset to create bulge in the middle
        double innerC1x = c1x;
        double innerC1y = c1y + direction * midThickness * 0.9;
        double innerC2x = c2x;
        double innerC2y = c2y + direction * midThickness * 0.9;

        // Outer curve: start → c1,c2 → end
        string outerPath = $"M {startX:F1},{startY:F1} C {c1x:F1},{c1y:F1} {c2x:F1},{c2y:F1} {endX:F1},{endY:F1}";

        // Inner curve: end → c2',c1' → start (reversed direction, endpoints shared)
        string innerPath = $"C {innerC2x:F1},{innerC2y:F1} {innerC1x:F1},{innerC1y:F1} {startX:F1},{startY:F1}";

        // Combined path creates tapered shape (pointed at endpoints)
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

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        foreach (var dynamicLayout in layout.DynamicLayouts)
        {
            double systemY = measureToSystemY.TryGetValue(dynamicLayout.MeasureIndex, out var sy) ? sy : 0;
            DrawDynamic(dynamicLayout, systemY);
        }
    }

    /// <summary>
    /// Draws a single dynamic marking.
    /// </summary>
    private void DrawDynamic(DynamicLayout dynamicLayout, double systemY)
    {
        // SMuFL dynamic glyphs
        // LILYPOND-REF: define-grobs.scm:1317 font-encoding = fetaText
        string glyph = GetDynamicGlyph(dynamicLayout.Text);

        double x = dynamicLayout.X;
        double y = systemY + dynamicLayout.Y;

        // Center the dynamic horizontally
        // LILYPOND-REF: define-grobs.scm:1311 self-alignment-X = CENTER
        double glyphWidth = EstimateDynamicWidth(dynamicLayout.Text);
        x -= glyphWidth / 2;

        // Dynamic font size: 2 staff spaces (FontSize * 0.5) to match LilyPond sizing
        // LILYPOND-REF: define-grobs.scm DynamicText font-size = 2 (relative units)
        double fontSize = FontSize * 0.5;

        // Dynamics use serif italic font (not Emmentaler which is for music symbols)
        // LILYPOND-REF: define-grobs.scm:1315 font-series = bold, font-shape = italic
        _svg.AppendLine($"  <text x=\"{x:F2}\" y=\"{y:F2}\" font-family=\"serif\" font-size=\"{fontSize:F1}\" font-style=\"italic\" font-weight=\"bold\" fill=\"black\" data-pos=\"{dynamicLayout.SourcePosition}\">{glyph}</text>");
    }

    /// <summary>
    /// Gets the SMuFL glyph string for a dynamic marking.
    /// </summary>
    private static string GetDynamicGlyph(string text) => text switch
    {
        // Emmentaler uses regular italic letters for dynamics, not SMuFL glyphs
        // LILYPOND-REF: define-grobs.scm:1317 font-encoding = fetaText
        "ppp" => "ppp",
        "pp" => "pp",
        "p" => "p",
        "mp" => "mp",
        "mf" => "mf",
        "f" => "f",
        "ff" => "ff",
        "fff" => "fff",
        "cresc" => "cresc.",
        "decresc" => "decresc.",
        "dim" => "dim.",
        _ => text
    };

    /// <summary>
    /// Estimates the width of a dynamic marking for centering.
    /// </summary>
    private static double EstimateDynamicWidth(string text) => text switch
    {
        // Width estimates scaled for FontSize * 0.5 font size
        "ppp" => 1.5,
        "pp" => 1.1,
        "p" => 0.5,
        "mp" => 1.1,
        "mf" => 1.1,
        "f" => 0.5,
        "ff" => 1.1,
        "fff" => 1.5,
        _ => text.Length * 0.5
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

        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
            foreach (var measure in system.Measures)
                measureToSystemY[measure.MeasureIndex] = system.Y;

        foreach (var articulationLayout in layout.ArticulationLayouts)
        {
            double systemY = measureToSystemY.TryGetValue(articulationLayout.MeasureIndex, out var sy) ? sy : 0;
            DrawArticulation(articulationLayout, systemY);
        }
    }

    /// <summary>
    /// Draws a single articulation mark.
    /// </summary>
    private void DrawArticulation(ArticulationLayout articulationLayout, double systemY)
    {
        double x = articulationLayout.X;
        double y = systemY + articulationLayout.Y;
        string glyph = articulationLayout.Glyph;

        if (string.IsNullOrEmpty(glyph))
            return;

        // LILYPOND-REF: define-grobs.scm:2289 self-alignment-X = CENTER
        // LilyPond centers the glyph's visual extent on the reference point,
        // NOT using text-anchor (which centers the advance width and may include
        // asymmetric side bearings). Use DrawGlyph for consistent rendering
        // with noteheads — the Emmentaler font preserves METAFONT origins.
        DrawGlyph(glyph[0], x, y, articulationLayout.SourcePosition);
    }

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
    private void DrawGraceNotes(Score score, ScoreLayout layout)
    {
        if (layout.GraceNoteLayouts.IsDefaultOrEmpty)
            return;

        // Build measure → (system, measureLayout) lookup
        var measureToInfo = new Dictionary<int, (SystemLayout System, MeasureLayout Measure)>();
        foreach (var system in layout.Systems)
            foreach (var measure in system.Measures)
                measureToInfo[measure.MeasureIndex] = (system, measure);

        foreach (var graceLayout in layout.GraceNoteLayouts)
        {
            if (!measureToInfo.TryGetValue(graceLayout.MeasureIndex, out var info))
                continue;

            // Get main note absolute X and Y
            double mainNoteX = info.Measure.X;
            if (graceLayout.MainNoteItemIndex < info.Measure.Items.Length)
                mainNoteX = info.Measure.X + info.Measure.Items[graceLayout.MainNoteItemIndex].X;

            double mainNoteY = info.System.Y + StaffHeight / 2;
            var measures = score.Voice.Measures;
            if (graceLayout.MeasureIndex < measures.Length)
            {
                var measure = measures[graceLayout.MeasureIndex];
                if (graceLayout.MainNoteItemIndex < measure.Items.Length
                    && measure.Items[graceLayout.MainNoteItemIndex] is NoteItem mainNote)
                {
                    mainNoteY = info.System.Y + StaffHeight / 2 - (mainNote.StaffPosition / 2.0);
                }
            }

            DrawGraceNoteGroup(graceLayout, info.System.Y, mainNoteX, mainNoteY);
        }
    }

    /// <summary>
    /// Draws a group of grace notes with optional slur to main note.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/grace-init.ly:21-48 acciaccatura/appoggiatura auto-slur
    /// Acciaccatura and appoggiatura automatically get a small slur connecting
    /// the last grace note to the main note.
    /// </remarks>
    private void DrawGraceNoteGroup(GraceNoteLayout graceLayout, double systemY,
        double mainNoteX, double mainNoteY)
    {
        double x = graceLayout.X;
        double scale = graceLayout.Scale;
        double scaledFontSize = FontSize * scale;
        double noteSpacing = 1.2 * scale;

        double lastNoteX = x;
        double lastNoteY = systemY + StaffHeight / 2;

        foreach (var noteInfo in graceLayout.Notes)
        {
            // Calculate Y position from staff position (same formula as regular notes)
            double y = systemY + StaffHeight / 2 - (noteInfo.StaffPosition / 2.0);

            // Draw the notehead using Emmentaler glyph
            char noteGlyph = EmmentalerGlyphs.NoteheadBlack;
            _svg.AppendLine($"  <text class=\"music\" x=\"{x:F2}\" y=\"{y:F2}\" font-size=\"{scaledFontSize:F1}\" data-pos=\"{graceLayout.SourcePosition}\">{noteGlyph}</text>");

            // Draw stem
            double stemX = x + 0.5 * scale;
            double stemStartY = y;
            double stemEndY = y - 3.5 * scale; // Stem goes up
            _svg.AppendLine($"  <line x1=\"{stemX:F2}\" y1=\"{stemStartY:F2}\" x2=\"{stemX:F2}\" y2=\"{stemEndY:F2}\" stroke=\"black\" stroke-width=\"{StemThickness:F2}\"/>");

            // Draw flag for grace notes using Emmentaler glyph
            char flagGlyph = EmmentalerGlyphs.Flag8thUp;
            _svg.AppendLine($"  <text class=\"music\" x=\"{stemX:F2}\" y=\"{stemEndY:F2}\" font-size=\"{scaledFontSize:F1}\">{flagGlyph}</text>");

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
                char? accidentalGlyph = noteInfo.Accidental switch
                {
                    "sharp" => EmmentalerGlyphs.AccidentalSharp,
                    "flat" => EmmentalerGlyphs.AccidentalFlat,
                    "natural" => EmmentalerGlyphs.AccidentalNatural,
                    "doubleSharp" => EmmentalerGlyphs.AccidentalDoubleSharp,
                    "doubleFlat" => EmmentalerGlyphs.AccidentalDoubleFlat,
                    _ => null
                };
                if (accidentalGlyph != null)
                {
                    double accX = x - 0.8 * scale;
                    _svg.AppendLine($"  <text class=\"music\" x=\"{accX:F2}\" y=\"{y:F2}\" font-size=\"{scaledFontSize:F1}\">{accidentalGlyph}</text>");
                }
            }

            lastNoteX = x;
            lastNoteY = y;
            x += noteSpacing;
        }

        // Draw grace slur for acciaccatura and appoggiatura
        // LILYPOND-REF: ly/grace-init.ly startGraceSlur/stopGraceSlur
        if (graceLayout.Type is GraceNoteType.Acciaccatura or GraceNoteType.Appoggiatura)
        {
            DrawGraceSlur(lastNoteX, lastNoteY, mainNoteX, mainNoteY, scale);
        }
    }

    /// <summary>
    /// Draws a small slur from the last grace note to the main note.
    /// </summary>
    private void DrawGraceSlur(double graceX, double graceY, double mainNoteX, double mainNoteY, double scale)
    {
        // Grace notes have stems up, so slur curves below (positive Y in SVG)
        // LILYPOND-REF: ly/grace-init.ly — slur arcs from grace notehead to main notehead
        // Position at notehead centers, not edges, to ensure visible width
        double startX = graceX + GlyphMetrics.NoteheadBlack.CenterX * scale;
        double startY = graceY + 0.5;

        double endX = mainNoteX + GlyphMetrics.NoteheadBlack.CenterX;
        double endY = mainNoteY + 0.5;

        double dx = endX - startX;
        if (dx < 0.5) return; // Safety: skip degenerate slurs

        double arcHeight = Math.Min(dx * 0.25, 1.2);

        double cpT1 = 0.3;
        double cpT2 = 0.7;
        double c1x = startX + dx * cpT1;
        double c1y = startY + cpT1 * (endY - startY) + arcHeight;
        double c2x = startX + dx * cpT2;
        double c2y = startY + cpT2 * (endY - startY) + arcHeight;

        // Draw tapered slur (same technique as regular slurs but thinner)
        double midThickness = EngravingDefaults.SlurMidThickness * scale;
        double innerC1y = c1y - midThickness * 0.9;
        double innerC2y = c2y - midThickness * 0.9;

        string outerPath = $"M {startX:F1},{startY:F1} C {c1x:F1},{c1y:F1} {c2x:F1},{c2y:F1} {endX:F1},{endY:F1}";
        string innerPath = $"C {c2x:F1},{innerC2y:F1} {c1x:F1},{innerC1y:F1} {startX:F1},{startY:F1}";
        _svg.AppendLine($"  <path d=\"{outerPath} {innerPath} Z\" fill=\"black\"/>");
    }

    /// <summary>
    /// Calculate stem end Y position using LilyPond's duration-dependent algorithm.
    /// Delegates to StemCalculator for duration-aware lengths, unnatural direction
    /// shortening, and staff extension rules.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:415-523 internal_calc_stem_end_position
    /// </remarks>
    private static double CalculateStemEndY(
        double stemAttachY, bool stemUp, double systemY,
        int durationLog = 2, int staffPosition = 0)
    {
        return StemCalculator.CalculateStemEndY(
            stemAttachY, stemUp, systemY,
            durationLog, staffPosition);
    }

    /// <summary>
    /// Draw lyrics below the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:60-150 process_music
    /// LILYPOND-REF: lily/lyric-hyphen.cc:1-150
    /// LILYPOND-REF: scm/define-grobs.scm:3020-3060 LyricText grob
    ///
    /// Lyrics are rendered as text elements centered under their associated notes.
    /// Hyphens (-) connect syllables of the same word (drawn as SVG lines).
    /// Extender lines (___) indicate melisma.
    /// </remarks>
    private void DrawLyrics(ScoreLayout layout)
    {
        if (layout.LyricLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // Lyric font size: slightly smaller than music font
        // LILYPOND-REF: scm/define-grobs.scm:3025 font-size = -1
        double lyricFontSize = FontSize * 0.8;

        // Draw syllable text
        foreach (var lyricLayout in layout.LyricLayouts)
        {
            // Get system Y offset for this lyric's measure
            double systemY = measureToSystemY.TryGetValue(lyricLayout.Item.MeasureIndex, out var y) ? y : 0;

            // Calculate absolute Y position (lyricLayout.Y is relative to staff top)
            double absoluteY = systemY + lyricLayout.Y;

            // Draw the syllable text
            _svg.AppendLine($"  <text x=\"{lyricLayout.X:F2}\" y=\"{absoluteY:F2}\" " +
                $"font-family=\"serif\" font-size=\"{lyricFontSize:F1}\" " +
                $"text-anchor=\"middle\" dominant-baseline=\"hanging\" class=\"lyric\">" +
                $"{EscapeXml(lyricLayout.Item.Text)}</text>");
        }

        // Draw hyphens and extenders using LyricHyphenLayouts
        // LILYPOND-REF: lily/lyric-hyphen.cc:80-120
        if (!layout.LyricHyphenLayouts.IsDefaultOrEmpty)
        {
            foreach (var hyphenLayout in layout.LyricHyphenLayouts)
            {
                if (hyphenLayout.Type == LyricConnectorType.Hyphen)
                {
                    // Draw hyphen dashes as SVG lines (more consistent than text)
                    foreach (var dash in hyphenLayout.Dashes)
                    {
                        // Get system Y for this dash
                        var sourceLyric = layout.LyricLayouts[hyphenLayout.LyricIndex];
                        double systemY = measureToSystemY.TryGetValue(sourceLyric.Item.MeasureIndex, out var y) ? y : 0;
                        double absoluteY = systemY + dash.Y;

                        _svg.AppendLine($"  <line x1=\"{dash.X1:F2}\" y1=\"{absoluteY:F2}\" " +
                            $"x2=\"{dash.X2:F2}\" y2=\"{absoluteY:F2}\" " +
                            $"stroke=\"black\" stroke-width=\"0.12\" stroke-linecap=\"round\" " +
                            $"class=\"lyric-hyphen\" />");
                    }
                }
                else if (hyphenLayout.Type == LyricConnectorType.Extender)
                {
                    var sourceLyric = layout.LyricLayouts[hyphenLayout.LyricIndex];
                    double systemY = measureToSystemY.TryGetValue(sourceLyric.Item.MeasureIndex, out var y) ? y : 0;
                    double absoluteY = systemY + hyphenLayout.ExtenderY;

                    if (hyphenLayout.CrossesSystemBreak)
                    {
                        // Draw two segments for system break crossing
                        _svg.AppendLine($"  <line x1=\"{hyphenLayout.ExtenderStartX:F2}\" y1=\"{absoluteY:F2}\" " +
                            $"x2=\"{hyphenLayout.FirstSegmentEndX:F2}\" y2=\"{absoluteY:F2}\" " +
                            $"stroke=\"black\" stroke-width=\"0.08\" class=\"lyric-extender\" />");

                        // Second segment on next system (need to recalculate Y)
                        // For now, draw at same relative Y on next system
                        _svg.AppendLine($"  <line x1=\"{hyphenLayout.SecondSegmentStartX:F2}\" y1=\"{absoluteY:F2}\" " +
                            $"x2=\"{hyphenLayout.ExtenderEndX:F2}\" y2=\"{absoluteY:F2}\" " +
                            $"stroke=\"black\" stroke-width=\"0.08\" class=\"lyric-extender\" />");
                    }
                    else
                    {
                        _svg.AppendLine($"  <line x1=\"{hyphenLayout.ExtenderStartX:F2}\" y1=\"{absoluteY:F2}\" " +
                            $"x2=\"{hyphenLayout.ExtenderEndX:F2}\" y2=\"{absoluteY:F2}\" " +
                            $"stroke=\"black\" stroke-width=\"0.08\" class=\"lyric-extender\" />");
                    }
                }
            }
        }
        else
        {
            // Fallback: use old rendering from LyricLayout (for backwards compatibility)
            foreach (var lyricLayout in layout.LyricLayouts)
            {
                double systemY = measureToSystemY.TryGetValue(lyricLayout.Item.MeasureIndex, out var y) ? y : 0;
                double absoluteY = systemY + lyricLayout.Y;

                // Draw hyphen if needed (fallback)
                if (lyricLayout.DrawHyphen)
                {
                    double hyphenY = absoluteY + 0.4;
                    double dashLength = 0.4;
                    _svg.AppendLine($"  <line x1=\"{lyricLayout.HyphenX - dashLength / 2:F2}\" y1=\"{hyphenY:F2}\" " +
                        $"x2=\"{lyricLayout.HyphenX + dashLength / 2:F2}\" y2=\"{hyphenY:F2}\" " +
                        $"stroke=\"black\" stroke-width=\"0.12\" stroke-linecap=\"round\" " +
                        $"class=\"lyric-hyphen\" />");
                }

                // Draw extender line if needed (fallback)
                if (lyricLayout.DrawExtender)
                {
                    double extenderY = absoluteY + 0.7;
                    double startX = lyricLayout.X + lyricLayout.Width / 2 + 0.2;
                    _svg.AppendLine($"  <line x1=\"{startX:F2}\" y1=\"{extenderY:F2}\" " +
                        $"x2=\"{lyricLayout.ExtenderEndX:F2}\" y2=\"{extenderY:F2}\" " +
                        $"stroke=\"black\" stroke-width=\"0.08\" class=\"lyric-extender\" />");
                }
            }
        }
    }

    /// <summary>
    /// Draws music marks (segno, coda, D.S., D.C., Fine, etc.).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/mark-engraver.cc - mark engraver
    /// LILYPOND-REF: scm/define-grobs.scm:2100-2150 RehearsalMark grob
    /// </remarks>
    private void DrawMusicMarks(ScoreLayout layout)
    {
        if (layout.MusicMarkLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        foreach (var markLayout in layout.MusicMarkLayouts)
        {
            // Skip types handled by specialized engravers (hairpin wedges, text spanners, ottava brackets)
            if (IsHandledBySpecializedEngraver(markLayout.MarkType))
                continue;

            double systemY = measureToSystemY.TryGetValue(markLayout.MeasureIndex, out var y) ? y : 0;
            double absoluteY = systemY + markLayout.Y;

            if (markLayout.IsSymbol)
            {
                // Draw symbol glyph (segno or coda)
                string glyph = GetMusicMarkGlyph(markLayout.MarkType);
                _svg.AppendLine($"  <text x=\"{markLayout.X:F2}\" y=\"{absoluteY:F2}\" " +
                    $"font-family=\"Emmentaler\" font-size=\"{FontSize:F1}\" " +
                    $"text-anchor=\"middle\" dominant-baseline=\"central\" " +
                    $"class=\"music-mark-symbol\">{glyph}</text>");
            }
            else if (markLayout.MarkType == MusicMarkType.Rehearsal)
            {
                // LILYPOND-REF: define-grobs.scm:2607-2636 RehearsalMark
                // Draw boxed rehearsal mark (rect + bold text centered inside)
                // font-size = 2 → base(11pt) * 2^(2/6) ≈ 2.77 staff spaces
                // LILYPOND-REF: define-markup-commands.scm box-padding = 0.2
                double rehearsalFontSize = FontSize * 0.6;
                double boxPadding = 0.2;
                double textWidth = MeasureSerifBoldText(markLayout.Text, rehearsalFontSize);
                double boxWidth = textWidth + boxPadding * 2;
                double boxHeight = rehearsalFontSize + boxPadding * 2;
                double boxX = markLayout.X - boxWidth / 2;
                double boxY = absoluteY - boxHeight / 2;

                _svg.AppendLine($"  <rect x=\"{boxX:F2}\" y=\"{boxY:F2}\" " +
                    $"width=\"{boxWidth:F2}\" height=\"{boxHeight:F2}\" " +
                    $"fill=\"white\" stroke=\"black\" stroke-width=\"0.10\" " +
                    $"class=\"rehearsal-mark-box\" />");
                _svg.AppendLine($"  <text x=\"{markLayout.X:F2}\" y=\"{absoluteY:F2}\" " +
                    $"font-family=\"serif\" font-size=\"{rehearsalFontSize:F1}\" " +
                    $"font-weight=\"bold\" " +
                    $"text-anchor=\"middle\" dominant-baseline=\"central\" " +
                    $"class=\"rehearsal-mark-text\">{EscapeXml(markLayout.Text)}</text>");
            }
            else if (markLayout.MarkType == MusicMarkType.SectionLabel)
            {
                // LILYPOND-REF: define-grobs.scm:2764-2802 SectionLabel
                // font-size = 1.5 → base(11pt) * 2^(1.5/6) ≈ 2.62 staff spaces
                // outside-staff-priority = 1450 (closer to staff than RehearsalMark at 1500)
                // LILYPOND-REF: define-markup-commands.scm box-padding = 0.2
                double sectionFontSize = FontSize * 0.55;
                double boxPadding = 0.2;
                double textWidth = MeasureSerifBoldText(markLayout.Text, sectionFontSize);
                double boxWidth = textWidth + boxPadding * 2;
                double boxHeight = sectionFontSize + boxPadding * 2;
                double boxX = markLayout.X - boxWidth / 2;
                double boxY = absoluteY - boxHeight / 2;

                _svg.AppendLine($"  <rect x=\"{boxX:F2}\" y=\"{boxY:F2}\" " +
                    $"width=\"{boxWidth:F2}\" height=\"{boxHeight:F2}\" " +
                    $"fill=\"white\" stroke=\"black\" stroke-width=\"0.10\" " +
                    $"class=\"section-label-box\" />");
                _svg.AppendLine($"  <text x=\"{markLayout.X:F2}\" y=\"{absoluteY:F2}\" " +
                    $"font-family=\"serif\" font-size=\"{sectionFontSize:F1}\" " +
                    $"font-weight=\"bold\" " +
                    $"text-anchor=\"middle\" dominant-baseline=\"central\" " +
                    $"class=\"section-label-text\">{EscapeXml(markLayout.Text)}</text>");
            }
            else if (IsPedalMark(markLayout.MarkType))
            {
                // LILYPOND-REF: define-grobs.scm:3255-3274 SustainPedal
                // Sustain pedal uses upright bold; sostenuto/una corda use italic
                bool isItalic = markLayout.MarkType == MusicMarkType.SostenutoOn ||
                                markLayout.MarkType == MusicMarkType.SostenutoOff ||
                                markLayout.MarkType == MusicMarkType.UnaCordaOn ||
                                markLayout.MarkType == MusicMarkType.UnaCordaOff;
                string fontStyle = isItalic ? "font-style=\"italic\" " : "";
                _svg.AppendLine($"  <text x=\"{markLayout.X:F2}\" y=\"{absoluteY:F2}\" " +
                    $"font-family=\"serif\" font-size=\"{FontSize * 0.7:F1}\" " +
                    $"{fontStyle}font-weight=\"bold\" " +
                    $"text-anchor=\"middle\" dominant-baseline=\"central\" " +
                    $"class=\"pedal-mark-text\">{EscapeXml(markLayout.Text)}</text>");
            }
            else
            {
                // Draw text (D.S., D.C., Fine, etc.) in italic bold
                _svg.AppendLine($"  <text x=\"{markLayout.X:F2}\" y=\"{absoluteY:F2}\" " +
                    $"font-family=\"serif\" font-size=\"{FontSize * 0.7:F1}\" " +
                    $"font-style=\"italic\" font-weight=\"bold\" " +
                    $"text-anchor=\"middle\" dominant-baseline=\"central\" " +
                    $"class=\"music-mark-text\">{EscapeXml(markLayout.Text)}</text>");
            }
        }
    }

    /// <summary>
    /// Types handled by HairpinEngraver, TextSpannerEngraver, or OttavaBracketEngraver.
    /// These should not be rendered as generic MusicMark text.
    /// </summary>
    private static bool IsHandledBySpecializedEngraver(MusicMarkType type) =>
        type == MusicMarkType.Cresc || type == MusicMarkType.Decresc || type == MusicMarkType.Dim ||
        type == MusicMarkType.Rit || type == MusicMarkType.Accel ||
        type == MusicMarkType.OttavaUp || type == MusicMarkType.OttavaDown ||
        type == MusicMarkType.QuindicesUp || type == MusicMarkType.QuindicesDown ||
        type == MusicMarkType.Loco;

    private static bool IsPedalMark(MusicMarkType type) =>
        type == MusicMarkType.SustainOn || type == MusicMarkType.SustainOff ||
        type == MusicMarkType.SostenutoOn || type == MusicMarkType.SostenutoOff ||
        type == MusicMarkType.UnaCordaOn || type == MusicMarkType.UnaCordaOff;

    private static string GetMusicMarkGlyph(MusicMarkType type) => type switch
    {
        MusicMarkType.Segno => "\uE047",  // SMuFL segno
        MusicMarkType.Coda => "\uE048",   // SMuFL coda
        _ => ""
    };

    /// <summary>
    /// Draws custom text annotations (e.g., "molto rit.", "a tempo").
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/text-interface.cc - text rendering
    /// LILYPOND-REF: scm/define-grobs.scm:3600-3650 TextScript grob
    /// </remarks>
    private void DrawCustomTexts(ScoreLayout layout)
    {
        if (layout.CustomTextLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // Custom text font size: similar to dynamics
        double textFontSize = FontSize * 0.7;

        foreach (var textLayout in layout.CustomTextLayouts)
        {
            double systemY = measureToSystemY.TryGetValue(textLayout.MeasureIndex, out var y) ? y : 0;
            double absoluteY = systemY + textLayout.Y;

            // Draw custom text in italic (standard for expression markings)
            _svg.AppendLine($"  <text x=\"{textLayout.X:F2}\" y=\"{absoluteY:F2}\" " +
                $"font-family=\"serif\" font-size=\"{textFontSize:F1}\" " +
                $"font-style=\"italic\" " +
                $"text-anchor=\"start\" dominant-baseline=\"hanging\" " +
                $"class=\"custom-text\">{EscapeXml(textLayout.Text)}</text>");
        }
    }

    /// <summary>
    /// Draws volta brackets (first/second ending brackets).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-bracket.cc:60-120 print method
    /// LILYPOND-REF: scm/define-grobs.scm:4850-4900 VoltaBracket grob
    ///
    /// Volta brackets consist of:
    /// - Horizontal line at top
    /// - Left vertical hook (always)
    /// - Right vertical hook (if closed)
    /// - Number text (e.g., "1.", "2.")
    /// </remarks>
    private void DrawVoltaBrackets(ScoreLayout layout)
    {
        if (layout.VoltaBracketLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // Bracket line thickness
        const double lineThickness = 0.13;
        // Edge height (vertical hook)
        double edgeHeight = VoltaBracketEngraver.GetEdgeHeight();

        foreach (var bracketLayout in layout.VoltaBracketLayouts)
        {
            // Get system Y for this bracket
            double systemY = measureToSystemY.TryGetValue(bracketLayout.StartMeasureIndex, out var y) ? y : 0;
            double absoluteY = systemY + bracketLayout.Y;

            double startX = bracketLayout.StartX;
            double endX = bracketLayout.EndX;

            // Draw left vertical hook (downward from line)
            _svg.AppendLine($"  <line x1=\"{startX:F2}\" y1=\"{absoluteY:F2}\" " +
                $"x2=\"{startX:F2}\" y2=\"{absoluteY + edgeHeight:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F2}\" class=\"volta-bracket\" />");

            // Draw horizontal line
            _svg.AppendLine($"  <line x1=\"{startX:F2}\" y1=\"{absoluteY:F2}\" " +
                $"x2=\"{endX:F2}\" y2=\"{absoluteY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F2}\" class=\"volta-bracket\" />");

            // Draw right vertical hook if closed
            if (bracketLayout.IsClosed)
            {
                _svg.AppendLine($"  <line x1=\"{endX:F2}\" y1=\"{absoluteY:F2}\" " +
                    $"x2=\"{endX:F2}\" y2=\"{absoluteY + edgeHeight:F2}\" " +
                    $"stroke=\"black\" stroke-width=\"{lineThickness:F2}\" class=\"volta-bracket\" />");
            }

            // Draw volta number text
            double textX = startX + 0.5;
            double textY = absoluteY + 0.3;
            double textFontSize = FontSize * 0.6;
            _svg.AppendLine($"  <text x=\"{textX:F2}\" y=\"{textY:F2}\" " +
                $"font-family=\"serif\" font-size=\"{textFontSize:F1}\" " +
                $"font-weight=\"bold\" " +
                $"text-anchor=\"start\" dominant-baseline=\"hanging\" " +
                $"class=\"volta-text\">{EscapeXml(bracketLayout.VoltaText)}</text>");
        }
    }

    /// <summary>
    /// Draws tuplet brackets with numbers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:200-350 print method
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket defaults
    ///
    /// Tuplet brackets consist of:
    /// - Horizontal line
    /// - Small hooks at both ends
    /// - Number centered on the bracket
    /// </remarks>
    private void DrawTupletBrackets(ScoreLayout layout)
    {
        if (layout.TupletBracketLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // Bracket line thickness
        const double lineThickness = 0.13;
        // Edge height (vertical hook)
        double edgeHeight = TupletBracketEngraver.GetEdgeHeight();

        foreach (var bracketLayout in layout.TupletBracketLayouts)
        {
            // Get system Y for this bracket
            double systemY = measureToSystemY.TryGetValue(bracketLayout.MeasureIndex, out var y) ? y : 0;
            double absoluteY = systemY + bracketLayout.Y;

            double startX = bracketLayout.StartX;
            double endX = bracketLayout.EndX;
            double midX = (startX + endX) / 2;

            // Hook direction based on stem direction
            double hookDir = bracketLayout.IsStemUp ? 1 : -1;

            // Draw left vertical hook
            _svg.AppendLine($"  <line x1=\"{startX:F2}\" y1=\"{absoluteY:F2}\" " +
                $"x2=\"{startX:F2}\" y2=\"{absoluteY + edgeHeight * hookDir:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F2}\" class=\"tuplet-bracket\" />");

            // Draw horizontal line (left part, up to number gap)
            double numberGap = 1.0;
            _svg.AppendLine($"  <line x1=\"{startX:F2}\" y1=\"{absoluteY:F2}\" " +
                $"x2=\"{midX - numberGap:F2}\" y2=\"{absoluteY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F2}\" class=\"tuplet-bracket\" />");

            // Draw horizontal line (right part, after number gap)
            _svg.AppendLine($"  <line x1=\"{midX + numberGap:F2}\" y1=\"{absoluteY:F2}\" " +
                $"x2=\"{endX:F2}\" y2=\"{absoluteY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F2}\" class=\"tuplet-bracket\" />");

            // Draw right vertical hook
            _svg.AppendLine($"  <line x1=\"{endX:F2}\" y1=\"{absoluteY:F2}\" " +
                $"x2=\"{endX:F2}\" y2=\"{absoluteY + edgeHeight * hookDir:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F2}\" class=\"tuplet-bracket\" />");

            // Draw tuplet number text centered
            double textFontSize = FontSize * 0.6;
            double textY = bracketLayout.IsStemUp ? absoluteY - 0.3 : absoluteY + 0.8;
            _svg.AppendLine($"  <text x=\"{midX:F2}\" y=\"{textY:F2}\" " +
                $"font-family=\"serif\" font-size=\"{textFontSize:F1}\" " +
                $"font-weight=\"bold\" " +
                $"text-anchor=\"middle\" dominant-baseline=\"middle\" " +
                $"class=\"tuplet-number\">{EscapeXml(bracketLayout.NumberText)}</text>");
        }
    }

    /// <summary>
    /// Draws all hairpin (crescendo/decrescendo) wedges.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:110-358 print()
    /// LILYPOND-REF: scm/define-grobs.scm:1641-1666 Hairpin grob
    ///
    /// A hairpin is drawn as two lines forming a wedge:
    /// - Crescendo (&lt;): point at left, opening at right
    /// - Decrescendo (&gt;): opening at left, point at right
    ///
    /// thickness: 1.0 staff line widths
    /// </remarks>
    private void DrawHairpins(ScoreLayout layout)
    {
        if (layout.HairpinLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // LILYPOND-REF: scm/define-grobs.scm:1664 (thickness . 1.0)
        // thickness is in staff line widths
        double lineThickness = StaffLineThickness;

        foreach (var hairpin in layout.HairpinLayouts)
        {
            double systemY = measureToSystemY.TryGetValue(hairpin.StartMeasureIndex, out var y) ? y : 0;
            double absoluteY = systemY + hairpin.Y;
            double opening = hairpin.Opening;

            double startX = hairpin.StartX;
            double endX = hairpin.EndX;

            // Crescendo: point on left, open on right
            // Decrescendo: open on left, point on right
            double leftTopY, leftBottomY, rightTopY, rightBottomY;

            if (hairpin.Direction == HairpinDirection.Crescendo)
            {
                // < shape: converges at left
                leftTopY = absoluteY;
                leftBottomY = absoluteY;
                rightTopY = absoluteY - opening;
                rightBottomY = absoluteY + opening;
            }
            else
            {
                // > shape: converges at right
                leftTopY = absoluteY - opening;
                leftBottomY = absoluteY + opening;
                rightTopY = absoluteY;
                rightBottomY = absoluteY;
            }

            // Draw upper line of wedge
            _svg.AppendLine($"  <line x1=\"{startX:F2}\" y1=\"{leftTopY:F2}\" " +
                $"x2=\"{endX:F2}\" y2=\"{rightTopY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                $"class=\"hairpin\" data-pos=\"{hairpin.SourcePosition}\" />");

            // Draw lower line of wedge
            _svg.AppendLine($"  <line x1=\"{startX:F2}\" y1=\"{leftBottomY:F2}\" " +
                $"x2=\"{endX:F2}\" y2=\"{rightBottomY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                $"class=\"hairpin\" data-pos=\"{hairpin.SourcePosition}\" />");
        }
    }

    /// <summary>
    /// Draws all text spanners (text + dashed line markings like "rit. ----").
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/line-spanner.cc:526-648 Line_spanner::print()
    /// LILYPOND-REF: scm/define-grobs.scm:3504-3535 TextSpanner grob
    ///
    /// A text spanner consists of:
    /// - Italic text at the start (e.g., "rit.", "accel.")
    /// - A dashed line extending from the text to the end point
    ///
    /// dash-period: 3.0, dash-fraction: 0.2
    /// </remarks>
    private void DrawTextSpanners(ScoreLayout layout)
    {
        if (layout.TextSpannerLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // LILYPOND-REF: scm/define-grobs.scm:3516 (font-shape . italic)
        double textFontSize = FontSize * 0.5;
        double lineThickness = StaffLineThickness;

        foreach (var spanner in layout.TextSpannerLayouts)
        {
            double systemY = measureToSystemY.TryGetValue(spanner.StartMeasureIndex, out var y) ? y : 0;
            double absoluteY = systemY + spanner.Y;

            // Draw the text label
            _svg.AppendLine($"  <text x=\"{spanner.StartX:F2}\" y=\"{absoluteY:F2}\" " +
                $"font-family=\"serif\" font-size=\"{textFontSize:F1}\" " +
                $"font-style=\"italic\" fill=\"black\" " +
                $"class=\"text-spanner\" data-pos=\"{spanner.SourcePosition}\">" +
                $"{EscapeXml(spanner.Text)}</text>");

            // Draw the dashed line (if there's space)
            if (spanner.Style != TextSpannerStyle.None && spanner.LineStartX < spanner.EndX)
            {
                // Adjust line Y to be at the text baseline level
                double lineY = absoluteY;

                if (spanner.Style == TextSpannerStyle.DashedLine)
                {
                    // SVG stroke-dasharray: "dash-length gap-length"
                    double dashLength = spanner.DashPeriod * spanner.DashFraction;
                    double gapLength = spanner.DashPeriod * (1 - spanner.DashFraction);
                    _svg.AppendLine($"  <line x1=\"{spanner.LineStartX:F2}\" y1=\"{lineY:F2}\" " +
                        $"x2=\"{spanner.EndX:F2}\" y2=\"{lineY:F2}\" " +
                        $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                        $"stroke-dasharray=\"{dashLength:F2} {gapLength:F2}\" " +
                        $"class=\"text-spanner-line\" data-pos=\"{spanner.SourcePosition}\" />");
                }
                else // Solid line
                {
                    _svg.AppendLine($"  <line x1=\"{spanner.LineStartX:F2}\" y1=\"{lineY:F2}\" " +
                        $"x2=\"{spanner.EndX:F2}\" y2=\"{lineY:F2}\" " +
                        $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                        $"class=\"text-spanner-line\" data-pos=\"{spanner.SourcePosition}\" />");
                }
            }
        }
    }

    /// <summary>
    /// Draws all ottava brackets (8va/8vb/15ma/15mb with dashed line and end hook).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-bracket.cc Ottava_bracket::print()
    /// LILYPOND-REF: scm/define-grobs.scm:2445-2468 OttavaBracket grob defaults
    ///
    /// An ottava bracket consists of:
    /// - Bold italic text at the start ("8va", "8vb", etc.)
    /// - Dashed line extending from text to end
    /// - Vertical hook at the end (edge-height = 0.8)
    ///
    /// dash-fraction: 0.3, staff-padding: 2.0, font-series: bold, font-shape: italic
    /// </remarks>
    private void DrawOttavaBrackets(ScoreLayout layout)
    {
        if (layout.OttavaBracketLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        double lineThickness = StaffLineThickness;
        double textFontSize = FontSize * 0.45;

        foreach (var bracket in layout.OttavaBracketLayouts)
        {
            double systemY = measureToSystemY.TryGetValue(bracket.StartMeasureIndex, out var y) ? y : 0;
            double absoluteY = systemY + bracket.Y;

            // Draw the text label (bold italic)
            // LILYPOND-REF: scm/define-grobs.scm:2453 (font-series . bold)
            // LILYPOND-REF: scm/define-grobs.scm:2454 (font-shape . italic)
            _svg.AppendLine($"  <text x=\"{bracket.StartX:F2}\" y=\"{absoluteY:F2}\" " +
                $"font-family=\"serif\" font-size=\"{textFontSize:F1}\" " +
                $"font-style=\"italic\" font-weight=\"bold\" fill=\"black\" " +
                $"class=\"ottava-bracket\" data-pos=\"{bracket.SourcePosition}\">" +
                $"{EscapeXml(bracket.Text)}</text>");

            // Calculate where the dashed line starts (after the text)
            double textWidth = bracket.Text.Length * 0.65;
            double lineStartX = bracket.StartX + textWidth + 0.5;

            // Draw dashed line from text to end
            if (lineStartX < bracket.EndX)
            {
                double dashLength = bracket.DashPeriod * bracket.DashFraction;
                double gapLength = bracket.DashPeriod * (1 - bracket.DashFraction);
                _svg.AppendLine($"  <line x1=\"{lineStartX:F2}\" y1=\"{absoluteY:F2}\" " +
                    $"x2=\"{bracket.EndX:F2}\" y2=\"{absoluteY:F2}\" " +
                    $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                    $"stroke-dasharray=\"{dashLength:F2} {gapLength:F2}\" " +
                    $"class=\"ottava-bracket-line\" data-pos=\"{bracket.SourcePosition}\" />");
            }

            // Draw end hook (vertical line at the right end)
            // LILYPOND-REF: scm/define-grobs.scm:2451 (edge-height . (0 . 0.8))
            // Hook direction: towards the staff (down for above, up for below)
            double hookDir = bracket.IsAbove ? 1 : -1;
            double hookEndY = absoluteY + bracket.EdgeHeight * hookDir;
            _svg.AppendLine($"  <line x1=\"{bracket.EndX:F2}\" y1=\"{absoluteY:F2}\" " +
                $"x2=\"{bracket.EndX:F2}\" y2=\"{hookEndY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                $"class=\"ottava-bracket-hook\" data-pos=\"{bracket.SourcePosition}\" />");
        }
    }

    /// <summary>
    /// Draws glissando lines between notes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/glissando-engraver.cc, scm/define-grobs.scm:1557-1577
    /// Renders a straight line connecting two notes of different pitch.
    /// </remarks>
    private void DrawGlissandos(ScoreLayout layout)
    {
        if (layout.GlissandoLayouts.IsDefaultOrEmpty)
            return;

        double lineThickness = StaffLineThickness;

        foreach (var gliss in layout.GlissandoLayouts)
        {
            // LILYPOND-REF: scm/define-grobs.scm:1575 (style . line)
            _svg.AppendLine($"  <line x1=\"{gliss.StartX:F2}\" y1=\"{gliss.StartY:F2}\" " +
                $"x2=\"{gliss.EndX:F2}\" y2=\"{gliss.EndY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                $"class=\"glissando\" data-pos=\"{gliss.SourcePosition}\" />");
        }
    }

    /// <summary>
    /// Draws arpeggio wavy lines next to chords.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
    /// Renders a vertical wavy line (tiled squiggle pattern) to the left of a chord.
    /// </remarks>
    private void DrawArpeggios(ScoreLayout layout)
    {
        if (layout.ArpeggioLayouts.IsDefaultOrEmpty)
            return;

        double lineThickness = StaffLineThickness;

        foreach (var arp in layout.ArpeggioLayouts)
        {
            double height = arp.BottomY - arp.TopY;
            if (height < 0.3)
                continue;

            // Generate a wavy line path (sinusoidal approximation of the scripts.arpeggio glyph)
            // LILYPOND-REF: lily/arpeggio.cc:133-145 - squiggle tiling
            double waveWidth = 0.35;  // horizontal amplitude
            double wavePeriod = 0.6;  // vertical period of each squiggle
            int waves = Math.Max(1, (int)(height / wavePeriod));
            double actualPeriod = height / waves;
            double halfPeriod = actualPeriod / 2;

            var path = new System.Text.StringBuilder();
            path.Append($"M {arp.X:F2} {arp.TopY:F2}");

            double y = arp.TopY;
            for (int i = 0; i < waves; i++)
            {
                double cy1 = y + halfPeriod * 0.3;
                double cy2 = y + halfPeriod * 0.7;
                double midY = y + halfPeriod;
                path.Append($" C {(arp.X + waveWidth):F2} {cy1:F2} {(arp.X + waveWidth):F2} {cy2:F2} {arp.X:F2} {midY:F2}");

                double cy3 = midY + halfPeriod * 0.3;
                double cy4 = midY + halfPeriod * 0.7;
                double endY = y + actualPeriod;
                path.Append($" C {(arp.X - waveWidth):F2} {cy3:F2} {(arp.X - waveWidth):F2} {cy4:F2} {arp.X:F2} {endY:F2}");

                y = endY;
            }

            _svg.AppendLine($"  <path d=\"{path}\" " +
                $"fill=\"none\" stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                $"class=\"arpeggio\" data-pos=\"{arp.SourcePosition}\" />");
        }
    }

    /// <summary>
    /// Draws piano pedal bracket lines (horizontal line with end hook).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/piano-pedal-bracket.cc - bracket rendering
    /// LILYPOND-REF: scm/define-grobs.scm:2586-2605 PianoPedalBracket grob
    ///
    /// Renders a horizontal line from pedal-on to pedal-off position,
    /// with a vertical hook (edge-height) at the release point.
    /// </remarks>
    private void DrawPedalBrackets(ScoreLayout layout)
    {
        if (layout.PedalBracketLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        double lineThickness = StaffLineThickness;

        foreach (var bracket in layout.PedalBracketLayouts)
        {
            // Use the first system Y as the baseline (bracket Y is relative)
            double systemY = 0;
            foreach (var system in layout.Systems)
            {
                foreach (var measure in system.Measures)
                {
                    if (measure.MeasureIndex >= 0)
                    {
                        systemY = system.Y;
                        goto foundSystem;
                    }
                }
            }
            foundSystem:

            double absoluteY = systemY + bracket.Y;

            // Draw horizontal bracket line
            _svg.AppendLine($"  <line x1=\"{bracket.StartX:F2}\" y1=\"{absoluteY:F2}\" " +
                $"x2=\"{bracket.EndX:F2}\" y2=\"{absoluteY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                $"class=\"pedal-bracket\" data-pos=\"{bracket.SourcePosition}\" />");

            // Draw end hook (vertical line at release point)
            double hookTopY = absoluteY - bracket.EdgeHeight;
            _svg.AppendLine($"  <line x1=\"{bracket.EndX:F2}\" y1=\"{hookTopY:F2}\" " +
                $"x2=\"{bracket.EndX:F2}\" y2=\"{absoluteY:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{lineThickness:F3}\" " +
                $"class=\"pedal-bracket-hook\" />");
        }
    }

    /// <summary>
    /// Draws figured bass numbers below the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc - create_grobs/print
    /// LILYPOND-REF: scm/define-grobs.scm:362-380 BassFigure defaults
    ///
    /// Figures are rendered as stacked numbers below the bass note,
    /// with the highest figure number at the top.
    /// </remarks>
    private void DrawFiguredBass(ScoreLayout layout)
    {
        if (layout.FiguredBassLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // LILYPOND-REF: scm/define-grobs.scm:362 BassFigure font-size
        double figureFontSize = FontSize * 0.75;
        double figureSpacing = 1.5;  // Vertical spacing between stacked figures

        foreach (var fb in layout.FiguredBassLayouts)
        {
            if (!measureToSystemY.TryGetValue(fb.MeasureIndex, out double systemY))
                continue;

            double x = fb.X;
            double baseY = systemY + fb.Y;

            for (int i = 0; i < fb.FigureTexts.Length; i++)
            {
                double y = baseY + i * figureSpacing;
                string text = fb.FigureTexts[i];

                _svg.AppendLine($"  <text x=\"{x:F2}\" y=\"{y:F2}\" " +
                    $"font-size=\"{figureFontSize:F2}\" " +
                    $"text-anchor=\"middle\" " +
                    $"font-family=\"serif\" " +
                    $"class=\"figured-bass\" " +
                    $"data-pos=\"{fb.SourcePosition}\">{EscapeXml(text)}</text>");
            }
        }
    }

    /// <summary>
    /// Draws chord name symbols above the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm - ChordName: font-family=sans, font-size=1.5
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm - chord name formatting
    /// </remarks>
    private void DrawChordNames(ScoreLayout layout)
    {
        if (layout.ChordNameLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // LILYPOND-REF: scm/define-grobs.scm - ChordName font-size=1.5 (relative)
        double chordFontSize = FontSize * 0.85;

        foreach (var chord in layout.ChordNameLayouts)
        {
            if (!measureToSystemY.TryGetValue(chord.MeasureIndex, out double systemY))
                continue;

            double x = chord.X;
            double y = systemY + chord.Y;

            _svg.AppendLine($"  <text x=\"{x:F2}\" y=\"{y:F2}\" " +
                $"font-size=\"{chordFontSize:F2}\" " +
                $"text-anchor=\"middle\" " +
                $"font-family=\"sans-serif\" " +
                $"font-weight=\"bold\" " +
                $"class=\"chord-name\" " +
                $"data-pos=\"{chord.SourcePosition}\">{EscapeXml(chord.ChordText)}</text>");
        }
    }

    /// <summary>
    /// Draws percent repeat symbols (diagonal slash with two dots).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/percent-repeat-interface.cc - x_percent() rendering
    /// LILYPOND-REF: lily/lookup.cc:513-532 - repeat_slash() SVG path
    /// LILYPOND-REF: scm/define-grobs.scm:2520-2539 - PercentRepeat properties
    ///   slope=1.0, thickness=0.48, dot-negative-kern=0.75
    /// </remarks>
    private void DrawPercentRepeats(ScoreLayout layout)
    {
        if (layout.PercentRepeatLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        // LILYPOND-REF: scm/define-grobs.scm:2530 - slope=1.0, thickness=0.48
        double slope = 1.0;
        double thickness = 0.48;
        double dotOffset = 1.0;  // Offset for dots from center
        double slashWidth = 2.0;
        double slashHeight = slashWidth * slope;
        double dotRadius = 0.25;

        foreach (var pr in layout.PercentRepeatLayouts)
        {
            if (!measureToSystemY.TryGetValue(pr.MeasureIndex, out double systemY))
                continue;

            double cx = pr.X;
            double cy = systemY + pr.Y;

            // Draw diagonal slash (parallelogram path)
            double halfW = slashWidth / 2;
            double halfH = slashHeight / 2;
            double halfT = thickness / 2;

            // Slash from bottom-left to top-right
            double x1 = cx - halfW;
            double y1 = cy + halfH;
            double x2 = cx + halfW;
            double y2 = cy - halfH;

            _svg.AppendLine($"  <line x1=\"{x1:F2}\" y1=\"{y1:F2}\" " +
                $"x2=\"{x2:F2}\" y2=\"{y2:F2}\" " +
                $"stroke=\"black\" stroke-width=\"{thickness:F2}\" " +
                $"stroke-linecap=\"round\" " +
                $"class=\"percent-repeat\" " +
                $"data-pos=\"{pr.SourcePosition}\"/>");

            // Upper dot (above and right of center)
            double dotUpX = cx + dotOffset * 0.3;
            double dotUpY = cy - dotOffset;
            _svg.AppendLine($"  <circle cx=\"{dotUpX:F2}\" cy=\"{dotUpY:F2}\" " +
                $"r=\"{dotRadius:F2}\" fill=\"black\" class=\"percent-dot\"/>");

            // Lower dot (below and left of center)
            double dotDownX = cx - dotOffset * 0.3;
            double dotDownY = cy + dotOffset;
            _svg.AppendLine($"  <circle cx=\"{dotDownX:F2}\" cy=\"{dotDownY:F2}\" " +
                $"r=\"{dotRadius:F2}\" fill=\"black\" class=\"percent-dot\"/>");
        }
    }

    /// <summary>
    /// Draws part combination text annotations ("a2", "Solo", "Solo II").
    /// LILYPOND-REF: scm/part-combiner.scm - CombineTextScript rendering
    /// </summary>
    private void DrawPartCombine(ScoreLayout layout)
    {
        if (layout.PartCombineLayouts.IsDefaultOrEmpty)
            return;

        // Build measure to system Y mapping
        var measureToSystemY = new Dictionary<int, double>();
        foreach (var system in layout.Systems)
        {
            foreach (var measure in system.Measures)
            {
                measureToSystemY[measure.MeasureIndex] = system.Y;
            }
        }

        double textFontSize = FontSize * 0.65;

        foreach (var pc in layout.PartCombineLayouts)
        {
            double systemY = measureToSystemY.TryGetValue(pc.MeasureIndex, out var y) ? y : 0;
            double absoluteY = systemY + pc.Y;

            _svg.AppendLine($"  <text x=\"{pc.X:F2}\" y=\"{absoluteY:F2}\" " +
                $"font-family=\"serif\" font-size=\"{textFontSize:F1}\" " +
                $"font-style=\"italic\" " +
                $"text-anchor=\"start\" dominant-baseline=\"auto\" " +
                $"class=\"part-combine\">{EscapeXml(pc.Text)}</text>");
        }
    }
}



















