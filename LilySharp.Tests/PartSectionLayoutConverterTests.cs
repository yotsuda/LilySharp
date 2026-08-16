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

using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// Converting a .lys document between the section-major and part-major layouts.
/// </summary>
[Trait("Category", "Unit")]
public class PartSectionLayoutConverterTests
{
    private readonly ITestOutputHelper _output;
    public PartSectionLayoutConverterTests(ITestOutputHelper output) => _output = output;

    private const string SectionMajor = """
        part low { clef bass }
        part high { clef treble }
        section A { low { c4 d } high { e'4 f' } }
        section B { low { g,4 a, } high { b'4 c'' } }
        form main { A B }
        score main "x" { staff low  staff high }
        """;

    [Fact]
    public void Detect_IdentifiesBothLayouts()
    {
        Assert.Equal(LayoutForm.SectionMajor, PartSectionLayoutConverter.Detect(SectionMajor));
        var pm = PartSectionLayoutConverter.Convert(SectionMajor);
        Assert.Equal(LayoutForm.PartMajor, PartSectionLayoutConverter.Detect(pm!));
    }

    [Fact]
    public void Convert_SectionMajor_ToPartMajor_KeepsCellsAndPassthrough()
    {
        var pm = PartSectionLayoutConverter.Convert(SectionMajor);
        Assert.NotNull(pm);
        _output.WriteLine(pm);

        // Part-major shape: each part owns its inner sections, music preserved.
        Assert.Contains("part low {", pm);
        Assert.Contains("section A { c4 d }", pm);
        Assert.Contains("section B { g,4 a, }", pm);
        Assert.Contains("part high {", pm);
        Assert.Contains("section A { e'4 f' }", pm);
        // Attributes and every other top-level item survive verbatim.
        Assert.Contains("clef bass", pm);
        Assert.Contains("form main { A B }", pm);
        Assert.Contains("score main \"x\" { staff low  staff high }", pm);
        // The result is valid .lys.
        Assert.False(SyntaxTree.Parse(pm!).HasErrors);
    }

    /// <summary>
    /// The converter overwrites the user's document in place, so a music cell has to
    /// come back as the characters that were TYPED — not as the tree's re-spelling of
    /// them. Those differ: a post-event written after a slur/tie/beam mark is hoisted
    /// onto the note and the mark replayed behind it, so <c>c,,1~@mark("A") c,,</c>
    /// spells itself back out of its own tree as <c>c,,1@mark("A") ~c,,</c> — the tie
    /// moved across the mark and now reads as belonging to the NEXT note. Measured
    /// 2026-08-16: the converter did exactly that to ~40 real files. The engraving is
    /// unaffected (both spellings render byte-identically), which is why only a text
    /// test can see it. §2F ⑺ tracks making the tree itself faithful; this pins the
    /// writer meanwhile.
    /// </summary>
    [Theory]
    [InlineData("c,,1~@mark(\"A\") c,, |")]   // tie then mark — the author's own idiom
    [InlineData("g4(@cresc a b c |")]         // slur open then hairpin
    [InlineData("g4( a b c)@f |")]            // slur close then dynamic
    [InlineData("c8[@accent d e f] g4 |")]    // beam open then articulation
    [InlineData("c4 d e f |")]                // control: nothing to reorder
    public void AMusicCell_ComesBackAsTheCharactersThatWereTyped(string music)
    {
        var sm = $"part bass\nsection A {{ bass {{ {music} }} }}\nform main {{ ~A }}\n"
                 + "score main { staff bass }\n";

        // The premise, stated so a failure says which half broke: the tree really does
        // re-spell these (except the control), so passing cannot be an accident of the
        // fixture being uninteresting.
        var respelled = SyntaxTree.Parse(sm).GetRoot().ToFullString();
        if (music != "c4 d e f |")
            Assert.NotEqual(sm, respelled);

        var pm = PartSectionLayoutConverter.Convert(sm);
        Assert.NotNull(pm);
        _output.WriteLine(pm);
        Assert.Contains(music, pm);
        Assert.False(SyntaxTree.Parse(pm!).HasErrors);
    }

    [Fact]
    public void Convert_RoundTrips_BackToSectionMajor()
    {
        var pm = PartSectionLayoutConverter.Convert(SectionMajor);
        var sm2 = PartSectionLayoutConverter.Convert(pm!);
        Assert.NotNull(sm2);
        _output.WriteLine(sm2);

        Assert.Equal(LayoutForm.SectionMajor, PartSectionLayoutConverter.Detect(sm2!));
        Assert.Contains("section A {", sm2);
        Assert.Contains("low { c4 d }", sm2);
        Assert.Contains("high { e'4 f' }", sm2);
        Assert.Contains("form main { A B }", sm2);
        Assert.False(SyntaxTree.Parse(sm2!).HasErrors);
    }

