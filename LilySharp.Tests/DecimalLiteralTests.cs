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
using LilySharp.Core.Parser;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The lexer's decimal literal (HANDOFF ▶ ⒯⑸, docs/VALUE_SITE_AUDIT.md §5).
///
/// The point of the feature is one sentence: LilyPond's grob values are routinely
/// fractional — <c>(padding . 0.5)</c>, <c>(thickness . 0.45)</c>, <c>(padding . -0.15)</c>
/// in <c>define-grobs.scm</c> — and until this existed Lily# stored <c>= 0.45</c> as
/// <b>0</b> and dropped the <c>.45</c> without saying anything.
///
/// The point of THIS file is the other sentence: the grammar was already full of dots,
/// and every one of them has to keep meaning what it meant. The dot after a duration
/// (<c>c4.</c>, <c>R2.*3</c>, <c>partial 2.</c>, <c>tempo 4. = 116</c>) is the positive
/// control the ticket named, and it is a control precisely because a rule that ate it
/// would break the corpus in a way no decimal test would notice.
/// </summary>
[Trait("Category", "Unit")]
public class DecimalLiteralTests
{
    private static SyntaxKind[] Kinds(string text) =>
        new Lexer(text).ScanAllTokens()
            .Where(t => t.Kind != SyntaxKind.EndOfFile)
            .Select(t => t.Kind)
            .ToArray();

    private static string[] Texts(string text) =>
        new Lexer(text).ScanAllTokens()
            .Where(t => t.Kind != SyntaxKind.EndOfFile)
            .Select(t => t.Text)
            .ToArray();

    // ---------- the rule itself ----------

    [Fact]
    public void DigitsDotDigitsAreOneDecimalToken()
    {
        Assert.Equal(new[] { SyntaxKind.DecimalLiteral }, Kinds("0.45"));
        Assert.Equal(new[] { "0.45" }, Texts("0.45"));
        Assert.Equal(new[] { "3.5" }, Texts("3.5"));
        Assert.Equal(new[] { "10.125" }, Texts("10.125"));
    }

    [Fact]
    public void ADotWithNoDigitAfterItIsStillItsOwnToken()
    {
        // The dot is REQUIRED to be followed by a digit. This is the whole reason the
        // rule can be added at all: every dot the grammar already spells — the
        // augmentation dot, the grob-property separator — is followed by something
        // that is not a digit.
        Assert.Equal(new[] { SyntaxKind.IntegerLiteral, SyntaxKind.Dot }, Kinds("4."));
        Assert.Equal(
            new[] { SyntaxKind.IntegerLiteral, SyntaxKind.Dot, SyntaxKind.Dot },
            Kinds("4.."));
        // A leading dot is not a number either: `.5` is a dot and a 5, as before.
        Assert.Equal(new[] { SyntaxKind.Dot, SyntaxKind.IntegerLiteral }, Kinds(".5"));
    }

    [Fact]
    public void TheOtherGluedNumberFormsAreUntouched()
    {
        // ScanNumber's existing branches run BEFORE the decimal check, and neither of
        // them can be followed by a dot+digit: a scale-degree accidental and an ottava
        // suffix are letters.
        Assert.Equal(new[] { SyntaxKind.ScaleDegree }, Kinds("3es"));
        Assert.Equal(new[] { SyntaxKind.Identifier }, Kinds("8va"));
        Assert.Equal(new[] { SyntaxKind.TremoloSuffix }, Kinds(":8"));
    }

    [Fact]
    public void AnIdentifierEndingInADigitStillSwallowsTheDigit()
    {
        // The measured near-miss: `g2:m7.5-` (Gm7♭5) is the ONLY digit-dot-digit
        // adjacency in the whole corpus (80 books) plus fixtures (209), and it does
        // not reach ScanNumber — `m7` is taken whole as an identifier, so the `7`
        // never starts a number and the `.5` cannot glue to it.
        // (chordnames.lys:21; docs/VALUE_SITE_AUDIT.md §5.)
        Assert.Equal(
            new[]
            {
                SyntaxKind.PitchG, SyntaxKind.IntegerLiteral, SyntaxKind.Colon,
                SyntaxKind.Identifier, SyntaxKind.Dot, SyntaxKind.IntegerLiteral,
                SyntaxKind.Minus,
            },
            Kinds("g2:m7.5-"));
    }

    // ---------- positive controls: the dots that were already there ----------

    [Theory]
    // The four spellings the ticket named as the guards for this change.
    [InlineData("{ c4. d8 }")]
    [InlineData("{ R2.*3 }")]
    [InlineData("partial 2.")]
    [InlineData("tempo 4. = 116")]
    // And the ones the grammar leans on elsewhere: the grob-property separator and
    // a dotted duration inside a chord.
    [InlineData("{ override Stem.length = 8 c4 }")]
    [InlineData("{ <c e g>2. }")]
    public void ADottedSpellingKeepsItsIntegerAndItsDot(string source)
    {
        var kinds = Kinds(source);

        Assert.DoesNotContain(SyntaxKind.DecimalLiteral, kinds);
        Assert.Contains(SyntaxKind.Dot, kinds);
        Assert.Equal(source, SyntaxTree.Parse(source).ToFullString());
    }

