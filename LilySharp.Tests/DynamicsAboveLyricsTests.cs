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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The side a hairpin stands on (docs/dynamics-above-lyrics-design.md, D1 to D3): above on a
/// staff with a lyrics row directly under it, below elsewhere, and where it is written
/// (<c>@cresc.up</c> / <c>@cresc.down</c>) whatever the staff — the decision (step 2 of the
/// design's order), and where the line then stands (steps 3 and 4): above its staff, texts
/// and wedge on one line, placed by the above pass, reserved in the staff's UP profile.
/// </summary>
[Trait("Category", "Unit")]
public class DynamicsAboveLyricsTests
{
    /// <summary>Two parts, each one bar with a hairpin and whatever else <paramref name="sop"/>
    /// and <paramref name="alt"/> write, under the given score rows.</summary>
    private static List<HairpinItem> Hairpins(string sop, string alt, string scoreRows)
    {
        var src =
            "time 4/4\noctave absolute\n" +
            "part sop { clef treble }\npart alt { clef treble }\n" +
            $"section A {{\n  sop {{ {sop} }}\n  alt {{ {alt} }}\n}}\n" +
            "lyrics w { la la la la | }\n" +
            "form { A }\n" +
            $"score {{ {scoreRows} }}\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics)
            .OrderBy(h => h.StaffIndex).ThenBy(h => h.StartItemIndex).ToList();
    }

    private const string Plain = "c''4@cresc d'' e'' f''@f |";

    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    /// <summary>One staff <c>m</c> holding <paramref name="music"/>, with the lyrics row under
    /// it or without.</summary>
    private static string Book(string music, bool lyrics, string syllables = "la la la la |")
        => "time 4/4\noctave absolute\npart m { clef treble }\n" +
           $"section A {{ m {{ {music} }} }}\n" +
           $"lyrics w {{ {syllables} }}\n" +
           "form { A }\n" +
           (lyrics ? "score { staff m  lyrics w }\n" : "score { staff m }\n");

    private static ScoreLayout LayoutOf(string music, bool lyrics)
    {
        var tree = SyntaxTree.Parse(Book(music, lyrics));
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    /// <summary>
    /// Over lyrics the wedge and the level that ends it stand ABOVE the staff on one line —
    /// the wedge's centre the spanner's origin, the text's baseline 0.6 under it, as below the
    /// staff — and the same bar with no lyrics row keeps both below.
    /// MEASURED (LilyPond 2.26.0, \dynamicUp, Lab sessions/p1225/dyn: r, dyn, choir, brkup):
    /// the page's wedge centres and text baselines stand where LilyPond's do.
    /// </summary>
    [Fact]
    public void OverLyrics_TheWedgeAndItsLevel_StandAboveOnOneLine()
    {
        var sung = LayoutOf(Plain, lyrics: true);
        var hp = Assert.Single(sung.HairpinLayouts);
        var f = Assert.Single(sung.DynamicLayouts);
        Assert.True(hp.IsAbove && f.IsAbove);
        // System-relative Y-up: above the top line is positive.
        Assert.True(hp.YUp - hp.EndOpening > 0, $"the wedge must clear the top line: {hp.YUp:F6}");
        // One staff, so the text's frame (about the staff middle) is the wedge's plus half a staff.
        Assert.Equal(hp.YUp + EngravingDefaults.StaffMiddle - 0.6, f.YUp, 6);

        var played = LayoutOf(Plain, lyrics: false);
        var below = Assert.Single(played.HairpinLayouts);
        Assert.False(below.IsAbove);
        Assert.True(below.YUp + below.EndOpening < -2 * EngravingDefaults.StaffMiddle,
            $"with no lyrics the wedge hangs below the bottom line: {below.YUp:F6}");
        Assert.True(Assert.Single(played.DynamicLayouts).YUp < -EngravingDefaults.StaffMiddle);
    }

    [Fact]
    public void WrittenDown_OverLyrics_HangsBelow()
    {
        var layout = LayoutOf("c''4@cresc.down d'' e'' f''@f.down |", lyrics: true);
        var hp = Assert.Single(layout.HairpinLayouts);
        Assert.False(hp.IsAbove);
        Assert.True(hp.YUp < -2 * EngravingDefaults.StaffMiddle);
        Assert.True(Assert.Single(layout.DynamicLayouts).YUp < -EngravingDefaults.StaffMiddle);
    }

    /// <summary>
    /// D3 on the page: a level written <c>.down</c> hangs below alone; the wedge starting on its
    /// note and the level ending it keep the staff's side, above. The wedge still OPENS right of
    /// the level's ink — the bound is the text whichever side it was sent to.
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1225/dyn/lp/d3.ly): the wedge above opens at
    /// 28.504, 1.0 right of the ff below (HairpinEngraver.BoundText).
    /// </summary>
    [Fact]
    public void ALevelWrittenDown_GoesAlone_AndStillBoundsTheWedgeAbove()
    {
        var layout = LayoutOf("c''4@ff.down@decresc d'' e'' f''@p |", lyrics: true);
        var hp = Assert.Single(layout.HairpinLayouts);
        var ff = layout.DynamicLayouts.Single(d => d.Text == "ff");
        var p = layout.DynamicLayouts.Single(d => d.Text == "p");
        Assert.True(hp.IsAbove && p.IsAbove && !ff.IsAbove);
        Assert.True(ff.YUp < -EngravingDefaults.StaffMiddle && p.YUp > EngravingDefaults.StaffMiddle);
        Assert.True(hp.StartX > ff.X + 1.0, $"the wedge opens past the ff: {hp.StartX:F4} vs {ff.X:F4}");

        // The control: with the ff on the wedge's side the bound is the same text.
        var same = LayoutOf("c''4@ff@decresc d'' e'' f''@p |", lyrics: true);
        Assert.Equal(Assert.Single(same.HairpinLayouts).StartX, hp.StartX, 6);
    }

