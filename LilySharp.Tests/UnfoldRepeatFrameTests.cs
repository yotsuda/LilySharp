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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>repeat unfold N { … }</c> means "play this N times", so all N copies are the same
/// music — in the engraving, in the MIDI and in the MusicXML alike.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ WHAT MAKES THIS A GRAMMAR DECISION rather than a defect report: until 2026-08-17 the
/// page and the MIDI AGREED — both climbed — because each copy re-entered the body with the
/// frame the last one left, and `g''` counts from the nearest g. Two outputs saying the same
/// thing is what a decision looks like from inside; the tie-breaker was LilyPond, which
/// resolves the relative chain once and copies the RESULT, so its four copies are identical.
/// Decided 2026-08-17 (HANDOFF §3) to follow it.
/// </para>
/// <para>
/// ⚠️ THE CONTROL IS CHOSEN BY STRUCTURE, not by "this one looks like it will not move"
/// (RULES §5.4): <c>octave absolute</c> has no frame to carry, so the poison cannot reach it
/// however the body is spelt. A control the poison DOES reach is not a control.
/// </para>
/// <para>
/// ⚠️ Reach, measured 2026-08-17 over the 566 books: ONE real <c>repeat unfold</c> site
/// exists (`samples/canon-in-d.lys`, `repeat unfold 13 { ground }`), and a phrase reference
/// opens its own frame — so no book in the tree changes and no snapshot moves. The rule had
/// no observer at all before this file.
/// </para>
/// </remarks>
public class UnfoldRepeatFrameTests
{
    private static string Book(string body, string directives = "") => $$"""
        {{directives}}
        part m { clef treble }
        section A { m { {{body}} } }
        form main { A }
        score main { staff m }
        """;

    private static int[] MidiPitches(string lys)
        => new MidiExporter().Export(SyntaxTree.Parse(lys))
            .Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).ToArray();

    private static int[] XmlPitches(string lys)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).Parts
            .SelectMany(p => p.Measures).SelectMany(m => m.Notes)
            .Where(n => n.Step != null && n.Octave is int)
            .Select(n => RelativeOctave.StepToMidi(
                "CDEFGAB".IndexOf(n.Step![0]), (int)(n.Alter ?? 0), n.Octave!.Value))
            .ToArray();

    private static int[] PageStaffPositions(string lys)
    {
        var tree = SyntaxTree.Parse(lys);
        var score = SvgGenerator.CollectScore(new MeasureCollector(), tree,
            RenderSpecParser.FindAll(tree).FirstOrDefault());
        var positions = new List<int>();
        foreach (var st in score.EnumerateStaves())
            foreach (var v in st.Staff.Voices)
                foreach (var m in v.Measures)
                    foreach (var it in m.Items)
                        if (it is NoteItem n)
                            positions.Add(n.StaffPosition);
        return positions.ToArray();
    }

    // `g''` counts from the NEAREST g, so a body that ends a step above where it started
    // used to re-enter two octaves up. Four copies, one pitch pair.
    private const string Climber = "repeat unfold 4 { g''8 a }";

    [Fact]
    public void EveryCopySoundsTheSamePitches()
    {
        int[] pitches = MidiPitches(Book(Climber));

        Assert.Equal(8, pitches.Length);
        Assert.Equal(pitches.Take(2), pitches.Skip(2).Take(2));
        Assert.Equal(pitches.Take(2), pitches.Skip(4).Take(2));
        Assert.Equal(pitches.Take(2), pitches.Skip(6).Take(2));
        // …and inside the range, which is the symptom that made it visible: the fourth copy
        // used to ask for key 153 and be pinned to 127.
        Assert.All(pitches, p => Assert.InRange(p, 0, 127));
    }

    [Fact]
    public void EveryCopyIsEngravedOnTheSameLines()
    {
        int[] page = PageStaffPositions(Book(Climber));

        Assert.Equal(8, page.Length);
        Assert.Equal(page.Take(2), page.Skip(6).Take(2));
        // Two staff positions in the whole line — the pair, four times over. Four rising
        // copies drew eight.
        Assert.Equal(2, page.Distinct().Count());
    }

    [Fact]
    public void TheMusicXmlWritesTheSameCopyToo()
    {
        int[] xml = XmlPitches(Book(Climber));

        Assert.Equal(8, xml.Length);
        Assert.Equal(xml.Take(2), xml.Skip(6).Take(2));
    }

    /// <summary>The three outputs agree — which is the point of the rule, and was equally
    /// true while all three were wrong, so it is asserted alongside the pitches and never
    /// instead of them.</summary>
    [Fact]
    public void ThePageTheMidiAndTheMusicXmlAgree()
    {
        string book = Book(Climber);
        Assert.Equal(MidiPitches(book), XmlPitches(book));
    }

    /// <summary>
    /// The control: <c>octave absolute</c> carries no frame between copies, so the rule has
    /// nothing to do and the copies were already identical.
    /// </summary>
    [Fact]
    public void AnAbsoluteBookIsUntouched()
    {
        int[] pitches = MidiPitches(
            Book("repeat unfold 4 { g''8 a'' }", directives: "octave absolute"));

        Assert.Equal(8, pitches.Length);
        Assert.Single(pitches.Chunk(2).Select(c => string.Join(',', c)).Distinct());
    }

    /// <summary>
    /// The duration default is part of the frame: the second copy of <c>{ c4 d }</c> is two
    /// quarters, not whatever the previous copy's last note left running.
    /// </summary>
    [Fact]
    public void TheDurationDefaultRestartsWithTheFrame()
    {
        var tree = SyntaxTree.Parse(Book("repeat unfold 2 { c4 d2 } |"));
        var notes = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).ToList();

        Assert.Equal(4, notes.Count);
        // note 3 opens the second copy: a bare `c` after a half note has to be a QUARTER
        // again, so its length matches note 1 and not note 2.
        Assert.Equal(notes[0].DurationTicks, notes[2].DurationTicks);
        Assert.NotEqual(notes[1].DurationTicks, notes[2].DurationTicks);
    }
}
