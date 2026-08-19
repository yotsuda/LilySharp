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
/// Completing the `sings` binding (`lyrics ja sings vocal { }` — the track
/// binds to its melody at the DEFINITION, user decision 2026-08-19): after
/// `lyrics NAME` the editor offers the keyword; after `sings` it offers the
/// declared parts and named voices. The guards matter as much as the offers —
/// a lyric BODY holds free English text, so the words `lyrics` and `sings`
/// inside one must never re-open code completion.
/// </summary>
[Trait("Category", "Unit")]
public class SingsCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("lyrics verse ")]                                    // track-major, top level
    [InlineData("lyrics verse si")]                                  // typing the keyword
    [InlineData("section A { melody { c4 d } lyrics words ")]        // section-major cell
    public void AfterTheTrackName_OffersTheBindingKeyword(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLyricsTrackName, Ctx(text));

    [Theory]
    [InlineData("lyrics verse sings ")]
    [InlineData("lyrics verse sings vo")]                            // typing the target
    [InlineData("section A { melody { c4 d } lyrics words sings ")]
    public void AfterSings_OffersTheBindingTargets(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterSingsTarget, Ctx(text));

    [Theory]
    [InlineData("lyrics verse { ")]                                  // the body, not the header
    [InlineData("score main { staff m  lyrics w ")]                  // a score ROW, not a definition
    public void NeitherTheBodyNorAScoreRow_OffersTheKeyword(string text)
        => Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterLyricsTrackName, Ctx(text));

    /// <summary>An English syllable "sings" inside a body (`he sings ▮`) must not
    /// re-open part-name completion — the guard is the word TWO back being
    /// `lyrics`, which a body's brace has already emptied.</summary>
    [Fact]
    public void ASyllableSpelledSings_DoesNotOfferBindingTargets()
        => Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterSingsTarget,
            Ctx("lyrics verse sings melody { section A { he sings "));

    [Fact]
    public void TrackNameCompletions_ContainSings()
        => Assert.Contains(LilySharpLanguageServer.GetLyricsTrackNameCompletions().Items,
            i => i.Label == "sings");

    [Fact]
    public void SingsTargets_AreTheDeclaredPartsAndNamedVoices()
    {
        var labels = LilySharpLanguageServer.GetVoiceBindingNameCompletions(
                "part vocal { }\npart bass { }\nsection A { vocal { voice sop { c4 } } }",
                "detail")
            .Items.Select(i => i.Label).ToArray();
        Assert.Contains("vocal", labels);
        Assert.Contains("bass", labels);
        Assert.Contains("sop", labels);
    }
}
