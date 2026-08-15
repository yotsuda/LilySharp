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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Integration")]
public class SvgTests
{
    private static string RenderSvg(string source) => LiveRender.Svg(source);

    [Fact]
    public void ExportSimpleNote()
    {
        var svg = RenderSvg("{ c4 }");

        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
        Assert.Contains("class=\"music\"", svg);
    }

    [Fact]
    public void ExportNoteWithAccidental()
    {
        var svg = RenderSvg("{ cis4 }");

        // Emmentaler accidentals.sharp
        Assert.Contains(EmmentalerGlyphs.AccidentalSharp.ToString(), svg);
    }

    [Fact]
    public void ExportRest()
    {
        var svg = RenderSvg("{ r4 }");

        // Emmentaler quarter rest (U+E008)
        Assert.Contains(EmmentalerGlyphs.RestQuarter.ToString(), svg);
    }

    [Fact]
    public void ExportWithClef()
    {
        var svg = RenderSvg("clef treble { c4 }");

        // Emmentaler G clef (U+E085)
        Assert.Contains(EmmentalerGlyphs.GClef.ToString(), svg);
    }

    [Fact]
    public void ExportWithTimeSignature()
    {
        // 3/4 is drawn with digit glyphs (Emmentaler time sig 3, U+E0B7)
        var svg = RenderSvg("time 3/4 { c4 }");
        Assert.Contains(EmmentalerGlyphs.TimeSig3.ToString(), svg);

        // 4/4 is drawn as the common-time C symbol (U+E091), as in LilyPond
        var svgCommon = RenderSvg("time 4/4 { c4 }");
        Assert.Contains(EmmentalerGlyphs.TimeSigCommon.ToString(), svgCommon);
    }

    [Fact]
    public void ExportChord()
    {
        var svg = RenderSvg("{ <c e g>4 }");

        // Emmentaler black notehead (U+E0EA)
        var noteheadCount = System.Text.RegularExpressions.Regex.Matches(svg, EmmentalerGlyphs.NoteheadBlack.ToString()).Count;
        Assert.True(noteheadCount >= 3);
    }

    [Fact]
    public void ExportBarline()
    {
        var svg = RenderSvg("{ c4 | d4 }");

        // Barline is now drawn as rect element
        Assert.Contains("<rect", svg);
    }

    [Fact]
    public void EmmentalerGlyphs_GetNotehead()
    {
        Assert.Equal(EmmentalerGlyphs.NoteheadWhole, EmmentalerGlyphs.GetNotehead(1)); // Whole
        Assert.Equal(EmmentalerGlyphs.NoteheadHalf, EmmentalerGlyphs.GetNotehead(2)); // Half
        Assert.Equal(EmmentalerGlyphs.NoteheadBlack, EmmentalerGlyphs.GetNotehead(4)); // Quarter
        Assert.Equal(EmmentalerGlyphs.NoteheadBlack, EmmentalerGlyphs.GetNotehead(8)); // Eighth
    }

    [Fact]
    public void EmmentalerGlyphs_GetRest()
    {
        // At the position each rest is drawn at when nothing moves it: a whole rest
        // hangs from +2 and everything else sits on the middle line — staff lines both.
        Assert.Equal(EmmentalerGlyphs.RestWhole, EmmentalerGlyphs.GetRest(1, 2));  // Whole
        Assert.Equal(EmmentalerGlyphs.RestHalf, EmmentalerGlyphs.GetRest(2, 0));  // Half
        Assert.Equal(EmmentalerGlyphs.RestQuarter, EmmentalerGlyphs.GetRest(4, 0));  // Quarter
        Assert.Equal(EmmentalerGlyphs.Rest8th, EmmentalerGlyphs.GetRest(8, 0));  // Eighth
        Assert.Equal(EmmentalerGlyphs.Rest16th, EmmentalerGlyphs.GetRest(16, 0)); // 16th
    }

