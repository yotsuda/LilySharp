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

using Xunit;
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class OctaveTests
{
    private static List<NoteItem> CollectNotes(string source, string? voiceName = null)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, voiceName);
        return score.Voice.Measures
            .SelectMany(m => m.Items.OfType<NoteItem>())
            .ToList();
    }

    [Fact]
    public void RelativeOctave_StepwiseAscending_StaysInSameOctave()
    {
        // c d e f in treble clef: all stay in octave 4
        // Treble: staffPosition 0 = b4, so c4 = -6, d4 = -5, e4 = -4, f4 = -3
        // The music lives inside a part now (top-level music is LYS0020). The wrapper
        // declares no clef, so the relative-octave anchor is still the file default —
        // treble, octave 4 — exactly as when this was a bare note stream.
        var notes = CollectNotes(MusicSource.Wrap("c4 d e f |"));
        Assert.Equal(4, notes.Count);

        // Each note should be higher than the previous (stepwise ascending)
        for (int i = 1; i < notes.Count; i++)
        {
            Assert.True(notes[i].StaffPosition > notes[i - 1].StaffPosition,
                $"Note {i} (pos={notes[i].StaffPosition}) should be higher than note {i - 1} (pos={notes[i - 1].StaffPosition})");
        }
    }

    [Fact]
    public void RelativeOctave_StepwiseDescending_StaysInSameOctave()
    {
        // f e d c: all stay in same octave (stepwise)
        var notes = CollectNotes(MusicSource.Wrap("f4 e d c |"));
        Assert.Equal(4, notes.Count);

        for (int i = 1; i < notes.Count; i++)
        {
            Assert.True(notes[i].StaffPosition < notes[i - 1].StaffPosition,
                $"Note {i} should be lower than note {i - 1}");
        }
    }

    [Fact]
    public void RelativeOctave_FifthJump_AdjustsOctaveDown()
    {
        // c4 then f: interval c->f = +3 (within range), stays same octave
        // c4 then g: interval c->g = +4 (> 3), so octave adjusts down
        // In relative mode, g after c should go DOWN (g3, not g4)
        var notes = CollectNotes(MusicSource.Wrap("c4 g |"));
        Assert.Equal(2, notes.Count);

        // g should be LOWER than c (jumped down)
        Assert.True(notes[1].StaffPosition < notes[0].StaffPosition,
            $"g after c should jump down: g(pos={notes[1].StaffPosition}) should be < c(pos={notes[0].StaffPosition})");
    }

    [Fact]
    public void RelativeOctave_OctaveMarkUp_ForcesHigherOctave()
    {
        // c then g': the ' forces g UP even though interval > 3
        var notes = CollectNotes(MusicSource.Wrap("c4 g' |"));
        Assert.Equal(2, notes.Count);

        Assert.True(notes[1].StaffPosition > notes[0].StaffPosition,
            $"g' after c should be higher: g'(pos={notes[1].StaffPosition}) should be > c(pos={notes[0].StaffPosition})");
    }

    [Fact]
    public void RelativeOctave_OctaveMarkDown_ForcesLowerOctave()
    {
        // c then d,: the , forces d DOWN
        var notes = CollectNotes(MusicSource.Wrap("c4 d, |"));
        Assert.Equal(2, notes.Count);

        Assert.True(notes[1].StaffPosition < notes[0].StaffPosition,
            $"d, after c should be lower: d,(pos={notes[1].StaffPosition}) should be < c(pos={notes[0].StaffPosition})");
    }

    [Fact]
    public void BassClef_StartsAtOctave3()
    {
        // With bass clef part, initial octave should be 3
        // Bass clef: staffPosition 0 = d3
        var source = @"
part bassline { clef bass }
section A {
    bassline { c4 d e f | }
}
form main { A }
score main ""test"" {
    staff bass bassline
}
";
        var notes = CollectNotes(source, "bassline");
        Assert.Equal(4, notes.Count);

        // c3 in bass clef: staffPosition = pitchIndex(c) - pitchIndex(d) + (3-3)*7 = 0 - 1 = -1
        Assert.Equal(-1, notes[0].StaffPosition);
    }

    [Fact]
    public void TrebleClef_StartsAtOctave4()
    {
        // Default treble clef, initial octave is 4
        // c4 in treble clef: staffPosition = pitchIndex(c) - pitchIndex(b) + (4-4)*7 = 0 - 6 = -6
        var notes = CollectNotes(MusicSource.Wrap("c4 |"));
        Assert.Single(notes);
        Assert.Equal(-6, notes[0].StaffPosition);
    }

    [Fact]
    public void PartWithInstrument_SetsClefAndOctave()
    {
        // Instrument "cello" should set clef=bass, octave=3
        var source = @"
part cellopart { instrument cello }
section A {
    cellopart { c4 d e f | }
}
form main { A }
score main ""test"" {
    staff bass cellopart
}
";
        var notes = CollectNotes(source, "cellopart");
        Assert.Equal(4, notes.Count);

        // c3 in bass clef: staffPosition = -1
        Assert.Equal(-1, notes[0].StaffPosition);
    }

    [Fact]
    public void InstrumentDefaults_Violin_TrebleOctave4()
    {
        var (clef, octave) = InstrumentDefaults.GetDefaults("violin");
        Assert.Equal(ClefType.Treble, clef);
        Assert.Equal(4, octave);
    }

    [Fact]
    public void InstrumentDefaults_Cello_BassOctave3()
    {
        var (clef, octave) = InstrumentDefaults.GetDefaults("cello");
        Assert.Equal(ClefType.Bass, clef);
        Assert.Equal(3, octave);
    }

    [Fact]
    public void InstrumentDefaults_Guitar_Treble8BelowOctave4()
    {
        var (clef, octave) = InstrumentDefaults.GetDefaults("guitar");
        Assert.Equal(ClefType.Treble8Below, clef);
        Assert.Equal(4, octave);
    }

    [Fact]
    public void InstrumentDefaults_Flute_TrebleOctave5()
    {
        var (clef, octave) = InstrumentDefaults.GetDefaults("flute");
        Assert.Equal(ClefType.Treble, clef);
        Assert.Equal(5, octave);
    }

    [Fact]
    public void InstrumentDefaults_Tuba_BassOctave2()
    {
        var (clef, octave) = InstrumentDefaults.GetDefaults("tuba");
        Assert.Equal(ClefType.Bass, clef);
        Assert.Equal(2, octave);
    }

    [Fact]
    public void InstrumentDefaults_Unknown_DefaultsTrebleOctave4()
    {
        var (clef, octave) = InstrumentDefaults.GetDefaults("theremin");
        Assert.Equal(ClefType.Treble, clef);
        Assert.Equal(4, octave);
    }

    [Fact]
    public void InstrumentDefaults_CaseInsensitive()
    {
        var (clef, octave) = InstrumentDefaults.GetDefaults("VIOLIN");
        Assert.Equal(ClefType.Treble, clef);
        Assert.Equal(4, octave);
    }

    [Fact]
    public void ClefDefaultOctave_Treble_Is4()
    {
        Assert.Equal(4, InstrumentDefaults.GetDefaultOctave(ClefType.Treble));
    }

    [Fact]
    public void ClefDefaultOctave_Bass_Is3()
    {
        Assert.Equal(3, InstrumentDefaults.GetDefaultOctave(ClefType.Bass));
    }

    [Fact]
    public void ClefDefaultOctave_Alto_Is3()
    {
        Assert.Equal(3, InstrumentDefaults.GetDefaultOctave(ClefType.Alto));
    }

    [Theory]
    [InlineData("violin", true)]
    [InlineData("cello", true)]
    [InlineData("guitar", true)]
    [InlineData("piano-right", true)]
    [InlineData("theremin", false)]
    [InlineData("synthesizer", false)]
    public void IsKnownInstrument_ReturnsCorrectly(string name, bool expected)
    {
        Assert.Equal(expected, InstrumentDefaults.IsKnownInstrument(name));
    }

    // --- Tests for instrument-based octave (not just clef-based) ---

    [Fact]
    public void GuitarPart_StartsAtOctave4_WithTreble8Clef()
    {
        // Guitar: treble_8 clef, octave=4, so c4 in treble_8 clef
        // staffPosition = pitchIndex(c) - pitchIndex(b) + (4-4)*7 = 0 - 6 = -6
        var source = @"
part lead { instrument guitar }
section A {
    lead { c4 d e f | }
}
form main { A }
score main ""test"" {
    staff treble_8 lead
}
";
        var notes = CollectNotes(source, "lead");
        Assert.Equal(4, notes.Count);
        Assert.Equal(-6, notes[0].StaffPosition);  // c4 in treble_8 clef
    }

    [Fact]
    public void FlutePart_StartsAtOctave5()
    {
        // Flute: treble clef, octave=5
        // c5 in treble clef: staffPosition = pitchIndex(c) - pitchIndex(b) + (5-4)*7 = 0 - 6 + 7 = 1
        var source = @"
part fl { instrument flute }
section A {
    fl { c4 d e f | }
}
form main { A }
score main ""test"" {
    staff treble fl
}
";
        var notes = CollectNotes(source, "fl");
        Assert.Equal(4, notes.Count);
        Assert.Equal(1, notes[0].StaffPosition);  // c5 in treble clef
    }

    // --- Tests for explicit octave attribute ---

    [Fact]
    public void ExplicitOctaveAttribute_OverridesInstrumentDefault()
    {
        // violin defaults to octave=4, but explicit octave: 5 overrides
        // c5 in treble clef: staffPosition = 0 - 6 + (5-4)*7 = 1
        var source = @"
part high { instrument violin, octave 5 }
section A {
    high { c4 d e f | }
}
form main { A }
score main ""test"" {
    staff treble high
}
";
        var notes = CollectNotes(source, "high");
        Assert.Equal(4, notes.Count);
        Assert.Equal(1, notes[0].StaffPosition);  // c5 in treble clef
    }

    // --- Tests for section octave reset ---

    [Fact]
    public void SectionBoundary_ResetsOctaveToInitial()
    {
        // After section A ends high, section B should reset to initial octave
        var source = @"
section A {
    melody { c4 d e f | g a b c' | }
}
section B {
    melody { c4 d e f | }
}
form main { A B }
score main ""test"" {
    staff treble melody
}
";
        var notes = CollectNotes(source, "melody");

        // Section A: 8 notes, Section B: 4 notes
        Assert.Equal(12, notes.Count);

        // First note of section A: c4 (staffPosition = -6)
        Assert.Equal(-6, notes[0].StaffPosition);

        // First note of section B should also be c4 (reset), not c5
        Assert.Equal(-6, notes[8].StaffPosition);
    }

    [Fact]
    public void SectionBoundary_ResetsOctaveForBassClef()
    {
        var source = @"
part bassline { clef bass }
section A {
    bassline { c4 d e f | g a b c' | }
}
section B {
    bassline { c4 d e f | }
}
form main { A B }
score main ""test"" {
    staff bass bassline
}
";
        var notes = CollectNotes(source, "bassline");
        Assert.Equal(12, notes.Count);

        // c3 in bass clef: staffPosition = -1
        Assert.Equal(-1, notes[0].StaffPosition);

        // First note of section B should reset to c3
        Assert.Equal(-1, notes[8].StaffPosition);
    }

    [Fact]
    public void RelativeOctave_FourthInterval_StaysSameOctave()
    {
        // c->f: interval = 3 (not > 3), should stay in same octave (f4, not f3)
        // This is the boundary case: interval=3 stays, interval=4 (fifth) jumps
        var notes = CollectNotes(MusicSource.Wrap("c4 f |"));
        Assert.Equal(2, notes.Count);

        // f should be HIGHER than c (same octave, ascending fourth)
        Assert.True(notes[1].StaffPosition > notes[0].StaffPosition,
            $"f after c should stay same octave f(pos={notes[1].StaffPosition}) should be > c(pos={notes[0].StaffPosition})");
    }

    [Fact]
    public void RelativeOctave_FourthDown_StaysSameOctave()
    {
        // f->c: interval = -3 (not < -3), should stay in same octave
        // f4->c4 (descending fourth)
        var notes = CollectNotes(MusicSource.Wrap("f4 c |"));
        Assert.Equal(2, notes.Count);

        // c should be LOWER than f (same octave, descending fourth)
        Assert.True(notes[1].StaffPosition < notes[0].StaffPosition,
            $"c after f should stay same octave c(pos={notes[1].StaffPosition}) should be < f(pos={notes[0].StaffPosition})");
    }

    [Fact]
    public void ClefDefaultOctave_Treble8Below_Is4()
    {
        Assert.Equal(4, InstrumentDefaults.GetDefaultOctave(ClefType.Treble8Below));
    }

    [Fact]
    public void Treble8Clef_ParsedCorrectly()
    {
        // Verify treble_8 is lexed and parsed as a clef keyword
        var source = @"
part melody { clef treble_8 }
section A {
    melody { c4 d e f | }
}
form main { A }
score main ""test"" {
    staff treble_8 melody
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree, "melody");
        var notes = score.Voice.Measures
            .SelectMany(m => m.Items.OfType<NoteItem>())
            .ToList();
        Assert.Equal(4, notes.Count);
        // treble_8 clef, default octave 4: c4 staffPosition = -6
        Assert.Equal(-6, notes[0].StaffPosition);
    }

    [Fact]
    public void InstrumentDefaults_Tenor_Treble8BelowOctave4()
    {
        var (clef, octave) = InstrumentDefaults.GetDefaults("tenor");
        Assert.Equal(ClefType.Treble8Below, clef);
        Assert.Equal(4, octave);
    }
}
