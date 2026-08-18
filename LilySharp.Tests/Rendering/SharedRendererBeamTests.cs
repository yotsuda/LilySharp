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
using LilySharp.Core.Svg;            // EngravingDefaults, EmmentalerGlyphs
using LilySharp.Core.Svg.Collector;  // MeasureCollector, RenderSpecParser, SingleStaffSpec
using LilySharp.Core.Svg.Layout;     // LayoutEngine, ScoreLayout
using LilySharp.Core.Svg.Model;      // MultiStaffScore
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// Guards the beamed-stem rendering contract in <see cref="SharedRenderer"/>:
/// (1) beamed notes/chords must NOT draw an inline stem — <c>DrawBeams</c> owns
///     the stem, so a beamed member contributes exactly one stem, not two; and
/// (2) in multi-staff (column) scores, noteheads must be placed at the shared
///     timing-column X so they sit directly under the beam stems (which are
///     positioned from the same columns) instead of drifting away.
/// </summary>
/// <remarks>
/// These exercise the <c>SharedRenderer.RenderTo</c> path through a recording
/// <see cref="IDocumentContext"/>, so they assert the actual draw calls rather
/// than just "produces a valid PNG/PDF". Without the beamed-stem gating the
/// stem count doubles; without the column-timing notehead X the stems and
/// noteheads separate in multi-staff scores.
/// </remarks>
public sealed class SharedRendererBeamTests
{
    [Fact]
    public void BeamedNotes_DrawExactlyOneStemPerMember_NoDuplicateInlineStem()
    {
        // Two explicit beam groups of four eighths each = 8 beamed members,
        // and nothing else with a stem. The correct rendering draws one stem
        // per member (8); the pre-fix code drew an inline stem in DrawNote AND
        // a beam stem in DrawBeams (16).
        const string source = """
            key C major
            time 4/4

            section Demo {
                line { | c'8[ d e f] g[ a b c''] | }
            }

            form main { Demo }
            score main "out" { staff line }
            """;
        var (score, layout) = BuildLayout(source);

        int expectedStems = layout.BeamLayouts.Sum(b => b.Group.Members.Length);
        Assert.Equal(8, expectedStems);  // sanity: manual beams produced 8 members

        var rec = new RecordingContext();
        SharedRenderer.RenderTo(score, layout, rec);

        int stemCount = rec.Lines.Count(IsVerticalStem);
        Assert.Equal(expectedStems, stemCount);
    }

    [Fact]
    public void MultiStaff_BeamStems_SitUnderColumnAlignedNoteheads()
    {
        // A beamed top staff over a bottom staff of wide half-note chords. The
        // chords widen the shared timing columns, so the beamed staff's per-
        // staff item X (Items[i].X) no longer equals the column X used to place
        // the beam stems. The notehead X must follow the column grid (the fix);
        // otherwise stems and noteheads drift apart.
        const string source = """
            key C major
            time 4/4

            section M {
                top { | c'8[ d e f] g[ a b c''] | }
                bot { | <c e g>2 <d f a>2 | }
            }

            form main { M }
            score main "grand" {
                staff top
                staff bot
            }
            """;
        var (score, layout) = BuildLayout(source);
        Assert.NotEmpty(layout.BeamLayouts);

        var rec = new RecordingContext();
        SharedRenderer.RenderTo(score, layout, rec);

        // Absolute X (margin applied by the recorder) of every notehead glyph.
        var noteheadChars = new HashSet<char>
        {
            EmmentalerGlyphs.GetNotehead(8),
            EmmentalerGlyphs.GetNotehead(4),
            EmmentalerGlyphs.GetNotehead(2),
            EmmentalerGlyphs.GetNotehead(1),
        };
        var noteheadXs = rec.Glyphs
            .Where(g => noteheadChars.Contains(g.Glyph))
            .Select(g => g.X)
            .ToList();
        Assert.NotEmpty(noteheadXs);

        // BeamLayout.MemberXPositions are the column-grid X anchors (already
        // include MeasureLayout.X) that DrawBeams uses for the stems. With the
        // fix, each beamed member's notehead is drawn at MarginLeft + that same
        // X. Allow a tight tolerance for floating-point only.
        const double tol = 0.01;
        double marginLeft = layout.Options.MarginLeft;
        foreach (var beam in layout.BeamLayouts)
        {
            for (int i = 0; i < beam.MemberXPositions.Length; i++)
            {
                double expectedX = marginLeft + beam.MemberXPositions[i];
                double nearest = noteheadXs.Min(x => Math.Abs(x - expectedX));
                Assert.True(nearest < tol,
                    $"Beam member {i}: notehead expected at column X={expectedX:F3} " +
                    $"(where the stem is drawn) but nearest notehead is Δ={nearest:F3} away — " +
                    "stems and noteheads have drifted apart.");
            }
        }
    }

    // ---- helpers ----

