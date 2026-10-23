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

using System.Collections.Generic;
using System.Text;

namespace LilySharp.Core.Music;

/// <summary>
/// The PRINTED spellings of a chord symbol, read into the one entry grammar: the chart's
/// glyphs and letters for a quality (<c>CΔ7</c>, <c>Cø7</c>, <c>C°7</c>, <c>C+7</c>,
/// <c>CM7</c>, <c>Cmi7</c>, <c>C-7</c>, <c>Co7</c>, <c>C07</c>, <c>C7no3</c>), the flat and
/// sharp GLYPHS wherever <c>b</c> and <c>#</c> are written (<c>B♭7/D♭</c>), and the
/// PARENTHESIZED alteration list a chart prints after the quality —
/// <c>C7(b9, #11)</c>, <c>Cm7(b5)</c>, <c>C(add9)</c>, <c>C7(omit3)</c>, <c>Cm(maj7)</c>,
/// <c>C7(9)</c>. Every spelling is rewritten to the CANONICAL quality
/// (<see cref="ChordFormula"/>'s grammar and <see cref="ChordQualityRegistry"/>'s tokens),
/// so one tone set has one structure, one printed name and one twin spelling however it
/// was written.
/// </summary>
/// <remarks>
/// <para>
/// Owner's decisions 2026-10-23 (session 967, HANDOFF §2 E ⑹ "a+e"): ⑴ spaces (and commas)
/// are allowed inside the parentheses — the list is one word of the symbol, so
/// <c>@chord(C7(b9, #11) x32310)</c> reads as a chords row's <c>C7(b9, #11)(x32310)</c> does;
/// the <c>(</c> is glued to the symbol as a shape group's is. ⑵ A BARE number in the
/// parentheses is the EXTENSION when the quality already has a seventh (<c>C7(9)</c> = C9,
/// <c>C7(13)</c> = C13, <c>Cmaj7(9)</c> = Cmaj9), else an ADDED tone (<c>C(9)</c> = Cadd9,
/// <c>C6(9)</c> = C69). ⑶ The page prints the canonical Ignatzek name, not the spelling
/// (<c>Cø7</c> prints as <c>Cm7♭5</c> under <c>chordQualities words</c>, <c>Cø7</c> under
/// <c>symbols</c>, exactly as <c>Cm7-5</c> does). ⑷ <c>Δ</c>/<c>△</c> is the major seventh,
/// <c>ø</c> the half-diminished seventh, <c>°</c> the diminished, <c>+</c> the augmented.
/// ⑸ <c>-</c> straight after the root is the MINOR third (<c>C-7</c> = Cm7) — but <c>C-5</c>
/// is refused and names <c>C(b5)</c>: a "minor fifth" is nobody's reading, and until this
/// session <c>-5</c> there was the flat five. ⑹ The letter aliases <c>M7</c>/<c>Ma7</c>/
/// <c>Maj7</c>, <c>mi</c>/<c>Mi</c>/<c>MI</c>, <c>o</c> (°), <c>0</c>/<c>Ø</c> (ø) and
/// <c>no3</c>/<c>no5</c> (omit); and <c>♭</c>/<c>♯</c> as the glyph forms of <c>b</c>/<c>#</c>.
/// </para>
/// <para>
/// WHAT STAYS REFUSED: a <c>b</c> or <c>#</c> GLUED after a letter or digit inside the quality
/// (<c>C7b9</c>, <c>C7#9</c>) — after a letter the two belong to the root and the bass alone
/// (what keeps <c>Bb5</c> B♭'s power chord), and the rule is kept one rule rather than two:
/// write <c>C7(b9)</c> or <c>C7-9</c>. <c>+</c> after the root followed by a DIGIT keeps its
/// older reading as an altered tension (<c>C+5</c> = Caug, <c>C+9</c> = C with a ♯9), and
/// is the augmented triad only when it ends the quality or a seventh follows (<c>C+</c>,
/// <c>C+7</c>, <c>C+maj7</c>).
/// </para>
/// <para>
/// LILYSHARP-OWN: LilyPond's chordmode reads <c>c:m7.5-</c> and names from the tones; it
/// reads no printed spelling. Departs from LilyPond in reading them at all; goes away for a
/// book that writes the canonical spellings; observed by the names a chart's symbols print.
/// </para>
/// </remarks>
public static class ChordSymbolSpelling
{
    /// <summary>The flat and sharp glyphs as the ASCII letters the entry grammar reads —
    /// <c>B♭7/D♭</c> is <c>Bb7/Db</c> (owner's decision ⑹ above).</summary>
    public static string Ascii(string s)
        => s.IndexOfAny(['♭', '♯']) < 0 ? s : s.Replace('♭', 'b').Replace('♯', '#');

