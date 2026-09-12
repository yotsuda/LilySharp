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
/// The two top-level scaffolds that write a FORM NAME, and the opposite questions they
/// answer: <c>form</c> CREATES one (it must be free — LYS1017 "Duplicate form name") and
/// <c>score</c> REFERS to one (it must exist — LYS1018 "Unknown form"). Both read the
/// document now; both wrote the literal <c>main</c> before 2026-09-12.
/// </summary>
/// <remarks>
/// ⚠️ Found by auditing every completion item that writes a NAME — the generalization of
/// the owner's <c>sings part</c> report. The test of a placeholder is not how it looks but
/// WHETHER THE THING IT NAMES HAS TO EXIST ALREADY: <c>chords ${1:prog}</c> is a name being
/// created and stays free text, while these two are a reference and a declaration of the
/// same name, and each was wrong in its own direction. Measured: in a book whose form is
/// <c>verse</c>, accepting <c>score</c> typed "Unknown form 'main'"; in a book that already
/// has <c>main</c>, accepting <c>form</c> typed a duplicate.
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
    public void TheScoreScaffold_NamesTheFormThisBookDeclares()
    {
        string doc = Piece + "form verse { A }\n";
        var item = Item(doc, "score");

        Assert.Contains("score verse", item.InsertText!);
        Assert.Null(item.Command);                      // nothing to choose between
        AssertCompiles(doc + Accept(item, "staff m"));
    }

    [Fact]
    public void WithSeveralForms_TheScoreScaffoldLeavesTheNameToThePicker()
    {
        string doc = Piece + "form verse { A }\nform chorus { A }\n";
        var item = Item(doc, "score");

        Assert.Contains("score $1", item.InsertText!);
        Assert.Equal("editor.action.triggerSuggest", item.Command?.CommandIdentifier);
        AssertCompiles(doc + Accept(item, "staff m", pick: "chorus"));
    }

    [Fact]
    public void ThePickerThatOpens_ListsThisBooksForms()
    {
        // The other end of the retrigger: the caret it leaves is a position that answers
        // with the forms — it answered with the top-level KEYWORD list until 2026-09-12,
        // which is why the item could not hand the choice over.
        string doc = Piece + "form verse { A }\nform chorus { A }\nscore ";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterScoreKeyword,
            LilySharpLanguageServer.GetCompletionContext(doc, doc.Length));
        Assert.Equal(new[] { "verse", "chorus" },
            LilySharpLanguageServer.GetDeclaredNameCompletions(doc, "form", "Form")
                .Items.Select(i => i.Label).ToArray());
    }

    [Fact]
    public void WithNoFormYet_TheTwoScaffoldsAgreeOnTheName()
    {
        // An empty book has nothing to name, so `score` keeps `main` as a placeholder — and
        // it is the same name the `form` item writes beside it, so accepting BOTH compiles.
        var form = Item("", "form");
        var score = Item("", "score");
        Assert.Contains("form main", form.InsertText!);
        Assert.Contains("${1:main}", score.InsertText!);

        AssertCompiles(Piece + Accept(form, "A") + "\n" + Accept(score, "staff m"));
    }

    [Theory]
    [InlineData("", "form main")]                                    // free
    [InlineData("form main { A }\n", "form main2")]                  // taken
    [InlineData("form main { A }\nform main2 { A }\n", "form main3")] // and the next
    public void TheFormScaffold_NeverWritesANameThatIsTaken(string declared, string expected)
    {
        var item = Item(Piece + declared, "form");
        Assert.Contains(expected, Accept(item, "A"));

        // …and the book it leaves behind compiles — a derived name is only useful if it is
        // legal (LYS1017 is what a taken one produces).
        string book = Piece + declared + Accept(item, "A") + "\n"
                    + "score " + (declared.Length == 0 ? "main" : "main") + " { staff m }\n";
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
