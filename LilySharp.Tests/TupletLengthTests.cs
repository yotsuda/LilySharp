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
/// LYS2020: a tuplet whose notes sound a length no note value writes holds the wrong notes.
/// Owner's question 2026-11-01 (omr-seeds/20261101-110443-bohemian-rhapsody-bar124-p1): the
/// scan's bracket covered a quarter and an eighth, the text read <c>ees''4 tuplet 3/2 { g''8 }</c>,
/// and the bar was 13/12 — LYS2002 named the bar, nothing the bracket.
/// </summary>
[Trait("Category", "Unit")]
public class TupletLengthTests
{
    private static string Book(string bar) =>
        $"octave absolute\ntime 4/4\npart m {{ clef treble }}\nsection A {{ m {{ {bar} }} }}\nform {{ A }}\nscore {{ staff m }}\n";

    private static System.Collections.Generic.List<Diagnostic> Found(string bar)
        => SemanticValidation.Run(SyntaxTree.Parse(Book(bar))).Where(d => d.Code == DiagnosticCodes.TupletNotANoteValue).ToList();

    [Fact]
    public void ATupletOfOneEighth_IsSaid_WithWhatABracketHolds()
    {
        var found = Assert.Single(Found("d''4 ees''8 r8 f''4 ees''4 tuplet 3/2 { g''8 } |"));
        Assert.Equal(DiagnosticSeverity.Warning, found.Severity);
        Assert.StartsWith("this tuplet sounds 1/12 of a whole note, a length no note value writes: the notes inside a 3/2 bracket add up to "
            + "3 of one value played in the time of 2 (three eighths, or a quarter and an eighth)", found.Message);
        // The bar the scan should have read, and the usual shapes, say nothing.
        Assert.Empty(Found("d''4 ees''8 r8 f''4 tuplet 3/2 { ees''4 g''8 } |"));
        Assert.Empty(Found("c'4 tuplet 3/2 { d'8 e' f' } g'2 |"));
        Assert.Empty(Found("c'4 tuplet 5/4 { d'16 e' f' g' a' } g'2 |"));
        Assert.Empty(Found("tuplet 3/2 { c'4 d' e' } tuplet 3/2 { c'4 d' e' } |"));
    }

    [Fact]
    public void TwoEighthsOfThree_AndANestedShortfall_AreSaid_OnceEach()
    {
        Assert.Single(Found("c'4 tuplet 3/2 { d'8 e' } g'2 r4 |"));
        // The inner tuplet is whole (three eighths = a quarter), the outer holds it and two quarters: 3/4 × 2/3 = 1/2, fine.
        Assert.Empty(Found("c'2 tuplet 3/2 { tuplet 3/2 { d'8 e' f' } g'4 a'4 } |"));
        // …and an outer short by a note is said once, the whole inner one not.
        Assert.Single(Found("c'2 tuplet 3/2 { tuplet 3/2 { d'8 e' f' } g'4 } r4 |"));
        // A phrase body counted where it plays and where it is declared: said once.
        string book = "octave absolute\ntime 4/4\npart m { clef treble }\nphrase odd { c'4 tuplet 3/2 { d'8 e' } g'2 r4 | }\n"
            + "section A { m { odd odd } }\nform { A }\nscore { staff m }\n";
        Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.TupletNotANoteValue));
    }
}
