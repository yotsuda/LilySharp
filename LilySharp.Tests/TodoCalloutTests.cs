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
using LilySharp.Core.Svg.Model;
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

    // A slash note took no mark: `lysc check` counted `/4@todo` while the page dropped it
    // (found writing the slash-chart lesson, 2026-10-12). Now it is ringed like a note.
    [Fact]
    public void ASlashNote_CarriesItsMark_AndThePreviewRingsIt()
    {
        var tree = SyntaxTree.Parse(Book("/4 /@todo(\"push\") / /@todo(k2).down |"));
        var items = new MeasureCollector().Collect(tree, "m").Voice.Measures[0].Items;
        Assert.Equal(("", "push", false), (items[1].TodoKey, items[1].TodoMemo, items[1].TodoBelow));
        Assert.Equal(("k2", null, true), (items[3].TodoKey, items[3].TodoMemo, items[3].TodoBelow));
        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        Assert.Equal(2, RingHeights(preview).Count);
        Assert.Contains(">push<", preview);
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
    public void TheLowerVoicesMemo_GoesBelow_UnlessItWroteItsSide()
    {
        // Owner's decision 2026-10-17: on a staff with two voices the lower voice's memo
        // goes below the staff, where that voice is read, without `.down`; `.up` keeps it
        // above. The upper voice's stays above. Both ways of having two voices on one
        // staff: `voice { } { }` in one part, and two parts on a combinedStaff.
        static (MusicItem Upper, MusicItem Lower) Voiced(string book)
        {
            var tree = SyntaxTree.Parse(book);
            Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            var staff = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).EnumerateStaves().Single().Staff;
            Assert.Equal(2, staff.Voices.Count());
            return (staff.Voices[0].Measures[0].Items[0], staff.Voices[1].Measures[0].Items[0]);
        }
        var (upper, lower) = Voiced(Book("voice { c''4@todo(\"upper\") d''4 e''4 f''4 } { c'4@todo(\"lower\") d'4 e'4 f'4 } |"));
        Assert.Equal(("upper", false), (upper.TodoMemo, upper.TodoBelow));
        Assert.Equal(("lower", true), (lower.TodoMemo, lower.TodoBelow));
        var (_, kept) = Voiced(Book("voice { c''4 d''4 e''4 f''4 } { c'4@todo(\"lower\").up d'4 e'4 f'4 } |"));
        Assert.False(kept.TodoBelow);
        Assert.True(kept.TodoSideWritten);
        var (one, two) = Voiced("octave absolute\npart vone { clef treble }\npart vtwo { clef treble }\n"
            // Different rhythms, so the combiner keeps two voices (the same rhythm would be
            // chords of a shared voice, which has no side).
            + "section A {\n  vone { c''4@todo(\"one\") d''4 e''4 f''4 | }\n  vtwo { c'2@todo(\"two\") d'2 | }\n}\n"
            + "form { A }\nscore { combinedStaff { vone vtwo } }\n");
        Assert.Equal(("one", false), (one.TodoMemo, one.TodoBelow));
        Assert.Equal(("two", true), (two.TodoMemo, two.TodoBelow));
        // And the picture: the lower voice's leader runs down from its ring, the upper's up.
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book("voice { c''4@todo(\"upper\") d''4 e''4 f''4 } { c'4@todo(\"lower\") d'4 e'4 f'4 } |")), SvgRenderOptions.Preview());
        var leaders = Leaders(svg);
        Assert.Equal(2, leaders.Count);
        Assert.Contains(leaders, l => l.Y2 < l.Y1);
        Assert.Contains(leaders, l => l.Y2 > l.Y1);
    }

    [Fact]
    public void AMemo_StandsClearOfTheInkAroundIt()
    {
        // The same marked note three times (the staff's edge is the same in all): followed by
        // notes within the staff; followed by notes high on ledger lines, whose heads and
        // stems stand where the memo would go, right of the ring, just over the staff; and
        // between such notes on both sides. The memo is set where nothing else is drawn
        // (owner's request 2026-10-10: a memo over another mark cannot be read): with the
        // right walled it crosses to the left, no higher, and with both walled it climbs.
        // Distances are leader lengths in Y — ring (y1) to underline (y2) — both page
        // overlays, so one frame; the notes themselves are drawn in the system's own frame
        // and are not compared.
        string bare = Book("c''4@todo(\"memo\") d''4 e''4 f''4 |");
        string rightWalled = Book("c''4@todo(\"memo\") c'''4 d'''4 e'''4 |");
        string bothWalled = Book("c'''4 c''4@todo(\"memo\") c'''4 d'''4 |");
        var bareLeader = Assert.Single(Leaders(SvgGenerator.Generate(SyntaxTree.Parse(bare), SvgRenderOptions.Preview())));
        var crossed = Assert.Single(Leaders(SvgGenerator.Generate(SyntaxTree.Parse(rightWalled), SvgRenderOptions.Preview())));
        var climbed = Assert.Single(Leaders(SvgGenerator.Generate(SyntaxTree.Parse(bothWalled), SvgRenderOptions.Preview())));
        double bareRise = bareLeader.Y1 - bareLeader.Y2, crossedRise = crossed.Y1 - crossed.Y2, climbedRise = climbed.Y1 - climbed.Y2;
        Assert.True(bareLeader.X2 > bareLeader.X1, $"bare: the memo stands to the right ({bareLeader})");
        Assert.True(crossed.X2 < crossed.X1, $"right walled: the memo crosses to the left ({crossed})");
        Assert.True(crossedRise <= bareRise + 0.01, $"bare rise {bareRise}, crossed {crossedRise}");
        Assert.True(climbedRise > bareRise + 1.5, $"bare rise {bareRise}, both walled {climbedRise}");
    }

    [Fact]
    public void ALeader_RunsThroughNoOtherMemo()
    {
        // Five memos on one short system (the p896 probe `one` less one): set short first and
        // long after, each where the ink is clear — and a later memo's leader ran down through
        // an earlier memo's words. A memo's words are the box over its underline, a line of
        // its font's height; no leader passes through another memo's box, and no memo's box
        // has a leader through it. The plain cases: memos on two notes side by side, the
        // first's words running over the second note — the second memo, set where the ink
        // is clear just over the first's words, had its leader run down through them; and
        // four memos on four notes in a row.
        // (A long memo on a note BETWEEN two short ones — the probe's sixth — still has one
        // crossing: its leader must pass a neighbour's words from either side, and the climb
        // that would clear them is dearer than the crossing. Lab sessions/p899/probe.)
        CheckLeaders("c'4@todo(\"F# or F natural? the sharp is faint\") d'4 e'4@todo f'4 | r2@todo(\"bar 12 is only 7/8\").down <e' g' b'>4@todo(\"both\") g'4 | R1@todo(\"tacet?\") | c''1@todo(\"high note\") |", 5);
        CheckLeaders("c''4@todo(\"first memo here\") d''4@todo(\"second memo here\") e''4 f''4 |", 2);
        CheckLeaders("c''4@todo(\"first\") d''4@todo(\"second\") e''4@todo(\"third\") f''4@todo(\"fourth\") |", 4);
        // A memo whose words, on its roomier side, would stand over a later memo's note: the
        // later one's leader must then pass through them from either side, so the first takes
        // its narrower side (the margin) instead — the arrangement that costs least over the
        // system, not the memo's own cheapest place.
        CheckLeaders("c''4@todo(\"first memo here\") d''4 e''4@todo(\"second memo here\") f''4 |", 2);
    }

    [Theory]
    [InlineData("3/4", "c'4@todo(\"memo\") d'4 e'4 |")]
    [InlineData("6/8", "c'4.@todo(\"memo\") d'4. |")]
    [InlineData("12/8", "c'1.@todo(\"memo\") |")]
    public void AMemo_InAScoreWithANumericMeter_StillRenders(string meter, string music)
    {
        // The memo's placing records the page's ink, glyph by glyph, from each glyph's design
        // box — and the time-signature digits have none (only an outline box), so the preview
        // of ANY score with a memo and a numeric meter threw "the music font has no box for
        // timeSig3" and drew nothing (found 2026-10-11, writing the lessons: 4/4 draws the C
        // glyph and passed, 3/4 and 6/8 failed).
        var tree = SyntaxTree.Parse($"time {meter}\n" + Book(music));
        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        Assert.Single(MemoBoxes(preview));
    }

    [Fact]
    public void TheLayoutReservesTheMemosRows_InThePreviewOnly()
    {
        // The p896 probe `one`: six memos on one system with no title over it. The preview's
        // layout reserves the rows the memos need as a band over the system
        // (LayoutOptions.TodoBands, set by the preview's render options): its page is taller
        // than the same book's export page, and every memo stands clear of every other —
        // they used to pile up at the page's top. The export's layout reserves nothing
        // (AMark_MovesNothingOnThePage). And a session's incremental render starts from the
        // same paper as the full render, so the two are the same picture.
        string book = Book("c'4@todo(\"F# or F natural? the sharp is faint\") d'4 e'4@todo f'4 | r2@todo(\"bar 12 is only 7/8\").down <e' g' b'>4@todo(\"both\") g'4@todo(\"a second memo, right beside\") | R1@todo(\"tacet?\") | c''1@todo(\"high note\") |");
        var tree = SyntaxTree.Parse(book);
        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        string export = SvgGenerator.Generate(tree, SvgRenderOptions.Default);
        static double PageHeight(string svg) => double.Parse(Regex.Match(svg, "viewBox=\"0 0 [0-9.]+ ([0-9.]+)\"").Groups[1].Value);
        Assert.True(PageHeight(preview) > PageHeight(export) + 4, $"preview page {PageHeight(preview)}, export page {PageHeight(export)}");
        var boxes = MemoBoxes(preview);
        Assert.Equal(6, boxes.Count);
        CheckLeadersOf(preview, 6);
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i];
                var b = boxes[j];
                bool apart = a.Right <= b.Left || b.Right <= a.Left || a.Bottom <= b.Top || b.Bottom <= a.Top;
                Assert.True(apart, $"memos {i} and {j} overlap: {a} / {b}");
            }
        Assert.Equal(preview, new IncrementalCompiler(tree, SvgRenderOptions.Preview()).Render());

        // And a memo that fits in the room the page has anyway moves nothing (owner's report
        // 2026-10-11: a band over every marked system widened the spacing for nothing): a
        // memo on a note inside the staff, under a system whose section mark stands higher
        // than the memo's box, leaves the preview page as tall as the export's. (The note is
        // e', not the g' it was: since the reservation carries the air the drawer keeps round
        // a memo and starts at the ring's top, a g' memo's box stands 0.05 over the mark.)
        var fits = SyntaxTree.Parse("title \"Room above\"\noctave absolute\npart m { clef treble }\nsection A { m { c'4 d'4 e'4 f'4 | e'4@todo(\"memo\") a'4 b'4 c''4 | } }\nform { A }\nscore { staff m }\n");
        string fitsPreview = SvgGenerator.Generate(fits, SvgRenderOptions.Preview());
        Assert.Single(Leaders(fitsPreview));
        Assert.Equal(PageHeight(SvgGenerator.Generate(fits, SvgRenderOptions.Default)), PageHeight(fitsPreview));
    }

    [Theory]
    [InlineData("m { e'4 g c g | a2 e | f4 a c@todo(\"or G?\") a | g2. r4 | }", "score { staff m }")]
    [InlineData("m { e'4 g c g | a2 e | f4 a c a | g2. r4 | }\n  chords prog { C | Am7@todo(\"Am7 or C?\") | F . G7@todo(\"or G?\") . | G | }", "score { chords prog  staff m }")]
    public void AMemo_UnderTheTitle_StaysUnderIt(string music, string score)
    {
        // A first system's memo, on a high note or on a chords row's symbol, climbed OVER the
        // title with its leader across the letters (2026-10-11, Lab sessions/p902/probe
        // `notetodo`, `chordtodo`): the page's ink recorder floored every text's box at 0.22 em
        // under its baseline, so a title without descenders fenced off the room under it that
        // the layout had reserved — and the reservation itself stopped at the memo's top,
        // without the air the drawer keeps round it, and under the ring's top. Every memo
        // stands under the title's baseline (the title has no descenders).
        var tree = SyntaxTree.Parse($"title \"Chord doubts\"\npart m {{ clef treble }}\nsection A {{\n  {music}\n}}\nform {{ ~A }}\n{score}\n");
        string preview = SvgGenerator.Generate(tree, SvgRenderOptions.Preview());
        double titleBaseline = double.Parse(Regex.Match(preview, "<text [^>]*y=\"(-?[0-9.]+)\"[^>]*>Chord doubts<").Groups[1].Value);
        var boxes = MemoBoxes(preview);
        Assert.NotEmpty(boxes);
        foreach (var b in boxes)
            Assert.True(b.Top > titleBaseline, $"a memo over the title: memo top {b.Top}, title baseline {titleBaseline}");
    }

    /// <summary>Each memo's box in the SVG's frame (Y down): its underline's span, and the
    /// line of text over it, a face's height.</summary>
    private static List<(double Left, double Right, double Top, double Bottom)> MemoBoxes(string svg)
    {
        double em = double.Parse(Regex.Match(svg, "<text [^>]*font-size=\"([0-9.]+)\"[^>]*fill=\"#E53935\"[^>]*data-todo=").Groups[1].Value);
        return Underlines(svg).Select(u => (Left: u.X1, Right: u.X2, Top: u.Y1 - 0.9 * em, Bottom: u.Y1)).ToList();
    }

    private static void CheckLeaders(string music, int memos)
        => CheckLeadersOf(SvgGenerator.Generate(SyntaxTree.Parse("title \"Room above\"\n" + Book(music)), SvgRenderOptions.Preview()), memos);

    private static void CheckLeadersOf(string svg, int memos)
    {
        var leaders = Leaders(svg);
        var underlines = Underlines(svg);
        Assert.Equal(memos, leaders.Count);
        Assert.Equal(memos, underlines.Count);
        // A memo's box: its underline's span, and the line of text over it (SVG's Y grows
        // downward, so the words lie at smaller Y than the underline — for the .down memo
        // too, whose leader ends at the top of its words instead of at its underline).
        var boxes = MemoBoxes(svg);
        int crossings = 0;
        foreach (var l in leaders)
            foreach (var b in boxes)
            {
                // The leader's own memo is the one whose centre it ends under or over.
                if (Math.Abs(l.X2 - (b.Left + b.Right) / 2) < 0.01)
                    continue;
                if (SegmentMeetsBox(l.X1, l.Y1, l.X2, l.Y2, b.Left, b.Right, b.Top, b.Bottom))
                    crossings++;
            }
        Assert.True(crossings == 0, $"{crossings} leader(s) through another memo: {string.Join(" ", leaders)} / {string.Join(" ", boxes)}");

        static bool SegmentMeetsBox(double x1, double y1, double x2, double y2, double left, double right, double top, double bottom)
        {
            double t0 = 0, t1 = 1, dx = x2 - x1, dy = y2 - y1;
            return Clip(-dx, x1 - left) && Clip(dx, right - x1) && Clip(-dy, y1 - top) && Clip(dy, bottom - y1);
            bool Clip(double p, double q)
            {
                if (p == 0) return q >= 0;
                double t = q / p;
                if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
                return true;
            }
        }
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
