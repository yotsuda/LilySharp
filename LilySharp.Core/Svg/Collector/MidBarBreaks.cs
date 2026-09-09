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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// A <c>break</c> (or <c>pageBreak</c>) written INSIDE a bar with music on both sides of it,
/// as one voice's walk met it: the bar it stands in (counted in BARS, not in model measures
/// — see <see cref="MidBarBreakTable"/>), how far into that bar, and where it was written.
/// </summary>
internal readonly record struct MidBarBreakRequest(
    int LogicalMeasure, Fraction Offset, bool PageBreak, int SourcePosition);

/// <summary>One split the table applies: the offset into the bar, and the break's kind.</summary>
internal readonly record struct MidBarSplit(Fraction Offset, bool PageBreak, int SourcePosition);

/// <summary>
/// A mid-bar <c>break</c> the page could NOT split at, and why — read back by
/// <c>MidBarBreakValidator</c> (LYS1037) the way <see cref="UnpairedRepeatWarning"/> is by
/// <c>RepeatPairingValidator</c>. The break then falls to the next bar line, as every
/// mid-bar break did before session 356.
/// </summary>
public sealed record MidBarBreakConflict(int SourcePosition, string Reason);

/// <summary>
/// The bars a score's line breaks split, settled once for the whole score and handed to
/// EVERY measure builder, so that each voice, each row and each omitted-part harvest cuts
/// the same bars at the same moments and the measure indices stay aligned across the score.
/// </summary>
/// <remarks>
/// <para>
/// WHY A TABLE, AND WHY TWO PASSES. A measure is this engine's layout atom: every side table
/// (ties, slurs, marks, lyrics, chord symbols) addresses <c>(measure, item)</c>, and every
/// part is aligned to every other by measure INDEX (<c>MeasureCollector.SynchronizeBarlines</c>).
/// A bar broken across two systems must therefore become TWO model measures — a head that
/// ends in no bar line and a tail that opens with none (<see cref="Measure.BreaksMidBar"/> /
/// <see cref="Measure.ContinuesBar"/>) — and it must become two in EVERY voice, or the
/// indices after it drift apart by one. The break is written in ONE voice, so the first
/// collect can only discover where the splits are (each builder records a
/// <see cref="MidBarBreakRequest"/> as it meets a break with music after it in the same
/// bar); the collector then settles the table here and collects AGAIN with it, and in that
/// pass every builder splits when its clock reaches the offset. Books with no mid-bar break
/// (every book before session 356) pay one empty-list check and collect once.
/// </para>
/// <para>
/// LOGICAL versus PHYSICAL. A split makes the model one measure longer, so the table is
/// keyed by the BAR — the count of bars begun, which a split's second half does not
/// advance (<c>MeasureBuilder.LogicalMeasureIndex</c>; it is what the bar number counts too)
/// — and <see cref="ToPhysical"/> / <see cref="ToLogical"/> translate for the readers that
/// address model measures (the chord and lyric rows, whose grids are written in bars).
/// </para>
/// <para>
/// WHAT REFUSES A SPLIT (<see cref="Build"/>, each a <see cref="MidBarBreakConflict"/>):
/// a voice whose item sounds ACROSS the offset (LILYSHARP-OWN: LilyPond breaks under the
/// whole note — mb2.ly — and draws the rest of the bar empty on the next line, while Lily#
/// has no measure that holds an item longer than itself and invents none; the break falls
/// to the bar line and LYS1037 says so); a beam group, a tuplet or a percent
/// repeat running across it (LilyPond breaks a beam into two pieces, one per system —
/// mb3.ly's PROBEBEAM prints two — which the per-measure beamer here would have to
/// re-derive from a bar that starts mid-beat); an unmetered bar (<c>time none</c>, whose
/// clock stands still); a second break in the same bar. A SPACER rest across the offset is
/// cut in two (an empty <c>| |</c> bar in another part, a row's slot) — it draws nothing.
/// </para>
/// </remarks>
internal sealed class MidBarBreakTable
{
    /// <summary>The settled table with no splits — what a nested collect is seeded with
    /// while the outer one is still discovering, so it never discovers on its own.</summary>
    public static readonly MidBarBreakTable Empty = new(ImmutableSortedDictionary<int, MidBarSplit>.Empty);

    private readonly ImmutableSortedDictionary<int, MidBarSplit> _splits;
    private readonly int[] _keys;

    private MidBarBreakTable(ImmutableSortedDictionary<int, MidBarSplit> splits)
    {
        _splits = splits;
        _keys = splits.Keys.ToArray();
    }

    public bool IsEmpty => _splits.Count == 0;

    /// <summary>The split in bar <paramref name="logical"/>, or null.</summary>
    public MidBarSplit? At(int logical)
        => _splits.TryGetValue(logical, out var s) ? s : null;

    /// <summary>The model index of the bar's first (or only) measure.</summary>
    public int ToPhysical(int logical)
    {
        int before = 0;
        foreach (int k in _keys)
        {
            if (k >= logical) break;
            before++;
        }
        return logical + before;
    }

