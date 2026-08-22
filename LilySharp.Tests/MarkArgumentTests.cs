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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What an <c>@name(…)</c> argument IS, now that it is read as a value instead of
/// being flattened into a dotted string. The rules under test are stated in
/// <see cref="MarkArgument"/>; VALUE_SITE_AUDIT §9 is the measurement they came from.
/// </summary>
[Trait("Category", "Unit")]
public class MarkArgumentTests
{
    private static MusicMarkSyntax Mark(string music)
        => Assert.Single(SyntaxTree.Parse("melody { " + music + " }")
            .GetRoot().DescendantNodes().OfType<MusicMarkSyntax>());

    /// <summary>
    /// The name is the word after '@' alone — no arguments glued onto it. This is
    /// the half of the dispatch that <c>MarkName.StartsWith("bend.")</c> was doing
    /// with a string prefix.
    /// </summary>
    [Theory]
    [InlineData("c4@fig(6) |", "fig")]
    [InlineData("c4@chord(Cm7) |", "chord")]
    [InlineData("c4@finger(3) |", "finger")]
    [InlineData("c4@text(\"dolce\") |", "text")]
    [InlineData("c4@chord |", "chord")]
    public void TheName_IsTheWordAfterTheAt(string music, string name)
        => Assert.Equal(name, Mark(music).Name);

    /// <summary>
    /// A single value token becomes a value. These are the families §9.5 names as
    /// the ones to migrate first, precisely because nothing is lost here.
    /// </summary>
    [Theory]
    [InlineData("c4@finger(3) |", "3")]
    [InlineData("c4@fig(6) |", "6")]
    [InlineData("c4@bend(5) |", "5")]
    public void AnIntegerArgument_IsAnInt(string music, string text)
    {
        var argument = Assert.Single(Mark(music).Arguments);
        Assert.Equal(text, argument.Text);
        Assert.Equal(new LysValue.Int(long.Parse(text)), argument.Value);
    }

    [Theory]
    [InlineData("c4@notehead(triangle) |", "triangle")]
    [InlineData("c4@feather(right) |", "right")]
    [InlineData("c4@ottava(bassa) |", "bassa")]
    public void AnIdentifierArgument_IsASymbol(string music, string name)
    {
        var argument = Assert.Single(Mark(music).Arguments);
        Assert.Equal(name, argument.Text);
        Assert.Equal(new LysValue.Symbol(name), argument.Value);
    }

    /// <summary>
    /// A quoted string yields its CONTENT as the value and keeps its quotes in the
    /// text — the two readings a consumer might want, neither guessed from the other.
    /// </summary>
    [Fact]
    public void AStringArgument_KeepsItsQuotesInTheTextAndDropsThemFromTheValue()
    {
        var argument = Assert.Single(Mark("c4@text(\"dolce\") |").Arguments);
        Assert.Equal("\"dolce\"", argument.Text);
        Assert.Equal(new LysValue.Str("dolce"), argument.Value);
    }

    /// <summary>
    /// ★ The constraint that decided the design (§9.2). <c>@frame(032010)</c> is a
    /// fret position STRING — one character per string — so its leading zero is the
    /// sixth string, and reading only the value turns a six-string shape into a
    /// five-string one. The value is offered; the text is what survives.
    /// </summary>
    [Fact]
    public void AFretPositionArgument_KeepsItsLeadingZeroInTheText()
    {
        var argument = Assert.Single(Mark("c4@frame(032010) |").Arguments);
        Assert.Equal("032010", argument.Text);
        Assert.Equal(new LysValue.Int(32010), argument.Value);
        Assert.NotEqual(argument.Text, argument.Value!.AsText);   // the loss, stated
    }

    /// <summary>
    /// ★ The other constraint: a chord argument is a SUB-LANGUAGE, so it can
    /// arrive as several tokens (<c>F#m</c> is Identifier '#' Identifier — the
    /// '#' a BadToken the chords region tolerates; <c>G7/B</c> crosses a '/').
    /// Adjacent tokens are ONE argument, so its text is the written symbol.
    /// It denotes no single value, and says so.
    /// </summary>
    [Theory]
    [InlineData("c4@chord(F#m) |", "F#m")]
    [InlineData("c4@chord(G7/B) |", "G7/B")]
    [InlineData("c4@chord(C7-9) |", "C7-9")]
    public void ASubLanguageArgument_IsOneRunWithNoValue(string music, string text)
    {
        var argument = Assert.Single(Mark(music).Arguments);
        Assert.Equal(text, argument.Text);
        Assert.Null(argument.Value);
    }

