using System.Diagnostics;

namespace LilySharp.Core.Syntax.InternalSyntax;

/// <summary>
/// Base class for immutable syntax nodes (Green nodes in Roslyn terminology).
/// Green nodes are position-independent and can be shared/cached.
/// </summary>
[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
internal abstract class GreenNode
{
    private readonly SyntaxKind _kind;
    private readonly int _fullWidth;
    private readonly GreenNode?[] _children;

    protected GreenNode(SyntaxKind kind, int fullWidth)
    {
        _kind = kind;
        _fullWidth = fullWidth;
        _children = [];
    }

    protected GreenNode(SyntaxKind kind, GreenNode?[] children)
    {
        _kind = kind;
        _children = children;
        
        int width = 0;
        foreach (var child in children)
        {
            if (child != null)
                width += child.FullWidth;
        }
        _fullWidth = width;
    }

    /// <summary>
    /// The kind of this syntax node or token.
    /// </summary>
    public SyntaxKind Kind => _kind;

    /// <summary>
    /// The full width of this node including all trivia.
    /// </summary>
    public int FullWidth => _fullWidth;

    /// <summary>
    /// The number of child slots.
    /// </summary>
    public int SlotCount => _children.Length;

    /// <summary>
    /// Gets the child at the specified index.
    /// </summary>
    public GreenNode? GetSlot(int index)
    {
        return index < _children.Length ? _children[index] : null;
    }

    /// <summary>
    /// Whether this is a token (leaf node).
    /// </summary>
    public virtual bool IsToken => false;

    /// <summary>
    /// Whether this is a trivia (whitespace, comment).
    /// </summary>
    public virtual bool IsTrivia => false;

    /// <summary>
    /// The text of this token (only for tokens).
    /// </summary>
    public virtual string Text => string.Empty;

    /// <summary>
    /// Leading trivia attached to this node.
    /// </summary>
    public virtual GreenNode? LeadingTrivia => null;

    /// <summary>
    /// Trailing trivia attached to this node.
    /// </summary>
    public virtual GreenNode? TrailingTrivia => null;

    /// <summary>
    /// Width without trivia.
    /// </summary>
    public int Width => FullWidth - LeadingTriviaWidth - TrailingTriviaWidth;

    public int LeadingTriviaWidth => LeadingTrivia?.FullWidth ?? 0;
    public int TrailingTriviaWidth => TrailingTrivia?.FullWidth ?? 0;

    private string GetDebuggerDisplay()
    {
        return $"{GetType().Name} {Kind} [{FullWidth}]";
    }

    public override string ToString()
    {
        return ToFullString();
    }

    /// <summary>
    /// Returns the full text including trivia.
    /// </summary>
    public virtual string ToFullString()
    {
        var writer = new System.IO.StringWriter();
        WriteTo(writer);
        return writer.ToString();
    }

    /// <summary>
    /// Writes this node's text to the writer.
    /// </summary>
    public virtual void WriteTo(System.IO.TextWriter writer)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var child = GetSlot(i);
            child?.WriteTo(writer);
        }
    }
}