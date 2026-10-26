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

namespace LilySharp.Core.Midi;

/// <summary>
/// The beats of a piece as it plays, in ticks: one per beat of the meter in force (its
/// denominator's note) in every bar, the bar's first accented — what the preview's metronome
/// clicks (owner's request 2026-10-25) and what the harmony reading divides the music by
/// (<see cref="Harmony.HarmonyReader"/>). The bars are where the bar lines stand as the music
/// plays (<see cref="MidiFile.BarTicks"/>), so a pickup keeps the accents on the bar lines: a
/// first bar shorter than the meter is a pickup, its beats counted back from its bar line and none
/// accented. Past the last bar line, the meter's bars to the last note's end.
/// </summary>
/// <remarks>Moved here from the language server (2026-10-26), which calls it.</remarks>
public static class MetronomeBeats
{
    /// <summary>The beats, and where the music's last bar ends (<paramref name="barEnd"/>, in
    /// ticks): its closing bar line when one stands after the last note's start — the beats stop
    /// there, a fermata's hold past it unclicked — else the end of the meter's bar the last note
    /// ends in. A loop's round ends at the bar line its stretch ends by (the preview's loop;
    /// owner's report 2026-10-25: a guessed bar past a fermata made a round a bar too long).</summary>
    public static List<(int Tick, bool Accent)> Of(MidiFile midi, int tpq, out int barEnd)
    {
        var meters = midi.Tracks.SelectMany(t => t.TimeSignatures)
            .GroupBy(m => m.Tick).Select(g => g.Last()).OrderBy(m => m.Tick).ToList();
        if (meters.Count == 0 || meters[0].Tick > 0)
            meters.Insert(0, new TimeSignatureChange(0, 4, 4));
        TimeSignatureChange MeterAt(int tick) => meters.Last(m => m.Tick <= tick);
        int Step(TimeSignatureChange m) => Math.Max(1, tpq * 4 / Math.Max(1, m.Denominator));
        int BarOf(TimeSignatureChange m) => Step(m) * Math.Max(1, m.Numerator);

        int endTick = midi.Tracks.SelectMany(t => t.Notes).Select(n => n.StartTick + n.DurationTicks).DefaultIfEmpty(0).Max();
        int lastOnset = midi.Tracks.SelectMany(t => t.Notes).Select(n => n.StartTick).DefaultIfEmpty(0).Max();
        int closing = midi.BarTicks.Count > 0 ? midi.BarTicks.Max : 0;
        // The closing bar line, when the music stops at it: the beats end there.
        if (closing > lastOnset)
            endTick = closing;
        var bars = midi.BarTicks.Where(t => t > 0 && t < endTick).ToList();
        var beats = new List<(int, bool)>();
        int start = 0;
        if (bars.Count > 0 && bars[0] < BarOf(MeterAt(0)))
        {
            // The pickup: its beats end at its bar line, none of them a bar's first.
            int step = Step(MeterAt(0));
            for (int tick = bars[0] - step; tick >= 0; tick -= step)
                beats.Insert(0, (tick, false));
            start = bars[0];
            bars.RemoveAt(0);
        }
        bars.Insert(0, start);
        for (int i = 0; i < bars.Count; i++)
        {
            var m = MeterAt(bars[i]);
            bool last = i + 1 == bars.Count;
            int until = last ? endTick : bars[i + 1];
            for (int k = 0, tick = bars[i]; tick < until; k++, tick += Step(m))
                beats.Add((tick, last ? k % Math.Max(1, m.Numerator) == 0 : k == 0));
        }
        if (closing > lastOnset)
            barEnd = closing;
        else
        {
            // No bar line after the last note: the meter's bars from the last bar line on.
            var m = MeterAt(bars[^1]);
            int bar = BarOf(m);
            barEnd = bars[^1] + Math.Max(1, (endTick - bars[^1] + bar - 1) / bar) * bar;
        }
        return beats;
    }
}
