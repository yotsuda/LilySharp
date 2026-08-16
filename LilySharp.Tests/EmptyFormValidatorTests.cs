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
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>form</c> that names no section arranges nothing, and what comes out is not the blank
/// page LYS6002 exists to prevent — it is NO page: <c>lysc svg</c> wrote a zero-byte file
/// while printing "Created" and exiting 0, and <c>lysc check</c> said "No errors found."
/// LYS6007 names it.
/// </summary>
[Trait("Category", "Unit")]
public class EmptyFormValidatorTests
{
    private const string Preamble =
        "part m { clef treble }\nsection A { m { c4 d e f | } }\nsection B { m { g4 a b c | } }\n";

    private static Diagnostic[] Validate(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src))
            .Where(d => d.Code == DiagnosticCodes.EmptyForm).ToArray();

    /// <summary>
    /// The four spellings measured 2026-08-16, all of which rendered a ZERO-BYTE SVG.
    /// A body holding only barlines / only navigation marks / only a <c>_"text"</c> is as
    /// empty as one holding nothing: none of them names a section.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("||")]
    [InlineData("segno")]
    [InlineData("_\"a title\"")]
    public void AFormThatNamesNoSection_IsAnError(string body)
    {
        var d = Assert.Single(Validate(Preamble + "form main { " + body + " }\n"));
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("main", d.Message);
        Assert.Contains("names no section", d.Message);
    }

    /// <summary>
    /// ⚠️ The point of the whole check: a form's section reference has THREE spellings, and
    /// two of them are reached only through a container (a repeat block, a volta bracket).
    /// Counting only the plain one would have called three legitimate arrangements empty.
    /// The list is not re-spelt here or in the validator — both ask SectionReferenceFinder.
    /// </summary>
    [Theory]
    [InlineData("A")]                      // plain
    [InlineData("~A")]                     // silent — label hidden, section still played
    [InlineData("|: A :|")]                // inside a repeat block
    [InlineData("|: [1. A] :| [2. B]")]    // ONLY inside volta alternatives
    public void AFormThatNamesASection_IsClean(string body)
        => Assert.Empty(Validate(Preamble + "form main { " + body + " }\n"));

    /// <summary>
    /// The claim behind the diagnostic, asserted directly rather than quoted: the empty form
    /// really does engrave nothing, and the control really does engrave something. Without
    /// this pair the test above would pass just as well if the renderer had always drawn a
    /// page — the diagnostic would then be noise rather than a rescue.
    /// </summary>
    [Fact]
    public void TheEmptyFormEngravesNothingAndTheControlEngravesSomething()
    {
        string empty = SvgGenerator.Generate(SyntaxTree.Parse(Preamble + "form main { }\n"));
        string control = SvgGenerator.Generate(SyntaxTree.Parse(Preamble + "form main { A }\n"));

        Assert.Equal("", empty);          // zero bytes — not a blank page, no page
        Assert.NotEqual("", control);
    }

    /// <summary>
    /// The squiggle sits on the form's OWN braces, for the reason recorded on LYS6002: the
    /// body is what has to change, and a mark on the keyword reads as "this line is wrong"
    /// while the eye goes to whatever declaration sits above it.
    /// </summary>
    [Fact]
    public void TheErrorMarksTheFormsOwnBraces()
    {
        const string src = "part m { clef treble }\nsection A { m { c4 } }\nform main {\n}\n";
        var d = Assert.Single(Validate(src));
        Assert.Equal(src.LastIndexOf('{'), d.Span.Start);
        Assert.Equal(src.LastIndexOf('}') + 1, d.Span.End);
    }

    /// <summary>Each empty form is reported on its own, matching LYS6002's behaviour, so a
    /// file with two of them does not hide one behind the other.</summary>
    [Fact]
    public void EveryEmptyFormIsReported()
        => Assert.Equal(2, Validate(Preamble + "form main { }\nform other { }\n").Length);

    /// <summary>
    /// ⚠️ This was the KNOWN edge of this check: of the 46 form-body shapes enumerated from
    /// GRAMMAR §StructureItem, 16 engraved zero bytes, 15 were caught above, and this was the
    /// sixteenth — a volta ending that no repeat opens, which DOES name a section (so LYS6007
    /// rightly stays quiet) but which the engraver dropped anyway. The note said "when that is
    /// closed this test has to change, which is the reason for writing it as a test and not a
    /// note", and it did exactly that: it was the one red in the suite when the ending started
    /// engraving. The half that survives is the half this check owns.
    /// </summary>
    /// <remarks>
    /// The other half — that a repeat-less ending IS its plain section, in all four outputs —
    /// now lives in <see cref="FormVoltaWithoutRepeatTests"/>. The zero-byte assertion is gone
    /// because the page is no longer zero bytes; asserting it is non-empty here as well would
    /// put a second home under a claim that already has one.
    /// </remarks>
    [Fact]
    public void AVoltaEndingNoRepeatOpens_NamesASection_SoThisCheckStaysQuiet()
        => Assert.Empty(Validate(Preamble + "form main { [1. A] }\n"));

    /// <summary>
    /// A form that names a section which does not exist is a DIFFERENT defect — LYS1005 says
    /// so by name. It is not called empty on top of that: the author named something, and
    /// telling them they named nothing would send them the wrong way.
    /// </summary>
    [Fact]
    public void AFormNamingAnUndefinedSection_IsNotCalledEmpty()
        => Assert.Empty(Validate(Preamble + "form main { Nope }\n"));

    /// <summary>
    /// An UNNAMED form is reported as unnamed (LYS1016) and nothing else. Naming it is the
    /// first repair, and the empty-form message would have to say "Form ''", which names
    /// nothing the author can look for.
    /// </summary>
    [Fact]
    public void AnUnnamedEmptyForm_IsReportedOnlyAsUnnamed()
    {
        var all = SemanticValidation.Run(SyntaxTree.Parse(Preamble + "form { }\n")).ToArray();
        Assert.Contains(all, d => d.Code == DiagnosticCodes.UnnamedForm);
        Assert.DoesNotContain(all, d => d.Code == DiagnosticCodes.EmptyForm);
    }
}
