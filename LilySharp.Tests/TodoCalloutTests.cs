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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The preview's <c>@todo</c> marks (owner's decisions 2026-10-10): a marked head or rest
/// wears a RING (not a red fill), and a memo stands in a CALLOUT beside the staff — above by
/// default, <c>.down</c> below — with a leader to the ring. Both are the preview's: the
/// callout exists only when the collector's <c>TodoCallouts</c> is on (the preview's render
/// options set it), the ring is drawn by the interactive SVG alone, and an export is laid out
/// and drawn as if the memo were not written.
/// </summary>
[Trait("Category", "Unit")]
public class TodoCalloutTests
{
    private static string Book(string music)
        => $"part m {{ clef treble }}\nsection A {{ m {{ {music} }} }}\nform {{ ~A }}\nscore {{ staff m }}\n";

    [Fact]
    public void AMemo_IsACallout_InThePreviewOnly()
    {
        var tree = SyntaxTree.Parse(Book("c'4@todo(\"why\") d'4 e'4@todo f'4@todo(o1) |"));

        var preview = new MeasureCollector { TodoCallouts = true }.Collect(tree, "m");
        var callout = Assert.Single(preview.Dynamics);
        Assert.True(callout.IsTodoCallout);
        Assert.True(callout.IsExpressiveText);
        Assert.Equal("why", callout.Text);
        Assert.True(callout.IsAbove);          // UP by default, where @text is DOWN
        Assert.Equal("", callout.TodoKey);     // no key written
        Assert.Equal(0, callout.ItemIndex);

        // The bare mark and the key alone are the ring alone; an export has no callout at all.
        Assert.Empty(new MeasureCollector().Collect(tree, "m").Dynamics);
    }

    [Fact]
    public void Down_PutsTheCalloutBelow_AndTheKeyTravels()
    {
        var tree = SyntaxTree.Parse(Book("c'4@todo(k1 \"memo\").down d'4 |"));
        var callout = Assert.Single(new MeasureCollector { TodoCallouts = true }.Collect(tree, "m").Dynamics);
        Assert.False(callout.IsAbove);
        Assert.Equal("k1", callout.TodoKey);
        Assert.Equal("memo", callout.Text);
    }

    [Fact]
    public void ThePreviewDrawsTheRingAndTheCallout_AndTheExportNeither()
    {
        var tree = SyntaxTree.Parse(Book("c'4@todo(\"why\") d'4 r4@todo <e' g'>4@todo(\"both\").down |"));

        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        // One ring per marked glyph: the note, the rest, and each head of the chord.
        Assert.Equal(4, preview.Split("class=\"todo-ring\"").Length - 1);
        // The ring's paint is on the element — fill none AND a stroke — because the
        // preview's caret highlight fills a shape that carries no stroke attribute
        // (the ring came up as a red disc under the caret, owner's report 2026-10-10).
        Assert.Equal(4, preview.Split("class=\"todo-ring\"").Skip(1)
            .Count(after => after[..after.IndexOf("/>")] is var ring
                && ring.Contains("fill=\"none\"") && ring.Contains("stroke=\"#E53935\"")));
        Assert.Contains(">why<", preview);
        Assert.Contains(">both<", preview);
        // The box, the leader and the memo carry the mark (data-todo), as the ring does.
        Assert.Contains("<rect", preview);
        Assert.Contains("data-todo=\"\"", preview);

        string export = SvgGenerator.Generate(tree, SvgRenderOptions.Default);
        Assert.DoesNotContain("todo-ring", export);
        Assert.DoesNotContain(">why<", export);
        Assert.DoesNotContain(">both<", export);
    }

    [Fact]
    public void TheExport_IsLaidOutAsIfTheMemoWereNotWritten()
    {
        // The same book with the memo and without it, written to the same length so every
        // data-pos agrees: the exported pictures are byte-identical.
        string withMemo = Book("c'4@todo(\"why\") d'4 e'4 f'4 | g'1 |");
        string without = Book("c'4@todo(\"\")    d'4 e'4 f'4 | g'1 |");
        Assert.Equal(withMemo.Length, without.Length);
        Assert.Equal(
            SvgGenerator.Generate(SyntaxTree.Parse(without), SvgRenderOptions.Default),
            SvgGenerator.Generate(SyntaxTree.Parse(withMemo), SvgRenderOptions.Default));
    }
}
