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
    // ---------- System-start delimiters (group brackets / bar lines) ----------

    /// <summary>
    /// Draws the system-start delimiter (brace / bracket / line-bracket /
    /// bar-line) on the left edge of each multi-staff group.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc:127-129 collapse_height check
    /// LILYPOND-REF: scm/define-grobs.scm SystemStartBrace/Bracket/Square/Bar
    /// </remarks>
    /// <summary>
    /// Draws the instrument name text for each staff group. When a grand-staff
    /// group has only one named staff, the name is centered vertically across
    /// the brace span; otherwise each named staff gets its own centered name.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/instrument-name-engraver.cc — InstrumentName grob
    /// LILYPOND-REF: scm/define-grobs.scm:1851 InstrumentName
    ///   font: serif, padding 0.3, self-alignment-X = CENTER, self-alignment-Y = CENTER
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
    ///   nameX = MarginLeft + indent / 2 (MarginLeft applied by the page-level
    ///   translate group, so this method uses indent / 2 directly).
    /// </remarks>
    private static void DrawInstrumentNames(MultiStaffScore score, SystemLayout system, IDrawingContext gc)
    {
        if (system.Indent <= 0) return;

        const double NameFontScale = 0.75;
        double actualFontSize = FontSize * NameFontScale;
        double nameX = system.Indent / 2.0;
        double systemYUp = LayoutUtilities.SystemTopYUp(system);

        // Single-staff scores carry no StaffGroup layouts — the one staff sits
        // at the system Y with the standard staff height.
        if (system.StaffGroups.IsDefaultOrEmpty)
        {
            foreach (var (_, st, _) in score.EnumerateStaves())
            {
                if (string.IsNullOrEmpty(st.InstrumentName) || st.IsTab)
                    continue;
                gc.DrawText(st.InstrumentName, nameX, systemYUp - StaffHeight / 2.0,
                    actualFontSize, "serif", FontStyle.Regular,
                    TextAnchor.Middle, fill: null,
                    verticalAnchor: VerticalAnchor.Middle);
                break;
            }
            return;
        }

        foreach (var staffGroup in system.StaffGroups)
        {
            bool anyNamed = false;
            foreach (var sl in staffGroup.Staves)
            {
                if (!string.IsNullOrEmpty(sl.InstrumentName)) { anyNamed = true; break; }
            }
            if (!anyNamed) continue;

            // Single name spanning a delimited group: center vertically across the brace.
            if (staffGroup.HasDelimiter && staffGroup.GrandStaffLayout is { } gs)
            {
                int namedCount = 0;
                StaffLayout? onlyNamed = null;
                foreach (var sl in staffGroup.Staves)
                {
                    if (string.IsNullOrEmpty(sl.InstrumentName)) continue;
                    namedCount++;
                    onlyNamed = sl;
                    if (namedCount > 1) break;
                }
                if (namedCount == 1 && onlyNamed is { })
                {
                    double centerY = systemYUp + (gs.BraceTop + gs.BraceBottom) / 2.0;
                    gc.DrawText(onlyNamed.InstrumentName!, nameX, centerY,
                        actualFontSize, "serif", FontStyle.Regular,
                        TextAnchor.Middle, fill: null,
                        verticalAnchor: VerticalAnchor.Middle);
                    continue;
                }
                // Multiple named staves fall through to per-staff rendering.
            }

            foreach (var staffLayout in staffGroup.Staves)
            {
                if (string.IsNullOrEmpty(staffLayout.InstrumentName) || staffLayout.IsHidden)
                    continue;
                double staffY = systemYUp + staffLayout.Y;
                double centerY = staffY - staffLayout.Height / 2.0;
                gc.DrawText(staffLayout.InstrumentName, nameX, centerY,
                    actualFontSize, "serif", FontStyle.Regular,
                    TextAnchor.Middle, fill: null,
                    verticalAnchor: VerticalAnchor.Middle);
            }
        }
    }

    /// <summary>
    /// Joins the staves of a multi-staff system: a SystemStartBar at the left
    /// edge (always, for 2+ staves), and — within delimited groups (grand
    /// staff etc.) — every barline extended through the inter-staff gap
    /// (Span_bar), with repeat dots omitted in the gap.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter-engraver.cc — Score-level
    ///   SystemStartBar joins all staves of any multi-staff system.
    /// LILYPOND-REF: lily/span-bar-engraver.cc + ly/engraver-init.ly —
    ///   Span_bar_engraver lives in GrandStaff/PianoStaff/StaffGroup, so
    ///   ungrouped staves do NOT span their barlines.
    /// LILYPOND-REF: lily/span-bar-engraver.cc — the spanned segment redraws
    ///   the bar glyph without the dots.
    /// </remarks>
    private static void DrawStaffConnectors(
        MultiStaffScore score, ScoreLayout layout, SystemLayout system,
        double systemStartX, IDrawingContext gc)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return;
        double systemYUp = LayoutUtilities.SystemTopYUp(system);

        // SystemStartBar across ALL visible staves of the system — EXCLUDING
        // independent text rows (chords / lyrics), which LilyPond's ChordNames /
        // Lyrics contexts do not connect (StaffLayout carries no kind, so resolve
        // back via EnumerateStaves).
        var textRowIndices = new HashSet<int>(
            score.EnumerateStaves().Where(t => t.Staff.IsTextRow)
                .Select(t => t.GlobalStaffIndex));
        var allStaves = system.StaffGroups
            .SelectMany(g => g.Staves)
            .Where(s => !s.IsHidden && !s.IsOssia && !textRowIndices.Contains(s.StaffIndex))
            // staff.Y is Y-up, so top-to-bottom order is DESCENDING.
            .OrderByDescending(s => s.Y)
            .ToList();
        if (allStaves.Count >= 2)
        {
            double top = systemYUp + allStaves[0].Y;
            double bottom = systemYUp + allStaves[^1].Y - allStaves[^1].Height;
            DrawSystemStartBarLine(systemStartX, top, bottom, gc);
        }

        // Span bars inside delimited groups. Barline types come from a content
        // voice — they are score-synchronized at collection time.
        var voice = score.PrimaryContentStaff.PrimaryVoice;
        foreach (var group in system.StaffGroups)
        {
            // A ChoirStaff is bracketed but its barlines are NOT spanned across the
            // gap — each staff keeps its own. LILYPOND-REF: ly/engraver-init.ly —
            // ChoirStaff has no Span_bar_engraver (unlike GrandStaff/StaffGroup).
            if (!group.HasDelimiter || group.Type == StaffGroupType.ChoirStaff)
                continue;
            var staves = group.Staves
                .Where(s => !s.IsHidden && !s.IsOssia)
                // staff.Y is Y-up, so top-to-bottom order is DESCENDING.
                .OrderByDescending(s => s.Y)
                .ToList();
            if (staves.Count < 2)
                continue;

            bool lineStart = true;
            foreach (var ml in system.Measures)
            {
                if (ml.MeasureIndex >= voice.Measures.Length)
                    continue;
                var measure = voice.Measures[ml.MeasureIndex];
                bool atLineStart = lineStart;
                lineStart = false;

                bool suppressEnd = measure.EndBarline == BarlineType.Single
                    && IsMmrInnerEndBarline(layout, ml.MeasureIndex);
                double endWidth = GetVisualBarlineWidth(measure.EndBarline);
                // Keep the inter-staff connector aligned with the staff barlines,
                // which clear the line-start clef by LineStartBarClearance.
                double startX = atLineStart ? ml.X + LineStartBarClearance : ml.X;

                for (int i = 0; i + 1 < staves.Count; i++)
                {
                    double gapTop = systemYUp + staves[i].Y - staves[i].Height;
                    double gapBottom = systemYUp + staves[i + 1].Y;
                    double gapHeight = gapTop - gapBottom;
                    if (gapHeight <= 0)
                        continue;

                    if (measure.StartBarline != BarlineType.None)
                        DrawBarline(measure.StartBarline, startX, gapTop, gapHeight,
                            gc, withDots: false);
                    if (!suppressEnd)
                        DrawBarline(measure.EndBarline, ml.X + ml.Width - endWidth,
                            gapTop, gapHeight, gc, withDots: false);
                }
            }
        }
    }

    private static void DrawSystemStartDelimiters(SystemLayout system, IDrawingContext gc)
    {
        if (system.StaffGroups.IsDefaultOrEmpty) return;
        double systemYUp = LayoutUtilities.SystemTopYUp(system);
        foreach (var group in system.StaffGroups)
        {
            if (group.GrandStaffLayout is not { } delim) continue;
            double top = systemYUp + delim.BraceTop;
            double bottom = systemYUp + delim.BraceBottom;
            double height = top - bottom;
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

    // LILYPOND-REF: lily/system-start-delimiter.cc System_start_delimiter::staff_bracket —
    // vertical filled box (thickness default 0.25) capped by brackettips.down/up serif
    // glyphs, with overlap = 0.1 x thickness where the tips join the line.
    private static void DrawSystemStartBracket(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = 0.25; // LP staff_bracket thickness default (in staff-space)
        double serifH = 0.4, serifW = 0.6;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
        // Top serif (right-pointing triangle filled); serif tip drops toward the
        // staff (device down = smaller Y-up).
        gc.DrawClosedBezier(
            (x, top), (x + serifW, top), (x + serifW, top),
            (x + serifW * 0.3, top - serifH), (x + serifW * 0.3, top - serifH), (x + serifW * 0.3, top - serifH),
            Color.Black);
        // Bottom serif
        gc.DrawClosedBezier(
            (x, bottom), (x + serifW, bottom), (x + serifW, bottom),
            (x + serifW * 0.3, bottom + serifH), (x + serifW * 0.3, bottom + serifH), (x + serifW * 0.3, bottom + serifH),
            Color.Black);
    }

    // LILYPOND-REF: lily/system-start-delimiter.cc System_start_delimiter::line_bracket —
    // vertical line with a horizontal hook of width w=0.8 at each end.
    private static void DrawSystemStartLineBracket(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = EngravingDefaults.StaffLineThickness;
        const double hookWidth = 0.8;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
        gc.DrawLine(x, top, x + hookWidth, top, Color.Black, thickness);
        gc.DrawLine(x, bottom, x + hookWidth, bottom, Color.Black, thickness);
    }

    // LILYPOND-REF: lily/system-start-delimiter.cc System_start_delimiter::simple_bar —
    // a single vertical line of width = line-thickness x thickness.
    private static void DrawSystemStartBarLine(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = EngravingDefaults.StaffLineThickness * 1.6;
        gc.DrawLine(x, top, x, bottom, Color.Black, thickness);
    }

    // ---------- Mid-measure clef change ----------

    /// <summary>
    /// Draws a mid-measure clef change at reduced size (LP _change variant glyphs).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/clef.cc:29-52 — calc_glyph_name appends "_change" suffix
    /// </remarks>
    private static void DrawClefChange(ClefChangeItem clefChange, double x, double staffY, IDrawingContext gc)
    {
        // A CUE clef is the PLAIN glyph shrunk, not the "_change" variant: MEASURED
        // (audit/lp-geometry/probes/cue-span.ly D-WITH) LilyPond's CueClef reads
        // `glyph=clefs.F fontsize=-4` and its CueEndClef `glyph=clefs.G fontsize=-4`,
        // where an ordinary mid-measure change would read clefs.F_change at full size.
        char glyph = clefChange.IsCue
            ? clefChange.NewClef switch
            {
                ClefType.Bass or ClefType.Bass8Below => EmmentalerGlyphs.FClef,
                ClefType.Alto or ClefType.Tenor or ClefType.Soprano
                    or ClefType.MezzoSoprano or ClefType.Baritone => EmmentalerGlyphs.CClef,
                ClefType.Percussion => EmmentalerGlyphs.PercussionClef,
                _ => EmmentalerGlyphs.GClef,
            }
            : clefChange.NewClef switch
            {
                ClefType.Bass or ClefType.Bass8Below => EmmentalerGlyphs.FClefChange,
                ClefType.Alto or ClefType.Tenor or ClefType.Soprano
                    or ClefType.MezzoSoprano or ClefType.Baritone => EmmentalerGlyphs.CClefChange,
                ClefType.Percussion => EmmentalerGlyphs.PercussionClefChange,
                _ => EmmentalerGlyphs.GClefChange,
            };
        // LILYPOND-REF: scm/parser-clef.scm supported-clefs — each clef's middle
        // integer is the staff position of the named line (treble G=-2, bass F=2,
        // alto C=0); the glyph anchors on the line it names.
        double clefY = clefChange.NewClef switch
        {
            ClefType.Bass or ClefType.Bass8Below => staffY - 1,
            ClefType.Alto or ClefType.Percussion => staffY - 2,
            ClefType.Tenor => staffY - 1,
            ClefType.Soprano => staffY - 4,
            ClefType.MezzoSoprano => staffY - 3,
            ClefType.Baritone => staffY - 0,
            _ => staffY - 3,
        };
        using (gc.Source(clefChange.SourcePosition))
        {
            gc.DrawGlyph(glyph, x, clefY,
                clefChange.IsCue ? FontSize * EngravingDefaults.CueScale : FontSize);
            if (clefChange.NewClef is ClefType.Treble8Below or ClefType.Bass8Below)
                DrawClefModifier8(x, staffY, change: true, gc);
            else if (clefChange.NewClef == ClefType.Treble8Above)
                DrawClefModifier8(x, staffY, change: true, gc, above: true);
        }
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
    /// <returns>
    /// The x AFTER the signature it drew. The end-of-line courtesy needs it so the meter that
    /// follows can stand on the key's real right edge — computing that edge a second time from
    /// the widths would be the same quantity spelled twice, and the two spellings drift.
    /// </returns>
    private static double DrawKeySignatureChange(KeySignatureChangeItem change, double x, double staffY,
        ClefType clef, IDrawingContext gc)
    {
        int prev = change.PreviousKey.Sharps;
        int next = change.NewKey.Sharps;
        double dx = 0;

        // A CUSTOM key on either side: cancel every step of the previous
        // signature that the new one no longer alters, then draw the new
        // signature (custom or standard) — the simple form of LilyPond's
        // per-step cancellation.
        if (change.PreviousKey.Custom != null || change.NewKey.Custom != null)
        {
            var prevSteps = change.PreviousKey.Custom is { } pc
                ? KeySignature.DecodeCustom(pc).ToList()
                : StandardKeySteps(prev);
            var newAltered = (change.NewKey.Custom is { } nc
                ? KeySignature.DecodeCustom(nc).Select(p => p.Step)
                : StandardKeySteps(next).Select(p => p.Step)).ToHashSet();
            int prevNaturalPos = int.MinValue;
            bool anyNatural = false;
            foreach (var (step, alter) in prevSteps)
            {
                if (newAltered.Contains(step)) continue;
                int staffPosition = KeySigStaffPositionForStep(clef, alter >= 0, step);
                if (anyNatural)
                    dx += GlyphMetrics.AccidentalNatural.Width
                        + NaturalKernPadding(prevNaturalPos, staffPosition);
                double ny = (staffY - StaffHeight / 2) + staffPosition / 2.0;
                using (gc.Source(change.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AccidentalNatural, x + dx, ny, FontSize);
                prevNaturalPos = staffPosition;
                anyNatural = true;
            }
            if (anyNatural)
                dx += GlyphMetrics.AccidentalNatural.Width + 0.4;
            using (gc.Source(change.SourcePosition))
                return DrawKeySignature(change.NewKey, clef, x + dx, staffY, gc);
        }

        // Cancellation naturals when the sign flips or count shrinks. Their
        // positions are the PREVIOUS key's accidental positions, resolved for
        // THIS staff's clef — the old treble-only table drew bass-staff
        // naturals a third off.
        // LILYPOND-REF: lily/key-engraver.cc — cancellation from key_signature;
        // scm/output-lib.scm key-signature-interface::alteration-positions.
        bool needNaturals = (prev != 0 && next == 0) ||
                            (prev > 0 && next < 0) || (prev < 0 && next > 0) ||
                            (Math.Sign(prev) == Math.Sign(next) && Math.Abs(next) < Math.Abs(prev));
        if (needNaturals)
        {
            int natCount = Math.Abs(prev) - (Math.Sign(prev) == Math.Sign(next) ? Math.Abs(next) : 0);
            int startAt = Math.Sign(prev) == Math.Sign(next) ? Math.Abs(next) : 0;
            // Naturals kern by their vertical-edge intervals, like LilyPond:
            // a natural has vertical edges on BOTH sides, so neighbours whose
            // edges overlap need 0.3 clearance, corner-touching pairs 0.15,
            // vertically clear pairs none. The old flat 0.7 advance was
            // narrower than the glyph itself (0.724) and the pair overlapped.
            // LILYPOND-REF: lily/key-signature-interface.cc — ht interval
            //   [2p−6, 2p+3], left side shifted +3; padding 0.3 / 0.15.
            int prevNatPos = 0;
            for (int i = 0; i < natCount; i++)
            {
                int staffPosition = KeySigStaffPosition(clef, prev > 0, startAt + i);
                if (i > 0)
                    dx += GlyphMetrics.AccidentalNatural.Width
                        + NaturalKernPadding(prevNatPos, staffPosition);
                double y = (staffY - StaffHeight / 2) + staffPosition / 2.0;
                using (gc.Source(change.SourcePosition))
                    gc.DrawGlyph(EmmentalerGlyphs.AccidentalNatural, x + dx, y, FontSize);
                prevNatPos = staffPosition;
            }
            dx += GlyphMetrics.AccidentalNatural.Width;
            // Gap before the new signature. LilyPond keeps the KeyCancellation and
            // the following KeySignature as SEPARATE break-aligned grobs, so there is
            // a deliberate gap between the cancellation naturals and the new key: it
            // comes from KeyCancellation's space-alist entry
            //   (key-signature . (extra-space . 0.5))
            // trimmed by their (extra-spacing-width . (0.0 . 1.0)) overlap — a spring,
            // not a fixed pad, whose net in this inline model is ~0.4.
            // LILYPOND-REF: scm/define-grobs.scm — KeyCancellation space-alist.
            dx += 0.4;
        }

        return next != 0
            ? DrawKeySignature(change.NewKey, clef, x + dx, staffY, gc)
            : x + dx;
    }

    /// <summary>
    /// Draws a mid-piece time signature change at the change point, full size
    /// (unlike clef changes, which use reduced _change glyphs).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/time-signature-engraver.cc</remarks>
    private static void DrawTimeSignatureChange(TimeSignatureChangeItem timeChange, double x, double staffY, IDrawingContext gc)
    {
        using (gc.Source(timeChange.SourcePosition))
            DrawTimeSignature(timeChange.NewTime, x, staffY, gc);
    }

    // Draws the curly brace used for grand staff (piano) groups as a single
    // Emmentaler-Brace glyph (576 sizes at U+E000+index, larger index → taller
    // brace). LILYPOND-REF: scm/define-markup-commands.scm (left-brace).
    private static void DrawSystemStartBrace(double x, double top, double bottom, IDrawingContext gc)
    {
        double height = top - bottom;
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
