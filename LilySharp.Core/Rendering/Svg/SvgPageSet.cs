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

namespace LilySharp.Core.Rendering.Svg;

/// <summary>How one page of a <see cref="SvgPageSet"/> compares with the page at the same
/// index of the session's previous render (see the set's remarks).</summary>
public enum SvgPageChange
{
    /// <summary>The page's text differs beyond its source offsets (or there was no previous
    /// page at this index): the viewer needs the page's markup.</summary>
    Changed,
    /// <summary>Byte-identical to the previous render's page — the same string instance.</summary>
    Same,
    /// <summary>The same text, except that every <c>data-pos</c>/<c>data-alt</c> number is
    /// the previous one mapped through <see cref="SvgPageSet.Window"/>: a viewer holding the
    /// previous page reproduces this one by shifting its offsets.</summary>
    Shifted,
}

/// <summary>The edit window between two renders' source texts, in old-text coordinates —
/// the arithmetic the collect splice and the fragment replay shift burned offsets by
/// (<c>CollectResumePlanner.ComputeWindow</c>): an offset below <see cref="Prefix"/> is
/// unchanged, one at or after <see cref="SuffixStart"/> moves by <see cref="Delta"/>, one
/// inside the window has no image.</summary>
public readonly record struct SvgEditWindow(int Prefix, int SuffixStart, int Delta)
{
    /// <summary>The window of a render with no edit before it: every offset maps to itself.</summary>
    public static SvgEditWindow None(int textLength) => new(textLength, textLength, 0);

    /// <summary>Maps one recorded source offset to the current text, or false when it lies
    /// inside the window (undefined shift). ONE spelling for the fragment memo's slot
    /// replay (<see cref="SvgSystemFragmentCache"/>) and the page classification
    /// (<see cref="SvgPageSet"/>) — and the rule the preview's page applies when it is
    /// told a page is <see cref="SvgPageChange.Shifted"/>.</summary>
    public bool TryMap(int offset, out int mapped)
    {
        if (offset < Prefix)
        {
            mapped = offset;
            return true;
        }
        if (offset >= SuffixStart)
        {
            mapped = offset + Delta;
            return true;
        }
        mapped = default;
        return false;
    }
}

/// <summary>
/// An SVG document as the preview ships it: the header (the root tag, the style and the
/// page background), one string per page, and the closing tag — joined, exactly the text
/// <see cref="SvgDocumentContext.ToSvg"/> returns. Each page is classified against the
/// page at the same index of the session's PREVIOUS render (R13⒝, session 404): the
/// language server sends a viewer that holds the previous picture only the pages that
/// <see cref="SvgPageChange.Changed"/>, where it used to send the whole document — measured
/// on a 1000-bar book, 3.6–12 MB of JSON per keystroke, of which one page (≈200–700 KB)
/// had changed.
/// </summary>
/// <remarks>
/// <para>
/// THE CLASSIFICATION IS A COMPARISON, NOT AN INFERENCE: a page is
/// <see cref="SvgPageChange.Same"/> only when its text equals the previous page's, and
/// <see cref="SvgPageChange.Shifted"/> only when a character-by-character scan finds the
/// two texts equal everywhere but in their <c>data-pos</c>/<c>data-alt</c> numbers, each of
/// which is the old number mapped through <see cref="Window"/>
/// (<see cref="SvgEditWindow.TryMap"/>). Nothing about the fragment memo or the layout
/// reuse is assumed, so a page the memo re-drew to the same bytes is still Same, and a
/// page whose drawing changed is Changed however it was produced. The scan costs one pass
/// over the changed pages' text (measured: 0.3–1.5 ms for a whole 3–10 MB document).
/// </para>
/// <para>
/// A viewer that is told a page is Shifted reproduces it by mapping every offset attribute
/// of the page it holds through the same window — the certificate is that the mapped
/// previous text IS the new text, which <c>SvgPageSetTests</c> pins by re-stamping the old
/// page and comparing.
/// </para>
/// <para>
/// The page unit is the viewer's: in the interactive document every page — a single one
/// included — is wrapped in <c>&lt;g class="page" …&gt;</c>, and the page string runs from
/// that tag through its <c>&lt;/g&gt;</c> and the newline after it. In the export document
/// (no wrapper on a single page) the one page is the body between header and closing tag.
/// </para>
/// </remarks>
public sealed class SvgPageSet
{
    internal SvgPageSet(string head, ImmutableArray<string> pages,
        ImmutableArray<SvgPageChange> changes, string tail, SvgEditWindow window)
    {
        Head = head;
        Pages = pages;
        Changes = changes;
        Tail = tail;
        Window = window;
    }

