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
    /// LILYPOND-REF: scm/define-grobs.scm:1851-1858 self-alignment-X system-start-text::print
    ///   — the InstrumentName entry: serif, <c>(padding . 0.3)</c>, self-alignment-X CENTER,
    ///   and <c>X-offset</c> the callback below.
    /// LILYPOND-REF: scm/output-lib.scm:2108-2142 system-start-text::calc-x-offset — the
    ///   placement ported in <see cref="InstrumentNameRightEdge"/>.
    /// <para>
    /// ⚠️ UNTIL 2026-08-04 THIS CENTRED EVERY NAME AT <c>Indent / 2</c> and never looked at
    /// the delimiter, while the indent was sized from a flat half-em-per-character ESTIMATE of
    /// the name's width and the name was DRAWN from real metrics — two spellings of one
    /// quantity, erring both ways (WWWWWWW estimated 10.5 against 20.55 real, iiiiiii 10.5
    /// against 6.69). With the brace ink 1.3734 wide, ordinary names overlapped it: measured
    /// gaps from the name's right edge to the brace's right edge were Alto 1.048, Bass 0.638,
    /// Piano 0.205, Tenor 0.154, Soprano -0.019, Contrabassoon -0.128 — the last two past the
    /// brace's right edge entirely.
    /// </para>
    /// </remarks>
    private static void DrawInstrumentNames(
        MultiStaffScore score, SystemLayout system, double systemStartX, IDrawingContext gc)
    {
        if (system.Indent <= 0) return;

        const double NameFontScale = 0.75;
        double actualFontSize = FontSize * NameFontScale;
        double systemYUp = LayoutUtilities.SystemTopYUp(system);

        // The leftmost delimiter's ink, which is what every name on this system is placed
        // against. Taken from the SAME numbers DrawSystemStartDelimiters draws from, so a
        // name cannot be placed against a delimiter that is not where it is drawn.
        double? totalLeft = null;
        void Take(double left) { if (totalLeft is null || left < totalLeft) totalLeft = left; }

        // ⚠️ THE SYSTEM-START BAR IS A DELIMITER TOO, and it does not live in any group's
        // GrandStaffLayout: DrawStaffConnectors draws it across the whole system from
        // systemStartX whenever two or more staves are connected. Reading only the groups
        // missed it, and on a plain multi-staff score the name was then placed against the
        // indent instead of against the bar.
        if (SystemStartBarStaves(score, system).Count >= 2)
            Take(systemStartX - SystemStartBarThickness / 2.0);

        if (!system.StaffGroups.IsDefaultOrEmpty)
            foreach (var g in system.StaffGroups)
                if (g.GrandStaffLayout is { } d
                    && SystemStartDelimiterInkLeft(d, d.BraceTop - d.BraceBottom) is { } left)
                    Take(left);

        // ⚠️ THE WIDTH COMES FROM THE METRICS THE TEXT IS DRAWN WITH, which is the whole
        // point of the change: the pair this replaced sized the indent from an estimate and
        // drew from these.
        double NameX(string name) => InstrumentNameRightEdge(
            TextFontMetrics.Serif(name, actualFontSize), system.Indent, totalLeft);

        // Single-staff scores carry no StaffGroup layouts — the one staff sits
        // at the system Y with the standard staff height.
        if (system.StaffGroups.IsDefaultOrEmpty)
        {
            foreach (var (_, st, _) in score.EnumerateStaves())
            {
                if (string.IsNullOrEmpty(st.InstrumentName) || st.IsTab)
                    continue;
                gc.DrawText(st.InstrumentName, NameX(st.InstrumentName),
                    systemYUp - StaffHeight / 2.0,
                    actualFontSize, "serif", FontStyle.Regular,
                    TextAnchor.End, fill: null,
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
                    gc.DrawText(onlyNamed.InstrumentName!, NameX(onlyNamed.InstrumentName!),
                        centerY,
                        actualFontSize, "serif", FontStyle.Regular,
                        TextAnchor.End, fill: null,
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
                gc.DrawText(staffLayout.InstrumentName, NameX(staffLayout.InstrumentName),
                    centerY,
                    actualFontSize, "serif", FontStyle.Regular,
                    TextAnchor.End, fill: null,
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

        var allStaves = SystemStartBarStaves(score, system);
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

    /// <summary>LILYPOND-REF: scm/define-grobs.scm:1853-1858 system-start-text::calc-x-offset
    /// — the InstrumentName entry, whose <c>(padding . 0.3)</c> is :1854.
    /// ⚠️ <c>self-alignment-X</c> is the natural name for these lines and the citation ratchet
    /// cannot claim it: LooksLikeLilyPondSymbol rejects any token whose later hyphen segment
    /// starts with a capital, so the trailing <c>X</c> disqualifies it. Cite a neighbour on
    /// the same lines rather than dropping the range.</summary>
    private const double InstrumentNamePadding = 0.3;

    /// <summary>
    /// Where an instrument name's RIGHT edge goes: LilyPond's
    /// <c>system-start-text::calc-x-offset</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/output-lib.scm:2108-2142 system-start-text::calc-x-offset — the three
    /// terms below are that function's three terms, in its order.
    /// <para>
    /// While the name is NARROWER than the indent it ends up centred in an indent-wide box
    /// whose right edge clears the delimiter; once it is wider, <c>padding</c> goes to zero,
    /// its right edge pins to the delimiter and it overflows LEFT into the margin. LilyPond
    /// really does that: measured, "Contrabassoon" sits 6.54 staff spaces left of the system
    /// origin.
    /// </para>
    /// <para>
    /// ⚠️ THE FIRST TERM IS DERIVED, NOT READ. Calling
    /// <c>ly:side-position-interface::x-aligned-side</c> from <c>after-line-breaking</c>
    /// returns <c>-(w + padding)</c>, and no combination of THAT with the other two terms
    /// reproduces any of the measured offsets — a grob callback invoked after the fact is not
    /// the value that was used. Solving the other two terms out of the seven measured
    /// X-offsets leaves <c>indent - padding - w</c>, i.e. side-position LEFT of the staff,
    /// whose left edge IS the indent. That is what a side-position with this padding means,
    /// so the term is written as the rule rather than as the residue it was recovered from.
    /// </para>
    /// <para>
    /// ⚠️ <c>interval-length</c> OF AN EMPTY INTERVAL IS 0, which is the whole of the
    /// no-delimiter case: <c>calc-x-offset</c> seeds <c>total-left</c> with <c>+inf.0</c>, so
    /// <c>(cons total-left indent)</c> is empty and the correction vanishes. Passing null here
    /// is that <c>+inf.0</c>. MEASURED, book 3 of instrument-name-x.ly.
    /// </para>
    /// <para>
    /// ⚠️ <paramref name="width"/> IS ASKED OF THE SAME METRICS THE TEXT IS DRAWN WITH. The
    /// defect this replaced kept the name's width in two spellings — an estimate that sized
    /// the indent and real metrics that drew the glyphs — so anything that re-derives it here
    /// puts the pair straight back.
    /// </para>
    /// </remarks>
    internal static double InstrumentNameRightEdge(
        double width, double indent, double? delimiterInkLeft)
    {
        // (ly:side-position-interface::x-aligned-side grob) — LEFT of the staff, which starts
        // at the indent, so the name's right edge would sit one padding short of it.
        double xAlignedSide = indent - InstrumentNamePadding - width;

        // (padding (min 0 (- (interval-length my-extent) indent))) and
        // (right-padding (- padding (/ (* padding (1+ align-x)) 2))), align-x = CENTER = 0.
        double padding = Math.Min(0, width - indent);
        double rightPadding = padding - padding * (1 + InstrumentNameSelfAlignmentX) / 2.0;

        // (- (interval-length (cons total-left indent))) — 0 when the interval is empty,
        // which covers both "no delimiter at all" and a delimiter right of the indent.
        double totalLeft = delimiterInkLeft ?? double.PositiveInfinity;
        double correction = -Math.Max(0, indent - totalLeft);

        return xAlignedSide + rightPadding + correction + width;
    }

    /// <summary>LILYPOND-REF: scm/define-grobs.scm:1853-1858 system-start-text::calc-x-offset
    /// — InstrumentName declares <c>(self-alignment-X . CENTER)</c> at :1855, and CENTER is 0
    /// in the arithmetic that callback does with it.</summary>
    private const double InstrumentNameSelfAlignmentX = 0;

    /// <summary>
    /// The staves the system-start BAR joins — all visible ones EXCLUDING independent text
    /// rows (chords / lyrics), which LilyPond's ChordNames / Lyrics contexts do not connect.
    /// Top-to-bottom (staff.Y is Y-up, so descending).
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE SPELLING BECAUSE TWO THINGS ASK. It decides whether the bar is DRAWN
    /// (<see cref="DrawStaffConnectors"/>) and whether an instrument name has a delimiter to
    /// be placed against (<see cref="DrawInstrumentNames"/>); a name placed against a bar
    /// that is not drawn, or clearing nothing while one is, is the shape HANDOFF 7.7 keeps
    /// naming. StaffLayout carries no kind, so a text row is resolved back through
    /// <c>EnumerateStaves</c>.
    /// </remarks>
    private static List<StaffLayout> SystemStartBarStaves(
        MultiStaffScore score, SystemLayout system)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return [];
        var textRowIndices = new HashSet<int>(
            score.EnumerateStaves().Where(t => t.Staff.IsTextRow)
                .Select(t => t.GlobalStaffIndex));
        return system.StaffGroups
            .SelectMany(g => g.Staves)
            .Where(s => !s.IsHidden && !s.IsOssia && !textRowIndices.Contains(s.StaffIndex))
            .OrderByDescending(s => s.Y)
            .ToList();
    }

    /// <summary>Below this span a system-start delimiter is not drawn at all.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3671 collapse-height — the SystemStartBrace
    /// entry's, declared the same at scm/define-grobs.scm:3687 collapse-height on
    /// SystemStartBracket.</remarks>
    private const double SystemStartCollapseHeight = 5.0;

    /// <summary>How thick Lily# draws a system-start bracket's vertical stroke.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3685-3693 ly:system-start-delimiter::print — the
    /// SystemStartBracket entry, whose <c>(thickness . 0.45)</c> is :3692.
    /// <para>
    /// ⚠️ THE 0.25 IN <c>staff_bracket</c> IS A FALLBACK AND IS NEVER TAKEN. That literal is
    /// <c>from_scm&lt;double&gt; (get_property (me, "thickness"), 0.25)</c> at
    /// lily/system-start-delimiter.cc:43 staff_bracket — the default for an ABSENT property, and
    /// SystemStartBracket always declares one. This constant carried the fallback for a
    /// while, and a comment claiming 0.45 was multiplied by the line thickness to reach it
    /// stood here for one commit and was an invention (0.45 × 0.1 is 0.045, and
    /// <c>staff_bracket</c> multiplies the thickness by nothing).
    /// </para>
    /// <para>
    /// The stroke and the tip glyph are ONE number: mf/feta-brackettips.mf draws the tip with
    /// <c>thick_sharp = 0.45 staff_space#</c>, which is why its bbox reaches ±0.225 across the
    /// join. A 0.25 stroke under a 0.45 tip steps visibly at the seam, so <see
    /// cref="DrawSystemStartBracket"/> cannot use the real glyph and keep the old width.
    /// </para>
    /// <para>
    /// ⚠️ LilyPond's bracket box is <c>Interval (0, thickness)</c> — the stroke runs RIGHT from
    /// the grob's origin, where Lily#'s <see cref="DrawSystemStartBracket"/> centres it on
    /// BraceX. <see cref="SystemStartDelimiterInkLeft"/> follows LILY#'s drawing, because what
    /// an instrument name has to clear is the ink this renderer puts on the page.
    /// </para>
    /// </remarks>
    private const double SystemStartBracketThickness = 0.45;

    /// <summary>LILYPOND-REF: lily/system-start-delimiter.cc:89-95 simple_bar — a single
    /// vertical line of <c>line-thickness x thickness</c>, and the 1.6 is the SystemStartBar
    /// entry's own declared <c>(thickness . 1.6)</c> —
    /// scm/define-grobs.scm:3653-3662 ly:system-start-delimiter::print — read 2026-08-04, and
    /// unlike the bracket's this one is LilyPond's declared value, not a fallback.</summary>
    private static double SystemStartBarThickness => EngravingDefaults.StaffLineThickness * 1.6;

    /// <summary>
    /// The LEFT edge of the ink a system-start delimiter draws, or null when it draws none.
    /// </summary>
    /// <remarks>
    /// This is what the instrument name is placed against — LILYPOND-REF:
    /// scm/output-lib.scm:2108-2142 system-start-text::calc-x-offset walks the system's
    /// elements for everything carrying <c>system-start-delimiter-interface</c> and takes the
    /// MINIMUM of their left edges.
    /// <para>
    /// ⚠️ THE FOUR STYLES ARE ANCHORED DIFFERENTLY ON <c>BraceX</c>, which is why this cannot
    /// be "BraceX minus a constant": the brace is drawn right-anchored so its ink runs LEFT of
    /// BraceX by its glyph's width, while the bracket, line-bracket and bar are drawn as a
    /// stroke ON BraceX whose serifs and hooks run RIGHT. Each arm reads the same constant its
    /// own <c>Draw…</c> method does, so a change to either moves both.
    /// </para>
    /// <para>
    /// ⚠️ NULL IS LILYPOND'S EMPTY EXTENT, not an error. A collapsed delimiter contributes
    /// nothing to the minimum, and with no delimiter at all <c>calc-x-offset</c>'s
    /// <c>total-left</c> stays at the <c>+inf.0</c> it was seeded with, whose interval with
    /// the indent is EMPTY and so has length 0 — the correction term vanishes and the answer
    /// is the same as if the left edge were the indent. MEASURED, book 3 of
    /// audit/lp-geometry/probes/instrument-name-x.ly.
    /// </para>
    /// </remarks>
    private static double? SystemStartDelimiterInkLeft(GrandStaffLayout delim, double height)
    {
        bool shown = height >= SystemStartCollapseHeight;
        return delim.DelimiterType switch
        {
            SystemStartDelimiterType.Bracket when shown
                => delim.BraceX - SystemStartBracketThickness / 2.0,
            SystemStartDelimiterType.LineBracket when shown
                => delim.BraceX - EngravingDefaults.StaffLineThickness / 2.0,
            // ⚠️ NO COLLAPSE GUARD, AND LILYPOND HAS ONE. SystemStartBar declares
            // (collapse-height . 5.0) exactly as the brace and bracket do
            // at scm/define-grobs.scm:3653-3662 ly:system-start-delimiter::print — but Lily#'s
            // DrawStaffConnectors draws the bar for any two connected staves without testing
            // the span, so the ink-left has to answer for the same books the drawing does.
            // Recorded rather than corrected: two connected staves are taller than 5.0 in
            // every ordinary book, so the guard would change nothing visible and everything
            // to re-approve. ⚠️ A comment claiming LilyPond declares no collapse-height stood
            // here for one commit and was wrong.
            SystemStartDelimiterType.BarLine
                => delim.BraceX - SystemStartBarThickness / 2.0,
            SystemStartDelimiterType.Brace when shown
                => delim.BraceX - BraceLadder.Widths[BraceLadder.NearestIndex(height)],
            _ => null,
        };
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
            bool shown = height >= SystemStartCollapseHeight;
            switch (delim.DelimiterType)
            {
                case SystemStartDelimiterType.Bracket:
                    if (shown)
                        DrawSystemStartBracket(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.LineBracket:
                    if (shown)
                        DrawSystemStartLineBracket(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.BarLine:
                    DrawSystemStartBarLine(delim.BraceX, top, bottom, gc);
                    break;
                case SystemStartDelimiterType.Brace:
                    if (shown)
                        DrawSystemStartBrace(delim.BraceX, top, bottom, gc);
                    break;
            }
        }
    }

    /// <summary>
    /// A system-start bracket: a vertical stroke with an Emmentaler tip glyph hung
    /// off each end.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc:36-66 staff_bracket — a filled box
    ///   <c>Interval (0, thickness) x Interval (-1, 1) * (height / 2 + overlap)</c> with
    ///   <c>overlap = 0.1 * thickness</c>, capped by <c>brackettips.down</c> and
    ///   <c>brackettips.up</c> attached with <c>add_at_edge (Y_AXIS, d, tips[d], -overlap)</c>.
    /// <para>
    /// THE TIP IS A GLYPH, NOT A SERIF WE DRAW. A pair of hand-built triangles stood here and
    /// did not look like LilyPond's at any size: the real tip is a long tapering curve that
    /// sweeps out and away from the staff, and no two constants reproduce it. Asking the font
    /// for it is also what makes the tip track the stroke — mf/feta-brackettips.mf draws it
    /// with <c>thick_sharp = 0.45 staff_space#</c>, exactly the stroke it is meant to cap.
    /// </para>
    /// <para>
    /// <c>add_at_edge</c> places by EXTENT, so the arithmetic is the tips' bboxes: the UP tip
    /// goes where its own bottom edge lands on the stroke's top, i.e. its origin sits at
    /// <c>top - BracketTipUp.Bottom</c> (the bbox bottom is negative), and the DOWN tip
    /// mirrors it. The stroke itself is the one drawn OVER-LONG by <c>overlap</c> at each
    /// end, so the join has no seam.
    /// </para>
    /// </remarks>
    private static void DrawSystemStartBracket(double x, double top, double bottom, IDrawingContext gc)
    {
        double thickness = SystemStartBracketThickness;
        double overlap = 0.1 * thickness;
        gc.DrawLine(x, top + overlap, x, bottom - overlap, Color.Black, thickness);

        // LilyPond's stroke runs RIGHT from the grob origin (Interval (0, thickness)) and the
        // tips share that origin; Lily# centres the stroke on x, so the tips go half a
        // thickness to its left. SystemStartDelimiterInkLeft reports the same edge.
        double glyphX = x - thickness / 2.0;
        gc.DrawGlyph(EmmentalerGlyphs.BracketTipUp, glyphX,
            top - GlyphMetrics.BracketTipUp.Bottom, FontSize);
        gc.DrawGlyph(EmmentalerGlyphs.BracketTipDown, glyphX,
            bottom - GlyphMetrics.BracketTipDown.Top, FontSize);
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
        gc.DrawLine(x, top, x, bottom, Color.Black, SystemStartBarThickness);
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
                // The same cancellation→key entry the standard branch reads — it was the same
                // 0.4 written a THIRD time (draw, reserve, and here), so it moves with them.
                dx += GlyphMetrics.AccidentalNatural.Width
                    + SpacingRules.BreakAlignGap(
                        BreakAlignSymbol.KeyCancellation, BreakAlignSymbol.KeySignature);
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
            // Gap before the new signature. LilyPond keeps the KeyCancellation and the
            // following KeySignature as SEPARATE break-aligned grobs, so this is one
            // space-alist entry read like every other in the group.
            // LILYPOND-REF: scm/define-grobs.scm:1930-1964 key-cancellation-interface — its
            //   space-alist carries (key-signature . (extra-space . 0.5)) at :1944.
            // ⚠️ WAS A BARE 0.4 UNTIL 2026-08-03, on the reasoning that LilyPond's 0.5 is
            //   "trimmed by their (extra-spacing-width . (0.0 . 1.0)) overlap -- a spring, not
            //   a fixed pad, whose net in this inline model is ~0.4". That is the same
            //   measured-net claim that was false for the group's other three gaps:
            // LILYPOND-REF: lily/break-alignment-interface.cc:241-243 Break_alignment_interface::calc_positioning_done
            //   places the next group at
            //   extents[l][RIGHT] + distance - extents[r][LEFT], so both extents cancel, the
            //   ink-to-ink gap IS the entry, and extra-spacing-width never enters that walk.
            //   LilyPond measured 0.500000 on probe courtesy-meter.ly (score CMK: the
            //   cancellation's ink ends 26.993307, the key starts 27.493307). Ledger
            //   courtesy.key.cancellation-to-key opened at -0.100000 and closes here.
            // ⚠️ THE RESERVATION READS THE SAME ENTRY (SpacingRules.KeyCourtesySuffixWidth),
            //   so the room widens by exactly what this moves. It was the same 0.4 spelled
            //   twice, which is why both had to change in one commit.
            dx += SpacingRules.BreakAlignGap(
                BreakAlignSymbol.KeyCancellation, BreakAlignSymbol.KeySignature);
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

    /// <summary>
    /// The curly brace of a grand staff, as the one Emmentaler-Brace glyph whose OWN height
    /// is nearest the span — drawn at the font's natural size, never fitted to the span.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/system-start-delimiter.cc:150-160 <c>staff_brace</c> and
    /// scm/define-markup-commands.scm:5072-5099 <c>get-y-from-brace</c>, the comparator the
    /// <c>left-brace</c> command searches the ladder on. The two conversions
    /// there cancel (<c>y * output_scale / point_constant</c> then
    /// <c>(ly:pt size) / scale</c>), so the size asked for is the span in staff spaces;
    /// <c>left-brace</c> binary searches the ladder for the nearest glyph and returns it
    /// UNSCALED. The ladder's granularity IS the error LilyPond accepts — 0.0464 staff
    /// spaces at the bottom of the ladder, 0.2800 at the top.
    /// <para>
    /// ⚠️ NOTHING IS FITTED TO <paramref name="top"/>/<paramref name="bottom"/> BUT THE
    /// CENTRE, and that is the whole shape of this grob. What stood here before fitted a
    /// font size to the span and then multiplied by a correction factor, over a glyph index
    /// guessed by a power law from two invented endpoints (263 and 11493) — none of which
    /// LilyPond has. It also read the em as one staff space where Emmentaler's is FOUR, so
    /// the target was 4x too large, the search clamped to the largest brace on any ordinary
    /// grand staff, and the factor pulled it back part of the way. MEASURED on a four-staff
    /// group spanning 31.0: it drew glyph 575 (76.9748 tall) at font size 2.05, i.e.
    /// 76.9748 x 2.05/4 = 39.45 staff spaces, and the overhang crossed the instrument names.
    /// The ladder's nearest to 31.0 is glyph 345 at 30.9588.
    /// </para>
    /// <para>
    /// THE EM IS FOUR STAFF SPACES — <c>unitsPerEm / 4</c> is one staff space, the
    /// convention <c>audit/scripts/Extract-EmmentalerMetrics.py</c> asserts per font — so the
    /// glyph draws at its natural size at <see cref="SharedRenderer.FontSize"/>, the same
    /// constant every other music glyph is drawn at. ⚠️ ASK FOR THAT CONSTANT, do not write
    /// 4.0 again: its own remark says it is internal "instead of a second literal", and this
    /// method carried one for a day.
    /// ⚠️ THIS ONE IS ASSUMED, NOT MEASURED HERE. Nothing in the test suite rasterises a
    /// glyph, so no test can catch the em being wrong — which is exactly how the previous
    /// spelling survived. What IS checked (<c>BraceLadderTests</c>) is the selection and the
    /// emitted size; the em itself was confirmed against LilyPond by hand, on the probe
    /// <c>audit/lp-geometry/probes/brace-name-clear.ly</c>: LilyPond draws a four-staff
    /// group's brace 1.3734 wide, and the glyph this selection picks for that span is
    /// 1.37 wide in the same dump. It goes on being an assumption until a point measures it.
    /// </para>
    /// </remarks>
    private static void DrawSystemStartBrace(double x, double top, double bottom, IDrawingContext gc)
    {
        double height = top - bottom;
        double yMid = (top + bottom) / 2;

        int glyphIndex = BraceLadder.NearestIndex(height);
        char braceChar = (char)(BraceGlyphStart + glyphIndex);
        gc.DrawText(braceChar.ToString(), x, yMid, FontSize, "Emmentaler-Brace",
            FontStyle.Regular, TextAnchor.End, Color.Black);
    }

    /// <summary>The brace ladder's encoding: <c>braceN</c> lives at U+E000+N.</summary>
    private const int BraceGlyphStart = 0xE000;
}
