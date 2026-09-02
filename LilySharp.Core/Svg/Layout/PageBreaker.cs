// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/page-spacing.cc
//     Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>
//   lily/include/constrained-breaking.hh
//     Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>
//   lily/page-breaking.cc
//     Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>
//   lily/page-layout-problem.cc
//     Copyright (C) 2009--2026 Joe Neeman <joeneeman@gmail.com>
//   lily/constrained-breaking.cc
//     Copyright (C) 2006--2026 Joe Neeman <joeneeman@gmail.com>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A line's silhouette as the page breaker prices it: what is there because the line
/// STARTS here, and what is there anywhere along it.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/constrained-breaking.hh:35-43 Line_shape, whose two
/// intervals come from <c>System::begin_of_line_pure_height</c> and
/// <c>rest_of_line_pure_height</c> (lily/constrained-breaking.cc:512-547).
/// <para>
/// The split is LilyPond's own X-awareness for the BREAKER, and it is a different device
/// from the pointwise skyline the placement chain uses: two buckets, compared bucket to
/// bucket, so a line-start grob is only ever measured against the previous line's
/// line-start grobs. MEASURED in LilyPond 2.26.0 on the deep figured-bass texture
/// (a bass staff, stems down, a figure row under every bar), dumping
/// <c>adjacent-pure-heights</c>: the staff's own buckets are
/// <c>begin (-2.05 . 2.05)</c> — the staff and nothing else — against
/// <c>rest (-10.0 . 2.05)</c>, and the line-start bar number appears in the begin
/// bucket alone. The figure row and the next line's bar number therefore never meet.
/// </para>
/// <para>
/// The two extents below are in Lily#'s system frame, like
/// <see cref="SystemDetails.TopExtent"/> and <see cref="SystemDetails.BottomExtent"/>:
/// UP above the system origin (the top staff's top line), DOWN below the body.
/// </para>
/// </remarks>
internal readonly record struct LineShape(
    double BeginUp, double BeginDown, double RestUp, double RestDown);

/// <summary>
/// Vertical spacing details for a single system (line of music).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/constrained-breaking.hh:45-119 Line_details struct
/// </remarks>
internal sealed record SystemDetails
{
    /// <summary>
    /// This line's two silhouette buckets, when the caller could split them. Absent, the
    /// whole line's extents stand in for both, which is what this breaker did everywhere
    /// before the split existed.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:49 — Line_details' shape_ field,
    /// of type Line_shape.
    /// <para>
    /// ⚠️ LILYSHARP-OWN: THE NULLABILITY. LilyPond's Line_details ALWAYS carries a shape —
    /// fill_line_details fills it for every line (lily/constrained-breaking.cc:547) and the
    /// Prob constructor hands a markup line the same interval twice (:618-619, "pretend it
    /// goes all the way across"). Lily# has callers that cannot split: a system with no
    /// paging skyline, no measures or an empty silhouette, and the hand-built details in
    /// this breaker's own tests. Those get null and are priced by the whole-line extents on
    /// both sides — arithmetic identical to this file before the split, which is the point.
    /// It goes when every producer can split, i.e. when the paging skylines are the only
    /// source of a system's extents. IT IS OBSERVED, so it cannot silently become the live
    /// path: PageBreakerTests' CalcLineHeights_PricesTheBucketsSeparately_… asserts BOTH
    /// branches, and audit/lp-geometry figbass.page.deep.systems-on-first-page is the
    /// end-to-end point that the split one is what reaches the page.
    /// </para>
    /// </remarks>
    public LineShape? Shape { get; init; }

    /// <summary>
    /// Full height of the system including top and bottom extents.
    /// </summary>
    public required double Height { get; init; }

    /// <summary>
    /// Height above the staff top (negative skyline extent).
    /// </summary>
    public required double TopExtent { get; init; }

    /// <summary>
    /// Height below the staff bottom (positive skyline extent).
    /// </summary>
    public required double BottomExtent { get; init; }

    /// <summary>
    /// Staff height (fixed, typically 4 staff spaces).
    /// </summary>
    public required double StaffHeight { get; init; }

    /// <summary>
    /// Compulsory space after this system (padding).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:63 padding_</remarks>
    public double Padding { get; init; }

    /// <summary>
    /// Spring length (natural distance from bottom of this system to top of next).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:69 space_</remarks>
    public double SpringLength { get; init; }

    /// <summary>
    /// Inverse of spring stiffness (higher = more flexible).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:71 inverse_hooke_</remarks>
    public double InverseHooke { get; init; } = 1.0;

    /// <summary>
    /// Penalty for breaking page after this system.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:77 page_penalty_</remarks>
    public double PagePenalty { get; init; }

    /// <summary>
    /// Penalty for line breaking at this system boundary.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:76 break_penalty_</remarks>
    public double BreakPenalty { get; init; }

    /// <summary>
    /// Penalty for page turn after this system (two-sided printing).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:78 turn_penalty_</remarks>
    public double TurnPenalty { get; init; }

    /// <summary>
    /// Whether a page break is forced after this system.
    /// </summary>
    public bool ForceBreakAfter { get; init; }

    /// <summary>
    /// Page break permission after this system.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:74 page_permission_</remarks>
    public BreakPermission PagePermission { get; init; } = BreakPermission.Allow;

    /// <summary>
    /// Whether this is a title/header line (uses title-specific spacing).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:80 title_</remarks>
    public bool IsTitle { get; init; }

    /// <summary>
    /// Minimum distance from refpoint to next system's refpoint.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:66 min_distance_</remarks>
    public double MinDistance { get; init; }

    /// <summary>
    /// Extra padding when this is the last system on a page.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/constrained-breaking.hh:68 bottom_padding_</remarks>
    public double BottomPadding { get; init; }

    /// <summary>
    /// Estimated footnote height for this system (0 if no footnotes).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:186-310 footnote_height()
    /// Footnotes attached to this system consume space at the bottom of the page.
    /// The page breaker subtracts this from available height.
    /// </remarks>
    public double FootnoteHeight { get; init; }

    /// <summary>
    /// How much the stacked page GROWS when this system is added below the previous one
    /// at minimum spacing — not this system's own height.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:61 tallness_, filled in by
    /// lily/page-breaking.cc:1099-1142 Page_breaking::calc_line_heights.
    /// Only the FIRST system on a page contributes its full height; every one after it
    /// contributes this (lily/page-spacing.cc:53-62).
    /// </remarks>
    public double Tallness { get; init; }

    /// <summary>
    /// The refpoint of this system's FIRST spaceable staff, as an offset UP from the
    /// system's origin.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/constrained-breaking.hh:56-60 refpoint_extent_ —
    /// "the refpoints of the first and last spaceable staff in this line; min-distance
    /// should be measured from the bottom refpoint_extent of one line to the top
    /// refpoint_extent of the next".
    /// <para>
    /// LilyPond reads the real grobs. Lily# has no per-staff refpoints in this model, so
    /// they are derived from the geometry it does have: every Lily# staff is a five-line
    /// staff whose refpoint is its centre, so the first staff's refpoint sits half a
    /// staff height below the body's top and the last staff's the same distance above its
    /// bottom. That is exact for the staves Lily# engraves; it would NOT be for a staff
    /// with a different line count, and this is the one place in the port that assumes
    /// something LilyPond looks up.
    /// </para>
    /// </remarks>
    public double RefpointExtentUp { get; init; }

    /// <summary>
    /// The refpoint of this system's LAST spaceable staff, as an offset (negative) DOWN
    /// from the system's origin. See <see cref="RefpointExtentUp"/>.
    /// </summary>
    public double RefpointExtentDown { get; init; }

