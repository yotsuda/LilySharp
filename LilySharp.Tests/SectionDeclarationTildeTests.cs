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
/// A section's rehearsal label is hidden at the FORM reference (<c>form { ~A }</c>) and
/// nowhere else: <c>section ~A { … }</c> is LYS0033, and the two readers that engrave a label
/// ask the reference alone.
/// </summary>
/// <remarks>
/// <para>
/// Owner's decision, 2026-09-24. From 2026-08-31 the declaration's tilde flipped the label
/// default and a reference's tilde then SHOWED — one equality, and no form line could be read
/// without the declarations. It went because in part-major layout the property had one home
/// per part (<c>part p1 { section ~A }</c> beside <c>part p2 { section A }</c> is one section,
/// declared hidden once), and because the author's books never used the flip: 342 tilde
/// declarations, each referenced once, none by a showing tilde.
/// </para>
/// <para>
/// The page and the LilyPond twin are both read because they are the only two outputs that
/// engrave a section label (MIDI and MusicXML write none, deliberately), and on 2026-08-25 one
/// of the page's arms had never been taught the tilde while the twin's comment claimed to
/// mirror it (SectionLabelRule's remarks).
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SectionDeclarationTildeTests
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

    // ===== the declaration's tilde: reported, kept, and meaning nothing =====

    /// <summary>
    /// Every place a section is declared reports the tilde once, as LYS0033, and names where
    /// it belongs — and nothing else goes wrong: the name after it parses, and the tree still
    /// spells the source byte for byte.
    /// </summary>
    [Theory]
    [InlineData("time 4/4\npart m\nsection ~A { m { c'4 c c c | } }\nform main { A }\nscore main { staff m }\n")]
    [InlineData("time 4/4\npart m {\n  section ~A { c'4 c c c | }\n}\nform main { A }\nscore main { staff m }\n")]
    [InlineData("time 4/4\npart m {\n  section A { c'4 c c c | }\n}\nlyrics verse {\n  section ~A { la la la la | }\n}\nform main { A }\nscore main {\n  staff m\n  lyrics verse sings m\n}\n")]
    [InlineData("time 4/4\npart m {\n  section A { c'4 c c c | }\n}\nchords prog {\n  section ~A { Dm7 | }\n}\nform main { A }\nscore main {\n  chords prog\n  staff m\n}\n")]
    public void ADeclarationTildeIsReportedOnceAndKept(string source)
    {
        var tree = SyntaxTree.Parse(source);

        var error = Assert.Single(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCodes.SectionDeclarationTilde, error.Code);
        Assert.Equal(source.IndexOf("~A", System.StringComparison.Ordinal), error.Span.Start);
        Assert.Equal(1, error.Span.Length);
        Assert.Contains("form { ~A }", error.Message);

        Assert.Equal(source, tree.GetRoot().ToFullString());
        Assert.Contains(tree.GetNodes<SectionDeclarationSyntax>(), s => s.SectionName == "A");
    }

    /// <summary>
    /// The recovered tilde hides nothing: a book that writes it labels exactly as the book
    /// without it. (This is the row that would go red if the declaration regained a say.)
    /// </summary>
    [Theory]
    [InlineData("A B")]
    [InlineData("~A B")]
    public void ADeclarationTildeChangesNoLabel(string form)
    {
        Assert.Equal(PageLabels(Book("A", form)), PageLabels(Book("~A", form)));
        Assert.Equal(TwinLabels(Book("A", form)), TwinLabels(Book("~A", form)));
        Assert.Equal(PageLabels(PartMajorBook("A", form)), PageLabels(PartMajorBook("~A", form)));
    }

    // ===== the reference decides =====

    /// <summary>A bare reference shows the label, a tilde reference hides it — on the page
    /// and in the twin, section-major and part-major alike.</summary>
    [Theory]
    [InlineData("A B", new[] { "A", "B" })]
    [InlineData("~A B", new[] { "B" })]
    [InlineData("A ~B", new[] { "A" })]
    public void TheReferenceAloneDecides(string form, string[] expected)
    {
        Assert.Equal(expected, PageLabels(Book("A", form)));
        Assert.Equal(expected, TwinLabels(Book("A", form)));
        Assert.Equal(expected, PageLabels(PartMajorBook("A", form)));
        Assert.Equal(expected, TwinLabels(PartMajorBook("A", form)));
    }

    /// <summary>A volta ending asks the same question: the tilde binds to the section NAME.</summary>
    [Theory]
    [InlineData("|: B [1. A ] :| [2. B ]", new[] { "B", "A", "B" })]
    [InlineData("|: B [1. ~A ] :| [2. B ]", new[] { "B", "B" })]
    public void AnEndingAsksTheSameQuestion(string form, string[] expected)
    {
        Assert.Equal(expected, PageLabels(Book("A", form)));
        Assert.Equal(expected, TwinLabels(Book("A", form)));
    }

    /// <summary>With no form, sections play in declaration order and every one labels itself
    /// — a form is the only place a label is hidden.</summary>
    [Fact]
    public void WithNoForm_EverySectionLabelsItself()
    {
        const string book = """
            time 4/4
            part m
            section A { m { c'4 c c c | } }
            section B { m { d'4 d d d | } }
            score main { staff m }
            """;
        Assert.Equal(new[] { "A", "B" }, PageLabels(book));
        Assert.Equal(new[] { "A", "B" }, TwinLabels(book));
    }

    // ===== the quoted occurrence label =====

    /// <summary>A parked label prints on a bare reference; an EMPTY one suppresses the mark.</summary>
    [Fact]
    public void AParkedLabelPrintsAndAnEmptyOneSuppresses()
    {
        Assert.Equal(new[] { "shown", "B" }, PageLabels(Book("A", "A \"shown\" B")));
        Assert.Equal(new[] { "shown", "B" }, TwinLabels(Book("A", "A \"shown\" B")));
        Assert.Equal(new[] { "B" }, PageLabels(Book("A", "A \"\" B")));
        Assert.Equal(new[] { "B" }, TwinLabels(Book("A", "A \"\" B")));
    }

    /// <summary>LYS0012 fires exactly when a label is written on a play that prints none —
    /// and agrees with the page.</summary>
    [Theory]
    [InlineData("~A \"alt\" B", true)]
    [InlineData("A \"alt\" B", false)]
    [InlineData("|: B [1. ~A \"alt\" ] :| [2. B ]", true)]
    public void ALabelThatWillNotPrintIsReported(string form, bool warns)
    {
        string book = Book("A", form);
        Assert.Equal(warns, WarnsHiddenLabel(book));
        Assert.Equal(!warns, PageLabels(book).Contains("alt"));
    }
}
