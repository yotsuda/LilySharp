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
        foreach (var kw in new[] { "break", "noBreak", "octave", "tempo", "partial", "voice",
                                   "repeat", "tuplet", "grace", "acciaccatura", "appoggiatura",
                                   "clef", "key", "time", "override", "revert", "once override" })
            Assert.Contains(kw, labels);
    }

    /// <summary>
    /// Nothing the music grammar refuses is offered. Repeat structure — <c>|:</c>, <c>:|</c>,
    /// <c>[1. …]</c> — is form-only (LYS1034, 2026-08-31), and the two volta snippets that
    /// stayed in this list taught the rejected spelling (owner report, 2026-09-02).
    /// </summary>
    /// <remarks>
    /// ⚠️ The net is the COMPILER, not a blacklist: every plain-insert item is written into a
    /// section's music and must parse. Snippets with tab stops are checked by label only
    /// (the stop is not source), so a future volta snippet trips the label half.
    /// </remarks>
    [Fact]
    public void MusicCompletion_OffersNothingTheMusicGrammarRefuses()
    {
        var items = LilySharpLanguageServer.GetMusicCompletions("", 0, false).Items;
        foreach (var item in items)
        {
            Assert.DoesNotContain("|:", item.Label);
            Assert.DoesNotContain(":|", item.Label);
            Assert.DoesNotContain("[1.", item.Label);
            if (item.InsertTextFormat == LilySharp.Lsp.Protocol.InsertTextFormat.Snippet)
                continue;
            string insert = item.InsertText ?? item.Label!;
            // A declaration keyword needs its argument; the plain items stand alone.
            string body = insert switch
            {
                "clef" => "clef bass", "key" => "key g major", "time" => "time 3/4",
                "tempo" => "tempo 4 = 100", "octave" => "octave relative", "partial" => "partial 4",
                "override" => "override Stem.thickness = 2", "revert" => "revert Stem.thickness",
                "once override" => "once override Stem.thickness = 2",
                _ => insert,
            };
            var tree = LilySharp.Core.Syntax.SyntaxTree.Parse($$"""
                time 4/4
                part m { clef treble }
                section A { m { c4 d {{body}} e f | } }
                form main { A }
                score main { staff m }
                """);
            Assert.False(tree.HasErrors,
                $"'{item.Label}' → {insert} does not parse in music: "
                + string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        }
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
