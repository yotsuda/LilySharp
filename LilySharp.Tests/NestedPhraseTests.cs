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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A phrase body may itself reference other phrases (<c>phrase x { y }</c>). The
/// collector used to drop a nested reference — its notes rendered as an EMPTY score
/// (0-byte SVG) while MIDI played them — so nesting now expands in every consumer, and
/// a reference CYCLE (direct, or around a longer ring) is reported instead of silently
/// truncated.
/// </summary>
[Trait("Category", "Unit")]
public class NestedPhraseTests
{
    private static int CollectedNoteCount(string src)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "m");
        return score.Voice.Measures.Sum(m => m.Items.Count(i => i is NoteItem));
    }

    private static int MidiNoteCount(string src) =>
        new MidiExporter().Export(SyntaxTree.Parse(src)).Tracks[1].Notes.Count;

    [Fact]
    public void NestedPhrase_RendersItsNotes_NotAnEmptyScore()
    {
        // phrase x { y }: x's only content is the nested reference y; the collector
        // must expand it (it dropped it before, leaving an empty staff).
        const string src = """
            phrase y { c d e f }
            phrase x { y }
            part m { section A { x } }
            form main { A }
            score main { staff m }
            """;
        Assert.Equal(4, CollectedNoteCount(src));
    }

    [Fact]
    public void NestedPhrase_CollectorAgreesWithMidi()
    {
        // The two consumers used to disagree (MIDI expanded nesting, SVG did not).
        // (Phrase names avoid the pitch letters a-g, which are notes, not names.)
        const string src = """
            phrase inner { c d e f }
            phrase outer { inner inner }
            part m { section A { outer } }
            form main { A }
            score main { staff m }
            """;
        Assert.Equal(8, CollectedNoteCount(src));
        Assert.Equal(8, MidiNoteCount(src));
    }

    private static System.Collections.Generic.IReadOnlyList<Diagnostic> Cycle(string decls)
    {
        var src = $"{decls}\npart m {{ section A {{ x }} }} form main {{ A }} score main {{ staff m }}";
        return SemanticValidation.Run(SyntaxTree.Parse(src));
    }

    [Theory]
    [InlineData("phrase x { x }", "x -> x")]                                  // direct self-reference
    [InlineData("phrase x { y } phrase y { x }", "x -> y -> x")]              // two-way
    [InlineData("phrase x { y } phrase y { z } phrase z { x }", "x -> y -> z -> x")] // three-way ring
    public void ReferenceCycle_IsReportedOnce(string decls, string chain)
    {
        var cycles = Cycle(decls).Where(d => d.Code == DiagnosticCodes.PhraseReferenceCycle).ToList();
        Assert.Single(cycles);
        Assert.Contains(chain, cycles[0].Message);
    }

    [Fact]
    public void ValidNesting_IsNotFlaggedAsACycle()
    {
        var diags = Cycle("phrase y { c d e f } phrase x { y }");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.PhraseReferenceCycle);
    }

    [Fact]
    public void ReferenceCycle_IsDetectedEvenWhenNoFormUsesIt()
    {
        // Form-independent, like the other declaration-graph checks.
        var src = "phrase x { y } phrase y { x } form main { }";
        var cycles = SemanticValidation.Run(SyntaxTree.Parse(src))
            .Where(d => d.Code == DiagnosticCodes.PhraseReferenceCycle).ToList();
        Assert.Single(cycles);
    }

    [Fact]
    public void ReferenceCycle_DoesNotBlowTheStackInAnyConsumer()
    {
        // The runtime guards must break the recursion; a cycle renders its acyclic
        // prefix rather than throwing.
        const string src = """
            phrase x { c4 y }
            phrase y { d4 x }
            part m { section A { x } }
            form main { A }
            score main { staff m }
            """;
        var tree = SyntaxTree.Parse(src);
        var ex1 = Record.Exception(() => new MeasureCollector().Collect(tree, "m"));
        var ex2 = Record.Exception(() => new MidiExporter().Export(tree));
        var ex3 = Record.Exception(() => new LilySharp.Core.MusicXml.MusicXmlExporter().Export(tree));
        Assert.Null(ex1);
        Assert.Null(ex2);
        Assert.Null(ex3);
    }
}
