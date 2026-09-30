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
using System.Collections.Immutable;
using System.Linq;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The passes of a repeat run a volta ending names: <c>[1. …]</c> is {1}, <c>[1-3. …]</c>
/// is {1, 2, 3}, <c>[1,3. …]</c> is {1, 3}. A VALUE — two endings naming the same passes are
/// equal, whichever order the numbers were written in — so it rides a play's stamp
/// (<see cref="Svg.Model.MusicItem.SectionEndingPasses"/>) like the stamp's other fields.
/// </summary>
public readonly struct PassSet : IEquatable<PassSet>
{
    // Ascending and distinct; default (a default struct) names no pass at all.
    private readonly ImmutableArray<int> _passes;

    private PassSet(ImmutableArray<int> passes) => _passes = passes;

    /// <summary>No pass named: a body play, or an item that opens no ending.</summary>
    public static PassSet None => default;

    /// <summary>The set of the passes given (a pass below 1 is not a pass and is dropped).</summary>
    public static PassSet Of(IEnumerable<int> passes)
    {
        var sorted = passes.Where(p => p >= 1).Distinct().OrderBy(p => p).ToImmutableArray();
        return sorted.IsEmpty ? default : new PassSet(sorted);
    }

    /// <summary>True when no pass is named.</summary>
    public bool IsEmpty => _passes.IsDefaultOrEmpty;

    /// <summary>True when <paramref name="pass"/> (1-based) is one of the passes named.</summary>
    public bool Contains(int pass) => !_passes.IsDefaultOrEmpty && _passes.Contains(pass);

    /// <summary>The highest pass named, 0 when none.</summary>
    public int Max => _passes.IsDefaultOrEmpty ? 0 : _passes[^1];

    public bool Equals(PassSet other)
    {
        if (_passes.IsDefaultOrEmpty)
            return other._passes.IsDefaultOrEmpty;
        return !other._passes.IsDefaultOrEmpty && _passes.SequenceEqual(other._passes);
    }

    public override bool Equals(object? obj) => obj is PassSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (!_passes.IsDefaultOrEmpty)
            foreach (int p in _passes)
                hash.Add(p);
        return hash.ToHashCode();
    }

    public static bool operator ==(PassSet left, PassSet right) => left.Equals(right);
    public static bool operator !=(PassSet left, PassSet right) => !left.Equals(right);

    /// <summary>The passes as a volta writes them: "1,3".</summary>
    public override string ToString() => _passes.IsDefaultOrEmpty ? "" : string.Join(",", _passes);
}

/// <summary>
/// How a repeat run plays its passes — ONE SENTENCE, FOUR READERS: the music stream's MIDI
/// (<c>MidiExporter.ProcessRepeatSpan</c>), the form's MIDI (<c>MidiExporter.PlayRepeatRun</c>),
/// the PLAYED order a tie is carried along (<c>PlayedOrder.Expand</c>) and the neighbours a
/// split bar is judged by (<c>SectionBoundaryBars.ExpandRepeat</c>). The body plays
/// <see cref="Count"/> times and, on pass p, the ending <see cref="EndingFor"/> names follows.
/// </summary>
/// <remarks>
/// Until 2026-09-29 (HANDOFF §1.1 第663 ⑾) only the music stream's MIDI read the numbers. The
/// form's three readers each spelled the run for themselves, and all three played the i-th
/// WRITTEN ending on pass i and counted the ENDINGS: <c>form main { |: A [1-2. B] :| [3. C] }</c>
/// sounded A B A C — and carried a tie, and exempted a split bar, along that order — while the
/// same music written inline sounded A B A B A C. Measured on that book and on
/// <c>[1,3. B] :| [2. C]</c> (A B A C A B against A B A C); <c>FormEndingPassTests</c>.
/// </remarks>
internal static class RepeatPasses
{
    /// <summary>The number of passes: the written <c>:|*N</c>; else the highest pass any
    /// ending names, and at least two; else two.</summary>
    /// <param name="written">The <c>:|*N</c> on the run, or null when none was written.</param>
    /// <param name="endings">The passes each ending names, in written order.</param>
    public static int Count(int? written, IReadOnlyList<PassSet> endings)
    {
        if (written is { } n)
            return n;
        int highest = 0;
        foreach (var ending in endings)
            highest = Math.Max(highest, ending.Max);
        return Math.Max(2, highest);
    }

    /// <summary>The index of the ending pass <paramref name="pass"/> (1-based) plays: the first
    /// written whose numbers name it, else the LAST written, or -1 with no endings.</summary>
    /// <remarks>
    /// LILYSHARP-OWN: a pass no ending names replays the LAST ending — <c>|: A [1. B] :|*3 [2. C]</c>
    /// plays C on passes 2 and 3 (<c>MidiRepeatTests.InlineVoltas_ExplicitCountClampsToLastEnding</c>).
    /// LilyPond departs here: with no <c>\volta</c> on any alternative, <c>make-repeat</c>
    /// (scm/music-functions.scm:346-352) gives the extra volte to the FIRST alternative, so
    /// <c>\repeat volta 3 { A } \alternative { { B } { C } }</c> plays B B C. Since 2026-09-30
    /// (owner's decision) such a pass is an ERROR — a count beside endings is LYS1042, a pass the
    /// numbers skip or name twice LYS1043 — so this arm plays only a file that does not compile
    /// clean; every clean form names each pass once and no reading is left to choose.
    /// </remarks>
    public static int EndingFor(int pass, IReadOnlyList<PassSet> endings)
    {
        for (int i = 0; i < endings.Count; i++)
            if (endings[i].Contains(pass))
                return i;
        return endings.Count - 1;
    }
}
