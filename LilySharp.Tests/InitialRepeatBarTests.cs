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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// LilyPond prints no automatic repeat bar line at the START of a piece.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: <c>lily/bar-engraver.cc:432-449 Bar_engraver::pre_process_music</c> — the
/// comment over the method is literally "At the start of a piece, we don't print any
/// repeat bars", and the <c>repeatCommands</c> loop that turns <c>start-repeat</c> into
/// <c>startRepeatBarType</c> is skipped while <c>first_time_</c> holds. MEASURED on 2.26.0
/// (session 318, <c>scratch/p318/t4/startrepeat.ly</c>): the same <c>\repeat volta 2</c>
/// draws no opener when it opens the piece and draws <c>.|:</c> when one bar precedes it.
/// </para>
/// <para>
/// ⚠️ EVERY TEST HERE CARRIES ITS OWN POSITIVE CONTROL — a second <c>|:</c> one or two bars
/// later, in the same book, that must still be collected. Without it "measure 0 has no
/// opener" is also what a book whose repeat was never collected at all would say, and the
/// four places that can produce a <c>RepeatStart</c> (the staff builder, the chord row, the
/// lyric row, and the rows-only form walk) each have their own way of not collecting one.
/// </para>
/// <para>
/// The gate lives in <c>ScoreAssembler</c>, the one place both model constructors are
/// invoked, so these spellings are not four rules — they are one rule reached by four
/// roads. That is what this class pins: each road, and the road's own text row.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class InitialRepeatBarTests
{
    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return SvgGenerator.CollectScore(tree, spec);
    }

    /// <summary>
    /// Asserts the rule on EVERY voice of every staff — the staves and the chord / lyric
    /// text rows alike, since a text row draws its own barlines from its own voice and a
    /// row that kept the opener would print it under a staff that had dropped it.
    /// </summary>
    private static void AssertDroppedAtTheStartAndKeptLater(MultiStaffScore score)
    {
        var voices = score.StaffGroups
            .SelectMany(g => g.Staves)
            .SelectMany(s => s.Voices)
            .Where(v => v.Measures.Length > 0)
            .ToList();
        Assert.NotEmpty(voices);
        foreach (var voice in voices)
        {
            Assert.Equal(BarlineType.None, voice.Measures[0].StartBarline);
            // The positive control: the SAME sign, later in the SAME book, survives.
            Assert.Contains(voice.Measures.Skip(1),
                m => m.StartBarline is BarlineType.RepeatStart or BarlineType.RepeatBoth);
        }
    }

    /// <summary>The single-staff wrap — one plain staff, form-spelled repeats.</summary>
    [Fact]
    public void SingleStaff_FormSpelling()
    {
        AssertDroppedAtTheStartAndKeptLater(Collect("""
            time 4/4
            part m { clef treble }
            section A { m { c'1 } }
            section B { m { d'1 } }
            section C { m { e'1 } }
            form main { |: A :| B |: C :| }
            score main { staff m }
            """));
    }

    /// <summary>The same book with the repeats written in the MUSIC rather than the form.</summary>
    [Fact]
    public void SingleStaff_MusicSpelling()
    {
        AssertDroppedAtTheStartAndKeptLater(Collect("""
            time 4/4
            part m { clef treble section A { |: c'1 :| d'1 |: e'1 :| } }
            form main { A }
            score main { staff m }
            """));
    }

    /// <summary>Several staves: the score-level barline sync must not put the opener back.</summary>
    [Fact]
    public void MultiStaff_EveryStaffDropsIt()
    {
        AssertDroppedAtTheStartAndKeptLater(Collect("""
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section A { rh { c'1 } lh { c1 } }
            section B { rh { d'1 } lh { d1 } }
            section C { rh { e'1 } lh { e1 } }
            form main { |: A :| B |: C :| }
            score main { grandStaff { staff rh  staff lh } }
            """));
    }

    /// <summary>A chord row under a staff — the row builds its own barlines.</summary>
    [Fact]
    public void ChordRowUnderAStaff_AgreesWithIt()
    {
        AssertDroppedAtTheStartAndKeptLater(Collect("""
            time 4/4
            part m { clef treble }
            chords prog { section A { C } section B { G } section C { A } }
            section A { m { c'1 } }
            section B { m { d'1 } }
            section C { m { e'1 } }
            form main { |: A :| B |: C :| }
            score main { chords prog  staff m }
            """));
    }

    /// <summary>A lyric row under a staff — the other text-row producer.</summary>
    [Fact]
    public void LyricRowUnderAStaff_AgreesWithIt()
    {
        AssertDroppedAtTheStartAndKeptLater(Collect("""
            time 4/4
            part m { clef treble }
            lyrics words { section A { la } section B { la } section C { la } }
            section A { m { c'1 } }
            section B { m { d'1 } }
            section C { m { e'1 } }
            form main { |: A :| B |: C :| }
            score main { staff m  lyrics words }
            """));
    }

    /// <summary>
    /// A ROWS-ONLY score (no staff at all) — the form walk lays the barlines against a bare
    /// bar cursor there, which is a different piece of code from the staff builder.
    /// </summary>
    [Fact]
    public void RowsOnlyScore_DropsItToo()
    {
        AssertDroppedAtTheStartAndKeptLater(Collect("""
            time 4/4
            chords prog { section A { C } section B { G } section C { A } }
            form main { |: A :| B |: C :| }
            score main { chords prog }
            """));
    }

    // ⚠️ NO TEST FOR A `RepeatBoth` AT MOMENT 0, deliberately: nothing can produce one. A
    // measure's StartBarline is only ever set to RepeatStart (MeasureBuilder's pending start,
    // the rows-only form walk, the chord row, the lyric row — all four), and the one place
    // that could widen it, MeasureCollector.SynchronizeBarlines' Stronger over the voices,
    // takes a max over values that are None or RepeatStart. A book written `:|: c'1 …` was
    // measured and leaves measure 0's start at None. The production code still names the
    // RepeatBoth case, as its other readers do (StartBarWithBreakPieces,
    // RepeatPairingScanner), because the rule is the same for it if a fifth producer ever
    // makes one — but a test here would be green either way and would say nothing.

    /// <summary>
    /// The other half of the rule, stated on its own so a change that simply stopped
    /// collecting repeat openers could not pass this class: one bar of music before the
    /// repeat and the opener is drawn, exactly as LilyPond's minimal pair does.
    /// </summary>
    [Fact]
    public void OneBarOfMusicFirst_AndTheOpenerIsBack()
    {
        var score = Collect("""
            time 4/4
            part m { clef treble section A { c'1 |: d'1 :| } }
            form main { A }
            score main { staff m }
            """);
        var measures = score.StaffGroups.SelectMany(g => g.Staves)
            .SelectMany(s => s.Voices).First().Measures;
        Assert.Equal(BarlineType.None, measures[0].StartBarline);
        Assert.Equal(BarlineType.RepeatStart, measures[1].StartBarline);
        Assert.Equal(BarlineType.RepeatEnd, measures[1].EndBarline);
    }
}
