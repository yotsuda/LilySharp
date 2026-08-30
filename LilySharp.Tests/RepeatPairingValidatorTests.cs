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
/// LYS4017: a <c>|:</c> that no <c>:|</c> ever closes.
/// </summary>
/// <remarks>
/// The reason this is decided on the COLLECTED measures and not on the written text is the
/// layer-crossing case pinned below: a section is not a piece of music on its own, so a
/// <c>|:</c> in a section's music may be closed by a <c>:|</c> the <c>form</c> writes.
/// Judging a section in isolation would reject a spelling that is correct once the score is
/// laid out — and books in the author's own library are written that way.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RepeatPairingValidatorTests
{
    private static int Count(string src)
    {
        var validator = new RepeatPairingValidator();
        validator.Validate(SyntaxTree.Parse(src));
        return validator.Diagnostics
            .Count(d => d.Code == DiagnosticCodes.UnpairedRepeat
                        && d.Severity == DiagnosticSeverity.Error);
    }

    // --- the defect -------------------------------------------------------------

    [Fact]
    public void ARepeatStartWithNoEnd_IsAnError()
    {
        Assert.Equal(1, Count(
            "part m { clef treble section A { |: c1 } }\n"
            + "form main { A }\nscore main { staff m }"));
    }

    [Fact]
    public void AClosedRepeat_IsNot()
    {
        Assert.Equal(0, Count(
            "part m { clef treble section A { |: c1 :| } }\n"
            + "form main { A }\nscore main { staff m }"));
    }

    // --- the half that has a MEANING, not a defect --------------------------------

    /// <summary>
    /// A <c>:|</c> with nothing open repeats from the beginning of the piece. That is the
    /// ordinary reading of the sign, so it is accepted in silence.
    /// </summary>
    /// <remarks>
    /// ⚠️ The third case is the one that can FAIL. The first two assert an absence, and an
    /// absence stays true under any poison that makes the scan record less — so on their own
    /// they observe nothing. The third puts a lone <c>:|</c> and a dangling <c>|:</c> in one
    /// score and demands exactly ONE error: it goes red both if the lone <c>:|</c> starts
    /// reporting and if it starts swallowing the dangling <c>|:</c>.
    /// </remarks>
    [Fact]
    public void ARepeatEndWithNothingOpen_IsAccepted()
    {
        Assert.Equal(0, Count(
            "part m { clef treble section A { c1 :| } }\n"
            + "form main { A }\nscore main { staff m }"));
        Assert.Equal(0, Count(
            "part m { clef treble section A { c1 } section B { d1 } }\n"
            + "form main { A B :| }\nscore main { staff m }"));
        // A lone ':|' first, then a '|:' that nothing closes: the lone ':|' must neither
        // report nor be spent closing the later '|:'.
        Assert.Equal(1, Count(
            "part m { clef treble section A { c1 :| d1 |: e1 } }\n"
            + "form main { A }\nscore main { staff m }"));
    }

    // --- the reason it cannot be decided on the text ------------------------------

    /// <summary>
    /// The <c>|:</c> is in the SECTION and the <c>:|</c> is in the FORM. Nothing is wrong
    /// with this score, and no scan of either layer alone could say so.
    /// </summary>
    [Fact]
    public void ARepeatOpenedInASectionAndClosedByTheForm_IsAccepted()
    {
        Assert.Equal(0, Count(
            "part m { clef treble section A { |: c1 } section B { d1 } }\n"
            + "form main { A B :| }\nscore main { staff m }"));
    }

    /// <summary>
    /// ⚠️ The crossing works in ONE direction only, and the other direction is not this
    /// validator's to report. A form-level <c>|:</c> opens a <c>FormRepeatBlock</c>, and the
    /// parser requires that block to close at form level — so "opened by the form, closed
    /// inside a section" cannot be written at all. MEASURED 2026-08-15:
    /// <c>form main { |: A B }</c> with section B ending <c>:|</c> is rejected by the parser
    /// before any of this runs.
    /// <para>
    /// The first draft of this file asserted that spelling was ACCEPTED, and the source it
    /// used to assert it contained no <c>|:</c> at all — so it was a green test about
    /// nothing. Kept as the negative it really is.
    /// </para>
    /// <para>
    /// ⚠️ 2026-08-31: the message this asserts USED to be the parser's bare
    /// "Expected 'RepeatEndBar', found 'EndOfFile'" — and it arrived after five wrong errors,
    /// because the unclosed block ate the rest of the file. The rejection is unchanged; the
    /// error is now LYS4017 at the <c>|:</c>, naming the missing half AND the direction that
    /// does work (FormRepeatUnclosedDiagnosticTests owns that behaviour).
    /// </para>
    /// </summary>
    [Fact]
    public void ARepeatOpenedByTheForm_MustCloseInTheForm_AndTheParserSaysSo()
    {
        var diagnostics = SyntaxTree.Parse(
            "part m { clef treble section A { c1 } section B { d1 :| } }\n"
            + "form main { |: A B }\nscore main { staff m }").Diagnostics;
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.UnpairedRepeat
            && d.Message.Contains("never closed"));
    }

    // --- score-level, not part-level ----------------------------------------------

    /// <summary>
    /// A repeat barline belongs to the SCORE (MeasureCollector.SynchronizeBarlines
    /// propagates it to every voice), so a dangling one is reported ONCE however many
    /// staves the score draws — not once per staff.
    /// </summary>
    [Fact]
    public void OnTwoStaves_ItIsReportedOnce()
    {
        Assert.Equal(1, Count(
            "part m { clef treble section A { |: c1 } }\n"
            + "part b { clef bass section A { |: c1 } }\n"
            + "form main { A }\nscore main { staff m staff b }"));
    }

    /// <summary>
    /// …and written in only ONE part it is still one error, because the bar propagates to
    /// the other staff rather than being that part's private business.
    /// </summary>
    [Fact]
    public void WrittenInOnePartOfTwo_ItIsStillOneError()
    {
        Assert.Equal(1, Count(
            "part m { clef treble section A { |: c1 } }\n"
            + "part b { clef bass section A { c1 } }\n"
            + "form main { A }\nscore main { staff m staff b }"));
    }

    // --- nesting -------------------------------------------------------------------

    [Fact]
    public void NestedRepeatsThatBothClose_AreAccepted()
    {
        Assert.Equal(0, Count(
            "part m { clef treble section A { |: c1 |: d1 :| e1 :| } }\n"
            + "form main { A }\nscore main { staff m }"));
    }

    [Fact]
    public void TwoOpenRepeats_AreTwoErrors()
    {
        Assert.Equal(2, Count(
            "part m { clef treble section A { |: c1 |: d1 } }\n"
            + "form main { A }\nscore main { staff m }"));
    }

    /// <summary>
    /// Two repeats in a row — the ordinary spelling — is not nesting and not a defect.
    /// The scan reads a fused <c>:|:</c> as close-then-open, which is the order it is
    /// written in; reading it either way round alone would make this score report.
    /// </summary>
    [Fact]
    public void TwoRepeatsInARow_AreAccepted()
    {
        Assert.Equal(0, Count(
            "part m { clef treble section A { |: c1 :| |: d1 :| } }\n"
            + "form main { A }\nscore main { staff m }"));
        Assert.Equal(0, Count(
            "part m { clef treble section A { |: c1 :|: d1 :| } }\n"
            + "form main { A }\nscore main { staff m }"));
    }
}
