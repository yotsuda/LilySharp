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
using LilySharp.Core.Midi;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A score's own <c>transpose</c> moves all of the score (owner's report 2026-11-03: its notes and
/// key moved, and its chord row's names and all it sounded did not): the row's names, name by name
/// as the notes are, the written shapes as under a <c>shift</c>, and the MIDI and the preview's Play.
/// </summary>
[Trait("Category", "Unit")]
public class ScoreTransposeRowTests
{
    private static string Book(string option, string key = "key c major\n", string row = "Dm7 | G7 | Cmaj7") => key + """
        part melody {
          clef treble
          section A { d' f a d | g,2 g | c,1 }
        }

        chords prog {
          section A {
            ROW
          }
        }

        form { A }

        score {
          chords prog
          staff melody
        }

        score transposed OPTION {
          chords prog
          staff melody
        }
        """.Replace("ROW", row).Replace("OPTION", option);

    private static RenderSpec Spec(SyntaxTree tree, string? name) =>
        (name == null ? RenderSpecParser.FindFirst(tree) : RenderSpecParser.FindByName(tree, name))!;

    private static string Names(string book, string? score = "transposed")
    {
        var tree = SyntaxTree.Parse(book);
        return string.Join(" ", SvgGenerator.CollectScore(tree, Spec(tree, score)).ChordNames
            .OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble()).Select(c => c.ChordText));
    }

    private static MidiFile Midi(string book, string? score = "transposed")
    {
        var tree = SyntaxTree.Parse(book);
        var spec = Spec(tree, score);
        return new MidiExporter { Score = spec, Form = spec.Form }.Export(tree);
    }

    private static int[] Melody(MidiFile midi) => [.. midi.Tracks.Where(t => t.Name == "melody")
        .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).Select(n => n.Pitch)];

    private static string[] Chords(MidiFile midi) => [.. midi.Tracks.Where(t => t.Name == "prog (chords)").SelectMany(t => t.Notes)
        .GroupBy(n => n.StartTick).OrderBy(g => g.Key)
        .Select(g => string.Join(",", g.Select(n => n.Pitch % 12).Distinct().Order()))];

    [Theory]
    [InlineData("transpose d", "Em7 A7 D△")]
    [InlineData("transpose bes,", "Cm7 F7 B♭△")]         // down: the octave says nothing of a name
    [InlineData("transpose des", "E♭m7 A♭7 D♭△")]
    [InlineData("transpose cis", "D♯m7 G♯7 C♯△")]        // as the notes spell it, not the flat side
    [InlineData("transpose c'", "Dm7 G7 C△")]            // an octave moves no name
    public void TheRowsNames_MoveAsTheNotesDo(string option, string names)
        => Assert.Equal(names, Names(Book(option)));

    [Fact]
    public void TheOtherScore_StaysAsWritten()
    {
        Assert.Equal("Dm7 G7 C△", Names(Book("transpose d"), null));
        Assert.Equal(Melody(Midi(Book("pitch transposed"))), Melody(Midi(Book("transpose d"), null)));
    }

    [Fact]
    public void ASlashBass_MovesWithItsChord()
        => Assert.Equal("D/F♯ A7 D△", Names(Book("transpose d", row: "C/E | G7 | Cmaj7")));

    [Fact]
    public void ADegreeOfTheKey_IsReadInTheWrittenKey_AndNamesTheMovedChord()
        => Assert.Equal("Em7 A7 D△", Names(Book("transpose d", row: "IIm7 | V7 | Imaj7")));

    [Fact]
    public void AKeyWritten_TheNamesFollowIt()
        => Assert.Equal("Am7 D7 G△", Names(Book("transpose d", "key f major\n", "Gm7 | C7 | Fmaj7")));

    [Fact]
    public void WhatItSounds_MovesToo()
    {
        var written = Melody(Midi(Book("transpose c")));
        var moved = Melody(Midi(Book("transpose d")));
        Assert.Equal(written.Length, moved.Length);
        Assert.NotEmpty(moved);
        Assert.Equal(written.Select(p => p + 2), moved);
        // Em7, A7, Dmaj7.
        Assert.Equal(["2,4,7,11", "1,4,7,9", "1,2,6,9"], Chords(Midi(Book("transpose d"))));
    }

    [Fact]
    public void Down_AndByItsOctave_ItSoundsThere()
    {
        var written = Melody(Midi(Book("transpose c")));
        Assert.Equal(written.Select(p => p - 2), Melody(Midi(Book("transpose bes,"))));
        Assert.Equal(written.Select(p => p + 12), Melody(Midi(Book("transpose c'"))));
    }

    [Fact]
    public void ItComposes_WithAPartsOwnTranspose_AndTheFilesShift()
    {
        string part = Book("transpose d").Replace("clef treble", "clef treble transpose f");
        var own = Melody(Midi(part, null));
        Assert.Equal(own.Select(p => p + 2), Melody(Midi(part)));
        string shifted = "shift 1\n" + Book("transpose d");
        Assert.Equal(Melody(Midi(shifted, null)).Select(p => p + 2), Melody(Midi(shifted)));
    }

    [Fact]
    public void ADrumPart_StaysWhereItIs()
    {
        const string drums = "part kit { clef percussion }\nsection A { kit { bd4 hh sn hh | } }\nform { A }\nscore { staff kit }\nscore moved transpose d { staff kit }\n";
        var tree = SyntaxTree.Parse(drums);
        int[] Hits(string? name) => [.. new MidiExporter { Score = Spec(tree, name) }.Export(tree).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).Select(n => n.Pitch)];
        Assert.NotEmpty(Hits("moved"));
        Assert.Equal(Hits(null), Hits("moved"));
    }

    /// <summary>A written shape is the written chord's: the diagram draws the moved chord's usual shape.</summary>
    [Fact]
    public void AWrittenShape_IsNotDrawnOnTheMovedChord()
    {
        string Shaped(string option) => "key c major\nlayout d { chordDiagrams written }\n"
            + "part m { clef treble }\nchords prog { section A { C(x32010) | } }\nsection A { m { c'1 | } }\nform { A }\n"
            + "score { layout d  chords prog  staff m }\nscore moved " + option + " { layout d  chords prog  staff m }\n";
        string Spec_(string option, string? name)
        {
            var tree = SyntaxTree.Parse(Shaped(option));
            return SvgGenerator.CollectScore(tree, Spec(tree, name)).ChordNames.Single().FrameSpec ?? "";
        }
        Assert.NotEqual(Spec_("transpose d", null), Spec_("transpose d", "moved"));
    }

    [Theory]
    [InlineData("transpose d", "\\new ChordNames \\transpose c d \\progChords")]
    [InlineData("transpose des", "\\new ChordNames \\transpose c des \\progChords")]
    public void TheTwin_MovesTheRowAsThePageDoes(string option, string row)
    {
        var tree = SyntaxTree.Parse(Book(option));
        var render = RenderSpecParser.FindDeclaredByName(tree, "transposed")!.Value.Declaration;
        Assert.Contains(row, new LilyPondExporter { Score = render }.Export(tree));
    }

    [Fact]
    public void TheTwinOfTheOtherScore_LeavesTheRow()
    {
        var tree = SyntaxTree.Parse(Book("transpose d"));
        Assert.Contains("\\new ChordNames \\progChords", new LilyPondExporter().Export(tree));
    }
}
