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

namespace LilySharp.Core.Syntax;

/// <summary>
/// Represents a span of text in the source.
/// </summary>
public readonly struct TextSpan : IEquatable<TextSpan>
{
    /// <summary>Initializes a new <see cref="TextSpan"/> with the given start offset and length.</summary>
    public TextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    /// <summary>
    /// Start position in the source.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Length of the span.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// End position (exclusive).
    /// </summary>
    public int End => Start + Length;

    /// <summary>
    /// Whether the span is empty.
    /// </summary>
    public bool IsEmpty => Length == 0;

    /// <summary>
    /// Whether this span contains the given position.
    /// </summary>
    public bool Contains(int position) => position >= Start && position < End;

    /// <summary>
    /// Whether this span contains the given span.
    /// </summary>
    public bool Contains(TextSpan span) => span.Start >= Start && span.End <= End;

    /// <summary>
    /// Whether this span overlaps with the given span.
    /// </summary>
    public bool OverlapsWith(TextSpan span) => Start < span.End && span.Start < End;

    /// <summary>Determines whether this span equals another span (same start and length).</summary>
    public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;
    /// <summary>Determines whether this span equals the given object.</summary>
    public override bool Equals(object? obj) => obj is TextSpan span && Equals(span);
    /// <summary>Returns a hash code for this span.</summary>
    public override int GetHashCode() => HashCode.Combine(Start, Length);
    /// <summary>Returns a string of the form <c>[start..end)</c>.</summary>
    public override string ToString() => $"[{Start}..{End})";

    /// <summary>Determines whether two spans are equal.</summary>
    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);
    /// <summary>Determines whether two spans are not equal.</summary>
    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
}