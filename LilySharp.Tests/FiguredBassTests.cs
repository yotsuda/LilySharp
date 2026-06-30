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
}
