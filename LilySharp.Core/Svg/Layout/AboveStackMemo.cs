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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A keystroke-crossing memo of ONE above-staff stacking pass's per-system results
/// (<see cref="OutsideStaffStacker.StackAboveStaff"/>). On an edit, a system whose
/// stacking INPUTS are unchanged replays its previous outputs instead of rebuilding
/// its tracker (a copy of the whole inside-staff profile) and re-placing every mover;
/// only the edited systems stack live. One instance per PASS — the annotation pass
/// runs twice per keystroke (preliminary and final) with different systems, so the
/// two passes hold separate instances (<see cref="SystemLayoutCache"/>) or they would
/// overwrite each other every keystroke and never hit.
/// </summary>
/// <remarks>
/// SOUNDNESS — the key is the RAW INPUTS themselves, not a content-key coverage claim
/// (the paging-augment memo's shape, and session 160's overlay-fold lesson: fold what
/// the pass reads, and fold it cheaper than the walk it saves). The pass touches no
/// cross-system state (every placement goes through a per-(system, staff) tracker),
/// so one system's outputs are a pure function of:
/// <list type="bullet">
/// <item>its grobs' layout records (all ten families, VALUE equality — every record
/// is a pure-value record struct, verified field-by-field 2026-08-14), in array
/// order. The measure→system assignment is folded by the partition itself: a grob
/// that moves to another system changes both systems' record lists.</item>
/// <item>the system's geometry the pass reads: Indent, every staff's
/// (StaffIndex, Y, IsHidden, Clef) — the whole read set of TopStaffIndex,
/// StaffOffsetInSystemUp and SeedClefInk — plus the resolved top staff.</item>
/// <item>the per-staff inside-staff profile, compared BY REFERENCE against the
/// stored table instances (<c>AnnotationLayoutContext.StaffInside</c>): an unchanged
/// system's profile comes back from <see cref="SystemLayoutCache"/> as the same
/// instances; a rebuilt profile misses into a live stack — conservative, never
/// wrong. A (system, staff) whose identity is unavailable (the delegate answers
/// null) is never memoized at all.</item>
/// <item>the system-silhouette fallback pair, also by reference.</item>
/// </list>
/// Font metrics and the pass's declared paddings are process constants. A stale entry
/// can only ever MATCH inputs that are value-identical to the ones its outputs were
/// computed from, so retention across ineligible edits is sound.
/// <para>
/// ★ TWO ROOMS PER SYSTEM INDEX since session 415, the older evicted on a miss — the
/// shape <see cref="SystemLayoutCache.GetOrComputePagingAugment"/> took in session 413,
/// for the same reason and against the same signature. ⚠️ THE SUMMARY ABOVE NAMED THIS
/// DISEASE AND STOPPED ONE LEVEL SHORT: it gives the preliminary and the final pass
/// separate instances because one instance "would overwrite each other every keystroke
/// and never hit" — but the PRELIMINARY pass itself runs twice on a keystroke whose page
/// score picks another line count (<c>LayoutEngine.Layout</c>'s <c>ChooseSystemCount</c>
/// leg calls <c>PlaceSystems</c> again), and those two placements break the score
/// differently, so system s is a different run of measures in each. With one room they
/// took each other's slot, exactly as two passes would have.
/// MEASURED (session 415, the owner's 231 books × 8 forward keystrokes, Release,
/// allocation bytes), by which run consulted the store:
/// <list type="bullet">
/// <item>first placement 44,121 systems, 84.6% hit; SECOND placement 7,315 systems,
/// <b>28.6%</b> hit; the final pass — which has no second run to fight with — 95.2%.
/// The final pass's rate is what the preliminary one's ceiling looks like.</item>
/// <item>of the preliminary pass's 12,034 misses, only <b>1,009</b> landed on a slot the
/// same run had written (the edited system — the irreducible floor). 5,805 landed on a
/// slot the second placement wrote and 5,216 on a slot the first placement wrote: ★ the
/// two nearly equal counts are the signature, each slot one placement takes costing the
/// other exactly one miss. Worth 2.8% of a keystroke, and a miss restacks the system live
/// (43,636–46,074 B) where a hit replays.</item>
/// </list>
/// ⚠️ TWO IS THE COUNT LOOP'S OWN BOUND, not a tuning knob — <c>Layout</c> calls
/// <c>PlaceSystems</c> at most twice — so the store stays bounded by twice the widest
/// system count the session ever saw and needs no generation eviction. A lookup served
/// from the older room PROMOTES it, so the two placements settle one per room and keep
/// hitting; a miss evicts the older room, the one the current placement is not using.
/// Eviction is sound for the reason above: a dropped entry costs a restack, never a wrong
/// reuse. ★ The final pass's store has only one writer and so never fills its second room
/// from a rival — what lands there is the PREVIOUS keystroke's entry, which an undo can
/// then hit; that is a bonus, not the reason for the change.
/// </para>
/// </remarks>
internal sealed class AboveStackMemo
{
    /// <summary>One system's program (every input the pass reads for it) and its
    /// outputs, positionally parallel to the input lists.</summary>
    internal sealed class SystemEntry
    {
        // --- program ---
        public double Indent;
        public int TopStaff;
        public (int StaffIndex, double Y, bool IsHidden, ClefType Clef)[] Staves
            = Array.Empty<(int, double, bool, ClefType)>();
        public object[] ProfileUps = Array.Empty<object>();
        public object[] ProfileDowns = Array.Empty<object>();
        public object? SilhouetteUp, SilhouetteDown;
        public TrillSpannerLayout[] Trills = Array.Empty<TrillSpannerLayout>();
        public BarNumberLayout[] BarNumbers = Array.Empty<BarNumberLayout>();
        public OttavaBracketLayout[] Ottavas = Array.Empty<OttavaBracketLayout>();
        public CustomTextLayout[] CustomTexts = Array.Empty<CustomTextLayout>();
        public VoltaBracketLayout[] Voltas = Array.Empty<VoltaBracketLayout>();
        public MusicMarkLayout[] MusicMarks = Array.Empty<MusicMarkLayout>();
        public ArticulationLayout[] Articulations = Array.Empty<ArticulationLayout>();
        public DynamicLayout[] Dynamics = Array.Empty<DynamicLayout>();
        public TextSpannerLayout[] TextSpanners = Array.Empty<TextSpannerLayout>();
        // Seed-only family: read by the pass (occupancy), never moved, so no outputs.
        public TupletBracketLayout[] TupletBrackets = Array.Empty<TupletBracketLayout>();
        // The other seed-only family: the chord symbols already placed above the staff.
        // ⚠️ THE LAYOUTS ALONE ARE THE PROGRAM even though the seed also reads each item's
        // StaffIndex and IsChordRow: a symbol that changed staff or became a row changes the
        // staff offset baked into its YUp, so no such edit can leave a layout byte-identical.
        public ChordNameLayout[] ChordNames = Array.Empty<ChordNameLayout>();

