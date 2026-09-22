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
using System.Runtime.InteropServices;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A keystroke-crossing memo of ONE below-staff stacking pass's per-system results
/// (<see cref="OutsideStaffStacker.StackBelowStaff"/>) — the below-side mirror of
/// <see cref="AboveStackMemo"/> (2026-08-26 review, finding 4-3: the above pass replayed
/// unchanged systems while the below pass — trills at 50, the fermata family at 75,
/// DynamicLineSpanner at 250 — ran every system live on every keystroke, twice per
/// keystroke counting both annotation passes). One instance per PASS, exactly as for
/// the above memo and for the same overwrite reason (<see cref="SystemLayoutCache"/>).
/// </summary>
/// <remarks>
/// SOUNDNESS — the key is the RAW INPUTS, <see cref="AboveStackMemo"/>'s shape. The
/// pass touches no cross-system state (every placement and seed goes through a
/// per-(system, staff) tracker), so one system's outputs are a pure function of:
/// <list type="bullet">
/// <item>its grobs' layout records (dynamics, hairpins, articulations, trills — all
/// pure-value record structs), in array order, partitioned by the same measure→system
/// map the pass itself builds;</item>
/// <item>the LINE-GROUP STRUCTURE over this system's members
/// (<c>DynamicAlignEngraver.AlignedLineGroup</c>), folded as per-system ordinals so an
/// unrelated edit that shifts global indices cannot move the fold. A group whose
/// members map to more than one system (or to none while others map) is never
/// memoized — the memoized front forces every touched system live instead of trusting
/// the one-piece-one-system construction;</item>
/// <item>the system geometry the pass reads: every staff's (StaffIndex, Y,
/// RefpointBelowTop) — the read set of the tracker seeds (staffYBySystem and
/// RefpointBelowTop) — plus the applyStaffOffsets flag;</item>
/// <item>the per-staff inside-staff profile of every staff the system PLACES on,
/// compared BY REFERENCE against the stored instances, with the above memo's
/// conservatism: a (system, staff) whose identity is unavailable is never memoized.</item>
/// </list>
/// Font metrics and the pass's declared paddings are process constants. Retention across
/// ineligible edits is sound because a stale entry can only match value-identical inputs.
/// <para>
/// ★ TWO ROOMS PER SYSTEM INDEX since session 417, the older evicted on a miss — the shape
/// <see cref="AboveStackMemo"/> took in session 415 and
/// <see cref="SystemLayoutCache.GetOrComputePagingAugment"/> in session 413, for the same
/// reason and against the same signature. ⚠️ THE ABOVE MEMO'S SUMMARY NAMED THE DISEASE
/// FOR ITS OWN STORE AND NOBODY ASKED ITS MIRROR: the preliminary pass runs twice on a
/// keystroke whose page score picks another line count (<c>LayoutEngine.Layout</c>'s
/// <c>ChooseSystemCount</c> leg calls <c>PlaceSystems</c> again), those two placements break
/// the score differently, and with one room they took each other's slot.
/// MEASURED (session 417, the owner's 231 books × 8 forward keystrokes, Release, allocation
/// bytes): of this store's 1,932 preliminary misses, <b>866</b> landed on a slot the SECOND
/// placement wrote and <b>866</b> on a slot the FIRST wrote — ★ the two counts EQUAL, each
/// slot one placement takes costing the other exactly one miss — against a floor of 199 on a
/// slot the same placement wrote. Hit rate by placement 85.6% / 55.5% before, 97.0% / 97.2%
/// after.
/// ⚠️ TWO IS THE COUNT LOOP'S OWN BOUND, not a tuning knob — <c>Layout</c> calls
/// <c>PlaceSystems</c> at most twice — so the store stays bounded by twice the widest system
/// count the session ever saw and needs no generation eviction. A lookup served from the
/// older room PROMOTES it, so the two placements settle one per room and keep hitting; a
/// miss evicts the older room, the one the current placement is not using. Eviction is sound
/// for the reason above: a dropped entry costs a restack, never a wrong reuse.
/// </para>
/// </remarks>
internal sealed class BelowStackMemo
{
    /// <summary>One system's program (every input the below pass reads for it) and its
    /// outputs, positionally parallel to the input lists.</summary>
    internal sealed class SystemEntry
    {
        // --- program ---
        public bool ApplyStaffOffsets;
        public (int StaffIndex, double Y, double? RefpointBelowTop)[] Staves
            = Array.Empty<(int, double, double?)>();
        public object[] ProfileUps = Array.Empty<object>();
        public object[] ProfileDowns = Array.Empty<object>();
        public DynamicLayout[] Dynamics = Array.Empty<DynamicLayout>();
        public HairpinLayout[] Hairpins = Array.Empty<HairpinLayout>();
        public ArticulationLayout[] Articulations = Array.Empty<ArticulationLayout>();
        public TrillSpannerLayout[] Trills = Array.Empty<TrillSpannerLayout>();
        // Per group anchored in this system: its members as ordinals into the
        // per-system Dynamics/Hairpins lists above, in group order.
        public int[][] GroupDynamics = Array.Empty<int[]>();
        public int[][] GroupHairpins = Array.Empty<int[]>();

