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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The two top-level scaffolds that write a form or a score: both write the UNNAMED block
/// first — the file's default form, the score that writes the input's own name — and a name
/// only beside it, a free one for <c>form</c> (LYS1017 "Duplicate form name" otherwise)
/// (docs/anonymous-blocks-design.md, 2026-10-09). A score's <c>form |</c> lists the forms
/// it may pick.
/// </summary>
/// <remarks>
/// ⚠️ Found by auditing every completion item that writes a NAME — the generalization of
/// the owner's <c>sings part</c> report. The test of a placeholder is not how it looks but
/// WHETHER THE THING IT NAMES HAS TO EXIST ALREADY: <c>chords ${1:prog}</c> is a name being
/// created and stays free text. Until 2026-10-09 a score's header named a form, which had
/// to exist; the score's name is its own now, and the reference is its <c>form</c> item.
/// </remarks>
[Trait("Category", "Unit")]
public class TopLevelNameScaffoldTests
{
    private const string Piece = "part m { clef treble\n  section A { c'4 d' e' f' | }\n}\n";

    private static CompletionItem Item(string doc, string label)
        => LilySharpLanguageServer.GetTopLevelCompletions(doc, doc.Length)
            .Items.Single(i => i.Label == label);

    /// <summary>The item as the editor leaves it: placeholders resolved to their defaults,
    /// an empty stop filled with <paramref name="pick"/>, the final caret with the body.</summary>
    private static string Accept(CompletionItem item, string body, string pick = "")
    {
        string text = Regex.Replace(item.InsertText!, @"\$\{\d+:([^}]*)\}", "$1").Replace("$0", body);
        return Regex.Replace(text, @"\$\d+", pick);
    }

    private static void AssertCompiles(string book)
    {
        var tree = SyntaxTree.Parse(book);
        var errors = tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error)
            .Select(d => d.Code + " " + d.Message)
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(" | ", errors) + "\n" + book);
    }

    [Fact]
    public void WithNothingYet_BothScaffoldsWriteTheUnnamedBlock()
    {
        var form = Item("", "form");
        var score = Item("", "score");
        Assert.StartsWith("form {", form.InsertText!);
        Assert.StartsWith("score {", score.InsertText!);
        Assert.Null(score.Command);

        AssertCompiles(Piece + Accept(form, "A") + "\n" + Accept(score, "staff m"));
    }

    [Fact]
    public void BesideTheUnnamedScore_TheScoreScaffoldWritesAName()
    {
        string doc = Piece + "form { A }\nscore { staff m }\n";
        var item = Item(doc, "score");

        Assert.Contains("score ${1:another}", item.InsertText!);
        AssertCompiles(doc + Accept(item, "staff m"));
    }

    [Fact]
    public void AScoresFormItem_ListsThisBooksForms()
    {
        string doc = Piece + "form verse { A }\nform chorus { A }\nscore {\n  form ";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterScoreForm,
            LilySharpLanguageServer.GetCompletionContext(doc, doc.Length));
        Assert.Equal(new[] { "verse", "chorus" },
            LilySharpLanguageServer.GetDeclaredNameCompletions(doc, "form", "Form")
                .Items.Select(i => i.Label).ToArray());
    }

    [Theory]
    [InlineData("", "form {")]                                             // the default is free
    [InlineData("form { A }\n", "form excerpt {")]                         // taken: a name
    [InlineData("form { A }\nform excerpt { A }\n", "form excerpt2 {")]    // and the next
    public void TheFormScaffold_NeverWritesANameThatIsTaken(string declared, string expected)
    {
        var item = Item(Piece + declared, "form");
        Assert.Contains(expected, Accept(item, "A"));

        // …and the book it leaves behind compiles — a derived name is only useful if it is
        // legal (LYS1016 / LYS1017 is what a taken one produces).
        string book = Piece + declared + Accept(item, "A") + "\n" + "score { staff m }\n";
        AssertCompiles(book);
    }

    [Fact]
    public void ANameBeingCREATEDStaysFreeText()
    {
        // The control that keeps the audit honest: `chords ${1:prog}` names a track the
        // writer is about to declare, so it must NOT be read from the document. The rule is
        // "does the thing have to exist already", not "is it a name".
        var chords = Item(Piece, "chords");
        Assert.Contains("${1:prog}", chords.InsertText!);
        Assert.Null(chords.Command);
    }
}
