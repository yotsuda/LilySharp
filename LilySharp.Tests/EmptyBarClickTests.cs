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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A bar a part does not write yet goes, clicked in the preview, to where it is to be typed: the
/// end of the part's block in that section (owner's request 2026-11-20 — a bass line added by ear
/// under a chord chart; a click on the next bar of the bass staff went to the section's head).
/// </summary>
[Trait("Category", "Unit")]
public class EmptyBarClickTests
{
    private const string Book = "time 4/4\npart bass { }\n"
        + "section A {\n  chords prog { C | D | }\n  bass { c2 d | e1 | }\n}\n"
        + "section B {\n  chords prog { C | D | E | }\n  bass {\n    c1 | \n  }\n}\n"
        + "section Cc {\n  chords prog { C | D | }\n}\n"
        + "section D {\n  chords prog { C | D | }\n  bass { c1 }\n}\n"
        + "form { A B Cc D }\nscore { chords prog\n  staff bass }\n";

    private static readonly Regex Box = new(
        @"<rect class=""bar-staff"" data-start=""(\d+)""(?: data-span=""(\d+) (\d+)"")?( data-row="""")?");

    private static (int Start, int From, int To)[] StaffBoxes(string book)
    {
        string preview = SvgGenerator.Generate(SyntaxTree.Parse(book), SvgRenderOptions.Preview());
        return Box.Matches(preview).Cast<Match>().Where(m => !m.Groups[4].Success)
            .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : -1, m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : -1))
            .ToArray();
    }

    [Fact]
    public void ABarNotWrittenYet_GoesToTheEndOfThePartsBlock()
    {
        var boxes = StaffBoxes(Book);
        Assert.Equal(9, boxes.Length);   // 2 + 3 + 2 + 2 bars
        int afterC1 = Book.IndexOf("c1 | ", StringComparison.Ordinal) + "c1 | ".Length;
        // A: written bars, their own first notes.
        Assert.Equal(Book.IndexOf("c2 d", StringComparison.Ordinal), boxes[0].Start);
        Assert.Equal(Book.IndexOf("e1 |", StringComparison.Ordinal), boxes[1].Start);
        // B: its first bar written; the two after it, past the block's last bar line and its space.
        Assert.Equal(Book.IndexOf("c1 | ", StringComparison.Ordinal), boxes[2].Start);
        Assert.Equal(afterC1, boxes[3].Start);
        Assert.Equal(afterC1, boxes[4].Start);
        // (no stretch of its own: no caret in other text lights it — the section's head did)
        Assert.Equal((-1, -1), (boxes[3].From, boxes[3].To));
        // Cc: no bass block at all — the section's head, as before.
        int cc = Book.IndexOf("section Cc", StringComparison.Ordinal);
        Assert.Equal(cc, boxes[5].Start);
        Assert.Equal(cc, boxes[6].Start);
        // D: a bar left open (no bar line, no space after it): just after its last note.
        int afterOpen = Book.IndexOf("c1 }", StringComparison.Ordinal) + "c1".Length;
        Assert.Equal(afterOpen - 2, boxes[7].Start);
        Assert.Equal(afterOpen + 1, boxes[8].Start);    // (" }": the space after it is taken)
    }

    [Fact]
    public void AnEmptyBlock_GoesInsideItsBraces()
    {
        string book = Book.Replace("bass {\n    c1 | \n  }", "bass { }");
        var boxes = StaffBoxes(book);
        int inside = book.IndexOf("bass { }", book.IndexOf("section B", StringComparison.Ordinal), StringComparison.Ordinal) + "bass { ".Length;
        Assert.Equal(inside, boxes[2].Start);
        Assert.Equal(inside, boxes[4].Start);
    }

    [Theory]
    [InlineData("bass { c1 | }", "bass { c1 | ")]
    [InlineData("bass { c1 |}", "bass { c1 |")]
    [InlineData("bass {\n  c1 |\n}", "bass {\n  c1 |")]
    [InlineData("bass { }", "bass { ")]
    public void TheTypingPlace_IsAfterTheLastTokenAndOneSpace(string block, string upTo)
    {
        var tree = SyntaxTree.Parse("part bass { }\nsection A {\n  " + block + "\n}\n");
        var node = tree.GetRoot().DescendantNodes<PartBlockSyntax>().Single();
        Assert.Equal(node.Span.Start + upTo.Length, LilySharp.Core.Svg.Collector.MeasureCollector.BlockEndForTyping(node));
    }
}
