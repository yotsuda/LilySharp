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
/// A repeat sign written in a section's music is the score's, so a part that writes a plain
/// <c>|</c> where another writes <c>|:</c> follows it and is told nothing — but two parts that
/// write DIFFERENT signs on one bar line leave no reading: LYS1070, an error on each sign past
/// the first (owner's decision 2026-11-14; docs/inline-repeat-design.md §3.2).
/// </summary>
[Trait("Category", "Unit")]
public sealed class RepeatSignAgreementValidatorTests
{
    private static Diagnostic[] Disagreements(string m, string b, string row = "")
    {
        string src = $$"""
            octave absolute
            time 4/4
            part m { section S { {{m}} } }
            part b { section S { {{b}} } }
            {{(row.Length > 0 ? "chords prog { section S { " + row + " } }" : "")}}
            form { S }
            score { staff m staff b }
            """;
        return [.. SemanticValidation.Run(SyntaxTree.Parse(src)).Where(d => d.Code == DiagnosticCodes.RepeatSignsDisagree)];
    }

    [Theory]
    [InlineData("c''1 |: d''1 :| e''1 |", "c1 | d1 | e1 |")]          // one part writes the signs, the other plain lines
    [InlineData("c''1 |: d''1 :| e''1 |", "c1 |: d1 :| e1 |")]        // both write the same
    [InlineData("c''1 |: d''1 :| e''1 |", "c1 | d1 :| e1 |")]         // …or only one of them
    [InlineData("c''1 |: d''1 | e''1 :|", "c1 | d1 :| e1 |")]         // different signs on different bar lines
    [InlineData("c''1 | |: d''1 :| e''1 |", "c1 | | d1 :| e1 |")]     // an empty bar is a bar: the ':|' stands after bar 3 in both
    public void PartsThatDoNotContradictEachOtherAreToldNothing(string m, string b)
        => Assert.Empty(Disagreements(m, b));

    [Fact]
    public void AStartInOnePartAndAnEndInAnotherOnOneBarLineIsAnError()
    {
        var d = Assert.Single(Disagreements("c''1 |: d''1 | e''1 |", "c1 :| d1 | e1 |"));
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(2, d.Span.Length);                     // the ':|' the second part wrote
        Assert.Contains("':|' here and '|:' in part 'm'", d.Message);
        Assert.Contains("after its bar 1", d.Message);
    }

    [Fact]
    public void TwoCountsOnOneBarLineDisagree()
    {
        var d = Assert.Single(Disagreements("|: c''1 :|*2 d''1 |", "|: c1 :|*3 d1 |"));
        Assert.Contains("':|*3' here and ':|*2' in part 'm'", d.Message);
    }

    [Fact]
    public void AChordsRowIsAVoiceLikeAnyOther()
    {
        var d = Assert.Single(Disagreements("c''1 |: d''1 | e''1 |", "c1 | d1 | e1 |", row: "C :| G | Am |"));
        Assert.Contains("':|' here and '|:' in part 'm'", d.Message);
    }
}