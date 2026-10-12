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

using LilySharp.Core.Harmony;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class KeyEstimatorTests
{
    private static string BySection(string a, string b = "c'1 |", string header = "") => $$"""
        octave absolute
        time 4/4
        part melody { clef treble }
        section A { {{header}} melody { {{a}} } }
        section B { melody { {{b}} } }
        form { A B }
        score { staff melody }
        """;

    private static KeyEstimator.KeyCandidate Best(string source, string section = "A")
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var estimate = KeyEstimator.Estimate(tree, section);
        Assert.NotNull(estimate);
        return estimate!.Candidates[0];
    }

    [Fact]
    public void CMajorScaleAndCadence_IsCMajor()
        => Assert.Equal("c major", Best(BySection("c'4 d' e' f' | g' a' b' c'' | g'2 f'4 d' | c'1 |")).Text);

    [Fact]
    public void DMajorMelody_SpellsDAndReadsMajor()
        => Assert.Equal("d major", Best(BySection("d'4 fis' a' d'' | cis''4 b' a' g' | fis'4 e' a' cis' | d'1 |")).Text);

    [Fact]
    public void Likely_OffersOnlyKeysCloseToTheBest()
    {
        var estimate = KeyEstimator.Estimate(SyntaxTree.Parse(
            BySection("c'4 d' e' f' | g' a' b' c'' | g'2 f'4 d' | c'1 |")), "A")!;
        var likely = estimate.Likely;
        Assert.Equal("c major", likely[0].Text);
        Assert.InRange(likely.Count, 1, KeyEstimator.ListMax);
        Assert.All(likely, c => Assert.True(c.Score >= likely[0].Score - KeyEstimator.ListMargin));
        // The keys left out really are further down: the next one is past the margin.
        var next = estimate.Candidates[likely.Count];
        Assert.True(next.Score < likely[0].Score - KeyEstimator.ListMargin || likely.Count == KeyEstimator.ListMax);
        Assert.True(likely.Count < estimate.Candidates.Count);
    }

    [Fact]
    public void AMinorMelodyEndingOnA_IsAMinorNotCMajor()
        // The same seven letters as C major; the raised seventh and the final A decide.
        => Assert.Equal("a minor", Best(BySection("a'4 c'' b' a' | gis'4 a' b' e' | c''4 b' gis' e' | a'1 |")).Text);

    [Fact]
    public void EnharmonicTonic_FollowsTheWrittenSpelling()
    {
        Assert.Equal("ges major",
            Best(BySection("ges'4 aes' bes' ces'' | des''4 ees'' f'' ges'' | des''2 bes'4 aes' | ges'1 |")).Text);
        Assert.Equal("fis major",
            Best(BySection("fis'4 gis' ais' b' | cis''4 dis'' eis'' fis'' | cis''2 ais'4 gis' | fis'1 |")).Text);
    }

    [Fact]
    public void EverySectionIsReadAlone()
    {
        var source = BySection("c'4 d' e' f' | g'1 | c'1 |", "g'4 a' b' c'' | d''4 e'' fis'' g'' | d''2 fis'' | g'1 |");
        Assert.Equal("c major", Best(source, "A").Text);
        Assert.Equal("g major", Best(source, "B").Text);
    }

    [Fact]
    public void SectionsAt_CaretAndSelection()
    {
        var source = BySection("c'1 |", "d'1 |");
        var root = SyntaxTree.Parse(source).GetRoot();
        int inA = source.IndexOf("c'1");
        int inB = source.IndexOf("d'1");
        Assert.Equal(new[] { "A" }, KeyEstimator.SectionsAt(root, inA, inA));
        Assert.Equal(new[] { "B" }, KeyEstimator.SectionsAt(root, inB, inB));
        Assert.Equal(new[] { "A", "B" }, KeyEstimator.SectionsAt(root, inA, inB));
        int inForm = source.IndexOf("form { A") + "form { ".Length;
        Assert.Equal(new[] { "A" }, KeyEstimator.SectionsAt(root, inForm, inForm));
    }

    [Fact]
    public void SetSectionKey_InsertsIntoTheSectionHeader()
    {
        var text = KeyEstimator.SetSectionKey(BySection("d'4 fis' a' d'' |"), "A", "d major");
        Assert.NotNull(text);
        Assert.Contains("section A { key d major  melody {", text);
        Assert.Equal("d major", KeyEstimator.Estimate(SyntaxTree.Parse(text!), "A")!.CurrentKey);
        Assert.DoesNotContain("section B { key", text);
    }

    [Fact]
    public void SetSectionKey_RewritesTheKeysTheSectionStartsIn()
    {
        var source = BySection("key! g major d'4 fis' a' d'' |", header: "key c major");
        var text = KeyEstimator.SetSectionKey(source, "A", "d major");
        Assert.NotNull(text);
        Assert.Contains("section A { key d major melody { key! d major d'4", text);
    }

    [Fact]
    public void SetSectionKey_ByPartWithoutHeader_AddsAStandaloneHeader()
    {
        const string source = """
            octave absolute
            time 4/4
            part melody {
              clef treble
              section A { d'4 fis' a' d'' | }
              section B { c'1 | }
            }
            part bass {
              clef bass
              section A { d1 | }
              section B { c1 | }
            }
            form { A B }
            score { staff melody  staff bass }
            """;
        var estimate = KeyEstimator.Estimate(SyntaxTree.Parse(source), "A");
        Assert.Equal("d major", estimate!.Candidates[0].Text);
        Assert.Null(estimate.CurrentKey);

        var text = KeyEstimator.SetSectionKey(source, "A", "d major");
        Assert.NotNull(text);
        Assert.Contains("}\nsection A { key d major }\nform", text!.Replace("\r\n", "\n"));
        Assert.Equal("d major", KeyEstimator.Estimate(SyntaxTree.Parse(text), "A")!.CurrentKey);
    }

    [Fact]
    public void SectionWithoutPitches_HasNoEstimate()
        => Assert.Null(KeyEstimator.Estimate(SyntaxTree.Parse(BySection("r1 |")), "A"));
}
