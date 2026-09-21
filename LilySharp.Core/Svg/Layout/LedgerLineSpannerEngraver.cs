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
    /// <param name="prebuiltMeasureMap">The caller's measure → (system, layout) map, when it
    /// has one — see <see cref="TieVariantEngraver.Calculate"/>'s parameter for why the
    /// annotation pass's tail shares one.</param>
    public static ImmutableArray<LedgerLineSpan> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        double staffHeight,
        int staffIndex = -1,
        IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)>? prebuiltMeasureMap = null)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<LedgerLineSpan>.Empty;

        var measureMap = prebuiltMeasureMap ?? LayoutUtilities.BuildMeasureMap(systems);
        var builder = RentSpanBuilder();
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

        // ToImmutable COPIES — it never hands out the builder's own array — so the builder is
        // finished with here and not at the caller's line. See the drawer's remark for the
        // measurement that says so.
        var spans = builder.ToImmutable();
        GiveSpanBuilder(builder);
        return spans;
    }

    /// <summary>
    /// The builder <see cref="Calculate"/> gathers a score's ledger spans into, lent from one
    /// builder the thread keeps between calculations.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, 231 books × eight forward
    /// keystrokes): 2.17 calculations a keystroke at 40.3 spans each (max 444), and all 4,010
    /// builders built were unreachable by the time the render that built them returned. The
    /// builders and their growth ladders were 7,822 B a keystroke, 0.21% of it — the largest
    /// single container left in the census after session 458.
    /// <para>
    /// WHY IT IS SAFE TO PARK, and this is the question the whole family turned on:
    /// <c>ImmutableArray&lt;T&gt;.Builder.ToImmutable</c> COPIES, so the array handed to the
    /// caller is never the one the drawer keeps. MEASURED rather than read off the
    /// documentation (session 459, .NET 10.0.12, reference identity through reflection on
    /// <c>Builder._elements</c> and <c>ImmutableArray.array</c>): not aliased at
    /// <c>Count == Capacity</c>, below capacity, at <c>Count == 0</c>, or after growing from
    /// capacity 0 — while the same probe DID see <c>MoveToImmutable</c> and
    /// <c>DrainToImmutable</c> hand their array over, which is what calibrates it. ⚠️ AND
    /// THOSE TWO DETACH IT (they leave the builder at capacity 0), so no exit can leave a
    /// parked builder owning a caller's array; what a Move/Drain site loses instead is the
    /// POINT of parking, since the drawer would start from empty every time. That is why the
    /// sibling at :219-style exits — <c>BarNumberEngraver</c>'s number list,
    /// <c>LayoutEngine.Prelim</c>'s carried moves, <c>MeasureLayouter</c>'s item layouts — are
    /// not parked: they are already exact-sized and already hand their array over.
    /// </para>
    /// <para>
    /// RENTING TAKES THE BUILDER OUT OF THE DRAWER (session 421's idiom): a re-entrant
    /// calculation would gather into its own builder, a second thread has its own drawer, and a
    /// calculation that threw would lose the builder instead of mixing it. THE CLEARING IS ON
    /// GIVE, not on rent (session 456), so a builder parked dirty is observable — the next
    /// score's spans would open with this score's.
    /// </para>
    /// <para>
    /// WHAT IT RETAINS is one builder a thread at that thread's widest score — 444 spans,
    /// emptied. <c>Clear</c> nulls the slots it drops (measured with the same probe), and a
    /// <see cref="LedgerLineSpan"/> holds no references anyway, so the drawer pins nothing but
    /// its own capacity.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static ImmutableArray<LedgerLineSpan>.Builder? t_spanBuilder;

    /// <summary>Takes the thread's span builder, or makes the thread's first.</summary>
    private static ImmutableArray<LedgerLineSpan>.Builder RentSpanBuilder()
    {
        var builder = t_spanBuilder;
        if (builder is null)
            return ImmutableArray.CreateBuilder<LedgerLineSpan>();
        t_spanBuilder = null;
        return builder;
    }

    /// <summary>Puts a finished calculation's builder back, emptied, with its capacity.</summary>
    private static void GiveSpanBuilder(ImmutableArray<LedgerLineSpan>.Builder builder)
    {
        builder.Clear();
        t_spanBuilder = builder;
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