    [Fact]
    public void Convert_SectionMajorWithChords_ToPartMajor_PreservesChordTrack()
    {
        // A `chords name { }` chord part transposes to a part-major chord track
        // (`chords name { section .. }`) and back — no data loss.
        var sm = """
            part melody { clef treble }
            section A { melody { c4 c g' g | } chords harmony { c1 | f1 | } }
            section B { melody { g'4 g f f | } chords harmony { c1 | } }
            form main { A B }
            score main "s" { staff melody with chords harmony }
            """;
        Assert.False(PartSectionLayoutConverter.HasUntransposableSectionContent(sm));

        var pm = PartSectionLayoutConverter.Convert(sm);
        Assert.NotNull(pm);
        Assert.Equal(LayoutForm.PartMajor, PartSectionLayoutConverter.Detect(pm!));
        Assert.Contains("chords harmony {", pm);
        Assert.Contains("section A { c1 | f1 | }", pm);
        Assert.Contains("section B { c1 | }", pm);
        Assert.False(SyntaxTree.Parse(pm!).HasErrors);

        // Round-trips back to section-major with the chords folded into the sections.
        var sm2 = PartSectionLayoutConverter.Convert(pm!);
        Assert.NotNull(sm2);
        Assert.Equal(LayoutForm.SectionMajor, PartSectionLayoutConverter.Detect(sm2!));
        Assert.Contains("chords harmony { c1 | f1 | }", sm2);
        Assert.False(SyntaxTree.Parse(sm2!).HasErrors);
    }

    [Fact]
    public void Convert_SectionMajorWithLyrics_ToPartMajor_PreservesLyricTrack()
    {
        // A lyrics block transposes to a part-major lyric track and back — no loss.
        var sm = """
            part melody { clef treble }
            section A { melody { c4 c g' g | } lyrics w { Twin- kle twin- kle | } }
            section B { melody { g'4 g f f | } lyrics w { how I won- der | } }
            form main { A B }
            score main "s" { staff melody with lyrics w }
            """;
        Assert.False(PartSectionLayoutConverter.HasUntransposableSectionContent(sm));

        var pm = PartSectionLayoutConverter.Convert(sm);
        Assert.NotNull(pm);
        Assert.Equal(LayoutForm.PartMajor, PartSectionLayoutConverter.Detect(pm!));
        Assert.Contains("lyrics w {", pm);
        Assert.Contains("section A { Twin- kle twin- kle | }", pm);
        Assert.Contains("section B { how I won- der | }", pm);
        Assert.False(SyntaxTree.Parse(pm!).HasErrors);

        var sm2 = PartSectionLayoutConverter.Convert(pm!);
        Assert.NotNull(sm2);
        Assert.Equal(LayoutForm.SectionMajor, PartSectionLayoutConverter.Detect(sm2!));
        Assert.Contains("lyrics w { Twin- kle twin- kle | }", sm2);
        Assert.False(SyntaxTree.Parse(sm2!).HasErrors);
    }

    [Fact]
    public void Convert_MultipleNamedLyricTracks_RoundTrip_NoLoss()
    {
        // Two named lyric tracks (EN + JA) on one melody — the 替え歌 / multi-language
        // mechanism — must survive BOTH directions with each track's words intact.
        var sm = """
            part melody { clef treble }
            section A { melody { c4 d e f | } lyrics en { do re mi fa | } lyrics ja { ど れ み ふぁ | } }
            form main { A }
            score main { staff melody lyrics en lyrics ja }
            """;

        var pm = PartSectionLayoutConverter.Convert(sm);
        Assert.NotNull(pm);
        Assert.Equal(LayoutForm.PartMajor, PartSectionLayoutConverter.Detect(pm!));
        Assert.Contains("lyrics en {", pm);
        Assert.Contains("lyrics ja {", pm);
        Assert.Contains("do re mi fa", pm);
        Assert.Contains("ど れ み ふぁ", pm);
        Assert.False(SyntaxTree.Parse(pm!).HasErrors);

        var sm2 = PartSectionLayoutConverter.Convert(pm!);
        Assert.NotNull(sm2);
        Assert.Equal(LayoutForm.SectionMajor, PartSectionLayoutConverter.Detect(sm2!));
        Assert.Contains("lyrics en { do re mi fa | }", sm2);
        Assert.Contains("lyrics ja { ど れ み ふぁ | }", sm2);
        Assert.False(SyntaxTree.Parse(sm2!).HasErrors);
    }