        // --- value: the pass's outputs for this system's grobs ---
        public TrillSpannerLayout[] OutTrills = Array.Empty<TrillSpannerLayout>();
        public BarNumberLayout[] OutBarNumbers = Array.Empty<BarNumberLayout>();
        public OttavaBracketLayout[] OutOttavas = Array.Empty<OttavaBracketLayout>();
        public CustomTextLayout[] OutCustomTexts = Array.Empty<CustomTextLayout>();
        public VoltaBracketLayout[] OutVoltas = Array.Empty<VoltaBracketLayout>();
        public MusicMarkLayout[] OutMusicMarks = Array.Empty<MusicMarkLayout>();
        public ArticulationLayout[] OutArticulations = Array.Empty<ArticulationLayout>();
        public DynamicLayout[] OutDynamics = Array.Empty<DynamicLayout>();
        public TextSpannerLayout[] OutTextSpanners = Array.Empty<TextSpannerLayout>();
    }

    private readonly Dictionary<int, Slot> _bySystem = new();

    /// <summary>Cumulative hit/miss counters (diagnostics / the liveness half of the
    /// nets — a net that asserts byte equality but never hits proves nothing).</summary>
    public int Hits { get; private set; }

    /// <inheritdoc cref="Hits"/>
    public int Misses { get; private set; }

