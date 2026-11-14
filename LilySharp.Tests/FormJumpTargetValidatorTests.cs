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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LYS4025: a form's jump text whose landmark is missing is reported at the jump, with the
/// fallback <see cref="FormRoute"/> takes spelled out (session 790). The forms are the ones
/// <see cref="FormJumpMidiTests"/> plays, so a fault here is a fallback the MIDI took there.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FormJumpTargetValidatorTests
{
    private const string Head =
        "part m { clef treble }\n" +
        "section I { m { a4 a a a | } }\n" +
        "section A { m { c'4 c c c | } }\n" +
        "section B { m { d'4 d d d | } }\n" +
        "section C { m { e'4 e e e | } }\n";

    private static string Source(string form) => Head + "form { " + form + " }\nscore { staff m }\n";

    private static IReadOnlyList<Diagnostic> Faults(string form)
    {
        var v = new FormJumpTargetValidator();
        v.Validate(SyntaxTree.Parse(Source(form)));
        return v.Diagnostics.Where(d => d.Code == DiagnosticCodes.JumpTargetMissing).ToList();
    }

    [Theory]
    [InlineData("A B")]
    [InlineData("A fine B dc al fine")]
    [InlineData("I segno A to coda B ds al coda coda C")]
    [InlineData("A B dc C")]
    [InlineData("segno A to coda ds al coda coda B segno C fine ds al fine")]
    [InlineData("segno |: A [1. B] :| [2. C] fine ds al fine")]
    public void AFormWhoseJumpsFindTheirLandmarks_IsClean(string form)
        => Assert.Empty(Faults(form));

    /// <summary>The two-route form <see cref="FormJumpMidiTests"/> plays writes neither the
    /// <c>to coda</c> nor the <c>fine</c> its jumps ask for: both routes are followed (the MIDI
    /// test holds) and both fallbacks are reported, one per jump.</summary>
    [Fact]
    public void ATwoRouteForm_ReportsEachRoutesMissingLandmark()
    {
        var faults = Faults("segno A ds al coda coda B segno C ds al fine");
        Assert.Equal(2, faults.Count);
        Assert.Contains(faults, f => f.Message.StartsWith("'ds al coda' finds no 'to coda'"));
        Assert.Contains(faults, f => f.Message.StartsWith("'ds al fine' finds no 'fine'"));
    }

    [Theory]
    [InlineData("A B ds al fine", "segno")]
    [InlineData("A B ds", "segno")]
    [InlineData("A ds B segno", "segno")]           // the segno comes AFTER the jump: not before it
    [InlineData("A B dc al fine", "fine")]
    [InlineData("A B dc al coda coda C", "to coda")]
    [InlineData("A to coda B dc al coda C", "coda")]
    public void AJumpWhoseLandmarkIsMissing_WarnsOnceAndNamesIt(string form, string landmark)
    {
        var fault = Assert.Single(Faults(form));
        Assert.Equal(DiagnosticSeverity.Warning, fault.Severity);
        Assert.Contains($"'{landmark}'", fault.Message);
    }

    /// <summary>A <c>ds</c> with no segno reports the segno alone — the fine or coda its
    /// <c>al</c> half asks for is moot when the jump is not taken.</summary>
    [Fact]
    public void ADalSegnoWithNoSegno_ReportsOnlyTheSegno()
    {
        var fault = Assert.Single(Faults("A B ds al coda"));
        Assert.Contains("'segno'", fault.Message);
        Assert.DoesNotContain("'coda'", fault.Message);
    }

    /// <summary>An <c>al coda</c> missing BOTH its landmarks reports both.</summary>
    [Fact]
    public void AnAlCodaMissingBothLandmarks_ReportsBoth()
    {
        var faults = Faults("A B dc al coda C");
        Assert.Equal(2, faults.Count);
        Assert.Contains(faults, f => f.Message.Contains("'to coda'"));
        Assert.Contains(faults, f => f.Message.Contains("'coda' after"));
    }

    [Fact]
    public void TheWarning_StandsAtTheJumpTextAndSpellsItAsWritten()
    {
        string source = Source("A B ds al fine");
        var v = new FormJumpTargetValidator();
        v.Validate(SyntaxTree.Parse(source));
        var fault = Assert.Single(v.Diagnostics);
        Assert.Equal(source.IndexOf("ds al fine"), fault.Span.Start);
        Assert.StartsWith("'ds al fine' has no 'segno' before it", fault.Message);
    }

    /// <summary>The second route of a two-route form is a first pass again, so its segno
    /// is found and its <c>ds</c> is clean; a <c>ds</c> on a resumed stretch with no segno
    /// anywhere before it is not.</summary>
    [Fact]
    public void OnAResumedStretch_TheSegnoIsStillTheLastOneTheFirstPassMet()
    {
        Assert.Empty(Faults("segno A to coda B dc al coda coda C ds"));
        Assert.Single(Faults("A to coda B dc al coda coda C ds"));
    }

    /// <summary>The whole validation set reports it too (the CLI's <c>check</c> and the LSP's
    /// live diagnostics run that set), and a file with no form reports nothing.</summary>
    [Fact]
    public void TheValidationSet_CarriesIt()
    {
        var all = SemanticValidation.Run(SyntaxTree.Parse(Source("A B ds al fine")));
        Assert.Single(all, d => d.Code == DiagnosticCodes.JumpTargetMissing);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(Head + "score { staff m }\n")),
            d => d.Code == DiagnosticCodes.JumpTargetMissing);
    }

    // ───────── marks written in the music (session 1249) ─────────

    private static string Book(string sections, string form)
        => "part m { clef treble }\npart b { clef bass }\n" + sections + "\n"
           + (form.Length > 0 ? "form { " + form + " }\n" : "") + "score { staff m staff b }\n";

    private static IReadOnlyList<Diagnostic> FaultsOf(string book)
    {
        var v = new FormJumpTargetValidator();
        v.Validate(SyntaxTree.Parse(book));
        return v.Diagnostics.Where(d => d.Code == DiagnosticCodes.JumpTargetMissing).ToList();
    }

    /// <summary>
    /// A mark a section's music writes is a landmark like the form's: the marks of the music
    /// and of the form are one set on the score's column (<c>RepeatStream.JumpFaults</c>).
    /// </summary>
    /// <remarks>Until LYS1034 narrowed to the phrase these books were errors and the question
    /// was never asked; the form's walk alone says "no segno" to the first row, whose writer
    /// is looking at one. Poison: drop the <c>RepeatStream.InTheMusic</c> arm of
    /// <c>FormJumpTargetValidator.Validate</c> — rows 1 and 3 report, and the next theory's
    /// music-only rows go silent.</remarks>
    [Theory]
    // the form's jump, the music's landmarks
    [InlineData("section A { m { c'4 c c c | segno d'1 | e'1 fine | f'1 | } }", "A ds al fine")]
    // all of it in the music, the coda in a later section
    [InlineData("section A { m { c'4 c c c | segno d'1 | e'1 to coda | f'1 | ds al coda } }\n"
        + "section B { m { coda g'1 | } }", "A B")]
    // the music's jump, the form's landmark; and the other part writes nothing
    [InlineData("section A { m { c'4 c c c | d'1 fine | e'1 | dc al fine } b { c1 | d1 | e1 | } }", "A")]
    // no form: the sections in declaration order
    [InlineData("section A { m { segno c'4 c c c | } }\nsection B { m { d'1 | ds } }", "")]
    public void AMarkOfTheMusic_IsALandmark(string sections, string form)
        => Assert.Empty(FaultsOf(Book(sections, form)));

    [Theory]
    [InlineData("section A { m { c'4 c c c | d'1 | ds al fine } }", "A", "segno")]
    [InlineData("section A { m { segno c'4 c c c | d'1 | ds al fine } }", "A", "fine")]
    [InlineData("section A { m { c'4 c c c | d'1 | dc al coda } }\nsection B { m { coda g'1 | } }", "A B", "to coda")]
    // the segno comes after the jump, in a later section
    [InlineData("section A { m { c'4 c c c | ds } }\nsection B { m { segno d'1 | } }", "A B", "segno")]
    // the jump stands in a later section: its place on the column is past the first play
    [InlineData("section A { m { c'4 c c c | } }\nsection B { m { d'1 | ds al fine } }", "A B", "segno")]
    // the form's jump beside a section that writes another mark: still the column's reading
    [InlineData("section A { m { c'4 c c c | d'1 fine | } }", "A ds al fine", "segno")]
    public void AJumpInABookOfMusicMarks_WarnsOnceAndNamesTheLandmark(string sections, string form, string landmark)
    {
        var fault = Assert.Single(FaultsOf(Book(sections, form)));
        Assert.Equal(DiagnosticSeverity.Warning, fault.Severity);
        Assert.Contains($"'{landmark}'", fault.Message);
        // The landmark may be written in the music or the form: the advice names neither.
        Assert.DoesNotContain("in the form", fault.Message);
    }

    /// <summary>The warning stands at the jump the MUSIC writes — once, though two parts write
    /// it and two forms play the section.</summary>
    [Fact]
    public void TheWarning_StandsAtTheMusicsJump_Once()
    {
        string book = "part m { clef treble }\npart b { clef bass }\n"
            + "section A { m { c'4 c c c | d'1 | ds al fine } b { c1 | d1 | ds al fine } }\n"
            + "form { A }\nform again { A A }\nscore { staff m staff b }\n";
        var fault = Assert.Single(FaultsOf(book));
        Assert.Equal(book.IndexOf("ds al fine"), fault.Span.Start);
        Assert.StartsWith("'ds al fine' has no 'segno' before it", fault.Message);
    }
}
