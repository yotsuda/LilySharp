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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

internal sealed partial class LayoutEngine
{
    /// <summary>
    /// Per system, the independent lyrics ROWS that hang below its last spaceable staff —
    /// the elements of that system's own loose block, after its note-bound verses.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 — a Lyrics context is a
    /// non-spaceable line wherever it stands, so the run below the last spaceable staff
    /// contains it and <c>distribute_loose_lines</c> solves it with everything else in that
    /// run. Nothing here asks whether the syllables were <c>\lyricsto</c> anything: measured
    /// as whole dumps, LilyPond reads books LYRC/LYRR and LYRV/LYRRV line for line the same.
    /// <para>
    /// ⚠️ EMPTY WHERE THE CHAIN DECLINES, so the row keeps the band it was laid out in rather
    /// than being solved into a room somebody else owns — the same bail-out
    /// <see cref="BuildLooseChainEnds"/> makes, out of the same classification.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Per system and per spaceable staff, the independent lyrics ROWS standing between that
    /// staff and the NEXT spaceable one — the elements of the run those two bound.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:936-939 distribute_loose_lines — the call
    /// handed two spaceable positions of ONE system and the loose lines strictly between
    /// them. The sibling of <see cref="BuildTrailingRowStaves"/>, which reads the OTHER call
    /// (:1004-1013, the run that closes on the next system or the page edge); one walk
    /// (<see cref="ClassifySystem"/>) answers both, because which run a row is in is decided
    /// by where the next spaceable staff falls and nothing else.
    /// <para>
    /// ★ THE RUN USED TO BE ABANDONED. <c>SystemAlignment.UnmodelledRow</c> was set
    /// the moment a spaceable staff appeared below a row, and every reader of that flag —
    /// this table's twin, the chain ends, the reservation — declined for the whole system.
    /// The row then kept the flat band <c>MultiStaffLayouter.GetStaffHeight</c> laid it out
    /// in, whose per-verse step is a Lily#-only 3.2 against LilyPond's solved
    /// <c>max(2.8, ink + 0.2)</c>, and whose HEIGHT is what the pair's gap is measured
    /// against. MEASURED on the reported book (scratch/ベースタブLy/Untitled-6.lys, user
    /// report 2026-08-25): a two-verse row left 0.440000 between its last syllable's baseline
    /// and the staff below, where the one-verse systems of the same score leave 4.600000.
    /// </para>
    /// </remarks>
    private static Func<int, int, IReadOnlyList<int>>? BuildBetweenRowStaves(
        ImmutableArray<SystemLayout> systemsArray, IReadOnlySet<int> textRowStaves)
    {
        if (textRowStaves.Count == 0 || systemsArray.IsDefaultOrEmpty)
            return null;

        var perSystem = new List<ILookup<int, int>>(systemsArray.Length);
        foreach (var system in systemsArray)
        {
            if (system.StaffGroups.IsDefaultOrEmpty)
            {
                perSystem.Add(Array.Empty<(int, int)>().ToLookup(p => p.Item1, p => p.Item2));
                continue;
            }
            var alignment = ClassifySystem(system.StaffGroups);
            perSystem.Add(alignment.Between.ToLookup(p => p.Anchor, p => p.Row));
        }
        return (s, anchor) => s >= 0 && s < perSystem.Count
            ? perSystem[s][anchor].ToList()
            : Array.Empty<int>();
    }

    private static Func<int, IReadOnlyList<int>>? BuildTrailingRowStaves(
        ImmutableArray<SystemLayout> systemsArray, IReadOnlySet<int> textRowStaves)
    {
        if (textRowStaves.Count == 0 || systemsArray.IsDefaultOrEmpty)
            return null;

        var perSystem = new List<IReadOnlyList<int>>(systemsArray.Length);
        foreach (var system in systemsArray)
        {
            if (system.StaffGroups.IsDefaultOrEmpty)
            {
                perSystem.Add(Array.Empty<int>());
                continue;
            }
            var alignment = ClassifySystem(system.StaffGroups);
            perSystem.Add(
                alignment.LastSpaceable is null ? Array.Empty<int>() : alignment.Trailing);
        }
        return s => s >= 0 && s < perSystem.Count ? perSystem[s] : Array.Empty<int>();
    }

