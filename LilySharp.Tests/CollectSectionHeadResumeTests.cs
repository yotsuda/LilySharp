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
/// A resume from any checkpoint of a book whose section heads restate what the section before
/// left (MeasureBuilder.SectionHead) collects the same score as the full walk. The head is
/// carried in the builder's checkpoint, because a clean boundary can stand at a head whose
/// prologue drew no reset, and a resume from there skips that prologue.
/// </summary>
public sealed class CollectSectionHeadResumeTests
{
    // The key is E♭ everywhere, so no head draws a reset and every head is a clean boundary;
    // B and C state the key again (nothing drawn), D states a new meter (drawn).
    private const string Book = """
        key ees major
        part melody { clef treble }
        section A { melody { c4 d e f | c4 d e f | } }
        section B { melody { key ees major c4 d e f | c4 d e f | } }
        section C { melody { key ees major c4 d e f | } }
        section D { melody { time 3/4 c4 d e | c4 d e | } }
        form { A B C D }
        score { staff melody }
        """;

    [Fact]
    public void AResumeFromAnyCheckpoint_CollectsTheRestatementsAsTheFullWalkDoes()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = spec?.ScoreTranspose };
        var full = SvgGenerator.CollectScore(source, tree, spec);

        var (ordinal, recording) = recorder.Recordings.Single(r => r.Value.Checkpoints.Count > 0);
        Assert.Null(recording.IneligibleReason);

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
                failures.Add($"@measure {checkpoint.MeasureCount}: {diff}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