    /// <summary>
    /// The stretchable space between the bottom of this system's extent and the top of
    /// <paramref name="next"/>'s — the part of the ideal distance that
    /// <see cref="Tallness"/> has NOT already accounted for.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/constrained-breaking.cc:657-667 Line_details::spring_length,
    /// transcribed:
    /// <code>
    ///   Real refpoint_dist
    ///     = tallness_ + refpoint_extent_[DOWN] - next_line.refpoint_extent_[UP];
    ///   Real space = next_line.title_ ? title_space_ : space_;
    ///   return std::max (0.0, space - refpoint_dist);
    /// </code>
    /// The subtraction is the point. This used to be <c>Padding + SpringLength</c>, which
    /// added the whole ideal on top of a rod that already covered the minimum, so the
    /// breaker priced every system about 1 ss more than the layout actually spends and
    /// packed fewer of them per page. What was then left over got stretched back out by
    /// PageLayouter's justification pass, which is why non-last pages came out looser
    /// than the (ragged, unstretched) last one.
    /// </remarks>
    public double SpringLengthTo(SystemDetails next)
    {
        double refpointDist = Tallness + RefpointExtentDown - next.RefpointExtentUp;
        return Math.Max(0.0, SpringLength - refpointDist);
    }
}

/// <summary>
/// Calculates vertical force for a page of systems.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-spacing.cc:31-132 Page_spacing class
/// Similar to horizontal Spring-Rod problem, but simpler because
/// each system only interacts with adjacent systems.
/// </remarks>
internal sealed class PageSpacing
{
    private readonly double _pageHeight;
    // Not readonly: Resize re-seats it, as Page_spacing::resize re-seats page_height_.
    private double _topMargin;
    private readonly double _bottomMargin;
    private readonly VerticalSpacingSpec _topSystem;
    private readonly VerticalSpacingSpec _lastBottom;

    private double _rodHeight;
    private double _springLength;
    private double _inverseSpringK;
    private SystemDetails? _firstSystem;
    private SystemDetails? _lastSystem;

    /// <summary>
    /// Current force (positive = stretch, negative = compress).
    /// </summary>
    public double Force { get; private set; }

    /// <summary>
    /// Total rod height (minimum height).
    /// </summary>
    public double RodHeight => _rodHeight;

    /// <summary>
    /// Total spring length.
    /// </summary>
    public double SpringLength => _springLength;

    public PageSpacing(double pageHeight, double topMargin, double bottomMargin,
        VerticalSpacingSpec topSystem, VerticalSpacingSpec lastBottom)
    {
        _pageHeight = pageHeight;
        _topMargin = topMargin;
        _bottomMargin = bottomMargin;
        _topSystem = topSystem;
        _lastBottom = lastBottom;
        Clear();
    }

    /// <summary>
    /// The whitespace <c>top-system-spacing</c> forces above the FIRST system on a page.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:1785-1802 min_whitespace_at_top_of_page —
    /// <c>translate = max (shape.begin_[UP], shape.rest_[UP])</c> and the result is
    /// <c>max (0, max (padding, minimum-distance - translate))</c>. Lily# carries one
    /// extent per system, so the two shape halves coincide and the max over them is inert
    /// (the same inertness CalcLineHeights transcribes).
    ///
    /// This and its bottom twin are the page breaker's ONLY knowledge of the top-system
    /// and last-bottom springs. Leaving them out let the breaker price a page against a
    /// band 2.000000 ss taller than the one PositionSystemsOnPage then has to fit the
    /// chain into. LILYPOND-REF: lily/page-layout-problem.cc:511-518, :538-545 (the two
    /// springs) — the breaker does not build them, it reserves their minimum here.
    /// </remarks>
    internal double MinWhitespaceAtTopOfPage(SystemDetails line)
        => Math.Max(0.0, Math.Max(_topSystem.Padding, _topSystem.MinimumDistance - line.TopExtent));

    /// <summary>
    /// The whitespace <c>last-bottom-spacing</c> forces below the LAST system on a page.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:1805-1823 min_whitespace_at_bottom_of_page —
    /// <c>translate = min (shape.begin_[DOWN], shape.rest_[DOWN])</c>, a NEGATIVE reach in
    /// the system's own Y-up frame, and the result is
    /// <c>max (0, max (padding, minimum-distance + translate))</c>. The sign is the whole
    /// point: ink hanging below the origin REDUCES what minimum-distance still demands,
    /// which is why the term is added rather than subtracted.
    /// </remarks>
    internal double MinWhitespaceAtBottomOfPage(SystemDetails line)
    {
        double translate = -(line.StaffHeight + line.BottomExtent);   // shape.*_[DOWN]
        return Math.Max(0.0, Math.Max(_lastBottom.Padding, _lastBottom.MinimumDistance + translate));
    }

    /// <summary>
    /// Re-seats the page this accumulator is priced against — the walk of the unconstrained
    /// page DP learns which page a configuration lands on only as it goes, and the first
    /// page's band is shorter by the title header.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-spacing.cc:43-48 Page_spacing::resize — a new
    /// page_height_, then calc_force (). Lily#'s pages differ only in the first page's
    /// header, so what is re-seated is the top margin.</remarks>
    internal void Resize(double topMargin)
    {
        _topMargin = topMargin;
        CalcForce();
    }

    /// <summary>
    /// Resets the spacing calculation.
    /// </summary>
    public void Clear()
    {
        _rodHeight = 0;
        _springLength = 0;
        _inverseSpringK = 0;
        _firstSystem = null;
        _lastSystem = null;
        Force = 0;
    }

