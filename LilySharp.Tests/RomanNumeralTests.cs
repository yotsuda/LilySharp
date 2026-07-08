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
        Assert.Equal("I", Roman("c", 0, 0));
        Assert.Equal("IIm7", Roman("d:m7", 0, 0));
        Assert.Equal("IV", Roman("f", 0, 0));
        Assert.Equal("V7", Roman("g:7", 0, 0));
        Assert.Equal("VIm", Roman("a:m", 0, 0));
        Assert.Equal("Imaj7", Roman("c:maj7", 0, 0));
    }

    [Fact]
    public void AMinor_MeasuredFromItsOwnTonic()
    {
        // tonic A = step 5, 0 sharps: Am = I, Dm = IV, E7 = V7.
        Assert.Equal("Im", Roman("a:m", 5, 0));
        Assert.Equal("IVm", Roman("d:m", 5, 0));
        Assert.Equal("V7", Roman("e:7", 5, 0));
    }

    [Fact]
    public void FlatKey_TonicIsI()
    {
        // E-flat major: tonic step E = 2, 3 flats.
        Assert.Equal("I", Roman("ees", 2, -3));
        Assert.Equal("IV", Roman("aes", 2, -3));
        Assert.Equal("V7", Roman("bes:7", 2, -3));
    }

    [Fact]
    public void ChromaticRoot_GetsAccidentalPrefix()
    {
        // In C major: a D-flat root is ♭II; an F-sharp root is ♯IV.
        Assert.Equal("♭II", Roman("des", 0, 0));
        Assert.Equal("♯IV", Roman("fis", 0, 0));
    }

    [Fact]
    public void SlashBass_ShownAsDegree()
    {
        Assert.Equal("V7/VII", Roman("g:7/b", 0, 0));
    }
}
