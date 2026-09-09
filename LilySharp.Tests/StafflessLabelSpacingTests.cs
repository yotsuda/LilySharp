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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A staffless chord row makes ROOM for the section label that sits on its line: the
/// line-start spring is floored at the label box's right edge (and, under
/// <c>marks beside</c>, the tempo's), so the first symbol stands on its own column inside
/// its own bar instead of being shifted past the bar line after spacing.
/// </summary>
/// <remarks>
/// MEASURED before the spring existed (2026-09-09, scratch/p356/mk9): bar 1 stayed 13.90 wide
/// under every label and `IntroductionLong' pushed the first `C' to 25.92, into bar 3 — the
/// window (MusicMarkEngraver.BoxedLabelXWindows → ChordNameEngraver) only MOVED the symbol.
/// Owner's decision 2026-09-09: the bar widens. Session 355.
/// </remarks>
[Trait("Category", "Unit")]
public class StafflessLabelSpacingTests
{
    private static string Book(string top, string label)
        => top + $$"""
        tempo 117
        time 4/4
        chords prog { section {{label}} { C | G | Am | F | } }
        form main { {{label}} }
        score main { chords prog }
        """ + "\n";

    private static (MultiStaffScore Score, ScoreLayout Layout) Lay(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (score, new LayoutEngine(score.Paper).Layout(score));
    }

    [Theory]
    [InlineData("", "Intro", false)]
    [InlineData("", "IntroductionLong", false)]
    [InlineData("marks beside\n", "Intro", false)]
    // A line opening on a drawn `|:` puts the box ON that bar (the label cannot sit on the
    // repeat sign, which is drawn on the row line), under both arrangements; the reach
    // starts there too (measured 2026-09-09, scratch/p356/mk10: bar and box at 3.50).
    [InlineData("", "Intro", true)]
    [InlineData("marks beside\n", "Intro", true)]
    public void TheFirstSymbol_StaysInsideItsBar_ClearOfTheLabel(string top, string label, bool openingRepeat)
    {
        string Form(bool hidden) => openingRepeat
            ? $"form main {{ |: {(hidden ? "~" : "")}{label} :| }}"
            : $"form main {{ {(hidden ? "~" : "")}{label} }}";
        string source = Book(top, label).Replace($"form main {{ {label} }}", Form(hidden: false));
        var (score, layout) = Lay(source);
        if (openingRepeat)
        {
            // The box's left edge is the opening bar's X, not the line-start edge.
            var boxed = layout.MusicMarkLayouts.Single(m => m.MarkType == MusicMarkType.SectionLabel);
            double left = boxed.X - MusicMarkEngraver.LabelBoxHalfWidth(fonts0(score), boxed.MarkType, boxed.Text);
            Assert.True(left > 0.3 + 1.0, $"the box's left edge ({left:F2}) should stand on the drawn `|:`, past the edge");
        }
        var fonts = score.TextMetrics;
        var box = Assert.Single(layout.MusicMarkLayouts, m => m.MarkType == MusicMarkType.SectionLabel);
        var first = layout.ChordNameLayouts.OrderBy(c => c.X).First();
        Assert.Equal("C", first.ChordText);

        // Inside bar 1: left of the second bar line.
        double bar2 = layout.Systems[0].Measures[1].X;
        Assert.True(first.X < bar2, $"`C' at {first.X:F2} should stand left of bar 2 at {bar2:F2}");

        // Clear of the label's box — and of the tempo beside it under `marks beside`.
        double window = box.X + MusicMarkEngraver.LabelBoxHalfWidth(fonts, box.MarkType, box.Text);
        var tempo = layout.MusicMarkLayouts.Single(m => m.MarkType == MusicMarkType.Tempo);
        if (tempo.BesideOfSourceIndex >= 0)
        {
            double tempoRight = MusicMarkEngraver.MarkXExtent(fonts, tempo, tempo.X).x1;
            Assert.True(tempoRight < bar2, $"the tempo's ink ({tempoRight:F2}) should not cross bar 2 ({bar2:F2})");
            window = tempoRight;
        }
        Assert.True(first.X >= window + ChordNameEngraver.SymbolGap - 1e-9,
            $"`C' at {first.X:F2} should clear the window's right {window:F2} by {ChordNameEngraver.SymbolGap}");

        // ...and it stands ON its column: the room after it to the bar line is the room the
        // same `C' keeps with the label hidden — the spring made the space in FRONT of the
        // column, and the window had nothing left to shift (the differential net between the
        // two spellings of the reach; a shifted symbol would eat the room behind it).
        var (_, hidden) = Lay(source.Replace(Form(hidden: false), Form(hidden: true)));
        var hiddenFirst = hidden.ChordNameLayouts.OrderBy(c => c.X).First();
        double hiddenBar2 = hidden.Systems[0].Measures[1].X;
        Assert.Equal(hiddenBar2 - hiddenFirst.X, bar2 - first.X, 6);
    }

    private static LilySharp.Core.Rendering.ScoreTextMetrics fonts0(MultiStaffScore score) => score.TextMetrics;

    [Fact]
    public void ABarWithNoLabel_IsUntouched()
    {
        // The same four bars with the label hidden: the wish is 0 and every bar keeps the
        // width it had before the spring existed (13.90 / 10.49, mk8).
        var (_, hidden) = Lay(Book("", "Intro").Replace("form main { Intro }", "form main { ~Intro }"));
        var (_, shown) = Lay(Book("", "Intro"));
        Assert.Equal(10.49, hidden.Systems[0].Measures[1].Width, 2);
        Assert.True(shown.Systems[0].Measures[0].Width > hidden.Systems[0].Measures[0].Width + 3.0,
            "the labelled bar should be wider than the unlabelled one by more than the box's share");
    }
}
