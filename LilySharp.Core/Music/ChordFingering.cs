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

using System.Collections.Generic;
using System.Linq;

namespace LilySharp.Core.Music;

/// <summary>
/// How a player frets a chord's own notes: a string for each, within one hand's reach — the
/// shape a bare <c>@diagram</c> on a <c>&lt;…&gt;</c> chord draws, and what LYS1055 finds no shape
/// for (owner's requests 2026-10-30).
/// </summary>
/// <remarks>
/// Fingerable: each note on a string of its own, its fret 0 (open) to <see cref="TopFret"/>, the
/// fretted ones no more than <see cref="Reach"/> frets apart (LilyPond's <c>maximumFretStretch</c>,
/// the tab's own rule — <c>Tunings.CalculateChordFrets</c>), and no more fretted notes above the
/// lowest fret than three fingers can hold (the index barring that fret). Every assignment is
/// tried — the tab's own placement is greedy, highest note first, and answers "no string" for
/// chords a hand can hold — and of those that hold, the one a player reaches for: its notes on
/// strings in the order of their pitches, then the lowest position, then the fewest fingers.
/// </remarks>
public static class ChordFingering
{
    /// <summary>The highest fret looked at.</summary>
    public const int TopFret = 22;

    /// <summary>The widest a fretted chord may stretch, from its lowest fretted note to its highest.</summary>
    public const int Reach = 4;

    /// <summary>A fret for each string (index 0 the lowest; -1 unplayed) holding every pitch,
    /// the one a player reaches for; null when no hand can.</summary>
    public static int[]? Fingering(IReadOnlyList<int> pitches, IReadOnlyList<int> strings)
    {
        var sorted = pitches.Distinct().OrderBy(p => p).ToArray();
        var frets = Enumerable.Repeat(-1, strings.Count).ToArray();
        int[]? best = null;
        (int, int, int)? bestCost = null;
        Place(0);
        return best;

        void Place(int i)
        {
            if (i == sorted.Length)
            {
                var cost = Cost(frets, sorted, strings);
                if (bestCost is not { } b || cost.CompareTo(b) < 0)
                    (best, bestCost) = ((int[])frets.Clone(), cost);
                return;
            }
            for (int s = 0; s < strings.Count; s++)
            {
                int fret = sorted[i] - strings[s];
                if (frets[s] >= 0 || fret < 0 || fret > TopFret)
                    continue;
                frets[s] = fret;
                if (Holdable(frets))
                    Place(i + 1);
                frets[s] = -1;
            }
        }
    }

    /// <summary>The chord's shape as the page's diagram spec (<see cref="ChordVoicings.ToFrameSpec"/>),
    /// or null when no hand can hold it, or it climbs past the diagram's 15th fret.</summary>
    public static string? FrameSpec(IReadOnlyList<int> pitches, IReadOnlyList<int> strings)
        => Fingering(pitches, strings) is { } frets && frets.Max() <= 15 ? ChordVoicings.ToFrameSpec(frets) : null;

    /// <summary>The fretted notes within <see cref="Reach"/>, and no more of them above the lowest
    /// fret than three fingers (the index barring the lowest).</summary>
    internal static bool Holdable(IReadOnlyList<int> frets)
    {
        int low = int.MaxValue, high = int.MinValue;
        foreach (int f in frets)
            if (f > 0)
                (low, high) = (System.Math.Min(low, f), System.Math.Max(high, f));
        if (low == int.MaxValue)
            return true;
        int above = 0;
        foreach (int f in frets)
            if (f > low)
                above++;
        return high - low <= Reach && above <= 3;
    }

    /// <summary>What a player weighs: strings crossed against the pitches' order, the highest fret
    /// (the position), the fretted notes (the fingers).</summary>
    private static (int Crossed, int Position, int Fingers) Cost(int[] frets, int[] sorted, IReadOnlyList<int> strings)
    {
        int crossed = 0, last = -1;
        foreach (int p in sorted)
        {
            int s = -1;
            for (int k = 0; k < frets.Length; k++)
                if (frets[k] >= 0 && strings[k] + frets[k] == p)
                {
                    s = k;
                    break;
                }
            if (s < last)
                crossed++;
            last = s;
        }
        return (crossed, frets.Max(), frets.Count(f => f > 0));
    }
}
