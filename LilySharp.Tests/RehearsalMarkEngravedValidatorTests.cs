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
/// A rehearsal mark that is WRITTEN and not printed is reported at the mark (LYS4019).
/// </summary>
/// <remarks>
/// ⚠️ THE FAMILY EARNED THIS BY BEING SILENT. Until 2026-08-30 a <c>@mark("A")</c> inside a
/// container that owns its own walk was dropped with no box, no letter and no diagnostic,
/// and 45 of one reader's books lost 120 letters before anyone noticed. That drop is fixed —
/// <see cref="RehearsalMarkTests.AMarkInsideAContainerThatOwnsItsWalk_IsCollectedOnceAndPrinted"/>
/// holds it — and this is the reader's decision that the family should answer the way the
/// SPAN family does: if it is not drawn, say where.
/// <para>
/// The two halves are tested apart on purpose. The NEGATIVE cases are the ones that catch a
/// validator that has quietly become "warn about every mark": each of them is a shape that
/// used to be broken, so a regression there would be reported twice — once as a missing
/// letter, once as a warning about a letter that is on the page.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RehearsalMarkEngravedValidatorTests
{
    private static IReadOnlyList<Diagnostic> Warnings(string source)
    {
        var validator = new RehearsalMarkEngravedValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UnengravedRehearsalMark
                        && d.Severity == DiagnosticSeverity.Warning)
            .ToList();
    }

    /// <summary>One part, one staff, the mark written in the given music.</summary>
    private static string OneStaff(string music)
        => "part m { clef treble }\nsection A { m {\n" + music + "\n} }\n"
           + "form main { ~A }\nscore main { staff m }\n";

    /// <summary>
    /// Every place a mark can be written and IS engraved: plainly, and inside each of the
    /// four containers that own their own walk. Nothing here may warn.
    /// </summary>
    [Theory]
    [InlineData("c'1@mark(\"B\") | d'1 |")]                                   // plain
    [InlineData("c'4 d' e' f' |: g'1 | [1. a'1@mark(\"B\") ] :| [2. b'1 ]")]  // inline ending
    [InlineData("tuplet 3/2 { g'4@mark(\"B\") a' b' } c''4 d'' |")]           // tuplet
    [InlineData("repeat unfold 2 { d'2@mark(\"B\") e' }")]                    // unfolded repeat
    [InlineData("repeat percent 2 { d'2@mark(\"B\") e' }")]                   // percent repeat
    [InlineData("cue { f'1@mark(\"B\") } | g'1 |")]                           // cue region
    [InlineData("c'4 d' e' f' |")]                                            // no mark at all
    public void AnEngravedMark_IsNotReported(string music)
        => Assert.Empty(Warnings(OneStaff(music)));

    /// <summary>
    /// ⚠️ A mark in a section no form plays. The music is not on the page, so neither is the
    /// letter — and the collector cannot report this one itself, because its walk never went
    /// there. That is the whole reason this validator is handed the TREE as well as the
    /// collect.
    /// </summary>
    [Fact]
    public void AMarkInASectionTheFormNeverPlays_IsReported()
    {
        var warning = Assert.Single(Warnings(
            "part m { clef treble }\n"
            + "section A { m { c'1@mark(\"P\") | d'1 } }\n"
            + "section B { m { e'1@mark(\"Q\") | f'1 } }\n"
            + "form main { ~A }\nscore main { staff m }\n"));
        Assert.Contains("not printed by this score", warning.Message);
    }

    /// <summary>A mark in a part no score renders — the same silence, a different cause.</summary>
    [Fact]
    public void AMarkInAPartNoScoreRenders_IsReported()
        => Assert.Single(Warnings(
            "part m { clef treble }\npart other { clef treble }\n"
            + "section A { m { c'1@mark(\"P\") | d'1 } other { e'1@mark(\"Q\") | f'1 } }\n"
            + "form main { ~A }\nscore main { staff m }\n"));

    /// <summary>
    /// ⚠️ A mark on a GRACE note is PRINTED, and this test used to assert the opposite.
    /// It was written (session 293) as the marker for whoever closed the grace hole, saying
    /// that turning it green was the correct direction; session 298 turned it, and the
    /// reason is that this never was a mark defect. LilyPond consists <c>Mark_engraver</c>
    /// in the SCORE context (ly/engraver-init.ly:729,764), so a mark's grob was never the
    /// note's Voice's and a grace being a Voice of its own could not stand between them —
    /// Lily# says the same by building the mark with no <c>itemIndex</c>. What is left of
    /// the grace hole is the NOTE-ANCHORED families, which have no column on a grace note
    /// to hang off; they are still dropped, and they are now reported by
    /// <c>LYS4020</c> rather than by a sentence in this warning
    /// (<see cref="GraceBodyValidatorTests"/>). The second assertion keeps that half of the
    /// statement here, where the first reader of this file will look for it.
    /// </summary>
    [Fact]
    public void AMarkOnAGraceNote_IsPrintedAndNotReported()
    {
        string book = OneStaff("grace { d'8@mark(\"P\") } c'1 | e'1 |");
        Assert.Empty(Warnings(book));
        Assert.Contains(">P</text>", LiveRender.Svg(book));

        // The company it keeps is still on the floor — and no longer silent about it.
        string svg = LiveRender.Svg(OneStaff("grace { d'8@staccato@text(\"hi\") } c'1 | e'1 |"));
        Assert.DoesNotContain(">hi</text>", svg);
    }

    /// <summary>
    /// The position is the mark's own, because a warning that cannot be clicked is a warning
    /// about the file rather than about the mark — LYS4018's whole improvement over
    /// LilyPond's line-less warning.
    /// </summary>
    [Fact]
    public void TheWarningStandsAtTheMark()
    {
        string source =
            "part m { clef treble }\n"
            + "section A { m { c'1 | d'1 } }\n"
            + "section B { m { e'1@mark(\"Q\") | f'1 } }\n"
            + "form main { ~A }\nscore main { staff m }\n";

        var warning = Assert.Single(Warnings(source));
        Assert.Equal(source.IndexOf("@mark(\"Q\")", System.StringComparison.Ordinal),
            warning.Span.Start);
    }
}
