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
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>|:</c> that opens the piece IS printed — the writer spelled it (owner decision,
/// session 328), and the twin tells LilyPond to print its own.
/// </summary>
/// <remarks>
/// <para>
/// LILYSHARP-OWN, and knowingly so. LilyPond's DEFAULT prints no automatic repeat bar at the
/// start of a piece —
/// LILYPOND-REF: <c>lily/bar-engraver.cc:432-449 Bar_engraver::pre_process_music</c>,
/// whose comment reads "At the start of a piece, we
/// don't print any repeat bars", the <c>repeatCommands</c> loop skipped while
/// <c>first_time_</c> holds — and session 319 ported that gate (<c>ScoreAssembler</c>,
/// HANDOFF §2 T5). Session 328 reversed it on the owner's word: in Lily# a <c>|:</c> is
/// always something the writer wrote, the corpus is lead sheets, and LilyPond itself keeps
/// the door open with <c>\set Score.printInitialRepeatBar = ##t</c>
/// (Documentation/en/notation/repeats.itely:160-172, "traditionally printed" on lead
/// sheets). So the model keeps the opener, and <see cref="LilyPondExporter"/> writes that
/// setting into every twin so the two pages agree — which is also what keeps the ledger's
/// <c>line-start.time-to-first-note.initial-repeat</c> an honest pair.
/// </para>
/// <para>
/// ⚠️ EVERY TEST HERE STILL CARRIES ITS OWN POSITIVE CONTROL — a second <c>|:</c> later in
/// the same book — so "measure 0 keeps its opener" is not satisfied by a book whose repeats
/// were never collected at all being compared with itself. The four producers of a
/// <c>RepeatStart</c> (the staff builder, the chord row, the lyric row, the rows-only form
/// walk) are each walked, as they were when the rule pointed the other way.
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
    /// row that dropped the opener would leave a gap under a staff that printed it.
    /// </summary>
    private static void AssertKeptAtTheStartAndLater(MultiStaffScore score)
    {
        var voices = score.StaffGroups
            .SelectMany(g => g.Staves)
            .SelectMany(s => s.Voices)
            .Where(v => v.Measures.Length > 0)
            .ToList();
        Assert.NotEmpty(voices);
        foreach (var voice in voices)
        {
            Assert.True(voice.Measures[0].StartBarline is BarlineType.RepeatStart or BarlineType.RepeatBoth,
                $"measure 0 opens with {voice.Measures[0].StartBarline}; the written |: is printed");
            // The positive control: the SAME sign, later in the SAME book, is there too.
            Assert.Contains(voice.Measures.Skip(1),
                m => m.StartBarline is BarlineType.RepeatStart or BarlineType.RepeatBoth);
        }
    }

    /// <summary>The single-staff wrap — one plain staff, form-spelled repeats.</summary>
    [Fact]
    public void SingleStaff_FormSpelling()
    {
        AssertKeptAtTheStartAndLater(Collect("""
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
        AssertKeptAtTheStartAndLater(Collect("""
            time 4/4
            part m { clef treble section A { |: c'1 :| d'1 |: e'1 :| } }
            form main { A }
            score main { staff m }
            """));
    }

    /// <summary>Several staves: the score-level barline sync keeps the opener on each.</summary>
    [Fact]
    public void MultiStaff_EveryStaffKeepsIt()
    {
        AssertKeptAtTheStartAndLater(Collect("""
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
        AssertKeptAtTheStartAndLater(Collect("""
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
        AssertKeptAtTheStartAndLater(Collect("""
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
    public void RowsOnlyScore_KeepsItToo()
    {
        AssertKeptAtTheStartAndLater(Collect("""
            time 4/4
            chords prog { section A { C } section B { G } section C { A } }
            form main { |: A :| B |: C :| }
            score main { chords prog }
            """));
    }

    /// <summary>
    /// The other half, stated on its own: one bar of music before the repeat and the opener
    /// stands on measure 1, not 0 — the sign goes where it was written.
    /// </summary>
    [Fact]
    public void OneBarOfMusicFirst_AndTheOpenerIsOnTheSecondBar()
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

    /// <summary>
    /// The twin carries LilyPond's own switch for the same picture, on every book — the
    /// setting is inert on a book that does not open with a repeat, and it is what keeps a
    /// twin of an opening <c>|:</c> a pair rather than a page with one bar line fewer.
    /// </summary>
    [Fact]
    public void TheTwin_TellsLilyPondToPrintItsOwn()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse("""
            time 4/4
            part m { clef treble }
            section A { m { c'1 } }
            form main { |: A :| }
            score main { staff m }
            """));
        Assert.Contains("printInitialRepeatBar = ##t", Regex.Replace(ly, @"\s+", " "));
    }
}