    /// <summary>
    /// What closes each system's lyric chain, and how much room the page left it — the two
    /// numbers <c>distribute_loose_lines</c> is called with.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:872-874, :936-939 and :1012-1013 — the
    /// three calls, whose last two arguments are <c>first_translation</c> (the placed staff
    /// above) and <c>last_translation</c> (the next placed staff, or <c>-page_height_</c>).
    /// Their difference is the room; this returns it directly.
    /// <para>
    /// The minimum on the gap that reaches the next system's staff is
    /// <c>elements_[i].padding - min_offsets[0]</c> (:931-932): the system-system-spacing
    /// padding, plus the ink that system carries ABOVE its own reference point —
    /// <c>min_offsets[0]</c> is <c>-(up-skyline height + padding)</c> out of
    /// align-interface.cc:215-220, the first element's own translation. Lily#'s up extent
    /// is measured from the top staff LINE, so the reference-point quantity is a half-staff
    /// more, the same conversion <c>LayoutUtilities.CreateTopSystemSpring</c> makes.
    /// ⚠️ CHECKED against LilyPond rather than assumed: on book LYRV the chain
    /// 3.737890 + 2.800000 + (1 + f) + (1 + 4.303666) solves to the measured room
    /// 12.000000 at f = -0.841556, and the first spring's 3.737890 is its floor at that
    /// force. Every term of that is six digits.
    /// </para>
    /// <para>
    /// ⚠️ THE ROOM IS BETWEEN TWO STAFF REFERENCE POINTS, never between two system origins,
    /// and LilyPond's call site is where that is plainest: the two arguments are
    /// <c>last_spaceable_line_translation</c> and <c>-solution_[spring_idx]</c> (:936-939) —
    /// the previous spaceable staff's position in the PAGE's spring chain and this one's.
    /// Neither end knows which system it belongs to; the same call serves a block between
    /// two systems and a block between two staves of one system, and only the minimum that
    /// closes it changes (:923-933). So the span from a system's origin down to its LAST
    /// spaceable staff has to come off the near end, and it is read PER SYSTEM because
    /// hara-kiri hides different staves on different systems — which is why it could not be
    /// taken from <c>systemsArray[0]</c> the way <c>lastSpaceableStaffY</c> is.
    /// MEASURED on book LYRMV (audit/lp-geometry, <c>lyrics.two-staff.two-verse.*</c>).
    /// </para>
    /// <para>
    /// ★ A LYRICS ROW BELOW THE LAST SPACEABLE STAFF NO LONGER BAILS (2026-07-28), AND SINCE
    /// 2026-08-26 NEITHER DOES A CHORDS ROW. Either is a non-spaceable line of THIS system's
    /// own run (:919-925), so it is an element of the chain this end closes rather than a
    /// reason to abandon it — see <see cref="BuildTrailingRowStaves"/> and
    /// <see cref="SystemAlignment"/>, whose <c>UnmodelledRow</c> flag went with the last
    /// arrangement it named.
    /// </para>
    /// <para>
    /// ⚠️ STILL NULL WHEN THE ROOM HOLDS SOMETHING THIS CHAIN DOES NOT MODEL, and that is
    /// the room being unknown rather than an exclusion (§5.2). The case left at force 0 is a
    /// block between two staves of one system, which <see cref="LyricEngraver"/> keeps out
    /// because its closing spring is <c>nonstaff-unrelatedstaff-spacing</c> against the next
    /// staff's up-skyline (:1301-1312) — an input the engraver is not given.
    /// <para>
    /// ★ AN OSSIA NO LONGER BAILS OUT AT ALL (2026-07-28), and the bail-out it had was
    /// written on a false premise twice over: it said an ossia "is a loose line to LilyPond
    /// and goes INTO the chain, while Lily# lays it out as a band of its own". LilyPond makes
    /// an ossia SPACEABLE (page-layout-problem.cc:1173-1177 <c>is_spaceable</c> asks only for
    /// <c>staff-affinity</c>, which a <c>\new Staff</c> has none of), so it BRACKETS a run
    /// instead of standing in one, and Lily# now agrees. It is a chain END here.
    /// </para>
    /// </para>
    /// <para>
    /// ★ A TEXT ROW NO LONGER BAILS OUT WHEN IT LEADS THE NEXT SYSTEM (2026-07-27), which is
    /// the whole of <c>lyrics.chord-row.between-systems.staff-to-lyric</c>: LilyPond pushes
    /// every non-spaceable line onto the SAME <c>loose_lines</c> vector and closes the run on
    /// the next spaceable staff (:948-990), so a chords row at the top of the next system is
    /// IN this block's chain and the two are squeezed into one room. MEASURED: 12.000000 of
    /// room in both engravers, and LilyPond's lyric line at 4.608814 where its rowless twin
    /// LYRM reads 5.500000. A row standing strictly BETWEEN two spaceable staves still bails,
    /// because that one is the other call's span (:936-939 takes two spaceable positions and
    /// the loose lines strictly between them).
    /// </para>
    /// </remarks>
    private Func<int, LooseLineSpacer.ChainEnd?>? BuildLooseChainEnds(
        MultiStaffScore score, ImmutableArray<PageLayout> pages,
        ImmutableArray<SystemLayout> systemsArray,
        List<(double upExtent, double downExtent)> perSystemExtents,
        Func<Staff, ImmutableDictionary<RestShiftKey, double>> restCollisionsOf,
        IReadOnlyList<List<MultiStaffLayouter.StaffInsideSpanners>>? staffSpanners,
        IReadOnlyList<List<(VerticalSkyline Up, VerticalSkyline Down)>>? staffInside)
    {
        if (score.Lyrics.IsDefaultOrEmpty || systemsArray.IsDefaultOrEmpty || pages.IsDefaultOrEmpty)
            return null;

        var staffByIndex = new Dictionary<int, Staff>();
        foreach (var (_, st, idx) in score.EnumerateStaves())
            staffByIndex[idx] = st;

        // Device-DOWN from each system's origin to its FIRST and its LAST spaceable staff's
        // top line — the two ends every chain on the page attaches to. A hidden staff is
        // skipped because hara-kiri leaves it at the current Y with zero height
        // (MultiStaffLayouter), so it neither draws nor takes room.
        //
        // ⚠️ BOTH ARE DERIVED, and the first one is derived even though the guard above
        // makes it 0 today: LilyPond's far end is `-solution_[spring_idx]`, the next
        // system's FIRST SPACEABLE STAFF's reference point, so reading that staff is the
        // port and assuming it coincides with the system origin is a shortcut. It does
        // coincide — MultiStaffLayouter advances its running Y only past a staff it has
        // already placed, and a hidden staff or a wholly hidden group advances it not at
        // all — but that is an invariant of another file, and §5.2.1 (6) is about exactly
        // this: a quantity whose value you can only justify by reading elsewhere. The
        // corpus confirms it rather than the comment doing so — introducing the term moved
        // no entry and no snapshot.
        var firstSpaceable = new double[systemsArray.Length];
        var lastSpaceable = new double[systemsArray.Length];
        var firstSpaceableIndex = new int[systemsArray.Length];
        // The non-spaceable lines each system OPENS with, in placement order — the run
        // LilyPond hands to the previous block's chain (:948-990).
        var leading = new List<StaffLayout>[systemsArray.Length];
        // Whether THIS system's alignment has the two ends a chain is written between.
        // LILYPOND-REF: lily/page-layout-problem.cc:936-939 distribute_loose_lines — the call
        // that solves ONE run, handed `last_spaceable_line_translation` and
        // `-solution_[spring_idx]` and nothing else. There is one such call per run, so what
        // one run holds says nothing about the next run down the page.
        // ★ THE ONLY REMAINING REASON TO DECLINE IS "no spaceable staff" (2026-08-26). It used
        // to be that plus SystemAlignment.UnmodelledRow — a chords row anywhere below a staff
        // — and the per-run GRAIN of this array was carried for that flag's sake. The grain
        // stays because hara-kiri still makes one page answer two ways: MEASURED on book ROWH,
        // page 1 reads 4.027851 where the row still stands between two staves and 5.500000 two
        // staves further down where it does not (audit/lp-geometry
        // lyrics.row.between-staves.hara-kiri.*).
        // ⚠️ AN OSSIA USED TO BAIL OUT HERE TOO, on the reading that it "is a loose line to
        // LilyPond and goes INTO the chain while Lily# lays it out as a band of its own". BOTH
        // HALVES OF THAT WERE WRONG: an ossia has no `staff-affinity`, so LilyPond makes it
        // SPACEABLE and it brackets runs rather than filling them
        // (page-layout-problem.cc:1173-1177 is_spaceable), and since 2026-07-28 Lily# does the
        // same. It is a chain END here like any other staff.
        var usable = new bool[systemsArray.Length];
        for (int s = 0; s < systemsArray.Length; s++)
        {
            if (systemsArray[s].StaffGroups.IsDefaultOrEmpty) continue;
            var alignment = ClassifySystem(systemsArray[s].StaffGroups);
            if (alignment.FirstSpaceable is not { } firstStaff
                || alignment.LastSpaceable is not { } lastStaff) continue;

            usable[s] = true;
            firstSpaceable[s] = -firstStaff.Y;
            firstSpaceableIndex[s] = firstStaff.StaffIndex;
            lastSpaceable[s] = -lastStaff.Y;
            leading[s] = alignment.Leading.ToList();
        }

        double halfStaff = _options.StaffHeight / 2.0;
        // The pair spec a music system takes; a title between two systems would take
        // another (VerticalSpacingParameters.SelectSpec), which no lyric score reaches.
        double systemPadding = _options.VerticalSpacing.SystemSystem.Padding;

        // systemsArray IS pages.SelectMany(p => p.Systems), so this running index is the
        // one SpannerBreakSubstitution.BuildMeasureToSystemMap hands the lyric engraver.
        var ends = new Dictionary<int, LooseLineSpacer.ChainEnd>();
        int index = 0;
        foreach (var page in pages)
        {
            var onPage = page.Systems;
            for (int i = 0; i < onPage.Length; i++, index++)
            {
                // TWO SYSTEMS ARE READ TO WRITE ONE END, so two have to be usable: this
                // system's own last spaceable staff, and — when the run closes on a next
                // system rather than on the page edge — that system's FIRST spaceable staff
                // and the lines it opens with. So an un-modelled row takes the chain from the
                // system it stands on and from the one above it when they share a page, and
                // from no others. ⚠️ IT IS NOT `index` ALONE: the far end below reads
                // firstSpaceable[index + 1], leading[index + 1] and that system's up extent,
                // none of which a declining classification filled in.
                // ⚠️ AND NOT THE NEXT PAGE'S EITHER — the else-branch closes this run on the
                // page edge and starts the next page's with its own call, so `i + 1 <
                // onPage.Length` is the whole of the question (:1004-1013, the remark below).
                if (!usable[index]) continue;
                if (i + 1 < onPage.Length && !usable[index + 1]) continue;

                // LilyPond's `last_spaceable_line_translation`.
                double anchor = onPage[i].Y - lastSpaceable[index] - halfStaff;
                if (i + 1 < onPage.Length)
                {
                    // ...and `-solution_[spring_idx]`, the next system's FIRST spaceable
                    // staff's reference point.
                    //
                    // The minimum on the spring that reaches it is
                    // `elements_[i].padding - min_offsets[0]` (:931-932), and min_offsets[0]
                    // is that same staff's own translation, so the ink term is measured from
                    // the SAME point: the system's up extent (from its origin) plus the span
                    // down to that staff plus the half-staff to its reference point.
                    double nextUpExtent = index + 1 < perSystemExtents.Count
                        ? perSystemExtents[index + 1].upExtent : 0;
                    double nextFirst = firstSpaceable[index + 1];
                    double room = anchor - (onPage[i + 1].Y - nextFirst - halfStaff);

                    var (lines, closingSpec, closingMin) = LeadingLinesOfSystem(
                        score, systemsArray, staffByIndex, index + 1,
                        leading[index + 1], firstSpaceableIndex[index + 1], restCollisionsOf,
                        staffSpanners, staffInside);

                    ends[index] = new LooseLineSpacer.ChainEnd(
                        room, systemPadding + nextUpExtent + nextFirst + halfStaff,
                        lines, closingSpec, closingMin);
                }
                else
                {
                    // The last block on a page runs to the bottom of the printable area.
                    // ⚠️ NO LEADING LINES HERE EVEN WHEN THE NEXT PAGE HAS THEM: LilyPond
                    // closes this chain on the page edge (:1004-1013) and starts the next
                    // page's with its own call, so a row at the top of the next PAGE is in
                    // that chain and not this one.
                    ends[index] = new LooseLineSpacer.ChainEnd(
                        anchor - _options.MarginBottom, double.NaN,
                        ImmutableArray<LooseLineSpacer.LeadingLine>.Empty, null, 0);
                }
            }
        }

        return s => ends.TryGetValue(s, out var end) ? end : null;
    }

