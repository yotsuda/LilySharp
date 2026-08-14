// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/page-layout-problem.cc
//     Copyright (C) 2009--2026 Joe Neeman <joeneeman@gmail.com>
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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The second spacing pass: lines that are NOT spaceable (lyrics, and in LilyPond also
/// dynamics and figured bass) are left out of the page's spring chain and distributed
/// afterwards, into the room between two already-placed spaceable staves.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:1025-1054
/// <c>Page_layout_problem::distribute_loose_lines</c> — one <c>Simple_spacer</c> over the
/// gaps between the two placed lines, solved to EXACTLY the room they leave
/// (<c>spacer.solve (first_translation - last_translation, false)</c>), and every loose
/// line put at the running sum of that solution.
/// <para>
/// ⚠️ THE ROOM DOES NOT GROW FOR THE LINES. A loose line is absent from the page's own
/// chain, so <c>system-system-spacing</c> is whatever it would have been without it —
/// MEASURED (audit/lp-geometry/probes/page-vertical.ly, books LYRC and LYRV): the staff
/// gap is 12.000000 with one lyric line and 12.000000 with two. A second verse is not
/// given room, it is SQUEEZED INTO the room that already exists, and this solve is what
/// does the squeezing: on book LYRV it runs at force -0.841556 and pulls the first line
/// from its basic-distance 5.500000 down to its ink floor 3.737890.
/// </para>
/// <para>
/// The counterpart of that is the reservation: what the system skyline holds for a lyric
/// block is <c>Align_interface::get_minimum_translations</c>, the block at its ALIGNMENT
/// MINIMUM, not the distance it is drawn at (:593-599 hands the skyline builder the
/// minimum translations, and align-interface.cc:235-238 adds the spec's basic-distance
/// only behind the PURE branch, which that call is not). That reservation is
/// <c>LayoutEngine.LyricReservationBelowSystem</c>, and it is <see cref="AlignmentWalk"/> —
/// the same walk this chain's own minimums come from, which is the point.
/// </para>
/// </remarks>
internal static class LooseLineSpacer
{
    /// <summary>
    /// The spring from a spaceable staff down to the loose line that belongs to it —
    /// a <c>staff-affinity = UP</c> line's <c>nonstaff-relatedstaff-spacing</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:649-652 (the Lyrics context's override) reached
    /// through lily/page-layout-problem.cc:1284-1294 <c>get_spacing_spec</c> — before is
    /// spaceable, after is not, and the affinity is UP.
    /// <para>
    /// ⚠️ NO <c>minimum-distance</c> MEMBER, and that is load-bearing rather than an
    /// omission: <c>set_default_strength</c> reads the compress strength off the spec as
    /// <c>ideal - minimum-distance</c> (lily/spring.cc:205-210), so it is 5.5 - 0 = 5.5,
    /// and the <c>ensure_min_distance</c> that raises the floor to the ink afterwards does
    /// NOT restrengthen the spring (spring.cc:155-159). The spring therefore leaves its
    /// floor at <c>f = (floor - 5.5) / 5.5</c>, not at f = -1 — which is the whole of the
    /// 0.158444 that stood in this island's way for a session (see the LYRV probe header).
    /// </para>
    /// </remarks>
    public static readonly VerticalSpacingSpec NonStaffRelatedStaff = new()
    {
        BasicDistance = 5.5,
        MinimumDistance = 0,
        Padding = SkylineDrop.RelatedStaffPadding,
        Stretchability = 1,
    };

    /// <summary>
    /// The spring between two loose lines that share an affinity — verse to verse.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:653-657 reached through
    /// lily/page-layout-problem.cc:1315-1332 — neither neighbour is spaceable, the upper
    /// one's affinity is UP and the lower one's is not DOWN, so the spec is the UPPER
    /// line's <c>nonstaff-nonstaff-spacing</c>.
    /// <para>
    /// This spring is RIGID in both directions, and the two halves come from different
    /// places: the stretch 0 is DECLARED by the spec (<c>(stretchability . 0)</c>,
    /// ly/engraver-init.ly:657) and the compress 0 is DERIVED, <c>max(0, 0 - 2.8)</c>
    /// (spring.cc:205-210). So it sits at <c>max(2.8, ink + 0.2)</c> at every force.
    /// ⚠️ CORRECTED 2026-07-26: this said <c>set_default_strength</c> produced the stretch 0.
    /// It does run (page-layout-problem.cc:1354) but the declaration overrides it right
    /// after (:1356-1357); the two agree only because this spec's ideal is 0 as well. That is why <c>lyrics.verse-step</c> reads 2.800000 even on a page whose
    /// loose chain is compressed hard enough to pull the line above it off its ideal.
    /// </para>
    /// </remarks>
    public static readonly VerticalSpacingSpec NonStaffNonStaff = new()
    {
        BasicDistance = 0,
        MinimumDistance = SkylineDrop.NonStaffNonStaffMinimum,
        Padding = SkylineDrop.NonStaffNonStaffPadding,
        Stretchability = 0,
    };

    /// <summary>
    /// The spring from the LAST line of a block to the staff on the far side of it — the
    /// side the line's <c>staff-affinity</c> does not point at. This is what closes a block
    /// that sits BETWEEN two staves of one system, where there is no system boundary and so
    /// no null line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1299-1312 — <c>before</c> is the loose
    /// line, its affinity is UP and <c>after</c> is spaceable, so <c>get_spacing_spec</c>
    /// returns the LINE's <c>nonstaff-unrelatedstaff-spacing</c> with
    /// <c>add_stretchability (…, LARGE_STRETCH)</c> on top.
    /// <para>
    /// ⚠️ ONLY THE PADDING IS THE SPEC'S. ly/engraver-init.ly:658 overrides
    /// <c>nonstaff-unrelatedstaff-spacing.padding</c> and declares nothing else, and
    /// <c>read_spacing_spec</c> writes only the members that are there — so the ideal 1.0
    /// and the compress strength 1.0 are the caller's own <c>Spring spring (1.0, 0.0)</c>
    /// (:1035, <c>set_default_strength</c> making the inverse strengths
    /// <c>distance - min_distance</c>), exactly as for <see cref="NullNeighbour"/>. Writing
    /// a basic-distance of 0 here would be a DIFFERENT spring, not a tidier spelling of
    /// this one.
    /// </para>
    /// <para>
    /// ⚠️ THE STRETCH IS LARGE, NOT HUGE — 10e5 against the null neighbour's 10e7
    /// (:1262-1263). Both exist so that a block keeps close to the staff it belongs to
    /// while the page opens around it; the two orders of magnitude are LilyPond's own and
    /// are never interchangeable, since the two springs can meet in one chain.
    /// </para>
    /// </remarks>
    public static readonly VerticalSpacingSpec NonStaffUnrelatedStaff = new()
    {
        BasicDistance = 1.0,
        MinimumDistance = 0,
        Padding = SkylineDrop.UnrelatedStaffPadding,
        // LILYPOND-REF: lily/page-layout-problem.cc:1262 LARGE_STRETCH = 10e5.
        Stretchability = 10e5,
    };

    /// <summary>
    /// The spring from a loose line to a NULL neighbour — the page edge, or the null line
    /// LilyPond inserts at a system boundary to break the affinity to the previous system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1274-1275 — <c>get_spacing_spec</c>
    /// returns <c>add_stretchability (SCM_EOL, HUGE_STRETCH)</c> when either neighbour is
    /// null, with LilyPond's own comment (:1257-1261) that this is deliberate so "a
    /// spacing-affinity UP line at the bottom of the page will still be placed close to
    /// its staff". The ideal and the compress strength come from the spring the caller
    /// starts with, <c>Spring spring (1.0, 0.0)</c> (:1035), because an empty spec alters
    /// neither.
    /// <para>
    /// ⚠️ THE IDEAL 1.0 IS NOT DECORATION. On book LYRV this spring is the one that is off
    /// its floor when the chain is compressed, at length <c>1 + f = 0.158444</c>, and
    /// adding the chain's minimums by hand without it is what made the sum come out at
    /// 11.841556 against a measured room of 12.000000.
    /// </para>
    /// </remarks>
    public static readonly VerticalSpacingSpec NullNeighbour = new()
    {
        BasicDistance = 1.0,
        MinimumDistance = 0,
        Padding = 0,
        // LILYPOND-REF: lily/page-layout-problem.cc:1263 HUGE_STRETCH = 10e7.
        Stretchability = 10e7,
    };

    /// <summary>
    /// One gap in the chain: the spec that governs it and the minimum the geometry
    /// imposes on it (LilyPond's <c>min_distances[i]</c>, applied with
    /// <c>ensure_min_distance</c>).
    /// </summary>
    internal readonly record struct Gap(VerticalSpacingSpec Spec, double MinDistance);

    /// <summary>
    /// One non-spaceable line the NEXT system leads with, as the chain sees it: its own
    /// skylines, the spec of the spring that reaches it, and that spring's minimum.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:948-990 — the run of loose lines a system
    /// opens with is pushed onto the SAME <c>loose_lines</c> vector the previous system's
    /// block is in, so one chain spans the system boundary and the two are solved together.
    /// <para>
    /// ⚠️ <see cref="MinInto"/> IS MEANINGLESS FOR THE FIRST LINE, and deliberately so: the
    /// first line's minimum is the SYSTEM-level term
    /// <c>elements_[i].min_distance + elements_[i].padding</c> (:971-972), which is measured
    /// from the PREVIOUS system's bottom profile and so is only knowable inside the chain's
    /// own walk. Every later line's is <c>min_offsets[k-1] - min_offsets[k]</c> (:961-962),
    /// the NEXT system's own alignment, which is what this carries.
    /// </para>
    /// </remarks>
    internal readonly record struct LeadingLine(
        VerticalSkyline Up, VerticalSkyline Down,
        VerticalSpacingSpec SpecInto, double MinInto, int StaffIndex);

    /// <summary>
    /// What closes a note-bound block's chain and how much room it has — the far end of
    /// LilyPond's <c>distribute_loose_lines</c> call.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:936-939 — the room is
    /// <c>last_spaceable_line_translation - (-solution_[spring_idx])</c>, i.e. this system's
    /// last spaceable staff's reference point down to the next system's first.
    /// </remarks>
    /// <param name="Room">The refpoint-to-refpoint span the chain is solved into.</param>
    /// <param name="NextStaffMinDistance">
    /// The minimum on the spring that reaches the next system's first spaceable staff when
    /// NOTHING is between — <c>elements_[i].padding - min_offsets[0]</c> (:931-932). NaN
    /// means the chain runs to the page edge instead (:1004-1013). Superseded by
    /// <paramref name="Lines"/> when the next system leads with loose lines: the chain then
    /// reaches those first and closes on <paramref name="ClosingSpec"/>.
    /// </param>
    /// <param name="Lines">
    /// The next system's LEADING non-spaceable lines, top to bottom. Empty on every score
    /// without a chords/lyrics track, which is what leaves those chains exactly as they were.
    /// </param>
    /// <param name="ClosingSpec">
    /// The spring from the last of <paramref name="Lines"/> to the next system's first
    /// spaceable staff — that line's OWN <c>nonstaff-relatedstaff-spacing</c>
    /// (get_spacing_spec :1299-1312). Null when <paramref name="Lines"/> is empty.
    /// </param>
    /// <param name="ClosingMinDistance">
    /// That spring's minimum, <c>min_offsets[k-1] - min_offsets[k]</c> (:924-925) — the next
    /// system's own alignment step from its last leading line down to its first staff.
    /// </param>
    internal sealed record ChainEnd(
        double Room,
        double NextStaffMinDistance,
        ImmutableArray<LeadingLine> Lines,
        VerticalSpacingSpec? ClosingSpec,
        double ClosingMinDistance);

    /// <summary>
    /// Distributes <paramref name="gaps"/>.Count - 1 loose lines into
    /// <paramref name="room"/>, returning where each of them sits below the anchor —
    /// element k is the k-th loose line, k from 1.
    /// </summary>
    /// <param name="gaps">
    /// The chain, top to bottom: the anchor staff to the first loose line, then line to
    /// line, then the last line to whatever closes the chain (the null that precedes the
    /// next system's staff, or the page edge).
    /// </param>
    /// <param name="room">
    /// <c>first_translation - last_translation</c>: the distance the already-placed lines
    /// at the two ends leave between them. <see cref="double.PositiveInfinity"/> or NaN
    /// means the caller does not know it, and the chain is laid out at force 0 instead —
    /// every spring at <c>max(min, ideal)</c>, which is where a chain with room to spare
    /// ends up anyway.
    /// </param>
    /// <returns>
    /// Positions measured DOWN from the anchor staff's reference point, one per gap
    /// (index 0 is the anchor itself, at 0).
    /// </returns>
    public static ImmutableArray<double> Distribute(IReadOnlyList<Gap> gaps, double room)
    {
        if (gaps.Count == 0)
            return ImmutableArray<double>.Empty;

        var springs = ImmutableArray.CreateBuilder<Spring>(gaps.Count);
        foreach (var (spec, min) in gaps)
        {
            // LILYPOND-REF: lily/page-layout-problem.cc:1033-1037 — the spec builds the
            // spring (alter_spring_from_spacing_spec) and the geometry then raises its
            // FLOOR through ensure_min_distance, which is exactly CreateSpring's contract.
            springs.Add(LayoutUtilities.CreateSpring(spec, min));
        }

        var solver = new SpringSolver(springs.ToImmutable());

        // LILYPOND-REF: lily/page-layout-problem.cc:1042-1045 — solved NOT ragged, so a
        // chain with slack really does stretch, and the positions are the running sum at
        // the solved force.
        double force = double.IsFinite(room) ? solver.Solve(room, ragged: false).Force : 0.0;
        return solver.GetPositions(force);
    }
}
