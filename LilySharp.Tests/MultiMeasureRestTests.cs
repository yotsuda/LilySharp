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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests the multi-measure rest syntax <c>R<dur>*N</c>: parser accepts the
/// multiplier and the collector expands it into N consecutive measure-rests.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob
/// LILYPOND-REF: lily/lily-parser.yy — R<dur>*N grammar
/// Current scope is structural (N measures get filled) — proper church-rest /
/// big-rest visual rendering is L-1b.
/// </remarks>
[Trait("Category", "Unit")]
public class MultiMeasureRestTests
{
    private static (Score Score, ScoreLayout Layout) BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return (score, engine.Layout(score));
    }

    [Fact]
    public void Parse_R1_NoMultiplier_HasMeasureCountOne()
    {
        var tree = SyntaxTree.Parse("R1 |");
        var rest = (RestSyntax)tree.GetRoot().DescendantNodes().First(n => n is RestSyntax);
        Assert.Equal(1, rest.MeasureCount);
        Assert.False(rest.IsMultiMeasure);
    }

    [Fact]
    public void Parse_R1Star8_HasMeasureCountEight()
    {
        var tree = SyntaxTree.Parse("R1*8 |");
        var rest = (RestSyntax)tree.GetRoot().DescendantNodes().First(n => n is RestSyntax);
        Assert.Equal(8, rest.MeasureCount);
        Assert.True(rest.IsMultiMeasure);
    }

    [Fact]
    public void Collect_R1Star4_ProducesFourMeasures()
    {
        var (score, _) = BuildLayout("R1*4 |");
        // Expansion: 4 measures, each a single full rest.
        Assert.Equal(4, score.Voice.Measures.Length);
        foreach (var m in score.Voice.Measures)
        {
            Assert.Single(m.Items);
            Assert.IsType<RestItem>(m.Items[0]);
        }
    }

    [Fact]
    public void Collect_R1Star1_BehavesLikePlainR1()
    {
        var (sourceA, _) = BuildLayout("R1 |");
        var (sourceB, _) = BuildLayout("R1*1 |");
        Assert.Equal(sourceA.Voice.Measures.Length, sourceB.Voice.Measures.Length);
    }

    [Fact]
    public void Collect_R1Star2_FollowedByMusic_ContinuesAfterMmr()
    {
        // After an MMR, regular notes continue normally.
        var (score, _) = BuildLayout("R1*2 c4 d e f |");
        // 2 MMR measures + 1 measure of music = 3 measures total.
        Assert.Equal(3, score.Voice.Measures.Length);

        // Last measure should be the c-d-e-f line.
        var lastMeasure = score.Voice.Measures[^1];
        Assert.Equal(4, lastMeasure.Items.Length);
        Assert.IsType<NoteItem>(lastMeasure.Items[0]);
    }

    /// <summary>
    /// The post-events of <c>R1*N</c> stand at the run's FIRST bar, on the written event —
    /// the mark, the dynamic, the text and the fermata alike. Until 2026-09-29 (HANDOFF §1.1
    /// 第662 ⑸) the expansion arm collected only the chord symbol: fermata, dynamic and text
    /// were dropped in silence, and the mark fell back to the builder's position AFTER the N
    /// bars (<c>R1*2@mark("Q")</c> drew Q over the bar that follows the rest).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest-engraver.cc:95-98 listen_multi_measure_text —
    /// the text is the event's, at the timestep the rest starts (:126-190 initialize_grobs
    /// hangs it on the rest's spanner); scm/define-grobs.scm:2450-2452
    /// MultiMeasureRestScript direction UP. The text's own placement (MultiMeasureRestText:
    /// UP, centred, sided off the count) is <see cref="Layout_TextOnAnR_IsMultiMeasureRestText"/>'s;
    /// this pins its bar. Poisons
    /// (RULES §5.4): drop the two collects from the expansion arm and the dynamic, the text
    /// and the fermata go missing while the mark moves to bar 3.
    /// </remarks>
    [Fact]
    public void Collect_R1StarN_PostEvents_StandAtTheRunsFirstBar()
    {
        var (score, _) = BuildLayout(
            "c'1 | R1*2@mark(\"Q\")@p@fermata@text(\"tacet\") | d'1 |");
        Assert.Equal(4, score.Voice.Measures.Length);

        var mark = Assert.Single(score.MusicMarks.Where(m => m.Type == MusicMarkType.Rehearsal));
        Assert.Equal("Q", mark.Text);
        Assert.Equal(1, mark.MeasureIndex);

        var fermata = Assert.Single(score.Articulations.Where(a => a.Type == ArticulationType.Fermata));
        Assert.Equal(1, fermata.MeasureIndex);
        Assert.True(fermata.IsAbove);

        var p = Assert.Single(score.Dynamics.Where(d => d.Text == "p"));
        Assert.Equal(1, p.MeasureIndex);
        var tacet = Assert.Single(score.Dynamics.Where(d => d.Text == "tacet"));
        Assert.Equal(1, tacet.MeasureIndex);
    }

    /// <summary>
    /// An <c>@text</c> on an R is LilyPond's MultiMeasureRestText: UP by default, centred on
    /// the rest's span, clear of its count number. MEASURED (LilyPond 2.26, \compressMMRests,
    /// Lab sessions/p712/mmtext): R1*4 "tacet" stands on 4.936 over the staff middle, R1
    /// "solo" (no number) on 2.536. Until 2026-09-30 it was a TextScript: below the staff,
    /// ink-left on the bar's column.
    /// ⚠️ Both land a little over LilyPond — "tacet" 4.947 (+0.011), "solo" 2.543 (+0.007) —
    /// and both are the outside-staff clearance (ink + 0.46), with and without the number, so
    /// the residual is the italic text's own outline below its baseline, deeper in Lily# than
    /// LilyPond's (0.022 / 0.026 there); the ranges pin it so a change to it is seen.
    /// </summary>
    [Theory]
    [InlineData("c'1 | R1*4@text(\"tacet\") | c'1 |", 4.936, 0.012)]
    [InlineData("c'1 | R1@text(\"solo\") | c'1 |", 2.536, 0.008)]
    public void Layout_TextOnAnR_IsMultiMeasureRestText(string music, double lilyPond, double residual)
    {
        var (_, layout) = BuildLayout(music);
        var rest = Assert.Single(layout.MultiMeasureRestLayouts);
        var text = Assert.Single(layout.DynamicLayouts);
        Assert.True(text.IsAbove);
        Assert.Equal((rest.StartX + rest.EndX) / 2.0, text.X, 6);
        Assert.InRange(text.YUp, lilyPond - 0.002, lilyPond + residual);
        // `.down` puts it below, still centred.
        var (_, below) = BuildLayout(music.Replace("\") |", "\").down |"));
        var down = Assert.Single(below.DynamicLayouts);
        Assert.False(down.IsAbove);
        Assert.True(down.YUp < -2.05);
        Assert.Equal(text.X, down.X, 6);
        // A text on a note is the TextScript it always was.
        var (_, onNote) = BuildLayout("c'1@text(\"dolce\") |");
        Assert.False(Assert.Single(onNote.DynamicLayouts).IsAbove);
    }

    /// <summary>The count's box, the MultiMeasureRestText's support, stands 0.4 over the staff's
    /// outer line INK, 2.45 over the middle, as LilyPond 2.26 draws the number (the drawn
    /// number itself still stands on 2.4 — MultiMeasureRestEngraver.NumberStaffPadding).</summary>
    [Fact]
    public void TheCountNumber_StandsOnTheStaffInkPlusItsPadding()
    {
        var box = MultiMeasureRestEngraver.NumberInkBox(4, 10.0, 2.05);
        Assert.Equal(2.45, box.Bottom, 9);
        Assert.Equal(2.45 + 2.004, box.Top, 9);
        Assert.Equal(10.0, (box.Left + box.Right) / 2.0, 9);
    }

    [Fact]
    public void Layout_R1Star4_DoesNotCrash()
    {
        var ex = Record.Exception(() => BuildLayout("R1*4 |"));
        Assert.Null(ex);
    }

    [Fact]
    public void Parse_RegularRest_StillWorks()
    {
        // Regression: r4 (lowercase = silent rest) with no multiplier still parses normally.
        var (score, _) = BuildLayout("r4 r4 r4 r4 |");
        Assert.Single(score.Voice.Measures);
        Assert.Equal(4, score.Voice.Measures[0].Items.Length);
    }
}
