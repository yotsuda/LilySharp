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

/// <summary>
/// Overlay engravers for <see cref="SharedRenderer"/>: the above/below-staff
/// annotations and spanners (dynamics, articulations, lyrics, hairpins, ottava/
/// volta/tuplet brackets, trill spanners, glissandos, arpeggios). Split out of
/// the main SharedRenderer file as a partial class; no behavior change.
/// </summary>
internal static partial class SharedRenderer
{
    // ---------- Dynamics ----------

    /// <summary>
    /// Draws dynamic markings ("p", "f", "mf", etc.) below the staff using
    /// serif bold-italic text (matching LP's DynamicText grob font).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1433 DynamicText grob
    /// LILYPOND-REF: scm/define-grobs.scm:1444 self-alignment-X = CENTER
    /// </remarks>
    private static void DrawDynamics(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.DynamicLayouts.IsDefaultOrEmpty) return;
        // The em from the one home the reservation reads (it was a second spelling of the
        // same 2.0, `FontSize * 0.5`, until 2026-09-08), through the score's plan.
        foreach (var d in layout.DynamicLayouts)
        {
            // Which staff it hangs on, for lysc boxes (a no-op in every picture).
            using var staffScope = gc.Staff(d.StaffIndex);
            string text = NormalizeDynamicText(d.Text);
            if (!sysTopYUp.ContainsKey(d.MeasureIndex)) continue; // other page
            // A dynamic on an ossia staff shrinks with its staff's notation —
            // both the glyph and its distance from the small staff.
            // Frame B -> device: reflect the Y-up value against this dynamic's own
            // staff middle (the shared per-grob draw boundary), then apply ossia.
            double midYup = os.StaffMiddleYUp(d.StaffIndex, d.MeasureIndex, StaffHeight);
            double y = os.YUp(midYup + d.YUp, d.StaffIndex, d.MeasureIndex);
            double size = os.Size(LilySharp.Core.Svg.Layout.DynamicEngraver.LabelEm(fonts, d.IsExpressiveText), d.StaffIndex);
            // Free expressive text (@text) prints plain italic; dynamic levels keep LP's
            // bold-italic DynamicText face — from the one home the reservation reads.
            var style = LilySharp.Core.Svg.Layout.DynamicEngraver.LabelStyle(fonts, d.IsExpressiveText);
            // A SMuFL score's level in the font's own dynamic glyphs, centred where the text
            // would stand — the run the layout reserved (DynamicEngraver.SmuflGlyphRun).
            if (!d.IsExpressiveText
                && LilySharp.Core.Svg.Layout.DynamicEngraver.SmuflGlyphRun(fonts, text) is { } run)
            {
                double scale = os.Size(1.0, d.StaffIndex);
                double left = d.X - run.Width * scale / 2;
                using (gc.Source(d.SourcePosition))
                    for (int i = 0; i < run.Glyphs.Length; i++)
                        gc.DrawGlyph(run.Glyphs[i], left + run.Xs[i] * scale, y, run.FontSize * scale);
                continue;
            }
            using (gc.Source(d.SourcePosition))
                gc.DrawText(text, d.X, y, size, LilySharp.Core.Svg.Layout.DynamicEngraver.LabelRole(d.IsExpressiveText),
                    style, TextAnchor.Middle, Color.Black);
        }
    }

    // ---------- @todo marks: the editor's red ink over the page ----------

    // The preview's ink (owner's decision 2026-10-10 — the same red as the scan view's boxes).
    internal static readonly Color TodoInk = new(0xE5, 0x39, 0x35);
    // LILYSHARP-OWN, every one: an editor's mark, with no LilyPond counterpart. Owner's
    // decisions 2026-10-10 — a ring as a hand draws one (not a red head, not a compass
    // ellipse), one ring round a whole chord, the ring's pen thicker than the leader's, the
    // memo smaller than @text (memos run long) and set off the note's column by ONE distance
    // towards the side with more room, with no box round it, and NOTHING ON THE PAGE MOVES
    // for a mark: it is red ink over the finished page, not an engraved grob.
    private const double TodoRingPadding = 0.35;   // the ring stands this far outside the glyphs' box
    private const double TodoPen = 0.2;            // ONE pen for the ring, the memo's underline and the leader (owner: unequal pens look wrong)
    private const double TodoUnderlineDrop = 0.15; // the underline this far under the memo's descent
    private const double TodoMemoOffset = 1.5;     // from the ring's centre X to the memo's near end
    private const double TodoMemoGap = 0.6;        // between the memo's ink and what it stands over
    private const double TodoMemoAir = 1.0;        // between two memos side by side on one level
    private const double TodoMemoScale = 0.75;     // the memo's em over @text's

    /// <summary>
    /// The preview's <c>@todo</c> marks, drawn OVER the page after every overlay — the ring
    /// around a marked head, rest or chord (one ring about every head of a chord) and, for a
    /// memo, a callout box with a leader down to the ring: above the staff, or above the ring
    /// where the note stands higher; below both for <see cref="MusicItem.TodoBelow"/>. Read
    /// off the LIVE score and the layout's columns, so no layout record exists for them and
    /// no cached layout can hold a stale data-pos; drawn by a backend that
    /// <see cref="IDrawingContext.DrawsEditorMarks"/> (the interactive SVG), so no export has
    /// them and nothing is reserved for them: other marks may stand under the ink, as under a
    /// reviewer's pencil. Everything is stamped <c>data-todo</c> and the item's <c>data-pos</c>.
    /// A multi-measure rest's ring spans its measure, a staff space each way. LILYSHARP-OWN.
    /// </summary>
    private static void DrawTodoMarks(MultiStaffScore score, Dictionary<int, SystemLayout> measureToSystem,
        Dictionary<int, double> sysTopYUp, in OssiaShrink os, IDrawingContext gc)
    {
        if (!gc.DrawsEditorMarks)
            return;
        var fonts = score.TextMetrics;
        // Every staff's outer drawn lines over its middle (half-positions × spacing), and the
        // count DrawRest places a rest by.
        var staffEdges = new Dictionary<int, (double Top, double Bottom, int Lines, double Spacing)>();
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
        {
            double spacing = MultiStaffLayouter.LineSpacingOf(staff);
            double top = double.MinValue, bottom = double.MaxValue;
            foreach (double line in MultiStaffLayouter.LinePositionsOf(staff))
            {
                top = Math.Max(top, line / 2.0 * spacing);
                bottom = Math.Min(bottom, line / 2.0 * spacing);
            }
            staffEdges[staffIndex] = (top, bottom, MultiStaffLayouter.NotationStaffLinesOf(staff), spacing);
        }

        // Pass 1: the rings, the memos to place, and how far each system's NOTES reach above
        // and below its staves (heads and stems — the memos stand clear of them).
        var callouts = new List<TodoCallout>();
        var inkTop = new Dictionary<int, double>();
        var inkBottom = new Dictionary<int, double>();
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
        {
            var (_, _, staffLines, spacing) = staffEdges[staffIndex];
            for (int v = 0; v < staff.Voices.Length; v++)
            {
                var measures = staff.Voices[v].Measures;
                for (int mi = 0; mi < measures.Length; mi++)
                {
                    if (!measureToSystem.TryGetValue(mi, out var system))
                        continue; // other page
                    MeasureLayout? measureLayout = null;
                    foreach (var ml in system.Measures)
                        if (ml.MeasureIndex == mi) { measureLayout = ml; break; }
                    if (measureLayout == null)
                        continue;
                    double midYup = os.StaffMiddleYUp(staffIndex, mi, StaffHeight);
                    if (double.IsNaN(midYup))
                        continue;
                    var items = measures[mi].Items;
                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        var item = items[ii];
                        if (NoteReach(item) is var (reachBottom, reachTop))
                        {
                            double t = os.YUp(midYup + reachTop, staffIndex, mi);
                            double b = os.YUp(midYup + reachBottom, staffIndex, mi);
                            inkTop[system.SystemIndex] = Math.Max(inkTop.GetValueOrDefault(system.SystemIndex, double.MinValue), t);
                            inkBottom[system.SystemIndex] = Math.Min(inkBottom.GetValueOrDefault(system.SystemIndex, double.MaxValue), b);
                        }
                        if (item.TodoKey is not { } key)
                            continue;
                        if (TodoInkBox(item, measureLayout, measures, mi, ii, staffLines, spacing) is not var (left, right, bottom, top))
                            continue;
                        double scale = os.Size(1.0, staffIndex);
                        double cx = (left + right) / 2, cy = (top + bottom) / 2;
                        double rx = ((right - left) / 2 + TodoRingPadding) * scale;
                        double ry = ((top - bottom) / 2 + TodoRingPadding) * scale;
                        double ringMid = os.YUp(midYup + cy, staffIndex, mi);
                        using (gc.Staff(staffIndex))
                        using (gc.Source(item.SourcePosition))
                        using (gc.Todo(key))
                            DrawHandRing(gc, cx, ringMid, rx, ry, TodoPen * scale, item.SourcePosition);
                        if (item.TodoMemo is { Length: > 0 } memo)
                            callouts.Add(new TodoCallout(system, staffIndex, mi, item.SourcePosition, key, memo,
                                item.TodoBelow, cx, ringMid, rx, ry, scale));
                    }
                }
            }
        }
        if (callouts.Count == 0)
            return;

        // Pass 2: the memos go in the MARGIN — over the system's topmost drawn line (under its
        // bottommost for .down), further out where the ring itself stands beyond it — as a
        // line of red text with no box, set OFF the note's column by one fixed distance towards
        // the side with more room (owner's requests 2026-10-10: a memo on the note's own x
        // looks wrong, and a shift that differs from memo to memo looks untidy), kept inside the
        // system's width; two that would overlap at one level stack outward, left to right. A
        // short leader runs from the ring's shoulder to the text's near end.
        var systemEdge = new Dictionary<(int System, bool Below), double>();
        foreach (var group in callouts.GroupBy(c => (c.System.SystemIndex, c.Below)))
        {
            var ordered = group.OrderBy(c => c.RingX).ToList();
            var first = ordered[0];
            bool below = first.Below;
            if (!systemEdge.TryGetValue(group.Key, out double edge))
            {
                // The margin's edge: the system's outermost drawn line, or its farthest note
                // (head and stem) where the notes reach past the lines — so the memos stand
                // in the gap between systems and not through a run of ledgered notes (the
                // owner's "余白": the first spelling stood them a gap over the top LINE). Other
                // marks there — a dynamic, a chord symbol, a lyric row — may lie under the ink.
                edge = below ? double.MaxValue : double.MinValue;
                if (!below && inkTop.TryGetValue(first.System.SystemIndex, out double top))
                    edge = top;
                if (below && inkBottom.TryGetValue(first.System.SystemIndex, out double bottomInk))
                    edge = bottomInk;
                foreach (var (si, (staffTop, staffBottom, _, _)) in staffEdges)
                {
                    double mid = os.StaffMiddleYUp(si, first.MeasureIndex, StaffHeight);
                    if (double.IsNaN(mid))
                        continue;
                    edge = below ? Math.Min(edge, os.YUp(mid + staffBottom, si, first.MeasureIndex))
                                 : Math.Max(edge, os.YUp(mid + staffTop, si, first.MeasureIndex));
                }
                systemEdge[group.Key] = edge;
            }
            var system = first.System;
            double sysLeft = system.Measures[0].X;
            double sysRight = system.Measures[^1].X + system.Measures[^1].Width;
            var placed = new List<(double Left, double Right, int Level)>();
            foreach (var c in ordered)
            {
                double scale = c.Scale;
                double em = os.Size(DynamicEngraver.LabelEm(fonts, true), c.StaffIndex) * TodoMemoScale;
                double width = 2 * DynamicEngraver.LabelHalfWidth(fonts, c.Memo, expressive: true) * scale * TodoMemoScale;
                var (ascent, descent) = DynamicEngraver.InkOf(fonts, c.Memo, true);
                ascent *= scale * TodoMemoScale;
                descent *= scale * TodoMemoScale;
                double offset = TodoMemoOffset * scale, gap = TodoMemoGap * scale;
                // The side with more room between the ring and the system's edge.
                bool toRight = sysRight - c.RingX >= c.RingX - sysLeft;
                double left = toRight ? c.RingX + offset : c.RingX - offset - width;
                left = Math.Max(sysLeft, Math.Min(left, sysRight - width));
                double right = left + width;
                // A level is free when the text clears every neighbour on it by a space of air.
                int level = 0;
                while (placed.Any(p => p.Level == level && p.Left < right + TodoMemoAir * scale && left - TodoMemoAir * scale < p.Right))
                    level++;
                placed.Add((left, right, level));
                double step = level * (ascent + descent + TodoUnderlineDrop * scale + gap / 2);
                double ringEdge = below ? c.RingY - c.RingRy : c.RingY + c.RingRy;
                double drop = TodoUnderlineDrop * scale;
                double baseline = below
                    ? Math.Min(edge, ringEdge) - gap - ascent - step
                    : Math.Max(edge, ringEdge) + gap + descent + drop + step;
                // The memo is UNDERLINED, and the leader joins the underline's centre to the
                // ring — the point of the ring that faces it — with the one pen (owner's
                // requests 2026-10-10). A memo below the staff is joined at the centre of its
                // top instead, so the leader does not run through the words.
                double underlineY = baseline - descent - drop;
                double centreX = (left + right) / 2;
                double attachY = below ? baseline + ascent + drop : underlineY;
                double angle = Math.Atan2((attachY - c.RingY) / c.RingRy, (centreX - c.RingX) / c.RingRx);
                double onRingX = c.RingX + c.RingRx * Math.Cos(angle);
                double onRingY = c.RingY + c.RingRy * Math.Sin(angle);
                double pen = TodoPen * scale;
                using (gc.Staff(c.StaffIndex))
                using (gc.Source(c.SourcePosition))
                using (gc.Todo(c.Key))
                {
                    gc.DrawLine(left, underlineY, right, underlineY, stroke: TodoInk, strokeWidth: pen, cap: LineCap.Round);
                    gc.DrawLine(onRingX, onRingY, centreX, attachY, stroke: TodoInk, strokeWidth: pen, cap: LineCap.Round);
                    gc.DrawText(c.Memo, left, baseline, em, DynamicEngraver.LabelRole(true),
                        DynamicEngraver.LabelStyle(fonts, true), TextAnchor.Start, TodoInk);
                }
            }
        }
    }

    /// <summary>How far an item's notes reach over the staff middle (Y-up, staff spaces), head
    /// and stem: a stem is read at LilyPond's plain length of 3.5 on the stem's side and a
    /// head's half height on the other; a rest its own position a space each way; null for
    /// an item that draws nothing of the kind. A rough bound for the margin — beams and flags
    /// are not read — which is all the memos need.</summary>
    private static (double Bottom, double Top)? NoteReach(MusicItem item)
    {
        const double stem = 3.5, head = 0.5;
        switch (item)
        {
            case NoteItem note:
            {
                double y = note.StaffPosition / 2.0;
                bool up = note.ForcedStemUp ?? note.StemUp;
                return (y - (up ? head : stem), y + (up ? stem : head));
            }
            case ChordItem { Notes.Length: > 0 } chord:
            {
                double lo = chord.Notes.Min(n => n.StaffPosition) / 2.0, hi = chord.Notes.Max(n => n.StaffPosition) / 2.0;
                bool up = chord.ForcedStemUp ?? chord.StemUp;
                return (lo - (up ? head : stem), hi + (up ? stem : head));
            }
            case RestItem { IsMultiMeasure: false } rest:
            {
                double y = (rest.StaffPosition ?? 0) / 2.0;
                return (y - 1.0, y + 1.0);
            }
            default:
                return null;
        }
    }

    /// <summary>A memo awaiting its place in the margin (<see cref="DrawTodoMarks"/>): its
    /// ring's centre and radii on the page (Y-up).</summary>
    private readonly record struct TodoCallout(SystemLayout System, int StaffIndex, int MeasureIndex,
        int SourcePosition, string Key, string Memo, bool Below, double RingX, double RingY,
        double RingRx, double RingRy, double Scale);

    /// <summary>
    /// A ring as a hand draws one round something on a page: a wobbly loop that starts at the
    /// upper left, runs round, and overshoots its start a little on the outside — not a
    /// compass ellipse (owner's request 2026-10-10: "人が〇をつけたような形"). The wobble is a
    /// sum of low harmonics whose phases, and a slight lean, come from the mark's source
    /// position, so a ring is the same picture at every render and no two rings are quite
    /// alike. Eight round-capped cubic segments, tangent-matched.
    /// </summary>
    private static void DrawHandRing(IDrawingContext gc, double cx, double cy, double rx, double ry,
        double pen, int seed)
    {
        const int Segments = 8;
        const double Overlap = 0.55;        // the pen runs this far past its start (radians)
        const double Start = Math.PI * 0.62; // upper left (the frame is Y-up)
        const double Sweep = 2 * Math.PI + Overlap;
        uint h = (uint)seed * 2654435761u ^ 0x9E3779B9u;
        double p1 = (h & 0xFF) / 255.0 * 2 * Math.PI;
        double p2 = ((h >> 8) & 0xFF) / 255.0 * 2 * Math.PI;
        double p3 = ((h >> 16) & 0xFF) / 255.0 * 2 * Math.PI;
        double lean = (((h >> 24) & 0xFF) / 255.0 - 0.5) * 0.25; // ±7° either way
        double cosLean = Math.Cos(lean), sinLean = Math.Sin(lean);
        (double X, double Y) At(double t)
        {
            double a = Start + t;
            double rho = 1 + 0.05 * Math.Sin(2 * a + p1) + 0.035 * Math.Sin(3 * a + p2) + 0.02 * Math.Sin(5 * a + p3)
                         + 0.06 * t / Sweep; // a slow spiral outward: the overshoot lies outside the start
            double x = rx * rho * Math.Cos(a), y = ry * rho * Math.Sin(a);
            return (cx + x * cosLean - y * sinLean, cy + x * sinLean + y * cosLean);
        }
        const double step = Sweep / Segments, eps = 1e-3;
        for (int i = 0; i < Segments; i++)
        {
            double t0 = i * step, t1 = t0 + step;
            var a0 = At(t0);
            var a1 = At(t1);
            var d0 = At(t0 + eps);
            var d1 = At(t1 - eps);
            // The tangents, by one-sided differences, each a third of the segment long.
            var c1 = (a0.X + (d0.X - a0.X) / eps * step / 3, a0.Y + (d0.Y - a0.Y) / eps * step / 3);
            var c2 = (a1.X + (d1.X - a1.X) / eps * step / 3, a1.Y + (d1.Y - a1.Y) / eps * step / 3);
            gc.DrawBezier(a0, c1, c2, a1, TodoInk, pen);
        }
    }

    /// <summary>
    /// The box the ring goes round, in staff spaces — X on the page, Y over the staff middle
    /// (Y-up): a note's head, a chord's heads together (the displaced heads' full width, the
    /// lowest head's bottom to the highest head's top), a rest's glyph at its drawn position,
    /// a multi-measure rest's bar over its measure; null for an item that draws no such thing.
    /// </summary>
    private static (double Left, double Right, double Bottom, double Top)? TodoInkBox(
        MusicItem item, MeasureLayout measureLayout, ImmutableArray<Measure> measures,
        int measureIndex, int itemIndex, int staffLines, double lineSpacing)
    {
        double xColumn = measureLayout.X + LayoutUtilities.GetItemXOffset(measures, measureIndex, itemIndex, measureLayout);
        switch (item)
        {
            case NoteItem note:
            {
                var box = GlyphMetrics.GetNoteheadBBox(HeadFontOf(note), GlyphMetrics.NoteValueOf(note.BaseDuration));
                double y = note.StaffPosition / 2.0;
                return (xColumn + box.Left, xColumn + box.Right, y - box.Height / 2, y + box.Height / 2);
            }
            case ChordItem { Notes.Length: > 0 } chord:
            {
                int noteValue = GlyphMetrics.NoteValueOf(chord.BaseDuration);
                var font = HeadFontOf(chord);
                var box = GlyphMetrics.GetNoteheadBBox(font, noteValue);
                // The displaced heads of a second, as DrawChord places them (a voice's forced
                // direction is not known here; the chord's own answer stands in).
                double[] offsets = ChordHeadPositioning.CalculateOffsets(
                    chord.Notes, chord.ForcedStemUp ?? chord.StemUp, noteValue, font);
                double left = double.PositiveInfinity, right = double.NegativeInfinity;
                double bottom = double.PositiveInfinity, top = double.NegativeInfinity;
                for (int i = 0; i < chord.Notes.Length; i++)
                {
                    double dx = i < offsets.Length ? offsets[i] : 0;
                    left = Math.Min(left, xColumn + dx + box.Left);
                    right = Math.Max(right, xColumn + dx + box.Right);
                    double y = chord.Notes[i].StaffPosition / 2.0;
                    bottom = Math.Min(bottom, y - box.Height / 2);
                    top = Math.Max(top, y + box.Height / 2);
                }
                return (left, right, bottom, top);
            }
            case RestItem { IsMultiMeasure: true }:
                // The H-bar's span (DrawBigRest: a space in from each bar line), a staff space
                // each way about the middle.
                return measureLayout.Width > 2.0
                    ? (measureLayout.X + 1.0, measureLayout.X + measureLayout.Width - 1.0, -lineSpacing, lineSpacing)
                    : null;
            case RestItem rest:
            {
                int noteValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                var box = GlyphMetrics.GetRestBBox(noteValue);
                // The glyph's origin: the rest's own position, else the neutral one DrawRest draws at.
                double y = (rest.StaffPosition ?? ElementCoordinator.NeutralRestPosition(staffLines, noteValue)) / 2.0;
                return (xColumn + box.Left, xColumn + box.Right, y + box.Bottom, y + box.Top);
            }
            default:
                return null;
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
    /// LILYPOND-REF: scm/define-grobs.scm:2992 Script grob
    /// LILYPOND-REF: lily/script-engraver.cc:235 acknowledge_rhythmic_head / :253 acknowledge_note_column
    /// </remarks>
    private static void DrawArticulationsLive(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.ArticulationLayouts.IsDefaultOrEmpty) return;
        foreach (var a in layout.ArticulationLayouts)
        {
            // Which staff it hangs on, for lysc boxes (a no-op in every picture).
            using var staffScope = gc.Staff(a.StaffIndex);
            if (string.IsNullOrEmpty(a.Glyph)) continue;
            if (!sysTopYUp.ContainsKey(a.MeasureIndex)) continue; // other page
            // A script on an ossia staff shrinks with its staff's notation —
            // both the glyph and its distance from the small staff.
            // Frame B -> device: reflect the Y-up value against this script's own
            // staff middle (the shared per-grob draw boundary), then apply ossia.
            double midYup = os.StaffMiddleYUp(a.StaffIndex, a.MeasureIndex, StaffHeight);
            double y = os.YUp(midYup + a.YUp, a.StaffIndex, a.MeasureIndex);
            // The grob's own font-size (0 for an ordinary script, −2 for an editorial
            // accidental), composed with the ossia its staff may carry.
            double scale = os.Size(
                EmmentalerDesignSize.Magstep(a.FontSizeStep), a.StaffIndex);
            // Fall / doit sentinels ("bendFall"/"bendDoit"): LilyPond's BendAfter, one stroked
            // cubic. The layout stored the stencil's box in Ink — its reach dx in Right, its
            // drop or rise in Bottom / Top (Y-up) — and put the anchor at the curve's start.
            if (a.Glyph is "bendFall" or "bendDoit")
            {
                double delta = a.Glyph == "bendFall" ? a.Ink.Bottom : a.Ink.Top;
                double yEnd = os.YUp(midYup + a.YUp + delta, a.StaffIndex, a.MeasureIndex);
                using (gc.Source(a.SourcePosition))
                    DrawBendAfter(a.X, y, a.Ink.Right, yEnd - y, gc);
                continue;
            }
            // Approach curves INTO the note from the left: scoop rises, plop falls.
            if (a.Glyph is "bendScoop" or "bendPlop")
            {
                using (gc.Source(a.SourcePosition))
                    DrawBendBefore(a.X, y, rise: a.Glyph == "bendScoop", gc);
                continue;
            }
            // Guitar bend-up sentinel ("bendUp:N", N = semitones): rising arrow
            // with the bend amount labelled above the arrowhead.
            if (a.Glyph.StartsWith("bendUp:", StringComparison.Ordinal))
            {
                int semis = int.TryParse(a.Glyph.AsSpan(7), out int bs) ? bs : 2;
                using (gc.Source(a.SourcePosition))
                    DrawGuitarBend(fonts, a.X, y, semis, gc);
                continue;
            }
            // TAB technique letters (H / P / T, and the pluck letters): small italic
            // serif text, drawn with its BASELINE on the layout's anchor.
            // ⚠️ THE SIZE AND STYLE COME FROM THE LAYOUT'S OWN HOME, not from literals
            // here. They used to be a bare `1.5` and `FontStyle.Italic` on this line while
            // ArticulationEngraver reserved a symmetric half-space box that knew nothing
            // about either — so the reservation was not the ink, and a letter placed BELOW
            // a note was drawn 0.383 ss into the notehead (2026-08-28). One home means the
            // box the layout reserved IS what this line draws.
            if (a.Glyph.StartsWith("tabtech:", StringComparison.Ordinal))
            {
                using (gc.Source(a.SourcePosition))
                    gc.DrawText(a.Glyph[8..], a.X, y,
                        LilySharp.Core.Svg.Layout.ArticulationEngraver.TabTechniqueEm(fonts),
                        TextRole.TabTechnique,
                        LilySharp.Core.Svg.Layout.ArticulationEngraver.TabTechniqueStyle(fonts),
                        TextAnchor.Middle, Color.Black);
                continue;
            }
            // Chord diagram sentinel ("frame:x32010").
            if (a.Glyph.StartsWith("frame:", StringComparison.Ordinal))
            {
                using (gc.Source(a.SourcePosition))
                    DrawFretFrame(fonts, a.X, y, a.Glyph[6..], gc);
                continue;
            }
            // Bartók (snap) pizzicato takes the ordinary glyph path below: it is the
            // font's scripts.snappizzicato (MusicGlyph.PluckedSnapPizzicatoAbove), not
            // primitives — see ArticulationItem's SnapPizz mapping.
            // …and from the design that font-size selects: an editorial accidental is the
            // 16 design's own outline (the same one ArticulationEngraver.ScriptSkylines
            // profiled), not the 20's drawn small. IDrawingContext.MusicFace.
            using (gc.Source(a.SourcePosition))
            using (gc.MusicFace(
                MusicFont.Current.DesignAt(a.FontSizeStep).Rounded))
                gc.DrawGlyph(a.Glyph[0], a.X, y, FontSize * scale);
        }
    }

    // (DrawDeadNotehead — two strokes across a black head's box for a `@dead` note — went in
    // session 562: the dead note is a cross-STYLE head (NoteItem.Notehead), drawn as the
    // noteheads.s2cross glyph by the ordinary head path, as LilyPond's \deadNote is.)

    /// <summary>
    /// Draws a jazz scoop (rises into the note) or plop (falls into it): the curve starts
    /// away-and-left and arrives at the notehead nearly horizontal, a polyline along a
    /// quadratic Bézier 1.25 long and 1.7 deep, 0.13 thick.
    /// LILYSHARP-OWN: LilyPond has no scoop or plop — no grob and no event spells either in
    /// lily/ or scm/ (2.26.0) — so this is Lily#'s own gesture, the mirror of the fall's old
    /// shape (the fall itself is LilyPond's BendAfter since session 566, <see cref="DrawBendAfter"/>).
    ///   departs from: nothing — there is no LilyPond counterpart to depart from.
    ///   goes away when: LilyPond grows one.
    ///   observed by: none against LilyPond.
    /// </summary>
    private static void DrawBendBefore(double x1, double y1, bool rise, IDrawingContext gc)
    {
        const double len = 1.25;
        double drop = rise ? 1.7 : -1.7;          // start BELOW for a scoop (device down)
        double x0 = x1 - len, y0 = y1 - drop;
        // Control point mirrored: arrives at the note nearly horizontal.
        double cx = x1 - len * 0.62, cy = y1 - drop * 0.08;
        double px = x0, py = y0;
        const int seg = 8;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * x1;
            double ny = u * u * y0 + 2 * u * t * cy + t * t * y1;
            gc.DrawLine(px, py, nx, ny, Color.Black, 0.13, cap: LineCap.Round);
            px = nx; py = ny;
        }
    }

    /// <summary>
    /// Draws a fall or a doit — LilyPond's BendAfter stencil: ONE stroked cubic from the
    /// anchor (<paramref name="x0"/>, <paramref name="y0"/>: the left bound's ink plus the
    /// padding, placed by ArticulationEngraver), its first control point a third of the
    /// reach along at the start's height, its second at the far end 0.66 of the drop down,
    /// ending <paramref name="dx"/> on and <paramref name="dy"/> down (a fall) or up (a
    /// doit); round caps and joins, <see cref="BendAfterGeometry.Thickness"/> thick. Until
    /// session 566 this was eight straight segments along an invented quadratic, 1.25 long
    /// and 1.7 deep, 0.13 thick, leaving the head by 0.15.
    /// LILYPOND-REF: scm/output-lib.scm:1343-1397 bend::print — rcurveto (dx/3, 0) (dx, 0.66 delta-y) (dx, delta-y) at :1387-1391, off a note-head-interface bound (:1349)
    /// </summary>
    private static void DrawBendAfter(double x0, double y0, double dx, double dy, IDrawingContext gc)
        => gc.DrawBezier(
            (x0, y0),
            (x0 + dx * BendAfterGeometry.FirstControlFraction, y0),
            (x0 + dx, y0 + BendAfterGeometry.SecondControlRise * dy),
            (x0 + dx, y0 + dy),
            Color.Black, BendAfterGeometry.Thickness);

    /// <summary>The "Nfr" label's ENGRAVING em at the diagram's default size — the one home
    /// is <see cref="FretFrameGeometry.LabelEm"/>, which also scales the grid with it.</summary>
    internal const double FretFrameLabelEm = FretFrameGeometry.LabelEm;

    /// <summary>The bend amount label's ENGRAVING em. LILYSHARP-OWN, as
    /// <see cref="FretFrameLabelEm"/> is: the bend arrow is Lily#'s own device and the label
    /// is drawn here and reserved nowhere.</summary>
    internal const double BendLabelEm = 1.6;

    /// <summary>
    /// Draws a chord diagram — LilyPond's fret diagram, stencil by stencil: the strings (one
    /// fret past the last row), the frets, a curved barre over the dots it spans, the dots 0.6
    /// of the way down their fret space, the thick nut at the first position, an <c>X</c> /
    /// <c>O</c> over each muted / open string, the "Nfr" label right, and — for a FretBoard,
    /// <paramref name="fingers"/>, a chords row's diagram — the finger numbers under the
    /// strings. The spec is LOW string first (<c>x32010</c>, a detail suffix for the fingers
    /// and barres: <see cref="FretFrameGeometry"/>); the anchor is the grid bottom's centre,
    /// Y-up. Every length is <see cref="FretFrameGeometry.Measure"/>'s, the same the
    /// reservation reads.
    /// LILYPOND-REF: scm/fret-diagrams.scm:855-899 make-fret-diagram — the body: draw-strings,
    ///   draw-frets, draw-barre, draw-dots, draw-thick-zero-fret, draw-xo, label-fret, in that order.
    /// </summary>
    private static void DrawFretFrame(ScoreTextMetrics fonts, double cx, double bottomY, string spec,
        IDrawingContext gc, bool fingers = false)
    {
        var m = LilySharp.Core.Svg.Layout.FretFrameGeometry.Measure(spec, fonts, fingers);
        double s = m.Size, th = m.Th, half = th / 2;
        int n = m.Strings, rows = m.Rows;
        double left = cx - (n - 1) * s / 2;
        double top = bottomY + rows * s;
        var style = fonts.Style(TextRole.FretFrame, FontStyle.Regular);

        // Strings: a box th wide from half a thickness over fret 0's line to the overhang's end,
        // half a thickness past it.
        // LILYPOND-REF: scm/fret-diagrams.scm:406-429 string-stencil — round-filled-box, string-overhang
        for (int i = 0; i < n; i++)
        {
            double x = left + i * s;
            gc.DrawLine(x, top + half, x,
                bottomY - LilySharp.Core.Svg.Layout.FretFrameGeometry.StringOverhang * s - half, Color.Black, th);
        }
        // Frets: a line th thick from the low string's centre to the high string's, round ends.
        // LILYPOND-REF: scm/fret-diagrams.scm:440-460 fret-stencil — make-line-stencil, fret-distance
        for (int f = 0; f <= rows; f++)
        {
            double y = top - f * s;
            gc.DrawLine(left, y, left + (n - 1) * s, y, Color.Black, th, cap: LineCap.Round);
        }

        // The barres: a bezier sandwich from the first string's dot centre to the last's, arching
        // bezier-height toward the nut, bezier-thick thick and stroked as thick again.
        // LILYPOND-REF: scm/fret-diagrams.scm:462-495 draw-barre — barre-fret-coordinate, the string coordinates
        // LILYPOND-REF: scm/fret-diagrams.scm:352-395 make-bezier-sandwich-list — the six control points
        // LILYPOND-REF: scm/stencil.scm:20-31 make-bezier-sandwich-stencil — two curveto, closepath, filled, thick
        foreach (var barre in LilySharp.Core.Svg.Layout.FretFrameGeometry.Barres(spec))
        {
            int fret = barre.Fret - (m.BaseFret - 1);
            if (fret < 1 || fret > rows)
                continue;
            double baseY = top - (fret - 1 + LilySharp.Core.Svg.Layout.FretFrameGeometry.DotPosition) * s;
            double start = left + barre.From * s, stop = left + barre.To * s;
            double halfThick = LilySharp.Core.Svg.Layout.FretFrameGeometry.BarreThick * s;
            double height = LilySharp.Core.Svg.Layout.FretFrameGeometry.BarreHeight * s;
            double width = 1 + (stop - start);          // LilyPond's own arithmetic: 1 + the span
            double cpLeft = start + width * halfThick, cpRight = stop - width * halfThick;
            gc.DrawClosedBezier(
                (start, baseY),
                (cpLeft, baseY + height - halfThick), (cpRight, baseY + height - halfThick),
                (stop, baseY),
                (cpRight, baseY + height), (cpLeft, baseY + height),
                Color.Black, strokeWidth: halfThick);
        }

        // The dots: a filled circle of dot-radius ringed by a line th thick, dot-position down
        // the fret space.
        // LILYPOND-REF: scm/fret-diagrams.scm:535-613 draw-dots — fret-coordinate, make-circle-stencil scale-dot-radius scale-dot-thick #t
        for (int i = 0; i < n; i++)
        {
            int written = LilySharp.Core.Svg.Layout.FretFrameGeometry.FretAt(spec, i);
            if (written <= 0)
                continue;
            int fret = written - (m.BaseFret - 1);
            if (fret < 1 || fret > rows)
                continue;
            gc.DrawCircle(left + i * s,
                top - (fret - 1 + LilySharp.Core.Svg.Layout.FretFrameGeometry.DotPosition) * s,
                LilySharp.Core.Svg.Layout.FretFrameGeometry.DotRadius * s + half, Color.Black);
        }

        // The nut at the first position: a box top-fret-thickness thick standing on half a
        // thickness under fret 0's line, from the low string's outer edge to the high string's.
        // LILYPOND-REF: scm/fret-diagrams.scm:737-762 draw-thick-zero-fret
        if (m.BaseFret == 1)
        {
            double nut = LilySharp.Core.Svg.Layout.FretFrameGeometry.TopFretThickness * th;
            gc.DrawLine(left - half, top - half + nut / 2, left + (n - 1) * s + half, top - half + nut / 2,
                Color.Black, nut);
        }

        // X / O: each centred on its string, the row centred xo-padding over the diagram's top.
        // LILYPOND-REF: scm/fret-diagrams.scm:764-801 draw-xo — xo-font-magnification, centered-stencil, xo-fret-offset
        if (m.XoCentre is { } xoCentre)
            for (int i = 0; i < n; i++)
            {
                int written = LilySharp.Core.Svg.Layout.FretFrameGeometry.FretAt(spec, i);
                var label = written < 0 ? m.X : written == 0 ? m.O : null;
                if (label is { } glyph)
                    gc.DrawText(glyph.Text, glyph.PenX(left + i * s), glyph.Baseline(bottomY + xoCentre), glyph.Em,
                        TextRole.FretFrame, style, TextAnchor.Start, Color.Black);
            }

        // The finger numbers under the strings (a FretBoard's): each centred on its string, its
        // ink top on the line finger-label-padding under the strings.
        // LILYPOND-REF: scm/fret-diagrams.scm:695-728 draw-dots — finger-label-fret-coordinate, finger-code below-string
        for (int i = 0; i < m.Fingers.Length; i++)
            if (m.Fingers[i] is { } finger)
                gc.DrawText(finger.Text, finger.PenX(left + i * s),
                    finger.Baseline(bottomY + m.FingerTop - finger.Height / 2), finger.Em,
                    TextRole.FretFrame, style, TextAnchor.Start, Color.Black);

        // "Nfr": centred half a fret under the top line, right of the strings.
        // LILYPOND-REF: scm/fret-diagrams.scm:816-852 label-fret — fret-label-vertical-offset, label-outside-diagram
        if (m.FretLabel is { } fl)
            gc.DrawText(fl.Text, fl.PenX(cx + m.LabelCentreX), fl.Baseline(bottomY + m.LabelCentreY), fl.Em,
                TextRole.FretFrame, style, TextAnchor.Start, Color.Black);
    }

    /// <summary>
    /// Draws a guitar bend-up: a curve leaving the note/fret nearly
    /// horizontally then rising steeply, an arrowhead at the top, and the
    /// bend amount ("½", "full", "1½", …) above the arrowhead — the TAB convention
    /// (MusicXML's bend-alter), a polyline along a quadratic 1.6 long and 2.6 high.
    /// LILYSHARP-OWN: LilyPond's guitar bend is the BendSpanner (`\^`) — a line and curve to
    /// the target fret, an arrow head and the amount as text, sized by its details — which
    /// Lily# has not ported; this arrow is its own device.
    ///   departs from: scm/output-lib.scm:2844 bend-spanner::print (its geometry is not read here).
    ///   goes away when: the bend spanner is ported (with the layout's BendTabXOffset / BendHeadPadding).
    ///   observed by: none against LilyPond.
    /// </summary>
    private static void DrawGuitarBend(ScoreTextMetrics fonts, double x0, double y0, int semitones, IDrawingContext gc)
    {
        const double len = 1.6;    // horizontal reach
        const double rise = 2.6;   // vertical reach (upward = larger Y-up)
        double cx = x0 + len * 0.95, cy = y0 + rise * 0.1; // sharp late turn upward
        double topX = x0 + len, topY = y0 + rise;
        double px = x0, py = y0;
        const int seg = 10;
        for (int s = 1; s <= seg; s++)
        {
            double t = s / (double)seg, u = 1 - t;
            double nx = u * u * x0 + 2 * u * t * cx + t * t * topX;
            double ny = u * u * y0 + 2 * u * t * cy + t * t * topY;
            gc.DrawLine(px, py, nx, ny, Color.Black, 0.13, cap: LineCap.Round);
            px = nx; py = ny;
        }
        // Arrowhead: a V of two strokes at the curve's top (no polygon API).
        const double ah = 0.55, aw = 0.32;
        gc.DrawLine(topX - aw, topY - ah, topX, topY, Color.Black, 0.16, cap: LineCap.Round);
        gc.DrawLine(topX + aw, topY - ah, topX, topY, Color.Black, 0.16, cap: LineCap.Round);
        // Amount label in guitar convention: semitones → steps.
        string label = semitones switch
        {
            1 => "½",
            2 => "full",
            _ => (semitones % 2 == 0) ? (semitones / 2).ToString() : $"{semitones / 2}½",
        };
        gc.DrawText(label, topX, topY + 0.35, fonts.Size(TextRole.Bend, BendLabelEm),
            TextRole.Bend, fonts.Style(TextRole.Bend, FontStyle.Italic),
            TextAnchor.Middle, Color.Black);
    }

    // ---------- Lyrics ----------

    /// <summary>
    /// Draws lyric syllables (and any hyphen / extender connectors) below
    /// the staff using serif text at the LP-style reduced size.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:32-52 LyricText grob
    /// LILYPOND-REF: scm/define-grobs.scm:2213-2230 LyricText, whose self-alignment-X is left-align-at-split-notes, declares (font-size . 1.0) — the size now
    /// comes from EngravingDefaults.LyricTextFontSize, shared with LyricEngraver so the
    /// drawn ink and the reserved ink are the same size. It used to be a local
    /// FontSize * 0.8 (= 3.2), 29.6% larger, with the divergence declared right here.
    /// </remarks>
    private static void DrawLyrics(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.LyricLayouts.IsDefaultOrEmpty) return;
        // Through the score's plan — the same call every reservation of a syllable makes
        // (LyricEngraver.LyricFontSize / LyricStyle), so `fonts { lyrics step -1 }` moves
        // the drawn ink and the reserved ink together.
        double lyricFontSize = LilySharp.Core.Svg.Layout.LyricEngraver.LyricFontSize(fonts);
        var lyricStyle = LilySharp.Core.Svg.Layout.LyricEngraver.LyricStyle(fonts);
        foreach (var l in layout.LyricLayouts)
        {
            if (!sysTopYUp.TryGetValue(l.Item.MeasureIndex, out var syUp)) continue; // other page
            // Page Y-up baseline: lift the system top and add the stored offset.
            double y = syUp + l.YUp;
            // Tag the syllable with its source offset (data-pos) so the preview can
            // click-to-jump and editor-highlight it like a note. SourcePosition 0
            // means "unknown" (would clash with the bar-0 section mark), so only
            // scope a real offset.
            if (l.Item.SourcePosition > 0)
                using (gc.Source(l.Item.SourcePosition))
                    gc.DrawText(l.Item.Text, l.X, y, lyricFontSize, TextRole.LyricText,
                        lyricStyle, TextAnchor.Middle, Color.Black);
            else
                gc.DrawText(l.Item.Text, l.X, y, lyricFontSize, TextRole.LyricText,
                    lyricStyle, TextAnchor.Middle, Color.Black);
            // Hyphen dashes / extender lines: DrawLyricHyphens (LyricHyphen
            // layouts) — the single source, matching LP's grobs.
        }
    }

    // ---------- Hairpins (cresc / dim wedges) ----------

    /// <summary>
    /// Draws crescendo/decrescendo wedges as a pair of straight lines that
    /// converge to a point (cresc) or open from a point (dim).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hairpin.cc:110-358 print()
    /// LILYPOND-REF: scm/define-grobs.scm:1777 Hairpin grob (thickness = 1.0)
    /// </remarks>
    private static void DrawHairpins(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.HairpinLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var h in layout.HairpinLayouts)
        {
            if (!sysTopYUp.TryGetValue(h.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift the system top (H − sy), add the stored offset, then
            // apply the ossia affine in the same frame.
            double absY = os.YUp(syUp + h.YUp, h.StaffIndex, h.StartMeasureIndex);
            double startOpening = os.Size(h.StartOpening, h.StaffIndex);
            double endOpening = os.Size(h.EndOpening, h.StaffIndex);
            double leftTop = absY + startOpening;
            double leftBottom = absY - startOpening;
            double rightTop = absY + endOpening;
            double rightBottom = absY - endOpening;
            using (gc.Source(h.SourcePosition))
            using (gc.Kind("hairpin"))
            using (gc.Staff(h.StaffIndex))
            {
                // Round caps so the two arms close cleanly at the wedge apex
                // (where StartOpening or EndOpening is 0): butt caps left a
                // small V-notch at the point. Matches LilyPond's blot-rounded
                // line ends. LILYPOND-REF: lily/hairpin.cc — Round_ blot.
                gc.DrawLine(h.StartX, leftTop, h.EndX, rightTop, Color.Black, thickness, cap: LineCap.Round);
                gc.DrawLine(h.StartX, leftBottom, h.EndX, rightBottom, Color.Black, thickness, cap: LineCap.Round);
                // The circle at a niente tip: an open circle of the hairpin's own line.
                // LILYPOND-REF: lily/hairpin.cc:342-355 Hairpin::print — circled_tip: (circle rad thick #f)
                if (!double.IsNaN(h.CircleX))
                {
                    double r = os.Size(LilySharp.Core.Svg.Layout.HairpinEngraver.CircleRadius, h.StaffIndex);
                    gc.DrawEllipse(h.CircleX, absY, r, r, fill: null, stroke: Color.Black,
                        strokeWidth: LilySharp.Core.Svg.Layout.HairpinEngraver.CircleThickness);
                }
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
    private static void DrawOttavaBrackets(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.OttavaBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var b in layout.OttavaBracketLayouts)
        {
            // Which staff it hangs on, for lysc boxes (a no-op in every picture).
            using var staffScope = gc.Staff(b.StaffIndex);
            if (!sysTopYUp.TryGetValue(b.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift the system top and add the stored offset, then ossia.
            double absY = os.YUp(syUp + b.YUp, b.StaffIndex, b.StartMeasureIndex);
            // The label rides the paper's text-font-size, which OttavaBracket does not step
            // (it declares only font-series and font-shape) — the em the TextScript pair
            // measured, not the 0.45 × FontSize this drew until 2026-08-02
            // (ledger ottava.label.ink-height, −0.288062616 before the port).
            double textFontSize = os.Size(OttavaBracketEngraver.LabelEm(fonts), b.StaffIndex);
            var textStyle = OttavaBracketEngraver.LabelStyle(fonts);
            using (gc.Source(b.SourcePosition))
            {
                // LilyPond CENTRES the label's ink on the line — text.align_to (Y_AXIS,
                // CENTER) in Ottava_bracket::print, which its symmetric grob extent shows
                // through — so the baseline sits the ink's own centre BELOW it. Drawing the
                // baseline on the line put the label 0.621000054 too high
                // (ledger ottava.label.line-to-ink-centre), and the reservation reads the
                // same offset (OttavaBracketEngraver.Skylines).
                gc.DrawText(b.Text, b.StartX,
                    absY - os.Size(
                        OttavaBracketEngraver.LabelInkCentre(
                            fonts, b.Text, OttavaBracketEngraver.LabelEm(fonts)),
                        b.StaffIndex),
                    textFontSize, TextRole.Ottava,
                    textStyle, TextAnchor.Start, Color.Black);

                double lineStartX = OttavaBracketEngraver.LineStartX(
                    fonts, b.Text, b.StartX, textFontSize);
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
                    gc.DrawLine(b.EndX, absY, b.EndX,
                        absY - os.Size(b.EdgeHeight, b.StaffIndex) * hookDir,
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
    private static void DrawVoltaBrackets(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, IDrawingContext gc)
    {
        if (layout.VoltaBracketLayouts.IsDefaultOrEmpty) return;
        double thickness = VoltaBracketEngraver.LineThickness;
        double edgeHeight = VoltaBracketEngraver.GetEdgeHeight();
        double numberSize = VoltaBracketEngraver.NumberEm(fonts);
        var numberStyle = VoltaBracketEngraver.NumberStyle(fonts);

        foreach (var v in layout.VoltaBracketLayouts)
        {
            if (!sysTopYUp.TryGetValue(v.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift this segment's own system top and add the stored offset.
            double absY = syUp + v.YUp;
            // The hooks hang edge-height below the LINE'S BOTTOM EDGE, not below its centre.
            // LILYPOND-REF: lily/bracket.cc:40-96 Bracket::make_bracket — the edge is drawn
            //   from the line's own thickness outward, which is why LilyPond's grob extent
            //   reads (-2.08 . 0.08) about a centre with half-thickness 0.08: 2.0 below the
            //   BOTTOM. Hanging it from the centre instead cost 0.065 of the bracket's height
            //   (ledger page.volta.no-ink.staff-to-line).
            double hookTip = absY - thickness / 2.0 - edgeHeight;
            bool hasText = !string.IsNullOrEmpty(v.VoltaText);
            using (gc.Source(v.SourcePosition))
            {
                if (hasText)
                    gc.DrawLine(v.StartX, absY, v.StartX, hookTip, Color.Black, thickness);
                gc.DrawLine(v.StartX, absY, v.EndX, absY,
                    Color.Black, thickness);
                if (v.IsClosed)
                    gc.DrawLine(v.EndX, absY, v.EndX, hookTip, Color.Black, thickness);
                if (hasText)
                {
                    // The number's INK TOP sits NumberOffsetY below the line's centre and its
                    // ink LEFT NumberOffsetX inside the bracket — LilyPond's
                    // volta-number-offset, addresses on the constants. Anchored at the
                    // BASELINE — the only anchor every backend places identically — a further
                    // Ink.Top down; the former VerticalAnchor.Hanging was realised as a
                    // TYPOGRAPHIC-ascent drop on every backend, which left an extra quarter
                    // staff-space of air over a digit's cap and drew the number deeper than
                    // OutsideStaffStacker reserves (the offset plus the string's own
                    // InkHeight, which THIS geometry makes exact).
                    var vInk = fonts.Ink(
                        v.VoltaText, numberSize, TextRole.Volta, numberStyle);
                    double textY = absY - VoltaBracketEngraver.NumberOffsetY - vInk.Top;
                    gc.DrawText(v.VoltaText, v.StartX + VoltaBracketEngraver.NumberOffsetX, textY,
                        numberSize, TextRole.Volta, numberStyle, TextAnchor.Start, Color.Black);
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
    /// LILYPOND-REF: lily/tuplet-bracket.cc:290 Tuplet_bracket::print
    /// LILYPOND-REF: scm/define-grobs.scm TupletBracket defaults
    /// </remarks>
    private static void DrawTupletBrackets(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TupletBracketLayouts.IsDefaultOrEmpty) return;
        // The number's em and slant through the score's plan — the same one home every
        // reservation of the number reads (skyline, stacker, slur scorer).
        double numberEm = TupletBracketEngraver.NumberEm(fonts);
        var numberStyle = TupletBracketEngraver.NumberStyle(fonts);
        // Was a bare 0.13 here, shadowing EngravingDefaults.TupletBracketThickness — which
        // has carried LilyPond's own 1.6 x line-thickness = 0.16, with its LILYPOND-REF,
        // all along and simply had no reader. Drawing and reserving must come from ONE
        // constant or they drift, so both now read that one.
        double thickness = EngravingDefaults.TupletBracketThickness;

        foreach (var b in layout.TupletBracketLayouts)
        {
            // Which staff it hangs on, for lysc boxes (a no-op in every picture).
            using var staffScope = gc.Staff(b.StaffIndex);
            if (!sysTopYUp.TryGetValue(b.MeasureIndex, out var syUp)) continue; // other page
            // The hook's height is edge-height × the staff's ss (1.05 on a tab), as the
            // reach past the bound is (TupletBracketLayout.LineSpacing).
            double edgeHeight = os.Size(TupletBracketEngraver.GetEdgeHeight() * b.LineSpacing, b.StaffIndex);
            // Page Y-up: lift the system top, add the stored offsets, then ossia.
            double startY = os.YUp(syUp + b.StartYUp, b.StaffIndex, b.MeasureIndex);
            double endY = os.YUp(syUp + b.EndYUp, b.StaffIndex, b.MeasureIndex);
            double midX = (b.StartX + b.EndX) / 2;
            double midY = (startY + endY) / 2;
            double hookDir = b.IsStemUp ? 1 : -1;

            using (gc.Source(b.SourcePosition))
            {
                if (b.ShowBracket)
                {
                    // LilyPond's make_bracket over the two positions, every offset taken ALONG
                    // the bracket (dz): the ends reach past the bounds by shorten-pair (−0.2, ×
                    // the staff's ss; TupletBracketLayout.DrawnStartX carries the rule), the
                    // hooks stand at those ends, and the line breaks for the number over
                    // (number's width + 1.0), centred on the midpoint and 0.1 to the right —
                    // "more space at right due to italics".
                    // LILYPOND-REF: lily/bracket.cc:39-87 Bracket::make_bracket — straight_corners, gap_corners, flare_corners
                    // LILYPOND-REF: lily/tuplet-bracket.cc:326-337 Tuplet_bracket::print — gap = ext.length () + 1.0
                    // LILYPOND-REF: lily/tuplet-bracket.cc:401-407 Tuplet_bracket::print — Interval (-0.5, 0.5) * gap + 0.1
                    // Until session 694 the ends ran out LEVEL (X only) and the gap was a flat
                    // ±1.0 about the midpoint, whatever the number's width.
                    double dx = b.EndX - b.StartX, dy = endY - startY;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    double ux = length > 0 ? dx / length : 1.0, uy = length > 0 ? dy / length : 0.0;
                    double reach = TupletBracketLayout.BracketOutwardReach * b.LineSpacing;
                    double lx = b.StartX - reach * ux, ly = startY - reach * uy;
                    double rx = b.EndX + reach * ux, ry = endY + reach * uy;
                    using var bracketKind = gc.Kind("tupletBracket");
                    gc.DrawLine(lx, ly, lx, ly - edgeHeight * hookDir, Color.Black, thickness);
                    if (!string.IsNullOrEmpty(b.NumberText))
                    {
                        double gap = fonts.Advance(b.NumberText, os.Size(numberEm, b.StaffIndex),
                            TextRole.Tuplet, numberStyle) + 1.0;
                        double g0 = -0.5 * gap + 0.1, g1 = 0.5 * gap + 0.1;
                        double mx = b.StartX + dx * 0.5, my = startY + dy * 0.5;
                        gc.DrawLine(lx, ly, mx + g0 * ux, my + g0 * uy, Color.Black, thickness);
                        gc.DrawLine(mx + g1 * ux, my + g1 * uy, rx, ry, Color.Black, thickness);
                    }
                    else
                    {
                        gc.DrawLine(lx, ly, rx, ry, Color.Black, thickness);
                    }
                    gc.DrawLine(rx, ry, rx, ry - edgeHeight * hookDir, Color.Black, thickness);
                }

                // CENTRED ON THE BRACKET LINE, both axes — LilyPond's own placement, and
                // the reason the number rather than the bracket is a tuplet's outermost
                // ink. lily/tuplet-number.cc:342 returns the midpoint of the bracket's
                // positions as the number's Y-offset for every tuplet that is not a knee
                // against a beam, and :227-228 aligns its stencil to CENTER on X and Y.
                // Lily# used to hang it off the line by a bare +0.3 / -0.8 at 2.4 units
                // bold-upright; it is now LilyPond's size and face, and SkylineBuilder
                // reserves exactly this ink.
                gc.DrawText(b.NumberText, midX, midY,
                    os.Size(numberEm, b.StaffIndex), TextRole.Tuplet,
                    numberStyle, TextAnchor.Middle, Color.Black,
                    VerticalAnchor.Middle);
            }
        }
    }

    // ---------- Trill spanners (tr + wavy line) ----------

    /// <summary>
    /// Draws trill spanners: the "tr" Emmentaler glyph, then the line as a RUN of
    /// <c>scripts.trill_element</c> glyphs — which is what the line is.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm Trill_spanner_engraver
    /// LILYPOND-REF: scm/define-grobs.scm:4083 (style . trill) selects
    ///   lily/line-interface.cc:226-229 <c>line</c> -> :48-108 make_trill_line, which repeats
    ///   the element along the line rather than stroking a curve.
    /// ⚠️ Until 2026-07-30 this stroked a parabolic polyline with an amplitude of its own,
    ///   so the drawn line and the reserved one were two models. They are one now: the
    ///   element positions come from <see cref="LilySharp.Core.Svg.Layout.TrillWaveOutline"/>, the same
    ///   house the engraver's aligned_side and the outside-staff pass read.
    /// </remarks>
    private static void DrawTrillSpanners(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.TrillSpannerLayouts.IsDefaultOrEmpty) return;
        foreach (var s in layout.TrillSpannerLayouts)
        {
            // Which staff it hangs on, for lysc boxes (a no-op in every picture).
            using var staffScope = gc.Staff(s.StaffIndex);
            if (!sysTopYUp.TryGetValue(s.StartMeasureIndex, out var syUp)) continue; // other page
            // Page Y-up: lift the system top, add the stored offset, then ossia.
            double absY = os.YUp(syUp + s.YUp, s.StaffIndex, s.StartMeasureIndex);
            using (gc.Source(s.SourcePosition))
            {
                bool isContinuation = Math.Abs(s.GlyphX - s.LineStartX) < 0.01;
                if (!isContinuation)
                {
                    // The "tr" glyph hangs a staff space below the LINE — LilyPond's
                    // bound text carries stencil-offset (0 . -1), so the glyph's ink
                    // straddles the line ((-1.0 . 1.1) in LP's own ext dump) instead of
                    // sitting on it. The layout's YUp is the line; the reservation
                    // (OutsideStaffStacker.PlaceTrills) prices the same offset.
                    // LILYPOND-REF: scm/define-grobs.scm:4068 TrillSpanner stencil-offset, inside its left-bound-info text
                    gc.DrawGlyph(Music(MusicGlyph.OrnamentTrill), s.GlyphX,
                        absY - os.Size(EngravingDefaults.TrillSpannerTextOffsetDown, s.StaffIndex),
                        os.Size(FontSize, s.StaffIndex));
                }
                if (s.LineStartX < s.LineEndX)
                {
                    // The line: one element glyph per copy, at the run's own positions, with
                    // the element's stencil box CENTRED on the line (align_to Y CENTER), so
                    // only whole elements are drawn and the run stops short of the bound by
                    // the remainder — LilyPond's own picture.
                    // LILYPOND-REF: lily/line-interface.cc:68-97 make_trill_line — find the
                    //   element by name, align_to (Y_AXIS, CENTER), add_at_edge per copy.
                    double baseline = absY
                        + os.Size(TrillWaveOutline.GlyphBaselineOffset, s.StaffIndex);
                    double size = os.Size(FontSize, s.StaffIndex);
                    foreach (double originX in TrillWaveOutline.ElementOrigins(
                                 s.LineStartX, s.LineEndX - s.LineStartX))
                    {
                        gc.DrawGlyph(Music(MusicGlyph.WiggleTrill), originX, baseline, size);
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
    private static void DrawGlissandos(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.GlissandoLayouts.IsDefaultOrEmpty) return;
        double thickness = EngravingDefaults.StaffLineThickness;
        foreach (var g in layout.GlissandoLayouts)
        {
            // Which staff it hangs on, for lysc boxes (a no-op in every picture).
            using var staffScope = gc.Staff(g.StaffIndex);
            // MeasureIndex -1 = direct unit-test construction: no page identity,
            // draw unconditionally.
            if (g.MeasureIndex >= 0 && !sysTopYUp.ContainsKey(g.MeasureIndex))
                continue; // other page
            // Frame B -> device: reflect each endpoint against this glissando's own
            // staff middle (the shared per-grob draw boundary), then apply ossia.
            double midYup = os.StaffMiddleYUp(g.StaffIndex, g.MeasureIndex, StaffHeight);
            using (gc.Source(g.SourcePosition))
                gc.DrawLine(
                    g.StartX, os.YUp(midYup + g.StartYUp, g.StaffIndex, g.MeasureIndex),
                    g.EndX, os.YUp(midYup + g.EndYUp, g.StaffIndex, g.MeasureIndex),
                    Color.Black, thickness);
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
    private static void DrawArpeggios(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc)
    {
        if (layout.ArpeggioLayouts.IsDefaultOrEmpty) return;
        foreach (var a in layout.ArpeggioLayouts)
        {
            // Which staff it hangs on, for lysc boxes (a no-op in every picture).
            using var staffScope = gc.Staff(a.StaffIndex);
            // MeasureIndex -1 = direct unit-test construction: no page identity,
            // draw unconditionally.
            if (a.MeasureIndex >= 0 && !sysTopYUp.ContainsKey(a.MeasureIndex))
                continue; // other page
            // ⚠️ EVERYTHING BELOW IS PAGE Y-UP, IN STAFF SPACES, UP-POSITIVE — the frame the
            // layout stores and the frame this renderer draws in. Nothing here converts to
            // device: YFlipDrawingContext does that at the output boundary, so a larger
            // number is HIGHER ON THE PAGE and stepping DOWN means SUBTRACTING. The names
            // carry the frame (…YUp) because a bare `topY` reads as device and cost a sign
            // error in the bracket's end ticks on 2026-08-03.
            double midYUp = os.StaffMiddleYUp(a.StaffIndex, a.MeasureIndex, StaffHeight);
            // Non-arpeggiate: a straight vertical bracket with end ticks — the chord is NOT
            // rolled.
            // LILYPOND-REF: lily/lookup.cc:542-560 Lookup::bracket — three round_filled_box
            //   calls, which is what lily/arpeggio.cc:191-201 Chord_bracket::print hands the
            //   interval to. Both of the things that were wrong here are in their bounds:
            //   :546-547  the SPINE spans the interval and is `thick` wide about the origin.
            //   :551-554  the TOP tick lies at (iv[UP] - thick .. iv[UP]) — INSIDE the
            //             interval, so it adds no length — and spans
            //             (-thick/2 .. thick/2 + protrusion), i.e. it starts at the spine's
            //             LEFT edge, not at its centre, which is why the grob is
            //             `thick + protrusion` wide.
            //   :556-557  the BOTTOM tick, the same box at (iv[DOWN] .. iv[DOWN] + thick).
            // Drawn as strokes rather than boxes: a butt-capped line of width `thick` centred
            // on each box's midline covers exactly that box.
            // ⚠️ NOT PORTED — THE CORNERS: LilyPond's boxes are round_filled_box(b, blot)
            //   with blot = thick, so its corners are rounded; a stroked line's are square.
            //   LP has the stencil, so this is a knowing form divergence, not a Lily#-own
            //   quantity (§5.2 audit, session 158).
            //   The EXTENTS are identical — which is why the ledger's three bracket points
            //   close — and this engine has no blot anywhere, so the difference is a standing
            //   convention rather than something chosen here. Naming it so the next reader does
            //   not take "covers exactly that box" for "is that stencil".
            if (a.Bracket)
            {
                double topYUp = os.YUp(midYUp + a.TopYUp, a.StaffIndex, a.MeasureIndex);
                double bottomYUp = os.YUp(midYUp + a.BottomYUp, a.StaffIndex, a.MeasureIndex);
                double t = os.Size(ArpeggioEngraver.BracketThickness, a.StaffIndex);
                double half = t / 2;
                // The ticks span the grob's own X extent — the one place it is spelled.
                double left = a.X + os.Size(ArpeggioEngraver.BracketXExtent.Left, a.StaffIndex);
                double right = a.X + os.Size(ArpeggioEngraver.BracketXExtent.Right, a.StaffIndex);
                using (gc.Source(a.SourcePosition))
                {
                    gc.DrawLine(a.X, topYUp, a.X, bottomYUp, Color.Black, t);
                    // The ticks lie INSIDE the interval, so each steps half a thickness AWAY
                    // from its own end — down from the top (subtract, this being Y-up) and up
                    // from the bottom. Signed the other way they land just outside and
                    // lengthen the bracket by a whole thickness: 3.700000 where LilyPond
                    // draws 3.500000, which is what audit/lp-geometry chordbracket.y.length
                    // caught the first time this was written.
                    gc.DrawLine(left, topYUp - half, right, topYUp - half, Color.Black, t);
                    gc.DrawLine(left, bottomYUp + half, right, bottomYUp + half, Color.Black, t);
                }
                continue;
            }
            if (a.Copies <= 0) continue;
            // The wiggle is the GLYPH, stacked upward from the pile's bottom one designed
            // height at a time — the same copies the engraver counted and the spacing
            // reserved, so what is drawn and what is spaced for are one calculation.
            // LILYPOND-REF: lily/arpeggio.cc:180-183 add_at_edge, translate_axis
            //   (Arpeggio::print) — `mol.add_at_edge (Y_AXIS, UP, squiggle, 0.0)` per copy,
            //   then `mol.translate_axis (heads[LEFT], Y_AXIS)`.
            // ⚠️ The glyph's box starts at its baseline (0 . 1.0), so copy i's baseline is
            // the pile's bottom raised i heights — no centring term, unlike the trill
            // element's align_to (see TrillWaveOutline.GlyphBaselineOffset).
            // A font that designs the wiggle lying down (SMuFL) has it stood up a quarter turn,
            // and its box as drawn (WiggleBox) need not start at the origin.
            double glyphSize = os.Size(FontSize, a.StaffIndex);
            bool turned = MusicFont.Current.LiesDown(MusicGlyph.WiggleArpeggiatoUp);
            double bottom = ArpeggioEngraver.WiggleBox.Bottom;
            using (gc.Source(a.SourcePosition))
            {
                for (int i = 0; i < a.Copies; i++)
                {
                    double baseline = os.YUp(
                        midYUp + a.BottomYUp + i * ArpeggioEngraver.WiggleHeight - bottom,
                        a.StaffIndex, a.MeasureIndex);
                    if (turned)
                        gc.DrawGlyphTurned(Music(MusicGlyph.WiggleArpeggiatoUp), a.X, baseline, glyphSize);
                    else
                        gc.DrawGlyph(Music(MusicGlyph.WiggleArpeggiatoUp), a.X, baseline, glyphSize);
                }
            }
        }
    }
}
