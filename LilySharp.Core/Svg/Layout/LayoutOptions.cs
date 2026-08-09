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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Options for score layout.
/// All dimensions are in staff spaces.
/// </summary>
/// <remarks>
/// Staff space is the distance between two adjacent staff lines.
/// This is the standard unit in LilyPond and music engraving.
/// </remarks>
internal sealed record LayoutOptions
{
    // === Page Dimensions (in staff spaces) ===

    // ONE conversion underlies every page constant below, and it is the thing that used to
    // be wrong: LilyPond's "point" is the TEX point of 1/72.27 inch, NOT the PostScript big
    // point of 1/72 (LILYPOND-REF: lily/include/dimensions.hh:27 INCH_TO_PT = 72.270, with
    // INCH_TO_BP = 72 kept separately on :31 for the cases that really do mean big points).
    // The default staff is 20pt tall (ly/paper-defaults-init.ly staff-height), so
    //
    //     1 staff space = staff-height / 4 = 5 pt = 5 x 25.4 / 72.27 mm = 127 / 72.27 mm
    //                   = 1.757299 mm       (and mm -> ss is therefore x 72.27 / 127)
    //
    // Reading that 5 pt as PostScript points gives 1.763889 mm and is where the old 168.4
    // and 119.05 came from. Every value below is <millimetres> * 72.27 / 127, and each one
    // agrees to six decimals with what audit/lp-geometry/Measure-LilyPondPageGeometry.ps1
    // reads out of LilyPond 2.26.0.

    /// <summary>Page width in staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/paper.scm:155-171 <c>documented-paper-alist</c> — a4 is
    /// <c>(cons (* 210 mm) (* 297 mm))</c> at :167, and the default paper size is a4
    /// (ly/paper-defaults-init.ly). 210 * 72.27 / 127 = 119.501575 ss through the one
    /// conversion above. Was 119.05, the same paper measured in PostScript points. Earlier
    /// still it was an arbitrary 80, which made line breaking pack ~25% fewer measures per
    /// line than LilyPond on the same A4 paper.
    /// </remarks>
    public double PageWidth { get; init; } = 119.501575;

    /// <summary>Left margin in staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly:93 left-margin-default = 15\mm.
    /// 15 * 72.27 / 127 = 8.535827 ss. Was 8.5 — the right millimetres, rounded.
    /// </remarks>
    public double MarginLeft { get; init; } = 8.535827;

    /// <summary>Right margin in staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly:94 right-margin-default = 15\mm.
    /// 15 * 72.27 / 127 = 8.535827 ss.
    /// </remarks>
    public double MarginRight { get; init; } = 8.535827;

    /// <summary>Top margin in staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly:53 top-margin-default = 10\mm.
    /// 10 * 72.27 / 127 = 5.690551 ss. Was 5. NOTE that 2.24.4 defaulted this to 5 mm and
    /// the bottom to 6 mm; 2.26.0 made both 10 mm, so a figure copied from an older
    /// measurement will not agree.
    /// </remarks>
    public double MarginTop { get; init; } = 5.690551;

    /// <summary>Bottom margin in staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly:54 bottom-margin-default = 10\mm.
    /// 10 * 72.27 / 127 = 5.690551 ss. Was 5.
    /// </remarks>
    public double MarginBottom { get; init; } = 5.690551;

    /// <summary>
    /// Page height in staff spaces. A4 is 297 mm tall: 297 * 72.27 / 127 = 169.009370 ss,
    /// the same scale as <see cref="PageWidth"/> — LilyPond always engraves onto a real
    /// paper size, so long pieces paginate instead of producing one endless page.
    /// Set to 0 or negative for a single content-driven page.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/paper.scm — a4 paper-height. Was 168.4, which is A4 read in
    /// PostScript points (297 / 1.763889) rather than LilyPond's TeX points.
    /// </remarks>
    public double PageHeight { get; init; } = 169.009370;

    // === Staff Dimensions (in staff spaces) ===

    /// <summary>
    /// Staff height in staff spaces — the span from the top LINE to the bottom LINE.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3391-3400 <c>ly:staff-symbol::calc-line-positions</c>
    /// — the StaffSymbol grob declares <c>line-count</c> 5 at :3396 and that callback puts the
    /// lines one staff space apart, so the line span is <c>line-count - 1</c> = 4, four staff
    /// spaces. This is a FRAME rather than a measured quantity, the same
    /// answer <c>StaffMiddle</c> got: LilyPond declares no "staff height" because a staff
    /// space IS its unit, so the number cannot be copied from anywhere — it is what five
    /// lines come to.
    /// ⚠️ NOT THE STAFF'S EXTENT, which is half a line thickness further out at each edge
    /// (±2.05, not ±2.0); SkylineBuilder.SeedStaffSymbol derives that from this rather than
    /// writing 2.05 down.
    /// </remarks>
    public double StaffHeight { get; init; } = 4;

