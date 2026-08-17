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
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// What a tie does to the SOUND when the tied thing is a chord: one sustained note per
/// member the next onset also sounds, never a second attack.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ EVERY CASE HERE IS WRITTEN AS A PAIR, because the count alone cannot tell a merge
/// from a book that had fewer notes to begin with. Each tied book is asserted against the
/// SAME music with the `~` removed, so what the case observes is the tie and not the chord.
/// </para>
/// <para>
/// WHY IT EXISTS. Until 2026-08-17 the MIDI merged a tie only when both sides were single
/// notes: a chord "broke the tie chain" by design, so `&lt;c e&gt;2 ~ &lt;c e&gt;2` was
/// played as two attacks while the page drew the tie and the MusicXML wrote the second
/// chord as a tie-stop. The whole suite was green — nothing in it asked the MIDI about a
/// tie at all, of any shape — and the instrument that found it was the cross-output sweep
/// (`audit/LilySharp.Probe -- pitches`), on 10 of 566 books.
/// </para>
/// <para>
/// ⚠️ THE PARTIAL CASE IS THE RULE, NOT AN EDGE CASE. `test/feature-tour` states it in the
/// corpus itself — 「&lt;c e g&gt;~ &lt;c e g&gt; でマッチするピッチ全てがタイに。一部不一致
/// なら共通分のみ」 — so a member the previous onset did not sound has to ATTACK. A rule
/// that ties by position instead would pass the equal-chord case and quietly sustain the
/// wrong pitch here.
/// </para>
/// </remarks>
public class ChordTieMidiTests
{
    private static List<MidiNote> ExportNotes(string body)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part v { }
            section Main { v { {{body}} } }
            form main { ~Main }
            score main { staff ~v }
            """);
        return new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).ToList();
    }

    [Fact]
    public void TiedChord_SustainsEveryMember_InsteadOfAttackingTwice()
    {
        var tied = ExportNotes("<c' e'>2 ~ <c' e'>2 |");
        var untied = ExportNotes("<c' e'>2 <c' e'>2 |");

        // The control fixes what "not merged" looks like: four attacks, two per chord.
        Assert.Equal(4, untied.Count);

        Assert.Equal(2, tied.Count);
        Assert.Equal(new[] { 72, 76 }, tied.Select(n => n.Pitch));
        // One sustained sound of both halves. Asserted as a ratio against the control
        // rather than a tick constant, so the pair survives a change of division.
        Assert.All(tied, n => Assert.Equal(2 * untied[0].DurationTicks, n.DurationTicks));
        Assert.All(tied, n => Assert.Equal(0, n.StartTick));
    }

    [Fact]
    public void TiedChord_AttachedSpelling_MergesLikeTheStandaloneOne()
    {
        // `<c e>~ <c e>` and `<c e> ~ <c e>` are the same tie written two ways — one as
        // the chord's own articulation, one as a sibling node — and they reach the walk
        // through different branches.
        Assert.Equal(2, ExportNotes("<c' e'>2~ <c' e'>2 |").Count);
        Assert.Equal(2, ExportNotes("<c' e'>2 ~ <c' e'>2 |").Count);
    }

    [Fact]
    public void TiedChord_WithOneMemberChanged_SustainsOnlyTheSharedPitches()
    {
        // test/feature-tour's own prose: `<g b d>2~ <g b e>` ties g and b only.
        var tied = ExportNotes("<g b d>2~ <g b e>2 |");
        var untied = ExportNotes("<g b d>2 <g b e>2 |");

        Assert.Equal(6, untied.Count);
        int half = untied[0].DurationTicks;

        // g and b sustain across both halves; d ends where it was written and e attacks.
        Assert.Equal(4, tied.Count);
        Assert.Equal(new[] { 67, 71, 62, 64 }, tied.Select(n => n.Pitch));
        Assert.Equal(2 * half, tied.Single(n => n.Pitch == 67).DurationTicks);
        Assert.Equal(2 * half, tied.Single(n => n.Pitch == 71).DurationTicks);
        Assert.Equal(half, tied.Single(n => n.Pitch == 62).DurationTicks);
        // The unmatched member is a new attack, and it starts at the SECOND half — a tie
        // that merged by position would have sustained it from tick 0 instead.
        Assert.Equal(half, tied.Single(n => n.Pitch == 64).DurationTicks);
        Assert.Equal(half, tied.Single(n => n.Pitch == 64).StartTick);
    }

    [Fact]
    public void TiedChord_ChainsThroughAThirdOnset()
    {
        var tied = ExportNotes("<c' e'>2~ <c' e'>2~ <c' e'>2 <c' e'>2 |");
        var untied = ExportNotes("<c' e'>2 <c' e'>2 <c' e'>2 <c' e'>2 |");

        Assert.Equal(8, untied.Count);
        // Three onsets collapse into one sustained pair; the fourth chord is separate.
        Assert.Equal(4, tied.Count);
        Assert.Equal(3 * untied[0].DurationTicks, tied[0].DurationTicks);
    }

    [Fact]
    public void ChordRepetition_IsATieTarget_LikeTheChordItCopies()
    {
        // A `q` is the same onset written shorter. The MusicXML exporter has always read
        // its `~`; this walk did not, which is how one rule keeps a broken third spelling.
        var tied = ExportNotes("<c' e'>2~ q2 |");
        var untied = ExportNotes("<c' e'>2 q2 |");

        Assert.Equal(4, untied.Count);
        Assert.Equal(2, tied.Count);
        Assert.Equal(2 * untied[0].DurationTicks, tied[0].DurationTicks);
    }

    [Fact]
    public void TiedSingleNote_StillMergesAfterTheChordWork()
    {
        // The single-note path shares the merge with the chord path now, so it needs its
        // own case: it had no MIDI observer at all before this file.
        var tied = ExportNotes("c'2 ~ c'2 |");
        var untied = ExportNotes("c'2 c'2 |");

        Assert.Equal(2, untied.Count);
        Assert.Single(tied);
        Assert.Equal(2 * untied[0].DurationTicks, tied[0].DurationTicks);
    }

    [Fact]
    public void Drums_DoNotTie_EvenWhenTheSpellingSaysSo()
    {
        // A drum key names an instrument, not a pitch, so there is nothing to sustain.
        // Stated here because the merge now searches a LIST of previous notes, and a drum
        // chord's members sit in the same track as the pitched ones.
        var tree = SyntaxTree.Parse("""
            part kit { clef percussion }
            section A { kit { bd4~ bd4 | } }
            form main { A }
            score main { staff kit }
            """);
        var notes = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).ToList();

        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.Equal(9, n.Channel));
        Assert.Equal(notes[0].DurationTicks, notes[1].DurationTicks);
    }
}
