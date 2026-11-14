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

/// <summary>
/// Plays a PRINTED timeline in the order a performer plays it: the exporter walks the music
/// once, as the page prints it, noting where the repeat signs, the endings and the navigation
/// marks stand (<see cref="RepeatSign"/>), and this class unfolds the result — the notes, the
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
    /// <summary>Rewrites <paramref name="midi"/> — a printed timeline — as it is played along
    /// <paramref name="marks"/>. A timeline with no jump in it is left as it is.</summary>
    /// <param name="printedEnd">Where the printed music ends (the walk's last tick: a closing
    /// rest sounds nothing and still takes its time).</param>
    /// <returns>The pass each PLAYED tick is on (<see cref="PassAt"/>): the stretches of
    /// <see cref="RepeatStream.RouteByPass"/> laid end to end, each <c>[From, To)</c> in played
    /// ticks.</returns>
    internal static IReadOnlyList<RepeatStream.PassStretch> Unfold(MidiFile midi, IReadOnlyList<RepeatSign> marks, int printedEnd)
    {
        int end = Math.Max(printedEnd, midi.EndTick);
        if (midi.BarTicks.Count > 0)
            end = Math.Max(end, midi.BarTicks.Max);
        var byPass = RepeatStream.RouteByPass(marks, end);
        var passes = new List<RepeatStream.PassStretch>(byPass.Count);
        int at = 0;
        foreach (var stretch in byPass)
        {
            passes.Add(new RepeatStream.PassStretch(at, at + (stretch.To - stretch.From), stretch.Pass));
            at += stretch.To - stretch.From;
        }
        Rewrite(midi, RepeatStream.Route(marks, end), end);
        return passes;
    }

    /// <summary>The pass the played tick <paramref name="tick"/> is on (<see cref="Unfold"/>'s
    /// answer) — 1 outside every stretch.</summary>
    internal static int PassAt(IReadOnlyList<RepeatStream.PassStretch> passes, int tick)
    {
        foreach (var stretch in passes)
            if (tick >= stretch.From && tick < stretch.To)
                return stretch.Pass;
        return 1;
    }

    private static void Rewrite(MidiFile midi, List<RepeatStream.Segment> route, int end)
    {
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