    /// <summary>
    /// The non-spaceable lines system <paramref name="sysIdx"/> opens with, as the previous
    /// block's chain needs them: each line's own skylines, the spec of the spring that
    /// reaches it, and — for every line after the first — that spring's minimum out of THIS
    /// system's own alignment.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:948-990 collects them, :956-962 gives every
    /// line after the first its <c>min_offsets[k-1] - min_offsets[k]</c>, and :923-925 gives
    /// the closing staff the same difference.
    /// <para>
    /// ⚠️ THE MINIMUMS COME FROM THIS SYSTEM'S ALIGNMENT, NOT FROM THE CHAIN'S RUNNING WALK,
    /// and the difference is not cosmetic: the chain's accumulation still carries the
    /// PREVIOUS system's lyric line raised into place, so at an x where the row has no symbol
    /// that line's descender would shine through and the closing distance would come out too
    /// large. <c>min_offsets</c> knows only its own system's elements. The one term that IS
    /// the chain's is the first line's, which is the system-level
    /// <c>elements_[i].min_distance + elements_[i].padding</c> — see
    /// <see cref="LooseLineSpacer.LeadingLine.MinInto"/>.
    /// </para>
    /// <para>
    /// ⚠️ THE INDENT GOES WITH THE CLOSING STAFF'S SKYLINE: that skyline is what the chain's
    /// last spring is floored by, and one built without the clef would floor it somewhere the
    /// room does not agree with.
    /// </para>
    /// <para>
    /// ⚠️ AND IT IS STILL A SECOND BUILD, WHICH IS WHAT THIS ONE HAS THAT
    /// <see cref="ComputeBetweenStavesEnd"/> NO LONGER DOES. That one used to rebuild too, and
    /// now reads the per-staff list <c>MultiStaffLayouter.BuildAllStaffSkylines</c> produced
    /// (see the remark there). This call site cannot read that list: it is reached from the
    /// PAGE pass, which runs before <c>AnnotationLayoutContext.StaffSkylines</c> exists. So
    /// the closing staff here is still measured WITHOUT its dynamics, scripts or beams, and a
    /// mark on the first staff of the next system is not in the distance a trailing row is
    /// closed by. NOT MEASURED — the sentence that stood here claimed the corpus has no such
    /// book and had not asked it, which is the shape HANDOFF 1 named three times in one
    /// session.
    /// </para>
    /// <para>
    /// ★ THE REST SHIFT IS HERE SINCE 2026-08-04, and it is the one side table that costs
    /// nothing to have: <c>Rest_collision</c>'s answer is a function of the MUSIC alone, so
    /// the room's memo already holds it this early
    /// (<c>MultiStaffLayouter.RestCollisionsOf</c>) and the closing staff is measured with
    /// its rests where they were pushed to.
    /// </para>
    /// <para>
    /// ⚠️ THE OTHER SIX ARE NOT IMPOSSIBLE HERE, and a sentence claiming they were stood in
    /// this remark for a few hours on 2026-08-04 until its own author read the signatures.
    /// <c>Staff{Beam,Slur,Tie,TupletBracket,Articulation}Layouts</c> take
    /// <c>(score, staff, staffIndex, measureLayouts)</c> and nothing else, and the dynamics
    /// are a <c>Where</c> over <c>score.Dynamics</c> — every input this method already holds
    /// (<paramref name="sysIdx"/>'s <c>Measures</c>). What stops it is not availability but
    /// that computing them HERE would be a second run of what
    /// <c>MultiStaffLayouter.BuildAllStaffSkylines</c> already did for this staff, which is
    /// the same objection this whole migration is about. The fix is to reach the room's
    /// result, not to recompute.
    /// ★ THREE OF THE SIX DO REACH IT SINCE 2026-08-04, and the sentence that used to end
    /// this paragraph — "that needs the per-staff list to exist before the page pass, which
    /// it does not" — was true only of the SKYLINES. The room now hands its slurs, ties and
    /// tuplet brackets out beside them (<c>MultiStaffLayouter.StaffInsideSpanners</c>), and
    /// <c>BuildLooseChainEnds</c> runs after the placement that produces them, so this call
    /// site takes them by lookup (<c>SpannersAt</c>) and lays nothing out twice. MEASURED on
    /// the book <c>LooseLineExtentScopeTests</c> builds: the row opening system 2 stood
    /// 9.947093 above its closing staff with a tuplet bracket over that staff and 9.947093
    /// without it, against 11.127093 once the bracket was in the profile — the same 1.180000
    /// the figured-bass drop gained from the same grob.
    /// ⚠️ THE REMAINING THREE ARE STILL OUT and still unmeasured: dynamics, scripts and
    /// beams are not in the room's carried tables, so nothing here can reach them without
    /// the recomputation this paragraph rules out.
    /// </para>
    /// </remarks>
    private (ImmutableArray<LooseLineSpacer.LeadingLine> Lines,
             VerticalSpacingSpec? ClosingSpec, double ClosingMin) LeadingLinesOfSystem(
        MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray,
        IReadOnlyDictionary<int, Staff> staffByIndex, int sysIdx,
        List<StaffLayout> leading, int firstSpaceableIndex,
        Func<Staff, ImmutableDictionary<RestShiftKey, double>> restCollisionsOf,
        IReadOnlyList<List<MultiStaffLayouter.StaffInsideSpanners>>? staffSpanners,
        IReadOnlyList<List<(VerticalSkyline Up, VerticalSkyline Down)>>? staffInside)
    {
        if (leading.Count == 0
            || !staffByIndex.TryGetValue(firstSpaceableIndex, out var closingStaff))
            return (ImmutableArray<LooseLineSpacer.LeadingLine>.Empty, null, 0);

        var measures = systemsArray[sysIdx].Measures;
        var sp = _options.StaffSpacing;

        var built = ImmutableArray.CreateBuilder<LooseLineSpacer.LeadingLine>(leading.Count);
        var walk = new AlignmentWalk();
        Staff? previous = null;

        foreach (var layout in leading)
        {
            if (!staffByIndex.TryGetValue(layout.StaffIndex, out var row))
                return (ImmutableArray<LooseLineSpacer.LeadingLine>.Empty, null, 0);
            var (up, down) = RowSkylinesOf(score, row, layout.StaffIndex, measures);
            if (up.IsEmpty && down.IsEmpty)
                // A line with no ink is one LilyPond's own walk skips outright
                // (align-interface.cc:209-213), so it cannot be given a spring here either.
                return (ImmutableArray<LooseLineSpacer.LeadingLine>.Empty, null, 0);

            var spec = previous is null
                // The spring the NULL line hands on: either neighbour null, so the spec is
                // empty and only the caller's HUGE_STRETCH survives (:1274-1275).
                ? LooseLineSpacer.NullNeighbour
                : StaffAffinity.GetSpacingSpec(
                    previous.StaffAffinity,
                    MultiStaffLayouter.NonStaffSpecsOf(previous, sp),
                    row.StaffAffinity, MultiStaffLayouter.NonStaffSpecsOf(row, sp),
                    sp.StaffStaff);

            // One step of THIS system's own alignment. For the first line the walk is empty
            // and the step is 0 — LilyPond's `!last_nonempty_element` branch, whose dy only
            // moves the alignment's own origin (AlignmentWalk.Seed) — so the number is
            // discarded and the chain's system-level term stands in its place.
            double minInto = walk.Advance(up, down, spec.Padding, spec.MinimumDistance);

            built.Add(new LooseLineSpacer.LeadingLine(
                up, down, spec, previous is null ? double.NaN : minInto, layout.StaffIndex));
            previous = row;
        }

        // ...and the step from the last line to the system's first spaceable staff, which is
        // that line's OWN nonstaff-relatedstaff-spacing (its affinity is not UP).
        var closingSpec = StaffAffinity.GetSpacingSpec(
            previous!.StaffAffinity, MultiStaffLayouter.NonStaffSpecsOf(previous, sp),
            null, sp.Lyrics, sp.StaffStaff);
        // ...and the closing staff's own silhouette: THE room's inside-staff skyline, not a
        // subset rebuilt here. Everything that is inside-staff ink in LilyPond — the notes
        // (with their rest shifts), the scripts, the spanners and the beams — is in it
        // because the room put it there once.
        // LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines.
        var closingInside = InsideAt(staffInside, sysIdx, firstSpaceableIndex)
            // The preliminary pass has no room to quote; build the one profile from the same
            // ingredients it would have carried.
            ?? _skylineBuilder.BuildInsideStaffSkylines(
                closingStaff, measures, systemLeft: systemsArray[sysIdx].Indent,
                tupletBrackets: SpannersAt(staffSpanners, sysIdx, firstSpaceableIndex).TupletBrackets,
                slurs: SpannersAt(staffSpanners, sysIdx, firstSpaceableIndex).Slurs,
                ties: SpannersAt(staffSpanners, sysIdx, firstSpaceableIndex).Ties,
                restShifts: restCollisionsOf(closingStaff));
        double closingMin = walk.Distance(closingInside.Up, closingSpec.Padding);

        return (built.ToImmutable(), closingSpec, closingMin);
    }

