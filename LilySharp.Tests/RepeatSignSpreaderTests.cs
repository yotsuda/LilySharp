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

    [Fact]
    public void TakenBackToOnePart_APlainLineStands_AndAnOpeningSignGoes()
    {
        string book = Book("|: c''1 | d''1 :| e''1 |", "|: c1 | d1 :| e1 |", row: "|: C | G :| Am |");
        var result = RepeatSignSpreader.KeepInOnePart(SyntaxTree.Parse(book));
        Assert.Equal(Book("|: c''1 | d''1 :| e''1 |", "c1 | d1 | e1 |", row: "C | G | Am |"), Applied(book, result));
        Assert.Equal(4, result.Count);
    }
}