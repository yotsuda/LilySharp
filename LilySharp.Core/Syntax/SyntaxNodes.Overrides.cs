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
/// Override declaration: override Grob.property = value
/// LILYPOND-REF: lily/context-property.cc (push/override)
/// </summary>
public sealed class OverrideDeclarationSyntax : SyntaxNode
{
    internal OverrideDeclarationSyntax(InternalSyntax.OverrideDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Grob type name (e.g., "Stem", "Beam").</summary>
    public SyntaxTokenNode GrobName => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>Property name (e.g., "length", "thickness").</summary>
    public SyntaxTokenNode PropertyName => (SyntaxTokenNode)GetChild(3)!;

    /// <summary>Value token.</summary>
    public SyntaxTokenNode ValueToken => (SyntaxTokenNode)GetChild(5)!;
}

/// <summary>
/// Revert declaration: revert Grob.property
/// LILYPOND-REF: lily/context-property.cc (pop/revert)
/// </summary>
public sealed class RevertDeclarationSyntax : SyntaxNode
{
    internal RevertDeclarationSyntax(InternalSyntax.RevertDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Grob type name.</summary>
    public SyntaxTokenNode GrobName => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>Property name.</summary>
    public SyntaxTokenNode PropertyName => (SyntaxTokenNode)GetChild(3)!;
}

/// <summary>
/// Once modifier: once override/revert ...
/// LILYPOND-REF: lily/context-property.cc (temporary_override/revert)
/// </summary>
public sealed class OnceModifierSyntax : SyntaxNode
{
    internal OnceModifierSyntax(InternalSyntax.OnceModifierGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The command modified by once (override or revert).</summary>
    public SyntaxNode Command => GetChild(1)!;
}
