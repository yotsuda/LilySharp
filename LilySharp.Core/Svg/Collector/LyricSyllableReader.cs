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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Shared reading of lyric-block tokens, used by BOTH lyric paths so they classify
/// markers identically: <see cref="LyricCollector"/> (note-bound, one syllable per
/// note) and <c>MeasureCollector.CollectLyricsRow</c> (an independent lyrics row,
/// syllables spread evenly across the bar). Each path still ASSEMBLES its result
/// differently — a row has no notes to hold, so it draws no melisma slots, and the
/// two paths intentionally differ on a trailing-hyphen word ("Mu-": note-bound
/// keeps it literal, the row strips it to a centered hyphen) — but the decision of
/// "what role does this token play" lives here in one place.
/// </summary>
internal static class LyricSyllableReader
{
    /// <summary>The role a raw lyric token plays.</summary>
    internal enum Marker
    {
        /// <summary>A sung syllable (its text is the word to render).</summary>
        Syllable,
        /// <summary>A sung syllable whose word ends in a hyphen (<c>Mu-</c>): it is
        /// rendered without the trailing dash and carries a hyphen connector to the
        /// next syllable. Use <see cref="TrimHyphenWord"/> for the display text.</summary>
        HyphenWord,
        /// <summary>A standalone hyphen marker (<c>--</c> / <c>-</c>): the previous
        /// syllable continues into the next as one word.</summary>
        Hyphen,
        /// <summary>An extender marker (<c>__</c> / <c>_</c>): a held note with a
        /// drawn extender line over it.</summary>
        Extender,
        /// <summary>A plain melisma (<c>~</c>): a held note with NO line.</summary>
        Melisma,
        /// <summary>A barline (<c>|</c>, <c>||</c>, <c>|.</c>, …): a measure boundary.</summary>
        Barline,
    }

    /// <summary>
    /// Reads a lyric measure's child node (a syllable or the trailing barline token)
    /// to its display text — string-literal quotes stripped — and the source byte
    /// offset of the word itself (Span.Start excludes leading trivia), so an emitted
    /// data-pos lands on the word and a preview click jumps to it, not the space
    /// before. Returns an empty string for a node with no token.
    /// </summary>
    public static (string Text, int Position) ReadToken(SyntaxNode node)
    {
        SyntaxTokenNode? tok = node.Kind == SyntaxKind.LyricSyllable
            ? node.GetChild(0) as SyntaxTokenNode
            : node as SyntaxTokenNode;
        if (tok == null)
            for (int i = 0; i < node.SlotCount && tok == null; i++)
                tok = node.GetChild(i) as SyntaxTokenNode;
        if (tok == null)
            return ("", node.Span.Start);

        var text = tok.Text;
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text.Substring(1, text.Length - 2);
        return (text, tok.Span.Start);
    }

    /// <summary>Classifies a raw lyric token into its <see cref="Marker"/> role.</summary>
    public static Marker Classify(string text) =>
        IsBarline(text) ? Marker.Barline
        : text is "--" or "-" ? Marker.Hyphen
        : text is "__" or "_" ? Marker.Extender
        : text is "~" ? Marker.Melisma
        : text.EndsWith('-') ? Marker.HyphenWord
        : Marker.Syllable;

    /// <summary>The display text of a <see cref="Marker.HyphenWord"/> — the word
    /// without its trailing hyphen(s).</summary>
    public static string TrimHyphenWord(string text) => text.TrimEnd('-');

    /// <summary>Display form of a sung syllable: a '~' INSIDE the word is a
    /// lyric ELISION (two vowels sung on one note) and prints as the undertie
    /// ‿ (U+203F). A standalone '~' is a melisma and never reaches here.
    /// LILYPOND-REF: lyric tie — "va~ga" joins with the elision tie.</summary>
    public static string DisplaySyllable(string text) => text.Replace('~', '‿');

    /// <summary>True for a barline glyph (<c>|</c>, <c>||</c>, <c>|.</c>, <c>|:</c>,
    /// <c>:|</c>, <c>.|</c>) — a run of only bar/dot/colon characters that contains
    /// at least one bar. Both paths treat it as a measure boundary, never a word.</summary>
    public static bool IsBarline(string text)
    {
        bool sawBar = false;
        foreach (char c in text)
        {
            if (c == '|') sawBar = true;
            else if (c != '.' && c != ':') return false;
        }
        return sawBar;
    }

    /// <summary>
    /// True for a lyric measure that is nothing but a lone bare <c>|</c> — the
    /// parse of a run that OPENS with a barline. Under the bare-barline rule
    /// (GRAMMAR.md "BARE-BARLINE SEMANTICS", the same rule music follows) that
    /// leading bar only ANCHORS the run's start and creates NO measure, so both
    /// lyric paths skip exactly one: <c>| きら | ひかる |</c> == <c>きら | ひかる</c>.
    /// An EMPTY leading bar is the explicit <c>| |</c> pair, whose second parsed
    /// measure survives the skip. A typed leading bar (<c>||</c>, <c>|:</c>) or a
    /// marker-only bar (<c>| ~ |</c>) is NOT an anchor.
    /// </summary>
    public static bool IsLeadingAnchor(SyntaxNode measure)
    {
        if (measure.Kind != SyntaxKind.LyricMeasure)
            return false;
        bool sawBar = false;
        for (int i = 0; i < measure.SlotCount; i++)
        {
            var child = measure.GetChild(i);
            if (child == null)
                continue;
            var (text, _) = ReadToken(child);
            if (string.IsNullOrEmpty(text))
                continue;
            if (text == "|" && !sawBar)
                sawBar = true;
            else
                return false; // a syllable, a marker, or a typed/second bar
        }
        return sawBar;
    }
}
