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
/// The part-major lyric track form: <c>lyrics w { section A { .. } section B { .. } }</c>
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
        section A { melody { c4 c g' g | a a g2 | } lyrics w { Twin- kle twin- kle | lit- tle star | } }
        section B { melody { g'4 g f f | e e d2 | } lyrics w { how I won- der | what you are | } }
        form main { A B }
        score main "s" { staff melody  lyrics w }
        """;

    private const string PartMajor = """
        time 4/4
        key c major
        part melody { clef treble
          section A { c4 c g' g | a a g2 | }
          section B { g'4 g f f | e e d2 | }
        }
        lyrics w {
          section A { Twin- kle twin- kle | lit- tle star | }
          section B { how I won- der | what you are | }
        }
        form main { A B }
        score main "s" { staff melody  lyrics w }
        """;

    // Lyrics attach EXPLICITLY (`staff melody  lyrics w`); collect through the render
    // path so the named track binds to the melody's notes exactly as it renders.
    private static LilySharp.Core.Svg.Model.MultiStaffScore CollectScored(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

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
            lyrics w { section A { Twin- kle | star | } section B { how | } }
            form main { A B A "A2" }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "w" });
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
            lyrics w {
              section A {
                [1. Twin- kle twin- kle | lit- tle star |]
                [2. How I won- der | what you are |]
              }
            }
            form main { A |: ~B :| A "A2" }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "w" });
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
    public void RepeatVoltaLyrics_StackAsVersesUnderTheOneRepeatedSection()
    {
        // B is sung twice via |: B :| but PRINTED once; its [1.][2.] verses stack as
        // verses 1 and 2 at B's single bar (not spread to two positions).
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble
              section A { c'4 d' e' f' | }
              section B { g'4 a' b' c'' | }
            }
            lyrics w {
              section A { la la la la | }
              section B { [1. up up up up |] [2. down down down down |] }
            }
            form main { A |: B :| }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "w" });
        var atB = score.Lyrics.Where(l => l.MeasureIndex == 1).ToList(); // A=bar0, B=bar1
        Assert.Contains(atB, l => l.Text == "up" && l.VerseNumber == 1);
        Assert.Contains(atB, l => l.Text == "down" && l.VerseNumber == 2);
    }

    [Fact]
    public void ListVoltaLyrics_ApplyToEachListedOccurrence()
    {
        // A occurs 3× written out; [1,3. …] covers occurrences 1 and 3, [2. …] the middle.
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble section A { c'4 d' e' f' | } }
            lyrics w { section A { [1,3. one two three four |] [2. aa bb cc dd |] } }
            form main { A "1" A "2" A "3" }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "w" }); // occurrences at bars 0,1,2
        Assert.Contains(score.Lyrics, l => l.Text == "one" && l.MeasureIndex == 0);
        Assert.Contains(score.Lyrics, l => l.Text == "one" && l.MeasureIndex == 2);
        Assert.Contains(score.Lyrics, l => l.Text == "aa" && l.MeasureIndex == 1);
        Assert.DoesNotContain(score.Lyrics, l => l.Text == "aa" && l.MeasureIndex == 0);
    }

    [Fact]
    public void DescendingListVolta_DoesNotDoublePlaceTheVerse()
    {
        // [3,1. …] covers occurrences 1 and 3 with label 3. A is written out only twice,
        // so occurrence 1 takes the verse and occurrence 3 never happens. The verse must
        // NOT also be stacked at the last occurrence just because its label (3) exceeds
        // the written-out count — that would print it twice.
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble section A { c'4 d' e' f' | } }
            lyrics w { section A { [3,1. one two three four |] } }
            form main { A "1" A "2" }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "w" }); // occurrences at bars 0,1
        Assert.Contains(score.Lyrics, l => l.Text == "one" && l.MeasureIndex == 0);
        Assert.DoesNotContain(score.Lyrics, l => l.Text == "one" && l.MeasureIndex == 1);
    }

    [Fact]
    public void TildeVoltaLyrics_HideTheStanzaNumberButKeepTheWords()
    {
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part melody { clef treble
              section A { c'4 d' e' f' | }
              section B { g'4 a' b' c'' | }
            }
            lyrics w {
              section A { la la la la | }
              section B { [1. up up up up |] [~2. down down down down |] }
            }
            form main { A |: B :| }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "w" });
        Assert.NotEmpty(score.Lyrics.Where(l => l.Text == "down"));
        Assert.All(score.Lyrics.Where(l => l.Text == "down"), l => Assert.True(l.HideStanza));
        Assert.All(score.Lyrics.Where(l => l.Text == "up"), l => Assert.False(l.HideStanza));
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
            lyrics w { section A { Do re mi fa | } }
            form main { A |: ~B :| A "A2" }
            score main { staff melody }
            """);
        var score = new MeasureCollector().Collect(tree, "melody", attachedLyricParts: new[] { "w" });
        var atReprise = score.Lyrics.Where(l => l.MeasureIndex == 2).Select(l => l.Text).ToList();
        // A is 1 bar; A2 starts at bar 2 (after A=0, B=1). Its words repeat A's.
        Assert.Contains("Do", atReprise);
    }

    [Fact]
    public void RowLyrics_RepeatUnderAWrittenOutReprise()
    {
        // An UNBOUND track placed as an independent ROW must carry its words at
        // every written-out occurrence, like the note-bound path — the row reader
        // used to place each section only at its first start, leaving the reprise
        // silent.
        var score = CollectScored("""
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
            form main { A B A "A2" }
            score main { staff melody  lyrics words }
            """);
        // A=bar0, B=bar1, A2=bar2.
        Assert.Contains(score.Lyrics, l => l.IsLyricsRow && l.Text == "Do" && l.MeasureIndex == 0);
        Assert.Contains(score.Lyrics, l => l.IsLyricsRow && l.Text == "Do" && l.MeasureIndex == 2);
    }

    [Fact]
    public void RowVoltaLyrics_StackAsVersesUnderTheOneRepeatedSection()
    {
        // `[1.][2.]` under |: B :| on a ROW: verse 2 stacks at B's single printed
        // pass instead of being dropped (the row reader used to flatten to the
        // first verse), and `~` still hides the stanza number.
        var score = CollectScored("""
            time 4/4
            key c major
            part melody { clef treble
              section A { c'4 d' e' f' | }
              section B { g'4 a' b' c'' | }
            }
            lyrics words {
              section A { la la la la | }
              section B { [1. up up up up |] [~2. down down down down |] }
            }
            form main { A |: B :| }
            score main { staff melody  lyrics words }
            """);
        var atB = score.Lyrics.Where(l => l.IsLyricsRow && l.MeasureIndex == 1).ToList();
        Assert.Contains(atB, l => l.Text == "up" && l.VerseNumber == 1);
        Assert.Contains(atB, l => l.Text == "down" && l.VerseNumber == 2);
        Assert.All(atB.Where(l => l.Text == "down"), l => Assert.True(l.HideStanza));
        Assert.All(atB.Where(l => l.Text == "up"), l => Assert.False(l.HideStanza));
    }

    [Fact]
    public void RowVoltaLyrics_GiveEachRenditionItsOwnWords()
    {
        // Written-out occurrences on a ROW: each rendition takes the verse whose
        // selector covers it, with no bleed between passes.
        var score = CollectScored("""
            time 4/4
            key c major
            part melody { clef treble section A { c'4 d' e' f' | } }
            lyrics words { section A { [1. one one one one |] [2. two two two two |] } }
            form main { A "1" A "2" }
            score main { staff melody  lyrics words }
            """);
        var rows = score.Lyrics.Where(l => l.IsLyricsRow).ToList();
        Assert.Contains(rows, l => l.Text == "one" && l.MeasureIndex == 0);
        Assert.Contains(rows, l => l.Text == "two" && l.MeasureIndex == 1);
        Assert.DoesNotContain(rows, l => l.Text == "two" && l.MeasureIndex == 0);
        Assert.DoesNotContain(rows, l => l.Text == "one" && l.MeasureIndex == 1);
    }

    private static string LyricSignature(string src)
    {
        var score = CollectScored(src);
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
