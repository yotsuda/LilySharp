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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>BeamScoringProblem.SolveLent</c> answers a question it has answered before for the same
/// beam group from its memo (<c>s_solved</c>). Both halves of a memo net (HANDOFF RULES §5.4):
/// the memo actually serves (liveness — an equality net a memo never fires under passes for
/// the wrong reason), and a question differing in one input is solved afresh (soundness),
/// asserted on a case where the two questions really do have different answers.
/// </summary>
public class BeamSolveMemoTests
{
    private static NoteItem Note(int staffPosition) =>
        new(staffPosition, Fraction.Eighth, 0, null, false, 0);

    /// <summary>A new group each call: the memo is keyed on the group's identity, so no
    /// other test's answers can reach these.</summary>
    private static BeamGroup NewGroup() =>
        new(ImmutableArray.Create(
                new BeamMember(Note(0), 1, 0, 1, 0, 0),
                new BeamMember(Note(0), 1, 1, 0, 0, 2)),
            0, 0, stemUp: true);

    private static readonly List<double> Xs = [50.0, 75.0, 100.0];

    [Fact]
    public void TheSameQuestionTwice_IsAnsweredOnce_AndRight()
    {
        var group = NewGroup();
        long before = BeamScoringProblem.t_solvedHits;
        var first = BeamScoringProblem.SolveLent(group, Xs);
        long afterFirst = BeamScoringProblem.t_solvedHits;
        // An equal list, not the same one: the key is the values.
        var second = BeamScoringProblem.SolveLent(group, new List<double>(Xs));

        Assert.Equal(before, afterFirst);
        Assert.Equal(afterFirst + 1, BeamScoringProblem.t_solvedHits);
        Assert.Equal(first, second);
        var fresh = new BeamScoringProblem(group, Xs).Solve();
        Assert.Equal((fresh.leftY, fresh.rightY), (second.LeftY, second.RightY));
    }

    [Fact]
    public void AQuestionDifferingInOneCollision_IsSolvedAfresh()
    {
        var group = NewGroup();
        var bare = BeamScoringProblem.SolveLent(group, Xs);
        // A large object right where the bare beam sits (Solve answers in staff POSITIONS,
        // a collision speaks staff SPACES) — BeamScoringTests shows it moves the beam.
        var collisions = new List<BeamCollision>
        {
            new(X: 25.0, MinY: bare.LeftY / 2 - 0.5, MaxY: bare.LeftY / 2 + 0.5, BasePenalty: 5.0),
        };
        var fresh = new BeamScoringProblem(group, Xs, collisions: collisions).Solve();
        Assert.True(Math.Abs(fresh.leftY - bare.LeftY) > 0.01,
            "the collision does not move the beam — the net cannot tell a wrong answer");

        long before = BeamScoringProblem.t_solvedHits;
        var withObject = BeamScoringProblem.SolveLent(group, Xs, collisions: collisions);

        Assert.Equal(before, BeamScoringProblem.t_solvedHits);
        Assert.Equal((fresh.leftY, fresh.rightY), (withObject.LeftY, withObject.RightY));
        // And the first question is still served beside the second.
        Assert.Equal(bare, BeamScoringProblem.SolveLent(group, Xs));
        Assert.Equal(before + 1, BeamScoringProblem.t_solvedHits);
    }

    [Fact]
    public void AQuestionDifferingInOneMemberX_IsSolvedAfresh()
    {
        var group = NewGroup();
        BeamScoringProblem.SolveLent(group, Xs);
        // Only the members' cells count (Bind reads nothing else of the list)…
        long before = BeamScoringProblem.t_solvedHits;
        BeamScoringProblem.SolveLent(group, [50.0, 80.0, 100.0]);
        Assert.Equal(before + 1, BeamScoringProblem.t_solvedHits);
        // …and a member's cell is a different question.
        List<double> steeper = [50.0, 75.0, 60.0];
        var moved = BeamScoringProblem.SolveLent(group, steeper);
        Assert.Equal(before + 1, BeamScoringProblem.t_solvedHits);
        var fresh = new BeamScoringProblem(group, steeper).Solve();
        Assert.Equal((fresh.leftY, fresh.rightY), (moved.LeftY, moved.RightY));
    }

    /// <summary>
    /// THE PAIR IT WAS WRITTEN FOR: the staff skylines and the preliminary annotation pass
    /// quant the same beams of a render (session 589: every repeat on the reader's corpus was
    /// this pair). A render of a beamed book must be served from the memo at least once, or
    /// the memo has stopped doing its one job.
    /// </summary>
    [Fact]
    public void ARender_ServesThePreliminaryPassFromTheSkylines()
    {
        const string book = """
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody {
              c'8 d' e' f' g' a' b' c'' | c''8 b' a' g' f' e' d' c' |
            } }
            form main { Main }
            score main "x" { staff melody }
            """;
        long before = BeamScoringProblem.t_solvedHits;
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(book),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        compiler.RenderIncrementalPages(SyntaxTree.Parse(book), CancellationToken.None);
        Assert.True(BeamScoringProblem.t_solvedHits > before,
            "no beam of the render was answered from the memo");
    }
}
