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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every voice of a <c>voice { } { }</c> span reads the key in force where the span OPENS.
/// </summary>
/// <remarks>
/// Voices 2..N are collected after the whole primary stream (BuildExtraVoiceTracks), and
/// they used to read the metadata the part ENDED in. In
/// <c>key e major voice { … } { &lt;a' dis'&gt;2 }</c> of a book that later returns to
/// D-flat, voice 2 was spelled in D-flat — a ♮ on the a, a ♯ on the dis — and the ♯ ran
/// into voice 1's eighth-note flag (03-piano-nocturne, 2026-09-26). LilyPond prints neither
/// accidental: A and D♯ are in E major. The last bar's key change is what makes the old
/// reading wrong; without it the end state and the opening state agree.
/// </remarks>
[Trait("Category", "Unit")]
public class VoiceSpanKeyTests
{
    private static Score Collect(string music)
    {
        var tree = MusicSource.Parse(music);
        Assert.False(tree.HasErrors);
        return new MeasureCollector().Collect(tree, null);
    }

    [Fact]
    public void AChordInTheSecondVoice_IsSpelledInTheKeyTheSpanOpensIn()
    {
        var score = Collect("key e major voice { gis''1 } { <a' dis''>1 } | key des major c'1 |");

        var chord = score.Voices[1].Measures[0].Items.OfType<ChordItem>().Single();
        Assert.All(chord.Notes, n => Assert.Null(n.Accidental));
    }

    [Fact]
    public void ANoteInTheSecondVoice_IsSpelledInTheKeyTheSpanOpensIn()
    {
        var score = Collect("key e major voice { gis''1 } { dis''1 } | key des major c'1 |");

        var note = score.Voices[1].Measures[0].Items.OfType<NoteItem>().Single();
        Assert.Null(note.Accidental);
    }

    /// <summary>The positive control: an accidental the opening key does NOT cover still
    /// prints, so the tests above cannot pass by printing nothing at all.</summary>
    [Fact]
    public void AnAccidentalOutsideTheOpeningKey_StillPrints()
    {
        var score = Collect("key e major voice { gis''1 } { d''1 } | key des major c'1 |");

        var note = score.Voices[1].Measures[0].Items.OfType<NoteItem>().Single();
        Assert.Equal("natural", note.Accidental);
    }
}
