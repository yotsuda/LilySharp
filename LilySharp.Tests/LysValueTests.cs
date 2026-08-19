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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A value written in the source is carried as a TYPE, not as the token's text
/// (docs/VALUE_SITE_AUDIT.md §2). These pin the decisions that change is allowed to
/// make — what the grammar can and cannot say, and which reading of a multi-token
/// value is the one reading.
/// </summary>
[Trait("Category", "Unit")]
public class LysValueTests
{
    // The production path: RenderSpecParser and the collector both reach a part
    // property through PartDeclarationSyntax.Properties, not by walking descendants.
    private static PropertyAssignmentSyntax PartProperty(SyntaxTree tree, string name)
        => tree.GetRoot().ChildNodes()
            .OfType<PartDeclarationSyntax>()
            .SelectMany(p => p.Properties)
            .Single(p => p.NameToken.Text == name);

    [Fact]
    public void AQuotedValueIsAStringAndAStringIsNotANumber()
    {
        // The string pipeline this replaced stripped the quotes before storing, so
        // `= "10"` used to answer 10 to GetDouble. That was the untyped plumbing
        // leaking, not a rule: a quoted value is text. Pinned so it is not "fixed"
        // back by someone reading GetDouble's null as a bug.
        var v = LysValue.FromToken(SyntaxKind.StringLiteral, "\"10\"");

        Assert.Equal(new LysValue.Str("10"), v);
        Assert.Null(v.AsDouble);
        Assert.Null(v.AsInt);
        Assert.Equal("10", v.AsText);
    }

    [Fact]
    public void AFoldedNegativeNumberDropsTheWhitespaceItKeepsForRoundTripping()
    {
        // CombineNegativeNumber keeps the interior space of "- 5" in the token TEXT so
        // the tree round-trips (root.FullWidth == text.Length). Reading it as a number
        // is the token's business, so the strip lives here rather than in a consumer.
        var v = LysValue.FromToken(SyntaxKind.IntegerLiteral, "- 5");

        Assert.Equal(new LysValue.Int(-5), v);
        Assert.Equal(-5.0, v.AsDouble);
    }

    [Fact]
    public void ABareWordIsASymbolAndReadsAsABoolOnlyForTheWordsThatMeanOne()
    {
        Assert.Equal(new LysValue.Symbol("up"), LysValue.FromToken(SyntaxKind.Identifier, "up"));
        Assert.Null(LysValue.FromToken(SyntaxKind.Identifier, "up").AsBool);
        Assert.True(LysValue.FromToken(SyntaxKind.Identifier, "true").AsBool);
        Assert.True(LysValue.FromToken(SyntaxKind.Identifier, "YES").AsBool);
        Assert.False(LysValue.FromToken(SyntaxKind.Identifier, "no").AsBool);
        Assert.True(LysValue.FromToken(SyntaxKind.IntegerLiteral, "1").AsBool);
        Assert.False(LysValue.FromToken(SyntaxKind.IntegerLiteral, "0").AsBool);
    }

    [Fact]
    public void ARealIsWritableInTheGrammar()
    {
        // This test used to be ARealIsNotWritableInTheGrammar, and it was written to
        // FAIL the day the lexer grew a decimal literal — "if this starts failing,
        // that is the news". It did, and this is the news: `= 3.5` now reaches the
        // collector as one value instead of storing 3 and dropping the .5.
        //
        // Kept here (rather than only in DecimalLiteralTests) because the claim it
        // pins is about this type: which cases of LysValue a .lys source can write.
        // Bool is now the only one it cannot.
        var tree = SyntaxTree.Parse("override Stem.length = 3.5 c4 d e f");
        var score = new MeasureCollector().Collect(tree);

        Assert.Equal(new LysValue.Real(3.5), Assert.Single(score.GrobOverrides).Value);
    }

    [Fact]
    public void APartPropertysValueIsTheWholeRunOfTokens()
    {
        // A hyphenated bare value is word+minus+word in the green tree. Reading only
        // the FIRST token answered "bass" while the live reader answered "bass-guitar"
        // — the same node with two values (docs/VALUE_SITE_AUDIT.md §7 ①).
        var tree = SyntaxTree.Parse("part gtr { instrument bass-guitar }\nscore main { staff gtr }");
        Assert.Empty(tree.Diagnostics);
        var prop = PartProperty(tree, "instrument");

        Assert.Equal("bass-guitar", prop.ValueText);
        Assert.Equal(new LysValue.Symbol("bass-guitar"), prop.Value);
    }

    [Fact]
    public void ANumericPartPropertyIsReadAsANumberNotReparsedFromText()
    {
        var tree = SyntaxTree.Parse("part perc { octave 3 }\nscore main { staff perc }");
        Assert.Empty(tree.Diagnostics);
        var prop = PartProperty(tree, "octave");

        Assert.Equal(new LysValue.Int(3), prop.Value);
        Assert.Equal(3, prop.Value!.AsInt);
    }
}
