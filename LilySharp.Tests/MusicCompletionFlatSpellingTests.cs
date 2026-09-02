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
        // (`partial` is NOT among them: it is a section directive, LYS1024 in music.)
        foreach (var kw in new[] { "break", "noBreak", "octave", "tempo", "voice",
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
    /// ⚠️ The net is the COMPILER, not a blacklist: every item — a snippet by its keyword,
    /// the rest by its insert — is written into a section's music as a whole bar and must
    /// pass the parser AND the semantic validators with no error. Labels are also refused
    /// the repeat spellings outright.
    /// ⚠️ IT WAS THE PARSER ALONE until 2026-09-02, and that let `partial` through: the
    /// parser reads `partial 4` anywhere, and the refusal is PartialScopeValidator's LYS1024
    /// (a pickup is a section directive). Owner report the same day. A net that stops at the
    /// parser is half a net.
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
            string insert = item.InsertText ?? item.Label!;
            // The keyword a snippet inserts (its insert carries tab stops, which are not
            // source), else the plain insert.
            string word = item.InsertTextFormat == LilySharp.Lsp.Protocol.InsertTextFormat.Snippet
                ? item.Label!.Split(' ')[0]
                : insert;
            // One whole 4/4 bar around the item, so nothing but the item can be at fault: a
            // declaration takes its argument, a pitch or rest its duration, a block its body.
            string bar = word switch
            {
                "clef" => "clef bass g4 a b c' |",
                "key" => "key g major g4 a b c' |",
                "time" => "time 3/4 g4 a b |",
                "tempo" => "tempo 4 = 100 g4 a b c' |",
                "octave" => "octave relative g4 a b c' |",
                "override" => "override Stem.transparent = true g4 a b c' |",
                "revert" => "revert Stem.transparent g4 a b c' |",
                "once" => "once override Stem.transparent = true g4 a b c' |",
                "repeat" => "repeat unfold 2 { g4 a b c' | }",
                "tuplet" => "tuplet 3/2 { g8 a b } c'4 d' e' |",
                "<<" => "<< g b d' >>4 e'4 f' g' |",   // the group's duration goes after >>

                "grace" => "grace { g16 } a4 b c' d' |",
                "acciaccatura" => "acciaccatura { g16 } a4 b c' d' |",
                "appoggiatura" => "appoggiatura { g16 } a4 b c' d' |",
                "voice" => "voice { g4 a b c' | } { e4 f g a | }",   // one keyword, N blocks
                "R" => "R1 |",
                "r" or "s" => word + "4 a b c' |",
                _ when insert.StartsWith('<') => insert + "4 a4 b c' |",
                _ when insert.Length <= 4 && char.IsLower(insert[0]) && !insert.Contains(' ')
                    => insert + "4 a b c' |",            // a pitch row: c, fis, bes …
                _ => insert + " g4 a b c' |",            // break, noBreak, pageBreak, noPageBreak
            };
            var tree = LilySharp.Core.Syntax.SyntaxTree.Parse($$"""
                time 4/4
                part m { clef treble }
                section A { m { c4 d e f | {{bar}} } }
                form main { A }
                score main { staff m }
                """);
            Assert.False(tree.HasErrors,
                $"'{item.Label}' → {bar} does not parse in music: "
                + string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
            var errors = LilySharp.Core.Semantics.SemanticValidation.Run(tree)
                .Where(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"'{item.Label}' → {bar} is refused in music: "
                + string.Join(" | ", errors.Select(d => d.Message)));
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