    /// <summary>
    /// Appends a system to this page.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/page-spacing.cc:53-72 append_system()</remarks>
    public void AppendSystem(SystemDetails system)
    {
        if (_firstSystem == null)
        {
            // First system on page
            _rodHeight = system.Height;
            _firstSystem = system;
        }
        else
        {
            // LILYPOND-REF: lily/page-spacing.cc:53-57 — only the FIRST system on a page
            // contributes full_height(); every one after it contributes tallness_, the
            // amount the stack GROWS when it is added at minimum spacing
            // (page-breaking.cc:1136). Adding the full height here instead counted each
            // system's own extents a second time, on top of a spring that already spanned
            // them, so the page looked about 1 ss per system fuller than it is.
            _rodHeight += system.Tallness;
            _springLength += _lastSystem!.SpringLengthTo(system);
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:186-310 footnote_height
        // Footnotes consume vertical space at the bottom of the page
        _rodHeight += system.FootnoteHeight;

        _inverseSpringK += system.InverseHooke;
        _lastSystem = system;

        CalcForce();
    }

    /// <summary>
    /// Adds a system at the TOP of this page — the direction LilyPond's page DP walks.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:110-126 prepend_system(), transcribed:
    /// <code>
    ///   if (rod_height_ != 0.0) spring_len_ += line.spring_length (first_line_);
    ///   else                    last_line_ = line;
    ///   rod_height_ -= first_line_.full_height ();
    ///   rod_height_ += first_line_.tallness_;
    ///   rod_height_ += line.full_height ();
    ///   rod_height_ += account_for_footnotes (line);
    ///   inverse_spring_k_ += line.inverse_hooke_;
    ///   first_line_ = line;
    /// </code>
    /// The three rod terms are why <see cref="AppendSystem"/> cannot be run backwards: the
    /// system that WAS first stops paying its full height and starts paying only its
    /// tallness, while the new first one pays its full height. ⚠️ That combination has no
    /// definite sign, so a page does NOT necessarily get taller as it gains a system at the
    /// top — see FindOptimalBreaks for what that costs the early exit.
    ///
    /// ⚠️ TWO DEPARTURES FROM THE LETTER, and they are separate claims — an earlier version of
    /// this remark used the second to justify the first, which settles nothing:
    ///
    /// ⑴ LilyPond runs the three rod terms UNCONDITIONALLY; the null branch here skips two of
    /// them. That is sound because they cancel on an empty page: a default-constructed
    /// Line_details has <c>tallness_ = 0</c>
    /// (LILYPOND-REF: lily/include/constrained-breaking.hh:93-119 Line_details ctor) and
    /// <c>full_height ()</c> unites two default Intervals, which are EMPTY, and an empty
    /// interval's length is 0
    /// (LILYPOND-REF: lily/constrained-breaking.cc:642-648 Line_details::full_height).
    /// ⚠️ This was checked in the source, not assumed.
    ///
    /// ⑵ LilyPond's "is the page still empty" test is <c>rod_height_ != 0.0</c>; Lily# asks
    /// <c>_firstSystem is null</c>. These are NOT the same predicate — a page holding one
    /// system of exactly zero rod height would read empty to LilyPond and non-empty here.
    /// It is the spelling <see cref="AppendSystem"/> already uses, so the two directions stay
    /// consistent with each other, and no SystemDetails in the corpus has zero height; it is
    /// recorded rather than defended.
    /// </remarks>
    public void PrependSystem(SystemDetails system)
    {
        if (_firstSystem != null)
        {
            _springLength += system.SpringLengthTo(_firstSystem);
            _rodHeight -= _firstSystem.Height;
            _rodHeight += _firstSystem.Tallness;
        }
        else
        {
            _lastSystem = system;
        }

        _rodHeight += system.Height;
        _rodHeight += system.FootnoteHeight;
        _inverseSpringK += system.InverseHooke;
        _firstSystem = system;

        CalcForce();
    }

    /// <summary>
    /// Calculates the force needed to fit systems on page.
    /// </summary>
    /// <summary>
    /// The height a page actually offers its systems: the printable band LESS the
    /// whitespace the top-system and last-bottom springs reserve at its two ends.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:30-34 —
    /// <c>page_height_ - min_whitespace_at_top_of_page (first_line_)
    /// - min_whitespace_at_bottom_of_page (last_line_)</c>.
    /// LilyPond's <c>page_height_</c> is Page::calc_printable_height, i.e. the paper less
    /// its margins, which is the subtraction Lily# does inline here.
    /// </remarks>
    public double AvailableHeight
    {
        get
        {
            double band = _pageHeight - _topMargin - _bottomMargin;
            if (_firstSystem is null || _lastSystem is null)
                return band;
            return band - MinWhitespaceAtTopOfPage(_firstSystem)
                        - MinWhitespaceAtBottomOfPage(_lastSystem);
        }
    }

    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:29-41 calc_force().
    ///
    /// ⚠️ <c>last_line_.bottom_padding_</c> is dead weight in LilyPond: the only two
    /// assignments to it are 0 (constrained-breaking.cc:621 and its header's initializer),
    /// so the term is inert there. Lily# used to put the system's own PADDING (1.000000)
    /// in its place, which is a different quantity and made every page 1 ss tighter than
    /// LilyPond prices it. Kept as an explicit zero rather than deleted so the
    /// transcription still lines up with the source.
    /// </remarks>
    private void CalcForce()
    {
        double availableHeight = AvailableHeight;
        const double bottomPadding = 0.0;   // LILYPOND-REF: bottom_padding_, never set nonzero

        if (_rodHeight + bottomPadding >= availableHeight)
        {
            // Overfull page
            Force = double.NegativeInfinity;
        }
        else
        {
            // Force = (available - rod - spring) / flexibility
            Force = (availableHeight - _rodHeight - bottomPadding - _springLength)
                    / Math.Max(0.1, _inverseSpringK);
        }
    }
}

/// <summary>
/// Result of page breaking optimization.
/// </summary>
internal sealed record PageBreakResult
{
    /// <summary>
    /// Total penalty (demerits) of this solution.
    /// </summary>
    public double Penalty { get; init; }

    /// <summary>
    /// Force values for each page.
    /// </summary>
    public ImmutableArray<double> Forces { get; init; }

    /// <summary>
    /// Number of systems on each page.
    /// </summary>
    public ImmutableArray<int> SystemsPerPage { get; init; }

    /// <summary>LILYPOND-REF: lily/page-spacing-result.cc:32-36 page_count.</summary>
    public int PageCount => SystemsPerPage.IsDefault ? 0 : SystemsPerPage.Length;

    /// <summary>The mean of the page forces.
    /// LILYPOND-REF: lily/page-spacing-result.cc:38-47 average_force.</summary>
    public double AverageForce
    {
        get
        {
            if (Forces.IsDefaultOrEmpty)
                return 0;
            double sum = 0;
            foreach (double f in Forces)
                sum += f;
            return sum / Forces.Length;
        }
    }
}

/// <summary>
/// Optimizes page breaking using dynamic programming.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-spacing.cc:134-402 Page_spacer class
/// Uses same DP approach as KnuthPlassBreaker but for vertical (page) dimension.
///
/// Algorithm:
/// Let D(n) = minimum penalty to put systems 0..n on some number of pages
/// Let D(n,k) = minimum penalty to put systems 0..n on exactly k pages
/// Then: D(n,k) = min over j { D(j,k-1) + penalty(j+1..n on one page) }
/// </remarks>
internal sealed class PageBreaker
{
    private readonly double _pageHeight;
    private readonly double _topMargin;
    private readonly double _bottomMargin;
    private readonly double _headerHeight;
    private readonly PageBreakingParameters _params;

    /// <summary>
    /// The vertical specs the breaker needs, which are exactly the two page-END springs:
    /// <c>top-system-spacing</c> and <c>last-bottom-spacing</c>. It reserves their minimum
    /// whitespace (PageSpacing.MinWhitespaceAtTopOfPage / …AtBottomOfPage) rather than
    /// building the springs, which is what PositionSystemsOnPage does afterwards.
    /// </summary>
    /// <remarks>
    /// ⚠️ The breaker deliberately does NOT use top-system-spacing as line 0's per-line
    /// spec. LilyPond gives every line system-system-spacing, first one included
    /// (lily/constrained-breaking.cc:548-555); top-system-spacing reaches the breaker only
    /// through the whitespace term. An earlier reading of this as "breaker and placement
    /// disagree about systemDetails[0]" was a misdiagnosis.
    /// </remarks>
    private readonly VerticalSpacingParameters _vs;

    /// <summary>
    /// Penalty for bad spacing (overflow or extreme stretch).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/page-spacing.hh:45 BAD_SPACING_PENALTY = 1e6</remarks>
    internal const double BadSpacingPenalty = 1e6;

    /// <summary>
    /// Penalty for terrible spacing (ignoring user constraints).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/include/page-spacing.hh:46</remarks>
    private const double TerribleSpacingPenalty = 1e8;

    public PageBreaker(double pageHeight, double topMargin, double bottomMargin, double headerHeight,
        PageBreakingParameters? parameters = null, VerticalSpacingParameters? verticalSpacing = null)
    {
        _pageHeight = pageHeight;
        _topMargin = topMargin;
        _bottomMargin = bottomMargin;
        _headerHeight = headerHeight;
        _params = parameters ?? PageBreakingParameters.Default;
        _vs = verticalSpacing ?? new VerticalSpacingParameters();
    }

