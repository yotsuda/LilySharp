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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The look of the preview's <c>@todo</c> marks — the ring, the memo's text and its leader —
/// in staff spaces, shared by the drawer (<c>SharedRenderer.DrawTodoMarks</c>) and the
/// layout's reservation of the memos' room (<see cref="TodoMemoBand"/>), so the two price
/// the same memo alike.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN, every one: an editor's mark, with no LilyPond counterpart. Owner's
/// decisions 2026-10-10 — a ring as a hand draws one (not a red head, not a compass
/// ellipse), one ring round a whole chord, the ring's pen thicker than the leader's, the
/// memo smaller than @text (memos run long) and set off the note's column by ONE distance
/// towards the side with more room, with no box round it.
/// </remarks>
internal static class TodoMemoStyle
{
    public const double RingPadding = 0.35;    // the ring stands this far outside the glyphs' box
    public const double Pen = 0.2;             // ONE pen for the ring, the memo's underline and the leader (owner: unequal pens look wrong)
    public const double UnderlineDrop = 0.15;  // the underline this far under the memo's descent
    public const double Offset = 1.5;          // from the ring's centre X to the memo's near end
    public const double Gap = 0.6;             // between the memo's ink and what it stands over
    public const double Air = 1.0;             // between two memos side by side on one level
    public const double Scale = 0.75;          // the memo's em over @text's
    public const double LeaderCrossing = 4.0;  // a leader through another memo's words costs this much climbing

    /// <summary>The memo's text size, width, and its box's reach above and below the
    /// baseline (ascent; descent + underline + pen), at the given scale.</summary>
    public static (double Em, double Width, double BoxUp, double BoxDown) MetricsOf(
        ScoreTextMetrics fonts, string memo, double scale)
    {
        double em = DynamicEngraver.LabelEm(fonts, true) * Scale * scale;
        double width = 2 * DynamicEngraver.LabelHalfWidth(fonts, memo, expressive: true) * Scale * scale;
        var (ascent, descent) = DynamicEngraver.InkOf(fonts, memo, true);
        // The text's own ink, as the drawer measures it — floored at a face's usual ascent and
        // descent only where the metrics cannot measure it and it came back with no height
        // (InkRecorder's floor, the same numbers and the same test). Floored always, the
        // reservation stood a third of a space over the drawn memo.
        ascent *= Scale * scale;
        descent *= Scale * scale;
        if (ascent + descent < 0.1 * em)
        {
            ascent = Math.Max(ascent, 0.72 * em);
            descent = Math.Max(descent, 0.22 * em);
        }
        return (em, width, ascent, descent + (UnderlineDrop + Pen) * scale);
    }
}

/// <summary>
/// The room the preview's memos need over a system (under it, for <c>.down</c>), reserved by
/// the layout when <see cref="LayoutOptions.TodoBands"/> is on — the interactive preview's
/// layout, never an export's. The memos themselves are still set at render time, where the
/// page's ink is clear (<c>SharedRenderer.DrawTodoMarks</c>); this is what makes sure such a
/// place exists near the note instead of beside another system or past the page's top
/// (owner's decision 2026-10-10: the page's other elements may move for a memo, so long as
/// the result is tidy; the p896 probe `one`, six memos on one system, had two of them over
/// each other at the page's edge).
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: an editor's aid with no LilyPond counterpart. The reservation is each
/// memo's own box where the drawer would set it — off its note to the side whose ink under
/// the box is lower (the roomier side among equals), on the system's paging silhouette at
/// that X, stacked over the memos estimated before it where they overlap — joined to the
/// silhouette as X-aware boxes. So a memo that fits in the room the page has anyway
/// (between the lyrics of the system above and a bar with nothing over it, in the margin)
/// moves nothing: the inter-system floor grows only where the system above really reaches
/// down to the box, and the page's top only where the box stands higher than the system's
/// own topmost ink (owner's report 2026-10-11: a full-width band over every system with a
/// memo widened the systems' spacing for nothing — `Untitled-2.lys` can show its memos
/// without a space of new room).
/// </remarks>
internal static class TodoMemoBand
{
    /// <summary>A memo's box in the system's origin frame (X as the measures', Y up).</summary>
    public readonly record struct Box(double X0, double X1, double Bottom, double Top);

