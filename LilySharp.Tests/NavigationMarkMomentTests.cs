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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// An inline navigation mark is an event at a MOMENT: `|` takes no time, so a mark written
/// after the bar and one written before it stand at the same barline, and which measure
/// carries the mark is the side its kind draws on — a text (fine, D.C., D.S., To Coda) to
/// the bar's left (the measure before), a sign (segno, coda) to its right (the measure
/// after). Owner's decision, session 559 (HANDOFF ⒳¹²).
/// (From 2026-10-04 to 2026-11-15 a mark in music was an error, LYS1034 — form-only. It is
/// the score's mark again: docs/inline-repeat-design.md §3.6. A mark with notes on both sides
/// of it in its bar is LYS1071, and still placed.)
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:1898-1925 jump-script-interface — JumpScript's
/// self-alignment-X is RIGHT (:1912); scm/define-grobs.scm:3083-3111 segno-mark-interface —
/// SegnoMark's is break-alignable-interface::self-alignment-opposite-of-anchor (:3097).
/// <para>
/// Before this the picture read "the measure the mark is written in": the doc's
/// <c>c4 d e f | fine</c> drew Fine one measure late and a <c>| ds al coda</c> after the
/// last bar was dropped without a word (Lab sessions/p480/doc-example.lys). Poison: make
/// <c>MeasureCollector.NavigationMarkMeasure</c> return <c>builder.CurrentMeasureIndex</c>
/// and the two TEXT facts go red (MEASURED: 2 of 5); the sign fact is a CONTROL — a full
/// measure is emitted as it fills, so <c>segno |</c> already stood where <c>| segno</c>
/// does — and the mid-measure and opening-bar facts hold the edges of the rule.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class NavigationMarkMomentTests
{
    private static string Book(string music)
        => "part m { clef treble }\nsection A { m {\n" + music + "\n} }\n"
           + "form { ~A }\nscore { staff m }\n";

    private static MusicMarkItem Mark(string music, MusicMarkType type)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.Empty(tree.Diagnostics);
        var score = new MeasureCollector().Collect(tree);
        return Assert.Single(score.MusicMarks.Where(m => m.Type == type));
    }

    private static string Masked(string music)
        => Regex.Replace(LiveRender.Svg(Book(music)), "\\s*data-pos=\"\\d+\"", "");

    [Fact]
    public void ATextAfterTheBar_StandsAtThatBar_LikeOneBeforeIt()
    {
        var after = Mark("c'4 d' e' f' | fine g'4 a' b' c'' |", MusicMarkType.Fine);
        var before = Mark("c'4 d' e' f' fine | g'4 a' b' c'' |", MusicMarkType.Fine);
        Assert.Equal(0, after.MeasureIndex);
        Assert.Equal(0, before.MeasureIndex);
        // The same page, whichever side of the `|` the word is written on.
        Assert.Equal(Masked("c'4 d' e' f' fine | g'4 a' b' c'' |"),
                     Masked("c'4 d' e' f' | fine g'4 a' b' c'' |"));
    }

    [Fact]
    public void ATextAfterTheLastBar_IsDrawnAtIt()
    {
        var mark = Mark("c'4 d' e' f' | g'4 a' b' c'' | ds al coda", MusicMarkType.DalSegnoAlCoda);
        Assert.Equal(1, mark.MeasureIndex);
        Assert.Contains(">D.S. al Coda</text>", LiveRender.Svg(Book("c'4 d' e' f' | g'4 a' b' c'' | ds al coda")));
    }

    /// <summary>The control (unchanged by the rule): a sign stands at the start of the measure
    /// after the bar from either side of it.</summary>
    [Fact]
    public void ASignBeforeTheBar_StandsAtTheNextMeasuresStart_LikeOneAfterIt()
    {
        var before = Mark("c'4 d' e' f' segno | g'4 a' b' c'' |", MusicMarkType.Segno);
        var after = Mark("c'4 d' e' f' | segno g'4 a' b' c'' |", MusicMarkType.Segno);
        Assert.Equal(1, before.MeasureIndex);
        Assert.Equal(1, after.MeasureIndex);
        Assert.Equal(Masked("c'4 d' e' f' | segno g'4 a' b' c'' |"),
                     Masked("c'4 d' e' f' segno | g'4 a' b' c'' |"));
    }

    /// <summary>The control: mid-measure there is no bar to stand at — the mark keeps its
    /// measure, and the writer is told a mark stands at a bar line (LYS1071; LYS4003 was the
    /// warning until 2026-10-04, LYS1034 the error from then to 2026-11-15).</summary>
    [Fact]
    public void AMidMeasureText_StaysInItsMeasure_AndIsTheSignInsideABarError()
    {
        string music = "c'4 d' fine e' f' | g'4 a' b' c'' |";
        Assert.Equal(0, Mark(music, MusicMarkType.Fine).MeasureIndex);
        var diagnostics = SemanticValidation.Run(SyntaxTree.Parse(Book(music)));
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.SignInsideABar);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCodes.RepeatStructureInPhrase);
        Assert.DoesNotContain(diagnostics, d => d.Code == "LYS4003");
    }

    /// <summary>A text at the piece's opening has no bar before it: bar 0, not −1.</summary>
    [Fact]
    public void ATextAtTheOpening_StaysInTheFirstBar()
        => Assert.Equal(0, Mark("fine c'4 d' e' f' | g'4 a' b' c'' |", MusicMarkType.Fine).MeasureIndex);
}
