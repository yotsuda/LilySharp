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

/// <summary>A text written as Markdown in a fence, at the top level, directly in a section, or
/// in a score (docs/markdown-text-design.md).</summary>
public sealed class MarkdownTextSyntax : SyntaxNode
{
    private FencedText? _value;

    internal MarkdownTextSyntax(MarkdownTextGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The fence, whole: one token.</summary>
    public SyntaxTokenNode Fence => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The opening line's words and the text between the fences.</summary>
    public FencedText Value => _value ??= FencedText.Read(Fence.Text);

    /// <summary>Whether the text stands under the last system (<c>end</c> on the opening line).</summary>
    public bool IsEnd
    {
        get
        {
            foreach (var (text, _) in Value.Words)
            {
                if (text == FencedText.EndWord)
                    return true;
            }
            return false;
        }
    }
}

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

/// <summary>One setting of a singer line's <c>with</c>: its name, where the name and the value
/// stand, and the value - <paramref name="Number"/>, or <paramref name="Word"/> (a word or a
/// quoted string, unquoted).</summary>
public readonly record struct SingerSetting(string Name, SyntaxTokenNode NameToken, SyntaxTokenNode ValueToken, double? Number, string? Word);

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
    public SyntaxTokenNode? GenderToken
    {
        get
        {
            var (first, second) = HeadWords;
            return second ?? (first != null && Music.LyricSinger.Genders.Contains(first.Text.ToLowerInvariant()) ? first : null);
        }
    }

    /// <summary>The singer's OWN name (<c>singer carol female "…" sings …</c>, owner's decision
    /// 2026-11-12), or null: the word before <c>sings</c> that is no gender, or the first of two.
    /// Lines of one name are one singer; a score prints what a named singer sings
    /// (<c>score { staff carol  lyrics carol }</c>).</summary>
    public SyntaxTokenNode? PersonToken
    {
        get
        {
            var (first, second) = HeadWords;
            return second != null || (first != null && !Music.LyricSinger.Genders.Contains(first.Text.ToLowerInvariant())) ? first : null;
        }
    }

    /// <summary>Where the head's <c>language</c> word stands (<c>singer alice female language
    /// japanese sings …</c>), or -1: the word before <c>sings</c> with a word after it that is not
    /// <c>sings</c> — the last two of the head.</summary>
    private int LanguageIndex
    {
        get
        {
            int sings = SingsIndex;
            return sings >= 3 && GetChild(sings - 2) is SyntaxTokenNode { Kind: SyntaxKind.Identifier, Text: Music.LyricLanguage.Keyword } ? sings - 2 : -1;
        }
    }

    /// <summary>The name of the language the singer sings (<c>language japanese</c> in the line's
    /// head, owner's decision 2026-11-12), or null: which of a part's several lyrics a line that
    /// names none sings.</summary>
    public SyntaxTokenNode? LanguageToken => LanguageIndex is var i and >= 0 ? (SyntaxTokenNode)GetChild(i + 1)! : null;