    /// <summary>A character the lexer makes a BadToken of that a chord symbol may hold:
    /// <c>#</c> (the sharp), <c>♭</c>/<c>♯</c> (its glyphs), <c>°</c> (diminished) and
    /// <c>△</c> (the major seventh). <c>Δ</c>, <c>ø</c> and <c>Ø</c> are letters and lex
    /// into the identifier. Tolerated in a chords body, a <c>@chord(…)</c> argument and a
    /// layout's shape table; flagged everywhere else as before.</summary>
    public static bool IsSymbolGlyph(string text) => text is "#" or "♭" or "♯" or "°" or "△";

    /// <summary>
    /// Takes the parenthesized alteration list off a symbol: <c>C7(b9, #11)/E</c> gives
    /// <c>C7/E</c> and the words <c>b9</c>, <c>#11</c> (split on whitespace and commas);
    /// a symbol with no parentheses is itself with no words. False, naming why, for an
    /// unclosed <c>(</c>, a second list, or anything but a <c>/</c> bass after the <c>)</c>.
    /// </summary>
    public static bool SplitAlterations(string s, out string symbol, out List<string> words, out string? refusal)
    {
        symbol = s;
        words = [];
        refusal = null;
        int open = s.IndexOf('(');
        if (open < 0)
            return true;
        int close = s.IndexOf(')', open + 1);
        if (close < 0)
        {
            refusal = $"'{s}': the '(' is not closed - write the alterations as C7(b9, #11).";
            return false;
        }
        if (s.IndexOf('(', close + 1) >= 0)
        {
            refusal = $"'{s}': one parenthesized list - write every alteration in it, C7(b9, #11).";
            return false;
        }
        string after = s[(close + 1)..];
        if (after.Length > 0 && after[0] != '/')
        {
            refusal = $"'{s}': only a '/' bass may follow the ')' - put '{after}' inside the parentheses.";
            return false;
        }
        symbol = s[..open] + after;
        foreach (var word in s[(open + 1)..close].Split([' ', '\t', ','], System.StringSplitOptions.RemoveEmptyEntries))
            words.Add(word);
        return true;
    }

    /// <summary>Whether every word of a glued <c>( )</c> group after a chords-row symbol is
    /// an alteration — what tells <c>C7(b9 #11)</c> from the shape group <c>C7(x32310)</c>:
    /// a shape is <c>x</c>/<c>o</c>/digits four or more long or holds a <c>-</c> between
    /// frets, a tuning word is a name; an alteration starts with <c>b</c>, <c>#</c>, <c>+</c>,
    /// <c>-</c>, <c>add</c>, <c>omit</c>/<c>no</c>, <c>sus</c>, <c>alt</c>, <c>maj</c> or is a
    /// bare number of one or two digits. An empty group is no alteration list.</summary>
    public static bool IsAlterationList(IReadOnlyList<string> words)
    {
        if (words.Count == 0)
            return false;
        foreach (var w in words)
            if (Classify(Ascii(w)) is null)
                return false;
        return true;
    }

    // One alteration word, read: an added/altered/removed step, a suspension, `alt`, the
    // major-seventh word with its extension, or a bare extension number.
    private enum Kind { Tension, Add, Omit, Sus, Alt, Maj, Bare }

    private readonly record struct Alteration(Kind Kind, int Number, int Sign);

