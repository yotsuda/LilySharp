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
using LilySharp.Core.Music;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Key-aware note spelling: the diatonic letters spelled under a key signature
/// (drives the editor's key-aware note completions).
/// </summary>
[Trait("Category", "Unit")]
public class KeySpellingTests
{
    private static string[] Diatonic(int sharps) =>
        "cdefgab".Select(l => KeySpelling.SpellLetter(l, sharps)).ToArray();

    [Fact]
    public void GMajor_SharpensF()
    {
        int sharps = KeySpelling.SharpsFor("g", "major")!.Value;
        Assert.Equal(1, sharps);
        Assert.Equal(new[] { "c", "d", "e", "fis", "g", "a", "b" }, Diatonic(sharps));
    }

    [Fact]
    public void DMajor_SharpensFAndC()
    {
        int sharps = KeySpelling.SharpsFor("d", "major")!.Value;
        Assert.Equal(2, sharps);
        Assert.Equal(new[] { "cis", "d", "e", "fis", "g", "a", "b" }, Diatonic(sharps));
    }

    [Fact]
    public void FMajor_FlattensB()
    {
        int sharps = KeySpelling.SharpsFor("f", "major")!.Value;
        Assert.Equal(-1, sharps);
        Assert.Equal(new[] { "c", "d", "e", "f", "g", "a", "bes" }, Diatonic(sharps));
    }

    [Fact]
    public void EFlatMajor_FlattensBEA()
    {
        // ees major = 3 flats: B, E, A.
        int sharps = KeySpelling.SharpsFor("ees", "major")!.Value;
        Assert.Equal(-3, sharps);
        Assert.Equal(new[] { "c", "d", "ees", "f", "g", "aes", "bes" }, Diatonic(sharps));
    }

    [Fact]
    public void CMajor_AllNatural()
    {
        Assert.Equal(0, KeySpelling.SharpsFor("c", "major")!.Value);
        Assert.Equal(new[] { "c", "d", "e", "f", "g", "a", "b" }, Diatonic(0));
    }

    [Fact]
    public void AMinor_IsCMajorSignature()
    {
        // a minor = relative of C major = 0 accidentals.
        Assert.Equal(0, KeySpelling.SharpsFor("a", "minor")!.Value);
    }

    [Fact]
    public void EMinor_SharpensF()
    {
        // e minor = relative of G major = one sharp (F).
        int sharps = KeySpelling.SharpsFor("e", "minor")!.Value;
        Assert.Equal(1, sharps);
        Assert.Equal("fis", KeySpelling.SpellLetter('f', sharps));
    }

    [Fact]
    public void UnknownTonic_ReturnsNull()
    {
        Assert.Null(KeySpelling.SharpsFor("h", "major"));
    }

    [Fact]
    public void CSharpMajor_AllSevenSingleSharps()
    {
        // Boundary: 7 sharps = every letter single-sharp, no doubles.
        int sharps = KeySpelling.SharpsFor("cis", "major")!.Value;
        Assert.Equal(7, sharps);
        Assert.Equal(new[] { "cis", "dis", "eis", "fis", "gis", "ais", "bis" }, Diatonic(sharps));
    }

    [Fact]
    public void CSharpLydian_DoubleSharpsF()
    {
        // 8 sharps: the order wraps and F is double-sharped (fisis). The old
        // Alteration capped at 7 and dropped this, spelling F as "fis".
        int sharps = KeySpelling.SharpsFor("cis", "lydian")!.Value;
        Assert.Equal(8, sharps);
        Assert.Equal(new[] { "cis", "dis", "eis", "fisis", "gis", "ais", "bis" }, Diatonic(sharps));
        Assert.Equal(2, KeySpelling.Alteration(KeySpelling.StepOf('f'), sharps));
    }

    [Fact]
    public void EFlatLocrian_DoubleFlatsB()
    {
        // 8 flats: B is double-flatted (beses), the rest single.
        int flats = KeySpelling.SharpsFor("ees", "locrian")!.Value;
        Assert.Equal(-8, flats);
        Assert.Equal(new[] { "ces", "des", "ees", "fes", "ges", "aes", "beses" }, Diatonic(flats));
        Assert.Equal(-2, KeySpelling.Alteration(KeySpelling.StepOf('b'), flats));
    }
}
