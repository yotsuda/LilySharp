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

using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;

namespace LilySharp.Tests;

/// <summary>
/// Tests for common-shortest-duration calculation and its effect on spacing (H-1).
/// LILYPOND-REF: lily/spacing-spanner.cc
/// </summary>
[Trait("Category", "Unit")]
public class CommonShortestDurationTests
{
    [Fact]
    public void QuarterNoteOnlyScore_IsCappedAtBaseShortest()
    {
        // LILYPOND-REF: lily/spacing-spanner.cc:166-171 — the spacing basis is
        // min(base-shortest-duration (3/16), mode of per-measure shortests), so a
        // quarters-only score spaces on the 3/16 basis (NOT 1/8) like LilyPond.
        var source = "c4 d e f |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);

        Assert.Equal(0.1875, shortest, 4);
    }

    [Fact]
    public void OneOrnamentalRun_DoesNotDominate_ModeWins()
    {
        // LILYPOND-REF: lily/spacing-spanner.cc:92-164 calc_common_shortest_duration —
        // the basis is the MOST COMMON per-measure shortest, so a single 32nd-note
        // measure must not loosen a whole piece of eighths.
        var source = "c8 d e f g a b c' | c8 d e f g a b c' | c8 d e f g a b c' | c32 d e f c d e f c d e f c d e f c d e f c d e f c d e f c d e f |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);

        Assert.Equal(0.125, shortest, 4);
    }

    [Fact]
    public void FullMeasureRests_DoNotContribute()
    {
        // Full-measure rests create no musical columns in LilyPond; the basis
        // comes from the sounding measures only (here: quarters → capped 3/16).
        var source = "R1*2 c4 d e f |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);

        Assert.Equal(0.1875, shortest, 4);
    }

