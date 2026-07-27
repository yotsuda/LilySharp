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
/// The alignment's walk down one group of vertically stacked lines: ONE running
/// down-skyline that is raised by each distance it fixes and then merged with the line
/// just placed, so every further line is measured against EVERYTHING above it rather
/// than against its neighbour alone.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:201-285
/// <c>Align_interface::internal_get_minimum_translations</c> — the loop body this is:
/// <code>
///   dy = down_skyline.distance (skyline[-stacking_dir]) + padding;   // :228
///   if (read_spacing_spec (spec, &amp;d, "minimum-distance")) dy = max (dy, d);  // :231-233
///   dy = max (0.0, dy);                                              // :271
///   down_skyline.raise (-stacking_dir * dy);                         // :272
///   down_skyline.merge (skyline[stacking_dir]);                      // :273
///   where += stacking_dir * dy;                                      // :274
/// </code>
/// with <c>stacking_dir == DOWN</c>, which is the only direction Lily# stacks in. Signs
/// are flipped to Lily#'s convention that a distance DOWN is positive, so LilyPond's
/// <c>where</c> (which decreases) is <see cref="Where"/> (which increases).
/// <para>
/// ⚠️ THE POINT OF THIS TYPE IS THAT THERE IS ONE OF IT. LilyPond runs this walk once per
/// system and hands the SAME vector to both consumers — <c>build_system_skyline</c> for
/// what the page RESERVES (page-layout-problem.cc:593-599) and
/// <c>elements_[i].min_offsets</c> for the minimums of the chain the loose lines are then
/// distributed into (:590-592, :923-925). A second model of it is HANDOFF 5.2.1②'s
/// signature defect, and this island exists because Lily# had one.
/// </para>
/// <para>
/// ⚠️ THE TWO VECTORS DIFFER ONLY ON SPACEABLE ELEMENTS, checked in the source rather
/// than inferred from the name: <c>get_minimum_translations_without_min_dist</c> passes
/// <c>include_fixed_spacing = false</c> (align-interface.cc:145-151), and that flag gates
/// only the block at :240-268, whose every branch is behind
/// <c>Page_layout_problem::is_spaceable (elems[j])</c>. The <c>minimum-distance</c> at
/// :231-233 is OUTSIDE it and runs for both. A lyric line is not spaceable, so the
/// reservation and the chain read the same numbers for it — which is what lets one walk
/// serve both.
/// </para>
/// <para>
/// The accumulated profile is not a by-product either: <c>build_system_skyline</c>
/// (:1093-1108) merges each element's skyline raised by its own translation, i.e. it
/// rebuilds exactly what this walk already has in <see cref="Profile"/>.
/// </para>
/// </remarks>
internal sealed class AlignmentWalk
{
    private readonly VerticalSkyline _downSkyline = new(VerticalDirection.Down);
    private readonly double _horizonPadding;

    /// <param name="horizonPadding">
    /// The <c>skyline-horizontal-padding</c> every <c>distance</c> in this walk is taken
    /// with. ⚠️ A NAMED DIVERGENCE, and a parameter rather than a constant because Lily#'s
    /// two callers disagree: align-interface.cc:228 calls plain <c>Skyline::distance</c>,
    /// whose padding defaults to 0, and LilyPond adds the system's own padding later and
    /// elsewhere (page-layout-problem.cc:619-628 says so in its own comment, "the system
    /// skyline-horizontal-padding is not added during the creation of an individual
    /// staff"). Lily#'s staff-to-staff minimum has always taken 0 and its lyric chain has
    /// always taken <see cref="SkylineDrop.HorizonPadding"/>; making them agree moves every
    /// staff pair in the corpus, so it is one island of its own and not this one. What
    /// matters here is that a pair WITH lyric lines between it takes the same value in the
    /// room and in the chain — otherwise the block does not fit the room it was given.
    /// </param>
    public AlignmentWalk(double horizonPadding = 0) => _horizonPadding = horizonPadding;

    /// <summary>
    /// The accumulated silhouette of everything placed so far, in the frame of the line
    /// placed LAST — the caller's own frame moves down with each <see cref="Advance"/>.
    /// </summary>
    public VerticalSkyline Profile => _downSkyline;

    /// <summary>How far below the walk's origin the last line placed sits.</summary>
    public double Where { get; private set; }

