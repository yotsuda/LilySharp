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
    public void DisplayModeCompletions_AreTheThreeModes()
    {
        var labels = LilySharpLanguageServer.GetChordDisplayModeCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "roman", "both", "names" }, labels);
    }
}
