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
/// The chord row's measure-relative grid (GRAMMAR_AUDIT 8.1, decided
/// 2026-08-21): a bar's written slots — chord entries, rests, and <c>.</c>
/// extensions — divide it on the METER'S OWN BEAT GRID, the one
/// <see cref="BeamingPattern.Options.For"/> holds and the melody's beams are
/// grouped by. One slot takes the whole bar; a slot count equal to the beat
/// count sits one per beat; an integer MULTIPLE k of it splits every beat into
/// k equal parts; an integer DIVISOR groups whole beats. Anything else matches
/// no beat and returns null — the caller divides the bar equally and warns
/// (LYS2009), so the picture stays deterministic while the writer is told.
/// </summary>
/// <remarks>
/// The beat grid is LilyPond's <c>beatStructure</c> — reusing it is the point:
/// a second "beat" notion here would be the same quantity in two places
/// (CLAUDE.md's next-defect address), and a 5/8 bar whose melody beams [3,2]
/// would grid its chords another way. Consequences, measured (2026-08-22, the
/// audit's 8.1 table): 5/8 and 8/8 get their uneven groups ([3,2] / [3,3,2]);
/// 7/8 has no table entry and grids as seven single eighths. In an uneven
/// meter the subdivision k splits each beat of ITS OWN length (5/8, k=2:
/// 3/16 3/16 1/8 1/8) — unusual, but the only reading in which the grid and
/// the beams agree.
/// </remarks>
public static class ChordRhythm
{
    /// <summary>
    /// The slot lengths of a chord-row bar with <paramref name="slotCount"/>
    /// written slots in the given meter, or null when the count fits no grid
    /// shape (see the class remarks; the caller falls back to equal division
    /// and warns).
    /// </summary>
    /// <param name="pickup">The bar's own length when it is a PICKUP — shorter than the meter's
    /// bar: its beats are the ones it has, the meter's LAST ones (<see cref="BeatsOf"/>), and
    /// the same three shapes are read on them. Null, or not shorter than the meter's bar: the
    /// whole bar.</param>
    public static ImmutableArray<Fraction>? SlotDurations(int slotCount, int beats, int beatType, Fraction? pickup = null)
    {
        if (slotCount < 1)
            return null;

        var beatLengths = BeatsOf(beats, beatType, pickup);
        int beatCount = beatLengths.Count;

        // One slot is the whole bar in ANY meter — "| Am |" needs no grid.
        if (slotCount == 1)
        {
            var whole = Fraction.Zero;
            foreach (var beat in beatLengths)
                whole += beat;
            return ImmutableArray.Create(whole);
        }

        var slots = ImmutableArray.CreateBuilder<Fraction>(slotCount);

        // k slots per beat: each beat splits into k equal parts of its own length.
        if (slotCount % beatCount == 0)
        {
            int k = slotCount / beatCount;
            foreach (var beat in beatLengths)
            {
                var part = beat * new Fraction(1, k);
                for (int i = 0; i < k; i++)
                    slots.Add(part);
            }
            return slots.MoveToImmutable();
        }

        // m beats per slot: whole beats grouped, never a beat split across slots.
        if (beatCount % slotCount == 0)
        {
            int m = beatCount / slotCount;
            for (int s = 0; s < slotCount; s++)
            {
                var len = Fraction.Zero;
                for (int b = 0; b < m; b++)
                    len += beatLengths[s * m + b];
                slots.Add(len);
            }
            return slots.MoveToImmutable();
        }

        return null;
    }

    /// <summary>
    /// The beats a chord row's bar is gridded on, each with its length: the meter's own
    /// (<see cref="BeamingPattern.Options.For"/>), or — for a <paramref name="pickup"/>, a bar
    /// shorter than the meter's — the LAST of them that fill it, the first cut where the pickup
    /// begins inside a beat.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN (2026-11-14, a design by musical validity — RULES §5.2; LilyPond's chord
    /// names are written with durations and have no slot grid): a pickup is the END of a bar,
    /// counted back from its bar line, as the metronome counts it
    /// (<see cref="LilySharp.Core.Midi.MetronomeBeats"/>) and as LilyPond's <c>\partial</c> sets
    /// measurePosition. So three beats of pickup in 4/4 take three slots, one a beat, and two
    /// slots there fit no beat as they fit none in 3/4. Until then the page and the LilyPond
    /// twin gridded the pickup on the WHOLE meter and scaled the slots to its length (three
    /// slots were warned off a grid they stood on, LYS2009; four were not, and stood off every
    /// beat), while the sound and the MusicXML kept the whole meter's grid and CUT it at the
    /// pickup's length — the bar's first beats, where a pickup is its last, and a slot past the
    /// cut neither sounded nor was written (Orecchiante's request 3, Lab sessions/p1242/orecchiante).
    /// Observed by ChordRowPickupGridTests.
    /// </remarks>
    public static IReadOnlyList<Fraction> BeatsOf(int beats, int beatType, Fraction? pickup = null)
    {
        var options = BeamingPattern.Options.For(new TimeSignature(beats, beatType));
        var structure = options.BeatStructure;
        var all = new List<Fraction>(structure.Length);
        var whole = Fraction.Zero;
        foreach (int g in structure)
        {
            var beat = options.BeatBase * new Fraction(g);
            all.Add(beat);
            whole += beat;
        }
        if (pickup is not { } length || length <= Fraction.Zero || length >= whole)
            return all;

        var tail = new List<Fraction>();
        var left = length;
        for (int i = all.Count - 1; i >= 0 && left > Fraction.Zero; i--)
        {
            var beat = all[i] <= left ? all[i] : left;
            tail.Insert(0, beat);
            left -= beat;
        }
        return tail;
    }
}