    /// <summary>
    /// Moves each text ROW to where the loose-line chain solved it, and everything anchored
    /// to that row with it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1046-1053 — <c>distribute_loose_lines</c>
    /// finishes by translating every loose line by <c>first_translation - solution[i] -
    /// system-Y-offset</c>, i.e. the line is PLACED by the alignment and then MOVED by the
    /// solve. This is that translate, in Lily#'s frames: the published number is the row's
    /// baseline in page Y-up, and what carries it is the row's <c>StaffLayout.Y</c> (its BAND
    /// TOP, <c>ChordNameEngraver.ChordRowTextBaseline</c> above the baseline) plus the chord
    /// symbols that hang from it.
    /// <para>
    /// ⚠️ THE SYMBOLS MOVE WITH THE BAND, not independently: a ChordNameLayout for a row
    /// stores <c>YUp = staffY - ChordRowTextBaseline</c> in its system's frame
    /// (<see cref="ChordNameEngraver"/>), so the SAME delta applies to both and the two
    /// cannot drift. The row's bar grid needs no term at all — the renderer takes it from
    /// this very <c>StaffLayout</c>.
    /// </para>
    /// <para>
    /// ★ A LYRICS ROW REACHES THIS SINCE 2026-07-28, and it needs no syllable term: the chain
    /// gives every verse an ABSOLUTE position (<c>LyricEngraver.DistributeLooseLines</c>
    /// rewrites each <c>LyricLayout.YUp</c>), so what travels here is only the band — the
    /// <c>StaffLayout.Y</c> the renderer draws the row's own bar grid from. Applying the delta
    /// to the syllables as well would move them twice.
    /// </para>
    /// </remarks>
    private static (ImmutableArray<SystemLayout>, AnnotationLayouts) ApplySolvedRowPositions(
        MultiStaffScore score, ImmutableArray<SystemLayout> systems,
        AnnotationLayouts annotations,
        IReadOnlyDictionary<(int System, int StaffIndex), double> solved)
    {
        if (solved.Count == 0)
            return (systems, annotations);

        // The MODEL staff behind each index — the row itself, which is what says whether its
        // refpoint is a chord row's baseline or a lyrics row's.
        var staffByIndex = new Dictionary<int, Staff>();
        foreach (var (_, st, idx) in score.EnumerateStaves())
            staffByIndex[idx] = st;

        // How far each solved row moved, by (system, staff) — computed once, applied to the
        // staff and to its symbols from the same number.
        var delta = new Dictionary<(int System, int StaffIndex), double>();
        var moved = systems.ToBuilder();
        foreach (var ((sysIdx, staffIndex), baselinePageY) in solved)
        {
            if (sysIdx < 0 || sysIdx >= systems.Length) continue;
            staffByIndex.TryGetValue(staffIndex, out var rowStaff);
            var system = systems[sysIdx];
            var groups = system.StaffGroups;
            if (groups.IsDefaultOrEmpty) continue;

            var newGroups = groups.ToBuilder();
            for (int g = 0; g < newGroups.Count; g++)
            {
                var staves = newGroups[g].Staves;
                if (staves.IsDefaultOrEmpty) continue;
                for (int k = 0; k < staves.Length; k++)
                {
                    if (staves[k].StaffIndex != staffIndex) continue;
                    if (rowStaff is not { } row) continue;
                    double bandTopPageY = system.Y + staves[k].Y;
                    // How far under the band's top the row's REFERENCE POINT sits. ⚠️ ASKED,
                    // NOT RESTATED: the choice between a chord row's text baseline and a
                    // lyrics row's verse-1 baseline lives in one place, because both are
                    // Lily#'s own band model and a second copy of the choice is
                    // HANDOFF 5.2.1②. This method had one for a day.
                    double d = baselinePageY - (bandTopPageY
                        - MultiStaffLayouter.TextRowRefpointBelowTop(
                            row, ChordNameEngraver.IsChordGridSheet(score.ChordNames, score.Lyrics)));
                    if (Math.Abs(d) < 1e-9) continue;
                    delta[(sysIdx, staffIndex)] = d;
                    newGroups[g] = newGroups[g] with
                    {
                        Staves = staves.SetItem(k, staves[k] with { Y = staves[k].Y + d }),
                    };
                }
            }
            moved[sysIdx] = system with { StaffGroups = newGroups.ToImmutable() };
        }
        if (delta.Count == 0)
            return (systems, annotations);

        // The symbols, by the same delta. A ChordNameLayout knows its source index, which is
        // what says which ROW it belongs to; its system comes from its measure.
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var chords = annotations.ChordNames;
        if (!chords.IsDefaultOrEmpty && !score.ChordNames.IsDefaultOrEmpty)
        {
            var newChords = chords.ToBuilder();
            for (int i = 0; i < newChords.Count; i++)
            {
                var c = newChords[i];
                if (c.SourceIndex < 0 || c.SourceIndex >= score.ChordNames.Length) continue;
                if (!measureToSystem.TryGetValue(c.MeasureIndex, out int sysIdx)) continue;
                if (!delta.TryGetValue((sysIdx, score.ChordNames[c.SourceIndex].StaffIndex),
                        out double d))
                    continue;
                newChords[i] = c with { YUp = c.YUp + d };
            }
            annotations = annotations with { ChordNames = newChords.ToImmutable() };
        }

        return (moved.ToImmutable(), annotations);
    }

