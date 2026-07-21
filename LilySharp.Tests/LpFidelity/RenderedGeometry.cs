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

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// A Lily# score rendered once, exposing the placed geometry in the same vocabulary the
/// LilyPond probes are measured in, so a ledger entry reads the same on both sides.
/// </summary>
/// <remarks>
/// <para>
/// EVERY quantity here is ANCHOR to ANCHOR — a glyph's draw origin, or a bar line
/// rectangle's edge. Nothing depends on glyph ink widths. That is deliberate: it keeps the
/// Lily# side free of the very metrics tables a fidelity measurement is supposed to be
/// auditing, and it matches what the LilyPond probes dump
/// (<c>ly:grob-relative-coordinate</c>, which is also an anchor). A notehead's anchor is
/// its ink LEFT edge on both sides, and that coincides with the paper column origin —
/// verified on 2.24.4 by dumping PaperColumn and NoteHead relative coordinates and finding
/// them equal (COORDINATE_AUDIT.md §2.4).
/// </para>
/// <para>
/// Only the first system of the first page is considered. Probes are written to be one
/// system long so a line break can never silently change what is being measured.
/// </para>
/// </remarks>
internal sealed class RenderedGeometry
{
    /// <summary>A plain bar line's drawn width, used to tell bar lines from other rects.</summary>
    /// <remarks>Thin bar lines only. A probe that needs a thick/repeat bar should select
    /// explicitly rather than widening this predicate.</remarks>
    private const double ThinBarlineMaxWidth = 0.35;

    private readonly RecordingDrawingContext _page;

    private RenderedGeometry(RecordingDrawingContext page) => _page = page;

    /// <summary>Parses and lays out <paramref name="source"/>, recording what gets drawn.</summary>
    public static RenderedGeometry Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
        {
            throw new InvalidOperationException(
                "probe source does not parse:\n  "
                + string.Join("\n  ", tree.Diagnostics.Select(d => d.ToString())));
        }

