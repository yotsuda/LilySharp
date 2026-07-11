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
/// In a grand staff, barlines must connect across the staves (LilyPond SpanBar):
/// a barline rect taller than one staff bridges the gap between the two staves.
/// A single-staff score has no such tall barline.
/// </summary>
public sealed class SharedRendererSpanBarTests
{
    private const double StaffHeight = 4.0;

    [Fact]
    public void GrandStaff_DrawsBarlineSpanningBothStaves()
    {
        var grand = Render("""
            title "T"
            time 4/4

            phrase rh { c''4 d'' e'' f'' | }
            phrase lh { c4 e g c' | }

            section Main { melody { $rh } bass { $lh } }
            form main { Main }

            score main "t" {
              grandStaff {
                staff treble melody
                staff bass bass
              }
            }
            """);

        bool spanBar = grand.Rects.Any(r => IsVerticalBar(r) && r.H > StaffHeight + 1.0);
        Assert.True(spanBar, "grand staff should have a barline spanning both staves");
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

        bool spanBar = single.Rects.Any(r => IsVerticalBar(r) && r.H > StaffHeight + 1.0);
        Assert.False(spanBar, "single staff should not have a group-spanning barline");
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

        public IDrawingContext BeginPage(double w, double h) => this;
        public void EndPage() { }
        public void Dispose() { }

        public void DrawRectangle(double x, double y, double w, double h,
            Color? fill = null, Color? stroke = null, double sw = 0)
            => Rects.Add((x, y, w, h));

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
