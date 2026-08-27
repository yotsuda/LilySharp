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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Pins for <see cref="ScoreSideTables"/> / <see cref="IndexBuckets{T}"/> — the
/// measure-bucketed side-table index the per-measure spring builders read instead of
/// filtering the FULL <c>score.Lyrics</c> / <c>score.ChordNames</c> on every measure.
/// Two contracts matter and are pinned here rather than left to the geometry nets:
/// ⑴ a bucket is EXACTLY the full-scan filter's answer, in the full scan's ORDER
/// (list append order and first-syllable ownEdge resolution ride on it), and
/// ⑵ repeated asks for one score answer the SAME instance (RULES §5.4's one-list
/// rule: the break gate and the system layout must price a bar off one table).
/// </summary>
[Trait("Category", "Unit")]
public class ScoreSideTablesTests
{
    private static LyricItem Ly(string text, int measure, int verse = 1) =>
        new LyricItem(text, measure, 0, VerseNumber: verse);

    [Fact]
    public void Buckets_AreTheFullScanFilter_InDocumentOrder()
    {
        // Interleaved across measures ON PURPOSE: the bucket must keep each measure's
        // items in document order even when the document does not group them.
        var lyrics = new[]
        {
            Ly("a0", 0), Ly("b2", 2), Ly("c0", 0, verse: 2), Ly("d1", 1), Ly("e2", 2), Ly("f0", 0),
        };
        var buckets = ScoreSideTables.BucketLyrics(lyrics);

        for (int m = 0; m <= 2; m++)
            Assert.Equal(
                lyrics.Where(ly => ly.MeasureIndex == m).Select(ly => ly.Text),
                buckets.At(m).Select(ly => ly.Text));
    }

    [Fact]
    public void Buckets_OutOfRangeMeasures_AnswerEmpty()
    {
        // GroupByLine asks for measure −1 (before the first bar) and one past the
        // end — the full scan answered those with "no items", so must the buckets.
        var buckets = ScoreSideTables.BucketLyrics(new[] { Ly("a", 3), Ly("b", 5) });
        Assert.True(buckets.At(-1).IsEmpty);
        Assert.True(buckets.At(2).IsEmpty);   // inside no bucket, below the observed min
        Assert.True(buckets.At(4).IsEmpty);   // a gap between occupied measures
        Assert.True(buckets.At(6).IsEmpty);   // past the observed max
        Assert.False(buckets.At(3).IsEmpty);
        Assert.False(buckets.At(5).IsEmpty);
    }

    [Fact]
    public void Buckets_DropNoItem_WhateverItsMeasureIndex()
    {
        // The full scan compared MeasureIndex by equality and assumed nothing about
        // its range; the buckets must not silently narrow that (an item on a
        // negative index stays reachable exactly where the filter found it).
        var buckets = ScoreSideTables.BucketLyrics(new[] { Ly("neg", -2), Ly("pos", 1) });
        Assert.Equal("neg", Assert.Single(buckets.At(-2)).Text);
        Assert.Equal("pos", Assert.Single(buckets.At(1)).Text);
    }

    [Fact]
    public void ScoreAccessors_AnswerTheSameInstancePerScore()
    {
        // The one-list rule made structural: the gate and the layout both fetch the
        // score's buckets, so one construction must serve every consumer — a rebuild
        // per caller would be two chances for the tables to drift apart.
        var score = MakeScore(
            lyrics: ImmutableArray.Create(Ly("la", 0)),
            chords: ImmutableArray.Create(new ChordNameItem("C", 0, 0, 0)));

        Assert.Same(ScoreSideTables.Lyrics(score), ScoreSideTables.Lyrics(score));
        Assert.Same(ScoreSideTables.ChordNames(score), ScoreSideTables.ChordNames(score));
        Assert.Same(MmrRunMap.ForScore(score), MmrRunMap.ForScore(score));
    }

