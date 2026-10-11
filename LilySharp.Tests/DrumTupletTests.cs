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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Drum notes inside <c>tuplet { }</c> — a drum fill's common shape. Until 2026-10-11 the
/// tuplet arm of the collector had no case for a drum note, and <c>tuplet 3/2 { sn8 sn sn }</c>
/// drew nothing and took no time, with no diagnostic (the owner's drum-grammar discussion).
/// </summary>
[Trait("Category", "Unit")]
public class DrumTupletTests
{
    private static string Book(string music) => $$"""
        part kit { clef percussion }
        section A { kit { {{music}} } }
        form { ~A }
        score { staff kit }
        """;

    [Fact]
    public void ADrumTriplet_IsDrawn_TakesItsTime_AndKeepsItsMarks()
    {
        var tree = SyntaxTree.Parse(Book("hh8 hh hh hh tuplet 3/2 { sn8 sn hho } r4 |"));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        var items = score.EnumerateStaves().Single().Staff.Voices[0].Measures[0].Items;

        // Four hi-hats, the three triplet notes, the rest — one bar, nothing spilled over.
        Assert.Equal(7, items.OfType<NoteItem>().Count());
        Assert.Single(score.EnumerateStaves().Single().Staff.Voices[0].Measures);
        var triplet = items.OfType<NoteItem>().Skip(4).ToList();
        Assert.All(triplet, n => Assert.Equal(new Fraction(2, 3), n.TimeScale));
        Assert.Single(score.TupletBrackets);
        // The open hi-hat's "○" rides the tuplet note as it rides any other.
        Assert.Contains(score.Articulations, a => a.ItemIndex == 6);
    }

    [Fact]
    public void ADrumTriplet_PlaysThreeEvenNotes_OnTheDrumChannel()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(Book("tuplet 3/2 { sn8 sn sn } r4 r2 |")))
            .Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Equal(3, notes.Count);
        Assert.All(notes, n => Assert.Equal(9, n.Channel));
        int step = notes[1].StartTick - notes[0].StartTick;
        Assert.Equal(step, notes[2].StartTick - notes[1].StartTick);
        // Three triplet eighths take the time of two plain ones.
        var plain = new MidiExporter().Export(SyntaxTree.Parse(Book("sn8 sn r4 r2 |")))
            .Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Equal(2 * (plain[1].StartTick - plain[0].StartTick), 3 * step);
    }
}
