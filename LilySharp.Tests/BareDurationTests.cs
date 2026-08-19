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

using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The bare duration (<c>bes8 8 8</c>): a spaced duration standing alone repeats
/// the previous note, chord or slash with the new length.
/// LILYPOND-REF: lily/parser.yy music_embedded — "duration post_events" builds a
/// NoteEvent with no pitch; the preceding note's or chord's pitches are used when
/// typeset. Behaviour pinned against 2.26.0 (2026-08-19, byte-identical SVGs):
/// a note repeats its pitch, a chord repeats the FULL chord, rests are
/// transparent. The trade this spelling makes is recorded in HANDOFF §3: a
/// dropped pitch letter (<c>4 g f e</c> for <c>a4 g f e</c>) now compiles as a
/// repeat instead of failing — the duration-preserving typo class LYS0016 can no
/// longer see.
/// </summary>
[Trait("Category", "Unit")]
public class BareDurationTests
{
    private static List<MusicItem> Items(string source)
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(source), null);
        return score.Voice.Measures.SelectMany(m => m.Items).ToList();
    }

    [Fact]
    public void RepeatsThePreviousNotesPitch()
    {
        var notes = Items("c'4 4 4").OfType<NoteItem>().ToList();
        Assert.Equal(3, notes.Count);
        Assert.All(notes, n => Assert.Equal(notes[0].Midi, n.Midi));
        Assert.All(notes, n => Assert.Equal(notes[0].StaffPosition, n.StaffPosition));
    }

    [Fact]
    public void RepeatsTheFullChord()
    {
        // Pinned against LP 2.26.0: <d' f'>4 4 is byte-identical to the chord
        // written twice — the bare duration repeats the CHORD, not its root.
        var chords = Items("<c' e g>4 4").OfType<ChordItem>().ToList();
        Assert.Equal(2, chords.Count);
        Assert.Equal(
            chords[0].Notes.Select(n => n.Midi).ToArray(),
            chords[1].Notes.Select(n => n.Midi).ToArray());
    }

    [Fact]
    public void TakesItsOwnDuration_AndFeedsTheCarry()
    {
        // The bare number IS a written duration, so it sets the running default
        // (LP: music_embedded assigns parser->default_duration_): the undurated
        // d after `8` is an eighth.
        var notes = Items("c'4 8 d").OfType<NoteItem>().ToList();
        Assert.Equal(new[] { 4, 8, 8 },
            notes.Select(n => (int)n.Duration.Denominator).ToArray());
    }

    [Fact]
    public void RestsAreTransparentToTheRun()
    {
        // Pinned against LP 2.26.0: { c'4 r4 4 8 8 } == { c'4 r4 c'4 c'8 c'8 }.
        var notes = Items("c'4 r4 4 8 8").OfType<NoteItem>().ToList();
        Assert.Equal(4, notes.Count);
        Assert.All(notes, n => Assert.Equal(notes[0].Midi, n.Midi));
    }

    [Fact]
    public void RederivesAccidentals_InsteadOfCopyingInk()
    {
        // Same rule as `q`: the copy runs through the measure's own accidental
        // state, so the original prints the sharp and the repeat does not.
        var notes = Items("fis'4 4").OfType<NoteItem>().ToList();
        Assert.Equal("sharp", notes[0].Accidental);
        Assert.Null(notes[1].Accidental);
    }

    [Fact]
    public void IsTransparentToTheRelativeFrame()
    {
        // The copy carries the original's ABSOLUTE pitch and the frame moves on
        // from the original — so the note after a repeat lands where it would
        // after the explicit spelling.
        var bare = Items("c'4 g8 d8 4 a8").OfType<NoteItem>().ToList();
        var explicitTwin = Items("c'4 g8 d8 d8 a8").OfType<NoteItem>().ToList();
        Assert.Equal(
            explicitTwin.Select(n => n.Midi).ToArray(),
            bare.Select(n => n.Midi).ToArray());
    }

    [Fact]
    public void RepeatsASlash()
    {
        var notes = Items("/4 4 8").OfType<NoteItem>().ToList();
        Assert.Equal(3, notes.Count);
        Assert.All(notes, n => Assert.Equal(NoteheadStyle.Slash, n.Notehead));
        Assert.All(notes, n => Assert.Equal(0, n.StaffPosition));
        Assert.Equal(new[] { 4, 4, 8 },
            notes.Select(n => (int)n.Duration.Denominator).ToArray());
    }

    [Fact]
    public void RepeatsADrumNote()
    {
        var notes = Items("bd4 4").OfType<NoteItem>().ToList();
        Assert.Equal(2, notes.Count);
        Assert.Equal(notes[0].StaffPosition, notes[1].StaffPosition);
        Assert.Equal(notes[0].Midi, notes[1].Midi);
        Assert.Equal(notes[0].Notehead, notes[1].Notehead);
    }

    [Fact]
    public void ReadsThroughAQ_ToTheChord()
    {
        var chords = Items("<c' e g>4 q8 4").OfType<ChordItem>().ToList();
        Assert.Equal(3, chords.Count);
        Assert.Equal(
            chords[0].Notes.Select(n => n.Midi).ToArray(),
            chords[2].Notes.Select(n => n.Midi).ToArray());
        Assert.Equal(4, (int)chords[2].Duration.Denominator);
    }

    [Fact]
    public void NothingToRepeat_WarnsAndOccupiesItsTimeAsASpacer()
    {
        // Same repair as a bad `q`: the time is kept, the validator says so.
        var tree = SyntaxTree.Parse("4 c' d e");
        Assert.Contains(SemanticValidation.Run(tree),
            d => d.Code == DiagnosticCodes.DetachedDuration);

        var spacer = Assert.IsType<RestItem>(Items("4 c' d e")[0]);
        Assert.True(spacer.IsSpacer);
        Assert.Equal(new Fraction(1, 4), spacer.Duration);
    }

    [Fact]
    public void RoundTrips_WithNoDiagnostics()
    {
        const string src = "part m\nsection A { m { bes8 8 8 8 8 8 8 8 | } }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(src, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void SoundsTheRepeatedPitchInMidi()
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npart v { }\n"
            + "section Main { v { c'4 4 <e' g'>4 4 | } }\nform main { Main }\nscore main { staff v }");
        var notes = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Equal(6, notes.Count); // 2 single notes + 2 chords of 2
        Assert.Equal(notes[0].Pitch, notes[1].Pitch);
    }

    [Fact]
    public void TheTwinPassesTheSpellingThrough()
    {
        // LilyPond 2.20+ reads isolated durations itself, so the twin writes the
        // source's own spelling — never an expansion.
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse("bes8 8 8 4"));
        Assert.Contains("bes8 8 8 4", ly.Replace("bes,8", "bes8"));
    }

    [Fact]
    public void DottedBareDurationInsideABeamBracket_StaysAVolta()
    {
        // `[` + Integer + `.` is the inline-volta opener; a beamed run that
        // starts on a dotted bare duration must spell the slash (or the pitch):
        // `[/4. 8]`. The ambiguity is resolved toward the volta, loudly for the
        // other reading — this pin is the decision's record.
        var tree = SyntaxTree.Parse("part m\nsection A { m { |: c4 [4. e f ] :| } }\n");
        Assert.Contains(tree.GetRoot().DescendantNodes(), n => n is InlineVoltaSyntax);
    }
}
