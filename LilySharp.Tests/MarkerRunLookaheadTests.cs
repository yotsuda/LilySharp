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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A note's tie/slur/beam marks come from the whole RUN of marker nodes after it,
/// not just the first one.
/// </summary>
/// <remarks>
/// LilyPond's post-events are an unordered list that all bind to the note before them
/// (lily/parser.yy post_events); the Lily# parser preserves their source order as
/// sequence items and the collector folds them back onto the note. The old ONE-node
/// lookahead read only the first marker: <c>c8[( c)]</c> lost the <c>(</c> behind the
/// <c>[</c> (a bogus LYS4010, no slur drawn) AND the <c>]</c> behind the <c>)</c>
/// (the manual beam never closed). Reported 2026-08-13
/// (scratch/ベースタブLy/beam-slur.lys); LilyPond 2.26.0 compiles the twin
/// <c>c8 [ ( c ) ] d [ d d d d d ]</c> to a slur over the two c's and two beams.
/// </remarks>
[Trait("Category", "Unit")]
public class MarkerRunLookaheadTests
{
    private static Score Collect(string music)
    {
        var tree = MusicSource.Parse(music);
        Assert.False(tree.HasErrors);
        return new MeasureCollector().Collect(tree, null);
    }

    private static List<NoteItem> Notes(Score score) =>
        score.Voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    [Fact]
    public void SlurOpenBehindABeamOpenBindsToTheNoteBeforeBoth()
    {
        var notes = Notes(Collect("c'8[( c')] d'[ d' d' d' d' d'] |"));

        Assert.True(notes[0].HasBeamStart);
        Assert.True(notes[0].HasSlurStart);   // was dropped behind the '['
        Assert.True(notes[1].HasSlurEnd);
        Assert.True(notes[1].HasBeamEnd);     // was dropped behind the ')'
    }

    [Fact]
    public void TheReportedSpellingDrawsItsSlur_NoBogusLys4010()
    {
        var validator = new SlurPairingValidator();
        validator.Validate(SyntaxTree.Parse(
            "part m { section A { c8[( c)] d[ d d d d d] } } form main { A } score main { staff m }"));
        Assert.DoesNotContain(
            validator.Diagnostics, d => d.Code == DiagnosticCodes.UnpairedSlur);
    }

    [Fact]
    public void TieBehindABeamOpenBindsToTheNoteBeforeBoth()
    {
        var notes = Notes(Collect("c'8[~ c'] d'4 e' f' |"));

        Assert.True(notes[0].HasBeamStart);
        Assert.True(notes[0].HasTieStart);
    }

    [Fact]
    public void SlurCloseThenOpenOnOneNoteDoesBoth()
    {
        // The middle of `c( d)( e)` — d closes one slur and opens the next.
        var notes = Notes(Collect("c'4( d')( e') f' |"));

        Assert.True(notes[1].HasSlurEnd);
        Assert.True(notes[1].HasSlurStart);   // was dropped behind the ')'
        Assert.True(notes[2].HasSlurEnd);
    }

    /// <summary>
    /// The flags above say only that both marks REACHED the note; this says what the page does
    /// with them. The middle note closes the first slur before it opens the second, so the page
    /// draws c→d and d→e — the LilyPond twin's two bows.
    /// </summary>
    /// <remarks>
    /// ⚠️ Until session 474 the page read the open first, and the ')' popped the slur the '(' had
    /// just pushed: one bow c→e and a zero-length one on d, while the test above stayed green.
    /// LILYPOND-REF: lily/slur-engraver.cc:295-324 process_music — stop_events_ before start_events_.
    /// </remarks>
    [Fact]
    public void SlurCloseThenOpenOnOneNote_PairsAsTwoSlurs()
    {
        var slurs = new SlurDetector().DetectSlurs(Collect("c'4( d')( e') f' |"));

        Assert.Equal(new[] { (0, 1), (1, 2) },
            slurs.Select(s => (s.StartItemIndex, s.EndItemIndex)).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void SlurCloseThenOpenOnOneNote_IsNotReportedUnpaired()
    {
        var validator = new SlurPairingValidator();
        validator.Validate(SyntaxTree.Parse(
            "part m { section A { c4( d)( e) f } } form main { A } score main { staff m }"));
        Assert.DoesNotContain(
            validator.Diagnostics, d => d.Code == DiagnosticCodes.UnpairedSlur);
    }

    /// <summary>
    /// Both marks on a note with no slur open is what LilyPond refuses — "cannot end slur" for
    /// the ')' and "unterminated slur" for the '(' — and not a one-note slur, which is not a
    /// thing in notation. Lily# reports the same two.
    /// </summary>
    [Fact]
    public void BothSlurMarksOnANoteWithNothingOpen_AreReportedAsLilyPondDoes()
    {
        var validator = new SlurPairingValidator();
        validator.Validate(SyntaxTree.Parse(
            "part m { section A { c4() d e f } } form main { A } score main { staff m }"));
        Assert.Equal(2, validator.Diagnostics.Count(d => d.Code == DiagnosticCodes.UnpairedSlur));
    }

    [Fact]
    public void MarkerRunInsideATupletBodyBindsTheSameWay()
    {
        var notes = Notes(Collect("tuplet 3/2 { c'8[( c' c')] } d'4 e' f' |"));

        Assert.True(notes[0].HasBeamStart);
        Assert.True(notes[0].HasSlurStart);
        Assert.True(notes[2].HasSlurEnd);
        Assert.True(notes[2].HasBeamEnd);
    }

    /// <summary>The single-marker paths are byte-identical to before — one marker,
    /// same flag, and a marker run still ends at the first non-marker node.</summary>
    [Fact]
    public void ASingleMarkerAndItsTerminatorAreUnchanged()
    {
        var notes = Notes(Collect("c'8[ c'] d'4( e') f'2 |"));

        Assert.True(notes[0].HasBeamStart);
        Assert.False(notes[0].HasSlurStart);
        Assert.True(notes[1].HasBeamEnd);
        Assert.True(notes[2].HasSlurStart);   // the '(' binds to d', not to c']'s run
        Assert.True(notes[3].HasSlurEnd);
        Assert.False(notes[4].HasSlurStart);
    }
}
