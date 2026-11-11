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

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The side of a staff each dynamic level and each hairpin stands on, by LilyPond's own rule:
/// the marks of one stretch ride ONE line, and the line has ONE direction. A mark that writes
/// a side (<c>.up</c> / <c>.down</c>) differing from its line's breaks off into a new line and
/// gives that line its direction; the marks after it that write none follow the line they
/// join — so the level ending a hairpin goes where a level written <c>.down</c> at its start
/// went. A line with no direction written takes the staff's own (below; above on a staff
/// with lyrics under it), and it lasts while a hairpin runs.
/// </summary>
/// <remarks>
/// Owner's decision 2026-11-11 (session 1225, "match LilyPond"), replacing the first reading
/// D3 of docs/dynamics-above-lyrics-design.md ("a written side moves that mark alone").
/// LILYPOND-REF: lily/dynamic-align-engraver.cc:119-160 Dynamic_align_engraver::acknowledge_dynamic —
///   an explicit direction differing from the line's ends the line (line_ = nullptr), the grob
///   joins the current or a new line (create_line_spanner), and its own direction, when it has
///   one, becomes the line's (set_grob_direction (line_, d)).
/// LILYPOND-REF: lily/dynamic-align-engraver.cc:194-235 Dynamic_align_engraver::stop_translation_timestep —
///   the line ends in a timestep where no dynamic spanner runs (running_.empty ()).
/// MEASURED (LilyPond 2.26.0, \dynamicUp, Lab sessions/p1225/dyn/lp/d3): <c>c''4_\ff\&gt; … f''\p</c>
///   draws the ff below, the hairpin ABOVE and the closing p below — the hairpin is
///   acknowledged before the text of its own timestep, so it has joined the staff's line when
///   the ff breaks off; <c>c''4\p_\&lt; … f''\f</c> draws all three below.
/// ⚠️ ONE STREAM A STAFF, where LilyPond keeps one line per Voice: a hairpin's start carries
///   no voice here (MusicMarkItem), so two voices of one staff share the line. Their marks
///   differ from LilyPond's only where one voice writes a side and the other, in the same
///   stretch, writes none.
/// </remarks>
internal struct DynamicLineSides
{
    private bool? _lineAbove;
    private bool _running;

    /// <summary>A mark joins: the side it stands on. <paramref name="written"/> is its own
    /// <c>.up</c> (true) / <c>.down</c> (false), null for none; <paramref name="staffAbove"/>
    /// the staff's own side, which a new line with nothing written takes.</summary>
    public bool Join(bool? written, bool staffAbove) => Join(written, staffAbove, out _);

    /// <inheritdoc cref="Join(bool?, bool)"/>
    /// <param name="newLine">The mark opened a line: none was alive, or it broke off the one
    /// that was by writing the other side.</param>
    public bool Join(bool? written, bool staffAbove, out bool newLine)
    {
        newLine = _lineAbove is not { } alive || (written is { } w && w != alive);
        _lineAbove = written ?? _lineAbove ?? staffAbove;
        return _lineAbove.Value;
    }

    /// <summary>A hairpin runs from here: the line stays open past this moment.</summary>
    public void HairpinStarts() => _running = true;

    /// <summary>The running hairpin ends at this moment (a level, a niente, a written stop).</summary>
    public void HairpinEnds() => _running = false;

    /// <summary>The moment is over: with no hairpin running, so is the line.</summary>
    public void MomentEnds()
    {
        if (!_running)
            _lineAbove = null;
    }

