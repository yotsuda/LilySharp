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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The collector reads the bar line that follows a note ahead, so the auto-fill that closes
/// the bar emits the measure with the bar's source end from the start instead of rewriting it
/// when the bar is walked (<see cref="MeasureBuilder.SetFollowingBarline"/>, session 550).
/// </summary>
/// <remarks>
/// ⚠️ TWO HALVES, BECAUSE THE PAGE CANNOT SEE THE SAVING. A wrong look-ahead is rewritten to
/// the right value by the bar's own arm, so every page is the same whether the look-ahead
/// works, is off by one, or is never set — session 550's off-by-one poison moved 0 of 5,816
/// corpus pages and left 8,913 tests green while the whole saving was gone. So: the EQUALITY
/// half says the values are the bar's (an absolute claim — which offset, not "unchanged"),
/// and the LIVENESS half counts the rewrites through <see cref="MeasureBuilder.t_boundaryRewrites"/>
/// (RULES §5.4, the page-buffer pool's two halves). The positive control shows the meter
/// counts: a <c>break</c> between the note and its bar is not read ahead and rewrites.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MeasureBuilderLookAheadTests
{
    private static (Measure[] Measures, int Rewrites) Collect(string src)
    {
        MeasureBuilder.t_boundaryRewrites = 0;
        var measures = new MeasureCollector().Collect(SyntaxTree.Parse(src), "m").Voice.Measures.ToArray();
        return (measures, MeasureBuilder.t_boundaryRewrites);
    }

    private static string Book(string music)
        => "part m { clef treble section A { " + music + " } }\nform main { A }\nscore main { staff m }";

    [Fact]
    public void ABarAfterANote_IsTheMeasuresEndFromTheEmit_AndNothingIsRewritten()
    {
        string music = "c4 d e f | g a b c || d e f g :|";
        string src = Book(music);
        var (m, rewrites) = Collect(src);
        Assert.Equal(3, m.Length);
        // The equality half: every measure ends AT its written bar's token, and the typed
        // bars are typed.
        int body = src.IndexOf(music, StringComparison.Ordinal);
        Assert.Equal(body + music.IndexOf('|'), m[0].SourceEnd);
        Assert.Equal(body + music.IndexOf("||", StringComparison.Ordinal), m[1].SourceEnd);
        Assert.Equal(body + music.IndexOf(":|", StringComparison.Ordinal), m[2].SourceEnd);
        Assert.Equal(BarlineType.Single, m[0].EndBarline);
        Assert.Equal(BarlineType.Double, m[1].EndBarline);
        Assert.Equal(BarlineType.RepeatEnd, m[2].EndBarline);
        // The liveness half: the look-ahead put those values there, so the bars rewrote nothing.
        Assert.Equal(0, rewrites);
    }

    [Fact]
    public void ABreakAfterTheBar_IsThePermissionFromTheEmit_AndNothingIsRewritten()
    {
        // `| break` is the corpus's spelling (6,106 sites to 3 of `break |`): the directive
        // standing right after the bar is read ahead too (session 551).
        string music = "c4 d e f | break g a b c | noBreak d e f g | pageBreak a b c d | noPageBreak e f g a |";
        var (m, rewrites) = Collect(Book(music));
        Assert.Equal(5, m.Length);
        Assert.Equal(BreakPermission.Force, m[0].LineBreakPermission);
        Assert.Equal(BreakPermission.Forbid, m[1].LineBreakPermission);
        Assert.Equal((BreakPermission.Force, BreakPermission.Force), (m[2].LineBreakPermission, m[2].PageBreakPermission));
        Assert.Equal(BreakPermission.Forbid, m[3].PageBreakPermission);
        Assert.Equal(BreakPermission.Allow, m[4].LineBreakPermission);
        Assert.Equal(0, rewrites);
    }

    [Fact]
    public void DeletingTheBreakAfterTheBar_IncrementalMatchesFull()
    {
        // The look-ahead makes the measure emitted at `f` depend on the `break` standing
        // AFTER its bar, so the walk folds the break's span into the checkpoint read watermark
        // (ProcessNodes). ⚠️ THIS NET DOES NOT REACH THAT FOLD, and says so: deleting the
        // break changes the section's shape, which turns the planner's PREFIX side off before
        // any watermark is read (CollectResumePlanner.StructureStable — session 551 measured
        // the plan: prefix checkpoint none, suffix splice 3 of 5 measures), and an edit that
        // only swaps the keyword is repaired by the directive's own setter. What it does hold:
        // the keystroke that deletes a `| break` is drawn as the full render draws it, through
        // the suffix splice at a boundary the look-ahead no longer rewrites.
        var options = new SvgRenderOptions { EmbedFont = false };
        string src = "part m { clef treble section A { c4 d e f | break g a b c | d e f g | a b c d | e f g a | } }\n"
            + "form main { A }\nscore main { staff m }";
        var session = new IncrementalCompiler(SyntaxTree.Parse(src), options);
        session.RenderIncrementalPages(SyntaxTree.Parse(src), default);
        string edited = src.Replace("| break g", "| g");
        Assert.NotEqual(src, edited);
        var set = session.RenderIncrementalPages(SyntaxTree.Parse(edited), default);
        Assert.Equal(
            SvgGenerator.Generate(SyntaxTree.Parse(edited), options).Replace("\r\n", "\n"),
            set.ToSvg().Replace("\r\n", "\n"));
    }

    [Fact]
    public void ARunOfTwoDirectives_ReadsTheFirstAhead_AndTheSecondWrites()
    {
        // The setters run in order and each writes its own value, so only the FIRST directive
        // is read ahead: `| break noBreak` ends Forbid (the second setter's write, one rewrite),
        // never Force (which an emit holding the run's final value would have been rewritten to).
        string music = "c4 d e f | break noBreak g a b c |";
        var (m, rewrites) = Collect(Book(music));
        Assert.Equal(2, m.Length);
        Assert.Equal(BreakPermission.Forbid, m[0].LineBreakPermission);
        Assert.Equal(1, rewrites);
    }

    [Fact]
    public void ABreakTheEmitDidNotReadAhead_StillWrites()
    {
        // The positive control of the break arm: a `break` after a bar that follows a
        // NON-leaf site (a phrase reference) is not read ahead, so the boundary-time setter
        // writes it — the same Force, one rewrite.
        string music = "x | break g a b c |";
        var (m, rewrites) = Collect("phrase x { c4 d e f }\n" + Book(music));
        Assert.Equal(2, m.Length);
        Assert.Equal(BreakPermission.Force, m[0].LineBreakPermission);
        Assert.True(rewrites >= 1, $"expected the un-hinted break to rewrite, saw {rewrites}");
    }

    [Fact]
    public void ABreakBetweenTheNoteAndItsBar_IsNotReadAhead_AndRewrites()
    {
        // The positive control of the meter: `break` is the site after `f`, not the bar, so
        // the auto-fill closes at f+1, the `break` writes Force at the boundary and the bar
        // retargets the measure's end — two rewrites, both counted.
        string music = "c4 d e f break | g a b c |";
        string src = Book(music);
        var (m, rewrites) = Collect(src);
        Assert.Equal(2, m.Length);
        int body = src.IndexOf(music, StringComparison.Ordinal);
        Assert.Equal(body + music.IndexOf('|'), m[0].SourceEnd);   // the bar still wins
        Assert.Equal(BreakPermission.Force, m[0].LineBreakPermission);
        Assert.Equal(2, rewrites);
    }

    [Fact]
    public void AMultiMeasureRest_HandsTheBarToItsLastCopyOnly()
    {
        // `R1*2 |` closes two bars from one site: the interior bar keeps the rest's own
        // placeholder (rest + 1, as before the look-ahead), the last one ends at the bar —
        // the grand-staff MMR snapshot moved when every copy took the bar's position.
        string music = "R1*2 | c4 d e f |";
        string src = Book(music);
        var (m, rewrites) = Collect(src);
        Assert.Equal(3, m.Length);
        int body = src.IndexOf(music, StringComparison.Ordinal);
        Assert.Equal(body + 1, m[0].SourceEnd);                       // the placeholder: R + 1
        Assert.Equal(body + music.IndexOf('|'), m[1].SourceEnd);      // the bar
        Assert.Equal(body + music.LastIndexOf('|'), m[2].SourceEnd);
        Assert.Equal(0, rewrites);
    }
}
