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

using LilySharp.Core.Semantics;

namespace LilySharp.Core.Music;

/// <summary>
/// The equal-subdivision timing for a <c>&lt;&lt; … &gt;&gt;</c> arpeggio — a
/// written-out broken chord whose members split the group's total duration
/// equally. Three notes in a quarter play a triplet, five a quintuplet, nine a
/// nonuplet: the members are notated at the value that fills the total with the
/// nearest lower power of two, and an <c>M:P</c> tuplet fits the M members into
/// that P-note frame. When M is itself a power of two there is no tuplet.
/// </summary>
/// <remarks>
/// Shared by the SVG collector, the MIDI exporter and the MusicXML exporter so
/// the three outputs agree on how a <c>&lt;&lt; … &gt;&gt;</c> divides its time.
/// </remarks>
internal readonly record struct ArpeggioSubdivision(
    Fraction Total,
    int MemberValue,
    int MemberDots,
    int TupletNum,
    int TupletBase)
{
    /// <summary>True when the members form an auto-tuplet (M is not a power of two).</summary>
    public bool HasTuplet => TupletNum != TupletBase;

    /// <summary>The notated (undotted) base duration of one member.</summary>
    public Fraction MemberBaseDuration => Fraction.FromNoteValue(MemberValue);

    /// <summary>The notated duration of one member WITH its dots (before the tuplet time
    /// scale) — the written value a member fills, e.g. a dotted eighth for <c>&lt;&lt; c e g
    /// &gt;&gt;4.</c>.</summary>
    public Fraction MemberDisplay =>
        MemberDots > 0 ? MemberBaseDuration.Dotted(MemberDots) : MemberBaseDuration;

    /// <summary>The tuplet time scale (Base/Num); 1/1 when there is no tuplet.</summary>
    public Fraction TimeScale => new(TupletBase, TupletNum);

    /// <summary>
    /// Computes the subdivision for <paramref name="memberCount"/> members sharing
    /// <paramref name="total"/> equally. <paramref name="total"/> is the trailing
    /// <c>&gt;&gt;N</c> duration or, absent one, the inherited running duration.
    /// </summary>
    public static ArpeggioSubdivision Compute(int memberCount, Fraction total)
    {
        int m = System.Math.Max(1, memberCount);
        int p = LargestPowerOfTwoAtMost(m);
        // The member is notated as if the total held P equal notes (P a power of
        // two), and the bracket says "M in the time of P". display = total / P.
        Fraction display = total / new Fraction(p);
        var (value, dots) = DecomposeNoteValue(display);
        return new ArpeggioSubdivision(total, value, dots, m, p);
    }

    private static int LargestPowerOfTwoAtMost(int n)
    {
        int p = 1;
        while (p * 2 <= n)
            p *= 2;
        return p;
    }

    /// <summary>Recovers a (base note value, dot count) from a note-value fraction —
    /// the inverse of <c>Fraction.FromNoteValue(value).Dotted(dots)</c>. Falls back to
    /// the raw denominator (no dots) for a value that is not a standard note.</summary>
    private static (int Value, int Dots) DecomposeNoteValue(Fraction f)
    {
        int[] values = { 0, 1, 2, 4, 8, 16, 32, 64, 128 };
        foreach (int value in values)
            for (int dots = 0; dots <= 4; dots++)
                if (Fraction.FromNoteValue(value).Dotted(dots) == f)
                    return (value, dots);
        return (f.Denominator, 0);
    }
}
