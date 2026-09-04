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

    /// <summary>
    /// Parses and lays out <paramref name="source"/>, recording what gets drawn.
    /// </summary>
    /// <param name="options">
    /// Paper to engrave onto; defaults to the product's own <see cref="LayoutOptions.Default"/>.
    /// </param>
    /// <remarks>
    /// The paper is a HARNESS parameter, not a language one, and deliberately so. In
    /// LilyPond the quantities it carries — <c>paper-height</c>, <c>top-system-spacing</c>,
    /// <c>systems-per-page</c> — are <c>\paper</c> variables, NOT grob properties, so a
    /// Lily# probe cannot reach them through <c>override</c> without inventing a spelling
    /// LilyPond does not have. The .ly twin says <c>\paper { … }</c> and the .lys twin says
    /// it here; that is the same kind of documented asymmetry as the octave spelling
    /// (Lily# <c>c</c> = LilyPond <c>c'</c>), and it keeps the corpus able to reach paper
    /// regimes the default page never enters.
    /// </remarks>
    public static RenderedGeometry Render(string source, LayoutOptions? options = null)
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
        var layout = options is null
            ? new LayoutEngine().Layout(score)
            : new LayoutEngine(options).Layout(score);

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
    /// <summary>
    /// The outermost-to-outermost span of the staff LINES on the page, i.e. line centre to
    /// line centre. On a probe with a single staff this is that staff's height.
    /// </summary>
    /// <remarks>
    /// Written for the TAB staff, whose line count is the string count rather than 5, so
    /// <see cref="StaffRefpoints"/>'s "whole number of 5-line staves" check cannot serve.
    /// LilyPond's own quantity is the StaffSymbol's line span: <c>(line-count - 1) *
    /// staff-space</c>, which its Y-extent then widens by half a line thickness at each
    /// edge — measured on 2.26.0 as 7.600000 for a six-string tab staff at staff-space
    /// 1.5, i.e. a 7.5 line span plus 2 × 0.05
    /// (audit/lp-geometry/probes/line-start-mindist.ly, scores CGT and CG4).
    /// </remarks>
    public double StaffLineSpan(int page = 0)
    {
        var ys = StaffLineYs(page);
        if (ys.Count < 2)
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff line(s); a staff span needs at least 2."
                + "\nDrawn geometry:\n" + Describe());
        return ys.Max() - ys.Min();
    }

    /// <summary>
    /// Every drawn STAFF LINE's Y on <paramref name="page"/>, deduplicated and top down —
    /// the one selection every staff reading here shares.
    /// </summary>
    /// <remarks>
    /// Ledger lines are excluded twice over, and deliberately: by THICKNESS (a ledger line is
    /// <see cref="EngravingDefaults.LegerLineThickness"/>, 0.1 thicker than a staff line) and,
    /// independently, by SPAN (a ledger line reaches a notehead's width, a staff line the
    /// system's). A probe whose notes hang below the staff draws plenty of them —
    /// <c>page.tab-control.*</c>'s does — and a grouping that swallowed one would report a
    /// staff where there is none.
    /// </remarks>
    /// <remarks>
    /// ⚠️ THE SPAN IS THE LOGICAL LINE'S, NOT ONE DRAWN SEGMENT'S. A tab string line is
    /// emitted in PIECES — it breaks around every fret digit sitting on it
    /// (SharedRenderer.Tab, DrawTabStringLine) — so testing each drawn rule against
    /// <see cref="MinStaffLineSpan"/> individually dropped whole string lines whose pieces
    /// were each short, and the staff-count guards below then reported "10 staff lines, not
    /// the 5 + 6 this reading is about". Grouping by Y first and measuring the union's reach
    /// asks the question the guard means: how far does this line get across the system.
    /// </remarks>
    /// <remarks>
    /// ⚠️ AND A DASHED RULE IS NEVER A STAFF LINE (2026-08-28). An accel./rit. spanner's
    /// rule is drawn at exactly <see cref="StaffLineThickness"/> and reaches across the
    /// system, so thickness and span BOTH admit it: books TSCR/TSLR put one over a staff and
    /// every reading on the page threw "found 6 staff lines". The dash is the only thing
    /// that tells them apart, which is why <see cref="DrawnLine"/> now carries it.
    /// </remarks>
    /// <remarks>
    /// ⚠️ AND A LONE RULE IS NEVER A STAFF LINE (2026-09-02). A pedal bracket's line is
    /// drawn at exactly <see cref="StaffLineThickness"/>, solid, and reaches across most of
    /// the system, so thickness, dash and span ALL admit it: book PDB (pedal-page.ly) put
    /// one 3.25 below a staff and every reading on the page threw "found 11 staff lines".
    /// What a staff line has that the bracket lacks is a SIBLING one line step away — a
    /// staff space (1.0) on a staff, a string space (1.5) on a tab — so a rule with no
    /// neighbour at either step is dropped. (A bracket sits at least 2.25 below the bottom
    /// line: staff ink 2.05 + spanner padding 1.2 + edge-height 1.0 from the middle.)
    /// </remarks>
    private List<double> StaffLineYs(int page)
    {
        var ys = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                        && !l.IsDashed
                        && Math.Abs(l.StrokeWidth - StaffLineThickness) < 1e-9)
            .GroupBy(l => Math.Round(l.Y1, 9))
            .Where(g => g.Max(l => Math.Max(l.X1, l.X2)) - g.Min(l => Math.Min(l.X1, l.X2))
                        >= MinStaffLineSpan)
            .Select(g => g.Key)
            .OrderBy(y => y)
            .ToList();
        static bool Sibling(double a, double b)
            => Math.Abs(Math.Abs(a - b) - 1.0) <= 1e-6 || Math.Abs(Math.Abs(a - b) - 1.5) <= 1e-6;
        return ys
            .Where((y, i) => (i > 0 && Sibling(ys[i - 1], y)) || (i + 1 < ys.Count && Sibling(y, ys[i + 1])))
            .ToList();
    }

    public IReadOnlyList<double> StaffRefpoints(int page = 0)
    {
        var ys = StaffLineYs(page);

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
                        + "are not one staff and the grouping into staves is wrong."
                        + "\nStaff line Ys: " + string.Join(", ", ys.Select(y => y.ToString("F6")))
                        + "\nDrawn geometry:\n" + Describe());
                }
            }
            refpoints.Add(ys[i + 2]);   // middle line = LilyPond position 0
        }
        return refpoints;
    }

    /// <summary>
    /// The refpoint-to-refpoint distance between two staves whose LINE COUNTS are given —
    /// the reading <see cref="StaffGapAt"/> cannot take, because it assumes five lines each.
    /// </summary>
    /// <remarks>
    /// A TAB staff has one line per string and its lines are 1.5 apart, not 1.0
    /// (<c>EngravingDefaults.TabStringSpace</c>; LilyPond's TabStaff sets
    /// <c>StaffSymbol.staff-space = 1.5</c> for every string count), so a notation staff over
    /// a six-string tab staff draws 11 lines at two different spacings and the five-line
    /// grouping has nothing to say about it.
    /// <para>
    /// ⚠️ THE COUNTS ARE PASSED, NOT INFERRED. Splitting 11 lines into 5 + 6 by looking for a
    /// change of spacing is a heuristic, and a measuring helper that guesses is the thing
    /// HANDOFF 5.4 warns about: it would keep returning a plausible number after the very
    /// defect it exists to measure changed the spacings. Given the counts, everything else is
    /// asserted — the total, and that each staff's own lines really are equally spaced.
    /// </para>
    /// <para>
    /// ⚠️ THE REFPOINT IS THE MIDDLE LINE for both, which is LilyPond position 0 and what
    /// <c>Align_interface</c> measures between. On an even-line staff there is no middle
    /// LINE, so the midpoint of the span is taken; a six-string tab staff's refpoint is
    /// therefore 2.5 lines down, and LilyPond's own dump agrees (its ink below the refpoint is
    /// 3.800000 = half the 7.6 extent).
    /// </para>
    /// </remarks>
    public double StaffRefpointGap(int upperLines, int lowerLines, int page = 0)
    {
        var ys = StaffLineYs(page);

        if (ys.Count != upperLines + lowerLines)
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff lines, not the {upperLines} + "
                + $"{lowerLines} this reading is about."
                + "\nDrawn geometry:\n" + Describe());

        static double Refpoint(List<double> ys, int from, int count, int page)
        {
            double step = ys[from + 1] - ys[from];
            for (int k = 1; k < count - 1; k++)
                if (Math.Abs(ys[from + k + 1] - ys[from + k] - step) > 1e-6)
                    throw new InvalidOperationException(
                        $"page {page}: the staff at {ys[from]:F6} has lines that are not "
                        + "equally spaced, so these are not one staff.");
            return (ys[from] + ys[from + count - 1]) / 2.0;
        }

        return Refpoint(ys, upperLines, lowerLines, page) - Refpoint(ys, 0, upperLines, page);
    }

    /// <summary>
    /// The refpoint-to-refpoint distance down from a staff drawn at
    /// <paramref name="upperScale"/> to a full-size staff below it — the reading
    /// <see cref="StaffRefpointGap(int, int, int)"/> cannot take, because an ossia's staff
    /// lines are not <see cref="StaffLineThickness"/> thick and
    /// <see cref="StaffLineYs"/> does not select them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An ossia is drawn inside a uniform scale group (<c>SharedRenderer.cs:409-412</c>), so
    /// every length it draws — its staff-line THICKNESS along with its staff SPACE — comes
    /// out multiplied by <c>EngravingDefaults.OssiaScale</c>. The shared selection keys on
    /// an exact 0.1, which is what keeps a ledger line out of a staff reading, so it keeps
    /// the ossia out as well. This admits the one extra thickness the caller names.
    /// </para>
    /// <para>
    /// ⚠️ THE SCALE IS PASSED, NOT INFERRED, for the same reason the line COUNTS are in
    /// <see cref="StaffRefpointGap(int, int, int)"/> (HANDOFF 5.4): a helper that recovered
    /// the scale from the drawing would keep returning a plausible number after the defect
    /// it exists to measure had changed it. Given the scale, everything else is asserted —
    /// the total line count, that each staff's own lines are equally spaced, and that the
    /// upper staff's own staff space really is <paramref name="upperScale"/>. That last
    /// assertion is what keeps this pair measuring an OSSIA: LilyPond scales the staff and
    /// not the distance (probe books OSSU / OSSUN), so the staff's own space must go on
    /// being scaled after the distance stops being.
    /// </para>
    /// <para>
    /// ⚠️ Ledger lines stay excluded, and by the same two guards: inside the group a ledger
    /// line is <c>LegerLineThickness * upperScale</c> — 0.141421, not the 0.070710 admitted
    /// here — and it still reaches only a notehead's width.
    /// </para>
    /// </remarks>
    public double ScaledStaffRefpointGap(int upperLines, double upperScale, int lowerLines,
                                         int page = 0)
    {
        double upperThickness = StaffLineThickness * upperScale;
        var ys = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                        && !l.IsDashed
                        && (Math.Abs(l.StrokeWidth - StaffLineThickness) < 1e-9
                            || Math.Abs(l.StrokeWidth - upperThickness) < 1e-9)
                        && Math.Abs(l.X2 - l.X1) >= MinStaffLineSpan)
            .Select(l => l.Y1)
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        if (ys.Count != upperLines + lowerLines)
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff lines, not the {upperLines} + "
                + $"{lowerLines} this reading is about."
                + "\nDrawn geometry:\n" + Describe());

        static double Refpoint(List<double> ys, int from, int count, double space, int page)
        {
            for (int k = 0; k < count - 1; k++)
                if (Math.Abs(ys[from + k + 1] - ys[from + k] - space) > 1e-6)
                    throw new InvalidOperationException(
                        $"page {page}: the staff at {ys[from]:F6} does not have lines "
                        + $"{space:F6} apart, so it is not the staff this reading is about."
                        + "\nStaff line Ys: "
                        + string.Join(", ", ys.Select(y => y.ToString("F6"))));
            return (ys[from] + ys[from + count - 1]) / 2.0;
        }

        return Refpoint(ys, upperLines, lowerLines, 1.0, page)
               - Refpoint(ys, 0, upperLines, upperScale, page);
    }

    /// <summary>
    /// Every staff refpoint on a page whose systems are each an upper staff drawn at
    /// <paramref name="upperScale"/> over a full-size lower one, top of the page downwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The MANY-SYSTEM reading <see cref="ScaledStaffRefpointGap"/> cannot take — that one is
    /// written for a page holding exactly one such pair and asserts the page's whole line
    /// count against it — and the SCALED reading <see cref="StaffRefpoints"/> cannot take,
    /// because an ossia's staff lines are <see cref="StaffLineThickness"/> times the scale and
    /// the shared selection keys on an exact thickness. Books OSSK / OSSKN need both at once:
    /// eight systems to a page, each an ossia over its staff.
    /// </para>
    /// <para>
    /// ⚠️ THE SCALE AND THE COUNTS ARE PASSED, NOT INFERRED, for the reason
    /// <see cref="StaffRefpointGap(int, int, int)"/> gives (HANDOFF 5.4): a helper that
    /// recovered them from the drawing would go on returning a plausible number after the
    /// defect it exists to measure had changed them. Everything else is asserted — the page's
    /// total is a whole number of pairs, and each staff's own lines really are
    /// <paramref name="upperScale"/> and 1.0 apart respectively.
    /// </para>
    /// <para>
    /// ⚠️ THE CONTROL IS READ BY THIS SAME METHOD with <paramref name="upperScale"/> 1.0,
    /// rather than by <see cref="StaffRefpoints"/>. The pair differs in one word and must
    /// differ in one word here too: an instrument used on only one half can move the pair's
    /// difference by itself.
    /// </para>
    /// </remarks>
    public IReadOnlyList<double> ScaledPairRefpoints(
        int upperLines, double upperScale, int lowerLines, int page = 0) =>
        PairRefpoints(upperLines, upperScale, lowerLines, 1.0, page);

    /// <summary>
    /// Every staff refpoint on a page whose systems are each a five-line notation staff over
    /// a TAB staff of <paramref name="tabLines"/> strings — the frame of books STBN / STBK
    /// (probes/staff-tab-page.ly) and of every <c>staff X  tab X</c> book in the bass corpus.
    /// </summary>
    /// <remarks>
    /// The same walk as <see cref="ScaledPairRefpoints"/> with the LOWER staff's line step
    /// passed instead of assumed: a tab's strings are
    /// <see cref="EngravingDefaults.TabStringSpace(int)"/> apart at the staff's own line
    /// thickness, so the ossia reading — which keys the upper staff on a scaled THICKNESS and
    /// the lower on a step of 1.0 — admits the lines and then refuses the tab as "not 1.0
    /// apart". Its refpoint is the midpoint of the span, LilyPond staff position 0 on a
    /// TabStaff too (system.cc:705-717 staff-refpoint-extent), which on five strings is the
    /// middle string.
    /// </remarks>
    public IReadOnlyList<double> StaffTabPairRefpoints(int tabLines, int page = 0) =>
        PairRefpoints(5, 1.0, tabLines, EngravingDefaults.TabStringSpace(tabLines), page);

    /// <summary>
    /// One named gap down a page of staff-over-tab systems: even indices are a system's own
    /// staff-to-tab distance, odd ones the distance from a system's tab to the next system's
    /// staff — <see cref="ScaledPairGapAt"/> for the tab frame.
    /// </summary>
    public double StaffTabPairGapAt(int index, int tabLines, int page = 0)
    {
        var refs = StaffTabPairRefpoints(tabLines, page);
        if (index < 0 || index + 1 >= refs.Count)
            throw new InvalidOperationException(
                $"page {page}: asked for gap {index} but the page holds {refs.Count} staves."
                + "\nDrawn geometry:\n" + Describe());
        return refs[index + 1] - refs[index];
    }

    /// <summary>How many STAVES a page of staff-over-tab systems drew — the count the index reads need.</summary>
    public int StaffTabPairStavesOnPage(int tabLines, int page = 0) =>
        StaffTabPairRefpoints(tabLines, page).Count;

    /// <summary>Top paper edge down to the FIRST staff refpoint of a page of staff-over-tab systems.</summary>
    public double StaffTabPairFirstStaffRefpoint(int tabLines, int page = 0) =>
        StaffTabPairRefpoints(tabLines, page)[0];

    /// <summary>The LAST refpoint (a tab's) on a page of staff-over-tab systems down to the bottom paper edge.</summary>
    public double StaffTabPairLastStaffToFoot(int tabLines, int page = 0) =>
        PageHeight(page) - StaffTabPairRefpoints(tabLines, page)[^1];

    private IReadOnlyList<double> PairRefpoints(
        int upperLines, double upperScale, int lowerLines, double lowerSpace, int page)
    {
        double upperThickness = StaffLineThickness * upperScale;
        // Grouped by Y and judged on the ROW's span, as StaffLineYs does, not segment by
        // segment: a tab string is drawn in pieces around its fret digits
        // (SharedRenderer.Tab.cs DrawTabStringLine), and a row whose every piece is shorter
        // than MinStaffLineSpan vanished from the per-segment reading — book STBN came back
        // with 28 lines for 3 staff-plus-tab systems.
        var ys = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                        && !l.IsDashed
                        && (Math.Abs(l.StrokeWidth - StaffLineThickness) < 1e-9
                            || Math.Abs(l.StrokeWidth - upperThickness) < 1e-9))
            .GroupBy(l => Math.Round(l.Y1, 9))
            .Where(g => g.Max(l => Math.Max(l.X1, l.X2)) - g.Min(l => Math.Min(l.X1, l.X2))
                        >= MinStaffLineSpan)
            .Select(g => g.Key)
            .OrderBy(y => y)
            .ToList();

        int perSystem = upperLines + lowerLines;
        if (ys.Count == 0 || ys.Count % perSystem != 0)
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff lines, which is not a whole number of "
                + $"{upperLines} + {lowerLines} systems. Either a system is spelled some other "
                + "way or the staff-line selection no longer admits the scaled staff."
                + "\nDrawn geometry:\n" + Describe());

        static double Refpoint(List<double> ys, int from, int count, double space, int page)
        {
            for (int k = 0; k < count - 1; k++)
                if (Math.Abs(ys[from + k + 1] - ys[from + k] - space) > 1e-6)
                    throw new InvalidOperationException(
                        $"page {page}: the staff at {ys[from]:F6} does not have lines "
                        + $"{space:F6} apart, so it is not the staff this reading is about."
                        + "\nStaff line Ys: "
                        + string.Join(", ", ys.Select(y => y.ToString("F6"))));
            return (ys[from] + ys[from + count - 1]) / 2.0;
        }

        var refpoints = new List<double>();
        for (int i = 0; i < ys.Count; i += perSystem)
        {
            refpoints.Add(Refpoint(ys, i, upperLines, upperScale, page));
            refpoints.Add(Refpoint(ys, i + upperLines, lowerLines, lowerSpace, page));
        }
        return refpoints;
    }

    /// <summary>
    /// One named gap down a page of scaled pairs: even indices are a system's own upper-to-
    /// lower distance, odd ones the distance from a system's lower staff to the next system's
    /// upper one.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="StaffGapAt"/> for books OSSK / OSSKN, and it does not
    /// check uniformity for the same reason: the caller has asserted by index which gap is the
    /// meaningful one. ⚠️ Pair it with <see cref="ScaledPairStavesOnPage"/> — an index means
    /// the staff it is supposed to mean only while the page holds the staves the probe assumes
    /// (HANDOFF 5.0 trap 8).
    /// </remarks>
    public double ScaledPairGapAt(
        int index, int upperLines, double upperScale, int lowerLines, int page = 0)
    {
        var refs = ScaledPairRefpoints(upperLines, upperScale, lowerLines, page);
        if (index < 0 || index + 1 >= refs.Count)
            throw new InvalidOperationException(
                $"page {page}: asked for gap {index} but the page holds {refs.Count} staves."
                + "\nDrawn geometry:\n" + Describe());
        return refs[index + 1] - refs[index];
    }

    /// <summary>How many STAVES a page of scaled pairs drew — the count the index reads need.</summary>
    public int ScaledPairStavesOnPage(
        int upperLines, double upperScale, int lowerLines, int page = 0) =>
        ScaledPairRefpoints(upperLines, upperScale, lowerLines, page).Count;

    /// <summary>
    /// Top paper edge down to the FIRST staff refpoint of a page of scaled pairs — the head of
    /// the page's spring chain.
    /// </summary>
    /// <remarks>
    /// ⚠️ NOT <see cref="FirstStaffRefpoint"/>, and the difference is the whole point on an
    /// ossia book: the first staff of such a system is the OSSIA, whose lines the ordinary
    /// selection does not admit at all, so that method would silently answer with the staff
    /// UNDER it — a different quantity by the very distance the pair is measuring.
    /// <para>
    /// Carried for the reason the foot reading below is (HANDOFF 5.3): a force is the page's
    /// slack over the chain's total strength, so a fixed term that is wrong at either END
    /// shows up in every gap at once, each scaled by its own spring, and the gaps alone have
    /// nothing to attribute it to.
    /// </para>
    /// </remarks>
    public double ScaledPairFirstStaffRefpoint(
        int upperLines, double upperScale, int lowerLines, int page = 0) =>
        ScaledPairRefpoints(upperLines, upperScale, lowerLines, page)[0];

    /// <summary>
    /// The LAST staff refpoint of a page of scaled pairs down to the bottom paper edge — the
    /// foot of the page's spring chain, the counterpart of <see cref="LastStaffRefpointToFoot"/>.
    /// </summary>
    public double ScaledPairLastStaffToFoot(
        int upperLines, double upperScale, int lowerLines, int page = 0) =>
        PageHeight(page)
        - ScaledPairRefpoints(upperLines, upperScale, lowerLines, page)[^1];

    /// <summary>
    /// Each system's staff refpoint on <paramref name="page"/>, top down, on a page whose
    /// staves all have <paramref name="linesPerStaff"/> lines — the reading
    /// <see cref="StaffRefpoints"/> cannot take, because it assumes five of them one staff
    /// space apart.
    /// </summary>
    /// <remarks>
    /// ONE STAFF PER SYSTEM, like <see cref="StaffRefpoints"/>: the consecutive difference is
    /// then the SYSTEM distance the page's springs decide (see that method for why a score
    /// with both a system distance and a staff-to-staff one cannot be read by index at all).
    /// <para>
    /// ⚠️ THE COUNT IS PASSED, NOT INFERRED, for the reason
    /// <see cref="StaffRefpointGap(int, int, int)"/> gives: a helper that guesses where one
    /// staff ends keeps returning a plausible number after the very defect it exists to
    /// measure has changed the spacing. Everything else is asserted — the total is a whole
    /// number of staves, each staff's own lines are equally spaced, and every staff on the
    /// page has the SAME spacing, so a page that mixes staff kinds says so instead of being
    /// silently cut into groups of <paramref name="linesPerStaff"/>.
    /// </para>
    /// <para>
    /// ⚠️ THE REFPOINT IS THE MIDPOINT OF THE SPAN, which is LilyPond staff position 0 and the
    /// anchor every page spring is written against (<c>top-system-spacing</c> to the first one,
    /// <c>system-system-spacing</c> between them). On a six-string tab staff that is 2.5 lines
    /// down rather than 2 — 3.750000 below the top line where an ordinary staff's refpoint is
    /// 2.000000 below it — and reading such a page in the nominal frame is exactly the defect
    /// <c>page.tab-only.first-staff-refpoint</c> exists to measure.
    /// </para>
    /// </remarks>
    public IReadOnlyList<double> StaffRefpointsOfLineCount(int linesPerStaff, int page = 0)
    {
        if (linesPerStaff < 2)
            throw new ArgumentOutOfRangeException(nameof(linesPerStaff),
                "a staff needs at least two lines for its span to have a midpoint.");

        var ys = StaffLineYs(page);
        if (ys.Count == 0 || ys.Count % linesPerStaff != 0)
        {
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff lines, which is not a whole number of "
                + $"{linesPerStaff}-line staves. Either the probe draws a staff with some "
                + "other line count, or the staff-line predicate no longer selects what it "
                + "used to.\nDrawn geometry:\n" + Describe());
        }

        double step = ys[1] - ys[0];
        var refpoints = new List<double>();
        for (int i = 0; i < ys.Count; i += linesPerStaff)
        {
            for (int k = 0; k < linesPerStaff - 1; k++)
            {
                if (Math.Abs(ys[i + k + 1] - ys[i + k] - step) > 1e-6)
                {
                    throw new InvalidOperationException(
                        $"page {page}: the staff at {ys[i]:F6} has lines {ys[i + k + 1] - ys[i + k]:F6} "
                        + $"apart where the first staff's are {step:F6}, so the page does not "
                        + $"hold {linesPerStaff}-line staves of one kind and this grouping "
                        + "would invent staves that are not there.");
                }
            }
            refpoints.Add((ys[i] + ys[i + linesPerStaff - 1]) / 2.0);
        }
        return refpoints;
    }

    /// <summary>
    /// Distance from the top paper edge down to the first system's staff refpoint, on a page
    /// of <paramref name="linesPerStaff"/>-line staves — <see cref="FirstStaffRefpoint"/>'s
    /// reading for a staff that is not four staff spaces tall.
    /// </summary>
    public double FirstStaffRefpointOfLineCount(int linesPerStaff, int page = 0) =>
        StaffRefpointsOfLineCount(linesPerStaff, page)[0];

    /// <summary>
    /// The single staff-to-staff distance on a page of <paramref name="linesPerStaff"/>-line
    /// staves — <see cref="StaffGap"/>'s reading for a staff that is not four staff spaces
    /// tall, and it refuses a non-uniform page for the same reason that one does.
    /// </summary>
    public double StaffGapOfLineCount(int linesPerStaff, int page = 0)
    {
        var refs = StaffRefpointsOfLineCount(linesPerStaff, page);
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

    /// <summary>
    /// Bar numbers, top of the page down — the small BOLD serif text runs
    /// (<c>SharedRenderer.DrawBarNumbers</c> draws them at
    /// <see cref="LilySharp.Core.Svg.Layout.BarNumberEngraver.FontSize"/>).
    /// </summary>
    /// <remarks>
    /// Told apart from the other serif text by SIZE, the same way
    /// <see cref="LyricSyllables"/> is: a bar number, a syllable, a title and a dynamic are
    /// all serif, and matching on the STRING cannot work when the string is a number.
    /// </remarks>
    public IReadOnlyList<DrawnText> BarNumbers =>
        Texts.Where(t => t.Role != TextRole.ChordName
                         && Math.Abs(t.FontSize - LilySharp.Core.Svg.Layout.BarNumberEngraver.FontSize) < 1e-9)
             .OrderBy(t => t.Y)
             .ToList();

    /// <summary>
    /// The FIRST bar number's baseline above the staff reference point it rides over.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarNumber — <c>outside-staff-priority 100</c> and
    /// <c>padding 1.0</c>, placed by the X-AWARE skyline pass
    /// (lily/axis-group-interface.cc:359-474). The number stands at the LINE START, left of
    /// the clef, so the notes — which begin after it — cannot push it up however high they
    /// are. That invariance is the whole of the pair this serves (books BNL/BNH): LilyPond's
    /// two readings are IDENTICAL, so any difference Lily# shows between them is its own.
    /// <para>
    /// ⚠️ Bar 1 carries no number, so the topmost one belongs to the SECOND system, and the
    /// reference point it is measured against is that system's — the first refpoint BELOW
    /// it, which is what the search here finds.
    /// </para>
    /// <para>
    /// ⚠️ CARRIES A FONT TERM, small and named: LilyPond puts the number's INK BOTTOM
    /// 1.0 above the staff (2.050000 + 1.0 = 3.050000 for a flat-bottomed digit), so the
    /// BASELINE this reads sits a digit's own bottom overshoot higher — measured across
    /// LilyPond's own books as 0.000000, 0.024440 and 0.026208 depending on which digits the
    /// number happens to contain. A residual of that order is the floor of this entry, not a
    /// defect; the defect it was opened for is over a staff space.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The FIRST bar number's INK BOTTOM above the staff reference point — the
    /// font-free spelling of <see cref="FirstBarNumberBaselineAboveStaff"/>: LilyPond
    /// puts this at staff ink 2.05 + BarNumber padding 1.0 = 3.050000 for EVERY digit,
    /// so the two engines' line breaking is free to pick different numerals without the
    /// entry acquiring an overshoot term.
    /// </summary>
    public double FirstBarNumberInkBottomAboveStaff(int page = 0)
    {
        var t = BarNumbers.Count > 0 ? BarNumbers[0]
            : throw new InvalidOperationException(
                $"page {page}: the probe drew no bar number.\nDrawn geometry:\n" + Describe());
        var ink = LilySharp.Core.Rendering.TextFontMetrics.Ink(
            t.Text, t.FontSize, sans: false, LilySharp.Core.Rendering.FontStyle.Bold);
        // ink.Bottom is the digit's own overshoot BELOW the baseline (<= 0), so the ink
        // bottom sits that much lower than the baseline the other reading returns.
        return FirstBarNumberBaselineAboveStaff(page) + ink.Bottom;
    }

    /// <summary>
    /// The FIRST bar number's INK BOTTOM above the LYRIC ROW's reference point — the reading
    /// for a system with no staff at all, where <see cref="FirstBarNumberInkBottomAboveStaff"/>
    /// has nothing to measure against.
    /// </summary>
    /// <remarks>
    /// A Lyrics VerticalAxisGroup's reference point IS the syllable baseline — LilyPond has
    /// no band above it — so the datum here is the topmost syllable baseline BELOW the
    /// number, i.e. the first verse of the system the number belongs to.
    /// LILYPOND-REF: lily/side-position-interface.cc:347-370 aligned_side — with an empty
    /// support set the `dim.is_empty ()' branch stands a FLAT SKYLINE AT HEIGHT 0 at that
    /// reference point, and the number's ink bottom lands exactly `padding' above it:
    /// 1.000000, measured on 2.26.0 in book SLP of probes/barnumber-staffless.ly, which
    /// switches the outside-staff pass off so this stage is read on its own.
    /// <para>
    /// ⚠️ IT IS THE STAGE-ONE READING, DELIBERATELY. The number's final position in a real
    /// LilyPond book is that plus an outside-staff translation whose size depends on the
    /// GLYPH OUTLINE of whatever ink is nearest the number's column (0.104576 + 0.46 on book
    /// SLN) — a quantity that changes with the syllable, so pinning it would pin the word.
    /// Lily#'s own stage two contributes zero here, because a text row carries no ink
    /// profile for the pass to work against, so this reading IS Lily#'s whole answer.
    /// </para>
    /// </remarks>
    public double FirstBarNumberInkBottomAboveLyricRow(int page = 0)
    {
        var numbers = BarNumbers;
        if (numbers.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: the probe drew no bar number.\nDrawn geometry:\n" + Describe());
        }
        var t = numbers[0];
        var below = LyricSyllables.Select(s => s.Y).Where(r => r > t.Y).ToList();
        if (below.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: the first bar number at {t.Y:F6} has no lyric row below it, so "
                + "it is not riding over one.\nDrawn geometry:\n" + Describe());
        }
        var ink = LilySharp.Core.Rendering.TextFontMetrics.Ink(
            t.Text, t.FontSize, sans: false, LilySharp.Core.Rendering.FontStyle.Bold);
        // ink.Bottom is the digit's own overshoot BELOW the baseline (<= 0), the same term
        // FirstBarNumberInkBottomAboveStaff removes, so the entry carries no font dependency.
        return (below.Min() - t.Y) + ink.Bottom;
    }

    public double FirstBarNumberBaselineAboveStaff(int page = 0)
    {
        var numbers = BarNumbers;
        if (numbers.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: the probe drew no bar number.\nDrawn geometry:\n" + Describe());
        }
        double y = numbers[0].Y;
        var below = StaffRefpoints(page).Where(r => r > y).ToList();
        if (below.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: the first bar number at {y:F6} has no staff below it, so it "
                + "is not riding over one.\nDrawn geometry:\n" + Describe());
        }
        return below.Min() - y;
    }

    /// <summary>
    /// Boxed section labels and rehearsal marks, top of the page down
    /// (<c>SharedRenderer.DrawSingleMusicMark</c> draws them in
    /// <see cref="TextRole.Mark"/>).
    /// </summary>
    public IReadOnlyList<DrawnText> MusicMarkLabels =>
        Texts.Where(t => t.Role == TextRole.Mark).OrderBy(t => t.Y).ToList();

    /// <summary>
    /// The drawn BOX's left edge of the boxed mark reading <paramref name="label"/>,
    /// measured FROM ITS OWN SYSTEM'S CLEF LEFT — the quantity LilyPond's
    /// break-alignable-interface places: the mark's refpoint (its stencil's X origin,
    /// which for a boxed markup is the box's left edge) lands on the aligned grob's
    /// <c>break-align-anchor</c> (lily/break-alignment-interface.cc:337-353
    /// self_align_callback). Relational on purpose: LilyPond's probe X is
    /// system-relative and Lily#'s device X carries the first line's indent, and the
    /// clef's left ink is the one column both frames share (the prefix opens
    /// LeftEdge→Clef with extra-space 0.8 in both engines), so the pair compares the
    /// same distance without an indent conversion on either side.
    /// </summary>
    /// <remarks>
    /// The box is read from the drawn RECT, not reconstructed from the centred text
    /// plus a width formula — a reading that reused the engraver's own box arithmetic
    /// would be an identity, and this entry's job is to price the drawn frame against
    /// LilyPond's. The rect is found by containment of the label's anchor point; the
    /// clef is the leftmost clef glyph within the mark's own system (the staff below
    /// the label). Exactly one box must contain the text or the reading refuses.
    /// </remarks>
    public double MusicMarkBoxLeftFromClefLeft(string label, int page = 0)
    {
        var texts = _pages[page].Texts
            .Where(t => t.Role == TextRole.Mark && t.Text == label).ToList();
        if (texts.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected ONE boxed mark reading \"{label}\", found "
                + $"{texts.Count}.\nDrawn geometry:\n" + Describe());
        }
        var t = texts[0];
        var boxes = _pages[page].Rects
            .Where(r => r.X <= t.X && t.X <= r.X + r.Width
                        && r.Y <= t.Y && t.Y <= r.Y + r.Height).ToList();
        if (boxes.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: the mark \"{label}\" at ({t.X:F3},{t.Y:F3}) sits in "
                + $"{boxes.Count} rect(s) — the reading cannot name its box.\n"
                + "Drawn geometry:\n" + Describe());
        }
        // The mark's system's clef: the leftmost clef glyph whose Y sits below the
        // label within one system's height (the label rides over its own top staff).
        var clefs = _pages[page].Glyphs
            .Where(g => g.Glyph is LilySharp.Core.Svg.EmmentalerGlyphs.GClef
                            or LilySharp.Core.Svg.EmmentalerGlyphs.FClef
                            or LilySharp.Core.Svg.EmmentalerGlyphs.CClef
                        && g.Y > t.Y && g.Y < t.Y + 12.0)
            .OrderBy(g => g.X).ToList();
        if (clefs.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: no clef glyph under the mark \"{label}\" — the reading "
                + "has no anchor column to relate to.\nDrawn geometry:\n" + Describe());
        }
        return boxes[0].X - clefs[0].X;
    }

    /// <summary>
    /// The boxed mark reading <paramref name="label"/>: its drawn BOX's centre minus the
    /// <c>break-align-anchor</c> of the bar line nearest it — the centre of that bar line's
    /// STROKES, repeat dots excluded. Mid-line LilyPond centres a RehearsalMark on exactly
    /// that anchor (scm/bar-line.scm:812-852 ly:bar-line::calc-anchor, span-bar-glyph-alist;
    /// RehearsalMark's self-alignment-X is the opposite of the bar's CENTER anchor
    /// alignment), so the number to read is 0.
    /// </summary>
    /// <remarks>
    /// The bar line is read from the drawn strokes: every rect no wider than a thick
    /// stroke, grouped into one bar line where the gaps are under one staff space (the
    /// same grouping <see cref="BarlineGroups"/> uses, but thick strokes included — a
    /// <c>.|:</c> opens with one). Repeat dots are circles, not rects, so they fall out of
    /// the reading by themselves, which is what makes the group's centre LilyPond's
    /// span-bar centre. The nearest group to the box's centre is the mark's bar.
    /// </remarks>
    public double MusicMarkBoxCenterFromBarlineAnchor(string label, int page = 0)
    {
        var texts = _pages[page].Texts
            .Where(t => t.Role == TextRole.Mark && t.Text == label).ToList();
        if (texts.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected ONE boxed mark reading \"{label}\", found "
                + $"{texts.Count}.\nDrawn geometry:\n" + Describe());
        }
        var t = texts[0];
        var boxes = _pages[page].Rects
            .Where(r => r.X <= t.X && t.X <= r.X + r.Width
                        && r.Y <= t.Y && t.Y <= r.Y + r.Height).ToList();
        if (boxes.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: the mark \"{label}\" at ({t.X:F3},{t.Y:F3}) sits in "
                + $"{boxes.Count} rect(s) — the reading cannot name its box.\n"
                + "Drawn geometry:\n" + Describe());
        }
        double boxCenter = boxes[0].X + boxes[0].Width / 2;

        // Bar-line strokes on the staff under the mark: thin or thick, taller than they
        // are wide, below the label within one system's height.
        var strokes = _pages[page].Rects
            .Where(r => r.Width > 0 && r.Width <= EngravingDefaults.ThickBarlineThickness + 1e-6
                        && r.Height > r.Width && r.Y > t.Y && r.Y < t.Y + 12.0)
            .OrderBy(r => r.X).ToList();
        var groups = new List<DrawnRect>();
        foreach (var r in strokes)
        {
            if (groups.Count > 0)
            {
                var last = groups[^1];
                if (r.X - (last.X + last.Width) < MaxBarlineStrokeGap)
                {
                    groups[^1] = last with { Width = r.X + r.Width - last.X };
                    continue;
                }
            }
            groups.Add(r);
        }
        if (groups.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: no bar-line stroke under the mark \"{label}\".\nDrawn geometry:\n"
                + Describe());
        }
        var bar = groups.OrderBy(g => Math.Abs(g.X + g.Width / 2 - boxCenter)).First();
        return boxCenter - (bar.X + bar.Width / 2);
    }

    /// <summary>
    /// The FIRST boxed mark's BASELINE above the staff reference point it rides over.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm RehearsalMark — <c>outside-staff-priority 1500</c>
    /// and <c>padding 0.8</c>. LilyPond draws the letter ON its baseline (measured extent
    /// bottom 0.0 in probes/mark-chord-row.ly), so LP's baseline IS its ink bottom and the
    /// two engines are compared on the same datum here.
    /// <para>
    /// ⚠️ THE PAIR IS THE POINT, NOT THE ABSOLUTE. The two books differ in ONE thing —
    /// whether a ChordNames line leads the system — and LilyPond reads the same number on
    /// both: a mark's Y-parent is a STAFF's axis group, and a ChordNames context is not
    /// one, so the row does not move it. Any difference Lily# shows BETWEEN the two books
    /// is its own, and it was 4.400000 until session 243.
    /// </para>
    /// <para>
    /// ⚠️⚠️ AND THE GROB HAS TO MATCH, WHICH IT DID NOT WHEN THIS WAS WRITTEN. Lily# draws
    /// a <c>form</c> section name as a SECTION LABEL, and in LilyPond a SectionLabel and a
    /// RehearsalMark are different grobs with different anchors — SectionLabel
    /// <c>(break-align-symbols . (left-edge staff-bar))</c>, <c>self-alignment-X</c> LEFT,
    /// priority 1450; RehearsalMark <c>(staff-bar key-signature clef)</c>, priority 1500.
    /// The first stands at the LEFT EDGE (measured X 0.000, probe book MKB), the second
    /// AFTER the clef (X 3.365, and 6.385 once a key signature is added — book MKK). These
    /// entries first quoted the REHEARSAL mark's 2.850000, which made Lily# look 1.810 too
    /// high and pointed a session at porting <c>break-alignable-interface</c> — a port that
    /// would have moved the section label AWAY from LilyPond. The value is book MKB's
    /// section label now, and the residual is 0.542971.
    /// ⇒ ★★ When a ledger entry compares "the mark", name WHICH GROB on both sides. Two
    /// grobs that draw the same-looking box can have different anchors, and the arithmetic
    /// closes either way.
    /// </para>
    /// </remarks>
    public double FirstMusicMarkBaselineAboveStaff(int page = 0)
    {
        var marks = MusicMarkLabels;
        if (marks.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: the probe drew no section label.\nDrawn geometry:\n" + Describe());
        }
        double y = marks[0].Y;
        var below = StaffRefpoints(page).Where(r => r > y).ToList();
        if (below.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: the first mark at {y:F6} has no staff below it, so it is not "
                + "riding over one.\nDrawn geometry:\n" + Describe());
        }
        return below.Min() - y;
    }

    /// <summary>
    /// Custom texts (<c>_"..."</c>), top of the page down — the serif runs at
    /// <c>EngravingDefaults.TextScriptFontSize</c>, the size <c>DrawCustomTexts</c> draws
    /// them at.
    /// </summary>
    /// <remarks>
    /// ⚠️ Told apart by SIZE alone, like <see cref="BarNumbers"/>: <c>DrawnText</c> records
    /// no font style. A REHEARSAL mark's boxed letter is serif at 2.4 — distinct from the
    /// script's 2.2 only since the em port — so the textscript-ink probes still suppress
    /// every section label with <c>~Name</c>: a fixture that lets a mark through should
    /// fail loudly in the count guard, not depend on a 0.2 size gap.
    /// </remarks>
    public IReadOnlyList<DrawnText> CustomTexts =>
        Texts.Where(t => t.Role != TextRole.ChordName
                         && Math.Abs(t.FontSize
                                     - LilySharp.Core.Svg.EngravingDefaults.TextScriptFontSize) < 1e-9)
             .OrderBy(t => t.Y)
             .ToList();

    /// <summary>
    /// The ONLY custom text's baseline above the staff reference point it rides over.
    /// </summary>
    /// <remarks>
    /// The text-script half of <see cref="FirstBarNumberBaselineAboveStaff"/>, for the
    /// textscript-ink pair (books TXD/TXP). LilyPond puts a TextScript's baseline at
    /// <c>max(staff ink + staff-padding, staff ink + outside-staff-padding + descent)</c> —
    /// the first term to the REFPOINT (lily/side-position-interface.cc:401-453 aligned_side,
    /// "Ensure 'staff-padding' from my refpoint to the staff"), the second to the INK BOTTOM
    /// (lily/axis-group-interface.cc:739-806 add_grobs_of_one_priority) — so the reading
    /// moves with the string's own descender. A stacker that prices descent as a flat
    /// fraction of the em reads the SAME number for "dolce" and "poco", and that identity
    /// is what the pair is built to catch.
    /// </remarks>
    public double SoleCustomTextBaselineAboveStaff(int page = 0)
    {
        var texts = CustomTexts;
        if (texts.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE custom text, found {texts.Count} — the "
                + "probe is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        double y = texts[0].Y;
        var below = StaffRefpoints(page).Where(r => r > y).ToList();
        if (below.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: the custom text at {y:F6} has no staff below it, so it is "
                + "not riding over one.\nDrawn geometry:\n" + Describe());
        }
        return below.Min() - y;
    }

    /// <summary>
    /// The sole custom text's drawn PEN ORIGIN minus the
    /// <paramref name="noteheadIndex"/>-th notehead's anchor — the TextScript X pair
    /// (books TXD/TXP). LILYPOND-REF: lily/self-alignment-interface.cc:143-175 aligned_on_parent
    /// — TextScript's self-alignment-X and parent-alignment-X are both
    /// #f, so NEITHER term applies and the X-offset is 0: the stencil's pen origin sits
    /// exactly on the anchor note column's origin (its head's left edge). MEASURED
    /// (textscript-ink.ly dump, 2026-07-29): the script's x-left equals the fifth
    /// notehead's x-left at 21.650925710824165, to 15 digits, for every string.
    /// </summary>
    /// <remarks>
    /// The pen origin is derived from the DRAWN anchor: a Start-anchored text starts at
    /// its X, a Middle-anchored one half an advance earlier — so the entry reads the
    /// drawn geometry whichever way the draw is anchored, and a centred draw shows up as
    /// the half-advance it is off by, not as a harness artifact. The advance is measured
    /// at the italic style DrawCustomTexts uses (DrawnText records no style).
    /// </remarks>
    public double SoleCustomTextPenToNotehead(int noteheadIndex, int page = 0)
    {
        var texts = CustomTexts;
        if (texts.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE custom text, found {texts.Count} — the "
                + "probe is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        var t = texts[0];
        double advance = LilySharp.Core.Rendering.TextFontMetrics.Advance(
            t.Text, t.FontSize, sans: false, LilySharp.Core.Rendering.FontStyle.Italic);
        double pen = t.Anchor switch
        {
            LilySharp.Core.Rendering.TextAnchor.Middle => t.X - advance / 2,
            LilySharp.Core.Rendering.TextAnchor.End => t.X - advance,
            _ => t.X,
        };
        return pen - NoteheadAnchor(noteheadIndex);
    }

    /// <summary>
    /// How wide the sole custom text is RESERVED to be — the string's advance at the face,
    /// size and style it was drawn with, which is the number Lily# spends wherever text
    /// takes horizontal room (<c>TextFontMetrics.Advance</c>: marks, chord names, lyrics,
    /// metronome marks, the ottava's line gap).
    /// </summary>
    /// <remarks>
    /// The one entry family in this file that reads a METRIC rather than a distance between
    /// two drawn anchors, and it is deliberate — see audit/lp-geometry/probes/text-advance.ly
    /// for the argument and the measurements. LilyPond's counterpart is the text grob's
    /// X-extent, which is the SHAPED ADVANCE and not the ink:
    /// <c>lily/pango-font.cc:351-362 Pango_font::pango_item_string_stencil</c> takes the
    /// box's X from Pango's LOGICAL rectangle
    /// (and only its Y from the ink one), so the left edge is the pen origin — which the
    /// <c>textscript.x.pen-to-notehead-left</c> pair already pins from the other side, for
    /// two strings whose first glyphs have different side bearings.
    /// <para>
    /// ⚠️ The style is <c>Italic</c> because <c>DrawCustomTexts</c> draws <c>_"text"</c>
    /// italic and <c>DrawnText</c> records no style — the same assumption
    /// <see cref="SoleCustomTextPenToNotehead"/> makes, and the .ly twin's
    /// <c>^\markup \italic</c> is what makes it true on the other side.
    /// </para>
    /// </remarks>
    public double SoleCustomTextReservedWidth(int page = 0)
    {
        var texts = CustomTexts;
        if (texts.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE custom text, found {texts.Count} — the "
                + "probe is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        var t = texts[0];
        return LilySharp.Core.Rendering.TextFontMetrics.Advance(
            t.Text, t.FontSize, sans: false, LilySharp.Core.Rendering.FontStyle.Italic);
    }

    /// <summary>
    /// How wide the sole PLAIN-TEXT MUSIC MARK of <paramref name="type"/> is — the
    /// navigation/pedal counterpart of <see cref="SoleCustomTextReservedWidth"/>, and the
    /// Lily# side of <c>audit/lp-geometry/probes/jump-mark-em.ly</c>. LilyPond's
    /// counterpart is the <c>JumpScript</c> / <c>SostenutoPedal</c> grob's X-extent, which
    /// is the shaped ADVANCE (lily/pango-font.cc:351-362 <c>pango_item_string_stencil</c>),
    /// i.e. the same quantity in the same units.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY term comes from the engraver's ONE HOME, on purpose: the em from the DRAWN
    /// text (<c>SharedRenderer.Marks</c> draws at
    /// <c>MusicMarkEngraver.PlainTextFontSize</c>), the style from
    /// <c>MusicMarkEngraver.TextStyleOf</c>, the role from
    /// <c>MusicMarkEngraver.TextRoleOf</c>. A reading that spelled 2.8 or
    /// <c>BoldItalic</c> here would still say 2.8 and BoldItalic on the day somebody ports
    /// the size or the slant, and the ledger entries it feeds would sit at their baselines
    /// straight through the port — a net that cannot fire (HANDOFF §5.2.1⑥). Written this
    /// way the residual moves the moment either home moves, which is why the entry is
    /// opened BEFORE the port rather than after it.
    /// </para>
    /// <para>
    /// ⚠️ The filter is role AND em, never the string: a section label, a title and a
    /// lyric are serif too, and <see cref="DrawnText"/> records no style. The count guard
    /// then fails LOUDLY rather than measuring the wrong run — the probes carry no title
    /// and reference their sections as <c>~Name</c> for the same reason the text-script
    /// books do.
    /// </para>
    /// <para>
    /// ⚠️ No <c>page</c> parameter, unlike its neighbours: <see cref="Texts"/> reads the
    /// one recorded page this type holds, so a page argument would be a dead one that
    /// <c>grep</c> reads as live (HANDOFF §5.2.1⑥ again — the other half of the same rule).
    /// </para>
    /// </remarks>
    /// <param name="type">The mark whose em, style and role the reading is taken at — all
    /// three from <c>MusicMarkEngraver</c>, none of them spelled here.</param>
    /// <param name="text">The string to select when the role alone leaves more than one run
    /// standing. A sustain pedal in <c>pedal text</c> style draws BOTH its engage word and
    /// its release star at the same role and the same em, and LilyPond engraves two
    /// <c>SustainPedal</c> grobs for the same reason — so the pair has to name which of them
    /// it prices. ⚠️ A STRING is safe to spell here and a size or a style would not be: this
    /// argument cannot make the entry sit still through a port of either (the two that can
    /// are read from their homes above), and if the drawn word ever changes the count guard
    /// fails loudly rather than measuring the other run.</param>
    public double SoleMusicMarkReservedWidth(
        LilySharp.Core.Svg.Model.MusicMarkType type, string? text = null)
    {
        // THE SUSTAIN PEDAL IS NOT DRAWN AS TEXT (lily/sustain-pedal.cc:47-76 is a glyph
        // run, and Lily# ports it), so the reading follows the mechanism rather than the
        // family: find the drawn glyph run and measure it from the first glyph's origin to
        // the last one's right edge. ⚠️ It is measured off the PAGE, not asked of
        // SustainPedalStencil — a reading that asked the builder for its own answer would
        // be an identity, and this entry's whole job is to price the drawn word against
        // LilyPond's.
        if (MusicMarkEngraver.IsGlyphPedal(type))
            return SoleSustainPedalDrawnWidth(text);

        var role = MusicMarkEngraver.TextRoleOf(type);
        var marks = Texts
            .Where(t => t.Role == role
                        && Math.Abs(t.FontSize - MusicMarkEngraver.PlainTextFontSize) < 1e-9
                        && (text is null || t.Text == text))
            .ToList();
        if (marks.Count != 1)
        {
            throw new InvalidOperationException(
                $"expected exactly ONE plain-text mark of role {role} at em "
                + $"{MusicMarkEngraver.PlainTextFontSize}"
                + (text is null ? "" : $" reading \"{text}\"")
                + $", found {marks.Count} — the probe "
                + "is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        var mark = marks[0];
        return LilySharp.Core.Rendering.TextFontMetrics.Advance(
            mark.Text, mark.FontSize, sans: false, MusicMarkEngraver.TextStyleOf(type));
    }

    /// <summary>
    /// The drawn width of the sustain pedal's glyph word — first glyph's origin to the last
    /// glyph's right edge, both read off the page.
    /// </summary>
    /// <remarks>
    /// The run is selected by GLYPH, which is what the mechanism gives: the three pedal
    /// glyphs appear nowhere else on a page, so no size or role filter is needed and none
    /// is invented. <paramref name="word"/> picks WHICH run when a book draws both the
    /// engage word and the release star (LilyPond engraves two SustainPedal grobs for the
    /// same reason); it is a string of the source, not of the drawing, so it is turned into
    /// the glyph sequence by the same one home the renderer uses.
    /// </remarks>
    private double SoleSustainPedalDrawnWidth(string? word)
    {
        var run = SoleSustainPedalRun(word);
        return run[^1].X + MusicMarkEngraver.PedalGlyphBox(run[^1].Glyph).Width - run[0].X;
    }

    /// <summary>
    /// The BASELINE of the sole drawn sustain-pedal glyph word spelling
    /// <paramref name="word"/>, below the staff reference point above it.
    /// </summary>
    public double PedalGlyphWordBaselineBelowStaff(string word, int page = 0)
    {
        var run = SoleSustainPedalRun(word);
        double y = run[0].Y;
        var above = StaffRefpoints(page).Where(r => r < y).ToList();
        if (above.Count == 0)
            throw new InvalidOperationException(
                $"page {page}: the pedal word \"{word}\" at {y:F6} has no staff above it."
                + "\nDrawn geometry:\n" + Describe());
        return y - above.Max();
    }

    /// <summary>
    /// The BASELINE of the sole drawn TEXT pedal row (a sostenuto / una corda word)
    /// spelling <paramref name="text"/>, below the staff reference point above it.
    /// </summary>
    public double PedalTextRowBaselineBelowStaff(string text, int page = 0)
    {
        var rows = Texts.Where(t => t.Text == text).ToList();
        if (rows.Count != 1)
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE drawn text \"{text}\", found "
                + $"{rows.Count}.\nDrawn geometry:\n" + Describe());
        double y = rows[0].Y;
        var above = StaffRefpoints(page).Where(r => r < y).ToList();
        if (above.Count == 0)
            throw new InvalidOperationException(
                $"page {page}: the pedal text \"{text}\" at {y:F6} has no staff above it."
                + "\nDrawn geometry:\n" + Describe());
        return y - above.Max();
    }

    /// <summary>
    /// The sole horizontal pedal-bracket LINE below the first staff, measured from that
    /// staff's reference point — the drawn mirror of the PianoPedalBracket grob's own
    /// relative coordinate (the hooks rise from it; DrawPedalBrackets puts the line at
    /// PedalBracketLayout.Y).
    /// </summary>
    /// <remarks>
    /// Identified as a horizontal rule of staff-line thickness BELOW the bottom staff
    /// line that is not a staff line (it starts to the right of the system's left edge)
    /// and is at least 1 ss long — a lyric EXTENDER satisfies none of the probes that use
    /// this (their melismas are none), and the count guard fails loudly if a second such
    /// rule appears down there.
    /// </remarks>
    public double PedalBracketLineBelowStaff(int page = 0)
    {
        double staffBottomEdge = StaffRefpoints(page).Min() + 2.0;
        var rules = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                && Math.Abs(l.StrokeWidth - StaffLineThickness) < 1e-9
                && Math.Abs(l.X2 - l.X1) >= 1.0
                && l.Y1 > staffBottomEdge)
            .Select(l => l.Y1).Distinct().ToList();
        if (rules.Count != 1)
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE pedal bracket line below the staff, "
                + $"found {rules.Count}.\nDrawn geometry:\n" + Describe());
        var above = StaffRefpoints(page).Where(r => r < rules[0]).ToList();
        return rules[0] - above.Max();
    }

    /// <summary>
    /// The sole hairpin's WEDGE CENTRE below the staff's middle line — the drawn mirror of
    /// the Hairpin grob's own reference point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CENTRE AGAINST CENTRE, deliberately. LilyPond's <c>lily/hairpin.cc</c> builds the
    /// stencil symmetrically about its own Y=0 (its grob Y-extent prints as ±0.7166 = the
    /// declared <c>height</c> 0.6666 plus half the 0.1 line), and
    /// <c>SharedRenderer.DrawHairpins</c> draws its two arms symmetrically about
    /// <c>HairpinLayout.YUp</c>. Reading either engine's arms would put half a line weight
    /// into the residual wearing the placement's clothes — the same slip
    /// <see cref="VoltaLineRaw"/> records having made once.
    /// </para>
    /// <para>
    /// SELECTED BY POSITION, not by weight: the wedge's two arms are the only rules these
    /// books draw below the bottom staff line (whole notes, so no stems and no ledgers; the
    /// fermata is a glyph). The count guard fails loudly if a book grows a second wedge, a
    /// pedal bracket or a lyric extender down there, and the symmetry guard fails if the two
    /// rules found are not in fact one wedge's mirrored arms.
    /// </para>
    /// </remarks>
    public double HairpinWedgeCentreBelowStaff(int page = 0)
    {
        var staffLines = StaffLineYs(page);
        if (staffLines.Count != 5)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE 5-line staff, found {staffLines.Count} "
                + "staff line(s) — the probe is not measuring what it claims."
                + "\nDrawn geometry:\n" + Describe());
        }
        double staffMiddle = staffLines[2];
        double staffBottom = staffLines[4];
        var arms = _pages[page].Lines
            .Where(l => !l.IsDashed
                && Math.Abs(l.StrokeWidth - StaffLineThickness) < 1e-9
                && Math.Abs(l.X2 - l.X1) >= 1.0
                && l.Y1 > staffBottom + 1e-6 && l.Y2 > staffBottom + 1e-6)
            .ToList();
        if (arms.Count != 2)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly TWO rules below the staff (one hairpin's two "
                + $"arms), found {arms.Count} — the probe is not measuring what it claims."
                + "\nDrawn geometry:\n" + Describe());
        }
        if (Math.Abs(arms[0].X1 - arms[1].X1) > 1e-9 || Math.Abs(arms[0].X2 - arms[1].X2) > 1e-9)
        {
            throw new InvalidOperationException(
                $"page {page}: the two rules below the staff do not span the same X — they "
                + "are not one wedge's arms.\nDrawn geometry:\n" + Describe());
        }
        double atLeft = (arms[0].Y1 + arms[1].Y1) / 2.0;
        double atRight = (arms[0].Y2 + arms[1].Y2) / 2.0;
        if (Math.Abs(atLeft - atRight) > 1e-9)
        {
            throw new InvalidOperationException(
                $"page {page}: the wedge's arms are not mirrored about one centre "
                + $"({atLeft:F9} at its left end, {atRight:F9} at its right) — the reading of "
                + "its centre is not well defined.\nDrawn geometry:\n" + Describe());
        }
        return atLeft - staffMiddle;
    }

    /// <summary>The sole drawn pedal glyph run spelling <paramref name="word"/> — the
    /// run-splitting half shared by the width and baseline readings.</summary>
    private List<DrawnGlyph> SoleSustainPedalRun(string? word)
    {
        var wanted = word is null
            ? null
            : MusicMarkEngraver.SustainPedalStencil(word).Glyphs.Select(g => g.Glyph).ToList();
        var pedalGlyphs = Glyphs
            .Where(g => g.Glyph is EmmentalerGlyphs.PedalPed or EmmentalerGlyphs.PedalDot
                                or EmmentalerGlyphs.PedalStar)
            .ToList();
        // Split the drawn glyphs into runs: a new run starts wherever the next glyph does
        // not sit exactly on the previous one's right edge, which is how LilyPond's
        // add_at_edge builds one and how the renderer lays it out.
        var runs = new List<List<DrawnGlyph>>();
        foreach (var g in pedalGlyphs)
        {
            if (runs.Count > 0)
            {
                var prev = runs[^1][^1];
                double edge = prev.X + MusicMarkEngraver.PedalGlyphBox(prev.Glyph).Width;
                if (Math.Abs(g.X - edge) < 1e-6) { runs[^1].Add(g); continue; }
            }
            runs.Add(new List<DrawnGlyph> { g });
        }
        var matching = wanted is null
            ? runs
            : runs.Where(r => r.Select(g => g.Glyph).SequenceEqual(wanted)).ToList();
        if (matching.Count != 1)
        {
            throw new InvalidOperationException(
                $"expected exactly ONE drawn sustain-pedal word"
                + (word is null ? "" : $" spelling \"{word}\"")
                + $", found {matching.Count} among {runs.Count} pedal glyph run(s) — the "
                + "probe is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        return matching[0];
    }

    /// <summary>
    /// The ottava bracket's dashed LINE above the first staff, measured from that staff's
    /// refpoint (middle line) — the mirror of the OttavaBracket grob's relative
    /// coordinate: <c>lily/ottava-bracket.cc</c> print puts the line at the stencil's own
    /// Y=0 (and centres the label's ink on it), and <c>DrawOttavaBrackets</c> places its
    /// <c>YUp</c> the same way, so both sides anchor the same drawn line.
    /// </summary>
    /// <remarks>
    /// Identified as a HORIZONTAL rule of staff-line thickness ABOVE the top staff line:
    /// staff lines sit at or below that line, ledger lines are drawn 0.1 thicker, stems,
    /// hooks and bar lines are vertical or rects. ⚠️ A trill spanner's wavy segments
    /// would satisfy this predicate — the ottava probes carry no trill, and the count
    /// guard fails loudly if a second horizontal rule appears up there.
    /// </remarks>
    /// <param name="staff">Which staff's refpoint the reading is about, TOP first. The
    /// default -1 also asserts that the book has exactly ONE staff; naming a staff explicitly
    /// says the book is multi-staff on purpose (the lower-staff regime, ledger
    /// <c>ottava.lower-staff.*</c>), and then the rule has to be found between that staff and
    /// the one above it.</param>
    public double OttavaLineAboveStaff(int page = 0, int staff = -1)
        => SoleRuleAboveStaff("the ottava line", page, staff);

    /// <summary>
    /// The text spanner's dashed LINE above the first staff, measured from that staff's
    /// refpoint (middle line) — the mirror of the TextSpanner grob's relative
    /// coordinate: <c>ly:line-spanner::print</c> builds the line at the stencil's own
    /// Y=0, and <c>DrawTextSpanners</c> draws its line (and the text's baseline) at the
    /// layout's <c>YUp</c>, so both sides anchor the same drawn line.
    /// </summary>
    /// <remarks>Same selection as <see cref="OttavaLineAboveStaff"/> — the dashed rule
    /// is recorded as ONE line primitive of staff-line thickness, so it is told from the
    /// staff lines by its LEFT END exactly like the ottava rule.</remarks>
    public double TextSpannerLineAboveStaff(int page = 0)
        => SoleRuleAboveStaff("the text spanner line", page);

    /// <summary>
    /// The text spanner's LABEL PEN, relative to notehead <paramref name="noteheadIndex"/>
    /// — the spanner's LEFT BOUND plus its bound padding, and nothing else.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/text-spanner-engraver.cc:108-115 <c>Text_spanner_engraver::stop_translation_timestep</c> —
    ///   the LEFT bound is the <c>currentMusicalColumn</c> of the timestep the START was
    ///   seen in, i.e. THE NOTE IT WAS WRITTEN ON, not its measure.
    /// LILYPOND-REF: lily/line-spanner.cc:149-176 <c>Line_spanner::calc_bound_info</c> —
    ///   that column's <c>generic_bound_extent</c> read at <c>attach-dir</c> (LEFT for
    ///   TextSpanner), and :596-600 <c>span_points[d] += -d * gaps[d] * magstep *
    ///   dz.direction ()</c> — the <c>bound-details.left.padding</c> 0.25 spent before the
    ///   left text stencil is translated to that point.
    /// <para>
    /// PEN AGAINST PEN, so no font metric enters on either side: a LilyPond text stencil
    /// takes its X box from Pango's LOGICAL rectangle (lily/pango-font.cc:351-362
    /// <c>Pango_font::pango_item_string_stencil</c>), so the
    /// grob's X-extent LEFT is the pen; Lily# draws the label with
    /// <c>TextAnchor.Start</c> (<c>SharedRenderer.DrawTextSpanners</c>), so its recorded X
    /// is the pen too. ⚠️ THAT is why this reads the LABEL and not the dashed line: the
    /// ottava's line-start twin is defined 0.05 apart on the two sides (LilyPond's number
    /// is a stencil edge, Lily#'s an SVG rule endpoint — ledger
    /// <c>ottava.x.line-start-to-notehead</c> names it), and that harness term has no
    /// business inside a reading about WHICH COLUMN a span starts on.
    /// </para>
    /// </remarks>
    public double TextSpannerLabelPenToNotehead(int noteheadIndex = 0)
    {
        var labels = Texts.Where(t => t.Role == TextRole.Text).ToList();
        if (labels.Count != 1)
        {
            throw new InvalidOperationException(
                $"expected exactly ONE text-spanner label, found {labels.Count} — the probe "
                + "is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        if (labels[0].Anchor != TextAnchor.Start)
        {
            throw new InvalidOperationException(
                $"the text-spanner label is drawn with anchor {labels[0].Anchor}, so its X is "
                + "not the pen and this reading would not be pen-against-pen.\n"
                + "Drawn geometry:\n" + Describe());
        }
        return labels[0].X - NoteheadAnchor(noteheadIndex);
    }

    /// <summary>
    /// The trill spanner's LINE above the first staff, measured from that staff's
    /// refpoint (middle line). The LINE is the grob's refpoint on both sides; the "tr"
    /// glyph hangs <c>stencil-offset (0 . -1)</c> below it (scm/define-grobs.scm:4068,
    /// mirrored by <c>DrawTrillSpanners</c>), so the line is read back as the drawn
    /// glyph's Y plus that offset — through the glyph because the wave is drawn as
    /// short sloped segments no rule predicate should try to own.
    /// </summary>
    /// <param name="staff">Which staff's refpoint the reading is about, TOP first. The
    /// default -1 also asserts that the book has exactly ONE staff; naming a staff explicitly
    /// says the book is multi-staff on purpose (the lower-staff regime, ledger
    /// <c>trill.lower-staff.*</c>) and then only the count has to cover it.</param>
    public double TrillLineAboveStaff(int page = 0, int staff = -1)
    {
        // The wavy segments are short (< 1 ss) and sloped, so StaffLineYs never sees
        // them and StaffRefpoints stays usable here (unlike the ottava/text-spanner
        // books, whose rule shares the staff lines' predicate).
        var refs = StaffRefpoints(page);
        if (staff < 0 ? refs.Count != 1 : refs.Count <= staff)
        {
            throw new InvalidOperationException(
                $"page {page}: expected {(staff < 0 ? "exactly ONE staff" : $"more than {staff} staves")}, "
                + $"found {refs.Count} — the probe is not measuring what it claims."
                + "\nDrawn geometry:\n" + Describe());
        }
        var tr = Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.OrnTrill).ToList();
        if (tr.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE tr glyph, found {tr.Count} — the "
                + "probe is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        // Device-down: the glyph sits BELOW the line, so the line is offset less deep.
        return refs[staff < 0 ? 0 : staff] - (tr[0].Y - EngravingDefaults.TrillSpannerTextOffsetDown);
    }

    /// <summary>
    /// The volta bracket's horizontal LINE and the staff's top line, both device-down: the
    /// rule's own BOTTOM EDGE, which is what a skyline distance is measured from.
    /// </summary>
    /// <remarks>
    /// The half thickness comes from the RECORDED line's own stroke width rather than from a
    /// constant, so the reading follows <c>SharedRenderer.DrawVoltaBrackets</c> if its 0.13
    /// ever becomes LilyPond's 1.6 x line-thickness. That matters here: the LilyPond side of
    /// <c>page.volta.*</c> is measured to ITS line's bottom edge (half of 0.16 below the
    /// grob's reference), so a residual carrying half a thickness would be a difference in
    /// the rule's WEIGHT wearing the clearance's clothes.
    /// <para>
    /// SELECTED BY REACH, not by weight: a ledger line is horizontal too and is the only
    /// other rule these books draw above the staff, and it spans a notehead (about 2 ss)
    /// where a volta bracket spans its endings. Both brackets of a repeat share one placed Y
    /// (the spanner is one axis group — <c>OutsideStaffStacker.PlaceVoltas</c> places the
    /// chain once), so the guard asks for exactly ONE distinct Y and says so loudly when a
    /// book grows a second chain or an ottava line.
    /// </para>
    /// </remarks>
    private (double StaffMiddleLine, double LineBottom) VoltaLineRaw(int page)
    {
        var staffLines = StaffLineYs(page);
        if (staffLines.Count != 5)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE 5-line staff, found {staffLines.Count} "
                + "staff line(s) — the probe is not measuring what it claims."
                + "\nDrawn geometry:\n" + Describe());
        }
        // ⚠️ THE MIDDLE LINE, and it was the TOP one for one commit. The LilyPond side reads
        // the StaffSymbol's own reference point (the middle line, no extent and so no
        // thickness); reading the drawn TOP line here put the two sides half a staff-line
        // apart (0.05) and the entries carried that as if it were engine divergence.
        double staffMiddle = staffLines[2];
        var rules = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                        && l.Y1 < staffLines[0] - 1e-6
                        && Math.Abs(l.X2 - l.X1) >= 5.0)
            .ToList();
        var ys = rules.Select(l => Math.Round(l.Y1, 9)).Distinct().ToList();
        if (ys.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE horizontal rule reaching 5 ss or more "
                + $"above the staff (the volta bracket's line), found {ys.Count} — the probe "
                + "is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        var half = rules.Select(l => l.StrokeWidth / 2.0).Distinct().ToList();
        if (half.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: the volta bracket's line is drawn at {half.Count} different "
                + "weights — the reading of its bottom edge is not well defined."
                + "\nDrawn geometry:\n" + Describe());
        }
        return (staffMiddle, ys[0] + half[0]);
    }

    /// <summary>The volta bracket line's TOP edge (device-down), by the same reading as
    /// <see cref="VoltaLineRaw"/>'s bottom — the edge an ending's label is cleared from.</summary>
    private double VoltaLineTop(int page)
    {
        var (_, lineBottom) = VoltaLineRaw(page);
        var rules = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && Math.Abs(l.Y1 + l.StrokeWidth / 2.0 - lineBottom) < 1e-9)
            .ToList();
        return lineBottom - rules[0].StrokeWidth;
    }

    /// <summary>
    /// How far the volta bracket line's bottom edge stands above the NAMED chord symbol's ink
    /// top — <see cref="VoltaLineBottomAboveChordInk(int)"/> for a book that carries a whole
    /// chord ROW, where the symbol under the bracket's number is the one that binds.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions — the
    /// ChordNames line's symbols are support for the System-level pass that places the
    /// VoltaBracketSpanner (probes/volta-chord-row.ly book VCR: the line stands on "Am").
    /// </remarks>
    public double VoltaLineBottomAboveChordInk(string chord, int page = 0)
    {
        var (_, lineBottom) = VoltaLineRaw(page);
        var chords = ChordSymbols.Where(t => t.Text == chord).ToList();
        if (chords.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE chord symbol reading \"{chord}\", found "
                + $"{chords.Count} — the probe is not measuring what it claims.\nDrawn geometry:\n"
                + Describe());
        }
        var ink = ChordNameEngraver.SymbolInk(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled, chords[0].Text);
        return (chords[0].Y - ink.Top) - lineBottom;
    }

    /// <summary>
    /// How far the boxed mark reading <paramref name="label"/> stands above the volta bracket's
    /// line: its drawn BOX's bottom edge above the line's TOP edge — the outside-staff
    /// clearance a RehearsalMark (1500) keeps from a VoltaBracketSpanner (600).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions —
    /// outside-staff-padding 0.46 between the mark's DOWN skyline and the bracket's UP; the
    /// box is read from the drawn RECT that contains the label, as
    /// <see cref="MusicMarkBoxLeftFromClefLeft"/> reads it (probes/volta-chord-row.ly: 0.460000
    /// on both endings, with and without a chord row).
    /// </remarks>
    public double MusicMarkBoxBottomAboveVoltaLineTop(string label, int page = 0)
    {
        var texts = _pages[page].Texts
            .Where(t => t.Role == TextRole.Mark && t.Text == label).ToList();
        if (texts.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected ONE boxed mark reading \"{label}\", found "
                + $"{texts.Count}.\nDrawn geometry:\n" + Describe());
        }
        var t = texts[0];
        var boxes = _pages[page].Rects
            .Where(r => r.X <= t.X && t.X <= r.X + r.Width
                        && r.Y <= t.Y && t.Y <= r.Y + r.Height).ToList();
        if (boxes.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: the mark \"{label}\" at ({t.X:F3},{t.Y:F3}) sits in "
                + $"{boxes.Count} rect(s) — the reading cannot name its box.\n"
                + "Drawn geometry:\n" + Describe());
        }
        // Device-down: the box's bottom is the larger Y; the line's top the smaller.
        return VoltaLineTop(page) - (boxes[0].Y + boxes[0].Height);
    }

    /// <summary>
    /// How far the volta bracket line's bottom edge stands above the staff's MIDDLE line.
    /// </summary>
    /// <remarks>
    /// Two entries of <c>page.volta.*</c> read this (probe volta-over-chord.ly): book VOCV,
    /// where notes poke above the staff and the outside-staff pass has something to clear,
    /// and book VOCF, where nothing does and the reading is the placement's FLOOR alone.
    /// Carrying both is what lets a change to the floor be told from a change to the
    /// clearance — neither reading can say which moved on its own.
    /// </remarks>
    public double VoltaLineBottomAboveStaffMiddle(int page = 0)
    {
        var (staffMiddle, lineBottom) = VoltaLineRaw(page);
        return staffMiddle - lineBottom;
    }

    /// <summary>
    /// How far the volta bracket line's bottom edge stands above the sole chord symbol's ink
    /// TOP — the clearance the outside-staff pass is supposed to leave, signed so that a
    /// bracket drawn THROUGH the symbol reads negative.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions —
    /// the mover is pushed by its own DOWN skyline's distance to the support's UP plus
    /// outside-staff-padding, and a VoltaBracketSpanner (priority 600) is a mover over a
    /// ChordName, which declares no priority and so belongs to the support
    /// (:914-935 inside_staff_skylines). LilyPond answers 0.460000 (volta-over-chord.ly VOC).
    /// <para>
    /// FACE-FREE ON PURPOSE: both sides are read from the SYMBOL'S OWN INK TOP, so the chord
    /// face divergence (LilyPond's Nimbus Sans against Lily#'s TeX Gyre Heros — ledger
    /// <c>page.chord-row.staff-to-chord-baseline</c>) cancels, and so does the LilyPond
    /// book's spelling its symbol as a TextScript (the probe's header says why it does).
    /// </para>
    /// <para>
    /// The ink comes from <see cref="ChordNameEngraver.SymbolInk"/> — the ONE house the
    /// reservation, the mark family's clearance and the draw all read — so this reading
    /// cannot drift from the ink the engine actually reserved.
    /// </para>
    /// </remarks>
    public double VoltaLineBottomAboveChordInk(int page = 0)
    {
        var (_, lineBottom) = VoltaLineRaw(page);
        var chords = ChordSymbols;
        if (chords.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE chord symbol, found {chords.Count} — the "
                + "probe is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        var ink = ChordNameEngraver.SymbolInk(
            LilySharp.Core.Rendering.ScoreTextMetrics.Bundled, chords[0].Text);
        // Device-down: the ink top is Ink.Top ABOVE the drawn baseline.
        return (chords[0].Y - ink.Top) - lineBottom;
    }

    /// <summary>
    /// The sole fermata's staff-FACING ink edge above the first staff's refpoint (middle
    /// line), signed up-positive: the ink BOTTOM of an above fermata, the ink TOP of a below
    /// one (so a below reading is negative). The comparable LilyPond quantity is the Script
    /// grob's own Y-extent edge about the staff refpoint, and the two boxes are the same
    /// number: LilyPond dumps <c>ext=(-0.075 . 1.45)</c> for scripts.ufermata and Lily#'s
    /// <c>GlyphMetrics.FermataAboveGlyph</c> is <c>(-0.075 . 1.45)</c>.
    /// </summary>
    /// <remarks>
    /// The EDGE, not the origin, because the edge is what both engines' padding chains talk
    /// about (staff ink + 0.46, head ink + 0.46, drawn stem tip + 0.40). ⚠️ It is the LILC
    /// box's edge on both sides — the SKYLINE's is a thousandth further out (LilyPond's
    /// flattened outline reads -0.076, Lily#'s <c>FermataAboveGlyphOutline</c> the same), and
    /// that sliver is what every one of these entries carries.
    /// <para>
    /// ONE staff and ONE fermata per page, for the reason <see cref="TrillLineAboveStaff"/>
    /// gives: a book that grew a second staff or a second script is not measuring what it
    /// claims, and saying so loudly beats averaging.
    /// </para>
    /// </remarks>
    /// <param name="staff">Which staff's refpoint the reading is about, TOP first. The
    /// default 0 also asserts that the book has exactly one staff; naming a staff explicitly
    /// says the book is multi-staff on purpose (the lower-staff regime, ledger
    /// <c>script.lower-staff.*</c>) and then only the count has to cover it.</param>
    public double FermataInkEdgeAboveStaff(bool above = true, int page = 0, int staff = -1)
        => ScriptInkEdgeAboveStaff(
            above ? EmmentalerGlyphs.FermataAbove : EmmentalerGlyphs.FermataBelow,
            above ? GlyphMetrics.FermataAboveGlyph : GlyphMetrics.FermataBelowGlyph,
            above, page, staff);

    /// <summary>
    /// <see cref="FermataInkEdgeAboveStaff"/> for ANY script glyph: the sole
    /// <paramref name="glyph"/>'s staff-FACING ink edge above the named staff's refpoint,
    /// signed up-positive. The fermata reading is this one with the fermata's glyph and box,
    /// so a script whose placement a point measures is read one way only (the two entries of
    /// dynamic-support.ly's round 3 are a staccato and a marcato, not fermatas).
    /// </summary>
    /// <param name="glyph">The drawn Emmentaler character; it must appear exactly once.</param>
    /// <param name="box">That glyph's LILC box, whose facing edge is the quantity — the same
    /// box LilyPond dumps as the Script grob's own Y-extent.</param>
    public double ScriptInkEdgeAboveStaff(
        char glyph, GlyphMetrics.BBox box, bool above = true, int page = 0, int staff = -1)
    {
        var refs = StaffRefpoints(page);
        if (staff < 0 ? refs.Count != 1 : refs.Count <= staff)
        {
            throw new InvalidOperationException(
                $"page {page}: expected {(staff < 0 ? "exactly ONE staff" : $"more than {staff} staves")}, "
                + $"found {refs.Count} — the probe is not measuring what it claims."
                + "\nDrawn geometry:\n" + Describe());
        }
        int staffIndex = staff < 0 ? 0 : staff;
        var f = Glyphs.Where(g => g.Glyph == glyph).ToList();
        if (f.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE {(above ? "above" : "below")} script glyph "
                + $"U+{(int)glyph:X4}, found {f.Count} — the probe is not measuring what it "
                + "claims.\nDrawn geometry:\n" + Describe());
        }
        // Device-down: an edge `e` staff-spaces up from the drawn origin sits at Y - e.
        double edge = above ? box.Bottom : box.Top;
        return refs[staffIndex] - (f[0].Y - edge);
    }

    /// <summary>
    /// The metronome mark's "= N" equation baseline above the first staff, measured
    /// from that staff's refpoint (middle line). In LilyPond the markup's baseline
    /// carries the digits AND the \smaller note's bottom (general-align Y DOWN,
    /// translation-functions.scm metronome-markup), and the grob's refpoint IS that
    /// baseline; Lily#'s DrawSingleMusicMark draws the equation text at the mark's
    /// anchor Y the same way, so both sides anchor the same drawn baseline.
    /// </summary>
    /// <remarks>Selected by the string: the equation is the only serif run starting
    /// with <c>"= "</c> these probes draw (a textual tempo would wrap it as
    /// <c>"= N)"</c> — still matched; the probes carry no such text).</remarks>
    public double TempoEquationBaselineAboveStaff(int page = 0)
    {
        var eq = Texts
            .Where(t => t.Role != TextRole.ChordName
                        && t.Text.StartsWith("= ", StringComparison.Ordinal))
            .ToList();
        if (eq.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE tempo equation (\"= N\"), found "
                + $"{eq.Count} — the probe is not measuring what it claims.\n"
                + "Drawn geometry:\n" + Describe());
        }
        var refs = StaffRefpoints(page);
        if (refs.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE staff, found {refs.Count} — the "
                + "probe is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        return refs[0] - eq[0].Y;
    }

    /// <summary>
    /// The metronome mark's ink LEFT against the TIME SIGNATURE's ink left. LilyPond
    /// self-aligns the mark LEFT on the break-aligned meter, so the difference is
    /// exactly 0 (probe tempo-mark.ly header, TMQ: both ink-lefts at 4.885000). The
    /// mark's head is the one notehead drawn at the \smaller metronome size
    /// (<see cref="MetronomeMarkGeometry.NoteSize"/>), which no music notehead shares;
    /// the meter is the sole common-time glyph.
    /// </summary>
    public double TempoMarkToTimeSignatureLeft(int page = 0)
    {
        var heads = Glyphs
            .Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack
                        && Math.Abs(g.FontSize - MetronomeMarkGeometry.NoteSize) < 1e-9)
            .ToList();
        if (heads.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE metronome-size notehead, found "
                + $"{heads.Count} — the probe is not measuring what it claims.\n"
                + "Drawn geometry:\n" + Describe());
        }
        var meters = Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.TimeSigCommon).ToList();
        if (meters.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE common-time glyph, found "
                + $"{meters.Count} — the probe is not measuring what it claims.\n"
                + "Drawn geometry:\n" + Describe());
        }
        return heads[0].X - meters[0].X;
    }

    private double SoleRuleAboveStaff(string what, int page, int staff = -1)
    {
        var (refpoint, ruleY) = SoleRuleAboveStaffRaw(what, page, staff);
        return refpoint - ruleY;
    }

    /// <summary>
    /// The same selection, handing back the two DEVICE Y's instead of their difference —
    /// for a reading about the rule itself rather than about the staff (the ottava label's
    /// ink centre, ledger <c>ottava.label.line-to-ink-centre</c>).
    /// </summary>
    private (double Refpoint, double RuleY) SoleRuleAboveStaffRaw(
        string what, int page, int staff = -1)
    {
        // ⚠️ Self-contained on purpose: the spanner line is itself a long horizontal rule
        // of staff-line thickness, so StaffRefpoints' predicate would count SIX lines
        // here and refuse the page. What separates them is the LEFT END: the five staff
        // lines all start at the system's left edge, the spanner line starts after its
        // label's text.
        var rules = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                && Math.Abs(l.StrokeWidth - StaffLineThickness) < 1e-9
                && Math.Abs(l.X2 - l.X1) >= 1.0)
            .ToList();
        double sysLeft = rules.Min(l => Math.Min(l.X1, l.X2));
        var edgeLines = rules
            .Where(l => Math.Min(l.X1, l.X2) - sysLeft < 1e-6)
            .Select(l => l.Y1).Distinct().OrderBy(y => y).ToList();
        // Device-down: the first five are the TOP staff, so grouping by five and taking
        // group `staff` counts staves top first, like StaffRefpoints.
        int wantStaves = staff < 0 ? 1 : staff + 1;
        if (edgeLines.Count != 5 * wantStaves
            || Enumerable.Range(0, edgeLines.Count - 1).Any(i =>
                // 1 apart inside a staff; the boundary between two staves is wider.
                (i + 1) % 5 == 0
                    ? edgeLines[i + 1] - edgeLines[i] < 1.0 + 1e-6
                    : Math.Abs(edgeLines[i + 1] - edgeLines[i] - 1.0) > 1e-6))
        {
            throw new InvalidOperationException(
                $"page {page}: expected {wantStaves} 5-line staff/staves at the system's left "
                + $"edge, found {edgeLines.Count} rule(s) — the probe is not measuring what it "
                + "claims.\nDrawn geometry:\n" + Describe());
        }
        var staffLines = edgeLines.Skip(5 * Math.Max(0, staff)).Take(5).ToList();
        double refpoint = staffLines[2];
        // The rule has to be found in THIS staff's own band: above its top line and, when a
        // staff sits above, below that one's bottom line — otherwise a two-staff page would
        // happily return the other staff's spanner.
        double ceiling = staff <= 0 ? double.NegativeInfinity : edgeLines[5 * staff - 1];
        var ys = rules
            .Where(l => Math.Min(l.X1, l.X2) - sysLeft >= 1e-6
                && l.Y1 < staffLines[0] - 1e-6 && l.Y1 > ceiling + 1e-6)
            .Select(l => l.Y1).Distinct().ToList();
        if (ys.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE horizontal staff-thickness rule above "
                + $"the staff ({what}), found {ys.Count} — the probe is not "
                + "measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        return (refpoint, ys[0]);
    }

    /// <summary>
    /// The ottava bracket's LABEL — the page's only serif run in the ottava books.
    /// </summary>
    /// <remarks>
    /// ⚠️ Told apart by FACE alone, not by size, on purpose: the size IS what one of the
    /// two entries below measures, so a size filter would make the reading circular
    /// (<see cref="CustomTexts"/> can afford one because the script's size is not in
    /// question). The count guard is what keeps the selection honest — a book that grows
    /// a second serif run fails loudly instead of returning the wrong one.
    /// </remarks>
    private DrawnText SoleSerifText(string what, int page)
    {
        var texts = Texts.Where(t => t.Role != TextRole.ChordName).ToList();
        if (texts.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE serif text run ({what}), found "
                + $"{texts.Count} — the probe is not measuring what it claims."
                + "\nDrawn geometry:\n" + Describe());
        }
        return texts[0];
    }

    /// <summary>
    /// The ottava LABEL's drawn PEN, relative to notehead <paramref name="noteheadIndex"/>
    /// — the bracket's LEFT BOUND on both sides, because LilyPond translates the text
    /// stencil's ORIGIN to that bound.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-bracket.cc:121-176 Ottava_bracket::print —
    ///   <c>span_points[d] = ext[d]</c> where <c>ext</c> is the union of the BOUND NOTE
    ///   COLUMN's note-heads' X extents (not the whole column), then
    ///   <c>span_points[d] -= d * shorten[d]</c> with shorten-pair (-0.8 . -0.6), and
    ///   finally <c>text.translate_axis (span_points[LEFT], X_AXIS)</c> — the text's
    ///   ORIGIN, so this reads pen against pen with no left-bearing term on either side.
    /// ⇒ LilyPond's answer is the shorten-pair LEFT, -0.8, exactly.
    /// </remarks>
    public double OttavaLabelPenToNotehead(int noteheadIndex = 0, int page = 0)
    {
        var t = SoleSerifText("the ottava label", page);
        double advance = LilySharp.Core.Rendering.TextFontMetrics.Advance(
            t.Text, t.FontSize, sans: false, LilySharp.Core.Rendering.FontStyle.BoldItalic);
        double pen = t.Anchor switch
        {
            LilySharp.Core.Rendering.TextAnchor.Middle => t.X - advance / 2,
            LilySharp.Core.Rendering.TextAnchor.End => t.X - advance,
            _ => t.X,
        };
        return pen - NoteheadAnchor(noteheadIndex);
    }

    /// <summary>
    /// Where the ottava's dashed LINE starts, relative to notehead
    /// <paramref name="noteheadIndex"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-bracket.cc:124-135 Ottava_bracket::print —
    ///   <c>text_size = text.extent (X_AXIS)[RIGHT] + 0.3</c> ("0.3 is ~ italic
    ///   correction", its own comment) and <c>bracket_span_points[LEFT] +=
    ///   text_size</c>. So the gap is measured from the label's INK right, not its
    ///   advance, and the constant is 0.3. Lily# spends <c>advance + 0.5</c>
    ///   (OttavaBracketEngraver.LabelLineGap), which is why this entry carries BOTH
    ///   differences and the label-pen entry carries only the bound.
    /// </remarks>
    public double OttavaLineStartToNotehead(int noteheadIndex = 0, int page = 0, int staff = -1)
    {
        var (_, ruleY) = SoleRuleAboveStaffRaw("the ottava line", page, staff);
        var rule = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - ruleY) < 1e-9 && Math.Abs(l.Y1 - l.Y2) < 1e-9)
            .OrderBy(l => Math.Min(l.X1, l.X2))
            .First();
        return Math.Min(rule.X1, rule.X2) - NoteheadAnchor(noteheadIndex);
    }

    /// <summary>
    /// The ottava LABEL's own ink height — the drawn "8va" measured at the face, size and
    /// style <c>DrawOttavaBrackets</c> draws it with.
    /// </summary>
    /// <remarks>
    /// LilyPond's OttavaBracket declares <c>font-series bold</c> and <c>font-shape italic</c>
    /// and NO font-size (scm/define-grobs.scm:2708-2731), so its label rides the text font
    /// size every undeclared text grob rides — the 2.2 the TextScript pair measured
    /// (ledger <c>textscript.*</c>, HANDOFF: the em mislabel, three times before this one).
    /// The grob's Y-extent is the centred label's ink, so LilyPond's half of this reading is
    /// its <c>ext</c> doubled: 2 × 0.7920313638041338.
    /// </remarks>
    public double OttavaLabelInkHeight(int page = 0)
    {
        var t = SoleSerifText("the ottava label", page);
        var ink = LilySharp.Core.Rendering.TextFontMetrics.Ink(
            t.Text, t.FontSize, sans: false, LilySharp.Core.Rendering.FontStyle.BoldItalic);
        return ink.Top - ink.Bottom;
    }

    /// <summary>
    /// How far the ottava LABEL's ink CENTRE sits above the drawn bracket LINE.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-bracket.cc <c>Ottava_bracket::print</c> —
    /// <c>text.align_to (Y_AXIS, CENTER)</c> before the line is built at the stencil's own
    /// Y=0, so LilyPond's answer is 0 BY CONSTRUCTION, and the grob's symmetric extent
    /// (−0.7920313638041337 . 0.7920313638041339) is that centring showing through.
    /// <c>DrawOttavaBrackets</c> puts the label's BASELINE there instead, so this reads the
    /// baseline-vs-centre difference the ottava-floor probe's header has flagged as an
    /// unmeasured DRAWING claim since session 30.
    /// </remarks>
    public double OttavaLabelInkCentreToLine(int page = 0, int staff = -1)
    {
        var (_, lineY) = SoleRuleAboveStaffRaw("the ottava line", page, staff);
        var t = SoleSerifText("the ottava label", page);
        var ink = LilySharp.Core.Rendering.TextFontMetrics.Ink(
            t.Text, t.FontSize, sans: false, LilySharp.Core.Rendering.FontStyle.BoldItalic);
        // Device Y is DOWN-positive; the ink centre sits (Top+Bottom)/2 ABOVE the baseline.
        return (lineY - t.Y) + (ink.Top + ink.Bottom) / 2;
    }

    /// <summary>
    /// The baseline step between EXACTLY TWO stacked custom texts (lower to upper).
    /// </summary>
    /// <remarks>
    /// The grob-vs-grob half of the textscript-ink pair (books TXL/TXS): LilyPond lifts the
    /// second script until its skyline clears the first one's by outside-staff-padding 0.46
    /// (lily/axis-group-interface.cc:45-50 get_default_outside_staff_padding, :739-806
    /// add_grobs_of_one_priority), so the step is
    /// <c>inkTop(lower) + 0.46 + descent(upper)</c> when the lower profile is flat under the
    /// upper's extremes (book TXL) and LESS when it is not (book TXS — outline against
    /// outline, pointwise). An interval stacker that prices ascent and descent as flat em
    /// fractions reads the SAME step whatever the strings are.
    /// </remarks>
    public double CustomTextBaselineStep(int page = 0)
    {
        var texts = CustomTexts;
        if (texts.Count != 2)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly TWO stacked custom texts, found {texts.Count} "
                + "— the probe is not measuring what it claims.\nDrawn geometry:\n"
                + Describe());
        }
        // Device Y grows downward: [0] is the UPPER text, [1] the LOWER one.
        return texts[1].Y - texts[0].Y;
    }

    /// <summary>
    /// How many systems the page breaker put on <paramref name="page"/>.
    /// </summary>
    /// <remarks>
    /// ONE-STAFF probes only — this counts STAVES, which is the same thing only when each
    /// system has exactly one (see <see cref="StaffRefpoints"/> for why that restriction is
    /// the same one every other page reading here lives under).
    ///
    /// This is the quantity <c>Page_breaking</c> decides, and it is deliberately separate
    /// from the refpoint and gap readings: those describe how a page that ALREADY holds N
    /// systems is spaced, and they stay green while N itself is wrong. LilyPond prices a
    /// page against
    /// <c>page_height_ - min_whitespace_at_top_of_page - min_whitespace_at_bottom_of_page</c>
    /// (lily/page-spacing.cc:30-41), so a breaker that knows nothing of the top and
    /// last-bottom springs can fit a different number of systems than the placement chain
    /// that follows it — with no committed fixture obliged to notice.
    /// </remarks>
    public int SystemsOnPage(int page = 0) => StavesOnPage(page);

    /// <summary>
    /// How many STAVES were drawn on <paramref name="page"/>.
    /// </summary>
    /// <remarks>
    /// The quantity <see cref="SystemsOnPage"/> actually computes, named for what it is, so a
    /// multi-staff probe can assert the shape of its page without claiming to count systems.
    /// A probe that reads gaps by index (<see cref="StaffGapAt"/>) needs this: the index means
    /// the staff it is supposed to mean only while the page holds the staves the probe assumes.
    /// </remarks>
    public int StavesOnPage(int page = 0) => StaffRefpoints(page).Count;

    /// <summary>
    /// Distance from the top paper edge down to the first system's staff refpoint.
    /// </summary>
    /// <remarks>
    /// LilyPond puts this at <c>top-margin + top-system-spacing</c>'s basic-distance when
    /// nothing pushes it further (measured on 2.26.0: 5.690551 + 6.000000).
    /// </remarks>
    public double FirstStaffRefpoint(int page = 0) => StaffRefpoints(page)[0];

    /// <summary>
    /// Distance from the LAST staff refpoint on <paramref name="page"/> down to the bottom
    /// paper edge — the closing term of that page's spring chain.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:538-545 — <c>last-bottom-spacing</c> is a
    /// spring appended after every system, and what it spans is the LAST SPACEABLE STAFF's
    /// refpoint to the bottom of the page's band, not the system origin and not the ink.
    /// So the refpoint frame <see cref="StaffRefpoints"/> already works in is the frame this
    /// term lives in, and the reading is the page height less the last refpoint.
    /// <para>
    /// ⚠️ Why the chain needs a reading down here at all, when six entries already read it:
    /// all of them read a GAP, and a gap is a spring's length at the page's force. A force is
    /// the page's slack over the chain's total strength, so an error in a FIXED term of the
    /// chain lands in every gap at once, each in proportion to its own strength — N readings
    /// of one cause, with nothing in the corpus to attribute it to. That is what
    /// <c>page.{stretched,compressed}.*</c> were for a session: four residuals that were two
    /// forces (HANDOFF 5.3 — divide a residual by its spring's strength before believing N
    /// residuals are N quantities). This reads the term itself.
    /// </para>
    /// </remarks>
    public double LastStaffRefpointToFoot(int page = 0) =>
        PageHeight(page) - StaffRefpoints(page)[^1];

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

    /// <summary>
    /// The gap between system <paramref name="index"/> and the next, on
    /// <paramref name="page"/> — for probes whose FIRST or LAST system differs from the
    /// interior ones so the gaps are deliberately NOT uniform and <see cref="StaffGap"/>
    /// would refuse to pick one.
    /// </summary>
    /// <remarks>
    /// A slur's arc depends on the horizontal span (unlike a tuplet bracket, which clears the
    /// notes by a fixed padding), so a system carrying a time signature or a final bar line
    /// spaces its notes — and thus its bow — a hair differently from a plain interior system.
    /// The page-crossing slur probes (system.slur-{under,over}-notes) name an INTERIOR gap,
    /// both of whose systems are plain, so the measured gap is the one the LilyPond probe's
    /// deliberately-uniform systems produce. Reads a single named gap and does not check
    /// uniformity — the caller has asserted, by index, which gap is the meaningful one.
    /// </remarks>
    public double StaffGapAt(int index, int page = 0)
    {
        var refs = StaffRefpoints(page);
        if (index < 0 || index + 1 >= refs.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: gap {index} needs systems {index} and {index + 1}, "
                + $"but only {refs.Count} exist.");
        }
        return refs[index + 1] - refs[index];
    }

    /// <summary>
    /// A beam's own height above the staff's middle line, up-positive, at one END of it —
    /// the quantity LilyPond calls <c>Beam.positions</c>.
    /// </summary>
    /// <remarks>
    /// The PRIMARY beam line (rank 0) is measured, which is the widest segment of the group:
    /// LilyPond's positions is that line's CENTRE, and every other beam of the stack is drawn
    /// at <c>positions + beam_dy × vertical_count</c> from it (lily/beam.cc:810-814
    /// Beam::print). Reading a beamlet stub instead would report a translation, not a quant.
    /// <para>
    /// ⚠️ Both ends, not their average: a sloped beam's height and its slope are two numbers
    /// and a single reading cannot separate them (an average matches a beam that is too steep
    /// AND too low). LilyPond quotes both for the same reason.
    /// </para>
    /// </remarks>
    /// <param name="beamIndex">Which beam group, left to right.</param>
    /// <param name="rightEnd">false = the beam's left end, true = its right end.</param>
    public double BeamPositionAboveStaffMiddle(int beamIndex, bool rightEnd, int page = 0)
    {
        var refs = StaffRefpoints(page);
        if (refs.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE staff, found {refs.Count} — the probe is "
                + "not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }

        var primaries = PrimaryBeamLines(page);

        if (primaries.Count <= beamIndex)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for beam {beamIndex} but only {primaries.Count} beam "
                + "group(s) were drawn.\nDrawn geometry:\n" + Describe());
        }

        var beam0 = primaries[beamIndex];
        // Device y is down; the staff refpoint is the middle line.
        return refs[0] - (rightEnd ? beam0.RightY : beam0.LeftY);
    }

    /// <summary>
    /// Which way the page's ONE bow curves: +1 up, −1 down — LilyPond's <c>Tie.direction</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ READ OFF THE CONTROL POINTS, NOT THE ENDPOINTS. A tie curving up and one curving
    /// down share their two ends exactly — <c>TieFormattingProblem.CreateLayout</c> puts both
    /// at the same attachment Y — and differ only in which side the controls sit, so a
    /// reading taken from the drawn extremes would report the arc's height and call it a
    /// direction. See <see cref="DrawnBezier"/> for why the sandwich's two halves are averaged.
    /// <para>
    /// ⚠️ THROWS UNLESS THERE IS EXACTLY ONE BOW, rather than taking the first. A tie and a
    /// slur are the same primitive here (both go through <c>SharedRenderer.DrawCurve</c>), so
    /// a probe that grew a slur — or a second tie — would otherwise silently start measuring
    /// a different grob and keep reporting a plausible ±1.
    /// </para>
    /// </remarks>
    public double SoleBowDirection(int page = 0)
    {
        var bows = _pages[page].Beziers;
        if (bows.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE bow (tie or slur), found {bows.Count} — "
                + "the probe is not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }

        var bow = bows[0];
        // Device Y is down, so a control ABOVE the endpoint has the SMALLER y.
        double lift = bow.P0.Y - bow.Centreline1.Y;
        if (Math.Abs(lift) < 1e-9)
        {
            throw new InvalidOperationException(
                $"page {page}: the bow is flat ({lift:E3}), so it has no direction to read."
                + "\nDrawn geometry:\n" + Describe());
        }
        return lift > 0 ? +1 : -1;
    }

    /// <summary>
    /// How wide bow <paramref name="index"/> comes out — end to end, which is LilyPond's
    /// <c>control-points</c> [3].x − [0].x.
    /// </summary>
    /// <remarks>
    /// The span is the whole attachment question in one number: LilyPond reads the column's
    /// chord-outline skyline at the tie's own Y, so a tie that clears its heads attaches at
    /// the head CENTRES and a tie running alongside a head, a neighbouring head or a stem
    /// attaches at their EDGES — a difference of one notehead per end. Bows are indexed in
    /// draw order (ties bottom to top within a chord, then along the staff).
    /// </remarks>
    /// <summary>
    /// How high bow <paramref name="index"/> attaches, in staff spaces above the middle line —
    /// LilyPond's <c>control-points</c> [0].y.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS EXISTS BECAUSE <see cref="BowSpan"/> CANNOT SEE THE COLUMN. LilyPond varies a
    /// whole <c>Ties_configuration</c> together (lily/tie-formatting-problem.cc:915-1001
    /// generate_configuration, find_best_variation), so a tie's chosen POSITION depends on the
    /// other ties of its chord; Lily# solved a column one tie at a time and could not follow that.
    /// MEASURED (probe tie-direction.ly): the same c, head position −6, front of its column,
    /// takes variation −7 in TWSEC and −8 in TW3 — and its WIDTH is 3.875445 in both. Six
    /// width points over two three-tie books therefore opened EXACT while the approximation
    /// they were built for went on standing. The height is where the chosen position shows, and
    /// it is what closed it: tie.y.triad.lower, +0.250000 until the column went to one problem.
    /// <para>
    /// ⚠️ The page must hold exactly ONE staff, the same demand
    /// <see cref="BeamPositionAboveStaffMiddle"/> makes and for the same reason: with two,
    /// "the middle line" is a question rather than a reference.
    /// </para>
    /// </remarks>
    public double BowAttachmentAboveStaffMiddle(int index, int page = 0)
    {
        var refs = StaffRefpoints(page);
        if (refs.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE staff, found {refs.Count} — the probe is "
                + "not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        var bows = _pages[page].Beziers;
        if (index < 0 || index >= bows.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for bow {index} but {bows.Count} were drawn.\n"
                + "Drawn geometry:\n" + Describe());
        }
        // Device y is down; the staff refpoint is the middle line.
        return refs[0] - bows[index].P0.Y;
    }

    public double BowSpan(int index, int page = 0)
    {
        var bows = _pages[page].Beziers;
        if (index < 0 || index >= bows.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for bow {index} but {bows.Count} were drawn.\n"
                + "Drawn geometry:\n" + Describe());
        }
        return bows[index].P1.X - bows[index].P0.X;
    }

    /// <summary>
    /// One reading per beam GROUP: the group's OUTERMOST beam line, which is what LilyPond's
    /// <c>positions</c> describes. Shared by the five-line and the tab readers.
    /// </summary>
    private List<(double Left, double Right, double LeftY, double RightY)> PrimaryBeamLines(
        int page)
    {
        // Group the drawn quads into beams by x span: a stub, and every further line of a
        // STACK, spans no wider than the group's widest quad.
        var quads = _pages[page].Quads
            .Select(q => (Left: Math.Min(q.X0, q.X3), Right: Math.Max(q.X1, q.X2),
                          LeftY: (q.Y0 + q.Y3) / 2, RightY: (q.Y1 + q.Y2) / 2))
            .OrderBy(q => q.Left).ThenByDescending(q => q.Right - q.Left)
            .ToList();
        var groups = new List<List<(double Left, double Right, double LeftY, double RightY)>>();
        foreach (var q in quads)
        {
            var owner = groups.FirstOrDefault(g =>
                q.Left >= g[0].Left - 1e-9 && q.Right <= g[0].Right + 1e-9);
            if (owner != null) owner.Add(q);
            else groups.Add(new List<(double, double, double, double)> { q });
        }

        // ⚠️ Of a STACK, the line LilyPond's positions describes is the OUTERMOST: every
        // further beam is drawn at positions + beam_dy × rank TOWARD the noteheads
        // (lily/beam.cc:810-814 Beam::print). Reading the inner line instead reports a beam
        // exactly one beam translation (0.81 at full size) off, which looks like a quanter
        // defect and is not — the corpus twin sweep of 2026-08-01 was fooled by it twice
        // before the rule was written down. The stems say which side the noteheads are on:
        // whichever way a stem runs past the stack is the way the music lies.
        var verticals = _pages[page].Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9 && Math.Abs(l.Y2 - l.Y1) > 0.3)
            .Select(l => (X: l.X1, Top: Math.Min(l.Y1, l.Y2), Bottom: Math.Max(l.Y1, l.Y2)))
            .ToList();
        var primaries = new List<(double Left, double Right, double LeftY, double RightY)>();
        foreach (var g in groups)
        {
            var full = g.Where(q => q.Right - q.Left >= (g[0].Right - g[0].Left) - 1e-9).ToList();
            double top = full.Min(q => Math.Min(q.LeftY, q.RightY));
            double bottom = full.Max(q => Math.Max(q.LeftY, q.RightY));
            double below = 0, above = 0;
            foreach (var s in verticals)
            {
                if (s.X < g[0].Left - 0.2 || s.X > g[0].Right + 0.2) continue;
                below = Math.Max(below, s.Bottom - bottom);
                above = Math.Max(above, top - s.Top);
            }
            primaries.Add(below > above
                ? full.OrderBy(q => q.LeftY + q.RightY).First()      // stems run DOWN: topmost
                : full.OrderByDescending(q => q.LeftY + q.RightY).First());
        }

        return primaries;
    }

    /// <summary>
    /// The same reading on a staff that is NOT five lines of unit space — a TAB staff — and
    /// in that staff's OWN spaces, which is the frame LilyPond's <c>positions</c> speaks.
    /// </summary>
    /// <remarks>
    /// <see cref="BeamPositionAboveStaffMiddle"/> cannot serve twice over: it refuses any
    /// page whose line count is not a multiple of five, and it answers in DRAWN spaces, where
    /// a tab staff's own space is 1.5. Both the middle line and the space are read back off
    /// the drawn cluster here, so a four- and a six-string staff are the same reading and the
    /// number can be compared with LilyPond's without a conversion in between.
    /// <para>
    /// ⚠️ The page must hold exactly ONE staff — the same demand the five-line reader makes,
    /// for the same reason: two staves make "the middle line" a question.
    /// </para>
    /// </remarks>
    public double TabBeamPositionAboveStaffMiddle(int beamIndex, bool rightEnd, int page = 0)
    {
        var ys = StaffLineYs(page);
        if (ys.Count < 4)
        {
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff line(s); a tab staff reading needs at "
                + "least four strings.\nDrawn geometry:\n" + Describe());
        }

        double top = ys.Min(), bottom = ys.Max();
        double space = (bottom - top) / (ys.Count - 1);
        double middle = (top + bottom) / 2;

        var primaries = PrimaryBeamLines(page);
        if (primaries.Count <= beamIndex)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for beam {beamIndex} but only {primaries.Count} beam "
                + "group(s) were drawn.\nDrawn geometry:\n" + Describe());
        }

        var beam = primaries[beamIndex];
        return (middle - (rightEnd ? beam.RightY : beam.LeftY)) / space;
    }

    /// <summary>
    /// A bow's control point on a TAB staff, in that staff's own spaces above its middle —
    /// the frame <see cref="TabBeamPositionAboveStaffMiddle"/> reports in, and the one
    /// LilyPond's tab-slur probe converts its <c>control-points</c> into.
    /// <paramref name="which"/> is 0 for the left attachment <c>P0</c> and 1 for the first
    /// control point <c>C1</c> (the sandwich's two halves averaged, which is the point the
    /// scorer actually solved).
    /// </summary>
    /// <remarks>
    /// <see cref="BowAttachmentAboveStaffMiddle"/> cannot serve: it measures against
    /// <see cref="StaffRefpoints"/>, which is a five-line staff's middle LINE, and a
    /// four-string tab has no line there at all — its middle falls in the gap between
    /// strings 2 and 3. Both the middle and the space are read back off the drawn strings
    /// here, so a four- and a six-string staff are the same reading.
    /// <para>
    /// ⚠️ The page must hold exactly ONE staff, for the reason its neighbours give: with
    /// two, "the middle" is a question rather than a reference.
    /// </para>
    /// </remarks>
    public double TabBowPointAboveStaffMiddle(int index, int which, int page = 0)
    {
        var ys = StaffLineYs(page);
        if (ys.Count < 4)
        {
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff line(s); a tab staff reading needs at "
                + "least four strings.\nDrawn geometry:\n" + Describe());
        }
        double top = ys.Min(), bottom = ys.Max();
        double space = (bottom - top) / (ys.Count - 1);
        double middle = (top + bottom) / 2;

        var bows = _pages[page].Beziers;
        if (index < 0 || index >= bows.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for bow {index} but {bows.Count} were drawn.\n"
                + "Drawn geometry:\n" + Describe());
        }
        double y = which == 0 ? bows[index].P0.Y : bows[index].Centreline1.Y;
        return (middle - y) / space;
    }

    /// <summary>
    /// A bow's end-to-end length on a TAB staff, in that staff's own spaces — LilyPond's
    /// <c>control-points</c> [3].x − [0].x, and the number a bow's HEIGHT is a function of
    /// (lily/bezier-bow.cc slur_height). <see cref="BowSpan"/> answers the same question in
    /// drawn spaces; this one shares the tab reader's frame so the pair can be read together.
    /// </summary>
    public double TabBowSpan(int index, int page = 0)
    {
        var ys = StaffLineYs(page);
        if (ys.Count < 4)
        {
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff line(s); a tab staff reading needs at "
                + "least four strings.\nDrawn geometry:\n" + Describe());
        }
        double space = (ys.Max() - ys.Min()) / (ys.Count - 1);
        var bows = _pages[page].Beziers;
        if (index < 0 || index >= bows.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for bow {index} but {bows.Count} were drawn.\n"
                + "Drawn geometry:\n" + Describe());
        }
        return (bows[index].P1.X - bows[index].P0.X) / space;
    }

    /// <summary>
    /// How many beam lines reach a stem on one side — LilyPond's <c>Stem.beaming</c>, read
    /// off what was DRAWN.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a different quantity from <see cref="BeamPositionAboveStaffMiddle"/>, which
    /// reads the primary line's HEIGHT and deliberately ignores every stub. What is under
    /// test here is the COUNT: whether the beamlet a stem should carry was drawn at all.
    /// LilyPond's <c>beaming</c> property is a pair of rank lists, one per side, and their
    /// LENGTHS are these numbers.
    /// </para>
    /// <para>
    /// A beam line covers the stem's x on a side exactly when it is drawn across it, so the
    /// count is read by probing a hair either side of the stem. A stub pointing right from
    /// stem <c>i</c> starts AT that stem and runs <c>beamlet-default-length</c> (1.1, capped
    /// at 0.75 of the gap — lily/beam.cc:604-624) toward the next, which is four orders of
    /// magnitude past <see cref="BeamletProbeOffset"/>.
    /// </para>
    /// <para>
    /// ⚠️ The OUTWARD side of a terminal stem is refused rather than answered. A beam's
    /// drawn end is extended half a stem thickness past its outer stem (lily/beam.cc:631),
    /// so probing outside stem 0 would find the primary line and report a beam that reaches
    /// nothing — LilyPond prints <c>#f</c> there. Only the interior sides are counts.
    /// </para>
    /// </remarks>
    /// <param name="beamIndex">Which beam group, left to right.</param>
    /// <param name="stemIndex">Which stem of that group, left to right.</param>
    /// <param name="rightSide">false = the stem's left side, true = its right.</param>
    public int BeamletsAtStem(int beamIndex, int stemIndex, bool rightSide, int page = 0)
    {
        var stems = BeamGroupStems(beamIndex, page, out var beam);

        if (stemIndex < 0 || stemIndex >= stems.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for stem {stemIndex} of beam {beamIndex}, which has "
                + $"{stems.Count}.\nDrawn geometry:\n" + Describe());
        }

        if ((stemIndex == 0 && !rightSide) || (stemIndex == stems.Count - 1 && rightSide))
        {
            throw new InvalidOperationException(
                $"page {page}: the outward side of terminal stem {stemIndex} is not a beamlet "
                + "count — the beam's drawn end is extended half a stem thickness past it "
                + "(lily/beam.cc:631), and LilyPond prints #f for that side.");
        }

        double probe = stems[stemIndex] + (rightSide ? BeamletProbeOffset : -BeamletProbeOffset);
        return _pages[page].Quads.Count(q =>
            Math.Min(q.X0, q.X3) <= probe && probe <= Math.Max(q.X1, q.X2));
    }

    /// <summary>
    /// How far either side of a stem <see cref="BeamletsAtStem"/> looks for beam lines.
    /// </summary>
    /// <remarks>
    /// Past the OVERHANG, short of the shortest STUB. Every segment end that stops at a
    /// stem overhangs it by half the stem thickness (0.065 — lily/beam.cc:627-631, interior
    /// ends included since the overhang port), so a probe closer than that to the stem
    /// catches the covering tail of a segment that ENDS there and inflates the count on the
    /// wrong side — 1e-6 did exactly that the day the interior overhang landed, reading
    /// e.g. peak-8-16-8's left as 2. LilyPond's own ledger numbers are its
    /// <c>Stem.beaming</c> DATA, not probed ink, so the probe must ask the same question:
    /// what runs BETWEEN the stems. 0.1 clears the 0.065 overhang and stays under the
    /// shortest beamlet in the corpus (a fifth of a staff space).
    /// </remarks>
    private const double BeamletProbeOffset = 0.1;

    /// <summary>
    /// The beam groups drawn on <paramref name="page"/>, left to right: each group's PRIMARY
    /// line, which is the widest segment starting at that group's left.
    /// </summary>
    /// <remarks>
    /// A beamlet stub lies strictly inside its group's primary, so containment is what tells
    /// the two apart — no assumption about how long a stub is.
    /// </remarks>
    private List<(double Left, double Right)> PrimaryBeams(int page)
    {
        var quads = _pages[page].Quads
            .Select(q => (Left: Math.Min(q.X0, q.X3), Right: Math.Max(q.X1, q.X2)))
            .OrderBy(q => q.Left).ThenByDescending(q => q.Right - q.Left)
            .ToList();
        var primaries = new List<(double Left, double Right)>();
        foreach (var q in quads)
            if (!primaries.Any(p => q.Left >= p.Left - 1e-9 && q.Right <= p.Right + 1e-9))
                primaries.Add(q);
        return primaries;
    }

    /// <summary>
    /// The x of every stem beam <paramref name="beamIndex"/> joins, left to right.
    /// </summary>
    /// <remarks>
    /// The FRAME is asserted, not assumed: a beam's drawn ends sit half a stem thickness
    /// outside its outer stems (lily/beam.cc:631), so if the vertical strokes found inside
    /// the span do not line up with that, they are not this group's stems and every count
    /// taken from them would be measured against the wrong x.
    /// </remarks>
    private List<double> BeamGroupStems(int beamIndex, int page, out (double Left, double Right) beam)
    {
        var primaries = PrimaryBeams(page);
        if (primaries.Count <= beamIndex)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for beam {beamIndex} but only {primaries.Count} beam "
                + "group(s) were drawn.\nDrawn geometry:\n" + Describe());
        }

        var span = primaries[beamIndex];
        beam = span;
        double half = EngravingDefaults.StemThickness / 2;

        // Staff and ledger lines are horizontal, so "x1 == x2" separates them; a bar line
        // would qualify but cannot fall inside a beam's span.
        // ⚠️ VERTICAL IS NOT ENOUGH — the thickness is part of the identification. A TUPLET
        // BRACKET's two end hooks are vertical strokes of TupletBracket thickness
        // (1.6 line-thicknesses = 0.16) and they land INSIDE the beam's x span whenever a
        // bracket rides its own beam. This instrument counted them as stems and reported
        // "the outermost two are not at the beam's ends" for books whose engraving was
        // correct — it began lying the moment the bracket-visibility rule was corrected to
        // LilyPond's (a beam LONGER than its tuplet draws a bracket). A stem is
        // EngravingDefaults.StemThickness thick; nothing else vertical here is.
        var stems = _pages[page].Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9
                        && Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-9)
            .Select(l => l.X1)
            .Where(x => x >= span.Left - 1e-9 && x <= span.Right + 1e-9)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (stems.Count < 2
            || Math.Abs(stems[0] - (span.Left + half)) > 1e-9
            || Math.Abs(stems[^1] - (span.Right - half)) > 1e-9)
        {
            throw new InvalidOperationException(
                $"page {page}: beam {beamIndex} spans [{span.Left:F6}, {span.Right:F6}] but the "
                + $"vertical strokes inside it are [{string.Join(", ", stems.Select(s => s.ToString("F6")))}] "
                + $"— expected the outermost two at {span.Left + half:F6} and {span.Right - half:F6}."
                + "\nDrawn geometry:\n" + Describe());
        }

        return stems;
    }

    /// <summary>
    /// How far a beam's drawn end reaches past the outer stem it ends over.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:631 <c>horizontal_[d] += d * stem_width / 2</c> — a beam
    /// ends half a stem thickness outside its outer stem, so the corner squares off against
    /// the stem's own edge. It is also the number the quanter's <c>x_span_</c> is measured
    /// with (lily/beam-quanting.cc:419), which is what makes it worth a point of its own:
    /// the quanter and the renderer have to spend the SAME one or the drawn beam is not the
    /// configuration that was scored.
    /// <para>
    /// ⚠️ Deliberately NOT routed through <see cref="BeamGroupStems"/>, which ASSERTS this
    /// very frame in order to trust the strokes it finds. A point that measures a quantity
    /// cannot be built on a helper that assumes it.
    /// </para>
    /// </remarks>
    /// <param name="beamIndex">Which beam group, left to right.</param>
    /// <param name="rightEnd">false = the beam's left end, true = its right end.</param>
    public double BeamOverhangPastOuterStem(int beamIndex, bool rightEnd, int page = 0)
    {
        var span = PrimaryBeamSpan(beamIndex, page);
        var stems = VerticalStrokesUnder(span, page);
        return rightEnd ? span.Right - stems[^1].X : stems[0].X - span.Left;
    }

    /// <summary>
    /// The stroke width a beam group's stems are drawn with — LilyPond's
    /// <c>Stem.thickness</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:909-913 Stem::thickness = thickness x line_thickness.
    /// The property is declared 1.3 at
    /// scm/define-grobs.scm:3469, so a stem is 0.13 staff spaces wide — and a
    /// <c>fontSize</c> does not reach LINE thickness.
    /// <para>
    /// ⚠️ MEASURED, not read (audit/lp-geometry/probes/grace-stem-frame.ly): a grace stem's
    /// drawn X extent is 0.130000 wide, the same as a full-size one, and its x sits 0.065
    /// left of its notehead's right edge in BOTH. LilyPond's answer is the same number twice,
    /// so whatever Lily# puts between the two books is the whole defect.
    /// </para>
    /// </remarks>
    public double BeamGroupStemThickness(int beamIndex, int page = 0)
    {
        var span = PrimaryBeamSpan(beamIndex, page);
        var stems = VerticalStrokesUnder(span, page);
        double first = stems[0].Width;
        foreach (var s in stems)
        {
            if (Math.Abs(s.Width - first) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"page {page}: beam {beamIndex} joins stems of DIFFERENT widths "
                    + $"({string.Join(", ", stems.Select(t => t.Width.ToString("F6")))}) — "
                    + "there is no single thickness to report.\nDrawn geometry:\n" + Describe());
            }
        }
        return first;
    }

    /// <summary>
    /// How thick ONE line of a beam is drawn — LilyPond's <c>Beam.beam-thickness</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm Beam <c>(beam-thickness . 0.48)</c>, and
    /// ly/grace-init.ly <c>Voice.Beam.beam-thickness = #0.384</c> for a grace — a DECLARED
    /// pair of numbers, not a scale applied to one of them. MEASURED on 2.26.0: the grace
    /// beam's drawn quad is 0.304 tall with a 0.08 blot, i.e. 0.384 exactly, against 0.48 for
    /// the same two pitches written as ordinary sixteenths.
    /// <para>
    /// ⚠️ Worth its own point because the quanter and the renderer can disagree about it and
    /// the beam's POSITION will not say so: the height a quant is measured to is the primary
    /// line's centre, which does not move when the line is drawn too thin.
    /// </para>
    /// </remarks>
    public double BeamLineThickness(int beamIndex, int page = 0)
    {
        var lines = FullWidthBeamQuads(beamIndex, page);
        var q = lines[0];
        double atLeft = Math.Abs(q.BottomLeftY - q.TopLeftY);
        double atRight = Math.Abs(q.BottomRightY - q.TopRightY);
        if (Math.Abs(atLeft - atRight) > 1e-9)
        {
            throw new InvalidOperationException(
                $"page {page}: beam {beamIndex} is drawn {atLeft:F6} thick at its left end and "
                + $"{atRight:F6} at its right — a beam is a parallelogram with vertical ends, so "
                + "there is no single thickness to report.\nDrawn geometry:\n" + Describe());
        }
        return atLeft;
    }

    /// <summary>
    /// The distance between the CENTRES of two adjacent lines of a beam stack — LilyPond's
    /// <c>Beam::get_beam_translation</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:130-145 — for fewer than four beams,
    /// <c>(2·staff_space·fract + line·fract − beam_thickness) / 2</c>. ⚠️ The staff space and
    /// the line thickness are scaled by <c>length-fraction</c>; the BEAM THICKNESS IS NOT,
    /// because it arrives already scaled (0.384 from ly/grace-init.ly). LilyPond's own comment
    /// at :138-141 says exactly that — "we divide the thickness by fract".
    /// <para>
    /// MEASURED on 2.26.0 (audit/lp-geometry/probes/beam-grace.ly): the grace Beam grob's drawn
    /// height is 1.316 = 0.384 + 0.648 + its own dy 0.284, so LilyPond's grace translation is
    /// 0.648 — which is <c>0.8 × 0.81</c>, the full-size translation scaled ONCE.
    /// </para>
    /// </remarks>
    public double BeamStackGap(int beamIndex, int page = 0)
    {
        var lines = FullWidthBeamQuads(beamIndex, page);
        if (lines.Count < 2)
        {
            throw new InvalidOperationException(
                $"page {page}: beam {beamIndex} is drawn as {lines.Count} full-width line(s) — "
                + "a stack of at least two is needed before there is a gap to read."
                + "\nDrawn geometry:\n" + Describe());
        }
        var centres = lines
            .Select(q => (q.TopLeftY + q.BottomLeftY) / 2)
            .OrderBy(y => y)
            .ToList();
        double first = centres[1] - centres[0];
        for (int i = 2; i < centres.Count; i++)
        {
            if (Math.Abs(centres[i] - centres[i - 1] - first) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"page {page}: beam {beamIndex}'s lines are not evenly spaced "
                    + $"({string.Join(", ", centres.Select(c => c.ToString("F6")))}) — there is no "
                    + "single translation to report.\nDrawn geometry:\n" + Describe());
            }
        }
        return first;
    }

    /// <summary>
    /// The quads of one beam group that run its FULL width — the stack, with every beamlet
    /// stub left out.
    /// </summary>
    private List<(double TopLeftY, double TopRightY, double BottomRightY, double BottomLeftY)>
        FullWidthBeamQuads(int beamIndex, int page)
    {
        var span = PrimaryBeamSpan(beamIndex, page);
        var lines = _pages[page].Quads
            .Where(q => Math.Abs(Math.Min(q.X0, q.X3) - span.Left) < 1e-9
                        && Math.Abs(Math.Max(q.X1, q.X2) - span.Right) < 1e-9)
            .Select(q => (TopLeftY: q.Y0, TopRightY: q.Y1, BottomRightY: q.Y2, BottomLeftY: q.Y3))
            .ToList();
        if (lines.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: beam {beamIndex} spans [{span.Left:F6}, {span.Right:F6}] but no quad "
                + "was drawn across the whole of it.\nDrawn geometry:\n" + Describe());
        }
        return lines;
    }

    /// <summary>The x span of one drawn beam group's primary line.</summary>
    private (double Left, double Right) PrimaryBeamSpan(int beamIndex, int page)
    {
        var primaries = PrimaryBeams(page);
        if (primaries.Count <= beamIndex)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for beam {beamIndex} but only {primaries.Count} beam "
                + "group(s) were drawn.\nDrawn geometry:\n" + Describe());
        }
        return primaries[beamIndex];
    }

    /// <summary>
    /// The vertical strokes standing under a beam's drawn span, left to right, with the
    /// width each is drawn at. These are its stems: staff and ledger lines are horizontal,
    /// and a bar line cannot fall inside a beam.
    /// </summary>
    private List<(double X, double Width)> VerticalStrokesUnder(
        (double Left, double Right) span, int page)
    {
        var stems = _pages[page].Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9)
            .Where(l => l.X1 >= span.Left - 1e-9 && l.X1 <= span.Right + 1e-9)
            .Select(l => (X: l.X1, Width: l.StrokeWidth))
            .OrderBy(s => s.X)
            .ToList();
        if (stems.Count < 2)
        {
            throw new InvalidOperationException(
                $"page {page}: a beam spanning [{span.Left:F6}, {span.Right:F6}] stands over "
                + $"{stems.Count} vertical stroke(s) — a beam joins at least two stems."
                + "\nDrawn geometry:\n" + Describe());
        }
        return stems;
    }

    /// <summary>
    /// How many beam groups were drawn — where the automatic beaming BROKE.
    /// </summary>
    /// <remarks>
    /// The quantity LilyPond's beat structure decides. ⚠️ ZERO IS A READING, not a failure:
    /// a grid that puts one note in every group leaves nothing to beam, and a bar of eighths
    /// then prints as separate flagged notes. That is exactly the shape of the defect these
    /// points were opened for, so this must not throw on an empty page.
    /// </remarks>
    public int BeamGroupCount(int page = 0) => PrimaryBeams(page).Count;

    /// <summary>
    /// How many stems beam group <paramref name="beamIndex"/> joins — 0 when that group was
    /// not drawn at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ The missing group ANSWERS ZERO rather than throwing, for the same reason
    /// <see cref="BeamGroupCount"/> tolerates an empty page: a wrong beat grid does not draw
    /// a differently-shaped beam, it draws none, and a point that cannot be read at all
    /// records no residual and holds nothing. Asking for a group past the end of a bar that
    /// DID beam is caught by the ledger's LilyPond number instead.
    /// </remarks>
    public int BeamGroupStemCount(int beamIndex, int page = 0)
        => beamIndex < PrimaryBeams(page).Count
            ? BeamGroupStems(beamIndex, page, out _).Count
            : 0;

    /// <summary>
    /// How many stems the LAST beam group of the page joins — 0 when nothing beamed.
    /// </summary>
    /// <remarks>
    /// Named from the end because that is what an uneven grid puts there: 8/8 is 3+3+2 and
    /// its group COUNT plus its FIRST group cannot tell 3+3+2 from 3+2+3.
    /// </remarks>
    public int LastBeamGroupStemCount(int page = 0)
    {
        int n = PrimaryBeams(page).Count;
        return n == 0 ? 0 : BeamGroupStems(n - 1, page, out _).Count;
    }

    /// <summary>Straight strokes in drawing order — stems, staff lines, ledger lines.</summary>
    public IReadOnlyList<DrawnLine> Lines => _page.Lines;

    /// <summary>
    /// The EXTENT of the page's one arpeggio — the stack of <c>scripts.arpeggio</c> copies
    /// as the drawn anchors plus the glyph's own designed box, in device coordinates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ EXTENT, NOT INK, AND THAT IS THE WHOLE POINT. LilyPond's numbers here are grob
    /// extents: the width is the glyph's box (lily/arpeggio.cc:313-319 <c>Arpeggio::width</c>
    /// returns <c>get_squiggle(me).extent(X_AXIS)</c>, declared as the grob's X-extent at
    /// scm/define-grobs.scm:218) and the length is whole copies of it. The glyph's INK
    /// overshoots that box — (-0.004 . 0.804) by (-0.224 . 1.224) against (0 . 0.8) by
    /// (0 . 1.0) — so copies blend where they meet and a pile's ink is 0.448 taller than its
    /// extent. Reading ink would report that overshoot as a divergence from LilyPond that
    /// LilyPond itself has.
    /// </para>
    /// <para>
    /// ⚠️ IT WAS AN INK READING UNTIL THE PORT, and the switch was predicted rather than
    /// discovered: while Lily# DREW the wiggle as stroked line segments there was no declared
    /// box to read, so the reading unioned the stroke rectangles instead. That mechanism is
    /// gone; a reading of it would now find no slanted stroke at all.
    /// </para>
    /// <para>
    /// The drawn SIZE is read from the glyph rather than assumed, so a wiggle set at the
    /// wrong font size — an ossia's, say — fails here instead of passing on the font's box.
    /// </para>
    /// <para>
    /// ⚠️ IT REFUSES ANYTHING BUT ONE STACK: every copy must stand at the same X, since the
    /// pile is one grob. A book with two arpeggios would otherwise have them merged into one
    /// box and read as a very long single wiggle.
    /// </para>
    /// </remarks>
    private IReadOnlyList<(double Left, double Right, double Bottom, double Top, int Copies)>
        ArpeggioStacks()
    {
        var box = LilySharp.Core.Svg.Layout.GlyphMetrics.Arpeggio;
        var stacks = _page.Glyphs
            .Where(g => g.Glyph == LilySharp.Core.Svg.EmmentalerGlyphs.Arpeggio)
            // One grob stands at one X, so the X groups ARE the arpeggios.
            .GroupBy(g => Math.Round(g.X, 9))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var copies = g.OrderBy(c => c.Y).ToList();
                double scale =
                    copies[0].FontSize / LilySharp.Core.Rendering.SharedRenderer.FontSize;
                // Device Y runs DOWN and a glyph's box runs UP from its baseline, so the
                // topmost copy (smallest Y) carries the pile's top edge.
                return (Left: copies[0].X + box.Left * scale,
                        Right: copies[0].X + box.Right * scale,
                        Bottom: copies[^1].Y - box.Bottom * scale,
                        Top: copies[0].Y - box.Top * scale,
                        Copies: copies.Count);
            })
            .ToList();
        if (stacks.Count == 0)
            throw new InvalidOperationException(
                "the probe drew no arpeggio glyph, so there is no wiggle to measure."
                + "\nDrawn geometry:\n" + Describe());
        return stacks;
    }

    private (double Left, double Right, double Bottom, double Top, int Copies) ArpeggioExtent()
    {
        var stacks = ArpeggioStacks();
        if (stacks.Count != 1)
            throw new InvalidOperationException(
                $"the probe drew {stacks.Count} arpeggios and this reading claims one — say "
                + "which.\nDrawn geometry:\n" + Describe());
        return stacks[0];
    }

    /// <summary>
    /// How wide the arpeggio wiggle's ink is — LilyPond's <c>Arpeggio</c> X-extent, which is
    /// the <c>scripts.arpeggio</c> glyph's own width and therefore a font metric.
    /// </summary>
    public double ArpeggioWidth()
    {
        var ext = ArpeggioExtent();
        return ext.Right - ext.Left;
    }

    /// <summary>
    /// The arpeggio wiggle's ink RIGHT edge → the chord's leftmost notehead ink LEFT edge:
    /// LilyPond's <c>Arpeggio</c> <c>padding</c>, 0.5, since the grob is placed by
    /// <c>side-position-interface</c> on the X axis toward <c>LEFT</c>
    /// (scm/define-grobs.scm:208-221) and both edges of that placement are grob extents.
    /// </summary>
    /// <remarks>
    /// The notehead anchor IS its ink left edge (see <see cref="UpStemRightFromHeadAnchor"/>,
    /// where the same anchor plus the head's own width lands on LilyPond's stem to six
    /// digits), and the LEFTMOST head is taken because the placement clears the column: a
    /// head reversed to the far side of the stem is what moves that edge, which is why the
    /// books this reads have no seconds.
    /// </remarks>
    public double ArpeggioRightToNoteheadLeft()
    {
        var heads = Noteheads;
        if (heads.Count == 0)
            throw new InvalidOperationException(
                "the probe drew no notehead for the arpeggio to stand left of."
                + "\nDrawn geometry:\n" + Describe());
        return heads.Min(h => h.X) - ArpeggioExtent().Right;
    }

    /// <summary>
    /// How LONG the arpeggio wiggle's ink is — LilyPond's stack of whole
    /// <c>scripts.arpeggio</c> glyphs, so the length is quantised to the glyph's own height
    /// (lily/arpeggio.cc:180-183: <c>add_at_edge</c> until the pile covers the head interval).
    /// </summary>
    public double ArpeggioLength()
    {
        var ext = ArpeggioExtent();
        return ext.Bottom - ext.Top;   // device Y runs down
    }

    /// <summary>
    /// The nearest notehead ANCHOR to the LEFT of arpeggio <paramref name="index"/> → that
    /// wiggle's left edge: how much room the column before it was given.
    /// </summary>
    /// <remarks>
    /// This is the reading the wiggle's OWN clearance cannot take. A second in a stem-down
    /// chord puts a head a full width left of the column and the wiggle clears THAT head, so
    /// <see cref="ArpeggioRightToNoteheadLeft"/> stays exact however far left the pair sits;
    /// what moves is where they land relative to the PREVIOUS column, i.e. whether the
    /// spacing reserved for the wiggle where it is actually drawn.
    /// <para>
    /// Taken from the head's ANCHOR rather than its ink right so that both sides of the
    /// comparison are drawn quantities — no glyph box enters the reading. LilyPond's own
    /// spring lands the previous ink exactly <c>padding</c> away, so the number is that
    /// head's width plus 0.5.
    /// </para>
    /// </remarks>
    public double PreviousHeadToArpeggio(int index)
    {
        var stacks = ArpeggioStacks();
        if (index < 0 || index >= stacks.Count)
            throw new InvalidOperationException(
                $"asked for arpeggio {index} but the probe drew {stacks.Count}.\n"
                + "Drawn geometry:\n" + Describe());
        double left = stacks[index].Left;
        var before = Noteheads.Where(h => h.X < left - 1e-9).ToList();
        if (before.Count == 0)
            throw new InvalidOperationException(
                $"no notehead is drawn left of arpeggio {index}, so there is no previous "
                + "column to measure from.\nDrawn geometry:\n" + Describe());
        return left - before.Max(h => h.X);
    }

    /// <summary>
    /// A stroked line's INK — the rectangle a butt-capped stroke covers, which is the segment
    /// widened by half the stroke width along its NORMAL only.
    /// </summary>
    /// <remarks>
    /// Stated once, here, because it is the whole content of the chord bracket's reading: a
    /// butt cap does not extend the stroke along its own direction, so a tick drawn from
    /// <c>x</c> to <c>x + 0.4</c> has ink exactly that wide, while the spine it crosses is
    /// <c>x ± thickness/2</c>. Reading a round or square cap instead would hand the bracket
    /// half a thickness at every end and hide precisely the divergence these points hold.
    /// </remarks>
    private static (double Left, double Right, double Top, double Bottom) StrokeInk(DrawnLine l)
    {
        double half = l.StrokeWidth / 2;
        bool vertical = Math.Abs(l.X1 - l.X2) < 1e-9;
        double x0 = Math.Min(l.X1, l.X2), x1 = Math.Max(l.X1, l.X2);
        double y0 = Math.Min(l.Y1, l.Y2), y1 = Math.Max(l.Y1, l.Y2);
        return vertical
            ? (x0 - half, x1 + half, y0, y1)
            : (x0, x1, y0 - half, y1 + half);
    }

    /// <summary>
    /// The EXTENT of the page's one chord BRACKET — the square bracket a NON-arpeggiated chord
    /// gets instead of a wiggle — as the union of the three strokes that draw it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ INK IS THE RIGHT READING HERE, unlike for the wiggle. LilyPond's bracket is not a
    /// glyph but three <c>round_filled_box</c>es (lily/lookup.cc:542-560 <c>Lookup::bracket</c>:
    /// a spine <c>thick</c> wide and a tick at each end), so the grob extents this is compared
    /// against ARE those boxes and there is no designed box overshooting its ink.
    /// </para>
    /// <para>
    /// The bracket is found by ITS OWN SHAPE, not by where it sits: a vertical stroke with
    /// exactly two horizontal strokes that begin at its x and end at its two ends' y. Nothing
    /// else on a page is that — a stem has no ticks, staff and ledger lines are horizontal, a
    /// bar line is a filled rect rather than a line, a beam is a quad. ⚠️ A rule keyed on
    /// "left of every notehead" would have read the first two books and thrown on the third,
    /// where the bracket stands BETWEEN two chords; that is exactly the book the spacing
    /// defect lives in, so the instrument must not depend on the bracket being leftmost.
    /// </para>
    /// </remarks>
    private (double Left, double Right, double Top, double Bottom) ChordBracketExtent()
    {
        var horizontals = _page.Lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9).ToList();
        var found = new List<(DrawnLine Spine, DrawnLine A, DrawnLine B)>();
        foreach (var spine in _page.Lines.Where(l => Math.Abs(l.X1 - l.X2) < 1e-9))
        {
            double top = Math.Min(spine.Y1, spine.Y2), bottom = Math.Max(spine.Y1, spine.Y2);
            // A tick STRADDLES the spine's x rather than starting at it — LilyPond's runs from
            // the spine's LEFT EDGE — and is short. The length bound is what keeps a staff or
            // ledger line from qualifying if a bracket end happens to land on one. The y is
            // matched to within one thickness of an end rather than exactly on it, because
            // LilyPond's ticks lie INSIDE the interval (their midlines sit half a thickness
            // in); a rule demanding equality would read only an engine that draws them
            // centred on the ends, i.e. only the spelling this port replaced.
            double reach = spine.StrokeWidth + 1e-9;
            var ticks = horizontals
                .Where(l => Math.Min(l.X1, l.X2) <= spine.X1 + 1e-9
                            && Math.Max(l.X1, l.X2) >= spine.X1 - 1e-9
                            && Math.Abs(l.X1 - l.X2) < 1.0
                            && (Math.Abs(l.Y1 - top) <= reach || Math.Abs(l.Y1 - bottom) <= reach))
                .ToList();
            if (ticks.Count == 2)
                found.Add((spine, ticks[0], ticks[1]));
        }

        if (found.Count != 1)
            throw new InvalidOperationException(
                $"the probe drew {found.Count} vertical strokes carrying two end ticks and this "
                + "reading claims one chord bracket.\nDrawn geometry:\n" + Describe());

        var ink = new[] { found[0].Spine, found[0].A, found[0].B }.Select(StrokeInk).ToList();
        return (ink.Min(b => b.Left), ink.Max(b => b.Right),
                ink.Min(b => b.Top), ink.Max(b => b.Bottom));
    }

    /// <summary>
    /// How wide the chord bracket is — LilyPond's <c>ly:chord-bracket::width</c>
    /// (lily/arpeggio.cc:216-225), which is the stencil's own X extent
    /// <c>(-thick/2 . thick/2 + protrusion)</c> and therefore <b>wider than the protrusion</b>:
    /// the tick starts at the spine's LEFT edge, not at its centre.
    /// </summary>
    public double ChordBracketWidth()
    {
        var ext = ChordBracketExtent();
        return ext.Right - ext.Left;
    }

    /// <summary>
    /// The chord bracket's ink RIGHT edge → the chord's leftmost notehead ink LEFT edge: the
    /// ChordBracket's own <c>padding</c>, 0.5 (scm/define-grobs.scm:811-835 — its own entry,
    /// not the Arpeggio's), since it too is placed by <c>side-position-interface</c> toward
    /// <c>LEFT</c> on the X axis.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE CONTROL, NOT THE DIVERGENCE, and deliberately so. An engine that both
    /// stands the bracket half a thickness too far right AND draws it half a thickness too
    /// narrow on the left reads EXACT here, because the two cancel. That is why
    /// <see cref="ChordBracketWidth"/> is read beside it: a placement point on its own cannot
    /// see an error that moves the shape and its own edge together.
    /// </remarks>
    public double ChordBracketRightToNoteheadLeft()
    {
        var heads = Noteheads;
        if (heads.Count == 0)
            throw new InvalidOperationException(
                "the probe drew no notehead for the chord bracket to stand left of."
                + "\nDrawn geometry:\n" + Describe());
        return heads.Min(h => h.X) - ChordBracketExtent().Right;
    }

    /// <summary>
    /// How LONG the chord bracket is — <c>positions</c> widened 0.75 either side
    /// (lily/arpeggio.cc:207-214), with NO half-space drop and NO quantising to whole glyphs.
    /// </summary>
    /// <remarks>
    /// Read beside <see cref="ArpeggioLength"/> on the same chord: both grobs are handed the
    /// same head interval and LilyPond answers 3.500000 here against the wiggle's 3.000000, so
    /// the two readings together pin which end treatment each one gets. The end ticks lie
    /// INSIDE the interval in LilyPond (lily/lookup.cc:551-557), so they add no length.
    /// </remarks>
    public double ChordBracketLength()
    {
        var ext = ChordBracketExtent();
        return ext.Bottom - ext.Top;   // device Y runs down
    }

    /// <summary>
    /// The nearest notehead ANCHOR to the LEFT of the chord bracket → the bracket's ink left:
    /// how much room the column before it was given.
    /// </summary>
    /// <remarks>
    /// The reading the bracket's own clearance cannot take, and the counterpart of
    /// <see cref="PreviousHeadToArpeggio"/>. A bracket and the head it clears move together,
    /// so <see cref="ChordBracketRightToNoteheadLeft"/> stays exact whether or not the spacing
    /// ever reserved for the bracket; only a reading from the PREVIOUS column notices. Both
    /// sides are drawn quantities — no glyph box enters it.
    /// </remarks>
    public double PreviousHeadToChordBracket()
    {
        double left = ChordBracketExtent().Left;
        var before = Noteheads.Where(h => h.X < left - 1e-9).ToList();
        if (before.Count == 0)
            throw new InvalidOperationException(
                "no notehead is drawn left of the chord bracket, so there is no previous "
                + "column to measure from.\nDrawn geometry:\n" + Describe());
        return left - before.Max(h => h.X);
    }

    /// <summary>Filled quadrilaterals in drawing order — the beam lines.</summary>
    public IReadOnlyList<DrawnQuad> Quads => _page.Quads;

    /// <summary>Music glyphs in drawing order, left to right.</summary>
    public IReadOnlyList<DrawnGlyph> Glyphs =>
        _page.Glyphs.OrderBy(g => g.X).ToList();

    /// <summary>Plain text runs in drawing order, left to right.</summary>
    public IReadOnlyList<DrawnText> Texts =>
        _page.Texts.OrderBy(t => t.X).ToList();

    /// <summary>
    /// Chord symbols, left to right — the sans-serif text runs. Everything else Lily# draws
    /// as text (title, composer, lyrics, dynamics, rehearsal marks) takes the document's
    /// serif face, so the family is what tells them apart; matching on the STRING would not,
    /// since a chord symbol and a lyric syllable are both arbitrary words.
    /// </summary>
    /// <remarks>
    /// Their <see cref="DrawnText.X"/> is the ink LEFT, which is also the symbol's column:
    /// Lily# draws a chord symbol with <c>TextAnchor.Start</c> (SharedRenderer.Marks), the
    /// port of ChordName declaring no X-offset and no self-alignment-interface at all
    /// (scm/define-grobs.scm:837-855). So a raw anchor is now comparable with LilyPond's.
    /// <para>
    /// ⚠️ The ledger points built on this are still DIFFERENCES of two anchors, and stay that
    /// way: they were designed so the convention cancels, which means they cannot see a
    /// symmetric error in it. <c>ChordSymbolsAreAnchoredAtTheirInkLeft</c> is what asserts the
    /// convention itself.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DrawnText> ChordSymbols =>
        Texts.Where(t => t.Role == TextRole.ChordName).ToList();

    /// <summary>
    /// Lyric syllables, left to right — the serif text runs at the lyric size.
    /// </summary>
    /// <remarks>
    /// The SIZE is what tells a syllable from the other serif text Lily# draws: a title, a
    /// composer line, a rehearsal mark and a dynamic are all serif too, and matching on the
    /// STRING could not tell them apart since a syllable is an arbitrary word. Probes that use
    /// this carry no title, so the filter is belt and braces rather than the only guard.
    /// <para>
    /// ⚠️ IT READS THE SAME CONSTANT THE ENGRAVER AND THE RENDERER DO, and it was a literal
    /// 3.2 until 2026-07-28. When the lyric em moved to LilyPond's own
    /// <c>LyricText</c> size, this filter matched nothing and eighteen ledger points failed
    /// with "no lyric syllable was drawn" — a harness that pins the value it is measuring
    /// against fails LOUDLY, which is the good version of this, but it has no business
    /// holding its own copy.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DrawnText> LyricSyllables =>
        Texts.Where(t => t.Role != TextRole.ChordName
                         && Math.Abs(t.FontSize - LilySharp.Core.Svg.EngravingDefaults.LyricTextFontSize) < 1e-9)
             .ToList();

    /// <summary>
    /// The first (leftmost) syllable's ink CENTRE — the quantity in which the syllable's own
    /// width cancels out of LilyPond's placement, and so the only one comparable across two
    /// engravers whose lyric faces differ.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:117-176 <c>aligned_on_parent</c>. With
    /// <c>self-alignment-X</c> and <c>parent-alignment-X</c> both CENTER (LyricText's
    /// <c>left-align-at-split-notes</c> returns CENTER unless a Completion_heads_engraver split
    /// the head, scm/output-lib.scm:1642-1673, and <c>parent-alignment-X</c> is <c>()</c> so it
    /// copies self), the offset is <c>x = -w/2 + he.centre</c>, hence
    /// <c>ink centre = column + x + w/2 = column + he.centre</c> — <c>w</c> cancels identically.
    /// <para>
    /// Lily# draws a syllable with <c>TextAnchor.Middle</c>, so the recorded anchor IS the ink
    /// centre and no width has to be added back here.
    /// </para>
    /// </remarks>
    public double FirstSyllableInkCentre() => SyllableInkCentre(0);

    /// <summary>
    /// The <paramref name="index"/>-th syllable's ink CENTRE (left to right), with
    /// <see cref="FirstSyllableInkCentre"/>'s width-cancelling rationale and anchor guard.
    /// A STEP of these between same-word syllables is the quantity in which the face
    /// cancels entirely — what the bound-voice mapping points read.
    /// </summary>
    public double SyllableInkCentre(int index)
    {
        var syllables = LyricSyllables;
        if (syllables.Count <= index)
            throw new InvalidOperationException(
                $"the probe drew {syllables.Count} lyric syllable(s), so there is no index "
                + $"{index} (no serif text run at the lyric em size)."
                + "\nDrawn geometry:\n" + Describe());
        var syllable = syllables[index];
        return syllable.Anchor == LilySharp.Core.Rendering.TextAnchor.Middle
            ? syllable.X
            : throw new InvalidOperationException(
                $"a syllable was drawn with TextAnchor.{syllable.Anchor}, so its recorded X is "
                + "not its ink centre and this measurement would be silently wrong.");
    }

    /// <summary>
    /// The first system's staff refpoint down to the baseline of the lyric row under it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 <c>distribute_loose_lines</c> —
    /// a Lyrics line is not spaceable, so it is left out of the page's spring chain and
    /// placed by a second spacer afterwards. This reads where that puts it.
    /// <para>
    /// BOTH SIDES ARE BASELINES and neither carries a font metric. LilyPond's Lyrics
    /// <c>VerticalAxisGroup</c> refpoint is the syllable's baseline (its Y-extent on the
    /// probe is (-0.037044 . 1.820098) — an overshoot below, the ascender above), and a
    /// Lily# syllable is drawn at its baseline. ⚠️ That holds only while the spring's
    /// BASIC-DISTANCE binds; when the alignment floor wins, the quantity acquires the
    /// lyric's own ink and the two engravers' faces differ by ~27% (HANDOFF 5.3). The
    /// probes that use this keep their melody above the middle line for that reason.
    /// </para>
    /// <para>
    /// The row is found by Y rather than by taking the leftmost syllable: every system's
    /// row starts at nearly the same X, so <c>LyricSyllables[0]</c> can belong to any of
    /// them. The smallest Y below the first staff is that staff's own row.
    /// </para>
    /// <para>
    /// PAGE 1 ONLY, and it takes no page argument on purpose: <see cref="Texts"/> reads the
    /// first page alone, so a page parameter here would silently pair one page's staff with
    /// another page's syllables.
    /// </para>
    /// </remarks>
    public double FirstStaffToLyricBaseline() => LyricBaselineBelowStaff(0);

    /// <summary>
    /// The topmost lyric baseline below staff <paramref name="staffIndex"/>, measured from
    /// that staff's reference point.
    /// </summary>
    /// <remarks>
    /// ⚠️ STAVES, not systems — <see cref="StaffRefpoints"/> returns one entry per staff top
    /// of the page down, so on a two-staff score index 1 is the FIRST system's BOTTOM staff.
    /// That is the index a note-bound lyric line is spaced from: a Lyrics line has
    /// staff-affinity UP, so <c>nonstaff-relatedstaff-spacing</c> runs from the staff
    /// directly above it, which page-layout-problem.cc:943-944 records as
    /// <c>last_spaceable_line</c>.
    /// </remarks>
    public double LyricBaselineBelowStaff(int staffIndex, int page = 0)
    {
        var refpoints = StaffRefpoints(page);
        if (staffIndex < 0 || staffIndex >= refpoints.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for staff {staffIndex} but only {refpoints.Count} "
                + "staff/staves are on it.");
        }
        double staff = refpoints[staffIndex];
        var below = LyricSyllables.Where(t => t.Y > staff).ToList();
        if (below.Count == 0)
            throw new InvalidOperationException(
                $"page {page}: no lyric syllable was drawn below staff {staffIndex}'s refpoint "
                + $"({staff:F6}).\nDrawn geometry:\n" + Describe());
        return below.Min(t => t.Y) - staff;
    }
    /// <summary>
    /// <see cref="LyricBaselineBelowStaff"/>'s reading on a page whose staves have
    /// <paramref name="linesPerStaff"/> lines rather than five — a tab staff, whose reference
    /// point is the midpoint of ITS OWN span.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE COUNT IS PASSED, NOT INFERRED, exactly as
    /// <see cref="StaffRefpointsOfLineCount"/> requires and for its reason: a reader that
    /// guesses how many lines a staff has keeps returning a plausible number after the defect
    /// it exists to measure has changed the spacing. A six-string tab's refpoint is 3.750000
    /// below its top line where an ordinary staff's is 2.000000, and reading a lyric line's
    /// distance in the nominal frame is precisely what <c>lyrics.tab.staff-to-lyric</c>
    /// exists to catch.
    /// </remarks>
    public double LyricBaselineBelowStaffOfLineCount(
        int linesPerStaff, int staffIndex = 0, int page = 0)
    {
        var refpoints = StaffRefpointsOfLineCount(linesPerStaff, page);
        if (staffIndex < 0 || staffIndex >= refpoints.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for staff {staffIndex} but only {refpoints.Count} "
                + $"{linesPerStaff}-line staff/staves are on it.");
        }
        double staff = refpoints[staffIndex];
        var below = LyricSyllables.Where(t => t.Y > staff).ToList();
        if (below.Count == 0)
            throw new InvalidOperationException(
                $"page {page}: no lyric syllable was drawn below staff {staffIndex}'s refpoint "
                + $"({staff:F6}).\nDrawn geometry:\n" + Describe());
        return below.Min(t => t.Y) - staff;
    }

    /// <summary>
    /// The lyric baseline nearest ABOVE staff <paramref name="staffIndex"/>'s reference
    /// point — the step that CLOSES a run standing between two staves.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1299-1312 — <c>before</c> is the loose line
    /// and <c>after</c> is spaceable, so a staff-affinity-UP line is held off the staff BELOW
    /// it by its own <c>nonstaff-unrelatedstaff-spacing</c> plus LARGE_STRETCH. That is a
    /// different spring from the one <see cref="LyricBaselineBelowStaff"/> reads, which is
    /// the step from the staff the line BELONGS to; a run between two staves has both, and a
    /// ledger entry that named only one of them would leave the other unwatched.
    /// <para>
    /// ⚠️ Y GROWS DOWNWARD, so "nearest above" is the LARGEST Y among those above — the
    /// mirror of <see cref="ChordBaselineAboveStaff"/>, and for its reason: taking
    /// <c>LyricSyllables[0]</c> would name whichever system happened to start furthest left.
    /// </para>
    /// </remarks>
    public double LyricBaselineAboveStaff(int staffIndex, int page = 0)
    {
        var refpoints = StaffRefpoints(page);
        if (staffIndex < 0 || staffIndex >= refpoints.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for staff {staffIndex} but only {refpoints.Count} "
                + "staff/staves are on it.");
        }
        double staff = refpoints[staffIndex];
        var above = LyricSyllables.Where(t => t.Y < staff).ToList();
        if (above.Count == 0)
            throw new InvalidOperationException(
                $"page {page}: no lyric syllable was drawn above staff {staffIndex}'s refpoint "
                + $"({staff:F6}).\nDrawn geometry:\n" + Describe());
        return staff - above.Max(t => t.Y);
    }

    /// <summary>
    /// Baseline to baseline between the first system's verse 1 and verse 2.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1315-1332 — with two loose lines the
    /// spacing spec is the UPPER line's <c>nonstaff-nonstaff-spacing</c>, not the
    /// <c>nonstaff-relatedstaff-spacing</c> that holds the first line under its staff. So
    /// this is a different quantity from <see cref="FirstStaffToLyricBaseline"/> and needs
    /// its own reading.
    /// <para>
    /// Rows are found by grouping baselines rather than by <c>Distinct()</c>: a row's
    /// syllables share a Y by construction, but comparing doubles for equality would turn a
    /// last-bit difference into a phantom row and silently return zero.
    /// </para>
    /// </remarks>
    public double LyricVerseStep() => LyricVerseStep(0);

    /// <summary>
    /// The same step read below an ARBITRARY staff of page 1 — the two nearest baselines
    /// under staff <paramref name="staffIndex"/>'s reference point.
    /// </summary>
    /// <remarks>
    /// ★ IT EXISTS BECAUSE ONE SCORE CAN HOLD TWO ANSWERS. LilyPond calls
    /// <c>distribute_loose_lines</c> once per RUN, so a system whose alignment this port
    /// cannot express says nothing about the next system down the page; the step between two
    /// row verses is therefore the BAND's on a declining system and the SPEC's 2.800000 on a
    /// solved one, in the same book on the same page. <see cref="LyricVerseStep()"/> reads
    /// staff 0 and so can only ever see the first of the two.
    /// </remarks>
    public double LyricVerseStep(int staffIndex, int page = 0)
    {
        double staff = StaffRefpoints(page)[staffIndex];
        var rows = LyricSyllables.Where(t => t.Y > staff)
                                 .Select(t => t.Y)
                                 .OrderBy(y => y)
                                 .Aggregate(new List<double>(), (acc, y) =>
                                 {
                                     if (acc.Count == 0 || y - acc[^1] > 1e-6) acc.Add(y);
                                     return acc;
                                 });
        if (rows.Count < 2)
            throw new InvalidOperationException(
                $"page {page}: found {rows.Count} lyric row(s) below staff {staffIndex}; a verse "
                + "step needs two.\nDrawn geometry:\n" + Describe());
        return rows[1] - rows[0];
    }

    /// <summary>The glyphs a figure is spelled from — the fetaText digits and the figbass
    /// accidentals — asked of the same house the renderer draws through, so the harness
    /// never holds its own copy of the code points.</summary>
    private static bool IsFiguredBassGlyph(char g) =>
        "0123456789♭♮♯".Any(
            c => GlyphMetrics.TryGetFiguredBassGlyph(c, out char drawn, out _, out _)
                 && drawn == g);

    /// <summary>
    /// Bass figures, left to right — the number-font glyph runs at the figure em.
    /// </summary>
    /// <remarks>
    /// Selected by FACE and SIZE, the way <see cref="LyricSyllables"/> is: the glyph says it
    /// is a bass figure (Emmentaler's fetaText digits with BassFigure's font-features
    /// applied, which no other Lily# grob draws) and the em says it is one of THESE figures.
    /// Both are read from the drawing houses —
    /// <see cref="GlyphMetrics.TryGetFiguredBassGlyph"/> and
    /// <see cref="EngravingDefaults.FiguredBassFontSize"/> — rather than copied, because a
    /// harness that pins the value it measures against stops measuring the day that value
    /// moves (HANDOFF §5.2.1⑤: the lyric em's move failed eighteen points with "no syllable
    /// was drawn", which is the loud version; a copy that still matched would have been the
    /// quiet one).
    /// <para>
    /// ⚠️ IT USED TO SELECT SERIF TEXT AT THE FIGURE EM, which is what Lily# drew until the
    /// face was ported on 2026-07-30. The instrument-name decoy that made the probe books
    /// suppress staff names (LpGeometryProbes' figured-bass block) is gone with it — a name
    /// is text and a figure is a glyph now — but the books keep suppressing them, because
    /// the LilyPond side has no names either.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DrawnGlyph> BassFigures =>
        Glyphs.Where(g => IsFiguredBassGlyph(g.Glyph)
                          && Math.Abs(g.FontSize - EngravingDefaults.FiguredBassFontSize) < 1e-9)
              .ToList();

    /// <summary>
    /// The first notehead's anchor → the FIRST COLUMN's bass figures' box LEFT. Zero in
    /// LilyPond, and the whole content of the reading.
    /// </summary>
    /// <remarks>
    /// MEASURED (probes/figured-bass-placement.ly, book FBLA, 2026-08-11): in EVERY column of
    /// every book the NoteHead, the Stem, the NoteColumn and the BassFigure report the same
    /// box left to fifteen digits (8.703400, 12.978844999134612, …) — so a figure is aligned
    /// on its column's left edge, and for a down stem that edge is the head's own ink left.
    /// NoteHead was added to that probe's dump for this reading, because a COLUMN's left edge
    /// is not always a head's and the two had to be told apart rather than assumed.
    /// <para>
    /// ⚠️ Lily# CENTRES the run instead (<c>SharedRenderer.DrawFiguredBass</c>:
    /// <c>x0 = fb.X − Width/2</c>), which its own remark already calls LILYSHARP-OWN. This is
    /// the point that remark says does not exist yet.
    /// </para>
    /// Both figures of the first column share one X, so the leftmost is taken and the
    /// duplicate is not a choice — the assertion below checks they agree.
    /// </remarks>
    public double NoteheadAnchorToBassFigureBoxLeft(int page = 0)
    {
        var figures = _pages[page].Glyphs
            .Where(g => IsFiguredBassGlyph(g.Glyph)
                        && Math.Abs(g.FontSize - EngravingDefaults.FiguredBassFontSize) < 1e-9)
            .OrderBy(g => g.X)
            .ToList();
        if (figures.Count < 2 || Math.Abs(figures[0].X - figures[1].X) > 1e-9)
        {
            throw new InvalidOperationException(
                "expected the first column's TWO figures to share one X — the probe is not "
                + "measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        return figures[0].X - NoteheadAnchor(0);
    }

    /// <summary>
    /// The topmost bass-figure baseline below staff <paramref name="staffIndex"/>, measured
    /// from that staff's middle line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:1108-1123 <c>\consists Figured_bass_engraver</c>,
    /// <c>VerticalAxisGroup.staff-affinity</c>, <c>nonstaff-relatedstaff-spacing</c> — the
    /// FiguredBass context declares affinity UP and that spec's padding 0.5 and NOTHING else,
    /// so unlike a lyric line (:649-652, basic-distance 5.5) there is no ideal to fall back
    /// on: the realized distance IS the staff's own ink plus 0.5, and it is the staff ABOVE
    /// the line that supplies that ink.
    /// LILYPOND-REF: scm/define-grobs.scm:387-411 <c>side-position-interface</c>,
    /// <c>outside-staff-priority</c>, <c>add-stem-support</c> — BassFigureAlignmentPositioning,
    /// the OTHER device, for figures entered in a Staff context: padding 0.5 and staff-padding
    /// 1.0, per-staff by construction. Both were measured
    /// (audit/lp-geometry/probes/figured-bass-placement.ly) and they agree to fifteen digits.
    /// <para>
    /// ⚠️ BOTH SIDES ARE BASELINES, and this one carries the FACE: with no basic-distance in
    /// either LilyPond device, the figure's own ink ALWAYS binds, so the reading can never
    /// fall into a regime where the digit's cap height cancels (contrast
    /// <see cref="FirstStaffToLyricBaseline"/>, whose probes deliberately stay on the
    /// basic-distance). The three arrangements therefore close by their residuals becoming
    /// EQUAL — the shared face term — and not by reaching zero.
    /// </para>
    /// <para>
    /// <paramref name="staffCount"/> asserts the arrangement rather than assuming it: the
    /// two-staff readings mean what they claim only while the second staff is actually on the
    /// page, and a removed staff would otherwise return a plausible number from the wrong
    /// pairing (HANDOFF §5.0 trap 7).
    /// </para>
    /// <para>PAGE 1 ONLY — <see cref="Texts"/> reads the first page, so a page argument here
    /// would silently pair one page's staff with another page's figures.</para>
    /// </remarks>
    public double FigureBaselineBelowStaff(int staffIndex = 0, int staffCount = 0)
    {
        const int page = 0;
        var refpoints = StaffRefpoints(page);
        if (staffCount > 0 && refpoints.Count != staffCount)
        {
            throw new InvalidOperationException(
                $"page {page}: the arrangement wanted {staffCount} staff/staves and the page "
                + $"has {refpoints.Count} — this reading is not measuring what it claims."
                + "\nDrawn geometry:\n" + Describe());
        }
        if (staffIndex < 0 || staffIndex >= refpoints.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for staff {staffIndex} but only {refpoints.Count} "
                + "staff/staves are on it.");
        }
        double staff = refpoints[staffIndex];
        var below = BassFigures.Where(t => t.Y > staff).ToList();
        if (below.Count == 0)
            throw new InvalidOperationException(
                $"page {page}: no bass figure was drawn below staff {staffIndex}'s middle line "
                + $"({staff:F6}).\nDrawn geometry:\n" + Describe());
        return below.Min(t => t.Y) - staff;
    }

    /// <summary>The first (leftmost) chord symbol's anchor.</summary>
    public double FirstChordSymbolAnchor()
    {
        var chords = ChordSymbols;
        if (chords.Count == 0)
            throw new InvalidOperationException(
                "the probe drew no chord symbol (no sans-serif text run).\nDrawn geometry:\n"
                + Describe());
        return chords[0].X;
    }

    /// <summary>The <paramref name="index"/>-th chord symbol's anchor, left to right.</summary>
    /// <remarks>
    /// Both engravers anchor a chord symbol at its ink LEFT (see <see cref="ChordSymbols"/>),
    /// so a DIFFERENCE of two of these between symbols of the SAME text is convention-free
    /// AND width-free on the right symbol — the quantity the <c>chord.symbol-width.*</c>
    /// points read, in which only the LEFT symbol's priced width survives.
    /// </remarks>
    public double ChordSymbolAnchor(int index)
    {
        var chords = ChordSymbols;
        if (index < 0 || index >= chords.Count)
            throw new InvalidOperationException(
                $"asked for chord symbol {index} but {chords.Count} were drawn.\n"
                + "Drawn geometry:\n" + Describe());
        return chords[index].X;
    }

    /// <summary>
    /// The chord row's baseline above staff <paramref name="staffIndex"/>, measured from
    /// that staff's reference point.
    /// </summary>
    /// <remarks>
    /// THE FIRST OF THE TWO TERMS THE TOP SPRING'S FLOOR IS MADE OF.
    /// <c>page.chord-row.first-staff-refpoint</c> reads where the staff lands, which on the
    /// side where the floor binds is <c>header + (baseline placement + the symbol's ascent)
    /// + padding</c> — ONE number holding TWO, and its residual cannot say which of them
    /// carries it. This reads the placement alone, so the two together split it.
    /// <para>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1257-1338 <c>get_spacing_spec</c> — a
    /// ChordNames line declares <c>staff-affinity</c> DOWN, so the spec that holds it is the
    /// staff BELOW's <c>nonstaff-relatedstaff-spacing</c>, and this is the distance that spec
    /// decides. LilyPond dumps it in as many words
    /// (<c>staff refpoint -&gt; ChordName baseline</c>, Measure-LilyPondPageGeometry.ps1),
    /// so no arithmetic on rounded prints stands between the two engines here.
    /// </para>
    /// <para>
    /// ⚠️ Y GROWS DOWNWARD and the row is ABOVE the staff, so the row belonging to this staff
    /// is the LARGEST Y among those above it — the mirror of
    /// <see cref="LyricBaselineBelowStaff"/>, which takes the smallest Y below. Taking
    /// <c>ChordSymbols[0]</c> instead would name whichever system happened to start furthest
    /// left, exactly as the lyric reader warns.
    /// </para>
    /// <para>
    /// ⚠️ PAGE 1 ONLY by construction, for the reason
    /// <see cref="FirstStaffToLyricBaseline"/> gives: <see cref="Texts"/> reads the first
    /// page alone, so a page argument here can only pair one page's staff with another
    /// page's symbols. It is kept for symmetry with the refpoint readers and must stay 0.
    /// </para>
    /// </remarks>
    public double ChordBaselineAboveStaff(int staffIndex = 0, int page = 0)
    {
        var refpoints = StaffRefpoints(page);
        if (staffIndex < 0 || staffIndex >= refpoints.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for staff {staffIndex} but only {refpoints.Count} "
                + "staff/staves are on it.");
        }
        double staff = refpoints[staffIndex];
        var above = ChordSymbols.Where(t => t.Y < staff).ToList();
        if (above.Count == 0)
            throw new InvalidOperationException(
                $"page {page}: no chord symbol was drawn above staff {staffIndex}'s refpoint "
                + $"({staff:F6}).\nDrawn geometry:\n" + Describe());
        return staff - above.Max(t => t.Y);
    }

    /// <summary>
    /// The chord baseline nearest BELOW staff <paramref name="staffIndex"/>'s reference
    /// point — the OPENING step of a run that stands between two staves.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1284-1294 — <c>before</c> is spaceable and
    /// <c>after</c> is not, so the spring is chosen by the LOWER line's affinity, and a
    /// ChordNames line's is DOWN: it takes its own <c>nonstaff-unrelatedstaff-spacing</c>
    /// plus LARGE_STRETCH, the branch <see cref="ChordBaselineAboveStaff"/> never reaches
    /// (that one reads the staff a chord row belongs to, whose spec is the relatedstaff one).
    /// <para>
    /// ⚠️ Y GROWS DOWNWARD, so "nearest below" is the SMALLEST Y among those below — the
    /// mirror of <see cref="ChordBaselineAboveStaff"/> in both senses.
    /// </para>
    /// </remarks>
    public double ChordBaselineBelowStaff(int staffIndex, int page = 0)
    {
        var refpoints = StaffRefpoints(page);
        if (staffIndex < 0 || staffIndex >= refpoints.Count)
        {
            throw new InvalidOperationException(
                $"page {page}: asked for staff {staffIndex} but only {refpoints.Count} "
                + "staff/staves are on it.");
        }
        double staff = refpoints[staffIndex];
        var below = ChordSymbols.Where(t => t.Y > staff).ToList();
        if (below.Count == 0)
            throw new InvalidOperationException(
                $"page {page}: no chord symbol was drawn below staff {staffIndex}'s refpoint "
                + $"({staff:F6}).\nDrawn geometry:\n" + Describe());
        return below.Min(t => t.Y) - staff;
    }

    /// <summary>Thin bar lines, left to right.</summary>
    public IReadOnlyList<DrawnRect> Barlines =>
        _page.Rects.Where(r => r.Width > 0 && r.Width <= ThinBarlineMaxWidth)
                   .OrderBy(r => r.X).ToList();

    /// <summary>
    /// The thin strokes GROUPED INTO BAR LINES — a double bar is one bar line, not two.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYPOND REPORTS ONE GROB PER BAR LINE WHATEVER ITS GLYPH, so a reading that took
    /// each drawn stroke as a bar line would not be comparing like with like: on a
    /// <c>\bar "||"</c> it names the FIRST stroke where LilyPond's extent covers both, and
    /// every distance measured off it is short by the kern plus a stroke. That is what
    /// <c>courtesy.meter.barline-to-meter.double-bar-numeral</c> read as 1.240000 against
    /// LilyPond's 0.750000 on its first run — the engine was exact (both engines draw the
    /// pair 0.680000 wide) and the instrument was wrong.
    /// <para>
    /// The strokes of ONE bar line are separated by its <c>kern</c>: LilyPond's BarLine
    /// declares <c>(kern . 3.0)</c> and <c>(segno-kern . 3.0)</c> in line-thickness units
    /// (scm/define-grobs.scm:284,289), i.e. 0.300000 staff spaces, while two DIFFERENT bar
    /// lines are a whole measure apart. Anything under one staff space is therefore the same
    /// bar line, with room to spare on both sides.
    /// </para>
    /// </remarks>
    private IReadOnlyList<DrawnRect> BarlineGroups()
    {
        var groups = new List<DrawnRect>();
        foreach (var r in Barlines)
        {
            if (groups.Count > 0)
            {
                var last = groups[^1];
                double gap = r.X - (last.X + last.Width);
                if (gap < MaxBarlineStrokeGap)
                {
                    groups[^1] = last with { Width = r.X + r.Width - last.X };
                    continue;
                }
            }
            groups.Add(r);
        }
        return groups;
    }

    /// <summary>
    /// The widest gap that still belongs to ONE bar line — see <see cref="BarlineGroups"/>.
    /// </summary>
    private const double MaxBarlineStrokeGap = 1.0;

    /// <summary>
    /// The <paramref name="index"/>-th bar line, 0-based, left to right.
    /// </summary>
    /// <remarks>
    /// Index 0 is the bar line between the FIRST and SECOND measures on every probe whose
    /// first measure opens with no bar line — which is all of them but the initial-repeat
    /// books, where the printed <c>.|:</c> at the system start is index 0 (its thin stroke;
    /// <see cref="TimeSignatureToLineStartBarline"/> reads the thick one). A compound bar
    /// (<c>||</c>, <c>|:</c>) is ONE entry here — see <see cref="BarlineGroups"/>.
    /// ⚠️ A final <c>|.</c> still contributes only its thin half, because
    /// <see cref="ThinBarlineMaxWidth"/> filters the thick one out before the grouping runs;
    /// no point reads one today, and closing that wants the thick stroke identified rather
    /// than excluded.
    /// </remarks>
    private DrawnRect Barline(int index)
    {
        var bars = BarlineGroups();
        if (index < 0 || index >= bars.Count)
        {
            throw new InvalidOperationException(
                $"wanted bar line #{index} but the probe drew {bars.Count} "
                + $"(from {Barlines.Count} thin stroke(s); a compound bar counts once). "
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

    /// <summary>
    /// Bar line <paramref name="barIndex"/>'s ink right edge → the END OF THE STAFF LINE it
    /// stands on: everything a line spends after its last musical column, which on a line that
    /// breaks into a key or meter change is the whole end-of-line courtesy group PLUS the gap
    /// the group's last member owes to <c>right-edge</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ TWO DRAWN THINGS, NO METRICS TABLE. Both ends are ink the renderer put down — a bar
    /// line's rect and a staff line's stroke — so the glyph widths inside the span enter
    /// through each engine's own drawing rather than through the table being audited. That is
    /// what lets this point see a wrong courtesy width as well as a wrong gap; which of the
    /// two moved is then read off the neighbouring points, not off this one.
    /// <para>
    /// ⚠️ THE LILYPOND SIDE IS THE LINE EDGE, NOT THE STAFF SYMBOL. LilyPond's StaffSymbol
    /// X-extent is inset by half the line thickness at both ends (0.05), so its right end is
    /// 0.05 short of the line edge; Lily# draws its staff line to the edge. The LilyPond
    /// values recorded for these points therefore add that 0.05 back, and
    /// probes/courtesy-meter.ly says so where it prints them. That inset is a real ±0.05
    /// difference between the engines and is NOT what these points are about.
    /// </para>
    /// <para>
    /// The staff lines are chosen by the Y band the bar line's own stencil spans, so the
    /// answer is about the system this bar line is on and not "whichever staff line was
    /// longest on the page".
    /// </para>
    /// <para>
    /// ⚠️ BUT <paramref name="barIndex"/> ITSELF IS PAGE-WIDE AND ORDERED BY X, not by system
    /// (see <see cref="Barlines"/>). On a two-system book an inner bar line of the SECOND
    /// system can sort ahead of the first system's break bar and steal index 0 — measured, and
    /// it read 48.901495 against LilyPond's 7.749800 while both engines were drawing the right
    /// thing. Every book that feeds this reading is therefore written with ONE measure each
    /// side of the break, which is the shape the other courtesy twins already have.
    /// </para>
    /// </remarks>
    public double BarlineRightToStaffLineEnd(int barIndex)
    {
        var bar = Barline(barIndex);
        double right = bar.X + bar.Width;
        var crossed = Lines
            .Where(l => System.Math.Abs(l.Y1 - l.Y2) < 1e-9
                     && l.Y1 >= bar.Y - 1e-6
                     && l.Y1 <= bar.Y + bar.Height + 1e-6)
            .ToList();
        if (crossed.Count == 0)
            throw new InvalidOperationException(
                $"bar line #{barIndex} (x={bar.X:F6}, y={bar.Y:F6}..{bar.Y + bar.Height:F6}) "
                + "crosses no horizontal line, so there is no staff line to measure its end "
                + $"— the probe drew {Lines.Count} line(s) in all.\nDrawn geometry:\n"
                + Describe());
        return crossed.Max(l => System.Math.Max(l.X1, l.X2)) - right;
    }

    /// <summary>
    /// Fret digits on a tab staff, left to right. LilyPond grob: <c>TabNoteHead</c>.
    /// </summary>
    /// <remarks>
    /// Selected by ROLE, not by size or string: a fret digit and a bar number are both a
    /// numeral in a sans face, and <see cref="TextRole.TabFret"/> is the one thing that tells
    /// them apart — it is what <c>DrawTabFret</c> passes and what
    /// <c>TextFontPlan.Resolve</c> keys on, so this filter cannot drift from the drawing walk
    /// the way <see cref="LyricSyllables"/>' size filter once did.
    /// </remarks>
    public IReadOnlyList<DrawnText> TabFrets =>
        Texts.Where(t => t.Role == TextRole.TabFret).ToList();

    /// <summary>
    /// Bar line <paramref name="barIndex"/>'s ink right edge → the next FRET DIGIT's anchor:
    /// <see cref="BarlineRightToNextGlyph"/>'s quantity on a staff whose note heads are text
    /// rather than Emmentaler glyphs.
    /// </summary>
    /// <remarks>
    /// LilyPond twin: <c>audit/lp-geometry/probes/tab-numbers-meter.ly</c>, which dumps BAR
    /// (ink <c>(0 . 0.19)</c>) and the first TabNoteHead after it. Every ledger point built on
    /// this is a DIFFERENCE of two such gaps, so the two engravers' fret-digit anchor
    /// conventions cancel — the same construction every <c>staffless.*</c> point uses, and for
    /// the same reason.
    /// </remarks>
    public double BarlineRightToNextFret(int barIndex)
    {
        double bar = BarlineRight(barIndex);
        var frets = TabFrets;
        foreach (var f in frets)
            if (f.X > bar + 1e-9)
                return f.X - bar;
        throw new InvalidOperationException(
            $"no fret digit is drawn to the right of bar line #{barIndex} (x={bar:F6}); "
            + $"the probe drew {frets.Count} fret digit(s) in all.\nDrawn geometry:\n"
            + Describe());
    }

    /// <summary>
    /// The gap between fret digit <paramref name="index"/>-1 and fret digit
    /// <paramref name="index"/>, left to right — the quantity a change column standing
    /// BETWEEN two musical columns is spent on.
    /// </summary>
    /// <remarks>
    /// ⚠️ This exists because <see cref="BarlineRightToNextFret"/> cannot see a MID-MEASURE
    /// change at all: a bar line's gap to its own first note is local to that bar line, so a
    /// column widening somewhere upstream leaves it untouched and the reading comes out 0 —
    /// "not measurable" wearing the face of "exact" (HANDOFF 5.3). The pair that opened
    /// mid-measure.tab-numbers.meter-identity read exactly 0.000000000 through the bar-line
    /// accessor before it was moved onto this one.
    /// </remarks>
    public double TabFretStep(int index)
    {
        var frets = TabFrets;
        if (index <= 0 || index >= frets.Count)
        {
            throw new InvalidOperationException(
                $"wanted the step into fret digit #{index} but the probe drew {frets.Count} "
                + "(and the step needs a digit on each side).\nDrawn geometry:\n" + Describe());
        }
        return frets[index].X - frets[index - 1].X;
    }

    /// <summary>
    /// The FLAG's draw origin minus its own NOTEHEAD's — where the flag is DRAWN, with the
    /// column's position divided out.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/flag.cc:198-205 Flag::calc_x_offset — the flag's X-offset is the
    /// stem's own extent <c>[RIGHT]</c>, and lily/flag.cc:118-165 Flag::print returns the
    /// stencil UNTRANSLATED, so the glyph lands exactly on that offset.
    /// LILYPOND-REF: lily/stem.cc:889-906 ly:stem::width (Stem::width, past its is_invisible
    ///   branch) — a stem's extent is <c>Interval (-1, 1) * thickness / 2</c>, so that offset is
    ///   the stem CENTRE plus 0.065, on both stem directions.
    /// <para>
    /// ⚠️ WHY THE NOTEHEAD IS SUBTRACTED rather than reading the flag's absolute x: the two
    /// engines do not put the column in the same place in a book this short, and the quantity
    /// in question is the flag's placement WITHIN its column. Head-relative, the column drops
    /// out of both sides and what is left is the term the source claim is about.
    /// </para>
    /// <para>
    /// ⚠️ The reading is direction-sensitive on purpose: a down stem stands at the head's LEFT
    /// edge and an up stem at its right, so a sign error in the placement would read as
    /// agreement on one of the two. Both are points.
    /// </para>
    /// </remarks>
    public double FlagOriginFromNotehead()
    {
        var flag = Glyphs.FirstOrDefault(g => IsFlag(g.Glyph),
            throw_: "the probe drew no flag; a beamed pair has no Flag grob at all");
        var head = Glyphs.FirstOrDefault(g => IsNotehead(g.Glyph),
            throw_: "the probe drew no notehead");
        return flag.X - head.X;
    }

    /// <summary>The flag glyphs, by codepoint (both directions, every duration).</summary>
    private static bool IsFlag(char g) =>
        Enumerable.Range(1, 6).SelectMany(i => new[]
        {
            EmmentalerGlyphs.GetFlag(1 << (i + 2), true),
            EmmentalerGlyphs.GetFlag(1 << (i + 2), false),
        }).Any(f => f == g);

    /// <summary>
    /// The courtesy CANCELLATION's ink right edge → the new KEY signature's ink left, in the
    /// end-of-line group after bar line <paramref name="barIndex"/>.
    /// </summary>
    /// <remarks>
    /// LilyPond keeps the cancellation and the signature as SEPARATE break-aligned grobs, so
    /// this reads one space-alist entry:
    /// LILYPOND-REF: scm/define-grobs.scm:1930-1964 key-cancellation-interface — KeyCancellation's
    ///   <c>(key-signature . (extra-space . 0.5))</c> at :1944, which
    /// LILYPOND-REF: lily/break-alignment-interface.cc:241-243 Break_alignment_interface::calc_positioning_done
    ///   turns into exactly that ink-to-ink gap (both extents cancel).
    /// <para>
    /// ⚠️ THE TWO SIDES DO NOT AGREE ON GLYPH COUNTS — the trap this file's notehead selector
    /// already names. LilyPond dumps the cancellation as ONE KeyCancellation grob with an
    /// X-extent; Lily# draws one natural per cancelled accidental. So the reading is GROUP ink
    /// right → GROUP ink left on both sides: the LAST natural's anchor plus its own glyph
    /// width, never "the n-th glyph after the bar line".
    /// </para>
    /// <para>
    /// The naturals are the RUN of them that OPENS the group, so a new signature that itself
    /// contained a natural could not be misread as more cancellation.
    /// </para>
    /// </remarks>
    public double CancellationRightToKeyLeft(int barIndex)
    {
        double bar = BarlineRight(barIndex);
        var after = Glyphs.Where(g => g.X > bar + 1e-9).OrderBy(g => g.X).ToList();
        var naturals = after
            .TakeWhile(g => g.Glyph == EmmentalerGlyphs.AccidentalNatural)
            .ToList();
        if (naturals.Count == 0)
            throw new InvalidOperationException(
                $"no cancellation naturals stand after bar line {barIndex}, so there is no "
                + "cancellation-to-key gap to read.\nDrawn geometry:\n" + Describe());
        if (naturals.Count == after.Count || !IsAccidental(after[naturals.Count].Glyph))
            throw new InvalidOperationException(
                "the glyph after the cancellation is not a key accidental.\nDrawn geometry:\n"
                + Describe());

        double cancellationRight = naturals[^1].X
            + LilySharp.Core.Svg.Layout.GlyphMetrics.AccidentalNatural.Width;
        return after[naturals.Count].X - cancellationRight;
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

    /// <summary>The line-start clef glyphs (G, F, C, percussion and their smaller change variants).</summary>
    private static bool IsClef(char g) =>
        g is EmmentalerGlyphs.GClef
          or EmmentalerGlyphs.FClef
          or EmmentalerGlyphs.CClef
          or EmmentalerGlyphs.PercussionClef
          or EmmentalerGlyphs.GClefChange
          or EmmentalerGlyphs.FClefChange
          or EmmentalerGlyphs.CClefChange
          or EmmentalerGlyphs.PercussionClefChange;

    /// <summary>The time-signature glyphs (common/cut and the numerals; digits are
    /// non-contiguous in the font, so they are matched through GetTimeSigDigit).</summary>
    private static bool IsTimeSignature(char g) =>
        g is EmmentalerGlyphs.TimeSigCommon or EmmentalerGlyphs.TimeSigCutCommon
        || Enumerable.Range(0, 10).Any(d => EmmentalerGlyphs.GetTimeSigDigit(d) == g);

    /// <summary>The plain accidental glyphs (sharp, flat, natural and the doubles).</summary>
    private static bool IsAccidental(char g) =>
        g is EmmentalerGlyphs.AccidentalSharp
          or EmmentalerGlyphs.AccidentalFlat
          or EmmentalerGlyphs.AccidentalNatural
          or EmmentalerGlyphs.AccidentalDoubleSharp
          or EmmentalerGlyphs.AccidentalDoubleFlat;

    /// <summary>Accidental glyphs, left to right.</summary>
    public IReadOnlyList<DrawnGlyph> Accidentals =>
        Glyphs.Where(g => IsAccidental(g.Glyph)).ToList();

    /// <summary>
    /// The LINE-START key signature's accidental glyphs, left to right — every accidental
    /// drawn before the TIME SIGNATURE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cut is by IDENTITY (the meter, which always follows the signature and precedes the
    /// music) rather than by a glyph count, because the count is the thing being measured:
    /// <see cref="KeySignatureGlyphCount"/> exists because Lily# can draw a different NUMBER
    /// of accidentals from LilyPond, so an index into the glyph list would stop meaning the
    /// same thing on the two sides — the hazard <see cref="Noteheads"/> records for its own
    /// selection-by-identity.
    /// </para>
    /// <para>
    /// ⚠️ CUTTING AT THE FIRST NOTEHEAD INSTEAD IS WRONG, AND IT LOOKS RIGHT. The first draft
    /// did that and reported EIGHT accidentals for <c>key gis major</c> against LilyPond's
    /// seven — a plausible off-by-one that reads as a defect in the port. It is not: eight
    /// sharps make every letter sharp, so the written <c>c</c> that follows needs a NATURAL
    /// to cancel the key, and that natural is a note's accidental standing between the meter
    /// and the head. The instrument was counting the music (HANDOFF §5.0: the first
    /// disagreement a new instrument reports is the instrument's).
    /// </para>
    /// </remarks>
    public IReadOnlyList<DrawnGlyph> KeySignatureAccidentals
    {
        get
        {
            var meter = Glyphs.Where(g => IsTimeSignature(g.Glyph)).ToList();
            if (meter.Count == 0)
                throw new InvalidOperationException(
                    "no time signature is drawn, so the key signature cannot be cut off the "
                    + "music — write one in the book.\nDrawn geometry:\n" + Describe());
            double meterX = meter[0].X;
            return Glyphs.Where(g => IsAccidental(g.Glyph) && g.X < meterX - 1e-9).ToList();
        }
    }

    /// <summary>
    /// How many accidentals the line-start key signature draws. LilyPond's key signature is
    /// the non-zero entries of <c>alteration-alist</c>, which has one entry per letter, so
    /// this is at most SEVEN however far round the circle of fifths the key sits.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/music-functions-init.ly — `key` is `ly:music-transpose` of a C-based
    /// pitch-alist, so the alist is always seven pairs; scm/output-lib.scm
    /// key-signature-interface::alteration-positions places the non-zero ones.
    /// </remarks>
    public int KeySignatureGlyphCount => KeySignatureAccidentals.Count;

    /// <summary>
    /// How many of the line-start key signature's accidentals are DOUBLE. This is the half of
    /// the reading that a count of glyphs cannot see: a signature eight fifths from C prints
    /// seven symbols either way, and only the first one's identity says whether the eighth
    /// accidental was applied or dropped.
    /// </summary>
    /// <remarks>
    /// LilyPond spells the alteration in WHOLE TONES, so its <c>alteration-alist</c> entry is
    /// 1 for a double sharp and 1/2 for a single — see the probe
    /// audit/lp-geometry/probes/key-signature-wrap.ly, which counts them that way.
    /// </remarks>
    public int KeySignatureDoubleCount =>
        KeySignatureAccidentals.Count(g =>
            g.Glyph is EmmentalerGlyphs.AccidentalDoubleSharp
                    or EmmentalerGlyphs.AccidentalDoubleFlat);

    /// <summary>
    /// The anchor distance from a single-note accidental of the given glyph to the notehead it
    /// sits before — the single-note accidental DRAW gap plus the glyph's own width. LilyPond
    /// seats each accidental so its real skyline / ink clears the head (a natural at 0.367672,
    /// a flat's ink starting 0.12 left of its origin), which the fixed AccidentalNoteGap 0.35
    /// draw path got wrong. The probe carries exactly ONE accidental of this glyph (the note's
    /// key signature uses a different one), so it is unambiguous.
    /// </summary>
    public double AccidentalToNoteheadAnchor(char accidentalGlyph)
    {
        var acc = Glyphs.FirstOrDefault(g => g.Glyph == accidentalGlyph,
            throw_: $"no accidental U+{(int)accidentalGlyph:X4} in the probe.\n"
                    + "Drawn geometry:\n" + Describe());
        foreach (var g in Glyphs)   // Glyphs is left-to-right
            if (g.X > acc.X + 1e-9 && IsNotehead(g.Glyph))
                return g.X - acc.X;
        throw new InvalidOperationException(
            "no notehead after the accidental.\nDrawn geometry:\n" + Describe());
    }

    /// <summary>
    /// A MUSICA FICTA (suggestion) accidental's draw origin against the origin of the
    /// notehead it annotates — positive, because the small glyph is CENTRED over the head.
    /// </summary>
    /// <remarks>
    /// The one reading that puts the whole editorial-accidental arithmetic on the page: the
    /// head's own half-width minus the suggestion's own half-width, so it moves if either
    /// glyph's box moves. Which is the point — the suggestion is drawn at font-size −2 and
    /// therefore out of ANOTHER Emmentaler design (the 16), and reading it against the head
    /// says whether that design reached the drawn page rather than only the metric table.
    /// <para>
    /// Selected by SIZE, not by glyph: a suggestion is drawn at <c>FontSize × magstep(-2)</c>
    /// while an ordinary accidental of the same character is drawn at the full size, so this
    /// stays unambiguous even in a book that carries both.
    /// </para>
    /// </remarks>
    public double SuggestionToNoteheadAnchor(char accidentalGlyph)
    {
        const double fullSize = LilySharp.Core.Rendering.SharedRenderer.FontSize;
        var acc = Glyphs.FirstOrDefault(
            g => g.Glyph == accidentalGlyph && g.FontSize < fullSize - 1e-9,
            throw_: $"no SMALL accidental U+{(int)accidentalGlyph:X4} in the probe — a "
                    + "suggestion is the only accidental drawn below the score's font size.\n"
                    + "Drawn geometry:\n" + Describe());
        var heads = Glyphs.Where(g => IsNotehead(g.Glyph)).ToList();
        if (heads.Count == 0)
            throw new InvalidOperationException(
                "no notehead in the probe.\nDrawn geometry:\n" + Describe());
        var head = heads.OrderBy(g => Math.Abs(g.X - acc.X)).First();
        // The SAME sign LilyPond's dump takes: the suggestion's origin minus its head's.
        return acc.X - head.X;
    }

    /// <summary>Single-note NATURAL accidental to the notehead it precedes (see the char overload).</summary>
    public double NaturalToNoteheadAnchor() =>
        AccidentalToNoteheadAnchor(EmmentalerGlyphs.AccidentalNatural);

    /// <summary>Single-note FLAT accidental to the notehead it precedes (see the char overload).</summary>
    public double FlatToNoteheadAnchor() =>
        AccidentalToNoteheadAnchor(EmmentalerGlyphs.AccidentalFlat);

    /// <summary>
    /// The X gap between the two accidental COLUMNS of the NOTE COLUMN that opens the measure
    /// after bar line <paramref name="barIndex"/>: the nearer (rightmost) accidental's anchor
    /// minus the farther (leftmost) one's. This is the quantity Accidental_placement decides —
    /// how far apart it stacks two accidentals whose glyphs overlap vertically.
    /// </summary>
    /// <remarks>
    /// The column, not the chord: LilyPond packs one AccidentalPlacement per staff MOMENT, so
    /// two accidentals reach this reading either from one chord (probes CSB/CSA/CFB/CFA) or
    /// from two voices standing on one column (probe XCC), and it is the same question both
    /// times — which is why the two agree to fourteen digits.
    /// LILYPOND-REF: lily/accidental-placement.cc:479-518 calc_positioning_done.
    /// <para>
    /// The probes carry exactly two accidentals of the SAME glyph, so whatever left-bearing
    /// that glyph's draw anchor holds cancels in the difference and the number is a pure
    /// column-to-column distance — the same reason the LilyPond side reads it anchor-to-anchor
    /// off two ACC dumps. Selecting by X (not by membership of a chord or a voice) is safe
    /// here because the probes' remaining notes carry no accidental, so the only two after the
    /// bar line are that column's.
    /// </para>
    /// </remarks>
    public double AccidentalColumnGap(int barIndex)
    {
        double bar = BarlineRight(barIndex);
        var accs = Accidentals.Where(a => a.X > bar + 1e-9).OrderBy(a => a.X).ToList();
        if (accs.Count < 2)
            throw new InvalidOperationException(
                $"expected two accidentals after bar line {barIndex} (one note column stacking "
                + $"into two accidental columns) but found {accs.Count}.\nDrawn geometry:\n"
                + Describe());
        return accs[1].X - accs[0].X;
    }

    /// <summary>
    /// The WIDEST head-to-head span of the note column that opens the measure after bar line
    /// <paramref name="barIndex"/> — the displacement <c>note-collision.cc</c> gives the two
    /// voices standing on it.
    /// </summary>
    /// <remarks>
    /// The widest span, not "the first two heads": a chord on one of the two voices puts more
    /// than two heads on the column (probe XCH has three), and what the collision decides is
    /// how far the two GROUPS are apart. With the rest of the measure written as rests, the
    /// heads after the bar line are exactly that column's.
    /// <para>
    /// LILYPOND-REF: lily/note-collision.cc:440-468 calc_positioning_done — each clash group
    /// is translated by <c>amount - left_most</c>, so one group stays on the column and the
    /// span between them is the whole displacement.
    /// </para>
    /// </remarks>
    public double CollidedColumnHeadSpan(int barIndex)
    {
        double bar = BarlineRight(barIndex);
        var heads = Noteheads.Where(h => h.X > bar + 1e-9).OrderBy(h => h.X).ToList();
        if (heads.Count < 2)
            throw new InvalidOperationException(
                $"expected at least two note heads after bar line {barIndex} (two voices on "
                + $"one column) but found {heads.Count}.\nDrawn geometry:\n" + Describe());
        return heads[^1].X - heads[0].X;
    }

    /// <summary>
    /// The NEAREST accidental of the column opening the measure after bar line
    /// <paramref name="barIndex"/> → that column's LEFTMOST note head, anchor to anchor.
    /// </summary>
    /// <remarks>
    /// The leftmost head is the point of the reading. LilyPond packs a staff column's
    /// accidentals against ALL of its heads and does not let them ride the note-collision
    /// shift, so the gap is to the head that stayed on the column — never to the accidental's
    /// own note, which may be the displaced one. Probes XCA and XCB are the same music with
    /// the accidental moved from one voice to the other and MUST read the same number; that
    /// they do is what says the accidental is not riding the shift.
    /// <para>
    /// LILYPOND-REF: lily/accidental-placement.cc:375-385 build_heads_skyline (every voice's
    /// heads) and :391-438 position_apes (the accidentals translate relative to the placement
    /// grob, which is not inside the shifted note column).
    /// </para>
    /// </remarks>
    public double AccidentalToColumnLeftmostHead(int barIndex)
    {
        double bar = BarlineRight(barIndex);
        var accs = Accidentals.Where(a => a.X > bar + 1e-9).OrderBy(a => a.X).ToList();
        if (accs.Count == 0)
            throw new InvalidOperationException(
                $"no accidental stands after bar line {barIndex}.\nDrawn geometry:\n" + Describe());
        var heads = Noteheads.Where(h => h.X > bar + 1e-9).OrderBy(h => h.X).ToList();
        if (heads.Count == 0)
            throw new InvalidOperationException(
                $"no note head stands after bar line {barIndex}.\nDrawn geometry:\n" + Describe());
        return heads[0].X - accs[^1].X;
    }

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

    /// <summary>
    /// Note head <paramref name="index"/>'s ANCHOR → its own up stem's RIGHT edge, 0-based,
    /// left to right. In LilyPond this reading IS the head's own ink width, because the stem
    /// stands at the support head's right edge less half its thickness and is then drawn half
    /// a thickness wide either side of that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED (audit/lp-geometry/probes/stem-x.ly, score SX, one system, three columns at
    /// one pitch): half head (8.585000 . 9.962400) with its stem at (9.832400 . 9.962400), and
    /// black heads (12.860445 . 14.164645) / (15.862690 . 17.166890) with stems at
    /// (14.034645 . 14.164645) / (17.036890 . 17.166890). Stem right and head right coincide
    /// to six digits in all three.
    /// </para>
    /// <para>
    /// ⚠️ IT REFUSES A BOOK WITH MORE HEADS THAN STEMS, and that is not tidiness. In a CHORD of
    /// seconds the stem's origin lands within a head width of the DISPLACED head, so "which
    /// head does this stem stand on" stops being answerable from the drawing — and it stops
    /// being answerable exactly because of the quantity these points measure, which would make
    /// the instrument depend on the answer. HANDOFF 5.0: suspect the instrument first.
    /// </para>
    /// <para>
    /// A stem is told from the other strokes by being VERTICAL and exactly stem-thickness wide:
    /// staff and ledger lines are horizontal, bar lines are rectangles
    /// (see <see cref="Barlines"/>), and beams are quads.
    /// </para>
    /// </remarks>
    public double UpStemRightFromHeadAnchor(int index)
    {
        var heads = Noteheads;
        var stems = Lines
            .Where(l => Math.Abs(l.X1 - l.X2) < 1e-9
                        && Math.Abs(l.StrokeWidth
                                    - LilySharp.Core.Svg.EngravingDefaults.StemThickness) < 1e-9)
            .OrderBy(l => l.X1)
            .ToList();

        if (stems.Count != heads.Count)
            throw new InvalidOperationException(
                $"this reading needs ONE head per stem and the probe drew {heads.Count} head(s) "
                + $"against {stems.Count} stem(s); a chord makes 'which head does this stem "
                + "stand on' undecidable from the drawing.\nDrawn geometry:\n" + Describe());
        if (index < 0 || index >= heads.Count)
            throw new InvalidOperationException(
                $"wanted head #{index} but the probe drew {heads.Count}.\n"
                + "Drawn geometry:\n" + Describe());

        return stems[index].X1 + stems[index].StrokeWidth / 2 - heads[index].X;
    }

    /// <summary>
    /// The notehead anchors on ONE system, left to right — for probes that span several
    /// systems, where <see cref="Noteheads"/> sorts across the whole page and interleaves
    /// them.
    /// </summary>
    /// <remarks>
    /// Systems are told apart by the heads' Y, grouped to a tolerance and ordered top of
    /// the page down, so this only serves a probe whose notes sit at ONE pitch — which is
    /// also what keeps the columns' skylines a plain reach difference. It is the same trap
    /// the LilyPond side has: its own dump script sorts every grob by X and mixes the
    /// systems together, which is why the .ly twin walks one system's columns instead.
    /// </remarks>
    public IReadOnlyList<double> NoteheadAnchorsOnSystem(int systemIndex)
    {
        var groups = Noteheads
            .GroupBy(h => Math.Round(h.Y, 3))
            .OrderBy(g => g.Key)
            .ToList();
        if (systemIndex < 0 || systemIndex >= groups.Count)
            throw new InvalidOperationException(
                $"wanted system #{systemIndex} but the probe drew noteheads at "
                + $"{groups.Count} distinct heights.\nDrawn geometry:\n" + Describe());
        return groups[systemIndex].Select(h => h.X).OrderBy(x => x).ToList();
    }

    /// <summary>
    /// The drawn LEDGER LINES' X span — the quantity LilyPond builds as the head's grob
    /// extent widened by <c>length-fraction</c> (0.25) of that extent's own length.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:228-230 Ledger_line_spanner::print — head_extent = h->extent (common_x, X_AXIS), then ledger_extent.widen (length_fraction * head_extent.length ())
    /// <para>
    /// MEASURED (audit/lp-geometry/probes/notehead-ink-frame.ly, book LDG): LilyPond's
    /// LedgerLineSpanner stencil runs <c>(8.0945 . 11.0375)</c> against a NoteHead
    /// <c>(8.585 . 10.547)</c> — span 2.943000 = 1.962 × 1.5, i.e. BOTH terms are the
    /// head's INK (the advance would give 2.940000).
    /// </para>
    /// ⚠️ THE SPAN, NOT AN EDGE, because the two ends carry the same term with opposite
    /// signs: an error in the ink width shows up twice here and once in either edge alone.
    /// ⚠️ Every ledger line of the book must share one span — this reads a SINGLE note's
    /// ledgers, and a second column's would silently widen the answer, so it is asserted
    /// rather than assumed (LilyPond's own spanner is per-system and had to be read from
    /// its stencil for the same reason).
    /// </remarks>
    public double LedgerLineSpan(int page = 0)
    {
        var ledgers = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                        && Math.Abs(l.StrokeWidth - EngravingDefaults.LegerLineThickness) < 1e-9)
            .ToList();
        if (ledgers.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: no ledger line is drawn — the probe is not measuring what it "
                + "claims.\nDrawn geometry:\n" + Describe());
        }
        double left = ledgers.Min(l => Math.Min(l.X1, l.X2));
        double right = ledgers.Max(l => Math.Max(l.X1, l.X2));
        foreach (var l in ledgers)
        {
            if (Math.Abs(Math.Min(l.X1, l.X2) - left) > 1e-9
                || Math.Abs(Math.Max(l.X1, l.X2) - right) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"page {page}: the {ledgers.Count} ledger lines do not share one X span, "
                    + "so this book has more than one ledgered column and the reading would "
                    + "be their union.\nDrawn geometry:\n" + Describe());
            }
        }
        return right - left;
    }

    /// <summary>
    /// The first notehead's anchor → the first AUGMENTATION DOT's ink left. LilyPond builds
    /// the dot column's base from the head's grob EXTENT and pads by one dot width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dot-column.cc:82-84 Dot_column::calc_positioning_done — base_x.unite (Stem::first_head (parent_stems[i])->extent (commonx, X_AXIS))
    /// <para>
    /// MEASURED (probes/notehead-ink-frame.ly, book DOT): NoteHead <c>(8.489735 .
    /// 10.451735)</c> and Dots <c>(10.901735 . 11.351735)</c>, so the dot's ink left sits
    /// 2.412000 right of the head's anchor = the head's INK 1.962 plus one dot INK 0.45
    /// (not the 1.960 advance, and not the 0.448 dot advance — both forks were named in
    /// the probe header before running and neither fired).
    /// </para>
    /// The dot glyph's own box starts at 0.000000, so its draw origin IS its ink left and
    /// this stays an anchor-to-anchor reading.
    /// </remarks>
    public double NoteheadAnchorToDotInkLeft(int page = 0)
    {
        var dots = _pages[page].Glyphs
            .Where(g => g.Glyph == EmmentalerGlyphs.AugmentationDot)
            .OrderBy(g => g.X)
            .ToList();
        if (dots.Count == 0)
        {
            throw new InvalidOperationException(
                $"page {page}: no augmentation dot is drawn — the probe is not measuring "
                + "what it claims.\nDrawn geometry:\n" + Describe());
        }
        return dots[0].X - NoteheadAnchor(0);
    }

    /// <summary>
    /// The first notehead's anchor → the sole FINGERING's ink centre. A Fingering is
    /// <c>self-alignment-X = CENTER</c> on its head, and what that centres on is the
    /// PARENT's stencil extent — the head's ink.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/self-alignment-interface.cc:147 aligned_on_parent — he = him->extent (him, a), the parent's own stencil extent
    /// <para>
    /// MEASURED (probes/notehead-ink-frame.ly, book FNG): Fingering <c>(9.139209 .
    /// 9.992791)</c> centres on 9.566000 against a NoteHead anchored at 8.585000 — a
    /// difference of 0.981000, which is 1.962/2. The falsifier was 0.980000 (the advance's
    /// half) and it did not fire.
    /// </para>
    /// Lily# draws a fingering as its fetaText digit run at the run's LOGICAL centre
    /// (origin + advance/2 — the same frame LilyPond's text extent answers in), so the
    /// centre this reads is the drawn origin plus half the run's advance.
    /// <para>
    /// ⚠️ HALF A WRONG WIDTH CANCELS HERE, which is why this point stayed exact through a run
    /// that was the wrong cut, the wrong optical design and unhinted all at once. The EDGE is
    /// where that shows: <see cref="NoteheadAnchorToFingeringBoxLeft"/> is its pair, and the
    /// two together are the reason a centre reading is not enough.
    /// </para>
    /// </remarks>
    public double NoteheadAnchorToFingeringCentre(int page = 0)
    {
        var digits = _pages[page].Glyphs
            .Where(g => FingeringDigitChar(g.Glyph) is not null)
            .OrderBy(g => g.X)
            .ToList();
        if (digits.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE fingering digit glyph, found "
                + $"{digits.Count} — the probe is not measuring what it claims.\n"
                + "Drawn geometry:\n" + Describe());
        }
        string text = FingeringDigitChar(digits[0].Glyph)!.Value.ToString();
        return digits[0].X + FingeringGlyphRun.Width(text) / 2.0 - NoteheadAnchor(0);
    }

    /// <summary>
    /// The sole notehead's anchor → the sole FINGERING's box LEFT. The same self-alignment
    /// as the centre reading, one edge out: it comes to <c>head ink centre − width/2</c>, so
    /// it observes the run's WIDTH directly and through the consumer that PLACES the digit.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pango-font.cc:358-360 Pango_font::pango_item_string_stencil — a text
    ///   stencil's X box is the LOGICAL rect,
    ///   so LilyPond's own left edge is the pen origin and this difference is exactly the
    ///   negative half-advance the centring applied. Every book of
    ///   probes/fingering-digit-width.ly dumps <c>xext = (0.0 . …)</c>, which is that fact.
    /// LILYPOND-REF: lily/self-alignment-interface.cc:147 aligned_on_parent — the parent's own
    ///   stencil extent, i.e. the head's INK (1.3042 on a black head, so the centre is 0.6521).
    /// The digit is named rather than indexed for the same reason the ink-edge reader names
    /// one: which digit is where is part of what the fingering books are asking.
    /// </remarks>
    public double NoteheadAnchorToFingeringBoxLeft(char digit, int page = 0)
    {
        var hits = _pages[page].Glyphs
            .Where(g => FingeringDigitChar(g.Glyph) == digit)
            .ToList();
        if (hits.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE fingering digit '{digit}', found "
                + $"{hits.Count} — the probe is not measuring what it claims.\n"
                + "Drawn geometry:\n" + Describe());
        }
        return hits[0].X - NoteheadAnchor(0);
    }

    /// <summary>
    /// The fingering digit <paramref name="digit"/>'s staff-FACING ink edge above the sole
    /// staff's refpoint, signed up-positive: its ink BOTTOM when it sits above the staff,
    /// its ink TOP when below. The quantity the chord-fingering books read.
    /// </summary>
    /// <remarks>
    /// Selected by the DIGIT, never by an index: a chord's fingerings are what the books
    /// measure and their order on the page is precisely what is in question, so an index
    /// would make the reading assume its own answer. The books therefore spell three
    /// DIFFERENT digits (1/3/5), and each one names itself.
    /// <para>
    /// The ink box is the digit run's own — the same <c>FingeringGlyphRun</c> metrics
    /// <c>FingeringEngraver.DigitRun</c> places by and <c>SharedRenderer.DrawFingerings</c>
    /// draws with, so this reads the drawn ink and not a nominal box. LilyPond's side is the
    /// Fingering grob's <c>ext</c>, which is <c>grob::always-Y-extent-from-stencil</c>
    /// (scm/define-grobs.scm:1540-1568) — the same object.
    /// </para>
    /// ⚠️ A fingering's drawn Y is its BASELINE, and a feta digit's ink runs UP from there
    /// (LilyPond dumps ext=(0.0 . 1.12…) on all three digits of both books), so the ink
    /// bottom is the baseline itself and the ink top is the baseline plus the run's height.
    /// </remarks>
    public double FingeringInkEdgeAboveStaff(char digit, bool above = true, int page = 0)
    {
        var refs = StaffRefpoints(page);
        if (refs.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE staff, found {refs.Count} — the probe is "
                + "not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        var hits = _pages[page].Glyphs
            .Where(g => FingeringDigitChar(g.Glyph) == digit)
            .ToList();
        if (hits.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE fingering digit '{digit}', found "
                + $"{hits.Count} — the probe is not measuring what it claims.\n"
                + "Drawn geometry:\n" + Describe());
        }
        string text = digit.ToString();
        double edge = above
            ? FingeringGlyphRun.InkBottom(text)
            : FingeringGlyphRun.InkTop(text);
        // Device-down: an edge `e` staff-spaces up from the drawn origin sits at Y - e.
        return refs[0] - (hits[0].Y - edge);
    }

    /// <summary>
    /// The sole DYNAMIC label's ink TOP above the staff refpoint, signed up-positive — how
    /// far under the staff a <c>\p</c> ends up, and therefore what has to have got out of
    /// its way. The books DYF / DYN / DYU read this one quantity three times.
    /// </summary>
    /// <remarks>
    /// The label's ink is <c>GlyphMetrics.TryGetDynamicInk</c>, the same union of fetaText
    /// letter boxes <c>DynamicEngraver.InkOf</c> reserves by and the same object LilyPond
    /// dumps as the DynamicText grob's Y-extent (<c>grob::always-Y-extent-from-stencil</c>).
    /// ⚠️ Lily# PRINTS a dynamic as bold-italic serif text while it MEASURES it by those
    /// feta boxes, so this reading is the reserved ink, not the printed outline. That
    /// difference is a constant of the label and appears in every book here identically —
    /// which is why the three points are read as a set: what the pair says is the
    /// DIFFERENCE between them, and the label's own metric cancels out of it.
    /// </remarks>
    public double DynamicInkTopAboveStaff(string label = "p", int page = 0)
    {
        var refs = StaffRefpoints(page);
        if (refs.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE staff, found {refs.Count} — the probe is "
                + "not measuring what it claims.\nDrawn geometry:\n" + Describe());
        }
        var hits = _pages[page].Texts
            .Where(t => t.Text == label && t.Role != TextRole.ChordName)
            .ToList();
        if (hits.Count != 1)
        {
            throw new InvalidOperationException(
                $"page {page}: expected exactly ONE dynamic reading \"{label}\", found "
                + $"{hits.Count} — the probe is not measuring what it claims.\n"
                + "Drawn geometry:\n" + Describe());
        }
        if (!GlyphMetrics.TryGetDynamicInk(label, out _, out double inkTop))
        {
            throw new InvalidOperationException(
                $"\"{label}\" is not spelled from the fetaText dynamic letters, so it has no "
                + "ink box to read — pick a label that is.");
        }
        // Device-down: an edge `e` staff-spaces up from the drawn baseline sits at Y - e.
        return refs[0] - (hits[0].Y - inkTop);
    }

    /// <summary>The plain digit a fetaText FINGERING glyph spells, or null.</summary>
    /// <remarks>
    /// ⚠️ NOT THE FIGURED BASS'S TEN GLYPHS, which is what this used to ask
    /// (<c>GlyphMetrics.TryGetFiguredBassGlyph</c>) and what the port these observers watch
    /// took away: a Fingering declares font-features without <c>tnum</c>, so it is set in the
    /// PROPORTIONAL cut where a BassFigure gets the tabular one (<c>FingeringGlyphRun</c>'s
    /// remark carries the reading that says so). Asked of the run itself rather than listed
    /// here, so this cannot become a second spelling of the mapping.
    /// </remarks>
    private static char? FingeringDigitChar(char glyph)
    {
        for (char c = '0'; c <= '9'; c++)
        {
            var pieces = FingeringGlyphRun.Pieces(c.ToString());
            if (pieces.Length == 1 && pieces[0].IsGlyph && pieces[0].Ch == glyph)
                return c;
        }
        return null;
    }


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
    /// Notehead <paramref name="index"/>'s anchor → the next notehead's, 0-based, left to
    /// right. For a probe with one head per column this is the COLUMN STEP.
    /// </summary>
    /// <remarks>
    /// The two engines are compared head-to-head rather than column-to-column because Lily#
    /// draws glyphs, not columns — but the two agree by measurement, not by assumption: in
    /// LilyPond every note head's extent in its own paper column starts at exactly 0.0
    /// (audit/lp-geometry/probes/grace-column-width.ly dumps <c>ext=</c> for every head of
    /// every book, grace and full size alike), so a head anchor IS its column's origin there.
    /// <para>
    /// A head anchor difference is also the only reading that survives the grace scaling: the
    /// heads are different SIZES, so anything measured from a head's right edge or centre
    /// would mix the column step with the font metric.
    /// </para>
    /// </remarks>
    public double NoteheadAnchorStep(int index) =>
        NoteheadAnchor(index + 1) - NoteheadAnchor(index);

    /// <summary>
    /// The clef anchor → first-notehead anchor on system <paramref name="systemIndex"/>
    /// (0-based). This is the quantity <c>Staff_spacing::get_spacing</c>'s
    /// <c>minimum-fixed-space</c> branch decides for a CLEF-ONLY line-start prefix.
    /// </summary>
    /// <remarks>
    /// An INTERIOR system carries a repeated clef but no repeated time signature, so its first
    /// note binds through Clef's <c>(first-note . minimum-fixed-space . 5.0)</c> — the one
    /// break-align spring measured from the left item's LEFT edge with a max
    /// (<c>staff-spacing.cc:183-187</c>: <c>fixed = last_ext[LEFT] + max(last_ext.length(),
    /// distance)</c>), so LilyPond absorbs the clef width into the 5.0 rather than adding it.
    /// System 0 cannot be used: Lily# always draws a meter glyph on it, so its prefix is not
    /// clef-only — which is why this reads an interior system rather than the one-system probes
    /// the rest of the X corpus uses.
    /// <para>
    /// The system is isolated by its staff middle line (<see cref="StaffRefpoints"/>): every
    /// glyph of a one-staff system sits within a few staff spaces of it, and systems are at
    /// least system-system-spacing (12) apart, so a ±6 ss band takes this system's prefix
    /// without reaching the next. Anchor-to-anchor and clef-selected-by-identity, so it needs
    /// no glyph metric and no index a repeated meter could shift.
    /// </para>
    /// </remarks>
    public double ClefToFirstNoteOnSystem(int systemIndex)
    {
        var refs = StaffRefpoints();
        if (systemIndex < 0 || systemIndex >= refs.Count)
            throw new InvalidOperationException(
                $"wanted system #{systemIndex} but the probe drew {refs.Count} system(s). "
                + "This quantity needs an INTERIOR system (index >= 1), whose line-start prefix "
                + "is clef-only.\nDrawn geometry:\n" + Describe());
        double mid = refs[systemIndex];
        const double band = 6.0;
        var onSystem = Glyphs.Where(g => Math.Abs(g.Y - mid) <= band).ToList();
        var clef = onSystem.FirstOrDefault(g => IsClef(g.Glyph),
            throw_: $"no clef on system {systemIndex} (±{band} ss around the middle line "
                    + $"y={mid:F6}).\nDrawn geometry:\n" + Describe());
        foreach (var g in onSystem)   // onSystem preserves Glyphs' left-to-right order
            if (g.X > clef.X + 1e-9 && IsNotehead(g.Glyph))
                return g.X - clef.X;
        throw new InvalidOperationException(
            $"no notehead after the clef on system {systemIndex}.\n"
            + "Drawn geometry:\n" + Describe());
    }

    /// <summary>
    /// The clef anchor → line-start time-signature anchor on the first system. The meter binds
    /// to the clef through Clef.space-alist (time-signature . extra-space 1.52), measured off
    /// the clef's own ink right edge, so this distance rides on the clef ink WIDTH — the
    /// quantity defect-3 (CalculatePrefixWidth once reserving a fixed GClefWidth for every clef)
    /// diverged on, now closed by threading the real per-clef ink (SpacingRules.MaxClefWidth /
    /// GlyphMetrics.LineStartClefWidth). Single-system probe, so the clef is the leftmost glyph and the meter the
    /// first time-signature glyph after it (no key between them on these probes).
    /// </summary>
    public double ClefToTimeSignatureOnFirstSystem()
    {
        var clef = Glyphs.FirstOrDefault(g => IsClef(g.Glyph),
            throw_: "no clef in the probe.\nDrawn geometry:\n" + Describe());
        foreach (var g in Glyphs)   // Glyphs is left-to-right
            if (g.X > clef.X + 1e-9 && IsTimeSignature(g.Glyph))
                return g.X - clef.X;
        throw new InvalidOperationException(
            "no time signature after the clef.\nDrawn geometry:\n" + Describe());
    }

    /// <summary>
    /// The spread (max − min) of the time-signature X across the staves of the first system —
    /// 0 when every staff's meter aligns to one shared break-align column, non-zero when a
    /// staff's meter sits in the wrong column (e.g. a transposed part's wider key not shared).
    /// Metric-free: it compares two glyph anchors in the SAME render, so it depends on no ink
    /// width — LilyPond prints both TimeSignatures at one x and so must Lily#.
    /// </summary>
    public double TimeSignatureAlignmentSpread()
    {
        var refs = StaffRefpoints();
        if (refs.Count < 2)
            throw new InvalidOperationException(
                $"cross-staff time alignment needs >= 2 staves; found {refs.Count}.");
        const double band = 3.5;   // the meter's digits sit within ~2 ss of the middle line
        var xs = new List<double>();
        foreach (var mid in refs)
        {
            var ts = Glyphs
                .Where(g => IsTimeSignature(g.Glyph) && Math.Abs(g.Y - mid) <= band)
                .OrderBy(g => g.X)
                .ToList();
            if (ts.Count == 0)
                throw new InvalidOperationException(
                    $"no time signature on the staff at y={mid:F3}.\nDrawn geometry:\n" + Describe());
            xs.Add(ts[0].X);
        }
        return xs.Max() - xs.Min();
    }

    /// <summary>
    /// The TIME-signature anchor → the first notehead after it, on a single-system probe.
    /// LilyPond places that head at the meter's ink RIGHT + 2.0 — TimeSignature.space-alist
    /// (first-note . (semi-shrink-space . 2.0)) at its natural length under ragged-right —
    /// so the anchor distance rides on the meter's ink width (3.70 for a 4/4).
    /// </summary>
    public double TimeSignatureToFirstNotehead()
    {
        var ts = Glyphs.FirstOrDefault(g => IsTimeSignature(g.Glyph),
            throw_: "no time signature in the probe.\nDrawn geometry:\n" + Describe());
        foreach (var g in Glyphs)   // Glyphs is left-to-right
            if (g.X > ts.X + 1e-9 && IsNotehead(g.Glyph))
                return g.X - ts.X;
        throw new InvalidOperationException(
            "no notehead after the time signature.\nDrawn geometry:\n" + Describe());
    }

    /// <summary>
    /// The TIME-signature anchor → the LEFT ink edge of the bar line that OPENS the line
    /// (a <c>.|:</c>), on a single-system probe whose first measure draws one. This is the
    /// <c>staff-bar</c> column of the begin-of-line break-align group, which LilyPond puts
    /// AFTER the meter at TimeSignature's <c>(staff-bar . (extra-space . 1.0))</c>:
    /// LILYPOND-REF: scm/define-grobs.scm:668-683 break-align-orders (begin of line),
    /// :3945-3953 TimeSignature's space-alist, whose staff-bar entry is the last. 2.700000
    /// for a 4/4.
    /// </summary>
    /// <remarks>
    /// A <c>.|:</c> opens with its THICK stroke, so this takes the first bar-line stroke of
    /// any width right of the meter — <see cref="Barlines"/> keeps thin strokes only, which
    /// here would name the second stroke and read 0.9 too much. Rects wider than a thick
    /// bar line (a section-label box, a hit rect) are not bar lines.
    /// </remarks>
    public double TimeSignatureToLineStartBarline()
    {
        var ts = Glyphs.FirstOrDefault(g => IsTimeSignature(g.Glyph),
            throw_: "no time signature in the probe.\nDrawn geometry:\n" + Describe());
        var strokes = _page.Rects
            .Where(r => r.Width > 0 && r.Width <= EngravingDefaults.ThickBarlineThickness + 1e-6
                        && r.X > ts.X + 1e-9)
            .OrderBy(r => r.X)
            .ToList();
        if (strokes.Count == 0)
            throw new InvalidOperationException(
                "no bar-line stroke after the time signature.\nDrawn geometry:\n" + Describe());
        return strokes[0].X - ts.X;
    }

    /// <summary>
    /// The ossia staff's first key-signature accidental X minus the main staff's, on a
    /// probe whose only accidentals are the two line-start key signatures. Metric-free:
    /// two glyph anchors in the SAME render. LilyPond break-aligns the ossia's
    /// KeySignature into the ONE key column spanning the system — the ossia has no clef,
    /// yet its key prints at the main staff's key X — so the offset there is 0
    /// (break-alignment-interface.cc:141-142, group extent = union across staves; probe
    /// scores OKN/OKNF, and OKM shows the same under \magnifyStaff).
    /// </summary>
    /// <remarks>
    /// The ossia's glyphs are told apart by their scaled font size (the ossia draws at
    /// <c>EngravingDefaults.OssiaScale</c>); the recorder resolves the ossia group
    /// transform, so both X values are absolute page space and directly comparable.
    /// </remarks>
    public double OssiaKeyAlignmentOffset()
    {
        var accs = Accidentals;
        if (accs.Count == 0)
            throw new InvalidOperationException(
                "no accidentals in the probe (expected two key signatures).\n"
                + "Drawn geometry:\n" + Describe());
        double full = accs.Max(g => g.FontSize);
        var main = accs.Where(g => Math.Abs(g.FontSize - full) < 1e-9).ToList();
        var ossia = accs.Where(g => g.FontSize < full - 1e-9).ToList();
        if (ossia.Count == 0)
            throw new InvalidOperationException(
                "no scaled (ossia) accidentals in the probe — the ossia key signature "
                + "was not drawn.\nDrawn geometry:\n" + Describe());
        return ossia.Min(g => g.X) - main.Min(g => g.X);
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
            // Texts too — a staff-less probe draws NO glyph and no bar line worth the name, so
            // without them a failure there would print an empty dump.
            .Concat(Texts.Select(t => (t.X, Label: $"text \"{t.Text}\" {t.Role} {t.Anchor}")))
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