        // The same two steps SvgGenerator.BuildLayout takes, which is private. Going
        // through CollectScore (internal, and explicitly the collector the render path
        // shares with IncrementalCompiler) keeps this on the product's path rather than
        // beside it.
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);

        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return new RenderedGeometry(doc.Page);
    }

    /// <summary>Music glyphs in drawing order, left to right.</summary>
    public IReadOnlyList<DrawnGlyph> Glyphs =>
        _page.Glyphs.OrderBy(g => g.X).ToList();

    /// <summary>Thin bar lines, left to right.</summary>
    public IReadOnlyList<DrawnRect> Barlines =>
        _page.Rects.Where(r => r.Width > 0 && r.Width <= ThinBarlineMaxWidth)
                   .OrderBy(r => r.X).ToList();

    /// <summary>
    /// The <paramref name="index"/>-th thin bar line, 0-based, left to right.
    /// </summary>
    /// <remarks>
    /// Index 0 is the bar line between the FIRST and SECOND measures: Lily# draws no bar
    /// line at a system start, so there is no opening one to skip past. (A final <c>|.</c>
    /// contributes its thin half here and its thick half is filtered out by
    /// <see cref="ThinBarlineMaxWidth"/>.)
    /// </remarks>
    private DrawnRect Barline(int index)
    {
        var bars = Barlines;
        if (index < 0 || index >= bars.Count)
        {
            throw new InvalidOperationException(
                $"wanted thin bar line #{index} but the probe drew {bars.Count}. "
                + "Index 0 is the bar line between measures 1 and 2 — Lily# draws none at a "
                + "system start.\nDrawn geometry:\n" + Describe());
        }
        return bars[index];
    }

    /// <summary>The <paramref name="index"/>-th bar line's LEFT edge. See <see cref="Barline"/>.</summary>
    public double BarlineLeft(int index) => Barline(index).X;

    /// <summary>The <paramref name="index"/>-th bar line's RIGHT (ink) edge.</summary>
    public double BarlineRight(int index)
    {
        var b = Barline(index);
        return b.X + b.Width;
    }

    /// <summary>The first music glyph drawn strictly to the right of <paramref name="x"/>.</summary>
    public DrawnGlyph FirstGlyphAfter(double x) =>
        Glyphs.FirstOrDefault(g => g.X > x + 1e-9,
            throw_: $"no music glyph is drawn to the right of x={x:F6}");

    /// <summary>The last music glyph drawn strictly to the left of <paramref name="x"/>.</summary>
    public DrawnGlyph LastGlyphBefore(double x) =>
        Glyphs.LastOrDefault(g => g.X < x - 1e-9,
            throw_: $"no music glyph is drawn to the left of x={x:F6}");

    /// <summary>
    /// Bar line <paramref name="barIndex"/>'s ink right edge → the next music glyph's anchor.
    /// This is the quantity Staff_spacing::get_spacing governs.
    /// </summary>
    public double BarlineRightToNextGlyph(int barIndex)
    {
        double bar = BarlineRight(barIndex);
        return FirstGlyphAfter(bar).X - bar;
    }

    /// <summary>The plain notehead glyphs, by codepoint.</summary>
    /// <remarks>
    /// Selecting the notehead BY IDENTITY rather than by counting glyphs is deliberate. The
    /// two sides of a comparison do not agree on glyph counts: LilyPond dumps a key
    /// signature as ONE KeySignature grob, while Lily# draws one glyph per accidental in it
    /// — so "the 3rd glyph after the bar line" means different things in the two probes, and
    /// an index that happens to line up today silently stops meaning the same thing the
    /// moment a signature gains or loses an accidental.
    /// </remarks>
    private static bool IsNotehead(char g) =>
        g is EmmentalerGlyphs.NoteheadWhole
          or EmmentalerGlyphs.NoteheadHalf
          or EmmentalerGlyphs.NoteheadBlack
          or EmmentalerGlyphs.NoteheadDoubleWhole;

    /// <summary>
    /// Bar line <paramref name="barIndex"/>'s ink right edge → the first NOTEHEAD after it.
    /// Use when a key or time signature stands between the bar line and the note.
    /// </summary>
    public double BarlineRightToNextNotehead(int barIndex)
    {
        double bar = BarlineRight(barIndex);
        foreach (var g in Glyphs)
            if (g.X > bar + 1e-9 && IsNotehead(g.Glyph))
                return g.X - bar;
        throw new InvalidOperationException(
            $"no notehead is drawn after bar line {barIndex}.\nDrawn geometry:\n" + Describe());
    }

    /// <summary>
    /// The last music glyph's anchor before bar line <paramref name="barIndex"/> → that bar
    /// line's LEFT edge. The closing side of a measure, in the same anchor frame.
    /// </summary>
    public double LastGlyphToBarlineLeft(int barIndex)
    {
        double bar = BarlineLeft(barIndex);
        return bar - LastGlyphBefore(bar).X;
    }

    /// <summary>Diagnostic dump, used in assertion messages so a failure explains itself.</summary>
    public string Describe()
    {
        var events = Glyphs.Select(g => (g.X, Label: $"glyph U+{(int)g.Glyph:X4}"))
            .Concat(Barlines.Select(b => (b.X, Label: $"barline w={b.Width:F3}")))
            .OrderBy(e => e.X);
        return string.Join("\n", events.Select(e => $"    x={e.X,10:F6}  {e.Label}"));
    }
}

internal static class GlyphQueryExtensions
{
    public static DrawnGlyph FirstOrDefault(this IReadOnlyList<DrawnGlyph> glyphs,
                                            Func<DrawnGlyph, bool> predicate, string throw_)
    {
        foreach (var g in glyphs)
            if (predicate(g))
                return g;
        throw new InvalidOperationException(throw_);
    }

    public static DrawnGlyph LastOrDefault(this IReadOnlyList<DrawnGlyph> glyphs,
                                           Func<DrawnGlyph, bool> predicate, string throw_)
    {
        for (int i = glyphs.Count - 1; i >= 0; i--)
            if (predicate(glyphs[i]))
                return glyphs[i];
        throw new InvalidOperationException(throw_);
    }
}
