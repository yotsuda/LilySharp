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

using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A standalone section HEADER (<c>section A { key g major }</c> beside
/// <c>part m { section A { … } }</c>) is a header wherever it is written: the twin plays
/// the part's declaration and writes the directive once, whether the header stands before
/// the part or after it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE PAIR IS THE RULE, not either book alone. The two books differ by LINE ORDER and
/// nothing else, so the twins must be byte-identical; asserting only "the notes are there"
/// would pass on a twin that had quietly moved something else. Written after the part, the
/// twin used to be <c>\key g \major \key g \major</c> — the directive twice and not one
/// note — while the page engraved the four notes and <c>check</c> said nothing
/// (HANDOFF §2 F ⒯, session 305's minimal pair scratch/p305/s6 vs s8).
/// </para>
/// <para>
/// The cause was <c>LilyPondExporter.OrderedMusic</c>: its single-part-shorthand arm asked
/// <c>LooseSectionMusic(s).Any()</c>, a list that counts a DIRECTIVE as music, so a header
/// registered as a playable declaration — and that dictionary is last-declaration-wins.
/// The collector's own shorthand arm asks <c>SectionHasInlineMusic</c>, which excludes
/// directives; the exporter now asks the same question, so the two walks agree by
/// construction rather than by coincidence.
/// </para>
/// <para>
/// ⚠️ THE ONE HOLE THIS FILE PINNED IS CLOSED, and by refusal rather than by picking a
/// reader: a name whose ONLY declaration is a directives-only header is now LYS1036
/// (owner's decision 2026-08-31, HANDOFF §2 F ⒰). The row below still asserts what the
/// EXPORTER does with such a book — the twin is still written, because a hard semantic
/// error does not stop the writers — but it is no longer a blessing of the spelling; the
/// spelling's own guard is SectionPlaysNothingValidatorTests.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LilyPondStandaloneSectionHeaderTests
{
    private static string Export(string source)
        => new LilyPondExporter().Export(SyntaxTree.Parse(source));

    private const string Tail = "form main { ~A }\nscore main { staff m }\n";

    // The minimal pair: the SAME book with the header moved across the part.
    private const string HeaderBefore =
        "time 4/4\n"
        + "section A { key g major }\n"
        + "part m { clef treble\n  section A { c'4 c c c | }\n}\n";
    private const string HeaderAfter =
        "time 4/4\n"
        + "part m { clef treble\n  section A { c'4 c c c | }\n}\n"
        + "section A { key g major }\n";

    // The same pair written section-major — the header still stands outside the part, so
    // it reaches the exporter by the same arm and used to eat the cell the same way.
    private const string SectionMajorBefore =
        "time 4/4\n"
        + "section A { key g major }\n"
        + "part m { clef treble }\n"
        + "section A { m { c'4 c c c | } }\n";
    private const string SectionMajorAfter =
        "time 4/4\n"
        + "part m { clef treble }\n"
        + "section A { m { c'4 c c c | } }\n"
        + "section A { key g major }\n";

    [Theory]
    [InlineData(HeaderBefore, HeaderAfter)]
    [InlineData(SectionMajorBefore, SectionMajorAfter)]
    public void MovingTheHeaderAcrossThePart_DoesNotChangeTheTwin(string before, string after)
        => Assert.Equal(Export(before + Tail), Export(after + Tail));

    [Theory]
    [InlineData(HeaderBefore)]
    [InlineData(HeaderAfter)]
    [InlineData(SectionMajorBefore)]
    [InlineData(SectionMajorAfter)]
    public void TheSectionsMusicReachesTheTwin(string book)
        => Assert.Contains("c'4 c c c |", Export(book + Tail));

    [Theory]
    [InlineData(HeaderBefore)]
    [InlineData(HeaderAfter)]
    [InlineData(SectionMajorBefore)]
    [InlineData(SectionMajorAfter)]
    public void TheHeadersDirectiveIsWrittenOnce(string book)
        => Assert.Equal(1, Regex.Matches(Export(book + Tail), Regex.Escape("\\key g \\major")).Count);

    [Fact]
    public void TheSinglePartShorthandStillPlays()
    {
        // The arm the fix NARROWS. A top-level section holding the lone part's bare music
        // — no cell around it — is the third spelling the exporter was taught to read; a
        // predicate that turned headers away by turning away every section without a cell
        // would silently empty every book written this way (35 of the fixtures then).
        var ly = Export("time 4/4\npart m { clef treble }\nsection A { c'4 c c c | }\n" + Tail);
        Assert.Contains("c'4 c c c |", ly);
    }

    [Fact]
    public void AHeaderOnlyNameIsRefused_AndTheTwinNoLongerWritesThreeKeys()
    {
        // `A` is declared ONLY as a header — no part gives it music — and the form plays it
        // before B. That book is now REFUSED (LYS1036), which is how the disagreement was
        // settled: the page armed A's key and carried it into B's bar, while the twin wrote
        // none, and neither reader was obviously wrong (§3's boundary rule was written about
        // sections that HAVE bars).
        //
        // ⚠️ The twin is still asserted here because the writers run anyway on a semantic
        // error ("written anyway, from the part of the file that parsed"), and what they
        // write must not be the old nonsense: before ⒯ it was
        // `\key g \major \key g \major \key c \major` — the same directive twice and then a
        // THIRD key that silently won.
        const string book =
            "time 4/4\npart m { clef treble\n  section B { f'4 f f f | }\n}\n"
            + "section A { key g major }\n"
            + "form main { ~A ~B }\nscore main { staff m }\n";

        var validator = new SymbolReferenceValidator();
        validator.Validate(SyntaxTree.Parse(book));
        Assert.Single(validator.Diagnostics.Where(
            d => d.Code == DiagnosticCodes.SectionPlaysNothing));

        var ly = Export(book);
        Assert.Contains("f'4 f f f |", ly);
        Assert.DoesNotContain("\\key g \\major", ly);
    }
}
