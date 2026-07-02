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
/// Completion inside a part { } header offers the part PROPERTY names (a part
/// body holds properties and inner sections, never notes), and completion
/// right after <c>removeEmpty</c> offers its values.
/// </summary>
[Trait("Category", "Unit")]
public class PartPropertyCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext ContextOf(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("part m { ")]              // bare part header
    [InlineData("part m { clef bass ")]    // after a completed property pair
    public void InsideAPartHeader_OffersPartProperties(string text)
    {
        Assert.Equal(LilySharpLanguageServer.CompletionContext.PartBlock, ContextOf(text));
    }

    [Theory]
    [InlineData("part m { removeEmpty ")]
    [InlineData("part m { removeEmpty tr")]
    [InlineData("part m { clef bass removeEmpty a")]
    public void AfterRemoveEmpty_OffersItsValues(string text)
    {
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterRemoveEmpty, ContextOf(text));
    }

    [Theory]
    [InlineData("part m { clef ", "AfterClef")]
    [InlineData("part m { instrument ", "AfterInstrument")]
    [InlineData("section S { m { ", "MusicBlock")] // part REFERENCE, not a header
    public void ValueContexts_TakePriorityOverThePropertyList(string text, string expected)
    {
        Assert.Equal(expected, ContextOf(text).ToString());
    }

    [Fact]
    public void PartPropertyCompletions_IncludeRemoveEmpty_AndNoNotes()
    {
        var labels = LilySharpLanguageServer.GetPartPropertyCompletions().Items
            .Select(i => i.Label).ToArray();

        Assert.Contains("removeEmpty", labels);
        Assert.Contains("clef", labels);
        Assert.Contains("instrument", labels);
        // `key` is NOT a parseable part property (ParsePartProperty accepts
        // time/tempo plus the identifier pairs) — offering it would insert
        // unparseable text.
        Assert.DoesNotContain("key", labels);
        foreach (var note in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(note, labels);
    }

    [Fact]
    public void RemoveEmptyCompletions_AreExactlyTheAcceptedValues()
    {
        var labels = LilySharpLanguageServer.GetRemoveEmptyCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "true", "all", "false" }, labels);
    }
}
