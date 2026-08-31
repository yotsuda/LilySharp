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
/// A <c>form</c> that plays a section declared ONLY as a directives-only header is refused
/// (LYS1036, user decision 2026-08-31): the name has no music in any part, so the play
/// engraves nothing.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE SILENT ROWS CARRY THE RULE, and one of them is the whole reason this is asked of
/// the NAME rather than of a part. <c>part fl { section A { … } }</c> beside a score that
/// draws only <c>part m</c> is a CORRECT book — m is spacer-filled across A — and a
/// predicate written as "does this part declare it" would refuse it. Measured before the
/// rule was written (scratch/p306/u3: two bars, clean).
/// </para>
/// <para>
/// ⚠️ THE SPLIT DECLARATION MUST STAY LEGAL. <c>section A { key g major }</c> beside
/// <c>part m { section A { … } }</c> is the spelling GRAMMAR.md documents for a standalone
/// header, and its reference example writes the header AFTER the part. It is silent here
/// because the OTHER declaration of the name carries the music — which is exactly the
/// question this rule asks, and exactly the question LilyPondExporter failed to ask
/// (HANDOFF §2 F ⒯).
/// </para>
/// <para>
/// The defect this closes was a disagreement, not a silence: the page ARMED the header's
/// key and carried it into the next section's bar (a header-only section engraves no bar,
/// so the boundary that restores the score key never fired), while the LilyPond twin wrote
/// no key at all. Refusing the spelling settles it without having to pick which reader was
/// right (HANDOFF §2 F ⒰).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class SectionPlaysNothingValidatorTests
{
    private static IReadOnlyList<Diagnostic> Reports(string source)
    {
        var validator = new SymbolReferenceValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.SectionPlaysNothing
                        && d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    private static IReadOnlyList<Diagnostic> Undefined(string source)
    {
        var validator = new SymbolReferenceValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics
            .Where(d => d.Code == DiagnosticCodes.UndefinedSection)
            .ToList();
    }

    [Theory]
    // the shape the decision names: A is declared, but only as a header
    [InlineData("time 4/4\npart m { clef treble\n  section B { f'4 f f f | }\n}\n"
        + "section A { key g major }\nform main { ~A ~B }\nscore main { staff m }\n")]
    // the label-printing spelling of the same reference
    [InlineData("time 4/4\npart m { clef treble\n  section B { f'4 f f f | }\n}\n"
        + "section A { key g major }\nform main { A B }\nscore main { staff m }\n")]
    // a header carrying the other three directives is the same mistake
    [InlineData("time 4/4\npart m { clef treble\n  section B { f'4 f f f | }\n}\n"
        + "section A { tempo 120 }\nform main { ~A ~B }\nscore main { staff m }\n")]
    public void AFormPlayingAHeaderOnlyName_IsRefused(string book)
        => Assert.Single(Reports(book));

    [Theory]
    // THE SPLIT DECLARATION - the header stands beside a part's cell for the same name.
    // GRAMMAR.md's own reference example writes the header AFTER the part, so both orders
    // are pinned: the rule must not care which side of the part the header is written on.
    [InlineData("time 4/4\npart m { clef treble\n  section A { f'4 f f f | }\n}\n"
        + "section A { key g major }\nform main { ~A }\nscore main { staff m }\n")]
    [InlineData("time 4/4\nsection A { key g major }\n"
        + "part m { clef treble\n  section A { f'4 f f f | }\n}\n"
        + "form main { ~A }\nscore main { staff m }\n")]
    // section-major: the cell is the music, and it is a sibling of the header
    [InlineData("time 4/4\npart m { clef treble }\nsection A { m { f'4 f f f | } }\n"
        + "section A { key g major }\nform main { ~A }\nscore main { staff m }\n")]
    // ⚠️ THE ROW THE PREDICATE IS SHAPED BY: A's music belongs to a part this score does not
    // draw. Correct - m is spacer-filled across A - and "does THIS part declare it" refuses it.
    [InlineData("time 4/4\npart m  { clef treble  section B { f'4 f f f | } }\n"
        + "part fl { clef treble  section A { g'4 g g g | } section B { g'4 g g g | } }\n"
        + "form main { ~A ~B }\nscore main { staff m }\n")]
    // an EMPTY section is not a header - there is no directive to be only - so it is left
    // alone: this rule raises an error, and silence on a shape nobody listed is the safe way
    // to be wrong.
    [InlineData("time 4/4\npart m { clef treble\n  section B { f'4 f f f | }\n}\n"
        + "section A { }\nform main { ~A ~B }\nscore main { staff m }\n")]
    // the single-part shorthand: bare music in a top-level section IS music
    [InlineData("time 4/4\npart m { clef treble }\nsection A { f'4 f f f | }\n"
        + "form main { ~A }\nscore main { staff m }\n")]
    public void ANameSomePartGivesMusic_IsLeftAlone(string book)
        => Assert.Empty(Reports(book));

    [Fact]
    public void AnUndeclaredNameIsStillOnlyUndefined_NotBoth()
    {
        // LYS1005's sibling must not double up on LYS1005's own case: `~Z` with no
        // `section Z` anywhere is undefined, and saying "it is also declared only as a
        // header" about a name that is not declared at all would be false as well as noisy.
        const string book = "time 4/4\npart m { clef treble\n  section B { f'4 f f f | }\n}\n"
            + "form main { ~Z ~B }\nscore main { staff m }\n";
        Assert.Single(Undefined(book));
        Assert.Empty(Reports(book));
    }

    [Fact]
    public void EveryPlayOfTheNameIsReported()
    {
        // Reported per PLAY, like LYS1005: the author fixes it where it is written, and a
        // name played twice is written wrong twice.
        const string book = "time 4/4\npart m { clef treble\n  section B { f'4 f f f | }\n}\n"
            + "section A { key g major }\nform main { ~A ~B ~A }\nscore main { staff m }\n";
        Assert.Equal(2, Reports(book).Count);
    }
}
