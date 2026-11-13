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
using System.Collections.Immutable;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace LilySharp.Core.Rendering;

/// <summary>A stretch of a paragraph in one weight and slant. <see cref="Break"/> is a line
/// the writer ended by hand (two spaces or a backslash before the line end).</summary>
/// <param name="Text">The words, spaces as written (a line end inside a paragraph is one
/// space, or nothing between two characters that take no space between them).</param>
/// <param name="Style">Bold and italic as the Markdown says, over the text's own style.</param>
/// <param name="Offset">Where the stretch begins in the text read, or −1.</param>
internal sealed record ProseRun(string Text, FontStyle Style, int Offset)
{
    /// <summary>The run that ends a line by hand.</summary>
    public const string Break = "\n";
}

/// <summary>One paragraph, one heading, or one paragraph of a list's item: its runs in order,
/// and what it is (owner's request 2026-11-13: headings and lists are drawn).</summary>
/// <param name="Runs">The runs in order. Empty for an item that writes nothing the page
/// draws: its marker stands alone.</param>
/// <param name="Heading">0 for a paragraph; 1 or 2 for a heading (a deeper level is read as 2).</param>
/// <param name="Lists">The lists the block stands in, outermost first — each by its WIDEST
/// marker as written (<see cref="Prose.Bullet"/> for a bullet list), which is what the list's
/// indent is measured from. Empty outside a list.</param>
/// <param name="Marker">The marker of the item this block begins (<see cref="Prose.Bullet"/>,
/// or <c>3.</c> / <c>3)</c>), or null: a later paragraph of an item, or no item.</param>
/// <param name="Tight">No white over this block: it follows the block before it in a tight
/// list (CommonMark: a list with no blank line between its items).</param>
internal sealed record ProseBlock(ImmutableArray<ProseRun> Runs, int Heading = 0,
    ImmutableArray<string> Lists = default, string? Marker = null, bool Tight = false)
{
    /// <summary>How many lists deep the block stands: 0 outside a list.</summary>
    public int Depth => Lists.IsDefaultOrEmpty ? 0 : Lists.Length;
}

/// <summary>Something the text writes that the page does not draw: where, how long, and what
/// it is called in the sentence that says so.</summary>
internal sealed record ProseNote(int Offset, int Length, string What);