    /// <summary>
    /// Rewrites <see cref="DynamicItem.IsAbove"/> and <see cref="MusicMarkItem.HairpinAbove"/>
    /// of what the collector gathered — each still holding its own written side, else its
    /// staff's — to the side of the line it rides. The arrays keep their order and every
    /// item whose side does not change keeps its instance.
    /// </summary>
    public static (ImmutableArray<DynamicItem> Dynamics, ImmutableArray<MusicMarkItem> Marks) Resolve(
        IReadOnlyList<DynamicItem> dynamics, IReadOnlyList<MusicMarkItem> marks)
    {
        // Nothing written anywhere: every line takes its staff's side, which each item holds.
        bool anyWritten = false;
        foreach (var d in dynamics)
            anyWritten |= d.WrittenAbove != null && !d.IsExpressiveText;
        foreach (var m in marks)
            anyWritten |= m.HairpinWritten != null;
        if (!anyWritten)
            return (dynamics.ToImmutableArray(), marks.ToImmutableArray());

        // The events of every staff in time order; within a moment LilyPond's order: a written
        // stop, the hairpin's start, then the level.
        var events = new List<(int Staff, int Measure, int Item, int Kind, int Source, int Index)>();
        for (int i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            if (m.Type == MusicMarkType.HairpinStop)
                events.Add((m.StaffIndex, m.MeasureIndex, m.AnchorItemIndex, 0, m.SourcePosition, i));
            else if (m.Type is MusicMarkType.Cresc or MusicMarkType.Decresc or MusicMarkType.Dim)
                events.Add((m.StaffIndex, m.MeasureIndex, m.AnchorItemIndex, 1, m.SourcePosition, i));
        }
        for (int i = 0; i < dynamics.Count; i++)
        {
            var d = dynamics[i];
            if (!d.IsExpressiveText)
                events.Add((d.StaffIndex, d.MeasureIndex, d.ItemIndex, 2, d.SourcePosition, i));
        }
        events.Sort(static (a, b) =>
            a.Staff != b.Staff ? a.Staff.CompareTo(b.Staff)
            : a.Measure != b.Measure ? a.Measure.CompareTo(b.Measure)
            : a.Item != b.Item ? a.Item.CompareTo(b.Item)
            : a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind)
            : a.Source.CompareTo(b.Source));

        ImmutableArray<DynamicItem>.Builder? dynOut = null;
        ImmutableArray<MusicMarkItem>.Builder? markOut = null;
        var line = default(DynamicLineSides);
        int lineId = 0;
        for (int at = 0; at < events.Count;)
        {
            int end = at;
            while (end < events.Count && events[end].Staff == events[at].Staff
                   && events[end].Measure == events[at].Measure && events[end].Item == events[at].Item)
                end++;
            if (at > 0 && events[at - 1].Staff != events[at].Staff)
                line = default;

            bool startsHere = false;
            int lastStop = -1, lastStart = -1;
            for (int k = at; k < end; k++)
            {
                var e = events[k];
                switch (e.Kind)
                {
                    case 0:
                        line.HairpinEnds();
                        lastStop = e.Source;
                        break;
                    case 1:
                    {
                        var m = marks[e.Index];
                        bool above = line.Join(m.HairpinWritten, m.HairpinAbove, out bool fresh);
                        if (fresh)
                            lineId++;
                        line.HairpinStarts();
                        startsHere = true;
                        lastStart = e.Source;
                        if (above != m.HairpinAbove)
                            (markOut ??= marks.ToImmutableArray().ToBuilder())[e.Index] = m with { HairpinAbove = above };
                        break;
                    }
                    default:
                    {
                        var d = dynamics[e.Index];
                        // A niente is a word or its hairpin's circle, no member of the line
                        // (MeasureCollector keeps its written side); it still ends the hairpin.
                        if (d.Level != Syntax.DynamicLevel.Niente)
                        {
                            bool above = line.Join(d.WrittenAbove, d.IsAbove, out bool fresh);
                            if (fresh)
                                lineId++;
                            (dynOut ??= dynamics.ToImmutableArray().ToBuilder())[e.Index] =
                                d with { IsAbove = above, LineId = lineId };
                        }
                        // The level of a hairpin's own start is its start text, not its end.
                        if (!startsHere)
                            line.HairpinEnds();
                        break;
                    }
                }
            }
            // `@cresc@!cresc` on one note: the stop written after the start ends it there.
            if (startsHere && lastStop > lastStart)
                line.HairpinEnds();
            line.MomentEnds();
            at = end;
        }
        return (dynOut?.MoveToImmutable() ?? dynamics.ToImmutableArray(),
            markOut?.MoveToImmutable() ?? marks.ToImmutableArray());
    }
}
