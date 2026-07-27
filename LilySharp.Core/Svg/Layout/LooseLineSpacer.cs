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