    /// <summary>
    /// The boxes the memos of <paramref name="system"/> would take, over its paging
    /// silhouette <paramref name="up"/> (under <paramref name="down"/> for <c>.down</c>) —
    /// null where the system has no memo. Reads the live score's items and the system's
    /// measure layouts, as the drawer does.
    /// </summary>
    public static List<Box>? Boxes(MultiStaffScore score, SystemLayout system,
        VerticalSkyline up, VerticalSkyline down)
    {
        if (system.Measures.IsDefaultOrEmpty)
            return null;
        var fonts = score.TextMetrics;
        double sysLeft = system.Measures[0].X;
        double sysRight = system.Measures[^1].X + system.Measures[^1].Width;
        var measureToLayout = new Dictionary<int, MeasureLayout>();
        foreach (var ml in system.Measures)
            measureToLayout[ml.MeasureIndex] = ml;

        // The memos, each with its note's column, its text's box and its ring's outer edges
        // (the drawer starts a memo at the ring's edge plus Gap — the marked item's ink +
        // RingPadding); the short first, as the drawer sets them.
        var memos = new List<(double X, bool Below, double Width, double Height, double RingTop, double RingBottom)>();
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
        {
            int staffLines = MultiStaffLayouter.NotationStaffLinesOf(staff);
            double spacing = MultiStaffLayouter.LineSpacingOf(staff);
            double middle = LayoutUtilities.StaffMiddleUpInSystem(system, staffIndex);
            foreach (var voice in staff.Voices)
            {
                var measures = voice.Measures;
                for (int mi = 0; mi < measures.Length; mi++)
                {
                    if (!measureToLayout.TryGetValue(mi, out var measureLayout))
                        continue;
                    var items = measures[mi].Items;
                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        var item = items[ii];
                        if (item.TodoMemo is not { Length: > 0 } memo)
                            continue;
                        var (_, width, boxUp, boxDown) = TodoMemoStyle.MetricsOf(fonts, memo, 1.0);
                        // The ring's box as the drawer rings it (the head, the chord's heads, the
                        // rest — never the stem: a stem-up note's ring stands far under its stem's
                        // top, and a floor read off the silhouette over the column reserved a memo
                        // over a note inside the staff a stem too high).
                        if (SharedRenderer.TodoInkBox(item, measureLayout, measures, mi, ii, staffLines, spacing)
                            is var (left, right, bottom, top))
                            memos.Add(((left + right) / 2, item.TodoBelow, width, boxUp + boxDown,
                                middle + top + TodoMemoStyle.RingPadding, middle + bottom - TodoMemoStyle.RingPadding));
                        else
                            memos.Add((measureLayout.X + LayoutUtilities.GetItemXOffset(measures, mi, ii, measureLayout) + 0.6,
                                item.TodoBelow, width, boxUp + boxDown, double.NegativeInfinity, double.PositiveInfinity));
                    }
                }
            }
        }
        // …and the chords rows' memos (ChordNameItem.TodoMemo, docs/todo-memo-design.md §5): a
        // row's symbol stands on its timing column (ChordNameEngraver.SymbolX), its ring's centre
        // half its ink to the right. Without these a row's memo had no room reserved and climbed
        // over the title (the p902 probe `chordtodo`).
        foreach (var chord in score.ChordNames)
        {
            if (chord.TodoMemo is not { Length: > 0 } memo || !chord.UseTiming
                || !measureToLayout.TryGetValue(chord.MeasureIndex, out var measureLayout))
                continue;
            double inkWidth = ChordNameEngraver.SymbolInkWidth(fonts, chord);
            double x0 = measureLayout.X + measureLayout.GetXForTiming(chord.Timing);
            var (_, width, boxUp, boxDown) = TodoMemoStyle.MetricsOf(fonts, memo, 1.0);
            double ringTop = up.IsEmpty ? 0 : up.MaxHeightInRange(x0, x0 + inkWidth);
            double ringBottom = down.IsEmpty ? 0 : down.MaxHeightInRange(x0, x0 + inkWidth);
            memos.Add((x0 + inkWidth / 2, chord.TodoBelow, width, boxUp + boxDown,
                (double.IsInfinity(ringTop) ? 0 : ringTop) + TodoMemoStyle.RingPadding,
                (double.IsInfinity(ringBottom) ? 0 : ringBottom) - TodoMemoStyle.RingPadding));
        }
        if (memos.Count == 0)
            return null;
        memos.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.X.CompareTo(b.X));

        var boxes = new List<Box>(memos.Count);
        foreach (var (x, below, width, height, ringTop, ringBottom) in memos)
        {
            bool roomierRight = sysRight - x >= x - sysLeft;
            var right = Settle(x + TodoMemoStyle.Offset);
            var left = Settle(x - TodoMemoStyle.Offset - width);
            // The side whose box stands nearer the system (ties to the roomier side).
            bool toRight = below
                ? (roomierRight ? right.Top >= left.Top : right.Top > left.Top)
                : (roomierRight ? right.Bottom <= left.Bottom : right.Bottom < left.Bottom);
            boxes.Add(toRight ? right : left);

            // The box with its left end at l: on the ink under it plus the gap — never nearer
            // than the ring's edge plus the gap, where the drawer starts it — or over the boxes
            // already estimated that it overlaps, whichever is further out. (The box set off to
            // the side stands over lower ink than the marked item itself: without the ring's
            // floor a high note's memo was reserved a ring's height too low, and the drawer
            // climbed past the reservation.)
            // Its OUTER edge carries the half Air the drawer keeps round a memo
            // (SharedRenderer.DrawTodoMarks: a place is clear when the box grown by Air/2 above
            // and below meets no ink). Without it the reservation ended at the memo's own top,
            // the title on the first page came down to the markup padding over it — 0.5, the
            // drawer's air exactly — and the drawer, finding its first place blocked by a hair,
            // climbed a step and then OVER the title with its leader across the letters
            // (2026-10-11, Lab sessions/p902/probe `chordtodo`; TodoCalloutTests
            // AMemo_UnderTheTitle_StaysUnderIt). The
            // near edge already has it: Gap (0.6) under the box exceeds Air/2.
            Box Settle(double l)
            {
                double x0 = l - TodoMemoStyle.Air * 0.4, x1 = l + width + TodoMemoStyle.Air * 0.4;
                if (!below)
                {
                    double ink = up.IsEmpty ? 0 : up.MaxHeightInRange(x0, x1);
                    double bottom = Math.Max(double.IsInfinity(ink) ? 0 : Math.Max(ink, 0), ringTop) + TodoMemoStyle.Gap;
                    foreach (var b in boxes)
                        if (b.X0 < x1 && x0 < b.X1 && b.Top > 0)
                            bottom = Math.Max(bottom, b.Top + TodoMemoStyle.Air / 2);   // b.Top holds the other half
                    return new Box(l, l + width, bottom, bottom + height + TodoMemoStyle.Air / 2);
                }
                else
                {
                    double ink = down.IsEmpty ? 0 : down.MaxHeightInRange(x0, x1);
                    double top = Math.Min(double.IsInfinity(ink) ? 0 : Math.Min(ink, 0), ringBottom) - TodoMemoStyle.Gap;
                    foreach (var b in boxes)
                        if (b.X0 < x1 && x0 < b.X1 && b.Bottom < 0)
                            top = Math.Min(top, b.Bottom - TodoMemoStyle.Air / 2);   // b.Bottom holds the other half
                    return new Box(l, l + width, top - height - TodoMemoStyle.Air / 2, top);
                }
            }
        }
        return boxes;
    }
}
