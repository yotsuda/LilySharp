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
/// Which group types carry the barline across the gap between their staves
/// (LilyPond SpanBar), and which leave each staff its own.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/engraver-init.ly — GrandStaff/PianoStaff/StaffGroup have a
///   Span_bar_engraver; ChoirStaff does not, and an ungrouped staff has no group
///   to span.
/// </remarks>
public sealed class SharedRendererSpanBarTests
{
    private const double StaffHeight = 4.0;

    [Theory]
    // A brace or bracket at the left edge says nothing about the barlines:
    // choirStaff is bracketed exactly like staffGroup and still must not span.
    [InlineData("grandStaff", true)]
    [InlineData("staffGroup", true)]
    [InlineData("choirStaff", false)]
    public void GroupBarlines_BridgeTheStaffGap_OnlyWhenTheGroupSpansThem(
        string keyword, bool expectSpan)
    {
        var rendered = Render($$"""
            title "T"
            time 4/4

            phrase rh { c''4 d'' e'' f'' | }
            phrase lh { c4 e g c' | }

            section Main { melody { rh } bass { lh } }
            form main { Main }

            score main "t" {
              {{keyword}} {
                staff treble melody
                staff bass bass
              }
            }
            """);

        Assert.Equal(expectSpan, HasBarlineBridgingTheGap(rendered));
    }

    [Fact]
    public void SingleStaff_HasNoSpanningBarline()
    {
        var single = Render("""
            time 4/4
            section S { line { c'4 d e f | } }
            form main { S }
            score main "s" { staff line }
            """);

        Assert.False(HasBarlineBridgingTheGap(single),
            "single staff should not have a group-spanning barline");
    }

    /// <summary>
    /// Is there an x where the barline runs UNBROKEN past one staff's worth of
    /// height — i.e. out of a staff and across the gap?
    /// </summary>
    /// <remarks>
    /// This asks about the line on the page, not about how it was emitted. The
    /// span is drawn as the staff bars plus a separate gap filler, so the older
    /// form of this test — "some single rect is taller than a staff" — held only
    /// while a second pass also painted one full-height rect over the group. That
    /// pass was the ChoirStaff bug: it had no idea the group must not span.
    /// </remarks>
    private static bool HasBarlineBridgingTheGap(RectRecorder rendered)
    {
        // Abutting rects are computed by different expressions; let them touch.
        const double Touching = 0.01;

        return rendered.Rects
            .Where(IsVerticalBar)
            .GroupBy(r => Math.Round(r.X, 3))
            .Any(column =>
            {
                double reach = double.NegativeInfinity, runTop = 0;
                foreach (var r in column.OrderBy(r => r.Y))
                {
                    // A rect that starts past the current run's end begins a new run.
                    if (r.Y > reach + Touching) { runTop = r.Y; reach = r.Y + r.H; }
                    else reach = Math.Max(reach, r.Y + r.H);
                    if (reach - runTop > StaffHeight + Touching) return true;
                }
                return false;
            });
    }

    private static bool IsVerticalBar((double X, double Y, double W, double H) r)
        => r.W <= EngravingDefaults.ThickBarlineThickness + 1e-6 && r.H > 0;

    private static RectRecorder Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        MultiStaffScore score = spec is { IsMultiStaff: true }
            ? new MeasureCollector().CollectMultiStaff(tree, spec)
            : MultiStaffScore.FromScore(new MeasureCollector().Collect(tree,
                spec is { Items.Length: 1 } && spec.Items[0] is SingleStaffSpec s ? s.Staff.VoiceName : null));
        var layout = new LayoutEngine().Layout(score);
        var rec = new RectRecorder();
        SharedRenderer.RenderTo(score, layout, rec);
        return rec;
    }

    private sealed class RectRecorder : IDocumentContext, IDrawingContext
    {
        public List<(double X, double Y, double W, double H)> Rects { get; } = new();

        public TextFontPlan Fonts { get; set; } = TextFontPlan.Default;

        public IDrawingContext BeginPage(double w, double h) => this;
        public void EndPage() { }
        public void Dispose() { }

        public void DrawRectangle(double x, double y, double w, double h,
            Color? fill = null, Color? stroke = null, double sw = 0)
            => Rects.Add((x, y, w, h));

        public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
            (double X, double Y) p2, (double X, double Y) p3, Color fill) { }

        public void DrawLine(double x1, double y1, double x2, double y2,
            Color? stroke = null, double strokeWidth = 0.1, (double On, double Off)? dash = null,
            LineCap cap = LineCap.Butt) { }
        public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null) { }
        public void DrawEllipse(double cx, double cy, double rx, double ry,
            Color? fill = null, Color? stroke = null, double sw = 0) { }
        public void DrawCircle(double cx, double cy, double r, Color? fill = null) { }
        public void DrawClosedBezier((double X, double Y) p0, (double X, double Y) c1,
            (double X, double Y) c2, (double X, double Y) p1, (double X, double Y) c2Back,
            (double X, double Y) c1Back, Color? fill = null, double strokeWidth = 0) { }
        public void DrawText(string text, double x, double y, double fontSize, TextRole role,
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
