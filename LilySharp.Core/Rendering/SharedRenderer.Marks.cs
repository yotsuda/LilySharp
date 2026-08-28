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
using LilySharp.Core.Rendering.Svg;
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
    /// LILYPOND-REF: scm/define-grobs.scm:837-855 — ChordName declares NO <c>X-offset</c> and
    ///   no <c>self-alignment-interface</c>, so the grob's reference point is its ink LEFT and
    ///   the symbol stands ON its paper column: <c>TextAnchor.Start</c>, not <c>Middle</c>.
    ///   MEASURED (audit/lp-geometry/probes/staffless-system.ly): every score there dumps the
    ///   ChordName anchor equal to its column's X, and the ledger point
    ///   <c>staffless.line-start.chords-vs-staff</c> priced the centring at 0.438600 ss.
    /// <para>
    /// ⚠️ A chord GRID is a different grob: <c>GridChordName</c> centres its ink in the
    /// measure square (scm/define-grobs.scm:1736-1752 → <c>grid-chord-name::calc-X-offset</c>,
    /// scm/output-lib.scm:3744-3768, which subtracts <c>interval-center</c> of the stencil).
    /// Lily# has no measure squares — its chords-only sheet places symbols on timing columns —
    /// so it stays on this ChordName path; porting GridChordName is separate work.
    /// </para>
    /// </remarks>
    private static void DrawChordNames(
        ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.ChordNameLayouts.IsDefaultOrEmpty) return;
        // The one home for the chord em, shared with ChordNameEngraver so the reserved ink
        // and the drawn ink are the same size. It was a local FontSize * 0.65 (= 2.6), an
        // approximation of LilyPond's own ChordName size.
        double size = LilySharp.Core.Svg.EngravingDefaults.ChordNameFontSize;
        // The series shares the same home as the em: ChordName declares NO font-series
        // (scm/define-grobs.scm:837-855), so the symbol renders regular, in the style the
        // engraver reserved for.
        const FontStyle style = LilySharp.Core.Svg.EngravingDefaults.ChordNameFontStyle;
        // ...and the ACCIDENTALS are not text at all: LilyPond draws each one as an Emmentaler
        // glyph, one font step smaller and lifted off the baseline
        // (scm/chord-name.scm:80-95 — the addresses and the 2.26.0 measurements are on
        // ChordNameGlyphRun). This loop therefore walks the run's pieces instead of handing
        // the whole string to the text face. Until 2026-08-25 it handed over the string, and
        // since the bundled face has no U+266F the glyph in the picture came from whatever the
        // PLATFORM supplied — a picture that was a function of the machine.
        // ⚠️ ONE HOME WITH THE RESERVATION: the pieces here are the pieces
        // ChordNameEngraver.SymbolInkWidth and SymbolInk price, so the drawn symbol and the
        // reserved one cannot drift (the failure this file already carries a note about).
        double glyphEm = FontSize
            * LilySharp.Core.Svg.Layout.EmmentalerDesignSize.Magstep(
                LilySharp.Core.Svg.Layout.ChordNameGlyphRun.AccidentalFontSizeStep);
        // ONE line per symbol. `as both` used to draw a second one 2.2 ss above this
        // baseline — a distance that lived here while the ink was reserved for in
        // ChordNameEngraver, so the row under-reserved by exactly it. Retired 2026-08-23:
        // a track shown both ways is placed twice, and each ROW reserves for its own line.
        foreach (var c in layout.ChordNameLayouts)
        {
            if (!sysTopYUp.TryGetValue(c.MeasureIndex, out var syUp)) continue;
            // Page Y-up: this measure's system top plus the stored offset.
            double cy = syUp + c.YUp;
            using (gc.Source(c.SourcePosition))
            {
                foreach (var piece in
                         LilySharp.Core.Svg.Layout.ChordNameGlyphRun.Pieces(fonts, c.ChordText))
                {
                    if (piece.IsGlyph)
                        gc.DrawGlyph(piece.Glyph, c.X + piece.DrawX, cy + piece.Raise, glyphEm);
                    else
                        gc.DrawText(piece.Text, c.X + piece.X, cy, size, TextRole.ChordName,
                            style, TextAnchor.Start, Color.Black);
                }
            }
        }
    }

    // ---------- Figured bass ----------

    /// <summary>
    /// Draws figured-bass numerals stacked vertically below the staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc:269 process_music / :157 stop_translation_timestep
    /// LILYPOND-REF: scm/define-grobs.scm:352-364 BassFigure — <c>ly:text-interface::print</c>
    /// over <c>\number</c> markup, i.e. Emmentaler's fetaText number glyphs with this grob's
    /// <c>font-features</c> applied. The glyph, its advance and its ink all come from
    /// <see cref="FiguredBassGlyphRun"/>, the same house the reservation reads — before
    /// 2026-07-30 this drew a SERIF face at an em of its own (3.0 ss, whose digit ink was
    /// 2.112000 against LilyPond's 1.124795235605315), which is why the digits reached
    /// 0.112 through the stem the row was placed below.
    /// ⚠️ THE ROW STEP IS NOT SPELLED HERE ANY MORE. It was a local 1.5 against the
    /// engraver's reserved 1.6 — one quantity, two spellings, and neither LilyPond's
    /// (HANDOFF §5.2.1②; the ledger's <c>figbass.upper-staff.staff-gap</c> carried the
    /// +0.600000 the pair of them cost). The offsets now come from
    /// <see cref="BassFigureAlignment.RowOffsets"/> through the layout, i.e. from the grob
    /// that does the stacking (scm/define-grobs.scm:366-385).
    /// </remarks>
    private static void DrawFiguredBass(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.FiguredBassLayouts.IsDefaultOrEmpty) return;
        double size = FiguredBassGlyphRun.Em;
        // The design the metrics came out of, opened once for the whole pass — a figure's
        // 11.2246 pt lands on emmentaler-11 and the two halves of that claim must not be
        // decided separately (IDrawingContext.MusicFace).
        using var face = gc.MusicFace(FiguredBassGlyphRun.Design);
        foreach (var fb in layout.FiguredBassLayouts)
        {
            if (!sysTopYUp.ContainsKey(fb.MeasureIndex)) continue;
            // Page Y-up against this figure's own staff middle; figures then stack
            // downward from the topmost baseline (device down = smaller Y-up).
            double baseY = os.StaffMiddleYUp(fb.StaffIndex, fb.MeasureIndex, StaffHeight) + fb.YUp;
            using (gc.Source(fb.SourcePosition))
            {
                for (int i = 0; i < fb.FigureTexts.Length; i++)
                {
                    string text = fb.FigureTexts[i];
                    // The alignment already placed this row (FiguredBassEngraver.StackRows,
                    // which every layout goes through). Indexed rather than guarded: a layout
                    // without offsets is a bug in the producer, and a fallback here would draw
                    // the rows on top of each other while looking like it worked.
                    double y = baseY - fb.RowOffsets[i];
                    // LEFT-ALIGNED ON THE NOTEHEAD'S INK LEFT, which is where LilyPond puts it:
                    // figures are left-aligned in a BassFigureLine (scm/define-grobs.scm:366-374
                    // BassFigureAlignment stacks the lines; the figure itself is a rhythmic
                    // grob at its column's X).
                    // ⚠️ "THE HEAD'S LEFT", NOT "THE COLUMN'S LEFT", and the two are not the
                    // same edge — the first wording here was an over-claim read off a book
                    // where the NoteHead, the Stem and the NoteColumn all reported ONE box
                    // left, so it could not tell which of them the figure was on. MEASURED on
                    // the case that separates them (scratch/figbass-accidental-x.ly, a bass
                    // cis with figures): the Accidental sits 1.450000 further left (8.003400
                    // against 9.453400) and the BassFigure does NOT follow it. An accidental
                    // is in an AccidentalPlacement group beside the column, not inside its X
                    // extent, so the column's own left edge stays the head's — which is why
                    // fb.X, a note anchor, is the right thing to draw at here.
                    // ⚠️ IT WAS CENTRED until 2026-08-11 — <c>fb.X − Width/2</c>, inherited from
                    // an older TextAnchor.Middle draw and kept while nothing watched it. What
                    // opened it is ledger figbass.alone.head-anchor-to-box-left, whose LilyPond
                    // side is an IDENTITY: the NoteHead, the Stem, the NoteColumn and the
                    // BassFigure share one box left to fifteen digits in every column of every
                    // book of probes/figured-bass-placement.ly. Lily# read −0.448984819 there,
                    // which is half the run and NOTHING ELSE — so the whole defect was this
                    // term, and the run's WIDTH (still the 20 design's tabular 1.600 unhinted
                    // against LilyPond's emmentaler-11 1.656 hinted) no longer reaches the
                    // drawn X at all. That width stays as it is until something watches it.
                    double x0 = fb.X;
                    // LILYPOND-REF: lily/modified-font-metric.cc:125-143 text_stencil — the
                    // glyphs of the run at their own advances, which FiguredBassGlyphRun has
                    // already accumulated; the reservation reads the same house.
                    foreach (var piece in FiguredBassGlyphRun.Pieces(text))
                    {
                        if (piece.IsGlyph)
                            gc.DrawGlyph(piece.Ch, x0 + piece.X, y, size, Color.Black);
                        else
                            gc.DrawText(piece.Ch.ToString(), x0 + piece.X, y, size, TextRole.FiguredBass,
                                FontStyle.Regular, TextAnchor.Start, Color.Black);
                    }
                }
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
    private static void DrawPercentRepeats(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.PercentRepeatLayouts.IsDefaultOrEmpty) return;
        const double thickness = 0.48;
        // The `dots.dot` glyph is a CIRCLE, and it does not scale with the staff: the same
        // path with the same scale appears on the notation staff and on the 1.5-spaced
        // TabStaff of one book (measured 2026-08-28, the two-bar percent twin). Only its
        // OFFSETS scale. The radius is the glyph's own half extent, which the repeat
        // barline's dots read from the same place.
        // ⚠️ NOT 0.224, WHICH IS WHAT LILYPOND'S SVG SEEMS TO SAY. That file prints the
        // glyph at `scale(0.0040)` over a 56-unit radius, but the 0.0040 is ROUNDED to four
        // decimals: the true scale is 0.225/56 = 0.00401786. Reading the size back out of a
        // rendered SVG is one decimal short of the font, and this constant was 0.224 for a
        // few hours on 2026-08-28 because of it.
        const double dotRadius = EngravingDefaults.RepeatDotRadius;
        // dot-negative-kern and slash-negative-kern: the paddings x_percent and brew_slash
        // hand to add_at_edge, both scaled by the staff space.
        // LILYPOND-REF: scm/define-grobs.scm — the PercentRepeat and DoublePercentRepeat
        //   entries: dot-negative-kern 0.75, slash-negative-kern 1.6, slope 1.0,
        //   thickness 0.48. Range-less: the grob names are one word each.
        const double dotKern = 0.75;

        foreach (var pr in layout.PercentRepeatLayouts)
        {
            if (!sysTopYUp.ContainsKey(pr.MeasureIndex)) continue;
            // LILYPOND-REF: lily/percent-repeat-interface.cc:40-49 brew_slash —
            // "Scale everything by staff-space": wid = 2.0/slope·ss and
            // thick = thickness·ss; :69-77 x_percent translates each dot ±0.5·ss.
            // A TabStaff sets StaffSymbol.staff-space = 1.5, so its sign is
            // one-and-a-half-sized.
            var staff = os.StaffLayoutOf(pr.StaffIndex, pr.MeasureIndex);
            double ss = staff?.Tuning is { } tuning
                ? EngravingDefaults.TabStringSpace(Tunings.GetStringCount(tuning))
                : 1.0;
            // FOUR GROBS SHARE THIS DRAWING and they differ in three numbers: how many
            // slashes, how steep, and how hard the copies overlap. The plain beat slash is
            // the odd one — steeper (1.7) and more tightly kerned (0.85) than the percent
            // family, and it carries NO dots.
            // LILYPOND-REF: scm/define-grobs.scm — the RepeatSlash entry (slope 1.7,
            //   slash-negative-kern 0.85) against the DoubleRepeatSlash entry (slope 1.0,
            //   slash-negative-kern 1.6, dot-negative-kern 0.75), which is the picture the
            //   PercentRepeat / DoublePercentRepeat pair already draws. Range-less like the
            //   neighbouring citation: the grob names are one word each (HANDOFF §5.2.1⑦).
            // LILYPOND-REF: lily/percent-repeat-interface.cc:107-121 beat_slash — count 0
            //   draws x_percent (me, 2), i.e. WITH dots, and any other count brew_slash
            //   (me, count), i.e. without.
            bool plainSlash = pr.IsBeatSlash && pr.SlashCount >= 1;
            double slope = plainSlash ? 1.7 : 1.0;
            double slashKern = plainSlash ? 0.85 : 1.6;
            int slashes = pr.IsBeatSlash
                ? (pr.SlashCount >= 1 ? pr.SlashCount : 2)
                : (pr.IsDouble ? 2 : 1);
            double slashWidth = 2.0 / slope * ss;
            double slashHeight = slashWidth * slope;
            double thick = thickness * ss;
            // THE SLASH IS A PARALLELOGRAM, NOT A STROKED LINE, and the difference is the
            // ENDS: LilyPond cuts them HORIZONTALLY, so the shape's height is exactly
            // `wid·slope` and its width `wid + x_width`. A stroked line of the same
            // perpendicular thickness cuts them square to the slope instead, which on a
            // 45° slash pushes each corner out by thick/(2√2) in BOTH axes — the ink comes
            // out 0.509 too tall and 0.51 too narrow on a TabStaff (ss 1.5), and a user
            // reported the tab sign as looking too thick. It is not: the perpendicular
            // thickness was right all along (0.720 = LP's, measured), the outline was not.
            // LILYPOND-REF: lily/lookup.cc:519-539 repeat_slash — the four points are
            //   (0,0) (x_width,0) (x_width+w,height) (w,height) with
            //   x_width = hypot (t, t/s) and height = w·s, and the box is (0, w + x_width).
            double xWidth = System.Math.Sqrt(thick * thick + thick / slope * (thick / slope));
            double slashInk = slashWidth + xWidth;
            // EVERY COPY BEYOND THE FIRST is added at the group's right edge with a NEGATIVE
            // padding, so consecutive origins end up (slash ink width − kern·ss) apart. ZERO
            // for a single slash, which then draws exactly one.
            // LILYPOND-REF: lily/percent-repeat-interface.cc:37-60 brew_slash — the
            //   `for (int i = count - 1; i--;) add_at_edge (X_AXIS, RIGHT, slash,
            //   -slash_neg_kern)` loop. It is a LOOP, not a pair: a sixteenth-note beat slash
            //   asks for two and nothing stops a thirty-second asking for three.
            double pairGap = slashes > 1 ? slashInk - slashKern * ss : 0.0;
            double groupWidth = slashInk + (slashes - 1) * pairGap;
            // ⚠️ A BEAT SLASH IS NOT CENTRED ON ITS X: beat_slash returns brew_slash's
            // stencil as it stands, where double_percent re-aligns to CENTRE. So the layout's
            // X is this group's LEFT EDGE, and the percent family's is its middle.
            // LILYPOND-REF: lily/percent-repeat-interface.cc:96-101 double_percent — the
            //   align_to (X_AXIS, CENTER) that :107-121 beat_slash does not do.
            double left = pr.IsBeatSlash ? pr.X : pr.X - groupWidth / 2;
            // Page Y-up against this sign's own staff middle — the staff's REAL
            // height (a six-string tab's lines span 7.5, not the nominal 4.0; the
            // grob has no Y-offset and its stencil is align_to'd CENTER on it).
            double cy = os.StaffMiddleYUp(pr.StaffIndex, pr.MeasureIndex,
                staff?.Height ?? StaffHeight) + pr.YUp;
            // The dots sit at the EDGES OF THE WHOLE SLASH GROUP, overlapping it by
            // dot-negative-kern: the upper one's RIGHT edge lands 0.75·ss inside the group's
            // left, the lower one's LEFT edge 0.75·ss inside its right. That is the term the
            // old code approximated as a constant 0.25·ss + r from the centre and got 0.19–0.3
            // wrong; the edge form reproduces LP's measured 0.81·(ss=1) / 1.11·(ss=1.5) for a
            // single sign, and 1.354 / 1.919 for a double, to three decimals.
            // LILYPOND-REF: lily/percent-repeat-interface.cc:63-81 x_percent — the two
            //   add_at_edge (X_AXIS, LEFT/RIGHT, d, -dot_neg_kern) calls, and the ±0.5·ss
            //   translate_axis that puts the upper dot LEFT and the lower one RIGHT.
            double upperDotCx = left + dotKern * ss - dotRadius;
            double lowerDotCx = left + groupWidth - dotKern * ss + dotRadius;
            double dotDy = 0.5 * ss;
            using (gc.Source(pr.SourcePosition))
            {
                // Bottom-left to top-right, with a dot in each pocket the slash leaves —
                // upper-LEFT and lower-RIGHT, like the "%" glyph. The double sign draws the
                // parallelogram twice, the pair centred on the bar line.
                for (int k = 0; k < slashes; k++)
                {
                    double x0 = left + k * pairGap;
                    double bottom = cy - slashHeight / 2;
                    double top = cy + slashHeight / 2;
                    gc.DrawFilledQuad(
                        (x0, bottom), (x0 + xWidth, bottom),
                        (x0 + xWidth + slashWidth, top), (x0 + slashWidth, top),
                        Color.Black);
                }
                // ⚠️ THE PLAIN BEAT SLASH HAS NO DOTS. Its stencil is brew_slash's, and the
                // dots are added by x_percent, which only the count-0 (mixed durations) beat
                // slash and the two percent signs go through.
                if (!plainSlash)
                {
                    gc.DrawCircle(upperDotCx, cy + dotDy, dotRadius, Color.Black);
                    gc.DrawCircle(lowerDotCx, cy - dotDy, dotRadius, Color.Black);
                }
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
    private static void DrawBarNumbers(ScoreLayout layout, Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.BarNumberLayouts.IsDefaultOrEmpty) return;
        // LILYPOND-REF: scm/define-grobs.scm BarNumber (font-size . -2) —
        // 2.2sp text height × magstep(-2); see BarNumberEngraver.FontSize.
        double fontSize = BarNumberEngraver.FontSize;
        // Collisions with voltas/marks are resolved by the unified
        // outside-staff stacking pass (OutsideStaffStacker.StackAboveStaff).
        foreach (var bn in layout.BarNumberLayouts)
        {
            if (!sysTopYUp.TryGetValue(bn.MeasureIndex, out var syUp))
                continue; // other page
            // Page Y-up: this measure's system top plus the stored offset.
            double y = syUp + bn.YUp;
            gc.DrawText(bn.Text, bn.X, y, fontSize, TextRole.BarNumber,
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
    private static void DrawStanzaNumbers(ScoreLayout layout, Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.StanzaNumberLayouts.IsDefaultOrEmpty) return;
        const double fontSize = 2.4;
        foreach (var sn in layout.StanzaNumberLayouts)
        {
            // sn.YUp is Y-up from the system top (the verse's lyric baseline); lift
            // the system top to the page Y-up frame and add it, like DrawLyrics.
            if (!sysTopYUp.TryGetValue(sn.MeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift the system top and add the stored offset, like DrawLyrics.
            double y = syUp + sn.YUp;
            gc.DrawText(sn.Text, sn.X, y, fontSize, TextRole.Stanza,
                FontStyle.Bold, TextAnchor.Start, Color.Black);
        }
    }

    // ---------- Fingering ----------

    /// <summary>
    /// Draws fingering numerals next to noteheads — the fetaText digit run at
    /// font-size −5, centred on the layout's X the way the profile the script column
    /// stacked was measured (<c>FingeringEngraver.DigitRun</c> — one metric home for pen
    /// and reservation).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1547-1568 Fingering (self-alignment-interface
    ///   family) — font-encoding fetaText, font-features cv47 ss01, font-size −5; its
    ///   stencil is ly:text-interface::print (:1559).
    /// ⚠️ IT WAS A SERIF DIGIT AT 0.56 EM until 2026-08-08 — about half LilyPond's
    /// ink (feta numerals are 2.0 design-ss tall), so a bow stacked over a fingering
    /// sat half a space low. Like the figured bass, the run does not scale on an
    /// ossia staff (the two halves — pen here, metric in DigitRun — stay together).
    /// ⚠️ AND IT DREW THE FIGURED BASS'S DIGITS until 2026-08-11 — the tabular cut, out of
    /// the default design. A Fingering declares font-features WITHOUT <c>tnum</c>, so the
    /// glyphs are the proportional ones, and its 11.2246 pt lands on <c>emmentaler-11</c>:
    /// hence the face scope below. <see cref="FingeringGlyphRun"/> holds both halves of that
    /// claim, and <c>DigitRun</c> has already applied the metric half.
    /// </remarks>
    /// <summary>⒭ overlay fragment: the score's fingerings bucketed per page, ONCE per
    /// render. This replaces the old per-page filter over the whole array — that scan
    /// was O(pages × fingerings) and, measured (session 160), it and the per-item staff
    /// resolution were the drawer's real cost, so a fragment key that re-ran them per
    /// page cost as much as the draw it replaced (the session-154 rule: a memo's key
    /// must be cheaper than the walk it saves). Null when the score has none; a page
    /// with no fingerings gets an empty list (the draw still opens the face scope,
    /// preserving the pre-change bytes).</summary>
    private static List<FingeringLayout>[]? GroupFingeringsByPage(ScoreLayout layout)
    {
        if (layout.FingeringLayouts.IsDefaultOrEmpty) return null;
        var pages = layout.Pages;
        int maxMeasure = -1;
        foreach (var p in pages)
            foreach (var s in p.Systems)
                if (!s.Measures.IsDefaultOrEmpty && s.Measures.Length > 0)
                    maxMeasure = Math.Max(maxMeasure, s.Measures[^1].MeasureIndex);
        var measureToPage = new int[maxMeasure + 1];
        Array.Fill(measureToPage, -1);
        for (int pi = 0; pi < pages.Length; pi++)
            foreach (var s in pages[pi].Systems)
            {
                if (s.Measures.IsDefaultOrEmpty)
                    continue;
                foreach (var m in s.Measures)
                    if (m.MeasureIndex >= 0 && m.MeasureIndex <= maxMeasure)
                        measureToPage[m.MeasureIndex] = pi;
            }
        var byPage = new List<FingeringLayout>[pages.Length];
        for (int i = 0; i < byPage.Length; i++)
            byPage[i] = new List<FingeringLayout>();
        foreach (var f in layout.FingeringLayouts)
        {
            int pi = f.MeasureIndex >= 0 && f.MeasureIndex <= maxMeasure
                ? measureToPage[f.MeasureIndex] : -1;
            if (pi >= 0)
                byPage[pi].Add(f);
        }
        return byPage;
    }

    /// <summary>
    /// The page's overlay-fragment value key: everything the emitted bytes are a
    /// function of EXCEPT source positions (those shift with the edit window and ride
    /// the anchor/slot machinery). Deliberately folds RAW inputs, not resolved x/y —
    /// resolving per item is the cost the fragment exists to avoid (session-154 rule).
    /// The inventory, each read traced: an item's emission is
    /// f(Number → DigitRun/Pieces, X, YUp, its staff middle) where the staff middle is
    /// system.Y + staff.Y(StaffIndex) − StaffHeight/2 (<see
    /// cref="LayoutUtilities.ResolveStaffMiddleY"/> via <see
    /// cref="OssiaShrink.StaffMiddleYUp"/>; the ossia branch of <see
    /// cref="OssiaShrink.YUp"/> is unreachable here — an ossia score disables the whole
    /// cache). So the fold covers: page height (the Y-flip bakes it into every device
    /// Y), each system's Y + measure range (the measure→system MAPPING — two systems
    /// can keep their Ys while trading a measure) + staff table (StaffIndex, Y), and
    /// each item's MeasureIndex/Number/X/YUp/StaffIndex.
    /// </summary>
    private static (long Hash, int[] Anchors) FoldFingeringPage(
        List<FingeringLayout> pageItems, PageLayout page)
    {
        var hc = new MeasureContentKey.Hash64();
        hc.Add(page.Height);
        foreach (var system in page.Systems)
        {
            hc.Add(system.Y);
            if (!system.Measures.IsDefaultOrEmpty && system.Measures.Length > 0)
            {
                hc.Add(system.Measures[0].MeasureIndex);
                hc.Add(system.Measures[^1].MeasureIndex);
            }
            if (!system.StaffGroups.IsDefaultOrEmpty)
                foreach (var g in system.StaffGroups)
                    foreach (var st in g.Staves)
                    {
                        hc.Add(st.StaffIndex);
                        hc.Add(st.Y);
                    }
        }
        hc.Add(pageItems.Count);
        var anchors = new int[pageItems.Count];
        for (int i = 0; i < pageItems.Count; i++)
        {
            var f = pageItems[i];
            hc.Add(f.MeasureIndex);
            hc.Add(f.Number);
            hc.Add(f.X);
            hc.Add(f.YUp);
            hc.Add(f.StaffIndex);
            anchors[i] = f.SourcePosition;
        }
        return (hc.ToHashCode(), anchors);
    }

    /// <summary>⒭ overlay fragment wiring for the one drawer that measured as the
    /// overlay term of the keystroke render floor (HANDOFF ▶ ⒭ — DrawFingerings is the
    /// largest page-level drawer; the rest measured ≈ 0 and stay live). Replays the
    /// recorded page output when the value fold and anchors match, else draws live
    /// under a capture. Non-SVG backends draw the page's bucket directly.</summary>
    private static void DrawFingerings(List<FingeringLayout>? pageItems, in OssiaShrink os,
        IDrawingContext gc, SvgDocumentContext? fragHost,
        SvgSystemFragmentCache? fragments, int pageIndex, PageLayout page)
    {
        if (pageItems == null)
            return;
        if (fragHost == null)
        {
            DrawFingeringsLive(pageItems, os, gc);
            return;
        }
        var (hash, anchors) = FoldFingeringPage(pageItems, page);
        if (fragments!.TryReplayOverlay(OverlayDrawerId.Fingerings, pageIndex, hash, anchors, fragHost))
            return;
        using (fragments.BeginOverlayCapture(OverlayDrawerId.Fingerings, pageIndex, hash, anchors, fragHost))
            DrawFingeringsLive(pageItems, os, gc);
    }

    private static void DrawFingeringsLive(List<FingeringLayout> pageItems,
        in OssiaShrink os, IDrawingContext gc)
    {
        double size = FingeringGlyphRun.Em;
        // The design the metrics came out of, opened once for the whole pass: every
        // fingering in a score is at the same font-size, so this scope never nests.
        using var face = gc.MusicFace(FingeringGlyphRun.Design);
        foreach (var f in pageItems)
        {
            // Frame B -> device: reflect the Y-up baseline against this fingering's
            // own staff middle (the shared per-grob draw boundary), then apply ossia.
            double midYup = os.StaffMiddleYUp(f.StaffIndex, f.MeasureIndex, StaffHeight);
            double y = os.YUp(midYup + f.YUp, f.StaffIndex, f.MeasureIndex);
            // The metric home is DigitRun (memoized for the ten single digits — the
            // preview redraws this every frame); a single-glyph run draws without
            // building a pieces array, the multi-glyph rarity walks the run.
            var (glyphs, _, width) = FingeringEngraver.DigitRun(f.Number);
            double x0 = f.X - width / 2.0;
            using (gc.Source(f.SourcePosition))
            {
                if (glyphs.Length == 1)
                    gc.DrawGlyph(glyphs[0], x0, y, size, Color.Black);
                else
                    foreach (var piece in FingeringGlyphRun.Pieces(f.Number.ToString()))
                    {
                        if (piece.IsGlyph)
                            gc.DrawGlyph(piece.Ch, x0 + piece.X, y, size, Color.Black);
                        else
                            gc.DrawText(piece.Ch.ToString(), x0 + piece.X, y, size, TextRole.Fingering,
                                FontStyle.Regular, TextAnchor.Start, Color.Black);
                    }
            }
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
    private static void DrawMusicMarks(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.MusicMarkLayouts.IsDefaultOrEmpty) return;
        foreach (var m in layout.MusicMarkLayouts)
        {
            if (IsHandledBySpannerEngraver(m.MarkType)) continue;
            if (!sysTopYUp.ContainsKey(m.MeasureIndex)) continue; // other page
            // The mark's page Y-up anchor: the staff middle's Y-up refpoint plus the
            // stored offset. Marks do not shrink on an ossia staff (StaffMiddleYUp is
            // not run through the ossia affine), so no ossia affine is applied here.
            // ⚠️ THE -1 SENTINEL IS RESOLVED HERE TOO, to the SAME staff the stacker priced
            // the mark against (LayoutUtilities.TopScoreGrobStaff). Unresolved it reaches
            // StaffOffsetInSystemUp's `staffIndex >= 0` guard and answers 0 — the SYSTEM
            // TOP — which is the staff's top line only while the system's first element IS
            // a staff. With a chords row leading, the mark was drawn a whole row band above
            // the line its room was reserved on (user report, session 243).
            double yUp = os.ScoreGrobStaffMiddleYUp(m.StaffIndex, m.MeasureIndex, StaffHeight)
                + m.YUp;
            using (gc.Source(m.SourcePosition))
                DrawSingleMusicMark(fonts, m, yUp, gc);
        }
    }

    /// <summary>
    /// Draws the swing/shuffle feel equation beside a tempo mark: two beamed straight
    /// notes "=" a beamed dotted + plain note under a triplet "3". <paramref name="subdivision"/>
    /// picks the note value — 8 = eighths (single beam), 16 = sixteenths (double beam).
    /// Hand-built from the same notehead/stem/beam primitives the metronome mark uses.
    /// </summary>
    private static void DrawSwingEquation(ScoreTextMetrics fonts, IDrawingContext gc,
        double startX, double baselineY, int subdivision)
    {
        int beams = subdivision >= 16 ? 2 : 1;
        const double beamGap = 0.3;          // spacing between the two beams of a 16th
        // LILYSHARP-OWN sizes: the feel equation keeps the small chart-style note (1.6)
        // its head-gap/stem/beam constants were tuned for; a beam scaled to that small
        // note (0.48 staff-beam x 1.6/FontSize) rather than the full staff-beam
        // thickness, which read as too heavy here.
        const double ns = SwingNoteSize;
        const double headGap = 1.0;          // x between the two heads of a pair
        const double stemUp = 1.4;           // stem height (tuned to ns)
        const double stemDx = ns * 0.32;     // stem offset from head origin (right side)
        const double stemW = 0.09;
        const double beamW = EngravingDefaults.BeamThickness * (ns / FontSize);
        const double eqSize = 1.8;           // the feel equation's own "=" size
        const FontStyle EqStyle = FontStyle.Regular;  // one home: drawn AND stepped past
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
                gc.DrawText("3", midX, brkY - 0.35, threeSize, TextRole.Tempo,
                    FontStyle.Bold, TextAnchor.Middle, Color.Black);
            }
            return s2 + ns * 0.35;
        }

        double x = DrawPair(startX, dotted: false, withThree: false);
        x += 0.35;
        gc.DrawText("=", x, baselineY, eqSize, TextRole.Tempo, EqStyle, TextAnchor.Start, Color.Black);
        // The step past the "=" measures the "=" that was drawn. It measured Bold against a
        // Regular draw until 2026-08-18; ⚠️ the page did NOT move, and that is a fact about
        // this face rather than a licence to leave such a pair alone — TeX Gyre Schola gives
        // "=" the SAME advance bold and regular (1.092585827 at this 1.8 em, to fifteen
        // digits), so the drift had nothing to show. A face where they differ would have.
        x += fonts.Advance("=", eqSize, TextRole.Tempo, EqStyle) + 0.45;
        DrawPair(x, dotted: true, withThree: true);
    }

    // absY is the mark's anchor in the page Y-up frame (page-bottom origin, up
    // positive), matching LilyPond's native sign convention: offsets that move a
    // sub-element visually UP the page ADD, device-downward offsets SUBTRACT. The
    // page context is Y-up, so every coordinate is emitted directly.
    private static void DrawSingleMusicMark(ScoreTextMetrics fonts, MusicMarkLayout m,
        double absY, IDrawingContext gc)
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
            // The metronome markup, drawn as LilyPond builds it: a concat of
            // [bold marking " ("] (general-align Y DOWN \smaller note) " = " count [")"],
            // all in the mark's plain upright text font (em 2.2) — only the textual
            // marking is \bold. The note's DOWN alignment puts its ink BOTTOM on the
            // equation baseline. All geometry comes from MetronomeMarkGeometry, the one
            // home the engraver and the stacker price the same mark from.
            // LILYPOND-REF: scm/translation-functions.scm:100-151 format-metronome-markup / metronome-markup;
            // scm/define-markup-commands.scm:5393-5650 note-by-number.
            double em = EngravingDefaults.MetronomeMarkFontSize;
            double noteSize = MetronomeMarkGeometry.NoteSize;
            double s = MetronomeMarkGeometry.NoteScale;
            double x = m.X;
            bool hasMetronome = m.Text.Length > 0;
            if (m.TempoText != null)
            {
                gc.DrawText(m.TempoText, x, absY, em,
                    TextRole.Tempo, FontStyle.Bold, TextAnchor.Start, Color.Black);
                if (!hasMetronome)
                    return;
                x += fonts.Advance(m.TempoText, em, TextRole.Tempo, FontStyle.Bold);
                // The concat's " (" — one run; its leading space carried as the
                // single-run offset so no backend collapses it.
                gc.DrawText("(", x + MetronomeMarkGeometry.LeadingSpaceAdvance(fonts, "("), absY, em,
                    TextRole.Tempo, FontStyle.Regular, TextAnchor.Start, Color.Black);
                x += fonts.Advance(" (", em, TextRole.Tempo);
            }
            // Beat-unit note: whole (1) = stemless whole head; 2 = hollow
            // half with stem; 4+ = black head, stem, flags from the 8th up.
            char head = MetronomeMarkGeometry.HeadGlyph(m.TempoBeatUnit);
            var headBox = MetronomeMarkGeometry.HeadBox(m.TempoBeatUnit);
            int log = MetronomeMarkGeometry.Log(m.TempoBeatUnit);
            // DOWN-aligned: the head's ink bottom on the baseline, so its centre rides
            // half a (scaled) head above it. Up = larger Y-up.
            double headY = absY - headBox.Bottom * s;
            gc.DrawGlyph(head, x, headY, noteSize);
            double headRight = headBox.Right * s;
            if (log > 0)
            {
                // note-by-number's up stem: lower-RIGHT corner on the head's attachment
                // point (the font's LILC datum — X is the head's right edge, Y a little
                // above its centre), rising to stemy = magstep x max(3, log-1) above
                // the head's origin line.
                double stemTh = MetronomeMarkGeometry.StemThickness;
                var att = MetronomeMarkGeometry.StemAttachment(m.TempoBeatUnit);
                double stemX = x + att.X * s - stemTh / 2;
                double stemBottom = headY + att.Y * s;
                double stemTop = headY
                    + MetronomeMarkGeometry.StemTopAboveCentre(m.TempoBeatUnit);
                gc.DrawLine(stemX, stemBottom, stemX, stemTop, Color.Black, stemTh);
                if (log >= 3)
                    gc.DrawGlyph(EmmentalerGlyphs.Flag8thUp, stemX, stemTop, noteSize);
            }
            // Dot run per note-by-number: one dotwid past the head's ink right,
            // 2 x dotwid apart, on the head's own line (dots-direction 0); an
            // up-stem flagged unit shifts the run +0.5 to clear the flag. The
            // arithmetic lives in MetronomeMarkGeometry.DotX.
            for (int d = 0; d < m.TempoDots; d++)
                gc.DrawGlyph(EmmentalerGlyphs.AugmentationDot,
                    x + MetronomeMarkGeometry.DotX(m.TempoBeatUnit, d), headY, noteSize);
            // " = N" — one run at the note's ink right; the leading space is the
            // concat's separator, carried as the single-run offset.
            string equation = MetronomeMarkGeometry.EquationText(
                m.Text, m.TempoText != null);
            double eqX = x
                + MetronomeMarkGeometry.NoteRight(m.TempoBeatUnit, m.TempoDots)
                + MetronomeMarkGeometry.LeadingSpaceAdvance(fonts, equation);
            gc.DrawText(equation, eqX, absY,
                em, TextRole.Tempo, FontStyle.Regular, TextAnchor.Start, Color.Black);
            if (m.SwingSubdivision != 0)
            {
                double textEnd = eqX + fonts.Advance(equation, em, TextRole.Tempo);
                // DrawSwingEquation draws in the page Y-up frame; hand it the Y-up baseline.
                DrawSwingEquation(fonts, gc, textEnd + 0.8, absY, m.SwingSubdivision);
            }
            return;
        }
        if (m.MarkType == MusicMarkType.Rehearsal || m.MarkType == MusicMarkType.SectionLabel)
        {
            double fs = m.MarkType == MusicMarkType.Rehearsal ? FontSize * 0.6 : FontSize * 0.55;
            // The frame's padding has ONE home, shared with the two sites that RESERVE for
            // this box (MusicMarkEngraver.GetMarkHalfExtent and MarkXExtent) — and with
            // LabelBaselineBelowCentre, which is how the engraver knows where this call puts
            // the baseline when it wants the label on a chord row's own line.
            const double pad = MusicMarkEngraver.LabelBoxPadding;
            double textWidth = fonts.Advance(m.Text, fs, TextRole.Mark, FontStyle.Bold);
            double boxW = textWidth + pad * 2;
            double boxH = fs + pad * 2;
            // DrawRectangle's y is the visual-top edge (Y-up): anchor + half the box.
            gc.DrawRectangle(m.X - boxW / 2, absY + boxH / 2, boxW, boxH,
                fill: Color.White, stroke: Color.Black, strokeWidth: EngravingDefaults.LineThickness);
            gc.DrawText(m.Text, m.X, absY - fs / 2 + pad, fs, TextRole.Mark,
                FontStyle.Bold, TextAnchor.Middle, Color.Black);
            return;
        }
        if (IsPedalMark(m.MarkType))
        {
            // The SUSTAIN pedal's word is not a word: LilyPond sets it in the MUSIC font
            // (lily/sustain-pedal.cc:47-76), and MusicMarkEngraver.SustainPedalStencil is
            // the one home that reproduces that run — the same call every reservation
            // makes, so the drawn glyphs and the reserved box cannot drift. The sostenuto
            // and una corda words ARE text in LilyPond and keep the text path.
            if (MusicMarkEngraver.IsGlyphPedal(m.MarkType))
            {
                var (pedalGlyphs, pedalWidth, _) =
                    MusicMarkEngraver.SustainPedalStencil(m.Text);
                double pedalLeft = m.X - pedalWidth / 2;   // the mark's anchor is its centre
                foreach (var g in pedalGlyphs)
                    gc.DrawGlyph(g.Glyph, pedalLeft + g.X, absY, FontSize, Color.Black);
                return;
            }
            gc.DrawText(m.Text, m.X, absY, MusicMarkEngraver.PlainTextFontSize, TextRole.Pedal,
                MusicMarkEngraver.TextStyleOf(m.MarkType), TextAnchor.Middle, Color.Black);
            return;
        }
        if (m.MarkType == MusicMarkType.ToCoda)
        {
            // "To" followed by the coda SIGN (not the word "Coda"), centered as a
            // group. LILYPOND-REF: the al-coda text is set with the coda glyph.
            double ts = MusicMarkEngraver.PlainTextFontSize;
            double gs = FontSize * 0.8;
            const string prefix = "To ";
            // The centring measures what the next line draws — one composition, read
            // from the one home (ToCodaStencilWidths), which the union placement's
            // reservation reads too, so the box and the ink cannot drift apart. (It
            // measured Bold against a BoldItalic draw until 2026-08-18, which put the
            // pair 0.068286614 staff spaces left of centre.)
            var style = MusicMarkEngraver.TextStyleOf(MusicMarkType.ToCoda);
            var (textW, glyphW) = MusicMarkEngraver.ToCodaStencilWidths(fonts);
            double left = m.X - (textW + glyphW) / 2;
            gc.DrawText(prefix, left, absY, ts, TextRole.Navigation,
                style, TextAnchor.Start, Color.Black);
            // The coda glyph's baseline sits low; lift it (up = larger Y-up) so its
            // centre aligns with the cap height of "To".
            gc.DrawGlyph(EmmentalerGlyphs.MarkCoda, left + textW, absY + gs * 0.30, gs, Color.Black);
            return;
        }
        // Default text marks (D.S./D.C./Fine/etc.) — size and style from the one home the
        // reservations read, so the box and the ink cannot drift apart again.
        gc.DrawText(m.Text, m.X, absY, MusicMarkEngraver.PlainTextFontSize, TextRole.Navigation,
            MusicMarkEngraver.TextStyleOf(m.MarkType), TextAnchor.Middle, Color.Black);
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
    private static void DrawCustomTexts(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.CustomTextLayouts.IsDefaultOrEmpty) return;
        foreach (var t in layout.CustomTextLayouts)
        {
            if (!sysTopYUp.ContainsKey(t.MeasureIndex)) continue; // other page
            // Page Y-up against the (top) staff middle this text resolves.
            double y = os.StaffMiddleYUp(t.StaffIndex, t.MeasureIndex, StaffHeight) + t.YUp;
            // TextScript declares no font-size, so the em is the paper's own text size —
            // 2.2 ss, one home with the stacker's reservation (was a Lily#-own 2.4).
            // LILYPOND-REF: scm/paper.scm:69-77 text-font-size (via EngravingDefaults).
            // START-anchored: t.X is the pen origin on the anchor note column's origin
            // (X-offset 0 — see CustomTextEngraver; ledger
            // textscript.x.pen-to-notehead-left). A centred draw here reads half an
            // advance off that entry.
            using (gc.Source(t.SourcePosition))
                gc.DrawText(t.Text, t.X, y, EngravingDefaults.TextScriptFontSize, TextRole.Text,
                    FontStyle.Italic, TextAnchor.Start, Color.Black);
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
    private static void DrawTextSpanners(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TextSpannerLayouts.IsDefaultOrEmpty) return;
        double textSize = LilySharp.Core.Svg.Layout.TextSpannerEngraver.TextFontSize;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var s in layout.TextSpannerLayouts)
        {
            if (!sysTopYUp.TryGetValue(s.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: system top plus the stored offset, then ossia.
            double absY = os.YUp(syUp + s.YUp, s.StaffIndex, s.StartMeasureIndex);
            using (gc.Source(s.SourcePosition))
            {
                gc.DrawText(s.Text, s.StartX, absY,
                    os.Size(textSize, s.StaffIndex), TextRole.Text,
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

    // Mixed style: how far past the note column the bracket line starts, clearing
    // the centred "Ped." text (its right half ≈ 1.9 ss) plus bound-padding (1.0).
    private const double MixedPedalTextClearance = 2.9;

    /// <summary>
    /// Draws piano pedal brackets: a below-staff line with an up hook at each end
    /// (bracket style) or a right hook only after the "Ped." text (mixed style).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/piano-pedal-bracket.cc — PianoPedalBracket grob
    /// </remarks>
    private static void DrawPedalBrackets(ScoreLayout layout, Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.PedalBracketLayouts.IsDefaultOrEmpty) return;
        // LILYPOND-REF: scm/define-grobs.scm PianoPedalBracket thickness = 1.0
        // (× line-thickness); edge-height (1.0 . 1.0), direction DOWN — the hooks
        // rise from the below-staff line back toward the staff.
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var b in layout.PedalBracketLayouts)
        {
            if (!sysTopYUp.TryGetValue(b.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: system top less the stored device offset.
            double absY = syUp - b.Y;
            // A pedal-change end is a flared edge (the "/\" notch); the horizontal
            // line stops PedalBracketFlare short of the note and the edge slants to
            // the note at edge height. An outer end is a straight vertical hook.
            // Mixed style keeps the leading "Ped." text, so its left end has no edge
            // and the line starts past the text. LILYPOND-REF: piano-pedal-bracket.cc.
            const double flare = EngravingDefaults.PedalBracketFlare;
            double top = absY + b.EdgeHeight; // hooks/notch rise toward the staff
            double lineStartX = b.IsMixed ? b.StartX + MixedPedalTextClearance
                              : b.StartChange ? b.StartX + flare : b.StartX;
            double lineEndX = b.EndChange ? b.EndX - flare : b.EndX;
            using (gc.Source(b.SourcePosition))
            {
                gc.DrawLine(lineStartX, absY, lineEndX, absY, Color.Black, thickness);
                // Right end: flared notch toward b.EndX (change) or vertical hook.
                if (b.EndChange)
                    gc.DrawLine(lineEndX, absY, b.EndX, top, Color.Black, thickness);
                else
                    gc.DrawLine(b.EndX, absY, b.EndX, top, Color.Black, thickness);
                // Left end: mixed has none; a change flares to b.StartX; else vertical.
                if (!b.IsMixed)
                {
                    if (b.StartChange)
                        gc.DrawLine(lineStartX, absY, b.StartX, top, Color.Black, thickness);
                    else
                        gc.DrawLine(lineStartX, absY, lineStartX, top, Color.Black, thickness);
                }
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
    private static void DrawMultiMeasureRests(ScoreLayout layout, Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.MultiMeasureRestLayouts.IsDefaultOrEmpty) return;
        foreach (var mmr in layout.MultiMeasureRestLayouts)
        {
            if (!sysTopYUp.TryGetValue(mmr.StartMeasureIndex, out double syUp))
                continue; // other page
            // mmr.Y is the within-system offset of the staff middle; syUp is this
            // measure's system-top page Y-up, so the middle's page Y-up is that minus
            // the offset (byte-identical to the former pageHeight - absoluteMiddle).
            double cy = syUp - mmr.Y;
            if (mmr.UseChurchRest)
                DrawChurchRest(mmr, cy, gc);
            else
                DrawBigRest(mmr, cy, gc);
        }
    }

    private static void DrawChurchRest(MultiMeasureRestLayout mmr, double cy, IDrawingContext gc)
    {
        double cx = (mmr.StartX + mmr.EndX) / 2.0;

        // Decomposition: 8 (maxima), 4 (longa), 2 (breve), 1 (whole) — church_rest's
        // loop starts at duration-log -3 and only ever increases it, emitting 2^-dl
        // measures while the remainder still covers it. With expand-limit 10 the maxima
        // appears only at counts 8, 9 and 10.
        // Use each rest glyph's REAL ink width (from the extracted font metrics) so
        // the centred row matches LilyPond's church_rest, which sums r.extent(X) for
        // each symbol. The block longa/breve rests are only ~0.6 ss wide; the whole
        // rest is 1.5 ss. LILYPOND-REF: lily/multi-measure-rest.cc church_rest.
        var pieces = new List<(int Span, char Glyph, double Width, double Y)>();
        double MaximaWidth = GlyphMetrics.RestMaximaWidth;
        double LongWidth = GlyphMetrics.RestLonga.Width;
        double BreveWidth = GlyphMetrics.RestDoubleWhole.Width;
        double WholeWidth = GlyphMetrics.RestWhole.Width;
        // Vertical placement (dy, in staff spaces below the staff middle cy — device
        // +Y is down). Each church-rest glyph sits at its own natural staff position
        // spi = Rest::staff_position_internal(me, dl, CENTER). For a normal 5-line staff
        // (line-positions {-4,-2,0,2,4}, neutral direction, default font-size 0 so the
        // dl<0 "(ss - fs)" term vanishes) that resolves to:
        //   whole  (dl= 0): spi = +2  → hangs from the 4th line (one line above middle)
        //   breve  (dl=-1): spi =  0  → sits on the middle line (ink fills the space above it)
        //   longa  (dl=-2): spi =  0  → centred on the middle line (ink spans ±1 space)
        //   maxima (dl=-3): spi =  0  → same as longa/breve. For duration_log < 0 the
        //     staff_position_internal else-branch snaps pos (0) to the nearest line at
        //     or below it, which is the middle line, with NO dependence on which
        //     negative duration log it is.
        // dy = -0.5 * spi converts a staff position to a device offset from cy.
        // Matches LilyPond 2.24 with \compressMMRests (verified by juxtaposition).
        // LILYPOND-REF: lily/rest.cc Rest::staff_position_internal; lily/multi-measure-rest.cc church_rest.
        int remaining = mmr.MeasureCount;
        foreach (var (span, glyph, width, dy) in new[]
        {
            (8, EmmentalerGlyphs.RestMaxima, MaximaWidth, 0.0),     // spi 0  → dy 0
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

        // Gap between symbols: LilyPond DERIVES it from the space actually available
        // between the bounding bar lines, so a long run's glyphs spread out instead of
        // huddling in the middle. mmr.StartX/EndX are those bar lines' inner edges,
        // i.e. exactly LilyPond's bar_width interval, so their difference is its `space`.
        // A fixed 0.4 ss here drew every church rest far too tight (an R1*9 row spanned
        // 3.7 ss where LilyPond spans 6.685).
        // LILYPOND-REF: lily/multi-measure-rest.cc:307-323 church_rest.
        const double OuterPaddingFactor = 1.5;
        const double MaxSymbolSeparation = 8.0;   // scm/define-grobs.scm MultiMeasureRest
        double symbolsWidth = pieces.Sum(p => p.Width);
        double space = mmr.EndX - mmr.StartX;
        double gap = (space - symbolsWidth)
                     / (2 * OuterPaddingFactor + (pieces.Count - 1));
        if (gap < 0)
            gap = 1.0;
        gap = Math.Min(gap, Math.Max(MaxSymbolSeparation, 1.0));

        double totalWidth = symbolsWidth + gap * (pieces.Count - 1);
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
            x += p.Width + gap;
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
    private static void DrawTieVariants(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TieVariantLayouts.IsDefaultOrEmpty) return;
        foreach (var v in layout.TieVariantLayouts)
        {
            if (!sysTopYUp.TryGetValue(v.MeasureIndex, out double syUp))
                continue; // other page
            // TieVariantLayout stores WITHIN-SYSTEM device offsets (down from the
            // system top). syUp is this measure's system-top page Y-up, so the arc's
            // page Y-up is syUp minus each device offset — byte-identical to the former
            // -(system.Y + offset) reflection DrawBow used to lift by pageHeight. The
            // arc geometry itself stays device-frame (intentional-device island 2).
            DrawBow(v.StartX, syUp - v.Y, v.EndX, syUp - v.Y,
                (v.Control1.X, syUp - v.Control1.Y), (v.Control2.X, syUp - v.Control2.Y),
                EngravingDefaults.TieMidThickness,
                v.StaffIndex, v.MeasureIndex, os, gc);
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
    private static void DrawLyricHyphens(ScoreLayout layout, Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.LyricHyphenLayouts.IsDefaultOrEmpty) return;
        // LILYPOND-REF: lily/lyric-hyphen.cc:64-65 th = get_dimension of the layout
        // line-thickness × the LyricHyphen thickness 1.3 (scm/define-grobs.scm).
        // ⚠️ NOT PORTED — the round-box form: LP draws each dash as a round_filled_box
        // with blot 0.8·lt (lily/lyric-hyphen.cc:126 dash_mol — corner radius 0.04 in
        // its SVG); this draws a square-ended line. LP has the stencil, so the square
        // end is a knowing form divergence, not a Lily#-own quantity (§5.2 audit,
        // session 158). The whiteout branch (:135-153, default OFF at
        // whiteout −1) is not ported.
        const double thickness = 1.3 * EngravingDefaults.LineThickness;
        foreach (var h in layout.LyricHyphenLayouts)
        {
            // A system-crossing connector's SECOND piece lives on the NEXT
            // syllable's system: its stored Y is relative to that system, so it
            // is resolved against that system's top — both pieces used to be
            // flipped against the FIRST system's top, which drew the stub before
            // the next syllable over the previous system's lyric row.
            // LILYPOND-REF: lily/lyric-extender.cc:98-107 print — each broken
            //   piece of the spanner sits within its own system.
            double? NextSystemTop() =>
                h.NextLyricIndex >= 0
                && sysTopYUp.TryGetValue(
                    layout.LyricLayouts[h.NextLyricIndex].Item.MeasureIndex, out var nextTop)
                    ? nextTop : null;

            if (h.Type == LyricConnectorType.Hyphen)
            {
                for (int di = 0; di < h.Dashes.Length; di++)
                {
                    var dash = h.Dashes[di];
                    var src = layout.LyricLayouts[h.LyricIndex];
                    if (!sysTopYUp.TryGetValue(src.Item.MeasureIndex, out var syUp)) continue; // other page
                    if (h.CrossesSystemBreak && dash.OnNextSystem && NextSystemTop() is { } nt)
                        syUp = nt;
                    double dashY = syUp - dash.Y;
                    gc.DrawLine(dash.X1, dashY, dash.X2, dashY,
                        Color.Black, thickness);
                }
            }
            else if (h.Type == LyricConnectorType.Extender)
            {
                var src = layout.LyricLayouts[h.LyricIndex];
                if (!sysTopYUp.TryGetValue(src.Item.MeasureIndex, out var syUp)) continue; // other page
                double extY = syUp - h.ExtenderY;
                if (h.CrossesSystemBreak)
                {
                    gc.DrawLine(h.ExtenderStartX, extY,
                        h.FirstSegmentEndX, extY, Color.Black, 0.1);
                    double ext2Y = NextSystemTop() is { } nextTop
                        ? nextTop - h.SecondSegmentY : extY;
                    gc.DrawLine(h.SecondSegmentStartX, ext2Y,
                        h.ExtenderEndX, ext2Y, Color.Black, 0.1);
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
    private static void DrawPartCombine(ScoreLayout layout, Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.PartCombineLayouts.IsDefaultOrEmpty) return;
        // LILYPOND-REF: scm/define-grobs.scm:1077-1094 CombineTextScript, outside-staff-priority
        // 475: it declares (font-series . bold) and NO font-shape or font-size entry, so the
        // label is upright text at the default size, not italic. MEASURED
        // (audit/lpreg/pcombine-lp.ly, dumped): series=bold shape=() size=().
        double size = LilySharp.Core.Svg.EngravingDefaults.CombineTextFontSize;
        foreach (var pc in layout.PartCombineLayouts)
        {
            if (!sysTopYUp.TryGetValue(pc.MeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: system top plus the stored offset.
            double y = syUp + pc.YUp;
            gc.DrawText(pc.Text, pc.X, y, size, TextRole.PartCombine,
                FontStyle.Bold, TextAnchor.Start, Color.Black);
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
    /// <summary>Tremolo slashes for a STEMLESS note (a whole/breve): the flag
    /// nearest the head sits 1.5ss beyond the outermost head in the would-be
    /// stem direction, the rest stack outward 0.81 apart, all centred on the
    /// head and rising to the right at the beamless slope.
    /// LILYPOND-REF: lily/stem-tremolo.cc:349-366 y_offset (whole_note branch),
    /// :45-79 calc_slope (0.25 below three flags), :81-94 calc_width (1.5 with
    /// no beam and no flag), :127-169 raw_stencil.</summary>
    private static void DrawStemlessTremolo(
        double headCenterX, double headY, bool up, int beamCount, IDrawingContext gc)
    {
        if (beamCount <= 0) return;
        const double width = 1.5;
        const double slope = 0.25;
        // LP: ss × length-fraction × 0.81. LILYSHARP-OWN: length-fraction is
        // folded to 1.0 — grace/cue tremolos (where it shrinks) have no Lily#
        // spelling yet, and nothing observes the folded factor.
        const double translation = 0.81;
        // ⚠️ LP re-decides the direction when the whole note stands in a
        // multi-voice collision (stem-tremolo.cc:288-309 calc_direction) —
        // NOT ported; this takes the note's own stem direction. No book and
        // no observer reaches that branch yet.
        double dir = up ? 1 : -1;
        for (int i = 0; i < beamCount; i++)
        {
            // Y-up page frame: +1.5 above the head for an up "stem".
            double y = headY + dir * (1.5 + translation * i);
            double dy = (width / 2) * slope;
            gc.DrawLine(headCenterX - width / 2, y - dy, headCenterX + width / 2, y + dy,
                Color.Black, EngravingDefaults.BeamThickness);
        }
    }

    // The slash stack hangs off the STEM END, not the stem midpoint: the slash
    // nearest the end centres one beam-translation short of it, and the rest
    // march a translation apiece back toward the head. An unbeamed flagged stem
    // backs the stack off (duration_log − 2) more translations to clear the
    // flag, an UP flagged stem another half. The slash always RISES to the
    // right — the slope's sign does not follow the stem direction.
    // LILYPOND-REF: lily/stem-tremolo.cc:314-368 y_offset — end_y =
    //   stem extent[dir] − dir·max(beam_count,1)·beam_translation (beamless
    //   beam_count folds to 1); the flag branch subtracts dir·(log−2)·bt and,
    //   for UP, another bt·0.5.
    // LILYPOND-REF: lily/stem-tremolo.cc:115-125 get_beam_translation —
    //   beamless translation = ss × length-fraction × 0.81 (length-fraction
    //   folded to 1.0, exactly as DrawStemlessTremolo above).
    // LILYPOND-REF: lily/stem-tremolo.cc:128-169 raw_stencil — beam-like
    //   slash, beam-thickness 0.48 (define-grobs.scm StemTremolo), each slash
    //   centre-aligned on both axes; :86-98 calc_width — 1.5, 1.0 for an UP
    //   flagged stem; :45-79 calc_slope — 0.25, 0.40 for a DOWN flagged stem.
    // LILYPOND-REF: scm/define-grobs.scm StemTremolo
    //   (parent-alignment-X . CENTER) — the stack centres on the stem's X.
    // ⚠️ The slash is a stroked line where LP draws a vertical-ended
    //   parallelogram — the known 0.04 ink-extent difference of this family.
    //   LP additionally switches to a sharp-cornered ROTATED BOX for an UP
    //   flagged stem (calc_shape, "rectangle") — same slope and width, corner
    //   shape only; the line approximates both shapes here.
    // ⚠️ y_offset's beam-end term is DROPPED: per
    //   lily/stem.cc:830-844 beam_end_corrective — half a beam thickness only
    //   `if (beam)`, else 0.0 — it vanishes for an unbeamed stem, and every
    //   caller sits inside `!isBeamed` (a beamed note's stem is the Beams
    //   path's, which never calls here). The beamed anchor law (end at the
    //   beam minus beam_count translations) is therefore unported and
    //   unreachable, not approximated.
    private static void DrawTremolo(
        double stemX, double stemEndY,
        bool stemUp, int noteValue, int beamCount, bool hasFlag, IDrawingContext gc)
    {
        // The note's own flags already show that many subdivision levels, so the
        // drawn slash count subtracts them: a8:32 gets 2 slashes where a4:32
        // gets 3 (probe ntrem-probe, LP 2 slashes vs a former Lily# 3). LP warns
        // "tremolo duration is too long" when nothing is left; here the stack
        // just vanishes.
        // LILYPOND-REF: lily/stem-engraver.cc:63-104 make_stem —
        //   tremolo_flags = intlog2 (requested_type) - 2 - (dur->duration_log ()
        //   > 2 ? dur->duration_log () - 2 : 0).
        int durationLog = (int)Math.Log2(noteValue);
        beamCount -= Math.Max(durationLog - 2, 0);
        if (beamCount <= 0) return;
        double dir = stemUp ? 1 : -1;
        double beamWidth = (stemUp && hasFlag) ? 1.0 : 1.5;
        const double beamThickness = EngravingDefaults.BeamThickness;
        const double translation = 0.81;
        double slope = (!stemUp && hasFlag) ? 0.40 : 0.25;

        // Y-up page frame: the end-side slash sits one translation inside the
        // stem end; successive slashes step toward the head.
        double endY = stemEndY - dir * translation;
        if (hasFlag)
        {
            endY -= dir * (durationLog - 2) * translation;
            if (stemUp)
                endY -= translation * 0.5;
        }

        for (int i = 0; i < beamCount; i++)
        {
            double y = endY - dir * translation * i;
            double halfW = beamWidth / 2;
            double dy = halfW * slope;
            gc.DrawLine(stemX - halfW, y - dy, stemX + halfW, y + dy,
                Color.Black, beamThickness);
        }
    }

}
