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

namespace LilySharp.Core.Semantics;

/// <summary>
/// Represents a rational number for duration calculations.
/// </summary>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    /// <summary>
    /// The numerator.
    /// </summary>
    public int Numerator { get; }

    /// <summary>
    /// The denominator.
    /// </summary>
    public int Denominator { get; }

    /// <summary>
    /// Initializes a new fraction from an integer numerator and denominator, reduced to lowest terms.
    /// </summary>
    public Fraction(int numerator, int denominator = 1)
        : this((long)numerator, denominator)
    {
    }

    /// <summary>
    /// Normalizes and reduces in 64-bit so intermediate products in the
    /// arithmetic operators cannot silently overflow. After reduction the
    /// result is range-checked back into <see cref="int"/>: a genuine overflow
    /// throws <see cref="OverflowException"/> rather than wrapping to a wrong
    /// (possibly negative) duration. For ordinary music — power-of-two
    /// denominators plus small tuplet factors — reduction keeps values tiny.
    /// </summary>
    private Fraction(long numerator, long denominator)
    {
        if (denominator == 0)
            throw new ArgumentException("Denominator cannot be zero", nameof(denominator));

        // Normalize sign (safe in long even for int.MinValue inputs).
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        // Reduce to lowest terms.
        long gcd = Gcd(Math.Abs(numerator), denominator);
        if (gcd != 0)
        {
            numerator /= gcd;
            denominator /= gcd;
        }

        checked
        {
            Numerator = (int)numerator;
            Denominator = (int)denominator;
        }
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            long temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }

    // Common durations

    /// <summary>The whole-note duration (1/1).</summary>
    public static readonly Fraction Whole = new(1, 1);
    /// <summary>The half-note duration (1/2).</summary>
    public static readonly Fraction Half = new(1, 2);
    /// <summary>The quarter-note duration (1/4).</summary>
    public static readonly Fraction Quarter = new(1, 4);
    /// <summary>The eighth-note duration (1/8).</summary>
    public static readonly Fraction Eighth = new(1, 8);
    /// <summary>The sixteenth-note duration (1/16).</summary>
    public static readonly Fraction Sixteenth = new(1, 16);
    /// <summary>The thirty-second-note duration (1/32).</summary>
    public static readonly Fraction ThirtySecond = new(1, 32);
    /// <summary>The zero duration (0/1).</summary>
    public static readonly Fraction Zero = new(0, 1);

    /// <summary>
    /// Creates a duration from a note value (1=whole, 2=half, 4=quarter, etc.)
    /// </summary>
    public static Fraction FromNoteValue(int noteValue)
    {
        return noteValue switch
        {
            // 0 = breve (double whole): the natural extension of the integer
            // scale downward (1 = whole). LILYPOND-REF: \breve.
            0 => new(2, 1),
            1 => Whole,
            2 => Half,
            4 => Quarter,
            8 => Eighth,
            16 => Sixteenth,
            32 => ThirtySecond,
            64 => new(1, 64),
            128 => new(1, 128),
            _ => new(1, noteValue)
        };
    }

    /// <summary>
    /// Creates a dotted duration. Each dot adds half of the previous value.
    /// </summary>
    public Fraction Dotted(int dots)
    {
        if (dots == 0) return this;

        // Dotted value = original * (2 - 1/2^dots)
        // For 1 dot: 3/2 of original
        // For 2 dots: 7/4 of original
        // For 3 dots: 15/8 of original
        int multiplier = (1 << (dots + 1)) - 1; // 2^(dots+1) - 1
        int divisor = 1 << dots; // 2^dots
        return this * new Fraction(multiplier, divisor);
    }

    // Arithmetic operators

    /// <summary>
    /// Adds two fractions.
    /// </summary>
    public static Fraction operator +(Fraction a, Fraction b)
    {
        return new Fraction(
            (long)a.Numerator * b.Denominator + (long)b.Numerator * a.Denominator,
            (long)a.Denominator * b.Denominator);
    }

    /// <summary>
    /// Subtracts one fraction from another.
    /// </summary>
    public static Fraction operator -(Fraction a, Fraction b)
    {
        return new Fraction(
            (long)a.Numerator * b.Denominator - (long)b.Numerator * a.Denominator,
            (long)a.Denominator * b.Denominator);
    }

    /// <summary>
    /// Multiplies two fractions.
    /// </summary>
    public static Fraction operator *(Fraction a, Fraction b)
    {
        return new Fraction((long)a.Numerator * b.Numerator, (long)a.Denominator * b.Denominator);
    }

    /// <summary>
    /// Divides one fraction by another.
    /// </summary>
    public static Fraction operator /(Fraction a, Fraction b)
    {
        return new Fraction((long)a.Numerator * b.Denominator, (long)a.Denominator * b.Numerator);
    }

    // Comparison

    /// <summary>
    /// Compares this fraction to another, returning a negative, zero, or positive value.
    /// </summary>
    public int CompareTo(Fraction other)
    {
        return ((long)Numerator * other.Denominator).CompareTo((long)other.Numerator * Denominator);
    }

    /// <summary>
    /// Determines whether one fraction is less than another.
    /// </summary>
    public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;
    /// <summary>
    /// Determines whether one fraction is greater than another.
    /// </summary>
    public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;
    /// <summary>
    /// Determines whether one fraction is less than or equal to another.
    /// </summary>
    public static bool operator <=(Fraction a, Fraction b) => a.CompareTo(b) <= 0;
    /// <summary>
    /// Determines whether one fraction is greater than or equal to another.
    /// </summary>
    public static bool operator >=(Fraction a, Fraction b) => a.CompareTo(b) >= 0;

    // Equality

    /// <summary>
    /// Determines whether this fraction equals another.
    /// </summary>
    public bool Equals(Fraction other)
    {
        return Numerator == other.Numerator && Denominator == other.Denominator;
    }

    /// <summary>
    /// Determines whether this fraction equals the specified object.
    /// </summary>
    public override bool Equals(object? obj) => obj is Fraction other && Equals(other);

    /// <summary>
    /// Returns a hash code for this fraction.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    /// <summary>
    /// Determines whether two fractions are equal.
    /// </summary>
    public static bool operator ==(Fraction a, Fraction b) => a.Equals(b);
    /// <summary>
    /// Determines whether two fractions are not equal.
    /// </summary>
    public static bool operator !=(Fraction a, Fraction b) => !a.Equals(b);

    /// <summary>
    /// Returns a string representation of this fraction.
    /// </summary>
    public override string ToString()
    {
        return Denominator == 1 ? Numerator.ToString() : $"{Numerator}/{Denominator}";
    }

    /// <summary>
    /// Returns the decimal value.
    /// </summary>
    public double ToDouble() => (double)Numerator / Denominator;
}