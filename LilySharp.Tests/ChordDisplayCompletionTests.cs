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
/// Completing the chord display selector: after a chord attachment name the editor
/// offers <c>as roman | as both | as names</c>; after <c>as</c> it offers the modes.
/// </summary>
[Trait("Category", "Unit")]
public class ChordDisplayCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("score main { staff melody with chords harmony ")]
    [InlineData("score main { chords harmony ")]
    public void AfterChordName_OffersTheAsSelector(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterChordAttachName, Ctx(text));

    [Theory]
    [InlineData("score main { staff melody with chords harmony as ")]
    [InlineData("score main { chords harmony as ")]
    public void AfterAs_OffersTheModes(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterChordDisplayAs, Ctx(text));

    [Fact]
    public void CompletingTheName_StillOffersDeclaredNames()
    {
        // `with chords |` (before the name) keeps completing the chord-part names.
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterChordsRef,
            Ctx("score main { staff melody with chords "));
    }

    [Fact]
    public void ChordAttachCompletions_ContainAsSelectorAndContinuations()
    {
        var items = LilySharpLanguageServer.GetChordAttachNameCompletions().Items;
        Assert.Contains(items, i => i.Label == "as roman");
        Assert.Contains(items, i => i.Label == "as both");
        Assert.Contains(items, i => i.Label == "as names");
        // A following render item is not blocked.
        Assert.Contains(items, i => i.Label == "staff");
    }

    [Fact]
    public void ScoreBlockCompletions_StaffAndTab_RetriggerPartNameSuggestions()
    {
        var items = LilySharpLanguageServer.GetScoreBlockCompletions().Items;
        // `tab` is offered alongside `staff`.
        Assert.Contains(items, i => i.Label == "tab");
        // Both staff and tab re-open the popup to list the declared parts.
        foreach (var label in new[] { "staff", "tab" })
        {
            var item = items.Single(i => i.Label == label);
            Assert.Equal("editor.action.triggerSuggest", item.Command?.CommandIdentifier);
        }
        // grandStaff opens a brace block, so it does NOT retrigger.
        Assert.Null(items.Single(i => i.Label == "grandStaff").Command);
    }

    [Fact]
    public void DisplayModeCompletions_AreTheThreeModes()
    {
        var labels = LilySharpLanguageServer.GetChordDisplayModeCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "roman", "both", "names" }, labels);
    }

    [Fact]
    public void TopLevelClef_InsertsAChoiceSnippetSoTheClefValuesShowAtOnce()
    {
        // Completing `clef` inserts `clef ` + a ${1|…|} choice, so the space is added and
        // the clef values show immediately (no extra keystroke / re-trigger).
        var clef = LilySharpLanguageServer.GetTopLevelCompletions().Items.Single(i => i.Label == "clef");
        Assert.StartsWith("clef ${1|", clef.InsertText);
        Assert.Contains("treble", clef.InsertText);
        Assert.Contains("bass", clef.InsertText);
    }
}
