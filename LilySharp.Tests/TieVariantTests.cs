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
/// Tests for half-tie variants: laissez-vibrer (l.v.) and repeat-tie.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/laissez-vibrer-engraver.cc — LaissezVibrerTie
/// LILYPOND-REF: lily/repeat-tie-engraver.cc — RepeatTie
/// </remarks>
[Trait("Category", "Unit")]
public class TieVariantTests
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
    public void Note_WithoutTieVariant_HasNoFlags()
    {
        var (score, _) = BuildLayout("c4 |");
        var note = (NoteItem)score.Voice.Measures[0].Items[0];
        Assert.False(note.HasLaissezVibrer);
        Assert.False(note.HasRepeatTie);
    }

    [Fact]
    public void Note_WithLaissezVibrer_HasFlag()
    {
        var (score, _) = BuildLayout("c4@laissezVibrer |");
        var note = (NoteItem)score.Voice.Measures[0].Items[0];
        Assert.True(note.HasLaissezVibrer);
        Assert.False(note.HasRepeatTie);
    }

    [Fact]
    public void Note_WithRepeatTie_HasFlag()
    {
        var (score, _) = BuildLayout("c4@repeatTie |");
        var note = (NoteItem)score.Voice.Measures[0].Items[0];
        Assert.True(note.HasRepeatTie);
        Assert.False(note.HasLaissezVibrer);
    }

    [Fact]
    public void Layout_LaissezVibrer_ProducesTieVariantLayout()
    {
        var (_, layout) = BuildLayout("c4@laissezVibrer |");
        Assert.Single(layout.TieVariantLayouts);
        Assert.Equal(TieVariantKind.LaissezVibrer, layout.TieVariantLayouts[0].Kind);
    }

    [Fact]
    public void Layout_RepeatTie_ProducesTieVariantLayout()
    {
        var (_, layout) = BuildLayout("c4@repeatTie |");
        Assert.Single(layout.TieVariantLayouts);
        Assert.Equal(TieVariantKind.Repeat, layout.TieVariantLayouts[0].Kind);
    }

    [Fact]
    public void Layout_LaissezVibrer_ExtendsRightFromNote()
    {
        var (_, layout) = BuildLayout("c4@laissezVibrer |");
        var lv = layout.TieVariantLayouts[0];
        // For l.v., StartX is the note's right edge and EndX is further right.
        Assert.True(lv.EndX > lv.StartX);
        // The visible end should be at least 1 staff space from the start (TieLength=1.0).
        Assert.True(lv.EndX - lv.StartX >= 0.9,
            $"L.v. tie should be at least ~1.0 ss long, got {lv.EndX - lv.StartX}");
    }

    [Fact]
    public void Layout_RepeatTie_StartsBeforeNote()
    {
        var (_, layout) = BuildLayout("c4@repeatTie |");
        var rt = layout.TieVariantLayouts[0];
        // For repeat-tie, the curve starts to the LEFT of the note and ends at the note's left edge.
        Assert.True(rt.EndX > rt.StartX);
        Assert.True(rt.EndX - rt.StartX >= 0.9,
            $"Repeat tie should be at least ~1.0 ss long, got {rt.EndX - rt.StartX}");
    }

    [Fact]
    public void Layout_LowNote_CurvesDown()
    {
        // A single unforced semi-tie takes sign(position): low c (StaffPosition < 0)
        // → DOWN. LILYPOND-REF: lily/tie-formatting-problem.cc:1026-1066
        // set_ties_config_standard_directions.
        var (_, layout) = BuildLayout("c4@laissezVibrer |");
        Assert.False(layout.TieVariantLayouts[0].CurveUp);
    }

    [Fact]
    public void Layout_HighNote_CurvesUp()
    {
        // c'' is high (StaffPosition > 0) → sign(position) = UP.
        var (_, layout) = BuildLayout("c''4@laissezVibrer |");
        Assert.True(layout.TieVariantLayouts[0].CurveUp);
    }

    [Fact]
    public void Note_WithBothFlags_ProducesTwoLayouts()
    {
        // Edge case: a note carrying both annotations gets one layout per kind.
        var (_, layout) = BuildLayout("c4@laissezVibrer@repeatTie |");
        Assert.Equal(2, layout.TieVariantLayouts.Length);
        Assert.Contains(layout.TieVariantLayouts, l => l.Kind == TieVariantKind.LaissezVibrer);
        Assert.Contains(layout.TieVariantLayouts, l => l.Kind == TieVariantKind.Repeat);
    }
}
