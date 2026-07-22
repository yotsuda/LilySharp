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

    /// <summary>
    /// Staff lines are horizontal rules of exactly this thickness. Ledger lines are drawn
    /// 0.1 thicker (EngravingDefaults.LegerLineThickness), so the thickness alone separates
    /// them; the span check below is a second, independent guard.
    /// </summary>
    private const double StaffLineThickness = EngravingDefaults.StaffLineThickness;

    /// <summary>A horizontal rule must reach at least this far to count as a staff line.</summary>
    private const double MinStaffLineSpan = 10.0;

    private readonly IReadOnlyList<RecordingDrawingContext> _pages;

    /// <summary>The first page, which is all the X probes look at.</summary>
    private RecordingDrawingContext _page => _pages[0];

    private RenderedGeometry(IReadOnlyList<RecordingDrawingContext> pages) => _pages = pages;

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
        return new RenderedGeometry(doc.Pages);
    }

    // ===================== PAGE VERTICAL =====================
    //
    // Y is DEVICE y-down measured from that page's top paper edge: SharedRenderer.cs:99
    // wraps each page's context in a YFlipDrawingContext, so what the recorder sees has
    // already been mapped out of page-Y-up. That is the same origin the LilyPond probe
    // reports against (scm/page.scm:184-192 places a system at -(Y-offset + top-margin)
    // from the top edge), so the two sides need no further reconciliation.
    //
    // Systems are located by their STAFF LINES, and the quantity taken from each is the
    // MIDDLE line — LilyPond position 0, which is the staff's refpoint and what
    // system-system-spacing actually works against. HANDOFF 5.3: measuring between system
    // ORIGINS instead produces distances that vary system to system even when the spacing
    // is uniform, because staff-refpoint-extent differs (a system carrying a bar number
    // above its staff reaches further up). That mistake is what put "LilyPond compresses to
    // 11.528583" into the handoff for several sessions.

    /// <summary>Number of pages the score paginated onto.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Page height in staff spaces, as the renderer was told to draw it.</summary>
    public double PageHeight(int page = 0) => _pages[page].HeightSpaces;

    /// <summary>Page width in staff spaces, as the renderer was told to draw it.</summary>
    public double PageWidth(int page = 0) => _pages[page].WidthSpaces;

    /// <summary>
    /// Each system's staff refpoint on <paramref name="page"/>, top of the page downwards,
    /// measured from the top paper edge.
    /// </summary>
    /// <remarks>
    /// One entry per STAFF, top of the page down — not per system. That is what makes the
    /// same method serve both regimes, and it is also the trap: on a one-staff score the
    /// consecutive difference is the SYSTEM distance the page's springs decide, while on a
    /// one-system two-staff score it is the STAFF distance Align_interface decides. The
    /// probes are written so that only one of the two is present (probes V/W/S are one
    /// staff and many systems; P/Q are one system and two staves), because a score with
    /// both would need grouping this does not do — and would silently return a brace's
    /// inner gap where the caller asked for a system's.
    /// </remarks>
    public IReadOnlyList<double> StaffRefpoints(int page = 0)
    {
        var ys = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                        && Math.Abs(l.StrokeWidth - StaffLineThickness) < 1e-9
                        && Math.Abs(l.X2 - l.X1) >= MinStaffLineSpan)
            .Select(l => l.Y1)
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        if (ys.Count % 5 != 0)
        {
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff lines, which is not a whole number of "
                + "5-line staves. Either the probe draws a staff with some other line count, "
                + "or the staff-line predicate no longer selects what it used to.");
        }

        var refpoints = new List<double>();
        for (int i = 0; i < ys.Count; i += 5)
        {
            // 1 staff space apart, by definition of a staff space.
            for (int k = 0; k < 4; k++)
            {
                if (Math.Abs(ys[i + k + 1] - ys[i + k] - 1.0) > 1e-6)
                {
                    throw new InvalidOperationException(
                        $"page {page}: staff lines at {ys[i]:F6}.. are not 1 apart, so they "
                        + "are not one staff and the grouping into staves is wrong.");
                }
            }
            refpoints.Add(ys[i + 2]);   // middle line = LilyPond position 0
        }
        return refpoints;
    }

    /// <summary>
    /// Distance from the top paper edge down to the first system's staff refpoint.
    /// </summary>
    /// <remarks>
    /// LilyPond puts this at <c>top-margin + top-system-spacing</c>'s basic-distance when
    /// nothing pushes it further (measured on 2.26.0: 5.690551 + 6.000000).
    /// </remarks>
    public double FirstStaffRefpoint(int page = 0) => StaffRefpoints(page)[0];

    /// <summary>
    /// The single staff-to-staff distance on <paramref name="page"/>.
    /// </summary>
    /// <remarks>
    /// Refpoint to refpoint, which is the frame both LilyPond spacings work in: between
    /// systems that is <c>system-system-spacing</c>, between two staves of one system it is
    /// <c>Align_interface</c>'s translation. Which one a probe reads follows from its shape
    /// — see <see cref="StaffRefpoints"/>.
    ///
    /// Throws when the gaps are not all equal rather than averaging them: a probe that
    /// stretches unevenly is not measuring one spring, and silently returning a mean would
    /// hide exactly the defect the corpus exists to catch.
    /// </remarks>
    public double StaffGap(int page = 0)
    {
        var refs = StaffRefpoints(page);
        if (refs.Count < 2)
        {
            throw new InvalidOperationException(
                $"page {page}: {refs.Count} staff/staves — a staff-to-staff gap needs two.");
        }
        var gaps = Enumerable.Range(0, refs.Count - 1).Select(i => refs[i + 1] - refs[i]).ToList();
        if (gaps.Max() - gaps.Min() > 1e-6)
        {
            throw new InvalidOperationException(
                $"page {page}: gaps are not uniform ({string.Join(", ", gaps.Select(g => g.ToString("F6")))}). "
                + "Measuring one of them would misrepresent the page.");
        }
        return gaps[0];
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

    /// <summary>Notehead anchors, left to right.</summary>
    public IReadOnlyList<DrawnGlyph> Noteheads =>
        Glyphs.Where(g => IsNotehead(g.Glyph)).ToList();

    /// <summary>The <paramref name="index"/>-th notehead's anchor, 0-based, left to right.</summary>
    public double NoteheadAnchor(int index)
    {
        var heads = Noteheads;
        if (index < 0 || index >= heads.Count)
            throw new InvalidOperationException(
                $"wanted notehead #{index} but the probe drew {heads.Count}.\n"
                + "Drawn geometry:\n" + Describe());
        return heads[index].X;
    }

    /// <summary>
    /// The anchor of the first NON-notehead music glyph right of <paramref name="x"/> — the
    /// change glyph of a mid-measure clef or key change.
    /// </summary>
    /// <remarks>
    /// For a key change this is the FIRST accidental of the signature, which is where
    /// LilyPond's KeySignature grob begins too, so the two sides line up without either
    /// having to know how many accidentals the signature contains.
    /// </remarks>
    public double FirstNonNoteheadAfter(double x)
    {
        foreach (var g in Glyphs)
            if (g.X > x + 1e-9 && !IsNotehead(g.Glyph))
                return g.X;
        throw new InvalidOperationException(
            $"no non-notehead glyph is drawn right of x={x:F6}.\nDrawn geometry:\n" + Describe());
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