    private static Alteration? Classify(string w)
    {
        if (w.Length == 0)
            return null;
        char c = w[0];
        if (c is 'b' or '#' or '+' or '-')
            return Number(w[1..]) is int n && n is 5 or 9 or 11 or 13
                ? new Alteration(Kind.Tension, n, c is 'b' or '-' ? -1 : 1)
                : null;
        if (w.StartsWith("add", System.StringComparison.Ordinal))
            return Number(w[3..]) is int a && a is 2 or 4 or 6 or 9 or 11 or 13 ? new Alteration(Kind.Add, a, 0) : null;
        if (w.StartsWith("omit", System.StringComparison.Ordinal))
            return Number(w[4..]) is int o && o is 3 or 5 ? new Alteration(Kind.Omit, o, 0) : null;
        if (w.StartsWith("no", System.StringComparison.Ordinal))
            return Number(w[2..]) is int o2 && o2 is 3 or 5 ? new Alteration(Kind.Omit, o2, 0) : null;
        if (w is "sus" or "sus4")
            return new Alteration(Kind.Sus, 4, 0);
        if (w == "sus2")
            return new Alteration(Kind.Sus, 2, 0);
        if (w == "alt")
            return new Alteration(Kind.Alt, 0, 0);
        if (TakeMajorWord(w, 0) is int afterMaj)
        {
            string tail = w[afterMaj..];
            if (tail.Length == 0)
                return new Alteration(Kind.Maj, 7, 0);
            return Number(tail) is int m && m is 7 or 9 or 11 or 13 ? new Alteration(Kind.Maj, m, 0) : null;
        }
        return Number(w) is int bare && bare is 2 or 4 or 6 or 7 or 9 or 11 or 13 ? new Alteration(Kind.Bare, bare, 0) : null;
    }

    private static int? Number(string s)
    {
        if (s.Length is 0 or > 2)
            return null;
        int n = 0;
        foreach (char ch in s)
        {
            if (!char.IsAsciiDigit(ch))
                return null;
            n = n * 10 + (ch - '0');
        }
        return n;
    }

    // The major-seventh words, longest first: the index after one at `i`, or null.
    private static readonly string[] MajorWords = ["maj", "Maj", "MAJ", "Ma", "MA", "M", "Δ", "△"];

    private static int? TakeMajorWord(string s, int i)
    {
        foreach (var w in MajorWords)
            if (string.CompareOrdinal(s, i, w, 0, w.Length) == 0)
                return i + w.Length;
        return null;
    }