    private static bool IsVerticalStem(RecordedLine l) =>
        Math.Abs(l.X1 - l.X2) < 1e-6 &&
        Math.Abs(l.Y1 - l.Y2) > 1e-6 &&
        Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-4;

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
            string? voiceName = null;
            if (spec != null && spec.Items.Length == 1 && spec.Items[0] is SingleStaffSpec single)
                voiceName = single.Staff.VoiceName;
            var single2 = new MeasureCollector().Collect(tree, voiceName);
            score = MultiStaffScore.FromScore(single2);
        }
        var layout = new LayoutEngine().Layout(score);
        return (score, layout);
    }

    // ---- recording draw context ----

    private readonly record struct RecordedLine(
        double X1, double Y1, double X2, double Y2, double StrokeWidth);

    private readonly record struct RecordedGlyph(char Glyph, double X, double Y, double FontSize);

    /// <summary>
    /// Captures stem lines and glyph anchors with the active group transform
    /// (translation + scale) applied, so recorded coordinates are absolute and
    /// directly comparable across staves. Non-geometric draw calls are ignored.
    /// </summary>
    private sealed class RecordingContext : IDocumentContext, IDrawingContext
    {
        public List<RecordedLine> Lines { get; } = new();
        public List<RecordedGlyph> Glyphs { get; } = new();

        // Seeded from DrawingTransform.Identity, a real identity since 2026-08-19. Before
        // that the property was `new()` — the record struct's parameterless constructor,
        // which zeroes ScaleX/ScaleY instead of taking the primary constructor's defaults —
        // so this line hand-wrote the identity to dodge it. See DrawingTransform.Identity.
        private DrawingTransform _current = DrawingTransform.Identity;
        private readonly Stack<DrawingTransform> _stack = new();

        public TextFontPlan Fonts { get; set; } = TextFontPlan.Default;

        public IDrawingContext BeginPage(double widthSpaces, double heightSpaces) => this;
        public void EndPage() { }
        public void Dispose() { }

        private (double X, double Y) Apply(double x, double y) =>
            (_current.TranslateX + x * _current.ScaleX,
             _current.TranslateY + y * _current.ScaleY);

        public void DrawLine(
            double x1, double y1, double x2, double y2,
            Color? stroke = null, double strokeWidth = 0.1,
            (double On, double Off)? dash = null, LineCap cap = LineCap.Butt)
        {
            var (ax1, ay1) = Apply(x1, y1);
            var (ax2, ay2) = Apply(x2, y2);
            Lines.Add(new RecordedLine(ax1, ay1, ax2, ay2, strokeWidth * _current.ScaleX));
        }

        public void DrawGlyph(char glyph, double x, double y, double fontSize, Color? fill = null)
        {
            var (ax, ay) = Apply(x, y);
            Glyphs.Add(new RecordedGlyph(glyph, ax, ay, fontSize * _current.ScaleX));
        }

        public void DrawRectangle(double x, double y, double width, double height,
            Color? fill = null, Color? stroke = null, double strokeWidth = 0) { }

        public void DrawFilledQuad((double X, double Y) p0, (double X, double Y) p1,
            (double X, double Y) p2, (double X, double Y) p3, Color fill) { }

        public void DrawEllipse(double cx, double cy, double rx, double ry,
            Color? fill = null, Color? stroke = null, double strokeWidth = 0) { }

        public void DrawCircle(double cx, double cy, double r, Color? fill = null) { }

        public void DrawClosedBezier(
            (double X, double Y) p0, (double X, double Y) c1, (double X, double Y) c2,
            (double X, double Y) p1, (double X, double Y) c2Back, (double X, double Y) c1Back,
            Color? fill = null, double strokeWidth = 0) { }

        public void DrawText(string text, double x, double y, double fontSize,
            TextRole role, FontStyle style = FontStyle.Regular,
            TextAnchor anchor = TextAnchor.Start, Color? fill = null,
            VerticalAnchor verticalAnchor = VerticalAnchor.Baseline) { }

        public IDisposable Source(int sourcePosition) => NullScope.Instance;

        public IDisposable BeginGroup(DrawingTransform transform)
        {
            _stack.Push(_current);
            _current = Compose(_current, transform);
            return new PopScope(this);
        }

        private static DrawingTransform Compose(DrawingTransform c, DrawingTransform t) =>
            new(c.TranslateX + c.ScaleX * t.TranslateX,
                c.TranslateY + c.ScaleY * t.TranslateY,
                c.ScaleX * t.ScaleX,
                c.ScaleY * t.ScaleY);

        private void Pop() => _current = _stack.Pop();

        private sealed class PopScope : IDisposable
        {
            private readonly RecordingContext _ctx;
            private bool _done;
            public PopScope(RecordingContext ctx) => _ctx = ctx;
            public void Dispose() { if (!_done) { _done = true; _ctx.Pop(); } }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
