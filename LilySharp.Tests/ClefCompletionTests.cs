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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Completion right after the <c>clef</c> keyword offers only clef names.
/// </summary>
[Trait("Category", "Unit")]
public class ClefCompletionTests
{
    [Theory]
    [InlineData("part m { clef ", "clef")]   // after 'clef ' (no partial word)
    [InlineData("part m { clef tr", "clef")] // mid clef name
    [InlineData("section A { c4 d ", "d")]    // not after clef
    public void WordBeforeCursor_FindsThePrecedingWord(string text, string expected)
    {
        Assert.Equal(expected, LilySharpLanguageServer.WordBeforeCursor(text, text.Length));
    }

    [Fact]
    public void ClefCompletions_AreOnlyClefNames()
    {
        var labels = LilySharpLanguageServer.GetClefCompletions().Items
            .Select(i => i.Label).ToHashSet();

        Assert.Equal(
            new[] { "alto", "bass", "tenor", "treble", "treble_8" }.ToHashSet(),
            labels);

        // No note names leak in.
        foreach (var note in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(note, labels);
    }

    [Fact]
    public void ClefCompletions_AreOrderedHighToLow()
    {
        // Sorted by SortText (what the editor shows), not alphabetically.
        var ordered = LilySharpLanguageServer.GetClefCompletions().Items
            .OrderBy(i => i.SortText, System.StringComparer.Ordinal)
            .Select(i => i.Label)
            .ToArray();

        Assert.Equal(new[] { "treble", "treble_8", "alto", "tenor", "bass" }, ordered);
    }
}
