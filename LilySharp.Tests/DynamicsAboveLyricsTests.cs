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

    /// <summary>A sung staff under a chord-name row, two bars a system apart: the first ends
    /// with <paramref name="barOneEnd"/>, the second is <paramref name="barTwo"/>.</summary>
    private static ScoreLayout UnderChordNames(string barOneEnd, string barTwo)
    {
        string source = "time 4/4\noctave absolute\npart m { clef treble }\n" +
            "section A {\n  chords h { G | D7 | C | }\n" +
            $"  m {{ r8 b'8~ b'4 d''8 b' {barOneEnd} | break {barTwo} | g'4 a' c'' b' | }}\n" +
            "  lyrics w { la la la la | la la la la | la la la la | }\n}\n" +
            "form { A }\nscore { chords h  staff m  lyrics w }\n";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    /// <summary>
    /// A wedge carried over a line break stands in ITS OWN system: the piece after the break is
    /// placed from where the staff is there, not from where it was in the system before.
    /// Owner's report 2026-11-11 (a song, `g4@mf@cresc | a4 b4 c'4 b4@ff` broken after the first
    /// bar, under the chord name D7): the second piece was drawn through the chord name and
    /// into the lyrics of the system above, 4 staff spaces over the ff that ends it. The staff's
    /// depth in its system was read once, at the hairpin's start, and spent on every piece —
    /// and under a chord-name row that depth differs from system to system (here the high
    /// notes and the ff of the second bar push the row up).
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1226/cresc/min.ly): the piece after the break
    /// and the ff stand on one line, the chord name clear above them.
    /// </summary>
    [Fact]
    public void AWedgeBrokenUnderChordNames_StandsInItsOwnSystem_OnTheLineOfTheLevelEndingIt()
    {
        const string Second = "a'4 b' c''' b''@ff";
        var broken = UnderChordNames("g'4@mf@cresc", Second);
        Assert.Equal(2, broken.HairpinLayouts.Length);
        var after = broken.HairpinLayouts.OrderBy(h => h.StartMeasureIndex).Last();
        var ff = broken.DynamicLayouts.Single(d => d.Text == "ff");
        Assert.True(after.IsAbove && ff.IsAbove);

        // The same second bar with the wedge BEGINNING in it: one piece, in one system — where
        // the piece after the break must stand too. (A crescendo is tallest at its right end
        // either way; what differs is its left tip, closed here and open after a break, which
        // may move the chord-name row a hair.)
        var whole = UnderChordNames("g'4@mf", Second.Replace("a'4 ", "a'4@cresc "));
        var one = Assert.Single(whole.HairpinLayouts);
        var ffWhole = whole.DynamicLayouts.Single(d => d.Text == "ff");
        // The fault was 3.7 staff spaces: the piece stood that far over its level's line.
        Assert.InRange(after.YUp - one.YUp, -0.4, 0.4);
        // …and on the line of its level, as far from it as in the unbroken bar. (The wedge's
        // frame is the system's and the text's is the staff's, so the open tip's hair is in
        // this difference too: 0.28 here, the row's own move.)
        Assert.InRange((after.YUp - ff.YUp) - (one.YUp - ffWhole.YUp), -0.4, 0.4);
    }

    /// <summary>
    /// WHERE the line stands, pinned to LilyPond's own numbers — the side alone is not enough
    /// of a net, since a line sided the wrong way is pushed back above the staff by the
    /// outside-staff pass and lands near, not on, its place (padding 0.46 where the side
    /// position spends 0.6).
    /// MEASURED (LilyPond 2.26.0, \dynamicUp, Lab sessions/p1225/dyn/lp/r.svg — the design's
    /// own bar, §6): staff middle 12.2846, the crescendo's centre 8.2608, the ff ending it
    /// 8.8608 (0.6 under the centre), the next bar's lone ff 8.9426.
    /// </summary>
    [Fact]
    public void TheDesignsBar_StandsWhereLilyPondsDynamicUpStandsIt()
    {
        var tree = SyntaxTree.Parse(
            "time 4/4\noctave absolute\npart m { clef treble }\n" +
            "section A {\n  m { b'8@cresc cis'' d'' e'' d'' a'4. | d''8 b' b'4 d''4@ff~ d''4 | g'4@ff a' b' r4 | }\n}\n" +
            "lyrics w { ず ん だ も ん と | ろ し く ー | な の よ | }\nform { A }\nscore { staff m  lyrics w }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        var hp = Assert.Single(layout.HairpinLayouts);
        var levels = layout.DynamicLayouts.OrderBy(d => d.X).ToList();
        Assert.Equal(2, levels.Count);
        // The wedge's frame is the system top, half a staff over the middle the texts read.
        Assert.Equal(12.2846 - 8.2608, hp.YUp + EngravingDefaults.StaffMiddle, 3);
        Assert.Equal(12.2846 - 8.8608, levels[0].YUp, 3);
        Assert.Equal(12.2846 - 8.9426, levels[1].YUp, 3);
    }

    /// <summary>
    /// A wedge alone above a quiet staff: the staff's top line, the side position's padding,
    /// the wedge's own half height — the mirror of the 3.3666 it hangs at below.
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1225/dyn/lp/d3.svg, bar 2: <c>c''4\&lt; … f''_\ff</c>
    /// under \dynamicUp): staff middle 12.2846, the wedge's centre 8.9180.
    /// </summary>
    [Fact]
    public void AWedgeAloneAboveAQuietStaff_StandsAtTheSidePositionsOwnHeight()
    {
        var layout = LayoutOf("c''4@cresc d'' e'' f''@ff.down |", lyrics: true);
        var hp = Assert.Single(layout.HairpinLayouts);
        Assert.True(hp.IsAbove);
        Assert.Equal(12.2846 - 8.9180, hp.YUp + EngravingDefaults.StaffMiddle, 3);
    }

    /// <summary>
    /// Four sung staves, each with a crescendo, a level and a decrescendo above it: the
    /// distances between the staves are the room's reading of those lines.
    /// MEASURED (LilyPond 2.26.0, \dynamicUp on each staff, Lab sessions/p1225/dyn/lp/choir.svg):
    /// top lines at 11.9229, 23.5426, 35.6181 and 49.0212.
    /// </summary>
    [Fact]
    public void FourSungStaves_StandAsFarApartAsLilyPondsDo()
    {
        var tree = SyntaxTree.Parse(
            "time 4/4\noctave absolute\n" +
            "part sop { clef treble }\npart alt { clef treble }\npart ten { clef treble }\npart bas { clef bass }\n" +
            "section A {\n" +
            "  sop { e''4@p@cresc f'' g'' a'' | b''2@f a''2@decresc | g''1@p | }\n" +
            "  alt { c''4@p@cresc c'' d'' e'' | f''2@f e''2@decresc | e''1@p | }\n" +
            "  ten { g'4@p@cresc a' b' c'' | d''2@f c''2@decresc | c''1@p | }\n" +
            "  bas { c4@p@cresc f g a | b2@f a2@decresc | c1@p | }\n}\n" +
            "lyrics w { la la la la | la la | la | }\nform { A }\n" +
            "score { choirStaff { staff sop lyrics w  staff alt lyrics w  staff ten lyrics w  staff bas lyrics w } }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        var tops = layout.Systems[0].StaffGroups.SelectMany(g => g.Staves)
            .OrderBy(s => s.StaffIndex).Select(s => -s.Y).ToList();
        Assert.Equal(4, tops.Count);
        Assert.Equal(23.5426 - 11.9229, tops[1] - tops[0], 2);
        Assert.Equal(35.6181 - 23.5426, tops[2] - tops[1], 2);
        Assert.Equal(49.0212 - 35.6181, tops[3] - tops[2], 2);
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
    /// A written side gives its LINE a direction, as LilyPond's does: the level written
    /// <c>.down</c> breaks off the staff's line (which the hairpin of its note has joined — a
    /// moment's hairpin is read before its text), and the level closing the hairpin, writing no
    /// side, follows that line below and stands on the first one's baseline. The wedge above
    /// still OPENS right of the level's ink — the bound is the text whichever side it is on.
    /// MEASURED (LilyPond 2.26.0, \dynamicUp, Lab sessions/p1225/dyn/lp/d3.svg): the wedge above
    /// opens at 28.504, 1.0 right of the ff; the ff and the closing p share the baseline 17.2776
    /// below the staff (middle 12.2846).
    /// </summary>
    [Fact]
    public void ALevelWrittenDown_TakesItsLineBelow_AndTheWedgeOfItsNoteStaysAbove()
    {
        var layout = LayoutOf("c''4@ff.down@decresc d'' e'' f''@p |", lyrics: true);
        var hp = Assert.Single(layout.HairpinLayouts);
        var ff = layout.DynamicLayouts.Single(d => d.Text == "ff");
        var p = layout.DynamicLayouts.Single(d => d.Text == "p");
        Assert.True(hp.IsAbove && !ff.IsAbove && !p.IsAbove);
        Assert.Equal(12.2846 - 17.2776, ff.YUp, 3);
        Assert.Equal(ff.YUp, p.YUp, 6);
        Assert.True(hp.StartX > ff.X + 1.0, $"the wedge opens past the ff: {hp.StartX:F4} vs {ff.X:F4}");

        // The control: with the ff on the wedge's side the bound is the same text.
        var same = LayoutOf("c''4@ff@decresc d'' e'' f''@p |", lyrics: true);
        Assert.Equal(Assert.Single(same.HairpinLayouts).StartX, hp.StartX, 6);
    }

    /// <summary>
    /// A hairpin written <c>.down</c> gives the line its direction too: the level on its own
    /// note and the level closing it, writing none, stand below with it. And a level written
    /// against a running hairpin's line goes alone; the next line is the staff's again.
    /// MEASURED (same twin): <c>c''4\p_\&lt; … f''\f</c> — p, wedge and f all below, the wedge's
    /// centre 16.4073; <c>c''4\&lt; … f''_\ff</c> — the wedge above at 8.9180, the ff below.
    /// </summary>
    [Fact]
    public void AHairpinWrittenDown_TakesTheLevelsOfItsLineWithIt()
    {
        var layout = LayoutOf("c''4@p@cresc.down d'' e'' f''@f | c''4@cresc d'' e'' f''@ff.down | g'1@mf |", lyrics: true);
        var wedges = layout.HairpinLayouts.OrderBy(h => h.StartX).ToList();
        Assert.Equal(2, wedges.Count);
        Assert.False(wedges[0].IsAbove);
        Assert.Equal(12.2846 - 16.4073, wedges[0].YUp + EngravingDefaults.StaffMiddle, 3);
        Assert.False(layout.DynamicLayouts.Single(d => d.Text == "p").IsAbove);
        Assert.False(layout.DynamicLayouts.Single(d => d.Text == "f").IsAbove);
        Assert.True(wedges[1].IsAbove);
        Assert.False(layout.DynamicLayouts.Single(d => d.Text == "ff").IsAbove);
        // The line ended with its hairpin: the next level is on the staff's own side.
        Assert.True(layout.DynamicLayouts.Single(d => d.Text == "mf").IsAbove);
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
    /// A side written on one note moves a mark in ANOTHER bar — the level closing the hairpin
    /// follows the line — though that bar's own text did not change: the preview redraws it
    /// as the full render does, in both directions of the edit.
    /// </summary>
    [Theory]
    [InlineData("@ff@decresc", "@ff.down@decresc")]
    [InlineData("@ff.down@decresc", "@ff@decresc")]
    [InlineData("@ff@decresc", "@ff@decresc.down")]
    public void ASideWrittenOnOneNote_MovesTheLevelClosingItsHairpin_InThePreviewToo(string from, string to)
    {
        string Bars(string start) => $"c''4{start} d'' e'' f'' | g''2 a''2 | g''1@p | c''1@mf |";
        string before = Book(Bars(from), lyrics: true, "la la la la | la la | la | la |");
        string after = Book(Bars(to), lyrics: true, "la la la la | la la | la | la |");
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
        // `@f.down` breaks off into a line of its own; the hairpin starting on the same note keeps the
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

        // A wedge with no level on its line is reserved by its own outline alone.
        var (wedgeOnly, alone) = Of("a''4@cresc b'' c''' b''@!cresc | a''1 |");
        Assert.True(wedgeOnly > plain + 0.5,
            $"the staves must open for the wedge alone: plain {plain:F6}, with it {wedgeOnly:F6}");
        var lone = Assert.Single(alone.HairpinLayouts);
        Assert.True(lone.YUp + lone.EndOpening < -2 * EngravingDefaults.StaffMiddle,
            $"the lone wedge stands under the upper staff: centre {lone.YUp:F6}");
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
    [InlineData("c''4@cresc.down d'' e'' f''@f |", true, "below", "below")]   // the f follows its line
    [InlineData("c''4@cresc d'' e'' f''@f.up |", false, "below", "above")]    // the f breaks off
    [InlineData("c''4@ff.down@decresc d'' e'' f''@p |", true, "above", "below")]   // both levels follow the ff
    public void MusicXml_WritesTheSideThePageDraws(string music, bool lyrics, string wedge, string level)
    {
        var part = new LilySharp.Core.MusicXml.MusicXmlExporter()
            .Export(SyntaxTree.Parse(Book(music, lyrics))).Parts.Single();
        var directions = part.Measures.SelectMany(m => m.Directions).ToList();
        Assert.All(directions.Where(d => d.WedgeType != null), d => Assert.Equal(wedge, d.Placement));
        Assert.Equal(2, directions.Count(d => d.WedgeType != null));
        Assert.NotEmpty(directions.Where(d => d.DynamicType != null));
        Assert.All(directions.Where(d => d.DynamicType != null), d => Assert.Equal(level, d.Placement));
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
