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

        // Emmentaler sharp accidental (U+E013)
        Assert.Contains("\uE013", svg);
    }

    [Fact]
    public void ExportRest()
    {
        var svg = RenderSvg("{ r4 }");

        // Emmentaler quarter rest (U+E008)
        Assert.Contains("\uE008", svg);
    }

    [Fact]
    public void ExportWithClef()
    {
        var svg = RenderSvg("clef treble { c4 }");

        // Emmentaler G clef (U+E085)
        Assert.Contains("\uE085", svg);
    }

    [Fact]
    public void ExportWithTimeSignature()
    {
        // 3/4 is drawn with digit glyphs (Emmentaler time sig 3, U+E0B7)
        var svg = RenderSvg("time 3/4 { c4 }");
        Assert.Contains("\uE0B7", svg);

        // 4/4 is drawn as the common-time C symbol (U+E091), as in LilyPond
        var svgCommon = RenderSvg("time 4/4 { c4 }");
        Assert.Contains("\uE091", svgCommon);
    }

    [Fact]
    public void ExportChord()
    {
        var svg = RenderSvg("{ <c e g>4 }");

        // Emmentaler black notehead (U+E0EA)
        var noteheadCount = System.Text.RegularExpressions.Regex.Matches(svg, "\uE0EA").Count;
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
        Assert.Equal('\uE0E8', EmmentalerGlyphs.GetNotehead(1)); // Whole
        Assert.Equal('\uE0E9', EmmentalerGlyphs.GetNotehead(2)); // Half
        Assert.Equal('\uE0EA', EmmentalerGlyphs.GetNotehead(4)); // Quarter
        Assert.Equal('\uE0EA', EmmentalerGlyphs.GetNotehead(8)); // Eighth
    }

    [Fact]
    public void EmmentalerGlyphs_GetRest()
    {
        Assert.Equal('\uE000', EmmentalerGlyphs.GetRest(1));  // Whole
        Assert.Equal('\uE001', EmmentalerGlyphs.GetRest(2));  // Half
        Assert.Equal('\uE008', EmmentalerGlyphs.GetRest(4));  // Quarter
        Assert.Equal('\uE00B', EmmentalerGlyphs.GetRest(8));  // Eighth
        Assert.Equal('\uE00C', EmmentalerGlyphs.GetRest(16)); // 16th
    }

    [Fact]
    public void EmmentalerGlyphs_GetFlag()
    {
        Assert.Equal('\uE0D2', EmmentalerGlyphs.GetFlag(8, true));   // 8th up
        Assert.Equal('\uE0DA', EmmentalerGlyphs.GetFlag(8, false));  // 8th down
        Assert.Equal('\uE0D3', EmmentalerGlyphs.GetFlag(16, true));  // 16th up
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
