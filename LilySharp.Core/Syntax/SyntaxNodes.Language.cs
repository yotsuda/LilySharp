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

using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Syntax;

/// <summary>The language the file's lyrics are written in: <c>language japanese</c>
/// (<see cref="Music.LyricLanguage"/>).</summary>
public sealed class LanguageDeclarationSyntax : SyntaxNode
{
    internal LanguageDeclarationSyntax(LanguageDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The token naming the language.</summary>
    public SyntaxTokenNode ValueToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>The language as written.</summary>
    public string Language => ValueToken.Text;
}
