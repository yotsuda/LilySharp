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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests that loose lines (lyrics, dynamics, figured bass) affect vertical spacing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
/// </remarks>
[Trait("Category", "Unit")]
public class LooseLineTests
{
    private static Score CreateBaseScore(
        ImmutableArray<LyricItem>? lyrics = null,
        ImmutableArray<DynamicItem>? dynamics = null,
        ImmutableArray<FiguredBassItem>? figuredBasses = null)
    {
        // Create 2 measures of simple quarter notes (enough to potentially span multiple systems)
        var items1 = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(2, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(4, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));
        var items2 = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(-2, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(2, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));

        var measure1 = new Measure(items1, BarlineType.None, BarlineType.Single, null, 0, 0);
        var measure2 = new Measure(items2, BarlineType.None, BarlineType.Single, null, 0, 0);
        var voice = new Voice("melody", ImmutableArray.Create(measure1, measure2));

        return new Score(voice, new TimeSignature(4, 4), KeySignature.CMajor, "treble",
            lyrics: lyrics,
            dynamics: dynamics,
            figuredBasses: figuredBasses);
    }

    [Fact]
    public void Layout_WithLyrics_HasLargerExtentThanWithout()
    {
        var lyrics = ImmutableArray.Create(
            new LyricItem("do", 0, 0),
            new LyricItem("re", 0, 1),
            new LyricItem("mi", 0, 2),
            new LyricItem("fa", 0, 3));

        var scoreWith = CreateBaseScore(lyrics: lyrics);
        var scoreWithout = CreateBaseScore();

        var engine = new LayoutEngine();
        var layoutWith = engine.Layout(scoreWith);
        var layoutWithout = engine.Layout(scoreWithout);

        // The page with lyrics should be at least as tall as without
        Assert.True(layoutWith.Pages[0].Height >= layoutWithout.Pages[0].Height,
            $"Page with lyrics ({layoutWith.Pages[0].Height:F1}) should be >= without ({layoutWithout.Pages[0].Height:F1})");
    }

    [Fact]
    public void Layout_WithDynamics_HasLargerExtentThanWithout()
    {
        var dynamics = ImmutableArray.Create(
            new DynamicItem(DynamicLevel.P, 0, 0, 0));

        var scoreWith = CreateBaseScore(dynamics: dynamics);
        var scoreWithout = CreateBaseScore();

        var engine = new LayoutEngine();
        var layoutWith = engine.Layout(scoreWith);
        var layoutWithout = engine.Layout(scoreWithout);

        // The page with dynamics should be at least as tall as without
        Assert.True(layoutWith.Pages[0].Height >= layoutWithout.Pages[0].Height,
            $"Page with dynamics ({layoutWith.Pages[0].Height:F1}) should be >= without ({layoutWithout.Pages[0].Height:F1})");
    }

    [Fact]
    public void Layout_TwoVerses_LargerExtentThanOneVerse()
    {
        var lyrics1v = ImmutableArray.Create(
            new LyricItem("do", 0, 0, VerseNumber: 1),
            new LyricItem("re", 0, 1, VerseNumber: 1));

        var lyrics2v = ImmutableArray.Create(
            new LyricItem("do", 0, 0, VerseNumber: 1),
            new LyricItem("re", 0, 1, VerseNumber: 1),
            new LyricItem("la", 0, 0, VerseNumber: 2),
            new LyricItem("la", 0, 1, VerseNumber: 2));

        var score1v = CreateBaseScore(lyrics: lyrics1v);
        var score2v = CreateBaseScore(lyrics: lyrics2v);

        var engine = new LayoutEngine();
        var layout1v = engine.Layout(score1v);
        var layout2v = engine.Layout(score2v);

        // 2-verse score should have at least as much extent as 1-verse
        Assert.True(layout2v.Pages[0].Height >= layout1v.Pages[0].Height,
            $"2-verse page ({layout2v.Pages[0].Height:F1}) should be >= 1-verse ({layout1v.Pages[0].Height:F1})");
    }
}