        // --- value: the pass's outputs for this system's grobs ---
        public DynamicLayout[] OutDynamics = Array.Empty<DynamicLayout>();
        public HairpinLayout[] OutHairpins = Array.Empty<HairpinLayout>();
        public ArticulationLayout[] OutArticulations = Array.Empty<ArticulationLayout>();
        public TrillSpannerLayout[] OutTrills = Array.Empty<TrillSpannerLayout>();
    }

    private readonly Dictionary<int, Slot> _bySystem = new();

    /// <summary>Cumulative hit/miss counters (diagnostics / the liveness half of the
    /// nets — a net that asserts byte equality but never hits proves nothing).</summary>
    public int Hits { get; private set; }

    /// <inheritdoc cref="Hits"/>
    public int Misses { get; private set; }

    /// <summary>Whether either room stored for <paramref name="systemIndex"/> matches
    /// <paramref name="probe"/>'s program exactly. A room served from the older slot is
    /// PROMOTED, so <see cref="Get"/> always reads the room that matched. Counts the
    /// hit/miss.</summary>
    public bool TryMatch(int systemIndex, Probe probe)
    {
        _bySystem.TryGetValue(systemIndex, out var slot);
        if (slot.Recent is { } recent && Matches(recent, probe))
        {
            Hits++;
            return true;
        }
        if (slot.Older is { } older && Matches(older, probe))
        {
            _bySystem[systemIndex] = new Slot(older, slot.Recent);
            Hits++;
            return true;
        }
        Misses++;
        return false;
    }

    /// <summary>The room that last matched or was stored for this system — only ever read
    /// for a system <see cref="TryMatch"/> just answered true for, and the promotion above
    /// is what makes that the matching one.</summary>
    public SystemEntry? Get(int systemIndex)
        => _bySystem.TryGetValue(systemIndex, out var s) ? s.Recent : null;

    /// <summary>Files this system's entry in the recent room, demoting what was there.</summary>
    public void Store(int systemIndex, SystemEntry entry)
    {
        _bySystem.TryGetValue(systemIndex, out var slot);
        _bySystem[systemIndex] = new Slot(entry, slot.Recent);
    }

    /// <summary>One system index's two rooms, the most recently served one first.</summary>
    private readonly record struct Slot(SystemEntry? Recent, SystemEntry? Older);

