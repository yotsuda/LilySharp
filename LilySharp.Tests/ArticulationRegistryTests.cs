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
/// Articulation/ornament names are written as <c>@name</c> and resolved from text
/// by <see cref="ArticulationRegistry"/> — they are no longer reserved lexer
/// keywords. Full names and abbreviations resolve to the same type, and the former
/// short keywords (<c>tr</c>, <c>acc</c>, <c>ten</c>, <c>dim</c>) are free to be
/// ordinary identifiers.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ArticulationRegistryTests
{
    private static ArticulationType ParseArticulationType(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        return tree.GetRoot().DescendantNodes<ArticulationSyntax>().First().Type;
    }

    [Theory]
    [InlineData("staccato", ArticulationType.Staccato)]
    [InlineData("accent", ArticulationType.Accent)]
    [InlineData("tenuto", ArticulationType.Tenuto)]
    [InlineData("trill", ArticulationType.Trill)]
    [InlineData("mordent", ArticulationType.Mordent)]
    public void Registry_ResolvesNames(string name, ArticulationType expected)
    {
        Assert.Equal(expected, ArticulationRegistry.Resolve(name));
    }

    // The abbreviations stac/acc/ten/marc/ferm/tr/ho/po and the synonyms
    // harmonic/bendafter were removed pre-0.3.0 (cf. @glissando); they must no
    // longer resolve to an articulation.
    [Theory]
    [InlineData("bogus")]
    [InlineData("stac")]
    [InlineData("acc")]
    [InlineData("ten")]
    [InlineData("marc")]
    [InlineData("ferm")]
    [InlineData("tr")]
    [InlineData("ho")]
    [InlineData("po")]
    [InlineData("harmonic")]
    [InlineData("bendafter")]
    public void Registry_UnknownName_ResolvesToNone(string name)
    {
        Assert.Equal(ArticulationType.None, ArticulationRegistry.Resolve(name));
    }

    [Theory]
    [InlineData("{ c4@staccato }", ArticulationType.Staccato)]
    [InlineData("{ c4@trill }", ArticulationType.Trill)]
    public void ParsedArticulation_ResolvesType(string source, ArticulationType expected)
    {
        Assert.Equal(expected, ParseArticulationType(source));
    }

    [Fact]
    public void FormerKeywords_AreUsableAsIdentifiers()
    {
        // 'tr', 'acc', 'ten', 'dim' were reserved words that collided with names;
        // they are now ordinary identifiers and can name phrases.
        var tree = SyntaxTree.Parse("phrase tr { c4 } phrase acc { d4 } phrase dim { e4 }");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }

    [Fact]
    public void RepeatTremolo_NowParses()
    {
        // 'tremolo' used to lex as a keyword that the repeat parser rejected; as a
        // plain identifier 'repeat tremolo N { … }' now parses.
        var tree = SyntaxTree.Parse("repeat tremolo 4 { c16 }");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
    }
}
