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
using LilySharp.Core.Editing;
using LilySharp.Core.Harmony;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class ChordHarmonizerTests
{
    private static string Harmonize(string melodyMeasures, string key = "key c major")
    {
        string Doc(string extra, string scoreStaff) => $$"""
            octave absolute
            time 4/4
            {{key}}
            part melody { clef treble }
            section A { melody { {{melodyMeasures}} } {{extra}} }
            form main { A }
            score main { {{scoreStaff}} }
            """;

        var block = ChordHarmonizer.Harmonize(SyntaxTree.Parse(Doc("", "staff melody")));
        Assert.NotNull(block);

        // Placed back into the section as a sibling chord part, the generated block
        // must parse cleanly (a chords part lives inside a section, placed by the
        // score as a row above the staff).
        var reparsed = SyntaxTree.Parse(Doc(block!, "chords harmony  staff melody"));
        Assert.False(reparsed.HasErrors, string.Join("\n", reparsed.Diagnostics));
        return block!;
    }

    [Fact]
    public void CMajor_ArpeggiosPickTheOutlinedChord_DominantIsV7()
        // C, Dm, G, C outlined measure by measure; the dominant is emitted as G7.
        => Assert.Contains("C | Dm | G7 | C",
            Harmonize("c'4 e' g' c'' | d'4 f' a' d'' | g'4 b' d'' g'' | c''4 e'' g'' c''' |"));

    [Fact]
    public void AMinor_MinorVIsNotMadeASeventh()
        // a c e a -> Am (i); d f a d -> Dm (iv); e g b e -> Em (the v, a MINOR triad —
        // left as-is, NOT a weak v7); a c e a -> Am.
        => Assert.Contains("Am | Dm | Em | Am",
            Harmonize("a4 c' e' a' | d'4 f' a' d'' | e'4 g' b' e'' | a'4 c'' e'' a'' |",
                key: "key a minor"));

    [Fact]
    public void DMajor_SpellsSharpRootsWithHash()
        // vii° of D major is C#dim — the symbol spells the sharp with '#'.
    {
        var block = Harmonize("cis'4 e' g' cis'' |", key: "key d major");
        Assert.Contains("C#dim", block);
    }

    [Fact]
    public void RestOnlyMeasure_HoldsThePreviousChord()
    {
        var block = Harmonize("c'4 e' g' c'' | r1 |");
        Assert.Contains("C | C", block);   // 2nd (rest) measure repeats C
    }

    [Fact]
    public void HarmonizeBySections_OneAlignedTrackPerSection()
    {
        // Each section is harmonized independently of the structure block.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4 e' g' c'' | } }
            section B { melody { g'4 b' d'' g'' | } }
            form main { A B }
            score main { staff melody }
            """);
        var tracks = ChordHarmonizer.HarmonizeBySections(tree);
        Assert.Equal(2, tracks.Count);
        Assert.Contains("C |", tracks[0].ChordsBlock);      // section A outlines C
        Assert.Contains("G7", tracks[1].ChordsBlock);    // section B outlines G -> V7
        Assert.Equal("melody", tracks[0].MelodyBlock.PartName.Text);
    }

    [Fact]
    public void PartMajor_AddsAPartMajorChordTrack_WithoutReshaping()
    {
        // The default (newScore) template is part-major. Adding a chord track must
        // RESPECT that layout — a top-level `chords harmony { section A { } … }` track
        // — not convert the file to section-major (which used to move the sections
        // above the parts).
        var partMajor = """
            octave absolute
            time 4/4
            key c major
            part melody { clef treble
              section A { c'4 e' g' c'' | }
              section B { g'4 b' d'' g'' | }
            }
            form main { A B }
            score main { staff melody }
            """;
        Assert.Equal(LayoutForm.PartMajor,
            PartSectionLayoutConverter.Detect(SyntaxTree.Parse(partMajor).GetRoot()));

        var result = ChordHarmonizer.AddChordTracks(partMajor);
        Assert.NotNull(result);
        Assert.Null(result!.Value.Info);   // no conversion happened

        var reparsed = SyntaxTree.Parse(result.Value.Text);
        Assert.False(reparsed.HasErrors, string.Join("\n", reparsed.Diagnostics));
        // Still part-major, with a part-major chord track wired into the score.
        Assert.Equal(LayoutForm.PartMajor,
            PartSectionLayoutConverter.Detect(reparsed.GetRoot()));
        Assert.Contains("part melody {", result.Value.Text);   // the part is untouched
        Assert.Contains("chords harmony {", result.Value.Text);
        Assert.Contains("section A", result.Value.Text);
        Assert.Contains("chords harmony  staff melody", result.Value.Text);
        Assert.Contains("C |", result.Value.Text);      // section A -> C
        Assert.Contains("G7", result.Value.Text);    // section B -> G7
    }

    [Fact]
    public void AddChordTracks_SectionMajor_DoesNotConvert()
    {
        var result = ChordHarmonizer.AddChordTracks("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4 e' g' c'' | } }
            form main { A }
            score main { staff melody }
            """);
        Assert.NotNull(result);
        Assert.Null(result!.Value.Info);   // already section-major: no conversion note
        Assert.False(SyntaxTree.Parse(result.Value.Text).HasErrors);
    }

    [Fact]
    public void Harmonize_IgnoresThePartWordInComments()
    {
        // Regression: the melody part is found from the tree, so the word "part" in a
        // comment cannot be mistaken for the part name (which used to yield "No melody").
        var tree = SyntaxTree.Parse("""
            // this is the melody part of the song
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4 e' g' c'' | } }
            form main { A }
            score main { staff melody }
            """);
        Assert.NotNull(ChordHarmonizer.Harmonize(tree));
    }

    [Fact]
    public void ProducesAChordsBlockWithOneChordPerMeasure()
    {
        var block = Harmonize("c'4 e' g' c'' | d'4 f' a' d'' |");
        Assert.StartsWith("chords harmony {", block);
        // Two measures -> two entries.
        var body = block.Split('{')[1].Split('}')[0];
        var entries = body.Split(new[] { ' ', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, entries.Count(e => e != "|"));   // two chords (barlines excluded)
    }
}
