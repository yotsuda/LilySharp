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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>break</c> written INSIDE a bar, with music on both sides of it, breaks the line
/// there (owner's decision 2026-09-09, HANDOFF §3; it used to fall to the next bar line —
/// §2 F ⒥, decided against on 2026-08-29 with zero books asking, and re-opened when
/// <c>time none</c> landed, since an unmetered span has no bar line to break at).
/// <para>
/// LilyPond 2.26.0's answers, measured on scratch/p357/lp (2026-09-09): <c>c4 d \break e f |
/// g1 | a1 |</c> ends its first system at the break's column with NO BarLine grob, opens the
/// second at moment 1/2 with the clef alone (first note x 5.8 — exactly where a bar-line
/// break puts it, mb8.ly) and prints NO BarNumber there (mb8.ly prints "2"); a tie and a slur
/// run across (mb6.ly); a lower staff holding a whole note across the break is broken under
/// it, the rest of its bar drawn empty (mb2.ly); a beam across it is broken into two pieces,
/// one per system (mb3.ly, two PROBEBEAM). On the fixture's twin the second system's bar lines
/// stand at 11.568 / 19.718 / 27.868 against Lily#'s 11.57 / 19.72 / 27.87.
/// </para>
/// <para>
/// The model: the bar becomes TWO measures in every voice — a head (<see cref="Measure.BreaksMidBar"/>,
/// no end bar line, the break forced) and a tail (<see cref="Measure.ContinuesBar"/>) — settled
/// score-wide by <see cref="MidBarBreakTable"/> on a second collect. Where a voice cannot be cut at
/// the offset (a sounding item across it, a beam, a tuplet, a percent repeat, an unmetered bar, a
/// second break in the bar) the bar is not split, the break falls to the bar line as before, and
/// LYS1037 names the reason. The sounding-item refusal is LILYSHARP-OWN (LilyPond breaks anyway).
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public class MidBarBreakTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Book(string melody, string? bass = null, string? rows = null, string? scoreExtra = null)
    {
        string bassPart = bass == null ? "" : "part bass { clef bass }\n";
        string bassCell = bass == null ? "" : $"  bass {{ {bass} }}\n";
        string bassStaff = bass == null ? "" : " staff bass";
        return $$"""
            paper { raggedRight }
            time 4/4
            key c major
            part melody { clef treble }
            {{bassPart}}section Main {
              melody { {{melody}} }
            {{bassCell}}}
            {{rows ?? ""}}
            form main { Main }
            score main { staff melody{{bassStaff}}{{scoreExtra ?? ""}} }
            """;
    }

    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static ImmutableArrayOfMeasures Melody(string source)
        => new(Collect(source).PrimaryContentStaff.PrimaryVoice.Measures);

    private static string Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, Opt);
    }

    private static string MaskDataPos(string svg)
        => Regex.Replace(svg, @"data-pos=""\d+""", "data-pos=\"\"");

    /// <summary>Bar-line rects (thin, staff-high) grouped by system row, X ascending.</summary>
    private static List<List<double>> BarLineRows(string svg)
        => Regex.Matches(svg, "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                          Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                          W: double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                          H: double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .Where(r => r.W < 0.5 && r.H > 3)
            .GroupBy(r => System.Math.Round(r.Y, 1))
            .OrderBy(g => g.Key)
            .Select(g => g.Select(r => r.X).Distinct().OrderBy(x => x).ToList())
            .ToList();

    private static List<string> BoldNumbers(string svg)
        => Regex.Matches(svg, "<text[^>]*font-weight=\"bold\"[^>]*>(\\d+)</text>")
            .Select(m => m.Groups[1].Value).ToList();

    private sealed class ImmutableArrayOfMeasures
    {
        public System.Collections.Immutable.ImmutableArray<Measure> Measures { get; }
        public ImmutableArrayOfMeasures(System.Collections.Immutable.ImmutableArray<Measure> m) => Measures = m;
    }

    // ---- the split ---------------------------------------------------------------------

    [Fact]
    public void ABreakInsideABar_SplitsTheBarIntoAHeadAndATail()
    {
        var measures = Melody(Book("c'4 d break e f | g1 | a1 |")).Measures;

        // head | tail | g1 | a1
        Assert.Equal(4, measures.Length);
        var head = measures[0];
        var tail = measures[1];
        Assert.True(head.BreaksMidBar);
        Assert.False(head.ContinuesBar);
        Assert.Equal(BarlineType.None, head.EndBarline);
        Assert.True(head.HasBreakAfter);
        Assert.Equal(2, head.Items.Count(i => i is NoteItem));
        Assert.True(tail.ContinuesBar);
        Assert.False(tail.BreaksMidBar);
        Assert.Equal(BarlineType.None, tail.StartBarline);
        Assert.Equal(BarlineType.Single, tail.EndBarline);
        Assert.Equal(2, tail.Items.Count(i => i is NoteItem));
        Assert.False(measures[2].ContinuesBar);

        // Bar numbers count bars: the two halves are bar 1, then 2 and 3.
        Assert.Equal(new[] { 1, 1, 2, 3 }, BarNumberEngraver.NumberMeasures(measures, 0));
    }

    [Fact]
    public void ThePage_EndsTheFirstSystemWithNoBarLine_AndOpensTheNextWithNoNumber()
    {
        string split = Book("c'4 d break e f | g1 | a1 |");
        string atBar = Book("c'4 d e f break | g1 | a1 |");

        var svg = Render(split);
        var rows = BarLineRows(svg);
        // Only the second system has bar lines: the first ends at the break's column (LP:
        // no BarLine grob there), and the second draws three (LP twin 11.568 / 19.718 /
        // 27.868 — see the class remarks).
        var second = Assert.Single(rows);
        Assert.Equal(3, second.Count);
        Assert.InRange(second[0], 11.568 - 0.05, 11.568 + 0.05);
        Assert.InRange(second[1], 19.718 - 0.05, 19.718 + 0.05);
        Assert.InRange(second[2], 27.868 - 0.05, 27.868 + 0.05);
        // …and no bar number on it: LilyPond's BarNumber is made with the BarLine.
        Assert.Empty(BoldNumbers(svg));

        // Positive control: broken AT the bar line, the second system is numbered 2 and the
        // first ends with its bar line.
        var control = Render(atBar);
        Assert.Contains("2", BoldNumbers(control));
        Assert.Equal(2, BarLineRows(control).Count);

        // The report counts bars, not model measures.
        var report = LayoutReport.Generate(SyntaxTree.Parse(split));
        Assert.Contains("2 systems, 3 bars", report);
        Assert.Contains("system 1: bar 1", report);
        Assert.Contains("forced breaks after bar: 1", report);
    }

    [Fact]
    public void TheFirstNoteOfTheTailSystem_StandsWhereABarLineBreakPutsIt()
    {
        // LilyPond: 5.8 on both (mb1.ly / mb8.ly); Lily# too.
        double FirstNoteX(string svg)
        {
            var glyphs = Regex.Matches(svg, "<text class=\"music\" x=\"([0-9.]+)\" y=\"([0-9.]+)\"")
                .Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                              Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();
            // Each system opens with its clef at x 0.80; the second system's is the lower one.
            double secondClefY = glyphs.Where(g => g.X < 1).Max(g => g.Y);
            // The first NOTE is the next glyph on that system (no meter is reprinted there).
            return glyphs.Where(g => System.Math.Abs(g.Y - secondClefY) < 4 && g.X > 1).Min(g => g.X);
        }
        Assert.InRange(FirstNoteX(Render(Book("c'4 d break e f | g1 | a1 |"))), 5.8 - 0.05, 5.8 + 0.05);
        Assert.InRange(FirstNoteX(Render(Book("c'4 d e f break | g1 | a1 |"))), 5.8 - 0.05, 5.8 + 0.05);
    }

    [Fact]
    public void ABreakRightBeforeTheBarLine_IsABarLineBreak_AsItAlwaysWas()
    {
        // The corpus's 198 reader sites are `e2 break |` — a break with a bar line after it.
        // Those split nothing: byte-identical (data-pos aside) to the break after the bar.
        var before = Melody(Book("c'4 d e f break | g1 |")).Measures;
        Assert.Equal(2, before.Length);
        Assert.DoesNotContain(before, m => m.BreaksMidBar || m.ContinuesBar);
        Assert.True(before[0].HasBreakAfter);
        Assert.Equal(
            MaskDataPos(Render(Book("c'4 d e f break | g1 |"))),
            MaskDataPos(Render(Book("c'4 d e f | break g1 |"))));

        // …and a break in an UNDERFULL bar closed by its bar line right after it.
        var underfull = Melody(Book("c'2 break | g1 |")).Measures;
        Assert.Equal(2, underfull.Length);
        Assert.DoesNotContain(underfull, m => m.BreaksMidBar || m.ContinuesBar);
        Assert.True(underfull[0].HasBreakAfter);
    }

    [Fact]
    public void TheSplit_ReachesEveryVoiceRowAndTheEmptyBar_AtTheSameBeat()
    {
        string book = Book(
            "c'4 d break e f | voice { g4 a break b c } { e4 f g a } | a1 |",
            bass: "c2 c2 | | c1 |",
            rows: "chords prog { section Main { C G | C | F | } }\nlyrics w { section Main { a b c d | e | f | } }",
            scoreExtra: " chords prog lyrics w");
        var score = Collect(book);
        var voices = score.AllVoices.ToList();
        Assert.True(voices.Count >= 5, $"voices: {voices.Count}"); // melody, melody.2, bass, prog, w

        // Every voice: head, tail, head, tail, bar 3 — five measures, the same flags at the
        // same indices.
        foreach (var v in voices)
        {
            Assert.Equal(5, v.Measures.Length);
            Assert.True(v.Measures[0].BreaksMidBar, v.Name);
            Assert.True(v.Measures[1].ContinuesBar, v.Name);
            Assert.True(v.Measures[2].BreaksMidBar, v.Name);
            Assert.True(v.Measures[3].ContinuesBar, v.Name);
            Assert.False(v.Measures[4].ContinuesBar, v.Name);
            Assert.Equal(BarlineType.None, v.Measures[0].EndBarline);
            Assert.Equal(BarlineType.None, v.Measures[2].EndBarline);
            // Each half is worth its half: the head 1/2, the tail 1/2 (the bass's `| |` gap
            // and the rows' slots were cut into two spacers). The second voice's track is
            // item-less outside its span (an empty mirror), so it has nothing to weigh there.
            if (v.Measures[0].Items.Length > 0)
                Assert.Equal(new Fraction(1, 2), v.Measures[0].TotalDuration);
            if (v.Measures[1].Items.Length > 0)
                Assert.Equal(new Fraction(1, 2), v.Measures[1].TotalDuration);
            // The polyphonic bar's two voices both weigh 1/2 + 1/2.
            Assert.Equal(new Fraction(1, 2), v.Measures[2].TotalDuration);
            Assert.Equal(new Fraction(1, 2), v.Measures[3].TotalDuration);
        }

        // The chord row: C in the head at 0, G in the tail at 0 (timed from the break).
        var chords = score.ChordNames.Where(c => c.IsChordRow).OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing).ToList();
        Assert.Equal(("C", 0), (chords[0].ChordText, chords[0].MeasureIndex));
        Assert.Equal(Fraction.Zero, chords[0].Timing);
        Assert.Equal(("G", 1), (chords[1].ChordText, chords[1].MeasureIndex));
        Assert.Equal(Fraction.Zero, chords[1].Timing);
        Assert.Equal(("C", 2), (chords[2].ChordText, chords[2].MeasureIndex));
        Assert.Equal(("F", 4), (chords[3].ChordText, chords[3].MeasureIndex));

        // The lyrics row: a b in the head, c d in the tail (item indices re-based), e in bar 2.
        var words = score.Lyrics.Where(l => l.IsLyricsRow).OrderBy(l => l.MeasureIndex).ThenBy(l => l.ItemIndex).ToList();
        Assert.Equal(new[] { ("a", 0, 0), ("b", 0, 1), ("c", 1, 0), ("d", 1, 1), ("e", 2, 0), ("f", 4, 0) },
            words.Select(l => (l.Text, l.MeasureIndex, l.ItemIndex)).ToArray());

        Assert.NotEmpty(Render(book));
    }

    [Fact]
    public void ARepeatedSection_SplitsAtEveryPlay_AndAPageBreakSplitsToo()
    {
        string book = """
            time 4/4
            part melody { clef treble }
            section A { melody { c'4 d break e f | g1 | } }
            section B { melody { a2 pageBreak b2 | c'1 | } }
            form main { A A B }
            score main { staff melody }
            """;
        var measures = Melody(book).Measures;
        // A: head tail g1 | A: head tail g1 | B: head tail c'1
        Assert.Equal(9, measures.Length);
        Assert.True(measures[0].BreaksMidBar && measures[1].ContinuesBar);
        Assert.True(measures[3].BreaksMidBar && measures[4].ContinuesBar);
        Assert.True(measures[6].BreaksMidBar && measures[7].ContinuesBar);
        Assert.Equal(BreakPermission.Force, measures[6].PageBreakPermission);
        Assert.Equal(BreakPermission.Allow, measures[0].PageBreakPermission);
        Assert.Equal(new[] { 1, 1, 2, 3, 3, 4, 5, 5, 6 }, BarNumberEngraver.NumberMeasures(measures, 0));
    }

    [Fact]
    public void AccidentalsCarryAcrossTheSplit_AsAcrossAnySystemEdgeInsideABar()
    {
        // A sharp before the break still governs the same pitch after it: the tail's cis
        // prints no accidental. Broken AT the bar line, the new bar's cis prints its sharp.
        // (Relative octaves: `cis4 cis cis cis` is one pitch four times.)
        var split = Melody(Book("cis'4 cis break cis cis | g1 |")).Measures;
        Assert.True(split[1].ContinuesBar);
        Assert.NotNull(split[0].Items.OfType<NoteItem>().First().Accidental);
        Assert.All(split[1].Items.OfType<NoteItem>(), n => Assert.Null(n.Accidental));

        var atBar = Melody(Book("cis'4 cis cis cis break | cis1 |")).Measures;
        Assert.NotNull(atBar[1].Items.OfType<NoteItem>().First().Accidental);
    }

    // ---- the refusals ------------------------------------------------------------------

    [Fact]
    public void ANoteSoundingAcrossTheBreak_RefusesTheSplit_AndTheBreakFallsToTheBarLine()
    {
        string book = Book("c'4 d break e f | g1 | a1 |", bass: "c1 | c1 | c1 |");
        var score = Collect(book);
        Assert.All(score.AllVoices, v =>
        {
            Assert.Equal(3, v.Measures.Length);
            Assert.DoesNotContain(v.Measures, m => m.BreaksMidBar || m.ContinuesBar);
        });
        // The page is the one a break after the bar line makes — LilyPond splits here
        // (mb2.ly), Lily# declares the divergence (LILYSHARP-OWN at MidBarBreakTable) and says so.
        Assert.Equal(
            MaskDataPos(Render(Book("c'4 d e f break | g1 | a1 |", bass: "c1 | c1 | c1 |"))),
            MaskDataPos(Render(book)));

        var warning = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(book)),
            d => d.Code == DiagnosticCodes.MidBarBreakNotSplit);
        Assert.Contains("'bass'", warning.Message);
        Assert.Contains("sounds across it", warning.Message);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        // …pointing at the break.
        Assert.Equal(book.IndexOf("break", System.StringComparison.Ordinal), warning.Span.Start);
    }

    [Theory]
    [InlineData("c'8 d break e f g4 a | b1 |", "a beam runs across it")]
    [InlineData("tuplet 3/2 { c'4 d break e } f2 | g1 |", "inside a tuplet")]
    [InlineData("time none c'4 d break e f | time 4/4 g1 |", "unmetered")]
    public void ABeamATupletOrAnUnmeteredBar_RefuseTheSplit(string melody, string reason)
    {
        string book = Book(melody);
        var measures = Melody(book).Measures;
        Assert.DoesNotContain(measures, m => m.BreaksMidBar || m.ContinuesBar);
        var warnings = SemanticValidation.Run(SyntaxTree.Parse(book))
            .Where(d => d.Code == DiagnosticCodes.MidBarBreakNotSplit).ToList();
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Message.Contains(reason));
    }

    [Fact]
    public void ATupletThatEndsAtTheBreak_IsNoObstacle()
    {
        var measures = Melody(Book("tuplet 3/2 { c'4 d e } break f2 | g1 |")).Measures;
        Assert.True(measures[0].BreaksMidBar && measures[1].ContinuesBar);
        Assert.Equal(new Fraction(1, 2), measures[0].TotalDuration);
    }

    [Fact]
    public void ASecondBreakInTheSameBar_IsRefused_TheFirstSplits()
    {
        string book = Book("c'4 break d break e f | g1 |");
        var measures = Melody(book).Measures;
        Assert.True(measures[0].BreaksMidBar && measures[1].ContinuesBar);
        Assert.Equal(new Fraction(1, 4), measures[0].TotalDuration);
        Assert.Equal(3, measures.Length);
        var warning = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(book)),
            d => d.Code == DiagnosticCodes.MidBarBreakNotSplit);
        Assert.Contains("only once", warning.Message);
        Assert.Equal(book.LastIndexOf("break", System.StringComparison.Ordinal), warning.Span.Start);
    }

    [Fact]
    public void APercentBody_SplitsItsWrittenPlay_AndTheCoveredPlayIsOneSpacerBar()
    {
        // The written play is music and splits; a covered play is written as spacers with the
        // sign (no notes, no break to replay) and stays one bar.
        string book = Book("repeat percent 2 { c'4 d break e f | } g1 |");
        var measures = Melody(book).Measures;
        Assert.Equal(4, measures.Length); // head | tail | % | g1
        Assert.True(measures[0].BreaksMidBar && measures[1].ContinuesBar);
        Assert.False(measures[2].ContinuesBar);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.MidBarBreakNotSplit));
    }

    [Fact]
    public void ABreakBetweenBeamGroups_Splits()
    {
        // 4/4 beams eighths 4 + 4; a break at the half bar crosses no beam. LilyPond twin:
        // second-system bar lines 15.603 / 24.455 / 33.307 (mb4.ly — this very book).
        string book = Book("c'8 d e f break g a b c | c1 | d1 |");
        var measures = Melody(book).Measures;
        Assert.True(measures[0].BreaksMidBar && measures[1].ContinuesBar);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.MidBarBreakNotSplit));
        var second = Assert.Single(BarLineRows(Render(book)));
        Assert.InRange(second[0], 15.603 - 0.05, 15.603 + 0.05);
        Assert.InRange(second[1], 24.455 - 0.05, 24.455 + 0.05);
        Assert.InRange(second[2], 33.307 - 0.05, 33.307 + 0.05);
    }

    // ---- the other readers ------------------------------------------------------------

    [Fact]
    public void MidiAndMusicXml_DoNotSeeTheSplit_AndTheTwinWritesTheBreakWhereItStands()
    {
        string split = Book("c'4 d break e f | g1 | a1 |");
        string plain = Book("c'4 d e f | g1 | a1 |");

        var midiSplit = new MidiExporter().Export(SyntaxTree.Parse(split)).Tracks.SelectMany(t => t.Notes)
            .Select(n => (n.Pitch, n.StartTick, n.DurationTicks)).ToList();
        var midiPlain = new MidiExporter().Export(SyntaxTree.Parse(plain)).Tracks.SelectMany(t => t.Notes)
            .Select(n => (n.Pitch, n.StartTick, n.DurationTicks)).ToList();
        Assert.Equal(midiPlain, midiSplit);

        string xmlSplit = new MusicXmlExporter().Export(SyntaxTree.Parse(split)).ToXml().ToString();
        string xmlPlain = new MusicXmlExporter().Export(SyntaxTree.Parse(plain)).ToXml().ToString();
        Assert.Equal(xmlPlain, xmlSplit);

        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(split));
        Assert.Contains("c'4 d \\break", ly);
        Assert.Contains("e f |", ly);
    }

    [Fact]
    public void AnEdit_ThatAddsOrRemovesTheBreak_RendersIdenticalToAFullRecompile_BothWays()
    {
        string plain = Book("c'4 d e f | g1 | a1 |");
        string split = Book("c'4 d break e f | g1 | a1 |");
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(plain), Opt);
        compiler.Render();
        foreach (var (text, label) in new[] { (split, "add the break"), (plain, "remove it"), (split, "add it again") })
        {
            string incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            string full = SvgGenerator.Generate(SyntaxTree.Parse(text), Opt);
            Assert.True(full == incremental, $"{label}: incremental != full");
        }
    }

    [Fact]
    public void TheTable_TranslatesBarsAndMeasuresBothWays()
    {
        var requests = new List<MidBarBreakRequest>
        {
            new(1, new Fraction(1, 2), false, 10),
            new(3, new Fraction(1, 4), false, 20),
        };
        // Four bars of quarters: every offset is a boundary.
        var items = System.Collections.Immutable.ImmutableArray.Create<MusicItem>(
            new RestItem(new Fraction(1, 4), 0, 0), new RestItem(new Fraction(1, 4), 0, 1),
            new RestItem(new Fraction(1, 4), 0, 2), new RestItem(new Fraction(1, 4), 0, 3));
        var voice = new Voice("v", System.Collections.Immutable.ImmutableArray.Create(
            new Measure(items, BarlineType.None, BarlineType.Single, null, 0, 0),
            new Measure(items, BarlineType.None, BarlineType.Single, null, 0, 0),
            new Measure(items, BarlineType.None, BarlineType.Single, null, 0, 0),
            new Measure(items, BarlineType.None, BarlineType.Single, null, 0, 0)));
        var conflicts = new List<MidBarBreakConflict>();
        var table = MidBarBreakTable.Build(requests, new[] { voice }, new TimeSignature(4, 4),
            System.Array.Empty<TupletBracketItem>(), System.Array.Empty<PercentRepeatItem>(), conflicts);
        Assert.Empty(conflicts);
        Assert.False(table.IsEmpty);

        // bars 0 1 2 3 → measures 0 | 1 2 | 3 | 4 5
        Assert.Equal(new[] { 0, 1, 3, 4 }, new[] { 0, 1, 2, 3 }.Select(table.ToPhysical));
        Assert.Equal(new[] { 0, 1, 1, 2, 3, 3, 4 }, new[] { 0, 1, 2, 3, 4, 5, 6 }.Select(table.ToLogical));
        Assert.Equal(new Fraction(1, 2), table.At(1)!.Value.Offset);
        Assert.Null(table.At(2));
    }
}
