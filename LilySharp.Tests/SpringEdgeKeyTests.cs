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

using System.Collections.Immutable;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The spring memo compares a measure's NEIGHBOURS by what their springs read of it
/// (<c>SystemBreaker.SpringEdgeKey</c>), not by their whole content keys. Each fact of the
/// inventory in the key's remarks is changed ALONE here, and the side of the key that
/// carries it must move — the liveness half: a fact dropped from the fold would leave the
/// neighbour serving a stale entry, and the incremental==full nets only see that on the
/// books that happen to change it. The note-only edit is the other half: the key must NOT
/// move on the edit the narrowing is for.
/// </summary>
public class SpringEdgeKeyTests
{
    private static MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static string Book(string music, string extra = "", string staff = "staff melody") => $$"""
        octave absolute
        time 4/4
        key c major
        part melody { clef treble }
        phrase mel { {{music}} }
        {{extra}}
        section Main { melody { mel } }
        form main { Main }
        score main "x" { {{staff}} }
        """;

    private const string Plain = "c4 d e f | g4 a b c | d4 e f g | a4 b c d |";

    private static (ImmutableArray<SystemBreaker.SpringEdgeKey> Edges, MultiStaffScore Score) Edges(string src)
    {
        var score = Collect(src);
        return (SystemBreaker.ComputeSpringEdgeKeys(score), score);
    }

    [Fact]
    public void ANoteChangedInsideTheBar_MovesNeitherSide()
    {
        var (a, _) = Edges(Book(Plain));
        var (b, _) = Edges(Book(Plain.Replace("g4 a b c", "g4 a bes c")));
        Assert.Equal(a, b);
    }

    [Fact]
    public void TheEndBarline_MovesWhatTheNextBarReads()
    {
        var (a, sa) = Edges(Book(Plain));
        var (b, sb) = Edges(Book(Plain.Replace("g4 a b c |", "g4 a b c ||")));
        var ma = sa.PrimaryContentStaff.PrimaryVoice.Measures;
        var mb = sb.PrimaryContentStaff.PrimaryVoice.Measures;
        Assert.NotEqual(ma[1].EndBarline, mb[1].EndBarline);   // the variant did what it says
        Assert.NotEqual(a[1].ReadByNext, b[1].ReadByNext);
        Assert.Equal(a[1].ReadByPrevious, b[1].ReadByPrevious);
    }

    [Fact]
    public void TheBreakPermission_MovesWhatTheNextBarReads()
    {
        var (a, sa) = Edges(Book(Plain));
        var (b, sb) = Edges(Book(Plain.Replace("g4 a b c |", "g4 a b c | noBreak")));
        var ma = sa.PrimaryContentStaff.PrimaryVoice.Measures;
        var mb = sb.PrimaryContentStaff.PrimaryVoice.Measures;
        Assert.NotEqual(ma[1].LineBreakPermission, mb[1].LineBreakPermission);
        Assert.Equal(ma[1].EndBarline, mb[1].EndBarline);
        Assert.NotEqual(a[1].ReadByNext, b[1].ReadByNext);
    }

    [Fact]
    public void TheMeterInForce_MovesWhatTheNextBarReads()
    {
        var (a, sa) = Edges(Book(Plain));
        var (b, sb) = Edges(Book(Plain.Replace("| g4 a b c |", "| time 3/4 g4 a b |")));
        Assert.NotEqual(ScoreSideTables.PrevailingMeters(sa)[1], ScoreSideTables.PrevailingMeters(sb)[1]);
        Assert.NotEqual(a[1].ReadByNext, b[1].ReadByNext);
    }

    [Fact]
    public void BeingSwallowedByARestRun_MovesWhatThePreviousBarReads()
    {
        var (a, sa) = Edges(Book("c1 | R1*2 | c1 |"));
        var (b, sb) = Edges(Book("c1 | R1 | c1 | c1 |"));
        Assert.True(MmrRunMap.ForScore(sa).IsInterior(2));
        Assert.False(MmrRunMap.ForScore(sb).IsInterior(2));
        Assert.NotEqual(a[2].ReadByPrevious, b[2].ReadByPrevious);
    }

    [Fact]
    public void ADoublePercentSignOnTheOpeningBarLine_MovesWhatThePreviousBarReads()
    {
        var (a, sa) = Edges(Book("repeat percent 2 { c4 d e f | g4 a b c | }"));
        var (b, sb) = Edges(Book("c4 d e f | g4 a b c | c4 d e f | g4 a b c |"));
        var half = ScoreSideTables.DoublePercentHalfWidths(sa);
        int sign = Enumerable.Range(0, half.Count).First(i => half[i] > 0);
        Assert.Equal(0.0, ScoreSideTables.DoublePercentHalfWidths(sb)[sign]);
        Assert.Equal(MmrRunMap.ForScore(sa).IsInterior(sign), MmrRunMap.ForScore(sb).IsInterior(sign));
        Assert.NotEqual(a[sign].ReadByPrevious, b[sign].ReadByPrevious);
    }

    private const string Sung = "lyrics words sings melody { la la la la | la la la la | la la la la | la la la la | }";

    [Fact]
    public void ALyricLineStartingOrEnding_MovesBothSides_ASyllableOfAPresentLineMovesNeither()
    {
        var (a, sa) = Edges(Book(Plain, Sung, "staff melody  lyrics words"));
        // Measure 1 loses its whole line: both neighbours' cross-bar halves change.
        var (b, sb) = Edges(Book(Plain, Sung.Replace("| la la la la | la la la la | la la la la |",
            "| | la la la la | la la la la |"), "staff melody  lyrics words"));
        Assert.NotEmpty(ScoreSideTables.Lyrics(sa).At(1));
        Assert.Empty(ScoreSideTables.Lyrics(sb).At(1));
        Assert.NotEmpty(ScoreSideTables.Lyrics(sb).At(2));
        Assert.NotEqual(a[1].ReadByNext, b[1].ReadByNext);
        Assert.NotEqual(a[1].ReadByPrevious, b[1].ReadByPrevious);
        // A syllable's TEXT changed on a line already present: neither side moves.
        var (c, _) = Edges(Book(Plain, Sung.Replace("| la la la la | la la la la | la la la la |",
            "| la la la la | lo la la la | la la la la |"), "staff melody  lyrics words"));
        Assert.Equal(a[1], c[1]);
    }
}
