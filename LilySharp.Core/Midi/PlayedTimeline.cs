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
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Midi;

/// <summary>What a <see cref="TimelineMark"/> says at its tick.</summary>
internal enum TimelineMarkKind
{
    /// <summary>A <c>|:</c>.</summary>
    RepeatStart,
    /// <summary>A <c>:|</c>, with its <c>*N</c> when one is written.</summary>
    RepeatEnd,
    /// <summary>A volta ending opens (<c>[1.</c>), with the passes it names.</summary>
    EndingStart,
    /// <summary>A volta ending closes (its <c>]</c>, or the <c>:|</c> that closes it).</summary>
    EndingEnd,
    /// <summary>A navigation mark: a sign (segno, coda) or a text (fine, to coda, dc, ds).</summary>
    Navigation,
}

/// <summary>A repeat sign, an ending's edge or a navigation mark at a tick of the PRINTED
/// timeline — the music walked once, in the order the page prints it.</summary>
/// <param name="Lane">Who noted it: the part (or the chord row) whose music writes it, null for the
/// form. Every lane walking the same bars notes the same sign, and those are ONE sign; two alike
/// in one lane are two (<c>:| :|</c>).</param>
internal readonly record struct TimelineMark(
    int Tick, TimelineMarkKind Kind, int? Count = null, PassSet Passes = default,
    NavigationMarkType? Navigation = null, string? Lane = null);

/// <summary>
/// Plays a PRINTED timeline in the order a performer plays it: the exporter walks the music
/// once, as the page prints it, noting where the repeat signs, the endings and the navigation
/// marks stand (<see cref="TimelineMark"/>), and this class unfolds the result — the notes, the
/// tempo, the meters, the pedals, the words and the bar lines — along the route those marks
/// spell. One reader for a mark wherever it is written: in the form, in a section's music, in
/// any part of it (docs/inline-repeat-design.md §3.2, §3.3).
/// </summary>
/// <remarks>
/// <para>
/// LILYSHARP-OWN: the language's reading of its own signs, on ticks. Departs from: LilyPond
/// unfolds the music tree (scm/music-functions.scm unfold-repeats), which needs the repeat
/// written around the same music in every voice; a Lily# repeat is the SCORE's and is written
/// once, in any part, and may open in one section and close in a later one. Goes away: never
/// on its own. Observed by: <c>InlineRepeatMidiTests</c>.
/// </para>
/// <para>
/// The rules are the form's (GRAMMAR §6), read on the timeline:
/// a run is a <c>|:</c>, its body, its endings and its <c>:|</c>s; it plays
/// <see cref="RepeatPasses.Count"/> times, the body and then the ending the pass names
/// (<see cref="RepeatPasses.EndingFor"/>). A <c>:|</c> no <c>|:</c> opened repeats the piece from
/// its beginning, once. The jump texts are followed along <see cref="FormRoute"/>, the route the
/// form's own reader takes: a replayed stretch plays each run once, on its last pass, and rewinds
/// nothing. A mark inside a run is not a point the route addresses (FormRoute's remarks), and a
/// <c>|:</c> nothing closes repeats nothing (LYS4017 says so).
/// </para>
/// <para>
/// A TIE IS CARRIED TO THE NOTE PLAYED NEXT, over any jump (owner's decision 2026-09-28, the
/// rule the form's older walk <c>MidiExporter.PlayForm</c> and the page's
/// <c>Svg.Collector.SectionPlayGraph</c> keep): the printed timeline has tied a note to the note
/// PRINTED next, so at a jump the note is cut, and it goes on into a note of its pitch that
/// starts where the jump lands — as does a note that ends at the jump with a tie nothing printed
/// took (<see cref="MidiNote.TiedOut"/>: <c>[1. c1~ :|</c> back to a body opening on c). With no
/// such note there the tie ends at the jump. Until session 1245 every note was cut there, so a
/// tie out of a repeat's body sounded into its first ending and struck the second one again.
/// </para>
/// </remarks>
internal static class PlayedTimeline
{
    /// <summary>A stretch <c>[From, To)</c> of the printed timeline, played as it stands.</summary>
    internal readonly record struct Segment(int From, int To);

    private abstract record Element;
    private sealed record Plain(int From, int To) : Element;
    private sealed record Run(int Start, int BodyEnd, int End, int? Count,
        List<(PassSet Passes, int From, int To)> Endings) : Element;
    private sealed record LoneEnd : Element;
    private sealed record Sign(NavigationMarkType Type) : Element;

