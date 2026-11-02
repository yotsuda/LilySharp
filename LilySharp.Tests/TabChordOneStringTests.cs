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
/// Two notes of a chord written on one string are drawn as two frets on one line, which no
/// hand can play: LYS1063 says so (found 2026-11-02, passing in silence).
/// </summary>
[Trait("Category", "Unit")]
public class TabChordOneStringTests
{
    private static string Book(string music) =>
        "part g { clef treble_8 instrument guitar }\nsection A { g { " + music + " } }\nform { A }\nscore { tab g }";

    private static System.Collections.Generic.List<Diagnostic> OneString(string text)
    {
        var tree = SyntaxTree.Parse(text);
        return [.. SemanticValidation.Run(tree).Where(d => d.Code == DiagnosticCodes.TabChordNotesOnOneString)];
    }

    [Theory]
    [InlineData("<e' g'>4\\1 r2. |", 1)]
    [InlineData("<e'\\1 g'\\1>4 r2. |", 1)]
    [InlineData("<e'\\1 g'\\1 b'\\1>4 r2. |", 1)]
    public void TwoNotesOnOneString_AreSaidOnce(string music, int onString)
    {
        var report = Assert.Single(OneString(Book(music)));
        Assert.Equal(DiagnosticSeverity.Warning, report.Severity);
        Assert.Contains($"string {onString}", report.Message);
    }

    [Theory]
    [InlineData("<e' g'>4 r2. |")]                // the tab chooses
    [InlineData("<e'\\1 b\\2>4 r2. |")]           // one each
    [InlineData("<e'\\1 b>4 r2. |")]              // one written
    [InlineData("e'4\\1 g'\\1 r2 |")]             // one note at a time
    [InlineData("<c\\2 e>4 r2. |")]               // LYS5003's, chosen again
    public void AnythingElse_IsNot(string music) => Assert.Empty(OneString(Book(music)));

    [Fact]
    public void TwoStringsDoubled_AreTwoReports()
        => Assert.Equal(2, OneString(Book("<e'\\1 g'\\1 b\\2 d\\2>4 r2. |")).Count);

    // Both written where the string cannot play them: each is LYS5003's, and each is chosen again.
    [Fact]
    public void TwoNotesTheStringCannotPlay_AreNotOnIt()
        => Assert.Empty(OneString(Book("<c\\2 e\\2>4 r2. |")));

    [Fact]
    public void TwoScoresOfOneChord_SayItOnce()
        => Assert.Single(OneString("part g { clef treble_8 instrument guitar }\nsection A { g { <e' g'>4\\1 r2. | } }\nform { A }\nscore a { tab g }\nscore b { tab g }"));

    [Fact]
    public void WithNoTab_NothingIsDrawn_AndNothingIsSaid()
        => Assert.Empty(OneString("part g { clef treble_8 instrument guitar }\nsection A { g { <e' g'>4\\1 r2. | } }\nform { A }\nscore { staff g }"));

    [Fact]
    public void ARepeatedChord_IsSaidOnce()
        => Assert.Single(OneString("part g { clef treble_8 instrument guitar }\nsection A { g { <e' g'>4\\1 r2. | } }\nform { A A }\nscore { tab g }"));
}
