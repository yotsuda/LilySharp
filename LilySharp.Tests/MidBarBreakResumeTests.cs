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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A book with a <c>break</c> INSIDE a bar resumes its collect on a keystroke (session 1340).
/// The bar split needs two collect passes (<c>MidBarBreakTable</c>'s remarks); the keystroke
/// path runs them in two collectors with two recordings
/// (<c>IncrementalCompiler.CollectWithResume</c>): the first pass resumes and settles the
/// table from the requests its walks adopted and made, and the split pass resumes while the
/// table cuts the same bars — a table that changes drops the split pass's baseline and it
/// runs in full. Before, such a book marked every recording ineligible and collected twice in
/// full per keystroke. Each step is held to a full compile of the same text, byte for byte,
/// and to the road it should have taken.
/// </summary>
[Trait("Category", "Unit")]
public class MidBarBreakResumeTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    // Fourteen bars. Bar 9 (index 8) breaks after its second beat; bar 11 asks to break after
    // its first, which the bass's half note refuses; bar 13 breaks inside a tuplet, which its
    // builder refuses on the spot — the two conflicts (LYS1037) the split pass reports for the
    // first pass.
    private const string Melody =
        "c''4 d e f | g1 | e4 f g a | b1 | c4 d e f | g2 g | a4 g f e | d1 | "
        + "c4 d break e f | g1 | a4 break g f e | d1 | c4 tuplet 3/2 { e8 break g e } g2 | c1 |";
    private const string Bass =
        "c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | "
        + "c4 g c g | c4 d e f | c4 g c g | c2 g | c4 g c g | c4 g c g | c1 |";
    private const string Rows =
        "chords prog { section Main { C | G | C | G | C | G | F | G | C G | C | F | G | C | C | } }\n"
        + "lyrics w { section Main { a b c d | e | f g h i | j | k l m n | o p | q r s t | u | "
        + "v w x y | z | a b c d | e | f g h i | j | } }";
    // An undrawn part with a repeat: the score harvests its structure in a NESTED collect,
    // which cuts the split bar too — on the split pass's own resume channels.
    private const string Alto =
        "c4 g c g | c4 g c g | c4 g c g | c4 g c g |: c4 g c g | c4 g c g | c4 g c g | "
        + "c4 g c g | c4 d e f | c4 g c g | c2 g | c4 g c g :| c4 g c g | c1 |";

    public enum Shape { Plain, Rows, Harvest }

    private static string Book(string melody, string bass, Shape shape) => $$"""
        paper { raggedRight }
        time 4/4
        key c major
        part melody { clef treble }
        part bass { clef bass }
        {{(shape == Shape.Harvest ? "part alto { clef treble }" : "")}}
        section Main {
          melody { {{melody}} }
          bass { {{bass}} }
          {{(shape == Shape.Harvest ? "alto { " + Alto + " }" : "")}}
        }
        {{(shape == Shape.Rows ? Rows : "")}}
        form { Main }
        score { {{(shape == Shape.Rows ? "lyrics w " : "")}}staff melody staff bass{{(shape == Shape.Rows ? " chords prog" : "")}} }
        """;

    [Theory]
    [InlineData(Shape.Plain)]
    [InlineData(Shape.Rows)]
    [InlineData(Shape.Harvest)]
    public void EveryStep_RendersAsAFullCompile_AndBothPassesResumeWhileTheTableHolds(Shape shape)
    {
        // (melody, bass, label, split pass: null = no split, else whether it resumes)
        var steps = new (string Melody, string Bass, string Label, bool? Split)[]
        {
            (Melody.Replace("a4 break g f e", "a4 break g f d"), Bass,
                "a note after the break", true),
            (Melody.Replace("g2 g |", "g4 a g2 |"), Bass,
                "a longer bar before the break, the tail spliced (the breaks' positions move)", true),
            (Melody.Replace("e4 f g a | b1", "e8 e f4 g a | b1"), Bass,
                "an edit near the top", true),
            (Melody, Bass.Replace("c4 d e f", "c1"),
                "a whole note under the break refuses the split", null),
            (Melody, Bass,
                "the split comes back (its baseline was dropped)", false),
            (Melody.Replace("} g2 | c1", "} a2 | c1"), Bass,
                "the split pass resumes again", true),
            (Melody.Replace("c4 d break e f", "c4 d e break f"), Bass,
                "the break moves to another beat (another table)", false),
            (Melody.Replace("c4 d break e f", "c4 d e break f").Replace("g2 g |", "g2 a |"), Bass,
                "an edit under the new table", true),
        };

        var compiler = new IncrementalCompiler(SyntaxTree.Parse(Book(Melody, Bass, shape)), Opt);
        compiler.Render();
        Assert.Equal((false, (bool?)false), compiler.LastCollectPasses);

        int bothResumed = 0;
        bool everSpliced = false;
        foreach (var (melody, bass, label, split) in steps)
        {
            Assert.True(melody != Melody || bass != Bass || split == false, $"{label}: the step edits nothing");
            // A resume that adopted little schedules a full collect for the next compile (the
            // re-record heuristic) — both passes then record afresh.
            bool rerecord = compiler.RerecordScheduled;
            string text = Book(melody, bass, shape);
            var tree = SyntaxTree.Parse(text);
            string incremental = compiler.RenderIncremental(tree);
            string full = SvgGenerator.Generate(SyntaxTree.Parse(text), Opt);
            Assert.True(full == incremental, $"{label}: incremental != full");
            // The conflicts the panel reads (LYS1037) are the full collect's, positions too.
            var fullCollector = new MeasureCollector();
            SvgGenerator.CollectScore(fullCollector, tree, RenderSpecParser.FindFirst(tree));
            var lent = compiler.CollectFor(tree);
            Assert.NotNull(lent);
            Assert.True(fullCollector.MidBarBreakConflicts.Count >= 2, $"{label}: the conflicts are gone: "
                + string.Join(" | ", fullCollector.MidBarBreakConflicts.Select(c => $"{c.SourcePosition} {c.Reason}")));
            Assert.Equal(
                fullCollector.MidBarBreakConflicts.Select(c => (c.SourcePosition, c.Reason)),
                lent!.MidBarBreakConflicts.Select(c => (c.SourcePosition, c.Reason)));
            var expected = (!rerecord, split is { } s ? s && !rerecord : (bool?)null);
            Assert.True(compiler.LastCollectPasses == expected,
                $"{label}: passes {compiler.LastCollectPasses}, expected {expected}");
            if (compiler.LastCollectPasses == (true, true))
                bothResumed++;
            everSpliced |= compiler.LastCollectResume.SplicedMeasures > 0;
        }
        // The road taken, not just the output: most steps resume both passes, and a tail
        // spliced past the edit carries the breaks' requests at their moved positions.
        Assert.True(bothResumed >= 3, $"both passes resumed on {bothResumed} steps");
        Assert.True(everSpliced, "no step spliced a tail");
    }

    [Fact]
    public void AResumedSplitPass_AdoptsMeasures()
    {
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(Book(Melody, Bass, Shape.Plain)), Opt);
        compiler.Render();
        compiler.RenderIncremental(SyntaxTree.Parse(Book(
            Melody.Replace("} g2 | c1", "} a2 | c1"), Bass, Shape.Plain)));
        Assert.Equal((true, (bool?)true), compiler.LastCollectPasses);
        // Both passes adopt the melody's prefix up to bar 13; the first pass counts its
        // unsplit bars, the split pass one more for the split bar.
        Assert.True(compiler.LastCollectResume.AdoptedMeasures >= 2 * 12,
            $"adopted {compiler.LastCollectResume.AdoptedMeasures} measures in {compiler.LastCollectResume.Walks} walks");
    }
}
