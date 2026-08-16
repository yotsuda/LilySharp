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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A token that a <c>section</c>, <c>form</c> or <c>score</c> body has no item rule for:
/// reported (LYS0030) and KEPT, so the tree still spells itself back.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The half this exists for is the WIDTH, not the message. Until 2026-08-16 all three
/// containers consumed such a token with a bare <c>Advance()</c>, and the measurement that
/// named it was an equality: four books differing from a control only by an inserted
/// <c>"oops"</c> (7 characters) rendered SVGs byte-identical to the control, <c>data-pos</c>
/// INCLUDED, while <c>lysc check</c> answered <c>No errors found.</c> for every one. A node's
/// position is the running sum of the green widths before it, so those offsets were the ones
/// the book has with the token DELETED — and the same slide reached other diagnostics:
/// <c>form main { A section B }</c> reported its (correct) <c>Undefined section: 'B'</c> at
/// column 15, on the dropped <c>section</c> keyword, with <c>B</c> standing at column 23.
/// </para>
/// <para>
/// ⚠️ Reporting and KEEPING are separate repairs and the noisy container had only the first
/// one — see <c>PartHeaderParseTests</c> for LYS0025, which spoke while dropping the width
/// anyway. Every test below therefore asserts BOTH halves.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class StrayItemTokenTests
{
    // One book, four holes: the stray goes into the section body, the form body, the score
    // body, or nowhere (the control). Everything else is identical so a width comparison
    // against the control means what it says.
    private const string Control =
        "part melody\n"
        + "section A { melody { c4 d e f | } }\n"
        + "form main { A }\n"
        + "score main \"p\" { staff melody }\n";

    private static string WithStray(string container, string token) => container switch
    {
        "section" => Control.Replace("section A { melody", $"section A {{ {token} melody"),
        "form" => Control.Replace("form main { A", $"form main {{ {token} A"),
        "score" => Control.Replace("{ staff melody", $"{{ {token} staff melody"),
        _ => Control,
    };

    private static Diagnostic[] Strays(string source) =>
        SyntaxTree.Parse(source).Diagnostics
            .Where(d => d.Code == DiagnosticCodes.StrayItemToken)
            .ToArray();

    // ---- it is reported ---------------------------------------------------------------

    [Theory]
    [InlineData("section", "\"oops\"")]
    [InlineData("section", "part")]      // the confusion a reader actually makes
    [InlineData("section", "42")]
    [InlineData("form", "\"oops\"")]
    [InlineData("form", "section")]      // measured: this one corrupted the NEXT diagnostic
    [InlineData("form", "42")]
    [InlineData("score", "\"oops\"")]
    [InlineData("score", "42")]
    public void AStrayItemToken_IsReported(string container, string token)
    {
        var d = Assert.Single(Strays(WithStray(container, token)));
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains(token, d.Message);
        Assert.Contains(container, d.Message);
    }

    [Theory]
    [InlineData("section", "\"oops\"")]
    [InlineData("section", "part")]
    [InlineData("form", "\"oops\"")]
    [InlineData("form", "section")]
    [InlineData("score", "\"oops\"")]
    public void TheReportPointsAtTheTokenItself(string container, string token)
    {
        string src = WithStray(container, token);
        var d = Assert.Single(Strays(src));
        Assert.Equal(token, src.Substring(d.Span.Start, d.Span.Length));
    }

    // ---- and KEPT: the half a reader who never mistypes still pays for -----------------

    [Theory]
    [InlineData("section", "\"oops\"")]
    [InlineData("section", "part")]
    [InlineData("section", "42")]
    [InlineData("form", "\"oops\"")]
    [InlineData("form", "section")]
    [InlineData("form", "42")]
    [InlineData("score", "\"oops\"")]
    [InlineData("score", "42")]
    public void AStrayItemToken_KeepsItsWidth(string container, string token)
    {
        string src = WithStray(container, token);
        var root = SyntaxTree.Parse(src).GetRoot();

        // The round trip is the detector for this whole family (RULES §5.0).
        Assert.Equal(src, root.ToFullString());
        // Stated twice on purpose: the width is what the round trip is ABOUT, and a
        // ToFullString that happened to be right for another reason would not say so.
        Assert.Equal(src.Length, root.FullWidth);
        // …and the control it is measured against must be a different length, or the two
        // assertions above are satisfied by a book that never had a stray in it.
        Assert.NotEqual(Control.Length, src.Length);
    }

    [Fact]
    public void ADroppedTokenUsedToSlideEveryLaterNode_AndNoLongerDoes()
    {
        // The concrete measurement, as an invariant: each note stands where the source
        // says it stands. Before the fix these five nodes reported the CONTROL's offsets.
        string src = WithStray("section", "\"oops\"");
        var root = SyntaxTree.Parse(src).GetRoot();
        var notes = root.DescendantNodes().OfType<NoteSyntax>().ToList();

        Assert.Equal(4, notes.Count);
        // Every node stands where it says it stands (the shape AnnotationRoundTripTests
        // uses). Before the fix these five nodes carried the CONTROL's offsets, seven
        // characters early — the offsets this book has with the stray DELETED.
        foreach (var node in root.DescendantNodes())
        {
            string text = node.ToFullString();
            Assert.True(
                node.Position + text.Length <= src.Length
                && src.AsSpan(node.Position, text.Length).SequenceEqual(text),
                $"{node.Kind} at {node.Position} spells [{text}] but the source there is "
                + $"[{src.Substring(node.Position, Math.Min(text.Length, src.Length - node.Position))}]");
        }

        // The stray itself is inside the section, not before it: the first note must sit
        // AFTER where the control's first note sits, or nothing above is being tested.
        int controlFirstNote = Control.IndexOf("c4", System.StringComparison.Ordinal);
        Assert.True(notes[0].Span.Start > controlFirstNote,
            $"first note at {notes[0].Span.Start}, control has it at {controlFirstNote}");
    }

    [Fact]
    public void OneStrayDoesNotCascade()
    {
        // It is consumed, so the items after it still parse as themselves.
        string src = WithStray("section", "\"oops\"");
        Assert.Single(Strays(src));
        Assert.Single(SyntaxTree.Parse(src).GetRoot()
            .DescendantNodes().OfType<PartBlockSyntax>());
    }

    [Fact]
    public void TheControlIsClean()
    {
        // Positive control: the report above must come from the stray, not from the book.
        Assert.Empty(Strays(Control));
        Assert.Empty(SyntaxTree.Parse(Control).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // ---- barlines in a form: kept, and kept as two different things --------------------

    [Fact]
    public void APlainBarInAForm_IsKeptButIsNotAnItem()
    {
        // A '|' between form items asks for nothing the page does not already do, so it is
        // kept as a bare token: it contributes width and NOTHING else. Made into a barline
        // NODE it asks for something the author did not write — measured 2026-08-16,
        // `form main { A | B }` engraved three bars where `form main { A B }` engraves two.
        string src = Control.Replace("form main { A }", "form main { | A }");
        var root = SyntaxTree.Parse(src).GetRoot();

        Assert.Equal(src, root.ToFullString());
        Assert.Empty(Strays(src));
        Assert.Empty(SyntaxTree.Parse(src).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error));
        var form = Assert.Single(root.DescendantNodes().OfType<FormDeclarationSyntax>());
        Assert.DoesNotContain(form.ChildNodes().OfType<BarlineSyntax>(),
            b => b.BarToken.Text == "|");
    }

    [Fact]
    public void AnyOtherBarlineInAForm_IsKeptAsABarline()
    {
        // '||' names a glyph the reader expects to see, and now gets it: measured on a
        // real book (scratch/…/blogger.lys) the page gained exactly one element — a single
        // barline rect became a pair 0.49 apart — with the bar count and MIDI unchanged.
        string src = Control.Replace("form main { A }", "form main { A || }");
        var root = SyntaxTree.Parse(src).GetRoot();

        Assert.Equal(src, root.ToFullString());
        Assert.Empty(Strays(src));
        var form = Assert.Single(root.DescendantNodes().OfType<FormDeclarationSyntax>());
        Assert.Contains(form.ChildNodes().OfType<BarlineSyntax>(), b => b.BarToken.Text == "||");
    }

    [Fact]
    public void TheRepeatBarlinesKeepTheirOwnMeanings()
    {
        // ':|' and '|:' are claimed by earlier arms (2026-08-15); the catch-all must not
        // have taken them, or a one-sided repeat would stop being a repeat.
        string src = Control.Replace("form main { A }", "form main { |: A :| }");
        var root = SyntaxTree.Parse(src).GetRoot();

        Assert.Equal(src, root.ToFullString());
        Assert.Empty(Strays(src));
        Assert.Single(root.DescendantNodes().OfType<FormRepeatBlockSyntax>());
    }
}
