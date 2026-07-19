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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Rendering;

internal static partial class SharedRenderer
{
    // ---------- Chord names ("Cm7", "B♭7") ----------

    /// <summary>
    /// Draws chord-name labels above the staff using a sans-serif bold font.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm ChordName: font-family=sans, font-size=1.5
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm — chord-name formatting
    /// </remarks>
    private static void DrawChordNames(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc,
        double pageHeight)
    {
        if (layout.ChordNameLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.65;
        // `as both`: the Roman degree stacked one line ABOVE the absolute name.
        const double stackLineHeight = 2.2;
        foreach (var c in layout.ChordNameLayouts)
        {
            if (!sysY.TryGetValue(c.MeasureIndex, out var sy)) continue;
            // Page Y-up: lift the measure's system top and add the stored offset.
            double cy = (pageHeight - sy) + c.YUp;
            using (gc.Source(c.SourcePosition))
            {
                gc.DrawText(c.ChordText, c.X, cy, size, "sans-serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
                if (c.AboveLine != null)
                    gc.DrawText(c.AboveLine, c.X, cy + stackLineHeight, size, "sans-serif",
                        FontStyle.Bold, TextAnchor.Middle, Color.Black);
            }
        }
    }

    // ---------- Figured bass ----------

    /// <summary>
    /// Draws figured-bass numerals stacked vertically below the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc:269 process_music / :157 stop_translation_timestep
    /// LILYPOND-REF: scm/define-grobs.scm:352 BassFigure defaults
    /// </remarks>
    private static void DrawFiguredBass(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.FiguredBassLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.75;
        const double figureSpacing = 1.5;
        foreach (var fb in layout.FiguredBassLayouts)
        {
            if (!sysY.ContainsKey(fb.MeasureIndex)) continue;
            // Page Y-up against this figure's own staff middle; figures then stack
            // downward from the topmost baseline (device down = smaller Y-up).
            double baseY = os.StaffMiddleYUp(fb.StaffIndex, fb.MeasureIndex, StaffHeight) + fb.YUp;
            using (gc.Source(fb.SourcePosition))
            {
                for (int i = 0; i < fb.FigureTexts.Length; i++)
                    gc.DrawText(fb.FigureTexts[i], fb.X, baseY - i * figureSpacing,
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
    /// LILYPOND-REF: scm/define-grobs.scm:2788-2807 — slope=1.0, thickness=0.48
    /// </remarks>
    private static void DrawPercentRepeats(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.PercentRepeatLayouts.IsDefaultOrEmpty) return;
        const double slope = 1.0;
        const double thickness = 0.48;
        const double slashWidth = 2.0;
        const double dotRadius = 0.25;
        // LILYPOND-REF: lily/percent-repeat-interface.cc x_percent — each dot is
        // translated ±0.5 staff-space vertically and pulled to just off the slash's
        // left/right edge (slash half-width 1.0 minus the 0.75 dot-negative-kern, plus
        // the dot's own radius ≈ 0.5). The upper dot sits upper-LEFT, the lower dot
        // lower-RIGHT — the "%" glyph.
        const double dotDx = 0.5;
        const double dotDy = 0.5;
        double slashHeight = slashWidth * slope;

        foreach (var pr in layout.PercentRepeatLayouts)
        {
            if (!sysY.ContainsKey(pr.MeasureIndex)) continue;
            double cx = pr.X;
            // Page Y-up against this sign's own staff middle.
            double cy = os.StaffMiddleYUp(pr.StaffIndex, pr.MeasureIndex, StaffHeight) + pr.YUp;
            using (gc.Source(pr.SourcePosition))
            {
                // Slash from bottom-left to top-right, with a dot in each pocket it
                // leaves — upper-LEFT and lower-RIGHT, like the "%" glyph.
                gc.DrawLine(cx - slashWidth / 2, cy - slashHeight / 2,
                    cx + slashWidth / 2, cy + slashHeight / 2,
                    Color.Black, thickness);
                gc.DrawCircle(cx - dotDx, cy + dotDy, dotRadius, Color.Black);
                gc.DrawCircle(cx + dotDx, cy - dotDy, dotRadius, Color.Black);
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
    /// </remarks>
    private static void DrawBarNumbers(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc,
        double pageHeight)
    {
        if (layout.BarNumberLayouts.IsDefaultOrEmpty) return;
        // LILYPOND-REF: scm/define-grobs.scm BarNumber (font-size . -2) —
        // 2.2sp text height × magstep(-2); see BarNumberEngraver.FontSize.
        double fontSize = BarNumberEngraver.FontSize;
        // Collisions with voltas/marks are resolved by the unified
        // outside-staff stacking pass (OutsideStaffStacker.StackAboveStaff).
        foreach (var bn in layout.BarNumberLayouts)
        {
            if (!sysY.TryGetValue(bn.MeasureIndex, out var sy))
                continue; // other page
            // Page Y-up: lift this measure's system top and add the stored offset.
            double y = (pageHeight - sy) + bn.YUp;
            gc.DrawText(bn.Text, bn.X, y, fontSize, "serif",
                FontStyle.Bold, bn.RightAligned ? TextAnchor.End : TextAnchor.Start,
                Color.Black);
        }
    }

    // ---------- Stanza numbers ----------

    /// <summary>
    /// Draws stanza numbers ("1.", "2.") at the left of each verse line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stanza-number-engraver.cc — Stanza_number_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:3412 StanzaNumber (font-series bold)
    /// </remarks>
    private static void DrawStanzaNumbers(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc,
        double pageHeight)
    {
        if (layout.StanzaNumberLayouts.IsDefaultOrEmpty) return;
        const double fontSize = 2.4;
        foreach (var sn in layout.StanzaNumberLayouts)
        {
            // sn.YUp is Y-up from the system top (the verse's lyric baseline); lift
            // the system top to the page Y-up frame and add it, like DrawLyrics.
            if (!sysY.TryGetValue(sn.MeasureIndex, out var s)) continue; // other page
            // Page Y-up: lift the system top and add the stored offset, like DrawLyrics.
            double y = (pageHeight - s) + sn.YUp;
            gc.DrawText(sn.Text, sn.X, y, fontSize, "serif",
                FontStyle.Bold, TextAnchor.Start, Color.Black);
        }
    }

    // ---------- Fingering ----------

    /// <summary>
    /// Draws fingering numerals (1-5) next to noteheads.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob
    /// LILYPOND-REF: scm/define-grobs.scm Fingering (font-size = -5 → ~0.56×)
    /// </remarks>
    private static void DrawFingerings(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.FingeringLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.56;  // magstep(-5)
        foreach (var f in layout.FingeringLayouts)
        {
            if (!sysY.ContainsKey(f.MeasureIndex)) continue; // other page
            // Frame B -> device: reflect the Y-up baseline against this fingering's
            // own staff middle (the shared per-grob draw boundary), then apply ossia.
            double midYup = os.StaffMiddleYUp(f.StaffIndex, f.MeasureIndex, StaffHeight);
            double y = os.YUp(midYup + f.YUp, f.StaffIndex, f.MeasureIndex);
            using (gc.Source(f.SourcePosition))
                gc.DrawText(f.Number.ToString(), f.X, y,
                    os.Size(size, f.StaffIndex), "serif",
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
    /// LILYPOND-REF: scm/define-grobs.scm SegnoMark:3083, CodaMark:1001
    /// </remarks>
    private static void DrawMusicMarks(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc, double pageHeight)
    {
        if (layout.MusicMarkLayouts.IsDefaultOrEmpty) return;
        foreach (var m in layout.MusicMarkLayouts)
        {
            if (IsHandledBySpannerEngraver(m.MarkType)) continue;
            if (!sysY.ContainsKey(m.MeasureIndex)) continue; // other page
            // The mark's page Y-up anchor: the staff middle's Y-up refpoint plus the
            // stored offset. Marks do not shrink on an ossia staff (the former code
            // used StaffMiddleDeviceY, not os.Y), so no ossia affine is applied here.
            double yUp = os.StaffMiddleYUp(m.StaffIndex, m.MeasureIndex, StaffHeight) + m.YUp;
            using (gc.Source(m.SourcePosition))
                DrawSingleMusicMark(m, yUp, gc);
        }
    }

    /// <summary>
    /// Draws the swing/shuffle feel equation beside a tempo mark: two beamed straight
    /// notes "=" a beamed dotted + plain note under a triplet "3". <paramref name="subdivision"/>
    /// picks the note value — 8 = eighths (single beam), 16 = sixteenths (double beam).
    /// Hand-built from the same notehead/stem/beam primitives the metronome mark uses.
    /// </summary>
    private static void DrawSwingEquation(IDrawingContext gc, double startX, double baselineY, int subdivision)
    {
        int beams = subdivision >= 16 ? 2 : 1;
        const double beamGap = 0.3;          // spacing between the two beams of a 16th
        // Sizes track the metronome mark: the same notehead size (1.6) and stem length,
        // and a beam scaled to that small note (0.48 staff-beam x 1.6/FontSize) rather
        // than the full staff-beam thickness, which read as too heavy here.
        const double ns = TempoNoteSize;
        const double headGap = 1.0;          // x between the two heads of a pair
        const double stemUp = 1.4;           // stem height (matches the metronome stem)
        const double stemDx = ns * 0.32;     // stem offset from head origin (right side)
        const double stemW = 0.09;
        const double beamW = EngravingDefaults.BeamThickness * (ns / FontSize);
        const double eqSize = 1.8;           // matches the "= NNN" text
        const double threeSize = 1.0;

        // Draws one beamed eighth pair at px; returns the x just past it.
        double DrawPair(double px, bool dotted, bool withThree)
        {
            double h1 = px;
            double h2 = px + headGap + (dotted ? ns * 0.42 : 0);
            gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, h1, baselineY, ns);
            gc.DrawGlyph(EmmentalerGlyphs.NoteheadBlack, h2, baselineY, ns);
            if (dotted)
                gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot, h1 + ns * 0.6, baselineY, ns);
            double s1 = h1 + stemDx;
            double s2 = h2 + stemDx;
            double beamY = baselineY + stemUp;
            gc.DrawLine(s1, baselineY, s1, beamY, Color.Black, stemW);
            gc.DrawLine(s2, baselineY, s2, beamY, Color.Black, stemW);
            for (int b = 0; b < beams; b++)   // 1 thin beam (8th) or 2 (16th)
                gc.DrawLine(s1, beamY - b * beamGap, s2, beamY - b * beamGap, Color.Black, beamW);
            if (withThree)
            {
                // Triplet bracket "3" above the beam (as on shuffle charts).
                double midX = (s1 + s2) / 2;
                double brkY = beamY + 0.55;
                const double hook = 0.22, halfGap = 0.3;
                gc.DrawLine(s1, brkY, s1, brkY - hook, Color.Black, 0.07);
                gc.DrawLine(s1, brkY, midX - halfGap, brkY, Color.Black, 0.07);
                gc.DrawLine(midX + halfGap, brkY, s2, brkY, Color.Black, 0.07);
                gc.DrawLine(s2, brkY, s2, brkY - hook, Color.Black, 0.07);
                gc.DrawText("3", midX, brkY - 0.35, threeSize, "serif",
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
            }
            return s2 + ns * 0.35;
        }

        double x = DrawPair(startX, dotted: false, withThree: false);
        x += 0.35;
        gc.DrawText("=", x, baselineY, eqSize, "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);
        x += SerifTextMetrics.MeasureBold("=", eqSize) + 0.45;
        DrawPair(x, dotted: true, withThree: true);
    }

    // absY is the mark's anchor in the page Y-up frame (page-bottom origin, up
    // positive), matching LilyPond's native sign convention: offsets that move a
    // sub-element visually UP the page ADD, device-downward offsets SUBTRACT. The
    // page context is Y-up, so every coordinate is emitted directly.
    private static void DrawSingleMusicMark(MusicMarkLayout m, double absY, IDrawingContext gc)
    {
        if (m.IsSymbol)
        {
            // Segno (U+E062) / Coda (U+E064) in this Emmentaler cmap.
            // NOTE: the SMuFL codepoints U+E047/E048 map to scripts.thumb /
            // scripts.sforzato here and previously drew the WRONG glyphs.
            char glyph = m.MarkType == MusicMarkType.Segno
                ? EmmentalerGlyphs.MarkSegno
                : EmmentalerGlyphs.MarkCoda;
            gc.DrawGlyph(glyph, m.X, absY, FontSize, Color.Black);
            return;
        }
        if (m.MarkType == MusicMarkType.Tempo)
        {
            // LILYPOND-REF: scm/define-grobs.scm:2335 MetronomeMark
            // LILYPOND-REF: lily/metronome-engraver.cc — notehead + stem + " = NNN";
            // a textual marking prints bold with the equation parenthesized after
            // it: Grave (♩ = 54). Text-only tempo prints just the bold marking.
            const double noteSize = TempoNoteSize;
            const double textSize = 1.8;
            double x = m.X;
            bool hasMetronome = m.Text.Length > 0;
            if (m.TempoText != null)
            {
                const double markingSize = 2.2;
                gc.DrawText(m.TempoText, x, absY, markingSize,
                    "serif", FontStyle.Bold, TextAnchor.Start, Color.Black);
                if (!hasMetronome)
                    return;
                x += SerifTextMetrics.MeasureBold(m.TempoText, markingSize) + 0.8;
                gc.DrawText("(", x, absY, textSize,
                    "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);
                x += SerifTextMetrics.Measure("(", textSize) + 0.1;
            }
            // Beat-unit note: whole (1) = stemless whole head; 2 = hollow
            // half with stem; 4+ = black head, stem, flags from the 8th up.
            char head = m.TempoBeatUnit <= 1
                ? EmmentalerGlyphs.NoteheadWhole
                : m.TempoBeatUnit == 2
                    ? EmmentalerGlyphs.NoteheadHalf
                    : EmmentalerGlyphs.NoteheadBlack;
            // The stemless whole note sits a touch higher so its center
            // lines up with the equation text the way the stemmed units do
            // (their stem carries the visual weight upward). Up = larger Y-up.
            double headY = m.TempoBeatUnit <= 1 ? absY + 0.5 : absY;
            gc.DrawGlyph(head, x, headY, noteSize);
            double headW = m.TempoBeatUnit <= 1 ? noteSize * 0.62 : noteSize * 0.5;
            if (m.TempoBeatUnit >= 2)
            {
                double stemX = x + noteSize * 0.32;
                // The stem rises from the head (up = larger Y-up).
                double stemTop = absY + 3.5 * (noteSize / FontSize);
                gc.DrawLine(stemX, absY, stemX, stemTop, Color.Black, 0.10);
                if (m.TempoBeatUnit >= 8)
                    gc.DrawGlyph(EmmentalerGlyphs.Flag8thUp, stemX, stemTop, noteSize);
            }
            double dotX = x + headW + 0.15;
            for (int d = 0; d < m.TempoDots; d++)
            {
                // Beside the head at ITS center (ly:dots::print puts the dot
                // on the head's line).
                gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot, dotX, headY, noteSize);
                dotX += 0.55;
            }
            double tempoTextX = Math.Max(x + headW + 0.3, m.TempoDots > 0 ? dotX + 0.15 : 0);
            string equation = "= " + m.Text + (m.TempoText != null ? ")" : "");
            gc.DrawText(equation, tempoTextX, absY,
                textSize, "serif", FontStyle.Regular, TextAnchor.Start, Color.Black);
            if (m.SwingSubdivision != 0)
            {
                double textEnd = tempoTextX + SerifTextMetrics.MeasureBold(equation, textSize);
                // DrawSwingEquation draws in the page Y-up frame; hand it the Y-up baseline.
                DrawSwingEquation(gc, textEnd + 0.8, absY, m.SwingSubdivision);
            }
            return;
        }
        if (m.MarkType == MusicMarkType.Rehearsal || m.MarkType == MusicMarkType.SectionLabel)
        {
            double fs = m.MarkType == MusicMarkType.Rehearsal ? FontSize * 0.6 : FontSize * 0.55;
            const double pad = 0.2;
            double textWidth = SerifTextMetrics.MeasureBold(m.Text, fs);
            double boxW = textWidth + pad * 2;
            double boxH = fs + pad * 2;
            // DrawRectangle's y is the visual-top edge (Y-up): anchor + half the box.
            gc.DrawRectangle(m.X - boxW / 2, absY + boxH / 2, boxW, boxH,
                fill: Color.White, stroke: Color.Black, strokeWidth: EngravingDefaults.LineThickness);
            gc.DrawText(m.Text, m.X, absY - fs / 2 + pad, fs, "serif",
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
        if (m.MarkType == MusicMarkType.ToCoda)
        {
            // "To" followed by the coda SIGN (not the word "Coda"), centered as a
            // group. LILYPOND-REF: the al-coda text is set with the coda glyph.
            double ts = FontSize * 0.7;
            double gs = FontSize * 0.8;
            const string prefix = "To ";
            double textW = SerifTextMetrics.MeasureBold(prefix, ts);
            double glyphW = gs * 0.42;   // approx advance of scripts.coda
            double left = m.X - (textW + glyphW) / 2;
            gc.DrawText(prefix, left, absY, ts, "serif",
                FontStyle.BoldItalic, TextAnchor.Start, Color.Black);
            // The coda glyph's baseline sits low; lift it (up = larger Y-up) so its
            // centre aligns with the cap height of "To".
            gc.DrawGlyph(EmmentalerGlyphs.MarkCoda, left + textW, absY + gs * 0.30, gs, Color.Black);
            return;
        }
        // Default text marks (D.S./D.C./Fine/etc.)
        gc.DrawText(m.Text, m.X, absY, FontSize * 0.7, "serif",
            FontStyle.BoldItalic, TextAnchor.Middle, Color.Black);
    }

    private static bool IsHandledBySpannerEngraver(MusicMarkType type) =>
        MusicMarkItem.IsSpannerHandled(type);

    private static bool IsPedalMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
             or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    // ---------- Custom text annotations ----------

    /// <summary>Draws free-form text annotations (e.g. "molto rit.", "a tempo").</summary>
    /// <remarks>LILYPOND-REF: lily/text-interface.cc — text rendering</remarks>
    private static void DrawCustomTexts(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.CustomTextLayouts.IsDefaultOrEmpty) return;
        foreach (var t in layout.CustomTextLayouts)
        {
            if (!sysY.ContainsKey(t.MeasureIndex)) continue; // other page
            // Page Y-up against the (top) staff middle this text resolves.
            double y = os.StaffMiddleYUp(t.StaffIndex, t.MeasureIndex, StaffHeight) + t.YUp;
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
    /// LILYPOND-REF: scm/define-grobs.scm:3835 TextSpanner grob
    /// </remarks>
    private static void DrawTextSpanners(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc, double pageHeight)
    {
        if (layout.TextSpannerLayouts.IsDefaultOrEmpty) return;
        double textSize = FontSize * 0.5;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TextSpannerLayouts)
        {
            if (!sysY.TryGetValue(s.StartMeasureIndex, out var y)) continue; // other page
            // Page Y-up: lift the system top, add the stored offset, then ossia.
            double absY = os.YUp((pageHeight - y) + s.YUp, s.StaffIndex, s.StartMeasureIndex);
            using (gc.Source(s.SourcePosition))
            {
                gc.DrawText(s.Text, s.StartX, absY,
                    os.Size(textSize, s.StaffIndex), "serif",
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
    /// LILYPOND-REF: lily/piano-pedal-bracket.cc — PianoPedalBracket grob
    /// </remarks>
    private static void DrawPedalBrackets(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc,
        double pageHeight)
    {
        if (layout.PedalBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var b in layout.PedalBracketLayouts)
        {
            if (!sysY.TryGetValue(b.StartMeasureIndex, out var y)) continue; // other page
            // Page Y-up: lift the system top and drop by the stored device offset.
            double absY = (pageHeight - y) - b.Y;
            using (gc.Source(b.SourcePosition))
            {
                gc.DrawLine(b.StartX, absY, b.EndX, absY, Color.Black, thickness);
                gc.DrawLine(b.EndX, absY + b.EdgeHeight, b.EndX, absY, Color.Black, thickness);
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
    private static void DrawMultiMeasureRests(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc,
        double pageHeight)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (!sysY.TryGetValue(mmr.StartMeasureIndex, out double sy))
                continue; // other page
            // mmr.Y is the within-system offset of the staff middle; the system-top
            // Y-up is pageHeight - system.Y, so the middle's page Y-up is that minus
            // the offset (byte-identical to the former pageHeight - absoluteMiddle).
            double cy = (pageHeight - sy) - mmr.Y;
            if (mmr.UseChurchRest)
                DrawChurchRest(mmr, cy, gc);
            else
                DrawBigRest(mmr, cy, gc);
        }
    }

    private static void DrawChurchRest(MultiMeasureRestLayout mmr, double cy, IDrawingContext gc)
    {
        double cx = (mmr.StartX + mmr.EndX) / 2.0;

        // Greedy decomposition: 4 (long), 2 (breve), 1 (whole).
        // Use each rest glyph's REAL ink width (from the extracted font metrics) so
        // the centred row matches LilyPond's church_rest, which sums r.extent(X) for
        // each symbol. The block longa/breve rests are only ~0.6 ss wide; the whole
        // rest is 1.5 ss. LILYPOND-REF: lily/multi-measure-rest.cc church_rest.
        var pieces = new List<(int Span, char Glyph, double Width, double Y)>();
        double LongWidth = GlyphMetrics.RestLonga.Width;
        double BreveWidth = GlyphMetrics.RestDoubleWhole.Width;
        double WholeWidth = GlyphMetrics.RestWhole.Width;
        const double Gap = 0.4;
        // Vertical placement (dy, in staff spaces below the staff middle cy — device
        // +Y is down). Each church-rest glyph sits at its own natural staff position
        // spi = Rest::staff_position_internal(me, dl, CENTER). For a normal 5-line staff
        // (line-positions {-4,-2,0,2,4}, neutral direction, default font-size 0 so the
        // dl<0 "(ss - fs)" term vanishes) that resolves to:
        //   whole (dl= 0): spi = +2  → hangs from the 4th line (one line above middle)
        //   breve (dl=-1): spi =  0  → sits on the middle line (ink fills the space above it)
        //   longa (dl=-2): spi =  0  → centred on the middle line (ink spans ±1 space)
        // dy = -0.5 * spi converts a staff position to a device offset from cy.
        // Matches LilyPond 2.24 with \compressMMRests (verified by juxtaposition).
        // LILYPOND-REF: lily/rest.cc Rest::staff_position_internal; lily/multi-measure-rest.cc church_rest.
        int remaining = mmr.MeasureCount;
        foreach (var (span, glyph, width, dy) in new[]
        {
            (4, EmmentalerGlyphs.RestLonga, LongWidth, 0.0),       // spi 0  → dy 0
            (2, EmmentalerGlyphs.RestDoubleWhole, BreveWidth, 0.0), // spi 0  → dy 0
            (1, EmmentalerGlyphs.RestWhole, WholeWidth, -1.0),      // spi +2 → dy -1.0
        })
        {
            while (remaining >= span)
            {
                // dy is a device offset from cy; flip its sign into the Y-up frame.
                pieces.Add((span, glyph, width, cy - dy));
                remaining -= span;
            }
        }
        if (pieces.Count == 0) return;

        double totalWidth = pieces.Sum(p => p.Width) + Gap * (pieces.Count - 1);
        // Centre the row of rest glyphs on cx. DrawGlyph anchors at the glyph's
        // LEFT edge (SVG text-anchor="start"; these rest glyphs have bbox Left=0),
        // so each glyph's left edge is laid at the running x — NOT at x+Width/2,
        // which would shift the ink right by half a glyph (an R1 then landed ~0.75 ss
        // right of the bar-line midpoint). The whole row spans [cx-totalWidth/2,
        // cx+totalWidth/2], centring the symbols' ink on the span midpoint.
        // LILYPOND-REF: lily/multi-measure-rest.cc church_rest — left_offset =
        // (space - symbols_width)/2, then each glyph add_at_edge LEFT (left-anchored).
        double x = cx - totalWidth / 2;
        foreach (var p in pieces)
        {
            gc.DrawGlyph(p.Glyph, x, p.Y, FontSize);
            x += p.Width + Gap;
        }
        if (mmr.MeasureCount > 1)
            DrawMmrNumber(mmr.MeasureCount, cx, cy, gc);
    }

    /// <summary>
    /// Draws a multi-measure rest's measure count above the staff.
    /// </summary>
    /// <remarks>
    /// LilyPond's MultiMeasureRestNumber uses the music-font number glyphs
    /// (font-encoding fetaText — NOT a text serif font), centred on the rest
    /// (self-alignment-X CENTER) and placed above the staff (direction UP,
    /// staff-padding 0.4). The feta digits are baseline-anchored (bottom =
    /// baseline), so the baseline sits 0.4 ss above the top staff line:
    /// cy - 2.0 (top line) - 0.4 = cy - 2.4.
    /// LILYPOND-REF: scm/define-grobs.scm MultiMeasureRestNumber.
    /// </remarks>
    private static void DrawMmrNumber(int count, double cx, double cy, IDrawingContext gc)
    {
        var digits = count.ToString();
        double totalAdvance = 0;
        foreach (var ch in digits)
            totalAdvance += GlyphMetrics.GetTimeSigDigitWidth(ch - '0');
        double x = cx - totalAdvance / 2;
        // The number sits above the staff (device up = larger Y-up).
        double baseline = cy + 2.4;
        foreach (var ch in digits)
        {
            gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(ch - '0'), x, baseline, FontSize);
            x += GlyphMetrics.GetTimeSigDigitWidth(ch - '0');
        }
    }

    // LILYPOND-REF: lily/multi-measure-rest.cc:195-220 Multi_measure_rest::big_rest —
    // thick horizontal bar (half-height = thick-thickness x line-thickness x ss/2) capped
    // by hair-thickness vertical end caps of full staff-space height.
    private static void DrawBigRest(MultiMeasureRestLayout mmr, double cy, IDrawingContext gc)
    {
        const double thickness = EngravingDefaults.MultiMeasureRestThickThickness;
        const double endCapHeight = 0.8;
        const double padding = 1.0;
        const double capThickness = EngravingDefaults.MultiMeasureRestHairThickness;

        double left = mmr.StartX + padding;
        double right = mmr.EndX - padding;
        if (right <= left) return;

        // Rectangles: the y arg is the visual-top edge (device up = larger Y-up),
        // heights stay positive.
        gc.DrawRectangle(left, cy + thickness / 2, right - left, thickness, fill: Color.Black);
        gc.DrawRectangle(left - capThickness / 2, cy + endCapHeight,
            capThickness, 2 * endCapHeight, fill: Color.Black);
        gc.DrawRectangle(right - capThickness / 2, cy + endCapHeight,
            capThickness, 2 * endCapHeight, fill: Color.Black);

        DrawMmrNumber(mmr.MeasureCount, (left + right) / 2, cy, gc);
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
    private static void DrawTieVariants(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc, double pageHeight)
    {
        if (layout.TieVariantLayouts.IsDefaultOrEmpty) return;
        foreach (var v in layout.TieVariantLayouts)
        {
            if (!sysY.TryGetValue(v.MeasureIndex, out double sy))
                continue; // other page
            // TieVariantLayout stores WITHIN-SYSTEM device offsets (down from the
            // system top). Reconstruct the absolute device Y (sy + offset), then
            // reflect to the layout Y-up frame (= -device) DrawBow expects — this is
            // byte-identical to the former absolute-Y reflection. The arc geometry
            // itself stays device-frame (intentional-device island 2).
            DrawBow(v.StartX, -(sy + v.Y), v.EndX, -(sy + v.Y),
                (v.Control1.X, -(sy + v.Control1.Y)), (v.Control2.X, -(sy + v.Control2.Y)), v.CurveUp,
                EngravingDefaults.TieMidThickness,
                v.StaffIndex, v.MeasureIndex, os, gc, pageHeight);
        }
    }

    // ---------- Lyric hyphen dashes ----------

    /// <summary>
    /// Draws explicit hyphen dashes between syllables of the same word
    /// (LyricLayout.DrawHyphen handles single-character hyphens; this draws
    /// the multi-dash sequence layouts that span wider gaps).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-hyphen.cc:37 Lyric_hyphen::print (LyricHyphen grob)
    /// </remarks>
    private static void DrawLyricHyphens(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc,
        double pageHeight)
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
                    if (!sysY.TryGetValue(src.Item.MeasureIndex, out var sy)) continue; // other page
                    double dashY = (pageHeight - sy) - dash.Y;
                    gc.DrawLine(dash.X1, dashY, dash.X2, dashY,
                        Color.Black, thickness);
                }
            }
            else if (h.Type == LyricConnectorType.Extender)
            {
                var src = layout.LyricLayouts[h.LyricIndex];
                if (!sysY.TryGetValue(src.Item.MeasureIndex, out var sy)) continue; // other page
                double extY = (pageHeight - sy) - h.ExtenderY;
                if (h.CrossesSystemBreak)
                {
                    gc.DrawLine(h.ExtenderStartX, extY,
                        h.FirstSegmentEndX, extY, Color.Black, 0.1);
                    gc.DrawLine(h.SecondSegmentStartX, extY,
                        h.ExtenderEndX, extY, Color.Black, 0.1);
                }
                else
                {
                    gc.DrawLine(h.ExtenderStartX, extY,
                        h.ExtenderEndX, extY, Color.Black, 0.1);
                }
            }
        }
    }

    // ---------- Part combine annotations ----------

    /// <summary>Draws part-combine text labels ("a2", "Solo", "Solo II").</summary>
    /// <remarks>LILYPOND-REF: lily/part-combine-engraver.cc — CombineTextScript (grob in scm/define-grobs.scm)</remarks>
    private static void DrawPartCombine(ScoreLayout layout, Dictionary<int, double> sysY, IDrawingContext gc,
        double pageHeight)
    {
        if (layout.PartCombineLayouts.IsDefaultOrEmpty) return;
        double size = FontSize * 0.65;
        foreach (var pc in layout.PartCombineLayouts)
        {
            if (!sysY.TryGetValue(pc.MeasureIndex, out var s)) continue; // other page
            // Page Y-up: lift the system top and add the stored offset.
            double y = (pageHeight - s) + pc.YUp;
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
    /// LILYPOND-REF: lily/stem-tremolo.cc:127-150 raw_stencil.
    /// Width — calc_width (stem-tremolo.cc:84-93): <c>((dir==UP &amp;&amp; flag) || beam) ? 1.0 :
    /// 1.5</c>. This method is reached ONLY for unbeamed stems (the caller in DrawNote guards
    /// the whole stem/flag/tremolo block with <c>!isBeamed</c>; beamed stems are drawn by
    /// DrawBeams), so the <c>beam</c> branch is unreachable and the width reduces to
    /// <c>(stemUp &amp;&amp; hasFlag) ? 1.0 : 1.5</c> — an UP-stem flag gets 1.0, a DOWN-stem
    /// flag keeps 1.5. (Tremolo slashes on a BEAMED stem are a separate unimplemented case:
    /// DrawBeams renders the beamed stem but not its tremolo.)
    /// Slope — calc_slope's non-beam branch (stem-tremolo.cc:75-78): a DOWN-stem flag gets
    /// the steeper 0.40 (avoids flag/stem collision), else 0.25.
    /// </remarks>
    private static void DrawTremolo(
        double stemX, double stemAttachY, double stemEndY,
        bool stemUp, int beamCount, bool hasFlag, IDrawingContext gc)
    {
        if (beamCount <= 0) return;
        double beamWidth = (stemUp && hasFlag) ? 1.0 : 1.5;
        const double beamThickness = EngravingDefaults.BeamThickness;
        const double beamGap = 0.8;
        double slope = (!stemUp && hasFlag) ? 0.40 : 0.25;

        double stemMidY = (stemAttachY + stemEndY) / 2;
        double totalHeight = beamCount * beamThickness + (beamCount - 1) * beamGap;
        // Offsets from the stem midpoint flip into the Y-up frame.
        double startY = stemMidY + totalHeight / 2 - beamThickness / 2;

        for (int i = 0; i < beamCount; i++)
        {
            double y = startY - i * (beamThickness + beamGap);
            double halfW = beamWidth / 2;
            double dy = halfW * slope;
            double y1 = stemUp ? y - dy : y + dy;
            double y2 = stemUp ? y + dy : y - dy;
            gc.DrawLine(stemX - halfW, y1, stemX + halfW, y2,
                Color.Black, beamThickness);
        }
    }

}
