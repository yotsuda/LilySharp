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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The part-major lyric track form: <c>lyrics { section A { .. } section B { .. } }</c>
/// — a verse written per section and replayed by the structure, the dual of an
/// in-section lyrics block. Must parse and collect to the SAME lyrics as the
/// equivalent section-major file.
/// </summary>
public class LyricPartSectionsTests
{
    private const string SectionMajor = """
        time 4/4
        key c major
        part melody { clef treble }
        section A { melody { c4 c g' g | a a g2 | } lyrics { Twin- kle twin- kle | lit- tle star | } }
        section B { melody { g'4 g f f | e e d2 | } lyrics { how I won- der | what you are | } }
        form main { A B }
        score main "s" { staff melody }
        """;

    private const string PartMajor = """
        time 4/4
        key c major
        part melody { clef treble
          section A { c4 c g' g | a a g2 | }
          section B { g'4 g f f | e e d2 | }
        }
        lyrics {
          section A { Twin- kle twin- kle | lit- tle star | }
          section B { how I won- der | what you are | }
        }
        form main { A B }
        score main "s" { staff melody }
        """;

    [Fact]
    public void PartMajorLyricTrack_ParsesClean()
    {
        Assert.False(SyntaxTree.Parse(PartMajor).HasErrors);
    }

    [Fact]
    public void PartMajorLyricTrack_CollectsSameLyricsAsSectionMajor()
    {
        Assert.Equal(ChordPartSectionsHelpers.NonEmpty(LyricSignature(SectionMajor)), LyricSignature(PartMajor));
        Assert.Equal(LyricSignature(SectionMajor), LyricSignature(PartMajor));
    }

    [Fact]
    public void LyricInnerSection_IsNotTreatedAsAStructureSection()
    {
        // The lyric track's `section A/B` must not shadow the melody's sections.
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(PartMajor), "melody");
        Assert.Equal(4, score.Voice.Measures.Length);
    }

    [Fact]
    public void LyricTrack_RepeatsUnderAReprise()
    {
        // form main { A B A "A2" }: A's verse must reappear at the A2 reprise.
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble
              section A { c'4 c' g' g' | a' a' g'2 | }
              section B { f'4 f' e' e' | }
            }
            lyrics { section A { Twin- kle | star | } section B { how | } }
            form main { A B A "A2" }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody");
        var byMeasure = score.Lyrics
            .OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing.ToDouble())
            .Select(l => $"{l.MeasureIndex}:{l.Text}").ToArray();
        // A: Twin/kle(m0) star(m1); B: how(m2); A2: Twin/kle(m3) star(m4).
        Assert.Equal(new[] { "0:Twin", "0:kle", "1:star", "2:how", "3:Twin", "3:kle", "4:star" }, byMeasure);
    }

    [Fact]
    public void PartMajorLyricTrack_AsIndependentRow_ReadsSectionSyllablesNotStructureTokens()
    {
        // A part-major lyric track referenced as an independent ROW (`lyrics words`)
        // must read each inner section's syllables and align them to that section's
        // bars — NOT walk the `section NAME { … }` wrapper and emit "section"/"A" as
        // literal words (the row reader used to do exactly that).
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble
              section A { c'4 d' e' f' | }
              section B { g'4 a' b' c'' | }
            }
            lyrics words {
              section A { Do re mi fa | }
              section B { sol la ti do | }
            }
            form main { A B }
            score main { staff melody  lyrics words }
            """);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var rowTexts = score.Lyrics.Where(l => l.IsLyricsRow).Select(l => l.Text).ToList();

        Assert.DoesNotContain("section", rowTexts);
        Assert.Contains("Do", rowTexts);
        Assert.Contains("sol", rowTexts);
        // Section B's verse aligns to B's bar (index 1), not bar 0.
        Assert.All(score.Lyrics.Where(l => l.Text == "sol"), l => Assert.Equal(1, l.MeasureIndex));
    }

    [Fact]
    public void PerOccurrenceVoltaLyrics_GiveEachSectionRenditionItsOwnWords()
    {
        // `[1. …] [2. …]` inside a lyric section = different words each time A is sung.
        // Form: A (bars 0-1) |: ~B :| (bar 2) A "A2" (bars 3-4). Occurrence 1 takes
        // verse 1, the A2 reprise takes verse 2 — not a repeat of verse 1.
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble
              section A { c'4 d' e' f' | g'4 a' g'2 | }
              section B { g'4 f' e' d' | }
            }
            lyrics {
              section A {
                [1. Twin- kle twin- kle | lit- tle star |]
                [2. How I won- der | what you are |]
              }
            }
            form main { A |: ~B :| A "A2" }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody");
        var byMeasure = score.Lyrics
            .OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing.ToDouble())
            .Select(l => $"{l.MeasureIndex}:{l.Text}").ToArray();

        // First A (bar 0) sings verse 1; the A2 reprise (bar 3) sings verse 2.
        Assert.Contains("0:Twin", byMeasure);
        Assert.Contains("3:How", byMeasure);
        // The verses do NOT bleed across occurrences.
        Assert.DoesNotContain("0:How", byMeasure);
        Assert.DoesNotContain("3:Twin", byMeasure);
    }

    [Fact]
    public void PlainSectionLyrics_StillRepeatIdenticallyUnderEveryOccurrence()
    {
        // A section WITHOUT [N. …] brackets keeps the old behavior: the same verse
        // appears at every occurrence (verse 1 == verse at the reprise).
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble
              section A { c'4 d' e' f' | }
              section B { g'4 f' e' d' | }
            }
            lyrics { section A { Do re mi fa | } }
            form main { A |: ~B :| A "A2" }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody");
        var atReprise = score.Lyrics.Where(l => l.MeasureIndex == 2).Select(l => l.Text).ToList();
        // A is 1 bar; A2 starts at bar 2 (after A=0, B=1). Its words repeat A's.
        Assert.Contains("Do", atReprise);
    }

    private static string LyricSignature(string src)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");
        return string.Join(" ", score.Lyrics
            .OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing.ToDouble())
            .Select(l => $"{l.MeasureIndex}:{l.Text}"));
    }
}

internal static class ChordPartSectionsHelpers
{
    /// <summary>Asserts the signature is non-empty (guards against both forms
    /// collecting nothing, which would make an equality check vacuously pass).</summary>
    public static string NonEmpty(string s)
    {
        Assert.NotEqual("", s);
        return s;
    }
}
