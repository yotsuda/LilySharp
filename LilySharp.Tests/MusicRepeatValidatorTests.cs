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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The rules of a repeat, an ending and a navigation mark written in a section's music
/// (<see cref="MusicRepeatValidator"/>, docs/inline-repeat-design.md §3.3, §3.5, §3.6): the
/// form's rules, asked of the score's sign column. MEASURED before (2026-11-15, Lab
/// sessions/p1248/probe/d-*): of the shapes here only an unclosed <c>|:</c> was reported.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MusicRepeatValidatorTests
{
    private static readonly string[] Ours =
    [
        DiagnosticCodes.SignInsideABar, DiagnosticCodes.NestedRepeat, DiagnosticCodes.EmptyRepeatBody,
        DiagnosticCodes.RepeatCountWithEndings, DiagnosticCodes.EndingPassNotNamedOnce,
        DiagnosticCodes.VoltaEndingWithoutRepeat,
    ];

    private static string OneSection(string mel) => $$"""
        time 4/4
        part mel { section A { {{mel}} } }
        form { A }
        score { staff mel }
        """;

    /// <summary>"LYS1071@col" for each of this validator's codes, in source order.</summary>
    private static string Said(string src)
    {
        var tree = SyntaxTree.Parse(src);
        return string.Join(" ", SemanticValidation.Run(tree).Where(d => Ours.Contains(d.Code))
            .OrderBy(d => d.Span.Start).Select(d => d.Code + "@" + src[d.Span.Start..(d.Span.Start + d.Span.Length)]));
    }

    [Theory]
    // An ending inside a bar; written with a line before it, it is one cut bar and says nothing.
    [InlineData("|: c'4 d e f | g a [1. b c :| [2. b g ] | c1 |", "LYS1071@[1.")]
    [InlineData("|: c'4 d e f | g a | [1. b c :| [2. b g ] | c1 |", "")]
    // A voice span whose voices end on their own bar line is that bar line (session 1364: the
    // ending after it was refused); one whose voices do not, is not.
    [InlineData("|: voice { e''4 f'' g'' a'' | } { c''2 c'' | } [1. g''1 :| [2. c''1 | ] c1 |", "")]
    [InlineData("|: c'1 | voice { e''4 f'' } { c''2 } [1. g''2 :| [2. c''2 | ] c1 |", "LYS1071@[1.")]
    // …and once only: the body's one bar is there, it has no line of its own yet — so the
    // repeat is not also told it has no body (LYS1041 stood beside this until 2026-11-15).
    [InlineData("|: c'1 [1. d1 :| [2. e1 ] | c1 |", "LYS1071@[1.")]
    [InlineData("|: [1. d'1 :| [2. e1 ] | c1 |", "LYS1041@|:")]
    // A mark with music on both sides of it in its bar; at either edge it stands on the line.
    [InlineData("c'1 | d2 segno e2 | f1 | g2 ds e2 | c1 |", "LYS1071@segno LYS1071@ds")]
    [InlineData("c'1 | segno d1 | f1 | g1 ds | c1 |", "")]
    [InlineData("c'1 segno | d1 | f1 | ds g1 | c1 |", "")]
    public void ASignStandsOnABarLine(string mel, string said) => Assert.Equal(said, Said(OneSection(mel)));

    [Theory]
    [InlineData("|: c'1 |: d1 :| e1 :| f1 |", "LYS1072@|:")]
    [InlineData("|: c'1 :|: d1 :| e1 |", "")]
    [InlineData("|: c'1 :| d1 |: e1 :| f1 |", "")]
    public void RepeatsDoNotNest(string mel, string said) => Assert.Equal(said, Said(OneSection(mel)));

    /// <summary>The form's `|:` and the music's are one column: a repeat the form opens is
    /// still open where the section's music opens another.</summary>
    [Fact]
    public void TheFormsRepeatIsOpenToo()
    {
        const string book = """
            time 4/4
            part mel { section A { c'1 |: d1 :| e1 | } section B { c'1 | } }
            FORM
            score { staff mel }
            """;
        Assert.Equal("LYS1072@|:", Said(book.Replace("FORM", "form { |: A :| B }")));
        Assert.Equal("", Said(book.Replace("FORM", "form { A B }")));
    }

    /// <summary>…and in a later section: the sign is found where the column puts its section.</summary>
    [Fact]
    public void TheSignIsFoundInItsOwnSection()
        => Assert.Equal("LYS1072@|:", Said("""
            time 4/4
            part mel { section A { c'1 | d1 | } section B { |: c'1 | e1 |: d1 :| e1 :| } }
            form { A B }
            score { staff mel }
            """));

    [Theory]
    [InlineData("|: c'1 | [1. d1 :| [3. e1 ] | f1 |", "LYS1043@[3.")]
    [InlineData("|: c'1 | [1-2. d1 :| [2. e1 ] | f1 |", "LYS1043@[2.")]
    [InlineData("|: c'1 | [1. d1 :|*3 [2. e1 ] | f1 |", "LYS1042@:|*3")]
    [InlineData("c'1 |: [1. d1 :| [2. e1 ] | f1 |", "LYS1041@|:")]
    [InlineData("c'1 | [1. d1 ] | e1 | f1 |", "LYS6008@[1.")]
    [InlineData("|: c'1 | [1-2. d1 :| [3. e1 ] | f1 |", "")]
    [InlineData("|: c'1 | [1. d1 :| [2. e1 :| [3. g1 ] | f1 |", "")]
    public void ARepeatsEndingsNameEveryPassOnce(string mel, string said) => Assert.Equal(said, Said(OneSection(mel)));

    /// <summary>The endings may be another section's: the column is the form's.</summary>
    [Fact]
    public void TheRulesAreAskedOfTheFormsColumn()
    {
        const string book = """
            time 4/4
            part mel { section A { |: c'1 | [1. d1 :| } section B { [3. e'1 ] | f1 | } }
            form { A B }
            score { staff mel }
            """;
        Assert.Equal("LYS1043@[3.", Said(book));
        Assert.Equal("", Said(book.Replace("[3.", "[2.")));
    }

    /// <summary>A book whose repeats are the form's is not this validator's.</summary>
    [Fact]
    public void ABookWithNoSignInItsMusicIsNotAsked()
        => Assert.Empty(new[] { new MusicRepeatValidator() }.SelectMany(v =>
        {
            v.Validate(SyntaxTree.Parse("""
                time 4/4
                part mel { section A { c'1 | } section B { d'1 | } section C { e'1 | } }
                form { |: A [1. B] :| [3. C] }
                score { staff mel }
                """));
            return v.Diagnostics;
        }));
}
