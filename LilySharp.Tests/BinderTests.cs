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
using LilySharp.Core.Semantics;
using LilySharp.Core.Semantics.Binding;
using LilySharp.Core.Semantics.BoundTree;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class BinderTests
{
    [Fact]
    public void Bind_SimpleNotes_ProducesBoundScore()
    {
        var source = "c4 d e f |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();

        var result = binder.Bind(tree, symbols);

        Assert.True(result.Success);
        Assert.Single(result.Voices);
        Assert.Single(result.PrimaryVoice.Measures);
        Assert.Equal(4, result.PrimaryVoice.Measures[0].Items.Length);
    }

    [Fact]
    public void Bind_NotesWithDurations_ResolvesDurations()
    {
        var source = "c4 d8 e16 f2 |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();

        var result = binder.Bind(tree, symbols);
        var measure = result.PrimaryVoice.Measures[0];

        var note0 = (BoundNote)measure.Items[0];
        var note1 = (BoundNote)measure.Items[1];
        var note2 = (BoundNote)measure.Items[2];
        var note3 = (BoundNote)measure.Items[3];

        Assert.Equal(new Fraction(1, 4), note0.BaseDuration);
        Assert.Equal(new Fraction(1, 8), note1.BaseDuration);
        Assert.Equal(new Fraction(1, 16), note2.BaseDuration);
        Assert.Equal(new Fraction(1, 2), note3.BaseDuration);
    }

    [Fact]
    public void Bind_Rests_ProducesBoundRests()
    {
        var source = "c4 r4 d4 r8 |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();

        var result = binder.Bind(tree, symbols);
        var measure = result.PrimaryVoice.Measures[0];

        Assert.IsType<BoundNote>(measure.Items[0]);
        Assert.IsType<BoundRest>(measure.Items[1]);
        Assert.IsType<BoundNote>(measure.Items[2]);
        Assert.IsType<BoundRest>(measure.Items[3]);
    }

    [Fact]
    public void Bind_Chord_ProducesBoundChord()
    {
        var source = "<c e g>4 |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();

        var result = binder.Bind(tree, symbols);
        var measure = result.PrimaryVoice.Measures[0];

        Assert.Single(measure.Items);
        var chord = Assert.IsType<BoundChord>(measure.Items[0]);
        Assert.Equal(3, chord.Notes.Count);
    }

    [Fact]
    public void Bind_MultipleMeasures_CreatesSeparateMeasures()
    {
        var source = "c4 d e f | g a b c' |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();

        var result = binder.Bind(tree, symbols);

        Assert.Equal(2, result.PrimaryVoice.Measures.Length);
        Assert.Equal(4, result.PrimaryVoice.Measures[0].Items.Length);
        Assert.Equal(4, result.PrimaryVoice.Measures[1].Items.Length);
    }

    [Fact]
    public void Bind_ExtractsMetadata()
    {
        var source = @"
title ""Test Song""
composer ""Test Composer""
tempo 120
time 3/4
key g major
c4 d e |";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();

        var result = binder.Bind(tree, symbols);

        Assert.Equal("Test Song", result.Metadata.Title);
        Assert.Equal("Test Composer", result.Metadata.Composer);
        Assert.Equal(120, result.Metadata.Tempo);
        Assert.Equal(3, result.Metadata.TimeSignature.Beats);
        Assert.Equal(4, result.Metadata.TimeSignature.BeatType);
        Assert.Equal(1, result.Metadata.KeySignature.Sharps); // G major = 1 sharp
    }

    [Fact]
    public void Bind_SectionStructure_ProducesValidScore()
    {
        var source = @"
section A { melody { c4 d e f | } }
structure { A }
render score ""test.svg"" { staff treble { melody } }
";
        var tree = SyntaxTree.Parse(source);
        var symbols = new SymbolCollector().Collect(tree).Symbols;
        var binder = new Binder();

        var result = binder.Bind(tree, symbols);

        Assert.True(result.Success);
    }
}

