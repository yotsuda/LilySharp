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
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An ENDING mark on a block's closing brace — <c>repeat tremolo 12 { … }@!sustain</c>, the pedal
/// lifted when the tremolo is over (Calamo request 29; owner's decision 2026-11-19: ending marks
/// alone, on any block of music). It is the empty chord a writer could have put after the block,
/// <c>} &lt;&gt;@!sustain</c>, to every reader: the page, the sound, the twin, MusicXML.
/// </summary>
[Trait("Category", "Unit")]
public class EndingMarkOnABlockTests
{
    private static string Book(string music) => $$"""
        octave absolute
        time 4/4
        part lh { clef bass }
        section A { lh { {{music}} } }
        score { staff lh }
        """;

    private static string Picture(string source) => Regex.Replace(
        SvgGenerator.Generate(SyntaxTree.Parse(source)), "data-pos=\"[^\"]*\"", "");

    private static string Pedals(string source) => string.Join(" ",
        new MidiExporter().Export(SyntaxTree.Parse(source)).Tracks.SelectMany(t => t.Pedals)
            .Select(c => (c.Down ? "v" : "^") + c.Tick));

    [Theory]
    // A tremolo's pedal, a tuplet's hairpin, two marks at once, the last voice of a group.
    [InlineData("c4@sustain d repeat tremolo 8 { e32 g }{0}@!sustain | c1 |")]
    [InlineData("c4@cresc tuplet 3/2 { d8 e f }{0}@!cresc g2@f | c1 |")]
    [InlineData("c4@8va@sustain d repeat tremolo 8 { e32 g }{0}@!8va@!sustain | c1 |")]
    [InlineData("c4@sustain d e voice { f4 } { a,4 }{0}@!sustain | c1 |")]
    public void OnTheBrace_IsTheEmptyChordAfterTheBlock(string music)
    {
        string glued = Book(music.Replace("{0}", "")), chord = Book(music.Replace("{0}", " <>"));
        var tree = SyntaxTree.Parse(glued);
        Assert.Empty(tree.Diagnostics);
        // The two zero-width brackets add no text: the tree is still the file.
        Assert.Equal(glued, tree.GetRoot().ToFullString());
        Assert.Equal(Picture(chord), Picture(glued));
        Assert.Equal(new LilyPondExporter().Export(SyntaxTree.Parse(chord)), new LilyPondExporter().Export(tree));
        Assert.Equal(new MusicXmlExporter().Export(SyntaxTree.Parse(chord)).ToXml().ToString(), new MusicXmlExporter().Export(tree).ToXml().ToString());
        Assert.Equal(Pedals(chord), Pedals(glued));
        // The editor's colours walk the same tree.
        Assert.NotEmpty(LilySharpLanguageServer.CollectSemanticTokens(tree.GetRoot(), glued).ToList());
    }

    [Fact]
    public void ThePedal_LiftsWhenTheTremoloIsOver()
        => Assert.Equal("v0 ^1920", Pedals(Book("c4@sustain d repeat tremolo 8 { e32 g }@!sustain | c1 |")));

    [Theory]
    // Not an ending mark: after the block, or the whole of it? Refused, the empty chord named.
    [InlineData("c4 d repeat tremolo 8 { e32 g }@f | c1 |", "} <>@f.")]
    [InlineData("c4 d repeat tremolo 8 { e32 g }@sustain | c1@!sustain |", "} <>@sustain.")]
    public void AnotherMarkOnTheBrace_IsToldTheEmptyChord(string music, string repair)
    {
        var error = SyntaxTree.Parse(Book(music)).Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("only an ending mark (}@!sustain)", error.Message);
        Assert.EndsWith(repair, error.Message);
    }

    [Fact]
    public void AnotherMarkAfterAnEndingOne_IsReportedWhereItStands()
    {
        string source = Book("c4@sustain d repeat tremolo 8 { e32 g }@!sustain@f | c1 |");
        var error = Assert.Single(SyntaxTree.Parse(source).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Only an ending mark", error.Message);
        Assert.Equal("@f", source.Substring(error.Span.Start, error.Span.Length));
    }

    [Fact]
    public void ASpaceBeforeTheMark_IsNotTheBlocksMark()
    {
        // `} @!sustain` is a mark on nothing, as it has always been.
        Assert.Contains(SyntaxTree.Parse(Book("c4@sustain d repeat tremolo 8 { e32 g } @!sustain | c1 |")).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
