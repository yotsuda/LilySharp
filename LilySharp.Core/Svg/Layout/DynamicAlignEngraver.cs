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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Aligns hairpins and dynamic texts on a horizontal line: the grobs a RUNNING hairpin
/// links (the text at its start moment, the wedge itself, the text that terminates it,
/// and a hairpin chained on at that same moment) ride ONE DynamicLineSpanner, so the
/// whole group is side-positioned ONCE and every member sits on the same baseline.
/// A dynamic with no hairpin at its moment keeps the one-column placement
/// <see cref="DynamicEngraver.Calculate"/> already gave it — LilyPond's line spanner
/// closes in the very timestep that created it, which IS the one-column case.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/dynamic-align-engraver.cc:119-160 acknowledge_dynamic — every
///   dynamic grob joins the CURRENT line spanner (:141 create_line_spanner, :152
///   Axis_group_interface::add_element).
/// LILYPOND-REF: lily/dynamic-align-engraver.cc:194-235 stop_translation_timestep —
///   :196-208 keep a running_ set of live dynamic spanners; :210
///   <c>bool end = line_ &amp;&amp; running_.empty ()</c> — the line closes only in a
///   timestep where NO hairpin runs. A hairpin therefore keeps the line alive from its
///   start timestep to its end timestep; the terminating text is acknowledged in that
///   final timestep and joins the SAME line; and a second hairpin STARTING at the
///   timestep the first ends is in <c>started_</c> when the first is erased, so the
///   line chains onward unbroken.
/// LILYPOND-REF: scm/define-grobs.scm DynamicLineSpanner — its description names the
///   members ("a vertical baseline to align successive dynamic grobs"); DynamicText
///   hangs <see cref="DynamicEngraver.TextOffsetInSpanner"/> below the spanner
///   (define-grobs.scm:1450 Y-offset, "center on an 'm'") and the Hairpin centres on
///   it (self-alignment-Y . CENTER), which is why the text spends −0.6 inside the
///   group profile and the wedge spends nothing.
/// ⚠️ LILYSHARP-OWN, DISCLOSED (no pair measures either):
///   ⑴ LilyPond BREAKS the line when a new dynamic carries an explicit direction
///     differing from the line's (:125-138). Lily#'s hairpins are always below
///     (the grammar rejects .up on a wedge), so a forced-above text is simply left
///     OUT of the group here instead of breaking it.
///   ⑵ Same-column stacking (two voices' dynamics on one column,
///     <see cref="DynamicEngraver.StackStep"/>) is not re-applied to a grouped text —
///     in LilyPond those come from two Voice contexts and ride two separate lines.
/// </remarks>
internal static class DynamicAlignEngraver
{
    /// <summary>
    /// One DynamicLineSpanner: the chained hairpins (indices into the detected
    /// hairpin items) and the below-staff dynamic texts (indices into the dynamic
    /// layouts) that ride it, spanning [Start..End] moments on one staff.
    /// </summary>
    internal readonly record struct DynamicLine(
        int StaffIndex,
        int StartMeasureIndex, int StartItemIndex,
        int EndMeasureIndex, int EndItemIndex,
        ImmutableArray<int> HairpinItemIndices,
        ImmutableArray<int> DynamicLayoutIndices);

    /// <summary>
    /// One BROKEN PIECE of a multi-member line, in layout-index terms: the members
    /// (indices into the returned dynamic/hairpin layout arrays) that sit on one
    /// system and must move as ONE grob through the outside-staff pass — LilyPond's
    /// pass places the DynamicLineSpanner, not its children one by one.
    /// LILYPOND-REF: scm/define-grobs.scm:1407 DynamicLineSpanner outside-staff-priority
    ///   = 250 — the priority is the SPANNER's.
    /// </summary>
    public readonly record struct AlignedLineGroup(
        ImmutableArray<int> DynamicIndices,
        ImmutableArray<int> HairpinIndices);

