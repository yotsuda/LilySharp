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

using LilySharp.Core.Semantics;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The shared closest-octave rule (LILYPOND-REF: lily/pitch.cc
/// Pitch::to_relative_octave) used by MeasureCollector, MidiExporter,
/// MusicXmlExporter, and RelativePitchResolver.
/// </summary>
[Trait("Category", "Unit")]
public class RelativeOctaveTests
{
    // c=0 d=1 e=2 f=3 g=4 a=5 b=6

    [Theory]
    // Same step stays in the same octave.
    [InlineData(0, 4, 0, 4)]
    // Up to a fourth above: same octave (c → f).
    [InlineData(0, 4, 3, 4)]
    // A fifth above is reached downward (c → g picks the g BELOW per
    // closest-step distance: up=4 steps, down=3 steps).
    [InlineData(0, 4, 4, 3)]
    // b → c goes UP across the octave boundary (closest c is above).
    [InlineData(6, 4, 0, 5)]
    // c → b goes DOWN across the octave boundary.
    [InlineData(0, 4, 6, 3)]
    // a → c: up candidate (2 steps) beats down candidate (5 steps).
    [InlineData(5, 4, 0, 5)]
    public void Resolve_PicksClosestOctave(int prevStep, int prevOctave, int step, int expected)
    {
        Assert.Equal(expected, RelativeOctave.Resolve(prevStep, prevOctave, step, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Resolve_AppliesExplicitOffsetOnTop(int offset)
    {
        int baseline = RelativeOctave.Resolve(0, 4, 3, 0);
        Assert.Equal(baseline + offset, RelativeOctave.Resolve(0, 4, 3, offset));
    }

    [Fact]
    public void StepIndex_MapsDiatonicSteps()
    {
        Assert.Equal(0, RelativeOctave.StepIndex('c'));
        Assert.Equal(6, RelativeOctave.StepIndex('b'));
        Assert.Equal(4, RelativeOctave.StepIndex('G')); // case-insensitive
    }
}
