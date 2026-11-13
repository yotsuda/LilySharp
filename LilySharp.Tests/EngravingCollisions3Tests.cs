// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
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
/// The collisions four more large scores found on 2026-11-13 (grammar evaluation 3; Lab
/// sessions/p1226/md/eval3/fixC, b4 – b7). As in <see cref="EngravingCollisions2Tests"/>, each
/// test holds two pieces of drawn ink apart — never a coordinate.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EngravingCollisions3Tests
{
    // Device Y grows DOWN on the recorded page, so "below" is the larger Y.

    /// <summary>
    /// b4 — <c>r8@text("Vamp (repeat till ready)").up</c> under a chords row was printed
    /// through the row's names: the words above a staff were reserved as a dynamic level's
    /// small box at their middle (<c>SkylineBuilder.AddDynamicsToSkyline</c>'s above arm), so
    /// the row was set where the words were then drawn. Also the word over a cue.
    /// </summary>
    [Theory]
    [InlineData("score { chords h  staff rh }")]
    [InlineData("score { chords h  grandStaff { staff rh staff lh } }")]
    public void WordsAboveAStaff_StandClearOfTheChordsRowOverIt(string score)
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part rh { instrument piano-right }
            part lh { instrument piano-left }
            section A {
              chords h { D G/D | D A7 | Bm | }
              rh { r8@text("Vamp (repeat till ready)").up <fis' a'>8 r8 <fis' a'>8 r8 <g' b'>8 r8 <g' b'>8 |
                   r8 <fis' a'>8 r8 <fis' a'>8 r8 <e' g' cis''>8 r8 q8 |
                   cue bass { b4@text("(Jon)").up b8 cis'8 d'4 b4 | } }
              lh { d4 a4 d4 b4 | d4 a4 a,4 e4 | <b, fis>1 | }
            }
            form { ~A }
            """ + "\n" + score + "\n");
        double rowBaseline = g.Texts.Where(t => t.Role == TextRole.ChordName && t.Text is "D" or "Bm").Max(t => t.Y);
        foreach (string words in new[] { "Vamp (repeat till ready)", "(Jon)" })
        {
            var text = g.Texts.Single(t => t.Text == words);
            // The words' ink top, over-read on purpose: 0.8 em is above the ascenders and the
            // parentheses of any text face, so the test cannot pass on words that touch a name.
            double top = text.Y - 0.8 * text.FontSize;
            Assert.True(top > rowBaseline,
                $"\"{words}\" reaches up to {top:F3} and the chords row's names stand on {rowBaseline:F3}: "
                + "the row has to stand over the words, not in them.");
        }
    }

    /// <summary>
    /// b6 — a "pp" under one staff was printed over the "arco" written <c>.up</c> on the staff
    /// beneath: the same unreserved words as b4, seen from the staff above.
    /// </summary>
    [Fact]
    public void ADynamicUnderAStaff_StandsClearOfTheWordsOverTheStaffBeneath()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part a { instrument violin }
            part b { instrument viola }
            section S {
              a { bes'2@pp~ bes'4 a' | }
              b { clef treble
                  d''2@text("arco").up@pp@flageolet a'2@flageolet | }
            }
            form { ~S }
            score { staffGroup { staff a staff b } }
            """);
        var level = g.Texts.Where(t => t.Text == "pp").OrderBy(t => t.Y).First();
        var words = g.Texts.Single(t => t.Text == "arco");
        Assert.True(GlyphMetrics.TryGetDynamicInk("pp", out double inkBottom, out _));
        double levelBottom = level.Y - inkBottom;
        Assert.True(levelBottom > level.Y, "the p's descender hangs under its baseline");
        // The words' ink top, over-read (0.8 em is above the ascenders of any text face).
        double wordsTop = words.Y - 0.8 * words.FontSize;
        Assert.True(wordsTop > levelBottom,
            $"the upper staff's pp reaches down to {levelBottom:F3} and \"arco\" up to {wordsTop:F3}: "
            + "the staves have to stand far enough apart for both.");
    }

    /// <summary>
    /// b7 — a cantata's "D.C. al Fine" under the system was printed through the 6/4 under the
    /// continuo's last note (<c>MusicMarkEngraver.Calculate</c>'s below-ink arm now reads the
    /// placed figures, as it reads the dynamics and the pedal brackets).
    /// </summary>
    [Fact]
    public void AJumpTextUnderTheSystem_StandsClearOfTheFiguresOverIt()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            time 4/4
            key d major
            part v { instrument violin }
            part bc { instrument cello }
            section A { v { d''1 | a'1 | } bc { d2@figuredBass(6) e2 | a,1@figuredBass(6 4) | } }
            section B { v { fis''1 | e''1 | } bc { d1 | a,1@figuredBass(6 4) | } }
            form { ~A fine ~B dc al fine }
            score { staff v staff bc }
            """);
        var jump = g.Texts.Single(t => t.Text == "D.C. al Fine");
        // The lowest figure, the 4 of a 6/4: digits stand on their baseline.
        double figureBottom = g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.FigBassDigit4).Max(x => x.Y);
        // The words' ink top, over-read (0.8 em is above the capitals of any text face).
        double jumpTop = jump.Y - 0.8 * jump.FontSize;
        Assert.True(jumpTop > figureBottom,
            $"\"D.C. al Fine\" reaches up to {jumpTop:F3} and the 4 of the 6/4 stands on {figureBottom:F3}: "
            + "the text has to stand under the figures, not in them.");
    }

    /// <summary>
    /// b5 — a bend's "½" was drawn inside the grid of the chord diagram a chords row hangs over
    /// the staff: the bend-up's arrow and amount were in no skyline the row is spaced off
    /// (<c>MultiStaffLayouter.StaffArticulationLayouts</c> left the bend out of the room).
    /// </summary>
    [Fact]
    public void ABendsAmount_StandsUnderTheChordDiagramOverIt()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part g { instrument guitar }
            section A { chords h { Bm(x24432) | Bm(x24432) | } g { b4 d' fis' b' | a'4@bend(half) a' fis' cis' | } }
            form { ~A }
            score { chords h staff g }
            """);
        var amount = g.Texts.Single(t => t.Role == TextRole.Bend);
        double topStaffLine = g.Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && Math.Abs(l.X2 - l.X1) > 10.0)
            .Min(l => l.Y1);
        // The diagram's strings over the bend: the upright rules that start well above the
        // staff (a stem of these notes starts within two spaces of it).
        var strings = g.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9 && Math.Min(l.Y1, l.Y2) < topStaffLine - 3.0
                && Math.Abs(l.X1 - amount.X) < 4.0)
            .ToList();
        Assert.NotEmpty(strings);
        double gridBottom = strings.Max(l => Math.Max(l.Y1, l.Y2));
        // The amount's ink top, over-read (0.8 em is above any digit of the face).
        double amountTop = amount.Y - 0.8 * amount.FontSize;
        Assert.True(amountTop > gridBottom,
            $"the bend's amount reaches up to {amountTop:F3} and the diagram's grid down to {gridBottom:F3}: "
            + "the diagram has to stand over the bend, not on it.");
    }

    /// <summary>
    /// fixC b5b — a text spanner's label ("CVII") stood 0.05 INTO the chord row over its staff
    /// when the span ended on its highest note: the band reserved for it read the staff's ink
    /// up to that note's left edge, the drawn line ends over its centre
    /// (<c>TextSpannerEngraver.InkAboveStaff</c>). A span ending on a low note cleared the row.
    /// </summary>
    [Theory]
    [InlineData("b'")]
    [InlineData("d'")]
    public void ATextSpannersLabel_StandsUnderTheChordRowOverIt(string last)
    {
        var g = RenderedGeometry.Render("octave absolute\npart g { instrument guitar }\n"
            + "section A { chords h { Bm | D | } g { b4@textSpan(\"CVII\") d' fis' " + last + "@!textSpan | a'4 a' fis' cis' | } }\n"
            + "form { ~A }\nscore { chords h staff g }\n");
        var label = g.Texts.Single(t => t.Text == "CVII");
        double rowBaseline = g.Texts.Where(t => t.Role == TextRole.ChordName).Max(t => t.Y);
        // The label's ink top, over-read (0.8 em is above the capitals of any text face).
        double top = label.Y - 0.8 * label.FontSize;
        Assert.True(top > rowBaseline,
            $"the label reaches up to {top:F3} and the chord row's names stand on {rowBaseline:F3}.");
    }

    /// <summary>
    /// fixD tab8 — on a tab that draws its rhythm, a quarter rest ran into the arpeggio line of
    /// the chord after it: the line stands where the notation frame puts it, the tab's rest half
    /// a head right of there, and nothing kept the difference
    /// (<c>SpacingRules.ApplyTabChordSpacing</c>). A digit before the line the same.
    /// </summary>
    [Theory]
    [InlineData("e'4 b' r <e a d' g'>4@arpeggio |", true)]
    [InlineData("e'4 b' <e' b'> <e a d' g'>4@arpeggio |", false)]
    public void ATabsArpeggioLine_StandsClearOfTheRestOrDigitsBeforeIt(string bar, bool rest)
    {
        var g = RenderedGeometry.Render("octave absolute\npart g { instrument guitar }\nsection A { g { " + bar
            + " } }\nform { ~A }\nscore { tab g }\n");
        double lineLeft = g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.Arpeggio).Min(x => x.X);
        double inkRight = rest
            ? g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.RestQuarter)
                .Max(x => x.X + GlyphMetrics.GetRestBBox(4).Right)
            // The digits of the chord before the line: every fret number left of it, over-read
            // by a whole em (a digit is narrower).
            : g.Texts.Where(t => t.Role == TextRole.TabFret && t.X < lineLeft).Max(t => t.X + 0.5 * t.FontSize);
        Assert.True(inkRight < lineLeft,
            $"the ink before the chord reaches right to {inkRight:F3} and its arpeggio line starts at {lineLeft:F3}.");
    }
}
