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

    /// <summary>
    /// The rule that replaced the fifteen-entry table answers those fifteen exactly as the
    /// table did — the port must not buy the wrap by moving the ordinary keys.
    /// </summary>
    /// <remarks>
    /// The expected numbers are the retired dictionary, copied here ON PURPOSE: this is the
    /// one place a second spelling earns its keep, because its whole job is to disagree if
    /// the rule ever drifts off the values fifteen years of scores were engraved with.
    /// </remarks>
    [Theory]
    [InlineData("c", 0)] [InlineData("g", 1)] [InlineData("d", 2)] [InlineData("a", 3)]
    [InlineData("e", 4)] [InlineData("b", 5)] [InlineData("fis", 6)] [InlineData("cis", 7)]
    [InlineData("f", -1)] [InlineData("bes", -2)] [InlineData("ees", -3)] [InlineData("aes", -4)]
    [InlineData("des", -5)] [InlineData("ges", -6)] [InlineData("ces", -7)]
    public void TheRule_AnswersTheRetiredTableExactly(string tonic, int expected) =>
        Assert.Equal(expected, KeySpelling.SharpsFor(tonic, "major"));

    /// <summary>
    /// LilyPond's Dutch contractions reach the rule. <c>es</c> IS <c>ees</c> and <c>as</c> IS
    /// <c>aes</c>, so all four spellings must give one answer.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS WHY THE DECODE MOVED INTO KeySpelling. Six of the eight callers passed raw
    /// token text rather than <c>PitchSyntax.PitchName</c>, and the table held only the long
    /// spellings — measured 2026-08-24: <c>key es major</c> drew three flats on the page and
    /// wrote <c>&lt;fifths&gt;0&lt;/fifths&gt;</c> to MusicXML, one book with two keys. See
    /// <c>KeyIsOneQuantityAcrossOutputsTests</c> for the reading that catches it end to end.
    /// </remarks>
    [Theory]
    [InlineData("es", -3)] [InlineData("ees", -3)]
    [InlineData("as", -4)] [InlineData("aes", -4)]
    public void TheDutchContractions_AreTheirLongSpellings(string tonic, int expected) =>
        Assert.Equal(expected, KeySpelling.SharpsFor(tonic, "major"));

    /// <summary>An octave mark on a tonic is meaningless but writable, and it used to zero
    /// the key rather than being ignored.</summary>
    [Theory]
    [InlineData("ees,")] [InlineData("ees'")] [InlineData("ees''")]
    public void OctaveMarksOnATonic_DoNotZeroTheKey(string tonic) =>
        Assert.Equal(-3, KeySpelling.SharpsFor(tonic, "major"));

    /// <summary>
    /// A tonic that is not a note name has no signature — the rule says so rather than
    /// answering C.
    /// </summary>
    /// <remarks>
    /// LilyPond refuses these too (measured 2026-08-24: <c>\key ef \major</c> gives "wrong
    /// type for argument 1. Expecting pitch, found \"ef\""). ⚠️ The quarter-tone spellings
    /// are in this list deliberately: they lex as NOTES, so a decode that shared
    /// <c>PitchSyntax.AccidentalOffset</c> would read <c>cih</c> as C and re-open the silence
    /// one spelling narrower.
    /// </remarks>
    [Theory]
    [InlineData("ef")] [InlineData("eb")] [InlineData("bf")] [InlineData("gs")]
    [InlineData("h")] [InlineData("bogus")] [InlineData("x")] [InlineData("")]
    [InlineData("cih")] [InlineData("ceh")] [InlineData("cisih")] [InlineData("ceseh")]
    public void ATonicThatIsNotANoteName_HasNoSignature(string tonic) =>
        Assert.Null(KeySpelling.SharpsFor(tonic, "major"));

    /// <summary>
    /// Every tonic the language lexes as a NOTE has a signature. There is no key LilyPond can
    /// spell and Lily# cannot.
    /// </summary>
    /// <remarks>
    /// The far entries were the fifteen-entry table's blind spot: <c>gis</c> is eight fifths
    /// up and <c>ceses</c> fourteen down, and both engraved as C major in silence until
    /// 2026-08-24 (ledger key.signature.glyphs.tonic-past-the-table).
    /// </remarks>
    [Theory]
    [InlineData("gis", 8)] [InlineData("dis", 9)] [InlineData("ais", 10)]
    [InlineData("eis", 11)] [InlineData("bis", 12)] [InlineData("cisis", 14)]
    [InlineData("fes", -8)] [InlineData("beses", -9)] [InlineData("eeses", -10)]
    [InlineData("aeses", -11)] [InlineData("deses", -12)] [InlineData("ceses", -14)]
    public void EveryTonicTheLanguageLexes_HasASignature(string tonic, int expected) =>
        Assert.Equal(expected, KeySpelling.SharpsFor(tonic, "major"));

    /// <summary>
    /// The signature LilyPond actually engraves, for the keys whose leading letters double.
    /// Each row is real LilyPond 2.26.0's <c>alteration-alist</c>, read off the grob.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED 2026-08-24 with audit/lp-geometry/probes/key-signature-wrap.ly and a wider
    /// sweep of 26 tonics × 4 modes = 104 signatures, 0 mismatches against this rule. The
    /// alteration is written in whole tones by LilyPond (1/2 single, 1 double) and in
    /// accidental steps here, so a LilyPond 1 is a 2 below.
    /// </para>
    /// <para>
    /// ⚠️ The list is SEVEN pairs long however far round the circle the key sits, because
    /// there are seven letters. "Eight sharps" is seven symbols, not eight — the reading that
    /// a count of accidentals cannot make.
    /// </para>
    /// <para>
    /// ⚠️ COMPARE THE MAP, NOT THE SEQUENCE. The alists below are quoted verbatim from the
    /// dump, and LilyPond emits them in the order its TRANSPOSITION produced, which is not
    /// the order the signature is PRINTED in (scm/output-lib.scm sorts by
    /// alteration-positions). The expectations are in print order; only the step→alteration
    /// pairing is being claimed.
    /// </para>
    /// </remarks>
    [Theory]
    // ((6 . 1/2) (2 . 1/2) (5 . 1/2) (1 . 1/2) (4 . 1/2) (0 . 1/2) (3 . 1/2))
    [InlineData("cis", "major", "3:1,0:1,4:1,1:1,5:1,2:1,6:1")]
    // ((3 . 1) (6 . 1/2) (2 . 1/2) (5 . 1/2) (1 . 1/2) (4 . 1/2) (0 . 1/2))
    [InlineData("cis", "lydian", "3:2,0:1,4:1,1:1,5:1,2:1,6:1")]
    // ((3 . 1) (6 . 1/2) (2 . 1/2) (5 . 1/2) (1 . 1/2) (4 . 1/2) (0 . 1/2))
    //   — byte-identical to cis lydian, from a tonic the retired table lacked.
    [InlineData("gis", "major", "3:2,0:1,4:1,1:1,5:1,2:1,6:1")]
    // ((4 . -1) (1 . -1) (5 . -1) (2 . -1) (6 . -1) (3 . -1/2) (0 . -1/2))
    [InlineData("ces", "locrian", "6:-2,2:-2,5:-2,1:-2,4:-2,0:-1,3:-1")]
    // ((6 . -1) (3 . -1/2) (0 . -1/2) (4 . -1/2) (1 . -1/2) (5 . -1/2) (2 . -1/2))
    [InlineData("ees", "locrian", "6:-2,2:-1,5:-1,1:-1,4:-1,0:-1,3:-1")]
    public void TheSignature_IsLilyPondsAlterationAlist(string tonic, string mode, string expected)
    {
        int sharps = KeySpelling.SharpsFor(tonic, mode)!.Value;
        string actual = string.Join(",",
            KeySpelling.SignatureSteps(sharps).Select(p => $"{p.Step}:{p.Alter}"));
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Below the wrap the walk is exactly what the capped loops produced — the first
    /// |sharps| steps of the print order, each single. This is the half of the port that had
    /// to move NOTHING, and every engraved score in the corpus lives here.
    /// </summary>
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(-1)] [InlineData(-2)] [InlineData(-3)]
    [InlineData(-4)] [InlineData(-5)] [InlineData(-6)] [InlineData(-7)]
    public void BelowTheWrap_TheWalkIsTheOldCappedLoop(int sharps)
    {
        var expected = KeySpelling.PrintOrder(sharps)
            .Take(System.Math.Abs(sharps))
            .Select(step => (step, System.Math.Sign(sharps)))
            .ToArray();
        Assert.Equal(expected, KeySpelling.SignatureSteps(sharps).ToArray());
    }

    /// <summary>A signature never has more symbols than there are letters, however far round
    /// the circle it sits.</summary>
    [Fact]
    public void NoSignatureEverHasMoreThanSevenSymbols()
    {
        for (int sharps = -21; sharps <= 21; sharps++)
            Assert.InRange(KeySpelling.SignatureSteps(sharps).Count, 0, 7);
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
