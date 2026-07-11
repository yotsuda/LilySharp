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
/// Right after a complete articulation name (or its '.'), Ctrl+Space offers the
/// '.up'/'.down' placement qualifier — '@fermata' → '@fermata.up'. A partial name,
/// an argument-taking annotation (@finger), or a trailing space must NOT.
/// </summary>
[Trait("Category", "Unit")]
public class ArticulationPlacementCompletionTests
{
    private static string Ctx(string text) =>
        LilySharpLanguageServer.GetCompletionContext(text, text.Length).ToString();

    [Theory]
    [InlineData("c4@fermata")]    // right after a complete name
    [InlineData("c4@fermata.")]   // after the dot
    [InlineData("c4@fermata.d")]  // partial 'down'
    [InlineData("c8@accent")]
    [InlineData("c8@staccato.u")]
    public void AfterCompleteArticulation_OffersPlacement(string text) =>
        Assert.Equal("AfterArticulationPlacement", Ctx(text));

    [Theory]
    [InlineData("c4@ferm")]       // partial name → keep the '@' list
    [InlineData("c4@finger")]     // arg-taking, not a placement articulation
    [InlineData("c4@fermata ")]   // trailing space → the user moved on
    public void PartialOrArgOrSpaced_NotPlacement(string text) =>
        Assert.NotEqual("AfterArticulationPlacement", Ctx(text));

    [Fact]
    public void NoDot_OffersDottedUpAndDown()
    {
        var labels = LilySharpLanguageServer
            .GetArticulationPlacementCompletions("c4@fermata", 10)
            .Items.Select(i => i.Label).ToList();
        Assert.Contains(".up", labels);
        Assert.Contains(".down", labels);
    }

    [Fact]
    public void AfterDot_OffersBareUpAndDown()
    {
        const string text = "c4@fermata.";
        var labels = LilySharpLanguageServer
            .GetArticulationPlacementCompletions(text, text.Length)
            .Items.Select(i => i.Label).ToList();
        Assert.Contains("up", labels);
        Assert.Contains("down", labels);
    }
}
