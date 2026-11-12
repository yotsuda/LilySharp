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

using System;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Four collisions found on 2026-11-12, writing large scores to try the grammar (Lab
/// sessions/p1226/md/eval2/verify, c1 / c2 / c3 / c5). Each test holds two pieces of drawn
/// ink apart — never a coordinate — so a later change of either one's placement rule keeps
/// the test as long as the two still do not meet.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EngravingCollisions2Tests
{
    // Device Y grows DOWN on the recorded page, so "below" is the larger Y.

    /// <summary>
    /// c1 — a form's "D.S. al Coda" under a grand staff was set on the left hand's sustain
    /// bracket's own row, through its release hook
    /// (<c>MusicMarkEngraver.Calculate</c>'s below-ink arm now reads the placed brackets).
    /// </summary>
    [Fact]
    public void AJumpTextUnderTheSystem_StandsClearOfThePedalBracketOverIt()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            time 3/4
            part rh { }
            part lh { clef bass }
            section A { rh { g'2. | g'2. | } lh { g,2.@sustain | d,2.@!sustain | } }
            section B { rh { g'2. | } lh { g,2. | } }
            form { segno ~A to coda ~B ds al coda coda ~B }
            score { grandStaff { staff rh staff lh } }
            """);
        var jump = g.Texts.Single(t => t.Text == "D.S. al Coda");
        double lowestStaffLine = g.Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && Math.Abs(l.X2 - l.X1) > 10.0)
            .Max(l => l.Y1);
        // The bracket: its rule and its two hooks, the only thin rules under the bottom staff.
        var bracket = g.Lines
            .Where(l => Math.Abs(l.StrokeWidth - EngravingDefaults.StaffLineThickness) < 1e-9
                && Math.Abs(l.X2 - l.X1) < 10.0
                && Math.Min(l.Y1, l.Y2) > lowestStaffLine + 0.5)
            .ToList();
        Assert.Equal(3, bracket.Count);
        // The word's ink top, under-read on purpose: 0.6 em is below the cap height of any
        // text face, so the test cannot pass on a word whose capitals reach the bracket.
        double jumpTop = jump.Y - 0.6 * jump.FontSize;
        double bracketBottom = bracket.Max(l => Math.Max(l.Y1, l.Y2))
            + EngravingDefaults.StaffLineThickness / 2.0;
        Assert.True(jumpTop > bracketBottom,
            $"\"D.S. al Coda\" reaches up to {jumpTop:F3} and the pedal bracket down to "
            + $"{bracketBottom:F3}: the text has to stand under the bracket, not on its row.");
    }

    /// <summary>
    /// c2 — <c>d1:32@pp</c> in the bass clef printed the dynamic on top of the tremolo strokes
    /// hanging under the stemless head: the strokes were in no skyline
    /// (<c>SkylineBuilder.MergeStemlessTremolo</c>).
    /// </summary>
    [Fact]
    public void ADynamicUnderAStemlessTremolo_StandsClearOfItsStrokes()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            time 4/4
            part t { clef bass }
            section A { t { d1:32@pp | a,1:32@ff | d2.:32@pp r4 | } }
            form { A }
            score { staff t }
            """);
        // The first bar's label, and the three strokes that hang under that bar's head.
        var label = g.Texts.Where(t => t.Text == "pp").OrderBy(t => t.X).First();
        var strokes = g.Lines
            .Where(l => Math.Abs(l.StrokeWidth - EngravingDefaults.BeamThickness) < 1e-9
                && Math.Abs((l.X1 + l.X2) / 2 - label.X) < 1.0)
            .ToList();
        Assert.Equal(3, strokes.Count);
        Assert.True(GlyphMetrics.TryGetDynamicInk("pp", out _, out double inkTop));
        double labelTop = label.Y - inkTop;
        double strokesBottom = strokes.Max(l => Math.Max(l.Y1, l.Y2)) + EngravingDefaults.BeamThickness / 2;
        Assert.True(labelTop > strokesBottom,
            $"the pp's ink top stands at {labelTop:F3} and the lowest tremolo stroke reaches "
            + $"{strokesBottom:F3}: the dynamic has to clear the strokes, as it clears a stem.");
        // …and by no more than the collision pass's padding and change: it is the strokes it
        // stands off, not a band under them.
        Assert.InRange(labelTop - strokesBottom, 0.0, OutsideStaffStacker.OutsideStaffPadding + 0.6);
    }

    /// <summary>
    /// c3 — an ottava stopping on a DOTTED HALF note stood its hook a black head's width past
    /// the column, inside the dot's column and down the side of the trill over the note; the
    /// right bound is the heads' extent united with their dots'
    /// (<c>OttavaBracketEngraver.HeadsAndDotsRight</c>; LilyPond 2.26.0 on the same book reads
    /// 2.88 from the head's left edge).
    /// </summary>
    [Fact]
    public void AnOttavaEndingOnADottedNote_HooksPastTheDot()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            time 3/4
            part a { }
            section A { a { g''4@8va a'' b'' | a''2.@trill@!8va | } }
            form { A }
            score { staff a }
            """);
        double hookFromHead = g.OttavaHookToNotehead(3);
        double headLeft = g.NoteheadAnchor(3);
        var dot = g.Glyphs.Single(d => d.Glyph == EmmentalerGlyphs.AugmentationDot);
        double dotRight = dot.X + GlyphMetrics.AugmentationDot.Right - headLeft;
        Assert.True(hookFromHead > dotRight,
            $"the hook stands {hookFromHead:F3} right of the last head's left edge and the dot "
            + $"ends at {dotRight:F3}: the bracket has to run past the dot before it turns down.");
        // …by the shorten-pair's right half (0.6), which is where LilyPond's stands.
        Assert.InRange(hookFromHead - dotRight, 0.5, 0.7);
    }

    /// <summary>
    /// c5 — a fermata on a <c>chord(…)</c> item under a <c>chords</c> row was drawn through the
    /// row's symbol: the outside-staff pass lifts it over the item's diagram, and the room had
    /// reserved it inside the diagram (<c>SkylineBuilder.LiftMoversOverDiagrams</c>).
    /// </summary>
    [Fact]
    public void AFermataOnAChordItem_StandsClearOfTheChordRowOverIt()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part gtr { instrument guitar }
            section A {
              chords harmony { G6 | Gmaj9 | }
              gtr { chord(G6 320000)1 | chord(Gmaj9 3x0202)1@fermata | }
            }
            form { ~A }
            score { chords harmony  staff gtr }
            """);
        var fermata = g.Glyphs.Single(f => f.Glyph == EmmentalerGlyphs.FermataAbove);
        double fermataTop = fermata.Y - GlyphMetrics.FermataAboveGlyph.Top;
        double fermataBottom = fermata.Y - GlyphMetrics.FermataAboveGlyph.Bottom;
        // The chord letters over the fermata's own column: the row's symbol and the item's.
        var letters = g.Texts
            .Where(t => t.Text == "G" && Math.Abs(t.X - fermata.X) < 3.0)
            .OrderBy(t => t.Y)
            .ToList();
        Assert.Equal(2, letters.Count);
        foreach (var letter in letters)
        {
            // A capital G: from its baseline up about 0.72 em (a round letter's overshoot
            // under the baseline is hundredths). The fermata is wholly over it or wholly under.
            double letterTop = letter.Y - 0.72 * letter.FontSize;
            double letterBottom = letter.Y;
            Assert.True(fermataBottom <= letterTop || fermataTop >= letterBottom,
                $"the fermata's ink runs {fermataTop:F3}..{fermataBottom:F3} and a chord symbol's "
                + $"G {letterTop:F3}..{letterBottom:F3} on the same column: they overprint.");
        }
    }
}
