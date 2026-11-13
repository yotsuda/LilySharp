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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// The fenced texts a score prints (docs/markdown-text-design.md): the Markdown under its
/// title and the Markdown after its last system, each as written between the fences, with
/// where its first fence begins in the file (0 = none) for the preview's click.
/// </summary>
/// <param name="Before">The Markdown under the title, or null.</param>
/// <param name="BeforePosition">Where its first fence begins.</param>
/// <param name="After">The Markdown after the last system, or null.</param>
/// <param name="AfterPosition">Where its first fence begins.</param>
/// <param name="BeforeShape">How the text under the title is set: its opening line's
/// <c>xMargin</c>, <c>yMargin</c>, <c>center</c>, <c>box</c> (owner-approved 2026-11-13).</param>
/// <param name="AfterShape">The same for the text after the last system: each place has its own.</param>
/// <param name="BeforeMore">The blocks that follow the first under the title, each a later
/// fence for the place with a shape of its own (grammar evaluation 4, 2026-11-13), or null.</param>
/// <param name="AfterMore">The same after the last system.</param>
public readonly record struct ScoreTexts(
    string? Before, int BeforePosition, string? After, int AfterPosition,
    Syntax.TextShape BeforeShape = default, Syntax.TextShape AfterShape = default,
    TextBlocks? BeforeMore = null, TextBlocks? AfterMore = null)
{
    /// <summary>The blocks of one place, in written order: the first, then the later ones.</summary>
    /// <param name="before">Under the title (true) or after the last system.</param>
    public IEnumerable<TextBlock> Blocks(bool before)
    {
        string? first = before ? Before : After;
        if (!string.IsNullOrEmpty(first))
            yield return new TextBlock(first, before ? BeforePosition : AfterPosition, before ? BeforeShape : AfterShape);
        if ((before ? BeforeMore : AfterMore) is { } more)
        {
            foreach (var block in more.Items)
                yield return block;
        }
    }
}

/// <summary>One block of a place's text: the Markdown of a fence (and of the plain fences
/// that follow a plain one), where the fence begins, and the shape its opening line gives it.</summary>
/// <param name="Markdown">The text between the fences.</param>
/// <param name="Position">Where the fence begins in the file.</param>
/// <param name="Shape">Its opening line's <c>xMargin</c>, <c>yMargin</c>, <c>center</c>, <c>box</c>.</param>
public sealed record TextBlock(string Markdown, int Position, Syntax.TextShape Shape);

/// <summary>
/// The blocks after a place's first, compared by what they hold — <see cref="ScoreTexts"/>
/// is a key of the incremental compiler's, and an array compares by identity.
/// </summary>
public sealed class TextBlocks : IEquatable<TextBlocks>
{
    /// <summary>The blocks, in written order.</summary>
    public ImmutableArray<TextBlock> Items { get; }

    /// <summary>The list of <paramref name="items"/>.</summary>
    public TextBlocks(ImmutableArray<TextBlock> items) => Items = items;

    /// <inheritdoc/>
    public bool Equals(TextBlocks? other) => other is not null && Items.SequenceEqual(other.Items);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TextBlocks);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}