/// <summary>
/// A fenced text as the page takes it (docs/markdown-text-design.md): its paragraphs,
/// headings and lists' items, and what it writes that is read and not drawn.
/// </summary>
/// <remarks>
/// The text is read as CommonMark WHOLE (Markdig), tables included, so that a table or a code
/// block is known for what it is and said, never set as a paragraph of pipes and backticks.
/// LILYSHARP-OWN: LilyPond has no Markdown; its markup is what the twin writes from this.
/// </remarks>
internal sealed record Prose(ImmutableArray<ProseBlock> Blocks, ImmutableArray<ProseNote> NotDrawn)
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UsePreciseSourceLocation().UsePipeTables().Build();

    /// <summary>A bullet list's marker as a block carries it. The glyph drawn is the page's
    /// to choose by the list's depth (Svg.Layout.ProseBand.MarkerGlyph).</summary>
    public const string Bullet = "•";

    /// <summary>The deepest heading drawn as its own level: <c>#</c> and <c>##</c>. A deeper
    /// one (<c>###</c> … <c>######</c>) is drawn as this level.</summary>
    /// <remarks>LILYSHARP-OWN (docs/markdown-text-design.md section 3: "headings, two
    /// levels"). A text that accompanies a score has a heading and a heading under it; a
    /// third size between 2.47 and the text's 2.2 would not be told from bold text.</remarks>
    public const int HeadingLevels = 2;

    /// <summary>Reads <paramref name="markdown"/>: a fenced text's body.</summary>
    public static Prose Read(string markdown)
    {
        var blocks = ImmutableArray.CreateBuilder<ProseBlock>();
        var notes = ImmutableArray.CreateBuilder<ProseNote>();
        AddBlocks(Markdown.Parse(markdown, Pipeline), ImmutableArray<string>.Empty, null, false, false, blocks, notes);
        return new Prose(blocks.ToImmutable(), notes.ToImmutable());
    }

    /// <summary>
    /// The blocks of <paramref name="container"/> — the text, or one item of a list — in
    /// order. The first one drawn begins the item (<paramref name="marker"/>) and takes
    /// <paramref name="tightFirst"/>; the later ones take <paramref name="tightRest"/>.
    /// </summary>
    /// <remarks>
    /// What is not drawn (a block quote, a code block, a rule, a table, HTML) is noted
    /// WHOLE, with what stands inside it: a list in a block quote is the quote's, and is said
    /// once as "A block quote". A task item (<c>- [ ] todo</c>) is an item whose words begin
    /// with the brackets, as CommonMark reads it (the task list is an extension Lily# does not
    /// switch on).
    /// </remarks>
    private static void AddBlocks(IEnumerable<Block> container, ImmutableArray<string> lists, string? marker,
        bool tightFirst, bool tightRest, ImmutableArray<ProseBlock>.Builder blocks, ImmutableArray<ProseNote>.Builder notes)
    {
        bool first = true;      // nothing of this container is drawn yet
        foreach (var block in container)
        {
            int before = blocks.Count;
            switch (block)
            {
                case ParagraphBlock or HeadingBlock:
                    var runs = ImmutableArray.CreateBuilder<ProseRun>();
                    if (((LeafBlock)block).Inline is { } inline)
                        AddRuns(inline, FontStyle.Regular, runs, notes);
                    if (runs.Count > 0)
                    {
                        blocks.Add(new ProseBlock(runs.ToImmutable(),
                            block is HeadingBlock heading ? System.Math.Clamp(heading.Level, 1, HeadingLevels) : 0,
                            lists, first ? marker : null, first ? tightFirst : tightRest));
                    }
                    break;
                case ListBlock list:
                    // An item that begins with a list of its own (`- - a`): its marker
                    // stands on a line by itself, the inner list under it.
                    if (first && marker is not null)
                    {
                        blocks.Add(new ProseBlock(ImmutableArray<ProseRun>.Empty, 0, lists, marker, tightFirst));
                        first = false;
                    }
                    AddList(list, lists, first ? tightFirst : tightRest, blocks, notes);
                    break;
                case LinkReferenceDefinitionGroup:
                    break;      // a link's address, written apart: nothing of its own to draw
                default:
                    notes.Add(new ProseNote(block.Span.Start, System.Math.Max(1, block.Span.Length), NameOf(block)));
                    break;
            }
            if (blocks.Count > before)
                first = false;
        }
        // An item with nothing the page draws (`-` alone, or only an image): its marker.
        if (first && marker is not null)
            blocks.Add(new ProseBlock(ImmutableArray<ProseRun>.Empty, 0, lists, marker, tightFirst));
    }

    /// <summary>
    /// One list: each item's blocks, the item's first carrying its marker. A numbered list
    /// counts on from the number its first item writes, whatever the later items write
    /// (CommonMark: <c>3. 3. 3.</c> is 3, 4, 5), and keeps the delimiter written (<c>1.</c> or
    /// <c>1)</c>). A tight list's items follow each other with no white; a loose one's — a
    /// blank line between two items, or in one — stand a paragraph apart.
    /// </summary>
    private static void AddList(ListBlock list, ImmutableArray<string> lists, bool tightFirst,
        ImmutableArray<ProseBlock>.Builder blocks, ImmutableArray<ProseNote>.Builder notes)
    {
        int start = 1;
        if (list.IsOrdered && !int.TryParse(list.OrderedStart, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out start))
            start = 1;
        string MarkerOf(int index) => list.IsOrdered
            ? (start + index).ToString(System.Globalization.CultureInfo.InvariantCulture) + list.OrderedDelimiter
            : Bullet;
        // The widest marker is the last one: the numbers only grow.
        var inner = lists.Add(MarkerOf(System.Math.Max(0, list.Count - 1)));
        bool tight = !list.IsLoose;
        int i = 0;
        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
                AddBlocks(listItem, inner, MarkerOf(i), i == 0 ? tightFirst : tight, tight, blocks, notes);
            i++;
        }
    }

    private static string NameOf(Block block) => block switch
    {
        QuoteBlock => "A block quote",
        CodeBlock => "A code block",
        ThematicBreakBlock => "A rule",
        Table => "A table",
        HtmlBlock => "HTML",
        _ => "This",
    };

    private static void AddRuns(ContainerInline container, FontStyle style,
        ImmutableArray<ProseRun>.Builder runs, ImmutableArray<ProseNote>.Builder notes)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    Add(literal.Content.ToString(), literal.Span.Start);
                    break;
                case EmphasisInline emphasis:
                    AddRuns(emphasis, style | (emphasis.DelimiterCount >= 2 ? FontStyle.Bold : FontStyle.Italic), runs, notes);
                    break;
                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                        runs.Add(new ProseRun(ProseRun.Break, style, lineBreak.Span.Start));
                    else
                        runs.Add(new ProseRun(" ", style, -1));     // −1: a line end, not a written space
                    break;
                case CodeInline code:
                    // Its words stay in the sentence; that they are code is not shown.
                    notes.Add(new ProseNote(code.Span.Start, System.Math.Max(1, code.Span.Length), "Code"));
                    Add(code.Content, code.Span.Start + 1);
                    break;
                case LinkInline { IsImage: true } image:
                    notes.Add(new ProseNote(image.Span.Start, System.Math.Max(1, image.Span.Length), "An image"));
                    break;
                case LinkInline link:
                    AddRuns(link, style, runs, notes);      // the words; the address is not printed
                    break;
                case AutolinkInline auto:
                    Add(auto.Url, auto.Span.Start + 1);
                    break;
                case HtmlEntityInline entity:
                    Add(entity.Transcoded.ToString(), entity.Span.Start);
                    break;
                case HtmlInline html:
                    notes.Add(new ProseNote(html.Span.Start, System.Math.Max(1, html.Span.Length), "HTML"));
                    break;
                case ContainerInline other:
                    AddRuns(other, style, runs, notes);
                    break;
            }
        }

        void Add(string text, int offset)
        {
            if (text.Length > 0)
                runs.Add(new ProseRun(text, style, offset));
        }
    }

    /// <summary>
    /// Whether a line end between <paramref name="before"/> and <paramref name="after"/> is
    /// no space: two characters of a script written without spaces between its words.
    /// </summary>
    public static bool JoinsWithoutSpace(char before, char after) => IsWide(before) && IsWide(after);

    /// <summary>
    /// Whether <paramref name="c"/> is of a script set a full square a character, with no
    /// spaces between words: kana, CJK ideographs, hangul, their punctuation, full-width forms.
    /// </summary>
    public static bool IsWide(char c) =>
        c is (>= '　' and <= 'ヿ')      // CJK punctuation, hiragana, katakana
            or (>= '㐀' and <= '䶿')    // ideographs, extension A
            or (>= '一' and <= '鿿')    // ideographs
            or (>= '가' and <= '힣')    // hangul syllables
            or (>= '豈' and <= '﫿')    // compatibility ideographs
            or (>= '＀' and <= '｠')    // full-width forms
            or (>= '￠' and <= '￦');

    /// <summary>Punctuation that does not begin a line: a closing bracket, a stop, a long
    /// vowel mark, a small kana.</summary>
    public static bool StaysWithPrevious(char c) =>
        "、。，．・：；？！゛゜ヽヾゝゞ々ー’”）〕］｝〉》」』】ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮ,.!?)]}".Contains(c);

    /// <summary>Punctuation that does not end a line: an opening bracket.</summary>
    public static bool StaysWithNext(char c) => "‘“（〔［｛〈《「『【([{".Contains(c);
}