    /// <summary>
    /// Breaks systems into pages optimally.
    /// </summary>
    /// <param name="systems">Details for each system.</param>
    /// <returns>Indices where page breaks occur.</returns>
    public List<int> BreakIntoPages(IReadOnlyList<SystemDetails> systems)
    {
        if (systems.Count == 0)
            return new List<int>();

        // Single system always fits on one page
        if (systems.Count == 1)
            return new List<int> { 1 };

        // LILYPOND-REF: lily/page-breaking.cc:1044-1081 cache_line_details — it ends by
        // calling calc_line_heights (:1079), so every line's tallness is known before any
        // page is priced.
        // Doing it here rather than in the caller keeps that ordering, and means no caller
        // can hand the breaker details whose tallness was never computed.
        systems = CalcLineHeights(systems);

        // Use dynamic programming to find optimal breaks
        return FindOptimalBreaks(systems);
    }

    /// <summary>
    /// The pages priced, with the page count UNCONSTRAINED: the page count, the systems per
    /// page, each page's solved force and the penalties the pages incurred — what the
    /// system-count loop compares one line count against another by.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:1413-1424 space_systems_on_best_pages →
    /// lily/page-spacing.cc:147-181 Page_spacer::solve () — the ONE-dimensional DP over
    /// lines (<c>simple_state_</c>, page == VPOS) LilyPond uses when nothing fixes the page
    /// count, then the walk back collecting <c>force_</c> and <c>systems_per_page_</c> per
    /// page and <c>penalty_</c> over all of them. <see cref="FindOptimalBreaks"/> is the
    /// two-dimensional (line, page count) table LilyPond keeps for a FORCED page count
    /// (<c>state_</c>, :183-267); it answers the same question when the best page count is
    /// taken over its columns, at a page-count factor more work — measured on a 200-system
    /// book, 79 candidate counts × 5 ms (session 322 ⑹), which the count loop cannot afford
    /// per keystroke. The paging path keeps the table (its ties are the corpus's), this
    /// path takes LilyPond's own shape for it.
    /// </remarks>
    internal PageBreakResult BreakIntoPagesScored(IReadOnlyList<SystemDetails> systems)
    {
        if (systems.Count == 0)
        {
            return new PageBreakResult
            {
                Penalty = 0,
                Forces = ImmutableArray<double>.Empty,
                SystemsPerPage = ImmutableArray<int>.Empty,
            };
        }
        return SolveUnconstrained(CalcLineHeights(systems));
    }

    /// <summary>
    /// LilyPond's unconstrained page DP: for each line, the cheapest way to end a page on
    /// it, walking the page's start DOWN from the line and prepending a system each step.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:298-405 Page_spacer::calc_subproblem with
    /// page == VPOS, transcribed per line; :147-181 solve () for the walk back. Read beside
    /// the source:
    /// <list type="bullet">
    /// <item>:312-314, :324-332 — the page's height is re-seated as the walk learns which
    /// page the configuration lands on (<see cref="PageSpacing.Resize"/>); Lily#'s pages
    /// differ only in the first page's header, so page_start == 0 is the first page.</item>
    /// <item>:337-349 — prepend, then the overfull exit that spares a page holding one system
    /// (the same exit <see cref="FindOptimalBreaks"/> takes, and the same hedge).</item>
    /// <item>:357-358 — a ragged LAST page that would stretch is priced at force 0.</item>
    /// <item>:360-366 — demerits are the BARE force squared clamped at BAD_SPACING_PENALTY
    /// (the page-spacing weight is applied by <see cref="Demerits"/>, as finalize_spacing_result
    /// applies it), plus the predecessor's.</item>
    /// <item>:368-373 — the line-count penalty, and the page/turn penalty of the line before the
    /// page start (LilyPond charges the turn penalty on even pages only; nothing in Lily# sets
    /// one, so the parity is not modelled).</item>
    /// <item>:386-396 — the first configuration for a line is always recorded, so a line is
    /// never left without a state; :399-402 — the walk stops at a forced page break.</item>
    /// </list>
    /// ⚠️ A page may not END on a line whose page permission is Forbid (Lily#'s
    /// <see cref="IsValidBreak"/> rule). LilyPond's permissions reach its DP through the
    /// breakpoint list (page-breaking.cc:780-875 find_chunks_and_breaks), which Lily# has no
    /// model of; the rule is stated here so both DPs refuse the same page.
    /// </remarks>
    private PageBreakResult SolveUnconstrained(IReadOnlyList<SystemDetails> lines)
    {
        int n = lines.Count;
        var demerits = new double[n];
        var force = new double[n];
        var penalty = new double[n];
        var prev = new int[n];
        Array.Fill(demerits, double.PositiveInfinity);
        Array.Fill(force, double.PositiveInfinity);
        Array.Fill(penalty, double.PositiveInfinity);
        Array.Fill(prev, -1);

        for (int line = 0; line < n; line++)
        {
            bool last = line == n - 1;
            bool ragged = _params.RaggedBottom || (_params.RaggedLastBottom && last);
            bool endsOnForbid = !last && lines[line].PagePermission == BreakPermission.Forbid;
            var space = new PageSpacing(_pageHeight, _topMargin, _bottomMargin,
                _vs.TopSystem, _vs.LastBottom);
            int lineCount = 0;

            for (int pageStart = line; pageStart >= 0; pageStart--)
            {
                int prevIdx = pageStart - 1;
                space.Resize(pageStart == 0 ? _topMargin + _headerHeight : _topMargin);
                space.PrependSystem(lines[pageStart]);

                bool overfull = double.IsNegativeInfinity(space.Force);
                bool tooFewLines = _params.MinSystemsPerPage > 0 && lineCount < _params.MinSystemsPerPage;
                if (!tooFewLines && pageStart < line && overfull)
                    break;

                lineCount++;
                bool prevReachable = prevIdx < 0 || !double.IsPositiveInfinity(demerits[prevIdx]);
                if (!endsOnForbid && prevReachable)
                {
                    double f = space.Force;
                    if (last && ragged && f > 0)
                        f = 0;
                    double dem = Math.Min(f * f, BadSpacingPenalty)
                                 + (prevIdx >= 0 ? demerits[prevIdx] : 0);
                    double pen = CalculateLineCountPenalty(lineCount);
                    if (pageStart > 0)
                        pen += lines[prevIdx].PagePenalty + lines[prevIdx].TurnPenalty;
                    dem += pen;
                    if (dem < demerits[line] || pageStart == line)
                    {
                        demerits[line] = dem;
                        force[line] = f;
                        penalty[line] = pen + (prevIdx >= 0 ? penalty[prevIdx] : 0);
                        prev[line] = prevIdx;
                    }
                }

                if (pageStart > 0 && lines[prevIdx].PagePermission == BreakPermission.Force)
                    break;
            }
        }

        if (double.IsPositiveInfinity(demerits[n - 1]))
        {
            return new PageBreakResult
            {
                Penalty = double.PositiveInfinity,
                Forces = ImmutableArray<double>.Empty,
                SystemsPerPage = ImmutableArray<int>.Empty,
            };
        }

        // LILYPOND-REF: lily/page-spacing.cc:157-180 Page_spacer::solve — the walk back from the last line.
        var forces = new List<double>();
        var perPage = new List<int>();
        int system = n - 1;
        while (system >= 0)
        {
            int p = prev[system];
            forces.Add(force[system]);
            perPage.Add(system - p);
            system = p;
        }
        forces.Reverse();
        perPage.Reverse();
        return new PageBreakResult
        {
            Penalty = penalty[n - 1] + lines[n - 1].PagePenalty + lines[n - 1].TurnPenalty,
            Forces = forces.ToImmutableArray(),
            SystemsPerPage = perPage.ToImmutableArray(),
        };
    }

