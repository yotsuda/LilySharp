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

using LilySharp.Core.Parser;
using LilySharp.Core.Syntax;

namespace LilySharp.Lsp;

/// <summary>
/// The trees of the texts the server MAKES of a document — its text with its <c>using</c>s
/// resolved, that text with the singers' parts written after it — parsed from the tree of the
/// text made the keystroke before; and the <c>using</c>s of the files a document includes,
/// found once for each content.
/// </summary>
/// <remarks>
/// <para>
/// A document's own tree is parsed incrementally (<c>DocumentManager</c> hands the edit to
/// <see cref="SyntaxTree.WithChange"/>), but a made text arrives whole, with no edit to hand
/// on, and until session 1294 each was parsed whole: a book with a <c>using</c> or a printed
/// singer paid a full parse of everything on every keystroke (review of session 1291, P8),
/// and its tree shared no node with the one before.
/// </para>
/// <para>
/// No key: the tree to parse from is the kept one made of the text that shares the most with
/// the text this one is made of, at its two ends (<see cref="SyntaxTree.SharedEnds"/>), which
/// a keystroke's text does with its own predecessor and with nothing else. One that shares
/// less than half is another text: it is parsed whole and kept beside the others.
/// </para>
/// <para>
/// The trees kept are few and held strongly, so a document that was closed keeps one alive
/// until that many other texts were made.
/// </para>
/// </remarks>
internal sealed class RecentTrees
{
    private const int Kept = 6;
    private const int ContentsKept = 64;

    // Most recently used first.
    private readonly List<(SyntaxTree Tree, int Own)> _trees = [];
    private readonly Dictionary<string, IReadOnlyList<(string Path, TextSpan Span)>> _usingsByContent
        = new(StringComparer.Ordinal);

    private static readonly RecentTrees Shared = new();

    /// <summary>A store of the calling thread's own, for a test that counts what a keystroke
    /// parses: the shared one is every test's at once, and what it keeps is few.</summary>
    [ThreadStatic]
    internal static RecentTrees? OfThisThread;

    private static RecentTrees Store => OfThisThread ?? Shared;

    /// <summary>The tree of <paramref name="text"/>, a made text.</summary>
    /// <param name="text">The made text.</param>
    /// <param name="own">How much of it, from its head, is the text it was made of: what was
    /// written after that is the rest.</param>
    public static SyntaxTree Parse(string text, int own) => Store.TreeOf(text, own);

    /// <summary>The <c>using</c> directives of an included file's text: the file is read again
    /// on every keystroke of the document that includes it (invalidation is by content), and
    /// its text is the same until it is saved.</summary>
    public static IReadOnlyList<(string Path, TextSpan Span)> IncludedUsings(string content)
        => Store.UsingsOf(content);

    private SyntaxTree TreeOf(string text, int own)
    {
        (SyntaxTree Tree, int Own)[] kept;
        lock (_trees)
            kept = [.. _trees];

        (SyntaxTree Tree, int Own)? from = null;
        int most = -1;
        foreach (var one in kept)
        {
            var (head, tail) = SyntaxTree.SharedEnds(one.Tree.Text.AsSpan(0, one.Own), text.AsSpan(0, own));
            if (head + tail > most)
            {
                most = head + tail;
                from = one;
            }
        }

        // (The same text again is asked by another document, or of another state of the files
        // a document includes: a tree of its own, as what is kept by the tree is kept for one.)
        bool sameLine = from != null && most * 2L >= own;
        SyntaxTree parsed;
        if (sameLine && !string.Equals(from!.Value.Tree.Text, text, StringComparison.Ordinal))
        {
            // Two windows, not one: the keystroke in the text it is made of, and what that
            // moved in what is written after it — a note of a section the singer sings is
            // written again at the file's end, and one window from the one to the other is
            // most of the file. So the new head with the old rest first (a text nobody reads),
            // then the new rest; where the rest stands, the first is the whole of it.
            var old = from.Value;
            string headFirst = string.Concat(text.AsSpan(0, own), old.Tree.Text.AsSpan(old.Own));
            parsed = SyntaxTree.ParseChanged(SyntaxTree.ParseChanged(old.Tree, headFirst), text);
        }
        else
        {
            parsed = SyntaxTree.Parse(text);
        }

        lock (_trees)
        {
            // The tree it was parsed from is the text of a keystroke ago: its place is taken.
            if (sameLine)
                _trees.Remove(from!.Value);
            _trees.Insert(0, (parsed, own));
            if (_trees.Count > Kept)
                _trees.RemoveRange(Kept, _trees.Count - Kept);
        }
        return parsed;
    }

    private IReadOnlyList<(string Path, TextSpan Span)> UsingsOf(string content)
    {
        lock (_usingsByContent)
        {
            if (_usingsByContent.TryGetValue(content, out var known))
                return known;
        }
        var found = UsingExpander.UsingsOf(SyntaxTree.Parse(content));
        lock (_usingsByContent)
        {
            // Few files are included at a time; the contents of ones long since edited go
            // with the rest when the table fills.
            if (_usingsByContent.Count >= ContentsKept)
                _usingsByContent.Clear();
            _usingsByContent[content] = found;
        }
        return found;
    }
}
