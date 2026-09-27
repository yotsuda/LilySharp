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

using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord (and an arpeggio) is ONE item of the relative chain: the note after it is relative
/// to its ANCHOR — the first member's bare letter, or the key tonic for a degree chord — plus
/// the marks after <c>&gt;</c> (user decision, 2026-09-27; GRAMMAR.md "Chord/arpeggio
/// OCTAVES"). The owner's report: in <c>g1 | &lt;c e g&gt;1 | &lt;f a c&gt;1</c>, rewriting the
/// g as f left the first chord where it was and moved the second an octave, because a chord
/// then handed the frame on untouched and the second chord read the note two items back.
/// Every case is asked of the three readers that resolve pitches on their own — the page,
/// MIDI and MusicXML — since they have disagreed about this sentence before (session 394
/// changed two of four and left the other two for a day).
/// </summary>
public sealed class ChordRelativeFrameTests
{
    [Theory]
    // The report, both spellings: the second chord is F4 A4 C5 either way.
    [InlineData("g1 <c e g>1 <f a c>1", new[] { 55, 60, 64, 67, 65, 69, 72 })]
    [InlineData("f1 <c e g>1 <f a c>1", new[] { 65, 60, 64, 67, 65, 69, 72 })]
    // The anchor is the BARE letter: a dropped root repeats without falling, and a note
    // between two copies does not part them.
    [InlineData("<c, e g>4 <c, e g> <c, e g>", new[] { 48, 64, 67, 48, 64, 67, 48, 64, 67 })]
    [InlineData("a4 <c, e g> b <c, e g>", new[] { 57, 48, 64, 67, 59, 48, 64, 67 })]
    // A root's own mark stays local: the next bare c reads the bare anchor.
    [InlineData("<c' e g>4 c4", new[] { 72, 64, 67, 60 })]
    // Marks after '>' move the anchor, so they propagate — and climb when pasted.
    [InlineData("<c e g>'4 c <c e g>'4", new[] { 72, 76, 79, 72, 84, 88, 91 })]
    // A run of chords climbs the way `c d g c` does; `<g b d>,` brings it back.
    [InlineData("<c e g>1 <d f a>1 <g b d>1 <c e g>1", new[] { 60, 64, 67, 62, 65, 69, 67, 71, 74, 72, 76, 79 })]
    [InlineData("<c e g>1 <d f a>1 <g b d>,1 <c e g>1", new[] { 60, 64, 67, 62, 65, 69, 55, 59, 62, 60, 64, 67 })]
    // Degree chords hand on the TONIC, not the first degree written: I–V–I stays put.
    [InlineData("<1 3 5>4 <5 7 2> <1 3 5> c", new[] { 60, 64, 67, 67, 71, 62, 60, 64, 67, 60 })]
    // An arpeggio hands on its anchor too, not its last member; rests only are transparent.
    [InlineData("g4 << c e g >>2 f4", new[] { 55, 60, 64, 67, 65 })]
    [InlineData("<< c, e g >>2 << c, e g >>2", new[] { 48, 64, 67, 48, 64, 67 })]
    [InlineData("g4 << r r >>2 a4", new[] { 55, 57 })]
    // A chord as the arpeggio's root: the later members stack on the CHORD's anchor (C4), so
    // g is G4 — under the 2026-09-16 rule the chord handed back the incoming G3 and the g
    // fell below the chord it was stacked on.
    [InlineData("g4 << <c e> g >>2 r4", new[] { 55, 60, 64, 67 })]
    public void TheNoteAfterAChord_ReadsTheChordsAnchor(string music, int[] expected)
    {
        Assert.Equal(expected, PagePitches(music));
        Assert.Equal(expected, MidiPitches(music));
        Assert.Equal(expected, XmlPitches(music));
    }

    private static int[] PagePitches(string music)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(music), null);
        var pitches = new List<int>();
        foreach (var item in score.Voice.Measures.SelectMany(m => m.Items))
        {
            if (item is NoteItem n)
                pitches.Add(n.Midi);
            else if (item is ChordItem c)
                pitches.AddRange(c.Notes.Select(x => x.Midi));
        }
        return pitches.ToArray();
    }

    // Written order within a chord, onset order across events.
    private static int[] MidiPitches(string music) =>
        new MidiExporter().Export(SyntaxTree.Parse(music)).Tracks[1].Notes
            .Select((n, i) => (n, i)).OrderBy(t => t.n.StartTick).ThenBy(t => t.i)
            .Select(t => t.n.Pitch).ToArray();

    private static int[] XmlPitches(string music) =>
        new MusicXmlExporter().Export(SyntaxTree.Parse(music)).Parts[0].Measures
            .SelectMany(m => m.Notes).Where(n => !n.IsRest && n.Step != null)
            .Select(n => 12 * (n.Octave!.Value + 1) + "C D EF G A B".IndexOf(n.Step![0]) + (int)(n.Alter ?? 0))
            .ToArray();
}