    [Fact]
    public void ScoreAccessors_ReadTheScoresOwnTables()
    {
        var score = MakeScore(
            lyrics: ImmutableArray.Create(Ly("la", 1), Ly("li", 4)),
            chords: ImmutableArray.Create(
                new ChordNameItem("C", 2, 0, 0), new ChordNameItem("G7", 2, 1, 0)));

        Assert.Equal(new[] { "la" },
            ScoreSideTables.Lyrics(score).At(1).Select(ly => ly.Text));
        Assert.Equal(new[] { "C", "G7" },
            ScoreSideTables.ChordNames(score).At(2).Select(cn => cn.ChordText));
        Assert.True(ScoreSideTables.Lyrics(score).At(0).IsEmpty);
        Assert.True(ScoreSideTables.ChordNames(score).At(0).IsEmpty);
    }

    [Fact]
    public void StaffBuckets_AreTheFullScanFilter_InDocumentOrder_AndMemoized()
    {
        // The per-(system, staff) skyline pass cuts its staff slices from these —
        // same contract as the measure buckets: the .Where's answer, in its order,
        // one construction per score.
        var dynamics = ImmutableArray.Create(
            Dyn("p", staff: 1), Dyn("f", staff: 0), Dyn("mf", staff: 1));
        var score = MakeScore(
            lyrics: ImmutableArray<LyricItem>.Empty,
            chords: ImmutableArray<ChordNameItem>.Empty,
            dynamics: dynamics);

        var byStaff = ScoreSideTables.DynamicsByStaff(score);
        for (int s = 0; s <= 1; s++)
            Assert.Equal(
                dynamics.Where(d => d.StaffIndex == s).Select(d => d.Text),
                byStaff.At(s).Select(d => d.Text));
        Assert.True(byStaff.At(2).IsEmpty);
        Assert.Same(byStaff, ScoreSideTables.DynamicsByStaff(score));
    }

    private static DynamicItem Dyn(string text, int staff) =>
        new DynamicItem(text, 0, 0, 0, staffIndex: staff);

    /// <summary>A minimal multi-staff score carrying the given side tables.</summary>
    private static MultiStaffScore MakeScore(
        ImmutableArray<LyricItem> lyrics, ImmutableArray<ChordNameItem> chords,
        ImmutableArray<DynamicItem>? dynamics = null)
    {
        var voice = new Voice("v", ImmutableArray<Measure>.Empty);
        var staff = new Staff(ClefType.Treble, ImmutableArray.Create(voice));
        var groups = ImmutableArray.Create(
            new StaffGroup(StaffGroupType.Single, ImmutableArray.Create(staff)));
        var content = new LilySharp.Core.Svg.Collector.ScoreContent(
            new TimeSignature(4, 4, "4/4", false),
            new KeySignature(0, null),
            "treble",
            Tempo: 120,
            Title: null,
            Composer: null,
            SwingSubdivision: 0,
            Dynamics: dynamics ?? ImmutableArray<DynamicItem>.Empty,
            Articulations: ImmutableArray<ArticulationItem>.Empty,
            GraceNotes: ImmutableArray<GraceNoteItem>.Empty,
            Lyrics: lyrics,
            MusicMarks: ImmutableArray<MusicMarkItem>.Empty,
            CustomTexts: ImmutableArray<CustomTextItem>.Empty,
            VoltaBrackets: ImmutableArray<VoltaBracketItem>.Empty,
            TupletBrackets: ImmutableArray<TupletBracketItem>.Empty,
            Arpeggios: ImmutableArray<ArpeggioItem>.Empty,
            FiguredBasses: ImmutableArray<FiguredBassItem>.Empty,
            ChordNames: chords,
            PercentRepeats: ImmutableArray<PercentRepeatItem>.Empty,
            CrossStaffItems: ImmutableArray<CrossStaffItem>.Empty,
            GrobOverrides: ImmutableArray<GrobOverride>.Empty,
            GrobReverts: ImmutableArray<GrobRevert>.Empty,
            TrillSpanners: ImmutableArray<TrillSpannerItem>.Empty,
            Header: new HeaderPositions(0, 0, 0, 0, 0),
            TempoText: null,
            TempoBeatUnit: 0,
            TempoDots: 0,
            Fonts: LilySharp.Core.Rendering.TextFontPlan.Default,
            Paper: LayoutOptions.Default);
        return LilySharp.Core.Svg.Collector.ScoreAssembler.BuildMultiStaffScore(groups, content);
    }
}
