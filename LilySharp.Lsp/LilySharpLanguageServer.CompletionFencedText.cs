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
//   1. An empty line there: the one block, ```md, low in the list of keywords (owner's decision
//      2026-11-14: one item, its place chosen on its opening line — taking it leaves the caret
//      after `md ` and opens the list of 3. there).
//   2. After the backticks (`, `` or ```), and on into the first word: the same one.
//   3. After ```md and a space, and after each further word: the places — `start` and `end`
//      (owner's decision 2026-11-14), then `title`, `subtitle`, `composer`, `poet`, `arranger`,
//      `copyright` (a header line as Markdown, owner's decision 2026-11-13) — then `box`,
//      `center`, `xMargin`, `yMargin` (owner-approved 2026-11-13) — those not on the line yet,
//      one place at most, nothing after a header place; a place word taken from the list moves
//      the caret down onto the empty body line; and after a margin its lengths, which taking
//      the word from the list opens at once (owner's request 2026-11-13).
// And between the fences nothing is Lily#, so nothing is offered there at all.
public sealed partial class LilySharpLanguageServer
{
    /// <summary>The trigger character of a fence: completion answers a typed backtick only
    /// where a fence may begin (<see cref="FencedTextCompletions"/>).</summary>
    internal const string FenceTrigger = "`";

    private const string FenceTicks = "```";

    /// <summary>VS Code's command that opens the suggestion list again at the caret.</summary>
    private const string SuggestCommand = "editor.action.triggerSuggest";

    /// <summary>VS Code's command that moves the caret one line down (its column kept, or the
    /// line's end where the line is shorter): what takes the caret from a fence's opening line,
    /// once its place word is chosen, onto the empty body line under it.</summary>
    internal const string CursorDownCommand = "cursorDown";

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
    /// <item>On the opening line, at the backticks or in the first word: <c>```md</c>. It writes
    /// the whole block — the opening line with the caret after <c>md </c>, an empty line, the
    /// closing fence — unless the fence already closes below, where it writes the opening line
    /// alone; and it opens the list again (<see cref="FenceItems"/>).</item>
    /// <item>After <c>```md</c> and a space, and after each word that follows: <c>start</c>,
    /// <c>end</c>, the header places, <c>box</c>, <c>center</c>, <c>xMargin</c>,
    /// <c>yMargin</c>, less those already on the line or ruled out by them; straight after a
    /// margin its lengths alone, and after its first length the second's beside the words
    /// (<see cref="FenceLengthItems"/>).</item>
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

        // ```▮ and ```m▮: the opener, replacing the line from its first backtick.
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

