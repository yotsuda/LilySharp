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
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>section ~A { … }</c> flips that section's label default — and the two readers that
/// engrave a label both have to say so.
/// </summary>
/// <remarks>
/// <para>
/// Owner's decision, 2026-08-31, and the prerequisite for making <c>|:</c> form-only: a
/// section cut only to carry a repeat edge should not print a rehearsal letter, and "this is
/// structure" is a property of the SECTION rather than something repeated at every reference.
/// The author's books hold 2309 bare references against 260 tilde ones, so the flip is
/// written once on the declaration.
/// </para>
/// <para>
/// ⚠️ THE TILDE KEEPS ONE MEANING: "the other one than the default". With an ordinary
/// declaration it hides, exactly as it always did; with <c>section ~A</c> it SHOWS. The whole
/// rule is one equality (<c>SectionLabelRule.IsShown</c>), and these cases are its truth
/// table — read by the page and by the LilyPond twin, which are the only two outputs that
/// engrave a section label at all (MIDI and MusicXML write none, deliberately).
/// </para>
/// <para>
/// ⚠️ THIS FILE EXISTS BECAUSE THE RULE HAD SEVENTEEN CALL SITES — 13 in the collector and 4
/// in the twin — each spelling <c>silent ? null : DisplayLabel ?? name</c> for itself,
/// and on 2026-08-25 one of the page's arms had never been taught <c>IsSilent</c> while the
/// twin's comment claimed to mirror it. A rule with seventeen homes cannot gain a term, so the
/// fold came first and this guard covers the folded rule at both readers.
/// ⚠️ THE COUNT WAS FIRST WRITTEN AS ELEVEN, from a partial grep that read the bar-counting
/// walk as two arms rather than six, and it reached three other files and a commit message
/// before it was checked. The counting method, so the next reader need not repeat it: grep the
/// four argument-shapers plus <c>SectionLabelRule.LabelFor</c> over LilySharp.Core and
/// subtract the four shaper DEFINITIONS — 4 in the twin, 4 in MeasureCollector.cs, 9 in
/// MeasureCollector.Form.cs.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SectionLabelDefaultTests
{
    /// <param name="declaration">"A" or "~A" — the section-major declaration of A.</param>
    /// <param name="form">The form body.</param>
    private static string Book(string declaration, string form) => $$"""
        time 4/4
        part m
        section {{declaration}} { m { c'4 c c c | } }
        section B { m { d'4 d d d | } }
        form main { {{form}} }
        score main { staff m }
        """;

    /// <summary>The same book written part-major — the layout converter turns these two into
    /// each other, so they must label identically.</summary>
    private static string PartMajorBook(string declaration, string form) => $$"""
        time 4/4
        part m {
          section {{declaration}} { c'4 c c c | }
          section B { d'4 d d d | }
        }
        form main { {{form}} }
        score main { staff m }
        """;

    // ===== the two readers =====

    /// <summary>The labels the PAGE engraves, in bar order.</summary>
    private static string[] PageLabels(string lys) =>
        new MeasureCollector().Collect(SyntaxTree.Parse(lys), "m")
            .Voice.Measures.Select(m => m.SectionLabel)
            .Where(l => l != null).Select(l => l!).ToArray();

    /// <summary>The labels the LilyPond twin writes, in emission order.</summary>
    private static string[] TwinLabels(string lys) =>
        Regex.Matches(new LilyPondExporter().Export(SyntaxTree.Parse(lys)),
                @"\\mark \\markup \\box ""([^""]*)""")
            .Select(m => m.Groups[1].Value).ToArray();

    private static bool WarnsHiddenLabel(string lys) =>
        SemanticValidation.Run(SyntaxTree.Parse(lys))
            .Any(d => d.Code == DiagnosticCodes.HiddenSectionLabel);

    // ===== the truth table, read twice =====

    /// <summary>
    /// One equality, four rows: the declaration sets the default and the reference's tilde
    /// asks for the other one.
    /// </summary>
    /// <remarks>
    /// The two ordinary rows are what the language has always done and must keep doing — they
    /// are here so that a change to the flip cannot quietly move them.
    /// </remarks>
    [Theory]
    [InlineData("A", "A B", new[] { "A", "B" })]     // ordinary declaration: a bare reference shows
    [InlineData("A", "~A B", new[] { "B" })]         // …and its tilde hides
    [InlineData("~A", "A B", new[] { "B" })]         // flipped: a bare reference is silent
    [InlineData("~A", "~A B", new[] { "A", "B" })]   // …and its tilde SHOWS
    public void TheDeclarationSetsTheDefaultAndTheReferenceAsksForTheOther(
        string declaration, string form, string[] expected)
    {
        Assert.Equal(expected, PageLabels(Book(declaration, form)));
        Assert.Equal(expected, TwinLabels(Book(declaration, form)));
    }

    /// <summary>Part-major and section-major are the same book, so they label the same.</summary>
    [Theory]
    [InlineData("A", "A B")]
    [InlineData("~A", "A B")]
    [InlineData("~A", "~A B")]
    public void TheTwoLayoutsAgree(string declaration, string form)
    {
        Assert.Equal(PageLabels(Book(declaration, form)),
                     PageLabels(PartMajorBook(declaration, form)));
        Assert.Equal(TwinLabels(Book(declaration, form)),
                     TwinLabels(PartMajorBook(declaration, form)));
    }

    // ===== the quoted occurrence label =====

    /// <summary>
    /// A parked label prints when the play is SHOWN, whichever tilde made it shown.
    /// </summary>
    /// <remarks>
    /// ⚠️ The tilde spelling used to drop its label on the floor (FormWalk passed null), which
    /// was harmless while a tilde always hid. Measured 2026-08-31 after the flip landed:
    /// <c>section ~A</c> + <c>form { ~A "shown" }</c> printed the section's NAME until the
    /// reader carried the parked label through.
    /// </remarks>
    [Fact]
    public void AParkedLabelPrintsOnTheSpellingThatShows()
    {
        Assert.Equal(new[] { "shown", "B" }, PageLabels(Book("~A", "~A \"shown\" B")));
        Assert.Equal(new[] { "shown", "B" }, TwinLabels(Book("~A", "~A \"shown\" B")));
        Assert.False(WarnsHiddenLabel(Book("~A", "~A \"shown\" B")));

        // …and on the ordinary declaration it is the bare reference that shows it.
        Assert.Equal(new[] { "shown", "B" }, PageLabels(Book("A", "A \"shown\" B")));
        Assert.False(WarnsHiddenLabel(Book("A", "A \"shown\" B")));
    }

    /// <summary>An EMPTY quoted label still suppresses the mark, under either default.</summary>
    /// <remarks>It is the occurrence-level spelling of "no label" and needs no declaration; it
    /// is applied after the shown/hidden question rather than folded into it, so the two stay
    /// separable.</remarks>
    [Theory]
    [InlineData("A", "A \"\" B")]
    [InlineData("~A", "~A \"\" B")]
    public void AnEmptyLabelStillSuppresses(string declaration, string form)
    {
        Assert.Equal(new[] { "B" }, PageLabels(Book(declaration, form)));
        Assert.Equal(new[] { "B" }, TwinLabels(Book(declaration, form)));
    }

    // ===== the diagnostic =====

    /// <summary>
    /// LYS0012 fires exactly when a label is written and the play prints nothing — asked of
    /// the RULE, not of the surface.
    /// </summary>
    /// <remarks>
    /// ⚠️ It used to be a PARSE-time warning that said "hidden by '~'", and it could not
    /// survive the flip: a parser cannot see the declaration, so it would have called
    /// <c>form { ~A "shown" }</c> hidden while the page printed it. The row that had NO
    /// diagnostic before is the interesting one — a bare reference to a <c>section ~A</c> is
    /// silent, and nothing said so.
    /// </remarks>
    [Theory]
    [InlineData("A", "~A \"alt\" B", true)]    // ordinary + tilde: hidden, warned (as before)
    [InlineData("A", "A \"alt\" B", false)]    // ordinary + bare: shown
    [InlineData("~A", "A \"alt\" B", true)]    // flipped + bare: hidden — this row had no diagnostic at all
    [InlineData("~A", "~A \"alt\" B", false)]  // flipped + tilde: shown
    public void ALabelThatWillNotPrintIsReported(string declaration, string form, bool warns)
    {
        string book = Book(declaration, form);
        Assert.Equal(warns, WarnsHiddenLabel(book));
        // …and the diagnostic agrees with the page rather than with the surface.
        Assert.Equal(!warns, PageLabels(book).Contains("alt"));
    }

    // ===== the form-less path =====

    /// <summary>
    /// With no form, sections play in declaration order and label themselves — and a
    /// <c>section ~A</c> silences its own.
    /// </summary>
    /// <remarks>This is the arm with no reference at all, so it is the one a rule written in
    /// terms of "the reference's tilde" would forget. It asks the same function with
    /// <c>referenceIsSilent: false</c>.</remarks>
    [Fact]
    public void WithNoForm_ADeclarationSilencesItsOwnLabel()
    {
        const string head = """
            time 4/4
            part m
            """;
        const string tail = """
            score main { staff m }
            """;
        string plain = head + "\nsection A { m { c'4 c c c | } }\nsection B { m { d'4 d d d | } }\n" + tail;
        string flipped = head + "\nsection ~A { m { c'4 c c c | } }\nsection B { m { d'4 d d d | } }\n" + tail;

        Assert.Equal(new[] { "A", "B" }, PageLabels(plain));
        Assert.Equal(new[] { "B" }, PageLabels(flipped));
        Assert.Equal(new[] { "A", "B" }, TwinLabels(plain));
        Assert.Equal(new[] { "B" }, TwinLabels(flipped));
    }

    // ===== the ending =====

    /// <summary>A volta ending asks the same question: the tilde binds to the section NAME.</summary>
    [Theory]
    [InlineData("A", "|: B [1. A ] :| [2. B ]", new[] { "B", "A", "B" })]
    [InlineData("A", "|: B [1. ~A ] :| [2. B ]", new[] { "B", "B" })]
    [InlineData("~A", "|: B [1. A ] :| [2. B ]", new[] { "B", "B" })]
    [InlineData("~A", "|: B [1. ~A ] :| [2. B ]", new[] { "B", "A", "B" })]
    public void AnEndingFollowsTheSameDefault(string declaration, string form, string[] expected)
        => Assert.Equal(expected, PageLabels(Book(declaration, form)));

    // ===== nothing existing moves =====

    /// <summary>
    /// An ordinary book is what it was: the flip can only fire on a section declared with a
    /// tilde, and none exists in the tree.
    /// </summary>
    /// <remarks>Measured 2026-08-31 across every .lys on disk: 0 of 1925 books write
    /// <c>section ~A</c>, and before this change it was a hard parse error. The addition
    /// cannot re-read anything already written.</remarks>
    [Fact]
    public void SectionTildeIsANewSpelling_NotAReReadingOfAnOldOne()
    {
        var tree = SyntaxTree.Parse(Book("~A", "A B"));
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.True(tree.GetNodes<SectionDeclarationSyntax>()
            .Single(s => s.SectionName == "A").LabelHiddenByDefault);
        Assert.False(tree.GetNodes<SectionDeclarationSyntax>()
            .Single(s => s.SectionName == "B").LabelHiddenByDefault);
    }
}