    /// <summary>A text row's own skylines, self-relative to its baseline — the same ink
    /// <c>MultiStaffLayouter.BuildAllStaffSkylines</c> puts in the per-staff list.</summary>
    /// <remarks>
    /// ⚠️ EMPTY FOR A LYRICS ROW, AND THAT IS THE ONE REGIME THIS ISLAND DID NOT PORT
    /// (2026-07-28). This feeds <see cref="LeadingLinesOfSystem"/> only — a row standing ABOVE
    /// a system's first spaceable staff — and a row standing BELOW one is now a chain element
    /// with real ink (<c>LyricEngraver.DistributeLooseLines</c>). Empty makes the caller
    /// decline, so a leading lyrics row keeps the band it was laid out in.
    /// <para>
    /// ⚠️ THE FIX IS NOT "RETURN THE ROW'S INK HERE". A row's VERSES are separate Lyrics
    /// contexts to LilyPond, so a leading row is N loose lines and not one
    /// (page-layout-problem.cc:948-990 pushes each): it wants one
    /// <see cref="LooseLineSpacer.LeadingLine"/> per verse, and its syllables moved from the
    /// solve the way this system's own rows are. Returning a single merged skyline would put
    /// the band model back where the chain can no longer see it. No corpus point measures the
    /// arrangement yet, which is why it is named here rather than guessed at.
    /// </para>
    /// </remarks>
    private static (VerticalSkyline Up, VerticalSkyline Down) RowSkylinesOf(
        MultiStaffScore score, Staff row, int staffIndex,
        ImmutableArray<MeasureLayout> measures)
        => row.IsLyricsTextRow
            ? (new VerticalSkyline(VerticalDirection.Up), new VerticalSkyline(VerticalDirection.Down))
            : ChordNameEngraver.RowSkylines(
                score.TextMetrics, score.ChordNames, measures, staffIndex,
                row.PrimaryVoice.Measures);

    /// <summary>A text ROW as a run element: its own affinity and its own context's specs —
    /// the two things <c>get_spacing_spec</c> reads off a grob. The spec rule is
    /// <c>MultiStaffLayouter.NonStaffSpecsOf</c>, the one home (this file carried a private
    /// copy of it until 2026-08-26).</summary>
    private static LooseLineSpacer.RunLine RunLineOf(Staff staff, StaffSpacingParameters sp)
        => new(staff.StaffAffinity, MultiStaffLayouter.NonStaffSpecsOf(staff, sp));

}
