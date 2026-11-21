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
/// An edit that ADDS a side-table entry before the boundary — a note, a mark, an accidental —
/// still splices the walk's tail (session 1344).
/// </summary>
/// <remarks>
/// Until then the splice demanded the live side tables hold exactly the recorded counts at
/// the boundary, so such an edit declined every candidate of its walk ("a side-table count
/// differs"): over 628 books a note typed spliced 666 measures, a mark 601, a letter swapped
/// 103,711 (LilySharp-Lab sessions/p1343). The entries carry no index into a table; the tail
/// slices are appended after the live entries, and the next walk reads its watermarks through
/// the drift. An accidental (0 measures) was the splice's prefix agreement besides: `b` → `bes`
/// is a token ending at the window turned one crossing it (CollectResumePlanner, spliceHalf).
/// Each row asserts the resumed collect is the full one AND that the edited walk spliced (an
/// equality alone would hold with the gates back). With the count gate gone, the boundary
/// measure's break permissions are compared instead (the last test).
/// </remarks>
public sealed class AddedEntrySpliceTests
{
    private const string Melody =
        "c''4 d e f | g1 | e4 f g a | b1 | c4 d e f | g2 g | a4 g f e | d1 | "
        + "c4 d e f | g1 | a4 g f e | d1 | c4 e g2 | c1 |";
    private const string Bass =
        "c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | c4 g c g | "
        + "c4 g c g | c4 d e f | c4 g c g | c2 g | c4 g c g | c4 g c g | c1 |";

    private static readonly string Book = $$"""
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

    [Theory]
    [InlineData("g1 | e4 f g a | b1 |", "g1 | e4 f g a | b2 b2 |", "a note typed (a whole note split)")]
    [InlineData("g1 | e4 f g a | b1 |", "g1 | e4 f g a | b1@f |", "a dynamic typed")]
    [InlineData("g1 | e4 f g a | b1 |", "g1 | e4 f g a | bes1 |", "an accidental typed")]
    [InlineData("g1 | e4 f g a | b1 |", "g1 | e4 f g a | b1@staccato |", "a script typed")]
    // A note typed into a full bar: the bar overflows and stays one bar (LYS2002). Until session
    // 1345 the overflow's first-bar length stayed in the builder after the bar line under a key
    // already cleared (MeasureBuilder._spanFirstBar), and every later boundary differed by it.
    [InlineData("g1 | e4 f g a | b1 |", "g1 | e4 f g g a | b1 |", "a note typed into a full bar")]
    public void AnEditThatAddsAnEntry_SplicesTheTail(string find, string replacement, string label)
    {
        int at = Book.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, "edit anchor not found");
        Assert.Equal(at, Book.LastIndexOf(find, StringComparison.Ordinal));
        string newText = Book.Substring(0, at) + replacement + Book.Substring(at + find.Length);

        var oldTree = SyntaxTree.Parse(Book);
        var oldSpec = RenderSpecParser.FindFirst(oldTree);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = oldSpec?.ScoreTranspose };
        SvgGenerator.CollectScore(source, oldTree, oldSpec);

        var newTree = oldTree.WithChange(new TextChange(new TextSpan(at, find.Length), replacement));
        Assert.Equal(newText, newTree.Text);
        var newSpec = RenderSpecParser.FindFirst(newTree);
        var fresh = SyntaxTree.Parse(newText);
        var full = SvgGenerator.CollectScore(fresh, RenderSpecParser.FindFirst(fresh));

        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source);
        Assert.True(resumer != null, $"{label}: the planner offered no resume");
        resumer!.RecordSpliceDeclines = true;
        var resumed = SvgGenerator.CollectScore(
            new MeasureCollector { WalkProbe = resumer, ScoreTranspose = newSpec?.ScoreTranspose }, newTree, newSpec);
        Assert.Null(ModelDeepDiff.FirstDifference(full, resumed, "score"));
        var melody = resumer.ResumePlans.OrderBy(p => p.Key).First().Value;
        Assert.True(melody.SplicedMeasures >= 8, $"{label}: the melody spliced {melody.SplicedMeasures}; declines: "
            + string.Join(" / ", melody.SpliceDeclines?.Select(d => d.Why).Distinct() ?? []));
    }

    /// <summary>
    /// A note typed into a bar that ends <c>| break</c>, so that another note fills the bar: the
    /// bar no longer reads the <c>break</c> ahead when it closes and takes it at the directive.
    /// The candidate right before the directive — whose RECORDED bar already held the break —
    /// must not splice it away (session 1344: the line stopped breaking there on the preview;
    /// the shape of a book of the owner's corpus, written anew).
    /// </summary>
    [Fact]
    public void ANoteThatChangesWhoFillsTheBar_KeepsTheBreakAfterIt()
    {
        const string book = """
            octave absolute
            part melody {
              instrument bass
              octave 4
              section A {
                r,4 r8. a,,16 d,8 d, a,, b,, | r4 b,8. f,16 b,,8 b,,4 a,,8 |
                r4 g,,8. f,,16 a,,8 b,, a,, b,, | r4 r8. f,,16 a,,8 a,,4 d,8 | break
              }
              section B {
                |
              }
            }
            part bass2 {
              pedal mixed
            }
            form { A }
            score {
              staff treble melody
            }
            """;
        string oldText = book.Replace("\r\n", "\n");
        const string find = "a,,8 a,,4 d,8 |";
        int at = oldText.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, "edit anchor not found");
        const string replacement = "a,,8 a,,8 a,,4 d,8 |";
        string newText = oldText.Substring(0, at) + replacement + oldText.Substring(at + find.Length);

        var oldTree = SyntaxTree.Parse(oldText);
        var oldSpec = RenderSpecParser.FindFirst(oldTree);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = oldSpec?.ScoreTranspose };
        SvgGenerator.CollectScore(source, oldTree, oldSpec);
        var newTree = oldTree.WithChange(new TextChange(new TextSpan(at, find.Length), replacement));
        var newSpec = RenderSpecParser.FindFirst(newTree);
        var fresh = SyntaxTree.Parse(newText);
        var full = SvgGenerator.CollectScore(fresh, RenderSpecParser.FindFirst(fresh));
        Assert.True(full.AllVoices.First().Measures[3].HasBreakAfter, "the full collect breaks after bar 4");

        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source);
        Assert.NotNull(resumer);
        resumer!.RecordSpliceDeclines = true;
        var resumed = SvgGenerator.CollectScore(
            new MeasureCollector { WalkProbe = resumer, ScoreTranspose = newSpec?.ScoreTranspose }, newTree, newSpec);
        Assert.Null(ModelDeepDiff.FirstDifference(full, resumed, "score"));
        var plan = resumer.ResumePlans.Values.Single();
        Assert.True(plan.SpliceDeclines != null
            && plan.SpliceDeclines.Any(d => d.Why.Contains("break permissions", StringComparison.Ordinal)),
            "declines: " + string.Join(" / ", plan.SpliceDeclines?.Select(d => d.Why) ?? []));
    }
}
