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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@parenthesize</c> — a head in parentheses, LilyPond's <c>\parenthesize</c>; a drummer's
/// ghost note above all (owner's request 2026-10-11).
/// </summary>
[Trait("Category", "Unit")]
public class ParenthesizeTests
{
    private static string Book(string music, string clef = "treble") => $$"""
        part p { clef {{clef}} }
        section A { p { {{music}} } }
        form { ~A }
        score { staff p }
        """;

    private static MusicItem[] Items(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity != DiagnosticSeverity.Info));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        return [.. score.EnumerateStaves().Single().Staff.Voices[0].Measures[0].Items];
    }

    [Fact]
    public void ANote_AndADrumNote_CarryTheirParentheses()
    {
        var notes = Items(Book("c'4 d@parenthesize e f |")).OfType<NoteItem>().ToList();
        Assert.Equal(new[] { false, true, false, false }, notes.Select(n => n.Parenthesized));
        var drums = Items(Book("sn16 sn@parenthesize 16 16 r4 r2 |", "percussion")).OfType<NoteItem>().ToList();
        Assert.Equal(new[] { false, true, false, false }, drums.Select(n => n.Parenthesized));
    }

    [Fact]
    public void AChordMember_HasItsOwnPair_AndAWholeChordOnePair()
    {
        var chords = Items(Book("<c' e@parenthesize g>2 <c e g>2@parenthesize |")).OfType<ChordItem>().ToList();
        Assert.Equal(new[] { false, true, false }, chords[0].Notes.Select(n => n.Parenthesized));
        Assert.False(chords[0].Parenthesized);
        // LilyPond's \parenthesize <c e g> gives the heads ONE parenthesis-id: one pair.
        Assert.True(chords[1].Parenthesized);
        Assert.All(chords[1].Notes, n => Assert.False(n.Parenthesized));
    }

    private static string[] ParenGlyphs(string svg) =>
        [.. System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\"[^>]* x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"2.00\"[^>]*>(.)<")
            .Select(m => m.Groups[1].Value)];

    [Fact]
    public void TheParentheses_StandWhereLilyPondPutsThem_AndMoveNothing()
    {
        // MEASURED on LilyPond 2.26.0 (Lab sessions/p902/paren/C.ly): c''8 \parenthesize d''
        // e'' f'' g''4 \parenthesize a''4 — the d'' head at 19.6250 with its pair at 19.4250
        // and 21.1292, the a'' at 30.8418 with 30.6418 and 32.3460; every other grob where
        // D.ly (no \parenthesize) has it.
        string plain = SvgGenerator.Generate(SyntaxTree.Parse(Book("c'8 d e f g4 a |")));
        string marked = SvgGenerator.Generate(SyntaxTree.Parse(Book("c'8 d@parenthesize e f g4 a@parenthesize |")));
        Assert.Equal(new[] { "19.43", "21.13", "30.64", "32.35" }, ParenGlyphs(marked));
        Assert.Empty(ParenGlyphs(plain));
        // (The marked page also declares the size −6 face, Emmentaler-11, in its style block.)
        static string Others(string svg) => System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(svg, "<style[\\s\\S]*?</style>", ""),
                "<text class=\"music\"[^>]*font-size=\"2.00\"[^>]*>.</text>", ""),
            " data-pos=\"\\d+\"", "");
        Assert.Equal(System.Text.RegularExpressions.Regex.Replace(Others(plain), "\\s+", " "),
            System.Text.RegularExpressions.Regex.Replace(Others(marked), "\\s+", " "));
    }

    [Fact]
    public void AChord_AMember_AndADottedNoteWithItsAccidental_AsLilyPondDrawsThem()
    {
        // MEASURED on LilyPond 2.26.0 (Lab sessions/p902/paren/E.ly): the chord's one pair
        // 0.2 outside its heads (16.9208 / 18.6250); the member's 20.6250 / 22.3292; fis''4.'s
        // from 0.2 left of its sharp to 0.2 right of its dot (0.65 past the dot's origin).
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(Book("<c'' e g>4@parenthesize <c e@parenthesize g>4 fis4.@parenthesize r8 |")));
        var x = ParenGlyphs(svg).Select(double.Parse).ToArray();
        Assert.Equal(6, x.Length);
        Assert.Equal(18.63 - 16.92, x[1] - x[0], 2);
        Assert.Equal(22.33 - 20.63, x[3] - x[2], 2);
        Assert.Equal(27.3091 - 23.2549, x[5] - x[4], 2);
    }

    [Fact]
    public void TheTwins_SayItLilyPondsWay_AndMusicXmlsWay()
    {
        var tree = SyntaxTree.Parse(Book("<c' e@parenthesize g>2 d4@parenthesize <c e g>4@parenthesize |"));
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("\\parenthesize e", ly);
        Assert.Contains("\\parenthesize d", ly);
        Assert.Contains("\\parenthesize <", ly);
        Assert.DoesNotContain("-\\parenthesize", ly);
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(tree).ToXml().ToString();
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(xml, "<notehead parentheses=\"yes\">normal</notehead>").Count);
    }

    [Fact]
    public void AGhostNote_InAChart_IsItsOwnHeadsPair()
    {
        var tree = SyntaxTree.Parse(Book("drums { { hh16 16 16 16 16 16 16 16 16 16 16 16 16 16 16 16 | }  { r4 sn r8. sn16@parenthesize sn4 | } }", "percussion"));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        var items = score.EnumerateStaves().Single().Staff.Voices[0].Measures[0].Items;
        var ghost = Assert.IsType<ChordItem>(items[11]);
        Assert.False(ghost.Parenthesized);
        Assert.Single(ghost.Notes, n => n.Parenthesized);   // the snare, not the hi-hat
    }

    [Fact]
    public void ADrumChordMember_TakesItsOwnMarks()
    {
        // Until 2026-10-11 `<hh sn@parenthesize>` was "Expected 'CloseAngle'": a drum member
        // took no annotation. Now its parentheses and its scripts are its own.
        var kit = Items(Book("<hh sn@parenthesize>4 <hh sn@accent>4 r2 |", "percussion")).OfType<ChordItem>().ToList();
        Assert.Equal(1, kit[0].Notes.Count(n => n.Parenthesized));
        Assert.True(kit[0].Notes.Single(n => n.Parenthesized).StaffPosition == 1);   // the snare's line
        var tree = SyntaxTree.Parse(Book("<hh sn@parenthesize>4 <hh sn@accent>4 r2 |", "percussion"));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        Assert.Contains(score.Articulations, a => a.ItemIndex == 1 && a.Type == ArticulationType.Accent);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("<hh \\parenthesize sn>4", ly);
        Assert.Contains("<hh sn-\\accent>4", ly);
    }

    [Fact]
    public void AGhostMemberOfAChartLinesChord_IsItsOwnPair()
    {
        var tree = SyntaxTree.Parse(Book("drums { { <hh sn@parenthesize>4 hh hh hh | }  { bd4 r bd r | } }", "percussion"));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        var first = Assert.IsType<ChordItem>(score.EnumerateStaves().Single().Staff.Voices[0].Measures[0].Items[0]);
        Assert.Single(first.Notes, n => n.Parenthesized);
    }

}