    /// <summary>
    /// A breve, whole or half rest that does not land on a staff line prints the cut of
    /// its glyph that carries a ledger line; shorter rests have no such cut at all.
    /// LILYPOND-REF: lily/rest.cc:166-185 Rest::glyph_name;
    /// LILYPOND-REF: lily/staff-symbol.cc:372-396 Staff_symbol::on_line.
    /// </summary>
    /// <remarks>
    /// The rule is asserted by MOVING the rest, not by pinning one position: every even
    /// position from −4 to 4 is a staff line and prints the bare glyph, and everything
    /// else prints the ledgered one. An implementation that answers with the bare glyph
    /// everywhere — the one this replaced — passes the case above and fails here.
    /// </remarks>
    [Theory]
    // Half rests (note value 2): on the five lines, then off them.
    [InlineData(2, -4, false)] [InlineData(2, -2, false)] [InlineData(2, 0, false)]
    [InlineData(2, 2, false)] [InlineData(2, 4, false)]
    [InlineData(2, -11, true)]  // rest-avoid-note.ly's lower voice, out under the staff
    [InlineData(2, -6, true)] [InlineData(2, 6, true)] [InlineData(2, 1, true)]
    // Whole rests (1): the line they hang from decides.
    [InlineData(1, 2, false)] [InlineData(1, -4, false)] [InlineData(1, 5, true)]
    [InlineData(1, -6, true)]
    // Breves (0) are spared by EITHER their own line or the one two positions up.
    [InlineData(0, 0, false)] [InlineData(0, -6, false)] [InlineData(0, -8, true)]
    [InlineData(0, 7, true)]
    // Quarter and shorter have no ledgered cut, wherever they land.
    [InlineData(4, -11, false)] [InlineData(8, 7, false)] [InlineData(16, -9, false)]
    public void EmmentalerGlyphs_GetRest_LedgersARestThatMissesEveryStaffLine(
        int noteValue, int staffPosition, bool ledgered)
    {
        char bare = noteValue switch
        {
            0 => EmmentalerGlyphs.RestDoubleWhole,
            1 => EmmentalerGlyphs.RestWhole,
            2 => EmmentalerGlyphs.RestHalf,
            4 => EmmentalerGlyphs.RestQuarter,
            8 => EmmentalerGlyphs.Rest8th,
            _ => EmmentalerGlyphs.Rest16th,
        };
        char expected = ledgered
            ? noteValue switch
            {
                0 => EmmentalerGlyphs.RestDoubleWholeLedgered,
                1 => EmmentalerGlyphs.RestWholeLedgered,
                _ => EmmentalerGlyphs.RestHalfLedgered,
            }
            : bare;
        Assert.Equal(expected, EmmentalerGlyphs.GetRest(noteValue, staffPosition));
    }

    [Fact]
    public void EmmentalerGlyphs_GetFlag()
    {
        Assert.Equal(EmmentalerGlyphs.Flag8thUp, EmmentalerGlyphs.GetFlag(8, true));   // 8th up
        Assert.Equal(EmmentalerGlyphs.Flag8thDown, EmmentalerGlyphs.GetFlag(8, false));  // 8th down
        Assert.Equal(EmmentalerGlyphs.Flag16thUp, EmmentalerGlyphs.GetFlag(16, true));  // 16th up
        Assert.Null(EmmentalerGlyphs.GetFlag(4, true));              // No flag for quarter
    }

    [Fact]
    public void ExportRepeatBarlines()
    {
        var source = @"
section A {
    melody { c4 d4 e4 f4 | }
}
form main {
    |: A :|
}
";
        var svg = LiveRender.Svg(source, "melody");

        // Repeat barlines drawn as shapes: circles for dots, rects for bars
        Assert.Contains("<circle", svg);
        Assert.Contains("<rect", svg);
    }


    [Fact]
    public void AccidentalCollisionTest_SpringLayout()
    {
        // Test that accidentals don't overlap with previous notes
        var source = @"{ c4 cis4 d4 dis4 | }";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree, null);
        var layoutEngine = new LayoutEngine();
        var layout = layoutEngine.Layout(score);

        var measure = layout.Systems[0].Measures[0];
        Console.WriteLine("Accidental collision test:");
        Console.WriteLine($"Measure width: {measure.Width:F1}");

        for (int i = 0; i < measure.Items.Length; i++)
        {
            var item = measure.Items[i];
            var musicItem = score.Voice.Measures[0].Items[i];
            // BOTH sides in the column's frame. This used to pair CalculateLeftExtent
            // (left-edge basis) with CalculateRightExtent (centre basis), so it measured one
            // box against two different origins and under-reported every right edge by half
            // a note head — a collision test that cannot see half the collisions.
            // CalculateRightExtent had no other caller and is gone.
            var leftExtent = SpacingRules.CalculateLeftExtent(musicItem);
            var rightExtent = SpacingRules.CalculateNoteheadRightExtent(musicItem);

            string accidental = musicItem switch
            {
                NoteItem note => note.Accidental ?? "none",
                _ => "n/a"
            };

            double leftEdge = item.X - leftExtent;
            double rightEdge = item.X + rightExtent;

            Console.WriteLine($"  Item {i}: X={item.X:F1}, W={item.Width:F1}, Acc={accidental}, LeftExt={leftExtent:F1}, RightExt={rightExtent:F1}");
            Console.WriteLine($"          LeftEdge={leftEdge:F1}, RightEdge={rightEdge:F1}");

            // Check for collision with previous item
            if (i > 0)
            {
                var prevItem = measure.Items[i - 1];
                var prevMusicItem = score.Voice.Measures[0].Items[i - 1];
                var prevRightExtent = SpacingRules.CalculateNoteheadRightExtent(prevMusicItem);
                double prevRightEdge = prevItem.X + prevRightExtent;
                double gap = leftEdge - prevRightEdge;
                Console.WriteLine($"          Gap from prev: {gap:F1}");
                Assert.True(gap >= 0, $"Item {i} overlaps with item {i-1}! Gap={gap:F1}");
            }
        }
    }
}
