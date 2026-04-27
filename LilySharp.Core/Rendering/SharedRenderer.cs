// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Rendering;

/// <summary>
/// Backend-agnostic music renderer covering the basic engraving primitives:
/// staff lines, clefs, time/key signatures, noteheads, stems, beams, ledger
/// lines, augmentation dots, and rests. Drives any
/// <see cref="IDocumentContext"/> implementation; the same call produces
/// SVG via <see cref="Rendering.Svg.SvgDocumentContext"/> or PDF via
/// <see cref="Rendering.Pdf.PdfDocumentContext"/>.
/// </summary>
/// <remarks>
/// This is the Phase 2-A renderer — feature-incomplete by design. It does
/// not yet draw ties, slurs, dynamics, articulations, lyrics, accidentals,
/// or any of the higher-level engraving artefacts that the full
/// <c>SvgRenderer</c> handles. Those will be migrated in subsequent
/// phases as <c>SvgRenderer</c> is incrementally ported to use
/// <see cref="IDrawingContext"/>.
/// </remarks>
public static class SharedRenderer
{
    private const double StaffHeight = 4.0;
    private const double FontSize = 4.0;
    private const double OssiaScale = 0.65;  // LP magnifyStaff default for ossia

    public static void RenderTo(
        MultiStaffScore score, ScoreLayout layout, IDocumentContext doc)
    {
        var options = layout.Options;
        foreach (var page in layout.Pages)
        {
            var gc = doc.BeginPage(page.Width, page.Height);
            // LILYPOND-REF: lily/page-layout-problem.cc:434 — header at MarginTop;
            // SystemLayout.Y already includes MarginTop, so apply MarginLeft only.
            DrawHeader(score, page, options, gc);
            var marginScope = options.MarginLeft != 0
                ? gc.BeginGroup(DrawingTransform.Translate(options.MarginLeft, 0))
                : null;
            try
            {
                foreach (var system in page.Systems)
                    DrawSystem(score, layout, system, gc);
                // Page-level overlays that span systems
                var measureToSystemY = BuildMeasureToSystemY(layout);
                DrawTies(layout, gc);
                DrawSlurs(layout, gc);
                DrawDynamics(layout, measureToSystemY, gc);
                DrawArticulations(layout, measureToSystemY, gc);
                DrawLyrics(layout, measureToSystemY, gc);
                DrawHairpins(layout, measureToSystemY, gc);
                DrawOttavaBrackets(layout, measureToSystemY, gc);
                DrawVoltaBrackets(layout, measureToSystemY, gc);
                DrawTupletBrackets(layout, measureToSystemY, gc);
                DrawTrillSpanners(layout, measureToSystemY, gc);
                DrawGlissandos(layout, gc);
                DrawArpeggios(layout, gc);
                DrawGraceNotes(layout, measureToSystemY, gc);
                DrawChordNames(layout, measureToSystemY, gc);
                DrawFiguredBass(layout, measureToSystemY, gc);
                DrawPercentRepeats(layout, measureToSystemY, gc);
                DrawBarNumbers(layout, gc);
                DrawStanzaNumbers(layout, gc);
                DrawFingerings(layout, measureToSystemY, gc);
                DrawMusicMarks(layout, measureToSystemY, gc);
                DrawCustomTexts(layout, measureToSystemY, gc);
                DrawTextSpanners(layout, measureToSystemY, gc);
                DrawPedalBrackets(layout, measureToSystemY, gc);
                DrawMultiMeasureRests(layout, gc);
                DrawTieVariants(layout, measureToSystemY, gc);
                DrawLyricHyphens(layout, measureToSystemY, gc);
                DrawPartCombine(layout, measureToSystemY, gc);
            }
            finally
            {
                marginScope?.Dispose();
            }
            doc.EndPage();
        }
    }

    // ---------- Header ----------

    // LILYPOND-REF: ly/titling-init.ly:79-108 — \huge \larger \larger \bold ≈ 3.49 ss
    // LILYPOND-REF: ly/titling-init.ly:100 — composer baseline ≈ 2.2 ss
    private const double TitleFontSize = 3.49;
    private const double ComposerFontSize = 2.2;

    private static void DrawHeader(
        MultiStaffScore score, PageLayout page, LayoutOptions options, IDrawingContext gc)
    {
        double y = options.MarginTop;
        if (score.Title is { } title)
        {
            double centerX = page.Width / 2;
            gc.DrawText(title, centerX, y, TitleFontSize, "serif",
                FontStyle.Bold, TextAnchor.Middle);
            y += TitleFontSize;
        }
        if (score.Composer is { } composer)
        {
            double rightX = page.Width - options.MarginLeft;
            gc.DrawText(composer, rightX, y, ComposerFontSize, "serif",
                FontStyle.Italic, TextAnchor.End);
        }
    }

    // ---------- System ----------

    private static void DrawSystem(
        MultiStaffScore score, ScoreLayout layout,
        SystemLayout system, IDrawingContext gc)
    {
        bool isFirstSystem = system.SystemIndex == 0;
        double systemStartX = system.Indent;

        // System-start delimiters (brackets / bar lines connecting staves in a group).
        DrawSystemStartDelimiters(system, gc);

        // Per-staff: staff lines + prefix glyphs + notes
        foreach (var (group, staff, globalIdx) in score.EnumerateStaves())
        {
            double staffY = LayoutUtilities.FindStaffYInSystem(system, globalIdx);
            bool isOssia = staff.IsOssia;

            IDisposable? groupScope = isOssia
                ? gc.BeginGroup(new DrawingTransform(0, staffY, OssiaScale, OssiaScale))
                : null;
            try
            {
                double localStaffY = isOssia ? 0 : staffY;
                DrawStaffLines(localStaffY, system.Width, gc);

                // System-start prefix (clef, key, time) only on first system
                double prefixEndX = systemStartX;
                var clef = ResolveClef(staff, system, score);
                prefixEndX = DrawClef(clef, systemStartX, localStaffY, gc);
                if (isFirstSystem)
                {
                    prefixEndX = DrawKeySignature(score.KeySignature, clef, prefixEndX, localStaffY, gc);
                    prefixEndX = DrawTimeSignature(score.TimeSignature, prefixEndX, localStaffY, gc);
                }

                // Notes per measure
                DrawStaffMeasures(staff, system, layout, localStaffY, clef, gc);

                // Barlines at end of each measure (single thin only in this phase)
                DrawBarlines(system, localStaffY, gc);
            }
            finally
            {
                groupScope?.Dispose();
            }
        }

        // Beams (use system-wide coordinates; ossia beams are rare and
        // outside the Phase 2-A scope so we draw at full scale)
        DrawBeams(layout, system, gc);
    }

    private static void DrawStaffLines(double staffY, double width, IDrawingContext gc)
    {
        for (int i = 0; i < 5; i++)
        {
            double y = staffY + i;
            gc.DrawLine(0, y, width, y, Color.Black, EngravingDefaults.StaffLineThickness);
        }
    }

    // ---------- Clef ----------

    private static ClefType ResolveClef(Staff staff, SystemLayout system, MultiStaffScore score)
    {
        // For Phase 2-A we just take the staff's notated clef. Mid-score
        // clef changes are not yet supported.
        return staff.Clef;
    }

    private static double DrawClef(ClefType clef, double x, double staffY, IDrawingContext gc)
    {
        char glyph = clef switch
        {
            ClefType.Bass => EmmentalerGlyphs.FClef,
            ClefType.Alto => EmmentalerGlyphs.CClef,
            ClefType.Tenor => EmmentalerGlyphs.CClef,
            _ => EmmentalerGlyphs.GClef,
        };
        // Y baseline matches LP positioning (treble: G line, bass: F line, etc.)
        double clefY = clef switch
        {
            ClefType.Bass => staffY + 1,
            ClefType.Alto => staffY + 2,
            ClefType.Tenor => staffY + 1,
            _ => staffY + 3,
        };
        gc.DrawGlyph(glyph, x + 0.3, clefY, FontSize);
        return x + 0.3 + 3.0;  // approximate clef width + padding
    }

    // ---------- Time signature ----------

