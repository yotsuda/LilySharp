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
/// Font metrics and the pass's declared paddings are process constants. Entries are
/// stored one per system index and overwritten on miss, so the store is bounded by
/// the widest system count of the session (the paging-augment memo's policy). A
/// stale entry can only ever MATCH inputs that are value-identical to the ones its
/// outputs were computed from, so retention across ineligible edits is sound.
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

    private readonly Dictionary<int, SystemEntry> _bySystem = new();

    /// <summary>Cumulative hit/miss counters (diagnostics / the liveness half of the
    /// nets — a net that asserts byte equality but never hits proves nothing).</summary>
    public int Hits { get; private set; }

    /// <inheritdoc cref="Hits"/>
    public int Misses { get; private set; }

    /// <summary>Whether the stored entry for <paramref name="systemIndex"/> matches
    /// <paramref name="probe"/>'s program exactly. Counts the hit/miss.</summary>
    public bool TryMatch(int systemIndex, SystemEntry probe)
    {
        if (_bySystem.TryGetValue(systemIndex, out var stored) && Matches(stored, probe))
        {
            Hits++;
            return true;
        }
        Misses++;
        return false;
    }

    public SystemEntry? Get(int systemIndex)
        => _bySystem.TryGetValue(systemIndex, out var e) ? e : null;

    public void Store(int systemIndex, SystemEntry entry) => _bySystem[systemIndex] = entry;

    private static bool Matches(SystemEntry a, SystemEntry b)
        => a.Indent == b.Indent
            && a.TopStaff == b.TopStaff
            && a.Staves.AsSpan().SequenceEqual(b.Staves)
            && RefSequenceEqual(a.ProfileUps, b.ProfileUps)
            && RefSequenceEqual(a.ProfileDowns, b.ProfileDowns)
            && ReferenceEquals(a.SilhouetteUp, b.SilhouetteUp)
            && ReferenceEquals(a.SilhouetteDown, b.SilhouetteDown)
            && a.Trills.AsSpan().SequenceEqual(b.Trills)
            && a.BarNumbers.AsSpan().SequenceEqual(b.BarNumbers)
            && a.Ottavas.AsSpan().SequenceEqual(b.Ottavas)
            && a.CustomTexts.AsSpan().SequenceEqual(b.CustomTexts)
            && a.Voltas.AsSpan().SequenceEqual(b.Voltas)
            && a.MusicMarks.AsSpan().SequenceEqual(b.MusicMarks)
            && a.Articulations.AsSpan().SequenceEqual(b.Articulations)
            && a.Dynamics.AsSpan().SequenceEqual(b.Dynamics)
            && a.TextSpanners.AsSpan().SequenceEqual(b.TextSpanners)
            && a.TupletBrackets.AsSpan().SequenceEqual(b.TupletBrackets);

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
