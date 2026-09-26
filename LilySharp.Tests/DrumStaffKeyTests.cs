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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A drum staff engraves no key signature: LilyPond's DrumStaff removes the Key_engraver
/// (ly/engraver-init.ly:297), as the TabStaff does (:1214). Until 2026-09-26 Lily# excluded
/// only the tab staff, so a keyed score printed its signature on the drum staff of every
/// system (user report, a big-band probe in F major).
/// </summary>
[Trait("Category", "Unit")]
public class DrumStaffKeyTests
{
    private static string Src(string key, string score, string drums = "hh8 hh hh hh sn4 bd |",
        string melody = "c4 d e f |") => $$"""
        key {{key}}
        part kit { clef percussion }
        part m { clef treble }
        section A { kit { {{drums}} } m { {{melody}} } }
        form main { ~A }
        score main { {{score}} }
        """;

    private static int Flats(string src)
        => SvgGenerator.Generate(SyntaxTree.Parse(src), new SvgRenderOptions { EmbedFont = false })
            .Count(ch => ch == EmmentalerGlyphs.AccidentalFlat);

    private static double PrefixWidth(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return new LayoutEngine().Layout(score).Systems[0].PrefixWidth;
    }

    [Fact]
    public void BesideAPitchedStaff_TheDrumStaffPrintsNoSignature()
    {
        // F major: one flat — the treble staff's, and nothing on the drum staff.
        Assert.Equal(1, Flats(Src("f major", "staff m  staff kit")));
        // Control: the same score without the drum staff prints the same one.
        Assert.Equal(1, Flats(Src("f major", "staff m")));
    }

    [Fact]
    public void ADrumOnlyScore_ReservesNoKeyColumn()
        => Assert.Equal(PrefixWidth(Src("c major", "staff kit")),
                        PrefixWidth(Src("e major", "staff kit")));

    private static string BarLines(string src)
    {
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(src), new SvgRenderOptions { EmbedFont = false });
        return string.Join(" ", System.Text.RegularExpressions.Regex.Matches(
            svg, "<rect x=\"([\\d.]+)\" y=\"[\\d.]+\" width=\"0.19\"").Select(m => m.Groups[1].Value));
    }

    [Theory]
    // a DrumStaff, F major to E major
    [InlineData("staff kit", true)]
    // a TabStaff (the tab of a pitched part): its Key_engraver is removed too (:1214)
    [InlineData("tab m", false)]
    public void AMidPieceKeyChange_TakesNoRoom_WhereNoStaffEngravesAKey(string score, bool drums)
    {
        // LilyPond 2.26 draws the bar lines of both twins at the same x (Lab sessions/p642
        // k0/k1, u0/u1): the column holds no KeySignature. Lily# booked the change's width
        // (KeySignatureChangeItem.Blanked, set by MeterStencil.Blank).
        string Book(string change) => drums
            ? Src("f major", score, drums: "hh8 hh hh hh sn4 bd | " + change + "hh8 hh hh hh sn4 bd |")
            // e and a: natural in both keys, so the frets do not move with the key.
            : Src("f major", score, melody: "e4 a e a | " + change + "e4 a e a |");
        Assert.Equal(BarLines(Book("")), BarLines(Book("key e major ")));
    }

    [Fact]
    public void BesideAPitchedStaff_AMidPieceKeyChange_StillTakesItsRoom()
    {
        // The column is the union of every staff: the treble staff's signature gives it width.
        string Book(string change) => Src("f major", "staff m  staff kit",
            drums: "hh8 hh hh hh sn4 bd | " + change + "hh8 hh hh hh sn4 bd |",
            melody: "c4 d e f | " + change + "c4 d e g |");
        Assert.NotEqual(BarLines(Book("")), BarLines(Book("key e major ")));
    }

    [Fact]
    public void AMidPieceKeyChange_IsNotDrawnOnTheDrumStaff()
    {
        // The change to B-flat major adds a second flat to the treble staff only.
        string src = Src("f major", "staff m  staff kit",
            drums: "hh8 hh hh hh sn4 bd | key bes major hh8 hh hh hh sn4 bd |",
            melody: "c4 d e f | key bes major bes4 a g f |");
        Assert.Equal(1 + 2, Flats(src));
    }
}
