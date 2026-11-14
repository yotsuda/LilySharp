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
using LilySharp.Core.Editing;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The editor's command that writes a section's repeat signs in every part that writes the
/// section, and the one that takes them back to one (<see cref="RepeatSignSpreader"/>; owner's
/// design 2026-11-14, docs/inline-repeat-design.md §8).
/// </summary>
[Trait("Category", "Unit")]
public sealed class RepeatSignSpreaderTests
{
    private static string Book(string m, string b, string row = "", string extra = "") =>
        "octave absolute\ntime 4/4\n" + extra
        + "part m { section S { " + m + " } }\n"
        + "part b { section S { " + b + " } }\n"
        + (row.Length > 0 ? "chords prog { section S { " + row + " } }\n" : "")
        + "form { S }\nscore { staff m staff b }\n";

    private static string Applied(string text, RepeatSignSpreader.Result result)
    {
        foreach (var edit in result.Edits.OrderByDescending(e => e.Start))
            text = text.Substring(0, edit.Start) + edit.NewText + text.Substring(edit.End);
        return text;
    }

    [Fact]
    public void ASignGoesWhereTheOthersWriteAPlainLine()
    {
        string book = Book("c''1 |: d''1 :| e''1 |", "c1 | d1 | e1 |", row: "C | G | Am |");
        var result = RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(book));
        string after = Applied(book, result);