    /// <summary>
    /// Starts the walk at an element that is already positioned — the anchor staff. Its
    /// skyline enters the accumulation at distance 0: the walk's origin IS that element's
    /// reference point.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:215-220 — the <c>!last_nonempty_element</c>
    /// branch, which merges the first element and back-fills <c>translates[]</c> for it.
    /// ⚠️ THE ONE LINE OF THAT BRANCH NOT PORTED is its <c>dy</c>,
    /// <c>skyline[-stacking_dir].max_height () + padding</c> (:217): that measures the first
    /// element from the ALIGNMENT's own origin, and Lily#'s callers hand in an element whose
    /// position is already fixed and walk relative to it. So the origin differs by that dy
    /// and every distance this walk returns is unaffected by it.
    /// </remarks>
    public void Seed(VerticalSkyline? anchorDown)
    {
        if (anchorDown is { IsEmpty: false } sky)
            _downSkyline.Merge(sky);
    }

    /// <summary>
    /// Places the next line: returns the distance from the accumulation down to it, and
    /// leaves the accumulation raised into that line's frame with the line merged in.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:228-275 — the body of the walk's loop, in its
    /// own order: the padded skyline distance, the spec's minimum-distance, the clamp to
    /// zero, the raise, the merge, and the running sum.
    /// </remarks>
    /// <param name="lineUp">The line's own UP-skyline, self-relative to its baseline.</param>
    /// <param name="lineDown">The line's own DOWN-skyline, self-relative to its baseline.</param>
    /// <param name="padding">
    /// The spec's <c>padding</c> member.
    /// LILYPOND-REF: lily/align-interface.cc:225-226 <c>read_spacing_spec (spec, &amp;padding,
    /// ly_symbol2scm ("padding"))</c>, whose default is the alignment's own
    /// <c>padding</c> property (:193, 0.0 for a VerticalAlignment).
    /// </param>
    /// <param name="minimumDistance">
    /// The spec's <c>minimum-distance</c> member, or 0 where the spec declares none.
    /// LILYPOND-REF: lily/align-interface.cc:231-233 — <c>dy</c> is raised only when
    /// <c>read_spacing_spec</c> FINDS the member, and a spec without one leaves <c>dy</c>
    /// alone, which is what a 0 here does.
    /// </param>
    public double Advance(
        VerticalSkyline? lineUp, VerticalSkyline? lineDown,
        double padding, double minimumDistance = 0)
    {
        // LILYPOND-REF: lily/align-interface.cc:228
        //   dy = down_skyline.distance (skyline[-stacking_dir]) + padding;
        double dy = Distance(lineUp, padding);
        // LILYPOND-REF: lily/align-interface.cc:231-233
        //   if (read_spacing_spec (spec, &spec_distance, "minimum-distance"))
        //     dy = std::max (dy, spec_distance);
        if (dy < minimumDistance) dy = minimumDistance;
        // LILYPOND-REF: lily/align-interface.cc:271  dy = std::max (0.0, dy);
        if (dy < 0) dy = 0;

        // LILYPOND-REF: lily/align-interface.cc:272-274
        //   down_skyline.raise (-stacking_dir * dy);   // stacking_dir == DOWN
        //   down_skyline.merge (skyline[stacking_dir]);
        //   where += stacking_dir * dy;                // sign flipped: DOWN is positive here
        _downSkyline.Raise(dy);
        if (lineDown is { IsEmpty: false } down) _downSkyline.Merge(down);
        Where += dy;
        return dy;
    }

    /// <summary>
    /// One step of the walk WITHOUT taking it: the distance from the accumulation down to
    /// a line's up-skyline plus the spec's padding. This is what closes a chain, where the
    /// line below is already placed and only the distance to it is wanted.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:228 — <c>down_skyline.distance (…) + padding</c>,
    /// the same expression <see cref="Advance"/> takes, without the raise and merge that
    /// follow it.
    /// <para>
    /// An empty skyline on either side yields 0 rather than an infinity.
    /// LILYPOND-REF: lily/align-interface.cc:209-213 — an element whose skyline pair is
    /// empty <c>continue</c>s with <c>translates.push_back (where)</c>, i.e. it advances
    /// <c>where</c> by nothing, so 0 IS that branch rather than an approximation of it.
    /// </para>
    /// </remarks>
    public double Distance(VerticalSkyline? lineUp, double padding)
    {
        if (_downSkyline.IsEmpty || lineUp is not { IsEmpty: false } up)
            return 0;

        double dist = _downSkyline.Distance(up, _horizonPadding);
        if (double.IsInfinity(dist) || double.IsNaN(dist))
            return 0;
        return dist + padding;
    }
}
