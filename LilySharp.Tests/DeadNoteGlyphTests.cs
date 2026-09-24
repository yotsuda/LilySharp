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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A dead note (<c>@dead</c>) is a cross-STYLE head, and the page draws the font's
/// <c>noteheads.s2cross</c> for it — on the staff at the head size, on the tab at the
/// TabNoteHead's font-size −2 in place of the fret number — as LilyPond's <c>\deadNote</c>
/// (a <c>\tweak style #cross-style</c>) draws both. Until session 562 the staff drew two
/// strokes of its own and the tab a bold "×" of the fret face.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/property-init.ly xNote / deadNote (a tweak of style to cross);
/// scm/output-lib.scm:782-786 note-head::calc-glyph-name; scm/tablature.scm:22-28
/// tab-note-head::calc-glyph-name; scm/define-grobs.scm:3717-3746 TabNoteHead tab-note-head-interface (font-size −2).
/// MEASURED on 2.26.0 (Lab sessions/p562/dead-lp.log): staff head glyph "2cross", box
/// 0 … 1.3042 × ±0.545; tab head "2cross" at fs −2, box 0 … 1.0325 × ±0.4387.
/// Poison: draw the two strokes again (or leave Notehead Default) and the staff fact goes
/// red; draw the "×" text again and the tab fact does.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class DeadNoteGlyphTests
{
    private const string Book =
        "part bl { clef bass octave 3 tuning bass }\n"
        + "section A { bl { c4 e@dead g e@dead | c2@dead c@dead | } }\n"
        + "form main { ~A }\nscore main { staff bl  tab bl }\n";

    private static string Svg(string book)
        => SvgGenerator.Generate(SyntaxTree.Parse(book), new SvgRenderOptions { EmbedFont = false });

    private static int GlyphCount(string svg, char glyph, string fontSize)
        => Regex.Matches(svg, @"<text class=""music"" x=""[\d.]+"" y=""[\d.]+"" font-size=""" + fontSize + @"""[^>]*>" + glyph + "</text>").Count;

    [Fact]
    public void ADeadNote_IsACrossStyleHead()
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(Book), "bl");
        var notes = score.Voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToArray();
        Assert.Equal(6, notes.Length);
        foreach (var n in notes)
            Assert.Equal(n.IsDead ? NoteheadStyle.Cross : NoteheadStyle.Default, n.Notehead);
    }

    [Fact]
    public void TheStaffDrawsTheCrossGlyph_ByDuration_AndNoStrokes()
    {
        string svg = Svg(Book);
        // Two dead quarters (s2cross) and two dead halves (s1cross), at the head size 4.0.
        Assert.Equal(2, GlyphCount(svg, EmmentalerGlyphs.NoteheadCrossBlack, "4.00"));
        Assert.Equal(2, GlyphCount(svg, EmmentalerGlyphs.NoteheadCrossHalf, "4.00"));
        // The old picture: two round-capped strokes per dead head, 1.4 stems thick.
        Assert.DoesNotContain("stroke-linecap=\"round\"", svg);
    }

    [Fact]
    public void TheTabDrawsTheCrossGlyph_AtFontSizeMinusTwo_InPlaceOfTheFret()
    {
        string svg = Svg(Book);
        // All four dead notes take s2cross on the tab whatever their duration, at
        // 4.0 × magstep(−2) = 3.17.
        Assert.Equal(4, GlyphCount(svg, EmmentalerGlyphs.NoteheadCrossBlack, "3.17"));
        Assert.DoesNotContain(">×</text>", svg);
    }
}
