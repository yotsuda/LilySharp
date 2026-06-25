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
/// Tests for the fingering pipeline: <c>@finger.N</c> on a note flows through
/// parser → collector → NoteItem.Fingering → FingeringEngraver → FingeringLayout.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob
/// </remarks>
[Trait("Category", "Unit")]
public class FingeringTests
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
    public void Note_WithoutFingering_HasNullFingering()
    {
        var (score, _) = BuildLayout("c4 d4 e4 f4 |");
        var firstNote = (NoteItem)score.Voice.Measures[0].Items[0];
        Assert.Null(firstNote.Fingering);
    }

    [Fact]
    public void Note_WithFingerOne_HasFingeringOne()
    {
        var (score, _) = BuildLayout("c4@finger.1 |");
        var note = (NoteItem)score.Voice.Measures[0].Items[0];
        Assert.Equal(1, note.Fingering);
    }

    [Fact]
    public void Notes_WithDifferentFingerings_PreserveEachValue()
    {
        var (score, _) = BuildLayout("c4@finger.1 d4@finger.2 e4@finger.3 f4@finger.4 |");
        var notes = score.Voice.Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, notes.Select(n => n.Fingering!.Value));
    }

    [Fact]
    public void Layout_ContainsOneFingeringPerAnnotatedNote()
    {
        var (_, layout) = BuildLayout("c4@finger.1 d4 e4@finger.3 f4 |");
        Assert.Equal(2, layout.FingeringLayouts.Length);

        var byNumber = layout.FingeringLayouts.ToLookup(f => f.Number);
        Assert.Single(byNumber[1]);
        Assert.Single(byNumber[3]);
    }

    [Fact]
    public void Layout_FingeringX_IsCenteredOnHostNote()
    {
        var (_, layout) = BuildLayout("c4@finger.2 |");
        var fingering = layout.FingeringLayouts[0];

        // The fingering centers on the NOTEHEAD GLYPH (self-alignment-X =
        // CENTER on the note column), not on the spacing-allocated width.
        var measure = layout.AllSystems[0].Measures[0];
        var item = measure.Items[fingering.ItemIndex];
        double noteCenterX = measure.X + item.X
            + LilySharp.Core.Svg.Layout.GlyphMetrics.NoteheadBlackAdvance / 2.0;
        Assert.Equal(noteCenterX, fingering.X, precision: 4);
    }

    [Fact]
    public void Layout_StemDownNote_PlacesFingeringAbove()
    {
        // High notes (positive staff position) → stem down → fingering above (default direction)
        // c''4 (staffPos > 0) is well above the staff middle line, stem points down.
        var (_, layout) = BuildLayout("c''4@finger.1 |");
        Assert.Single(layout.FingeringLayouts);
        Assert.True(layout.FingeringLayouts[0].IsAbove);
    }

    [Fact]
    public void Layout_StemUpNote_StillPlacesFingeringAbove()
    {
        // A melodic (single-voice) fingering defaults ABOVE regardless of stem
        // direction: LilyPond's fingeringOrientations default is '(up down), so
        // even a stem-UP note keeps its fingering above (same side as the stem).
        // c4 in treble clef has staffPos < 0 (stem up); the fingering is still above.
        // LILYPOND-REF: ly/engraver-init.ly:907 fingeringOrientations = #'(up down).
        var (_, layout) = BuildLayout("c4@finger.4 |");
        Assert.Single(layout.FingeringLayouts);
        Assert.True(layout.FingeringLayouts[0].IsAbove);
    }
}