    /// <summary>
    /// Rewrites the Y of every grouped member so texts and wedges linked by running
    /// hairpins share one side-positioned line, and returns the per-system member
    /// groups for the outside-staff pass to move as single grobs. Layouts not in any
    /// multi-member group are returned unchanged (byte-identical to their engravers'
    /// own answers) and appear in no group.
    /// </summary>
    public static (ImmutableArray<DynamicLayout> Dynamics, ImmutableArray<HairpinLayout> Hairpins,
                   ImmutableArray<AlignedLineGroup> Groups)
        AlignLines(
            ImmutableArray<HairpinItem> hairpinItems,
            ImmutableArray<DynamicItem> dynamics,
            ImmutableArray<DynamicLayout> dynamicLayouts,
            ImmutableArray<HairpinLayout> hairpinLayouts,
            ImmutableArray<SystemLayout> systems,
            ImmutableArray<MeasureLayout> measureLayouts,
            Func<int, int, double>? staffYAt,
            ImmutableArray<Voice> voices,
            Dictionary<int, ImmutableArray<Voice>>? voicesByStaff,
            Dictionary<int, ImmutableArray<Measure>>? measuresByStaff,
            ImmutableArray<BeamLayout> beamLayouts)
    {
        if (hairpinItems.IsDefaultOrEmpty || hairpinLayouts.IsDefaultOrEmpty)
            return (dynamicLayouts, hairpinLayouts, ImmutableArray<AlignedLineGroup>.Empty);

        var lines = BuildLines(hairpinItems, dynamicLayouts);
        // A line with one wedge and no text is the answer HairpinEngraver already
        // computed (same span supports, same wedge outline) — leave it untouched.
        var grouped = lines.Where(l =>
            l.DynamicLayoutIndices.Length > 0 || l.HairpinItemIndices.Length > 1).ToList();
        if (grouped.Count == 0)
            return (dynamicLayouts, hairpinLayouts, ImmutableArray<AlignedLineGroup>.Empty);
        var groups = ImmutableArray.CreateBuilder<AlignedLineGroup>();

        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var beamMembers = DynamicEngraver.BuildBeamMembers(beamLayouts);

        // A member hairpin's broken pieces, found by the mark identity its layouts
        // carry. A harness-built item without a source index has no way back to its
        // layouts; its line is skipped (production items always carry one).
        var piecesBySource = new Dictionary<int, List<int>>();
        for (int i = 0; i < hairpinLayouts.Length; i++)
            if (hairpinLayouts[i].SourceIndex >= 0)
                (piecesBySource.TryGetValue(hairpinLayouts[i].SourceIndex, out var list)
                    ? list
                    : piecesBySource[hairpinLayouts[i].SourceIndex] = new List<int>()).Add(i);

        var dynBuilder = dynamicLayouts.IsDefaultOrEmpty ? null : dynamicLayouts.ToBuilder();
        var hpBuilder = hairpinLayouts.ToBuilder();

        foreach (var line in grouped)
        {
            var lineVoices = voicesByStaff != null
                && voicesByStaff.TryGetValue(line.StaffIndex, out var vv) ? vv : voices;
            var lineMeasures = LayoutUtilities.ResolveStaffMeasures(
                measuresByStaff, line.StaffIndex,
                lineVoices.IsDefaultOrEmpty ? ImmutableArray<Measure>.Empty : lineVoices[0].Measures);

            // The line's voice: LilyPond's engraver lives in ONE Voice context, so all
            // members share it. The texts carry theirs; a wedge does not (see
            // HairpinEngraver.VoiceIndex) — the first text's voice stands for the line.
            int voiceIndex = 0;
            foreach (int di in line.DynamicLayoutIndices)
            {
                int si = dynamicLayouts[di].SourceIndex;
                if (si >= 0 && si < dynamics.Length)
                {
                    voiceIndex = dynamics[si].VoiceIndex;
                    break;
                }
            }

            // Wedge pieces per system, via each member item's layouts.
            var wedgePieces = new List<(int LayoutIdx, int HairpinItemIdx)>();
            bool resolvable = true;
            foreach (int hi in line.HairpinItemIndices)
            {
                int src = hairpinItems[hi].SourceIndex;
                if (src < 0 || !piecesBySource.TryGetValue(src, out var pieceIdxs))
                {
                    resolvable = false;
                    break;
                }
                foreach (int pi in pieceIdxs)
                    wedgePieces.Add((pi, hi));
            }
            if (!resolvable)
                continue;

            // LILYPOND-REF: lily/spanner.cc:36-144 Spanner::do_break_processing — the
            // DynamicLineSpanner breaks per system and each piece is side-positioned
            // against the supports inside it, so the level is resolved per system, not
            // once for the whole span.
            foreach (int sysIdx in MemberSystems(line, wedgePieces, hairpinLayouts,
                         dynamicLayouts, measureToSystem))
            {
                (VerticalSkyline Up, VerticalSkyline Down)? myDim = null;
                void Fold((VerticalSkyline Up, VerticalSkyline Down) part)
                {
                    if (myDim is { } dim)
                    {
                        dim.Up.Merge(part.Up);
                        dim.Down.Merge(part.Down);
                    }
                    else
                        myDim = part;
                }

                var sysTexts = new List<int>();
                foreach (int di in line.DynamicLayoutIndices)
                {
                    var d = dynamicLayouts[di];
                    if (!measureToSystem.TryGetValue(d.MeasureIndex, out int ds) || ds != sysIdx)
                        continue;
                    sysTexts.Add(di);
                    Fold(DynamicEngraver.LabelSkylines(d.Text, d.IsExpressiveText, d.X,
                        -DynamicEngraver.TextOffsetInSpanner));
                }
                var sysWedges = new List<(int LayoutIdx, int HairpinItemIdx)>();
                foreach (var (pi, hi) in wedgePieces)
                {
                    var piece = hpBuilder[pi];
                    if (!measureToSystem.TryGetValue(piece.StartMeasureIndex, out int ws)
                        || ws != sysIdx)
                        continue;
                    sysWedges.Add((pi, hi));
                    Fold(HairpinEngraver.WedgeSkylines(piece.StartX, piece.EndX,
                        piece.StartOpening, piece.EndOpening, 0.0));
                }
                if (myDim is not { } my)
                    continue;

                var support = DynamicEngraver.SpanSupportSkylines(
                    lineVoices, voiceIndex,
                    SpanColumns(line, sysIdx, lineMeasures, measureLayouts, measureToSystem),
                    (vi, mi, ii) => beamMembers.TryGetValue((line.StaffIndex, vi, mi, ii), out var b)
                        ? b : null);
                double spannerY = DynamicEngraver.SpannerOffsetY(dir: -1.0, support, my);

                foreach (int di in sysTexts)
                    dynBuilder![di] = dynBuilder[di] with
                    {
                        YUp = spannerY - DynamicEngraver.TextOffsetInSpanner,
                    };
                foreach (var (pi, hi) in sysWedges)
                {
                    // The same staff-middle → system-top conversion HairpinEngraver
                    // spends, with ITS convention for the within-system offset (one
                    // lookup per item, at the item's own start measure).
                    double staffOffset = staffYAt?.Invoke(
                        hairpinItems[hi].StartMeasureIndex, line.StaffIndex) ?? 0;
                    hpBuilder[pi] = hpBuilder[pi] with
                    {
                        YUp = spannerY - EngravingDefaults.StaffMiddle - staffOffset,
                    };
                }
                groups.Add(new AlignedLineGroup(
                    sysTexts.ToImmutableArray(),
                    sysWedges.Select(w => w.LayoutIdx).ToImmutableArray()));
            }
        }

        return (dynBuilder?.ToImmutable() ?? dynamicLayouts, hpBuilder.ToImmutable(),
            groups.ToImmutable());
    }