    /// <summary>
    /// Two voices' levels above one column: the one whose ink starts further left stands by the
    /// staff and the other close over it — the order and the distance are the outside-staff
    /// pass's, not a fixed step.
    /// MEASURED (LilyPond 2.26.0, test/above-dynamics' twin, Lab sessions/p1225/dyn/lp/abv):
    /// the lower voice's p at 17.615 and the upper voice's f at 15.380, 2.235 apart; until
    /// session 1225 the page stood the f by the staff and the p a fixed 2.0 over it.
    /// </summary>
    [Fact]
    public void TwoVoicesLevelsAboveOneColumn_StackInLeftEdgeOrder()
    {
        var layout = LayoutOf("voice { e''4@f.up f'' g'' a'' } { b'4@p.up c'' d'' e'' } |", lyrics: false);
        var f = layout.DynamicLayouts.Single(d => d.Text == "f");
        var p = layout.DynamicLayouts.Single(d => d.Text == "p");
        Assert.True(p.YUp < f.YUp, $"the p stands by the staff: p {p.YUp:F6}, f {f.YUp:F6}");
        Assert.InRange(f.YUp - p.YUp, 2.0, 2.5);
    }

    /// <summary>
    /// The lyrics row coming or going changes the side of the staff's dynamics though no bar
    /// of the staff changed: the preview must redraw them as the full render does (design §3.5).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheLyricsRowComingOrGoing_ThePreviewMovesTheLineAsTheFullRenderDoes(bool lyricsFirst)
    {
        const string music = "c''4@p@cresc d'' e'' f'' | g''2@f a''2@decresc | g''1@p |";
        string before = Book(music, lyricsFirst, "la la la la | la la | la |");
        string after = Book(music, !lyricsFirst, "la la la la | la la | la |");
        var session = new IncrementalCompiler(SyntaxTree.Parse(before), Opt);
        session.Render();
        var incremental = session.RenderIncremental(SyntaxTree.Parse(after)).Replace("\r\n", "\n");
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(after), Opt).Replace("\r\n", "\n"), incremental);
    }

    /// <summary>
    /// A keystroke in one bar replays the other systems' above-staff stacks from the memo
    /// (AboveStackMemo carries the wedges and the aligned lines since session 1225): the
    /// preview after the edit is the full render's page, over several systems of lines above.
    /// </summary>
    [Fact]
    public void AnEditElsewhere_ReplaysTheLinesAbove_AsTheFullRenderDrawsThem()
    {
        string bars = string.Concat(Enumerable.Repeat(
            "c''8@p@cresc d'' e'' f'' g'' a'' b'' c''' | c'''8@f@decresc b'' a'' g'' f'' e'' d'' c''@p | ", 8));
        string words = string.Concat(Enumerable.Repeat("la la la la la la la la | ", 16));
        string before = Book(bars, lyrics: true, words);
        int at = before.LastIndexOf("d'' c''@p", System.StringComparison.Ordinal);
        string after = before.Remove(at, 3).Insert(at, "e''");
        var session = new IncrementalCompiler(SyntaxTree.Parse(before), Opt);
        session.Render();
        var incremental = session.RenderIncremental(SyntaxTree.Parse(after)).Replace("\r\n", "\n");
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(after), Opt).Replace("\r\n", "\n"), incremental);
    }

    [Fact]
    public void AHairpinOverLyrics_StandsAbove_AndTheStaffWithoutLyricsKeepsItsBelow()
    {
        var hairpins = Hairpins(Plain, Plain, "staff sop  lyrics w  staff alt");
        Assert.Equal(2, hairpins.Count);
        Assert.True(hairpins[0].IsAbove);    // sop: the row is directly under it
        Assert.False(hairpins[1].IsAbove);   // alt: no lyrics under it
    }

    [Fact]
    public void WithNoLyricsInTheScore_EveryHairpinStandsBelow()
    {
        // The control: the same music, no row. Without it the test above passes as well on a
        // collector that says "above" for every staff.
        var hairpins = Hairpins(Plain, Plain, "staff sop  staff alt");
        Assert.Equal(2, hairpins.Count);
        Assert.All(hairpins, h => Assert.False(h.IsAbove));
    }

    [Fact]
    public void AWrittenSide_Wins_OnEitherKindOfStaff()
    {
        var hairpins = Hairpins(
            "c''4@cresc.down d'' e'' f''@f |",   // over lyrics, sent back below
            "c''4@decresc.up d'' e'' f''@p |",   // no lyrics, sent above
            "staff sop  lyrics w  staff alt");
        Assert.Equal(2, hairpins.Count);
        Assert.False(hairpins[0].IsAbove);
        Assert.True(hairpins[1].IsAbove);
    }

    [Fact]
    public void ARowFollowingAnotherPart_DoesNotMakeTheStaffAboveItAVocalStaff()
    {
        // D1: `lyrics w on alt` under the soprano's staff follows alt's rhythm and stands on a
        // band of its own; it is the lyrics of neither staff.
        var hairpins = Hairpins(Plain, Plain, "staff sop  lyrics w on alt  staff alt");
        Assert.Equal(2, hairpins.Count);
        Assert.All(hairpins, h => Assert.False(h.IsAbove));
    }

    [Fact]
    public void ADynamicsWrittenSide_DoesNotMoveTheHairpinBesideIt()
    {
        // D3: `@f.down` moves that f alone; the hairpin starting on the same note keeps the
        // staff's side.
        var hairpins = Hairpins(
            "c''4@f.down@decresc d'' e'' f''@p |", Plain, "staff sop  lyrics w  staff alt");
        Assert.True(hairpins[0].IsAbove);
    }

    [Fact]
    public void EachPieceOfTheChorus_TakesItsOwnStaffsSide()
    {
        // A choir staff: the rule is per staff, inside a group as outside one.
        var hairpins = Hairpins(Plain, Plain, "choirStaff { staff sop  lyrics w  staff alt }");
        Assert.Equal(2, hairpins.Count);
        Assert.True(hairpins[0].IsAbove);
        Assert.False(hairpins[1].IsAbove);
    }

    /// <summary>
    /// The room: a line above a sung staff is ink of that staff's UP profile, so the staff
    /// over it is spaced off the wedge and the level — the rule, not a value (what is asserted
    /// is that the distance RESPONDS, and that the drawn wedge then stands clear of the staff
    /// above). MEASURED (LilyPond 2.26.0, Lab sessions/p1225/dyn choir): four sung staves 11.62 /
    /// 12.08 / 13.40 apart on both pages; before the wedge entered the UP profile the page stood
    /// them 13.52 / 13.24 / 14.96 apart, the wedge reserved BELOW the staff it stands above.
    /// </summary>
    [Fact]
    public void TheStaffAbove_IsSpacedOffTheLineOverASungStaff()
    {
        static (double Gap, ScoreLayout Layout) Of(string low)
        {
            var tree = SyntaxTree.Parse(
                "time 4/4\noctave absolute\npart up { clef treble }\npart low { clef treble }\n" +
                $"section A {{\n  up {{ c'4 b a b | c'1 | }}\n  low {{ {low} }}\n}}\n" +
                "lyrics w { la la la la | la | }\nform { A }\n" +
                "score { staff up  staff low  lyrics w }\n");
            Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
            var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
            var staves = layout.Systems[0].StaffGroups.SelectMany(g => g.Staves).OrderBy(s => s.StaffIndex).ToList();
            return (staves[0].Y - staves[1].Y, layout);
        }
        var (plain, _) = Of("a''4 b'' c''' b'' | a''1 |");
        var (sung, layout) = Of("a''4@p@cresc b'' c''' b'' | a''1@f |");
        Assert.True(sung > plain + 0.5, $"the staves must open for the line: plain {plain:F6}, with it {sung:F6}");
        var hp = Assert.Single(layout.HairpinLayouts);
        Assert.True(hp.IsAbove);
        // System-relative Y-up: the upper staff's bottom line is 4 below the system top.
        Assert.True(hp.YUp + hp.EndOpening < -2 * EngravingDefaults.StaffMiddle,
            $"the wedge stands under the upper staff: centre {hp.YUp:F6}");
    }

    /// <summary>The twin's staff takes LilyPond's <c>\dynamicUp</c> exactly where the page
    /// stands the dynamics above: the staff with the lyrics row under it.</summary>
    [Fact]
    public void TheTwin_WritesDynamicUp_OnTheStaffOverLyricsOnly()
    {
        string sung = new LilyPondExporter().Export(SyntaxTree.Parse(Book(Plain, lyrics: true)));
        Assert.Contains("\\new Staff \\with { \\dynamicUp }", sung);
        Assert.DoesNotContain("dynamicUp",
            new LilyPondExporter().Export(SyntaxTree.Parse(Book(Plain, lyrics: false))));
    }

    /// <summary>MusicXML says the side the page draws: the staff's own (above over lyrics),
    /// or the written one; a wedge's stop stands where its start did.</summary>
    [Theory]
    [InlineData("c''4@cresc d'' e'' f''@f |", true, "above", "above")]
    [InlineData("c''4@cresc d'' e'' f''@f |", false, "below", "below")]
    [InlineData("c''4@cresc.down d'' e'' f''@f |", true, "below", "above")]
    [InlineData("c''4@cresc d'' e'' f''@f.up |", false, "below", "above")]
    public void MusicXml_WritesTheSideThePageDraws(string music, bool lyrics, string wedge, string level)
    {
        var part = new LilySharp.Core.MusicXml.MusicXmlExporter()
            .Export(SyntaxTree.Parse(Book(music, lyrics))).Parts.Single();
        var directions = part.Measures.SelectMany(m => m.Directions).ToList();
        Assert.All(directions.Where(d => d.WedgeType != null), d => Assert.Equal(wedge, d.Placement));
        Assert.Equal(2, directions.Count(d => d.WedgeType != null));
        Assert.Equal(level, directions.Single(d => d.DynamicType != null).Placement);
    }

    [Theory]
    [InlineData("@cresc.up", "^\\<")]
    [InlineData("@cresc.down", "_\\<")]
    [InlineData("@decresc.up", "^\\>")]
    [InlineData("@dim.down", "_\\>")]
    public void TheTwin_WritesAHairpinsWrittenSide(string mark, string expected)
    {
        var tree = SyntaxTree.Parse(
            "time 4/4\noctave absolute\npart m { clef treble }\n" +
            $"section A {{ m {{ c''4{mark} d'' e'' f''@mf | }} }}\n" +
            "form { A }\nscore { staff m }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        Assert.Contains(expected, new LilyPondExporter().Export(tree));
    }
}