    [Fact]
    public void ADottedDurationStillReadsAsADottedDuration()
    {
        // Not just "lexes the same" — the value the collector gets is the same. A
        // dotted quarter is 1.5 quarters; if the dot had been eaten it would be 1.
        var tree = SyntaxTree.Parse("{ c4. d8 }");
        var duration = tree.GetRoot().DescendantNodes()
            .OfType<DurationSyntax>().First();

        Assert.Equal("4", duration.NumberToken.Text);
        Assert.Equal(1, duration.DotCount);
    }

    // ---------- round-trip ----------

    [Theory]
    [InlineData("{ override Stem.length = 3.5 c4 }")]
    [InlineData("{ override Slur.thickness = -0.15 c4 }")]
    [InlineData("{ override Slur.thickness = - 0.15 c4 }")]
    [InlineData("part perc { lines 0.5 }")]
    public void ADecimalRoundTrips(string source)
    {
        // The token's TEXT covers every character it consumed, so the tree's full
        // width equals the source length — the invariant the whole editor sync rests
        // on (see CombineNegativeNumber's remark for the folded-sign case).
        var tree = SyntaxTree.Parse(source);

        Assert.Equal(source, tree.ToFullString());
        Assert.Equal(source.Length, tree.Root.FullWidth);
    }

    // ---------- the value arrives as a number ----------

    [Fact]
    public void AFractionalOverrideValueIsARealNotATruncatedInt()
    {
        // This is the defect the ticket recorded: `= 3.5` used to store 3 and drop
        // the .5 in silence, which is worse than refusing it.
        var tree = SyntaxTree.Parse("override Stem.length = 3.5 c4 d e f");
        var score = new MeasureCollector().Collect(tree);

        var value = Assert.Single(score.GrobOverrides).Value;
        Assert.Equal(new LysValue.Real(3.5), value);
        Assert.Equal(3.5, value.AsDouble);
        // AsInt does NOT truncate — int.TryParse("3.5") failed before this type
        // existed either, and a property that wants a whole number should say so.
        Assert.Null(value.AsInt);
    }

    [Theory]
    [InlineData("override Slur.thickness = -0.15 c4", -0.15)]
    [InlineData("override Slur.thickness = - 0.15 c4", -0.15)]
    [InlineData("override Slur.thickness = 0.45 c4", 0.45)]
    public void ANegativeOrLeadingZeroDecimalReachesTheCollectorAsThatNumber(
        string source, double expected)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));

        Assert.Equal(expected, Assert.Single(score.GrobOverrides).Value.AsDouble);
    }

    [Fact]
    public void AWholeOverrideValueIsStillAnInt()
    {
        // The kinds stay apart on purpose: a whole number must not start arriving as
        // a Real, or every consumer that asks AsInt loses its answer.
        var score = new MeasureCollector().Collect(
            SyntaxTree.Parse("override Stem.length = 3 c4 d e f"));

        Assert.Equal(new LysValue.Int(3), Assert.Single(score.GrobOverrides).Value);
    }

    [Fact]
    public void AFractionalPartPropertyIsAReal()
    {
        var tree = SyntaxTree.Parse("part perc { lines 0.5 }\nscore main { staff perc }");
        var prop = tree.GetRoot().ChildNodes()
            .OfType<PartDeclarationSyntax>()
            .SelectMany(p => p.Properties)
            .Single(p => p.NameToken.Text == "lines");

        Assert.Equal(new LysValue.Real(0.5), prop.Value);
        // ...and a part property that wants a whole number gets nothing rather than
        // a rounded guess.
        Assert.Null(prop.Value!.AsInt);
    }

    // ---------- what used to break in silence ----------

    [Fact]
    public void AFractionalDurationIsReportedInsteadOfHalfDropped()
    {
        // `c4.5` lexed as c + 4 + . + 5 and read as a dotted quarter followed by a
        // stray 5 that the music loop dropped without a word. Now the 4.5 is one
        // token and the file is told.
        var tree = SyntaxTree.Parse("{ c4.5 d8 }");

        var error = Assert.Single(tree.Diagnostics.Where(
            d => d.Code == DiagnosticCodes.FractionalDuration));
        Assert.Contains("4.5", error.Message);

        // The rest of the stream survives — the recovery skips the one token, it does
        // not abandon the bar.
        Assert.Equal("{ cd8 }", tree.ToFullString());

        // ⚠️ Yes, the token is DROPPED from the tree, so this input does not round-trip.
        // That is the sequence loop's skip recovery, not something this change chose:
        // a detached duration (LYS0016) has always behaved the same way. Pinned as a
        // PAIR so the two cannot drift apart — if someone makes error recovery keep its
        // tokens, both sides of this move together.
        // (The surviving space differs only because the trivia goes with the token
        // that carried it: `4.5 ` held the space, `c ` holds it in the other spelling.)
        Assert.Equal("{ c d8 }", SyntaxTree.Parse("{ c 4 d8 }").ToFullString());
    }

    // ---------- incremental lexing ----------
    //
    // Typing the '.' and then the digit of a decimal moves the END of the number
    // before it, so that number must be re-lexed rather than reused. Those rows live
    // with the rest of the incremental equivalence theory, which already owns the
    // green-deep-compare machinery: IncrementalParseTests.WithChange_MatchesFullParse
    // ("e1 |" → "e1. |" / "e1.5 |", "c4 d e f" → "c4.5 d e f").
}
