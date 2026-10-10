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
/// layout's reservation of the memos' rows (<see cref="TodoMemoBand"/>), so the two price
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
        // Never shallower than a face's usual ascent and descent: a text the metrics cannot
        // measure came back with no height (InkRecorder's floor, the same numbers).
        ascent = Math.Max(ascent * Scale * scale, 0.72 * em);
        descent = Math.Max(descent * Scale * scale, 0.22 * em);
        return (em, width, ascent, descent + (UnderlineDrop + Pen) * scale);
    }
}

/// <summary>
/// The rows the preview's memos need above a system (below, for <c>.down</c>), reserved by
/// the layout as a BAND over the system's own ink when <see cref="LayoutOptions.TodoBands"/>
/// is on — the interactive preview's layout, never an export's. The memos themselves are
/// still set at render time, where the page's ink is clear (<c>SharedRenderer.DrawTodoMarks</c>);
/// the band is what makes sure such a place exists near the note instead of beside another
/// system or past the page's top (owner's decision 2026-10-10: the page's other elements may
/// move for a memo, so long as the result is tidy; the p896 probe `one`, five memos on one
/// system, had two of them over each other at the page's edge).
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: an editor's aid with no LilyPond counterpart. The estimate is the memos'
/// own boxes packed into rows, left to right, each on the side of its note with more room —
/// the drawer's first choice; a memo the drawer then crosses to the other side or lifts over
/// a mark needs no more rows than this, only other ones. The band is a full-width box: a
/// memo may stand anywhere along its line, the page's margin included.
/// </remarks>
internal static class TodoMemoBand
{
    /// <summary>Per system, the band's height above and below the system's ink (0 where
    /// the system has no memo on that side).</summary>
    public readonly record struct Bands(double Up, double Down);

    /// <summary>
    /// The bands of every system of one placement: how much higher the memos above lift
    /// the system's up extent, and how much deeper the memos below push its down extent.
    /// Reads the live score's items and the systems' measure layouts, exactly as the
    /// drawer does.
    /// </summary>
    public static ImmutableArray<Bands> Estimate(MultiStaffScore score, ImmutableArray<SystemLayout> systems)
    {
        var fonts = score.TextMetrics;
        var above = new List<(double Left, double Right)>[systems.Length];
        var below = new List<(double Left, double Right)>[systems.Length];
        var rowUp = new double[systems.Length];
        var rowDown = new double[systems.Length];
        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length; s++)
            foreach (var ml in systems[s].Measures)
                measureToSystem[ml.MeasureIndex] = s;

        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            foreach (var voice in staff.Voices)
            {
                var measures = voice.Measures;
                for (int mi = 0; mi < measures.Length; mi++)
                {
                    var items = measures[mi].Items;
                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        var item = items[ii];
                        if (item.TodoMemo is not { Length: > 0 } memo)
                            continue;
                        if (!measureToSystem.TryGetValue(mi, out int s))
                            continue;
                        var system = systems[s];
                        MeasureLayout? measureLayout = null;
                        foreach (var ml in system.Measures)
                            if (ml.MeasureIndex == mi) { measureLayout = ml; break; }
                        if (measureLayout == null)
                            continue;
                        // The note's column, about where its ring's centre stands.
                        double x = measureLayout.X + LayoutUtilities.GetItemXOffset(measures, mi, ii, measureLayout) + 0.6;
                        double sysLeft = system.Measures[0].X;
                        double sysRight = system.Measures[^1].X + system.Measures[^1].Width;
                        var (_, width, boxUp, boxDown) = TodoMemoStyle.MetricsOf(fonts, memo, 1.0);
                        bool toRight = sysRight - x >= x - sysLeft;
                        double left = toRight ? x + TodoMemoStyle.Offset : x - TodoMemoStyle.Offset - width;
                        var side = item.TodoBelow ? (below[s] ??= new()) : (above[s] ??= new());
                        side.Add((left, left + width));
                        // One row's pitch: the box, and the air the drawer keeps about it.
                        double pitch = boxUp + boxDown + TodoMemoStyle.Air;
                        if (item.TodoBelow)
                            rowDown[s] = Math.Max(rowDown[s], pitch);
                        else
                            rowUp[s] = Math.Max(rowUp[s], pitch);
                    }
                }
            }
        }

        var bands = ImmutableArray.CreateBuilder<Bands>(systems.Length);
        for (int s = 0; s < systems.Length; s++)
            bands.Add(new Bands(BandOf(above[s], rowUp[s]), BandOf(below[s], rowDown[s])));
        return bands.MoveToImmutable();
    }

    /// <summary>The band one side's memos need: the gap the first row keeps from the ink
    /// under it, then as many rows as the memos' boxes take packed left to right.</summary>
    private static double BandOf(List<(double Left, double Right)>? memos, double pitch)
    {
        if (memos is null || memos.Count == 0)
            return 0;
        memos.Sort((a, b) => a.Left.CompareTo(b.Left));
        // Greedy rows: each memo joins the first row whose last memo it clears by the air.
        var rowRights = new List<double>();
        foreach (var (left, right) in memos)
        {
            int row = -1;
            for (int r = 0; r < rowRights.Count; r++)
                if (rowRights[r] + TodoMemoStyle.Air <= left) { row = r; break; }
            if (row < 0)
                rowRights.Add(right);
            else
                rowRights[row] = right;
        }
        return TodoMemoStyle.Gap + rowRights.Count * pitch;
    }
}