    [Fact]
    public void Convert_PlainSections_NotFlaggedAsUntransposable()
    {
        // The ordinary section-major file (only part blocks in its sections) must
        // still convert — the guard only trips on chord/lyric blocks.
        Assert.False(PartSectionLayoutConverter.HasUntransposableSectionContent(SectionMajor));
        Assert.NotNull(PartSectionLayoutConverter.Convert(SectionMajor));
    }

    [Fact]
    public void Convert_Unknown_ReturnsNull()
    {
        // No part blocks / inner sections — nothing to transpose.
        Assert.Null(PartSectionLayoutConverter.Convert("title \"x\"\nsection A { c4 d e f }\n"));
    }

    [Fact]
    public void Convert_FileWithSyntaxError_ReturnsNull_NoCorruption()
    {
        // Unbalanced braces — must NOT be transposed (the caller overwrites the
        // whole document, so a malformed file would be mangled).
        var broken = "part low { clef bass }\nsection A { low { c4 d } \n";
        Assert.Null(PartSectionLayoutConverter.Convert(broken));
    }

    [Fact]
    public void Convert_CellEndingInLineComment_DoesNotSwallowBrace()
    {
        var src = "part low { clef bass }\nsection A { low { c4 d e f // melody\n} }\nform main { A }\n";
        var converted = PartSectionLayoutConverter.Convert(src);
        Assert.NotNull(converted);
        // The // comment must not comment out the regenerated closing brace.
        Assert.False(SyntaxTree.Parse(converted!).HasErrors);
        Assert.Contains("// melody", converted);
    }

    [Fact]
    public void Convert_KeepsCommentAboveFirstStructuralBlock()
    {
        var src = "// verse arrangement\npart low { clef bass }\nsection A { low { c4 d } }\nform main { A }\n";
        var converted = PartSectionLayoutConverter.Convert(src);
        Assert.NotNull(converted);
        Assert.Contains("// verse arrangement", converted);
        Assert.False(SyntaxTree.Parse(converted!).HasErrors);
    }

    // A section-major section may state its own key beside its part blocks. It must not
    // block the conversion (it used to refuse), and it becomes a standalone header.
    private const string SectionMajorWithKey = """
        section A {
          key g major
          melody { c4 c g' g }
          bass { c2 e }
        }
        form main { A }
        score main { staff melody  staff bass }
        """;

    [Fact]
    public void Convert_SectionKey_NoLongerRefuses()
    {
        Assert.NotNull(PartSectionLayoutConverter.Convert(SectionMajorWithKey));
    }

    [Fact]
    public void Convert_SectionMajorKey_ToPartMajor_EmitsStandaloneHeader()
    {
        var pm = PartSectionLayoutConverter.Convert(SectionMajorWithKey);
        Assert.NotNull(pm);
        _output.WriteLine(pm);
        // The section's key stands parallel to the parts as its own header block…
        Assert.Contains("section A { key g major }", pm);
        // …and the part cells no longer carry it.
        Assert.Contains("part melody {", pm);
        Assert.Contains("section A { c4 c g' g }", pm);
        // The header leads the layout — it comes BEFORE the parts that use it.
        Assert.True(pm!.IndexOf("section A { key g major }", System.StringComparison.Ordinal)
            < pm.IndexOf("part melody", System.StringComparison.Ordinal));
        Assert.False(SyntaxTree.Parse(pm).HasErrors);
    }

    [Fact]
    public void Convert_PartMajorStandaloneHeader_ToSectionMajor_FoldsTheKeyIn()
    {
        var src = """
            part melody { section A { c4 c g' g } }
            part bass { section A { c2 e } }
            section A { key g major }
            form main { A }
            score main { staff melody  staff bass }
            """;
        Assert.Equal(LayoutForm.PartMajor, PartSectionLayoutConverter.Detect(src));
        var sm = PartSectionLayoutConverter.Convert(src);
        Assert.NotNull(sm);
        _output.WriteLine(sm);
        // The header folds back into the section, above its part cells.
        Assert.Contains("key g major", sm);
        Assert.Matches(@"section A \{\s*key g major", sm);
        Assert.DoesNotContain("section A { key g major }", sm); // no longer standalone
        Assert.False(SyntaxTree.Parse(sm!).HasErrors);
    }

    [Fact]
    public void Convert_SectionKey_RoundTrips()
    {
        var pm = PartSectionLayoutConverter.Convert(SectionMajorWithKey);
        var back = PartSectionLayoutConverter.Convert(pm!);
        Assert.NotNull(back);
        // The key survives the round trip, folded back into the section.
        Assert.Contains("key g major", back);
        Assert.Equal(LayoutForm.SectionMajor, PartSectionLayoutConverter.Detect(back!));
        Assert.False(SyntaxTree.Parse(back!).HasErrors);
    }
}
