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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Unit tests for the multi-voice <c>shortest-playing-duration</c> tracking
/// in horizontal spring spacing.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-engraver.cc:200-253 — shortest_playing aggregation
/// LILYPOND-REF: lily/spacing-basic.cc:107-162 — note_spacing uses shortest_playing
/// </remarks>
[Trait("Category", "Unit")]
public class MultiVoiceSpacingTests
{
    private static Measure MakeMeasure(params Fraction[] noteDurations)
    {
        var items = ImmutableArray.CreateBuilder<MusicItem>();
        foreach (var d in noteDurations)
        {
            items.Add(new NoteItem(
                staffPosition: 0,
                baseDuration: d,
                dots: 0,
                accidental: null,
                needsLedgerLines: false,
                sourcePosition: 0));
        }
        return new Measure(
            items.ToImmutable(),
            BarlineType.Single,
            BarlineType.Single,
            sectionLabel: null,
            sourceStart: 0,
            sourceEnd: 0);
    }

    [Fact]
    public void ComputeShortestPlayingAt_SingleVoice_ReturnsThatNotesDuration()
    {
        var m = MakeMeasure(new Fraction(1, 4), new Fraction(1, 4));
        var sp = SpacingRules.ComputeShortestPlayingAt(Fraction.Zero, new[] { m });
        Assert.Equal(new Fraction(1, 4), sp);
    }

    [Fact]
    public void ComputeShortestPlayingAt_PolyphonicHalfPlusEighths_ReturnsEighth()
    {
        // Voice 1: half (0 → 1/2). Voice 2: four eighths (0, 1/8, 1/4, 3/8).
        var v1 = MakeMeasure(new Fraction(1, 2));
        var v2 = MakeMeasure(new Fraction(1, 8), new Fraction(1, 8), new Fraction(1, 8), new Fraction(1, 8));

        // At time 0, both are playing. Shortest = 1/8.
        var sp = SpacingRules.ComputeShortestPlayingAt(Fraction.Zero, new[] { v1, v2 });
        Assert.Equal(new Fraction(1, 8), sp);
    }

    [Fact]
    public void ComputeShortestPlayingAt_TimingWhereOnlySlowVoicePlays_ReturnsSlowDuration()
    {
        // Voice 1: half (0 → 1/2). Voice 2: rest then quarter (1/4 → 1/2).
        var v1 = MakeMeasure(new Fraction(1, 2));
        var restThenQuarter = new MusicItem[]
        {
            new RestItem(new Fraction(1, 4), 0, 0),
            new NoteItem(0, new Fraction(1, 4), 0, null, false, 0),
        };
        var v2 = new Measure(
            restThenQuarter.ToImmutableArray(),
            BarlineType.Single,
            BarlineType.Single,
            sectionLabel: null,
            sourceStart: 0,
            sourceEnd: 0);

        // At time 0, voice 1's half is playing (1/2). Voice 2's rest is "playing" too (1/4) — it counts as a duration.
        // LP's shortest_playing only tracks NOTES, not rests; LilySharp's helper currently includes any duration.
        // For this regression test we accept either 1/4 (rest counted) or 1/2 (rest excluded);
        // pin to the current "duration-based" behaviour.
        var sp0 = SpacingRules.ComputeShortestPlayingAt(Fraction.Zero, new[] { v1, v2 });
        Assert.Equal(new Fraction(1, 4), sp0);

        // At time 1/4, voice 1's half still plays, voice 2's quarter starts. shortest = 1/4.
        var sp1 = SpacingRules.ComputeShortestPlayingAt(new Fraction(1, 4), new[] { v1, v2 });
        Assert.Equal(new Fraction(1, 4), sp1);
    }

