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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie over a repeat sign or an ending written in a section's MUSIC is carried to the note
/// PLAYED next, on the page and in the check, as one over a form's repeat is (owner's decision
/// 2026-09-28; docs/inline-repeat-design.md §7 stage 4, "the rest, 1"): the arc only where the
/// note printed next is played next, a repeat tie on every other note it reaches, a hanging
/// tie where no arc leaves it, and LYS4007 on a note it reaches at another pitch — whichever
/// voice writes the signs (<see cref="RepeatStream"/>, stamped by the collector).
/// </summary>
[Trait("Category", "Unit")]
public sealed class InlineRepeatTieTests
{
    private static string Book(string mel, string bas)
        => $"time 4/4\npart mel {{ section A {{ {mel} }} }}\npart bas {{ clef bass section A {{ {bas} }} }}\nform {{ A }}\nscore {{ staff mel staff bas }}\n";

    private static Score Collect(string src) => new MeasureCollector().Collect(SyntaxTree.Parse(src), "mel");

    private static List<NoteItem> Notes(Score score) =>
        score.Voices[0].Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    private static List<(int, int)> Arcs(Score score) =>
        new TieDetector().DetectTies(score).Select(t => (t.StartMeasureIndex, t.EndMeasureIndex)).ToList();

    private static int Mismatches(string src) =>
        SemanticValidation.Run(SyntaxTree.Parse(src)).Count(d => d.Code == DiagnosticCodes.TieTargetMismatch);

    /// <summary>Out of the body into the endings: the arc into the first, printed and played
    /// next, and a repeat tie on the head of the second — with the signs in the melody, and
    /// with the signs in the bass alone.</summary>
    [Theory]
    [InlineData("|: c'1 | d1~ | [1. d1 :| [2. d1 ] | c1 |", "c1 | g | g | g | c |")]
    [InlineData("c'1 | d1~ | d1 | d1 | c1 |", "|: c1 | g | [1. g :| [2. g ] | c |")]
    public void ATieOutOfTheBodyReachesBothEndings(string mel, string bas)
    {
        string src = Book(mel, bas);
        var score = Collect(src);
        var notes = Notes(score);
        Assert.True(notes[3].HasRepeatTie, "the second ending's head is reached on the second pass");
        Assert.False(notes[2].HasRepeatTie, "the first ending's head takes the arc");
        Assert.False(notes[1].HasLaissezVibrer);
        Assert.Equal([(1, 2)], Arcs(score));
        Assert.Equal(0, Mismatches(src));
    }

    /// <summary>Out of the first ending back to the head: no arc (the note printed next is the
    /// second ending's, which never follows it), a hanging tie on the note and a repeat tie on
    /// the head — and no LYS4007 on the second ending's note.</summary>
    [Theory]
    [InlineData("|: c'1 | d1 | [1. c1~ :| [2. e1 ] | c1 |", "c1 | g | g | g | c |")]
    [InlineData("c'1 | d1 | c1~ | e1 | c1 |", "|: c1 | g | [1. g :| [2. g ] | c |")]
    public void ATieOutOfTheFirstEndingGoesBackToTheHead(string mel, string bas)
    {
        string src = Book(mel, bas);
        var score = Collect(src);
        var notes = Notes(score);
        Assert.True(notes[0].HasRepeatTie, "the head is what the first ending's tie reaches");
        Assert.True(notes[2].HasLaissezVibrer, "no arc leaves the tied note");
        Assert.False(notes[3].HasRepeatTie);
        Assert.Empty(Arcs(score));
        Assert.Equal(0, Mismatches(src));
    }

    /// <summary>A tie at a <c>:|</c> reaches the head AND the note after the sign: the head on
    /// another pitch is LYS4007, as the same music cut into sections and repeated in the form is.</summary>
    [Fact]
    public void ATieThatGoesBackToAnotherPitchIsLys4007()
    {
        Assert.Equal(1, Mismatches(Book("|: c'1 | d1~ :| d1 | c1 |", "c1 | g | g | c |")));
        Assert.Equal(1, Mismatches(Book("c'1 | d1~ | d1 | c1 |", "|: c1 | g :| g | c |")));
        Assert.Equal(0, Mismatches(Book("c'1 | d1~ | d1 | c1 |", "c1 | g | g | c |")));
    }

    /// <summary>A sign typed in ANOTHER part changes this part's ties, and the preview, which
    /// keeps what an edit did not touch, draws what the full render draws — typed, and typed back.</summary>
    [Theory]
    [InlineData("c'1 | d1~ | d1 | d1 | c1 |", "c1 | g | g | g | c |", "|: c1 | g | [1. g :| [2. g ] | c |")]
    [InlineData("c'1 | d1 | c1~ | e1 | c1 |", "c1 | g | g | g | c |", "|: c1 | g | [1. g :| [2. g ] | c |")]
    [InlineData("c'1 | d1~ | d1 | c1 | c1 |", "c1 | g | g | g | c |", "|: c1 | g :| g | g | c |")]
    public void ThePreviewDrawsTheTiesTheFullRenderDraws(string mel, string plain, string signed)
    {
        var opt = new SvgRenderOptions { EmbedFont = false };
        string Full(string src) => SvgGenerator.Generate(SyntaxTree.Parse(src), opt).Replace("\r\n", "\n");
        string before = Book(mel, plain), after = Book(mel, signed);
        Assert.NotEqual(Full(before), Full(after));

        var typing = new IncrementalCompiler(SyntaxTree.Parse(before), opt);
        typing.Render();
        Assert.Equal(Full(after), typing.RenderIncremental(SyntaxTree.Parse(after)).Replace("\r\n", "\n"));
        Assert.Equal(Full(before), typing.RenderIncremental(SyntaxTree.Parse(before)).Replace("\r\n", "\n"));
        Assert.Equal(Full(after), typing.RenderIncremental(SyntaxTree.Parse(after)).Replace("\r\n", "\n"));
    }
}
