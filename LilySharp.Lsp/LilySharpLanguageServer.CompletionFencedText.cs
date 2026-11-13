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
using LilySharp.Core.Syntax;
using LilySharp.Lsp.Protocol;

namespace LilySharp.Lsp;

// The fenced text in completion (owner's request 2026-11-13: "in the fitting contexts, let the
// md fence come up in completion"; docs/markdown-text-design.md).
//
// Three places, and one rule for all of them — a fence is offered only where the page prints
// it: on a line of its own at the file's top level or directly in a `score { }`. Not in a
// section (read and not printed yet, LYS4058), not in a part, music, lyrics, a string or a
// comment.
//   1. An empty line there: the two blocks, low in the list of keywords.
//   2. After the backticks (`, `` or ```), and on into the first word: the same two.
//   3. After ```md and a space: `end`.
// And between the fences nothing is Lily#, so nothing is offered there at all.
public sealed partial class LilySharpLanguageServer
{
    /// <summary>The trigger character of a fence: completion answers a typed backtick only
    /// where a fence may begin (<see cref="FencedTextCompletions"/>).</summary>
    internal const string FenceTrigger = "`";

    private const string FenceTicks = "```";

    /// <summary>Where a fenced text is printed from, or null where it is not.</summary>
    private enum FencePlace
    {
        /// <summary>The file's top level: every score's text.</summary>
        File,

        /// <summary>Directly in a <c>score { }</c>: that score's text, over the file's.</summary>
        Score,
    }

    /// <summary>The spans of the tree's fenced texts, each one token from its first backtick to
    /// its closing fence (to the end of the file when it has none).</summary>
    private static List<TextSpan> FencedTextSpans(SyntaxTree tree)
        => [.. tree.GetRoot().DescendantNodesOfKinds(SyntaxKind.FencedText).Select(n => n.Span)];

    /// <summary>
    /// <paramref name="text"/> with everything in its fenced texts but the backticks and the
    /// line ends made blank, offset for offset. The lists' readers scan the text for braces,
    /// quotes, comments and declared names, and a Markdown text holds all of them
    /// (<c>{</c>, <c>"</c>, <c>//</c> in a link, a line that begins <c>section</c>) meaning
    /// nothing. The backticks stay so that a line holding a fence does not read as empty.
    /// </summary>
    private static string WithFencedTextsBlank(string text, List<TextSpan> fences)
    {
        if (fences.Count == 0)
            return text;
        char[] chars = text.ToCharArray();
        foreach (var fence in fences)
        {
            for (int i = fence.Start; i < fence.End && i < chars.Length; i++)
            {
                if (chars[i] is not ('`' or '\n' or '\r'))
                    chars[i] = ' ';
            }
        }
        return new string(chars);
    }

    /// <summary>The place a fence beginning at <paramref name="at"/> is printed from: the file's
    /// top level or a score's own body, in live code. <paramref name="text"/> is the text with
    /// its fences blank.</summary>
    private static FencePlace? FencePlaceAt(string text, int at)
    {
        at = Math.Clamp(at, 0, text.Length);
        // In a string or a comment a backtick is text. One character past the place tells
        // what the place itself is in.
        string upTo = text[..at] + "x";
        if (!CodeMask(upTo, upTo.Length)[at])
            return null;
        var stack = new BlockContextScan(text, at).Stack;
        // Directly in the score, as a `singer` line: a staff group inside it holds staves only.
        if (!TakesASingerLine(stack))
            return null;
        return stack.Count == 0 ? FencePlace.File : FencePlace.Score;
    }

