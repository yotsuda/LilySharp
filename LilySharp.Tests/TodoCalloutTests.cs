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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The preview's <c>@todo</c> marks (owner's decisions 2026-10-10): red ink OVER the page —
/// a ring around the marked head, rest or chord (one ring about a whole chord), and for a
/// memo a callout beside the staff with a leader down to the ring, above by default and
/// below for <c>.down</c>. Nothing on the page moves for a mark, the interactive SVG alone
/// draws them (<c>IDrawingContext.DrawsEditorMarks</c>), and an export carries none.
/// </summary>
[Trait("Category", "Unit")]
public class TodoCalloutTests
{
    private static string Book(string music)
        => $"octave absolute\npart m {{ clef treble }}\nsection A {{ m {{ {music} }} }}\nform {{ ~A }}\nscore {{ staff m }}\n";

    private static readonly Regex Ring = new("<ellipse [^>]*data-todo=\"[^\"]*\"[^>]*/>", RegexOptions.Compiled);
    // The callout's box: the one rect painted on the preview's paper (a head's click
    // target is a rect carrying data-todo too).
    private static readonly Regex CalloutBox = new("<rect [^>]*fill=\"#FFF3F2\"[^>]*data-todo=\"([^\"]*)\"[^>]*/>", RegexOptions.Compiled);

    [Fact]
    public void TheItem_CarriesTheMemoAndTheSide()
    {
        var tree = SyntaxTree.Parse(Book("c'4@todo(\"why\") d'4@todo(k1 \"memo\").down e'4@todo f'4@todo(o1) |"));
        var items = new MeasureCollector().Collect(tree, "m").Voice.Measures[0].Items;
        Assert.Equal(("", "why", false), (items[0].TodoKey, items[0].TodoMemo, items[0].TodoBelow));
        Assert.Equal(("k1", "memo", true), (items[1].TodoKey, items[1].TodoMemo, items[1].TodoBelow));
        Assert.Equal(("", null, false), (items[2].TodoKey, items[2].TodoMemo, items[2].TodoBelow));
        Assert.Equal(("o1", null, false), (items[3].TodoKey, items[3].TodoMemo, items[3].TodoBelow));
    }

    [Fact]
    public void ThePreviewDrawsTheRingsAndTheCallouts_AndTheExportNeither()
    {
        var tree = SyntaxTree.Parse(Book("c'4@todo(\"why\") d'4 r4@todo <e' g' b'>4@todo(\"both\") |"));

        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        // One ring per marked ITEM — the note, the rest, and the chord as a whole — with its
        // paint on the element (fill none and a stroke: the caret highlight fills a shape
        // that carries no stroke attribute, owner's report 2026-10-10).
        var rings = Ring.Matches(preview).Select(m => m.Value).ToList();
        Assert.Equal(3, rings.Count);
        Assert.All(rings, r => Assert.True(r.Contains("fill=\"none\"") && r.Contains("stroke=\"#E53935\""), r));
        // The chord's one ring is taller than a head's: three heads a third apart.
        double ChordRy(string r) => double.Parse(Regex.Match(r, "ry=\"([0-9.]+)\"").Groups[1].Value);
        Assert.True(rings.Max(ChordRy) > 2 * rings.Min(ChordRy));
        // A memo's callout: its box, its text; the bare mark has none.
        Assert.Equal(2, CalloutBox.Matches(preview).Count);
        Assert.Contains(">why<", preview);
        Assert.Contains(">both<", preview);

        string export = SvgGenerator.Generate(tree, SvgRenderOptions.Default);
        Assert.Empty(Ring.Matches(export));
        Assert.Empty(CalloutBox.Matches(export));
        Assert.DoesNotContain(">why<", export);
    }

    [Fact]
    public void Down_PutsTheCalloutBelowTheStaff()
    {
        // The same memo above and below: the box stands over the ring in the one picture and
        // under it in the other. Both are page overlays, so their Y's share a frame (the
        // SVG's Y grows downward; a head's own hit rect is in its system's frame instead).
        string above = SvgGenerator.Generate(SyntaxTree.Parse(Book("c'4@todo(\"memo\") d'4 e'4 f'4 |")), SvgRenderOptions.Preview());
        string below = SvgGenerator.Generate(SyntaxTree.Parse(Book("c'4@todo(\"memo\").down d'4 e'4 f'4 |")), SvgRenderOptions.Preview());
        double BoxY(string svg) => double.Parse(Regex.Match(CalloutBox.Match(svg).Value, "y=\"(-?[0-9.]+)\"").Groups[1].Value);
        double BoxBottom(string svg) => BoxY(svg) + double.Parse(Regex.Match(CalloutBox.Match(svg).Value, "height=\"(-?[0-9.]+)\"").Groups[1].Value);
        double RingY(string svg) => double.Parse(Regex.Match(Ring.Match(svg).Value, "cy=\"(-?[0-9.]+)\"").Groups[1].Value);
        Assert.True(BoxBottom(above) < RingY(above), $"above: box bottom {BoxBottom(above)} ring {RingY(above)}");
        Assert.True(BoxY(below) > RingY(below), $"below: box top {BoxY(below)} ring {RingY(below)}");
    }

    [Fact]
    public void AMark_MovesNothingOnThePage()
    {
        // The same book with the memo and with an empty one (the mark stays, so the export's
        // data-todo stays), written to the same length: the layout is the same system for
        // system — the memo reserves nothing — and the export is byte-identical.
        const string memo = "a long memo that would not fit over one note";
        string marked = Book($"c'4@todo(\"{memo}\") d'4 e'4 f'4 | g'1 |");
        string plain = Book("c'4@todo(\"\")" + new string(' ', memo.Length) + " d'4 e'4 f'4 | g'1 |");
        Assert.Equal(marked.Length, plain.Length);
        var markedLayout = Layout(marked);
        var plainLayout = Layout(plain);
        Assert.Equal(plainLayout.Height, markedLayout.Height);
        Assert.Equal(plainLayout.AllSystems.Select(s => (s.Y, s.Width)), markedLayout.AllSystems.Select(s => (s.Y, s.Width)));
        Assert.Equal(
            SvgGenerator.Generate(SyntaxTree.Parse(plain), SvgRenderOptions.Default),
            SvgGenerator.Generate(SyntaxTree.Parse(marked), SvgRenderOptions.Default));

        static ScoreLayout Layout(string source)
        {
            var tree = SyntaxTree.Parse(source);
            var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
            return new LayoutEngine(score.Paper).Layout(score);
        }
    }
}
