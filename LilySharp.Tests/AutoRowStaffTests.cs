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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A score of rows alone — chords and lyrics, no staff — draws a one-line staff they stand on,
/// above the first lyrics row, else last; the rows keep the order written (owner's decision
/// 2026-11-15, docs/row-staff-design.md). Until then such a score drew its rows alone, and a
/// line of empty bars was a blank line.
/// </summary>
public class AutoRowStaffTests
{
    private const string Head = "octave absolute\ntime 4/4\npart m { instrument soprano  lyrics en }\n"
        + "section A { m { c''2 d'' | e''1 | | | } lyrics en { one two | three | | | } }\n"
        + "chords prog { section A { C | G7 | | | } }\nform { A }\n";

    private static SyntaxTree Book(string score) => SyntaxTree.Parse(Head + "score { " + score + " }\n");

    /// <summary>The score's items, top to bottom: "C" a chords row, "L" a lyrics row, "S" the
    /// automatic staff, "s" a staff of the book's.</summary>
    private static string Items(string score)
        => string.Concat(RenderSpecParser.FindFirst(Book(score))!.Items.Select(i => i switch
        {
            ChordRowSpec => "C",
            LyricsRowSpec => "L",
            SingleStaffSpec s when RenderSpecParser.IsAutoStaff(s.Staff.VoiceName) => "S",
            SingleStaffSpec => "s",
            _ => "?",
        }));

    [Theory]
    [InlineData("chords prog", "CS")]
    [InlineData("lyrics en", "SL")]
    [InlineData("chords prog  lyrics en", "CSL")]
    [InlineData("lyrics en  chords prog", "SLC")]     // the rows keep their order: the staff above the lyrics
    // A score with a staff of its own gets none.
    [InlineData("chords prog  staff m  lyrics en", "Cs")]
    public void TheAutomaticStaff_StandsAboveTheFirstLyricsRowElseLast(string score, string items)
        => Assert.StartsWith(items, Items(score));

    /// <summary>
    /// The automatic staff IS the hand-written one: a part with no music placed
    /// <c>staff l as lines 1</c> where the automatic one stands draws the same page, bar for bar
    /// and mark for mark (docs/row-staff-design.md — the shape the owner's "My Life (chord).lys"
    /// wrote by hand). The pictures that stand on the staff — the label at the line start, the
    /// tempo, the endings, a diagram, the breaks — are the staff's rules then, not a second set.
    /// </summary>
    [Theory]
    [InlineData("chords prog { section A { C | G7 | Am | F | } }\nform { A }", "chords prog")]
    [InlineData("tempo 100\nsection A { }\nsection B { tempo 4 = 90 }\nchords prog { section A { Cmaj7 | Dm7 | } section B { Am | F | } }\nform { A B }", "chords prog")]
    [InlineData("chords prog { section A { Bm7-5 | Dm7(xx0211) Dm7 | Dm Fmaj7 | } }\nform { A }", "chords prog  chords prog as roman")]
    [InlineData("chords prog { section A { |: D9 | C9 | break | | | A | [1. A7] [2. ] } }\nform { A }", "chords prog")]
    [InlineData("chords prog { C | G | [1. Am :| [2. F ] | C | }", "chords prog")]
    [InlineData("part melody { section A { c'4 c g' g | a a g2 | } }\nchords prog { section A { C | F | } }\n"
        + "lyrics verse { section A { one two | three four | } }\nform { |: A :| A \"A2\" }", "chords prog as names  lyrics verse on melody")]
    // A row that names no melody: under the hand-written staff it used to sing that staff's part,
    // which has no note — every word vanished, silently (RenderSpecParser.SungStaffAbove).
    [InlineData("tempo 100\nchords prog { section A { Cmaj7 | Dm7 | } }\nlyrics verse { section A { one two three four | five six sev- en | } }\nform { A }",
        "chords prog  lyrics verse")]
    public void ThePage_IsAPartWithNoMusic_PlacedAsLines1(string body, string rows)
    {
        static string Svg(string source)
        {
            var tree = SyntaxTree.Parse(source);
            Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
            var svg = SvgGenerator.Generate(tree, new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
            // What is drawn, and every height it is drawn at — not the X: the automatic staff's
            // bars are spaced by the rows (MultiStaffScore.RowsSpaceTheGrid), as the rows alone
            // were, so the words keep inside their bars; a staff of the book's is spaced as a staff.
            var texts = System.Text.RegularExpressions.Regex.Matches(svg, ">([^<>]+)</text>").Select(m => m.Groups[1].Value).Order();
            var ys = System.Text.RegularExpressions.Regex.Matches(svg, " (?:y|y1|y2|height)=\"([0-9.-]+)\"").Select(m => m.Groups[1].Value).Order();
            return string.Join(" ", texts) + "\n" + string.Join(" ", ys);
        }
        string head = "octave absolute\ntime 4/4\n" + body + "\n";
        // Where the automatic staff stands: above the first lyrics row, else last.
        int lyricsAt = rows.IndexOf("lyrics", System.StringComparison.Ordinal);
        string byHand = lyricsAt < 0 ? rows + "  staff l as lines 1"
            : rows[..lyricsAt] + "staff l as lines 1  " + rows[lyricsAt..];
        Assert.Equal(Svg(head + "part l { }\nscore { " + byHand + " }\n"), Svg(head + "score { " + rows + " }\n"));
    }

    /// <summary>
    /// The words hang 3.5 staff spaces below the one line on every system — where a five-line
    /// staff's bottom line would leave them (owner's request 2026-11-15, "a natural-looking
    /// gap"; ScoreAssembler.RowStaffPaper). LilyPond's 5.5 runs from the middle line, which IS
    /// the line here: the first system, squeezed by the next, stood 4.4 below and the last, at
    /// rest, 5.5 — two gaps on one page. Two verses, so the squeeze is in reach.
    /// </summary>
    [Theory]
    [InlineData("")]                                         // the automatic staff
    [InlineData("part l { }\n")]                             // a hand-written staff of a part with no music
    public void TheWords_Hang3Point5BelowTheLine_OnEverySystem(string part)
    {
        string staff = part.Length == 0 ? "" : "staff l as lines 1  ";
        string src = "time 4/4\n" + part
            + "chords prog { section A { C | F | G | C } section B { Am | G } }\n"
            + "lyrics verse { section A { one two | three four | five six | sev- en | }\n"
            + "  section B { verse ~1 { eight nine | ten e- } verse ~2 { le- ven | twelve thir- } } }\n"
            + "form { A |: B :| A \"A2\" }\n"
            + "score { chords prog as names  " + staff + "lyrics verse }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var svg = SvgGenerator.Generate(tree, new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        var lines = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([0-9.]+)\" y1=\"([0-9.]+)\" x2=\"([0-9.]+)\" y2=\"\\2\"[^>]*stroke-width=\"0.100\"")
            .Where(m => double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)
                - double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > 30)
            .Select(m => double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToList();
        Assert.Equal(2, lines.Count);
        var words = System.Text.RegularExpressions.Regex.Matches(svg, "<text x=\"[0-9.]+\" y=\"([0-9.]+)\" font-size=\"2.47\"")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToList();
        foreach (var line in lines)
        {
            double first = words.Where(y => y > line).Min();
            Assert.True(System.Math.Abs(first - line - 3.5) < 0.011,
                $"the words under the line at {line:F2} stand {first - line:F2} below it, not 3.5");
        }
    }

