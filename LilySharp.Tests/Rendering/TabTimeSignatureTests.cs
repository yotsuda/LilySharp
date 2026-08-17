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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// A tablature staff engraves a time signature exactly when it is FULL NOTATION — which is
/// Lily#'s default <c>tab</c> — and not when it is <c>tab … as numbers</c>.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: ly/engraver-init.ly:1219-1220 sits five lines under \remove Key_engraver in
/// the TabStaff block and only BLANKS the meter — <c>\override TimeSignature.stencil = ##f</c>
/// — so the engraver stays and the grob keeps its place in the shared break-align column; the
/// revert is at ly/property-init.ly:825-826, above tabFullNotation's no-stem-extend one.
/// Lily#'s default
/// <c>tab</c> draws stems, flags, dots, rests, beams and tuplet brackets, and
/// <c>LilyPondExporter</c> writes <c>\tabFullNotation</c> into its twin; only
/// <c>as numbers</c> is the bare TabStaff. So the meter follows that switch and nothing else.
/// </para>
/// <para>
/// MEASURED on LilyPond, the same two bars (<c>c1 | \time 2/4 c2</c>, bass four-string) put
/// through <c>lysc ly</c> twice — once as a default <c>tab</c> (twin carries
/// <c>\tabFullNotation</c>) and once <c>as numbers</c> (twin does not). The two SVGs carry
/// FOUR glyph paths against ONE:
/// </para>
/// <list type="table">
/// <item><term>\tabFullNotation</term><description>TAB clef 9.1358, initial 4/4 (the C glyph)
/// 13.4558, mid-piece 2/4 at 23.6142 / 23.5459, frets 17.1558 and 28.1507.</description></item>
/// <item><term>bare TabStaff</term><description>TAB clef 9.1358 alone; frets 14.3358 and
/// 21.8759.</description></item>
/// </list>
/// <para>
/// So the meter costs 2.8200 of prefix and 3.4548 between the two frets, and Lily# used to
/// reserve the 3.4548 in BOTH modes while drawing the glyph in NEITHER — the reported
/// defect: "the meter is not drawn on the tab; only the blank space is reserved".
/// </para>
/// <para>
/// The VERTICAL rule is measured from the same pair against a notation staff of the same
/// music: the digits stand 2.000000 apart on BOTH staves though the tab's staff-space is
/// 1.5 — so the stencil is NOT scaled with the tab's string spacing — and the numerator's
/// baseline sits ON the staff centre with the denominator's 2.000000 below it, the 4/4 C
/// glyph on the centre. Those are a notation staff's own offsets with "middle line" read as
/// the tab's centre.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabTimeSignatureTests
{
    // `c1 | time 2/4 c2` on a bass tab: one initial meter and one mid-piece change, the
    // smallest book that reaches both sites. The score block chooses the mode.
    // ⚠️ The meter is TOP-LEVEL, not in the part header: `time` there is a score setting in a
    // part block and ScoreSettingInPartHeaderValidator rejects it outright (LYS1026), so a
    // fixture written that way fails this file's own HasErrors guard rather than quietly
    // measuring 4/4 — which is how the 3/4 cases here were first written and caught.
    private static string Src(string scoreBlock, string meter = "4/4", string music = "c1 | time 2/4 c2") => $$"""
        time {{meter}}
        part melody {
          instrument bass
          section A { {{music}} }
        }
        form main { A }
        score main { {{scoreBlock}} }
        """;

    [Fact]
    public void DefaultTab_EngravesTheInitialMeter()
    {
        // The reported defect, as a page assertion: 4/4 prints the C glyph
        // (DrawTimeSignature's LilyPond-default style branch) on the tab staff.
        var rec = Render(Src("tab melody"));
        Assert.Equal(1, rec.Glyphs.Count(g => g.Glyph == EmmentalerGlyphs.TimeSigCommon));
    }

    [Fact]
    public void NumbersOnlyTab_EngravesNoMeter()
    {
        // The falsifier's other half — and the half that was already right. `as numbers` IS
        // the bare TabStaff, whose TimeSignature stencil LilyPond blanks.
        var rec = Render(Src("tab melody as numbers"));
        Assert.Empty(rec.Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.TimeSigCommon));
        // …and the mid-piece change is absent too: one switch, both sites.
        Assert.Empty(rec.Glyphs.Where(g => g.Glyph is '2' or '4'));
    }

    [Fact]
    public void DefaultTab_EngravesAMidPieceMeterChange()
    {
        // `time 2/4` inside the music. Not the C glyph — 2/4 takes the stacked-digit path,
        // so the page carries a '2' over a '4'.
        var rec = Render(Src("tab melody"));
        Assert.Equal(1, rec.Glyphs.Count(g => g.Glyph == '2'));
        Assert.Equal(1, rec.Glyphs.Count(g => g.Glyph == '4'));
    }

    [Fact]
    public void TabMeterDigits_StandTwoApart_AndAreNotScaledByTheStringSpacing()
    {
        // MEASURED on LilyPond: 2.000000 between the numerator's and the denominator's
        // baselines on a tab staff whose staff-space is 1.5 — the same 2.000000 a notation
        // staff shows. A stencil scaled with the tab's string spacing would print 3.0 here,
        // which is what reusing the tab's own geometry (rather than a synthetic staffY)
        // would have produced.
        var tab = Render(Src("tab melody", meter: "3/4", music: "c2."));
        var staff = Render(Src("staff melody", meter: "3/4", music: "c2."));

        double tabGap = Single(tab, '3').Y - Single(tab, '4').Y;
        double staffGap = Single(staff, '3').Y - Single(staff, '4').Y;
        Assert.Equal(2.0, Math.Abs(tabGap), 6);
        // The same VECTOR, not just the same magnitude: the denominator hangs below the
        // numerator on the tab exactly as on the notation staff, and the tab's 1.5 string
        // spacing does not enter.
        Assert.Equal(staffGap, tabGap, 6);
        Assert.Equal(Single(tab, '3').X, Single(tab, '4').X, 0);  // rows centred on each other
    }

    [Fact]
    public void TabMeter_SitsOnTheTabStaffCentre()
    {
        // The anchor. The tab's centre is the mean of its string lines, which is also where
        // the TAB clef sits — LilyPond puts the numerator's baseline exactly there.
        var rec = Render(Src("tab melody", meter: "3/4", music: "c2."));
        double centre = TabCentreY(rec);
        Assert.Equal(centre, Single(rec, '3').Y, 6);
    }

    [Fact]
    public void MeterChangeAtALineBreak_PrintsTheCourtesyAndThePrefix_AndNothingElse()
    {
        // Two sites and exactly two, which is the whole claim:
        //   ⒜ scm/define-grobs.scm:3922-3953 break-align-anchor-alignment … break-visibility,
        //      the TimeSignature grob's own block, where all-visible sits — so a CHANGED
        //      meter prints as a COURTESY at the end of the previous line. The
        //      tab hangs it off the final BAR LINE — never off a courtesy key, which a tab
        //      staff can never have (no Key_engraver in either mode).
        //   ⒝ and again in the next system's PREFIX.
        // A THIRD would mean the in-measure copy printed too. The tab takes its mid-piece
        // changes from EnumerateStaffItems — the same walk the notation staff uses — and that
        // walk owns the skip, so the two cannot come apart.
        var rec = Render(Src("tab melody", music: "c1 | c1 | break time 2/4 c2 | c2"));
        Assert.Equal(2, rec.Glyphs.Count(g => g.Glyph == '2'));
    }

    [Fact]
    public void MeterChangeAtALineBreak_IsAbsentFromANumbersOnlyTab()
    {
        // Both sites are behind the one predicate: a bare TabStaff prints neither the
        // courtesy nor the prefix.
        var rec = Render(Src("tab melody as numbers", music: "c1 | c1 | break time 2/4 c2 | c2"));
        Assert.Empty(rec.Glyphs.Where(g => g.Glyph == '2'));
    }

    [Fact]
    public void TheReservationAndTheDrawingAskTheSameQuestion()
    {
        // §7.7: the meter column is booked by SpacingRules.ContributesToTimeColumnWidth and
        // drawn under the same predicate, so a mode cannot book a column it does not draw
        // (the bug) or draw a glyph into a hole nobody booked. Asserted on the predicate
        // itself, since that is the single house both sides read.
        var full = CollectStaff(Src("tab melody"));
        var numbers = CollectStaff(Src("tab melody as numbers"));
        Assert.True(SpacingRules.ContributesToTimeColumnWidth(full));
        Assert.False(SpacingRules.ContributesToTimeColumnWidth(numbers));
        // The KEY is NOT symmetric: \tabFullNotation has no removed Key_engraver to revert,
        // so neither mode engraves a signature.
        Assert.False(SpacingRules.ContributesToKeyColumnWidth(full));
        Assert.False(SpacingRules.ContributesToKeyColumnWidth(numbers));
    }

    // ---------- helpers ----------

    /// <summary>The tab staff's vertical centre, read off the DRAWN string lines rather than
    /// recomputed — the quantity the assertions are about is where the glyph landed relative
    /// to the lines the reader sees.</summary>
    private static double TabCentreY(GlyphRecorder rec)
    {
        var ys = rec.Lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9).Select(l => l.Y1).ToList();
        return (ys.Max() + ys.Min()) / 2.0;
    }

    private static (char Glyph, double X, double Y) Single(GlyphRecorder rec, char glyph)
        => rec.Glyphs.Single(g => g.Glyph == glyph);

    private static Staff CollectStaff(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree)!;
        return new MeasureCollector().CollectMultiStaff(tree, spec)
            .EnumerateStaves().Single().Staff;
    }

    private static GlyphRecorder Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree)!;
        var score = new MeasureCollector().CollectMultiStaff(tree, spec);
        var layout = new LayoutEngine().Layout(score);
        var rec = new GlyphRecorder();
        SharedRenderer.RenderTo(score, layout, rec);
        return rec;
    }

    private sealed class GlyphRecorder : IDocumentContext, IDrawingContext
    {
        public List<(char Glyph, double X, double Y)> Glyphs { get; } = new();
        public List<(double X1, double Y1, double X2, double Y2)> Lines { get; } = new();

        public IDrawingContext BeginPage(double w, double h) => this;
        public void EndPage() { }
        public void Dispose() { }

        public void DrawLine(double x1, double y1, double x2, double y2,
            Color? stroke = null, double strokeWidth = 0.1, (double On, double Off)? dash = null,
            LineCap cap = LineCap.Butt)
            => Lines.Add((x1, y1, x2, y2));

        public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
            => Glyphs.Add((glyph, x, y));

        public void DrawRectangle(double x, double y, double w, double h,
            Color? fill = null, Color? stroke = null, double sw = 0) { }
        public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
            (double X, double Y) p2, (double X, double Y) p3, Color fill) { }
        public void DrawEllipse(double cx, double cy, double rx, double ry,
            Color? fill = null, Color? stroke = null, double sw = 0) { }
        public void DrawCircle(double cx, double cy, double r, Color? fill = null) { }
        public void DrawClosedBezier((double X, double Y) p0, (double X, double Y) c1,
            (double X, double Y) c2, (double X, double Y) p1, (double X, double Y) c2Back,
            (double X, double Y) c1Back, Color? fill = null, double strokeWidth = 0) { }
        public void DrawText(string text, double x, double y, double fontSize, string fontFamily,
            FontStyle style = FontStyle.Regular, TextAnchor anchor = TextAnchor.Start,
            Color? fill = null, VerticalAnchor verticalAnchor = VerticalAnchor.Baseline) { }
        public IDisposable Source(int sourcePosition) => NullScope.Instance;
        public IDisposable BeginGroup(DrawingTransform transform) => NullScope.Instance;

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
