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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A hairpin nothing ends — no level, hairpin or stop after it in its staff — is drawn to the
/// next bar line and said in a WARNING (owner's decision 2026-11-20, session 1336: "draw it and
/// say so"). LilyPond warns "unterminated crescendo" and draws nothing; until then Lily# drew it
/// and said nothing.
/// </summary>
[Trait("Category", "Unit")]
public class UnendedHairpinTests
{
    private static string Book(string music, string score = "score { staff m }") =>
        "octave absolute part m { clef treble } section A { m { " + music + " } } form { A } " + score;

    private static List<Diagnostic> Reports(string text)
    {
        var validator = new SpanPairingValidator();
        validator.Validate(SyntaxTree.Parse(text));
        return validator.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnpairedSpan).ToList();
    }

    [Theory]
    // Nothing after it.
    [InlineData("c'4@p d'@cresc e' f' | g'1 |", "d'@cresc", "a crescendo")]
    [InlineData("c'4@f d'@decresc e' f' | g'1 |", "d'@decresc", "a decrescendo")]
    [InlineData("c'4@f d' e' f'@dim | g'1 |", "f'@dim", "a diminuendo")]
    public void AHairpinNothingEnds_IsAWarning_AndIsDrawn(string music, string at, string what)
    {
        string text = Book(music);
        var report = Assert.Single(Reports(text));
        Assert.Equal(DiagnosticSeverity.Warning, report.Severity);
        Assert.Equal(text.IndexOf(at, System.StringComparison.Ordinal) + at.IndexOf('@'), report.Span.Start);
        Assert.StartsWith(what + " is never ended", report.Message);
        // Drawn all the same: to the next bar line.
        var score = SvgGenerator.CollectScore(SyntaxTree.Parse(text), RenderSpecParser.FindFirst(SyntaxTree.Parse(text)));
        var layout = new Core.Svg.Layout.LayoutEngine(score.Paper).Layout(score);
        Assert.Single(layout.HairpinLayouts);
    }

    [Theory]
    // A level, a stop, the next hairpin, a level in the other voice of its staff: each ends it.
    [InlineData("c'4@p d'@cresc e' f'@f | g'1 |")]
    [InlineData("c'4@p d'@cresc e' f'@!cresc | g'1 |")]
    [InlineData("c'4@p d'@cresc e' f'@decresc | g'1@p |")]
    [InlineData("voice { c''4@p d''@cresc e'' f'' } { c'2 c'2@f } | g'1 |")]
    public void AHairpinSomethingEnds_SaysNothing(string music)
        => Assert.Empty(Reports(Book(music)));

    /// <summary>One warning for one written mark, though a repeated section plays it twice.</summary>
    [Fact]
    public void ARepeatedSection_WarnsOnce()
    {
        string text = "octave absolute part m { clef treble } section A { m { c'4@p d'@cresc e' f' | } } form { A A } score { staff m }";
        Assert.Single(Reports(text));
    }
}
