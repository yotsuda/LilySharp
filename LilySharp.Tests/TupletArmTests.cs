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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The tuplet walk and the main walk are two arms over the same sounding items, and a tuplet
/// scales TIME and nothing else — LilyPond's <c>\tuplet</c> wraps the very same note events, and
/// every engraver sees them exactly as it would outside. So this is a differential net: the same
/// music written inside and outside <c>tuplet 3/2 { }</c> must answer the same for everything
/// that is not a duration (HANDOFF §2 R3, session 397).
/// </summary>
[Trait("Category", "Unit")]
public class TupletArmTests
{
    private static Score Collect(string music)
    {
        var tree = MusicSource.Parse(music);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
        return new MeasureCollector().Collect(tree, null);
    }

    private static List<NoteItem> Notes(Score score) =>
        score.Voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().Where(n => !n.GraceTime).ToList();

    private static List<ChordItem> Chords(Score score) =>
        score.Voice.Measures.SelectMany(m => m.Items).OfType<ChordItem>().Where(c => !c.GraceTime).ToList();

    // ---- cue { tuplet { … } } : the region's size reaches the tuplet's items ----

    [Fact]
    public void CueRegion_SizesTheNotesOfATupletInsideIt()
    {
        var notes = Notes(Collect("cue { tuplet 3/2 { c'8 d' e' } } f'4 |"));
        Assert.Equal(4, notes.Count);
        Assert.True(notes[0].IsCue);
        Assert.True(notes[1].IsCue);
        Assert.True(notes[2].IsCue);
        Assert.False(notes[3].IsCue);
        // The region's edge stamp lands on its first item, tuplet or not.
        Assert.True(notes[0].BeginsCueRegion);
        Assert.False(notes[1].BeginsCueRegion);
    }

    [Fact]
    public void CueRegion_SizesTheChordsOfATupletInsideIt()
    {
        var chords = Chords(Collect("cue { tuplet 3/2 { <c' e'>8 <d' f'> <e' g'> } } <f' a'>4 |"));
        Assert.Equal(4, chords.Count);
        Assert.True(chords[0].IsCue);
        Assert.True(chords[2].IsCue);
        Assert.False(chords[3].IsCue);
        Assert.True(chords[0].BeginsCueRegion);
    }

    // ---- per-note annotations the main arm reads before or after building the item ----

    [Fact]
    public void Courtesy_ParenthesisesTheAccidentalInsideATupletToo()
    {
        var plain = Notes(Collect("c'8@courtesy d' e' |"))[0];
        var scaled = Notes(Collect("tuplet 3/2 { c'8@courtesy d' e' } |"))[0];
        Assert.True(plain.IsCourtesy);
        Assert.Equal(plain.IsCourtesy, scaled.IsCourtesy);
        Assert.Equal(plain.Accidental, scaled.Accidental);
    }

    [Fact]
    public void NoteheadStyle_ReachesANoteInsideATuplet()
    {
        var plain = Notes(Collect("c'8@notehead(x) d' e' |"))[0];
        var scaled = Notes(Collect("tuplet 3/2 { c'8@notehead(x) d' e' } |"))[0];
        Assert.Equal(NoteheadStyle.Cross, plain.Notehead);
        Assert.Equal(plain.Notehead, scaled.Notehead);
    }

    [Fact]
    public void NoteheadStyle_ReachesAChordInsideATuplet()
    {
        var plain = Chords(Collect("<c' e'>8@notehead(diamond) d' e' |"))[0];
        var scaled = Chords(Collect("tuplet 3/2 { <c' e'>8@notehead(diamond) d' e' } |"))[0];
        Assert.Equal(NoteheadStyle.Diamond, plain.Notehead);
        Assert.Equal(plain.Notehead, scaled.Notehead);
    }

    [Fact]
    public void Feather_ReachesANoteInsideATuplet()
    {
        var plain = Notes(Collect("c'16@feather(right) d' e' f' |"))[0];
        var scaled = Notes(Collect("tuplet 3/2 { c'16@feather(right) d' e' } |"))[0];
        Assert.Equal(1, plain.FeatherDirection);
        Assert.Equal(plain.FeatherDirection, scaled.FeatherDirection);
    }

    // ---- a grace group rides the next SOUNDING item, whether or not that item is scaled ----

    [Fact]
    public void GraceBeforeATuplet_RidesTheTupletsFirstNote()
    {
        var score = Collect("grace { d'16 } tuplet 3/2 { c'8 d' e' } f'4 |");
        var notes = Notes(score);
        Assert.Equal(4, notes.Count);
        var grace = Assert.Single(score.GraceNotes);
        // The main note is the c' that opens the tuplet, not the f' after it.
        var allItems = score.Voice.Measures[0].Items;
        Assert.Same(notes[0], allItems[grace.MainNoteItemIndex]);
        Assert.NotEmpty(notes[0].LeadingGrace);
        Assert.Empty(notes[3].LeadingGrace);
        // The bracket opens on the tuplet's own first column, not on the grace's: LilyPond
        // hides a bracket whose bounds are the parallel beam's, and a grace column in front
        // would give the bracket a bound the beam does not have (the page then drew a bracket
        // under a fully beamed triplet, session 397's probe-cue-tuplet).
        var bracket = Assert.Single(score.TupletBrackets);
        Assert.Same(notes[0], allItems[bracket.StartNoteIndex]);
        Assert.Same(notes[2], allItems[bracket.EndNoteIndex]);
    }

    [Fact]
    public void GraceInsideATuplet_IsInsideItsBracket()
    {
        var score = Collect("tuplet 3/2 { grace { d'16 } c'8 d' e' } f'4 |");
        var allItems = score.Voice.Measures[0].Items;
        var bracket = Assert.Single(score.TupletBrackets);
        // The grace is the scaled music's own first column (LilyPond's TimeScaledMusic wraps it).
        Assert.True(allItems[bracket.StartNoteIndex].GraceTime);
    }

    [Fact]
    public void GraceInsideATuplet_RidesTheNoteThatFollowsIt()
    {
        var score = Collect("tuplet 3/2 { grace { d'16 } c'8 d' e' } f'4 |");
        var notes = Notes(score);
        Assert.Equal(4, notes.Count);
        var grace = Assert.Single(score.GraceNotes);
        var allItems = score.Voice.Measures[0].Items;
        Assert.Same(notes[0], allItems[grace.MainNoteItemIndex]);
        Assert.NotEmpty(notes[0].LeadingGrace);
    }
}