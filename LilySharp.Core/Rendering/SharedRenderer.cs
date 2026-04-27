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
        foreach (var page in layout.Pages)
        {
            var gc = doc.BeginPage(page.Width, page.Height);
            DrawHeader(score, page, gc);
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
            doc.EndPage();
        }
    }

    // ---------- Header ----------

    private static void DrawHeader(MultiStaffScore score, PageLayout page, IDrawingContext gc)
    {
        if (score.Title is { } title)
        {
            double centerX = page.Width / 2;
            double titleY = page.HeaderHeight * 0.5;
            gc.DrawText(title, centerX, titleY, 1.6, "serif",
                FontStyle.Bold, TextAnchor.Middle);
        }
        if (score.Composer is { } composer)
        {
            double rightX = page.Width - 2;
            double composerY = page.HeaderHeight * 0.85;
            gc.DrawText(composer, rightX, composerY, 1.0, "serif",
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
                }
            }
        }
    }

    private static void DrawNote(NoteItem note, double x, double staffMiddleY, IDrawingContext gc)
    {
        int noteValue = note.BaseDuration.Denominator;
        if (note.BaseDuration.Numerator != 1) noteValue = 1;
        double noteY = staffMiddleY - note.StaffPosition * 0.5;

        // Accidental (left of notehead)
        if (note.Accidental != null)
            DrawAccidental(note.Accidental, note.IsCourtesy, x, noteY, note.SourcePosition, gc);

        // Notehead
        char head = EmmentalerGlyphs.GetNotehead(noteValue);
        using (gc.Source(note.SourcePosition))
            gc.DrawGlyph(head, x, noteY, FontSize);

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
            if (noteValue >= 8)
            {
                var flag = EmmentalerGlyphs.GetFlag(noteValue, note.StemUp);
                if (flag.HasValue)
                    gc.DrawGlyph(flag.Value, stemX, stemEndY, FontSize);
            }
        }

        // Augmentation dots
        for (int d = 0; d < note.Dots; d++)
        {
            double dotX = x + 1.0 + d * 0.5;
            double dotY = note.StaffPosition % 2 == 0 ? noteY - 0.5 : noteY;
            gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot, dotX, dotY, FontSize);
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
}
