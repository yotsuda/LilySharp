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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The staff's one timeline (docs/moment-walk-design.md, stage ②): every item of every voice
/// of a measure in moment order, the order a staff-level engraver meets them. Pins the merge
/// — by onset, the grace part before the main part, lower voice first on a tie, and never a
/// voice reordered against itself — which <c>VoiceCollector.CollectMeasure</c> and
/// <c>StaffAccidentals</c> both read.
/// </summary>
[Trait("Category", "Unit")]
public class StaffTimelineTests
{
    private static NoteItem Note(int staffPosition, Fraction duration, bool grace = false)
        => new NoteItem(staffPosition, duration, dots: 0, accidental: null, sourcePosition: staffPosition * 10) { GraceTime = grace };

    private static Voice VoiceOf(string name, params MusicItem[] items)
        => new Voice(name, ImmutableArray.Create(new Measure(
            items.ToImmutableArray(), BarlineType.None, BarlineType.Single,
            sectionLabel: null, sourceStart: 0, sourceEnd: 100)));

    private static string Spell(ImmutableArray<StaffTimeline.Entry> timeline)
        => string.Join(" ", timeline.Select(e => $"v{e.VoiceIndex}i{e.ItemIndex}@{e.Onset}{(e.GraceRank == 0 ? "g" : "")}"));

    /// <summary>Two voices with different rhythms interleave by onset; at one onset the lower
    /// voice index comes first.</summary>
    [Fact]
    public void VoicesInterleaveByOnset_LowerVoiceFirstOnATie()
    {
        var upper = VoiceOf("v1", Note(4, Fraction.Half), Note(5, Fraction.Half));
        var lower = VoiceOf("v2", Note(0, Fraction.Quarter), Note(1, Fraction.Quarter), Note(2, Fraction.Half));

        var timeline = StaffTimeline.OfMeasure(ImmutableArray.Create(upper, lower), 0);

        Assert.Equal("v0i0@0 v1i0@0 v1i1@1/4 v0i1@1/2 v1i2@1/2", Spell(timeline));
    }

    /// <summary>A grace of one voice stands before the other voice's main-time item at the
    /// same onset — the grace part of the moment comes first — and shares that onset.</summary>
    [Fact]
    public void AGraceOfOneVoice_PrecedesTheOtherVoicesItemAtItsOnset()
    {
        var upper = VoiceOf("v1", Note(4, Fraction.Quarter), Note(5, Fraction.Quarter));
        var lower = VoiceOf("v2", Note(0, Fraction.Quarter), Note(7, Fraction.Eighth, grace: true), Note(1, Fraction.Quarter));

        var timeline = StaffTimeline.OfMeasure(ImmutableArray.Create(upper, lower), 0);

        Assert.Equal("v0i0@0 v1i0@0 v1i1@1/4g v0i1@1/4 v1i2@1/4", Spell(timeline));
    }

    /// <summary>A voice is never reordered against itself: a clef written just before a grace
    /// stays before it although its rank is the main part's, while the OTHER voice's grace at
    /// that onset still comes before both.</summary>
    [Fact]
    public void AVoiceKeepsItsWrittenOrder_EvenWhereTheKeyIsNotMonotone()
    {
        var clef = new ClefChangeItem(ClefType.Bass, sourcePosition: 50);
        var upper = VoiceOf("v1", Note(4, Fraction.Quarter), clef, Note(7, Fraction.Eighth, grace: true), Note(5, Fraction.Quarter));
        var lower = VoiceOf("v2", Note(0, Fraction.Quarter), Note(8, Fraction.Eighth, grace: true), Note(1, Fraction.Quarter));

        var timeline = StaffTimeline.OfMeasure(ImmutableArray.Create(upper, lower), 0);

        // v1's grace (rank 0) outranks v0's clef (rank 1) at 1/4; v0's own grace follows its clef.
        Assert.Equal("v0i0@0 v1i0@0 v1i1@1/4g v0i1@1/4 v0i2@1/4g v0i3@1/4 v1i2@1/4", Spell(timeline));
        Assert.Same(clef, timeline[3].Item);
    }

    /// <summary>Rests are items of the timeline too (a staff-level reader may need the
    /// voice's silence), and a voice shorter than the measure index contributes nothing.</summary>
    [Fact]
    public void RestsAreEntries_AndAShortVoiceContributesNothing()
    {
        var upper = VoiceOf("v1", new RestItem(Fraction.Half, dots: 0, sourcePosition: 1), Note(5, Fraction.Half));
        var lower = new Voice("v2", ImmutableArray<Measure>.Empty);

        var timeline = StaffTimeline.OfMeasure(ImmutableArray.Create(upper, lower), 0);

        Assert.Equal("v0i0@0 v0i1@1/2", Spell(timeline));
        Assert.IsType<RestItem>(timeline[0].Item);
        Assert.Empty(StaffTimeline.OfMeasure(ImmutableArray.Create(upper, lower), 1));
    }

    /// <summary>The collision table's columns are the timeline's sounding main-time items
    /// grouped by onset, in voice order — what <c>VoiceCollector.CollectMeasure</c> built for
    /// itself until session 969.</summary>
    [Fact]
    public void TheVoiceColumns_AreTheTimelinesSoundingItemsGroupedByOnset()
    {
        var upper = VoiceOf("v1", Note(4, Fraction.Half), Note(5, Fraction.Half));
        var lower = VoiceOf("v2", new RestItem(Fraction.Quarter, dots: 0, sourcePosition: 1), Note(7, Fraction.Eighth, grace: true), Note(1, Fraction.Quarter), Note(2, Fraction.Half));
        var voices = ImmutableArray.Create(upper, lower);

        var columns = VoiceCollector.CollectMeasure(voices, 0);

        Assert.Equal(3, columns.Length);
        Assert.Equal(new[] { 1 }, columns[0].Entries.Select(e => e.VoiceId));           // v1's half at 0 (v2 rests)
        Assert.Equal(new[] { 2 }, columns[1].Entries.Select(e => e.VoiceId));           // v2's quarter at 1/4 (its grace is not a column)
        Assert.Equal(new[] { 1, 2 }, columns[2].Entries.Select(e => e.VoiceId));        // both at 1/2, voice order
        Assert.Equal(new[] { 1, 3 }, columns[2].Entries.Select(e => e.ItemIndex));
    }
}
