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

using System;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section's label declared once, on its top-level declaration — <c>section A2 "A'" { }</c>
/// (owner's decision 2026-10-09): every play prints it unless the form reference writes its
/// own, so several forms need not repeat <c>A2 "A'"</c>. A section inside a part or a track
/// takes no label (LYS0039); two top-level labels that disagree warn (LYS1045).
/// </summary>
/// <remarks>
/// Poisons: drop the declaration fallback from <c>SectionReferenceSyntax.DisplayLabel</c> ⇒ the
/// by-part and LilyPond tests go red; drop it from <c>LabelForDeclarationOrder</c> ⇒ the
/// form-less test goes red; read <c>Label</c> off any declaration, not only a top-level one ⇒
/// the part-label test goes red.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SectionDeclaredLabelTests
{
    private const string ByPart = """
        section A2 "A'" {}

        part p1 {
          section A2 { c'4 d' e' r }
          section B { c'1 }
        }
        part p2 {
          section A2 { e'4 f' g' r }
          section B { e'1 }
        }

        form { A2 B A2 "A''" ~A2 }
        score { staff p1 staff p2 }
        """;

    private static string?[] Labels(string source, string? part)
        => new MeasureCollector().Collect(SyntaxTree.Parse(source), part)
            .Voice.Measures.Select(m => m.SectionLabel).ToArray();

    [Fact]
    public void EveryPlay_PrintsTheDeclaredLabel_UnlessItsReferenceWritesOneOrHidesIt()
    {
        var tree = SyntaxTree.Parse(ByPart);
        Assert.Empty(tree.Diagnostics);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));

        // A2 → the declared A', B → its name, A2 "A''" → the reference's own, ~A2 → none.
        Assert.Equal(new[] { "A'", "B", "A''", null }, Labels(ByPart, "p1"));
        Assert.Equal(new[] { "A'", "B", "A''", null }, Labels(ByPart, "p2"));
    }

    [Fact]
    public void TheLilyPondTwin_MarksTheDeclaredLabel()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(ByPart));
        Assert.Contains("\\mark \\markup \\box \"A'\"", ly, StringComparison.Ordinal);
        Assert.Contains("\\mark \\markup \\box \"A''\"", ly, StringComparison.Ordinal);
        Assert.DoesNotContain("\\mark \\markup \\box \"A2\"", ly, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoForm_TheDeclarationOrderPrintsTheLabelToo()
    {
        const string single = """
            section A "Verse 1" { c'4 d' e' f' }
            section B { g'1 }
            """;
        Assert.Equal(new[] { "Verse 1", "B" }, Labels(single, null));
    }

    [Fact]
    public void AnEmptyDeclaredLabel_HidesTheMarkLikeAnEmptyReferenceLabel()
        => Assert.Equal(new string?[] { null, "B", "A''", null },
            Labels(ByPart.Replace("section A2 \"A'\" {}", "section A2 \"\" {}"), "p1"));

    [Theory]
    [InlineData("part p1 {\n  section A2 \"A'\" { c'4 d' e' r }\n}\nform { A2 }\n", "a part")]
    [InlineData("part p1 { section A2 { c'4 d' e' r } }\nlyrics w sings p1 { section A2 \"A'\" { la la la la } }\nform { A2 }\n", "a lyrics track")]
    [InlineData("part p1 { section A2 { c'4 d' e' r } }\nchords h { section A2 \"A'\" { C } }\nform { A2 }\n", "a chords track")]
    public void ALabelInsideAPartOrATrack_IsOneErrorAndPrintsNothing(string source, string where)
    {
        var tree = SyntaxTree.Parse(source);

        var d = Assert.Single(tree.Diagnostics);
        Assert.Equal(DiagnosticCodes.SectionLabelInsidePart, d.Code);
        Assert.Contains("inside " + where, d.Message, StringComparison.Ordinal);
        Assert.Contains("section A2 \"A'\" { }", d.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "A2" }, Labels(source, "p1"));
    }

    [Fact]
    public void TwoTopLevelLabelsThatDisagree_Warn_AndTheFirstWins()
    {
        string source = ByPart.Replace("section A2 \"A'\" {}",
            "section A2 \"A'\" { key g major }\nsection A2 \"A-two\" {}\nsection A2 \"A'\" {}");

        var warnings = SemanticValidation.Run(SyntaxTree.Parse(source))
            .Where(x => x.Code == DiagnosticCodes.SectionLabelConflict).ToList();

        var w = Assert.Single(warnings);   // the agreeing third one is not a conflict
        Assert.Contains("\"A'\"", w.Message, StringComparison.Ordinal);
        Assert.Equal("A'", Labels(source, "p1")[0]);
    }

    [Theory]
    [InlineData("section A2 \"A'\" { |}", "section A2 { |}")]
    [InlineData("section A2 \"A'\" { key g major |}", "section A2 { key g major |}")]
    [InlineData("section A2 \"A'\" |", "section A2 |")]
    public void TheEditor_ReadsALabelledHeaderAsTheUnlabelledOne(string labelled, string plain)
    {
        const string head = "part p1 { section A2 { c'4 d' e' r } }\n";
        static LilySharpLanguageServer.CompletionContext At(string text)
            => LilySharpLanguageServer.GetCompletionContext(text.Replace("|", ""), text.IndexOf('|'));

        Assert.Equal(At(head + plain), At(head + labelled));
    }
}