    /// <summary>
    /// The demerits of one whole configuration — a line count's lines AND the pages they
    /// were put on — the number the system-count loop minimises.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:1548-1586 finalize_spacing_result, transcribed:
    /// <code>
    ///   line_force   = Σ details[i].force_ * details[i].force_
    ///   line_penalty = Σ details[i].break_penalty_
    ///   page_demerits = res.penalty_ + Σ_{pages in range} min (f * f, BAD_SPACING_PENALTY)
    ///   res.demerits_ = line_force + line_penalty + page_demerits * page-spacing-weight
    /// </code>
    /// where the page range is <c>[ragged () ? last : 0, count − (is_last () &amp;&amp;
    /// ragged_last () ? 1 : 0))</c> — ragged pages are not charged for their force, and with
    /// ragged-last-bottom (the default) the last page never is. <c>is_last ()</c> is true:
    /// Lily# pages one book.
    /// <para>
    /// ⚠️ THERE IS NO (prev − force)² TERM. The line DP minimises force² + Δforce² per line
    /// (constrained-breaking.cc:568-573); the page score charges force² alone. A forced
    /// break that leaves a line very underfull makes every Δ against it expensive, so the
    /// line DP splits its neighbour to soften the step while this score does not — measured
    /// on scratch/p321/fx/bis-v6-proper-rests-first.lys (session 321): the line DP's
    /// 4-system demerits 74.2 against 63.1 for 3, LilyPond's page score 42.47 against 38.78
    /// the other way. That difference is the 69-book B-eng family of HANDOFF §2 T7.
    /// </para>
    /// </remarks>
    internal double Demerits(PageBreakResult pages, double lineForceSquared, double lineBreakPenalty)
    {
        if (pages.Forces.IsDefaultOrEmpty)
            return double.PositiveInfinity;

        double pageDemerits = pages.Penalty;
        int count = pages.Forces.Length;
        int from = _params.RaggedBottom ? count - 1 : 0;
        int to = count - (_params.RaggedLastBottom ? 1 : 0);
        for (int i = from; i < to; i++)
        {
            double f = pages.Forces[i];
            pageDemerits += Math.Min(f * f, BadSpacingPenalty);
        }
        return lineForceSquared + lineBreakPenalty + pageDemerits * _params.PageSpacingWeight;
    }

    /// <summary>
    /// The fewest pages the systems can be stacked on at MINIMUM spacing — the bound the
    /// system-count loop uses to skip a line count that could not keep the ideal page count.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:1186-1278 Page_breaking::min_page_count,
    /// transcribed. A greedy stack: each system adds its full height when it opens a page
    /// and its tallness otherwise (the same two quantities <see cref="PageSpacing"/> sums);
    /// ragged pages count their springs too; a page closes when the next system would not
    /// fit, when it would exceed max-systems-per-page, or after a forced page break. The
    /// last page is then re-checked at its own height, and one more page is added when the
    /// stack overflows it and holds more than that page's last system alone (:1257-1275).
    /// <para>
    /// LilyPond's <c>page_height (num, last)</c> varies per page with headers and footers;
    /// Lily#'s pages differ only in the first page's title header, so the first page's band
    /// is the printable height less the header and every other page's is the printable
    /// height — the same two bands <see cref="CalculatePagePenalty"/> prices with. The
    /// <c>compressed_nontitle_lines_count_</c> of a line is 1 here, as everywhere in this
    /// breaker (see FindOptimalBreaks' remark on too_few_lines).
    /// </para>
    /// </remarks>
    internal int MinPageCount(IReadOnlyList<SystemDetails> systems)
    {
        if (systems.Count == 0)
            return 0;
        var lines = CalcLineHeights(systems);
        var whitespace = new PageSpacing(_pageHeight, _topMargin, _bottomMargin,
            _vs.TopSystem, _vs.LastBottom);
        double FirstBand() => _pageHeight - (_topMargin + _headerHeight) - _bottomMargin;
        double RestBand() => _pageHeight - _topMargin - _bottomMargin;
        bool TooFewLines(int lineCount) =>
            _params.MinSystemsPerPage > 0 && lineCount < _params.MinSystemsPerPage;
        bool TooManyLines(int lineCount) =>
            _params.MaxSystemsPerPage > 0 && lineCount > _params.MaxSystemsPerPage;
        bool ragged = _params.RaggedBottom;

        int ret = 1;
        int pageStarter = 0;
        double curRodHeight = 0;
        double curSpringHeight = 0;
        double curPageHeight = FirstBand() - whitespace.MinWhitespaceAtTopOfPage(lines[0]);
        int lineCount = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            var cur = lines[i];
            var prev = i > 0 ? lines[i - 1] : null;
            double extLen = curRodHeight > 0 ? cur.Tallness : cur.Height;
            double springLen = prev != null ? prev.SpringLengthTo(cur) : 0;
            double nextRodHeight = curRodHeight + extLen;
            double nextSpringHeight = curSpringHeight + springLen;
            double nextHeight = nextRodHeight + (ragged ? nextSpringHeight : 0)
                                + whitespace.MinWhitespaceAtBottomOfPage(cur);
            int nextLineCount = lineCount + 1;

            if ((!TooFewLines(lineCount) && nextHeight > curPageHeight && curRodHeight > 0)
                || TooManyLines(nextLineCount)
                || (prev != null && prev.PagePermission == BreakPermission.Force))
            {
                lineCount = 1;
                curRodHeight = cur.Height;
                curSpringHeight = 0;
                pageStarter = i;
                curPageHeight = RestBand() - whitespace.MinWhitespaceAtTopOfPage(cur);
                ret++;
            }
            else
            {
                curRodHeight = nextRodHeight;
                curSpringHeight = nextSpringHeight;
                lineCount = nextLineCount;
            }
        }

        // LILYPOND-REF: :1257-1275 — is_last (): the last page at its own height.
        double lastPageHeight = (ret == 1 ? FirstBand() : RestBand())
            - whitespace.MinWhitespaceAtTopOfPage(lines[pageStarter])
            - whitespace.MinWhitespaceAtBottomOfPage(lines[^1]);
        if (!TooFewLines(lineCount - 1)
            && curRodHeight > lastPageHeight
            && curRodHeight > lines[^1].Height)
            ret++;

        return ret;
    }

