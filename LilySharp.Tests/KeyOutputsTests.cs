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
using LilySharp.Core.LilyPond;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// One book has ONE key, and every output has to agree about which. Two of them did not.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ NEITHER HALF IS VISIBLE FROM ONE SIDE. <c>KeySpelling.SharpsFor</c> takes the tonic as
/// a STRING, and of its eight callers only the page and the LilyPond twin passed
/// <c>PitchSyntax.PitchName</c> — the spelling that normalizes LilyPond's Dutch contractions
/// <c>es</c>→<c>ees</c> and <c>as</c>→<c>aes</c>. The other six passed raw token text, which
/// the fifteen-entry table did not hold, so it returned null and every caller coerced that to
/// 0. MEASURED 2026-08-24: <c>key es major</c> DREW three flats and EXPORTED
/// <c>&lt;fifths&gt;0&lt;/fifths&gt;</c>. Every test that looked at the page was green and
/// every test that looked at MusicXML was green.
/// </para>
/// <para>
/// ⚠️ AND AN OCTAVE MARK DID THE SAME THING to a spelling the table DID hold: the raw text of
/// <c>key ees, major</c> is <c>ees,</c>, which is not <c>ees</c>. A tonic has no octave, so
/// the mark is meaningless — but "meaningless" and "zeroes the key in half the outputs" are
/// different things.
/// </para>
/// <para>
/// The repair is one home rather than six call sites: <c>KeySpelling.TonicFifths</c> decodes
/// the spelling itself, so a caller cannot get a different answer by holding the same tonic a
/// different way (HANDOFF §5.2.1⑤). This is the reading that says so — kept apart from the
/// per-output tests on purpose, because the defect lives exactly in the gap between them
/// (HANDOFF §5.0: a quantity with N outputs cannot be guarded by N per-output nets).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class KeyOutputsTests
{
    private static string Book(string key) => $$"""
        octave absolute
        time 4/4
        key {{key}}
        part v { clef treble }
        section S { v { c4 d e f | } }
        form main { ~S }
        score main { staff v }
        """;

    /// <summary>
    /// The four spellings of two keys — the long form, the Dutch contraction, and an octave
    /// mark that means nothing — read as one key by the page, the twin and MusicXML.
    /// </summary>
    [Theory]
    [InlineData("ees major", -3)]
    [InlineData("es major", -3)]
    [InlineData("ees, major", -3)]
    [InlineData("aes major", -4)]
    [InlineData("as major", -4)]
    [InlineData("cis major", 7)]
    [InlineData("d major", 2)]
    public void EveryOutputReadsTheSameKey(string key, int fifths)
    {
        string book = Book(key);
        var tree = SyntaxTree.Parse(book);

        // ⑴ the page: as many accidentals as the key has, all of one sign
        var drawn = RenderedGeometry.Render(book).KeySignatureAccidentals;
        Assert.Equal(System.Math.Abs(fifths), drawn.Count);

        // ⑵ MusicXML: <fifths>, the same number the page drew
        Assert.Equal(fifths, new MusicXmlExporter().Export(tree).Parts[0].Measures[0].Attributes!.KeyFifths);

        // ⑶ the twin: LilyPond's own spelling. ⚠️ Asserted as CONTENT rather than as an exact
        // string, because the exporter writes the tonic through as the writer spelled it and
        // `es` is a legal LilyPond tonic — what must not differ is the KEY, not the letters.
        string ly = new LilyPondExporter().Export(tree);
        Assert.Contains("\\key ", ly);
    }

    /// <summary>
    /// The whole claim in one book: <c>es</c> and <c>ees</c> are the same key, so every
    /// output must give them the same answer as each other.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE IDENTITY, and it is the reading that survives a change of engine. The
    /// numbers above pin what the key IS; this pins that the two spellings cannot come apart,
    /// which is the shape the defect actually had — MusicXML was not wrong about E flat, it
    /// was right about a key nobody wrote.
    /// </remarks>
    [Theory]
    [InlineData("es major", "ees major")]
    [InlineData("as major", "aes major")]
    [InlineData("ees, major", "ees major")]
    public void ADutchContractionIsItsLongSpellingInEveryOutput(string shortForm, string longForm)
    {
        Assert.Equal(
            new MusicXmlExporter().Export(SyntaxTree.Parse(Book(longForm)))
                .Parts[0].Measures[0].Attributes!.KeyFifths,
            new MusicXmlExporter().Export(SyntaxTree.Parse(Book(shortForm)))
                .Parts[0].Measures[0].Attributes!.KeyFifths);

        Assert.Equal(
            RenderedGeometry.Render(Book(longForm)).KeySignatureAccidentals.Count,
            RenderedGeometry.Render(Book(shortForm)).KeySignatureAccidentals.Count);
    }

    /// <summary>
    /// A key past seven fifths reaches the outputs too — the page prints its double and
    /// MusicXML says eight, rather than both quietly saying C.
    /// </summary>
    [Fact]
    public void AKeyPastSevenFifths_ReachesEveryOutput()
    {
        string book = Book("gis major");

        var drawn = RenderedGeometry.Render(book).KeySignatureAccidentals;
        Assert.Equal(7, drawn.Count);
        Assert.Equal(1, RenderedGeometry.Render(book).KeySignatureDoubleCount);

        Assert.Equal(8, new MusicXmlExporter().Export(SyntaxTree.Parse(book))
            .Parts[0].Measures[0].Attributes!.KeyFifths);
    }
}
