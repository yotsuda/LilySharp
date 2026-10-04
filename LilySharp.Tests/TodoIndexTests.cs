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

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <see cref="TodoIndex"/> and <c>lilysharp/todos</c> (LilySharp-Omr proposal C1): each
/// <c>@todo</c> with its spans, the bar number the page prints for it and its part; and the
/// preview page's bar boxes that number the bars where they stand (proposal C2).
/// </summary>
[Trait("Category", "Unit")]
public class TodoIndexTests
{
    // A pickup (bar 0), two staves, and a part no score draws.
    private const string Book = """
        octave absolute
        time 4/4
        part rh { }
        part lh { clef bass }
        part x { }
        section A {
          partial 4
          rh { g'4 | c''1@todo(r1 "high C?") | d''1 | e''1 | }
          lh { r4 | c1 | d1 | e1@todo(l3) | }
          x { c'4@todo | c'1 | c'1 | c'1 | }
        }
        form main { A }
        score main { staff rh staff lh }
        """;

    [Fact]
    public void EachMark_HasItsSpans_ThePrintedBarNumber_AndItsPart()
    {
        var todos = TodoIndex.Find(SyntaxTree.Parse(Book));

        Assert.Equal(3, todos.Count);
        var (r1, l3, x) = (todos[0], todos[1], todos[2]);
        Assert.Equal(("r1", "high C?", 1, "rh"), (r1.Key, r1.Memo, r1.Measure, r1.Part));
        // The pickup is bar 0, so the fourth measure prints 3.
        Assert.Equal(("l3", (string?)null, 3, "lh"), (l3.Key, l3.Memo, l3.Measure, l3.Part));
        // No score draws x: listed, unlocated.
        Assert.Equal(((string?)null, (int?)null, (string?)null), (x.Key, x.Measure, x.Part));

        Assert.StartsWith("@todo(r1", Book[r1.Start..]);
        Assert.Equal("@todo(r1 \"high C?\")", Book[r1.Start..r1.End]);
        Assert.Equal("c''1@todo(r1 \"high C?\")", Book[r1.HostStart..r1.HostEnd]);
    }

    /// <summary>C2: the preview's page carries one invisible box per bar with the number the
    /// page prints (the pickup is 0), the system's height tall; an exported page carries none.</summary>
    [Fact]
    public void ThePreviewPage_HasABoxPerBar_WithItsPrintedNumber()
    {
        var tree = SyntaxTree.Parse(Book);
        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        var boxes = Regex.Matches(preview,
            @"<rect class=""bar-box"" data-bar=""(\d+)"" x=""([^""]+)"" y=""([^""]+)"" width=""([^""]+)"" height=""([^""]+)""");
        Assert.Equal(new[] { 0, 1, 2, 3 }, boxes.Select(m => int.Parse(m.Groups[1].Value)));
        // Bar after bar: each box starts where the one before it ends.
        for (int i = 1; i < boxes.Count; i++)
            Assert.Equal(Num(boxes[i - 1], 2) + Num(boxes[i - 1], 4), Num(boxes[i], 2), 3);
        // Two staves tall: more than one staff (4 spaces) high.
        Assert.All(boxes, b => Assert.True(Num(b, 5) > 4 * 2));

        Assert.DoesNotContain("data-bar", SvgGenerator.Generate(tree, SvgRenderOptions.Default));

        static double Num(Match m, int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void AFileWithoutMarks_ListsNone()
        => Assert.Empty(TodoIndex.Find(SyntaxTree.Parse(Book.Replace("@todo(r1 \"high C?\")", "")
            .Replace("@todo(l3)", "").Replace("@todo", ""))));

    [Fact]
    public void TheRequest_AnswersTheOpenDocument_AtItsVersion()
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///todos-test.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = Book, Version = 7, LanguageId = "lilysharp" },
        });

        var response = server.Todos(new TodosParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });

        Assert.Null(response.Error);
        Assert.Equal(7, response.Version);
        Assert.Equal(new int?[] { 1, 3, null }, response.Todos.Select(t => t.Measure));
        Assert.Equal(new[] { "rh", "lh", null }, response.Todos.Select(t => t.Part));
        Assert.Equal("high C?", response.Todos[0].Memo);

        var missing = server.Todos(new TodosParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = new Uri("file:///nope.lys") },
        });
        Assert.NotNull(missing.Error);
    }
}
