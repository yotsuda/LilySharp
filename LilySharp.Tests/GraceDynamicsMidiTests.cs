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
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A dynamic written on a grace is struck as a full-size note's is: a level stands from the grace
/// on, an accent is the grace's alone. The page draws it under the grace. Until session 1347 the
/// MIDI read none of them: <c>c''4@p grace { e''16@f } d''4 e'' f''</c> played every note at p
/// (Lab sessions/p1347/grace g1, g4, g5).
/// </summary>
[Trait("Category", "Unit")]
public class GraceDynamicsMidiTests
{
    private static int[] Velocities(string bars)
        => new MidiExporter().Export(SyntaxTree.Parse(
                "octave absolute\ntime 4/4\npart m { clef treble }\nsection A { m { " + bars
                + " } }\nform { ~A }\nscore { staff m }\n"))
            .Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => n.Velocity).ToArray();

    [Theory]
    // A level stands from the grace on.
    [InlineData("c''4@p grace { e''16@f } d''4 e''4 f''4 | c''1 |", "c''4@p e''4@f d''4 e''4 | f''4 c''2. |")]
    // …a grace chord's too, on both its heads.
    [InlineData("c''4@p grace { <e'' g''>16@f } d''4 e''4 f''4 | c''1 |", "c''4@p <e'' g''>4@f d''4 e''4 | f''4 c''2. |")]
    // The last of the group's levels is the one that stands.
    [InlineData("c''4@f grace { e''16@p f''16@pp } d''4 e''4 f''4 | c''1 |", "c''4@f e''4@p f''4@pp d''4 | e''4 f''4 c''2 |")]
    // An accent is the grace's alone; the level stands as it stood.
    [InlineData("c''4@p grace { e''16@sfz } d''4 e''4 f''4 | c''1 |", "c''4@p e''4@sfz d''4 e''4 | f''4 c''2. |")]
    public void AGracesDynamic_SoundsAsAFullSizeNotesDoes(string grace, string fullSize)
        => Assert.Equal(Velocities(fullSize), Velocities(grace));

    /// <summary>An empty chord's script before the grace is played over the grace's own level,
    /// as <c>ProcessNote</c> plays a note's (its dynamic, then the held scripts).</summary>
    [Fact]
    public void AnEmptyChordsAccent_IsPlayedOverTheGracesOwnLevel()
        => Assert.Equal(Velocities("c''4@p e''4@f@accent d''4 e''4 | f''4 c''2. |"),
            Velocities("c''4@p <>@accent grace { e''16@f } d''4 e''4 f''4 | c''1 |"));
}
