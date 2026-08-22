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

using LilySharp.Core.Music;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Jazz-style Roman-numeral degrees: uppercase numeral for the root's scale degree
/// + the same quality suffix as the printed name (Imaj7, IIm7, V7), with ♯/♭ for a
/// chromatic root — measured from the key's actual tonic.
/// </summary>
public class RomanNumeralTests
{
    private static string Roman(string entry, int tonicStep, int keySharps)
    {
        Assert.True(ChordStructure.TryParseChordEntry(entry, out var s));
        return s.ToRomanNumeral(tonicStep, keySharps);
    }

    [Fact]
    public void CMajor_DiatonicDegrees()
    {
        // tonic C = step 0, 0 sharps.
        Assert.Equal("I", Roman("C", 0, 0));
        Assert.Equal("IIm7", Roman("Dm7", 0, 0));
        Assert.Equal("IV", Roman("F", 0, 0));
        Assert.Equal("V7", Roman("G7", 0, 0));
        Assert.Equal("VIm", Roman("Am", 0, 0));
        Assert.Equal("Imaj7", Roman("Cmaj7", 0, 0));
    }

    [Fact]
    public void AMinor_MeasuredFromItsOwnTonic()
    {
        // tonic A = step 5, 0 sharps: Am = I, Dm = IV, E7 = V7.
        Assert.Equal("Im", Roman("Am", 5, 0));
        Assert.Equal("IVm", Roman("Dm", 5, 0));
        Assert.Equal("V7", Roman("E7", 5, 0));
    }

    [Fact]
    public void FlatKey_TonicIsI()
    {
        // E-flat major: tonic step E = 2, 3 flats.
        Assert.Equal("I", Roman("Eb", 2, -3));
        Assert.Equal("IV", Roman("Ab", 2, -3));
        Assert.Equal("V7", Roman("Bb7", 2, -3));
    }

    [Fact]
    public void ChromaticRoot_GetsAccidentalPrefix()
    {
        // In C major: a D-flat root is ♭II; an F-sharp root is ♯IV.
        Assert.Equal("♭II", Roman("Db", 0, 0));
        Assert.Equal("♯IV", Roman("F#", 0, 0));
    }

    [Fact]
    public void SlashBass_ShownAsDegree()
    {
        Assert.Equal("V7/VII", Roman("G7/B", 0, 0));
    }

    [Fact]
    public void DiminishedAugmentedHalfDim_UseSymbolStyle()
    {
        // ° / +  / ø read better as Roman numerals than dim / aug / m7♭5.
        Assert.Equal("VII°", Roman("Bdim", 0, 0));
        Assert.Equal("VII°7", Roman("Bdim7", 0, 0));
        Assert.Equal("VIIø7", Roman("Bm7-5", 0, 0));
        Assert.Equal("I+", Roman("Caug", 0, 0));
    }
}
