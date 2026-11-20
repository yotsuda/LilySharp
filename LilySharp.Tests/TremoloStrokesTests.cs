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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A stem tremolo's strokes (owner's decisions 2026-11-20, Calamo request 33: Kücken Op.47-3
/// bars 73, 78-79): every <c>:8</c> / <c>:16</c> / <c>:32</c> is played stroke by stroke, and a
/// <c>@portato</c> on it is the old prints' arc over one dot a stroke — each stroke portato.
/// </summary>
[Trait("Category", "Unit")]
public class TremoloStrokesTests
{
    private static string Book(string music, string time = "6/8") => $$"""
        octave absolute
        time {{time}}
        part p { clef treble }
        section A { p { {{music}} } }
        form { ~A }
        score { staff p }
        """;

    private static (int Key, int Start, int Length)[] Sounds(string text)
    {
        var midi = new LilySharp.Core.Midi.MidiExporter().Export(SyntaxTree.Parse(text));
        return [.. midi.Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => (n.Pitch, n.StartTick, n.DurationTicks))];
    }

    [Theory]
    [InlineData(":8", 3, 4, 6)]
    [InlineData(":16", 1, 2, 8)]
    [InlineData(":32", 1, 1, 32)]
    [InlineData(":8", 1, 8, 0)]     // one stroke: the note itself
    [InlineData(":16", 3, 8, 6)]    // c4.:16
    public void TheStrokes_AreTheWrittenValueOverTheTremolos(string tremolo, int num, int den, int expected)
    {
        var tree = SyntaxTree.Parse(Book($"c'2.{tremolo} |"));
        var token = tree.GetRoot().DescendantNodes<NoteSyntax>().Single().Tremolo;
        Assert.Equal(expected, LilySharp.Core.Music.TremoloStrokes.Of(token, new Fraction(num, den)));
    }

    [Fact]
    public void AChordWithAStroke_IsPlayedStrokeByStroke_AndAPortatoShortensEach()
    {
        var plain = Sounds(Book("<fis' b'>2.:8 |"));
        Assert.Equal(12, plain.Length);
        int eighth = plain.Where(n => n.Key == 66).Skip(1).First().Start;
        Assert.Equal(Enumerable.Range(0, 6).Select(i => (66, i * eighth, eighth)), plain.Where(n => n.Key == 66));

        // Six eighths played portato — the same as the music written out, a slur and a dot on each.
        var portato = Sounds(Book("<fis' b'>2.:8@portato |"));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => (66, i * eighth, eighth * 3 / 4)), portato.Where(n => n.Key == 66));
    }

    [Theory]
    [InlineData("c''2:16 r4 |", 8)]
    [InlineData("c''2 c''8:8 r8 |", 2)]   // a stroke as long as the note: one sound each
    [InlineData("<c'' e''>4 q2:8 |", 1 + 4)]
    [InlineData("c''4 2:8 |", 1 + 4)]
    public void EveryStemTremolo_IsPlayedStrokeByStroke(string music, int onsetsOfC)
    {
        Assert.Equal(onsetsOfC, Sounds(Book(music, "3/4")).Count(n => n.Key == 72));
    }

    [Fact]
    public void ATie_GoesOnFromTheLastStroke()
    {
        var notes = Sounds(Book("c''2:8~ c''4 |", "3/4")).Where(n => n.Key == 72).ToArray();
        Assert.Equal(4, notes.Length);
        int eighth = notes[1].Start;
        Assert.Equal((72, 3 * eighth, eighth + 2 * eighth), notes[3]);
    }

    [Fact]
    public void AStruckMember_SoundsOnce()
    {
        var notes = Sounds(Book("<c'' e''@struck>2.:8 |"));
        Assert.Equal(6, notes.Count(n => n.Key == 72));
        Assert.Single(notes, n => n.Key == 76);
    }

    [Theory]
    [InlineData("<fis b dis' fis'>2.:8@portato |", 6, true)]      // stem up: over the stem's tip
    [InlineData("<fis'' b'' dis''' fis'''>2.:8@portato |", 6, true)] // stem down: over the heads
    [InlineData("c''2:16@portato r4 |", 8, true)]
    [InlineData("g'2.:8@portato.down |", 6, false)]
    public void APortatoOverStrokes_IsOneDotAStroke_OverTheNote(string music, int strokes, bool above)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity != DiagnosticSeverity.Info));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        var portato = Assert.Single(score.Articulations);
        Assert.Equal((strokes, above), (portato.TremoloStrokes, portato.IsAbove));
        Assert.Equal("portatoStrokes:" + strokes, portato.GetGlyph());

        // The twin draws the same as a markup — the dots, one a stroke, and the arc.
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains((above ? "^" : "_") + "\\markup \\center-align \\overlay {", ly);
        Assert.Equal(strokes, System.Text.RegularExpressions.Regex.Matches(ly, "dots\\.dot").Count);
        Assert.DoesNotContain("\\portato", ly);
    }

    [Theory]
    [InlineData("c''2.@portato |")]     // no tremolo
    [InlineData("c''8:8@portato r8 r2 |")] // one stroke
    public void APortatoWithoutStrokes_IsTheScript(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        Assert.Equal(0, Assert.Single(score.Articulations).TremoloStrokes);
        Assert.Contains("\\portato", new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree));
    }

    [Fact]
    public void TheTwin_WritesAChordsStroke()
    {
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(Book("<fis' b'>2.:8 |")));
        Assert.Contains("<fis' b'>2.:8", ly);
    }

    [Fact]
    public void ThePage_DrawsTheDotsAndTheArc()
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book("<fis b dis' fis'>2.:8@portato |")));
        string plain = SvgGenerator.Generate(SyntaxTree.Parse(Book("<fis b dis' fis'>2.:8 |")));
        // Six more augmentation dots than the chord's own, and the arc a path more.
        static int Count(string s, string what) => System.Text.RegularExpressions.Regex.Matches(s, what).Count;
        char dot = LilySharp.Core.Svg.Layout.MusicFont.Current.Codepoint(MusicGlyph.AugmentationDot);
        Assert.Equal(6, Count(svg, System.Text.RegularExpressions.Regex.Escape(dot.ToString())) - Count(plain, System.Text.RegularExpressions.Regex.Escape(dot.ToString())));
        Assert.True(Count(svg, "<path") > Count(plain, "<path"));
    }
}
