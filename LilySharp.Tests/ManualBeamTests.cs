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

using Xunit;
using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class ManualBeamTests
{
    // ========== Parser Tests ==========

    [Fact]
    public void ParseBeamMarkers_NoErrors()
    {
        var tree = SyntaxTree.Parse("{ c8[ d e f] }");
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
    }

    [Fact]
    public void ParseBeamMarkers_ContainsBeamMarkerSyntax()
    {
        var tree = SyntaxTree.Parse("{ c8[ d e f] }");
        var root = tree.GetRoot();
        var beamMarkers = root.DescendantNodes<BeamMarkerSyntax>().ToList();

        Assert.Equal(2, beamMarkers.Count);
        Assert.True(beamMarkers[0].IsStart);
        Assert.False(beamMarkers[1].IsStart);
    }

    [Fact]
    public void ParseBeamMarkers_MultipleGroups()
    {
        var tree = SyntaxTree.Parse("{ c8[ d] e[ f] }");
        var root = tree.GetRoot();
        var beamMarkers = root.DescendantNodes<BeamMarkerSyntax>().ToList();

        Assert.Equal(4, beamMarkers.Count);
        Assert.True(beamMarkers[0].IsStart);
        Assert.False(beamMarkers[1].IsStart);
        Assert.True(beamMarkers[2].IsStart);
        Assert.False(beamMarkers[3].IsStart);
    }

    // ========== Volta Coexistence Tests ==========

    [Fact]
    public void VoltaBracket_NotAffectedByBeamMarkerInMusicBlock()
    {
        var source = @"
section A {
    melody {
        c8[ d e f] g a b c' |
    }
}

structure {
    |: A | [1. A] :| [2. A]
}

render score ""test.svg"" {
    staff treble { melody }
}
";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));
    }

    // ========== MeasureCollector Tests ==========

    [Fact]
    public void MeasureCollector_PropagatesBeamFlags()
    {
        var source = "{ c8[ d e f] }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);

        Assert.NotEmpty(score.Voice.Measures);

        var items = score.Voice.Measures[0].Items;
        Assert.True(items.Length >= 4);

        // First note should have HasBeamStart
        var firstNote = items[0] as NoteItem;
        Assert.NotNull(firstNote);
        Assert.True(firstNote.HasBeamStart);
        Assert.False(firstNote.HasBeamEnd);

        // Last note (index 3) should have HasBeamEnd
        var lastNote = items[3] as NoteItem;
        Assert.NotNull(lastNote);
        Assert.False(lastNote.HasBeamStart);
        Assert.True(lastNote.HasBeamEnd);

        // Middle notes should have neither
        var middleNote = items[1] as NoteItem;
        Assert.NotNull(middleNote);
        Assert.False(middleNote.HasBeamStart);
        Assert.False(middleNote.HasBeamEnd);
    }

    // ========== BeamDetector Tests ==========

    [Fact]
    public void BeamDetector_ManualBeamGroupOverridesAutomatic()
    {
        // In 4/4 time, automatic beaming would split at beat 3:
        // c8 d e f | g a b c' - auto: [c d e f] [g a b c']
        // But with manual beaming: c8[ d e f g a] b c' - should be [c d e f g a] [b c']
        var source = "{ c8[ d e f g a] b c' | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);

        var detector = new BeamDetector();
        var beamGroups = detector.DetectBeamGroups(score);

        // Should have 2 beam groups:
        // 1. Manual: c d e f g a (6 notes)
        // 2. Auto: b c' (2 notes)
        Assert.Equal(2, beamGroups.Length);
        Assert.Equal(6, beamGroups[0].Count);
        Assert.Equal(2, beamGroups[1].Count);
    }

    [Fact]
    public void BeamDetector_ManualBeamGroupWithTwoNotes()
    {
        var source = "{ c8[ d] e f g a b c' | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);

        var detector = new BeamDetector();
        var beamGroups = detector.DetectBeamGroups(score);

        // First group should be manual [c d] with 2 notes
        Assert.True(beamGroups.Length >= 1);
        Assert.Equal(2, beamGroups[0].Count);
    }

    [Fact]
    public void BeamDetector_NoManualBeams_AutomaticStillWorks()
    {
        var source = "{ c8 d e f g a b c' | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);

        var detector = new BeamDetector();
        var beamGroups = detector.DetectBeamGroups(score);

        // Without manual beams, automatic beaming applies
        // In 4/4: pure 8ths grouped per half-measure → [c d e f] [g a b c']
        Assert.Equal(2, beamGroups.Length);
        Assert.Equal(4, beamGroups[0].Count);
        Assert.Equal(4, beamGroups[1].Count);
    }

    // ========== E2E Tests ==========

    [Fact]
    public void EndToEnd_ManualBeam_SvgRendered()
    {
        var source = "{ c8[ d e f] g a b c' | }";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.ToString())));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);
        var renderer = new SvgRenderer();
        var svg = renderer.Render(score, layout);

        // SVG should be non-empty and contain noteheads
        Assert.NotEmpty(svg);
        Assert.Contains("<svg", svg);
    }
}
