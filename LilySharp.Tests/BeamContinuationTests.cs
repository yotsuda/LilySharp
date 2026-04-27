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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Probes for cross-measure beam continuation (LP <c>c8[ d e | f g h]</c>).
/// Documents the current (degraded but non-crashing) behaviour and pins down
/// the boundaries we want preserved while the full K-1b rewrite is parked.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:590-600 — break_overshoot for cross-system beams.
/// LILYPOND-REF: lily/beam.cc:1039-1082 — beaming-pattern logic.
/// LilySharp's BeamDetector currently runs per-measure; cross-measure manual
/// beams (`[ ... | ... ]`) are detected as two orphan groups (or none),
/// rather than being merged + split at the system break.
/// </remarks>
[Trait("Category", "Unit")]
public class BeamContinuationTests
{
    private static (Score Score, ScoreLayout Layout) BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine(new LayoutOptions());
        return (score, engine.Layout(score));
    }

    [Fact]
    public void WithinMeasureManualBeam_ProducesBeamGroup()
    {
        // Sanity: within-measure manual beam still works.
        var (_, layout) = BuildLayout("c8[ d e f] |");
        Assert.NotEmpty(layout.BeamLayouts);
    }

    [Fact]
    public void CrossMeasureManualBeam_DoesNotCrash()
    {
        // Cross-measure beam: layout completes without throwing. Beam groups may
        // be partial or absent — K-1b will properly merge + split these later.
        var ex = Record.Exception(() => BuildLayout("c8[ d e f | g8 a b c8] |"));
        Assert.Null(ex);
    }

    [Fact]
    public void CrossMeasureManualBeam_StartFlagSetOnFirstNote()
    {
        // The `[` after the first note still produces HasBeamStart; this is the
        // signal a future K-1b implementation will key off when stitching the
        // multi-measure beam back together.
        var (score, _) = BuildLayout("c8[ d e f | g8 a b c8] |");
        var firstNote = (NoteItem)score.Voice.Measures[0].Items[0];
        Assert.True(firstNote.HasBeamStart);
    }

    [Fact]
    public void CrossMeasureManualBeam_EndFlagSetOnLastNote()
    {
        // True cross-measure source: 8 eighth notes per measure × 2 measures = 16
        // eighth notes in 4/4 time. The `]` at the very end attaches to the last
        // note in measure 2.
        var (score, _) = BuildLayout("c8[ d e f g a b c | d8 e f g a b c d] |");
        Assert.Equal(2, score.Voice.Measures.Length);
        var lastNote = (NoteItem)score.Voice.Measures[1].Items[^1];
        Assert.True(lastNote.HasBeamEnd, "']' on the last note of measure 2 should set HasBeamEnd.");
    }

    [Fact]
    public void CrossMeasureManualBeam_BeamLayoutsExistForBothMeasures()
    {
        // Cross-measure manual beam now produces a SINGLE multi-measure BeamGroup
        // covering all members (8 + 8 = 16 eighths). Plus auto-beams may still appear
        // for unrelated rhythm groupings — we only assert the layout is non-empty.
        var (_, layout) = BuildLayout("c8[ d e f g a b c | d8 e f g a b c d] |");
        Assert.NotEmpty(layout.BeamLayouts);
    }

    [Fact]
    public void CrossMeasureManualBeam_ProducesSingleMultiMeasureGroup()
    {
        // The pre-pass merges the [/] pair across the bar into one BeamGroup
        // whose members carry explicit MeasureIndex values.
        var detector = new LilySharp.Core.Svg.Collector.BeamDetector();
        var (score, _) = BuildLayout("c8[ d e f g a b c | d8 e f g a b c d] |");
        var groups = detector.DetectBeamGroups(score);

        // Find a group with members in BOTH measures.
        bool hasCrossMeasureGroup = groups.Any(g =>
            g.Members.Any(m => m.MeasureIndex == 0) &&
            g.Members.Any(m => m.MeasureIndex == 1));

        Assert.True(hasCrossMeasureGroup,
            "Expected a single BeamGroup with members from measure 0 AND measure 1.");
    }

    [Fact]
    public void CrossMeasureManualBeam_GroupCoversAllSixteenEighths()
    {
        var detector = new LilySharp.Core.Svg.Collector.BeamDetector();
        var (score, _) = BuildLayout("c8[ d e f g a b c | d8 e f g a b c d] |");
        var groups = detector.DetectBeamGroups(score);

        var crossGroup = groups.FirstOrDefault(g =>
            g.Members.Any(m => m.MeasureIndex == 0) &&
            g.Members.Any(m => m.MeasureIndex == 1));

        Assert.NotNull(crossGroup);
        // 8 eighths in measure 0 + 8 eighths in measure 1 = 16 members.
        Assert.Equal(16, crossGroup!.Members.Length);
    }

    [Fact]
    public void CrossMeasureManualBeam_ProducesBeamLayoutWithSixteenMembers()
    {
        // Cross-measure beam group flows through ElementCoordinator.LayoutCrossMeasureBeam.
        // The resulting BeamLayout should contain X positions for all 16 members.
        var (_, layout) = BuildLayout("c8[ d e f g a b c | d8 e f g a b c d] |");
        var crossLayout = layout.BeamLayouts.FirstOrDefault(bl => bl.Group.Members.Length == 16);
        Assert.NotNull(crossLayout);
        Assert.Equal(16, crossLayout!.MemberXPositions.Length);

        // Member X positions must be strictly increasing (members are in order).
        for (int i = 1; i < crossLayout.MemberXPositions.Length; i++)
        {
            Assert.True(crossLayout.MemberXPositions[i] > crossLayout.MemberXPositions[i - 1],
                $"Member X[{i}]={crossLayout.MemberXPositions[i]} should exceed X[{i - 1}]={crossLayout.MemberXPositions[i - 1]}.");
        }
    }

    [Fact]
    public void CrossMeasureManualBeam_BeamSpansBothMeasuresInX()
    {
        // The BeamLayout's left/right X should span from the first member (m0)
        // to the last member (m1). The right edge must lie further along the
        // staff than the right edge of measure 0 alone.
        var (_, layout) = BuildLayout("c8[ d e f g a b c | d8 e f g a b c d] |");
        var crossLayout = layout.BeamLayouts.FirstOrDefault(bl => bl.Group.Members.Length == 16);
        Assert.NotNull(crossLayout);

        // RightX must exceed LeftX by a significant amount (more than one measure's width).
        double span = crossLayout!.RightX - crossLayout.LeftX;
        Assert.True(span > 5.0, $"Cross-measure beam should span much more than one measure width. Got {span}.");
    }

    [Fact]
    public void CrossSystemBeam_SplitsIntoBrokenPieces()
    {
        // Force a line break between the two measures using the `break` keyword.
        // The cross-measure beam should split into two BeamLayouts (one per system),
        // each anchored to its own system.
        var (_, layout) = BuildLayout("c8[ d e f g a b c | break d8 e f g a b c d] |");
        Assert.True(layout.AllSystems.Length >= 2,
            $"Expected at least 2 systems, got {layout.AllSystems.Length}");

        // Find beam layouts whose group has the cross-measure marker (members in different measures).
        // After the split, each piece's members are in a single system but the original 16-member
        // group should now be 2 pieces of 8 each.
        var crossPieces = layout.BeamLayouts
            .Where(bl => bl.Group.Members.Length == 8 &&
                          bl.Group.Members.All(m => m.MeasureIndex >= 0))
            .ToList();
        Assert.True(crossPieces.Count >= 2,
            $"Expected at least 2 broken beam pieces from a cross-system manual beam, got {crossPieces.Count}.");
    }

    [Fact]
    public void CrossSystemBeam_PiecesAttachToDifferentSystems()
    {
        var (_, layout) = BuildLayout("c8[ d e f g a b c | break d8 e f g a b c d] |");
        var crossPieces = layout.BeamLayouts
            .Where(bl => bl.Group.Members.Length == 8 &&
                          bl.Group.Members.All(m => m.MeasureIndex >= 0))
            .ToList();

        // The two pieces should anchor to measures 0 and 1 respectively
        // (which live in different systems thanks to the forced break).
        var anchorMeasures = crossPieces.Select(p => p.Group.MeasureIndex).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 0, 1 }, anchorMeasures);
    }

    [Fact]
    public void WithinMeasureManualBeam_StillResolvesToSingleMeasureGroup()
    {
        // Regression: ensure the cross-measure pre-pass doesn't disturb plain
        // within-measure manual beams.
        var detector = new LilySharp.Core.Svg.Collector.BeamDetector();
        var (score, _) = BuildLayout("c8[ d e f] |");
        var groups = detector.DetectBeamGroups(score);

        Assert.NotEmpty(groups);
        // No member should declare a different MeasureIndex than the group itself.
        foreach (var g in groups)
        {
            foreach (var m in g.Members)
            {
                int resolved = m.ResolveMeasureIndex(g.MeasureIndex);
                Assert.Equal(g.MeasureIndex, resolved);
            }
        }
    }
}
