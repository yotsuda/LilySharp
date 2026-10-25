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

using LilySharp.Core.Editing;
using Xunit;

namespace LilySharp.Tests;

/// <summary>Remove All String Numbers (the owner's request, 2026-10-25).</summary>
[Trait("Category", "Unit")]
public class StringNumberRemoverTests
{
    private static string Book(string music) => $$"""
        part b { instrument bass }
        section A {
          b { {{music}} }
        }
        form { A }
        score { tab b }
        """;

    [Theory]
    [InlineData(@"c,2 g,\2 | c,4\3( d,) <e,\4 b,\3>2 |", @"c,2 g, | c,4( d,) <e, b,>2 |", 4)]
    [InlineData(@"a'4\4@staccato b | g,8\2~ g, |", @"a'4@staccato b | g,8~ g, |", 2)]
    [InlineData(@"a' \2 b \3 | c |", @"a' b | c |", 2)]          // a mark standing apart takes its space
    [InlineData(@"a' \2| c |", @"a' | c |", 1)]                 // …but a bar line keeps one
    [InlineData(@"grace { a,16\2 } b,8 r4 r2 |", @"grace { a,16 } b,8 r4 r2 |", 1)]
    public void EveryWrittenStringGoes(string before, string after, int removed)
    {
        var result = StringNumberRemover.Remove(Book(before));
        Assert.Equal(Book(after), result.NewText);
        Assert.Equal(removed, result.Removed);
    }

    [Fact]
    public void AMarkInACommentOrAStringStays()
    {
        string source = "title \"Bass \\2\"\n" + Book(@"c,2\3 g, | // play g\2 here") ;
        var result = StringNumberRemover.Remove(source);
        Assert.Equal("title \"Bass \\2\"\n" + Book(@"c,2 g, | // play g\2 here"), result.NewText);
        Assert.Equal(1, result.Removed);
    }

    [Fact]
    public void AFileWithoutMarks_IsUnchanged()
    {
        string source = Book("c,4 d, e, f, |");
        var result = StringNumberRemover.Remove(source);
        Assert.Equal(source, result.NewText);
        Assert.Equal(0, result.Removed);
    }
}
