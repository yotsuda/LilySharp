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
