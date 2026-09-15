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
using System.Xml.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A clef is drawing only: wherever it is written — a part header, mid-music, a cue, a
/// score's staff item — it moves no pitch, asked of ALL FOUR outputs at once.
/// </summary>
/// <remarks>
/// <para>
/// THE RULE, decided 2026-09-15 (reversing 2026-08-17): the relative anchor is
/// <c>octave N</c> &gt; the <c>instrument</c> preset's octave &gt; 4, and the clef is not a
/// step of it. The old rule re-anchored at the clef's own octave but kept the previous
/// letter, so <c>g'4 | clef bass b</c> read B3 while <c>c''4 | clef bass b</c> read B2, and a
/// staff item's clef could not be followed by the MIDI at all. LilyPond's <c>\relative</c>
/// never looks at a clef, so the twin now writes the source's own marks.
/// </para>
/// <para>
/// ⚠️ EVERY CASE IS A PAIR against the same music with the clef removed, and asks every
/// output: the defect this replaces survived because each output was green about its own
/// answer.
/// </para>
/// </remarks>
public class ClefMovesNoPitchTests
{
    private static int[] PagePitches(string src)
        => (ResolvedPitches.ForFile(SyntaxTree.Parse(src)) ?? [])
            .Select(p => RelativeOctave.StepToMidi(
                "CDEFGAB".IndexOf(p.Pitch[0]),
                p.Pitch.Skip(1).TakeWhile(c => c is '#' or 'b' or 'x')
                    .Sum(c => c == '#' ? 1 : c == 'x' ? 2 : -1),
                int.Parse(new string(p.Pitch.SkipWhile(c => !char.IsDigit(c)).ToArray()))))
            .ToArray();

    private static int[] MidiPitches(string src)
        => new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).Select(n => n.Pitch).ToArray();

    private static int[] XmlPitches(string src)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(src)).ToXml().Descendants("pitch")
            .Select(p => RelativeOctave.StepToMidi(
                "CDEFGAB".IndexOf(p.Element("step")!.Value[0]),
                (int)(double.Parse(p.Element("alter")?.Value ?? "0")),
                int.Parse(p.Element("octave")!.Value)))
            .ToArray();

    private static string Twin(string src) => new LilyPondExporter().Export(SyntaxTree.Parse(src));

    private static void AllOutputsAre(int[] expected, string src)
    {
        Assert.Equal(expected, PagePitches(src));
        Assert.Equal(expected, MidiPitches(src));
        Assert.Equal(expected, XmlPitches(src));
    }

    private static string Book(string header, string music, string score = "staff m") => $$"""
        part m { {{header}} }
        section A { m { {{music}} } }
        form main { ~A }
        score main { {{score}} }
        """;

    [Fact]
    public void AMidMusicClef_CarriesTheFrameOnFromTheLastNote()
    {
        // The first bar puts the frame at A5 so the old answer (C3 D3) and this one differ.
        int[] expected = { 72, 74, 76, 77, 79, 81, 72, 74 };
        AllOutputsAre(expected, Book("clef treble", "c'4 d e f | g4 a  c,4 d |"));
        AllOutputsAre(expected, Book("clef treble", "c'4 d e f | g4 a  clef bass  c,4 d |"));
        // The twin writes the source's own comma: LilyPond's \relative ignores the clef too.
        string ly = Twin(Book("clef treble", "c'4 d e f | g4 a  clef bass  c,4 d |"));
        Assert.Contains("\\clef \"bass\" c,4", ly);
        Assert.DoesNotContain("c,,,4", ly);
    }

    [Fact]
    public void TheLetterBeforeTheClef_DecidesNothingTheClefDidNot()
    {
        // The old rule's inconsistency: octave from the clef, letter from the last note.
        AllOutputsAre(PagePitches(Book("", "g'4 r r r | b4 r r r |")),
            Book("", "g'4 r r r | clef bass b4 r r r |"));
        AllOutputsAre(PagePitches(Book("", "c''4 r r r | b4 r r r |")),
            Book("", "c''4 r r r | clef bass b4 r r r |"));
    }

    [Fact]
    public void ACueClef_MovesNoPitch()
    {
        AllOutputsAre(Enumerable.Repeat(60, 12).ToArray(),
            Book("clef treble", "c4 c c c | voice { cue bass { c4 c c c } } { R1 } | c4 c c c |"));
    }

    [Fact]
    public void ASectionBoundaryRestoringTheClef_MovesNoPitch()
    {
        const string withClef = """
            part m { }
            section A { m { e4 f g a | clef bass b c d e | } }
            section B { m { c4 d e f | } }
            form main { ~A ~B }
            score main { staff m }
            """;
        const string noClef = """
            part m { }
            section A { m { e4 f g a | b c d e | } }
            section B { m { c4 d e f | } }
            form main { ~A ~B }
            score main { staff m }
            """;
        AllOutputsAre(PagePitches(noClef), withClef);
    }

    [Theory]
    [InlineData("clef bass")]
    [InlineData("clef alto")]
    [InlineData("clef tenor")]
    public void AHeaderClef_LeavesBareCAtMiddleC(string header)
    {
        AllOutputsAre(new[] { 60, 62, 64, 65 }, Book(header, "c4 d e f |"));
        Assert.Contains("\\relative c' {", Twin(Book(header, "c4 d e f |")));
    }

    [Fact]
    public void AnInstrumentPreset_StillMovesTheRegister_ThePositiveControl()
    {
        // `instrument cello` anchors at octave 3 (and sounds as written).
        AllOutputsAre(new[] { 48, 50, 52, 53 }, Book("instrument cello", "c4 d e f |"));
        // `octave 3` is the other thing that moves it.
        AllOutputsAre(new[] { 48, 50, 52, 53 }, Book("clef bass octave 3", "c4 d e f |"));
    }

    [Fact]
    public void AStaffItemClef_MovesNoPitch()
    {
        AllOutputsAre(new[] { 60, 62, 64, 65 }, Book("", "c4 d e f |", "staff bass m"));
    }
}
