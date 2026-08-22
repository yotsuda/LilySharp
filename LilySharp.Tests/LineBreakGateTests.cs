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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// F3 engine slice 1 (S4a): the line-break GATE.
///
/// The global Knuth-Plass line-break solution is a pure function of the
/// per-measure natural-width vector (<see cref="KnuthPlassBreaker.ComputeMeasureSpringData"/>
/// -> <c>FindOptimalBreaks</c>) plus the paper width. These tests prove the
/// design's central early-cutoff (the F3 incremental design notes §4): an edit that
/// does not change any measure's natural width CANNOT change where the lines
/// break, so the incremental driver may reuse the previous break solution; and
/// when a width DOES change, the change is LOCALIZED to the edited measure (the
/// rest of the vector is untouched), which is what lets later stages recompute
/// only the affected region.
/// </summary>
[Trait("Category", "Unit")]
public class LineBreakGateTests
{
    // Collect the renderer's way (resolve the render spec, pass the voice name)
    // so the score is faithful, then compute the gate vector exactly as the
    // breaker does: ComputeMeasureSpringData over the common shortest duration.
    private static MeasureSpringData[] Gate(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        string? voiceName = spec is { Items.Length: 1 } && spec.Items[0] is SingleStaffSpec single
            ? single.Staff.VoiceName
            : null;
        var score = new MeasureCollector { ScoreTranspose = spec?.ScoreTranspose }
            .Collect(tree, voiceName, spec?.Form);
        double shortest = SpacingRules.CalculateCommonShortestDuration(score);
        return KnuthPlassBreaker.ComputeMeasureSpringData(score.Voice.Measures, shortest);
    }

    private static string Score(string phraseBody) => $$"""
        time 4/4
        key c major
        part melody { clef treble }
        phrase mel { {{phraseBody}} }
        section Main { melody { mel } }
        form main { Main }
        score main "x" { staff melody }
        """;

    [Fact]
    public void WidthPreservingEdit_LeavesTheGateIdentical_SoLineBreakIsSkippable()
    {
        // Articulations collect into a separate list, not the measure's items,
        // so adding them changes nothing the springs depend on: the entire gate
        // vector is unchanged -> FindOptimalBreaks would return the same breaks.
        var bare = Gate(Score("c4 d e f | g4 a b c |"));
        var withArticulations = Gate(Score("c4@staccato d@staccato e f | g4@accent a b c |"));

        Assert.Equal(bare.Length, withArticulations.Length);
        Assert.Equal(bare, withArticulations); // value equality over the whole vector
    }

    [Fact]
    public void WidthChangingEdit_ChangesOnlyTheEditedMeasure_TheRestOfTheGateIsUntouched()
    {
        // Re-rhythm measure 0 (c4 d e f -> c2 d4 e4, still a full 4/4 bar) while
        // measure 1 is identical. The common shortest duration stays a quarter,
        // so only measure 0's natural width moves; measure 1's is bit-identical.
        var bare = Gate(Score("c4 d e f | g4 a b c |"));
        var reRhythmed = Gate(Score("c2 d4 e4 | g4 a b c |"));

        Assert.Equal(2, bare.Length);
        Assert.Equal(2, reRhythmed.Length);
        Assert.NotEqual(bare[0], reRhythmed[0]); // the edited measure's width moved
        Assert.Equal(bare[1], reRhythmed[1]);    // ...and ONLY that measure's
    }

    [Fact]
    public void GateElement_HasValueEquality()
    {
        // The cutoff comparison relies on MeasureSpringData being compared by
        // value (it is a readonly record struct).
        var gate = Gate(Score("c4 d e f | g4 a b c |"));
        var same = Gate(Score("c4 d e f | g4 a b c |"));
        Assert.Equal(gate, same);
        Assert.True(gate[0] == same[0]);
    }
}