    /// <summary>The bar a model measure belongs to — the same bar for a split's two halves.
    /// One past the last measure maps to one past the last bar, so a COUNT translates too.</summary>
    public int ToLogical(int physical)
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            int head = _keys[i] + i;
            if (physical < head)
                return physical - i;
            if (physical == head || physical == head + 1)
                return _keys[i];
        }
        return physical - _keys.Length;
    }

    /// <summary>
    /// Settles the table from the requests one collect recorded, against the voices that
    /// collect produced (every voice the score synchronises, the omitted parts' included):
    /// each bar that every voice can be cut at its offset becomes a split; every other
    /// request becomes a <see cref="MidBarBreakConflict"/> in <paramref name="conflicts"/>.
    /// </summary>
    public static MidBarBreakTable Build(
        IReadOnlyList<MidBarBreakRequest> requests,
        IEnumerable<Voice> voices,
        TimeSignature timeSignature,
        IReadOnlyList<TupletBracketItem> tuplets,
        IReadOnlyList<PercentRepeatItem> percents,
        List<MidBarBreakConflict> conflicts)
    {
        if (requests.Count == 0)
            return Empty;

        // One split per bar: the first written break wins, a second is reported.
        var chosen = new SortedDictionary<int, MidBarBreakRequest>();
        foreach (var r in requests.OrderBy(r => r.SourcePosition))
        {
            if (chosen.TryGetValue(r.LogicalMeasure, out var first))
            {
                if (first.Offset != r.Offset)
                    conflicts.Add(new MidBarBreakConflict(r.SourcePosition,
                        "a bar can be broken across a line only once, and this bar already breaks earlier"));
                continue;
            }
            chosen[r.LogicalMeasure] = r;
        }

        var voiceList = voices.ToList();
        var beamsByVoice = new Dictionary<Voice, ImmutableArray<BeamGroup>>(ReferenceEqualityComparer.Instance);
        var builder = ImmutableSortedDictionary.CreateBuilder<int, MidBarSplit>();
        foreach (var (bar, request) in chosen)
        {
            string? reason = null;
            foreach (var voice in voiceList)
            {
                if (bar >= voice.Measures.Length)
                    continue;
                reason = ReasonNotToSplit(voice, bar, request.Offset, timeSignature, tuplets, percents, beamsByVoice);
                if (reason != null)
                    break;
            }
            if (reason != null)
            {
                conflicts.Add(new MidBarBreakConflict(request.SourcePosition, reason));
                continue;
            }
            builder[bar] = new MidBarSplit(request.Offset, request.PageBreak, request.SourcePosition);
        }
        return builder.Count == 0 ? Empty : new MidBarBreakTable(builder.ToImmutable());
    }

    /// <summary>Why <paramref name="voice"/>'s bar cannot be cut at <paramref name="offset"/>,
    /// or null when it can. The voices are the FIRST collect's — unsplit, so bar == index.</summary>
    private static string? ReasonNotToSplit(
        Voice voice, int bar, Fraction offset, TimeSignature timeSignature,
        IReadOnlyList<TupletBracketItem> tuplets, IReadOnlyList<PercentRepeatItem> percents,
        Dictionary<Voice, ImmutableArray<BeamGroup>> beamsByVoice)
    {
        var measure = voice.Measures[bar];
        if (measure.Unmetered)
            return "the bar is unmetered (time none), so it has no position to break at";

        foreach (var p in percents)
            if (p.MeasureIndex == bar || (p.IsDouble && p.MeasureIndex - 1 == bar))
                return "a percent repeat covers the bar";

        // An item-less measure (a `voice { }` track's mirror of a bar the span does not
        // reach) is rebuilt from its primary stream, split and all: nothing to refuse.
        if (measure.Items.Length == 0)
            return null;

        // The item boundaries of this bar: an item that sounds across the offset refuses
        // the split unless it is a spacer, which draws nothing and is cut in two; a bar
        // that ends at or before the offset has no second half to open the next line with.
        var onsets = new Fraction[measure.Items.Length + 1];
        var onset = Fraction.Zero;
        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            onsets[i] = onset;
            var end = onset + item.Duration;
            if (onset < offset && end > offset && item is not RestItem { IsSpacer: true })
                return $"in '{voice.Name}' a {Kind(item)} sounds across it";
            onset = end;
        }
        onsets[measure.Items.Length] = onset;
        if (onset <= offset)
            return $"in '{voice.Name}' the bar ends at or before it";

        foreach (var t in tuplets)
        {
            if (t.MeasureIndex != bar || t.StartNoteIndex < 0 || t.EndNoteIndex >= measure.Items.Length)
                continue;
            var start = onsets[t.StartNoteIndex];
            var end = onsets[t.EndNoteIndex] + measure.Items[t.EndNoteIndex].Duration;
            if (start < offset && end > offset)
                return $"in '{voice.Name}' a tuplet runs across it";
        }

        if (!beamsByVoice.TryGetValue(voice, out var groups))
        {
            groups = new BeamDetector().DetectBeamGroups(voice, timeSignature);
            beamsByVoice[voice] = groups;
        }
        foreach (var g in groups)
        {
            bool before = false, after = false;
            foreach (var m in g.Members)
            {
                int mi = m.MeasureIndex < 0 ? g.MeasureIndex : m.MeasureIndex;
                if (mi < bar) before = true;
                else if (mi > bar) after = true;
                else if (m.ItemIndex >= 0 && m.ItemIndex < onsets.Length)
                {
                    if (onsets[m.ItemIndex] < offset) before = true;
                    else after = true;
                }
            }
            if (before && after)
                return $"in '{voice.Name}' a beam runs across it";
        }
        return null;
    }

    private static string Kind(MusicItem item) => item switch
    {
        NoteItem => "note",
        ChordItem => "chord",
        RestItem => "rest",
        _ => "item",
    };
}