        Assert.Equal(Book("c''1 |: d''1 :| e''1 |", "c1 |: d1 :| e1 |", row: "C |: G :| Am |"), after);
        Assert.Equal(4, result.Count);
        Assert.Empty(result.Skipped);
        // The book is as it was read, and nothing is left to write.
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(after)), d => d.Code == DiagnosticCodes.RepeatSignsDisagree);
        Assert.Empty(RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(after)).Edits);
    }

    [Fact]
    public void ASignThatOpensTheSectionIsWrittenAtTheOthersHead_WithItsCount()
    {
        string book = Book("|: c''1 :|*3 d''1 |", "c1 | d1 |");
        string after = Applied(book, RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(book)));
        Assert.Equal(Book("|: c''1 :|*3 d''1 |", "|: c1 :|*3 d1 |"), after);
    }

    [Fact]
    public void AVoiceWithNoBarLineOfItsOwnThereIsNamed_AndLeft()
    {
        string book = Book("c''1 |: d''1 :| e''1 |", "bass", extra: "phrase bass { c1 | d1 | e1 | }\n");
        var result = RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(book));
        Assert.Empty(result.Edits);
        Assert.Equal(2, result.Skipped.Count);
        Assert.All(result.Skipped, s => Assert.Contains("part 'b', section 'S'", s));
        Assert.Contains("no bar line of its own", result.Skipped[0]);
    }

    [Fact]
    public void SignsThatDisagreeAreLeftToTheWriter()
    {
        string book = Book("c''1 |: d''1 | e''1 |", "c1 :| d1 | e1 |", row: "C | G | Am |");
        var result = RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(book));
        Assert.Empty(result.Edits);
    }

    /// <summary>The endings and the navigation marks go with the bar lines (session 1254): an
    /// ending around the same bars — its <c>]</c> only where no <c>:|</c> closes it — and a mark
    /// at the same bar line, a text before the line and a sign after it. Written out, the book
    /// reads the same and nothing is left to write; taken back, it is the book it was.</summary>
    [Theory]
    // endings, closed by the ':|' and by a ']'
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 ] | f''1 |", "c1 | d1 | e1 | f1 |", "|: c1 | [1. d1 :| [2. e1 ] | f1 |",
        "C | G | Am | F |", "|: C | [1. G :| [2. Am ] | F |")]
    // an ending of two bars, and passes as a list
    [InlineData("|: c''1 | [1,2. d''1 | e''1 :| [3. f''1 | g''1 ] |", "c1 | d1 | e1 | f1 | g1 |", "|: c1 | [1,2. d1 | e1 :| [3. f1 | g1 ] |",
        "C | G | Am | F | C |", "|: C | [1,2. G | Am :| [3. F | C ] |")]
    // marks: a sign opens its bar, a text closes its own
    [InlineData("c''1 | segno d''1 | e''1 fine | f''1 ds al fine |", "c1 | d1 | e1 | f1 |", "c1 | segno d1 | e1 fine | f1 ds al fine |",
        "C | G | Am | F |", "C | segno G | Am fine | F ds al fine |")]
    // a sign on the section's first bar
    [InlineData("segno c''1 | d''1 ds |", "c1 | d1 |", "segno c1 | d1 ds |", "C | G |", "segno C | G ds |")]
    public void EndingsAndMarksAreWrittenInEveryPart_AndTakenBack(string m, string b, string spreadB, string row, string spreadRow)
    {
        string book = Book(m, b, row: row);
        var result = RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(book));
        string after = Applied(book, result);

        Assert.Equal(Book(m, spreadB, row: spreadRow), after);
        Assert.Empty(result.Skipped);
        // No two edits touch one place.
        var ordered = result.Edits.OrderBy(e => e.Start).ToList();
        for (int i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i - 1].End <= ordered[i].Start && ordered[i - 1].Start < ordered[i].Start);

        // The book reads as it did: no error of its own, the same signs on the column.
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(after)), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(Column(book), Column(after));
        Assert.Empty(RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(after)).Edits);

        // …and taken back to one part it is the book it was.
        Assert.Equal(book, Applied(after, RepeatSignSpreader.KeepInOnePart(SyntaxTree.Parse(after))));
    }

    private static string Column(string book)
    {
        var root = SyntaxTree.Parse(book).GetRoot();
        var stream = LilySharp.Core.Semantics.RepeatStream.Of(root, root.DescendantNodes<FormDeclarationSyntax>().First());
        return string.Join(" ", stream.Signs.Select(s => $"{s.At}:{s.Kind}{s.Count}{s.Passes}{s.Navigation}"))
            + " / " + string.Join(" ", stream.Played.Select(p => $"{p.From}-{p.To}"));
    }

    [Fact]
    public void AnEndingWhereAVoiceHasNoBarLineOfItsOwn_IsNamed_AndLeft()
    {
        string book = Book("|: c''1 | [1. d''1 :| [2. e''1 ] |", "bass", extra: "phrase bass { c1 | d1 | e1 | }\n");
        var result = RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(book));
        // The '|:' that opens the section is written at the voice's head, as before; no ending is.
        Assert.DoesNotContain(result.Edits, e => e.NewText.Contains('[') || e.NewText.Contains(']'));
        Assert.Contains(result.Skipped, s => s.Contains("the ending '[1.'") && s.Contains("no bar line of its own"));
        Assert.Contains(result.Skipped, s => s.Contains("the ending '[2.'"));
    }

    [Theory]
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 ] |", "|: c1 | [2. d1 :| [1. e1 ] |")]  // endings that disagree
    [InlineData("c''1 | d''1 fine |", "c1 | d1 ds |")]                                    // marks that disagree
    public void EndingsAndMarksThatDisagreeAreLeftToTheWriter(string m, string b)
    {
        string book = Book(m, b);
        Assert.Empty(RepeatSignSpreader.WriteInEveryPart(SyntaxTree.Parse(book)).Edits);
        // Taken back, the bar lines that agree go; the endings and the marks stay.
        string kept = Applied(book, RepeatSignSpreader.KeepInOnePart(SyntaxTree.Parse(book)));
        foreach (string sign in new[] { "[1.", "[2.", "]", "fine", "ds" })
            Assert.Equal(book.Split(sign).Length, kept.Split(sign).Length);
    }

    /// <summary>A sign that opens the section stands on its first bar line. MEASURED before
    /// (Lab sessions/p1254/probe.txt): <c>SectionSigns</c> read the section's name as music
    /// standing before the mark, so the segno was one bar late on the column and a <c>ds</c>
    /// went back to the second bar.</summary>
    [Fact]
    public void ASignThatOpensTheSectionStandsOnItsFirstBarLine()
        => Assert.Equal("0:NavigationSegno 2:NavigationDalSegno / 0-2 0-2", Column(Book("segno c''1 | d''1 ds |", "c1 | d1 |")));

    /// <summary>LYS1070 on an ending or a navigation mark has its ways out too
    /// (<see cref="RepeatSignFixes"/>, session 1254): the other part's here, this one there, or
    /// none here. Each leaves a book that no longer reports it.</summary>
    [Theory]
    // endings whose passes differ
    [InlineData("|: c''1 | [1. d''1 :| e''1 |", "|: c1 | [2. d1 :| e1 |", 0, "Write '[1.' here, as part 'm' does", "|: c''1 | [1. d''1 :| e''1 |", "|: c1 | [1. d1 :| e1 |")]
    [InlineData("|: c''1 | [1. d''1 :| e''1 |", "|: c1 | [2. d1 :| e1 |", 1, "Write '[2.' in part 'm' too", "|: c''1 | [2. d''1 :| e''1 |", "|: c1 | [2. d1 :| e1 |")]
    [InlineData("|: c''1 | [1. d''1 :| e''1 |", "|: c1 | [2. d1 :| e1 |", 2, "Leave the ending out here - it follows the '[1.' of part 'm'", "|: c''1 | [1. d''1 :| e''1 |", "|: c1 | d1 :| e1 |")]
    // an ending that runs a different length: only its brackets can go
    [InlineData("|: c''1 :| [2. d''1 ] | e''1 |", "|: c1 :| [2. d1 | e1 ] |", 0, "Leave the ending out here - it follows the '[2.' of part 'm'", "|: c''1 :| [2. d''1 ] | e''1 |", "|: c1 :| d1 | e1 |")]
    // marks
    [InlineData("c''1 | d''1 fine |", "c1 | d1 ds |", 0, "Write 'fine' here, as part 'm' does", "c''1 | d''1 fine |", "c1 | d1 fine |")]
    [InlineData("c''1 | d''1 fine |", "c1 | d1 ds |", 1, "Write 'ds' in part 'm' too", "c''1 | d''1 ds |", "c1 | d1 ds |")]
    [InlineData("c''1 | d''1 fine |", "c1 | d1 ds |", 2, "Leave 'ds' out here - it follows the mark of part 'm'", "c''1 | d''1 fine |", "c1 | d1 |")]
    public void AnEndingOrAMarkThatDisagreesHasItsWaysOut(string m, string b, int which, string title, string fixedM, string fixedB)
    {
        string book = Book(m, b);
        var error = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(book)), d => d.Code == DiagnosticCodes.RepeatSignsDisagree);
        var fix = RepeatSignFixes.For(SyntaxTree.Parse(book), error.Span)[which];
        Assert.Equal(title, fix.Title);
        string after = book.Substring(0, fix.Start) + fix.NewText + book.Substring(fix.End);
        Assert.Equal(Book(fixedM, fixedB), after);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(after)), d => d.Code == DiagnosticCodes.RepeatSignsDisagree);
    }

    [Fact]
    public void TakenBackToOnePart_APlainLineStands_AndAnOpeningSignGoes()
    {
        string book = Book("|: c''1 | d''1 :| e''1 |", "|: c1 | d1 :| e1 |", row: "|: C | G :| Am |");
        var result = RepeatSignSpreader.KeepInOnePart(SyntaxTree.Parse(book));
        Assert.Equal(Book("|: c''1 | d''1 :| e''1 |", "c1 | d1 | e1 |", row: "C | G | Am |"), Applied(book, result));
        Assert.Equal(4, result.Count);
    }
}