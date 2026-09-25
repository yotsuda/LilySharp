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
using LilySharp.Core.Rendering.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Rendering;

internal static partial class SharedRenderer
{
    /// <summary>
    /// One page of one overlay drawer through the ⒭ overlay fragment memo: replay the text
    /// recorded under <paramref name="hash"/> when the anchors follow the edit window, else run
    /// <paramref name="draw"/> under a capture. The fold is the drawer's own — what its draw
    /// reads of every item on the page (see each caller).
    /// </summary>
    /// <remarks>
    /// MEASURED (session 598, the reader's corpus, 3,760 pitch keystrokes): the page-level
    /// marks, percent repeats, bar numbers and scripts were 0.9%, 0.9%, 0.5% and 0.4% of the
    /// render, drawn live on every page of every keystroke. The item folds below are each
    /// layout record's own value hash with its source offset zeroed — every field the record
    /// grows joins it by construction (the soundness bias of MeasureContentKey) — plus what the
    /// draw reads beside the record (the system top, the staff middle, the page height, the
    /// font plan). An ossia staff turns the whole memo off (SvgSystemFragmentCache.PrepareRender),
    /// so <see cref="OssiaShrink"/> is the identity wherever this runs.
    /// ⚠️ THE ITEM FOLDS' OBSERVER IS THE KEYSTROKE VERIFY, not a unit net: a same-length script
    /// edit also moves the staff it sits on, so the unit nets
    /// (<c>OverlayFragments_Marks…</c>) stay green with the four record folds left out — but the
    /// incremental-against-full verify over real keystrokes does not (session 611: 211 of 5,264
    /// repo fuzz keystrokes and 447 of 1,880 pitch keystrokes mismatched under that poison, 0
    /// with the folds in).
    /// </remarks>
    private static void ThroughOverlayMemo(OverlayDrawerId drawer, long hash, List<int>? anchors,
        SvgDocumentContext fragHost, SvgSystemFragmentCache fragments, int pageIndex, Action draw)
    {
        int[] anchorArray = anchors is null ? [] : anchors.ToArray();
        if (fragments.TryReplayOverlay(drawer, pageIndex, hash, anchorArray, fragHost))
            return;
        using (fragments.BeginOverlayCapture(drawer, pageIndex, hash, anchorArray, fragHost))
            draw();
    }

    private static MeasureContentKey.Hash64 PageFold(ScoreTextMetrics fonts, PageLayout page)
    {
        var hc = new MeasureContentKey.Hash64();
        hc.Add(fonts.Plan.Signature);
        hc.Add(page.Height);
        return hc;
    }

    private static void DrawBarNumbers(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, IDrawingContext gc, SvgDocumentContext? fragHost,
        SvgSystemFragmentCache? fragments, int pageIndex, PageLayout page)
    {
        if (layout.BarNumberLayouts.IsDefaultOrEmpty) return;
        if (fragHost == null)
        {
            DrawBarNumbersLive(fonts, layout, sysTopYUp, gc);
            return;
        }
        var hc = PageFold(fonts, page);
        foreach (var bn in layout.BarNumberLayouts)
        {
            if (!sysTopYUp.TryGetValue(bn.MeasureIndex, out var syUp))
                continue;
            hc.Add(syUp);
            hc.Add(bn.GetHashCode());
        }
        ThroughOverlayMemo(OverlayDrawerId.BarNumbers, hc.ToHashCode(), null, fragHost, fragments!,
            pageIndex, () => DrawBarNumbersLive(fonts, layout, sysTopYUp, gc));
    }

