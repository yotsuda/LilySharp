namespace LilySharp.Core.Syntax;

/// <summary>
/// Represents a change to source text.
/// </summary>
public readonly struct TextChange
{
    /// <summary>
    /// The span of text being replaced.
    /// </summary>
    public TextSpan Span { get; }
    
    /// <summary>
    /// The new text to insert.
    /// </summary>
    public string NewText { get; }

    public TextChange(TextSpan span, string newText)
    {
        Span = span;
        NewText = newText;
    }

    /// <summary>
    /// Creates a TextChange for an insertion at a position.
    /// </summary>
    public static TextChange Insert(int position, string text)
        => new(new TextSpan(position, 0), text);

    /// <summary>
    /// Creates a TextChange for a deletion of a span.
    /// </summary>
    public static TextChange Delete(TextSpan span)
        => new(span, string.Empty);

    /// <summary>
    /// Creates a TextChange for replacing a span with new text.
    /// </summary>
    public static TextChange Replace(TextSpan span, string newText)
        => new(span, newText);

    public override string ToString()
        => $"[{Span.Start}..{Span.Start + Span.Length}) => \"{NewText}\"";
}