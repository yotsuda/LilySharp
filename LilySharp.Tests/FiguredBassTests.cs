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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class FiguredBassTests
{
    // --- FiguredBassFigure ---

    [Fact]
    public void FiguredBassFigure_DisplayText_NumberOnly()
    {
        var figure = new FiguredBassFigure(6);
        Assert.Equal("6", figure.DisplayText);
    }

    [Fact]
    public void FiguredBassFigure_DisplayText_WithSharp()
    {
        var figure = new FiguredBassFigure(6, 1);
        Assert.Equal("6\u266F", figure.DisplayText);  // 6♯
    }

    [Fact]
    public void FiguredBassFigure_DisplayText_WithFlat()
    {
        var figure = new FiguredBassFigure(4, -1);
        Assert.Equal("4\u266D", figure.DisplayText);  // 4♭
    }

    [Fact]
    public void FiguredBassFigure_DisplayText_Held()
    {
        var figure = new FiguredBassFigure(0, 0, Held: true);
        Assert.Equal("–", figure.DisplayText);  // en dash (extension)
    }

    [Fact]
    public void FiguredBassFigure_DisplayText_WithNatural()
    {
        var figure = new FiguredBassFigure(7, 2);
        Assert.Equal("7\u266E", figure.DisplayText);  // 7♮
    }

    // --- ParseFigures ---

    [Fact]
    public void ParseFigures_SingleDigit()
    {
        var result = FiguredBassItem.ParseFigures("fig.6");
        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.Equal(6, result.Value[0].Number);
        Assert.Equal(0, result.Value[0].Alteration);
    }

    [Fact]
    public void ParseFigures_TwoDigits()
    {
        var result = FiguredBassItem.ParseFigures("fig.6.4");
        Assert.NotNull(result);
        Assert.Equal(2, result!.Value.Length);
        Assert.Equal(6, result.Value[0].Number);
        Assert.Equal(4, result.Value[1].Number);
    }

    [Fact]
    public void ParseFigures_ThreeDigits()
    {
        var result = FiguredBassItem.ParseFigures("fig.6.4.3");
        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.Length);
        Assert.Equal(6, result.Value[0].Number);
        Assert.Equal(4, result.Value[1].Number);
        Assert.Equal(3, result.Value[2].Number);
    }

    [Fact]
    public void ParseFigures_WithSharp()
    {
        var result = FiguredBassItem.ParseFigures("fig.6.s");
        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.Equal(6, result.Value[0].Number);
        Assert.Equal(1, result.Value[0].Alteration);
    }

    [Fact]
    public void ParseFigures_WithFlat()
    {
        var result = FiguredBassItem.ParseFigures("fig.4.f");
        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.Equal(4, result.Value[0].Number);
        Assert.Equal(-1, result.Value[0].Alteration);
    }

    [Fact]
    public void ParseFigures_WithNatural()
    {
        var result = FiguredBassItem.ParseFigures("fig.7.n");
        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.Equal(7, result.Value[0].Number);
        Assert.Equal(2, result.Value[0].Alteration);
    }

    [Fact]
    public void ParseFigures_MixedAlterations()
    {
        var result = FiguredBassItem.ParseFigures("fig.7.6.s.4.f");
        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.Length);
        Assert.Equal(7, result.Value[0].Number);
        Assert.Equal(0, result.Value[0].Alteration);
        Assert.Equal(6, result.Value[1].Number);
        Assert.Equal(1, result.Value[1].Alteration);
        Assert.Equal(4, result.Value[2].Number);
        Assert.Equal(-1, result.Value[2].Alteration);
    }

    [Fact]
    public void ParseFigures_SharpPrefix()
    {
        // @fig(#6) → MarkName "fig.#.6"; the '#' sharpens the following figure.
        var result = FiguredBassItem.ParseFigures("fig.#.6");
        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.Equal(6, result.Value[0].Number);
        Assert.Equal(1, result.Value[0].Alteration);
    }

    [Fact]
    public void ParseFigures_StandaloneSharp()
    {
        // @fig(#) → a bare sharp (raised third), number 0.
        var result = FiguredBassItem.ParseFigures("fig.#");
        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.Equal(0, result.Value[0].Number);
        Assert.Equal(1, result.Value[0].Alteration);
    }

    [Fact]
    public void ParseFigures_SharpPrefix_ThenPlainFigure()
    {
        // @fig(#6 4) → 6♯ over a plain 4.
        var result = FiguredBassItem.ParseFigures("fig.#.6.4");
        Assert.NotNull(result);
        Assert.Equal(2, result!.Value.Length);
        Assert.Equal(6, result.Value[0].Number);
        Assert.Equal(1, result.Value[0].Alteration);
        Assert.Equal(4, result.Value[1].Number);
        Assert.Equal(0, result.Value[1].Alteration);
    }

    [Fact]
    public void ParseFigures_HeldFigure()
    {
        // @fig(7 _) → a 7 over a held/continuation slot.
        var result = FiguredBassItem.ParseFigures("fig.7._");
        Assert.NotNull(result);
        Assert.Equal(2, result!.Value.Length);
        Assert.Equal(7, result.Value[0].Number);
        Assert.False(result.Value[0].Held);
        Assert.Equal(0, result.Value[1].Number);
        Assert.True(result.Value[1].Held);
    }

    [Fact]
    public void ParseFigures_StandaloneHeld()
    {
        // @fig(_) → a lone held figure line.
        var result = FiguredBassItem.ParseFigures("fig._");
        Assert.NotNull(result);
        Assert.Single(result!.Value);
        Assert.True(result.Value[0].Held);
    }

    [Fact]
    public void ParseFigures_NotFiguredBass_ReturnsNull()
    {
        Assert.Null(FiguredBassItem.ParseFigures("segno"));
        Assert.Null(FiguredBassItem.ParseFigures("coda"));
        Assert.Null(FiguredBassItem.ParseFigures("mark.A"));
    }

    [Fact]
    public void ParseFigures_InvalidFig_ReturnsNull()
    {
        Assert.Null(FiguredBassItem.ParseFigures("fig."));
        Assert.Null(FiguredBassItem.ParseFigures("fig.x"));
    }

    // --- FiguredBassEngraver ---

    [Fact]
    public void FiguredBassEngraver_Calculate_EmptyInput()
    {
        var result = FiguredBassEngraver.Calculate(
            ImmutableArray<FiguredBassItem>.Empty,
            ImmutableArray<SystemLayout>.Empty,
            ImmutableArray<MeasureLayout>.Empty);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void FiguredBassEngraver_Calculate_ProducesLayout()
    {
        var figuredBasses = ImmutableArray.Create(
            new FiguredBassItem(
                ImmutableArray.Create(new FiguredBassFigure(6)),
                0, 0, 0));

        var itemLayout = new ItemLayout(0, 2.0, 1.0);
        var measureLayout = new MeasureLayout(0, 5.0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 20.0, 50.0, 5.0, ImmutableArray.Create(measureLayout));

        var result = FiguredBassEngraver.Calculate(
            figuredBasses,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(measureLayout));

        Assert.Single(result);
        Assert.Equal(0, result[0].MeasureIndex);
        Assert.Equal(7.0, result[0].X, 1);  // measureX(5.0) + itemX(2.0)
        Assert.Single(result[0].FigureTexts);
        Assert.Equal("6", result[0].FigureTexts[0]);
    }

    [Fact]
    public void FiguredBassEngraver_Calculate_MultipleFigures()
    {
        var figuredBasses = ImmutableArray.Create(
            new FiguredBassItem(
                ImmutableArray.Create(
                    new FiguredBassFigure(6),
                    new FiguredBassFigure(4)),
                0, 0, 0));

        var itemLayout = new ItemLayout(0, 2.0, 1.0);
        var measureLayout = new MeasureLayout(0, 5.0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 20.0, 50.0, 5.0, ImmutableArray.Create(measureLayout));

        var result = FiguredBassEngraver.Calculate(
            figuredBasses,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(measureLayout));

        Assert.Single(result);
        Assert.Equal(2, result[0].FigureTexts.Length);
        Assert.Equal("6", result[0].FigureTexts[0]);
        Assert.Equal("4", result[0].FigureTexts[1]);
    }

    // --- MeasureCollector integration ---

    [Fact]
    public void Collector_FiguredBass_SingleFigure()
    {
        var source = "c4 @fig(6) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.FiguredBasses);
        var fb = score.FiguredBasses[0];
        Assert.Equal(0, fb.MeasureIndex);
        Assert.Equal(0, fb.ItemIndex);
        Assert.Single(fb.Figures);
        Assert.Equal(6, fb.Figures[0].Number);
    }

    [Fact]
    public void Collector_FiguredBass_TwoFigures()
    {
        var source = "c4 @fig(6 4) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.FiguredBasses);
        var fb = score.FiguredBasses[0];
        Assert.Equal(2, fb.Figures.Length);
        Assert.Equal(6, fb.Figures[0].Number);
        Assert.Equal(4, fb.Figures[1].Number);
    }

    [Fact]
    public void Collector_FiguredBass_WithAlteration()
    {
        var source = "c4 @fig(6 s) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.FiguredBasses);
        var fb = score.FiguredBasses[0];
        Assert.Equal(6, fb.Figures[0].Number);
        Assert.Equal(1, fb.Figures[0].Alteration);
    }

    [Fact]
    public void Collector_FiguredBass_SharpPrefix()
    {
        var source = "c4 @fig(#6) d e f";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.FiguredBasses);
        var fb = score.FiguredBasses[0];
        Assert.Single(fb.Figures);
        Assert.Equal(6, fb.Figures[0].Number);
        Assert.Equal(1, fb.Figures[0].Alteration);
    }

    [Fact]
    public void Collector_FiguredBass_HeldFigure()
    {
        var source = "c4 @fig(7 _) d e f";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.FiguredBasses);
        var fb = score.FiguredBasses[0];
        Assert.Equal(2, fb.Figures.Length);
        Assert.Equal(7, fb.Figures[0].Number);
        Assert.True(fb.Figures[1].Held);
    }

    [Fact]
    public void Collector_FiguredBass_MultipleNotes()
    {
        var source = "c4 @fig(6) d @fig(5) e @fig(6 4) f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(3, score.FiguredBasses.Length);
        Assert.Equal(0, score.FiguredBasses[0].ItemIndex);
        Assert.Equal(1, score.FiguredBasses[1].ItemIndex);
        Assert.Equal(2, score.FiguredBasses[2].ItemIndex);
    }

    [Fact]
    public void Collector_FiguredBass_NoFiguredBass()
    {
        var source = "c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.FiguredBasses.IsEmpty);
    }

    // --- BassFigureAlignment: the row step, and the fact that it is a MAX of two branches ---

    /// <summary>
    /// Digits step by the spec's minimum-distance, because their ink does not reach it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:449-450 <c>staff-staff-spacing</c> of BassFigureLine —
    /// the ledger's own texture (books FBSA..FBSC use <c>&lt;5 3&gt;</c> and <c>&lt;6 4&gt;</c>),
    /// where LilyPond's dumped step is 1.5 exactly.
    /// </remarks>
    [Fact]
    public void BassFigureAlignment_DigitRows_StepByTheSpecMinimum()
    {
        var offsets = BassFigureAlignment.RowOffsets(new[]
        {
            new BassFigureAlignment.Column(10.0, ImmutableArray.Create("5", "3")),
        });

        Assert.Equal(2, offsets.Length);
        Assert.Equal(0.0, offsets[0], 9);
        Assert.Equal(BassFigureAlignment.LineMinimumDistance, offsets[1], 9);
    }

    /// <summary>
    /// ⚠️ THE OTHER BRANCH, which no ledger point reaches: an alteration both descends below
    /// its baseline and caps higher than a digit, so a row of them steps by INK and the
    /// minimum stops deciding. This is the machine that stops the port being "simplified"
    /// back to the constant 1.5 the probe's texture happens to show (HANDOFF §5.2).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:228-233 <c>internal_get_minimum_translations</c>'s
    /// <c>dy</c> — the skyline distance plus the spec's padding, floored by its
    /// minimum-distance, and this pair is on the other side of that floor.
    /// </remarks>
    [Fact]
    public void BassFigureAlignment_AlteredRows_StepByTheirInk()
    {
        var sharp = "5♯";
        var offsets = BassFigureAlignment.RowOffsets(new[]
        {
            new BassFigureAlignment.Column(10.0, ImmutableArray.Create(sharp, sharp)),
        });

        double byInk = FiguredBassGlyphRun.InkTop(sharp)
                       - FiguredBassGlyphRun.InkBottom(sharp)
                       + BassFigureAlignment.LinePadding;
        Assert.True(byInk > BassFigureAlignment.LineMinimumDistance,
            $"the ink branch must be the larger one for this pair, was {byInk}");
        Assert.Equal(byInk, offsets[1], 9);
    }

    /// <summary>
    /// A column with fewer figures than the deepest one stops at its OWN last row, and its
    /// depth is that row's offset plus its own descent — not a tail added to the baseline.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:374 <c>axis-group-interface::height</c>,
    /// BassFigureAlignment's Y-extent.</remarks>
    [Fact]
    public void BassFigureAlignment_ShallowColumn_IsAsDeepAsItsOwnLastRow()
    {
        var columns = new[]
        {
            new BassFigureAlignment.Column(10.0, ImmutableArray.Create("5", "3")),
            new BassFigureAlignment.Column(20.0, ImmutableArray.Create("6")),
        };
        var offsets = BassFigureAlignment.RowOffsets(columns);

        // The digits sit ON their baseline, so a one-row column is zero deep and a two-row
        // one is exactly the step — no 0.5 tail under either (the debt this port paid).
        Assert.Equal(0.0, BassFigureAlignment.ColumnDepth(offsets, columns[1].Texts), 9);
        Assert.Equal(BassFigureAlignment.LineMinimumDistance,
            BassFigureAlignment.ColumnDepth(offsets, columns[0].Texts), 9);
    }

    // The topmost figure's baseline on a staff whose SECOND voice holds the given item —
    // printed rests in the book under test, spacers in its control. One token apart, so
    // the difference is the rest's ink and nothing else.
    private static (double Baseline, double RestShift) FigureBaseline(string secondVoice)
    {
        var src =
            "octave absolute\n" +
            "part bs { clef treble }\n" +
            $"section Main {{\n  bs {{ voice {{ b4@fig(6) b b b }} {{ {secondVoice} }} | }}\n}}\n" +
            "form main { Main }\n" +
            "score main \"o\" { staff bs }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        return (layout.FiguredBassLayouts.Select(f => f.YUp).Min(),
                layout.GetRestShift(measureIndex: 0, voiceIndex: 1, itemIndex: 0));
    }

    /// <summary>
    /// The figures drop below the rest ANOTHER VOICE pushed out of the staff — the per-staff
    /// DOWN profile the drop is computed against holds that rest where <c>Rest_collision</c>
    /// put it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-950 <c>skyline_spacing</c> — a Rest is
    /// inside-staff ink, and BassFigureAlignmentPositioning's outside-staff-priority 25 places
    /// it against exactly those inside skylines. LilyPond translates the one Rest grob
    /// (lily/rest-collision.cc:211-290 <c>calc_positioning_done</c>), so no consumer can read
    /// an unmoved position.
    /// <para>
    /// ⚠️ THE ROOM HAD IT AND THIS PROFILE DID NOT — the same omission, at the second of the
    /// four call sites that build their own profile from
    /// <c>SkylineBuilder.BuildStaffSkylines</c>. MEASURED: the figure baseline read -3.672462
    /// with the rests as spacers and -5.902462 with them printed only after the table reached
    /// this call; before it, both books read -3.672462. The 2.230000 between them is the
    /// number LilyPond itself gives a rest pushed DOWN out of the staff
    /// (audit/lp-geometry <c>staff.staff.rest-under-notes</c> against its control), which is
    /// what says the reservation now matches the ink rather than merely responding to it.
    /// </para>
    /// <para>
    /// ⚠️ THREE LEGS: the PREMISE that the rest left the staff at all, a CONTROL that the drop
    /// reads this staff's profile, and the quantity. Two equal numbers otherwise cannot tell
    /// "nothing moved" from "nothing is measured" (HANDOFF 5.3).
    /// </para>
    /// </remarks>
    [Fact]
    public void FiguredBass_DropsBelowARestAnotherVoicePushedOutOfTheStaff()
    {
        var moved = FigureBaseline("r4 r r r");
        var spacer = FigureBaseline("s4 s s s");
        var lowNotes = FigureBaseline("c4 c c c");

        Assert.True(moved.RestShift <= -5.0,
            "premise: Rest_collision must push this rest out of the staff, "
            + $"got {moved.RestShift:F6} staff positions");

        Assert.True(lowNotes.Baseline < spacer.Baseline - 0.1,
            "control: the drop must respond to ink in this staff's profile: "
            + $"low notes {lowNotes.Baseline:F6}, spacer control {spacer.Baseline:F6}");

        Assert.True(moved.Baseline < spacer.Baseline - 0.1,
            "the figures must drop below the rest pushed out of the staff: "
            + $"printed rests {moved.Baseline:F6}, spacer control {spacer.Baseline:F6}");
    }
}
