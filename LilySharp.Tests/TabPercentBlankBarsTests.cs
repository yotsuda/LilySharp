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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The BLANK bars a 3+-bar <c>repeat percent</c> body leaves after its single slash draw
/// nothing on a tab staff, exactly as on the notation staff and as LilyPond prints them
/// (lily/percent-repeat-iterator.cc emits one RepeatSlashEvent for the whole body; measured
/// 2.26.0 on the owner's Billie Jean bassTab book, bars 8-10 / 12-14 empty). The collector
/// fills those bars with SPACER rests; the notation arm of the renderer skips spacers, and
/// until 2026-09-02 the tab arm did not — it printed a whole rest in every blank bar.
/// Same shape as the dotted-chord gate of the day before: one rule, two arms.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TabPercentBlankBarsTests
{
    private static RecordingDrawingContext Render(string sectionBody, string staffLine)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part bl {
              clef bass
              tuning bass
              section A { {{sectionBody}} }
            }
            form main { A }
            score main { {{staffLine}} }
            """);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    // A three-bar body: bars 4-6 are one slash and two blank bars.
    private const string ThreeBarPercent =
        "repeat percent 2 { a,,8\\4 a,,\\4 a,,\\4 a,,\\4 c\\1 c\\1 c\\1 c\\1 | d,4\\3 d,\\3 d,\\3 d,\\3 | g,2\\2 g,\\2 | }";

    private static int WholeRests(RecordingDrawingContext page)
        => page.Glyphs.Count(g => g.Glyph is EmmentalerGlyphs.RestWhole or EmmentalerGlyphs.RestWholeLedgered);

    [Theory]
    [InlineData("tab bl as full")]
    [InlineData("staff bl")]
    public void PercentBlankBars_DrawNoWholeRest(string staffLine)
        => Assert.Equal(0, WholeRests(Render(ThreeBarPercent, staffLine)));

    [Theory]
    [InlineData("tab bl as full")]
    [InlineData("staff bl")]
    public void AWrittenWholeRest_StillPrints_OnBothStaves(string staffLine)
    {
        // The positive control: the same book with a REAL `r1` after the repeat draws
        // exactly one whole rest — the gate skips spacers, not rests.
        Assert.Equal(1, WholeRests(Render(ThreeBarPercent + " r1 |", staffLine)));
    }

    [Fact]
    public void AWrittenSpacer_DrawsNothing_OnTheTab()
    {
        // `s1` is the same spacer item the collector uses for the blank bars, written by
        // hand — a tab prints nothing for it, like the notation staff.
        Assert.Equal(0, WholeRests(Render("a,,4\\4 a,,\\4 a,,\\4 a,,\\4 | s1 |", "tab bl as full")));
    }
}
