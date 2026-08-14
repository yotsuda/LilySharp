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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A keystroke-crossing memo of ONE annotation pass's FINGERINGS — the island answer
/// (<c>FingeringEngraver</c>) and the column answer the script-column walk gives it
/// (<see cref="ArticulationEngraver.CalculateWithFingerings"/>), which session 163
/// measured as the pass's remaining term on a fingered book: islands 28.1 + walk 39.1 ms
/// per keystroke on perf-fingbeam1k (Release floor, prelim + final), against 3.4 + 0.0
/// on perf-plain1k, which carries no digits at all.
/// <para>
/// The unit is a (staff, system): every digit's answer is a function of its OWN note, so
/// filtering whole units out of the walk's <c>fingerings</c> argument leaves the rest of
/// that call byte-identical. One instance per PASS — the annotation pass runs twice per
/// keystroke over different systems, so a shared store would overwrite itself twice per
/// keystroke and never hit (<see cref="AboveStackMemo"/>'s finding).
/// </para>
/// </summary>
/// <remarks>
/// SOUNDNESS — the key is the RAW INPUTS, folded cheaper than the walk it saves (session
/// 160's overlay lesson, session 154's "the key must cost less than the walk"): per unit
/// it is O(the unit's measures), never O(its digits).
/// <list type="bullet">
/// <item>the unit's measure indices, and for each its <c>MeasureLayout</c> BY REFERENCE.
/// ⚠️ THE MODEL IS NOT IN THE KEY BY REFERENCE, and it cannot be: the annotation pass's
/// <c>Measure</c> (and its <c>Items</c> array) is rebuilt on EVERY keystroke — MEASURED,
/// session 163, all six units of a fingered book declined on that clause alone while the
/// layouts and beams matched — which is the same wall HANDOFF ▶ ⒫ ⑵⑶ already names for
/// the beam-detection memo ("the model remakes the Staff every keystroke"). What stands
/// in for it is the layout instance itself: <see cref="SystemLayoutCache"/> hands back the
/// SAME <c>MeasureLayout</c> array only on a hit, and its hit test compares that system's
/// slice of the per-measure <see cref="MeasureContentKey"/> vector element-wise. That
/// vector is an over-sensitive REFLECTION fold of every staff's items at the measure
/// (<c>MeasureContentKey.Compute(MultiStaffScore)</c> — "a new content field cannot
/// silently drift out of the key"), so a changed digit, head, or duration changes the key,
/// changes the entry, and changes the instance. This is not a new coverage claim: it is the
/// one the per-system measure cache and whole-layout reuse already stand on, and its
/// residual is that fold's own ~2⁻⁶⁴ collision bound, which the engine relies on
/// everywhere. The staleness net proves the direction that matters — an edited DIGIT (not
/// a pitch, so nothing else about the measure moves) must decline its unit.</item>
/// <item>the <see cref="BeamLayout"/>s touching the unit, BY REFERENCE — the beamed
/// stem is a fingering's support and its reach is read off the quanted beam
/// (<c>add-stem-support</c>), and a system's beams come back from
/// <c>SystemLayoutCache.GetOrComputeStaffSystemBeams</c> as the same instances.</item>
/// <item>the <see cref="SlurLayout"/>s covering the unit's measures in voice 0, BY
/// REFERENCE (a bow lifts the digit off it — <c>avoid-slur #'around</c>), and the staff
/// offset <c>staffYAt</c> answers for each of the unit's measures, BY VALUE (the slur
/// shift is computed in the staff's placed frame).</item>
/// </list>
/// ⚠️ A UNIT THAT CARRIES ANY SCRIPT IS NEVER MEMOIZED. A script and a digit on the same
/// note stack in ONE column — the digit becomes a side-support of the script above it —
/// so a memoized digit would have to be replayed WITH the script that reads it, which
/// means reassembling the articulation output and remapping every
/// <c>ArticulationLayout.SourceIndex</c>. That is a bigger claim than any measured book
/// needs today: the fingered book the keystroke bench prices carries no scripts at all,
/// and the walk on the two script-free books costs 0.0 ms. Declining the whole unit on
/// the presence of a script — not just on a shared note — keeps the articulation half of
/// the call literally untouched (the full array goes in, the full array comes out, in the
/// same order, with the same source indices). Stated rather than silently approximated:
/// a book with digits AND scripts in one system pays the old price.
/// <para>
/// Entries are stored one per (staff, system) and overwritten on miss, so the store is
/// bounded by the session's widest system count. A stale entry can only ever MATCH inputs
/// that are reference/value-identical to the ones its outputs were computed from.
/// </para>
/// </remarks>
internal sealed class FingScriptMemo
{
    /// <summary>One (staff, system)'s program and the digits it produced.</summary>
    internal sealed class UnitEntry
    {
        // --- program ---
        public int[] MeasureIndices = Array.Empty<int>();
        public object[] Layouts = Array.Empty<object>();
        public object[] Beams = Array.Empty<object>();
        public SlurLayout[] Slurs = Array.Empty<SlurLayout>();
        public double[] StaffOffsets = Array.Empty<double>();

        // --- value: the digits at their COLUMN answer (what the pass hands on) ---
        public FingeringLayout[] Adjusted = Array.Empty<FingeringLayout>();
    }

    private readonly Dictionary<(int Staff, int System), UnitEntry> _byUnit = new();

    /// <summary>Cumulative hit/miss counters (diagnostics, and the liveness half of the
    /// nets — a net that asserts byte equality but never hits proves nothing).</summary>
    public int Hits { get; private set; }

    /// <inheritdoc cref="Hits"/>
    public int Misses { get; private set; }

    /// <summary>The stored entry for this unit when its program matches
    /// <paramref name="probe"/> exactly, else null. Counts the hit/miss.</summary>
    public UnitEntry? TryMatch(int staff, int system, UnitEntry probe)
    {
        if (_byUnit.TryGetValue((staff, system), out var stored) && Matches(stored, probe))
        {
            Hits++;
            return stored;
        }
        Misses++;
        return null;
    }

    public void Store(int staff, int system, UnitEntry entry) => _byUnit[(staff, system)] = entry;

    private static bool Matches(UnitEntry a, UnitEntry b)
        => a.MeasureIndices.AsSpan().SequenceEqual(b.MeasureIndices)
            && a.StaffOffsets.AsSpan().SequenceEqual(b.StaffOffsets)
            && RefSequenceEqual(a.Layouts, b.Layouts)
            && RefSequenceEqual(a.Beams, b.Beams)
            // Slurs by VALUE (a record class's structural equality): unlike the beams
            // there is no per-system slur cache to hand identity back, and a book's slur
            // list is short next to its digits.
            && SlurSequenceEqual(a.Slurs, b.Slurs);

    private static bool SlurSequenceEqual(SlurLayout[] a, SlurLayout[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (!Equals(a[i], b[i]))
                return false;
        return true;
    }

    private static bool RefSequenceEqual(object[] a, object[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i]))
                return false;
        return true;
    }
}
