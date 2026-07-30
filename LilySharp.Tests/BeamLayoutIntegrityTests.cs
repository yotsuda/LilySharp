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
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Beam layout must stay indexable against the real score: members carry REAL
/// (measure, item) positions (the renderer's stem/flag suppression set and the
/// data-pos resolver key on them), and a beam whose voice's item stream is not
/// covered by the layout's X table must be skipped, not crash.
/// </summary>
[Trait("Category", "Unit")]
public class BeamLayoutIntegrityTests
{
    private static string Wrap(string music) => $$"""
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody { {{music}} } }
        form main { Main }
        score main "x" { staff melody }
        """;

    [Fact]
    public void SecondaryVoiceBeams_OnTheNonColumnPath_AreSkippedNotCrashed()
    {
        // Voice 2's beam groups carry item indices (0..7) far beyond the
        // primary voice's layout items. On the NON-COLUMN path (a MeasureLayout
        // without timing columns) the X table is built from those layout items,
        // so indexing a member's ItemIndex used to throw
        // ArgumentOutOfRangeException out of CollectBeamCollisions and kill the
        // whole layout. The uncoverable beam must be skipped instead (same
        // convention as the renderer's item-range guard).
        string src = Wrap("voice { c'1 | } voice { g8 g g g g g g g | }");
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");

        // A column-less measure layout for the primary voice — the degenerate
        // shape that forces LayoutBeams onto the non-column path.
        var measureLayout = new MeasureLayout(0, 0, 20, ImmutableArray<ItemLayout>.Empty);
        var system = new SystemLayout(0, 0, 100, 0, ImmutableArray.Create(measureLayout));

        var beams = new ElementCoordinator(LayoutOptions.Default)
            .LayoutBeams(score, ImmutableArray.Create(system), staffIndex: 0);

        Assert.Empty(beams); // skipped, and no ArgumentOutOfRangeException
    }

    [Fact]
    public void CrossMeasureBeam_MembersKeepRealItemIndices()
    {
        // The scorer needs dense member indices to look up memberXs, but the
        // EMITTED layout must carry the real (measure, item) positions — the
        // renderer suppresses the members' own stems/flags by exactly these
        // keys. Dense indices here meant duplicate stems on the beamed notes
        // and suppressed stems on unrelated items (the stray-stem defect
        // class).
        string src = Wrap("r4 r4 r4 c8[ d8 | e8 f8] r4 r4 r4 |");
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");
        var layout = new LayoutEngine().Layout(score);

        var beam = Assert.Single(layout.BeamLayouts);
        var members = beam.Group.Members
            .Select(m => (Measure: m.ResolveMeasureIndex(beam.Group.MeasureIndex), Item: m.ItemIndex))
            .ToArray();
        Assert.Equal(new[] { (0, 3), (0, 4), (1, 0), (1, 1) }, members);
    }
}
