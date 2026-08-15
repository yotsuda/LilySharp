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
/// What a part header accepts, and what it now says instead of swallowing.
/// Two spellings used to pass through in silence or blame the wrong line:
/// a token the header could not place (LYS0025) and a property with no value (LYS0026).
/// </summary>
[Trait("Category", "Unit")]
public class PartHeaderParseTests
{
    private static string Source(string parts) =>
        $"octave absolute\n{parts}\nsection Main {{ m {{ c4 d4 e4 f4 | }} }}\n"
        + "form main { ~Main }\nscore main { staff m }\n";

    private static IReadOnlyList<Diagnostic> ParseErrors(string parts, string code) =>
        SyntaxTree.Parse(Source(parts)).Diagnostics
            .Where(d => d.Code == code && d.Severity == DiagnosticSeverity.Error)
            .ToList();

    // ---- what a header accepts, unchanged -------------------------------------------

    [Theory]
    [InlineData("part m { clef bass }")]
    [InlineData("part m { clef treble instrument \"Violin\" }")]
    [InlineData("part m { }")]
    [InlineData("part m { octave absolute }")]
    [InlineData("part m { transpose d' }")]
    [InlineData("part m { instrument bass-guitar }")]        // hyphenated value is ONE word
    [InlineData("part m { key fis major }")]                 // a key is legitimately per-part
    [InlineData("part m { override NoteHead.transparent = true }")]
    public void AcceptedHeaders_NoParseError(string parts) =>
        Assert.Empty(SyntaxTree.Parse(Source(parts)).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error));

    // ---- LYS0025: a token the header cannot place ------------------------------------

    [Theory]
    // The trap this closes: a bare clef word reads exactly like a clef, and `bass` lexes
    // as BassKeyword (never ClefKeyword), so it fell straight to the header loop's
    // `else Advance()`. `part m { bass }` MEASURED byte-identical to `part m { }`.
    [InlineData("part m { bass }")]
    [InlineData("part m { treble }")]
    [InlineData("part m { treble treble }")]
    [InlineData("part m { 42 }")]
    public void StrayTokenInHeader_IsReported(string parts) =>
        Assert.NotEmpty(ParseErrors(parts, DiagnosticCodes.PartHeaderStrayToken));

    [Fact]
    public void TheStrayTokenMessageNamesItAndPointsAtTheClefKeyword()
    {
        var d = ParseErrors("part m { bass }", DiagnosticCodes.PartHeaderStrayToken).First();
        Assert.Contains("'bass'", d.Message);
        Assert.Contains("clef bass", d.Message);
    }

    [Fact]
    public void OneStrayTokenDoesNotCascade()
    {
        // It is still consumed, so the rest of the header parses: exactly one complaint,
        // and the property after it is not dragged in.
        Assert.Single(ParseErrors("part m { bass clef treble }", DiagnosticCodes.PartHeaderStrayToken));
    }

    // ---- LYS0026: a property with no value -------------------------------------------

    [Theory]
    [InlineData("part m { clef }")]
    [InlineData("part m { instrument }")]
    [InlineData("part m { lines }")]
    public void PropertyWithNoValue_IsReported(string parts) =>
        Assert.Single(ParseErrors(parts, DiagnosticCodes.PartPropertyMissingValue));

    [Fact]
    public void TheMissingValueMessageNamesTheProperty()
    {
        var d = ParseErrors("part m { clef }", DiagnosticCodes.PartPropertyMissingValue).First();
        Assert.Contains("'clef'", d.Message);
        Assert.Contains("no value", d.Message);
    }

    [Fact]
    public void AMissingValueNoLongerEatsTheClosingBrace()
    {
        // The whole point. The value used to be taken unconditionally, so `clef` consumed
        // `}` and everything below was parsed INSIDE the part: the complaint landed on a
        // line far away ("Undefined variable or phrase: 'm'"), or, with another part after
        // it, the brace itself was reported as a clef name ("Unknown clef '}'").
        var errors = SyntaxTree.Parse(Source("part m { clef } part n { clef treble }"))
            .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        Assert.Single(errors);
        Assert.Equal(DiagnosticCodes.PartPropertyMissingValue, errors[0].Code);
        Assert.DoesNotContain("}", errors[0].Message);

        // and the part below it survived as its own declaration
        var parts = SyntaxTree.Parse(Source("part m { clef } part n { clef treble }"))
            .GetRoot().DescendantNodes().OfType<PartDeclarationSyntax>().ToList();
        Assert.Equal(2, parts.Count);
    }

    // ---- the known-property list ------------------------------------------------------

    [Fact]
    public void TheUnknownPropertyListNamesKey()
    {
        // `key` never reaches SymbolCaseValidator (a part-header key parses as a
        // KeySignature, not a PropertyAssignment), but leaving it out of the list told a
        // reader who wrote `Key c major` that there is no per-part key. There is.
        var validator = new SymbolCaseValidator();
        validator.Validate(SyntaxTree.Parse(Source("part m { wibble wobble }")));
        var d = Assert.Single(validator.Diagnostics.Where(x => x.Message.Contains("Unknown part property")));
        Assert.Contains("key", d.Message);
    }
}
