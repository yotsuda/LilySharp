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

using LilySharp.Core.Editing;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>Owner's request 2026-10-30: a composed guitar part wrote a chord no hand could hold. A
/// tool answers the voicings a player can hold ("Cm7, on the guitar, round here"), and a warning
/// names a chord that cannot be (LYS1055); a bowed string sounds two strings at once.</summary>
public class ChordVoicingFinderTests
{
    [Fact]
    public void TheGuitarsVoicings_AreTheDiagramsShapes_InTheRegister_SpelledAsTheChordIs()
    {
        var answer = ChordVoicingFinder.Find("Cm7", "guitar", "C3", "C5", 6);
        Assert.Null(answer.Problem);
        Assert.Contains(answer.Voicings, v => v.Diagram == "x35343" && v.Chord == "<c g bes ees' g'>");
        Assert.All(answer.Voicings, v => Assert.True(v.Pitches[0] >= 48 && v.Pitches[^1] <= 72));
        // Every one of them can be held — the warning's own test agrees.
        var strings = LilySharp.Core.Tablature.Tunings.Guitar;
        Assert.All(answer.Voicings, v => Assert.NotNull(ChordFingeringValidator.Fingering(v.Pitches, strings)));
        Assert.Contains("x35343", ChordVoicingFinder.Describe("Cm7", "guitar", "C3", "C5"));
    }

    [Fact]
    public void ABowedString_AnswersDoubleStops_OnNeighbouringStrings()
    {
        var answer = ChordVoicingFinder.Find("Cm7", "violin", "G3", "D5", 6);
        Assert.True(answer.Bowed);
        Assert.All(answer.Voicings, v =>
        {
            Assert.Equal(2, v.Pitches.Count);
            int first = v.Frets.ToList().FindIndex(f => f >= 0);
            Assert.True(v.Frets[first + 1] >= 0);   // the next string up
        });
        Assert.Contains("two strings at once", ChordVoicingFinder.Describe("Cm7", "cello", null, null));
    }

    [Fact]
    public void AnUnknownChordOrInstrument_SaysWhy()
    {
        Assert.Contains("not a chord symbol", ChordVoicingFinder.Describe("Xyz", "guitar", null, null));
        Assert.Contains("no string instrument", ChordVoicingFinder.Describe("C", "piano", null, null));
    }

    static IEnumerable<Diagnostic> Fingered(string instrument, string chord) => SemanticValidation.Run(SyntaxTree.Parse(
        $"octave absolute\npart p {{ instrument {instrument} }}\nsection A {{ p {{ {chord}1 | }} }}\nform {{ A }}\n"))
        .Where(d => d.Code == DiagnosticCodes.ChordNotFingerable);

    [Fact]
    public void AChordNoHandCanHold_IsWarned_WithOneThatCan()
    {
        // G2 B2 D3 F3: F3 has no string left within reach.
        Assert.Contains(Fingered("guitar", "<g, b, d f>"), d => d.Message.StartsWith("this chord (G2 B2 D3 F3) cannot be fingered")
            && d.Message.Contains("a voicing of its notes a player can hold"));
        Assert.Empty(Fingered("guitar", "<g b d' f'>"));    // xx5431
        Assert.Empty(Fingered("guitar", "<c g bes ees' g'>")); // x35343
    }

    /// <summary>Owner's request 2026-10-30: a bare <c>@diagram</c> on a <c>&lt;…&gt;</c> chord draws the
    /// chord's own notes as the part's strings fret them — on the page and in the twin, no warning;
    /// a chord no hand can hold draws none (LYS1055 says why).</summary>
    [Fact]
    public void ABareDiagramOnAChord_DrawsItsOwnNotesFretted()
    {
        const string book = "octave absolute\npart gt { instrument guitar }\n"
            + "section A { gt { <g b d' f'>1@diagram | <c e g c'>1@diagram | <g, b, d f>1@diagram | } }\nform { ~A }\nscore { staff gt }\n";
        var tree = SyntaxTree.Parse(book);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        var score = LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree));
        var frames = score.Articulations.Where(a => a.Type == ArticulationType.FretFrame)
            .OrderBy(a => a.SourcePosition).Select(a => a.FrameSpec).ToList();
        Assert.Equal(new[] { "xx5431", "x3201x" }, frames);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("\\fret-diagram-terse \"x;x;5;4;3;1;\"", ly);
        Assert.Contains("\\fret-diagram-terse \"x;3;2;o;1;x;\"", ly);
        // On a single note it draws where that note is held, its lowest fret (owner's decision
        // 2026-10-31; it was unknown until then): C4 is the B string's first fret.
        var single = SyntaxTree.Parse("octave absolute\npart gt { instrument guitar }\nsection A { gt { c'1@diagram | } }\nform { A }\nscore { staff gt }\n");
        Assert.DoesNotContain(SemanticValidation.Run(single), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Equal("xxxx1x", Assert.Single(LilySharp.Core.Svg.SvgGenerator.CollectScore(single,
            LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(single)).Articulations,
            a => a.Type == ArticulationType.FretFrame).FrameSpec);
    }

    [Fact]
    public void ABowedStringsChord_OfMoreThanTwo_OrFarApart_IsWarned()
    {
        Assert.Contains(Fingered("violin", "<g d' b'>"), d => d.Message.Contains("sounds two strings together, not 3"));
        Assert.Empty(Fingered("violin", "<g d'>"));
        Assert.Empty(Fingered("violin", "<b g'>"));          // G string +4, D string +5
        Assert.Contains(Fingered("violin", "<a e''>"), d => d.Message.Contains("do not lie on neighbouring strings"));
    }
}
