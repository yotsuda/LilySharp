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
/// Inside a <c>chords { }</c> block the completion offers the key's diatonic chords,
/// the same set (and insert format) as inside a <c>@chord(…)</c> argument.
/// </summary>
[Trait("Category", "Unit")]
public class ChordEntryCompletionTests
{
    [Theory]
    [InlineData("chords harmony { ", true)]
    [InlineData("chords { ", true)]
    [InlineData("chords harmony { section A { ", true)]  // part-major inner section
    [InlineData("chords harmony { C | ", true)]
    [InlineData("part melody { section A { ", false)]    // music, not chords
    [InlineData("lyrics { section A { ", false)]
    [InlineData("section A { melody { ", false)]
    [InlineData("chords harmony ", false)]               // before the brace = the name
    [InlineData("", false)]
    public void IsInsideChordsBlock_DetectsChordEntryContexts(string text, bool expected)
        => Assert.Equal(expected, LilySharpLanguageServer.IsInsideChordsBlock(text, text.Length));

    [Fact]
    public void DiatonicCompletions_ForCMajor_OfferTheKeysChords()
    {
        var items = LilySharpLanguageServer.GetDiatonicChordCompletions("key c major\n", "key c major\n".Length);
        // C major diatonic triads/sevenths — the symbol is both label and insert.
        Assert.Contains(items.Items, i => i.Label == "Dm");   // ii
        Assert.Contains(items.Items, i => i.Label == "G7");   // V7
        Assert.Contains(items.Items, i => i.InsertText == "G7");
    }
}
