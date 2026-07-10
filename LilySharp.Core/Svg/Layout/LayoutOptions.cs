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

    /// <summary>Page width in staff spaces.</summary>
    /// <remarks>
    /// A4 (210mm) at the default 20pt staff: staff-space = 5pt = 1.764mm, so
    /// 210 / 1.764 = 119 staff spaces. Earlier this was an arbitrary 80, which
    /// made line breaking pack ~25% fewer measures per line than LilyPond on the
    /// same A4 paper. ContentWidth now equals LP's 180mm line-width (102 ss).
    /// </remarks>
    public double PageWidth { get; init; } = 119.05;

    /// <summary>Left margin in staff spaces.</summary>
    /// <remarks>LilyPond A4 default left-margin 15mm = 8.5 staff spaces at 20pt.</remarks>
    public double MarginLeft { get; init; } = 8.5;

    /// <summary>Right margin in staff spaces.</summary>
    /// <remarks>LilyPond A4 default right-margin 15mm = 8.5 staff spaces at 20pt.</remarks>
    public double MarginRight { get; init; } = 8.5;

    /// <summary>Top margin in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:49 top-margin</remarks>
    public double MarginTop { get; init; } = 5;

    /// <summary>Bottom margin in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:22 bottom-margin</remarks>
    public double MarginBottom { get; init; } = 5;

    /// <summary>
    /// Page height in staff spaces. Defaults to A4 at the same scale as
    /// <see cref="PageWidth"/> (595×842 pt at ~5 pt per staff space:
    /// width 119.05, height 168.4) — LilyPond always engraves onto a real
    /// paper size, so long pieces paginate instead of producing one
    /// endless page. Set to 0 or negative for a single content-driven page.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm — a4 paper-height</remarks>
    public double PageHeight { get; init; } = 168.4;

    // === Staff Dimensions (in staff spaces) ===

    /// <summary>
    /// Staff height in staff spaces (always 4 for standard 5-line staff).
    /// </summary>
    public double StaffHeight { get; init; } = 4;

    /// <summary>Vertical spacing between systems in staff spaces.</summary>
    public double SystemSpacing { get; init; } = 8;

    /// <summary>
    /// LILYPOND-REF: lily/page-layout-problem.cc:477-478
    /// Padding between header (title) bottom and first system's topmost element.
    /// </summary>
    public double TopSystemPadding { get; init; } = 1;

    // === Spacing Parameters (in staff spaces) ===

    /// <summary>Horizontal padding for collision detection in staff spaces.</summary>
    public double CollisionXPadding { get; init; } = 2;

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
    /// Tolerance for line stretch/compression in optimal breaking.
    /// Lines with ratio outside 1/tolerance to tolerance*2 are rejected.
    /// </summary>
    public double LineBreakingTolerance { get; init; } = 1.1;

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
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3040-3054 StaffGrouper</remarks>
    public StaffSpacingParameters StaffSpacing { get; init; } = StaffSpacingParameters.Default;

    // === Spacing Parameters ===

    /// <summary>
    /// Spacing increment, approximately notehead width.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1550-1580 SpacingSpanner.spacing-increment
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

    // === Part Combination ===

    /// <summary>
    /// If true, two-voice staves are analyzed for part combination and annotated
    /// with "a2" / "Solo" / "Solo II" text. Default false.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/part-combiner.scm — part combination is opt-in via
    /// \partcombine; plain simultaneous voices (&lt;&lt; … \\ … &gt;&gt;) are NOT combined
    /// and carry no a2/Solo text. Lily# has no \partcombine syntax yet, so this
    /// stays off to match LilyPond's default rendering of simultaneous voices.
    /// </remarks>
    public bool EnablePartCombine { get; init; } = false;

    // === Computed Properties ===

    /// <summary>Available width for music content in staff spaces.</summary>
    public double ContentWidth => PageWidth - MarginLeft - MarginRight;

    /// <summary>Default options for standard layout.</summary>
    public static LayoutOptions Default { get; } = new();
}
