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

    // ───────── endings and navigation marks (session 1251) ─────────

    /// <summary>The same rule for the other two signs of the score: one voice writes them and
    /// the rest follow, or several write the SAME — passes, length, marks.</summary>
    /// <remarks>Poison: compare the endings' numbers alone (drop the <c>To</c> arm), or the
    /// marks as sets rather than voice by voice, and the facts below go red; these rows are the
    /// other half — the spellings of one ending by a part and by a row must count the same bars.</remarks>
    [Theory]
    // endings in one part, in both, and in a row beside a part
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 | ]", "c1 | d1 | e1 |", "")]
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 | ]", "|: c1 | [1. d1 :| [2. e1 | ]", "")]
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 | f''1 | ]", "c1 | d1 | e1 | f1 |", "|: C | [1. G :| [2. Am | F | ]")]
    [InlineData("|: c''1 | [1. d''1 | e''1 :| [2. f''1 ]", "|: c1 | [1. d1 | e1 :| [2. f1 ]", "C | G | Am | F |")]
    // marks in one part, the same in both, a sign and a text on one line written by one voice
    [InlineData("c''1 | segno d''1 | e''1 fine | f''1 | ds al fine", "c1 | d1 | e1 | f1 |", "")]
    [InlineData("c''1 | segno d''1 | e''1 | ds", "c1 | segno d1 | e1 | ds", "")]
    [InlineData("c''1 | d''1 to coda | segno e''1 |", "c1 | d1 | e1 |", "C | G to coda segno | Am |")]
    // different marks on different bar lines
    [InlineData("c''1 | segno d''1 | e''1 |", "c1 | d1 | e1 fine |", "")]
    public void EndingsAndMarksThatDoNotContradictEachOtherAreToldNothing(string m, string b, string row)
        => Assert.Empty(Disagreements(m, b, row));

    [Fact]
    public void TwoEndingsOfDifferentPassesOnOneBarLineDisagree()
    {
        var d = Assert.Single(Disagreements("|: c''1 | [1. d''1 :| [2. e''1 | ]", "|: c1 | [1-2. d1 :| [3. e1 | ]")
            .Where(x => x.Message.Contains("'[1-2.'")));
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal("[1-2.".Length, d.Span.Length);
        Assert.Contains("'[1-2.' here and '[1.' in part 'm' open on the same bar line", d.Message);
        Assert.Contains("after its bar 1", d.Message);
    }

    [Fact]
    public void OneEndingOfTwoLengthsDisagrees()
    {
        var d = Assert.Single(Disagreements("|: c''1 | [1. d''1 | e''1 :| [2. f''1 | ]", "c1 | d1 | e1 | f1 |",
            row: "|: C | [1. G :| Am | [2. F | ]").Where(x => x.Message.Contains("runs")));
        Assert.Contains("The ending '[1.' runs 1 bar here and 2 bars in part 'm'", d.Message);
    }

    /// <summary>The other direction of each: as many numbers but other ones, and the longer
    /// ending written second.</summary>
    [Fact]
    public void TheNumbersAndTheLengthAreComparedBothWays()
    {
        var swapped = Disagreements("|: c''1 | [1. d''1 :| [2. e''1 | ]", "|: c1 | [2. d1 :| [1. e1 | ]");
        Assert.Contains(swapped, d => d.Message.Contains("'[2.' here and '[1.' in part 'm'"));
        Assert.Contains(swapped, d => d.Message.Contains("'[1.' here and '[2.' in part 'm'"));

        var longer = Disagreements("|: c''1 | [1. d''1 :| e''1 | [2. f''1 | ]", "|: c1 | [1. d1 | e1 :| [2. f1 | ]");
        Assert.Contains(longer, d => d.Message.Contains("The ending '[1.' runs 2 bars here and 1 bar in part 'm'"));
    }

    [Theory]
    [InlineData("c''1 | segno d''1 | e''1 | ds al coda", "c1 | segno d1 | e1 | ds al fine", "'ds al fine' here and 'ds al coda' in part 'm'")]
    [InlineData("c''1 | segno d''1 | e''1 |", "c1 | coda d1 | e1 |", "'coda' here and 'segno' in part 'm'")]
    // one voice writes two marks on the line, the other one of them
    [InlineData("c''1 | d''1 to coda | segno e''1 |", "c1 | d1 | segno e1 |", "'segno' here and 'to coda' 'segno' in part 'm'")]
    public void DifferentMarksOnOneBarLineDisagree(string m, string b, string said)
    {
        var d = Assert.Single(Disagreements(m, b));
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(said, d.Message);
        Assert.Contains("write the same marks", d.Message);
    }
}