    /// <summary>The stretches of a printed timeline <c>[0, end)</c> in played order.</summary>
    internal static List<Segment> Route(IEnumerable<TimelineMark> marks, int end)
    {
        var elements = Elements(marks, end);
        var signs = new List<NavigationMarkType?>(elements.Count);
        foreach (var element in elements)
            signs.Add(element is Sign sign ? sign.Type : null);

        var played = new List<Segment>();
        foreach (var stretch in FormRoute.Of(signs))
        {
            for (int i = stretch.From; i < stretch.To; i++)
            {
                // A ':|' no '|:' opened: the piece from its beginning, once more — on the first
                // pass only (one rewind per written sign, MidiExporter.PlayForm's rule).
                if (elements[i] is LoneEnd && !stretch.Replay)
                {
                    for (int j = 0; j < i; j++)
                        Play(elements[j], lastPassOnly: false, played);
                    continue;
                }
                Play(elements[i], stretch.Replay, played);
            }
        }
        return Joined(played);
    }

    private static void Play(Element element, bool lastPassOnly, List<Segment> played)
    {
        switch (element)
        {
            case Plain plain:
                Add(played, plain.From, plain.To);
                break;
            case Run run:
                var passes = new List<PassSet>(run.Endings.Count);
                foreach (var ending in run.Endings)
                    passes.Add(ending.Passes);
                int count = RepeatPasses.Count(run.Count, passes);
                for (int pass = lastPassOnly ? count : 1; pass <= count; pass++)
                {
                    Add(played, run.Start, run.BodyEnd);
                    int ending = RepeatPasses.EndingFor(pass, passes);
                    if (ending >= 0)
                        Add(played, run.Endings[ending].From, run.Endings[ending].To);
                }
                break;
        }
    }

    private static void Add(List<Segment> played, int from, int to)
    {
        if (from < to)
            played.Add(new Segment(from, to));
    }

    /// <summary>Neighbours that follow each other on the printed timeline are one stretch, so
    /// every seam left is a jump.</summary>
    private static List<Segment> Joined(List<Segment> played)
    {
        var joined = new List<Segment>(played.Count);
        foreach (var segment in played)
        {
            if (joined.Count > 0 && joined[^1].To == segment.From)
                joined[^1] = new Segment(joined[^1].From, segment.To);
            else
                joined.Add(segment);
        }
        return joined;
    }

    /// <summary>At one tick: an ending closes, the repeat ends, a text of the bar behind it, a
    /// sign of the bar ahead, the next repeat opens, an ending opens.</summary>
    private static int Rank(TimelineMark mark) => mark.Kind switch
    {
        TimelineMarkKind.EndingEnd => 0,
        TimelineMarkKind.RepeatEnd => 1,
        TimelineMarkKind.Navigation => mark.Navigation is NavigationMarkType.Segno or NavigationMarkType.Coda ? 3 : 2,
        TimelineMarkKind.RepeatStart => 4,
        _ => 5,
    };

    private static List<Element> Elements(IEnumerable<TimelineMark> marks, int end)
    {
        // Every lane that walks a sign notes it: as many of a sign as the lane that wrote the
        // most of it (the record's remark).
        var sorted = marks.GroupBy(m => m with { Lane = null })
            .SelectMany(g => Enumerable.Repeat(g.Key, g.GroupBy(m => m.Lane).Max(lane => lane.Count())))
            .OrderBy(m => m.Tick).ThenBy(Rank).ToList();
        var elements = new List<Element>();
        int pos = 0;            // where the plain stretch being gathered began
        int runStart = -1;      // the open run's '|:' (or -1)
        int bodyEnd = -1;       // …where its body ends: its first ending, or its ':|'
        int? count = null;      // …its written ':|*N'
        int lastEnd = -1;       // …its last ':|' (or -1 before the first)
        int tail = -1;          // …where it ends so far: that ':|', or an ending closed after it
        var endings = new List<(PassSet Passes, int From, int To)>();
        (PassSet Passes, int From)? open = null; // the ending being read

        void Flush(int upTo)
        {
            if (pos < upTo)
                elements.Add(new Plain(pos, upTo));
            pos = Math.Max(pos, upTo);
        }
        void CloseEnding(int at)
        {
            if (open is { } ending)
                endings.Add((ending.Passes, ending.From, at));
            open = null;
        }
        void CloseRun(int at)
        {
            CloseEnding(at);
            elements.Add(new Run(runStart, bodyEnd, at, count, endings));
            pos = at;
            (runStart, bodyEnd, count, lastEnd, tail) = (-1, -1, null, -1, -1);
            endings = new List<(PassSet, int, int)>();
        }

        foreach (var mark in sorted)
        {
            bool inRun = runStart >= 0;
            // A run that has met its ':|' goes on only through an ending opening where it ends
            // so far, or a further ':|' closing such an ending; anything else finds it finished.
            if (inRun && lastEnd >= 0 && open == null
                && !(mark.Kind == TimelineMarkKind.EndingStart && mark.Tick == tail)
                && !(mark.Kind == TimelineMarkKind.RepeatEnd && mark.Tick == tail && tail > lastEnd))
            {
                CloseRun(tail);
                inRun = false;
            }
            switch (mark.Kind)
            {
                case TimelineMarkKind.RepeatStart:
                    if (inRun && lastEnd >= 0)
                        CloseRun(mark.Tick); // a trailing ending nothing closed ends here
                    // A '|:' nothing closed repeats nothing: its music is plain (LYS4017).
                    Flush(mark.Tick);
                    (runStart, bodyEnd, count, lastEnd, tail) = (mark.Tick, -1, null, -1, -1);
                    endings = new List<(PassSet, int, int)>();
                    open = null;
                    break;
                case TimelineMarkKind.RepeatEnd:
                    if (!inRun)
                    {
                        Flush(mark.Tick);
                        elements.Add(new LoneEnd());
                        break;
                    }
                    CloseEnding(mark.Tick);
                    if (bodyEnd < 0)
                        bodyEnd = mark.Tick;
                    count ??= mark.Count;
                    lastEnd = tail = mark.Tick;
                    break;
                case TimelineMarkKind.EndingStart:
                    // An ending no repeat opens is its plain music, once (GRAMMAR §6, LYS6008).
                    if (!inRun)
                        break;
                    CloseEnding(mark.Tick);
                    if (bodyEnd < 0)
                        bodyEnd = mark.Tick;
                    open = (mark.Passes, mark.Tick);
                    break;
                case TimelineMarkKind.EndingEnd:
                    if (inRun && open != null)
                    {
                        CloseEnding(mark.Tick);
                        if (lastEnd >= 0)
                            tail = mark.Tick;
                    }
                    break;
                case TimelineMarkKind.Navigation:
                    if (!inRun && mark.Navigation is { } type)
                    {
                        Flush(mark.Tick);
                        elements.Add(new Sign(type));
                    }
                    break;
            }
        }
        if (runStart >= 0 && lastEnd >= 0)
            CloseRun(open != null ? end : tail);
        Flush(end);
        return elements;
    }

