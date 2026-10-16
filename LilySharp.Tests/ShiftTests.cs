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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>shift N</c> (<see cref="FileShift"/>, owner's request 2026-10-17): the whole piece N
/// semitones up or down — every part, its own <c>transpose</c> included, the key signatures and
/// the chord rows, on the page, in the MIDI and in the LilyPond twin — spelled as ONE interval
/// from the file's first key (C major +1 is D♭ major, −1 B major; G major −1 F♯ major).
/// </summary>
[Trait("Category", "Unit")]
public class ShiftTests
{
    private static string Book(string head, string part = "part m { clef treble }", string music = "c4 d e f |")
        => "time 4/4\noctave absolute\n" + head + "\n" + part + "\nsection A { m { " + music + " } }\nform { A }\nscore { staff m }\n";

    private static SyntaxTree Parsed(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        return tree;
    }

    private static List<string> Pitches(string source)
        => ResolvedPitches.ForFile(Parsed(source))!.Select(e => e.Pitch).ToList();

    [Theory]
    [InlineData("shift +1")]
    [InlineData("shift -2")]
    [InlineData("shift 3")]
    [InlineData("shift 0")]
    public void ASignedOrBareNumber_Parses(string head)
        => Parsed(Book(head));

    [Theory]
    [InlineData("shift 12", "octave")]
    [InlineData("shift -12", "octave")]
    [InlineData("shift d", "number of semitones")]
    [InlineData("shift", "number of semitones")]
    public void AnythingElse_IsRefusedAtTheWord(string head, string says)
    {
        var tree = SyntaxTree.Parse(Book(head));
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains(says));
    }

    [Fact]
    public void TheNumber_IsReadWithItsSign()
    {
        Assert.Equal(1, FileShift.ReadSemitones(Parsed(Book("shift +1")).GetRoot()));
        Assert.Equal(-2, FileShift.ReadSemitones(Parsed(Book("shift -2")).GetRoot()));
        Assert.Equal(3, FileShift.ReadSemitones(Parsed(Book("shift 3")).GetRoot()));
        Assert.Equal(0, FileShift.ReadSemitones(Parsed(Book("")).GetRoot()));
    }

    [Theory]
    // C major: up a semitone is D-flat major (five flats), not C-sharp (seven sharps); down
    // a semitone is B major (five sharps), not C-flat; the tritone goes to the sharp side.
    [InlineData(1, 0, -5)]
    [InlineData(-1, 0, 5)]
    [InlineData(6, 0, 6)]
    [InlineData(2, 0, 2)]
    // G major down a semitone is F-sharp major (six sharps); E major up is F major.
    [InlineData(-1, 1, 6)]
    [InlineData(1, 4, -1)]
    public void TheTargetKey_StaysWithinFiveFlatsAndSixSharps(int semitones, int key, int after)
        => Assert.Equal(after, FileShift.KeySharpsAfter(key, semitones));

    [Theory]
    // The interval reaches the target key and moves by exactly the semitones asked.
    [InlineData(1, 0, 1, -1, 0)]    // c -> des
    [InlineData(-1, 0, 6, 0, -1)]   // c -> b,
    [InlineData(6, 0, 3, 1, 0)]     // c -> fis
    [InlineData(-1, 1, 6, 0, -1)]   // G major: c -> b, (g lands on fis, F-sharp major)
    [InlineData(1, 4, 1, -1, 0)]    // E major: c -> des (e lands on f, F major)
    [InlineData(-2, 0, 6, -1, -1)]  // c -> bes,
    public void TheInterval_SpellsTheTargetKey(int semitones, int key, int step, int alt, int oct)
    {
        var interval = FileShift.Interval(semitones, key);
        Assert.Equal((step, alt, oct), interval);
        Assert.Equal(semitones, PitchTransposer.IntervalSemitones(step, alt, oct));
    }

    [Fact]
    public void EveryNoteMoves_SpelledInTheTargetKey()
    {
        Assert.Equal(new[] { "C4", "D4", "E4", "F4" }, Pitches(Book("key c major")));
        Assert.Equal(new[] { "Db4", "Eb4", "F4", "Gb4" }, Pitches(Book("key c major\nshift +1")));
        Assert.Equal(new[] { "B3", "C#4", "D#4", "E4" }, Pitches(Book("key c major\nshift -1")));
        Assert.Equal(new[] { "F#4", "G#4", "A#4", "B4" }, Pitches(Book("key g major\nshift -1", music: "g4 a b c' |")));
        Assert.Equal(new[] { "D4", "E4", "F#4", "G4" }, Pitches(Book("shift +2")));
    }

    [Fact]
    public void TheShift_ComposesOutsideThePartsOwnTranspose()
    {
        // The part reads a tone up (transpose d); the shift adds a semitone on top: c sounds e-flat.
        Assert.Equal(new[] { "Eb4", "F4", "G4", "Ab4" },
            Pitches(Book("key c major\nshift +1", part: "part m { clef treble transpose d }")));
        // Where a top-level transpose is only the default of the parts that write none, the
        // shift reaches a part with its own transpose too (the whole piece moves).
        Assert.Equal(new[] { "Eb4", "F4", "G4", "Ab4" },
            Pitches(Book("key c major\ntranspose d\nshift +1")));
    }

    [Fact]
    public void TheTwin_WrapsThePartInTheSameInterval()
    {
        string twin = new LilyPondExporter().Export(Parsed(Book("key c major\nshift +1")));
        Assert.Contains("\\transpose c des", twin);
        twin = new LilyPondExporter().Export(Parsed(Book("key c major\nshift -1")));
        Assert.Contains("\\transpose c b,", twin);
    }

    [Fact]
    public void TheMidi_PlaysTheShift()
    {
        int Pitch(string head) => new MidiExporter().Export(Parsed(Book(head, music: "c'1 |"))).Tracks[1].Notes[0].Pitch;
        int plain = Pitch("key c major");
        Assert.Equal(plain + 1, Pitch("key c major\nshift +1"));
        Assert.Equal(plain - 2, Pitch("key c major\nshift -2"));
    }

    // ---------------------------------------------------------------- chord rows

    private static string RowBook(string head, string row, string score = "score { chords prog  staff m }") => $$"""
        time 4/4
        {{head}}
        part m { clef treble }
        section A {
          chords prog { {{row}} }
          m { c1 | g1 | }
        }
        form { A }
        {{score}}
        """;

    private static string Names(string book)
    {
        var tree = Parsed(book);
        var spec = RenderSpecParser.FindFirst(tree)!;
        return string.Join(" ", SvgGenerator.CollectScore(tree, spec).ChordNames
            .OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble()).Select(c => c.ChordText));
    }

    [Fact]
    public void ARowsNames_MoveWithThePiece_SpelledAsTheNotesAre()
    {
        Assert.Equal("C G7", Names(RowBook("key c major", "C | G7 |")));
        Assert.Equal("D♭ A♭7", Names(RowBook("key c major\nshift +1", "C | G7 |")));
        Assert.Equal("B F♯7", Names(RowBook("key c major\nshift -1", "C | G7 |")));
        Assert.Equal("F♯ C♯7", Names(RowBook("key g major\nshift -1", "G | D7 |")));
        // A row over no staff (a lead sheet) moves too.
        Assert.Equal("D♭ A♭7", Names(RowBook("key c major\nshift +1", "C | G7 |", "score { chords prog }")));
    }

    [Fact]
    public void ARowsMidi_PlaysTheShift()
    {
        // A row's notes carry the part label "NAME (chords)" (MidiExporter.PlayChordRow).
        List<int> Row(string head) => new MidiExporter().Export(Parsed(RowBook(head, "C | G7 |")))
            .Tracks.SelectMany(t => t.Notes).Where(n => n.Part is { } p && p.EndsWith("(chords)"))
            .Select(n => n.Pitch).ToList();
        var plain = Row("key c major");
        Assert.NotEmpty(plain);
        Assert.Equal(plain.Select(p => p + 1), Row("key c major\nshift +1"));
    }

    [Fact]
    public void ARowsWrittenShape_IsTheWrittenChords_SoTheDiagramDrawsTheShiftedChordsUsualShape()
    {
        // A staff whose part frets nothing draws on the guitar only where the layout says so.
        string? Frame(string head, string row = "C(x32010) | G7 |")
        {
            var tree = Parsed(RowBook(head, row));
            var spec = RenderSpecParser.FindFirst(tree)!;
            return SvgGenerator.CollectScore(tree, spec).ChordNames.OrderBy(c => c.MeasureIndex).First().FrameSpec;
        }
        string? plain = Frame("layout { chordDiagrams guitar }\nkey c major");
        string? shifted = Frame("layout { chordDiagrams guitar }\nkey c major\nshift +1");
        Assert.Equal("x32010", plain);
        Assert.NotNull(shifted);
        Assert.NotEqual(plain, shifted);
        // The shifted chord is D-flat, whose usual shape is what a bare D-flat draws under `all`.
        Assert.Equal(Frame("layout { chordDiagrams guitar all }\nkey c major", "Db | G7 |"), shifted);
    }

    [Fact]
    public void TheTwin_MovesTheRowWithThePiece()
    {
        string twin = new LilyPondExporter().Export(Parsed(RowBook("key c major\nshift +1", "C | G7 |")));
        Assert.Contains("\\new ChordNames \\transpose c des", twin);
    }
}
