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
    public ArticulationType Type => ArticulationRegistry.Resolve(Name);

    /// <summary>The articulation name (the text after <c>@</c>) off the green token — the same
    /// string <see cref="NameToken"/>'s <c>Text</c> is, with no token red built for it
    /// (session 520).</summary>
    public string Name => Green.GetSlot(1)!.Text;

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
    public bool? ForcedAbove => Green.GetSlot(3) is { IsToken: true } dir
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
    /// Forced placement from a <c>.up</c> / <c>.down</c> qualifier (<c>@f.up</c>,
    /// <c>@cresc.down</c>): <c>true</c> = above, <c>false</c> = below, <c>null</c> = the
    /// staff's side (below; above on a staff with lyrics under it).
    /// </summary>
    /// <remarks>Slot 3 — slot 2 is the qualifier's '.', see ArticulationSyntax. After a
    /// <c>.subito</c> (slots 2-3) the placement is slot 5 (<c>@p.subito.up</c>).</remarks>
    public bool? ForcedAbove => PlacementOf(Green.GetSlot(3) is { IsToken: true, Text: "subito" }
        ? Green.GetSlot(5)
        : IsEditorial ? (Green.SlotCount > 8 ? Green.GetSlot(8) : null) : Green.GetSlot(3));

    /// <summary>The word that makes a level an editor's: <c>@p(editorial)</c>.</summary>
    public const string EditorialWord = "editorial";

    /// <summary><c>@p(editorial)</c> (owner-approved 2026-11-13, grammar evaluation 3): the
    /// level printed in parentheses, "(p)" — an editor's dynamic, not the composer's — and
    /// sounding as the plain one. Slots 4-6 hold the parentheses and the word (the placement
    /// after it, <c>@p(editorial).up</c>, slots 7-8).</summary>
    public bool IsEditorial => Green.SlotCount > 5 && Green.GetSlot(4) is { IsToken: true, Text: "(" };

    private static bool? PlacementOf(InternalSyntax.GreenNode? dir) => dir is { IsToken: true }
        ? dir.Text == "up" ? true : dir.Text == "down" ? false : (bool?)null
        : null;

    /// <summary><c>@p.subito</c>: the dynamic is printed with an italic "sub." before it
    /// (LilyPond's <c>make-dynamic-script</c> of "sub." and the dynamic) and sounds as the
    /// plain one.</summary>
    public bool IsSubito => Green.GetSlot(3) is { IsToken: true, Text: "subito" };

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
    /// <remarks>LILYSHARP-OWN: a note struck at <c>@niente</c> plays as <c>@pppp</c> (owner's
    /// decision 2026-11-10). A hairpin is not played, so at velocity 1 the fade was no fade: the
    /// notes under it kept their level and the one written <c>@niente</c>, in both hands of a
    /// grand staff, made no sound (Dorian Mist's last chord). A player fading to nothing still
    /// plays that note.</remarks>
    public int Velocity => Level == DynamicLevel.Niente ? (int)DynamicLevel.PPPP : (int)Level;
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
    public int StringNumber => int.Parse(Green.GetSlot(0)!.Text.TrimStart('\\'));
}