    /// <summary>
    /// The canonical quality of a written one — <paramref name="head"/> is what follows the
    /// root (<c>Δ7</c>, <c>-7</c>, <c>ø</c>, <c>7</c>), <paramref name="alterations"/> the
    /// words of its parenthesized list — in <see cref="ChordFormula"/>'s grammar, for
    /// <see cref="ChordQualityRegistry.TryResolve(string?, out ChordQuality, out ChordFormula?)"/>.
    /// False, naming the fix, for a spelling the grammar refuses on purpose (<c>C-5</c>,
    /// <c>C7b9</c>, a word in the list that is no alteration).
    /// </summary>
    public static bool TryCanonicalQuality(string head, IReadOnlyList<string> alterations,
        out string canonical, out string? refusal)
    {
        canonical = "";
        refusal = null;
        head = Ascii(head);
        int i = 0;
        bool Take(string word)
        {
            if (string.CompareOrdinal(head, i, word, 0, word.Length) != 0)
                return false;
            i += word.Length;
            return true;
        }
        bool TakeAny(params string[] words)
        {
            foreach (var w in words)
                if (Take(w))
                    return true;
            return false;
        }

        // ⑴ The quality word, in every spelling: the canonical one, the chart's letters, the
        // glyphs. `maj` first (its `m` is not the minor's), then the minor's spellings, then
        // the rest. `o` is the diminished circle unless it opens `omit`.
        string word = "";
        bool maj = false, halfDiminished = false;
        if (head.StartsWith('-'))
        {
            if (head.Length > 1 && head[1] == '5')
            {
                refusal = $"'{head}': '-' after the root is the minor third (C-7 is Cm7) - for the flat five write C(b5).";
                return false;
            }
            word = "m";
            i = 1;
        }
        else if (TakeAny("maj", "Maj", "MAJ"))
            maj = true;
        else if (TakeAny("min", "Min", "MIN", "mi", "Mi", "MI"))
            word = "m";
        else if (TakeAny("dim", "Dim", "DIM", "°")
                 || (!head.StartsWith("omit", System.StringComparison.Ordinal) && Take("o")))
            word = "dim";
        else if (TakeAny("aug", "Aug", "AUG"))
            word = "aug";
        else if (TakeAny("ø", "Ø", "0"))
        {
            // The half-diminished seventh: its own `7` is written or not (Cø, Cø7).
            word = "m";
            halfDiminished = true;
            Take("7");
        }
        else if (TakeAny("Δ", "△", "Ma", "MA", "M"))
            maj = true;
        else if (Take("m"))
            word = "m";
        else if (head.StartsWith('+') && IsAugmentedPlus(head))
        {
            word = "aug";
            i = 1;
        }
        // ⑵ `maj` after the minor or augmented word, in every spelling (mM7, mΔ7, -Δ, augM7).
        if (!maj && word is "m" or "aug" && TakeMajorWord(head, i) is int afterMaj)
        {
            maj = true;
            i = afterMaj;
        }

        // ⑶ The extension, longest first (as ChordFormula reads it), then the modifiers.
        int ext = 0;
        foreach (var (text, value) in new[] { ("69", 69), ("13", 13), ("11", 11), ("9", 9), ("7", 7), ("6", 6), ("5", 5) })
            if (Take(text))
            {
                ext = value;
                break;
            }
        var mods = new StringBuilder(head[i..].Replace("no3", "omit3").Replace("no5", "omit5"));
        if (halfDiminished)
        {
            if (ext == 0)
                ext = 7;
            mods.Append("-5");
        }
        foreach (char ch in mods.ToString())
            if (ch is 'b' or '#')
            {
                refusal = "An altered tension is spelled with '+' or '-' (C7-9, C7+11) or in parentheses "
                    + "(C7(b9), C7(#11)) - '#' and 'b' after a letter belong to the root and the bass alone, "
                    + "which is what keeps 'Bb9' unambiguous.";
                return false;
            }

        // ⑷ The parenthesized list.
        foreach (var raw in alterations)
        {
            string w = Ascii(raw);
            if (Classify(w) is not { } a)
            {
                refusal = $"'{w}' is not an alteration - inside the parentheses write b9, #11, b5, #5, "
                    + "add9, omit3 (no3), sus4, maj7 or a bare 9, 11 or 13.";
                return false;
            }
            switch (a.Kind)
            {
                case Kind.Tension:
                    mods.Append(a.Sign < 0 ? '-' : '+').Append(a.Number);
                    break;
                case Kind.Add:
                    mods.Append("add").Append(a.Number);
                    break;
                case Kind.Omit:
                    mods.Append("omit").Append(a.Number);
                    break;
                case Kind.Sus:
                    mods.Append("sus").Append(a.Number);
                    break;
                case Kind.Alt:
                    mods.Append("alt");
                    break;
                case Kind.Maj:
                    maj = true;
                    if (ext == 6)
                    {
                        mods.Append("add6");
                        ext = 0;
                    }
                    if (a.Number > ext)
                        ext = a.Number;
                    break;
                case Kind.Bare:
                    // Owner's decision ⑵: the extension when a seventh is there, else an added tone.
                    if (a.Number == 7)
                    {
                        if (ext == 6)
                            mods.Append("add6");
                        if (ext is 0 or 6)
                            ext = 7;
                    }
                    else if (a.Number is 9 or 11 or 13)
                    {
                        if (maj || ext >= 7)
                            ext = System.Math.Max(ext, a.Number);
                        else if (ext == 6 && a.Number == 9)
                            ext = 69;
                        else
                        {
                            if (ext == 6)
                            {
                                mods.Append("add6");
                                ext = 0;
                            }
                            mods.Append("add").Append(a.Number);
                        }
                    }
                    else
                        mods.Append("add").Append(a.Number);
                    break;
            }
        }

        var sb = new StringBuilder(word);
        if (maj)
            sb.Append("maj");
        if (ext != 0)
            sb.Append(ext);
        sb.Append(mods);
        canonical = sb.ToString();
        return true;
    }

    // `+` after the root is the augmented triad when nothing, a seventh, or a major-seventh
    // word follows (C+, C+7, C+maj7); before any other digit it is the older altered tension
    // (C+5, C+9, C+11 — ChordFormula's `+N`).
    private static bool IsAugmentedPlus(string head)
        => head.Length == 1 || head[1] == '7' || TakeMajorWord(head, 1) is not null;
}
