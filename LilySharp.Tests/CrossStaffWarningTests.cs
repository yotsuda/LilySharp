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
/// An <c>@cross</c> with no other staff to move to is reported (LYS4031; owner's decision
/// 2026-10-23, session 966): on a single staff the page draws the note where it stands
/// (<c>CrossStaffEngraver.Calculate</c> answers nothing for one staff), and until then nothing
/// said so — the owner's own lead sheet wrote <c>b4@cross</c> for an x head.
/// </summary>
[Trait("Category", "Unit")]
public class CrossStaffWarningTests
{
    private static System.Collections.Generic.List<Diagnostic> Warnings(string book)
        => SemanticValidation.Run(SyntaxTree.Parse(book))
            .Where(d => d.Code == DiagnosticCodes.CrossStaffWithoutPartner).ToList();

    [Fact]
    public void OnASingleStaff_TheCrossIsReported_AndNamesTheXHeadSpellings()
    {
        var found = Warnings("""
            octave absolute
            part m { clef treble }
            section A { m { b'4@cross b'8 b' b'4 d''8 f'' | } }
            form { A }
            score { staff m }
            """);
        var only = Assert.Single(found);
        Assert.Equal(DiagnosticSeverity.Warning, only.Severity);
        Assert.Contains("'@dead'", only.Message);
        Assert.Contains("'@notehead(cross)'", only.Message);
        Assert.Contains("'m'", only.Message);
    }

    [Theory]
    // The grand staff: the note moves to the other staff.
    [InlineData("score { grandStaff { staff rh staff lh } }")]
    // Two staves side by side: the engraver moves it to the next staff all the same.
    [InlineData("score { staff rh  staff lh }")]
    // One score of two draws the part beside another staff.
    [InlineData("score { staff rh }\nscore both { grandStaff { staff rh staff lh } }")]
    public void BesideAnotherStaff_NothingIsReported(string scores)
    {
        var found = Warnings($$"""
            octave absolute
            part rh { clef treble }
            part lh { clef bass }
            section A { rh { e''2@cross a,,2@cross | } lh { c,1 | } }
            form { A }
            {{scores}}
            """);
        Assert.Empty(found);
    }

    [Fact]
    public void APartNoScoreDraws_AndAPhrase_AreNotJudged()
    {
        Assert.Empty(Warnings("""
            octave absolute
            part m { clef treble }
            part x { clef treble }
            phrase p { c''4@cross d'' e'' f'' | }
            section A { m { p } x { c'4@cross d' e' f' | } }
            form { A }
            score { staff m }
            """));
    }

    [Fact]
    public void AChordRowUnderTheStaff_IsNotAStaff()
    {
        // The owner's lead sheet, in today's spelling: the chord row under the staff is a
        // voice binding of its own but no staff a note can move to.
        var found = Warnings("""
            octave absolute
            part lead { clef treble }
            section A {
              chords prog { C | }
              lead { b'4@cross b'8 b' b'4 d''8 f'' | }
            }
            form { A }
            score {
              staff lead
              chords prog
            }
            """);
        Assert.Single(found);
    }
}
