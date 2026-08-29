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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The two closed vocabularies a <c>tab</c> render item carries. Both used to fall through a
/// <c>_ =&gt;</c> arm without a word.
/// </summary>
[Trait("Category", "Unit")]
public class TabRenderVocabularyTests
{
    private static string Book(string tabItem) => $$"""
        octave absolute
        time 4/4
        part m { instrument bass }
        section A { m { c8 e g8. e16 g4 r4 | } }
        form main { A }
        score main {
          staff m
          {{tabItem}}
        }
        """;

    private static Diagnostic[] Check(string tabItem) =>
        SemanticValidation.Run(SyntaxTree.Parse(Book(tabItem))).ToArray();

    private static bool Refuses(string tabItem, string needle) =>
        Check(tabItem).Any(d => d.Code == DiagnosticCodes.UnknownTabRenderWord
                                && d.Message.Contains(needle));

    [Theory]
    [InlineData("tab m as bogus")]
    [InlineData("tab m as roman")]      // the CHORD row's word — the shared clause's other half
    [InlineData("tab m as Numbers")]
    [InlineData("tab m as NUMBERS")]    // was accepted: the reader compared OrdinalIgnoreCase
    [InlineData("tab m as Full")]
    public void AStyleOutsideTheVocabulary_IsRefused(string tabItem) =>
        Assert.True(Refuses(tabItem, "is not a tab style"), tabItem);

    [Theory]
    [InlineData("tab bogus m")]
    [InlineData("tab BASS m")]          // the right word, the wrong case
    [InlineData("tab Guitar m")]
    public void ATuningOutsideTheVocabulary_IsRefused(string tabItem) =>
        Assert.True(Refuses(tabItem, "Unknown tuning"), tabItem);

    /// <summary>
    /// Every spelling that engraved before still engraves — the half that must not move.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TUNING NAMES COME FROM THE PUBLISHED VOCABULARY rather than being listed, so a
    /// tuning added to <c>SymbolCaseValidator</c> and not to the score position lands here
    /// red instead of being silently un-writable. Counting the vocabulary rather than
    /// sampling it is the whole difference between this and the 2026-08-19 measurement, which
    /// checked that all seven are accepted and concluded the position was covered.
    /// </remarks>
    [Fact]
    public void EveryPublishedTuning_IsAcceptedInAScore()
    {
        Assert.NotEmpty(LanguageVocabulary.TuningNames);
        foreach (string tuning in LanguageVocabulary.TuningNames)
            Assert.DoesNotContain(Check($"tab {tuning} m"),
                d => d.Code == DiagnosticCodes.UnknownTabRenderWord);
    }

    /// <inheritdoc cref="EveryPublishedTuning_IsAcceptedInAScore"/>
    [Fact]
    public void EveryPublishedStyle_IsAcceptedInAScore()
    {
        Assert.NotEmpty(LanguageVocabulary.TabStyles);
        foreach (string style in LanguageVocabulary.TabStyles)
            Assert.DoesNotContain(Check($"tab m as {style}"),
                d => d.Code == DiagnosticCodes.UnknownTabRenderWord);
    }

    [Theory]
    [InlineData("tab m")]
    [InlineData("tab m as numbers")]
    [InlineData("tab m as full")]
    [InlineData("tab bass m")]
    [InlineData("tab bass m as numbers")]
    public void AWellFormedTabItem_IsSilent(string tabItem) =>
        Assert.DoesNotContain(Check(tabItem),
            d => d.Code == DiagnosticCodes.UnknownTabRenderWord);

    /// <summary>
    /// With no `as` clause the SCORE chooses: a tab beside a notation staff of the same part
    /// is `as numbers`, a tab standing alone is `as full` (user decision, 2026-08-29).
    /// </summary>
    /// <remarks>
    /// The rule is about what the reader already has. A paired tab needs fret digits only,
    /// because the staff above carries the meter, the rests, the dots, the stems and the
    /// ties; a lone tab has to carry all of it. Asserting it as an EQUALITY with the explicit
    /// spelling is what makes it a statement about the default rather than about any one
    /// glyph -- a later change to what `numbers` draws cannot make this pass vacuously.
    /// </remarks>
    [Fact]
    public void WithNoClause_TheScoreChoosesTheStyle()
    {
        static string Draw(string book) => LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(book),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        static string Score(string items) => $$"""
            octave absolute
            time 4/4
            part m { instrument bass }
            section A { m { c8 e g8. e16 g4~ g4 | } }
            form main { A }
            score main {
              {{items}}
            }
            """;

        const string pair = "staff m\n  tab m";
        const string pairNumbers = "staff m\n  tab m as numbers";

        // Paired with its own notation staff -> numbers.
        Assert.Equal(Draw(Score(pairNumbers)), Draw(Score(pair)));
        // Standing alone -> full.
        Assert.Equal(Draw(Score("tab m as full")), Draw(Score("tab m")));
        // ...and the two answers really are different pictures, so neither line above can
        // pass by both sides drawing the same thing.
        Assert.NotEqual(Draw(Score("tab m as numbers")), Draw(Score("tab m as full")));
    }
}