    /// <summary>
    /// One system's program as it is being gathered, in lists the thread lends — the
    /// below-side twin of <see cref="AboveStackMemo.Probe"/>, for the same measured reason
    /// (8.65 programs a keystroke built to hit and be dropped, 6,534 B, session 512). The
    /// line groups' ordinals are held flat, each group's run length beside them.
    /// </summary>
    internal sealed class Probe
    {
        public bool ApplyStaffOffsets;
        public readonly List<(int StaffIndex, double Y, double? RefpointBelowTop)> Staves = new();
        public readonly List<object> ProfileUps = new();
        public readonly List<object> ProfileDowns = new();
        public readonly List<DynamicLayout> Dynamics = new();
        public readonly List<HairpinLayout> Hairpins = new();
        public readonly List<ArticulationLayout> Articulations = new();
        public readonly List<TrillSpannerLayout> Trills = new();
        public readonly List<int> GroupDynamics = new(), GroupDynamicLengths = new();
        public readonly List<int> GroupHairpins = new(), GroupHairpinLengths = new();

        /// <summary>Scratch for the builder: the staves the system places on, before they
        /// are sorted and made distinct. Not part of the program.</summary>
        public readonly List<int> Used = new();

        /// <summary>Forgets the last system's program and keeps every list's array.</summary>
        public void Clear()
        {
            ApplyStaffOffsets = false;
            Staves.Clear();
            ProfileUps.Clear();
            ProfileDowns.Clear();
            Dynamics.Clear();
            Hairpins.Clear();
            Articulations.Clear();
            Trills.Clear();
            GroupDynamics.Clear();
            GroupDynamicLengths.Clear();
            GroupHairpins.Clear();
            GroupHairpinLengths.Clear();
            Used.Clear();
        }

        /// <summary>The program as an entry of its own arrays, to be stored.</summary>
        public SystemEntry ToEntry() => new()
        {
            ApplyStaffOffsets = ApplyStaffOffsets,
            Staves = Staves.ToArray(),
            ProfileUps = ProfileUps.ToArray(),
            ProfileDowns = ProfileDowns.ToArray(),
            Dynamics = Dynamics.ToArray(),
            Hairpins = Hairpins.ToArray(),
            Articulations = Articulations.ToArray(),
            Trills = Trills.ToArray(),
            GroupDynamics = Unflatten(GroupDynamics, GroupDynamicLengths),
            GroupHairpins = Unflatten(GroupHairpins, GroupHairpinLengths),
        };

        private static int[][] Unflatten(List<int> flat, List<int> lengths)
        {
            var result = new int[lengths.Count][];
            int at = 0;
            for (int k = 0; k < lengths.Count; k++)
            {
                result[k] = flat.GetRange(at, lengths[k]).ToArray();
                at += lengths[k];
            }
            return result;
        }
    }

    private static bool Matches(SystemEntry a, Probe b)
        => a.ApplyStaffOffsets == b.ApplyStaffOffsets
            && a.Staves.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Staves))
            && RefSequenceEqual(a.ProfileUps, CollectionsMarshal.AsSpan(b.ProfileUps))
            && RefSequenceEqual(a.ProfileDowns, CollectionsMarshal.AsSpan(b.ProfileDowns))
            && a.Dynamics.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Dynamics))
            && a.Hairpins.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Hairpins))
            && a.Articulations.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Articulations))
            && a.Trills.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Trills))
            && JaggedEqual(a.GroupDynamics, b.GroupDynamics, b.GroupDynamicLengths)
            && JaggedEqual(a.GroupHairpins, b.GroupHairpins, b.GroupHairpinLengths);

    private static bool RefSequenceEqual(object[] a, ReadOnlySpan<object> b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i]))
                return false;
        return true;
    }

    /// <summary>A stored group list against the probe's flat one: the same number of
    /// groups, each the same run of ordinals.</summary>
    private static bool JaggedEqual(int[][] a, List<int> flat, List<int> lengths)
    {
        if (a.Length != lengths.Count)
            return false;
        var all = CollectionsMarshal.AsSpan(flat);
        int at = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].AsSpan().SequenceEqual(all.Slice(at, lengths[i])))
                return false;
            at += lengths[i];
        }
        return true;
    }
}
