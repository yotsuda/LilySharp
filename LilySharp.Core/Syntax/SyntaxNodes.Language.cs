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

/// <summary>The words a part sings, in its header: <c>part sop { lyrics words }</c>. It names a
/// track and defines nothing (docs/lyrics-singer-design.md §10).</summary>
public sealed class PartLyricsDeclarationSyntax : SyntaxNode
{
    internal PartLyricsDeclarationSyntax(PartLyricsDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>lyrics</c> word.</summary>
    public SyntaxTokenNode Keyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The track's name; zero-width where it is missing (an error).</summary>
    public SyntaxTokenNode NameToken => (SyntaxTokenNode)GetChild(1)!;
}

/// <summary>Who sings what, at the top level of a file: <c>singer [S] sings [NAME] [on PART]</c>
/// (owner's decisions 2026-11-10, docs/lyrics-singer-design.md).</summary>
public sealed class SingerDeclarationSyntax : SyntaxNode
{
    internal SingerDeclarationSyntax(SingerDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>singer</c> word.</summary>
    public SyntaxTokenNode Keyword => (SyntaxTokenNode)GetChild(0)!;

    private int SingsIndex
    {
        get
        {
            for (int i = 1; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Identifier, Text: "sings" })
                    return i;
            return -1;
        }
    }

    /// <summary>The <c>sings</c> word, or null where it is missing (an error).</summary>
    public SyntaxTokenNode? SingsKeyword => SingsIndex is var i and >= 0 ? (SyntaxTokenNode)GetChild(i)! : null;

    /// <summary>The written name, or null.</summary>
    public SyntaxTokenNode? NameToken
    {
        get
        {
            int end = SingsIndex >= 0 ? SingsIndex : SlotCount;
            for (int i = 1; i < end; i++)
                if (GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } t)
                    return t;
            return null;
        }
    }

    /// <summary>The <c>male</c> or <c>female</c> written before the name, or null.</summary>
    public SyntaxTokenNode? GenderToken =>
        GetChild(1) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } t && t.Text != "sings" ? t : null;

    /// <summary>Who sings, as the engine is told: the name, else the gender, else null (the part's
    /// gender decides).</summary>
    public string? Singer =>
        NameToken is { } name && Music.LyricSinger.ValueOf(name.Text) is { Length: > 0 } value ? value : GenderToken?.Text;

    /// <summary>The token naming the words, or null for the file's unnamed track.</summary>
    public SyntaxTokenNode? TrackToken =>
        SingsIndex is var i and >= 0 && GetChild(i + 1) is SyntaxTokenNode t && t.Text != "on" ? t : null;

    /// <summary>The track sung: its name, or the unnamed track's.</summary>
    public string TrackName => TrackToken?.Text ?? SyntaxFacts.UnnamedLyricsName;

    /// <summary>The token after <c>on</c>: the part or voice whose notes are sung, or null.</summary>
    public SyntaxTokenNode? OnToken
    {
        get
        {
            for (int i = SingsIndex + 1; i >= 1 && i + 1 < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode { Text: "on" })
                    return GetChild(i + 1) as SyntaxTokenNode;
            return null;
        }
    }
}