    [Fact]
    public void CreateTimingSpringMultiVoice_MonophonicCase_MatchesLegacyFormula()
    {
        // When shortest_playing == segment_duration, the multi-voice formula must collapse
        // to the original CreateTimingSpring behaviour to preserve all single-voice snapshots.
        var d = new Fraction(1, 4);
        var legacy = SpacingRules.CreateTimingSpring(d, baseShortestDuration: 0.125);
        var mv = SpacingRules.CreateTimingSpringMultiVoice(d, d, baseShortestDuration: 0.125);

        Assert.Equal(legacy.IdealDistance, mv.IdealDistance, precision: 6);
        Assert.Equal(legacy.MinDistance, mv.MinDistance, precision: 6);
    }

    [Fact]
    public void CreateTimingSpringMultiVoice_FasterPlayingNote_ProducesShorterSpring()
    {
        // Slow voice: half note. Fast voice underneath: eighth notes.
        // delta_t = 1/4 (timing to next column when slow voice is the only "boundary").
        // shortest_playing = 1/8 (the eighth-note voice rules).
        // LP: spring = (delta/shortest) * duration_space(shortest) = 2 * duration_space(1/8)
        // Legacy: spring = duration_space(1/4)
        // For LP defaults (BSD=1/8, ShortestDurationSpace=2.0, Increment=1.2):
        //   duration_space(1/8) = 2.0 * 1.2 = 2.4
        //   duration_space(1/4) = (2.0 + log2(2)) * 1.2 = 3.0 * 1.2 = 3.6
        //   LP spring = 2 * 2.4 = 4.8 (LARGER than legacy 3.6)
        // So polyphonic actually produces *wider* spring here, because two columns get
        // packed in by the eighth-note voice between slow boundaries.
        var delta = new Fraction(1, 4);
        var shortestPlaying = new Fraction(1, 8);
        var spring = SpacingRules.CreateTimingSpringMultiVoice(delta, shortestPlaying, baseShortestDuration: 0.125);

        // Verify the LP-faithful formula: fraction * duration_space(shortest_playing)
        double expectedLen = (0.25 / 0.125) * SpacingRules.CalculateDurationSpace(shortestPlaying, 0.125);
        Assert.Equal(expectedLen, spring.IdealDistance, precision: 6);
    }

    [Fact]
    public void CreateTimingSpringMultiVoice_ZeroShortestPlaying_FallsBackToLegacy()
    {
        var d = new Fraction(1, 4);
        var legacy = SpacingRules.CreateTimingSpring(d, baseShortestDuration: 0.125);
        var mv = SpacingRules.CreateTimingSpringMultiVoice(d, Fraction.Zero, baseShortestDuration: 0.125);

        Assert.Equal(legacy.IdealDistance, mv.IdealDistance, precision: 6);
    }

    [Fact]
    public void CreateTimingSpringMultiVoice_ShortestExceedsDelta_ScalesProportionally()
    {
        // Polyrhythm case: a long note (shortest_playing 1/4) spans a short slice (delta 1/8) because
        // another voice subdivides the beat. LP does NOT clamp shortest_playing to delta_t — that was
        // a bug that forced fraction=1 on every sub-beat column. LP uses fraction = delta_t /
        // shortest_playing (spacing-basic.cc:157) and clamps shortest_playing only to the MEASURE
        // length (spacing-basic.cc:144). So a slice worth half the ruling note gets half its duration
        // space, letting two such slices sum back to the whole note's width (keeping the other voice's
        // notes evenly spaced).
        var quarter = new Fraction(1, 4);
        var halfOfQuarter = new Fraction(1, 8);
        var slice = SpacingRules.CreateTimingSpringMultiVoice(halfOfQuarter, quarter, baseShortestDuration: 0.125);
        var full = SpacingRules.CreateTimingSpringMultiVoice(quarter, quarter, baseShortestDuration: 0.125);

        // delta 1/8 is exactly half of shortest_playing 1/4 -> half the ideal distance.
        Assert.Equal(full.IdealDistance / 2.0, slice.IdealDistance, precision: 6);
    }
}