    /// <summary>
    /// One system's program as it is being gathered — the same inputs a
    /// <see cref="SystemEntry"/> keeps, in lists the thread lends — so that a HIT compares
    /// against the stored entry without building one. Only a miss copies it out
    /// (<see cref="ToEntry"/>).
    /// </summary>
    /// <remarks>
    /// ⚠️ THE PROBE IS BUILT ON EVERY SYSTEM OF EVERY CALL AND ALMOST ALWAYS DROPPED.
    /// MEASURED (2026-09-23, session 512, Release, the owner's corpus, 232 books × eight
    /// forward keystrokes, allocated bytes around each program build, by outcome): the above
    /// pass built 48.92 programs a keystroke that HIT and 2.29 that missed — 35,600 B a
    /// keystroke for entries compared once and dropped, 727 B each (fourteen arrays and a
    /// <see cref="SortedSet{T}"/>) — and the below pass 8.65 hits for 6,534 B. The shape is
    /// session 508's paging-augment memo (<c>PagingAugmentProgram.Builder.Matches</c>): the
    /// steps are compared, and the program is built from them only to be stored.
    /// </remarks>
    internal sealed class Probe
    {
        public double Indent;
        public int TopStaff;
        public readonly List<(int StaffIndex, double Y, bool IsHidden, ClefType Clef)> Staves = new();
        public readonly List<object> ProfileUps = new();
        public readonly List<object> ProfileDowns = new();
        public object? SilhouetteUp, SilhouetteDown;
        public readonly List<TrillSpannerLayout> Trills = new();
        public readonly List<BarNumberLayout> BarNumbers = new();
        public readonly List<OttavaBracketLayout> Ottavas = new();
        public readonly List<CustomTextLayout> CustomTexts = new();
        public readonly List<VoltaBracketLayout> Voltas = new();
        public readonly List<MusicMarkLayout> MusicMarks = new();
        public readonly List<ArticulationLayout> Articulations = new();
        public readonly List<DynamicLayout> Dynamics = new();
        public readonly List<TextSpannerLayout> TextSpanners = new();
        public readonly List<TupletBracketLayout> TupletBrackets = new();
        public readonly List<ChordNameLayout> ChordNames = new();

        /// <summary>Scratch for the builder: the staves the system consumes a profile for,
        /// before they are sorted and made distinct. Not part of the program.</summary>
        public readonly List<int> Used = new();

        /// <summary>Forgets the last system's program and keeps every list's array.</summary>
        public void Clear()
        {
            Indent = 0;
            TopStaff = 0;
            Staves.Clear();
            ProfileUps.Clear();
            ProfileDowns.Clear();
            SilhouetteUp = SilhouetteDown = null;
            Trills.Clear();
            BarNumbers.Clear();
            Ottavas.Clear();
            CustomTexts.Clear();
            Voltas.Clear();
            MusicMarks.Clear();
            Articulations.Clear();
            Dynamics.Clear();
            TextSpanners.Clear();
            TupletBrackets.Clear();
            ChordNames.Clear();
            Used.Clear();
        }

        /// <summary>The program as an entry of its own arrays, to be stored.</summary>
        public SystemEntry ToEntry() => new()
        {
            Indent = Indent,
            TopStaff = TopStaff,
            Staves = Staves.ToArray(),
            ProfileUps = ProfileUps.ToArray(),
            ProfileDowns = ProfileDowns.ToArray(),
            SilhouetteUp = SilhouetteUp,
            SilhouetteDown = SilhouetteDown,
            Trills = Trills.ToArray(),
            BarNumbers = BarNumbers.ToArray(),
            Ottavas = Ottavas.ToArray(),
            CustomTexts = CustomTexts.ToArray(),
            Voltas = Voltas.ToArray(),
            MusicMarks = MusicMarks.ToArray(),
            Articulations = Articulations.ToArray(),
            Dynamics = Dynamics.ToArray(),
            TextSpanners = TextSpanners.ToArray(),
            TupletBrackets = TupletBrackets.ToArray(),
            ChordNames = ChordNames.ToArray(),
        };
    }

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

    private static bool Matches(SystemEntry a, Probe b)
        => a.Indent == b.Indent
            && a.TopStaff == b.TopStaff
            && a.Staves.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Staves))
            && RefSequenceEqual(a.ProfileUps, CollectionsMarshal.AsSpan(b.ProfileUps))
            && RefSequenceEqual(a.ProfileDowns, CollectionsMarshal.AsSpan(b.ProfileDowns))
            && ReferenceEquals(a.SilhouetteUp, b.SilhouetteUp)
            && ReferenceEquals(a.SilhouetteDown, b.SilhouetteDown)
            && a.Trills.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Trills))
            && a.BarNumbers.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.BarNumbers))
            && a.Ottavas.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Ottavas))
            && a.CustomTexts.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.CustomTexts))
            && a.Voltas.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Voltas))
            && a.MusicMarks.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.MusicMarks))
            && a.Articulations.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Articulations))
            && a.Dynamics.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.Dynamics))
            && a.TextSpanners.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.TextSpanners))
            && a.TupletBrackets.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.TupletBrackets))
            && a.ChordNames.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(b.ChordNames));

    private static bool RefSequenceEqual(object[] a, ReadOnlySpan<object> b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (!ReferenceEquals(a[i], b[i]))
                return false;
        return true;
    }
}