    /// <summary>
    /// Finds optimal page breaks using 2D dynamic programming over page counts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:146-179 solve()
    /// LILYPOND-REF: lily/page-spacing.cc:296-402 calc_subproblem()
    ///
    /// Two-dimensional DP: dp[j, p] = minimum penalty to lay out systems 0..j-1
    /// on exactly p pages. We try all feasible page counts and keep the best.
    /// </remarks>
    private List<int> FindOptimalBreaks(IReadOnlyList<SystemDetails> systems)
    {
        int n = systems.Count;

        // Page-count range for the DP. maxPages = n (one system per page) is the
        // correct upper bound: the demerit-optimal layout can legitimately use any
        // count up to that, so it must not be capped lower. (A previous "better upper
        // bound" block here was dead — its inner loop did no height check, its result
        // was discarded, and it left maxPages == n — so it is removed.)
        // LILYPOND-REF: lily/page-spacing.cc:146-179 — iterate min_pages..max_pages
        int minPages = 1;
        int maxPages = n;

        // 2D DP: dp[j * (maxPages+1) + p] = min demerits for systems 0..j-1 on p pages
        int cols = maxPages + 1;
        var dp = new double[(n + 1) * cols];
        var prev = new int[(n + 1) * cols];
        Array.Fill(dp, double.MaxValue);
        Array.Fill(prev, -1);
        dp[0] = 0; // 0 systems on 0 pages

        // The REACHABLE page-count band per break index — the page DP's copy of the line
        // DP's minLines/maxLines walk (KnuthPlassBreaker.FindOptimalBreaks), and the same
        // construction argument: a break's states are all written while the outer loop
        // stands at j == that break and only read later, so the band is stable when read;
        // p outside it skips only the dp[prevIdx] >= MaxValue `continue`, whose iteration
        // has no other effect (the two lazy penalty memos fire only on reachable p).
        // MEASURED (session 136, before this): 652,800 p-iterations per 200-system break,
        // 55% of them that empty `continue`.
        var minPageCount = new int[n + 1];
        var maxPageCount = new int[n + 1];
        Array.Fill(minPageCount, int.MaxValue);
        Array.Fill(maxPageCount, int.MinValue);
        minPageCount[0] = 0;
        maxPageCount[0] = 0;

        for (int j = 1; j <= n; j++)
        {
            // LILYPOND-REF: lily/page-spacing.cc:312-320 calc_subproblem — LilyPond builds
            // ONE Page_spacing per (page, line) and walks `page_start` DOWN from `line`,
            // prepending a system each step, so the page GROWS along the walk. Lily# walked
            // i UP from 0, which is the same set of (i, j) pairs in the opposite order but
            // rebuilt the page from scratch for each one: MEASURED at 200 systems,
            // 1,353,400 AppendSystem calls per break = n³/6, against the n×(systems per
            // page) this walk pays.
            // ⚠️ The order is not cosmetic. The DP update below keeps the FIRST i that
            // reaches the minimum (strict <), so reversing the walk changes which i wins a
            // TIE — toward LilyPond's, which keeps the largest page_start (:386 keeps the
            // earlier candidate too). Ties are what the corpus run has to answer for.
            var pageSpacing = new PageSpacing(_pageHeight, _topMargin, _bottomMargin,
                _vs.TopSystem, _vs.LastBottom);
            for (int i = j - 1; i >= 0; i--)
            {
                int systemCount = j - i;

                // LILYPOND-REF: lily/page-spacing.cc:337 space.prepend_system (lines_[page_start])
                // — unconditional, BEFORE any of the filters below, because the accumulator
                // has to see every system on the walk even when this (i, j) is not a legal
                // page.
                pageSpacing.PrependSystem(systems[i]);

                // LILYPOND-REF: lily/page-spacing.cc:339-349 too_few_lines / page_start guard
                //   bool overfull = (space.rod_height_ > paper_height || …);
                //   if (!breaker_->too_few_lines (line_count) && page_start < line && overfull)
                //     break;
                // `line_count` there is the count BEFORE this system joins (it is incremented
                // at :351), i.e. systemCount - 1 here; `page_start < line` exempts the page
                // holding ONE system, which is the first step of this walk — so a single
                // system is never rejected and the DP always has an answer.
                // ⚠️⚠️ THIS EXIT IS NOT A PURE PRUNING, and the handoff entry that asked for
                // it said it was ("overflow is monotone as systems are added, so every
                // dropped candidate is MaxValue anyway"). PrependSystem's arithmetic does not
                // support that: the outgoing first system swaps its full height for its
                // tallness while the incoming one adds a full height, and
                // MinWhitespaceAtTopOfPage changes with it — so a page CAN get shorter as it
                // grows upward. LilyPond takes the exit regardless (its own comment at :343
                // hedges it as a heuristic), so this is a port of LilyPond's algorithm, not a
                // free refactor, and the corpus is what says whether any book moves.
                // ⚠️ ONE MORE CONSEQUENCE, since the exit can end the walk before i == 0: the
                // p == 1 candidate for this j — the whole prefix 0..j on ONE page — is then
                // never priced, because dp[i][0] is finite only at i == 0. Every book measured
                // is unaffected (the page that overflowed is a subset of that one), and the
                // same hole is in LilyPond, whose walk stops at `page_start > page_num`.
                // LILYPOND-REF: lily/page-breaking.cc:401-404 Page_breaking::too_few_lines —
                //   return line_count < min_systems_per_page ();
                // ⚠️ `line_count` IS NOT THE SYSTEM COUNT IN LILYPOND. It is the running sum of
                // Line_details::compressed_nontitle_lines_count_ (constrained-breaking.cc:632),
                // which is 0 for a title line and can exceed 1 for a compressed run. Lily# has
                // no such field on SystemDetails, and CalculateLineCountPenalty below ALREADY
                // equates the two — this guard is a second reader of that same fold, not a new
                // one. ⇒ With min-systems-per-page set on a book that pages a title line, this
                // exit can fire one system earlier than LilyPond's would. Closing it wants the
                // compressed/title line count on SystemDetails, which is where the orphan rule
                // (see CalculateLineCountPenalty's remarks) also stalls.
                // ⚠️ The `> 0` is Lily#'s "unset" convention for this parameter, used by the
                // three filters above; LilyPond needs no such test because an unset
                // min-systems-per-page is 0 and `line_count < 0` is already false.
                bool tooFewLines = _params.MinSystemsPerPage > 0
                    && systemCount - 1 < _params.MinSystemsPerPage;
                if (!tooFewLines && i < j - 1 && double.IsNegativeInfinity(pageSpacing.Force))
                    break;

                // A start no page count reaches prices to nothing — every p below would
                // hit the dp[prevIdx] >= MaxValue `continue`. Placed AFTER the prepend and
                // the overfull exit above, which belong to the walk itself, not to this
                // (i, j)'s pricing.
                if (minPageCount[i] > maxPageCount[i])
                    continue;

                // Check break permissions
                if (!IsValidBreak(systems, i, j))
                    continue;

                // Check min/max systems per page constraints
                if (_params.SystemsPerPage > 0 && systemCount != _params.SystemsPerPage)
                    continue;
                if (_params.MaxSystemsPerPage > 0 && systemCount > _params.MaxSystemsPerPage)
                    continue;
                if (_params.MinSystemsPerPage > 0 && systemCount < _params.MinSystemsPerPage)
                {
                    // Allow fewer systems only on the last page
                    if (j < n) continue;
                }

                bool isLastPage = (j == n);
                bool isRagged = _params.RaggedBottom
                    || (isLastPage && _params.RaggedLastBottom);

                // The page's demerits do not depend on WHICH page it is, only on whether it
                // is the first one (which loses the header's height from its available
                // space) — CalculatePagePenalty's other inputs are the system range and the
                // two flags above, all fixed for this (i, j). So the p loop below asks for
                // at most TWO distinct numbers, and it used to recompute one of them for
                // every page count: with maxPages = n that is an O(n) factor on top of the
                // O(n²) (i, j) pairs, each costing another O(j - i) to append the systems.
                // MEASURED on a 200-system book: 1,265,322 penalty calls / 64,068,701 system
                // appends per break, against 12,926 / 146,896 for a 43-system one — 4.65x the
                // systems for 436x the work, i.e. the fourth power. Memoising the two values
                // is arithmetically identical (the function is pure: it builds its own
                // PageSpacing and reads only readonly configuration).
                double firstPenalty = 0, restPenalty = 0;
                bool haveFirst = false, haveRest = false;

                // Predecessor states live at (i, p - 1): walk the band shifted by one.
                // The guard stays — the band brackets the reachable set, no more.
                for (int p = minPageCount[i] + 1; p <= maxPageCount[i] + 1; p++)
                {
                    int prevIdx = i * cols + (p - 1);
                    if (dp[prevIdx] >= double.MaxValue) continue;

                    double penalty;
                    if (p == 1)
                    {
                        if (!haveFirst)
                        {
                            firstPenalty = CalculatePagePenalty(
                                systems, i, j, isFirstPage: true, isLastPage, isRagged);
                            haveFirst = true;
                        }
                        penalty = firstPenalty;
                    }
                    else
                    {
                        if (!haveRest)
                        {
                            // ...and this is the page the walk has been accumulating, so it
                            // is priced rather than rebuilt. The first-page arm above still
                            // builds its own, because its top margin carries the header —
                            // and it is reachable only at i == 0 (p == 1 needs dp[i][0],
                            // which is finite only for i == 0), i.e. once per j.
                            restPenalty = DemeritsOf(
                                pageSpacing, systems, i, j, isLastPage, isRagged);
                            haveRest = true;
                        }
                        penalty = restPenalty;
                    }

                    if (penalty < double.MaxValue)
                    {
                        double totalPenalty = dp[prevIdx] + penalty;
                        int curIdx = j * cols + p;
                        if (totalPenalty < dp[curIdx])
                        {
                            dp[curIdx] = totalPenalty;
                            prev[curIdx] = i;
                            if (p < minPageCount[j]) minPageCount[j] = p;
                            if (p > maxPageCount[j]) maxPageCount[j] = p;
                        }
                    }
                }
            }
        }

        // Find best page count for all n systems
        double bestDemerits = double.MaxValue;
        int bestPages = -1;
        for (int p = minPages; p <= maxPages; p++)
        {
            int idx = n * cols + p;
            if (dp[idx] < bestDemerits)
            {
                bestDemerits = dp[idx];
                bestPages = p;
            }
        }

        // ⚠️ Now unreachable for any non-empty score, and deliberately kept: every line has
        // at least the page-holding-it-alone candidate, which CalculatePagePenalty never
        // rejects (LILYPOND-REF: lily/page-spacing.cc:339-349). It survives as the guard
        // for a state that should not arise — silently putting a whole score on one page is
        // a much worse failure than a wrong break, and it went unnoticed for as long as it
        // did precisely because it is silent.
        if (bestPages < 0)
        {
            // Fallback: single page
            return new List<int> { n };
        }

        // Backtrack to find break points
        var breaks = new List<int>();
        int current = n;
        int curP = bestPages;
        while (current > 0 && curP > 0)
        {
            breaks.Add(current);
            current = prev[current * cols + curP];
            curP--;
        }

        breaks.Reverse();
        return breaks;
    }

