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
/// Tests per-pitch fingering inside chord brackets (L-2 ext / FingeringColumn).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob on chord pitches
/// LILYPOND-REF: lily/fingering-column.cc — FingeringColumn stacking
/// </remarks>
[Trait("Category", "Unit")]
public class ChordFingeringTests
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
    public void Chord_WithoutFingering_HasNoChordFingering()
    {
        var (score, _) = BuildLayout("<c e g>4 |");
        var chord = (ChordItem)score.Voice.Measures[0].Items[0];
        Assert.All(chord.Notes, n => Assert.Null(n.Fingering));
    }

    [Fact]
    public void Chord_PerPitchFingering_PropagatesToChordNoteInfo()
    {
        // <c@finger(1) e@finger(3) g@finger(5)> attaches a different finger to each pitch.
        var (score, _) = BuildLayout("<c@finger(1) e@finger(3) g@finger(5)>4 |");
        var chord = (ChordItem)score.Voice.Measures[0].Items[0];
        Assert.Equal(3, chord.Notes.Length);
        Assert.Equal(1, chord.Notes[0].Fingering);
        Assert.Equal(3, chord.Notes[1].Fingering);
        Assert.Equal(5, chord.Notes[2].Fingering);
    }

    [Fact]
    public void Chord_PartialFingering_OnlyMarkedNotesCarryNumbers()
    {
        var (score, _) = BuildLayout("<c@finger(1) e g@finger(5)>4 |");
        var chord = (ChordItem)score.Voice.Measures[0].Items[0];
        Assert.Equal(1, chord.Notes[0].Fingering);
        Assert.Null(chord.Notes[1].Fingering);
        Assert.Equal(5, chord.Notes[2].Fingering);
    }

    [Fact]
    public void Layout_ChordFingerings_OneLayoutPerAnnotatedPitch()
    {
        var (_, layout) = BuildLayout("<c@finger(1) e@finger(3) g@finger(5)>4 |");
        Assert.Equal(3, layout.FingeringLayouts.Length);
        var numbers = layout.FingeringLayouts.Select(f => f.Number).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { 1, 3, 5 }, numbers);
    }

    [Fact]
    public void Layout_ChordFingerings_AllShareSameItemIndex()
    {
        // Each chord-pitch fingering points at the chord's item index in the measure.
        var (_, layout) = BuildLayout("<c@finger(1) e@finger(3) g@finger(5)>4 |");
        var distinctItemIndices = layout.FingeringLayouts.Select(f => f.ItemIndex).Distinct().ToList();
        Assert.Single(distinctItemIndices);
    }

    [Fact]
    public void Layout_ChordFingerings_DistinctYPositions()
    {
        // Each pitch sits at a different staff position so its finger Y differs.
        var (_, layout) = BuildLayout("<c@finger(1) e@finger(3) g@finger(5)>4 |");
        var distinctYs = layout.FingeringLayouts.Select(f => f.Y).Distinct().ToList();
        Assert.Equal(3, distinctYs.Count);
    }

    [Fact]
    public void SingleNoteFingering_StillWorks_NoRegression()
    {
        // Regression check: existing single-note `c@finger(1)` path keeps working.
        var (score, _) = BuildLayout("c4@finger(1) |");
        var note = (NoteItem)score.Voice.Measures[0].Items[0];
        Assert.Equal(1, note.Fingering);
    }
}