    /// <summary>
    /// Vertical step used while systems are STACKED PROVISIONALLY, before the page solves
    /// where they actually go.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN, AND INERT IN EVERY BOOK MEASURED. The distance between two systems
    /// is decided by the page's spring chain out of <c>VerticalSpacingParameters.SystemSystem</c>
    /// (basic-distance 12, minimum-distance 8, stretchability 60 — ly/paper-defaults-init.ly
    /// system-system-spacing), and PageLayouter overwrites every Y this term contributed. So
    /// it has a reader — LayoutEngine's placement loop — whose answer never survives.
    /// ⚠️ MEASURED 2026-08-04, in both directions: setting it to 37 and to -50 leaves the
    /// whole suite green — 4009 tests including 657 SVG snapshots, not one byte moved. The
    /// positive control is that the same suite is anything but blind to this distance:
    /// changing SystemSystem's basic-distance from 12 to 33 fails 85 tests. A silence with no
    /// control beside it would say nothing (HANDOFF 5.0).
    /// ⚠️ CANDIDATE FOR DELETION, NOT DELETED. Removing it means removing the provisional
    /// stacking term, which changes an intermediate <c>SystemLayout.Y</c> that later passes
    /// read before the page pass runs. That those passes are Y-invariant is IMPLIED by the
    /// perturbation above and has not been shown directly, so it is its own small island.
    /// </remarks>
    public double SystemSpacing { get; init; } = 8;

    /// <summary>
    /// LILYPOND-REF: lily/page-layout-problem.cc:477-478
    /// Padding between header (title) bottom and first system's topmost element.
    /// </summary>
    public double TopSystemPadding { get; init; } = 1;

    // === Layout Algorithm Options ===

    /// <summary>
    /// If true, lines are not justified (stretched to fill width).
    /// Measures are placed at their ideal width, left-aligned.
    /// </summary>
    public bool RaggedRight { get; init; } = false;

    /// <summary>
    /// If true, uses Knuth-Plass optimal line breaking algorithm.
    /// Otherwise uses greedy first-fit algorithm.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/constrained-breaking.cc</remarks>
    public bool UseOptimalLineBreaking { get; init; } = true;

    /// <summary>
    /// If true, ALWAYS runs the optimal page breaker. When false (default),
    /// a score that FITS one page keeps the simple content-driven layout
    /// (byte-identical to the historical output) and the breaker engages
    /// automatically only when the content overflows <see cref="PageHeight"/>.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-spacing.cc</remarks>
    public bool UseOptimalPageBreaking { get; init; } = false;

    /// <summary>
    /// Parameters for page breaking optimization.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-breaking.cc:256-310</remarks>
    public PageBreakingParameters PageBreaking { get; init; } = PageBreakingParameters.Default;

    /// <summary>
    /// Parameters for vertical spacing between systems, markups, and page edges.
    /// </summary>
    /// <remarks>LILYPOND-REF: ly/paper-defaults-init.ly:64-89</remarks>
    public VerticalSpacingParameters VerticalSpacing { get; init; } = VerticalSpacingParameters.Default;

    /// <summary>
    /// Parameters for spacing between staves within a system.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3350-3362 StaffGrouper</remarks>
    public StaffSpacingParameters StaffSpacing { get; init; } = StaffSpacingParameters.Default;

    // === Spacing Parameters ===

    /// <summary>
    /// Spacing increment, approximately notehead width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3239-3256 SpacingSpanner.spacing-increment
    /// Configurable per-score. Default 1.2 staff spaces matches LilyPond.
    /// When set, overrides EngravingDefaults.SpacingIncrement for all spacing calculations.
    /// </remarks>
    public double SpacingIncrement { get; init; } = EngravingDefaults.SpacingIncrement;

    // === Indent (in staff spaces) ===

    /// <summary>
    /// Indentation for the first system in staff spaces.
    /// Creates space for instrument names to the left of staff lines.
    /// Default 0 = auto-calculate from instrument names if present.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly — indent default 15\mm
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset uses indent
    /// </remarks>
    public double Indent { get; init; } = 0;

    /// <summary>
    /// Indentation for subsequent systems in staff spaces.
    /// Default 0 = no indent (matching LilyPond's short-indent default).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly — short-indent default 0\mm
    /// </remarks>
    public double ShortIndent { get; init; } = 0;

    // === Computed Properties ===

    /// <summary>Available width for music content in staff spaces.</summary>
    public double ContentWidth => PageWidth - MarginLeft - MarginRight;

    /// <summary>Default options for standard layout.</summary>
    public static LayoutOptions Default { get; } = new();
}
