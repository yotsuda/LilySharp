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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Midi;

/// <summary>
/// A score's MIDI sounds the parts THAT score shows — the ones it engraves and its bare
/// MIDI-only rows — the way a <c>chords</c> row it does not place is silent (owner decision
/// 2026-09-25, extended 2026-09-29): with <c>score "p2" { staff p2 }</c> picked, the
/// preview's Play sounded p1 as well (user report, <c>scratch/parts.lys</c>). A file with no
/// <c>score</c> block sounds every part, as it always did, and the timeline of the played
/// parts does not move: a section the picked parts sit out still takes its bars.
/// </summary>
public class ScorePartsMidiTests
{
    private const int Bar = 1920; // 4/4 at 480 ticks a quarter

    // scratch/parts.lys: two parts, two scores on one form — the whole file, and p2 alone.
    private const string TwoScores = """
        section A {
          p1 { c'4 d e f | g2 g | }
          p2 { e f g a | b2 b }
        }

        form { A }

        score {
          staff p1
          staff p2
        }

        score "p2" {
          staff p2
        }
        """;

    private static MidiFile Export(string lys, string? scoreName = null)
    {
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var score = scoreName == null ? null : RenderSpecParser.FindByName(tree, scoreName);
        if (scoreName != null)
            Assert.NotNull(score);
        return new MidiExporter { Form = score?.Form, Score = score }.Export(tree);
    }

    private static (string? Part, int Start, int Pitch)[] Sounding(MidiFile midi)
        => midi.Tracks.SelectMany(t => t.Notes)
            .Select(n => (n.Part, n.StartTick, n.Pitch))
            .OrderBy(n => n.StartTick).ThenBy(n => n.Part).ThenBy(n => n.Pitch)
            .ToArray();

    [Fact]
    public void TheScoreOfOnePart_SoundsThatPartAlone()
    {
        var p2Only = Sounding(Export(TwoScores, "p2"));
        Assert.Equal(6, p2Only.Length);
        Assert.All(p2Only, n => Assert.Equal("p2", n.Part));
    }

    [Fact]
    public void TheScoreOfBothParts_SoundsBoth_AndTheOnePartScoreKeepsItsTiming()
    {
        var both = Sounding(Export(TwoScores, "main"));
        Assert.Equal(12, both.Length);
        Assert.Equal(new[] { "p1", "p2" }, both.Select(n => n.Part).Distinct().OrderBy(p => p));

        // p2's notes in its own score are EXACTLY its notes in the full score: same onsets,
        // same pitches — the strip takes p1's notes out and moves nothing.
        Assert.Equal(both.Where(n => n.Part == "p2"), Sounding(Export(TwoScores, "p2")));
    }

    [Fact]
    public void NoScoreNamed_PlaysTheFirstScore()
    {
        // The exporter with no Score resolves the file's first score (RenderSpecParser.PlayedSpec),
        // which is `main` — both parts — so the CLI and the exports that named no score keep
        // their old output on this file.
        Assert.Equal(12, Sounding(Export(TwoScores)).Length);
    }

    [Fact]
    public void AFileWithNoScoreBlock_SoundsEveryPart()
    {
        var midi = Export("""
            section A {
              p1 { c'4 d e f | }
              p2 { e f g a | }
            }
            form { A }
            """);
        Assert.Equal(new[] { "p1", "p2" }, Sounding(midi).Select(n => n.Part).Distinct().OrderBy(p => p));
    }

    [Fact]
    public void ABareMidiOnlyRow_Sounds_BesideTheStaves()
    {
        // GRAMMAR §7: a bare part name is played and never engraved (a click track).
        var midi = Export("""
            section A {
              p1 { c'4 d e f | }
              click { c4 c c c | }
            }
            form { A }
            score {
              staff p1
              click
            }
            """);
        Assert.Equal(new[] { "click", "p1" }, Sounding(midi).Select(n => n.Part).Distinct().OrderBy(p => p));
    }

    [Fact]
    public void ASectionThePickedPartSitsOut_StillTakesItsBars()
    {
        // The page pads p2's staff with A's bar; the .mid keeps the same grid — p2's first
        // note is at bar 2, not at tick 0.
        var midi = Export("""
            section A { p1 { c'4 d e f | } }
            section B { p2 { g'1 | } }
            form { A B }
            score { staff p1  staff p2 }
            score "p2" { staff p2 }
            """, "p2");
        var notes = Sounding(midi);
        Assert.Single(notes);
        Assert.Equal(("p2", Bar, 67), notes[0]);
    }

    [Fact]
    public void TheWordsOfAPartThatDoesNotSound_LeaveWithItsNotes()
    {
        const string sung = """
            section A {
              p1 { c'4 d e f | }
              lyrics words { la la la la | }
            }
            section B { p2 { g'1 | } }
            form { A B }
            score { staff p1 with lyrics words  staff p2 }
            score "p2" { staff p2 }
            """;
        Assert.Equal(4, Export(sung, "main").Tracks.Sum(t => t.Lyrics.Count));
        Assert.Equal(0, Export(sung, "p2").Tracks.Sum(t => t.Lyrics.Count));
    }

    [Fact]
    public void TheCapo_IsThePlayedScores_NotTheFirstScores()
    {
        // x32010 is C major open; under the main score's capo 3 it sounds E♭ major, and the
        // "open" score, which references a layout with no capo, sounds C — it used to sound
        // the first score's E♭ whichever score was written (HANDOFF §1.0 ⒜ capo hole).
        const string book = """
            layout capo3 { chordDiagrams guitar capo 3 }
            layout open { chordDiagrams guitar }
            part gt { instrument guitar }
            section A { gt { chord(C x32010)1 | } }
            form { A }
            score { layout capo3  staff gt }
            score "open" { layout open  staff gt }
            """;
        static int[] Pitches(MidiFile midi) => midi.Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { 51, 55, 58, 63, 67 }, Pitches(Export(book, "main")));   // E♭3 G3 B♭3 E♭4 G4
        Assert.Equal(new[] { 48, 52, 55, 60, 64 }, Pitches(Export(book, "open")));   // C3 E3 G3 C4 E4
    }

    [Fact]
    public void AChordRowThePickedScorePlaces_StillSounds()
    {
        // The strip tells a row's notes ("harmony (chords)") from a part's: the row the
        // p2 score places sounds with p2, and p1 alone is gone.
        var midi = Export("""
            time 4/4
            section A {
              p1 { c'4 d e f | }
              p2 { e f g a | }
              chords harmony { C | }
            }
            form { A }
            score { chords harmony  staff p1  staff p2 }
            score "p2" { chords harmony  staff p2 }
            """, "p2");
        Assert.Equal(new[] { "harmony (chords)", "p2" },
            Sounding(midi).Select(n => n.Part).Distinct().OrderBy(p => p));
    }
}