public class RelativePitchResolverTests
{
    [Fact]
    public void Resolve_StepUp_StaysInSameOctave()
    {
        var resolver = new RelativePitchResolver();
        resolver.Initialize(Pitch.MiddleC);

        var source = "d4";
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();

        var pitch = resolver.Resolve(note.Pitch);

        Assert.Equal(1, pitch.Step); // D
        Assert.Equal(4, pitch.Octave); // Same octave
    }

    [Fact]
    public void Resolve_LargeIntervalUp_GoesDownOctave()
    {
        var resolver = new RelativePitchResolver();
        resolver.Initialize(Pitch.MiddleC);

        // Jump from C to A (more than a fourth up in pitch space)
        var source = "a4";
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();

        var pitch = resolver.Resolve(note.Pitch);

        Assert.Equal(5, pitch.Step); // A
        Assert.Equal(3, pitch.Octave); // Down an octave (closest A)
    }

    [Fact]
    public void Resolve_OctaveMarker_OverridesDefault()
    {
        var resolver = new RelativePitchResolver();
        resolver.Initialize(Pitch.MiddleC);

        var source = "a'4"; // Explicit octave up
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();

        var pitch = resolver.Resolve(note.Pitch);

        Assert.Equal(5, pitch.Step); // A
        Assert.Equal(4, pitch.Octave); // Same octave due to '
    }

    [Fact]
    public void Resolve_Accidentals_Preserved()
    {
        var resolver = new RelativePitchResolver();
        resolver.Initialize(Pitch.MiddleC);

        var source = "fis4"; // F sharp
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();

        var pitch = resolver.Resolve(note.Pitch);

        Assert.Equal(3, pitch.Step); // F
        Assert.Equal(1, pitch.Alteration); // Sharp
    }

    [Fact]
    public void Reset_WithBaseOctave_SetsCorrectOctave()
    {
        var resolver = new RelativePitchResolver();
        
        // Reset to octave 3 (like bass clef)
        resolver.Reset(3);

        var source = "c4";
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();

        var pitch = resolver.Resolve(note.Pitch);

        Assert.Equal(0, pitch.Step); // C
        Assert.Equal(3, pitch.Octave); // Octave 3
    }

    [Fact]
    public void Reset_DefaultsToOctave4()
    {
        var resolver = new RelativePitchResolver();
        resolver.Reset(); // Default reset

        var source = "c4";
        var tree = SyntaxTree.Parse(source);
        var note = tree.GetRoot().DescendantNodes().OfType<NoteSyntax>().First();

        var pitch = resolver.Resolve(note.Pitch);

        Assert.Equal(0, pitch.Step); // C
        Assert.Equal(4, pitch.Octave); // Default octave 4
    }
}

public class PitchTests
{
    [Fact]
    public void MidiNote_MiddleC_Is60()
    {
        var pitch = Pitch.MiddleC;
        Assert.Equal(60, pitch.MidiNote);
    }

    [Fact]
    public void MidiNote_A440_Is69()
    {
        var pitch = new Pitch(5, 4, 0); // A4
        Assert.Equal(69, pitch.MidiNote);
    }

    [Fact]
    public void StaffPosition_MiddleC_IsZero()
    {
        var pitch = Pitch.MiddleC;
        Assert.Equal(0, pitch.StaffPosition);
    }

    [Fact]
    public void StaffPosition_G4_Is4()
    {
        var pitch = new Pitch(4, 4, 0); // G4
        Assert.Equal(4, pitch.StaffPosition);
    }

    [Fact]
    public void FromName_CreatesCorrectPitch()
    {
        var pitch = Pitch.FromName('G', 4, 1); // G#4

        Assert.Equal(4, pitch.Step);
        Assert.Equal(4, pitch.Octave);
        Assert.Equal(1, pitch.Alteration);
        Assert.Equal("#", pitch.AccidentalString);
    }
}