    /// <summary>The document before its first page (xml prolog, root tag, style, background).</summary>
    public string Head { get; }

    /// <summary>The pages, in order; a <see cref="SvgPageChange.Same"/> page is the previous
    /// set's string instance.</summary>
    public ImmutableArray<string> Pages { get; }

    /// <summary>Per page, how it compares with the previous render (all
    /// <see cref="SvgPageChange.Changed"/> when there was none, or its page count differs).</summary>
    public ImmutableArray<SvgPageChange> Changes { get; }

    /// <summary>The document after its last page (the closing tag).</summary>
    public string Tail { get; }

    /// <summary>The window the <see cref="SvgPageChange.Shifted"/> pages' offsets moved by.</summary>
    public SvgEditWindow Window { get; }

    /// <summary>The whole document, byte for byte what the one-string render returns.</summary>
    public string ToSvg()
    {
        int total = Head.Length + Tail.Length;
        foreach (var p in Pages) total += p.Length;
        return string.Create(total, this, static (span, set) =>
        {
            int at = 0;
            set.Head.AsSpan().CopyTo(span);
            at += set.Head.Length;
            foreach (var p in set.Pages)
            {
                p.AsSpan().CopyTo(span[at..]);
                at += p.Length;
            }
            set.Tail.AsSpan().CopyTo(span[at..]);
        });
    }

    /// <summary>
    /// True when <paramref name="current"/> is <paramref name="previous"/> with every
    /// <c>data-pos</c>/<c>data-alt</c> number mapped through <paramref name="window"/> and
    /// nothing else different. A number the window has no image for, a number missing where
    /// the previous text had one, or any other character difference is false. The two are
    /// walked in lockstep; the tokens are the exact attribute spellings the drawing context
    /// emits (<c>SvgDrawingContext.AppendSource</c>).
    /// </summary>
    internal static bool SameModuloWindow(string previous, string current, SvgEditWindow window)
    {
        const string Pos = " data-pos=\"";
        const string Alt = " data-alt=\"";
        int i = 0, j = 0;
        while (i < previous.Length && j < current.Length)
        {
            char c = previous[i];
            if (c != current[j])
                return false;
            i++;
            j++;
            if (c != '"' || i < Pos.Length)
                continue;
            var opener = previous.AsSpan(i - Pos.Length, Pos.Length);
            bool isAlt = opener.SequenceEqual(Alt);
            if (!isAlt && !opener.SequenceEqual(Pos))
                continue;
            // The number(s): one for data-pos, a space-separated list for data-alt. Each
            // previous number must map, and map to exactly the current number.
            while (true)
            {
                if (!ReadInt(previous, ref i, out int was) || !ReadInt(current, ref j, out int now))
                    return false;
                if (!window.TryMap(was, out int mapped) || mapped != now)
                    return false;
                if (!isAlt || i >= previous.Length || previous[i] != ' ' || j >= current.Length || current[j] != ' ')
                    break;
                i++;
                j++;
            }
        }
        return i == previous.Length && j == current.Length;
    }

    private static bool ReadInt(string s, ref int i, out int value)
    {
        int start = i;
        bool negative = i < s.Length && s[i] == '-';
        if (negative)
            i++;
        long acc = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            acc = acc * 10 + (s[i] - '0');
            i++;
        }
        value = (int)(negative ? -acc : acc);
        return i > start + (negative ? 1 : 0);
    }
}