    private static void DrawPercentRepeats(ScoreLayout layout, Dictionary<int, double> sysTopYUp,
        in OssiaShrink os, IDrawingContext gc, SvgDocumentContext? fragHost,
        SvgSystemFragmentCache? fragments, int pageIndex, PageLayout page, ScoreTextMetrics fonts)
    {
        if (layout.PercentRepeatLayouts.IsDefaultOrEmpty) return;
        if (fragHost == null)
        {
            DrawPercentRepeatsLive(layout, sysTopYUp, os, gc);
            return;
        }
        var hc = PageFold(fonts, page);
        List<int>? anchors = null;
        foreach (var pr in layout.PercentRepeatLayouts)
        {
            if (!sysTopYUp.ContainsKey(pr.MeasureIndex))
                continue;
            var staff = os.StaffLayoutOf(pr.StaffIndex, pr.MeasureIndex);
            hc.Add(staff?.Tuning);
            double height = staff?.Height ?? StaffHeight;
            hc.Add(height);
            hc.Add(os.StaffMiddleYUp(pr.StaffIndex, pr.MeasureIndex, height));
            hc.Add((pr with { SourcePosition = 0 }).GetHashCode());
            (anchors ??= new()).Add(pr.SourcePosition);
        }
        var local = os;
        ThroughOverlayMemo(OverlayDrawerId.PercentRepeats, hc.ToHashCode(), anchors, fragHost, fragments!,
            pageIndex, () => DrawPercentRepeatsLive(layout, sysTopYUp, local, gc));
    }

    private static void DrawMusicMarks(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, in OssiaShrink os, IDrawingContext gc,
        SvgDocumentContext? fragHost, SvgSystemFragmentCache? fragments, int pageIndex, PageLayout page)
    {
        if (layout.MusicMarkLayouts.IsDefaultOrEmpty) return;
        if (fragHost == null)
        {
            DrawMusicMarksLive(fonts, layout, sysTopYUp, os, gc);
            return;
        }
        var hc = PageFold(fonts, page);
        List<int>? anchors = null;
        foreach (var m in layout.MusicMarkLayouts)
        {
            if (IsHandledBySpannerEngraver(m.MarkType)) continue;
            if (!sysTopYUp.ContainsKey(m.MeasureIndex)) continue;
            hc.Add(os.ScoreGrobStaffMiddleYUp(m.StaffIndex, m.MeasureIndex, StaffHeight));
            hc.Add((m with { SourcePosition = 0 }).GetHashCode());
            (anchors ??= new()).Add(m.SourcePosition);
        }
        var local = os;
        ThroughOverlayMemo(OverlayDrawerId.MusicMarks, hc.ToHashCode(), anchors, fragHost, fragments!,
            pageIndex, () => DrawMusicMarksLive(fonts, layout, sysTopYUp, local, gc));
    }

    private static void DrawArticulations(ScoreTextMetrics fonts, ScoreLayout layout,
        Dictionary<int, double> sysTopYUp, in OssiaShrink os, IDrawingContext gc,
        SvgDocumentContext? fragHost, SvgSystemFragmentCache? fragments, int pageIndex, PageLayout page)
    {
        if (layout.ArticulationLayouts.IsDefaultOrEmpty) return;
        if (fragHost == null)
        {
            DrawArticulationsLive(fonts, layout, sysTopYUp, os, gc);
            return;
        }
        var hc = PageFold(fonts, page);
        List<int>? anchors = null;
        foreach (var a in layout.ArticulationLayouts)
        {
            if (string.IsNullOrEmpty(a.Glyph)) continue;
            if (!sysTopYUp.ContainsKey(a.MeasureIndex)) continue;
            hc.Add(os.StaffMiddleYUp(a.StaffIndex, a.MeasureIndex, StaffHeight));
            hc.Add((a with { SourcePosition = 0 }).GetHashCode());
            (anchors ??= new()).Add(a.SourcePosition);
        }
        var local = os;
        ThroughOverlayMemo(OverlayDrawerId.Articulations, hc.ToHashCode(), anchors, fragHost, fragments!,
            pageIndex, () => DrawArticulationsLive(fonts, layout, sysTopYUp, local, gc));
    }
}
