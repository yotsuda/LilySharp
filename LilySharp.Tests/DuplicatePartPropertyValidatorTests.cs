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
/// One part header setting the same property twice. Both values used to be accepted in
/// silence, and the language did not agree with itself about which one survived:
/// MEASURED, <c>clef bass clef treble</c> engraved as treble (the LAST) while
/// <c>lines 5 lines 3</c> engraved as five lines (the FIRST — byte-identical to
/// <c>lines 5</c> alone). LYS7003 refuses the duplicate rather than freezing either
/// accident as the rule.
/// </summary>
[Trait("Category", "Unit")]
public class DuplicatePartPropertyValidatorTests
{
    private static IReadOnlyList<Diagnostic> Errors(string parts)
    {
        var source = $"octave absolute\n{parts}\nsection Main {{ m {{ c4 d4 e4 f4 | }} }}\n"
                     + "form main { ~Main }\nscore main { staff m }\n";
        var validator = new DuplicatePartPropertyValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.DuplicatePartProperty
                        && d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    [Theory]
    [InlineData("part m { clef bass clef treble }")]     // measured: last won
    [InlineData("part m { clef bass clef bass }")]       // same value, still two settings
    [InlineData("part m { lines 5 lines 3 }")]           // measured: FIRST won
    [InlineData("part m { instrument \"A\" instrument \"B\" }")]
    [InlineData("part m { transpose 2 transpose 5 }")]
    public void ADuplicateIsReported(string parts) =>
        Assert.Single(Errors(parts));

    [Theory]
    [InlineData("part m { clef bass }")]
    [InlineData("part m { clef bass instrument \"Violin\" }")]
    [InlineData("part m { }")]
    // Two parts each setting clef is not a duplicate — the scope is ONE header.
    [InlineData("part m { clef bass } part n { clef treble }")]
    public void DistinctPropertiesAreFine(string parts) =>
        Assert.Empty(Errors(parts));

    [Fact]
    public void ThreeOfTheSamePropertyReportTwice()
    {
        // Every setting after the first is refused, so the count says how many to remove.
        Assert.Equal(2, Errors("part m { clef bass clef treble clef alto }").Count);
    }

    [Fact]
    public void TheMessageNamesTheProperty()
    {
        var d = Assert.Single(Errors("part m { lines 5 lines 3 }"));
        Assert.Contains("'lines'", d.Message);
        Assert.Contains("twice", d.Message);
    }
}
