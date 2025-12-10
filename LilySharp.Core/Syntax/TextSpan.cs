namespace LilySharp.Core.Syntax;

/// <summary>
/// Represents a span of text in the source.
/// </summary>
public readonly struct TextSpan : IEquatable<TextSpan>
{
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

    public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;
    public override bool Equals(object? obj) => obj is TextSpan span && Equals(span);
    public override int GetHashCode() => HashCode.Combine(Start, Length);
    public override string ToString() => $"[{Start}..{End})";

    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);
    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
}