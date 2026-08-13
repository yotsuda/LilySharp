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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>voice { } { }</c> span that opens MID-measure starts its extra voices at the
/// span's beat, not at the head of the measure.
/// </summary>
/// <remarks>
/// Voice 0 flows inline in the primary stream, so in <c>c4 voice { e } { g' }</c> the
/// e always sat on beat 2 for free — but the reconstructed second track
/// (<see cref="MeasureCollector"/>'s BuildExtraVoiceTracks) collected <c>{ g' }</c>
/// with a fresh builder from the measure HEAD, drawing g' against the c on beat 1
/// (reported off scratch/ベースタブLy/voiceissue.lys, 2026-08-13). The recorded span now
/// carries the in-measure elapsed duration at its opening, and the sub-voice walk is
/// seeded with a leading spacer of that length — the same device PartCombiner uses to
/// pad a part up to an onset. LilyPond places both voices of the equivalent
/// <c>c4 &lt;&lt; { e } \\ { g } &gt;&gt;</c> on beat 2 (compiled 2.26.0, twin of the
/// report's fixture; the crossed-voice shift 0.4434 matches Lily#'s).
/// </remarks>
[Trait("Category", "Unit")]
public class VoiceSpanOnsetTests
{
    private static Score Collect(string music)
    {
        var tree = MusicSource.Parse(music);
        Assert.False(tree.HasErrors);
        return new MeasureCollector().Collect(tree, null);
    }

    [Fact]
    public void MidMeasureSpanPadsTheExtraVoiceUpToItsBeat()
    {
        var score = Collect("c'4 voice { e'2. } { g'2. } |");

        Assert.Equal(2, score.Voices.Length);
        var items = score.Voices[1].Measures[0].Items;
        var spacer = Assert.IsType<RestItem>(items[0]);
        Assert.True(spacer.IsSpacer);
        Assert.Equal(Fraction.Quarter, spacer.BaseDuration);
        Assert.IsType<NoteItem>(items[1]);
    }

    /// <summary>
    /// The user-visible claim: the second voice's note stands in the SAME column as the
    /// first voice's, and NOT in the c's. Fails on the pre-fix collector (the columns
    /// came out as [c+g'] [e]).
    /// </summary>
    [Fact]
    public void MidMeasureSpanAlignsAllItsVoicesOnTheSpansColumn()
    {
        var score = Collect("c'4 voice { e'2. } { g'2. } |");
        var columns = new VoiceCollector().Collect(score);

        Assert.Equal(2, columns.Length);
        Assert.Single(columns[0].Entries);          // the c, alone on beat 1
        Assert.Equal(2, columns[1].Entries.Length); // e and g' together on beat 2
    }

    /// <summary>A span at the measure head is the offset-zero path: byte-identical to
    /// before — no spacer.</summary>
    [Fact]
    public void MeasureHeadSpanGetsNoLeadingSpacer()
    {
        var score = Collect("voice { e'1 } { g'1 } |");

        Assert.Equal(2, score.Voices.Length);
        Assert.DoesNotContain(
            score.Voices[1].Measures[0].Items, i => i is RestItem { IsSpacer: true });
    }

    /// <summary>A mid-measure span in a LATER measure: the offset is within the span's
    /// own measure, composed with the measure-index shift.</summary>
    [Fact]
    public void MidMeasureSpanInALaterMeasureKeepsBothShifts()
    {
        var score = Collect("c'1 | c'2 voice { e'2 } { g'2 } |");

        var track2 = score.Voices[1].Measures;
        Assert.Empty(track2[0].Items);              // measure 1: placeholder
        var spacer = Assert.IsType<RestItem>(track2[1].Items[0]);
        Assert.True(spacer.IsSpacer);
        Assert.Equal(Fraction.Half, spacer.BaseDuration);
    }
}
