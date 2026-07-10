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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <see cref="NoteItem.Midi"/> (the sounding/display pitch used for tab-fret
/// assignment and note-collision) must reflect the DISPLAYED pitch of a
/// transposed part — including its octave. Pairing the transposed step with the
/// untransposed written octave used to drop octave-crossing transpositions by a
/// full octave.
/// </summary>
public class TransposedMidiPitchTests
{
    private static NoteItem FirstNote(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var voice = score.StaffGroups[0].Staves[0].Voices[0];
        return (NoteItem)voice.Measures[0].Items[0];
    }

    [Fact]
    public void TransposedNote_CrossingOctave_MidiUsesDisplayOctave()
    {
        // transpose g = up a perfect 5th: written f' (F5) displays as c'' (C6),
        // crossing an octave boundary. The transposed note's Midi must equal an
        // untransposed c'' — not C5, the octave-low value produced when the
        // display step was paired with the written octave.
        var transposed = FirstNote("""
            octave absolute
            part m { clef treble transpose g }
            section A { m { f'4 | } }
            form main { A }
            score main { staff m }
            """);
        var control = FirstNote("""
            octave absolute
            part m { clef treble }
            section A { m { c''4 | } }
            form main { A }
            score main { staff m }
            """);

        Assert.Equal(control.Midi, transposed.Midi);
    }
}
