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
/// The verses of a section whose own music repeats are the passes of that repeat, one verse a
/// pass (owner's rules 2026-11-14, docs/inline-repeat-design.md §6). A book that writes them
/// otherwise still plays — LYS4080 is a warning that says how it is sung — and each warning
/// has its way out as an edit (<see cref="VerseRepeatFixes"/>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class VerseRepeatValidatorTests
{
    private static string Book(string music, string verses) =>
        "octave absolute\ntime 4/4\n"
        + "part m { section S { " + music + " } }\n"
        + "lyrics w {\n  section S {\n" + verses + "\n  }\n}\n"
        + "form { S }\nscore { staff m  lyrics w on m }\n";

    private static Diagnostic[] Warnings(string book) =>
        [.. SemanticValidation.Run(SyntaxTree.Parse(book)).Where(d => d.Code == DiagnosticCodes.VerseAgainstRepeat)];

    private static string Fixed(string book, Diagnostic warning, int which)
    {
        var fix = VerseRepeatFixes.For(SyntaxTree.Parse(book), warning.Span)[which];
        return book.Substring(0, fix.Start) + fix.NewText + book.Substring(fix.End);
    }

    [Theory]
    [InlineData("|: c''1 :|", "    verse 1 { one | }\n    verse 2 { two | }")]      // a verse a pass
    [InlineData("|: c''1 :|*3", "    verse 1 { one | }\n    verse 2-3 { two | }")]  // a range covers two
    [InlineData("|: c''1 :|", "    la |")]                                           // the same words every pass
    [InlineData("c''1 |", "    verse 1 { one | }\n    verse 2 { two | }\n    verse 3 { three | }")] // no repeat of its own: the form's plays
    public void VersesThatFitAreToldNothing(string music, string verses)
        => Assert.Empty(Warnings(Book(music, verses)));

    [Fact]
    public void APassNoVerseNames_IsAWarning_AndTheVerseCanBeAdded()
    {
        string book = Book("|: c''1 | d''1 :|", "    verse 1 { one | uno | }");
        var warning = Assert.Single(Warnings(book));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("verse 1", book.Substring(warning.Span.Start, warning.Span.Length));
        Assert.Contains("no verse here is for pass 2", warning.Message);

        Assert.Equal(Book("|: c''1 | d''1 :|", "    verse 1 { one | uno | }\n    verse 2 { | | }"), Fixed(book, warning, 0));
        Assert.Equal(Book("|: c''1 | d''1 :|", "    verse 1 { one | uno | }\n    verse 2 { one | uno | }"), Fixed(book, warning, 1));
        Assert.Empty(Warnings(Fixed(book, warning, 0)));
    }

    [Fact]
    public void AVerseForAPassTheRepeatDoesNotHave_IsAWarning_AndCanBeDeleted()
    {
        string book = Book("|: c''1 :|", "    verse 1 { one | }\n    verse 2 { two | }\n    verse 3 { three | }");
        var warning = Assert.Single(Warnings(book));
        Assert.Equal("verse 3", book.Substring(warning.Span.Start, warning.Span.Length));
        Assert.Contains("never sung", warning.Message);
        Assert.Equal(Book("|: c''1 :|", "    verse 1 { one | }\n    verse 2 { two | }"), Fixed(book, warning, 0));
    }

    [Fact]
    public void APassTwoVersesName_IsAWarning_AndTheSecondCanTakeTheFreePass()
    {
        string book = Book("|: c''1 :|", "    verse 1 { one | }\n    verse 1 { two | }");
        var warnings = Warnings(book);
        // The second verse names pass 1 again, and so pass 2 has none.
        Assert.Equal(2, warnings.Length);
        var twice = warnings.Single(w => w.Message.Contains("two verses"));
        string after = Fixed(book, twice, 0);
        Assert.Equal(Book("|: c''1 :|", "    verse 1 { one | }\n    verse 2 { two | }"), after);
        Assert.Empty(Warnings(after));
    }

    /// <summary>Rule 3: a verse writes words only in the bars its pass plays. Words elsewhere
    /// are printed and not sung (<c>InlineRepeatMidiTests.AVerseSingsInTheBarsItsPassPlays</c>
    /// hears that), and the warning stands on the words.</summary>
    [Theory]
    // a first ending is pass 1's
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 | ]", "    verse 1 { one | first | }\n    verse 2 { two | lost | second | }", "lost", "verse 2", "bar 2", "pass 1")]
    // a second ending is pass 2's
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 | ]", "    verse 1 { one | first | lost | }\n    verse 2 { two | | second | }", "lost", "verse 1", "bar 3", "pass 2")]
    // a bar after the repeat is played once
    [InlineData("|: c''1 :| d''1 |", "    verse 1 { one | after | }\n    verse 2 { two | lost gone | }", "lost gone", "verse 2", "bar 2", "pass 1")]
    public void WordsInABarThePassDoesNotPlay_AreAWarningOnTheWords(string music, string verses, string words, string verse, string bar, string pass)
    {
        string book = Book(music, verses);
        var warning = Assert.Single(Warnings(book));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(words, book.Substring(warning.Span.Start, warning.Span.Length));
        Assert.Contains("not sung", warning.Message);
        Assert.Contains($"'{verse}'", warning.Message);
        Assert.Contains(bar, warning.Message);
        Assert.Contains(pass, warning.Message);
        // The last way out empties the bar.
        var fixes = VerseRepeatFixes.For(SyntaxTree.Parse(book), warning.Span);
        Assert.Empty(Warnings(Fixed(book, warning, fixes.Count - 1)));
    }

    [Theory]
    [InlineData("    verse 1 { one | | lost | }\n    verse 2 { two | | | }", "    verse 1 { one | |  | }\n    verse 2 { two | | lost | }")]  // down to a later verse
    [InlineData("    verse 1 { one | | | }\n    verse 2 { two | lost | | }", "    verse 1 { one | lost | | }\n    verse 2 { two |  | | }")]  // up to an earlier one
    public void TheWordsCanBeMovedToTheVerseSungInThatBar(string verses, string moved)
    {
        const string music = "|: c''1 | [1. d''1 :| [2. e''1 | ]";
        string book = Book(music, verses);
        var warning = Assert.Single(Warnings(book));
        var fixes = VerseRepeatFixes.For(SyntaxTree.Parse(book), warning.Span);
        Assert.Equal(2, fixes.Count);
        Assert.Contains("Move these words", fixes[0].Title);
        Assert.Equal(Book(music, moved), Fixed(book, warning, 0));
        Assert.Empty(Warnings(Fixed(book, warning, 0)));
    }

    [Theory]
    // a third pass no verse names sings the last verse: its words under the third ending are sung
    [InlineData("|: c''1 | [1,2. d''1 :|*3 [3. e''1 | ]", "    verse 1 { one | first | | }\n    verse 2-3 { two | again | last | }")]
    // a verse of two passes under the ending of one of them
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 | ]", "    verse 1-2 { one | first | second | }")]
    // a blank and a held note are no words
    [InlineData("|: c''1 | [1. d''1 :| [2. e''1 | ]", "    verse 1 { one | first | _ | }\n    verse 2 { two | \"\" | second | }")]
    public void WordsInABarOneOfThePassesPlays_AreToldNothing(string music, string verses)
        => Assert.Empty(Warnings(Book(music, verses)));

    [Fact]
    public void AnEndingsNumberIsAPass()
    {
        // Three endings: three passes, and the verses stop at two.
        string book = Book("|: c''1 | [1. d''1 :| [2. e''1 :| [3. f''1 ] |", "    verse 1 { a | b | | | }\n    verse 2 { a | | c | | }");
        Assert.Contains("pass 3", Assert.Single(Warnings(book)).Message);
    }
}