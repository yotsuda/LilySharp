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

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Default per-measure rhythm for an independent chord part when the user writes
/// no explicit durations: the chords are spread across the measure by a fixed
/// per-count table (a lead-sheet convention — "just type the chords"). Only 4/4
/// is tabulated for now; other meters require explicit durations (null result).
/// </summary>
/// <remarks>
/// Agreed table (4/4), favouring quarters then eighths (longer value last for 3):
///   1: 1            2: 2 2            3: 4 4 2          4: 4 4 4 4
///   5: 4 4 4 8 8    6: 4 4 8 8 8 8    7: 4 8 8 8 8 8 8  8: 8 8 8 8 8 8 8 8
/// 9+ chords in a measure are rare; the user supplies explicit durations there.
/// </remarks>
public static class ChordRhythm
{
    /// <summary>
    /// The default duration for each of <paramref name="count"/> chords in a
    /// measure of the given time signature, or null when no default applies
    /// (unsupported meter, or count outside the tabulated 1..8 range).
    /// </summary>
    public static ImmutableArray<Fraction>? DefaultDurations(int count, int beats, int beatType)
    {
        // Only common time (a whole-note measure) is tabulated for now.
        if (beats != 4 || beatType != 4)
            return null;
        if (count < 1 || count > 8)
            return null;

        return count switch
        {
            1 => ImmutableArray.Create(Fraction.Whole),
            2 => ImmutableArray.Create(Fraction.Half, Fraction.Half),
            3 => ImmutableArray.Create(Fraction.Quarter, Fraction.Quarter, Fraction.Half),
            4 => ImmutableArray.Create(Fraction.Quarter, Fraction.Quarter, Fraction.Quarter, Fraction.Quarter),
            // 5..8: (8 - count) quarters, then the rest as eighths (sum = one whole).
            _ => BuildQuartersThenEighths(count),
        };
    }

    private static ImmutableArray<Fraction> BuildQuartersThenEighths(int count)
    {
        int quarters = 8 - count;       // 5→3, 6→2, 7→1, 8→0
        int eighths = count - quarters; // the remainder
        var b = ImmutableArray.CreateBuilder<Fraction>(count);
        for (int i = 0; i < quarters; i++) b.Add(Fraction.Quarter);
        for (int i = 0; i < eighths; i++) b.Add(Fraction.Eighth);
        return b.MoveToImmutable();
    }
}
