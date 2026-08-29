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

    /// <summary>
    /// The leading trivia width of the first TERMINAL under this node — the whitespace a
    /// reader would have to skip to reach the node's first real character.
    /// </summary>
    /// <remarks>
    /// ⚠️ <see cref="LeadingTriviaWidth"/> IS NOT THAT, AND FOR A COMPOSITE NODE IT IS ALWAYS
    /// ZERO. <see cref="LeadingTrivia"/> is virtual and only a TOKEN overrides it, so a node
    /// built out of tokens — a note, a chord, a repeat — reports no leading trivia even when
    /// its first token carries a newline and an indent. Everything computed from the node's
    /// own width was therefore right (Width subtracts the node's own trivia, which is none)
    /// and everything computed as an ADDRESS was one indent early.
    /// ★ This is why the first note of every indented line was un-clickable: the note node's
    /// leading trivia read 0, so its address came out at the line break in front of it
    /// (reported 2026-08-29, scratch/ベースタブLy/Walk.lys line 15, `ees,1`).
    /// </remarks>
    public int GetLeadingTriviaWidth()
    {
        if (FullWidth == 0)
            return 0;
        var node = this;
        while (node.LeadingTrivia is null)
        {
            GreenNode? first = null;
            for (int i = 0; i < node.SlotCount; i++)
            {
                var slot = node.GetSlot(i);
                if (slot is { FullWidth: > 0 })
                {
                    first = slot;
                    break;
                }
            }
            if (first is null)
                return 0;
            node = first;
        }
        return node.LeadingTriviaWidth;
    }
    public int TrailingTriviaWidth => TrailingTrivia?.FullWidth ?? 0;

    /// <summary>
    /// The trailing trivia width of the LAST terminal under this node — the mirror of
    /// <see cref="GetLeadingTriviaWidth"/>, and zero on a composite for the same reason.
    /// </summary>
    public int GetTrailingTriviaWidth()
    {
        if (FullWidth == 0)
            return 0;
        var node = this;
        while (node.TrailingTrivia is null)
        {
            GreenNode? last = null;
            for (int i = node.SlotCount - 1; i >= 0; i--)
            {
                var slot = node.GetSlot(i);
                if (slot is { FullWidth: > 0 })
                {
                    last = slot;
                    break;
                }
            }
            if (last is null)
                return 0;
            node = last;
        }
        return node.TrailingTriviaWidth;
    }

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