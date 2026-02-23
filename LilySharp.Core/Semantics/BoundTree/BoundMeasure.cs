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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.BoundTree;

/// <summary>
/// A bound measure with resolved music content.
/// </summary>
public sealed record BoundMeasure : BoundMusic
{
    /// <summary>
    /// Creates a bound measure.
    /// </summary>
    public BoundMeasure(
        SyntaxNode? syntax,
        ImmutableArray<BoundMusic> items,
        BarlineType startBarline,
        BarlineType endBarline,
        string? sectionLabel)
    {
        _syntax = syntax;
        Items = items;
        StartBarline = startBarline;
        EndBarline = endBarline;
        SectionLabel = sectionLabel;
    }

    private readonly SyntaxNode? _syntax;

    /// <inheritdoc/>
    public override SyntaxNode? Syntax => _syntax;

    /// <summary>The music items in this measure.</summary>
    public ImmutableArray<BoundMusic> Items { get; }

    /// <summary>The barline at the start of this measure.</summary>
    public BarlineType StartBarline { get; }

    /// <summary>The barline at the end of this measure.</summary>
    public BarlineType EndBarline { get; }

    /// <summary>Optional section label for this measure.</summary>
    public string? SectionLabel { get; }
}
