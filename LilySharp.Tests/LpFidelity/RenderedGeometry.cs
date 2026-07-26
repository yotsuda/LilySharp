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
        var ys = _pages[page].Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9
                        && Math.Abs(l.StrokeWidth - StaffLineThickness) < 1e-9
                        && Math.Abs(l.X2 - l.X1) >= MinStaffLineSpan)
            .Select(l => l.Y1)
            .ToList();
        if (ys.Count < 2)
            throw new InvalidOperationException(
                $"page {page}: found {ys.Count} staff line(s); a staff span needs at least 2."
                + "\nDrawn geometry:\n" + Describe());
        return ys.Max() - ys.Min();
    }

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
        Texts.Where(t => t.FontFamily != "sans-serif"
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
        Texts.Where(t => t.FontFamily == "sans-serif").ToList();

    /// <summary>
    /// Lyric syllables, left to right — the serif text runs at the lyric size (SharedRenderer
    /// draws them at <c>FontSize * 0.8</c> = 3.2 ss).
    /// </summary>
    /// <remarks>
    /// The SIZE is what tells a syllable from the other serif text Lily# draws: a title, a
    /// composer line, a rehearsal mark and a dynamic are all serif too, and matching on the
    /// STRING could not tell them apart since a syllable is an arbitrary word. Probes that use
    /// this carry no title, so the filter is belt and braces rather than the only guard.
    /// </remarks>
    public IReadOnlyList<DrawnText> LyricSyllables =>
        Texts.Where(t => t.FontFamily != "sans-serif" && Math.Abs(t.FontSize - 3.2) < 1e-9)
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
    public double FirstSyllableInkCentre()
    {
        var syllables = LyricSyllables;
        if (syllables.Count == 0)
            throw new InvalidOperationException(
                "the probe drew no lyric syllable (no serif text run at the 3.2 ss lyric size)."
                + "\nDrawn geometry:\n" + Describe());
        var first = syllables[0];
        return first.Anchor == LilySharp.Core.Rendering.TextAnchor.Middle
            ? first.X
            : throw new InvalidOperationException(
                $"a syllable was drawn with TextAnchor.{first.Anchor}, so its recorded X is not "
                + "its ink centre and this measurement would be silently wrong.");
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
    public double LyricVerseStep()
    {
        const int page = 0;
        double staff = StaffRefpoints(page)[0];
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
                $"page {page}: found {rows.Count} lyric row(s) below the first staff; a verse "
                + "step needs two.\nDrawn geometry:\n" + Describe());
        return rows[1] - rows[0];
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

    /// <summary>Single-note NATURAL accidental to the notehead it precedes (see the char overload).</summary>
    public double NaturalToNoteheadAnchor() =>
        AccidentalToNoteheadAnchor(EmmentalerGlyphs.AccidentalNatural);

    /// <summary>Single-note FLAT accidental to the notehead it precedes (see the char overload).</summary>
    public double FlatToNoteheadAnchor() =>
        AccidentalToNoteheadAnchor(EmmentalerGlyphs.AccidentalFlat);

    /// <summary>
    /// The X gap between the two accidental COLUMNS of the chord that opens the measure after
    /// bar line <paramref name="barIndex"/>: the nearer (rightmost) accidental's anchor minus
    /// the farther (leftmost) one's. This is the quantity Accidental_placement decides —
    /// how far apart it stacks two accidentals whose glyphs overlap vertically.
    /// </summary>
    /// <remarks>
    /// The probes carry exactly two accidentals of the SAME glyph, so whatever left-bearing
    /// that glyph's draw anchor holds cancels in the difference and the number is a pure
    /// column-to-column distance — the same reason the LilyPond side reads it anchor-to-anchor
    /// off two ACC dumps. Selecting by X (not by chord membership) is safe here because the
    /// probe's trailing notes carry no accidental, so the only two after the bar line are the
    /// chord's.
    /// </remarks>
    public double ChordAccidentalColumnGap(int barIndex)
    {
        double bar = BarlineRight(barIndex);
        var accs = Accidentals.Where(a => a.X > bar + 1e-9).OrderBy(a => a.X).ToList();
        if (accs.Count < 2)
            throw new InvalidOperationException(
                $"expected two accidentals after bar line {barIndex} (a chord stacking into "
                + $"two columns) but found {accs.Count}.\nDrawn geometry:\n" + Describe());
        return accs[1].X - accs[0].X;
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
            .Concat(Texts.Select(t => (t.X, Label: $"text \"{t.Text}\" {t.FontFamily} {t.Anchor}")))
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
