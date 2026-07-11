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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Lsp;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The <c>lilysharp.completion.flatSpelling</c> setting only changes how the flat
/// scale-note completions are SPELLED — E-flat and A-flat as ees/aes (full) or
/// es/as (contracted). Both compile regardless; this just picks the suggestion.
/// </summary>
[Trait("Category", "Unit")]
public class MusicCompletionFlatSpellingTests
{
    // 3 flats (Bb Eb Ab) — the E, A, B rows are flat.
    private const int ThreeFlats = -3;

    private static List<string> Labels(bool contracted) =>
        LilySharpLanguageServer.GetMusicCompletions("", ThreeFlats, contracted)
            .Items.Select(i => i.Label).ToList();

    // The same parse backs both initializationOptions (startup) and the live
    // didChangeConfiguration push, so a change takes effect without a reload.
    [Theory]
    [InlineData("{\"completion\":{\"flatSpelling\":\"contracted\"}}", true)]
    [InlineData("{\"completion\":{\"flatSpelling\":\"full\"}}", false)]
    [InlineData("{\"completion\":{}}", false)]
    [InlineData("{}", false)]
    public void ParseFlatSpellingContracted_ReadsPushedSetting(string json, bool expected) =>
        Assert.Equal(expected, LilySharpLanguageServer.ParseFlatSpellingContracted(JObject.Parse(json)));

    [Fact]
    public void ParseFlatSpellingContracted_NullSettings_DefaultsToFull() =>
        Assert.False(LilySharpLanguageServer.ParseFlatSpellingContracted(null));

    [Fact]
    public void Full_OffersFullDutchFlats()
    {
        var labels = Labels(contracted: false);
        Assert.Contains("ees", labels);
        Assert.Contains("aes", labels);
        Assert.DoesNotContain("es", labels);
        Assert.DoesNotContain("as", labels);
    }

    [Fact]
    public void Contracted_OffersContractedFlats_NotFull()
    {
        var labels = Labels(contracted: true);
        Assert.Contains("es", labels);
        Assert.Contains("as", labels);
        Assert.DoesNotContain("ees", labels);
        Assert.DoesNotContain("aes", labels);
    }

    [Fact]
    public void Contracted_LeavesOtherFlatsAlone()
    {
        // Only E-flat and A-flat have a Dutch contraction; B-flat stays "bes".
        Assert.Contains("bes", Labels(contracted: true));
    }

    [Fact]
    public void MusicCompletion_OffersEveryValidMusicKeyword()
    {
        // Everything the music-item parser accepts (Parser.Music.cs) should be
        // offered — break/octave/tempo/partial/voice were previously missing.
        var labels = LilySharpLanguageServer.GetMusicCompletions("", 0, false)
            .Items.Select(i => i.Label).ToList();
        foreach (var kw in new[] { "break", "octave", "tempo", "partial", "voice",
                                   "repeat", "tuplet", "grace", "acciaccatura", "appoggiatura",
                                   "clef", "key", "time", "override", "revert", "once" })
            Assert.Contains(kw, labels);
    }

    [Fact]
    public void MusicCompletion_InsideVoice_WithholdsNestedVoice()
    {
        // Nested voice blocks silently become siblings, so `voice` is withheld
        // once the cursor is already inside a voice; other keywords stay.
        var labels = LilySharpLanguageServer.GetMusicCompletions("", 0, false, insideVoice: true)
            .Items.Select(i => i.Label).ToList();
        Assert.DoesNotContain("voice", labels);
        Assert.Contains("break", labels);
    }
}
