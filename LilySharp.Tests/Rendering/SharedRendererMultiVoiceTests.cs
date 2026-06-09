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
/// SharedRenderer must render ALL voices of a staff, not just the first.
/// In a two-voice staff, voice 1 gets stems up and voice 2 gets stems down,
/// so both stem directions appear. Previously only PrimaryVoice was drawn.
/// </summary>
public sealed class SharedRendererMultiVoiceTests
{
    [Fact]
    public void TwoVoices_RenderBothWithOppositeStemDirections()
    {
        // Voice 1 high (stems up), voice 2 low (stems down). Quarter notes so the
        // stems are drawn inline (no beams).
        var (score, layout) = BuildLayout("""
            key C major
            time 4/4

            section S { line { << { c''4 c'' c'' c'' } \\ { e4 e e e } >> } }

            structure { S }
            render score "o.svg" { staff { line } }
            """);

        // Sanity: the staff really has two voices.
        int maxVoices = score.EnumerateStaves().Max(s => s.Staff.Voices.Length);
        Assert.True(maxVoices >= 2, $"expected a 2-voice staff, got {maxVoices}");

        var rec = new StemRecorder();
        SharedRenderer.RenderTo(score, layout, rec);

        var stems = rec.Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-6 && Math.Abs(l.Y1 - l.Y2) > 1e-6
                        && Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-4)
            .ToList();

        // Up stems go to a smaller Y (y2 < y1); down stems to a larger Y.
        bool anyUp = stems.Any(l => l.Y2 < l.Y1);
        bool anyDown = stems.Any(l => l.Y2 > l.Y1);
        Assert.True(anyUp, "voice 1 should produce up-stems");
        Assert.True(anyDown, "voice 2 should produce down-stems (only PrimaryVoice was drawn before)");
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

    private sealed class StemRecorder : IDocumentContext, IDrawingContext
    {
        public List<(double X1, double Y1, double X2, double Y2, double StrokeWidth)> Lines { get; } = new();

        public IDrawingContext BeginPage(double w, double h) => this;
        public void EndPage() { }
        public void Dispose() { }

        public void DrawLine(double x1, double y1, double x2, double y2,
            Color? stroke = null, double strokeWidth = 0.1, (double On, double Off)? dash = null)
            => Lines.Add((x1, y1, x2, y2, strokeWidth));

        public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null) { }
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
