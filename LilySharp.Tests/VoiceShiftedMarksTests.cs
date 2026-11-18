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
/// The scripts, the text spanner and the trill of a voice the note collision shifted stand on
/// the SHIFTED column (session 1263, after the hairpin and the dynamic of session 1262). The
/// lower voice a second under the upper has its heads moved 1.377 right; its accent and
/// fermata were centred on the unshifted column — under the other voice's note — its "rit."
/// began and its dashed line ended there, and its trill began there.
/// MEASURED (LilyPond 2.26.0, Lab sessions/p1263/sp: s1/s0, t1/t0, r1/r0, dump.ily,
/// raggedRight). Each `1` book has the lower voice a second under the upper, each `0` book a
/// third (nothing shifted); the first column stands at 17.121 in both, so a first-column mark
/// moves by the shift alone, and the second column by the shift and the 0.689 the bar is wider.
/// </summary>
[Trait("Category", "Unit")]
public class VoiceShiftedMarksTests
{
    private static ScoreLayout LayoutOf(string bars)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A { m {\n" + bars + "\n} }\nform { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private const string Shifted = "voice { c''2 d''2 } { b'2{0} c''2{1} } |";
    private const string Control = "voice { d''2 e''2 } { b'2{0} c''2{1} } |";

    private static ScoreLayout Book(string pattern, string first, string second)
        => LayoutOf(pattern.Replace("{0}", first).Replace("{1}", second));

    /// <summary>LilyPond centres the accent at 19.187 (17.810 unshifted), the fermata on the
    /// same column, and moves the second column's staccato by 2.066 (23.527 against 21.461).</summary>
    [Fact]
    public void AShiftedVoicesScripts_AreCentredOnTheShiftedHead()
    {
        var shifted = Book(Shifted, "@accent@fermata.down", "@staccato").ArticulationLayouts;
        var control = Book(Control, "@accent@fermata.down", "@staccato").ArticulationLayouts;
        Assert.Equal(3, shifted.Length);
        Assert.Equal(3, control.Length);
        var first = shifted.Where(a => a.ItemIndex == 0).ToList();
        var firstControl = control.Where(a => a.ItemIndex == 0).ToList();
        for (int i = 0; i < 2; i++)
            Assert.Equal(1.377, first[i].X - firstControl[i].X, 0.005);
        Assert.Equal(2.066, shifted.Single(a => a.ItemIndex == 1).X - control.Single(a => a.ItemIndex == 1).X, 0.005);
    }

    /// <summary>LilyPond's TextSpanner runs 18.748..23.951 (17.371..21.885 unshifted). Its right
    /// end stands 0.05 outside Lily#'s on both books.</summary>
    [Fact]
    public void AShiftedVoicesTextSpanner_BeginsAndEndsOnTheShiftedColumns()
    {
        var span = Assert.Single(Book(Shifted, "@rit", "@!rit").TextSpannerLayouts);
        Assert.Equal(18.748, span.StartX, 0.005);
        Assert.Equal(23.951, span.EndX, 0.06);
        var control = Assert.Single(Book(Control, "@rit", "@!rit").TextSpannerLayouts);
        Assert.Equal(17.371, control.StartX, 0.005);
        Assert.Equal(21.885, control.EndX, 0.06);
    }

    /// <summary>LilyPond's TrillSpanner begins at 17.899 (16.522 unshifted).</summary>
    [Fact]
    public void AShiftedVoicesTrill_BeginsOnTheShiftedColumn()
    {
        var trill = Assert.Single(Book(Shifted, "@trillSpan.down", "@!trillSpan").TrillSpannerLayouts);
        var control = Assert.Single(Book(Control, "@trillSpan.down", "@!trillSpan").TrillSpannerLayouts);
        Assert.Equal(17.899 - 16.522, trill.GlyphX - control.GlyphX, 0.005);
        Assert.Equal(17.899 - 16.522, trill.LineStartX - control.LineStartX, 0.005);
    }

    /// <summary>The upper voice is not shifted: its marks stay where they were.</summary>
    [Fact]
    public void TheUnshiftedVoicesMarks_DoNotMove()
    {
        var shifted = LayoutOf("voice { c''2@accent@rit d''2@!rit } { b'2 c''2 } |");
        var alone = LayoutOf("c''2@accent@rit d''2@!rit |");
        Assert.Equal(Assert.Single(alone.ArticulationLayouts).X, Assert.Single(shifted.ArticulationLayouts).X, 6);
        Assert.Equal(Assert.Single(alone.TextSpannerLayouts).StartX, Assert.Single(shifted.TextSpannerLayouts).StartX, 6);
    }

    /// <summary>Where the voices CROSS, the first voice is the shifted one (it stands a second
    /// under the second): its digits are centred on its shifted heads, and the second voice's
    /// on its own, which did not move. MEASURED (LilyPond 2.26.0, Lab sessions/p1306/shift g1,
    /// g5): Fingering 18.502..19.321 and 23.311..24.165; g5 17.400..18.219 and
    /// 22.209..23.063.</summary>
    [Fact]
    public void TheFirstVoicesDigits_RideItsShift_WhereTheVoicesCross()
    {
        var digits = LayoutOf("voice { b'2@finger(1) c''2@finger(2) } { c''2 d''2 } |").FingeringLayouts;
        Assert.Equal(2, digits.Length);
        Assert.Equal(18.9115, digits.Single(d => d.Number == 1).X, 0.005);
        Assert.Equal(23.738, digits.Single(d => d.Number == 2).X, 0.005);

        var under = LayoutOf("voice { b'2 c''2 } { c''2@finger(1) d''2@finger(2) } |").FingeringLayouts;
        Assert.Equal(2, under.Length);
        Assert.Equal(17.8095, under.Single(d => d.Number == 1).X, 0.005);
        Assert.Equal(22.636, under.Single(d => d.Number == 2).X, 0.005);
    }

    private static double TextX(ScoreLayout layout, string text)
        => layout.DynamicLayouts.Single(t => t.IsExpressiveText && t.Text == text).X;

    /// <summary>A free text (it rides the dynamics' pipeline) stands on its note's head, where the collision put it. MEASURED
    /// (LilyPond 2.26.0, Lab sessions/p1306/shift): the lower voice's TextScript begins at
    /// 18.498 and 23.462 (t1) against 17.121 and 21.396 unshifted (t0); the first voice's, where
    /// the voices cross, at 18.223 and 23.049 (t2) against 17.121 and 21.396 (t3); and the
    /// unshifted upper voice's at 17.121 and 22.085 (t5) — the bar's 0.689 alone.</summary>
    [Fact]
    public void AShiftedVoicesText_BeginsAtItsShiftedHead()
    {
        var shifted = Book(Shifted, "@text(\"x\")", "@text(\"y\")");
        var control = Book(Control, "@text(\"x\")", "@text(\"y\")");
        Assert.Equal(2, shifted.DynamicLayouts.Length);
        Assert.Equal(1.377, TextX(shifted, "x") - TextX(control, "x"), 0.005);
        Assert.Equal(2.066, TextX(shifted, "y") - TextX(control, "y"), 0.005);

        var above = LayoutOf("voice { d''2@text(\"x\").up e''2@text(\"y\").up } { b'2 c''2 } |");
        var crossed = LayoutOf("voice { b'2@text(\"x\").up c''2@text(\"y\").up } { c''2 d''2 } |");
        Assert.Equal(1.102, TextX(crossed, "x") - TextX(above, "x"), 0.005);
        Assert.Equal(1.653, TextX(crossed, "y") - TextX(above, "y"), 0.005);

        var unshifted = LayoutOf("voice { c''2@text(\"x\").up d''2@text(\"y\").up } { b'2 c''2 } |");
        Assert.Equal(0.0, TextX(unshifted, "x") - TextX(above, "x"), 0.005);
        Assert.Equal(0.689, TextX(unshifted, "y") - TextX(above, "y"), 0.005);
    }

    /// <summary>A chord name is NOT its note's: it stands at the column the voices share,
    /// whichever head the collision moved — a row's symbol and a note's <c>@chord</c> alike.
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1306/shift): ChordName begins at 17.121 and
    /// 22.085 over the lower voice shifted (h1 the row, c1 the shifted voice's own @chord) and
    /// at 17.121 and 21.947 where the voices cross (h2, c2).</summary>
    [Theory]
    [InlineData("h1", "chords prog { C G7 | }\n", "voice { c''2 d''2 } { b'2 c''2 } |", 22.085)]
    [InlineData("h2", "chords prog { C G7 | }\n", "voice { b'2 c''2 } { c''2 d''2 } |", 21.947)]
    [InlineData("c1", "", "voice { c''2 d''2 } { b'2@chord(C) c''2@chord(G7) } |", 22.085)]
    [InlineData("c2", "", "voice { b'2@chord(C) c''2@chord(G7) } { c''2 d''2 } |", 21.947)]
    public void AChordName_StandsAtTheSharedColumn_NotAtAShiftedHead(string book, string row, string bars, double second)
    {
        var tree = SyntaxTree.Parse("octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble }\n"
            + "section A {\n" + row + "m {\n" + bars + "\n} }\nform { A }\nscore { "
            + (row.Length > 0 ? "chords prog " : "") + "staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var names = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree))).ChordNameLayouts;
        Assert.Equal(2, names.Length);
        Assert.True(System.Math.Abs(17.121 - names.Single(n => n.ChordText == "C").X) <= 0.005, $"{book} C: {names.Single(n => n.ChordText == "C").X:F3}");
        Assert.True(System.Math.Abs(second - names.Single(n => n.ChordText == "G7").X) <= 0.005, $"{book} G7: {names.Single(n => n.ChordText == "G7").X:F3}");
    }
}