    /// <summary>
    /// Builds the lines: per staff, hairpins sorted by start moment chain while each
    /// starts no later than the running end (:210 — the set of running spanners never
    /// empties at a shared timestep), and every below-staff dynamic text whose moment
    /// falls inside the chained span [start..end] (both ends inclusive: the start
    /// text and the terminating text are acknowledged while the line is alive) joins.
    /// </summary>
    internal static ImmutableArray<DynamicLine> BuildLines(
        ImmutableArray<HairpinItem> hairpinItems,
        ImmutableArray<DynamicLayout> dynamicLayouts)
    {
        var lines = ImmutableArray.CreateBuilder<DynamicLine>();
        foreach (var staffGroup in hairpinItems
            .Select((hp, i) => (Item: hp, Index: i))
            .GroupBy(x => x.Item.StaffIndex))
        {
            var ordered = staffGroup
                .OrderBy(x => x.Item.StartMeasureIndex)
                .ThenBy(x => x.Item.StartItemIndex)
                .ToList();
            int at = 0;
            while (at < ordered.Count)
            {
                var first = ordered[at];
                var members = ImmutableArray.CreateBuilder<int>();
                members.Add(first.Index);
                (int M, int I) start = (first.Item.StartMeasureIndex, first.Item.StartItemIndex);
                (int M, int I) end = (first.Item.EndMeasureIndex, first.Item.EndItemIndex);
                at++;
                while (at < ordered.Count)
                {
                    var next = ordered[at];
                    if (Cmp((next.Item.StartMeasureIndex, next.Item.StartItemIndex), end) > 0)
                        break;
                    members.Add(next.Index);
                    var nextEnd = (next.Item.EndMeasureIndex, next.Item.EndItemIndex);
                    if (Cmp(nextEnd, end) > 0)
                        end = nextEnd;
                    at++;
                }

                var texts = ImmutableArray.CreateBuilder<int>();
                for (int di = 0; di < dynamicLayouts.Length; di++)
                {
                    var d = dynamicLayouts[di];
                    if (d.IsAbove || d.IsExpressiveText || d.StaffIndex != staffGroup.Key)
                        continue;
                    var moment = (d.MeasureIndex, d.ItemIndex);
                    if (Cmp(moment, start) >= 0 && Cmp(moment, end) <= 0)
                        texts.Add(di);
                }

                lines.Add(new DynamicLine(staffGroup.Key,
                    start.M, start.I, end.M, end.I,
                    members.ToImmutable(), texts.ToImmutable()));
            }
        }
        return lines.ToImmutable();
    }

