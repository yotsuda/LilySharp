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
/// The diatonic-interval transposition (LILYPOND-REF: lily/pitch.cc
/// Pitch::transpose) used by the part-option <c>transpose:</c>. Steps are
/// c=0 … b=6; alterations are semitones (−2…+2).
/// </summary>
[Trait("Category", "Unit")]
public class PitchTransposerTests
{
    // ---- TryParseTarget ----

    [Theory]
    [InlineData("d", 1, 0)]
    [InlineData("c", 0, 0)]
    [InlineData("fis", 3, 1)]
    [InlineData("ees", 2, -1)]
    [InlineData("bes", 6, -1)]
    [InlineData("cisis", 0, 2)]
    [InlineData("aeses", 5, -2)]
    [InlineData("G", 4, 0)] // case-insensitive
    public void TryParseTarget_ParsesPitch(string text, int step, int alteration)
    {
        Assert.True(PitchTransposer.TryParseTarget(text, out int s, out int a));
        Assert.Equal(step, s);
        Assert.Equal(alteration, a);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("h")]      // not a–g
    [InlineData("r")]      // rest, not a pitch
    [InlineData("dx")]     // unknown accidental suffix
    [InlineData("4")]
    public void TryParseTarget_RejectsNonPitch(string? text)
    {
        Assert.False(PitchTransposer.TryParseTarget(text, out _, out _));
    }

    // ---- Transpose: the canonical c d e f → d e fis g (transpose: d) ----

    [Theory]
    // transpose: d (up a major 2nd): c→d, d→e, e→fis, f→g
    [InlineData(0, 0, 4, /*to*/ 1, 0, /*=>*/ 1, 0, 4)]  // c4 → d4
    [InlineData(1, 0, 4, 1, 0, 2, 0, 4)]                 // d4 → e4
    [InlineData(2, 0, 4, 1, 0, 3, 1, 4)]                 // e4 → fis4
    [InlineData(3, 0, 4, 1, 0, 4, 0, 4)]                 // f4 → g4
    // transpose: c is the identity (preserves any alteration)
    [InlineData(0, 1, 4, 0, 0, 0, 1, 4)]                 // cis4 → cis4
    // transpose: g (up a perfect 5th): f→c of the NEXT octave
    [InlineData(3, 0, 4, 4, 0, 0, 0, 5)]                 // f4 → c5
    // octave roll-over: b up a major 2nd → cis of the next octave
    [InlineData(6, 0, 4, 1, 0, 0, 1, 5)]                 // b4 → cis5
    // alteration carried through: cis up a major 2nd → dis
    [InlineData(0, 1, 4, 1, 0, 1, 1, 4)]                 // cis4 → dis4
    // transpose: ees (up a minor 3rd): c → ees
    [InlineData(0, 0, 4, 2, -1, 2, -1, 4)]               // c4 → ees4
    // can produce a double sharp: fis up an augmented unison (transpose: cis)
    [InlineData(3, 1, 4, 0, 1, 3, 2, 4)]                 // fis4 → fisis4
    public void Transpose_RespellsPreservingChroma(
        int step, int alt, int oct, int toStep, int toAlt,
        int expStep, int expAlt, int expOct)
    {
        var (s, a, o) = PitchTransposer.Transpose(step, alt, oct, toStep, toAlt);
        Assert.Equal(expStep, s);
        Assert.Equal(expAlt, a);
        Assert.Equal(expOct, o);
    }

    [Theory]
    // transpose: c, (DOWN an octave): every degree drops one octave, same spelling
    [InlineData(0, 0, 4, /*to*/ 0, 0, -1, /*=>*/ 0, 0, 3)]  // c4 -> c3
    [InlineData(2, 0, 4, 0, 0, -1, 2, 0, 3)]                 // e4 -> e3
    [InlineData(6, 0, 4, 0, 0, -1, 6, 0, 3)]                 // b4 -> b3
    // transpose: bes, (DOWN a major 2nd): c -> bes of the octave below
    [InlineData(0, 0, 4, 6, -1, -1, 6, -1, 3)]               // c4 -> bes3
    [InlineData(1, 0, 4, 6, -1, -1, 0, 0, 4)]                // d4 -> c4
    // transpose: d' (UP a ninth): c -> d of the octave above
    [InlineData(0, 0, 4, 1, 0, 1, 1, 0, 5)]                  // c4 -> d5
    [InlineData(3, 0, 4, 1, 0, 1, 4, 0, 5)]                  // f4 -> g5
    public void Transpose_HandlesOctaveDisplacedTargets(
        int step, int alt, int oct, int toStep, int toAlt, int toOct,
        int expStep, int expAlt, int expOct)
    {
        var (s, a, o) = PitchTransposer.Transpose(step, alt, oct, toStep, toAlt, toOct);
        Assert.Equal(expStep, s);
        Assert.Equal(expAlt, a);
        Assert.Equal(expOct, o);
    }

    [Fact]
    public void Transpose_PreservesSemitoneDistanceForEveryDegree()
    {
        // transpose: ees = +3 semitones; every transposed pitch must be exactly
        // 3 semitones above the original regardless of how it is spelled.
        PitchTransposer.TryParseTarget("ees", out int toStep, out int toAlt);
        int[] semis = { 0, 2, 4, 5, 7, 9, 11 };
        for (int step = 0; step < 7; step++)
        {
            var (s, a, o) = PitchTransposer.Transpose(step, 0, 4, toStep, toAlt);
            int origSemis = 4 * 12 + semis[step];
            int newSemis = o * 12 + semis[s] + a;
            Assert.Equal(origSemis + 3, newSemis);
        }
    }

    // ---- IntervalSemitones (sounding/MIDI shift) ----

    [Theory]
    [InlineData("c", 0, 0)]    // unison
    [InlineData("d", 0, 2)]    // major 2nd up
    [InlineData("ees", 0, 3)]  // minor 3rd up
    [InlineData("g", 0, 7)]    // perfect 5th up
    [InlineData("c", -1, -12)] // down an octave (transpose: c,)
    [InlineData("bes", -1, -2)]// down a major 2nd (transpose: bes,)
    [InlineData("d", 1, 14)]   // up a ninth (transpose: d')
    public void IntervalSemitones_MatchesChromaticSize(string target, int octave, int expected)
    {
        Assert.True(PitchTransposer.TryParseTarget(target, out int step, out int alt));
        Assert.Equal(expected, PitchTransposer.IntervalSemitones(step, alt, octave));
    }

    // ---- KeySignatureFifthsShift ----

    [Theory]
    [InlineData("c", 0)]
    [InlineData("g", 1)]
    [InlineData("d", 2)]
    [InlineData("a", 3)]
    [InlineData("f", -1)]
    [InlineData("bes", -2)]
    [InlineData("ees", -3)]
    [InlineData("fis", 6)]
    public void KeySignatureFifthsShift_MovesAlongCircleOfFifths(string target, int expected)
    {
        Assert.True(PitchTransposer.TryParseTarget(target, out int step, out int alt));
        Assert.Equal(expected, PitchTransposer.KeySignatureFifthsShift(step, alt));
    }
}
