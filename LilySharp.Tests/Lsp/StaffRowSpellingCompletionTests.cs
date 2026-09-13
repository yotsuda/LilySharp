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
using System.IO;
using LilySharp.Lsp.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// A staff row carries an optional clef before the part name and optional selectors after
/// it (GRAMMAR StaffRender: <c>'staff' , [ClefName] , PartRef , [ 'as' , StaffSelector ,
/// { StaffSelector } ]</c>), so the popup has to read the row at both lengths:
/// <c>staff treble ▮</c> still wants the part NAME, and <c>staff treble melody ▮</c> wants
/// the selectors the two-word row gets.
/// </summary>
/// <remarks>
/// Both halves were user-reported (session 374). ⚠️ They are one defect seen from two
/// sides: the row's readers counted WORDS, so writing the clef — the very word the popup
/// itself offers at <c>staff ▮</c> — shifted the part name out of the position the readers
/// watched. The `as` appeared where the name was still missing and vanished where the name
/// was already written.
/// ⚠️ The selectors after a bare clef were not a guess to make at all: whether
/// <c>staff bass ▮</c> is "clef bass, name next" or "part bass, selectors next" is a
/// question about THIS DOCUMENT, and the document answers it — the ambiguity is real only
/// where a part by that name is declared.
/// </remarks>
[Trait("Category", "Unit")]
public class StaffRowSpellingCompletionTests
{
    /// <summary>One part, named so that no clef or tuning word collides with it.</summary>
    private const string OnePart = "part melody { section A { c'4 d' e' f' | } }\nform main { A }\n";

    /// <summary>…and a book whose SECOND part is named after a clef (and a tuning) word.</summary>
    private const string PartNamedBass =
        "part melody { section A { c'4 d' e' f' | } }\npart bass { section A { c4 d e f | } }\nform main { A }\n";

    private static string[] LabelsAt(string doc, string row)
    {
        int i = row.IndexOf('▮');
        string text = doc + row.Remove(i, 1);
        int offset = doc.Length + i;

        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///score.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        return list?.Items.Select(i => i.Label!).ToArray() ?? System.Array.Empty<string>();
    }

    [Theory]
    [InlineData("score main { staff treble ▮ }")]
    [InlineData("score main { ossia treble ▮ }")]
    [InlineData("score main { grandStaff { staff treble ▮ } }")]
    public void AfterAClefThatNamesNoPart_OnlyThePartsAreOffered(string row)
    {
        // The clef has been written, so a part NAME is the only thing that can follow —
        // `staff treble as lines 1` is not an ambiguous row, it is a row with no part.
        Assert.Equal(new[] { "melody" }, LabelsAt(OnePart, row));
    }

    [Fact]
    public void AfterAClefThatALSONamesAPart_TheSelectorsStayBeside()
    {
        // `staff bass` in a book that declares `part bass` may already be complete, so both
        // readings are offered — the case the unconditional list was written for.
        var labels = LabelsAt(PartNamedBass, "score main { staff bass ▮ }");
        Assert.Equal(new[] { "melody", "bass", "as lines", "as removeEmpty" }, labels);
    }

    [Theory]
    [InlineData("score main { staff treble melody ▮ }")]
    [InlineData("score main { ossia treble melody ▮ }")]
    [InlineData("score main { staff bass melody ▮ }")]   // clef word, other part name
    public void AfterAClefAndTheName_TheSelectorsAreOffered(string row)
    {
        var labels = LabelsAt(OnePart, row);
        Assert.Contains("as lines", labels);
        Assert.Contains("as removeEmpty", labels);
        // …and the row may simply end there, so the next render item is still offered.
        Assert.Contains("staff", labels);
    }

    [Fact]
    public void InsideAGroup_TheThreeWordRowKeepsTheGroupsNarrowList()
    {
        // A group refuses the score-wide list (LYS6011), so the three-word row must land in
        // the GROUP's continuation set, not the score's.
        Assert.Equal(new[] { "as lines", "as removeEmpty", "staff", "condensedStaff", "combinedStaff", "lyrics" },
            LabelsAt(OnePart, "score main { grandStaff { staff treble melody ▮ } }"));
    }

    [Theory]
    [InlineData("score main { staff melody ▮ }")]
    [InlineData("score main { tab melody ▮ }")]
    public void TheTwoWordRowIsUnchanged(string row)
    {
        var labels = LabelsAt(OnePart, row);
        Assert.Contains(row.Contains("tab") ? "as numbers" : "as lines", labels);
    }

    [Fact]
    public void TheTabRowReadsTheSameQuestion()
    {
        // The tab row's tuning is the clef's twin (`tab bass5 melody`), and its `as` was
        // offered after every tuning word for the same untested reason. One book, one part
        // named `bass`, both answers.
        Assert.Equal(new[] { "melody" }, LabelsAt(OnePart, "score main { tab bass5 ▮ }"));
        Assert.Equal(new[] { "melody", "bass", "as numbers", "as full" },
            LabelsAt(PartNamedBass, "score main { tab bass ▮ }"));

        var withName = LabelsAt(OnePart, "score main { tab bass5 melody ▮ }");
        Assert.Contains("as numbers", withName);
        Assert.Contains("as full", withName);
    }