    private static int Cmp((int M, int I) a, (int M, int I) b)
        => a.M != b.M ? a.M.CompareTo(b.M) : a.I.CompareTo(b.I);

    /// <summary>The systems any member of the line lands on, each visited once.</summary>
    private static IEnumerable<int> MemberSystems(DynamicLine line,
        List<(int LayoutIdx, int HairpinItemIdx)> wedgePieces,
        ImmutableArray<HairpinLayout> hairpinLayouts,
        ImmutableArray<DynamicLayout> dynamicLayouts,
        Dictionary<int, int> measureToSystem)
    {
        var seen = new SortedSet<int>();
        foreach (int di in line.DynamicLayoutIndices)
            if (measureToSystem.TryGetValue(dynamicLayouts[di].MeasureIndex, out int s))
                seen.Add(s);
        foreach (var (pi, _) in wedgePieces)
            if (measureToSystem.TryGetValue(hairpinLayouts[pi].StartMeasureIndex, out int s))
                seen.Add(s);
        return seen;
    }

    /// <summary>
    /// The (measure, item, column X) of every timestep the line runs over inside one
    /// system — the heads and stems LilyPond's engraver adds as support at each
    /// stop_translation_timestep while the line is alive.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/dynamic-align-engraver.cc:222-223 add_support.</remarks>
    private static IEnumerable<(int Measure, int Item, double X)> SpanColumns(
        DynamicLine line, int sysIdx,
        ImmutableArray<Measure> staffMeasures, ImmutableArray<MeasureLayout> measureLayouts,
        Dictionary<int, int> measureToSystem)
    {
        if (staffMeasures.IsDefaultOrEmpty)
            yield break;
        for (int m = line.StartMeasureIndex;
             m <= line.EndMeasureIndex && m < measureLayouts.Length && m < staffMeasures.Length;
             m++)
        {
            if (!measureToSystem.TryGetValue(m, out int s) || s != sysIdx)
                continue;
            var layout = measureLayouts[m];
            int itemCount = staffMeasures[m].Items.Length;
            int from = m == line.StartMeasureIndex ? line.StartItemIndex : 0;
            int to = m == line.EndMeasureIndex ? line.EndItemIndex : itemCount - 1;
            for (int i = Math.Max(0, from); i <= to && i < itemCount; i++)
                yield return (m, i,
                    layout.X + LayoutUtilities.GetItemXOffset(staffMeasures, m, i, layout));
        }
    }
}
