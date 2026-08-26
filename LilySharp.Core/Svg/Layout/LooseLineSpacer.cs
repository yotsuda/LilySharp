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
    /// The three <c>nonstaff-*</c> specs a run's springs are built from, for ONE line —
    /// <c>get_spacing_spec</c> reads them off the GROB, so which set applies is a question
    /// about the line and not about the score.
    /// </summary>
    /// <remarks>
    /// ★ THIS REPLACES THREE SCORE-WIDE STATICS (2026-08-26), and the reason is the defect
    /// they hid rather than tidiness. <c>LooseLineSpacer</c> held the Lyrics context's
    /// <c>nonstaff-relatedstaff</c> / <c>nonstaff-nonstaff</c> / <c>nonstaff-unrelatedstaff</c>
    /// specs as constants and the chain read THOSE for every line whatever context it came
    /// from, while <see cref="StaffSpacingParameters"/> held the same three for the staff
    /// layout — two homes for one quantity (HANDOFF 5.2.1②), pinned to agree by
    /// <c>SystemSpacingTests.TheTwoHomesOfTheLyricSpacingSpecs_Agree</c>. ⚠️ THE PIN ASSERTED
    /// TWO OF THE THREE: the pair it left out, <c>nonstaff-unrelatedstaff-spacing</c>, is
    /// the pair that DISAGREED (ideal 1.0 here against 0 there), and the disagreement was
    /// invisible because only this copy was reachable. One home now
    /// (<see cref="StaffSpacingParameters.Lyrics"/> / <see cref="StaffSpacingParameters.ChordNames"/>),
    /// selected per gap by <see cref="StaffAffinity.GetSpacingSpec"/>.
    /// <para>
    /// The reasoning that lived on the three constants belongs to the numbers and has moved
    /// with them; what is worth keeping HERE is why the chain cannot use one set. A run may
    /// hold a Lyrics line and a ChordNames line at once — the reported book
    /// (scratch/ベースタブLy/Untitled-6.lys) is exactly that — and the two contexts declare
    /// different numbers under the same property names: Lyrics'
    /// <c>nonstaff-relatedstaff-spacing</c> has <c>(basic-distance . 5.5)</c> and ChordNames'
    /// has no members at all past a padding, which leaves the caller's
    /// <c>Spring spring (1.0, 0.0)</c> standing (page-layout-problem.cc:1035).
    /// </para>
    /// </remarks>
    internal readonly record struct RunLine(
        int? Affinity, StaffSpacingParameters.NonStaffSpacing Specs);

    /// <summary>
    /// The SPACEABLE staff a run hangs from, as <c>get_spacing_spec</c> sees it: no
    /// <c>staff-affinity</c>, which is the whole of what makes a line spaceable
    /// (page-layout-problem.cc:1173-1177), and no <c>nonstaff-*</c> set that any branch of
    /// the selection will read.
    /// </summary>
    /// <remarks>
    /// It exists so the walk over a run can be written as one loop with the anchor as its
    /// zeroth line, rather than as "the first gap, then the rest" — the shape that made the
    /// first gap's spec a constant and hid that it belonged to a particular context.
    /// </remarks>
    public static readonly RunLine SpaceableStaffLine = new(null, default);

    /// <summary>
    /// A NOTE-BOUND lyric line — a <c>\lyricsto</c> verse hanging under its own staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:648 — a Lyrics context declares
    /// <c>staff-affinity = UP</c> whether or not its syllables are associated with a voice.
    /// Association decides which COLUMN a syllable stands on, not what holds the line
    /// (the LYRC/LYRR and LYRV/LYRRV pairs measure that: LilyPond reads them line for line
    /// the same).
    /// </remarks>
    public static RunLine NoteBoundLyricLine(StaffSpacingParameters sp)
        => new(StaffAffinityDirection.Up, sp.Lyrics);

    /// <summary>
    /// A ChordNames line — an independent chords row or a staff's attached chord line;
    /// LilyPond makes no distinction, both are the ChordNames context.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:719-723 nonstaff-relatedstaff-spacing —
    /// <c>staff-affinity = DOWN</c> and the ChordNames <c>nonstaff-*</c> set are the whole
    /// of what the context declares.
    /// One constructor, two readers (the pair walk's trailing element and the chain's),
    /// because a second spelling of "which line is this" is HANDOFF 5.2.1②.
    /// </remarks>
    public static RunLine ChordNamesLine(StaffSpacingParameters sp)
        => new(StaffAffinityDirection.Down, sp.ChordNames);

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
