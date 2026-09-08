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
/// equally, written the way engraving convention writes a tuplet: the members take
/// a plain (undotted) note value that divides the total into P parts, and an
/// <c>M:P</c> tuplet fits the M members into that P-note frame. For a plain total
/// P is the largest power of two not above M — three in a quarter are eighths
/// under 3:2, five, six and seven are sixteenths under 5:4, 6:4, 7:4, nine are
/// thirty-seconds under 9:8 — and when P equals M there is no tuplet (four in a
/// quarter are four plain sixteenths). For a dotted total the frames are 3·2^k and
/// the one nearest M is taken (ties to the smaller): two in a dotted quarter are
/// eighths under 2:3, four are eighths under 4:3, five sixteenths under 5:6, three
/// are three plain eighths.
/// </summary>
/// <remarks>
/// <para>Shared by the SVG collector, the MIDI exporter and the MusicXML exporter so
/// the three outputs agree on how a <c>&lt;&lt; … &gt;&gt;</c> divides its time.</para>
/// <para>The plain-total rule is the convention (Gould, Behind Bars, tuplets: the
/// number against the next lower power of two; the duplet and quadruplet of compound
/// metre are the exceptions, written against 3). ⚠️ For one day (2026-09-07) the frame
/// was the count ABOVE M — sixteenths under 3:4 for a triplet — generalised from a
/// single hand-written <c>tuplet 3/4 { r16 c cis }</c> without checking the
/// convention; that gave quintuplets three beams (32nds under 5:8) and was reverted
/// the same day. A hand-written tuplet is what LilyPond draws, not what it
/// recommends: the twin cannot arbitrate a spelling, only the convention can.</para>
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
    /// The written notes for a member holding <paramref name="shares"/> shares of the
    /// group (<c>&lt;&lt; c . d &gt;&gt;</c> gives c two): one note when the span is a plain or
    /// dotted value, else the fewest tied notes, longest first. Two shares of a triplet's
    /// eighths are a quarter (<c>&lt;&lt; c . d &gt;&gt;4</c> = <c>tuplet 3/2 { c4 d8 }</c>),
    /// three of a plain quarter's sixteenths a dotted eighth (<c>c8. d16</c>), five of
    /// them a quarter tied to a sixteenth.
    /// </summary>
    public IReadOnlyList<(int Value, int Dots)> SpellShares(int shares)
    {
        var parts = new List<(int Value, int Dots)>();
        Fraction left = MemberDisplay * new Fraction(System.Math.Max(1, shares));
        while (left > Fraction.Zero)
        {
            var part = LongestNoteWithin(left);
            parts.Add(part);
            left = left - Fraction.FromNoteValue(part.Value).Dotted(part.Dots);
        }
        return parts;
    }

    /// <summary>The longest (up to double-dotted) note value not above <paramref name="f"/>.</summary>
    private static (int Value, int Dots) LongestNoteWithin(Fraction f)
    {
        for (int value = 1; value <= 1024; value *= 2)
            for (int dots = 2; dots >= 0; dots--)
                if (Fraction.FromNoteValue(value).Dotted(dots) <= f)
                    return (value, dots);
        return (f.Denominator, 0);
    }

    /// <summary>
    /// Computes the subdivision for <paramref name="memberCount"/> members sharing
    /// <paramref name="total"/> equally. <paramref name="total"/> is the trailing
    /// <c>&gt;&gt;N</c> duration or, absent one, the inherited running duration.
    /// </summary>
    public static ArpeggioSubdivision Compute(int memberCount, Fraction total)
    {
        int m = System.Math.Max(1, memberCount);
        // Every plain note value that divides the total, with the count P it takes,
        // ascending in P: for a plain total the powers of two (quarter: 1, 2, 4, 8 …),
        // for a dotted total 3·2^k (dotted quarter: 3, 6, 12 …).
        var frames = new List<(int Value, int P)>();
        for (int value = 1; value <= 1024; value *= 2)
        {
            Fraction parts = total / Fraction.FromNoteValue(value);
            if (parts.Numerator % parts.Denominator == 0)
                frames.Add((value, parts.Numerator / parts.Denominator));
        }
        if (frames.Count > 0)
        {
            var pick = frames[0];
            if (frames[0].P == 1)
            {
                // Plain total: the largest power of two not above M (3 → 2, 5..7 → 4,
                // 9..15 → 8) — the tuplet number against the next lower power of two.
                foreach (var f in frames)
                    if (f.P <= m)
                        pick = f;
            }
            else
            {
                // Dotted (compound) total: the frame nearest M, ties to the smaller —
                // the duplet 2:3 and quadruplet 4:3 of compound metre, 5:6 and 7:6.
                foreach (var f in frames)
                    if (System.Math.Abs(f.P - m) < System.Math.Abs(pick.P - m))
                        pick = f;
            }
            return new ArpeggioSubdivision(total, pick.Value, 0, m, pick.P);
        }
        // A total that is no note value at all: fall back to the power-of-two frame
        // below M with whatever (dotted) member value that leaves.
        int q = LargestPowerOfTwoAtMost(m);
        var (fallbackValue, dots) = DecomposeNoteValue(total / new Fraction(q));
        return new ArpeggioSubdivision(total, fallbackValue, dots, m, q);
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
