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
using System.Linq;
using Markdig;
using Markdig.Syntax;

namespace LilySharp.Core.Rendering;

/// <summary>
/// A header line written as Markdown (<c>```md title</c> …; owner's decision 2026-11-13) as
/// the page takes it: its lines, each a row of runs in the emphasis the Markdown writes —
/// bold, italic — and what it writes that a header line does not take.
/// </summary>
/// <remarks>
/// A header place takes INLINE text: emphasis, hard line breaks, and paragraphs, each a line
/// of its own (a header line does not wrap, as the plain string does not). A heading, a list,
/// a block quote, a code block, an image is noted (LYS4058's family) and drawn as plain lines
/// — its words, with no size, marker or frame: a heading's line, each item's line, each line
/// of the quote or the code, an image's alt text. A rule, a table and HTML are noted and draw
/// nothing. LILYSHARP-OWN.
/// </remarks>
/// <param name="Lines">The lines, top to bottom; none is empty.</param>
/// <param name="NotInline">What is not inline text: where, how long, what it is called.</param>
internal sealed record HeaderProse(ImmutableArray<ImmutableArray<ProseRun>> Lines, ImmutableArray<ProseNote> NotInline)
{
    /// <summary>Reads a header fence's body.</summary>
    public static HeaderProse Read(string markdown)
    {
        var lines = ImmutableArray.CreateBuilder<ImmutableArray<ProseRun>>();
        var notes = ImmutableArray.CreateBuilder<ProseNote>();
        AddBlocks(Markdown.Parse(markdown, Prose.Pipeline), lines, notes, noted: false);
        return new HeaderProse(lines.ToImmutable(), notes.ToImmutable());
    }

    /// <summary>The words with the formatting stripped, every line break one space: what a
    /// header line written only as Markdown gives everything that is not the page — MusicXML's
    /// title, the MIDI copyright (owner's decision 2026-11-13).</summary>
    public string Plain => string.Join(" ", Lines.Select(l => string.Concat(l.Select(r => r.Text)).Trim())
        .Where(s => s.Length > 0));

    /// <summary><see cref="Plain"/> of <paramref name="markdown"/>.</summary>
    public static string PlainOf(string markdown) => Read(markdown).Plain;

    private static void AddBlocks(IEnumerable<Block> container, ImmutableArray<ImmutableArray<ProseRun>>.Builder lines,
        ImmutableArray<ProseNote>.Builder notes, bool noted)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    if (!noted)
                        notes.Add(Note(block, "A heading"));
                    AddInline(heading, lines, notes);
                    break;
                case ParagraphBlock paragraph:
                    AddInline(paragraph, lines, notes);
                    break;
                case ListBlock list:
                    if (!noted)
                        notes.Add(Note(block, "A list"));
                    foreach (var item in list.OfType<ListItemBlock>())
                        AddBlocks(item, lines, notes, noted: true);
                    break;
                case QuoteBlock quote:
                    if (!noted)
                        notes.Add(Note(block, "A block quote"));
                    AddBlocks(quote, lines, notes, noted: true);
                    break;
                case CodeBlock code:
                    if (!noted)
                        notes.Add(Note(block, code is FencedCodeBlock ? "A code block"
                            : "A line indented four spaces or more beyond the text around it (Markdown reads it as a code block)"));
                    foreach (var line in code.Lines.Lines.Take(code.Lines.Count))
                    {
                        string text = line.Slice.ToString().TrimEnd();
                        if (text.Length > 0)
                            lines.Add([new ProseRun(text, FontStyle.Regular, line.Position)]);
                    }
                    break;
                case LinkReferenceDefinitionGroup:
                    break;      // a link's address, written apart: nothing of its own to draw
                case ContainerBlock other:
                    if (!noted)
                        notes.Add(Note(block, NameOf(block)));
                    AddBlocks(other, lines, notes, noted: true);
                    break;
                default:
                    if (!noted)
                        notes.Add(Note(block, NameOf(block)));
                    break;
            }
        }
    }

    private static ProseNote Note(Block block, string what)
        => new(block.Span.Start, System.Math.Max(1, block.Span.Length), what);

    private static string NameOf(Block block) => block switch
    {
        ThematicBreakBlock => "A rule",
        Markdig.Extensions.Tables.Table => "A table",
        HtmlBlock => "HTML",
        _ => "This",
    };

    /// <summary>A paragraph's (or a heading's) runs, cut into lines at its hard breaks.</summary>
    private static void AddInline(LeafBlock block, ImmutableArray<ImmutableArray<ProseRun>>.Builder lines,
        ImmutableArray<ProseNote>.Builder notes)
    {
        if (block.Inline is not { } inline)
            return;
        var runs = ImmutableArray.CreateBuilder<ProseRun>();
        Prose.AddRuns(inline, FontStyle.Regular, runs, notes, imageWords: true);
        var line = ImmutableArray.CreateBuilder<ProseRun>();
        void Flush()
        {
            // A line's ends are not drawn as white: a break written after a space, or a line
            // that begins with one, keeps its words where the line's edge is.
            while (line.Count > 0 && line[^1].Text.TrimEnd().Length == 0)
                line.RemoveAt(line.Count - 1);
            if (line.Count > 0)
                line[^1] = line[^1] with { Text = line[^1].Text.TrimEnd() };
            while (line.Count > 0 && line[0].Text.TrimStart().Length == 0)
                line.RemoveAt(0);
            if (line.Count > 0)
            {
                line[0] = line[0] with { Text = line[0].Text.TrimStart() };
                lines.Add(line.ToImmutable());
            }
            line.Clear();
        }
        for (int i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            // A line end inside a paragraph is a space, or nothing between two characters of a
            // script written without spaces (Prose.JoinsWithoutSpace, as the page text's).
            if (run is { Text: " ", Offset: -1 } && line.Count > 0 && line[^1].Text.Length > 0
                && i + 1 < runs.Count && runs[i + 1].Text.Length > 0
                && Prose.JoinsWithoutSpace(line[^1].Text[^1], runs[i + 1].Text[0]))
                continue;
            if (run.Text == ProseRun.Break)
                Flush();
            else
                line.Add(run);
        }
        Flush();
    }
}