        // ```md ▮ and on along the line: the words a text's place takes here — where it is
        // printed (`start`, `end`, a header line) and how it is set (`box`, `center`, `xMargin`, `yMargin`;
        // owner-approved 2026-11-13) — in any order, each once, so a word already written is
        // not offered again. (A section's printings — `2`, `all` — are not offered: a
        // section's text is not printed yet.)
        if (before.Length >= 1 && before[0] is FencedText.MarkdownWord or FencedText.MarkdownLongWord)
        {
            var range = RangeOf(text, wordStart, wordEnd);
            // After a margin its lengths (owner's request 2026-11-13: "md indent を補完で入れたら、
            // 数字を補完ウィンドウにリストして" — said of xMargin under its first name, and
            // yMargin's the same): the first, which it needs — nothing else belongs there —
            // and after that one the second, which it may take, beside the words.
            int marginAt = Array.FindLastIndex(before, w => w is FencedText.XMarginWord or FencedText.YMarginWord);
            int lengthsWritten = marginAt >= 0 && before.Skip(marginAt + 1).All(w => char.IsAsciiDigit(w[0]))
                ? before.Length - marginAt - 1
                : -1;
            string typed = text[wordStart..offset];
            var lengths = lengthsWritten is 0 or 1
                ? FenceLengthItems(typed, range, second: lengthsWritten == 1, sides: before[marginAt] == FencedText.XMarginWord)
                : [];
            if (lengthsWritten == 0 || (lengths.Length > 0 && typed.Length > 0 && char.IsAsciiDigit(typed[0])))
                return new CompletionList { Items = lengths };
            // The PLACE words first — `start` and `end` (owner's decision 2026-11-14: the two
            // positions, "start と end で進めて"), then the six header lines (owner's decision
            // 2026-11-13: ```md title is the title written as Markdown) — and the shape words
            // after them. The owner: "あまり多いと、補完入力が使いにくくなる" — so the list
            // stays short: once a place is written no other place is offered, and after a header
            // place (which takes no shape) nor any shape word; after a shape word no header place.
            bool placeWritten = before.Skip(1).Any(FencedText.IsPlaceWord);
            bool headerPlaceWritten = before.Skip(1).Any(w => FencedText.HeaderPlaceWords.Contains(w));
            bool shapeWritten = before.Skip(1).Any(w => FencedText.ShapeWords.Contains(w));
            if (headerPlaceWritten)
                return new CompletionList { Items = [] };
            string own = place == FencePlace.Score ? "this score's " : "the ";
            var words = new List<(string Word, string Detail)>();
            if (!placeWritten)
            {
                words.Add((FencedText.StartWord, place == FencePlace.Score
                    ? "Print this score's text under its title, above the first system (as with no place word)"
                    : "Print the text under the title, above the first system (as with no place word)"));
                words.Add((FencedText.EndWord, place == FencePlace.Score
                    ? "Print this score's text under its last system"
                    : "Print the text under the last system"));
            }
            if (!placeWritten && !shapeWritten)
                words.AddRange(FencedText.HeaderPlaceWords.Select(w => (w,
                    $"Write {own}{w} as Markdown: bold, italic and line breaks, set where and as the {w} is")));
            // A place word ends what the writer has to say on the line: taking it from the list
            // moves the caret down onto the body's line, where the text is written — when that
            // line is there and empty, as the fence item writes it (the opening line, an empty
            // line, the closing fence). A shape word may be followed by more, and stays.
            int nextLine = lineEnd + 1;
            bool bodyLineBelow = lineEnd < text.Length && IsBlank(text, nextLine, LineEndOf(text, nextLine));
            words.AddRange(
            [
                (FencedText.BoxWord, "Rule a frame around the text"),
                (FencedText.CenterWord, "Centre each line in the text's width"),
                (FencedText.XMarginWord, "White at the text's left and right, inside the line width: "
                    + "xMargin 20mm (both sides) or xMargin 20mm 10mm (left, right); mm, cm, in, or staff spaces"),
                (FencedText.YMarginWord, "White above and below the text, 1 staff space where nothing is written: "
                    + "yMargin 2 (both) or yMargin 3 1 (above, below); staff spaces, or mm, cm, in"),
            ]);
            return new CompletionList
            {
                Items = [.. words.Where(w => !before.Contains(w.Word)).Select((w, i) =>
                {
                    // A margin needs its length: it is written with the space after it and
                    // opens the list again, on the lengths (the same request), as a singer
                    // line's `up` opens its numbers.
                    bool takesLength = w.Word is FencedText.XMarginWord or FencedText.YMarginWord;
                    bool toBody = bodyLineBelow && FencedText.IsPlaceWord(w.Word);
                    return new CompletionItem
                    {
                        Label = w.Word,
                        Kind = CompletionItemKind.Keyword,
                        Detail = w.Detail,
                        // the places first, then the shape, as the sentence lists them
                        SortText = "fence" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                        TextEdit = new TextEdit { Range = range, NewText = takesLength ? w.Word + " " : w.Word },
                        Command = takesLength
                            ? new Command { Title = "Suggest a length", CommandIdentifier = SuggestCommand }
                            : toBody
                                ? new Command { Title = "Go to the text", CommandIdentifier = CursorDownCommand }
                                : null,
                    };
                }), .. lengths],
            };
        }
        return null;
    }

    /// <summary>The lengths the list offers after <c>xMargin</c>, smallest first: Lily#'s
    /// choice, the round millimetres a page's side white is commonly set to.</summary>
    internal static readonly string[] FenceXMarginLengths =
        ["5mm", "10mm", "15mm", "20mm", "25mm", "30mm", "40mm", "50mm"];

    /// <summary>The lengths the list offers after <c>yMargin</c>, smallest first: Lily#'s
    /// choice. Staff spaces read naturally here — the page's own vertical distances are
    /// written in them, and the default is 1 — then a few millimetres.</summary>
    internal static readonly string[] FenceYMarginLengths =
        ["0", "1", "2", "3", "4", "5mm", "10mm", "15mm"];

    /// <summary>
    /// The list at a length of <c>xMargin</c> or <c>yMargin</c> (owner's request 2026-11-13):
    /// the number being typed with each unit (<c>20</c> → <c>20mm</c>, <c>20cm</c>,
    /// <c>20in</c>; a bare number is staff spaces already), then those of
    /// <see cref="FenceXMarginLengths"/> or <see cref="FenceYMarginLengths"/> that begin as
    /// typed — all of them where nothing is typed yet. Each replaces the word; the sort text
    /// keeps them in this order (<c>10mm</c> would sort before <c>5mm</c> by its label).
    /// </summary>
    /// <param name="typed">The word from its start to the caret.</param>
    /// <param name="range">The word, which an item replaces.</param>
    /// <param name="second">Whether this is the margin's second length: the right side's, or below's.</param>
    /// <param name="sides">Whether the margin is <c>xMargin</c>; else <c>yMargin</c>.</param>
    private static CompletionItem[] FenceLengthItems(string typed, LilySharp.Lsp.Protocol.Range range, bool second, bool sides)
    {
        var offered = new List<string>();
        int digits = 0;
        while (digits < typed.Length && (char.IsAsciiDigit(typed[digits]) || typed[digits] == '.'))
            digits++;
        string number = typed[..digits], unit = typed[digits..];
        if (number.Length > 0 && char.IsAsciiDigit(number[0]) && char.IsAsciiDigit(number[^1])
            && number.Count(c => c == '.') <= 1)
            offered.AddRange(FencedText.LengthUnits.Where(u => u.StartsWith(unit, StringComparison.Ordinal)).Select(u => number + u));
        offered.AddRange((sides ? FenceXMarginLengths : FenceYMarginLengths)
            .Where(l => l.StartsWith(typed, StringComparison.Ordinal) && !offered.Contains(l)));
        string detail = (sides, second) switch
        {
            (true, false) => "White at the text's left and right; a second length sets the right apart",
            (true, true) => "White at the text's right; the length before it is then the left's alone",
            (false, false) => "White above and below the text; a second length sets the white below apart",
            (false, true) => "White below the text; the length before it is then the white above alone",
        };
        return [.. offered.Select((length, i) => new CompletionItem
        {
            Label = length,
            Kind = CompletionItemKind.Value,
            Detail = detail,
            SortText = "length" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
            TextEdit = new TextEdit { Range = range, NewText = length },
        })];
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
    /// The one fenced text a place offers, <c>```md</c> (owner's decision 2026-11-14: one item;
    /// the place is chosen on its opening line). A snippet: the opening line <c>```md</c> and a
    /// space with the caret there (<c>$1</c>), an empty body line (<c>$0</c>, a Tab away), the
    /// closing fence — or the opening line alone (<paramref name="wholeBlock"/> false). Either
    /// way taking it opens the list again at the caret, on the place and shape words
    /// (<c>start</c>, <c>end</c>, the header places, then <c>box</c>, <c>center</c>,
    /// <c>xMargin</c>, <c>yMargin</c>); a place word taken from there moves the caret down to
    /// the body line (<see cref="CursorDownCommand"/>). Moving on with none leaves a bare
    /// <c>```md</c>, the text under the title.
    /// </summary>
    /// <remarks>
    /// It sorts after every keyword and after <c>singer</c> (a page text is rarer than a
    /// section), and a label that begins with a backtick would otherwise sort FIRST. The filter
    /// text is what a writer types on an empty line to find it — <c>md</c>, <c>markdown</c>,
    /// <c>text</c> — the backtick being no word character to the editor.
    /// </remarks>
    private static CompletionItem[] FenceItems(FencePlace place, string ticks, bool wholeBlock)
    {
        bool score = place == FencePlace.Score;
        string documentation = (score
            ? "Inside `score { }` the text is this score's alone: it replaces the file's text for the same place. "
            : "Every score of the file prints it; a fence inside `score { }` replaces it for that score. ")
            + "Write Markdown between the two fence lines — paragraphs, **bold** and *italic* are printed. "
            + "Nothing between them is Lily#. The opening line says where the text goes — `start` (under the title, "
            + "above the first system: the same as no word), `end` (under the last system), or `title`, `subtitle`, "
            + "`composer`, `poet`, `arranger` or `copyright` (that header line, written as Markdown) — and how it is set: "
            + "`box` (a ruled frame), `center`, `xMargin 20mm` (white at the sides), `yMargin 2` (white above and below).";
        string opening = ticks + FencedText.MarkdownWord + " ";
        return
        [
            new()
            {
                Label = ticks + FencedText.MarkdownWord,
                Kind = CompletionItemKind.Snippet,
                InsertTextFormat = wholeBlock ? InsertTextFormat.Snippet : InsertTextFormat.Plaintext,
                InsertText = wholeBlock ? opening + "$1\n$0\n" + ticks : opening,
                FilterText = FencedText.MarkdownWord + " markdown text",
                SortText = "~text0",
                Detail = score
                    ? "This score's text, written as Markdown: say where it goes (start, end, a header line) on its first line"
                    : "A text for the page, written as Markdown: say where it goes (start, end, a header line) on its first line",
                Documentation = new MarkupContent { Kind = MarkupKind.Markdown, Value = documentation },
                Command = new Command { Title = "Suggest where the text goes", CommandIdentifier = SuggestCommand },
            },
        ];
    }

    /// <summary>
    /// A list of the top level's keywords or of a score's items, with the fenced text
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
