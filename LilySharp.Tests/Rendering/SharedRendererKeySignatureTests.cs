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
/// Key-signature accidentals must be placed using the clef's c0-position
/// (LilyPond's rule), not a uniform integer shift. In treble the first sharp of
/// a sharp key (F sharp) sits on the TOP staff line; the old code placed it half
/// a space too low.
/// </summary>
public sealed class SharedRendererKeySignatureTests
{
    [Fact]
    public void TrebleSharpKey_FirstSharpSitsOnTopStaffLine()
    {
        // D major = 2 sharps (F#, C#). Notes d/e/g/a are diatonic (no accidental
        // glyphs), so the only sharp glyphs are the key signature's.
        var (score, layout) = BuildLayout("""
            key d major
            time 4/4

            section S { line { | d'4 e g a | } }

            structure { S }
            score "o" { staff line }
            """);

        var rec = new GlyphRecorder();
        SharedRenderer.RenderTo(score, layout, rec);

        var sharps = rec.Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.AccidentalSharp)
                               .OrderBy(g => g.X).ToList();
        Assert.Equal(2, sharps.Count);

        // Top staff line: the highest (min-Y) full-width horizontal line (x1 ≈ 0).
        double topLineY = rec.Lines
            .Where(l => Math.Abs(l.X1) < 1e-6 && Math.Abs(l.Y1 - l.Y2) < 1e-6)
            .Min(l => l.Y1);

        Assert.True(Math.Abs(sharps[0].Y - topLineY) < 0.05,
            $"first key sharp (F#) Y={sharps[0].Y:F3} should sit on the top staff line Y={topLineY:F3}");
    }

    private static (MultiStaffScore Score, ScoreLayout Layout) BuildLayout(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        MultiStaffScore score;
        if (spec != null && spec.IsMultiStaff)
        {
            score = new MeasureCollector().CollectMultiStaff(tree, spec);
        }
        else
        {
            string? voice = null;
            if (spec != null && spec.Items.Length == 1 && spec.Items[0] is SingleStaffSpec single)
                voice = single.Staff.VoiceName;
            score = MultiStaffScore.FromScore(new MeasureCollector().Collect(tree, voice));
        }
        return (score, new LayoutEngine().Layout(score));
    }

    private sealed class GlyphRecorder : IDocumentContext, IDrawingContext
    {
        // Y is unaffected by the margin group's translate(MarginLeft, 0), so raw
        // coordinates are fine for comparing glyph Y against staff-line Y.
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
        public void DrawEllipse(double cx, double cy, double rx, double ry,
            Color? fill = null, Color? stroke = null, double sw = 0) { }
        public void DrawCircle(double cx, double cy, double r, Color? fill = null) { }
        public void DrawClosedBezier((double X, double Y) p0, (double X, double Y) c1,
            (double X, double Y) c2, (double X, double Y) p1, (double X, double Y) c2Back,
            (double X, double Y) c1Back, Color? fill = null) { }
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
