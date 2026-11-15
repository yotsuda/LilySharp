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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The fingering of a voice that is not its staff's first (session 1265). Until then
/// <c>voice { … } { c''2@finger(2) }</c> drew no digit and said nothing: the fingering engraver
/// walked the staff's first voice only.
/// MEASURED (LilyPond 2.26.0, Lab sessions/p1265/fg g1…gd, p1263's dump.ily, raggedRight). A
/// Fingering's y there is its baseline about the staff's middle line — <see cref="FingeringLayout.YUp"/>
/// — and its x-extent's middle is <see cref="FingeringLayout.X"/>.
/// </summary>
[Trait("Category", "Unit")]
public class LaterVoiceFingeringTests
{
    private static ScoreLayout LayoutOf(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    /// <summary>A lone note's digit follows its voice: the second voice's goes BELOW, at the
    /// staff's clearance (LilyPond: ink top −2.55, baseline −3.6725, centred on 22.085).</summary>
    [Fact]
    public void TheSecondVoicesDigit_IsDrawnBelowItsNote()
    {
        var digit = Assert.Single(LayoutOf("voice { d''2 e''2 } { b'2 c''2@finger(2) } |").FingeringLayouts);
        Assert.Equal(1, digit.VoiceIndex);
        Assert.False(digit.IsAbove);
        Assert.Equal(-3.6725, digit.YUp, 0.005);
        Assert.Equal(22.085, digit.X, 0.005);
    }

    /// <summary>Below a note under the staff it clears the head (LilyPond: −5.181 under c',
    /// −6.1675 under a).</summary>
    [Fact]
    public void TheSecondVoicesDigit_ClearsALowHead()
    {
        var digits = LayoutOf("voice { d''2 e''2 } { c'4@finger(1) a@finger(2) c'2 } |").FingeringLayouts;
        Assert.Equal(2, digits.Length);
        Assert.Equal(-5.181, digits[0].YUp, 0.005);
        Assert.Equal(-6.1675, digits[1].YUp, 0.005);
    }

    /// <summary>A CHORD's digits do not follow the voice: they are split as in a single voice
    /// (LilyPond: the 1 at −3.686, the 3 at 2.55; a one-note chord's digit above).</summary>
    [Fact]
    public void TheSecondVoicesChord_SplitsItsDigitsAsASingleVoiceDoes()
    {
        var digits = LayoutOf("voice { e''2 f''2 } { g'2 <a'@finger(1) c''@finger(3)>2 } |").FingeringLayouts;
        Assert.Equal(2, digits.Length);
        Assert.Equal(-3.686, digits.Single(d => d.Number == 1).YUp, 0.005);
        Assert.Equal(2.55, digits.Single(d => d.Number == 3).YUp, 0.005);
    }

    /// <summary>A beamed stem is the digit's support, read off the digit's OWN voice's beam
    /// (LilyPond: −4.876 and −4.8625, the ink tops both at −3.74).</summary>
    [Fact]
    public void TheSecondVoicesDigit_ClearsItsOwnBeam()
    {
        var digits = LayoutOf("voice { e''2 f''2 } { b'8@finger(1) c''@finger(2) b' c'' b'2 } |").FingeringLayouts;
        Assert.Equal(2, digits.Length);
        Assert.Equal(-4.876, digits[0].YUp, 0.005);
        Assert.Equal(-4.8625, digits[1].YUp, 0.005);
    }

    /// <summary>The digit is centred on the head where the note collision put it (LilyPond:
    /// 19.187 and 24.151, the lower voice a second under the upper), and so is a third voice's,
    /// which goes above (18.4985, baseline 2.55).</summary>
    [Fact]
    public void AShiftedVoicesDigit_IsCentredOnTheShiftedHead()
    {
        var digits = LayoutOf("voice { c''2 d''2 } { b'2@finger(1) c''2@finger(2) } |").FingeringLayouts;
        Assert.Equal(2, digits.Length);
        Assert.Equal(19.187, digits[0].X, 0.005);
        Assert.Equal(24.151, digits[1].X, 0.005);

        var third = Assert.Single(LayoutOf("voice { e''2 f''2 } { g'2 a'2 } { c''2@finger(3) d''2 } |").FingeringLayouts);
        Assert.Equal(2, third.VoiceIndex);
        Assert.True(third.IsAbove);
        Assert.Equal(18.4985, third.X, 0.005);
        Assert.Equal(2.55, third.YUp, 0.005);
    }

    /// <summary>A digit below stands in its note's script column: under the accent (LilyPond:
    /// −5.9425) and under the staccato (−5.1645 — Lily#'s stem-side dot is centred on the head
    /// where LilyPond's is on the stem, which is worth 0.06 here and is an older difference).
    /// A fermata goes outside the digit, which stays at the staff's clearance.</summary>
    [Fact]
    public void ADigitBelow_StacksUnderItsNotesScripts()
    {
        var accent = LayoutOf("voice { d''2 e''2 } { b'2@accent@finger(1) c''2@fermata@finger(2) } |");
        Assert.Equal(-5.9425, accent.FingeringLayouts.Single(d => d.Number == 1).YUp, 0.01);
        Assert.Equal(-3.6725, accent.FingeringLayouts.Single(d => d.Number == 2).YUp, 0.005);
        var fermata = accent.ArticulationLayouts.Single(a => a.ItemIndex == 1);
        Assert.Equal(-4.2085, fermata.YUp, 0.01);

        var staccato = Assert.Single(LayoutOf("voice { d''2 e''2 } { b'2 c''2@staccato@finger(2) } |").FingeringLayouts);
        Assert.Equal(-5.1645, staccato.YUp, 0.07);
    }

    /// <summary>…and OVER the scripts that outrank it: the bow goes outside the digit, which
    /// keeps its own place (LilyPond: the 1 at −3.686 and the down-bow at −3.886; under a
    /// staccato the 2 at −5.2225 and the up-bow at −5.4225).</summary>
    [Fact]
    public void ADigitBelow_SupportsTheScriptsThatOutrankIt()
    {
        var layout = LayoutOf("voice { d''2 e''2 } { b'2@downBow@finger(1) c''2@upBow@staccato@finger(2) } |");
        Assert.Equal(-3.686, layout.FingeringLayouts.Single(d => d.Number == 1).YUp, 0.005);
        Assert.Equal(-3.886, Assert.Single(layout.ArticulationLayouts, a => a.ItemIndex == 0).YUp, 0.005);
        Assert.Equal(-5.2225, layout.FingeringLayouts.Single(d => d.Number == 2).YUp, 0.005);
        Assert.Equal(-5.4225, layout.ArticulationLayouts.Where(a => a.ItemIndex == 1).Min(a => a.YUp), 0.005);
    }

    /// <summary>A digit below is pushed off its own voice's bow where the bow crosses it, and
    /// stays where it does not (LilyPond: −4.8455, −3.6725, −4.9296, −3.6725).</summary>
    [Fact]
    public void ADigitBelow_AvoidsItsVoicesSlur()
    {
        var digits = LayoutOf("voice { e''2 f''2 } { b'4@finger(1)( c''@finger(2) d''@finger(3) c''@finger(2)) } |").FingeringLayouts;
        Assert.Equal(4, digits.Length);
        Assert.Equal(-4.8455, digits[0].YUp, 0.04);
        Assert.Equal(-3.6725, digits[1].YUp, 0.005);
        Assert.Equal(-4.9296, digits[2].YUp, 0.04);
        Assert.Equal(-3.6725, digits[3].YUp, 0.005);
    }

    /// <summary>The first voice's digits are where they were: above, on their own heads.</summary>
    [Fact]
    public void TheFirstVoicesDigits_DoNotMove()
    {
        var digits = LayoutOf("voice { d''2@finger(4) e''2 } { b'2 c''2 } |").FingeringLayouts;
        var digit = Assert.Single(digits);
        Assert.Equal(0, digit.VoiceIndex);
        Assert.True(digit.IsAbove);
        Assert.Equal(2.55, digit.YUp, 0.005);
        Assert.Equal(17.8095, digit.X, 0.005);
    }

    /// <summary>A tab beside a staff prints no digit of any voice (the staff prints them);
    /// until session 1265 only the first voice's were taken off the tab's copy.</summary>
    [Fact]
    public void ATabBesideAStaff_KeepsNoVoicesDigits()
    {
        static int Fingerings(string score)
        {
            var tree = SyntaxTree.Parse("octave absolute\npart a { instrument guitar }\n"
                + "section A { a { voice { e'2@finger(1) f'2 } { a2@finger(2) c'2@finger(3) } | } }\nform { ~A }\nscore { " + score + " }\n");
            Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
            return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)).StaffGroups.SelectMany(g => g.Staves).Where(s => s.IsTab)
                .SelectMany(s => s.Voices).SelectMany(v => v.Measures).SelectMany(m => m.Items)
                .Count(i => i is LilySharp.Core.Svg.Model.NoteItem { Fingering: not null });
        }
        Assert.Equal(0, Fingerings("staff a  tab a"));
        Assert.Equal(3, Fingerings("tab a"));
    }
}