    private static double DrawTimeSignature(TimeSignature ts, double x, double staffY, IDrawingContext gc)
    {
        if (ts.Beats == 4 && ts.BeatType == 4)
        {
            gc.DrawGlyph(EmmentalerGlyphs.TimeSigCommon, x, staffY + 2, FontSize);
            return x + 2.0;
        }
        if (ts.Beats == 2 && ts.BeatType == 2)
        {
            gc.DrawGlyph(EmmentalerGlyphs.TimeSigCutCommon, x, staffY + 2, FontSize);
            return x + 2.0;
        }
        // Stack numerator over denominator
        var num = ts.Beats.ToString();
        var den = ts.BeatType.ToString();
        double dx = 0;
        for (int i = 0; i < Math.Max(num.Length, den.Length); i++)
        {
            if (i < num.Length)
                gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(num[i] - '0'),
                    x + dx, staffY + 1, FontSize);
            if (i < den.Length)
                gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(den[i] - '0'),
                    x + dx, staffY + 3, FontSize);
            dx += 1.4;
        }
        return x + dx + 0.4;
    }

    // ---------- Key signature ----------

    private static double DrawKeySignature(
        KeySignature key, ClefType clef, double x, double staffY, IDrawingContext gc)
    {
        if (key.Sharps == 0) return x;

        // LP key signature accidental positions (staff positions from bottom line, going up)
        // Treble clef positions for sharps (FCGDAEB) and flats (BEADGCF)
        int[] sharpPositions = { 8, 5, 9, 6, 3, 7, 4 };  // F G A B C D E (relative)
        int[] flatPositions = { 4, 7, 3, 6, 2, 5, 1 };

        // Map by clef (rough approximation; full LP rules in lily/key-signature-interface.cc)
        int clefShift = clef switch
        {
            ClefType.Bass => -2,    // F clef shifts positions
            ClefType.Alto => -1,
            ClefType.Tenor => 1,
            _ => 0,
        };

        char glyph = key.Sharps > 0
            ? EmmentalerGlyphs.AccidentalSharp
            : EmmentalerGlyphs.AccidentalFlat;
        var positions = key.Sharps > 0 ? sharpPositions : flatPositions;
        int n = Math.Min(Math.Abs(key.Sharps), 7);
        double dx = 0;
        for (int i = 0; i < n; i++)
        {
            int pos = positions[i] + clefShift;
            // pos is 1-based from bottom line (1 = bottom line, 9 = top space)
            // staffY is top of staff; bottom line is staffY + 4.
            double y = staffY + 4 - (pos - 1) * 0.5;
            gc.DrawGlyph(glyph, x + dx, y, FontSize);
            dx += 0.7;
        }
        return x + dx + 0.4;
    }

    // ---------- Notes & rests per staff ----------

    private static void DrawStaffMeasures(
        Staff staff, SystemLayout system, ScoreLayout layout,
        double staffY, ClefType clef, IDrawingContext gc)
    {
        double staffMiddleY = staffY + StaffHeight / 2;
        var voice = staff.PrimaryVoice;

        foreach (var ml in system.Measures)
        {
            if (ml.MeasureIndex >= voice.Measures.Length)
                continue;

            var measure = voice.Measures[ml.MeasureIndex];
            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                var item = measure.Items[itemIdx];
                if (itemIdx >= ml.Items.Length) continue;
                var il = ml.Items[itemIdx];
                double itemX = ml.X + il.X;

                switch (item)
                {
                    case NoteItem note:
                        DrawNote(note, itemX, staffMiddleY, gc);
                        break;
                    case RestItem rest:
                        DrawRest(rest, itemX, staffY, gc);
                        break;
                    case ChordItem chord:
                        DrawChord(chord, itemX, staffMiddleY, gc);
                        break;
                    case ClefChangeItem clefChange:
                        DrawClefChange(clefChange, itemX, staffY, gc);
                        break;
                    case KeySignatureChangeItem keyChange:
                        DrawKeySignatureChange(keyChange, itemX, staffY, gc);
                        break;
                }
            }
        }
    }

    private static void DrawNote(NoteItem note, double x, double staffMiddleY, IDrawingContext gc)
    {
        int noteValue = note.BaseDuration.Denominator;
        if (note.BaseDuration.Numerator != 1) noteValue = 1;
        double noteY = staffMiddleY - note.StaffPosition * 0.5;
        // Cue notes scale to ~0.66× (LP CueVoice fontSize = -4 → magstep(-4)).
        // LILYPOND-REF: ly/engraver-init.ly CueVoice — fontSize = #-4
        double noteFontSize = note.IsCue ? FontSize * 0.66 : FontSize;

        // Accidental (left of notehead)
        if (note.Accidental != null)
            DrawAccidental(note.Accidental, note.IsCourtesy, x, noteY, note.SourcePosition, gc);

        // Notehead
        char head = EmmentalerGlyphs.GetNotehead(noteValue);
        using (gc.Source(note.SourcePosition))
            gc.DrawGlyph(head, x, noteY, noteFontSize);

        // Ledger lines for notes far from middle line
        DrawLedgerLines(note.StaffPosition, x, staffMiddleY, gc);

        // Stem (no stem for whole notes)
        if (noteValue >= 2)
        {
            double stemX = note.StemUp
                ? x + EngravingDefaults.StemUpAttachX
                : x + EngravingDefaults.StemDownAttachX;
            double stemEndY = note.StemUp
                ? noteY - EngravingRules.StandardStemLength
                : noteY + EngravingRules.StandardStemLength;
            gc.DrawLine(stemX, noteY, stemX, stemEndY, Color.Black, EngravingDefaults.StemThickness);

            // Flag (only for unbeamed eighth+ notes; beamed notes handled in DrawBeams)
            bool hasFlag = false;
            if (noteValue >= 8)
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, note.StemUp);
                if (flag.HasValue)
                {
                    gc.DrawGlyph(flag.Value, stemX, stemEndY, noteFontSize);
                    hasFlag = true;
                }
            }

            // Tremolo slashes on the stem
            if (note.HasTremolo)
                DrawTremolo(stemX, noteY, stemEndY, note.StemUp, note.TremoloBeams, hasFlag, gc);
        }

        // Augmentation dots
        for (int d = 0; d < note.Dots; d++)
        {
            double dotX = x + 1.0 + d * 0.5;
            double dotY = note.StaffPosition % 2 == 0 ? noteY - 0.5 : noteY;
            gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot, dotX, dotY, noteFontSize);
        }
    }

    private static void DrawChord(ChordItem chord, double x, double staffMiddleY, IDrawingContext gc)
    {
        int noteValue = chord.BaseDuration.Denominator;
        if (chord.BaseDuration.Numerator != 1) noteValue = 1;
        char head = EmmentalerGlyphs.GetNotehead(noteValue);

        double topY = double.MaxValue, bottomY = double.MinValue;
        foreach (var n in chord.Notes)
        {
            double y = staffMiddleY - n.StaffPosition * 0.5;
            if (n.Accidental != null)
                DrawAccidental(n.Accidental, isCourtesy: false, x, y, chord.SourcePosition, gc);
            using (gc.Source(chord.SourcePosition))
                gc.DrawGlyph(head, x, y, FontSize);
            DrawLedgerLines(n.StaffPosition, x, staffMiddleY, gc);
            if (y < topY) topY = y;
            if (y > bottomY) bottomY = y;
        }

        if (noteValue >= 2 && chord.Notes.Length > 0)
        {
            double stemX = chord.StemUp
                ? x + EngravingDefaults.StemUpAttachX
                : x + EngravingDefaults.StemDownAttachX;
            double stemStartY = chord.StemUp ? bottomY : topY;
            double stemEndY = chord.StemUp
                ? topY - EngravingRules.StandardStemLength
                : bottomY + EngravingRules.StandardStemLength;
            gc.DrawLine(stemX, stemStartY, stemX, stemEndY,
                Color.Black, EngravingDefaults.StemThickness);
        }
    }

    private static void DrawLedgerLines(int staffPosition, double x, double staffMiddleY, IDrawingContext gc)
    {
        double ext = EngravingDefaults.LegerLineExtension;
        double thickness = EngravingDefaults.LegerLineThickness;
        // Notehead width approx 1.18 sp; centered around x
        double x1 = x - ext;
        double x2 = x + EngravingDefaults.NoteheadBlackWidth + ext;

        // Ledger lines above staff (staff position > 4 = above top line)
        for (int pos = 6; pos <= staffPosition; pos += 2)
        {
            double y = staffMiddleY - pos * 0.5;
            gc.DrawLine(x1, y, x2, y, Color.Black, thickness);
        }
        // Ledger lines below staff (staff position < -4 = below bottom line)
        for (int pos = -6; pos >= staffPosition; pos -= 2)
        {
            double y = staffMiddleY - pos * 0.5;
            gc.DrawLine(x1, y, x2, y, Color.Black, thickness);
        }
    }

    private static void DrawRest(RestItem rest, double x, double staffY, IDrawingContext gc)
    {
        int noteValue = rest.BaseDuration.Denominator;
        if (rest.BaseDuration.Numerator != 1) noteValue = 1;
        char glyph = EmmentalerGlyphs.GetRest(noteValue);
        double y = noteValue == 1 ? staffY + 1 : staffY + 2;  // whole rests hang from 4th line
        using (gc.Source(rest.SourcePosition))
            gc.DrawGlyph(glyph, x, y, FontSize);
    }

    // ---------- Barlines ----------

    private static void DrawBarlines(SystemLayout system, double staffY, IDrawingContext gc)
    {
        double thinW = EngravingDefaults.ThinBarlineThickness;
        foreach (var ml in system.Measures)
        {
            double barX = ml.X + ml.Width;
            gc.DrawRectangle(barX, staffY, thinW, StaffHeight, fill: Color.Black);
        }
    }

    // ---------- Beams ----------

    private static void DrawBeams(ScoreLayout layout, SystemLayout system, IDrawingContext gc)
    {
        double staffMiddleY = system.Y + StaffHeight / 2;
        foreach (var beam in layout.BeamLayouts)
        {
            // Only draw beams whose first measure is in this system
            bool inSystem = system.Measures.Any(m => m.MeasureIndex == beam.Group.MeasureIndex);
            if (!inSystem) continue;

            var grp = beam.Group;
            double leftBeamY = staffMiddleY - beam.LeftY * 0.5;
            double rightBeamY = staffMiddleY - beam.RightY * 0.5;
            double leftStemX = beam.MemberXPositions[0]
                + (grp.StemUp ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);
            double rightStemX = beam.MemberXPositions[^1]
                + (grp.StemUp ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);

            // Primary beam — drawn as a thick filled rectangle (sloped by polygon)
            DrawBeamSegment(leftStemX, leftBeamY, rightStemX, rightBeamY, gc);

            // Secondary beams (16th+)
            int maxBeamCount = grp.Members.Max(m => m.BeamCount);
            for (int level = 1; level < maxBeamCount; level++)
            {
                double offset = level * EngravingDefaults.BeamTranslation;
                if (!grp.StemUp) offset = -offset;
                double beamSpanX = rightStemX - leftStemX;

                for (int i = 0; i < grp.Members.Length - 1; i++)
                {
                    if (grp.Members[i].BeamCount > level && grp.Members[i + 1].BeamCount > level)
                    {
                        double xa = beam.MemberXPositions[i]
                            + (grp.StemUp ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);
                        double xb = beam.MemberXPositions[i + 1]
                            + (grp.StemUp ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);
                        double ta = beamSpanX > 0.001 ? (xa - leftStemX) / beamSpanX : 0;
                        double tb = beamSpanX > 0.001 ? (xb - leftStemX) / beamSpanX : 0;
                        double ya = leftBeamY + offset + ta * (rightBeamY - leftBeamY);
                        double yb = leftBeamY + offset + tb * (rightBeamY - leftBeamY);
                        DrawBeamSegment(xa, ya, xb, yb, gc);
                    }
                }
            }

            // Stems for beam members (replace any individual stems)
            double slope = (rightStemX - leftStemX) > 0.001
                ? (rightBeamY - leftBeamY) / (rightStemX - leftStemX) : 0;
            for (int i = 0; i < grp.Members.Length; i++)
            {
                var member = grp.Members[i];
                double mx = beam.MemberXPositions[i];
                double stemX = mx + (grp.StemUp
                    ? EngravingDefaults.StemUpAttachX
                    : EngravingDefaults.StemDownAttachX);
                double beamY = leftBeamY + slope * (stemX - leftStemX);
                double headY = staffMiddleY - GetMemberStaffPosition(member) * 0.5;
                gc.DrawLine(stemX, headY, stemX, beamY,
                    Color.Black, EngravingDefaults.StemThickness);
            }
        }
    }

    private static int GetMemberStaffPosition(BeamMember m) => m.Item switch
    {
        NoteItem n => n.StaffPosition,
        ChordItem c => c.StemUp
            ? c.Notes.Min(x => x.StaffPosition)
            : c.Notes.Max(x => x.StaffPosition),
        _ => 0,
    };

    private static void DrawBeamSegment(double x1, double y1, double x2, double y2, IDrawingContext gc)
    {
        // Sloped beam as a filled polygon would be ideal; simple thick line is a
        // good Phase 2-A approximation (LP uses precise quad polygons).
        gc.DrawLine(x1, y1, x2, y2, Color.Black, EngravingDefaults.BeamThickness);
    }

    // ---------- Accidentals ----------

    private static void DrawAccidental(
        string accidentalKind, bool isCourtesy, double noteheadX, double noteheadY,
        int sourcePosition, IDrawingContext gc)
    {
        char glyph = accidentalKind switch
        {
            "doubleSharp" => EmmentalerGlyphs.AccidentalDoubleSharp,
            "sharp" => EmmentalerGlyphs.AccidentalSharp,
            "flat" => EmmentalerGlyphs.AccidentalFlat,
            "doubleFlat" => EmmentalerGlyphs.AccidentalDoubleFlat,
            _ => EmmentalerGlyphs.AccidentalNatural,
        };
        var accBBox = GlyphMetrics.GetAccidentalBBox(accidentalKind);
        double accWidth = accBBox.Width;
        double gap = GlyphMetrics.AccidentalNoteGap;

        if (isCourtesy)
        {
            // LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize()
            double parenWidth = GlyphMetrics.AccidentalParenWidth;
            double total = parenWidth + accWidth + parenWidth;
            double startX = noteheadX - total - gap;
            using (gc.Source(sourcePosition))
            {
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalLeftParen, startX, noteheadY, FontSize);
                gc.DrawGlyph(glyph, startX + parenWidth, noteheadY, FontSize);
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalRightParen,
                    startX + parenWidth + accWidth, noteheadY, FontSize);
            }
        }
        else
        {
            using (gc.Source(sourcePosition))
                gc.DrawGlyph(glyph, noteheadX - accWidth - gap, noteheadY, FontSize);
        }
    }

    // ---------- Ties & slurs ----------

    private static void DrawTies(ScoreLayout layout, IDrawingContext gc)
    {
        foreach (var tie in layout.TieLayouts)
            DrawCurve(
                tie.StartX, tie.StartY, tie.EndX, tie.EndY,
                tie.Control1, tie.Control2, tie.CurveUp,
                EngravingDefaults.TieMidThickness, gc);
    }

    private static void DrawSlurs(ScoreLayout layout, IDrawingContext gc)
    {
        foreach (var slur in layout.SlurLayouts)
            DrawCurve(
                slur.StartX, slur.StartY, slur.EndX, slur.EndY,
                slur.Control1, slur.Control2, slur.CurveUp,
                EngravingDefaults.SlurMidThickness, gc);
    }

    /// <summary>
    /// Draws a tapered cubic Bézier "bow" (used for both ties and slurs) by
    /// emitting an outer curve from <c>start → c1 c2 → end</c> and an inner
    /// curve back, offset toward the curve interior to create the LP-style
    /// thicker middle / pointed endpoints.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie.cc, lily/slur.cc — Bezier bow rendering
    /// </remarks>
    private static void DrawCurve(
        double startX, double startY, double endX, double endY,
        (double X, double Y) c1, (double X, double Y) c2,
        bool curveUp, double midThickness, IDrawingContext gc)
    {
        double direction = curveUp ? -1.0 : 1.0;
        var c1Back = (X: c1.X, Y: c1.Y + direction * midThickness * 0.9);
        var c2Back = (X: c2.X, Y: c2.Y + direction * midThickness * 0.9);
        gc.DrawClosedBezier(
            (startX, startY), c1, c2,
            (endX, endY), c2Back, c1Back,
            Color.Black);
    }

    // ---------- Helpers for system-Y lookup ----------

    private static Dictionary<int, double> BuildMeasureToSystemY(ScoreLayout layout)
    {
        var map = new Dictionary<int, double>();
        foreach (var system in layout.AllSystems)
            foreach (var ml in system.Measures)
                map[ml.MeasureIndex] = system.Y;
        return map;
    }

    // ---------- Dynamics ----------

    /// <summary>
    /// Draws dynamic markings ("p", "f", "mf", etc.) below the staff using
    /// serif bold-italic text (matching LP's DynamicText grob font).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1298-1327 DynamicText grob
    /// LILYPOND-REF: scm/define-grobs.scm:1311 self-alignment-X = CENTER
    /// </remarks>
    private static void DrawDynamics(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.DynamicLayouts.IsDefaultOrEmpty) return;
        double fontSize = FontSize * 0.5;
        foreach (var d in layout.DynamicLayouts)
        {
            string text = NormalizeDynamicText(d.Text);
            double y = (sysY.TryGetValue(d.MeasureIndex, out var sy) ? sy : 0) + d.Y;
            using (gc.Source(d.SourcePosition))
                gc.DrawText(text, d.X, y, fontSize, "serif",
                    FontStyle.BoldItalic, TextAnchor.Middle, Color.Black);
        }
    }

    private static string NormalizeDynamicText(string raw) => raw switch
    {
        "cresc" => "cresc.",
        "decresc" => "decresc.",
        "dim" => "dim.",
        _ => raw,
    };

    // ---------- Articulations ----------

    /// <summary>
    /// Draws articulation marks (staccato, accent, tenuto, fermata, etc.)
    /// using their precomputed Emmentaler glyphs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2268-2310 Script grob
    /// LILYPOND-REF: lily/script-engraver.cc:92-125 acknowledge_note_head
    /// </remarks>
    private static void DrawArticulations(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.ArticulationLayouts.IsDefaultOrEmpty) return;
        foreach (var a in layout.ArticulationLayouts)
        {
            if (string.IsNullOrEmpty(a.Glyph)) continue;
            double y = (sysY.TryGetValue(a.MeasureIndex, out var sy) ? sy : 0) + a.Y;
            using (gc.Source(a.SourcePosition))
                gc.DrawGlyph(a.Glyph[0], a.X, y, FontSize);
        }
    }

    // ---------- Lyrics ----------

    /// <summary>
    /// Draws lyric syllables (and any hyphen / extender connectors) below
    /// the staff using serif text at the LP-style reduced size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:32-52 LyricText grob
    /// LILYPOND-REF: scm/define-grobs.scm:3025 font-size = -1
    /// </remarks>
    private static void DrawLyrics(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.LyricLayouts.IsDefaultOrEmpty) return;
        double lyricFontSize = FontSize * 0.8;
        foreach (var l in layout.LyricLayouts)
        {
            double y = (sysY.TryGetValue(l.Item.MeasureIndex, out var sy) ? sy : 0) + l.Y;
            gc.DrawText(l.Item.Text, l.X, y, lyricFontSize, "serif",
                FontStyle.Regular, TextAnchor.Middle, Color.Black);
            if (l.DrawHyphen)
                gc.DrawText("-", l.HyphenX, y, lyricFontSize, "serif",
                    FontStyle.Regular, TextAnchor.Middle, Color.Black);
            if (l.DrawExtender)
                gc.DrawLine(l.X + l.Width / 2, y - 0.2, l.ExtenderEndX, y - 0.2,
                    Color.Black, 0.1);
        }
    }

    // ---------- Hairpins (cresc / dim wedges) ----------

    /// <summary>
    /// Draws crescendo/decrescendo wedges as a pair of straight lines that
    /// converge to a point (cresc) or open from a point (dim).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:110-358 print()
    /// LILYPOND-REF: scm/define-grobs.scm:1641-1666 Hairpin grob (thickness = 1.0)
    /// </remarks>
    private static void DrawHairpins(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.HairpinLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var h in layout.HairpinLayouts)
        {
            double absY = (sysY.TryGetValue(h.StartMeasureIndex, out var sy) ? sy : 0) + h.Y;
            double leftTop = absY - h.StartOpening;
            double leftBottom = absY + h.StartOpening;
            double rightTop = absY - h.EndOpening;
            double rightBottom = absY + h.EndOpening;
            using (gc.Source(h.SourcePosition))
            {
                gc.DrawLine(h.StartX, leftTop, h.EndX, rightTop, Color.Black, thickness);
                gc.DrawLine(h.StartX, leftBottom, h.EndX, rightBottom, Color.Black, thickness);
            }
        }
    }

    // ---------- Ottava brackets (8va / 8vb / 15ma) ----------

    /// <summary>
    /// Draws ottava brackets: serif italic-bold text label, dashed extension
    /// line, and a vertical hook on the closing end.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm OttavaBracket grob
    /// LILYPOND-REF: lily/ottava-bracket.cc — Ottava_bracket
    /// </remarks>
    private static void DrawOttavaBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.OttavaBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        double textFontSize = FontSize * 0.45;
        foreach (var b in layout.OttavaBracketLayouts)
        {
            double absY = (sysY.TryGetValue(b.StartMeasureIndex, out var sy) ? sy : 0) + b.Y;
            using (gc.Source(b.SourcePosition))
            {
                gc.DrawText(b.Text, b.StartX, absY, textFontSize, "serif",
                    FontStyle.BoldItalic, TextAnchor.Start, Color.Black);

                double textWidth = b.Text.Length * 0.65;
                double lineStartX = b.StartX + textWidth + 0.5;
                if (lineStartX < b.EndX)
                {
                    double dashOn = b.DashPeriod * b.DashFraction;
                    double dashOff = b.DashPeriod * (1 - b.DashFraction);
                    gc.DrawLine(lineStartX, absY, b.EndX, absY,
                        Color.Black, thickness, (dashOn, dashOff));
                }
                if (b.EdgeHeight > 0)
                {
                    double hookDir = b.IsAbove ? 1 : -1;
                    gc.DrawLine(b.EndX, absY, b.EndX, absY + b.EdgeHeight * hookDir,
                        Color.Black, thickness);
                }
            }
        }
    }

    // ---------- Volta brackets (1./2. endings) ----------

    /// <summary>
    /// Draws volta (repeat ending) brackets: optional left hook, horizontal
    /// line, optional right hook, and the volta-number text label.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-bracket.cc:1-170 Volta_bracket_interface
    /// LILYPOND-REF: scm/define-grobs.scm:4292-4317 VoltaBracket grob
    /// </remarks>
    private static void DrawVoltaBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.VoltaBracketLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.13;
        double edgeHeight = VoltaBracketEngraver.GetEdgeHeight();

        foreach (var v in layout.VoltaBracketLayouts)
        {
            double absY = (sysY.TryGetValue(v.StartMeasureIndex, out var sy) ? sy : 0) + v.Y;
            bool hasText = !string.IsNullOrEmpty(v.VoltaText);
            using (gc.Source(v.SourcePosition))
            {
                if (hasText)
                    gc.DrawLine(v.StartX, absY, v.StartX, absY + edgeHeight,
                        Color.Black, thickness);
                gc.DrawLine(v.StartX, absY, v.EndX, absY,
                    Color.Black, thickness);
                if (v.IsClosed)
                    gc.DrawLine(v.EndX, absY, v.EndX, absY + edgeHeight,
                        Color.Black, thickness);
                if (hasText)
                {
                    double textY = absY + 0.3 + 0.6;  // baseline below the bracket line
                    gc.DrawText(v.VoltaText, v.StartX + 0.5, textY,
                        FontSize * 0.6, "serif", FontStyle.Bold, TextAnchor.Start, Color.Black);
                }
            }
        }
    }

    // ---------- Tuplet brackets ----------

    /// <summary>
    /// Draws tuplet brackets: hook + sloped line (split around the number) +
    /// hook + centered number text. When all members are beamed the bracket
    /// is suppressed (number-only).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:200-350 print()
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket defaults
    /// </remarks>
    private static void DrawTupletBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.TupletBracketLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.13;
        double edgeHeight = TupletBracketEngraver.GetEdgeHeight();

        foreach (var b in layout.TupletBracketLayouts)
        {
            double sy = sysY.TryGetValue(b.MeasureIndex, out var s) ? s : 0;
            double startY = sy + b.StartY;
            double endY = sy + b.EndY;
            double midX = (b.StartX + b.EndX) / 2;
            double midY = (startY + endY) / 2;
            double hookDir = b.IsStemUp ? 1 : -1;

            using (gc.Source(b.SourcePosition))
            {
                if (b.ShowBracket)
                {
                    gc.DrawLine(b.StartX, startY, b.StartX, startY + edgeHeight * hookDir,
                        Color.Black, thickness);

                    const double numberGap = 1.0;
                    double totalWidth = b.EndX - b.StartX;
                    double leftFrac = totalWidth > 0 ? (midX - numberGap - b.StartX) / totalWidth : 0.5;
                    double rightFrac = totalWidth > 0 ? (midX + numberGap - b.StartX) / totalWidth : 0.5;
                    double leftGapY = startY + (endY - startY) * leftFrac;
                    double rightGapY = startY + (endY - startY) * rightFrac;

                    gc.DrawLine(b.StartX, startY, midX - numberGap, leftGapY,
                        Color.Black, thickness);
                    gc.DrawLine(midX + numberGap, rightGapY, b.EndX, endY,
                        Color.Black, thickness);
                    gc.DrawLine(b.EndX, endY, b.EndX, endY + edgeHeight * hookDir,
                        Color.Black, thickness);
                }

                double textY = b.IsStemUp ? midY - 0.3 : midY + 0.8;
                gc.DrawText(b.NumberText, midX, textY,
                    FontSize * 0.6, "serif", FontStyle.Bold, TextAnchor.Middle, Color.Black);
            }
        }
    }

    // ---------- Trill spanners (tr + wavy line) ----------

    /// <summary>
    /// Draws trill spanners: the "tr" Emmentaler glyph followed by a wavy
    /// extension line. The wave is approximated as a polyline through
    /// peak/valley points (enough segments per cycle that it reads as smooth).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:2228 (style . trill)
    /// </remarks>
    private static void DrawTrillSpanners(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.TrillSpannerLayouts.IsDefaultOrEmpty) return;
        const double wavePeriod = 0.8;
        const double waveAmplitude = 0.2;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TrillSpannerLayouts)
        {
            double absY = (sysY.TryGetValue(s.StartMeasureIndex, out var sy) ? sy : 0) + s.Y;
            using (gc.Source(s.SourcePosition))
            {
                bool isContinuation = Math.Abs(s.GlyphX - s.LineStartX) < 0.01;
                if (!isContinuation)
                    gc.DrawGlyph(EmmentalerGlyphs.OrnTrill, s.GlyphX, absY, FontSize);
                if (s.LineStartX < s.LineEndX)
                {
                    double length = s.LineEndX - s.LineStartX;
                    int halfWaves = Math.Max(1, (int)(length / (wavePeriod / 2)));
                    double seg = length / halfWaves;
                    double prevX = s.LineStartX, prevY = absY;
                    // Approximate Q-curves with 4 line segments per half-wave;
                    // visually indistinguishable at typical print sizes.
                    const int subdivisions = 4;
                    for (int i = 0; i < halfWaves; i++)
                    {
                        double startX = s.LineStartX + i * seg;
                        double sign = (i % 2 == 0) ? -1 : 1;
                        for (int j = 1; j <= subdivisions; j++)
                        {
                            double t = (double)j / subdivisions;
                            double x = startX + t * seg;
                            // Parabolic shape: y = absY + sign * amplitude * 4 t (1-t)
                            double y = absY + sign * waveAmplitude * 4 * t * (1 - t);
                            gc.DrawLine(prevX, prevY, x, y, Color.Black, thickness);
                            prevX = x; prevY = y;
                        }
                    }
                }
            }
        }
    }

    // ---------- Glissandos ----------

    /// <summary>Draws a simple straight glissando line between two notes.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm, scm/define-grobs.scm Glissando grob
    /// </remarks>
    private static void DrawGlissandos(ScoreLayout layout, IDrawingContext gc)
    {
        if (layout.GlissandoLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var g in layout.GlissandoLayouts)
        {
            using (gc.Source(g.SourcePosition))
                gc.DrawLine(g.StartX, g.StartY, g.EndX, g.EndY, Color.Black, thickness);
        }
    }

    // ---------- Arpeggios (wavy vertical line) ----------

    /// <summary>
    /// Draws arpeggio markings: a wavy vertical line on the left of a chord.
    /// Like the trill wavy line, the curve is approximated as a polyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
    /// </remarks>
    private static void DrawArpeggios(ScoreLayout layout, IDrawingContext gc)
    {
        if (layout.ArpeggioLayouts.IsDefaultOrEmpty) return;
        const double wavePeriod = 0.8;
        const double waveAmplitude = 0.2;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var a in layout.ArpeggioLayouts)
        {
            double length = a.BottomY - a.TopY;
            if (length <= 0) continue;
            int halfWaves = Math.Max(1, (int)(length / (wavePeriod / 2)));
            double seg = length / halfWaves;
            double prevX = a.X, prevY = a.TopY;
            const int subdivisions = 4;
            using (gc.Source(a.SourcePosition))
            {
                for (int i = 0; i < halfWaves; i++)
                {
                    double startY = a.TopY + i * seg;
                    double sign = (i % 2 == 0) ? -1 : 1;
                    for (int j = 1; j <= subdivisions; j++)
                    {
                        double t = (double)j / subdivisions;
                        double y = startY + t * seg;
                        double x = a.X + sign * waveAmplitude * 4 * t * (1 - t);
                        gc.DrawLine(prevX, prevY, x, y, Color.Black, thickness);
                        prevX = x; prevY = y;
                    }
                }
            }
        }
    }

    // ---------- Grace notes ----------

    /// <summary>
    /// Draws grace-note groups: small noteheads (with optional accidentals)
    /// scaled to GraceNoteLayout.Scale (typically 0.65), placed before the
    /// main note's column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grace-engraver.cc:36-80 Grace_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:1358-1402 GraceSpacing grob
    /// </remarks>
    private static void DrawGraceNotes(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.GraceNoteLayouts.IsDefaultOrEmpty) return;
        foreach (var g in layout.GraceNoteLayouts)
        {
            double sy = sysY.TryGetValue(g.MeasureIndex, out var s) ? s : 0;
            double staffMiddleY = sy + StaffHeight / 2;
            double scaledFontSize = FontSize * g.Scale;
            double currentX = g.X;
            using (gc.Source(g.SourcePosition))
            {
                foreach (var note in g.Notes)
                {
                    double y = staffMiddleY - note.StaffPosition * 0.5;
                    if (note.Accidental is { } acc)
                        DrawAccidental(acc, isCourtesy: false, currentX, y,
                            g.SourcePosition, gc);
                    gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, currentX, y, scaledFontSize);
                    if (note.NeedsLedger)
                        DrawLedgerLines(note.StaffPosition, currentX, staffMiddleY, gc);
                    currentX += 1.2 * g.Scale;  // approximate advance per grace note
                }
            }
        }
    }

    // ---------- Chord names ("Cm7", "B♭7") ----------

    /// <summary>
    /// Draws chord-name labels above the staff using a sans-serif bold font.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm ChordName: font-family=sans, font-size=1.5
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm — chord-name formatting
    /// </remarks>
    private static void DrawChordNames(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.ChordNameLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.65;
        foreach (var c in layout.ChordNameLayouts)
        {
            if (!sysY.TryGetValue(c.MeasureIndex, out var sy)) continue;
            using (gc.Source(c.SourcePosition))
                gc.DrawText(c.ChordText, c.X, sy + c.Y, size, "sans-serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
        }
    }

    // ---------- Figured bass ----------

    /// <summary>
    /// Draws figured-bass numerals stacked vertically below the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc:200-350 print()
    /// LILYPOND-REF: scm/define-grobs.scm:362-380 BassFigure defaults
    /// </remarks>
    private static void DrawFiguredBass(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.FiguredBassLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.75;
        const double figureSpacing = 1.5;
        foreach (var fb in layout.FiguredBassLayouts)
        {
            if (!sysY.TryGetValue(fb.MeasureIndex, out var sy)) continue;
            double baseY = sy + fb.Y;
            using (gc.Source(fb.SourcePosition))
            {
                for (int i = 0; i < fb.FigureTexts.Length; i++)
                    gc.DrawText(fb.FigureTexts[i], fb.X, baseY + i * figureSpacing,
                        size, "serif", FontStyle.Regular, TextAnchor.Middle, Color.Black);
            }
        }
    }

    // ---------- Percent repeats (slash + dots) ----------

    /// <summary>
    /// Draws the percent-repeat sign (a slanted slash with two dots) inside
    /// a measure that repeats the previous one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/percent-repeat-interface.cc — x_percent() rendering
    /// LILYPOND-REF: scm/define-grobs.scm:2520-2539 — slope=1.0, thickness=0.48
    /// </remarks>
    private static void DrawPercentRepeats(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.PercentRepeatLayouts.IsDefaultOrEmpty) return;
        const double slope = 1.0;
        const double thickness = 0.48;
        const double slashWidth = 2.0;
        const double dotOffset = 1.0;
        const double dotRadius = 0.25;
        double slashHeight = slashWidth * slope;

        foreach (var pr in layout.PercentRepeatLayouts)
        {
            if (!sysY.TryGetValue(pr.MeasureIndex, out var sy)) continue;
            double cx = pr.X;
            double cy = sy + pr.Y;
            using (gc.Source(pr.SourcePosition))
            {
                // Slash from bottom-left to top-right
                gc.DrawLine(cx - slashWidth / 2, cy + slashHeight / 2,
                    cx + slashWidth / 2, cy - slashHeight / 2,
                    Color.Black, thickness);
                gc.DrawCircle(cx + dotOffset * 0.3, cy - dotOffset, dotRadius, Color.Black);
                gc.DrawCircle(cx - dotOffset * 0.3, cy + dotOffset, dotRadius, Color.Black);
            }
        }
    }

    // ---------- Bar numbers ----------

    /// <summary>
    /// Draws the bar-number text at the start of each system (and at any
    /// requested period). Position is precomputed by BarNumberEngraver.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/bar-number-engraver.cc — Bar_number_engraver
    /// LILYPOND-REF: scm/define-grobs.scm BarNumber (font-size = -2)
    /// </remarks>
    private static void DrawBarNumbers(ScoreLayout layout, IDrawingContext gc)
    {
        if (layout.BarNumberLayouts.IsDefaultOrEmpty) return;
        const double fontSize = 1.8;
        foreach (var bn in layout.BarNumberLayouts)
            gc.DrawText(bn.Text, bn.X, bn.Y, fontSize, "serif",
                FontStyle.Bold, TextAnchor.Start, Color.Black);
    }

    // ---------- Stanza numbers ----------

    /// <summary>
    /// Draws stanza numbers ("1.", "2.") at the left of each verse line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stanza-number-engraver.cc — Stanza_number_engraver
    /// LILYPOND-REF: scm/define-grobs.scm StanzaNumber (font-size=-1, bold)
    /// </remarks>
    private static void DrawStanzaNumbers(ScoreLayout layout, IDrawingContext gc)
    {
        if (layout.StanzaNumberLayouts.IsDefaultOrEmpty) return;
        const double fontSize = 2.4;
        foreach (var sn in layout.StanzaNumberLayouts)
            gc.DrawText(sn.Text, sn.X, sn.Y, fontSize, "serif",
                FontStyle.Bold, TextAnchor.Start, Color.Black);
    }

    // ---------- Fingering ----------

    /// <summary>
    /// Draws fingering numerals (1-5) next to noteheads.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob
    /// LILYPOND-REF: scm/define-grobs.scm Fingering (font-size = -5 → ~0.56×)
    /// </remarks>
    private static void DrawFingerings(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.FingeringLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.56;  // magstep(-5)
        foreach (var f in layout.FingeringLayouts)
        {
            double y = (sysY.TryGetValue(f.MeasureIndex, out var sy) ? sy : 0) + f.Y;
            using (gc.Source(f.SourcePosition))
                gc.DrawText(f.Number.ToString(), f.X, y, size, "serif",
                    FontStyle.Regular, TextAnchor.Middle, Color.Black);
        }
    }

    // ---------- Music marks (segno, coda, fine, tempo, rehearsal, pedal text) ----------

    /// <summary>
    /// Draws music marks: navigation labels (Segno/Coda/Fine/D.S./D.C.),
    /// pedal text (Ped./Sost.), tempo markings (♩= NNN), rehearsal marks
    /// (boxed letters), and section labels.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/mark-engraver.cc:90-140 Mark types
    /// LILYPOND-REF: scm/define-grobs.scm:3650-3710 Segno, Coda
    /// </remarks>
    private static void DrawMusicMarks(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.MusicMarkLayouts.IsDefaultOrEmpty) return;
        foreach (var m in layout.MusicMarkLayouts)
        {
            if (IsHandledBySpannerEngraver(m.MarkType)) continue;
            double y = (sysY.TryGetValue(m.MeasureIndex, out var s) ? s : 0) + m.Y;
            using (gc.Source(m.SourcePosition))
                DrawSingleMusicMark(m, y, gc);
        }
    }

    private static void DrawSingleMusicMark(MusicMarkLayout m, double absY, IDrawingContext gc)
    {
        if (m.IsSymbol)
        {
            // Segno (U+E047) / Coda (U+E048): SMuFL music symbols rendered via
            // Emmentaler. Centered on the anchor.
            char glyph = m.MarkType == MusicMarkType.Segno ? '' : '';
            gc.DrawGlyph(glyph, m.X, absY, FontSize, Color.Black);
            return;
        }
        if (m.MarkType == MusicMarkType.Tempo)
        {
            // LILYPOND-REF: scm/define-grobs.scm:1835 MetronomeMark
            // LILYPOND-REF: lily/metronome-engraver.cc — notehead + stem + " = NNN"
            const double noteSize = 1.6;
            const double textSize = 1.8;
            gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, m.X, absY, noteSize);
            double stemX = m.X + noteSize * 0.32;
            double stemTop = absY - 3.5 * (noteSize / FontSize);
            gc.DrawLine(stemX, absY, stemX, stemTop, Color.Black, 0.10);
            gc.DrawText("= " + m.Text, m.X + noteSize * 0.5 + 0.3, absY,
                textSize, "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);
            return;
        }
        if (m.MarkType == MusicMarkType.Rehearsal || m.MarkType == MusicMarkType.SectionLabel)
        {
            double fs = m.MarkType == MusicMarkType.Rehearsal ? FontSize * 0.6 : FontSize * 0.55;
            const double pad = 0.2;
            double textWidth = m.Text.Length * fs * 0.6;  // crude advance estimate
            double boxW = textWidth + pad * 2;
            double boxH = fs + pad * 2;
            gc.DrawRectangle(m.X - boxW / 2, absY - boxH / 2, boxW, boxH,
                fill: Color.White, stroke: Color.Black, strokeWidth: 0.10);
            gc.DrawText(m.Text, m.X, absY + fs / 2 - pad, fs, "serif",
                FontStyle.Bold, TextAnchor.Middle, Color.Black);
            return;
        }
        if (IsPedalMark(m.MarkType))
        {
            bool italic = m.MarkType is MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
                or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;
            gc.DrawText(m.Text, m.X, absY, FontSize * 0.7, "serif",
                italic ? FontStyle.BoldItalic : FontStyle.Bold, TextAnchor.Middle, Color.Black);
            return;
        }
        // Default text marks (D.S./D.C./Fine/etc.)
        gc.DrawText(m.Text, m.X, absY, FontSize * 0.7, "serif",
            FontStyle.BoldItalic, TextAnchor.Middle, Color.Black);
    }

    private static bool IsHandledBySpannerEngraver(MusicMarkType type) =>
        type is MusicMarkType.Cresc or MusicMarkType.Decresc or MusicMarkType.Dim
             or MusicMarkType.Rit or MusicMarkType.Accel
             or MusicMarkType.OttavaUp or MusicMarkType.OttavaDown
             or MusicMarkType.QuindicesUp or MusicMarkType.QuindicesDown
             or MusicMarkType.Loco;

    private static bool IsPedalMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
             or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    // ---------- Custom text annotations ----------

    /// <summary>Draws free-form text annotations (e.g. "molto rit.", "a tempo").</summary>
    /// <remarks>LILYPOND-REF: lily/text-interface.cc — text rendering</remarks>
    private static void DrawCustomTexts(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.CustomTextLayouts.IsDefaultOrEmpty) return;
        foreach (var t in layout.CustomTextLayouts)
        {
            double y = (sysY.TryGetValue(t.MeasureIndex, out var s) ? s : 0) + t.Y;
            using (gc.Source(t.SourcePosition))
                gc.DrawText(t.Text, t.X, y, FontSize * 0.6, "serif",
                    FontStyle.Italic, TextAnchor.Middle, Color.Black);
        }
    }

    // ---------- Text spanners (rit. ----, accel. ----) ----------

    /// <summary>
    /// Draws text spanners: italic label followed by an extension line (dashed
    /// or solid) to the spanner end.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/text-spanner-engraver.cc TextSpanner engraver
    /// LILYPOND-REF: scm/define-grobs.scm:3504-3535 TextSpanner grob
    /// </remarks>
    private static void DrawTextSpanners(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.TextSpannerLayouts.IsDefaultOrEmpty) return;
        double textSize = FontSize * 0.5;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TextSpannerLayouts)
        {
            double absY = (sysY.TryGetValue(s.StartMeasureIndex, out var y) ? y : 0) + s.Y;
            using (gc.Source(s.SourcePosition))
            {
                gc.DrawText(s.Text, s.StartX, absY, textSize, "serif",
                    FontStyle.Italic, TextAnchor.Start, Color.Black);
                if (s.Style != TextSpannerStyle.None && s.LineStartX < s.EndX)
                {
                    (double On, double Off)? dash = s.Style == TextSpannerStyle.DashedLine
                        ? (s.DashPeriod * s.DashFraction, s.DashPeriod * (1 - s.DashFraction))
                        : null;
                    gc.DrawLine(s.LineStartX, absY, s.EndX, absY,
                        Color.Black, thickness, dash);
                }
            }
        }
    }

    // ---------- Pedal brackets ----------

    /// <summary>
    /// Draws piano pedal brackets: horizontal line below staff with a
    /// vertical hook at the release point.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pedal-bracket.cc — PianoPedalBracket grob
    /// </remarks>
    private static void DrawPedalBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.PedalBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var b in layout.PedalBracketLayouts)
        {
            double absY = (sysY.TryGetValue(b.StartMeasureIndex, out var y) ? y : 0) + b.Y;
            using (gc.Source(b.SourcePosition))
            {
                gc.DrawLine(b.StartX, absY, b.EndX, absY, Color.Black, thickness);
                gc.DrawLine(b.EndX, absY - b.EdgeHeight, b.EndX, absY, Color.Black, thickness);
            }
        }
    }

    // ---------- Multi-measure rests ----------

    /// <summary>
    /// Draws multi-measure rest indicators. Short runs (≤ ExpandLimit) use the
    /// church_rest decomposition (combinations of long/breve/whole rest
    /// glyphs); longer runs use the big_rest H-bar with a bold count above.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest.cc:194-220 big_rest
    /// LILYPOND-REF: lily/multi-measure-rest.cc:225-300 church_rest
    /// </remarks>
    private static void DrawMultiMeasureRests(ScoreLayout layout, IDrawingContext gc)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (mmr.UseChurchRest)
                DrawChurchRest(mmr, gc);
            else
                DrawBigRest(mmr, gc);
        }
    }

    private static void DrawChurchRest(MultiMeasureRestLayout mmr, IDrawingContext gc)
    {
        double cx = (mmr.StartX + mmr.EndX) / 2.0;
        double cy = mmr.Y;

        // Greedy decomposition: 4 (long), 2 (breve), 1 (whole).
        var pieces = new List<(int Span, char Glyph, double Width, double Y)>();
        const double LongWidth = 2.0, BreveWidth = 1.5, WholeWidth = 1.5, Gap = 0.4;
        int remaining = mmr.MeasureCount;
        foreach (var (span, glyph, width, dy) in new[]
        {
            (4, EmmentalerGlyphs.RestLonga, LongWidth, 0.0),
            (2, EmmentalerGlyphs.RestDoubleWhole, BreveWidth, 0.0),
            (1, EmmentalerGlyphs.RestWhole, WholeWidth, -0.5),
        })
        {
            while (remaining >= span)
            {
                pieces.Add((span, glyph, width, cy + dy));
                remaining -= span;
            }
        }
        if (pieces.Count == 0) return;

        double totalWidth = pieces.Sum(p => p.Width) + Gap * (pieces.Count - 1);
        double x = cx - totalWidth / 2;
        foreach (var p in pieces)
        {
            gc.DrawGlyph(p.Glyph, x + p.Width / 2, p.Y, FontSize);
            x += p.Width + Gap;
        }
        if (mmr.MeasureCount > 1)
            gc.DrawText(mmr.MeasureCount.ToString(), cx, cy - 2.5,
                2.4, "serif", FontStyle.Bold, TextAnchor.Middle, Color.Black);
    }

    private static void DrawBigRest(MultiMeasureRestLayout mmr, IDrawingContext gc)
    {
        const double thickness = 0.5;
        const double endCapHeight = 0.8;
        const double padding = 1.0;
        const double capThickness = 0.18;

        double left = mmr.StartX + padding;
        double right = mmr.EndX - padding;
        if (right <= left) return;
        double cy = mmr.Y;

        gc.DrawRectangle(left, cy - thickness / 2, right - left, thickness, fill: Color.Black);
        gc.DrawRectangle(left - capThickness / 2, cy - endCapHeight,
            capThickness, 2 * endCapHeight, fill: Color.Black);
        gc.DrawRectangle(right - capThickness / 2, cy - endCapHeight,
            capThickness, 2 * endCapHeight, fill: Color.Black);

        double textX = (left + right) / 2;
        double textY = cy - endCapHeight - 0.5;
        gc.DrawText(mmr.MeasureCount.ToString(), textX, textY,
            2.4, "serif", FontStyle.Bold, TextAnchor.Middle, Color.Black);
    }

    // ---------- Tie variants (laissez-vibrer / repeat-tie) ----------

    /// <summary>
    /// Draws half-ties: laissez-vibrer (let-ring, pointing right out of the
    /// note) and repeat-tie (pointing left into the note from a repeat).
    /// Same Bezier-bow shape as full ties.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/laissez-vibrer-engraver.cc — LaissezVibrerTie grob
    /// LILYPOND-REF: lily/repeat-tie-engraver.cc — RepeatTie grob
    /// </remarks>
    private static void DrawTieVariants(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.TieVariantLayouts.IsDefaultOrEmpty) return;
        // Tie variants use staff-relative Y already in the layout — no system offset needed
        // (TieVariantEngraver computes absolute Y).
        foreach (var v in layout.TieVariantLayouts)
        {
            DrawCurve(v.StartX, v.Y, v.EndX, v.Y,
                v.Control1, v.Control2, v.CurveUp,
                EngravingDefaults.TieMidThickness, gc);
        }
    }

    // ---------- Lyric hyphen dashes ----------

    /// <summary>
    /// Draws explicit hyphen dashes between syllables of the same word
    /// (LyricLayout.DrawHyphen handles single-character hyphens; this draws
    /// the multi-dash sequence layouts that span wider gaps).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-hyphen.cc:60-100 LyricHyphen grob
    /// </remarks>
    private static void DrawLyricHyphens(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.LyricHyphenLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.16;
        foreach (var h in layout.LyricHyphenLayouts)
        {
            if (h.Type == LyricConnectorType.Hyphen)
            {
                foreach (var dash in h.Dashes)
                {
                    var src = layout.LyricLayouts[h.LyricIndex];
                    double sy = sysY.TryGetValue(src.Item.MeasureIndex, out var s) ? s : 0;
                    gc.DrawLine(dash.X1, sy + dash.Y, dash.X2, sy + dash.Y,
                        Color.Black, thickness);
                }
            }
            else if (h.Type == LyricConnectorType.Extender)
            {
                var src = layout.LyricLayouts[h.LyricIndex];
                double sy = sysY.TryGetValue(src.Item.MeasureIndex, out var s) ? s : 0;
                if (h.CrossesSystemBreak)
                {
                    gc.DrawLine(h.ExtenderStartX, sy + h.ExtenderY,
                        h.FirstSegmentEndX, sy + h.ExtenderY, Color.Black, 0.1);
                    gc.DrawLine(h.SecondSegmentStartX, sy + h.ExtenderY,
                        h.ExtenderEndX, sy + h.ExtenderY, Color.Black, 0.1);
                }
                else
                {
                    gc.DrawLine(h.ExtenderStartX, sy + h.ExtenderY,
                        h.ExtenderEndX, sy + h.ExtenderY, Color.Black, 0.1);
                }
            }
        }
    }

    // ---------- Part combine annotations ----------

    /// <summary>Draws part-combine text labels ("a2", "Solo", "Solo II").</summary>
    /// <remarks>LILYPOND-REF: scm/part-combiner.scm — CombineTextScript</remarks>
    private static void DrawPartCombine(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc)
    {
        if (layout.PartCombineLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.65;
        foreach (var pc in layout.PartCombineLayouts)
        {
            double y = (sysY.TryGetValue(pc.MeasureIndex, out var s) ? s : 0) + pc.Y;
            gc.DrawText(pc.Text, pc.X, y, size, "serif",
                FontStyle.Italic, TextAnchor.Start, Color.Black);
        }
    }

    // ---------- Tremolo (stem slashes, drawn from DrawNote) ----------

    /// <summary>
    /// Draws tremolo beams across a stem: short angled slashes at the stem's
    /// midpoint. Number of slashes corresponds to the tremolo subdivision.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem-tremolo.cc:129-150 raw_stencil
    /// LILYPOND-REF: lily/stem-tremolo.cc:81-94 width depends on flag
    /// LILYPOND-REF: lily/stem-tremolo.cc:45-79 calc-slope
    /// </remarks>
    private static void DrawTremolo(
        double stemX, double stemAttachY, double stemEndY,
        bool stemUp, int beamCount, bool hasFlag, IDrawingContext gc)
    {
        if (beamCount <= 0) return;
        double beamWidth = hasFlag ? 1.0 : 1.5;
        const double beamThickness = 0.48;
        const double beamGap = 0.8;
        double slope = (!stemUp && hasFlag) ? 0.40 : 0.25;

        double stemMidY = (stemAttachY + stemEndY) / 2;
        double totalHeight = beamCount * beamThickness + (beamCount - 1) * beamGap;
        double startY = stemMidY - totalHeight / 2 + beamThickness / 2;

        for (int i = 0; i < beamCount; i++)
        {
            double y = startY + i * (beamThickness + beamGap);
            double halfW = beamWidth / 2;
            double dy = halfW * slope;
            double y1 = stemUp ? y + dy : y - dy;
            double y2 = stemUp ? y - dy : y + dy;
            gc.DrawLine(stemX - halfW, y1, stemX + halfW, y2,
                Color.Black, beamThickness);
        }
    }

    // ---------- System-start delimiters (group brackets / bar lines) ----------

    /// <summary>
    /// Draws the system-start delimiter (bracket / line-bracket / bar-line)
    /// on the left edge of each multi-staff group. Brace rendering is left
    /// to a future phase that ports BraceRenderer's path output.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc:127-129 collapse_height check
    /// LILYPOND-REF: scm/define-grobs.scm SystemStartBrace/Bracket/Square/Bar
    /// </remarks>
    private static void DrawSystemStartDelimiters(SystemLayout system, IDrawingContext gc)
    {
        if (system.StaffGroups.IsDefaultOrEmpty) return;
        foreach (var group in system.StaffGroups)
        {
            if (group.GrandStaffLayout is not { } delim) continue;
            double top = system.Y + delim.BraceTop;
            double bottom = system.Y + delim.BraceBottom;
            double height = bottom - top;
            switch (delim.DelimiterType)
            {
                case SystemStartDelimiterType.Bracket:
                    if (height >= 5)
                        DrawSystemStartBracket(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.LineBracket:
                    if (height >= 5)
                        DrawSystemStartLineBracket(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.BarLine:
                    DrawSystemStartBarLine(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.Brace:
                    // LILYPOND-REF: scm/define-grobs.scm SystemStartBrace collapse-height = 5
                    if (height >= 5)
                        DrawSystemStartBrace(delim.BraceX, top, bottom, gc);
                    break;
            }
        }
    }

    private static void DrawSystemStartBracket(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = 0.45;
        double serifH = 0.4, serifW = 0.6;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
        // Top serif (right-pointing triangle filled)
        gc.DrawClosedBezier(
            (x, top), (x + serifW, top), (x + serifW, top),
            (x + serifW * 0.3, top + serifH), (x + serifW * 0.3, top + serifH), (x + serifW * 0.3, top + serifH),
            Color.Black);
        // Bottom serif
        gc.DrawClosedBezier(
            (x, bottom), (x + serifW, bottom), (x + serifW, bottom),
            (x + serifW * 0.3, bottom - serifH), (x + serifW * 0.3, bottom - serifH), (x + serifW * 0.3, bottom - serifH),
            Color.Black);
    }

    private static void DrawSystemStartLineBracket(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = EngravingDefaults.StaffLineThickness;
        const double hookWidth = 0.5;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
        gc.DrawLine(x, top, x + hookWidth, top, Color.Black, thickness);
        gc.DrawLine(x, bottom, x + hookWidth, bottom, Color.Black, thickness);
    }

    private static void DrawSystemStartBarLine(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = EngravingDefaults.StaffLineThickness * 1.6;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
    }

    /// <summary>
    /// Draws the curly brace used for grand staff (piano) groups. The brace
    /// is rendered as a single Emmentaler-Brace glyph (576 sizes available
    /// at U+E000+index, larger index → taller brace). Glyph selection mirrors
    /// <see cref="Svg.Renderer.BraceRenderer"/> so SVG and PDF agree on size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-markup-commands.scm (left-brace)
    /// </remarks>
    // ---------- Mid-measure clef change ----------

    /// <summary>
    /// Draws a mid-measure clef change at reduced size (LP _change variant glyphs).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/clef.cc:29-52 — calc_glyph_name appends "_change" suffix
    /// </remarks>
    private static void DrawClefChange(ClefChangeItem clefChange, double x, double staffY, IDrawingContext gc)
    {
        char glyph = clefChange.NewClef switch
        {
            ClefType.Bass => EmmentalerGlyphs.FClefChange,
            ClefType.Alto => EmmentalerGlyphs.CClefChange,
            ClefType.Tenor => EmmentalerGlyphs.CClefChange,
            _ => EmmentalerGlyphs.GClefChange,
        };
        double clefY = clefChange.NewClef switch
        {
            ClefType.Bass => staffY + 1,
            ClefType.Alto => staffY + 2,
            ClefType.Tenor => staffY + 1,
            _ => staffY + 3,
        };
        using (gc.Source(clefChange.SourcePosition))
            gc.DrawGlyph(glyph, x, clefY, FontSize);
    }

    // ---------- Mid-measure key signature change ----------

    /// <summary>
    /// Draws a mid-measure key signature change. Cancellation naturals are
    /// shown for accidentals removed from the previous key, followed by the
    /// new key's accidentals.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/key-engraver.cc — process_music()
    /// </remarks>
    private static void DrawKeySignatureChange(KeySignatureChangeItem change, double x, double staffY, IDrawingContext gc)
    {
        int prev = change.PreviousKey.Sharps;
        int next = change.NewKey.Sharps;
        double dx = 0;

        // Cancellation naturals when the sign flips or count shrinks.
        bool needNaturals = (prev != 0 && next == 0) ||
                            (prev > 0 && next < 0) || (prev < 0 && next > 0) ||
                            (Math.Sign(prev) == Math.Sign(next) && Math.Abs(next) < Math.Abs(prev));
        if (needNaturals)
        {
            int natCount = Math.Abs(prev) - (Math.Sign(prev) == Math.Sign(next) ? Math.Abs(next) : 0);
            int[] sharpPos = { 8, 5, 9, 6, 3, 7, 4 };
            int[] flatPos = { 4, 7, 3, 6, 2, 5, 1 };
            var positions = prev > 0 ? sharpPos : flatPos;
            int startAt = Math.Sign(prev) == Math.Sign(next) ? Math.Abs(next) : 0;
            for (int i = 0; i < natCount; i++)
            {
                int pos = positions[startAt + i];
                double y = staffY + 4 - (pos - 1) * 0.5;
                using (gc.Source(change.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AccidentalNatural, x + dx, y, FontSize);
                dx += 0.7;
            }
        }

        if (next != 0)
            DrawKeySignature(change.NewKey, ClefType.Treble, x + dx, staffY, gc);
    }

    private static void DrawSystemStartBrace(double x, double top, double bottom, IDrawingContext gc)
    {
        double height = bottom - top;
        double yMid = (top + bottom) / 2;

        const int braceGlyphStart = 0xE000;
        const int braceGlyphCount = 576;
        const double minGlyphHeight = 263.0;
        const double maxGlyphHeight = 11493.0;
        const double unitsPerEm = 1000.0;
        const double scaleFactor = 0.76;

        double targetUnits = height * unitsPerEm;
        double ratio = Math.Clamp((targetUnits - minGlyphHeight) / (maxGlyphHeight - minGlyphHeight), 0, 1);
        int glyphIndex = Math.Clamp((int)(Math.Pow(ratio, 0.8) * (braceGlyphCount - 1)), 0, braceGlyphCount - 1);
        double glyphHeightUnits = minGlyphHeight + ((double)glyphIndex / (braceGlyphCount - 1)) * (maxGlyphHeight - minGlyphHeight);
        double fontSize = (height / (glyphHeightUnits / unitsPerEm)) * scaleFactor;

        char braceChar = (char)(braceGlyphStart + glyphIndex);
        gc.DrawText(braceChar.ToString(), x, yMid, fontSize, "Emmentaler-Brace",
            FontStyle.Regular, TextAnchor.End, Color.Black);
    }
}
