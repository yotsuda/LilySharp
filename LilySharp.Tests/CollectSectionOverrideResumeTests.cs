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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A checkpoint's grob-prop set (<see cref="WalkCheckpoint.SectionActiveGrobProps"/>) is
/// what the next section boundary reverts: a resume from a boundary INSIDE a section that
/// overrode a grob in-music must still revert it where the next section starts. No net
/// book held that shape (session 522: a poison that hands every checkpoint an empty set
/// reddened nothing), so this one does.
/// </summary>
public sealed class CollectSectionOverrideResumeTests
{
    private const string Book = """
        part melody
        section A {
          melody { c4 d e f | override NoteHead.color = "red" c4 d e f | c4 d e f | }
        }
        section B {
          melody { c4 d e f | c4 d e f | }
        }
        form main { A B }
        score main { staff melody }
        """;

    [Fact]
    public void AResumeFromInsideAnOverridingSection_StillRevertsAtTheNextSection()
    {
        var tree = SyntaxTree.Parse(Book);
        var spec = RenderSpecParser.FindFirst(tree);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = spec?.ScoreTranspose };
        var full = SvgGenerator.CollectScore(source, tree, spec);

        var (ordinal, recording) = recorder.Recordings.Single(r => r.Value.Checkpoints.Count > 0);
        Assert.Null(recording.IneligibleReason);
        var inside = recording.Checkpoints.Where(ck => ck.SectionActiveGrobProps.Count > 0).ToList();
        Assert.True(inside.Count >= 1, "no checkpoint stands inside the overriding section — the net has nothing to bite on");

        var failures = new List<string>();
        foreach (var checkpoint in recording.Checkpoints)
        {
            var resumer = CollectWalkProbe.Resumer();
            var plan = new VoiceResumePlan { Checkpoint = checkpoint, Recording = recording, Source = source };
            resumer.ResumePlans[ordinal] = plan;
            var resumed = SvgGenerator.CollectScore(
                new MeasureCollector { WalkProbe = resumer, ScoreTranspose = spec?.ScoreTranspose }, tree, spec);
            if (!plan.Consumed)
                failures.Add($"@measure {checkpoint.MeasureCount}: the plan was never consumed");
            else if (ModelDeepDiff.FirstDifference(full, resumed, "score") is { } diff)
                failures.Add($"@measure {checkpoint.MeasureCount} (props {checkpoint.SectionActiveGrobProps.Count}): {diff}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
