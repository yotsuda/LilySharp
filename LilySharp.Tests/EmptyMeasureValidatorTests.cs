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
/// A bare-barline gap in a MUSIC section — a leading <c>|</c>, a <c>| |</c> gap, or a
/// trailing <c>| |</c> — is a real empty placeholder measure (it holds a slot so parts
/// stay aligned, renders as an empty bar, and warns until filled). A barline that merely
/// delimits content, confirms an auto-filled bar, or carries a type (":|", "||", "|.")
/// is NOT a placeholder and must stay quiet.
/// </summary>
[Trait("Category", "Unit")]
public class EmptyMeasureValidatorTests
{
    private static int PlaceholderCount(string music)
    {
        var source = $"part m {{ section A {{ {music} }} }} form main {{ A }} score main {{ staff m }}";
        var validator = new EmptyMeasureValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics.Count(d => d.Code == DiagnosticCodes.EmptyPlaceholderMeasure
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("| c4 c g' g | a a g2")]        // leading `|`
    [InlineData("c4 c g' g | | a a g2")]        // `| |` gap after a full bar
    [InlineData("c4 c | | a a g2")]             // `| |` gap after an UNDERFULL bar (same result)
    [InlineData("c4 c g' g | a a g2 | |")]      // trailing `| |`
    public void OneBareGap_WarnsOnce(string music) => Assert.Equal(1, PlaceholderCount(music));

    [Fact]
    public void LeadingAndMiddleGaps_WarnTwice() =>
        Assert.Equal(2, PlaceholderCount("| c4 c g' g | | a a g2"));

    [Theory]
    [InlineData("c4 c g' g | a a g2")]          // one plain `|` delimiting two bars
    [InlineData("c4 c g' g | a a g2 |")]        // trailing `|` confirms the auto-filled last bar
    [InlineData("c4 c g' g | a a g2 |.")]       // typed final barline, not a gap
    [InlineData("c4 c g' g | a a g2 ||")]       // typed double barline, not a gap
    public void NoBareGap_NoWarning(string music) => Assert.Equal(0, PlaceholderCount(music));
}