    /// <summary>
    /// Whitespace separates arguments, and in the whole corpus only the genuinely
    /// multi-valued figured bass uses it.
    /// </summary>
    [Fact]
    public void WhitespaceSeparatesArguments()
    {
        var arguments = Mark("c4@fig(3 5) |").Arguments;
        Assert.Equal(2, arguments.Length);
        Assert.Equal(new LysValue.Int(3), arguments[0].Value);
        Assert.Equal(new LysValue.Int(5), arguments[1].Value);
    }

    /// <summary>A ',' separates too, and never appears inside an argument's text.</summary>
    [Fact]
    public void ACommaSeparatesArgumentsAndIsNotPartOfOne()
    {
        var arguments = Mark("c4@fig(6, 4) |").Arguments;
        Assert.Equal(2, arguments.Length);
        Assert.Equal(["6", "4"], arguments.Select(a => a.Text));
    }

    /// <summary>
    /// A figure's alteration suffix is written with a space, so it is its own
    /// argument — <c>@fig(6 s)</c> is two, matching the two parts MarkName's split
    /// hands the figured-bass parser today.
    /// </summary>
    [Fact]
    public void AFigureAndItsSuffix_AreTwoArguments()
    {
        var arguments = Mark("c4@fig(6 s) |").Arguments;
        Assert.Equal(2, arguments.Length);
        Assert.Equal(new LysValue.Int(6), arguments[0].Value);
        Assert.Equal(new LysValue.Symbol("s"), arguments[1].Value);
    }

    /// <summary>
    /// ⚠️ Where this reading and MarkName's DIFFER, pinned so the family migrations
    /// (§9.5 ⑵) start from a measured fact rather than an assumption: a leading '#'
    /// is written against its figure, so it is ONE argument here, while MarkName
    /// splits it into two dotted parts ("fig.#.6") and the figured-bass parser binds
    /// them back together.
    /// <para>
    /// ★ This is the measurement that decided how the figured bass moved (§9.5.3 ⑴).
    /// A reader on the RUNS would have had to re-split "#6" itself — and re-splitting is
    /// re-lexing, because "6s6" divides while "6S0" does not ('s' is a pitch token). So
    /// it reads the argument TOKENS instead, which are the parts MarkName was joining.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("c4@fig(#6) |", "#6", "fig.#.6")]
    [InlineData("c4@fig(6s) |", "6s", "fig.6.s")]
    public void AFigureWrittenWithoutASpace_IsOneRunButTwoDottedParts(
        string music, string run, string markName)
    {
        var mark = Mark(music);
        Assert.Equal(run, Assert.Single(mark.Arguments).Text);
        Assert.Equal(markName, mark.MarkName);
    }

    /// <summary>
    /// Bare <c>@chord</c> (derive the symbol from the notes) has no argument list at
    /// all — the distinction the collector makes by comparing MarkName to "chord".
    /// </summary>
    [Fact]
    public void ABareAnnotation_HasNoArgumentList()
    {
        var mark = Mark("<c e g>4@chord |");
        Assert.False(mark.HasArgumentList);
        Assert.Empty(mark.Arguments);
    }

    /// <summary>
    /// The trailing '.up' / '.down' on @text is a qualifier, NOT an argument: it
    /// stays out of the argument list and is answered on its own.
    /// </summary>
    [Theory]
    [InlineData("c4@text(\"dolce\").up |", true)]
    [InlineData("c4@text(\"dolce\").down |", false)]
    [InlineData("c4@text(\"dolce\") |", null)]
    public void APlacementQualifier_IsNotAnArgument(string music, bool? forcedAbove)
    {
        var mark = Mark(music);
        Assert.Equal(forcedAbove, mark.ForcedAbove);
        Assert.Equal("\"dolce\"", Assert.Single(mark.Arguments).Text);
    }

    /// <summary>
    /// ★ Positive control for the whole commit: the arguments are a SECOND reading
    /// of the same run, and adding them moved nothing. Every spelling above still
    /// produces the dotted MarkName its consumers parse today.
    /// </summary>
    [Theory]
    [InlineData("c4@fig(6) |", "fig.6")]
    [InlineData("c4@fig(3 5) |", "fig.3.5")]
    [InlineData("c4@finger(3) |", "finger.3")]
    [InlineData("c4@frame(032010) |", "frame.032010")]
    [InlineData("c4@chord(Cm7) |", "chord.Cm7")]
    [InlineData("c4@text(\"dolce\").up |", "text.\"dolce\".up")]
    [InlineData("c4@notehead(triangle) |", "notehead.triangle")]
    public void TheDottedMarkName_IsUnchanged(string music, string markName)
        => Assert.Equal(markName, Mark(music).MarkName);
}
