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
using LilySharp.Core.Editing;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>strokes NAME { … }</c> part (owner's design 2026-11-01: a song to sing and play yourself
/// whose chords sounded only on beat 1): a rhythm of slashes and no pitch, placed as a one-line
/// staff by <c>strokes NAME</c> in the score or by <c>chords NAME plays STROKES</c> under a chords
/// row, which then STRIKES the row's chords at the strokes in the MIDI. A row without
/// <c>plays</c> keeps holding each chord (a keyboard's reading, the owner's choice that day).
/// </summary>
[Trait("Category", "Unit")]
public class StrokesTests
{
    private const int Bar = 1920;

    private const string Song = """
        time 4/4
        octave absolute
        part singer { instrument alto }
        strokes rhythm { section A { /4 8 8 4 4 | % | } }
        chords harmony { section A { C | G | } }
        section A {
          singer { c'4 d'4 e'4 f'4 | g'1 | }
          lyrics words sings singer { a b c d | e | }
          lyrics strum sings rhythm { ka e ri mi chi | % | }
        }
        form { A }
        score {
          staff singer
          lyrics words
          chords harmony plays rhythm
          lyrics strum
        }
        """;

    private static SyntaxTree Parse(string lys) => SyntaxTree.Parse(lys);

    private static (int Start, int Length)[] ChordStrikes(MidiFile midi, string row = "harmony")
        => midi.Tracks.Where(t => t.Name == row + " (chords)").SelectMany(t => t.Notes)
            .GroupBy(n => (n.StartTick, n.DurationTicks)).Select(g => g.Key).OrderBy(s => s.StartTick).ToArray();

    [Fact]
    public void AStrokesPart_ParsesAsAPart_ThatIsStrokes()
    {
        var tree = Parse(Song);
        var parts = tree.GetRoot().DescendantNodes<PartDeclarationSyntax>().ToList();
        Assert.True(parts.Single(p => p.Name.Text == "rhythm").IsStrokes);
        Assert.False(parts.Single(p => p.Name.Text == "singer").IsStrokes);
        Assert.Empty(tree.Diagnostics);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void APitchInAStrokesPart_IsRefused()
    {
        var tree = Parse("""
            part singer
            strokes rhythm { section A { /4 c'8 8 4 4 | <c e g>1 | } }
            section A { singer { c'1 | c'1 | } }
            form { A }
            score { staff singer  strokes rhythm }
            """);
        var errors = SemanticValidation.Run(tree).Where(d => d.Code == DiagnosticCodes.StrokesHoldNoPitch).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains("'rhythm' is a strokes part and holds no pitches", errors[0].Message);
    }

    [Fact]
    public void AChordsRowThatPlaysStrokes_PutsAOneLineStaffUnderTheRow()
    {
        var spec = RenderSpecParser.FindFirst(Parse(Song))!;
        // Each row is folded into the staff beside it (RenderSpecParser.FoldAdjacentRows): the
        // words under the singer, the chords row and the strum's words round the strokes' staff.
        Assert.Equal(2, spec.Items.Length);
        var staff = Assert.IsType<SingleStaffSpec>(spec.Items[1]).Staff;
        Assert.Equal("rhythm", staff.VoiceName);
        Assert.Equal(1, staff.Lines);
        Assert.True(staff.NameSuppressed);
        Assert.Equal("rhythm", staff.ChordPlays);
        Assert.Equal("harmony", staff.WithChords);
        Assert.Equal("strum", Assert.Single(staff.WithLyrics));
    }

    [Fact]
    public void AStrokesItemInTheScore_IsAOneLineStaff_WithNoName()
    {
        var spec = RenderSpecParser.FindFirst(Parse("""
            part singer
            strokes rhythm { section A { /4 4 4 4 | } }
            section A { singer { c'1 | } }
            form { A }
            score { staff singer  strokes rhythm }
            """))!;
        var staff = Assert.IsType<SingleStaffSpec>(spec.Items[1]).Staff;
        Assert.Equal(1, staff.Lines);
        Assert.True(staff.NameSuppressed);
        Assert.Null(staff.ChordPlays);
    }

    [Fact]
    public void ARowThatPlaysStrokes_StrikesItsChordAtEachStroke()
    {
        var midi = new MidiExporter().Export(Parse(Song));
        Assert.Equal(new[]
        {
            (0, 480), (480, 240), (720, 240), (960, 480), (1440, 480),
            (Bar, 480), (Bar + 480, 240), (Bar + 720, 240), (Bar + 960, 480), (Bar + 1440, 480),
        }, ChordStrikes(midi));
        // The singer still sings: the by-part strokes declared above the by-section section did
        // not take its place as the section's body (MeasureCollector.Definitions, 2026-11-01).
        Assert.Equal(5, midi.Tracks.Single(t => t.Name == "singer").Notes.Count);
    }

    [Fact]
    public void ARowWithoutPlays_HoldsEachChord()
    {
        var midi = new MidiExporter().Export(Parse(Song.Replace(" plays rhythm", "").Replace("lyrics strum\n", "")));
        Assert.Equal(new[] { (0, Bar), (Bar, Bar) }, ChordStrikes(midi));
    }

    [Fact]
    public void TheTwin_WritesTheStrokesAsAOneLineStaffOfSlashes()
    {
        string ly = new LilyPondExporter().Export(Parse(Song));
        Assert.Contains("\\override StaffSymbol.line-count = #1", ly);
        Assert.Contains("\\improvisationOn", ly);
        Assert.Contains("\\new ChordNames \\harmonyChords", ly);
    }

    [Fact]
    public void ACompositionCheck_DoesNotAskAnInstrumentOfAStrokesPart()
    {
        var findings = CompositionChecks.Of(Song.Replace("part singer { instrument alto }", "part singer { instrument alto }\npart bass { instrument bass }")
            .Replace("singer { c'4 d'4 e'4 f'4 | g'1 | }", "singer { c'4 d'4 e'4 f'4 | g'1 | } bass { c2 g | c1 | }")
            .Replace("staff singer\n", "staff singer\n  staff bass\n"));
        Assert.DoesNotContain(findings, f => f.StartsWith("No instrument for"));
    }
}
