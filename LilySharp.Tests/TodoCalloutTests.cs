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
using System.Collections.Generic;
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

    // A ring is eight round-capped cubic paths (a hand's loop); they share the mark's data-pos.
    private static readonly Regex RingPath = new("<path d=\"([^\"]*)\"[^>]*fill=\"none\"[^>]*stroke=\"#E53935\"[^>]*data-todo=\"[^\"]*\" data-pos=\"(\\d+)\"", RegexOptions.Compiled);
    // The memo's two red lines: its underline (level) and the leader from the ring to the
    // underline's centre.
    private static readonly Regex RedLine = new("<line x1=\"(-?[0-9.]+)\" y1=\"(-?[0-9.]+)\" x2=\"(-?[0-9.]+)\" y2=\"(-?[0-9.]+)\"[^>]*stroke=\"#E53935\"[^>]*data-todo=", RegexOptions.Compiled);

    private static List<(double X1, double Y1, double X2, double Y2)> RedLines(string svg)
        => RedLine.Matches(svg).Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value),
            double.Parse(m.Groups[3].Value), double.Parse(m.Groups[4].Value))).ToList();

    private static List<(double X1, double Y1, double X2, double Y2)> Underlines(string svg)
        => RedLines(svg).Where(l => l.Y1 == l.Y2).ToList();

    private static List<(double X1, double Y1, double X2, double Y2)> Leaders(string svg)
        => RedLines(svg).Where(l => l.Y1 != l.Y2).ToList();

    /// <summary>Each ring's height on the page, by the mark's data-pos: the span of the Y's
    /// its paths visit.</summary>
    private static Dictionary<int, double> RingHeights(string svg)
    {
        var ys = new Dictionary<int, (double Min, double Max)>();
        foreach (Match m in RingPath.Matches(svg))
        {
            int pos = int.Parse(m.Groups[2].Value);
            var numbers = Regex.Matches(m.Groups[1].Value, "-?[0-9.]+").Select(n => double.Parse(n.Value)).ToList();
            for (int i = 1; i < numbers.Count; i += 2)
            {
                var (min, max) = ys.TryGetValue(pos, out var r) ? r : (double.MaxValue, double.MinValue);
                ys[pos] = (Math.Min(min, numbers[i]), Math.Max(max, numbers[i]));
            }
        }
        return ys.ToDictionary(kv => kv.Key, kv => kv.Value.Max - kv.Value.Min);
    }

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
        // One ring per marked ITEM — the note, the rest, and the chord as a whole — drawn as
        // a hand's loop (eight paths, fill none and a stroke: the caret highlight fills a
        // shape that carries no stroke attribute, owner's report 2026-10-10).
        var rings = RingHeights(preview);
        Assert.Equal(3, rings.Count);
        Assert.Equal(24, RingPath.Matches(preview).Count);
        // The chord's one ring is taller than a head's: three heads a third apart.
        Assert.True(rings.Values.Max() > 2 * rings.Values.Min(), string.Join(" ", rings.Values));
        // A memo is a line of text, underlined, with a leader from the ring to the underline's
        // centre, and NO box (owner's requests 2026-10-10); the bare mark has none of them.
        // The underline, the leader and the ring share one pen.
        Assert.Equal(2, Underlines(preview).Count);
        Assert.Equal(2, Leaders(preview).Count);
        foreach (var leader in Leaders(preview))
        {
            var underline = Assert.Single(Underlines(preview), u => Math.Abs(u.Y1 - leader.Y2) < 0.01);
            Assert.Equal((underline.X1 + underline.X2) / 2, leader.X2, 2);
        }
        Assert.Equal(24 + 4, Regex.Matches(preview, "stroke-width=\"0\\.200\"[^>]*data-todo=").Count);
        Assert.DoesNotContain("fill=\"#FFF3F2\"", preview); // p893's paper box is gone
        Assert.Contains(">why<", preview);
        Assert.Contains(">both<", preview);

        string export = SvgGenerator.Generate(tree, SvgRenderOptions.Default);
        Assert.Empty(RingPath.Matches(export));
        Assert.Empty(RedLines(export));
        Assert.DoesNotContain(">why<", export);
    }

    [Fact]
    public void Down_PutsTheMemoBelow_AndTheMemoStandsOffToTheRoomierSide()
    {
        // The same memo above and below: the leader runs from the ring (x1, y1) to the text's
        // near end (x2, y2) — up in the one picture, down in the other (the SVG's Y grows
        // downward; both are page overlays, so their Y's share a frame). And the text is set
        // OFF the note's column: a first-bar note has more room to its right, so the leader
        // runs to the right (owner's requests 2026-10-10).
        string above = SvgGenerator.Generate(SyntaxTree.Parse(Book("c'4@todo(\"memo\") d'4 e'4 f'4 |")), SvgRenderOptions.Preview());
        string below = SvgGenerator.Generate(SyntaxTree.Parse(Book("c'4@todo(\"memo\").down d'4 e'4 f'4 |")), SvgRenderOptions.Preview());
        var up = Assert.Single(Leaders(above));
        var down = Assert.Single(Leaders(below));
        Assert.True(up.Y2 < up.Y1, $"above: text end {up.Y2} ring {up.Y1}");
        Assert.True(down.Y2 > down.Y1, $"below: text end {down.Y2} ring {down.Y1}");
        // The text starts right of the ring, by the one offset every memo gets.
        double TextX(string svg) => double.Parse(Regex.Match(svg, "<text [^>]*x=\"(-?[0-9.]+)\"[^>]*data-todo=\"\"[^>]*>memo<").Groups[1].Value);
        Assert.True(TextX(above) > up.X1 + 0.5, $"the memo stands off to the right: ring {up.X1}, text {TextX(above)}");
        Assert.True(TextX(below) > down.X1 + 0.5, $"the memo stands off to the right: ring {down.X1}, text {TextX(below)}");
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