    // ===== the `as` chain: `[ 'as' , StaffSelector , { StaffSelector } ]` =====

    [Theory]
    [InlineData("score main { staff m as lines 1 ▮ }", "removeEmpty", "lines")]
    [InlineData("score main { staff m as removeEmpty all ▮ }", "lines", "removeEmpty")]
    [InlineData("score main { ossia m as lines 1 ▮ }", "removeEmpty", "lines")]
    public void AfterACompleteSelector_TheChainGoesOnWithTheOtherOne(
        string row, string expected, string written)
    {
        var labels = LabelsAt(OnePart, row);
        // BARE — the chain shares the one `as` (ConsumeStaffSelectors), so a second `as`
        // would not parse.
        Assert.Contains(expected, labels);
        Assert.DoesNotContain("as " + expected, labels);
        // The selector already written is not offered again: it is consumed and means
        // nothing the second time.
        Assert.DoesNotContain(written, labels);
        // …and the row may simply end here, so the continuations are still offered.
        Assert.Contains("staff", labels);
    }

    [Fact]
    public void WhenBothSelectorsAreWritten_OnlyTheContinuationsRemain()
    {
        var labels = LabelsAt(OnePart, "score main { staff m as lines 1 removeEmpty all ▮ }");
        Assert.DoesNotContain("lines", labels);
        Assert.DoesNotContain("removeEmpty", labels);
        Assert.Contains("staff", labels);
    }

    [Fact]
    public void TheChainInsideAGroup_KeepsTheGroupsNarrowContinuations()
        => Assert.Equal(new[] { "removeEmpty", "staff", "condensedStaff", "combinedStaff", "lyrics" },
            LabelsAt(OnePart, "score main { grandStaff { staff m as lines 1 ▮ } }"));

    [Theory]
    [InlineData("score main { staff m as ▮ }", "lines")]          // a selector MUST follow `as`
    [InlineData("score main { staff m as lines ▮ }", "1")]        // the line count
    [InlineData("score main { staff m as removeEmpty ▮ }", "true")]
    public void TheValueSlotsInsideTheClauseAreUnchanged(string row, string expected)
    {
        var labels = LabelsAt(OnePart, row);
        Assert.Contains(expected, labels);
        Assert.DoesNotContain("staff", labels);   // no row may start in a value slot
    }

    [Fact]
    public void TheChainTheCompilerAccepts_IsWhatTheItemsSpell()
    {
        // The net under the two items above: the spelling they build parses and validates.
        string book = OnePart + "score main { staff melody as lines 1 removeEmpty all }\n";
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors,
            "the chained selectors do not parse: "
            + string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        Assert.Empty(LilySharp.Core.Semantics.SemanticValidation.Run(tree)
            .Where(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error));
    }

    // ===== the row's other optional clauses =====

    [Theory]
    [InlineData("score main { staff ~m ▮ }")]                       // the label suppressor
    [InlineData("score main { staff m \"Violin I\" ▮ }")]           // the display name
    [InlineData("score main { staff ~treble m \"Violin I\" ▮ }")]   // all of them at once
    public void EveryOptionalClauseKeepsTheRowsSelectors(string row)
    {
        var labels = LabelsAt(OnePart, row);
        Assert.Contains("as lines", labels);
        Assert.Contains("as removeEmpty", labels);
    }

    [Theory]
    [InlineData("score main { chords prog as roman ▮ }")]
    [InlineData("score main { tab m as numbers ▮ }")]
    public void ARowWhoseSelectorDoesNotChain_EndsThere(string row)
    {
        // Control on the other rows: `ConsumeAsSelector` takes exactly ONE word, so there is
        // no chain to offer — the score's continuations are the whole answer.
        string doc = "part m { section A { c'4 } }\nchords prog { section A { C } }\nform main { A }\n";
        var labels = LabelsAt(doc, row);
        Assert.Contains("staff", labels);
        Assert.DoesNotContain("numbers", labels);
        Assert.DoesNotContain("roman", labels);
        Assert.DoesNotContain("lines", labels);
    }

    [Fact]
    public void ABarePartNameAfterAStaffRow_IsItsOwnRow()
    {
        // The row reader has to END the staff row at a word the grammar cannot attach:
        // `staff m  n` is a staff row AND a MIDI-only part row, so the caret after `n` is
        // not inside the staff row and must not be offered its selectors.
        var labels = LabelsAt(OnePart + "part n { section A { c4 } }\n", "score main { staff m  n ▮ }");
        Assert.DoesNotContain("as lines", labels);
        Assert.Contains("staff", labels);
    }

    [Fact]
    public void TheClefListItselfIsStillOfferedBeforeAName()
    {
        // Control: at `staff ▮` both the parts and the clefs that may precede one belong —
        // the row this whole test file is about starts here.
        var labels = LabelsAt(OnePart, "score main { staff ▮ }");
        Assert.Contains("melody", labels);
        Assert.Contains("treble", labels);
        Assert.DoesNotContain("as lines", labels);
    }
}
