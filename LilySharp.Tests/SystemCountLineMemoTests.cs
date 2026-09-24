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
using System.Text;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The system-count loop's line memo crosses keystrokes by VALUE (session 554,
/// <c>LayoutEngine.ChooseSystemCount</c>'s <c>t_builtLines</c>): a candidate line whose
/// measures did not change reuses last keystroke's <c>SystemDetails</c>, a line whose
/// estimate moved is rebuilt.
/// </summary>
/// <remarks>
/// ⚠️ THE SUITE HAD NO OBSERVER OF THE VALUE HALF. Session 554's poison — the memo answering
/// by (start, end) alone — moved 1,423 of the corpus's 5,816 keystroke pages (the stale line
/// priced a taller system as its old self, and the count loop picked another line count)
/// and left every one of 8,921 tests green. This book is the net: several pages of plain
/// bars, then one keystroke that raises a bar high enough to change its line's height, and
/// the claim that the incremental page set is the full render's — the absolute claim, not
/// "something changed".
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SystemCountLineMemoTests
{
    private static string Book(string bar14)
    {
        var sb = new StringBuilder();
        sb.Append("title \"Line memo\"\ntime 4/4\npart m { clef treble section A {\n");
        for (int i = 0; i < 120; i++)
            sb.Append(i == 14 ? bar14 : "c4 d e f | ");
        sb.Append("\n} }\nform main { A }\nscore main { staff m }\n");
        return sb.ToString();
    }

    [Fact]
    public void ABarRaisedAboveTheStaff_RepricesItsLine_AndThePagesAreTheFullRenders()
    {
        var options = new SvgRenderOptions { EmbedFont = false };
        string before = Book("c4 d e f | ");
        // The edit: bar 15 leaps three octaves up — ledger lines raise the line's estimated
        // height, so every candidate line holding it must be rebuilt, not replayed.
        string after = Book("c'''4 d''' e''' f''' | ");
        Assert.NotEqual(before, after);

        var session = new IncrementalCompiler(SyntaxTree.Parse(before), options);
        var first = session.RenderIncrementalPages(SyntaxTree.Parse(before), default);
        Assert.True(first.Pages.Length >= 2, $"the book should page, not {first.Pages.Length}");

        // The liveness half: the same text again builds NO line (every candidate line hits
        // by value), and the raised bar's keystroke builds the lines that hold it (a hit
        // by key alone would build none — session 554's poison).
        // A pitch moved INSIDE the staff: the same widths, the same heights, every line hits.
        string same = Book("d4 d e f | ");
        LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineBuilds = 0;
        LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineLookups = 0;
        session.RenderIncrementalPages(SyntaxTree.Parse(same), default);
        Assert.True(LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineLookups > 0, "the count loop did not run");
        Assert.Equal(0, LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineBuilds);

        LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineBuilds = 0;
        LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineLookups = 0;
        LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineValueMisses = 0;
        var set = session.RenderIncrementalPages(SyntaxTree.Parse(after), default);
        int lookups = LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineLookups;
        int rebuilt = LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineBuilds;
        int valueMisses = LilySharp.Core.Svg.Layout.LayoutEngine.t_estimatedLineValueMisses;
        // The lines holding the raised bar keep their (start, end) — the widths did not move —
        // and are found by key with OTHER inputs: the value half must refuse them.
        Assert.True(valueMisses >= 1,
            $"the raised bar's lines were replayed from the last keystroke (lookups {lookups}, built {rebuilt}, value misses {valueMisses})");

        // The equality half: the pages are the full render's.
        string full = SvgGenerator.Generate(SyntaxTree.Parse(after), options).Replace("\r\n", "\n");
        Assert.Equal(full, set.ToSvg().Replace("\r\n", "\n"));
    }
}