    private static bool IsBlank(string text, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (text[i] is not (' ' or '\t' or '\r'))
                return false;
        }
        return true;
    }

    private static int LineStartOf(string text, int offset)
        => offset <= 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;

    private static int LineEndOf(string text, int offset)
    {
        int end = text.IndexOf('\n', offset);
        return end < 0 ? text.Length : end;
    }

    /// <summary>
    /// The answer where the caret stands in or at a fenced text, or null where it does not and
    /// the lists go on as ever (over <paramref name="blanked"/>, the text with its fences blank).
    /// <list type="bullet">
    /// <item>On the opening line, at the backticks or in the first word: <c>```md</c> and
    /// <c>```md end</c>. Each writes the whole block — the opening line, an empty line with the
    /// caret, the closing fence — unless the fence already closes below, where it writes the
    /// opening line alone.</item>
    /// <item>After <c>```md</c> and a space: <c>end</c>.</item>
    /// <item>Between the fences, on the closing fence, and at a fence that stands where none is
    /// printed: an empty list. Nothing there is Lily#. ⚠️ Until this was written the body
    /// answered whatever list the text before the fence led to — the top level's keywords
    /// under a top-level fence, the score's items in a score — at every space of the prose
    /// (the space is a trigger character).</item>
    /// </list>
    /// One or two backticks are no token yet, and are read here from the text: the list opens
    /// at the first backtick typed.
    /// </summary>
    private static CompletionList? FencedTextCompletions(
        string text, List<TextSpan> fences, int offset, out string blanked)
    {
        blanked = WithFencedTextsBlank(text, fences);
        var nothing = new CompletionList { Items = [] };
        foreach (var fence in fences)
        {
            if (offset <= fence.Start || offset > fence.End)
                continue;
            string token = text[fence.Start..fence.End];
            int openingEnd = token.IndexOf('\n');
            if (openingEnd >= 0 && offset - fence.Start > openingEnd)
                return nothing;
            return FenceOpeningLineCompletions(text, blanked, fence.Start, offset, FencedText.Read(token)) ?? nothing;
        }

        int run = offset;
        while (run > 0 && text[run - 1] == '`')
            run--;
        return run < offset ? FenceOpeningLineCompletions(text, blanked, run, offset, fence: null) : null;
    }

    /// <summary>The list on a fence's opening line, the caret at or after its backticks; null
    /// where the fence begins no line of its own, stands where none is printed, or the line
    /// goes on after the caret's word.</summary>
    /// <param name="text">The document.</param>
    /// <param name="blanked">The document with its fences blank.</param>
    /// <param name="start">The fence's first backtick.</param>
    /// <param name="offset">The caret.</param>
    /// <param name="fence">The fence as the lexer read it; null for one or two backticks.</param>
    private static CompletionList? FenceOpeningLineCompletions(
        string text, string blanked, int start, int offset, FencedText? fence)
    {
        if (!IsBlank(text, LineStartOf(text, start), start) || FencePlaceAt(blanked, start) is not { } place)
            return null;
        int ticksEnd = start;
        while (ticksEnd < text.Length && text[ticksEnd] == '`')
            ticksEnd++;
        int lineEnd = LineEndOf(text, offset);
        // The word the caret is in or after, to its end: what an item replaces.
        int wordStart = offset, wordEnd = offset;
        while (wordStart > ticksEnd && !char.IsWhiteSpace(text[wordStart - 1]))
            wordStart--;
        while (wordEnd < lineEnd && !char.IsWhiteSpace(text[wordEnd]))
            wordEnd++;
        if (offset < ticksEnd)
            wordStart = wordEnd = ticksEnd;
        if (!IsBlank(text, wordEnd, lineEnd))
            return null;
        string[] before = text[ticksEnd..wordStart].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        // ```▮ and ```m▮: the two openers, replacing the line from its first backtick.
        if (before.Length == 0 && wordStart == ticksEnd)
        {
            // A fence that already closes below takes its opening line alone. ⚠️ A fence typed
            // above another one closes on THAT one's closing line and holds its opening line
            // in its body: that is no closing fence of its own, and the block is written whole.
            bool closesBelow = fence is { Closed: true }
                && !fence.Body.Split('\n').Any(l => l.TrimStart().StartsWith(FenceTicks, StringComparison.Ordinal));
            string ticks = ticksEnd - start >= FenceTicks.Length ? text[start..ticksEnd] : FenceTicks;
            var range = RangeOf(text, start, wordEnd);
            return new CompletionList
            {
                Items = [.. FenceItems(place, ticks, wholeBlock: !closesBelow).Select(item =>
                {
                    // The editor filters by the text from the range's start to the caret, which
                    // is the backticks typed so far and then the word.
                    item.FilterText = item.Label;
                    item.TextEdit = new TextEdit { Range = range, NewText = item.InsertText! };
                    return item;
                })],
            };
        }

        // ```md ▮: the one word a text's place takes here. (A section's printings — `2`, `all` —
        // are not offered: a section's text is not printed yet.)
        if (before.Length == 1 && before[0] is FencedText.MarkdownWord or FencedText.MarkdownLongWord)
        {
            return new CompletionList
            {
                Items =
                [
                    new CompletionItem
                    {
                        Label = FencedText.EndWord,
                        Kind = CompletionItemKind.Keyword,
                        Detail = place == FencePlace.Score
                            ? "Print this score's text under its last system instead of under the title"
                            : "Print the text under the last system instead of under the title",
                        TextEdit = new TextEdit { Range = RangeOf(text, wordStart, wordEnd), NewText = FencedText.EndWord },
                    },
                ],
            };
        }
        return null;
    }

    private static LilySharp.Lsp.Protocol.Range RangeOf(string text, int start, int end)
    {
        var (startLine, startCharacter) = GetLineAndCharacter(text, start);
        var (endLine, endCharacter) = GetLineAndCharacter(text, end);
        return new LilySharp.Lsp.Protocol.Range
        {
            Start = new Position(startLine, startCharacter),
            End = new Position(endLine, endCharacter),
        };
    }

    /// <summary>
    /// The two fenced texts a place takes, <c>```md</c> (under the title) and <c>```md end</c>
    /// (under the last system), each a snippet: the opening line, an empty line with the caret,
    /// the closing fence — or the opening line alone (<paramref name="wholeBlock"/> false).
    /// </summary>
    /// <remarks>
    /// They sort after every keyword and after <c>singer</c> (a page text is rarer than a
    /// section), and a label that begins with a backtick would otherwise sort FIRST. The filter
    /// text is what a writer types on an empty line to find them — <c>md</c>, <c>markdown</c>,
    /// <c>text</c> — the backtick being no word character to the editor.
    /// </remarks>
    private static CompletionItem[] FenceItems(FencePlace place, string ticks, bool wholeBlock)
    {
        bool score = place == FencePlace.Score;
        string documentation = (score
            ? "Inside `score { }` the text is this score's alone: it replaces the file's text for the same place. "
            : "Every score of the file prints it; a fence inside `score { }` replaces it for that score. ")
            + "Write Markdown between the two fence lines — paragraphs, **bold** and *italic* are printed. "
            + "Nothing between them is Lily#.";
        CompletionItem Item(string words, string detail, string sort) => new()
        {
            Label = ticks + words,
            Kind = CompletionItemKind.Snippet,
            InsertTextFormat = wholeBlock ? InsertTextFormat.Snippet : InsertTextFormat.Plaintext,
            InsertText = wholeBlock ? ticks + words + "\n$0\n" + ticks : ticks + words,
            FilterText = words + " markdown text",
            SortText = sort,
            Detail = detail,
            Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = documentation },
        };
        return
        [
            Item(FencedText.MarkdownWord, score
                ? "This score's text, written as Markdown: printed under its title"
                : "A text for the page, written as Markdown: printed under the title", "~text0"),
            Item(FencedText.MarkdownWord + " " + FencedText.EndWord, score
                ? "This score's text, written as Markdown: printed under its last system"
                : "A text for the page, written as Markdown: printed under the last system", "~text1"),
        ];
    }

    /// <summary>
    /// A list of the top level's keywords or of a score's items, with the two fenced texts
    /// after them where a fence may begin: the caret on a line that holds nothing else (the
    /// word being typed apart), at the file's top level or directly in a score.
    /// </summary>
    private static CompletionList WithFencedText(CompletionList list, string text, int offset)
    {
        int at = Math.Clamp(offset, 0, text.Length);
        int wordStart = at, wordEnd = at;
        while (wordStart > 0 && (char.IsLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] is '_' or '-'))
            wordStart--;
        while (wordEnd < text.Length && (char.IsLetterOrDigit(text[wordEnd]) || text[wordEnd] is '_' or '-'))
            wordEnd++;
        if (!IsBlank(text, LineStartOf(text, wordStart), wordStart) || !IsBlank(text, wordEnd, LineEndOf(text, wordEnd))
            || FencePlaceAt(text, wordStart) is not { } place)
            return list;
        return new CompletionList
        {
            IsIncomplete = list.IsIncomplete,
            Items = [.. list.Items, .. FenceItems(place, FenceTicks, wholeBlock: true)],
        };
    }
}
