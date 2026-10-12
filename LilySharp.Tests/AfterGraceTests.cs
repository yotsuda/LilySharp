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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// `c2 afterGrace { d16 e }` — grace notes AFTER the note before them, LilyPond's
/// <c>\afterGrace c2 { d16 e }</c> (grammar feedback A4, owner's syntax 2026-10-12). The run
/// stands at 3/4 of the main note (afterGraceFraction), which LilyPond builds as
/// <c>&lt;&lt; c2 { s2*3/4 \grace { d16 e } } &gt;&gt;</c>: the page holds the main note sounding 3/4
/// and a spacer of the last quarter that carries the run (RestItem.LeadingGrace).
/// MEASURED (2.26.0, Lab sessions/p903): mid-bar the run reads as the next note's grace, at a
/// bar's end it sits before the bar line, and a tie over it reaches the next note.
/// </summary>
[Trait("Category", "Unit")]
public class AfterGraceTests
{
    private static string Book(string music) =>
        "octave absolute\npart m\nsection A { m { " + music + " } }\nform { ~A }\nscore { staff m }";

    private static string Items(string music)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(Book(music)));
        return string.Join(" | ", score.Voice.Measures.Select(m => string.Join(" ", m.Items.Select(i => i switch
        {
            _ when i.GraceTime => "g",
            RestItem { IsSpacer: true } r => $"s{r.Duration}:{r.LeadingGrace.Length}",
            _ => i.Duration.ToString(),
        }))));
    }

    [Theory]
    [InlineData("c'2 afterGrace { d'16 e' } f'2 |", "3/8 g g s1/8:2 1/2")]
    [InlineData("g'1 afterGrace { a'16 } |", "3/4 g s1/4:1")]
    [InlineData("<c' e'>2 afterGrace { d'16 } r2 afterGrace { e'16 } |", "3/8 g s1/8:1 3/8 g s1/8:1")]
    // Inside a tuplet the main note gives up a quarter of its SCALED length, and the bar is whole.
    [InlineData("tuplet 3/2 { c'4 afterGrace { d'16 } d' e' } f'2 |", "1/8 g s1/24:1 1/6 1/6 1/2")]
    // A tie or slur mark between the note and its afterGrace still names that note.
    [InlineData("g'1~ afterGrace { a'16 } | g'1 |", "3/4 g s1/4:1 | 1")]
    public void TheMainNoteSoundsThreeQuarters_AndASpacerCarriesTheRun(string music, string expected)
        => Assert.Equal(expected, Items(music));

    [Fact]
    public void TheRunIsMarkedAfterGrace()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(Book("c'2 afterGrace { d'16 } grace { e'16 } f'2 |")));
        Assert.Equal([true, false], score.GraceNotes.Select(g => g.IsAfterGrace).ToArray());
    }

    [Theory]
    [InlineData("afterGrace { d'16 } c'1 |")]
    [InlineData("c'4 | afterGrace { d'16 } c'2. |")]
    public void WithNoMainNote_NothingIsEngraved_AndLilySharpSaysSo(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.Contains(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.UnengravedGraceContent
            && d.Message.Contains("afterGrace"));
        Assert.Empty(new MeasureCollector().Collect(tree).GraceNotes);
        Assert.DoesNotContain("afterGrace", new LilyPondExporter().Export(tree));
    }

    [Fact]
    public void WithItsMainNote_NothingIsReported()
    {
        var tree = SyntaxTree.Parse(Book("c'2 afterGrace { d'16 e' } f'2 |"));
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.UnengravedGraceContent);
    }

    /// <summary>The run hangs between the main note and the next one, clear of the main head:
    /// the spacer's column reserves it (SpacingRules.GraceNotesOf). MEASURED (2.26.0, Lab
    /// sessions/p903, `\afterGrace c''2 { b'16 c'' } d''2`): about 3 staff spaces from the main
    /// note to the run.</summary>
    [Fact]
    public void TheRunStandsBetweenTheMainNoteAndTheNext()
    {
        var tree = SyntaxTree.Parse(Book("c''2 afterGrace { b'16 c'' } d''2 |"));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        var bar = layout.AllSystems.SelectMany(s => s.Measures).Single(m => m.MeasureIndex == 0);
        var grace = Assert.Single(layout.GraceNoteLayouts);
        double main = bar.X + bar.Items[0].X, next = bar.X + bar.Items[^1].X;
        Assert.InRange(grace.X - main, 2.5, 3.5);
        Assert.True(grace.MainNoteX < next, $"run at {grace.X}..{grace.MainNoteX}, next note at {next}");
    }

    [Fact]
    public void ATieReachesPastTheRun()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(Book("g'1~ afterGrace { a'16 b' } | g'1 |")));
        var tie = Assert.Single(new TieDetector().DetectTies(score));
        Assert.Equal((0, 0, 1, 0), (tie.StartMeasureIndex, tie.StartItemIndex, tie.EndMeasureIndex, tie.EndItemIndex));
    }

    [Fact]
    public void TheTwin_WritesLilyPondsAfterGrace()
    {
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(Book(
            "c''2 afterGrace { b'16 c'' } d''2 | g'1~ afterGrace { a'16 } | g'4 tuplet 3/2 { c''4 afterGrace { d''16 } d'' e'' } r2 afterGrace { e''16 } |")));
        Assert.Contains("\\afterGrace c''2 { b'16 c'' } d''2", ly);
        Assert.Contains("\\afterGrace g'1 ~ { a'16 }", ly);
        Assert.Contains("\\tuplet 3/2 { \\afterGrace c''4 { d''16 } d''4 e'' }", ly);
        Assert.Contains("\\afterGrace r2 { e''16 }", ly);
    }

    /// <summary>The run sounds at the END of the main note and the main note gives up its time
    /// — a leading grace's steal from the note after, mirrored.</summary>
    [Fact]
    public void Midi_TheRunEndsTheMainNote()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(Book("c'2 afterGrace { d'16 e' } f'2 |")))
            .Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToList();
        Assert.Equal([0, 2, 4, 5], notes.Select(n => n.Pitch - notes[0].Pitch).ToArray());
        var (main, d, e, f) = (notes[0], notes[1], notes[2], notes[3]);
        Assert.True(d.IsGrace && e.IsGrace);
        Assert.Equal(d.StartTick, main.StartTick + main.DurationTicks);
        Assert.Equal(e.StartTick, d.StartTick + d.DurationTicks);
        Assert.Equal(f.StartTick, e.StartTick + e.DurationTicks);
        // …and the note after keeps its own onset and length.
        Assert.Equal(f.DurationTicks, f.StartTick);
    }

    [Fact]
    public void MusicXml_MarksTheRunAsStealingFromTheNoteBefore()
    {
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter()
            .Export(SyntaxTree.Parse(Book("c'2 afterGrace { d'16 } grace { e'16 } f'2 |"))).ToXml().ToString();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xml, "<grace steal-time-previous=\"25\" />"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(xml, "<grace").Count);
    }

    [Fact]
    public void AfterGrace_IsNotAPartName()
        => Assert.Contains(SyntaxTree.Parse("part afterGrace\nsection A { afterGrace { c'1 | } }\nform { A }").Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error);
}
