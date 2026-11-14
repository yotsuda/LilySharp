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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The bar under a caret, found in the other parts and chords rows that write its section
/// (<see cref="SameBarFinder"/>; owner's request 2026-11-14): the place each opens the same bar
/// at, counted from the bar lines it writes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SameBarFinderTests
{
    private const string Book =
        "octave absolute\ntime 4/4\n"
        + "part m { section S { c''1 | d''1 | e''1 | } section T { f''1 | } }\n"
        + "part b { section S { c1 | d1 | e1 | } section T { f1 | } }\n"
        + "chords prog { section S { C | G | Am | } }\n"
        + "form { S T }\nscore { chords prog staff m staff b }\n";

    private static string After(int offset) => Book.Substring(offset).Split(' ')[0];

    [Fact]
    public void TheSameBarOfEveryOtherVoiceOfTheSection()
    {
        var answer = SameBarFinder.At(SyntaxTree.Parse(Book), Book.IndexOf("d''1"))!;
        Assert.Equal(("S", 2, "part 'm'"), (answer.Section, answer.Bar, answer.Voice));
        // The other voices, in document order from the caret's own: the bass, then the row.
        Assert.Equal(new[] { ("part 'b'", "d1"), ("chords 'prog'", "G") },
            answer.Others.Select(o => (o.Voice, After(o.Offset))).ToArray());
    }

    [Fact]
    public void TheFirstBarOpensAfterTheVoicesBrace_AndTheSectionIsTheCaretsOwn()
    {
        var answer = SameBarFinder.At(SyntaxTree.Parse(Book), Book.IndexOf("f1"))!;
        Assert.Equal(("T", 1, "part 'b'"), (answer.Section, answer.Bar, answer.Voice));
        var other = Assert.Single(answer.Others);       // the row writes no section T
        Assert.Equal(("part 'm'", "f''1"), (other.Voice, After(other.Offset)));
    }

    [Fact]
    public void FromARowToTheParts()
    {
        var answer = SameBarFinder.At(SyntaxTree.Parse(Book), Book.IndexOf("Am"))!;
        Assert.Equal(("S", 3, "chords 'prog'"), (answer.Section, answer.Bar, answer.Voice));
        Assert.Equal(new[] { "e''1", "e1" }, answer.Others.Select(o => After(o.Offset)).ToArray());
    }

    [Fact]
    public void ACaretOutsideEverySectionHasNoBar()
        => Assert.Null(SameBarFinder.At(SyntaxTree.Parse(Book), Book.IndexOf("form")));
}