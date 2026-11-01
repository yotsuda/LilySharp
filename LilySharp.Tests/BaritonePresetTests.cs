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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>instrument baritone</c> (owner's decision 2026-11-01): the untrained man's voice, G2–G4, for
/// a song to sing and play yourself — a tenor's C5 let such a tune go where the singer cannot. It
/// reads the tenor's octave-down treble clef and sings as the other voices do.
/// </summary>
[Trait("Category", "Unit")]
public class BaritonePresetTests
{
    private static string Book(string notes) =>
        $"octave absolute\npart v {{ instrument baritone }}\nsection A {{ v {{ {notes} }} }}\nform {{ A }}\nscore {{ staff v }}\n";

    [Fact]
    public void Baritone_IsAVoicePreset_G2ToG4()
    {
        Assert.Contains("baritone", InstrumentDefaults.KnownInstruments);
        Assert.Equal((43, 67), InstrumentDefaults.SoundingRange("baritone"));
        Assert.Equal("Baritone", InstrumentDefaults.PrintedName("baritone"));
        Assert.Equal(InstrumentDefaults.GetMidiProgram("tenor"), InstrumentDefaults.GetMidiProgram("baritone"));
        Assert.Equal(InstrumentDefaults.GetDefaults("tenor"), InstrumentDefaults.GetDefaults("baritone"));
    }

    [Fact]
    public void ANoteAboveG4_IsOutOfTheBaritonesRange()
    {
        // Absolute octaves: `g,` is G2, `g'` G4, `a'` A4 — one step over the top.
        var tree = SyntaxTree.Parse(Book("g,4 d4 g'4 a'4 |"));
        Assert.False(tree.HasErrors);
        var outOfRange = SemanticValidation.Run(tree).Where(d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange).ToList();
        var one = Assert.Single(outOfRange);
        Assert.Contains("A4", one.Message);
        Assert.Contains("Baritone", one.Message);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(Book("g,4 d4 g'4 e'4 |"))).Where(d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange));
    }
}
