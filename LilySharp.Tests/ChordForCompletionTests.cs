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
/// The editor offers a chord track's <c>for PART</c> (owner's decision 2026-10-28,
/// Semantics.ChordTrackPitch): after the track's name at its definition and on a score row, then
/// the parts after <c>for</c>; and a <c>for</c> before the brace leaves the body a chord body.
/// </summary>
[Trait("Category", "Unit")]
public class ChordForCompletionTests
{
    private const string Head = "part tp { instrument trumpet }\n";

    [Theory]
    [InlineData("chords tpc ", "AfterChordsTrackName")]
    [InlineData("chords tpc for ", "AfterChordForTarget")]
    [InlineData("chords for ", "AfterChordForTarget")]
    [InlineData("score { chords prog for ", "AfterChordForTarget")]
    [InlineData("score { chords prog ", "AfterChordAttachName")]
    public void TheForPair_IsOffered(string tail, string expected)
    {
        string text = Head + tail;
        Assert.Equal(expected, LilySharpLanguageServer.GetCompletionContext(text, text.Length).ToString());
    }

    [Fact]
    public void TheRowsList_CarriesFor_WhichReopensOntoTheParts()
    {
        var item = LilySharpLanguageServer.GetChordAttachNameCompletions().Items.Single(i => i.Label == "for");
        Assert.Equal("editor.action.triggerSuggest", item.Command!.CommandIdentifier);
        Assert.Single(LilySharpLanguageServer.GetChordsTrackNameCompletions().Items, i => i.Label == "for");
    }

    [Theory]
    [InlineData("chords tpc for tp { ")]
    [InlineData("chords for tp { ")]
    public void ABodyAfterFor_IsStillAChordBody(string tail)
    {
        string text = Head + tail;
        Assert.True(LilySharpLanguageServer.IsInsideChordsBlock(text, text.Length));
    }
}
