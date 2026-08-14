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

using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Syntax;

/// <summary>
/// An articulation mark: @staccato, @accent, etc.
/// </summary>
public sealed class ArticulationSyntax : SyntaxNode
{
    internal ArticulationSyntax(InternalSyntax.ArticulationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The leading <c>@</c> token.</summary>
    public SyntaxTokenNode AtToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The articulation name token.</summary>
    public SyntaxTokenNode NameToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>
    /// Gets the articulation type by resolving the <c>@name</c> text (and its
    /// abbreviations) against <see cref="ArticulationRegistry"/>. Returns
    /// <see cref="ArticulationType.None"/> for non-articulation marks (e.g. a
    /// music mark), which are then resolved by name downstream.
    /// </summary>
    public ArticulationType Type => ArticulationRegistry.Resolve(NameToken.Text);

    /// <summary>
    /// Forced placement from a <c>.up</c> / <c>.down</c> qualifier
    /// (<c>@staccato.up</c>): <c>true</c> = above, <c>false</c> = below,
    /// <c>null</c> = automatic (opposite the stem, the default).
    /// </summary>
    /// <remarks>
    /// Slot 3, not 2: slot 2 holds the qualifier's own <c>.</c>, which the tree
    /// has to keep or every position after it slides left by one character
    /// (see <c>ArticulationGreen</c>).
    /// </remarks>
    public bool? ForcedAbove => GetChild(3) is SyntaxTokenNode dir
        ? dir.Text == "up" ? true : dir.Text == "down" ? false : (bool?)null
        : null;
}

/// <summary>
/// A dynamic mark: \p, \f, \ff, etc.
/// </summary>
public sealed class DynamicSyntax : SyntaxNode
{
    internal DynamicSyntax(InternalSyntax.DynamicGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The leading <c>\</c> token.</summary>
    public SyntaxTokenNode BackslashToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The dynamic name token (e.g. <c>p</c>, <c>f</c>, <c>ff</c>).</summary>
    public SyntaxTokenNode DynamicToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>
    /// Forced placement from a <c>.up</c> / <c>.down</c> qualifier (<c>@f.up</c>):
    /// <c>true</c> = above, <c>false</c> = below, <c>null</c> = default (below).
    /// </summary>
    /// <remarks>Slot 3 — slot 2 is the qualifier's '.', see ArticulationSyntax.</remarks>
    public bool? ForcedAbove => GetChild(3) is SyntaxTokenNode dir
        ? dir.Text == "up" ? true : dir.Text == "down" ? false : (bool?)null
        : null;

    /// <summary>
    /// Gets the dynamic level.
    /// </summary>
    public DynamicLevel Level
    {
        get
        {
            // Proper dynamic tokens carry the level in their Kind; the pitch-token
            // 'f' and identifier-lexed accents fall back to text. Both sets live in
            // SyntaxFacts, so this velocity map and the parser's IsDynamicName gate
            // read the same single source and can't drift apart.
            var byKind = SyntaxFacts.DynamicLevelForKind(DynamicToken.Kind);
            if (byKind != DynamicLevel.None) return byKind;
            return SyntaxFacts.DynamicTextLevels.TryGetValue(DynamicToken.Text, out var byText)
                ? byText : DynamicLevel.None;
        }
    }

    /// <summary>
    /// Gets the MIDI velocity value for this dynamic.
    /// </summary>
    public int Velocity => (int)Level;
}

// ============================================================
// Tablature Nodes
// ============================================================

/// <summary>
/// Represents a string number annotation: \1, \2, \3, etc.
/// </summary>
public sealed partial class StringNumberAnnotationSyntax : SyntaxNode
{
    internal StringNumberAnnotationSyntax(StringNumberAnnotationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The string-number token (the full <c>\N</c> annotation, e.g. <c>\4</c>).</summary>
    public SyntaxTokenNode StringNumberToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// Gets the string number (1-based). The token text is the full <c>\N</c>
    /// annotation (e.g. "\4"), so the leading backslash is stripped before parsing.
    /// </summary>
    public int StringNumber => int.Parse(StringNumberToken.Text.TrimStart('\\'));
}