    /// <summary>The one or two bare words between <c>singer</c> and <c>sings</c> (or the voice's string).</summary>
    private (SyntaxTokenNode? First, SyntaxTokenNode? Second) HeadWords
    {
        get
        {
            // …up to the `language NAME` clause, whose two words are the clause's (LanguageToken).
            int stop = LanguageIndex is var l and >= 0 ? l : int.MaxValue;
            var first = 1 < stop && GetChild(1) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } a && a.Text != "sings" ? a : null;
            var second = first != null && 2 < stop && GetChild(2) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } b && b.Text != "sings" ? b : null;
            return (first, second);
        }
    }

    /// <summary>Who sings, as the engine is told: the name, else the gender, else null (the part's
    /// gender decides).</summary>
    public string? Singer =>
        NameToken is { } name && Music.LyricSinger.ValueOf(name.Text) is { Length: > 0 } value ? value : GenderToken?.Text;

    /// <summary>The token naming the words, or null for the file's unnamed track.</summary>
    public SyntaxTokenNode? TrackToken =>
        SingsIndex is var i and >= 0 && GetChild(i + 1) is SyntaxTokenNode t && t.Text != "on" && !ReferenceEquals(t, WithKeyword)
            && !IsPlaceWordAt(i + 1) ? t : null;

    /// <summary>Whether the token at <paramref name="i"/> begins one of the line's place clauses:
    /// <c>up</c> / <c>down</c> / <c>octave</c> with a number (or a sign) after it, <c>in</c> with a
    /// section's name — not the part of that name (<c>on in</c>) nor the track (<c>sings in on lead</c>).</summary>
    private bool IsPlaceWordAt(int i)
    {
        if (i <= SingsIndex || i + 1 >= SlotCount
            || GetChild(i) is not SyntaxTokenNode { Kind: SyntaxKind.Identifier } word
            || GetChild(i + 1) is not SyntaxTokenNode next
            || GetChild(i - 1) is SyntaxTokenNode { Text: "on" })
            return false;
        return word.Text switch
        {
            SyntaxFacts.SingerUpWord or SyntaxFacts.SingerDownWord => next.Kind == SyntaxKind.IntegerLiteral,
            SyntaxFacts.SingerOctaveWord => next.Kind is SyntaxKind.IntegerLiteral or SyntaxKind.Minus or SyntaxKind.Plus,
            SyntaxFacts.SingerInWord => next.Kind == SyntaxKind.Identifier && next.Text != "on",
            _ => false,
        };
    }

    private int PlaceIndex(params string[] words)
    {
        for (int i = SingsIndex + 1; i >= 1 && i + 1 < SlotCount; i++)
        {
            if (ReferenceEquals(GetChild(i), WithKeyword))
                break;
            if (IsPlaceWordAt(i) && Array.IndexOf(words, ((SyntaxTokenNode)GetChild(i)!).Text) >= 0)
                return i;
        }
        return -1;
    }

    /// <summary>The number of <c>up N</c> / <c>down N</c>, or null: the token the step keys move.</summary>
    public SyntaxTokenNode? IntervalToken =>
        PlaceIndex(SyntaxFacts.SingerUpWord, SyntaxFacts.SingerDownWord) is var i and >= 0 ? GetChild(i + 1) as SyntaxTokenNode : null;

    /// <summary>
    /// The interval the voice sings above its part's notes, in the key: 3 for <c>up 3</c> (a
    /// third above), −3 for <c>down 3</c>; 0 with neither, and for a number that is no interval.
    /// </summary>
    public int Interval
    {
        get
        {
            int i = PlaceIndex(SyntaxFacts.SingerUpWord, SyntaxFacts.SingerDownWord);
            if (i < 0 || GetChild(i + 1) is not SyntaxTokenNode number || !int.TryParse(number.Text, out int n))
                return 0;
            return ((SyntaxTokenNode)GetChild(i)!).Text == SyntaxFacts.SingerDownWord ? -n : n;
        }
    }

    /// <summary>The number of <c>octave N</c> (its digits; the sign is the token before), or null.</summary>
    public SyntaxTokenNode? OctaveToken
    {
        get
        {
            int i = PlaceIndex(SyntaxFacts.SingerOctaveWord);
            if (i < 0)
                return null;
            for (int k = i + 1; k < SlotCount && k <= i + 2; k++)
                if (GetChild(k) is SyntaxTokenNode { Kind: SyntaxKind.IntegerLiteral } number)
                    return number;
            return null;
        }
    }

    /// <summary>How many octaves above its part's notes the voice sings: −1 for <c>octave -1</c>; 0 with none.</summary>
    public int Octaves
    {
        get
        {
            int i = PlaceIndex(SyntaxFacts.SingerOctaveWord);
            if (i < 0 || OctaveToken is not { } number || !int.TryParse(number.Text, out int n))
                return 0;
            return GetChild(i + 1) is SyntaxTokenNode { Kind: SyntaxKind.Minus } ? -n : n;
        }
    }

    /// <summary>The <c>in</c> of <c>in SECTION …</c>, or null.</summary>
    public SyntaxTokenNode? InKeyword => PlaceIndex(SyntaxFacts.SingerInWord) is var i and >= 0 ? (SyntaxTokenNode)GetChild(i)! : null;

    /// <summary>The sections the voice sings in, as <c>in A B</c> names them; empty with no
    /// <c>in</c>: the whole part.</summary>
    public IReadOnlyList<SyntaxTokenNode> Sections
    {
        get
        {
            int i = PlaceIndex(SyntaxFacts.SingerInWord);
            if (i < 0)
                return [];
            var names = new List<SyntaxTokenNode>();
            for (int k = i + 1; k < SlotCount; k++)
            {
                if (GetChild(k) is not SyntaxTokenNode { Kind: SyntaxKind.Identifier } name || ReferenceEquals(name, WithKeyword))
                    break;
                names.Add(name);
            }
            return names;
        }
    }

    /// <summary>The track sung: its name, or the unnamed track's.</summary>
    public string TrackName => TrackToken?.Text ?? SyntaxFacts.UnnamedLyricsName;

    /// <summary>The <c>with</c> that begins the voice's settings, or null.</summary>
    public SyntaxTokenNode? WithKeyword
    {
        get
        {
            // The word, a setting's name after it - not the lyrics track or the part of that name
            // (`sings with on lead`, `on with`), which no name follows.
            for (int i = Math.Max(1, SingsIndex + 1); i + 1 < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Identifier, Text: SyntaxFacts.SingerWithWord } with
                    && GetChild(i - 1) is not SyntaxTokenNode { Text: "on" }
                    && GetChild(i + 1) is SyntaxTokenNode { Kind: SyntaxKind.Identifier, Text: not "on" })
                    return with;
            return null;
        }
    }

    /// <summary>
    /// How the voice sings, as the line writes it after <c>with</c>: each setting's name (its
    /// dotted words joined), the name's first token, and the value - a number, or the word or
    /// string written. In the order written; a name written twice is there twice.
    /// </summary>
    public IReadOnlyList<SingerSetting> Settings
    {
        get
        {
            if (WithKeyword is not { } with)
                return [];
            var list = new List<SingerSetting>();
            int i = 0;
            while (i < SlotCount && !ReferenceEquals(GetChild(i), with)) i++;
            i++;
            while (i < SlotCount && GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } first)
            {
                string name = first.Text;
                i++;
                while (i + 1 < SlotCount && GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Dot }
                    && GetChild(i + 1) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } more)
                {
                    name += "." + more.Text;
                    i += 2;
                }
                if (i >= SlotCount || GetChild(i) is not SyntaxTokenNode value)
                    break;
                bool minus = value.Kind == SyntaxKind.Minus;
                if (minus)
                {
                    if (++i >= SlotCount || GetChild(i) is not SyntaxTokenNode number)
                        break;
                    value = number;
                }
                i++;
                bool isNumber = value.Kind is SyntaxKind.IntegerLiteral or SyntaxKind.DecimalLiteral
                    && double.TryParse(value.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                double? numberValue = isNumber
                    ? (minus ? -1 : 1) * double.Parse(value.Text, System.Globalization.CultureInfo.InvariantCulture) : null;
                string? word = isNumber ? null
                    : value.Kind == SyntaxKind.StringLiteral ? Music.LyricSinger.ValueOf(value.Text) : value.Text;
                list.Add(new SingerSetting(name, first, value, numberValue, word));
            }
            return list;
        }
    }

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
