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

/// <summary>Zundamon-to-Metan-no-Jikoshoukai, 2026-11-11: the chorus opened on `partial 8` right after
/// the pre-chorus's full last bar, and the piece took a bar an eighth long in its middle. LYS2022 says so;
/// a pickup opening the piece, or completing the bar the section before leaves open, is not warned.</summary>
public class PickupAfterAWholeBarTests
{
    private static string Book(string a, string form) => $$"""
        time 4/4
        octave absolute
        part m { clef treble }
        part b { clef bass }
        section A { m { {{a}} } b { c1 | c1 | } }
        section B { partial 8 m { a'8 | c''1 | } b { c8 | c1 | } }
        form { {{form}} }
        score { staff m staff b }
        """;

    private static System.Collections.Generic.List<Diagnostic> Warned(string book)
        => SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.PickupAfterAWholeBar).ToList();

    [Fact]
    public void APickupAfterAWholeBar_IsWarned_OnceAtThePartial()
    {
        string book = Book("c'1 | c'1 |", "A B");
        var warned = Assert.Single(Warned(book));
        Assert.Equal(DiagnosticSeverity.Warning, warned.Severity);
        Assert.Equal("partial 8", book.Substring(warned.Span.Start, warned.Span.Length));
        Assert.Contains("opens section 'B' right after 'A' ends on a whole bar", warned.Message);
    }

    [Fact]
    public void APickupOpeningThePiece_OrCompletingTheBarLeftOpen_IsNotWarned()
    {
        Assert.Empty(Warned(Book("c'1 | c'1 |", "B A")));
        Assert.Empty(Warned(Book("c'1 | c'2.. |", "A B").Replace("b { c1 | c1 | }", "b { c1 | c2.. | }")));
    }

    [Fact]
    public void OnePredecessorEndingWhole_IsEnough()
    {
        // B opens the piece, then comes after A's whole bar: that playing stumbles.
        Assert.Single(Warned(Book("c'1 | c'1 |", "B A B")));
    }
}