    /// <summary>Rewrites <paramref name="midi"/> — a printed timeline — as it is played along
    /// <paramref name="marks"/>. A timeline with no jump in it is left as it is.</summary>
    /// <param name="printedEnd">Where the printed music ends (the walk's last tick: a closing
    /// rest sounds nothing and still takes its time).</param>
    internal static void Unfold(MidiFile midi, IReadOnlyList<TimelineMark> marks, int printedEnd)
    {
        int end = Math.Max(printedEnd, midi.EndTick);
        if (midi.BarTicks.Count > 0)
            end = Math.Max(end, midi.BarTicks.Max);
        var route = Route(marks, end);
        if (route.Count == 1 && route[0].From == 0 && route[0].To >= end)
            return;

        // Where each stretch starts in the played file.
        var starts = new int[route.Count];
        for (int k = 1; k < route.Count; k++)
            starts[k] = starts[k - 1] + (route[k - 1].To - route[k - 1].From);

        foreach (var track in midi.Tracks)
        {
            var notes = track.Notes.ToList();
            var tempos = track.TempoChanges.ToList();
            var meters = track.TimeSignatures.ToList();
            var keys = track.KeySignatures.ToList();
            var words = track.Lyrics.ToList();
            var pedals = track.Pedals.ToList();
            track.Notes.Clear();
            track.TempoChanges.Clear();
            track.TimeSignatures.Clear();
            track.KeySignatures.Clear();
            track.Lyrics.Clear();
            track.Pedals.Clear();
            // The notes of the stretch before that reach its end TIED (indices into track.Notes).
            var tiedOver = new List<int>();
            for (int k = 0; k < route.Count; k++)
            {
                var (from, to) = (route[k].From, route[k].To);
                int shift = starts[k] - from;
                bool last = k + 1 == route.Count;
                var arriving = tiedOver;
                tiedOver = new List<int>();
                foreach (var note in notes)
                {
                    int noteEnd = note.StartTick + note.DurationTicks;
                    // A note the printed walk tied INTO the stretch from the bars printed before
                    // it (`[1. d1~ :| [2. d1 ]`): what is played before the stretch is somewhere
                    // else, so the part of it standing here is a note written here.
                    bool tail = note.StartTick < from && noteEnd > from && note.DurationTicks > 0;
                    if (!tail && (note.StartTick < from || note.StartTick >= to))
                        continue;
                    int start = Math.Max(note.StartTick, from);
                    // Cut at the jump; the last stretch has none after it.
                    int length = last ? noteEnd - start : Math.Min(noteEnd, to) - start;
                    // …and TIED over it when the cut took the rest of a tie away, or the note ends
                    // at the jump with a tie written after it.
                    bool tied = !last && note.DurationTicks > 0 && (noteEnd > to || (noteEnd == to && note.TiedOut));
                    // A tie is carried to the note PLAYED next (the class remarks).
                    int into = start == from ? arriving.FindIndex(i => track.Notes[i].Pitch == note.Pitch
                        && track.Notes[i].Channel == note.Channel && track.Notes[i].Part == note.Part
                        && track.Notes[i].Voice == note.Voice) : -1;
                    if (into >= 0)
                    {
                        int held = arriving[into];
                        arriving.RemoveAt(into);
                        track.Notes[held] = track.Notes[held] with { DurationTicks = track.Notes[held].DurationTicks + length };
                        if (tied)
                            tiedOver.Add(held);
                        continue;
                    }
                    if (tied)
                        tiedOver.Add(track.Notes.Count);
                    track.Notes.Add(note with { StartTick = start + shift, DurationTicks = length });
                }

                // What stands at the stretch's start is what was in force there when printed.
                if (InForce(tempos, from, t => t.Tick) is { } tempo && (track.TempoChanges.Count == 0
                        || track.TempoChanges[^1].MicrosecondsPerBeat != tempo.MicrosecondsPerBeat))
                    track.TempoChanges.Add(tempo with { Tick = starts[k] });
                foreach (var change in tempos)
                    if (change.Tick > from && change.Tick < to)
                        track.TempoChanges.Add(change with { Tick = change.Tick + shift });

                if (InForce(meters, from, t => t.Tick) is { } meter && (track.TimeSignatures.Count == 0
                        || (track.TimeSignatures[^1].Numerator, track.TimeSignatures[^1].Denominator) != (meter.Numerator, meter.Denominator)))
                    track.TimeSignatures.Add(meter with { Tick = starts[k] });
                foreach (var change in meters)
                    if (change.Tick > from && change.Tick < to)
                        track.TimeSignatures.Add(change with { Tick = change.Tick + shift });

                if (InForce(keys, from, t => t.Tick) is { } key && (track.KeySignatures.Count == 0
                        || track.KeySignatures[^1].Fifths != key.Fifths))
                    track.KeySignatures.Add((starts[k], key.Fifths));
                foreach (var change in keys)
                    if (change.Tick > from && change.Tick < to)
                        track.KeySignatures.Add((change.Tick + shift, change.Fifths));

                foreach (var word in words)
                    if (word.Tick >= from && word.Tick < to)
                        track.Lyrics.Add(word with { Tick = word.Tick + shift });

                // A pedal is where the printed music has it at the place jumped to.
                if (k > 0)
                    foreach (int controller in pedals.Select(p => p.Controller).Distinct())
                    {
                        bool was = Down(pedals, controller, route[k - 1].To), now = Down(pedals, controller, from);
                        if (was != now)
                            track.Pedals.Add(new PedalChange(starts[k], now, controller));
                    }
                foreach (var pedal in pedals)
                    if (pedal.Tick >= from && pedal.Tick < to)
                        track.Pedals.Add(pedal with { Tick = pedal.Tick + shift });
            }
        }

        var bars = midi.BarTicks.ToList();
        var sections = midi.SectionStarts.ToList();
        var keyStarts = midi.KeyStarts.ToList();
        midi.BarTicks.Clear();
        midi.SectionStarts.Clear();
        midi.KeyStarts.Clear();
        for (int k = 0; k < route.Count; k++)
        {
            var (from, to) = (route[k].From, route[k].To);
            int shift = starts[k] - from;
            foreach (int bar in bars)
                if (bar >= from && bar <= to)
                    midi.BarTicks.Add(bar + shift);
            foreach (var (tick, name) in sections)
                if (tick >= from && tick < to)
                    midi.SectionStarts.TryAdd(tick + shift, name);
            if (InForce(keyStarts, from, t => t.Key) is { } standing)
                midi.KeyStarts[starts[k]] = standing.Value;
            foreach (var (tick, tonic) in keyStarts)
                if (tick > from && tick < to)
                    midi.KeyStarts[tick + shift] = tonic;
        }
    }

    /// <summary>The last entry at or before <paramref name="tick"/> of a tick-ordered list.</summary>
    private static T? InForce<T>(List<T> list, int tick, Func<T, int> tickOf) where T : struct
    {
        T? found = null;
        foreach (var entry in list)
        {
            if (tickOf(entry) > tick)
                break;
            found = entry;
        }
        return found;
    }

    /// <summary>Whether the pedal on <paramref name="controller"/> is down just before <paramref name="tick"/>.</summary>
    private static bool Down(List<PedalChange> pedals, int controller, int tick)
    {
        bool down = false;
        foreach (var pedal in pedals)
            if (pedal.Controller == controller && pedal.Tick < tick)
                down = pedal.Down;
        return down;
    }
}
