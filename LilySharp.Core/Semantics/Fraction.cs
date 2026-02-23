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
    public int Numerator { get; }
    public int Denominator { get; }

    public Fraction(int numerator, int denominator = 1)
    {
        if (denominator == 0)
            throw new ArgumentException("Denominator cannot be zero", nameof(denominator));

        // Normalize sign
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        // Reduce to lowest terms
        int gcd = Gcd(Math.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }

    // Common durations
    public static readonly Fraction Whole = new(1, 1);
    public static readonly Fraction Half = new(1, 2);
    public static readonly Fraction Quarter = new(1, 4);
    public static readonly Fraction Eighth = new(1, 8);
    public static readonly Fraction Sixteenth = new(1, 16);
    public static readonly Fraction ThirtySecond = new(1, 32);
    public static readonly Fraction Zero = new(0, 1);

    /// <summary>
    /// Creates a duration from a note value (1=whole, 2=half, 4=quarter, etc.)
    /// </summary>
    public static Fraction FromNoteValue(int noteValue)
    {
        return noteValue switch
        {
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
    public static Fraction operator +(Fraction a, Fraction b)
    {
        return new Fraction(
            a.Numerator * b.Denominator + b.Numerator * a.Denominator,
            a.Denominator * b.Denominator);
    }

    public static Fraction operator -(Fraction a, Fraction b)
    {
        return new Fraction(
            a.Numerator * b.Denominator - b.Numerator * a.Denominator,
            a.Denominator * b.Denominator);
    }

    public static Fraction operator *(Fraction a, Fraction b)
    {
        return new Fraction(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    }

    public static Fraction operator /(Fraction a, Fraction b)
    {
        return new Fraction(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    // Comparison
    public int CompareTo(Fraction other)
    {
        return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    }

    public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;
    public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;
    public static bool operator <=(Fraction a, Fraction b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Fraction a, Fraction b) => a.CompareTo(b) >= 0;

    // Equality
    public bool Equals(Fraction other)
    {
        return Numerator == other.Numerator && Denominator == other.Denominator;
    }

    public override bool Equals(object? obj) => obj is Fraction other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public static bool operator ==(Fraction a, Fraction b) => a.Equals(b);
    public static bool operator !=(Fraction a, Fraction b) => !a.Equals(b);

    public override string ToString()
    {
        return Denominator == 1 ? Numerator.ToString() : $"{Numerator}/{Denominator}";
    }

    /// <summary>
    /// Returns the decimal value.
    /// </summary>
    public double ToDouble() => (double)Numerator / Denominator;
}