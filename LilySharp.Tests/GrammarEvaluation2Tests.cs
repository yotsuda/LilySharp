// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What four large scores written to try the grammar found (2026-11-12: an orchestral movement,
/// a piano piece, a band chart, an anthem — the owner's "write some large scores and judge the
/// grammar", then "fix them all, in order"). Each test is one finding, as it was met.
/// </summary>
public class GrammarEvaluation2Tests
{
    private static List<Diagnostic> Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static void Clean(string source) => Assert.Empty(Said(source).Select(d => d.Message));

    /// <summary>A group of one staff placed nothing: the flute was neither drawn nor played.</summary>
    [Theory]
    [InlineData("staffGroup { staff fl }  staffGroup { staff vn  staff vc }")]
    [InlineData("staffGroup { staff vn  staff vc }  staffGroup { staff fl }")]
    [InlineData("staffGroup { staffGroup { staff fl }  staff vn  staff vc }")]
    [InlineData("choirStaff { staff fl }  staff vn  staff vc")]
    public void AGroupOfOneStaff_StillPlacesItsStaff(string score)
    {
        string source = "time 4/4\npart fl { }\npart vn { }\npart vc { clef bass }\n"
            + "section A { fl { c''1 | } vn { e'1 | } vc { c1 | } }\nform { A }\nscore { " + score + " }\n";
        Clean(source);
        var tree = SyntaxTree.Parse(source);
        var collected = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        Assert.Equal(3, collected.EnumerateStaves().Count());
    }

    /// <summary>"R lasts its bar in any meter, a pickup too" — and the cross-part check said the
    /// pickup "lasts 1/4 but part 'b' has 1".</summary>
    [Fact]
    public void ABareBarRest_LastsThePickupTheOtherPartsPlay()
        => Clean("time 4/4\npart a { }\npart b { }\nsection M { partial 4\n a { g'4 | c''1 | }\n b { R | c'1 | } }\nform { M }\nscore { staff a  staff b }\n");

    /// <summary>A phrase that opens a section plays its pickup: what follows is not held to it, and
    /// a phrase that ends the section may end it with the rest of the pickup.</summary>
    [Theory]
    [InlineData("phrase up { a'4 | b'2 d''4 | }", "m { up d'2. | }", "~A")]
    [InlineData("phrase head { a'4 | d''2 e''4 | }\nphrase tail { a'4 | b'2 d''4 | d'2. | }", "m { head b'2 tail }", "~A")]
    [InlineData("phrase head { a'4 | d''2 e''4 | b'2 }\nphrase tail { a'4 | b'2 d''4 | d'2 | }", "m { head tail }", "~A ~A")]
    [InlineData("phrase tail { a'4 | b'2 d''4 | d'2 | }", "m { a'4 | d''2 e''4 | b'2 tail }", "~A ~A")]
    public void APhraseInASectionWithAPickup_IsJudgedAsItsNotesWrittenOutAre(string phrases, string music, string form)
        => Clean($"octave absolute\ntime 3/4\npart m {{ }}\n{phrases}\nsection A {{ partial 4  {music} }}\nform {{ {form} }}\nscore {{ staff m }}\n");

    /// <summary>A section whose music changes the meter ends in THAT meter: three beats into a
    /// 4/4 bar is not "a whole bar" of the file's 3/4.</summary>
    [Fact]
    public void ASectionsLastBar_IsOfTheMeterItsMusicWrites()
    {
        Clean("time 3/4\npart m { }\nsection B { m { g'2. | time 4/4 c''4 b' a' g' | f'2. | } }\n"
            + "section C { time 4/4  partial 4  m { g'4 | c''1 | } }\nform { B C }\nscore { staff m }\n");
        // …and one that does end on a whole bar of it is still told.
        Assert.Contains(Said("time 3/4\npart m { }\nsection B { m { g'2. | time 4/4 c''4 b' a' g' | f'1 | } }\n"
            + "section C { time 4/4  partial 4  m { g'4 | c''1 | } }\nform { B C }\nscore { staff m }\n"),
            d => d.Code == DiagnosticCodes.PickupAfterAWholeBar);
    }
}