    [Fact]
    public void TheAutomaticStaff_IsOneLineWithNoName()
    {
        var staff = RenderSpecParser.FindFirst(Book("chords prog"))!.Items.OfType<SingleStaffSpec>().Single().Staff;
        Assert.Equal(1, staff.Lines);
        Assert.True(staff.NameSuppressed);
        Assert.Null(RenderSpecParser.SingleEngravedPart(Book("chords prog"), null, null));
    }

    /// <summary>On the page: the staff has every bar the rows have — the empty ones too, which
    /// drew nothing at all before.</summary>
    [Theory]
    [InlineData("chords prog")]
    [InlineData("lyrics en")]
    public void ThePage_DrawsTheStaffWithEveryBar(string score)
    {
        var tree = Book(score);
        var collected = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        var staff = collected.StaffGroups.SelectMany(g => g.Staves).Single(s => !s.IsTextRow);
        Assert.Equal(1, staff.Lines);
        Assert.Equal(4, staff.PrimaryVoice.Measures.Length);
        Assert.False(collected.IsLeadSheet);
        Assert.True(collected.RowsSpaceTheGrid);
    }

    /// <summary>The words still size their bars, as on a lead sheet: no bar is narrower than
    /// a readable cell, and "three" fits in its bar.</summary>
    [Fact]
    public void TheWords_StillSizeTheirBars()
    {
        var tree = Book("chords prog  lyrics en");
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        var widths = layout.Systems.SelectMany(s => s.Measures).Select(m => m.Width).ToList();
        Assert.Equal(4, widths.Count);
        // The two bars with words (the empty two are an empty bar's width).
        Assert.All(widths.Take(2), w => Assert.True(w >= 10.0, $"bar width {w}"));
    }

    [Fact]
    public void TheSound_IsTheRowsAlone()
    {
        var tree = Book("chords prog");
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        Assert.DoesNotContain(midi.Tracks, t => t.Name == RenderSpecParser.AutoStaffPart);
        Assert.DoesNotContain(midi.Tracks.SelectMany(t => t.Notes), n => n.Part == RenderSpecParser.AutoStaffPart);
    }

    [Fact]
    public void MusicXml_WritesAOneLinePartWithTheHarmonies()
    {
        var xml = new MusicXmlExporter().Export(Book("chords prog")).ToXml();
        var part = Assert.Single(xml.Descendants("part"), p => p.Descendants("staff-lines").Any(l => l.Value == "1"));
        Assert.Equal(4, part.Elements("measure").Count());
        Assert.Equal(2, part.Descendants("harmony").Count());
        var name = xml.Descendants("score-part").Single(p => p.Attribute("id")!.Value == part.Attribute("id")!.Value).Element("part-name")!;
        // The row's own part, as before the staff, its name not printed (the page prints none).
        Assert.Equal("prog (chords)", name.Value);
        Assert.Equal("no", (string?)name.Attribute("print-object"));
    }

    [Theory]
    [InlineData("chords prog  lyrics en", @"\\new ChordNames \\progChords\s+\\new RhythmicStaff \\leadSheetTiming\s+\\new Lyrics")]
    [InlineData("lyrics en  chords prog", @"<<\s+\\new RhythmicStaff \\leadSheetTiming\s+\\new Lyrics \\\w+\s+\\new ChordNames")]
    [InlineData("chords prog", @"\\new ChordNames \\progChords\s+\\new RhythmicStaff \\leadSheetTiming\s+>>")]
    public void TheTwin_WritesARhythmicStaffInItsPlace(string score, string pattern)
    {
        string ly = new LilyPondExporter().Export(Book(score));
        Assert.Matches(pattern, ly);
        Assert.DoesNotContain(@"\new Devnull \leadSheetTiming", ly);
    }
}
