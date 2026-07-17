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
/// An empty placeholder measure is an explicit <c>| |</c> PAIR — two written barlines
/// with no music between them, anywhere in a MUSIC section (it holds a slot so parts
/// stay aligned, renders as an empty bar, and warns until filled). A SINGLE bare
/// barline never creates one: at the section's head or tail it merely anchors the
/// boundary, between full bars it confirms the auto-filled close, and a typed barline
/// (":|", "||", "|.") is a decoration. Lyrics keep their own rule (a lone leading
/// <c>|</c> there skips a bar) — lyrics have no durations, so their barlines ARE the
/// structure.
/// </summary>
[Trait("Category", "Unit")]
public class EmptyMeasureValidatorTests
{
    private static int PlaceholderCount(string music)
    {
        var source = $"part m {{ section A {{ {music} }} }} form main {{ A }} score main {{ staff m }}";
        var validator = new EmptyMeasureValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics.Count(d => d.Code == DiagnosticCodes.MeasureIncomplete
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("| | c4 c g' g | a a g2")]      // leading `| |` — the explicit empty bar
    [InlineData("c4 c g' g | | a a g2")]        // `| |` gap after a full bar
    [InlineData("c4 c | | a a g2")]             // `| |` gap after an UNDERFULL bar (same result)
    [InlineData("c4 c g' g | a a g2 | |")]      // trailing `| |`
    public void BarePair_WarnsOnce(string music) => Assert.Equal(1, PlaceholderCount(music));

    [Fact]
    public void LeadingAndMiddlePairs_WarnTwice() =>
        Assert.Equal(2, PlaceholderCount("| | c4 c g' g | | a a g2"));

    [Fact]
    public void ThreeConsecutiveBars_AreTwoEmptyMeasures() =>
        Assert.Equal(2, PlaceholderCount("c4 c g' g | | | a a g2"));

    [Theory]
    [InlineData("c4 c g' g | a a g2")]          // one plain `|` delimiting two bars
    [InlineData("| c4 c g' g | a a g2")]        // leading `|` anchors the section start
    [InlineData("c4 c g' g | a a g2 |")]        // trailing `|` confirms the auto-filled last bar
    [InlineData("| c4 c g' g | a a g2 |")]      // both edges anchored — the symmetric idiom
    [InlineData("c4 c g' g | a a g2 |.")]       // typed final barline, not a gap
    [InlineData("c4 c g' g | a a g2 ||")]       // typed double barline, not a gap
    [InlineData("|")]                           // a lone bar delimits nothing: empty section
    public void NoBarePair_NoWarning(string music) => Assert.Equal(0, PlaceholderCount(music));

    [Fact]
    public void LeadingSingleBar_CreatesNoMeasure()
    {
        // `{ | c1 | c1 | }` is exactly `{ c1 | c1 }` — two measures, edges anchored.
        var src = "part m { section A { | c1 | c1 | } } form main { A } score main { staff m }";
        var score = new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(src), "m");
        Assert.Equal(2, score.Voice.Measures.Length);
        Assert.All(score.Voice.Measures, m => Assert.False(m.IsEmptyPlaceholder));
    }
}
