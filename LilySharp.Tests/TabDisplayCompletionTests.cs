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
/// Completing the tab style selector. <c>tab … as</c> takes <c>numbers | full</c>,
/// which the completer must distinguish from the chord display selector
/// (<c>roman | both | names</c>) that shares the <c>as</c> keyword.
/// </summary>
[Trait("Category", "Unit")]
public class TabDisplayCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("score main { tab melody as ")]
    [InlineData("score main { tab drop-d melody as ")]   // an explicit tuning precedes the part
    [InlineData("score main { tab melody as nu")]        // partial word keeps the list
    [InlineData("score main { staff melody  tab bass as ")] // a staff item ahead of the tab
    public void AfterTabAs_OffersTheTabStyles(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterTabDisplayAs, Ctx(text));

    [Theory]
    [InlineData("score main { tab melody with chords harmony as ")] // chord attach on a tab
    [InlineData("score main { chords harmony as ")]
    [InlineData("score main { staff melody with chords harmony as ")]
    public void AfterChordsAs_StillOffersChordModes_EvenOnATabLine(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterChordDisplayAs, Ctx(text));

    [Fact]
    public void TabDisplayModeCompletions_AreNumbersAndFull()
    {
        var labels = LilySharpLanguageServer.GetTabDisplayModeCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "numbers", "full" }, labels);
    }
}
