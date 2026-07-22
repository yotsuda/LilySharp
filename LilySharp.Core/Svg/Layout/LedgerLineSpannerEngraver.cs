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
/// One LP-faithful ledger-line span: a continuous horizontal line at a given
/// staff position covering one or more noteheads that share the same ledger
/// position and are horizontally close.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ledger-line-spanner.cc — LedgerLineSpanner grob
/// LILYPOND-REF: scm/define-grobs.scm — LedgerLineSpanner.length-fraction = 0.25
/// </remarks>
public readonly record struct LedgerLineSpan(
    int SystemIndex,
    int StaffPosition,
    double LeftX,
    double RightX,
    // Within-system Y offset (device, down from the system top) of the ledger
    // line. NOT an absolute page Y: a consumer resolves the system-top Y-up
    // (pageHeight - system.Y) and subtracts this. Independent of where paging
    // places the system, so the Stage-4 W2 stacking-origin flip won't touch it.
    double Y);

/// <summary>
/// Detects runs of adjacent noteheads sharing the same ledger position so a
/// renderer can draw a single continuous ledger line instead of per-note
/// short stubs.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ledger-line-spanner.cc Ledger_line_spanner::set_spacing_rods
/// Two noteheads are considered "adjacent" when their right and left edges are
/// within <see cref="MergeThreshold"/> of one another (in staff spaces).
/// Per-note (unmerged) ledger lines remain a valid, simpler rendering — this
/// engraver's output is purely additive metadata; renderers may opt in.
/// </remarks>
internal static class LedgerLineSpannerEngraver
{
    /// <summary>
    /// Distance (in staff spaces) below which two same-position notehead ledger
    /// lines are merged into a single span.
    /// </summary>
    public const double MergeThreshold = 1.5;

    /// <summary>
    /// Calculates ledger-line spans for a single-staff score.
    /// </summary>
    public static ImmutableArray<LedgerLineSpan> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        double staffHeight,
        int staffIndex = -1)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<LedgerLineSpan>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var builder = ImmutableArray.CreateBuilder<LedgerLineSpan>();
        var voice = score.Voice;

        // Per-system, per-staff-position list of (left, right, sortKey) ledger entries.
        // Indexed by (systemIndex, staffPosition) → list.
        var perSystem = new Dictionary<(int sys, int pos), List<(double Left, double Right)>>();

        for (int mi = 0; mi < voice.Measures.Length; mi++)
        {
            if (!measureMap.TryGetValue(mi, out var info))
                continue;
            var (system, measureLayout) = info;
            var measure = voice.Measures[mi];
            for (int ii = 0; ii < measure.Items.Length; ii++)
            {
                var item = measure.Items[ii];
                if (item is not NoteItem note || !note.NeedsLedgerLines)
                    continue;
                if (ii >= measureLayout.Items.Length)
                    continue;
                // Resolve via the shared column grid (the X the notehead is drawn at), not
                // the raw item slot — a bar opening with a mid-piece time/clef grob skews the
                // slot X off the grid, which drifted the ledger lines off their noteheads.
                double centerX = measureLayout.X
                    + LayoutUtilities.GetItemXOffset(voice.Measures, mi, ii, measureLayout);
                double headWidth = EngravingDefaults.NoteheadBlackWidth;
                // LP widens each side by length_fraction * head width (NOT an absolute
                // 0.25 ss). This matches the renderer / skyline, which already use
                // LedgerLengthFraction * headWidth.
                // LILYPOND-REF: lily/ledger-line-spanner.cc:228-230 ledger_extent.widen.
                double ext = EngravingDefaults.LedgerLengthFraction * headWidth;
                double left = centerX - ext;
                double right = centerX + headWidth + ext;

                // For each ledger position the note crosses, accumulate the entry.
                // Above staff (positions >= 6 even values).
                if (note.StaffPosition >= 6)
                {
                    for (int pos = 6; pos <= note.StaffPosition; pos += 2)
                        AddEntry(perSystem, system.SystemIndex, pos, left, right);
                }
                else if (note.StaffPosition <= -6)
                {
                    for (int pos = -6; pos >= note.StaffPosition; pos -= 2)
                        AddEntry(perSystem, system.SystemIndex, pos, left, right);
                }
            }
        }

        // Merge adjacent entries within each (system, staffPosition).
        foreach (var ((sysIdx, staffPos), entries) in perSystem)
        {
            entries.Sort((a, b) => a.Left.CompareTo(b.Left));
            double mergedLeft = entries[0].Left;
            double mergedRight = entries[0].Right;
            for (int i = 1; i < entries.Count; i++)
            {
                var (l, r) = entries[i];
                if (l - mergedRight <= MergeThreshold)
                {
                    if (r > mergedRight) mergedRight = r;
                }
                else
                {
                    EmitSpan(builder, systems, sysIdx, staffPos, mergedLeft, mergedRight, staffHeight, staffIndex);
                    mergedLeft = l;
                    mergedRight = r;
                }
            }
            EmitSpan(builder, systems, sysIdx, staffPos, mergedLeft, mergedRight, staffHeight, staffIndex);
        }

        return builder.ToImmutable();
    }

    private static void AddEntry(
        Dictionary<(int, int), List<(double, double)>> map,
        int sysIdx, int staffPos, double left, double right)
    {
        if (!map.TryGetValue((sysIdx, staffPos), out var list))
        {
            list = new List<(double, double)>();
            map[(sysIdx, staffPos)] = list;
        }
        list.Add((left, right));
    }

    private static void EmitSpan(
        ImmutableArray<LedgerLineSpan>.Builder builder,
        ImmutableArray<SystemLayout> systems,
        int sysIdx, int staffPos, double left, double right,
        double staffHeight, int staffIndex)
    {
        if (sysIdx >= systems.Length)
            return;
        var system = systems[sysIdx];
        // Within-system Y offset (device, down from the system top) of the staff
        // middle, NOT an absolute page Y — so it is independent of where paging
        // places the system. This decouples the span from SystemLayout.Y for the
        // Stage-4 W2 stacking-origin flip, mirroring the MultiMeasureRest change.
        // (LedgerLineSpans are additive metadata that no renderer draws — the
        // notehead path draws the actual ledger lines independently — so this is
        // byte-invariant.)
        double staffMiddleOffset = LayoutUtilities.StaffOffsetInSystemDown(system, staffIndex)
            + staffHeight / 2.0;
        double y = staffMiddleOffset - staffPos / 2.0;
        builder.Add(new LedgerLineSpan(
            SystemIndex: sysIdx,
            StaffPosition: staffPos,
            LeftX: left,
            RightX: right,
            Y: y));
    }
}