    /// <summary>
    /// Checks whether a page break is valid between systems[i..j-1].
    /// Handles forced breaks (must break) and forbidden breaks (cannot break).
    /// </summary>
    private static bool IsValidBreak(IReadOnlyList<SystemDetails> systems, int startIdx, int endIdx)
    {
        // Check for forced breaks in the middle (cannot skip over them)
        for (int k = startIdx; k < endIdx - 1; k++)
        {
            if (systems[k].ForceBreakAfter ||
                systems[k].PagePermission == BreakPermission.Force)
            {
                return false;
            }
        }

        // Check if we're breaking at a forbidden point
        if (endIdx < systems.Count && endIdx > startIdx)
        {
            if (systems[endIdx - 1].PagePermission == BreakPermission.Forbid)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Calculates penalty for putting systems startIdx..endIdx-1 on one page.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-spacing.cc:296-402 calc_subproblem()
    /// LILYPOND-REF: lily/page-breaking.cc:1547-1586 finalize_spacing_result()
    ///
    /// Demerits = force² + page_penalty + line_count_penalty + orphan_penalty
    /// For ragged pages: penalty based on unused space.
    /// </remarks>
    private double CalculatePagePenalty(
        IReadOnlyList<SystemDetails> systems,
        int startIdx,
        int endIdx,
        bool isFirstPage,
        bool isLastPage,
        bool isRagged)
    {
        // Calculate available height
        double topMargin = isFirstPage ? _topMargin + _headerHeight : _topMargin;
        var spacing = new PageSpacing(_pageHeight, topMargin, _bottomMargin,
            _vs.TopSystem, _vs.LastBottom);

        // Add systems to page
        for (int i = startIdx; i < endIdx; i++)
        {
            spacing.AppendSystem(systems[i]);
        }

        return DemeritsOf(spacing, systems, startIdx, endIdx, isLastPage, isRagged);
    }

    /// <summary>
    /// Prices a page whose <see cref="PageSpacing"/> has already been built — the ONE
    /// spelling of the page's demerits, shared by the caller that appends the systems and
    /// by the DP walk that prepends them one at a time.
    /// </summary>
    private double DemeritsOf(
        PageSpacing spacing,
        IReadOnlyList<SystemDetails> systems,
        int startIdx,
        int endIdx,
        bool isLastPage,
        bool isRagged)
    {
        int systemCount = endIdx - startIdx;
        double force = spacing.Force;
        bool overfull = double.IsNegativeInfinity(force);

        // LILYPOND-REF: lily/page-spacing.cc:339-349 — an overfull configuration is dropped
        //   if (!breaker_->too_few_lines (line_count) && page_start < line && overfull) break;
        // The loop it guards starts at `page_start = line` and walks BACKWARDS, so the first
        // configuration tried for any line is the page holding that line ALONE, and
        // `page_start < line` deliberately exempts it. A single system is therefore never
        // rejected for failing to fit — which is what guarantees the search always has an
        // answer. Only a page of two or more systems can be thrown out for overflowing.
        if (overfull && systemCount > 1)
        {
            return double.MaxValue;
        }

        double demerits;

        if (overfull)
        {
            // LILYPOND-REF: lily/page-spacing.cc:362-365 — "Clamp the demerits at
            // BAD_SPACING_PENALTY, even if the page is overfull. This ensures that
            // TERRIBLE_SPACING_PENALTY takes precedence over overfull pages."
            //
            // ⚠️ Lily# returned double.MaxValue here, for ANY overfull page. That is a
            // rejection, and LilyPond never rejects: it prices the page badly and keeps it.
            // The difference is not academic — it is the whole reason BreakIntoPages could
            // reach its `bestPages < 0` state and silently put a whole score on one page.
            // When no page could hold even one system (a fixture owning very small paper),
            // every candidate was MaxValue, the DP found nothing, and the fallback collapsed
            // the book. LilyPond emits overfull pages instead. Caught by
            // HaraKiriVisualTests.PagedRendering when the breaker's band was corrected.
            demerits = BadSpacingPenalty;
        }
        else if (isRagged)
        {
            // LILYPOND-REF: lily/page-spacing.cc:345-355
            // LILYPOND-REF: lily/page-layout-problem.cc:1057-1061 fixed_force_solution
            //
            // For ragged pages, use fixed_force_solution (force=0):
            // - Overfull but systems fit at minimum distances: allow with penalty
            //   (lily/page-layout-problem.cc:1057-1061 — fixed_force attempts placement)
            // - Underfull (force >= 0): no spacing penalty; systems placed at natural
            //   spring positions with remaining space at the bottom.
            if (force < 0)
            {
                // LILYPOND-REF: lily/page-layout-problem.cc:1057-1061
                // fixed_force_solution: even when force<0 the page is feasible — just
                // with its systems at minimum distances. Charge force² rather than
                // rejecting.
                //
                // The rod-fits test that used to guard this is gone because it could
                // never fail here: CalcForce already returns -infinity exactly when
                // RodHeight >= AvailableHeight, so a finite force means the rod fits.
                // Keeping it implied a second, stricter feasibility rule that did not
                // exist, and its `else` was the second of the two MaxValue rejections
                // LilyPond does not have.
                demerits = force * force * _params.PageSpacingWeight;
                demerits = Math.Min(demerits, BadSpacingPenalty);
            }
            else
            {
                demerits = 0;
            }
        }
        else
        {
            // LILYPOND-REF: lily/page-spacing.cc:358, lily/page-breaking.cc:1360-1362
            // demerits = force² × page_spacing_weight
            demerits = force * force * _params.PageSpacingWeight;
            demerits = Math.Min(demerits, BadSpacingPenalty);
        }

        // LILYPOND-REF: lily/constrained-breaking.cc:112-113 combine_demerits
        // Add page_penalty_, break_penalty_, and turn_penalty_ for page break
        if (!isLastPage && endIdx > startIdx)
        {
            demerits += systems[endIdx - 1].PagePenalty;
            demerits += systems[endIdx - 1].BreakPenalty;
            demerits += systems[endIdx - 1].TurnPenalty;
        }

        // Line count penalty (min/max systems per page)
        demerits += CalculateLineCountPenalty(systemCount);

        // NO orphan penalty here, and that is the LilyPond behaviour.
        //
        // LILYPOND-REF: lily/page-spacing.cc:375-383 — the widow/orphan rule reads
        //   if (page_start > 0 && page_start < lines_.size ()
        //       && lines_[page_start].last_markup_line_)      penalty += orphan_penalty ();
        //   if (page_start > 0 && page_start < lines_.size ()
        //       && lines_[page_start - 1].first_markup_line_) penalty += orphan_penalty ();
        // Those two flags come from a MARKUP line's Prob properties `last-markup-line` and
        // `first-markup-line` (constrained-breaking.cc:633-636); the Line_details a music
        // system gets initialises both to false (constrained-breaking.hh:115-116) and
        // nothing ever sets them. So in LilyPond the penalty fires only when a multi-line
        // markup PARAGRAPH — a title or text block — is split across a page boundary, and
        // it can never fire for a system of music.
        //
        // ⚠️ This used to read `isLastPage && systemCount == 1 && startIdx > 0` — "a lone
        // system on the last page" — which is a different rule, invented here, citing
        // page-breaking.cc:269 (where the VALUE is read from \paper, not where it is
        // applied). At 100000 against force-squared demerits of about 0.001 it decided
        // every page break by itself: it is why Lily# split six systems 4 + 2 where
        // LilyPond splits them 5 + 1, LilyPond's choice being an "orphan" under the
        // invented rule and unremarkable under the real one.
        //
        // Lily# has no markup-paragraph model in the breaker (SystemDetails.IsTitle marks a
        // title line but there is no first/last-line-of-paragraph notion), so the real rule
        // has nothing to fire on and OrphanPenalty stays carried but unreachable. Modelling
        // it needs multi-line markup in the page breaker first.

        return demerits;
    }

    /// <summary>
    /// Calculates penalty for having too few or too many systems on a page.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:407 line_count_penalty()
    /// </remarks>
    private double CalculateLineCountPenalty(int systemCount)
    {
        if (_params.SystemsPerPage > 0 && systemCount != _params.SystemsPerPage)
        {
            return TerribleSpacingPenalty;
        }

        double penalty = 0;

        if (_params.MaxSystemsPerPage > 0 && systemCount > _params.MaxSystemsPerPage)
        {
            penalty += TerribleSpacingPenalty;
        }

        if (_params.MinSystemsPerPage > 0 && systemCount < _params.MinSystemsPerPage)
        {
            penalty += TerribleSpacingPenalty;
        }

        return penalty;
    }

    /// <summary>
    /// Creates system details from layout information.
    /// </summary>
    public static SystemDetails CreateFromLayout(
        double staffHeight,
        double topExtent,
        double bottomExtent,
        double padding,
        double springLength,
        bool forceBreakAfter = false)
    {
        return new SystemDetails
        {
            Height = topExtent + staffHeight + bottomExtent,
            TopExtent = topExtent,
            BottomExtent = bottomExtent,
            StaffHeight = staffHeight,
            Padding = padding,
            SpringLength = springLength,
            ForceBreakAfter = forceBreakAfter
        };
    }

    /// <summary>
    /// Fills in every system's <see cref="SystemDetails.Tallness"/> — how much the stack
    /// grows when it is added below its predecessor at minimum spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-breaking.cc:1099-1142 Page_breaking::calc_line_heights,
    /// transcribed. Note it runs over the WHOLE sequence of systems, not per page: a
    /// system's tallness depends only on its predecessor, so the page breaker can then
    /// price any candidate page by summing them.
    /// <para>
    /// Lily#'s system origin is the top staff's TOP LINE, so LilyPond's Line_shape becomes
    /// <c>(-(StaffHeight + Down) . Up)</c> per bucket, out of
    /// <see cref="SystemDetails.Shape"/>. A system whose caller could not split it carries
    /// no shape and lends its whole-line extents to both buckets, which makes the max()
    /// over them inert — what this transcription did everywhere until the split arrived.
    /// </para>
    /// <para>
    /// ⚠️ THE SPLIT IS BY X, NOT BY COLUMN, and the deviation is here rather than hidden.
    /// LilyPond partitions the GROBS (lily/axis-group-interface.cc:441-458: a grob whose
    /// rank span starts at the line's first breakable column goes to the begin bucket,
    /// everything later to the other), which is why a wide rehearsal mark at a line start
    /// stays wholly in its begin bucket there and would spill into the rest bucket here.
    /// Lily# has no grob-to-column map at this seam; it has the paging skylines, and the
    /// same partition lives in them geometrically — see LayoutEngine.BuildLineShapes.
    /// </para>
    /// <para>
    /// <c>tight_spacing_</c> has no Lily# counterpart, so the padding is never dropped
    /// (page-breaking.cc:1123-1124 takes the padding unless the line is tight).
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<SystemDetails> CalcLineHeights(
        IReadOnlyList<SystemDetails> lines)
    {
        double prevHanging = 0;
        double prevHangingBegin = 0;
        double prevHangingRest = 0;

        // refpoint_hanging is the y coordinate of the origin of this system. It may not
        // be the same as RefpointExtentUp, which is the refpoint of the first spaceable
        // staff in this system. LILYPOND-REF: page-breaking.cc:1105-1107.
        double prevRefpointHanging = 0;

        var result = new List<SystemDetails>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            var cur = lines[i];
            var shape = cur.Shape ?? new LineShape(
                cur.TopExtent, cur.BottomExtent, cur.TopExtent, cur.BottomExtent);
            double a = shape.BeginUp;                                   // shape.begin_[UP]
            double b = shape.RestUp;                                    // shape.rest_[UP]
            double refpointHanging = Math.Max(prevHangingBegin + a, prevHangingRest + b);

            if (i > 0)
            {
                var prev = lines[i - 1];
                double padding = prev.Padding;
                double minDist = prev.MinDistance;
                refpointHanging = Math.Max(
                    refpointHanging + padding,
                    prevRefpointHanging - prev.RefpointExtentDown
                        + cur.RefpointExtentUp + minDist);
            }

            double hangingBegin                                         // shape.begin_[DOWN]
                = refpointHanging + cur.StaffHeight + shape.BeginDown;
            double hangingRest                                          // shape.rest_[DOWN]
                = refpointHanging + cur.StaffHeight + shape.RestDown;
            double hanging = Math.Max(hangingBegin, hangingRest);

            result.Add(cur with { Tallness = hanging - prevHanging });

            prevHanging = hanging;
            prevHangingBegin = hangingBegin;
            prevHangingRest = hangingRest;
            prevRefpointHanging = refpointHanging;
        }
        return result;
    }
}
