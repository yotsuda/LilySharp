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
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One item's note-collision answer — the X shift its column pushed it by, whether a merge
/// wiped its head, and the dot-column adjustment — stamped with the measure it stands in.
/// The unit every collision reader consumes (the renderer's <see cref="ScoreLayout"/>
/// tables, the spacing floor, the skyline seed, the beam frame, the ledger rods) and the
/// unit the per-system memo stores. An item whose column moved nothing has no entry.
/// </summary>
internal readonly record struct VoiceCollisionEntry(
    int MeasureIndex, int VoiceId, int ItemIndex,
    double XOffset, bool HeadTransparent, DotAdjustment Dot);

/// <summary>
/// One staff's note-collision answer, a measure at a time: a measure is solved on the first
/// ask for it and kept, so a reader that wants three bars pays for three bars.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/note-collision.cc:403-472 calc_positioning_done — one NoteCollision
/// grob per moment, positioned from the note columns at that moment alone. A measure's
/// moments are its own, so measure i's answer reads measure i of every voice and nothing
/// else (<see cref="Collector.VoiceCollector.CollectMeasure"/>; the forced direction of a
/// <c>voice { }</c> span is read off measure i too, <see cref="VoiceDefaults.GetDefaultStemUpAt"/>).
/// That locality is what makes a per-measure fill, and a per-system slice of it in
/// <see cref="SystemLayoutCache"/>, the same answer as the whole-staff walk — not an
/// approximation of it. <see cref="ElementCoordinator.ComputeVoiceOffsets"/> is that walk,
/// spelt as the union of these measures.
/// </para>
/// <para>
/// ⚠️ UNTIL SESSION 405 the answer was computed whole, twice per keystroke: the spacing
/// side's memo filled itself with one walk over every bar (keyed on a <c>Voice[]</c> that
/// every edit replaces), and the finishing pass walked every bar again with no memo at all.
/// MEASURED (perf-v2bow1k, Release, TieredCompilation=0, an edit at the last bar): two
/// calls, 6,000 columns, 6–10 ms and 15 MB per keystroke — on a book with no collision in
/// it. Now the spacing side fills the bars it is asked about (the edited ones) and the
/// finishing pass reads the rest from the per-system memo.
/// </para>
/// <para>
/// A slot is an <see cref="ImmutableArray{T}"/> — one reference — so two threads filling
/// the same measure write the same value twice and never a torn one.
/// </para>
/// </remarks>
internal sealed class VoiceCollisionTable
{
    /// <summary>The answer of a staff that cannot collide (fewer than two voices).</summary>
    public static readonly VoiceCollisionTable Empty = new(ImmutableArray<Voice>.Empty);

    private readonly ImmutableArray<Voice> _voices;
    // default = not yet solved; Empty = solved, nothing collided.
    private readonly ImmutableArray<VoiceCollisionEntry>[] _measures;

    public VoiceCollisionTable(ImmutableArray<Voice> voices)
    {
        _voices = voices;
        int n = 0;
        foreach (var v in voices)
            n = Math.Max(n, v.Measures.Length);
        _measures = new ImmutableArray<VoiceCollisionEntry>[voices.Length < 2 ? 0 : n];
    }

    /// <summary>The measures this table answers for (the longest voice's count; 0 when the
    /// staff cannot collide).</summary>
    public int MeasureCount => _measures.Length;

    /// <summary>The entries of one measure — the items its columns moved, wiped or dotted;
    /// empty where nothing collided or the measure is outside the staff. Solved on first ask.</summary>
    public ImmutableArray<VoiceCollisionEntry> EntriesOf(int measureIndex)
    {
        if ((uint)measureIndex >= (uint)_measures.Length)
            return ImmutableArray<VoiceCollisionEntry>.Empty;
        var slot = _measures[measureIndex];
        if (slot.IsDefault)
            _measures[measureIndex] = slot =
                ElementCoordinator.ComputeVoiceCollisionsOfMeasure(_voices, measureIndex);
        return slot;
    }

    /// <summary>Whether any item of the measure carries a shift — the readers' early-out,
    /// so a bar that moved nothing costs no per-item lookup.</summary>
    public bool AnyShiftIn(int measureIndex)
    {
        foreach (var e in EntriesOf(measureIndex))
            if (e.XOffset != 0)
                return true;
        return false;
    }

    /// <summary>The X shift the collision pass gave this item, 0 when none. VoiceId is
    /// 1-based, as <see cref="Collector.VoiceCollector"/> stamps it and the renderer's
    /// <see cref="VoiceItemKey"/> reads it.</summary>
    public double ShiftOf(int measureIndex, int voiceId, int itemIndex)
    {
        foreach (var e in EntriesOf(measureIndex))
            if (e.VoiceId == voiceId && e.ItemIndex == itemIndex)
                return e.XOffset;
        return 0;
    }

    /// <summary>The entries of measures [first, first + count), in measure order — one
    /// system's slice, the unit the per-system memo stores.</summary>
    public ImmutableArray<VoiceCollisionEntry> SliceOf(int first, int count)
    {
        ImmutableArray<VoiceCollisionEntry>.Builder? b = null;
        for (int m = first; m < first + count; m++)
        {
            var entries = EntriesOf(m);
            if (entries.IsEmpty)
                continue;
            (b ??= ImmutableArray.CreateBuilder<VoiceCollisionEntry>()).AddRange(entries);
        }
        return b?.ToImmutable() ?? ImmutableArray<VoiceCollisionEntry>.Empty;
    }
}
