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

using System;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Trailing trivia is not structure, at either end of the edit window (session 1343): the
/// parse agreements a resumed collect stands on compared a node whose last token's trailing
/// space ran across the window, and declined.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED before the fix, on a two-part book: a space typed between two notes declined every
/// splice of every walk (the prefix half: the note before it now ends past the window), a space
/// typed before a bar line cost a full collect (the same half refused the plan), and
/// <c>g2 g |</c> → <c>g4 a g2 |</c> declined every splice of the bass too, whose text all lies
/// after the window (the suffix half: the space before <c>|</c> belonged to the pitch on the old
/// side and to the duration on the new). Correctness never suffered — a declined splice walks
/// live — only every keystroke of that kind collected the whole tail.
/// </para>
/// <para>
/// The refusal had also been standing in for a missing state guard: a <c>~</c> typed before a
/// bar line changes the next bar's tied-head accidental, which no compared field held, so the
/// splice from that bar line served the old one (<c>TiedAccidentalTests</c> reddened). The
/// splice now compares the tie the last event carries into the boundary
/// (<c>MeasureBuilder.TieCarry</c>); the last net here asserts that guard is the one declining.
/// </para>
/// </remarks>
public sealed class TrailingTriviaSpliceTests
{
    private const string Melody =
        "c''4 d e f | g1 | e4 f g a | b1 | c4 d e f | g2 g | a4 g f e | d1 | "
        + "c4 d e f | g1 | a4 g f e | d1 | c4 e g2 | c1 |";
    private const string Bass =
        "c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | "
        + "c4 g c g | c4 d e f | c4 g c g | c2 g | c4 g c g | c4 g c g | c1 |";

    private static readonly string TwoParts = $$"""
        time 4/4
        key c major
        part melody { clef treble }
        part bass { clef bass }
        section Main {
          melody { {{Melody}} }
          bass { {{Bass}} }
        }
        form { Main }
        score { staff melody staff bass }
        """.Replace("\r\n", "\n");

    private static readonly string Tied = $$"""
        octave absolute
        clef bass
        key a major
        time 4/4
        part melody
        section A { melody { c2 c2 | c2 c2 | a,2 c2 | {{string.Concat(Enumerable.Repeat("c2 c2 | ", 13))}} } }
        form { ~A }
        score { staff melody }
        """.Replace("\r\n", "\n");

    // (the walk's ordinal → spliced measures) of one edit, after asserting the resumed collect
    // is the full one; null when the planner offered no resume at all.
    private static (CollectWalkProbe Resumer, int[] Spliced)? Resume(string oldText, string find, string replacement)
    {
        int at = oldText.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, "edit anchor not found");
        Assert.Equal(at, oldText.LastIndexOf(find, StringComparison.Ordinal));
        string newText = oldText.Substring(0, at) + replacement + oldText.Substring(at + find.Length);

        var oldTree = SyntaxTree.Parse(oldText);
        var oldSpec = RenderSpecParser.FindFirst(oldTree);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = oldSpec?.ScoreTranspose };
        SvgGenerator.CollectScore(source, oldTree, oldSpec);

        // The LSP's tree: the old one incrementally reparsed.
        var newTree = oldTree.WithChange(new TextChange(new TextSpan(at, find.Length), replacement));
        Assert.Equal(newText, newTree.Text);
        var newSpec = RenderSpecParser.FindFirst(newTree);
        var fresh = SyntaxTree.Parse(newText);
        var full = SvgGenerator.CollectScore(fresh, RenderSpecParser.FindFirst(fresh));

        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source);
        if (resumer == null)
            return null;
        resumer.RecordSpliceDeclines = true;
        var resumed = SvgGenerator.CollectScore(
            new MeasureCollector { WalkProbe = resumer, ScoreTranspose = newSpec?.ScoreTranspose }, newTree, newSpec);
        Assert.Null(ModelDeepDiff.FirstDifference(full, resumed, "score"));
        return (resumer, resumer.ResumePlans.OrderBy(p => p.Key).Select(p => p.Value.SplicedMeasures).ToArray());
    }

    [Theory]
    [InlineData("e4 f g a", "e4 f  g a", "a space between two notes")]
    [InlineData("g2 g |", "g2  g |", "a space before a bar line")]
    public void ASpaceTyped_SplicesEveryWalksTail(string find, string replacement, string label)
    {
        var r = Resume(TwoParts, find, replacement);
        Assert.True(r != null, $"{label}: the planner offered no resume (a full collect)");
        // The melody re-walks the bar holding the space and splices the rest; the bass lies
        // wholly after the window and splices all thirteen bars after its first.
        Assert.True(r!.Value.Spliced.Length == 2 && r.Value.Spliced.All(n => n >= 8),
            $"{label}: spliced {string.Join(", ", r.Value.Spliced)}");
    }

    [Theory]
    [InlineData("g2 g |", "g4 a g2 |", "a longer bar of the same length")]
    [InlineData("g2 g |", "g1 |", "a shorter one")]
    public void ABarRewrittenToAnotherTextLength_SplicesTheLaterPartsTail(string find, string replacement, string label)
    {
        var r = Resume(TwoParts, find, replacement);
        Assert.True(r != null, $"{label}: the planner offered no resume");
        Assert.True(r!.Value.Spliced.Length == 2 && r.Value.Spliced[1] == 13,
            $"{label}: spliced {string.Join(", ", r.Value.Spliced)}");
    }

    [Theory]
    [InlineData("a,2 c2 |", "a,2 c2~ |")]
    [InlineData("a,2 c2~ |", "a,2 c2 |")]
    public void ATieTypedOrDeletedBeforeABarLine_IsDeclinedByTheTieGuard_ThenTheTailSplices(string find, string replacement)
    {
        var r = Resume(Tied.Replace("a,2 c2 |", find), find, replacement);
        Assert.True(r != null, "the planner offered no resume");
        var plan = r!.Value.Resumer.ResumePlans.Values.Single();
        Assert.True(plan.SpliceDeclines != null
            && plan.SpliceDeclines.Any(d => d.Why.Contains("tie carried", StringComparison.Ordinal)),
            "declines: " + string.Join(" / ", plan.SpliceDeclines?.Select(d => d.Why) ?? []));
        Assert.True(plan.SplicedMeasures > 0, "nothing spliced past the tied bar");
    }
}
