// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/align-interface.cc
//     Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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
/// ⚠️ BOTH CALLERS NOW PASS THE SAME ARGUMENTS — the padding and the minimum-distance of
/// whichever spec governs each step. The chain used to omit the minimum-distance on the
/// grounds that <c>CreateSpring</c> applies the same member to the same spring a line
/// later; that is true of the SPRING and false of the WALK, which raises by the clamped dy
/// (:271-273) and so puts a different accumulation in front of every later line. Passing it
/// was measured inert, so "one walk" is now a statement about the numbers and not only
/// about the code.
/// </para>
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
/// rebuilds exactly what this walk already accumulates. ⚠️ It is NOT exposed: a
/// <c>Profile</c> property was added with the type and nothing ever read it, so it went
/// again (HANDOFF 5.2.1⑥ — an unread member reads to the next person as a used one). The
/// reservation wants a LENGTH, which is <see cref="Where"/> plus a closing
/// <see cref="Distance"/>; whoever needs the silhouette itself can put it back with a
/// caller attached. ★ 2026-08-20: the page reservation now wants a PROFILE, and it
/// accumulates the BLOCK's own beside this walk (<c>LyricReservationBelowSystem</c>,
/// block-only — the anchor staff stays the system skyline's business), so the walk's
/// accumulation, which carries the seed too, remains unexposed.
/// </para>
/// <para>
/// ⚠️ NO SKYLINE-HORIZONTAL-PADDING ON THE DISTANCES, and that is LilyPond's reading rather
/// than an omission: align-interface.cc:228 calls plain <c>Skyline::distance</c>, whose
/// padding defaults to 0, and the per-staff skylines it reads are not pre-padded either —
/// <c>Axis_group_interface::skyline_spacing</c> widens only OUTSIDE-staff grobs, by their
/// own <c>outside-staff-horizontal-padding</c> (axis-group-interface.cc:747-753, default
/// 0). LilyPond adds the system's padding later and elsewhere, and says so in its own
/// comment (page-layout-problem.cc:619-628: "the system skyline-horizontal-padding is not
/// added during the creation of an individual staff").
/// ⚠️ Lily#'s lyric chain took <c>SkylineDrop.HorizonPadding</c> here for as long as it
/// existed. MEASURED 2026-07-27, IT IS INERT: taking LilyPond's 0 in both the chain and the
/// room leaves all 3411 tests, every ledger entry and every snapshot unchanged. It was
/// replaced rather than kept behind a parameter, so no caller is left with a choice to get
/// wrong.
/// </para>
/// </remarks>
internal sealed class AlignmentWalk
{
    private readonly VerticalSkyline _downSkyline = new(VerticalDirection.Down);

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

        // LILYPOND-REF: lily/align-interface.cc:228 — plain Skyline::distance, no padding.
        double dist = _downSkyline.Distance(up);
        if (double.IsInfinity(dist) || double.IsNaN(dist))
            return 0;
        return dist + padding;
    }
}