    [Fact]
    public void FullMeasureRests_DoNotContribute_OutsideCommonTime()
    {
        // A full-measure rest is measured against the PREVAILING meter, not a whole note:
        // in 2/4 the bar IS a half rest, so `R2` creates no musical column just as `R1`
        // does not in 4/4. This used to floor at a whole note, so every 2/4 rest bar voted
        // its half into the mode.
        //
        // One bar of eighths plus three rest bars: with the rest bars excluded the mode is
        // the eighth (0.125). Counting them would make the half the mode, and the 3/16 cap
        // would then report 0.1875 — so this value is what distinguishes the two.
        // LILYPOND-REF: lily/spacing-spanner.cc:92-173 calc_common_shortest_duration.
        var source = """
            time 2/4
            key c major
            part melody
            section Main { melody { c8 d e f | R2*3 | } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec!);

        double shortest = SpacingRules.CalculateCommonShortestDuration(multi);

        Assert.Equal(0.125, shortest, 4);
    }

    /// <summary>The multi-staff collect of a whole book, the way the page reads it.</summary>
    private static double ShortestOf(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec!);
        return SpacingRules.CalculateCommonShortestDuration(multi);
    }

    [Fact]
    public void ABoundLyricRow_CastsNoVote_AtTheMelodysFullMeasureRests()
    {
        // LILYPOND-REF: lily/spacing-engraver.cc:176-183 add_starter_duration — a grob
        // with lyric-syllable-interface (LyricText, scm/define-grobs.scm:2213-2236) is
        // turned away before its duration is recorded, so lyrics never vote.
        //
        // A bound ROW (placed away from its staff, so it stays a band rather than folding
        // under it) carries a spacer skeleton of the melody: the same durations, which the
        // melody already votes — except the melody's `R1`, which the skeleton re-spells as
        // a whole-bar spacer. One bar of eighths and three rest bars: the melody votes the
        // eighth once and nothing else, so the basis is the eighth. With the row voting
        // its three wholes, the mode was the whole and the basis the 3/16 cap.
        string Book(bool row) => $$"""
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c8 d e f g a b c' | R1*3 | } }
            lyrics verse { section A { one two three four five six seven eight | } }
            form main { A }
            score main {
              {{(row ? "lyrics verse sings melody" : "")}}
              staff melody
            }
            """;

        Assert.Equal(0.125, ShortestOf(Book(row: false)), 12);
        Assert.Equal(0.125, ShortestOf(Book(row: true)), 12);
    }

    [Fact]
    public void AnIndependentLyricRow_CastsNoVote_WithItsEvenSpreadSlots()
    {
        // An unbound row spreads a bar's syllables evenly: eight words are eight eighth-note
        // slots, a duration no note sounds. Over a melody of quarters the basis stays the
        // 3/16 cap (the quarter's), as it is with no row at all; with the slots voting, the
        // eighth was the per-bar shortest and the whole piece loosened to it.
        // LILYPOND-REF: lily/spacing-engraver.cc:176-183 add_starter_duration.
        string Book(bool row) => $$"""
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c4 d e f | g a b c' | } }
            lyrics verse { section A { one two three four five six seven eight | one two three four five six seven eight | } }
            form main { A }
            score main {
              staff melody
              {{(row ? "lyrics verse" : "")}}
            }
            """;

        Assert.Equal(0.1875, ShortestOf(Book(row: false)), 12);
        Assert.Equal(0.1875, ShortestOf(Book(row: true)), 12);
    }

    /// <summary>The bar widths of the top staff in a rendered page: the X steps between
    /// its barlines (the thin, tall rects), in staff spaces.</summary>
    private static double[] TopStaffBarWidths(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        string svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        var bars = Regex.Matches(svg,
                "<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.-]+)\" height=\"([0-9.-]+)\"")
            .Select(m => (X: Num(m.Groups[1].Value), Y: Num(m.Groups[2].Value),
                          W: Num(m.Groups[3].Value), H: Num(m.Groups[4].Value)))
            .Where(r => r.W < 0.5 && r.H > 3)
            .ToList();
        Assert.True(bars.Count > 1, "the page drew no barlines");
        double topY = bars.Min(b => b.Y);
        var xs = bars.Where(b => Math.Abs(b.Y - topY) < 0.1).Select(b => b.X).Distinct().OrderBy(x => x).ToArray();
        // The SVG carries two decimals; round the steps so the two books' widths compare as
        // the numbers they are, not as the floating-point noise of two different subtractions.
        return xs.Skip(1).Zip(xs, (b, a) => Math.Round(b - a, 3)).ToArray();
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void ABoundLyricRow_LeavesTheStaffsBarWidths_WhereLilyPondHasThem()
    {
        // The page-level reading of the vote. A part sheet: the sax staff over a row that
        // sings the (unengraved) vocal's eighths, with one-letter syllables so no syllable's
        // width can push a column. LilyPond 2.26.0 spaces the sax the same with and without
        // that row — MEASURED (scratch/p351/lp, a.ly / d.ly = a Staff alone / with a Devnull
        // vocal and a \lyricsto Lyrics; common-shortest-duration 3/16 in both, the barline X
        // steps of the first three bars 9.836 / 13.336 / 8.150 in both). With the row's
        // skeleton voting the eighth, Lily# spaced the same sax at 11.24 / 16.14 / 8.85.
        string Book(bool row) => $$"""
            time 4/4
            part sax { }
            part vocal { }
            section Chorus {
              sax   { c4 d e f | g2 g | a4 g f e | c1 | }
              vocal { g8 g a4 a8 a a4 | g2 f | e4 f g2 | c1 | }
              lyrics en sings vocal { a b a b a b | a b | a b a | b | }
            }
            form main { Chorus }
            score main {
              staff sax
              {{(row ? "lyrics en" : "")}}
            }
            """;

        double[] alone = TopStaffBarWidths(Book(row: false));
        double[] withRow = TopStaffBarWidths(Book(row: true));
        Assert.Equal(alone, withRow);

        double[] lilypond = { 9.836, 13.336, 8.150 };
        Assert.Equal(3, withRow.Length);
        for (int i = 0; i < 3; i++)
            Assert.True(Math.Abs(withRow[i] - lilypond[i]) < 0.01,
                $"bar {i + 1}: Lily# {withRow[i]:F3} vs LilyPond {lilypond[i]:F3}");
    }

    [Fact]
    public void AChordRow_KeepsVoting_ItsSlotsStandForChordNames()
    {
        // The control: a CHORD row's slot stands for a ChordName, which IS a rhythmic grob
        // (scm/define-grobs.scm ChordName rhythmic-grob-interface) with no early return —
        // eight chords in a bar of quarters vote the eighth, exactly as before.
        var source = """
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c4 d e f | } }
            chords prog { section A { C G C G C G C G | } }
            form main { A }
            score main {
              chords prog as names
              staff melody
            }
            """;

        Assert.Equal(0.125, ShortestOf(source), 12);
    }

    [Fact]
    public void MixedDurations_ShortestIsSmallest()
    {
        // Score with half, quarter, and eighth notes → shortest is eighth (0.125)
        var source = "c2 d4 e8 f |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);

        Assert.Equal(0.125, shortest, 4);
    }

    [Fact]
    public void SixteenthNotes_ShortestIsSixteenth()
    {
        var source = "c16 d e f g a b c' d' e' f' g' a' b' c'' d'' |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        double shortest = SpacingRules.CalculateCommonShortestDuration(score);

        Assert.Equal(0.0625, shortest, 4);
    }

    [Fact]
    public void DurationSpaceChangesWithBaseShortestDuration()
    {
        // LILYPOND-REF: lily/spacing-options.cc:68-104 get_duration_space()
        // With base=1/4 (quarter), a quarter note gets ratio=1 → spaceFactor = 2.0
        // With base=1/8 (eighth), a quarter note gets ratio=2 → spaceFactor = 2.0 + log2(2) = 3.0
        var quarter = new Fraction(1, 4);

        double spaceWithQuarterBase = SpacingRules.CalculateDurationSpace(quarter, 0.25);
        double spaceWithEighthBase = SpacingRules.CalculateDurationSpace(quarter, 0.125);

        // Quarter base: (2.0 + log2(1)) * 1.2 = 2.0 * 1.2 = 2.4
        Assert.Equal(2.4, spaceWithQuarterBase, 2);
        // Eighth base: (2.0 + log2(2)) * 1.2 = 3.0 * 1.2 = 3.6
        Assert.Equal(3.6, spaceWithEighthBase, 2);

        // Quarter base should produce tighter spacing
        Assert.True(spaceWithQuarterBase < spaceWithEighthBase,
            "Quarter-base spacing should be tighter than eighth-base");
    }

    [Fact]
    public void SpringCreationUsesBaseShortestDuration()
    {
        // Verify that CreateSpring uses the provided baseShortestDuration
        var quarter = new Fraction(1, 4);

        var springDefault = SpacingRules.CreateSpring(null, null, quarter);
        var springQuarterBase = SpacingRules.CreateSpring(null, null, quarter,
            baseShortestDuration: 0.25);

        // Default uses BaseShortestDuration = 0.125, so ideal is larger
        Assert.True(springDefault.IdealDistance > springQuarterBase.IdealDistance,
            $"Default base (ideal={springDefault.IdealDistance:F2}) should produce wider spacing " +
            $"than quarter base (ideal={springQuarterBase.IdealDistance:F2})");
    }

    [Fact]
    public void TimingSpringUsesBaseShortestDuration()
    {
        // Verify that CreateTimingSpring uses the provided baseShortestDuration
        var quarter = new Fraction(1, 4);

        var springDefault = SpacingRules.CreateTimingSpring(quarter);
        var springQuarterBase = SpacingRules.CreateTimingSpring(quarter,
            baseShortestDuration: 0.25);

        Assert.True(springDefault.IdealDistance > springQuarterBase.IdealDistance,
            $"Default timing spring (ideal={springDefault.IdealDistance:F2}) should be wider " +
            $"than quarter-base (ideal={springQuarterBase.IdealDistance:F2})");
    }

    [Fact]
    public void MeasureIdealWidthAffectedByBaseShortestDuration()
    {
        // A measure's ideal width should differ based on the score's common shortest duration
        var source = "c4 d e f |";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);
        var measure = score.Voice.Measures[0];

        double widthDefault = SpacingRules.CalculateMeasureIdealWidth(measure);
        double widthQuarterBase = SpacingRules.CalculateMeasureIdealWidth(measure,
            baseShortestDuration: 0.25);

        // With base=1/4, quarter notes are the shortest, so spacing is tighter
        Assert.True(widthDefault > widthQuarterBase,
            $"Default width ({widthDefault:F2}) should be wider than quarter-base ({widthQuarterBase:F2})");
    }
}
