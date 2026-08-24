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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>key WORD mode</c> where WORD is not a note. It used to engrave as C major in silence.
/// </summary>
[Trait("Category", "Unit")]
public class KeyTonicValidatorTests
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

    private static Diagnostic[] Check(string key) =>
        SemanticValidation.Run(SyntaxTree.Parse(Book(key)))
            .Concat(SyntaxTree.Parse(Book(key)).Diagnostics)
            .ToArray();

    /// <summary>
    /// The spellings a writer actually reaches for. <c>ef</c> is the one that put this island
    /// on the list: session 240's own first probe wrote it and read the result as E natural.
    /// </summary>
    [Theory]
    [InlineData("ef major")] [InlineData("eb major")] [InlineData("bf major")]
    [InlineData("gs major")] [InlineData("h major")] [InlineData("bogus major")]
    [InlineData("X major")] [InlineData("ef minor")] [InlineData("eb dorian")]
    public void ATonicThatIsNotANote_IsRefused(string key) =>
        Assert.Contains(Check(key), d =>
            d.Code == DiagnosticCodes.UnknownSymbolCase && d.Message.Contains("is not a key"));

    /// <summary>
    /// The suggestion names a spelling that COMPILES. An editor that offers a word the
    /// compiler rejects is worse than one that offers nothing (session 240 shipped that
    /// twice), so the candidate is round-tripped rather than pattern-printed.
    /// </summary>
    [Theory]
    [InlineData("ef major", "ees")]
    [InlineData("eb major", "ees")]
    [InlineData("bf major", "bes")]
    [InlineData("gs major", "gis")]
    [InlineData("cs major", "cis")]
    public void TheSuggestionIsASpellingThatCompiles(string key, string suggested)
    {
        Assert.Contains(Check(key), d => d.Message.Contains($"Did you mean '{suggested}'?"));
        Assert.Empty(Check($"{suggested} major"));
    }

    /// <summary>A word this cannot repair is refused without a guess.</summary>
    /// <remarks>
    /// ⚠️ THE FIRST ASSERT IS LOAD-BEARING. "no diagnostic says 'Did you mean'" is satisfied
    /// by there being no diagnostic at all, so without the refusal beside it this reading
    /// passes with the validator DELETED — measured, that is exactly what it did (HANDOFF
    /// §5.4: a DoesNotContain / Empty claim needs "the subject exists" in the same test).
    /// </remarks>
    [Theory]
    [InlineData("bogus major")] [InlineData("h major")] [InlineData("X major")]
    public void AWordWithNoNearbySpelling_GetsNoGuess(string key)
    {
        Assert.Contains(Check(key), d => d.Message.Contains("is not a key"));
        Assert.DoesNotContain(Check(key), d => d.Message.Contains("Did you mean"));
    }

    /// <summary>
    /// Every key the language CAN spell is still accepted — including the ones past seven
    /// fifths, which used to fail the same way an unreadable tonic did.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE HALF THAT MUST NOT MOVE. The two defects looked identical from the
    /// outside (no signature, no diagnostic) and are opposite repairs: <c>gis</c> gains a
    /// signature, <c>ef</c> gains an error. A net that only asserted the refusal would be
    /// satisfied by refusing both.
    /// <para>
    /// ⚠️ AND THIS ONE IS SATISFIED BY SILENCE, so it is not the reading that says the
    /// validator runs — deleting the validator leaves it green, which is correct and is the
    /// point. The poison that reddens it is the opposite one: making the validator refuse a
    /// tonic it should accept. Its siblings above carry the other direction.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("c major")] [InlineData("ees major")] [InlineData("es major")]
    [InlineData("aes minor")] [InlineData("as minor")] [InlineData("fis dorian")]
    [InlineData("cis lydian")] [InlineData("ces locrian")] [InlineData("gis major")]
    [InlineData("deses major")] [InlineData("cisis major")] [InlineData("ees, major")]
    [InlineData("custom fis cis")]
    public void EveryKeyTheLanguageCanSpell_IsStillAccepted(string key) =>
        Assert.Empty(Check(key));

    /// <summary>
    /// A key written in a PART HEADER or mid-music is checked too — the validator walks the
    /// node, not one position.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE POSITIONS ARE COUNTED, not sampled: <c>ParseKeySignature</c> is reached from
    /// four call sites (Parser.cs top level, Parser.Declarations part header,
    /// Parser.Sections, Parser.Music mid-music). A per-position check would have to be
    /// written four times and the fourth would be forgotten — which is the shape this whole
    /// island is made of.
    /// </remarks>
    [Theory]
    [InlineData("""
        octave absolute
        time 4/4
        part v { clef treble key ef major }
        section S { v { c4 d e f | } }
        form main { ~S }
        score main { staff v }
        """)]
    [InlineData("""
        octave absolute
        time 4/4
        part v { clef treble }
        section S { v { c4 d key ef major e f | } }
        form main { ~S }
        score main { staff v }
        """)]
    public void ATonicIsCheckedWhereverAKeyCanBeWritten(string book) =>
        Assert.Contains(
            SemanticValidation.Run(SyntaxTree.Parse(book)),
            d => d.Message.Contains("is not a key"));

    /// <summary>
    /// The mode half of the same declaration still refuses its own unknown word, and the two
    /// halves report independently.
    /// </summary>
    /// <remarks>
    /// The mode has been refused since long before the tonic was; keeping both in one reading
    /// is what says the declaration now has ONE weight rather than two (HANDOFF §5.0: the
    /// noisy spelling is the one that drops out of the list).
    /// </remarks>
    [Fact]
    public void BothHalvesOfTheDeclarationAreChecked()
    {
        Assert.Contains(Check("c bogusmode"), d => d.Message.Contains("Unknown mode"));
        Assert.DoesNotContain(Check("c bogusmode"), d => d.Message.Contains("is not a key"));

        var both = Check("ef bogusmode");
        Assert.Contains(both, d => d.Message.Contains("is not a key"));
        Assert.Contains(both, d => d.Message.Contains("Unknown mode"));
    }
}
