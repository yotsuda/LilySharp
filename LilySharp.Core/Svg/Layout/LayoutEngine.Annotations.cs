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
    /// Inputs to <see cref="CalculateAnnotationLayouts"/>. Collapses the former
    /// 21-parameter signature into one context object; the optional members default
    /// to null/empty, matching the old optional parameters exactly.
    /// </summary>
    private sealed class AnnotationLayoutContext
    {
        public required Score? Score { get; init; }

        /// <summary>The whole multi-staff score — what the attached chord line's run walk
        /// reads (<c>MultiStaffLayouter.AttachedChordLineInRun</c> asks it which staves are
        /// spaceable and which symbols are a chords track). Null only in constructions that
        /// predate it; the two passes both set it.</summary>
        public MultiStaffScore? MultiScore { get; init; }
        /// <summary>Whether the laid-out score is a staff-less lead sheet — carried
        /// here because <see cref="Score"/> is the FLAT model and cannot answer it;
        /// both builders read <c>MultiStaffScore.IsLeadSheet</c>. The stanza-number
        /// engraver anchors its labels at the line start on a lead sheet (the grid
        /// opens every line with a bar line the label must clear).</summary>
        public bool IsLeadSheet { get; init; }

        /// <summary>The global staff index of the row that draws a lead sheet's barlines,
        /// −1 when there is none — carried here for the same reason
        /// <see cref="IsLeadSheet"/> is, and read from the same place the renderer reads it
        /// (<c>MultiStaffScore.GridBarlineRowIndex</c>). The bar-number anchor needs it:
        /// on a system with no staff at all, that row is the only one whose ink reaches the
        /// number's column.</summary>
        public int GridBarlineRowIndex { get; init; } = -1;

        /// <summary>
        /// The faces this score's text is measured against — the whole-score answer, not
        /// the primary staff's.
        /// </summary>
        /// <remarks>
        /// It is its OWN member rather than <c>Score?.TextMetrics</c> because
        /// <see cref="Score"/> is nullable here and a <c>?? bundled</c> would be a silent
        /// choice made in the wrong place (HANDOFF RULES §7.7). The caller holds the
        /// MultiStaffScore and knows the answer; this makes it say so.
        /// </remarks>
        public required Rendering.ScoreTextMetrics Fonts { get; init; }

        public required ImmutableArray<SystemLayout> Systems { get; init; }
        public required ImmutableArray<DynamicItem> Dynamics { get; init; }
        public required ImmutableArray<ArticulationItem> Articulations { get; init; }
        public required ImmutableArray<GraceNoteItem> GraceNotes { get; init; }
        public required ImmutableArray<LyricItem> Lyrics { get; init; }
        public required ImmutableArray<MusicMarkItem> MusicMarks { get; init; }
        public required ImmutableArray<CustomTextItem> CustomTexts { get; init; }
        public required ImmutableArray<VoltaBracketItem> VoltaBrackets { get; init; }
        public required ImmutableArray<TupletBracketItem> TupletBrackets { get; init; }
        public required ImmutableArray<ArpeggioItem> Arpeggios { get; init; }
        public required ImmutableArray<Measure> Measures { get; init; }
        public ImmutableArray<FiguredBassItem>? FiguredBasses { get; init; }
        public ImmutableArray<ChordNameItem>? ChordNames { get; init; }
        public ImmutableArray<PercentRepeatItem>? PercentRepeats { get; init; }
        public ImmutableArray<CrossStaffLayout>? CrossStaffLayouts { get; init; }
        public ImmutableArray<TrillSpannerItem>? TrillSpanners { get; init; }
        public ImmutableArray<BeamGroup>? BeamGroups { get; init; }
        public ImmutableArray<BeamLayout>? BeamLayouts { get; init; }

        /// <summary>
        /// The drawn ties, for the scripts' tie supports ("scripts avoid ties" —
        /// ArticulationEngraver's tiesAtBound). Supplied by BOTH passes — the
        /// preliminary pass lays its own ties out just above its context — because a
        /// table the two passes disagree about is invisible in the drawing and comes
        /// out as spacing (see <see cref="RestCollisionsOf"/>'s remark).
        /// </summary>
        public ImmutableArray<TieLayout> TieLayouts { get; init; }

        /// <summary>
        /// The drawn slurs, for the scripts' slur avoidance (outside_slur_callback —
        /// an 'around/'outside script rides off the bow). Supplied by BOTH passes,
        /// same reason as <see cref="TieLayouts"/>.
        /// </summary>
        public ImmutableArray<SlurLayout> SlurLayouts { get; init; }
        public IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? SystemSkylines { get; init; }

        /// <summary>
        /// Per system, the per-staff UP/DOWN skylines THAT system was placed against,
        /// indexed by global staff index — the same lists
        /// <c>MultiStaffLayouter.BuildAllStaffSkylines</c> produced for the alignment.
        /// </summary>
        /// <remarks>
        /// ⚠️ SUPPLIED SO THERE IS ONE SILHOUETTE AND NOT TWO. A note-bound lyric line is
        /// placed against its anchor staff's down-skyline, and until 2026-08-04 that skyline
        /// was REBUILT here from <c>SkylineBuilder.BuildStaffSkylines</c> with every side
        /// table left at its default — no dynamics, articulations, tuplet brackets, slurs,
        /// ties or beams — under a comment claiming it was "the same silhouette the room was
        /// measured from". It was not: the room passes all of them. So a dynamic under the
        /// staff widened the gap to the staff below and the syllable stayed where it was,
        /// and the syllable was drawn over the dynamic while the gap between the staves
        /// stayed correct (LyricStaffOrderTests
        /// <c>LyricBaseline_RespondsToADynamicUnderItsOwnStaff</c>).
        /// <para>
        /// Null in the PRELIMINARY pass, which runs before the page is laid out. That pass
        /// never asks: the lookup is only wired up when <see cref="NoteBoundAnchorY"/> is
        /// non-empty, and the preliminary context supplies none.
        /// </para>
        /// </remarks>
        public IReadOnlyList<List<(VerticalSkyline Up, VerticalSkyline Down)>>? StaffSkylines { get; init; }

        /// <summary>Per system, the pair-run suppliers that system was placed and sprung
        /// with — carried from the per-system pass (finding 4-4) so the drawn-baseline
        /// walk consumes the SAME suppliers (and their within-pass caches) instead of
        /// rebuilding them per keystroke. Null in the preliminary pass, which never asks
        /// (its <see cref="StaffSkylines"/> is null too, and the lookup is gated on it).</summary>
        public IReadOnlyList<MultiStaffLayouter.PairRunSources>? RunSources { get; init; }

        /// <summary>Per system, per staff: the pedal bracket lines the DOWN profiles in
        /// <see cref="StaffSkylines"/> were solved with (Y-up about each staff's middle
        /// line). The pedal draw reads these so the drawn line is the reserved line --
        /// one computation, two readers (PedalEngraver.SolveAndSeed).</summary>
        public IReadOnlyList<List<ImmutableArray<PedalEngraver.SolvedPedalLine>>>? PedalLines { get; init; }

        /// <summary>Per system, per staff: the TEXT-style pedal rows the DOWN profiles were
        /// solved with (family rank → baseline, Y-up about that staff's middle line). The
        /// mark draw reads these — one computation, two readers
        /// (PedalEngraver.SolveAndSeedText).</summary>
        public IReadOnlyList<List<ImmutableArray<PedalEngraver.SolvedPedalRow>>>? PedalRows { get; init; }

        /// <summary>
        /// Per system, the inside-staff spanners each staff's skyline was built from,
        /// indexed by global staff index — the slurs, ties and tuplet brackets
        /// <c>MultiStaffLayouter.BuildAllStaffSkylines</c> laid out for the alignment.
        /// </summary>
        /// <remarks>
        /// ⚠️ FOR THE PROFILES THIS PASS STILL BUILDS, and for the same reason
        /// <see cref="RestCollisionsOf"/> is here: a consumer that cannot read
        /// <see cref="StaffSkylines"/> — it needs the silhouette WITHOUT the movers it is
        /// about to place — still has to reserve everything LilyPond calls inside-staff ink,
        /// and none of Slur, Tie or TupletBracket declares an <c>outside-staff-priority</c>
        /// — ⚠️ the trap being that all three DO list <c>outside-staff-interface</c>, which
        /// is not the same thing. (Addresses at each grob's seeding site in
        /// <c>SkylineBuilder</c>, not repeated here.) Until 2026-08-04 the
        /// outside-staff stacker's seed was built with these three at their defaults, so a
        /// below-staff dynamic was engraved straight through a slur's bow, a tie's bow and a
        /// lower voice's tuplet bracket. MEASURED on one book each, against the room as the
        /// positive control (<c>OutsideStaffSeedTests</c>): the room widened by 1.417596 /
        /// 0.420441 / 1.727738 while the dynamic did not move at all.
        /// <para>
        /// ⚠️ CARRIED, NOT RECOMPUTED. See <c>MultiStaffLayouter.StaffInsideSpanners</c>:
        /// asking the engravers again here would be a second spelling of the room's answer
        /// AND a whole-staff walk added to a pass that is not memoised per system, i.e. paid
        /// on every keystroke.
        /// </para>
        /// <para>
        /// ⚠️ SUPPLIED BY BOTH PASSES, AND REQUIRED, since 2026-08-14. The preliminary pass
        /// runs after <c>LayoutSystems</c> — the systems it iterates ARE the placed list the
        /// room built these tables from — and while it carried none, its profile consumers
        /// rebuilt with <see cref="SpannersOf"/> returning empty: the page reserved a mover
        /// at the spanner-free height the drawn pass then cleared
        /// (<c>PreliminaryPassSeedTests</c>). Required for the reason
        /// <see cref="RestCollisionsOf"/> gives: a nullable read with <c>?.</c> would let a
        /// third construction put the divergence back silently, with the suite green.
        /// </para>
        /// </remarks>
        public required IReadOnlyList<List<MultiStaffLayouter.StaffInsideSpanners>> StaffSpanners { get; init; }

        /// <summary>
        /// One (system, staff)'s inside-staff spanners, or an empty set when this pass has
        /// none — the preliminary pass, or an index outside what was placed.
        /// </summary>
        /// <remarks>
        /// ⚠️ ONE LOOKUP FOR ALL FOUR CONSUMERS. Each of them rebuilds a profile of its own
        /// and so needs the same three tables; spelling the bounds check per call site is how
        /// three of them could disagree about the empty case. An empty
        /// <see cref="MultiStaffLayouter.StaffInsideSpanners"/> is exactly what
        /// <c>SkylineBuilder.BuildStaffSkylines</c> already treats as "no such ink", so the
        /// absent case needs no branch at the call sites.
        /// </remarks>
        public MultiStaffLayouter.StaffInsideSpanners SpannersOf(int systemIndex, int staffIndex)
            => SpannersAt(StaffSpanners, systemIndex, staffIndex);

        /// <summary>Per system, each staff's INSIDE-staff skyline as the room built it —
        /// LilyPond's one profile per VerticalAxisGroup. Supplied by BOTH passes and
        /// required, for the reasons <see cref="StaffSpanners"/> gives — the preliminary
        /// pass's rebuilds held no scripts, fingerings or spanners, and each rebuild walked
        /// the system's music on every keystroke.</summary>
        public required IReadOnlyList<List<(VerticalSkyline Up, VerticalSkyline Down)>> StaffInside { get; init; }

        /// <summary>
        /// One (system, staff)'s inside-staff skyline — a COPY, ready to be raised into the
        /// caller's frame — or null when this pass has none (the preliminary pass), in which
        /// case the caller builds its own.
        /// </summary>
        /// <remarks>
        /// ★ THIS IS WHAT THE THREE REBUILD SITES NOW READ. They used to call
        /// <c>SkylineBuilder.BuildStaffSkylines</c> with their own subset of the side tables
        /// — which is why the chord row and the loose chain were missing scripts and beams —
        /// and each rebuild walked the whole staff again. The reasons they could not read
        /// <see cref="StaffSkylines"/> all name the same object: the movers are not in it, and
        /// neither is the chord row's own reserved band.
        /// LILYPOND-REF: lily/axis-group-interface.cc:914-935 inside_staff_skylines.
        /// </remarks>
        public (VerticalSkyline Up, VerticalSkyline Down)? InsideOf(int systemIndex, int staffIndex)
            => InsideAt(StaffInside, systemIndex, staffIndex);

        /// <summary>
        /// The STORED profile instances behind <see cref="InsideOf"/> — the above-stack
        /// memo's reference key. <see cref="InsideOf"/> hands out a fresh COPY per call
        /// (its consumers raise the skylines into their own frame), so the copy can never
        /// be an identity; the identity is the table entry itself, which an unchanged
        /// system gets back from <see cref="SystemLayoutCache"/> as the same instances.
        /// Null when the table holds no such (system, staff) — the memo then stacks that
        /// system live rather than risking a false match (AboveStackMemo's remarks).
        /// </summary>
        public (object Up, object Down)? InsideIdentityOf(int systemIndex, int staffIndex)
        {
            if (systemIndex < 0 || systemIndex >= StaffInside.Count
                || staffIndex < 0 || staffIndex >= StaffInside[systemIndex].Count)
                return null;
            var (up, down) = StaffInside[systemIndex][staffIndex];
            return (up, down);
        }

        /// <summary>This pass's above-staff stacking memo, or null outside the
        /// incremental session (batch renders, tests). See <see cref="AboveStackMemo"/>.</summary>
        public AboveStackMemo? AboveStackMemo { get; init; }

        /// <summary>This pass's below-staff stacking memo, or null outside the
        /// incremental session — the below-side mirror (finding 4-3).
        /// See <see cref="BelowStackMemo"/>.</summary>
        public BelowStackMemo? BelowStackMemo { get; init; }

        /// <summary>This pass's per-(staff, system) fingering memo, or null outside the
        /// incremental session — where null means the pass runs the whole-score island
        /// and walk it always ran. See <see cref="FingScriptMemo"/>.</summary>
        public FingScriptMemo? FingScriptMemo { get; init; }

        /// <summary>The SHARED per-system lyric verse-skyline memo (one instance serves
        /// both passes — see <see cref="VerseSkylineMemo"/>), or null outside the
        /// incremental session, where the skylines are built fresh as always.</summary>
        public VerseSkylineMemo? VerseSkylines { get; init; }

        /// <summary>The SHARED per-(family, system) lyric chain-prefix memo (see
        /// <see cref="LyricChainMemo"/>), or null outside the incremental session.</summary>
        public LyricChainMemo? LyricChains { get; init; }

        /// <summary>
        /// A staff's rest/note collision shifts — <c>MultiStaffLayouter.RestCollisionsOf</c>
        /// itself, so this pass reserves each rest where the ROOM reserved it and where the
        /// renderer draws it.
        /// </summary>
        /// <remarks>
        /// ⚠️ SUPPLIED FOR THE PROFILES THIS PASS STILL BUILDS. Three call sites here cannot
        /// read <see cref="StaffSkylines"/>, and for a real reason each: the figured-bass drop
        /// and the outside-staff stacker's seed need the INSIDE-staff silhouette, before the
        /// movers they are about to place are merged into it, and a chord row under a non-top
        /// staff would otherwise clear the band that row itself reserved
        /// (<c>MultiStaffLayouter.ReserveChordRowBand</c>). So they call
        /// <c>SkylineBuilder.BuildStaffSkylines</c> — and until 2026-08-04 they called it with
        /// this table at its default, i.e. with every rest at its unshifted position. A rest
        /// another voice has pushed out of the staff is the one rest that reaches these
        /// consumers at all, so the omission was the whole of it: MEASURED on a one-staff book
        /// whose second voice holds printed rests, the below-staff dynamic read -4.546000 with
        /// the rests printed and -4.546000 with them spacers — the moved rest contributed
        /// nothing and the dynamic was engraved on top of it
        /// (<c>DynamicPlacementTests.BelowDynamic_ClearsARestAnotherVoicePushedOutOfTheStaff</c>).
        /// <para>
        /// ⚠️ WHAT IS STILL NOT IN IT, named rather than left to be found: the BEAM rest shift
        /// (<c>ElementCoordinator.CalculateRestShifts</c>, <c>Beam::rest_collision_callback</c>).
        /// LilyPond has one Rest grob that both passes translate, so the silhouette sees the
        /// outer position of the two; Lily# reserves only the collision half — here, in the
        /// room, and in the loose-line chain alike. That is one gap in one shape, not four,
        /// which is why this hands over the room's table rather than the renderer's merged one
        /// (<c>ScoreLayout.RestShifts</c>): the merged table is available in this pass and not
        /// in the room, and taking it here would put the reservation on two spellings again.
        /// </para>
        /// <para>
        /// ⚠️ REQUIRED, NOT NULLABLE, and that is the whole guard. A nullable one read with
        /// <c>?.</c> would let a third construction of this context omit it and put every
        /// rest back at its unshifted position — silently, with the suite green, which is
        /// exactly how the defect this closes survived. <see cref="StaffSkylines"/> is
        /// nullable because it has a real absent case (the preliminary pass runs before the
        /// systems are placed); this one has none, since <c>Rest_collision</c> needs only the
        /// music. HANDOFF 7.7's "fallback / try で握りつぶす", caught by that list.
        /// </para>
        /// </remarks>
        public required Func<Staff, ImmutableDictionary<RestShiftKey, double>> RestCollisionsOf { get; init; }

        public bool TupletForceStemUp { get; init; }
        public ImmutableArray<Voice> StaffVoices { get; init; }
        public Dictionary<int, ImmutableArray<Voice>>? VoicesByStaff { get; init; }
        public Dictionary<int, ImmutableArray<Measure>>? MeasuresByStaff { get; init; }
        public Dictionary<int, double>? StaffYByIndex { get; init; }
        public Dictionary<int, double>? NoteBoundAnchorY { get; init; }
        public Dictionary<int, Staff>? StaffByIndex { get; init; }

        /// <summary>Per system, the ABSOLUTE X of the line-start meter column's ink
        /// left, or NaN when that system's prefix engraves no meter — the break-align
        /// anchor a measure-start metronome mark self-aligns LEFT on. See
        /// <see cref="BuildPrefixTimeSignatureX"/>.</summary>
        public Func<int, double>? PrefixTimeSignatureX { get; init; }

        /// <summary>Per system, the ABSOLUTE X of the line-start break-align anchor a
        /// (staff-bar key-signature clef)-aligned mark lands on — key right, else clef
        /// right — or NaN without a clef column. See
        /// <see cref="BuildPrefixMarkAnchorX"/>.</summary>
        public Func<int, double>? PrefixMarkAnchorX { get; init; }

        /// <summary>
        /// By MEASURE index: the absolute X of the bar line DRAWN at a system start, or NaN
        /// when that measure does not open a system or opens one with no bar line. See
        /// <see cref="BuildLineStartBarlineX"/>.
        /// </summary>
        public Func<int, double>? LineStartBarlineX { get; init; }

        /// <summary>Device-down from the system origin to the LAST SPACEABLE staff's top
        /// line — the staff a note-bound lyric block hangs from. 0 on a one-staff system,
        /// which is why that case is untouched by it.</summary>
        public double LastSpaceableStaffY { get; init; }

        /// <summary>Per system, the room its lyric chain is solved into and what closes it
        /// — see <see cref="BuildLooseChainEnds"/>. Null in the preliminary pass, which
        /// runs before the page exists, so that pass lays the block out at force 0.</summary>
        public Func<int, LooseLineSpacer.ChainEnd?>? LooseChainEnd { get; init; }

        /// <summary>Per system, the independent lyrics ROWS that stand below its last
        /// spaceable staff — elements of that system's loose block, after its note-bound
        /// verses. See <see cref="BuildTrailingRowStaves"/>. Null in the preliminary pass,
        /// which runs before the systems are placed, so that pass leaves a row in its
        /// band.</summary>
        public Func<int, IReadOnlyList<int>>? TrailingRowStaves { get; init; }

        /// <summary>Per system and anchor staff, the rows standing between it and the next
        /// spaceable staff. See <see cref="BuildBetweenRowStaves"/>.</summary>
        public Func<int, int, IReadOnlyList<int>>? BetweenRowStaves { get; init; }

        /// <summary>
        /// FILLED BY THE PASS, not supplied to it: where the loose-line solve put each text
        /// ROW, by (system, global staff index), as a baseline in page Y-up.
        /// </summary>
        /// <remarks>
        /// LILYPOND-REF: lily/page-layout-problem.cc:1046-1053 — <c>distribute_loose_lines</c>
        /// translates its loose lines after solving, so a row's position is an OUTPUT of the
        /// lyric chain and not an input to the layout. The engraver publishes it
        /// (<c>LyricEngraver.SolvedRowBaselines</c>) and <see cref="LayoutEngine"/> applies
        /// it once the annotation pass is over, because the row's Y is read both inside that
        /// pass (<c>ChordNameEngraver</c>) and after it (the renderer's grid barlines).
        /// </remarks>
        public Dictionary<(int System, int StaffIndex), double> SolvedRowBaselines { get; } = new();
    }

    /// <summary>
    /// Per system, the ABSOLUTE X of the line-start TimeSignature column's ink left
    /// (NaN when that system's prefix engraves no meter) — the break-align anchor the
    /// metronome mark self-aligns LEFT on. Derived from the SAME line-start prefix
    /// table the spring model and the measure layout solve
    /// (<see cref="MultiStaffLayouter.SolveLineStartPrefix"/>), so the annotated,
    /// reserved and drawn meter columns cannot drift apart.
    /// </summary>
    /// <summary>
    /// By MEASURE index: where the bar line that OPENS a system is actually drawn, or NaN
    /// when there is none. A mark that break-aligns on <c>staff-bar</c> needs this because at
    /// a line start the bar line is not at the measure's X — the redrawn clef/key/time prefix
    /// stands there and the stroke is nudged past it by
    /// <see cref="Rendering.SharedRenderer.LineStartBarClearance"/>, which this reads from
    /// that one home so the mark cannot land beside the stroke it is aligning to.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE PREMISE THIS REPLACES was "at a line start the bar line is invisible, so the
    /// anchor falls back to the prefix" — true of a system that simply continues, false of
    /// one that opens with a repeat: a <c>|:</c> IS drawn there. The owner's book showed a
    /// coda sign at the system's left edge (x 0.30) with the <c>|:</c> at 6.44.
    /// LILYPOND-REF: scm/define-grobs.scm CodaMark declares
    ///   <c>(break-align-symbols . (staff-bar key-signature clef))</c> — the staff bar FIRST,
    ///   where SectionLabel declares <c>(left-edge staff-bar)</c> and so keeps the edge.
    ///   SegnoMark repeats CodaMark's list.
    /// </remarks>
    private static Func<int, double> BuildLineStartBarlineX(
        MultiStaffScore score, ImmutableArray<SystemLayout> systems)
        => measureIndex =>
        {
            var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
            if (measureIndex < 0 || measureIndex >= measures.Length
                || measures[measureIndex].StartBarline == BarlineType.None)
                return double.NaN;
            foreach (var sys in systems)
                if (!sys.Measures.IsDefaultOrEmpty
                    && sys.Measures[0].MeasureIndex == measureIndex)
                    return sys.Measures[0].X + Rendering.SharedRenderer.LineStartBarClearance;
            return double.NaN;
        };

    private static Func<int, double> BuildPrefixTimeSignatureX(
        MultiStaffScore score, ImmutableArray<SystemLayout> systems)
        => sysIdx =>
        {
            if (sysIdx < 0 || sysIdx >= systems.Length
                || systems[sysIdx].Measures.IsDefaultOrEmpty)
                return double.NaN;
            var prefix = MultiStaffLayouter.SolveLineStartPrefix(
                score, systems[sysIdx].Measures[0].MeasureIndex, sysIdx == 0);
            return prefix.HasTime
                ? systems[sysIdx].Indent + prefix.Columns.TimeX
                : double.NaN;
        };

    /// <summary>
    /// Per system, the ABSOLUTE X a (staff-bar key-signature clef)-aligned mark's
    /// refpoint lands on at that system's line start: the KEY ink's right edge when a
    /// key signature stands in the prefix, the CLEF ink's right edge otherwise, NaN
    /// when no row engraves a clef (rows-only sheets keep the line-start edge).
    /// The drawn opening bar (staff-bar first in the list) is the caller's
    /// <c>lineStartBarlineX</c>; this covers the invisible-bar fallback chain.
    /// </summary>
    /// <remarks>
    /// One derivation with the spring model and the draw
    /// (<see cref="MultiStaffLayouter.SolveLineStartPrefix"/>), like the metronome
    /// mark's meter anchor above — a hand-rolled copy is how the reserved and drawn
    /// prefixes would drift apart.
    /// LILYPOND-REF: lily/break-alignment-interface.cc:299-353 find_parent / self_align_callback.
    /// LILYPOND-REF: scm/define-grobs.scm:905-907 Clef break-align-anchor-alignment, :1975-1977 KeySignature break-align-anchor-alignment —
    ///   break-align-anchor-alignment RIGHT, so the anchor is the ink's right edge.
    /// ⚠️ LILYSHARP-OWN bridge, declared: LilyPond's mark aligns to the BreakAlignGroup,
    /// whose anchor is calc-average-anchor over the member grobs' own anchors; Lily#'s
    /// prefix model carries ONE group column (the widest clef / the key union), so the
    /// group's right edge stands in for the average — equal whenever the system's
    /// staves engrave the same clef and key, which is every book the pair measures.
    /// </remarks>
    private static Func<int, double> BuildPrefixMarkAnchorX(
        MultiStaffScore score, ImmutableArray<SystemLayout> systems)
        => sysIdx =>
        {
            if (sysIdx < 0 || sysIdx >= systems.Length
                || systems[sysIdx].Measures.IsDefaultOrEmpty)
                return double.NaN;
            double clefWidth = SpacingRules.MaxClefWidth(score);
            if (clefWidth <= 0)
                return double.NaN;
            int m0 = systems[sysIdx].Measures[0].MeasureIndex;
            var prefix = MultiStaffLayouter.SolveLineStartPrefix(score, m0, sysIdx == 0);
            return systems[sysIdx].Indent
                + (prefix.Columns.HasKey
                    ? prefix.Columns.KeyX + SpacingRules.WidestActiveKeyInk(score, m0)
                    : prefix.Columns.ClefX + clefWidth);
        };

    private AnnotationLayouts CalculateAnnotationLayouts(AnnotationLayoutContext ctx)
    {
        var score = ctx.Score;
        var systems = ctx.Systems;
        var dynamics = ctx.Dynamics;
        var articulations = ctx.Articulations;
        var graceNotes = ctx.GraceNotes;
        var lyrics = ctx.Lyrics;
        var musicMarks = ctx.MusicMarks;
        var customTexts = ctx.CustomTexts;
        var voltaBrackets = ctx.VoltaBrackets;
        var tupletBrackets = ctx.TupletBrackets;
        var arpeggios = ctx.Arpeggios;
        var measures = ctx.Measures;
        var figuredBasses = ctx.FiguredBasses;
        var chordNames = ctx.ChordNames;
        var percentRepeats = ctx.PercentRepeats;
        var crossStaffLayouts = ctx.CrossStaffLayouts;
        var trillSpanners = ctx.TrillSpanners;
        var beamGroups = ctx.BeamGroups;
        var beamLayouts = ctx.BeamLayouts;
        var systemSkylines = ctx.SystemSkylines;
        var tupletForceStemUp = ctx.TupletForceStemUp;
        var staffVoices = ctx.StaffVoices;
        var voicesByStaff = ctx.VoicesByStaff;
        var measuresByStaff = ctx.MeasuresByStaff;
        var staffYByIndex = ctx.StaffYByIndex;
        var staffByIndex = ctx.StaffByIndex;

        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();

        // Per-system staff-Y resolver. A staff's within-system offset can differ
        // between systems under hara-kiri (a hidden upper staff shifts the staves
        // below it up), so a per-staff annotation must use the offset for the system
        // its OWN measure falls in, not a single global value. Built from each
        // system's StaffGroups only when staffYByIndex is supplied (the multi-staff
        // final pass); staffYAt stays null for the offset-free prelim/single-staff
        // passes, preserving their exact behavior. Without hara-kiri every system has
        // the same staff Y, so staffYAt collapses to the old staffYByIndex lookup and
        // the result is byte-identical.
        Func<int, int, double>? staffYAt = null;
        Func<int, double>? minStaffYAt = null;
        if (staffYByIndex != null)
        {
            var measureToSystem = new Dictionary<int, int>();
            var staffYBySystem = new List<Dictionary<int, double>>(systems.Length);
            for (int s = 0; s < systems.Length; s++)
            {
                var map = new Dictionary<int, double>();
                if (!systems[s].StaffGroups.IsDefaultOrEmpty)
                    foreach (var sg in systems[s].StaffGroups)
                        foreach (var st in sg.Staves)
                            map[st.StaffIndex] = -st.Y;   // Y-up storage → device-down offset
                staffYBySystem.Add(map);
                foreach (var m in systems[s].Measures)
                    measureToSystem[m.MeasureIndex] = s;
            }
            int SysOf(int measureIndex) =>
                staffYBySystem.Count == 0 ? 0
                : measureToSystem.TryGetValue(measureIndex, out var s) ? s : 0;
            staffYAt = (measureIndex, staffIndex) =>
                staffYBySystem.Count > 0
                && staffYBySystem[SysOf(measureIndex)].TryGetValue(staffIndex, out var y) ? y : 0;
            minStaffYAt = measureIndex =>
            {
                if (staffYBySystem.Count == 0) return 0;
                var map = staffYBySystem[SysOf(measureIndex)];
                return map.Count > 0 ? map.Values.Min() : 0;
            };
        }

        // Scripts are laid out BEFORE the lyric/figured-bass rows: a note-bound
        // script (marcato/staccato/tenuto/…) carries no outside-staff-priority, so it
        // stays in the staff's support skyline that those rows drop below. Without the
        // augmented DOWN skyline a marcato hanging under a low note overprints the
        // syllable beneath it. LILYPOND-REF: lily/axis-group-interface.cc:359-474.
        // The fingerings go in WITH them: a vertical fingering is a script of its
        // note's column (priority 100 + position), so the one walk stacks both — a
        // bow over a fingering over a tenuto, in script-priority order, not islands.
        // LILYPOND-REF: lily/new-fingering-engraver.cc:314-340 position_scripts.
        // The beams go in with them: a BEAMED note's stem is a fingering's support and it ends
        // on the beam, so the island answer has to be built against the quanted beam the
        // renderer draws (ledger fingering.chord.beamed-*).
        var (articulationLayouts, fingeringLayouts) = ComputeFingeringsAndScripts(
            ctx, score, systems, voicesByStaff, beamLayouts ?? default,
            articulations, ml, measuresByStaff, staffYAt, staffByIndex);
        var scriptedSkylines = AugmentSkylinesWithScripts(systemSkylines, articulationLayouts, systems);

        var lyricLayouts = LayoutLyrics(ctx, ml, scriptedSkylines);

        // LILYPOND-REF: axis-group-interface.cc skyline_spacing
        // Outside-staff elements are placed in priority order (lower priority = closer to staff).
        // DynamicLineSpanner (250) must be calculated before TextSpanner (350)
        // so text spanners can be placed below dynamics.

        // Dynamics first (outside-staff-priority: 250)
        var dynamicLayouts = score != null ? DynamicEngraver.Calculate(score, dynamics, ml, staffVoices, voicesByStaff, measuresByStaff, beamLayouts ?? default) : ImmutableArray<DynamicLayout>.Empty;
        // …minus the ones a tab staff blanks. LILYPOND-REF:
        // ly/engraver-init.ly:1280-1285 Tab_staff_symbol_engraver — that context's
        // \override DynamicText.stencil = ##f / \override TextScript.stencil = ##f — one
        // arm here because @text("…") rides the DynamicItem pipeline as expressive text
        // (MeasureCollector.Annotations), which is exactly LilyPond's TextScript.
        // THE LAYOUTS, NOT THE ITEMS: DynamicEngraver keys each layout's SourceIndex off
        // its input's POSITION, so filtering the input would renumber every data-pos.
        // See TabStaffStencils.
        dynamicLayouts = TabStaffStencils.Blank(
            ctx.MultiScore, dynamicLayouts, static d => d.StaffIndex);

        // Detect and layout hairpins from cresc/decresc marks
        var hairpinItems = HairpinEngraver.DetectHairpins(musicMarks, dynamics);
        // A tab staff blanks the wedge too, and the ITEM carries its own SourceIndex, so
        // this one is cut before the layout is built rather than after.
        // LILYPOND-REF: ly/engraver-init.ly:1283 Tab_staff_symbol_engraver — that
        // context's \override Hairpin.stencil = ##f.
        // ⚠️ AFTER DetectHairpins, not before: the detector PAIRS a cresc mark with the
        // dynamic that terminates it, and a tab staff's own dynamics are what terminate
        // its own hairpins. Cutting either list first would leave the other half of a
        // pair looking for a partner on the notation staff.
        hairpinItems = TabStaffStencils.Blank(
            ctx.MultiScore, hairpinItems, static h => h.StaffIndex);
        // Same supports as the dynamics on the same DynamicLineSpanner: the staff's own
        // voices, its measures, and the beams that quant its stems.
        // Dynamic layouts ride along: a bound that carries a dynamic text starts/ends
        // against the TEXT's padded extent (hairpin.cc's Text_interface bound).
        var hairpinLayouts = HairpinEngraver.Calculate(hairpinItems, systems, ml, staffYAt,
            score != null && staffVoices.IsDefaultOrEmpty
                ? ImmutableArray.Create(score.Voice) : staffVoices,
            voicesByStaff, measuresByStaff, beamLayouts ?? default, dynamicLayouts);

        // Texts and wedges linked by RUNNING hairpins ride one DynamicLineSpanner: the
        // group is side-positioned once and every member re-seats on the shared line
        // (the fff of "a1\fff\>" drops to the level the terminating pp's low column
        // demands). Must run before the text spanners read the dynamics' Y. The groups
        // ride to the outside-staff pass, which moves each as ONE grob.
        // LILYPOND-REF: lily/dynamic-align-engraver.cc:194-235 stop_translation_timestep.
        ImmutableArray<DynamicAlignEngraver.AlignedLineGroup> dynamicLineGroups;
        (dynamicLayouts, hairpinLayouts, dynamicLineGroups) = DynamicAlignEngraver.AlignLines(
            ctx.Fonts, hairpinItems, dynamics, dynamicLayouts, hairpinLayouts, systems, ml, staffYAt,
            score != null && staffVoices.IsDefaultOrEmpty
                ? ImmutableArray.Create(score.Voice) : staffVoices,
            voicesByStaff, measuresByStaff, beamLayouts ?? default);

        // Detect and layout text spanners from rit/accel marks (outside-staff-priority: 350)
        // Pass dynamic layouts so text spanners can stack below them
        // …minus the ones a tab staff blanks — the ink half of the same cut
        // ScoreSideTables.TextSpannersByStaff makes for the reservation.
        // LILYPOND-REF: ly/engraver-init.ly:1282 Tab_staff_symbol_engraver — that
        // context's \override TextSpanner.stencil = ##f.
        var textSpannerItems = TabStaffStencils.Blank(
            ctx.MultiScore, TextSpannerEngraver.DetectTextSpanners(musicMarks),
            static t => t.StaffIndex);
        var textSpannerLayouts = TextSpannerEngraver.Calculate(textSpannerItems, systems, ml, dynamicLayouts, staffYAt);

        // Detect and layout ottava brackets from ottava/loco marks
        var ottavaItems = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);
        // The staff's voices and the drawn beams ride along: the bracket's quiet height is
        // aligned_side over its OWN staff's note columns (Ottava_spanner_engraver is a
        // Staff-context engraver, so every voice counts), and a beamed support column's
        // stem ends at the quanted beam face — the same ingredients the trill reads.
        var ottavaLayouts = OttavaBracketEngraver.Calculate(
            ctx.Fonts, ottavaItems, systems, ml, staffYAt,
            voicesByStaff, beamLayouts ?? ImmutableArray<BeamLayout>.Empty);

        // Layout arpeggio markings
        var arpeggioLayouts = ArpeggioEngraver.Calculate(arpeggios, systems, measures, measuresByStaff);

        // Piano pedal marks render per the part's `pedal` style (Staff.PedalStyle):
        //   bracket (Lily# default) / mixed  -> a spanning bracket, and the "Ped." /
        //                                        "*" text is suppressed (mixed keeps
        //                                        the leading "Ped." only);
        //   text                             -> keep "Ped." / "*", no bracket.
        // LILYPOND-REF: lily/piano-pedal-engraver.cc — pedalSustainStyle.
        PedalStyle StaffPedalStyle(int staffIndex) =>
            staffByIndex != null && staffByIndex.TryGetValue(staffIndex, out var st)
                ? st.PedalStyle : PedalStyle.Text; // no staff info -> plain text
        var pedalBracketBuilder = ImmutableArray.CreateBuilder<PedalBracketLayout>();
        if (!musicMarks.IsDefaultOrEmpty && staffByIndex != null)
        {
            // measure -> system INDEX, to find the solved line of the system a bracket
            // starts on (the profile that reserved it).
            Dictionary<int, int>? measureToSysIdx = null;
            if (ctx.PedalLines != null)
            {
                measureToSysIdx = new Dictionary<int, int>();
                for (int si = 0; si < systems.Length; si++)
                    foreach (var m in systems[si].Measures)
                        measureToSysIdx[m.MeasureIndex] = si;
            }
            foreach (var staffIndex in musicMarks
                .Where(m => IsPedalMark(m.Type)).Select(m => m.StaffIndex).Distinct())
            {
                var style = StaffPedalStyle(staffIndex);
                if (style == PedalStyle.Text)
                    continue;
                var staffMarks = musicMarks.Where(m => m.StaffIndex == staffIndex).ToImmutableArray();
                var brackets = PedalEngraver.DetectPedalBrackets(staffMarks);
                // The line the staff's down profile was solved with -- null (fallback to
                // the legacy below-the-system baseline) when the seed declined this staff
                // or this pass has no per-staff skylines (the preliminary pass).
                int sIdx = staffIndex;
                Func<int, PedalType, double?>? solvedLineUpOf = null;
                if (ctx.PedalLines is { } pl && measureToSysIdx != null)
                    solvedLineUpOf = (startMeasure, type) =>
                    {
                        if (!measureToSysIdx.TryGetValue(startMeasure, out int si)
                            || si >= pl.Count || sIdx >= pl[si].Count)
                            return null;
                        foreach (var line in pl[si][sIdx])
                            if (line.StartMeasureIndex == startMeasure && line.Type == type)
                                return line.LineYUp;
                        return null;
                    };
                double? staffTopDown =
                    staffYByIndex != null && staffYByIndex.TryGetValue(staffIndex, out var td)
                        ? td : null;
                pedalBracketBuilder.AddRange(
                    PedalEngraver.Calculate(brackets, systems, ml,
                        isMixed: style == PedalStyle.Mixed, solvedLineUpOf, staffTopDown));
            }
        }
        var pedalBracketLayouts = pedalBracketBuilder.ToImmutable();
        // A bracket/mixed style suppresses the "Ped." / "*" text a mark would draw.
        // The predicate is applied when the mark LAYOUT is built (below), so the raw
        // mark list — and every mark's SourceIndex into it — stays intact for the
        // incremental-reuse data-pos path (SharedRenderer.ResolveDataPos).
        Func<MusicMarkItem, bool> keepMarkText = m => KeepPedalTextMark(m, StaffPedalStyle(m.StaffIndex));

        // Layout figured bass (drops below below-staff scripts via the
        // script-augmented DOWN skylines)
        var fbItems = figuredBasses ?? ImmutableArray<FiguredBassItem>.Empty;

        // ...and it drops below ITS OWN STAFF's profile, not the system silhouette. Built
        // lazily per (system, staff) — the shape LayoutChordNames uses for a chord row under
        // a non-top staff — so a score with no figures does no extra work.
        // ⚠️ WHAT IS IN THE PROFILE IS DECIDED BY PRIORITY. BassFigureAlignmentPositioning
        // declares outside-staff-priority 25 (scm/define-grobs.scm:387-411
        // side-position-interface, outside-staff-priority, add-stem-support), so it is placed
        // BEFORE the dynamics at 250 and clears the INSIDE-staff ink only: the staff, the
        // clef, the notes, the beams and the note-bound scripts (which declare no priority of
        // their own and so stay in the support skyline). Dynamics are deliberately NOT passed
        // — at 250 it is the dynamic that clears the figures.
        // LILYPOND-REF: lily/axis-group-interface.cc:914-950 skyline_spacing — the inside
        //   skylines first, then add_grobs_of_one_priority in ascending priority order.
        Func<int, int, VerticalSkyline?>? figuredBassStaffDown = null;
        if (!fbItems.IsDefaultOrEmpty && staffByIndex != null)
        {
            var fbSkyCache = new Dictionary<(int, int), VerticalSkyline?>();
            figuredBassStaffDown = (sysIdx, staffIndex) =>
            {
                if (sysIdx < 0 || sysIdx >= systems.Length
                    || !staffByIndex.TryGetValue(staffIndex, out var staff))
                    return null;
                var key = (sysIdx, staffIndex);
                if (!fbSkyCache.TryGetValue(key, out var sky))
                {
                    // The scripts enter with NO step: an ArticulationLayout's YUp is already
                    // about its own staff's middle, which is the frame BuildStaffSkylines
                    // works in. (AugmentSkylinesWithScripts takes the step because ITS target
                    // is the system frame.)
                    var staffScripts = articulationLayouts.IsDefaultOrEmpty
                        ? ImmutableArray<ArticulationLayout>.Empty
                        : articulationLayouts.Where(a => a.StaffIndex == staffIndex)
                                             .ToImmutableArray();
                    // ...and the SPANNERS, for the very reason the priority argument above
                    // gives: Slur, Tie and TupletBracket declare no outside-staff-priority at
                    // all (addresses at their seeding sites in SkylineBuilder), so they are
                    // not grobs the figures are placed before — they are part of the
                    // inside-staff ink the figures drop below. The room's own tables, not a
                    // second layout: see AnnotationLayoutContext.StaffSpanners.
                    var fbSpanners = ctx.SpannersOf(sysIdx, staffIndex);
                    // ★ THE room's inside-staff skyline, not a rebuild: this list is exactly
                    // what the priority argument above describes, and the room already built
                    // it once for this (system, staff).
                    var down = (ctx.InsideOf(sysIdx, staffIndex)
                        ?? _skylineBuilder.BuildInsideStaffSkylines(
                            staff, systems[sysIdx].Measures,
                            articulationLayouts: staffScripts,
                            tupletBrackets: fbSpanners.TupletBrackets,
                            slurs: fbSpanners.Slurs,
                            ties: fbSpanners.Ties,
                            beams: beamLayouts ?? ImmutableArray<BeamLayout>.Empty,
                            systemLeft: systems[sysIdx].Indent,
                            // ...and a rest another voice pushed DOWN out of the staff is ink
                            // the figures drop below — see AnnotationLayoutContext.RestCollisionsOf.
                            restShifts: ctx.RestCollisionsOf(staff))).Down;
                    // ⚠️ REFLECTED ONCE, HERE AT THE EDGE, into the system Y-up frame the drop
                    // works in — and by the SAME expression AugmentSkylinesWithScripts uses for
                    // "this staff's middle in the system's frame", so the two cannot drift.
                    down.Raise(LayoutUtilities.StaffOffsetInSystemUp(systems[sysIdx], staffIndex)
                               - _options.StaffHeight / 2.0);
                    sky = down;
                    fbSkyCache[key] = sky;
                }
                return sky;
            };
        }

        var figuredBassLayouts = FiguredBassEngraver.Calculate(
            fbItems, systems, ml, measures,
            measuresByStaff, scriptedSkylines, figuredBassStaffDown);

        var chordNameLayouts = LayoutChordNames(
            ctx, ml, scriptedSkylines, staffYAt, minStaffYAt);

        // Layout percent repeats
        var percentRepeatLayouts = PercentRepeatEngraver.Calculate(
            percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty, systems, ml);

        // Layout trill spanners (tr + wavy line). The drawn beams ride along so a
        // beamed support column's stem ends at the quanted face (ledger
        // trill.beam-face.staff-to-line), the same beam model every other consumer reads.
        // LILYPOND-REF: lily/grob.cc:81-89 simple_vertical_skylines_from_extents — a
        //   support's skyline defaults to its EXTENT, and a Stem's extent is the drawn one.
        // LILYPOND-REF: scm/scheme-engravers.scm — trill spanner positioning
        var trillSpannerLayouts = TrillSpannerEngraver.Calculate(
            trillSpanners ?? ImmutableArray<TrillSpannerItem>.Empty, systems, ml, staffYAt,
            voicesByStaff, beamLayouts ?? ImmutableArray<BeamLayout>.Empty);

        // Calculate volta brackets first — needed by MusicMarkEngraver for collision avoidance
        // LILYPOND-REF: axis-group-interface.cc — elements sorted by outside-staff-priority
        var voltaBracketLayouts = VoltaBracketEngraver.Calculate(voltaBrackets, systems, ml);

        // LILYPOND-REF: lily/axis-group-interface.cc:860-985 Axis_group_interface::skyline_spacing
        // Post-process below-staff elements using priority-based stacking.
        // This ensures hairpins avoid dynamics (both priority 250) and
        // text spanners avoid both dynamics and hairpins (priority 350).
        // BOTH passes run over each staff's REAL profile — the same ingredients the
        // inter-staff seed accumulated (staff symbol, clef, notes with real thin stems,
        // beams) — so the draw lands where the seed reserved.
        // LILYPOND-REF: lily/axis-group-interface.cc:937-950 skyline_spacing.
        // ⚠️ BUILT ONCE PER (system, staff), HANDED OUT AS COPIES. The trackers RAISE and then
        // MERGE INTO the skyline they are given, so a shared instance would accumulate one
        // pass's movers into the other's support. The copy is the cheap half — a resolved
        // skyline's buildings are already sorted and non-overlapping and a wrap preserves
        // that (VerticalSkyline.FromResolvedBuildings, the same shape TextOutlineSkylines and
        // SeedClef use for their caches) — where the BUILD walks the whole system's music.
        // COUNTED (HANDOFF 5.3, calls not milliseconds) — AND THE GAIN IS SMALLER THAN THE
        // OBVIOUS GUESS, so here is what was actually measured (builds / saved):
        // multi-staff-hairpins 4/2, test/notes 4/0, showcase/08-chorale 2/0,
        // showcase/04-advanced 4/0. It saves a build only where the ABOVE and BELOW passes
        // want the SAME (system, staff) in one run; on the three that saved nothing the below
        // pass asks for nothing at all (no below-staff mover), so there was never a duplicate.
        // ⚠️ THE BIGGER DUPLICATE IS GONE (2026-08-14): the annotation pass runs TWICE (once
        // for the extents, once final) and this cache lives inside one run — but BOTH runs
        // now read the room's one build through ctx.InsideOf (the preliminary pass carries
        // placed.StaffInside since the same date), so what this cache fronts is a lookup and
        // a wrap, not a walk. The check the hoist demanded — "do the two runs hold the same
        // measure layouts?" — is reference identity by construction: the preliminary pass
        // iterates placed.Systems itself, and the final pass's paging keeps every system's
        // Measures instance (CreatePages re-Ys the system and at most respaces its staves —
        // `system with { Y = …, StaffGroups = RespaceStaves(…) }` — and this profile is in
        // the STAFF-LOCAL frame, which a staff moving within its system does not change;
        // the offset into the system is applied by the consumers through staffYAt, built
        // from the CURRENT systems). The BuildInsideStaffSkylines arm below is the
        // out-of-range guard, not a second profile: InsideAt answers null only for an
        // index outside what was placed.
        // ⚠️ HOW IT SCALES, counted on the longest fixtures: grammar-tour 12 builds,
        // feature-tour 18, multi-page-vertical 66. A BAR NUMBER sits on every system, so a
        // (system, staff) that places something is nearly every system — and each build walks
        // that ONE system's measures. So the port costs about TWO extra walks over the score's
        // music per render (once per annotation pass), not one per system per system. On this
        // machine that is not measurable in milliseconds: the same binary rendered
        // test/fermata-down at min-of-20 = 4.98 ms in one run and 14.70 ms in another, so a
        // 10-20% difference at 5-15 ms is below the noise floor here (HANDOFF 5.3 — count the
        // calls, do not time them).
        // ⚠️ A BEAM IS SELECTED BY BOTH OF ITS PARENTS, staff and system, and for one session
        // it was selected by the staff alone. `beamLayouts` is the WHOLE SCORE's beams; each
        // carries the X its own system laid it out at, and those ranges OVERLAP (every system
        // starts near x 0), so system 0's profile was reserving system 1's beam ink. MEASURED
        // on test/notes, whose first system has no beamed note at all: the staff-alone profile
        // read 0.666644 at x 10 and 0.516527 at x 30 — system 1's beam edges to the digit —
        // where both this system's own profile and its silhouette read the staff line 0.050
        // across the whole line. Those two phantom numbers are what a1d22431's message recorded
        // as "the first system's silhouette carries no music ink"; the silhouette was right.
        // ⚠️ AND THE PREDICATE READS THE ATTRIBUTION, NOT A RECONSTRUCTION OF IT. The first
        // repair (50533a8d) recovered the system from the group's measure index and built a
        // set per call — correct, but a SECOND way of answering a question the producer
        // already knew the answer to, which is HANDOFF 5.2.1 (2). BeamLayout carries both
        // parents now (LilyPond's shape: a Beam grob is created inside one System's axis
        // group), so this is one comparison against carried fields and there is nothing left
        // to keep in step.
        // ⚠️ IT IS ALSO WHERE THE COST WAS, and the shape is worth knowing: the leak made every
        // profile seed EVERY system's beams, so the seeding grew with the system count.
        // COUNTED (beam seeds summed over one render's profile builds, staff-and-system vs the
        // staff-alone spelling): test/notes 18 vs 36, showcase/grammar-tour 20 vs 120,
        // test/feature-tour 16 vs 144 — the ratio IS the number of systems (2x / 6x / 9x), the
        // signature of a per-system walk doing the whole score. Scores whose profile builds see
        // no beams at all (showcase/04-advanced, 08-chorale) are unaffected either way.
        // ⇒ The fix removes work that scaled with score length, which is the preview's axis.
        // ⚠️ NO MILLISECOND FIGURE IS CLAIMED (HANDOFF 5.3 — this machine timed one binary at
        // 4.98 ms and 14.70 ms on the same fixture).
        // ⚠️ LILYSHARP-OWN: SELECTING A GROB'S SIBLINGS OUT OF A SCORE-WIDE ARRAY. LilyPond
        // has no line to cite here because it never performs this step: a Beam is created in
        // one System's VerticalAxisGroup and Axis_group_interface::skyline_spacing
        // (axis-group-interface.cc:914-950 building inside_staff_skylines) walks the elements
        // of the group it was called on. Lily# keeps one flat per-score array and recovers the
        // grouping with the predicate below, which is why the grouping can be got WRONG at all
        // — it was, for one session, and the two carried parents are the cheapest true fix
        // short of the structure.
        // ⇒ IT DISAPPEARS the day the per-(system, staff) beams are held that way at
        // production time (LayoutAllSpanners already loops over exactly those pairs), leaving
        // a lookup rather than a scan. That is the same shape as the remaining beam-GEOMETRY
        // duplication on the handoff's list, and they should go together.
        Func<int, int, (VerticalSkyline Up, VerticalSkyline Down)?>? staffProfile = null;
        if (staffByIndex != null)
        {
            var allBeams = beamLayouts ?? ImmutableArray<BeamLayout>.Empty;
            var profileCache = new Dictionary<(int Sys, int Staff),
                (VerticalSkyline Up, VerticalSkyline Down)?>();
            staffProfile = (sysIdx, staffIndex) =>
            {
                if (!profileCache.TryGetValue((sysIdx, staffIndex), out var built))
                {
                    // The room's own slurs, ties and tuplet brackets for this (system, staff)
                    // — inside-staff ink in LilyPond, so this seed has to hold them exactly
                    // as the alignment's silhouette does. See
                    // AnnotationLayoutContext.StaffSpanners for why they are carried here
                    // rather than laid out a second time.
                    var spanners = ctx.SpannersOf(sysIdx, staffIndex);
                    // ★ THE room's inside-staff skyline — the same object every other
                    // consumer of this staff's silhouette reads. The scripts are in it
                    // because the room seeds them, which is what this seed could not reach
                    // while it built a subset of its own.
                    built = sysIdx >= 0 && sysIdx < systems.Length
                            && staffByIndex.TryGetValue(staffIndex, out var profStaff)
                        ? ctx.InsideOf(sysIdx, staffIndex)
                            ?? _skylineBuilder.BuildInsideStaffSkylines(
                                profStaff, systems[sysIdx].Measures,
                                tupletBrackets: spanners.TupletBrackets,
                                slurs: spanners.Slurs,
                                ties: spanners.Ties,
                                beams: allBeams.IsDefaultOrEmpty
                                    ? ImmutableArray<BeamLayout>.Empty
                                    : allBeams.Where(b => b.StaffIndex == staffIndex
                                            && b.SystemIndex == sysIdx)
                                        .ToImmutableArray(),
                                systemLeft: systems[sysIdx].Indent,
                                // A rest another voice pushed out of the staff is inside-staff
                                // ink where it was pushed to — see ctx.RestCollisionsOf.
                                restShifts: ctx.RestCollisionsOf(profStaff))
                        : null;
                    profileCache[(sysIdx, staffIndex)] = built;
                }
                if (built is not { } p)
                    return null;
                return (VerticalSkyline.FromResolvedBuildings(VerticalDirection.Up, p.Up.Buildings),
                        VerticalSkyline.FromResolvedBuildings(VerticalDirection.Down, p.Down.Buildings));
            };
        }
        // The passes' keystroke-crossing memos: reference identity of each staff's
        // profile is the STORED table pair, not the copy staffProfile wraps per call
        // (ctx.InsideIdentityOf). staffProfile's OWN fallback arm (the InsideOf-null
        // rebuild) has no stable identity, and InsideIdentityOf answers null exactly
        // there, which makes both memos stack that system live — never a false match.
        Func<int, int, (object Up, object Down)?>? profileIdentity =
            staffProfile == null ? null : ctx.InsideIdentityOf;

        // The scripts come BACK out: the fermata family declares outside-staff-priority 75
        // (scm/script.scm), so it is a MOVER of this pass — below here, above in
        // StackAboveStaff, which is handed the below pass's result so one array carries
        // both halves' moves.
        var (stackedDynamics, stackedHairpins, stackedArticulations, belowStackedTrills) =
            OutsideStaffStacker.StackBelowStaff(ctx.Fonts, systems, dynamicLayouts, hairpinLayouts,
                articulationLayouts, applyStaffOffsets: staffYAt != null,
                staffProfile: staffProfile, lineGroups: dynamicLineGroups,
                trills: trillSpannerLayouts,
                memo: ctx.BelowStackMemo, profileIdentity: profileIdentity);

        // ABOVE-staff: one unified priority pass (trill 50, bar number 100,
        // tuplet brackets 200 as immovable seeds, ottava 400, text 450,
        // volta 600, marks 1500), seeded per (system, STAFF) from that staff's own profile —
        // LilyPond runs the pass on one staff's VerticalAxisGroup at a time.
        // Replaces the old pairwise hacks (bar-number-vs-volta in the
        // renderer; music-mark-vs-volta inside MusicMarkEngraver).
        // The PRE-STACK articulations are the right frame for avoid-scripts: the
        // scripts the bracket must clear are exactly the priority-less ones
        // (LP :690-692 skips the movers), and those are seeds the outside-staff
        // passes never move — their YUp is final here.
        // Only the SCRIPT family may join: LilyPond's tuplet engraver
        // acknowledges Script (dynamics excluded), Fingering and StringNumber
        // grobs and nothing else — the breath/caesura/bend marks riding this
        // same layout stream are not Scripts in LP (no acknowledger), so they
        // must not add points. The multi-staff path is pre-filtered by the same
        // sieve (StaffArticulationLayouts → IsSidePositionedScript).
        // LILYPOND-REF: lily/tuplet-engraver.cc:199-233 acknowledge_script.
        var tupletScripts = articulationLayouts;
        if (!tupletBrackets.IsDefaultOrEmpty && !articulationLayouts.IsDefaultOrEmpty)
        {
            var sb = ImmutableArray.CreateBuilder<ArticulationLayout>(articulationLayouts.Length);
            foreach (var a in articulationLayouts)
                if (a.SourceIndex >= 0 && a.SourceIndex < articulations.Length
                    && ArticulationEngraver.IsSidePositionedScript(articulations[a.SourceIndex].Type))
                    sb.Add(a);
            tupletScripts = sb.ToImmutable();
        }
        var tupletBracketLayouts = TupletBracketEngraver.Calculate(
            tupletBrackets, ml, measures, beamGroups ?? default, beamLayouts ?? default,
            forceStemUp: tupletForceStemUp,
            measuresByStaff: measuresByStaff, voicesByStaff: voicesByStaff, staffYAt: staffYAt,
            staffByIndex: staffByIndex, scripts: tupletScripts);
        // TEXT-style pedal words were solved where the room was built (the same
        // skyline-time solve the brackets take); hand the draw those baselines, keyed
        // (staff, system, the mark's source position), Y-up about the mark's OWN staff
        // middle.
        Func<int, int, int, double?>? solvedPedalRowUp = null;
        if (ctx.PedalRows is { } pedalRows)
            solvedPedalRowUp = (staffIdx, sysIdx, sourcePosition) =>
            {
                if (sysIdx < 0 || sysIdx >= pedalRows.Count
                    || staffIdx < 0 || staffIdx >= pedalRows[sysIdx].Count)
                    return null;
                foreach (var row in pedalRows[sysIdx][staffIdx])
                    if (row.SourcePosition == sourcePosition)
                        return row.BaselineYUp;
                return null;
            };
        var musicMarkLayouts = MusicMarkEngraver.Calculate(
            ctx.Fonts, score, musicMarks, systems, ml, measures, default,
            chordNames: chordNameLayouts, lyrics: lyricLayouts, keepMarkText: keepMarkText,
            prefixTimeSignatureX: ctx.PrefixTimeSignatureX,
            lineStartBarlineX: ctx.LineStartBarlineX,
            prefixMarkAnchorX: ctx.PrefixMarkAnchorX,
            solvedPedalRowUp: solvedPedalRowUp);
        var customTextLayouts = CustomTextEngraver.Calculate(customTexts, ml);
        // A leading \partial pickup is bar 0: shift displayed numbers down by one
        // so the first FULL measure is numbered 1, not 2.
        int barNumberOffset = (!measures.IsDefaultOrEmpty && measures[0].IsPickup) ? -1 : 0;
        // A staffless system hangs its number on the row that draws the barlines — the only
        // row whose ink reaches the number's column. One home for that choice, shared with
        // the renderer that draws them (MultiStaffScore.GridBarlineRowIndex).
        var barNumberLayouts = BarNumberEngraver.Calculate(ctx.Fonts, systems,
            numberOffset: barNumberOffset,
            gridBarlineRowIndex: ctx.GridBarlineRowIndex);
        // Forced-above dynamics (@f.up) join the above-staff pass so they clear, and are
        // cleared by, the other above-staff grobs. Below dynamics were already placed by
        // StackBelowStaff and pass through untouched.
        var (stackedTrills, stackedBarNumbers, stackedOttavas, stackedCustomTexts,
             stackedVoltas, stackedMarks, stackedDynamicsAbove, stackedTextSpanners,
             stackedArticulationsAbove) = OutsideStaffStacker.StackAboveStaff(
            ctx.Fonts,
            systems, systemSkylines, tupletBracketLayouts,
            belowStackedTrills, barNumberLayouts, ottavaLayouts,
            customTextLayouts, voltaBracketLayouts, musicMarkLayouts,
            stackedArticulations, aboveDynamics: stackedDynamics, textSpanners: textSpannerLayouts,
            // The chord symbols go in as SUPPORT, not as movers: a ChordName declares no
            // outside-staff-priority, so LilyPond collects it into the inside-staff skylines
            // every outside-staff grob is placed against — the seeding site's remarks carry
            // the addresses (OutsideStaffStacker.SeedAboveTrackers).
            // They are already placed by this point — LayoutChordNames ran above — which is
            // what lets them be handed over as occupancy. The ITEMS travel with them because
            // a ChordNameLayout carries no StaffIndex and the seed is keyed per staff.
            chordNames: chordNameLayouts,
            chordItems: ctx.ChordNames ?? ImmutableArray<ChordNameItem>.Empty,
            staffProfile: staffProfile,
            memo: ctx.AboveStackMemo, profileIdentity: profileIdentity);
        stackedDynamics = stackedDynamicsAbove;
        stackedArticulations = stackedArticulationsAbove;
        // (No To-Coda/label co-placement here any more: the pass above owns it. A
        // boundary "To Coda" is paired with the section label it shares a barline
        // with INSIDE PlaceMusicMarks — moved beside it before either is priced,
        // then placed as ONE union extent so whatever stands under either drawn
        // column raises the pair together. See MusicMarkEngraver's remarks.)
        // (No tempo/label co-placement any more: the metronome mark and the section
        // label each break-align to their own anchor and the priority pass above
        // already stacked them pointwise, LilyPond's shape. The chart-pair device
        // died with the tempo port — see MusicMarkEngraver's note.)

        // The fingerings were placed in the script-column walk itself (with the
        // articulations, above) — nothing to re-clamp after the movers' pass: a
        // fingering does not dodge a fermata; the fermata, a MOVER, goes above it.

        return new AnnotationLayouts(
            Dynamics: stackedDynamics,
            Articulations: stackedArticulations,
            GraceNotes: score != null ? GraceNoteEngraver.Calculate(score, graceNotes, ml, measuresByStaff, staffYByIndex, staffByIndex, articulations) : ImmutableArray<GraceNoteLayout>.Empty,
            Lyrics: lyricLayouts,
            LyricHyphens: new LyricHyphenEngraver().CalculateLayouts(
                lyricLayouts, systems, measuresByStaff),
            MusicMarks: stackedMarks,
            CustomTexts: stackedCustomTexts,
            VoltaBrackets: stackedVoltas,
            TupletBrackets: tupletBracketLayouts,
            Hairpins: stackedHairpins,
            TextSpanners: stackedTextSpanners,
            OttavaBrackets: stackedOttavas,
            Arpeggios: arpeggioLayouts,
            PedalBrackets: pedalBracketLayouts,
            FiguredBasses: figuredBassLayouts,
            ChordNames: chordNameLayouts,
            PercentRepeats: percentRepeatLayouts,
            CrossStaffs: crossStaffLayouts ?? ImmutableArray<CrossStaffLayout>.Empty,
            TrillSpanners: stackedTrills,
            // LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob.
            Fingerings: fingeringLayouts,
            // LILYPOND-REF: lily/laissez-vibrer-engraver.cc + repeat-tie-engraver.cc — half-ties.
            TieVariants: score != null
                ? TieVariantEngraver.Calculate(score, systems)
                : ImmutableArray<TieVariantLayout>.Empty,
            // LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob.
            MultiMeasureRests: score != null
                ? MultiMeasureRestEngraver.Calculate(score, systems, _options.StaffHeight,
                    allStaffMeasures: measuresByStaff != null && measuresByStaff.Count > 1
                        ? measuresByStaff.Values.ToArray() : null)
                : ImmutableArray<MultiMeasureRestLayout>.Empty,
            // LILYPOND-REF: lily/ledger-line-spanner.cc — LedgerLineSpanner grob.
            LedgerLineSpans: score != null
                ? LedgerLineSpannerEngraver.Calculate(score, systems, _options.StaffHeight)
                : ImmutableArray<LedgerLineSpan>.Empty,
            // LILYPOND-REF: lily/bar-number-engraver.cc — BarNumber grob.
            BarNumbers: stackedBarNumbers,
            // LILYPOND-REF: lily/stanza-number-engraver.cc — StanzaNumber grob.
            StanzaNumbers: StanzaNumberEngraver.Calculate(lyricLayouts, systems,
                leadSheet: ctx.IsLeadSheet));
    }

    /// <summary>
    /// The chord symbols, and the up-skyline a row below the top staff has to clear.
    /// </summary>
    /// <remarks>
    /// Extracted verbatim from <see cref="CalculateAnnotationLayouts"/>. Skyline-spaced
    /// above high notes when skylines are available; a chords-ONLY sheet (chord rows, no
    /// lyric rows) is a measure grid instead, and its symbols centre between the
    /// full-height grid barlines.
    /// </remarks>
    private ImmutableArray<ChordNameLayout> LayoutChordNames(
        AnnotationLayoutContext ctx, ImmutableArray<MeasureLayout> ml,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? scriptedSkylines,
        Func<int, int, double>? staffYAt, Func<int, double>? minStaffYAt)
    {
        var systems = ctx.Systems;
        var staffByIndex = ctx.StaffByIndex;
        var staffYByIndex = ctx.StaffYByIndex;

        bool chordGridSheet = ChordNameEngraver.IsChordGridSheet(
            ctx.ChordNames ?? ImmutableArray<ChordNameItem>.Empty, ctx.Lyrics);

        // Chord names on a NON-top staff (`staff bass with chords ...`) must clear
        // that staff's own high/ledger notes, but the system up-skyline carries only
        // the topmost staff. Provide a per-(system, staff) up-skyline for the staves
        // that actually host a chord row below the top; built lazily and only when
        // such a row exists, so the common lead sheet (chords on the top staff only)
        // does no extra work and is byte-identical.
        Func<int, int, VerticalSkyline?>? lowerStaffUpSkyline = null;
        var cn = ctx.ChordNames ?? ImmutableArray<ChordNameItem>.Empty;
        if (!cn.IsDefaultOrEmpty && staffByIndex != null && staffYByIndex != null
            && staffYByIndex.Count > 0)
        {
            double topStaffY = staffYByIndex.Values.Min();
            bool anyLowerStaffChords = cn.Any(c => !c.IsChordRow
                && staffYByIndex.TryGetValue(c.StaffIndex, out var sy) && sy > topStaffY + 1e-6);
            if (anyLowerStaffChords)
            {
                var skyCache = new Dictionary<(int, int), VerticalSkyline?>();
                lowerStaffUpSkyline = (sysIdx, staffIndex) =>
                {
                    if (sysIdx < 0 || sysIdx >= systems.Length
                        || !staffByIndex.TryGetValue(staffIndex, out var staff))
                        return null;
                    var key = (sysIdx, staffIndex);
                    if (!skyCache.TryGetValue(key, out var sky))
                    {
                        // A bow or a bracket arching ABOVE this staff is ink the row has to
                        // clear exactly as it clears a high note — inside-staff ink, no
                        // outside-staff-priority (addresses in SkylineBuilder's seeding sites).
                        // ⚠️ THE SPANNERS AND NOT THE ROOM'S FINISHED UP-SKYLINE: that one has
                        // ReserveChordRowBand merged into it (MultiStaffLayouter), so reading
                        // it would make this row clear the band it reserved for ITSELF. The
                        // side tables carry no such reservation, which is why they can be
                        // shared where the skyline cannot.
                        var rowSpanners = ctx.SpannersOf(sysIdx, staffIndex);
                        // ★ THE room's inside-staff skyline. It carries no chord-row band —
                        // that is merged into the room's FINISHED up-skyline, which is why
                        // this consumer cannot read that one — and it carries the scripts and
                        // beams this site used to be missing.
                        var up = (ctx.InsideOf(sysIdx, staffIndex)
                            ?? _skylineBuilder.BuildInsideStaffSkylines(
                                staff, systems[sysIdx].Measures,
                                tupletBrackets: rowSpanners.TupletBrackets,
                                slurs: rowSpanners.Slurs,
                                ties: rowSpanners.Ties,
                                systemLeft: systems[sysIdx].Indent,
                                // ...including a rest another voice pushed UP out of the staff,
                                // which is exactly the ink a row above this staff has to clear.
                                restShifts: ctx.RestCollisionsOf(staff))).Up;
                        // ⚠️ REFLECTED ONCE, HERE AT THE EDGE. BuildStaffSkylines works about
                        // the staff's REFERENCE POINT, which is LilyPond's frame;
                        // ChordNameEngraver works in "above the staff's TOP line" throughout,
                        // because that is where a chord row's padding is defined and because
                        // its OTHER input — the SYSTEM up-skyline, for a row on the top staff
                        // — is measured from the system origin, which IS that staff's top
                        // line. Lowering by the half-staff makes the engraver's two inputs one
                        // frame, so it needs no branch and no knowledge of either. Reflecting
                        // inside the engraver would put a half-staff on one path and not the
                        // other, which is the shape this migration exists to remove.
                        up.Raise(-_options.StaffHeight / 2.0);
                        sky = up;
                        skyCache[key] = sky;
                    }
                    return sky;
                };
            }
        }

        // The boxed section labels that will share this row's LINE — on a staffless sheet the
        // label is set ON the chord line (MusicMarkEngraver.StafflessAnchorRefpointBelowTop),
        // so the symbols have to keep out of its frame. Asked BEFORE the marks are laid out
        // because the chord layouts are an input to that pass; a mark's X depends only on its
        // break-align column, so the two readings agree by construction (see
        // BoxedLabelXWindows). Empty — and free — on every book that has a staff.
        var labelWindows = MusicMarkEngraver.BoxedLabelXWindows(
            ctx.Fonts, ctx.MusicMarks, ctx.Measures, cn, systems, ml,
            prefixTimeSignatureX: ctx.PrefixTimeSignatureX,
            lineStartBarlineX: ctx.LineStartBarlineX,
            prefixMarkAnchorX: ctx.PrefixMarkAnchorX);

        // An attached chord line that is a RUN ELEMENT is drawn at the run's own answer —
        // the walk's closing step over the pair that brackets it — instead of the
        // 0.6+protrusion offset (which stays the placement for the top staff and for
        // @chord-only staves). The walk is the SAME one that reserved the pair's room
        // (MultiStaffLayouter.AttachedChordBaselineAboveTop), fed the same skylines
        // (ctx.StaffSkylines are the room's lists) and the same run parts.
        // Null in the preliminary pass, which runs before the per-staff skylines exist;
        // that pass estimates with the 0.6+protrusion arm, exactly as it did for the band.
        Func<int, int, double?>? attachedBaselineAboveTop = null;
        if (ctx.MultiScore is { } multiScore
            && ctx.StaffSkylines is { } roomSkylines
            && staffByIndex != null
            && !cn.IsDefaultOrEmpty && cn.Any(c => !c.IsChordRow && c.UseTiming))
        {
            var baseCache = new Dictionary<(int, int), double?>();
            var sourceCache = new Dictionary<int, MultiStaffLayouter.PairRunSources>();
            attachedBaselineAboveTop = (sysIdx, staffIndex) =>
            {
                var key = (sysIdx, staffIndex);
                if (baseCache.TryGetValue(key, out var hit))
                    return hit;
                double? result = null;
                if (sysIdx >= 0 && sysIdx < systems.Length && sysIdx < roomSkylines.Count)
                {
                    var system = systems[sysIdx];
                    if (!sourceCache.TryGetValue(sysIdx, out var runSources))
                    {
                        // The per-system pass carried its own suppliers out (finding
                        // 4-4) — same score, same measure layouts, same range, so the
                        // rebuild below is the identical value paid a second time.
                        // The rebuild stays as the arm for a context that carried none.
                        if (ctx.RunSources != null && sysIdx < ctx.RunSources.Count)
                        {
                            runSources = ctx.RunSources[sysIdx];
                        }
                        else
                        {
                            int start = int.MaxValue, end = int.MinValue;
                            foreach (var m in system.Measures)
                            {
                                start = Math.Min(start, m.MeasureIndex);
                                end = Math.Max(end, m.MeasureIndex + 1);
                            }
                            runSources = end > start
                                ? BuildPairRunSources(multiScore, system.Measures, start, end)
                                : default;
                        }
                        sourceCache[sysIdx] = runSources;
                    }
                    IReadOnlyList<(Staff Staff, StaffLayout Layout)> RowsBelow(int upper)
                    {
                        var rowIdxs = ctx.BetweenRowStaves?.Invoke(sysIdx, upper);
                        if (rowIdxs is not { Count: > 0 })
                            return Array.Empty<(Staff, StaffLayout)>();
                        var rows = new List<(Staff, StaffLayout)>(rowIdxs.Count);
                        foreach (int idx in rowIdxs)
                            if (staffByIndex.TryGetValue(idx, out var rowStaff))
                                foreach (var g in system.StaffGroups)
                                    foreach (var st in g.Staves)
                                        if (st.StaffIndex == idx)
                                            rows.Add((rowStaff, st));
                        return rows;
                    }
                    result = MultiStaffLayouter.AttachedChordBaselineAboveTop(
                        system, roomSkylines[sysIdx], staffIndex, runSources, RowsBelow,
                        _options.StaffSpacing);
                }
                baseCache[key] = result;
                return result;
            };
        }

        return ChordNameEngraver.Calculate(ctx.Fonts,
            cn, systems, ml, ctx.Measures,
            ctx.MeasuresByStaff, staffYAt, minStaffYAt, scriptedSkylines,
            chordGridSheet: chordGridSheet, lowerStaffUpSkyline: lowerStaffUpSkyline,
            labelWindows: labelWindows,
            attachedBaselineAboveTop: attachedBaselineAboveTop);
    }

    /// <summary>
    /// The syllables, with the two things only the surrounding music can answer: what each
    /// one is centred on, and what it has to clear.
    /// </summary>
    /// <remarks>
    /// Extracted verbatim from <see cref="CalculateAnnotationLayouts"/>. Both caches are
    /// per-call and lazy, which is what keeps a score without note-bound lyrics doing no
    /// extra work at all.
    /// </remarks>
    private ImmutableArray<LyricLayout> LayoutLyrics(
        AnnotationLayoutContext ctx, ImmutableArray<MeasureLayout> ml,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? scriptedSkylines)
    {
        var systems = ctx.Systems;
        var lyrics = ctx.Lyrics;
        var staffByIndex = ctx.StaffByIndex;
        var measuresByStaff = ctx.MeasuresByStaff;
        var measures = ctx.Measures;

        // Per-(system, staff) DOWN-skyline for a note-bound lyric line that sits under an
        // UPPER staff, so it clears THAT staff's own notes and real (font-metric) glyph
        // height instead of the whole system's lowest staff. Mirrors lowerStaffUpSkyline
        // (chord names on a non-top staff).
        //
        // ⚠️ THE ROOM'S OWN LIST, NOT A SECOND BUILD. This is literally the skyline
        // MultiStaffLayouter.BuildAllStaffSkylines handed the alignment for this system —
        // dynamics, tab articulations, tuplet brackets, slurs, ties, beams, the chord-row
        // band, a text row's ink and a figured-bass row all merged in. Rebuilding it here
        // was the defect: the rebuild took SkylineBuilder.BuildStaffSkylines' side-table
        // parameters at their defaults, so the ROOM knew about an `f` under the staff and
        // the DRAWN baseline did not, and the syllable was engraved over it while the gap
        // between the staves stayed correct (HANDOFF 7.7's two spellings, and the reason
        // this reads a list instead of calling the builder: an argument can be forgotten
        // again, an index cannot).
        // LILYPOND-REF: lily/align-interface.cc:163-285 internal_get_minimum_translations —
        // the walk measures each element against `down_skyline`, what has accumulated above
        // it, and asks each element for its silhouette exactly once, through
        // lily/align-interface.cc:71-87 get_skylines, which reads that grob's OWN
        // `vertical-skylines` property. For a staff that property is its VerticalAxisGroup's,
        // which every outside-staff grob of the staff is already in
        // (lily/axis-group-interface.cc:220-238 generic_group_extent). There is no second,
        // thinner silhouette in LilyPond for a placement to read.
        var staffSkylines = ctx.StaffSkylines;
        VerticalSkyline? StaffDownSkyline(int sysIdx, int staffIndex)
        {
            if (staffSkylines == null || sysIdx < 0 || sysIdx >= staffSkylines.Count)
                return null;
            var perStaff = staffSkylines[sysIdx];
            return staffIndex >= 0 && staffIndex < perStaff.Count ? perStaff[staffIndex].Down : null;
        }

        // ...and the OTHER end of the block, out of the same list. The block hangs between two
        // staves, so it is closed by the LOWER one's UP profile, and that profile is the one
        // the room was measured from — a fermata on the lower part reaches up INTO this gap.
        // Rebuilding it here was the second half of the same defect the remark on
        // AnnotationLayoutContext.StaffSkylines describes, one call site further along:
        // ComputeBetweenStavesEnd built its own with every side table at its default, so the
        // chain closed against a staff with no marks on it.
        VerticalSkyline? StaffUpSkyline(int sysIdx, int staffIndex)
        {
            if (staffSkylines == null || sysIdx < 0 || sysIdx >= staffSkylines.Count)
                return null;
            var perStaff = staffSkylines[sysIdx];
            return staffIndex >= 0 && staffIndex < perStaff.Count ? perStaff[staffIndex].Up : null;
        }

        // Wired whenever the score carries ANY lyric line: the upper families read their own
        // staff's Down through it, and the block below the system reads its ANCHOR staff's
        // — same list, same reason (the system silhouette knows nothing of dynamics, and a
        // sung staff's f was engraved over the syllable it should have pushed down;
        // audit/lp-geometry lyrics.dynamic.staff-to-lyric).
        // ⚠️ IT USED TO ASK FOR A NOTE-BOUND LINE (`lyrics.Any(l => !l.IsLyricsRow)`), and the
        // remark above already named the reader that condition forgets: THE BLOCK BELOW THE
        // SYSTEM. That block exists with no note-bound line at all — a score whose every
        // lyric is an independent ROW is exactly the case `DistributeLooseLines` adds the
        // empty `-1` family for ("a book whose only lyrics ARE a row still has that block"),
        // and it is what `score { staff a  staff b  lyrics v }` builds whenever the row sings
        // a staff that is NOT the one directly above it. The condition was written for the
        // families and applied to both.
        // ⚠️ WHAT THE CLOSED GATE COST IS NOT A THINNER SILHOUETTE, IT IS THE FLOOR ITSELF.
        // With no per-staff skyline, `ResolveAnchor` falls through to the system silhouette
        // and pays the `skylineToAnchor` frame step, which is subtracted from the FIRST gap's
        // minimum — so that minimum went NEGATIVE (−2.994200 on the two-staff repro) where
        // the anchor staff's own ink floor is +6.362500, and `nonstaff-relatedstaff-spacing`
        // stopped binding. What the syllable was then drawn at is whatever the spring solve
        // left: 5.500000 — the spring's NATURAL LENGTH, not a floor — where the room was
        // loose, and 3.443800 on the system whose chain also carries the next system's
        // leading row. MEASURED on scratch/…/lyrics.lys: the lower staff's whole notes hang
        // 2.000000 below its bottom line and verse 1's baseline landed 1.443800 below it, so
        // the syllables were engraved THROUGH the notes on every system but the last (the
        // last one runs to the page edge, where the slack hides it).
        // ⚠️ THE RESERVATION NEVER HAD THIS SPLIT — `LyricReservationBelowSystem` seeds its
        // walk from `staffSkylines[anchorStaff.StaffIndex].Down` for any anchor staff — so
        // the page reserved the room and the chain declined to use it. One quantity, two
        // representations (HANDOFF 5.2.1②), and the disagreement was invisible to the suite
        // because the reserved side is the one every ledger point reads.
        Func<int, int, VerticalSkyline?>? anchorStaffDownSkyline = null;
        var nbAnchor = ctx.NoteBoundAnchorY;
        if (staffByIndex != null && !lyrics.IsDefaultOrEmpty)
            anchorStaffDownSkyline = StaffDownSkyline;

        // ...and the OTHER end of that block's chain: the next spaceable staff of the same
        // system. Per (system, anchor staff), how much room the two reference points leave
        // and the up-skyline that closes it.
        //
        // LILYPOND-REF: lily/page-layout-problem.cc:936-939 — distribute_loose_lines is
        // handed `last_spaceable_line_translation` and `-solution_[spring_idx]`, two members
        // of the PAGE's spring chain, and the same call site serves a block between two
        // SYSTEMS and a block between two STAVES of one system. So the room is read the same
        // way here as in BuildLooseChainEnds; what differs is only the minimum that closes
        // it (:923-925, no null line), which the engraver builds from NextStaffUp.
        //
        // ⚠️ A SPAN, NOT TWO POSITIONS, so no frame can be mixed: both staves are read out
        // of the SAME system, and the half-staff from a top line to a reference point is the
        // same on both ends and cancels. Reading the near end here and the far end from
        // systemsArray[0] would be the frame error HANDOFF 1 keeps naming.
        //
        // ⚠️ PER SYSTEM, because the page's own solve can have moved the staves apart by
        // different amounts (PageLayouter.RespaceStaves) and hara-kiri can leave different
        // staves alive — the same reason BuildLooseChainEnds could not read systemsArray[0].
        Func<int, int, (double Room, VerticalSkyline NextStaffUp)?>? betweenStavesEnd = null;
        // ⚠️ A ROW-ONLY BLOCK NEEDS THIS END TOO, and the gate used to ask only for a
        // NOTE-BOUND line under an upper anchor. `score { staff m  lyrics v  staff m }` has
        // none — its whole block is the row — so the end was never built, the chain had
        // nothing to close on, and the row kept its band. What the run holds is not what says
        // whether it has two ends.
        if (nbAnchor is { Count: > 0 } && staffByIndex != null
            && (lyrics.Any(l => !l.IsLyricsRow && nbAnchor.ContainsKey(l.StaffIndex))
                || (ctx.BetweenRowStaves is { } betweenRows
                    && nbAnchor.Keys.Any(a => Enumerable.Range(0, systems.Length)
                        .Any(sysIdx => betweenRows(sysIdx, a).Count > 0)))))
        {
            var endCache = new Dictionary<(int, int), (double, VerticalSkyline)?>();
            betweenStavesEnd = (sysIdx, anchorStaffIndex) =>
            {
                var key = (sysIdx, anchorStaffIndex);
                if (endCache.TryGetValue(key, out var cached))
                    return cached;
                var computed = ComputeBetweenStavesEnd(
                    sysIdx, anchorStaffIndex, systems, staffByIndex, StaffUpSkyline);
                endCache[key] = computed;
                return computed;
            };
        }

        // WHICH LINE EACH ROW IS, and — for the ones that are not made of syllables — its ink.
        // The chain's springs are get_spacing_spec's (page-layout-problem.cc:1266-1342), which
        // reads both off the GROB, so both have to travel per row rather than per score.
        // ⚠️ ONE DELEGATE FOR "IS IT A CHORDS ROW" AND "WHAT IS ITS INK", because they have
        // one answer: null means the row's ink is its syllables and the engraver already has
        // them. A second predicate would be free to disagree with this one (HANDOFF 5.2.1②).
        var sp = _options.StaffSpacing;
        Func<int, LooseLineSpacer.RunLine>? runLineOf = null;
        Func<int, int, (VerticalSkyline Up, VerticalSkyline Down)?>? chordRowInk = null;
        if (staffByIndex != null)
        {
            runLineOf = idx => staffByIndex.TryGetValue(idx, out var st)
                ? RunLineOf(st, sp) : LooseLineSpacer.NoteBoundLyricLine(sp);
            var inkCache = new Dictionary<(int, int), (VerticalSkyline, VerticalSkyline)?>();
            chordRowInk = (sysIdx, idx) =>
            {
                var key = (sysIdx, idx);
                if (inkCache.TryGetValue(key, out var hit)) return hit;
                (VerticalSkyline, VerticalSkyline)? built = null;
                if (staffByIndex.TryGetValue(idx, out var row)
                    && row.IsTextRow && !row.IsLyricsTextRow
                    && sysIdx >= 0 && sysIdx < systems.Length)
                    built = ChordNameEngraver.RowSkylines(
                        ctx.Fonts, ctx.ChordNames ?? ImmutableArray<ChordNameItem>.Empty,
                        systems[sysIdx].Measures, idx, row.PrimaryVoice.Measures);
                inkCache[key] = built;
                return built;
            };
        }

        // The closing staff's attached chord line, as the chain's last element — see the
        // append in LyricEngraver.BuildChainPrefix. One construction with the walk's
        // (MultiStaffLayouter.AttachedChordLine), asked per (system, anchor staff).
        Func<int, int, (int StaffIndex, VerticalSkyline Up, VerticalSkyline Down)?>?
            attachedChordBelow = null;
        if (ctx.MultiScore is { } multiScore && staffByIndex != null
            && ctx.ChordNames is { IsDefaultOrEmpty: false } chordItems
            && chordItems.Any(c => !c.IsChordRow && c.UseTiming))
        {
            var attCache =
                new Dictionary<(int, int), (int, VerticalSkyline, VerticalSkyline)?>();
            var attLinesOf =
                new Dictionary<int, Func<int, MultiStaffLayouter.PairLooseLine?>?>();
            attachedChordBelow = (sysIdx, anchorStaffIndex) =>
            {
                var key = (sysIdx, anchorStaffIndex);
                if (attCache.TryGetValue(key, out var hit))
                    return hit;
                (int, VerticalSkyline, VerticalSkyline)? result = null;
                if (sysIdx >= 0 && sysIdx < systems.Length)
                {
                    // The next LIVE spaceable staff below the anchor — the staff that
                    // closes this chain (ComputeBetweenStavesEnd picks the same one).
                    int lower = int.MaxValue;
                    foreach (var g in systems[sysIdx].StaffGroups)
                    {
                        if (g.Staves.IsDefaultOrEmpty) continue;
                        foreach (var st in g.Staves)
                            if (!st.IsHidden
                                && StaffAffinity.IsSpaceable(st.StaffAffinity)
                                && st.StaffIndex > anchorStaffIndex
                                && st.StaffIndex < lower)
                                lower = st.StaffIndex;
                    }
                    if (lower != int.MaxValue)
                    {
                        // The one construction (BuildAttachedChordLines), per system —
                        // the same supplier the placement's PairRunSources carries.
                        if (!attLinesOf.TryGetValue(sysIdx, out var linesOf))
                            attLinesOf[sysIdx] = linesOf =
                                BuildAttachedChordLines(multiScore, systems[sysIdx].Measures);
                        if (linesOf?.Invoke(lower) is { } line)
                            result = (lower, line.Up, line.Down);
                    }
                }
                attCache[key] = result;
                return result;
            };
        }

        var engraver = new LyricEngraver(
            parentAlignmentEdge: LyricEngraver.ParentAlignmentEdge(
                measuresByStaff, measures, ctx.VoicesByStaff),
            systemPadding: _options.VerticalSpacing.SystemSystem.Padding,
            fonts: ctx.Fonts);
        var laid = engraver.CalculateLayouts(
            lyrics, ml, _options.StaffHeight, systems, scriptedSkylines, ctx.StaffYByIndex,
            ctx.NoteBoundAnchorY, anchorStaffDownSkyline, ctx.LooseChainEnd,
            betweenStavesEnd, ctx.LastSpaceableStaffY, ctx.TrailingRowStaves,
            ctx.BetweenRowStaves, ctx.VerseSkylines, ctx.LyricChains,
            sp, runLineOf, chordRowInk, attachedChordBelow);

        // The rows the chain solved travel back out through the context — see
        // AnnotationLayoutContext.SolvedRowBaselines for why they are applied afterwards
        // rather than here.
        foreach (var kv in engraver.SolvedRowBaselines)
            ctx.SolvedRowBaselines[kv.Key] = kv.Value;

        return laid;
    }

    /// <summary>
    /// The alignment's loose lines between each pair of staves of ONE system: the
    /// <c>with lyrics</c> block a non-last group carries, as skylines, so the room the two
    /// staves leave is the block's own ink rather than a constant per verse.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 — the run of non-spaceable lines
    /// collected between two spaceable staves, which
    /// <c>Align_interface::internal_get_minimum_translations</c> has already walked over.
    /// <para>
    /// Null when the score has no such block, so a score without one does no extra work and
    /// takes the adjacent-pair path unchanged.
    /// </para>
    /// <para>
    /// ⚠️ ONLY ACROSS A GROUP BOUNDARY, AND THAT CONDITION IS LILYSHARP-OWN. LilyPond has
    /// no such test: a Lyrics context is an element of the alignment wherever it sits, so a
    /// block between two staves of ONE group would be in its walk too. Lily# hangs a
    /// note-bound line below the whole GROUP (<c>LyricEngraver.CalculateLayouts</c>: a block
    /// under a non-last group anchors on that group's bottom staff), so under this model a
    /// pair inside one group really has nothing between it — the condition follows the
    /// model rather than the source, and closing it means moving the model first.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The system's LOOSE BLOCK — the note-bound verses and the independent lyrics ROWS
    /// standing under them, in alignment order — as DOWN profiles in the SYSTEM-ORIGIN
    /// frame, in the two readings its two consumers need: every line at its ALIGNMENT
    /// MINIMUM (what the page RESERVES for it) and every line at its spring's FORCE-0 REST
    /// LENGTH (where the chain that draws it comes to rest). Both null when the system has
    /// no such block.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:593-599 — <c>build_system_skyline</c> is
    /// handed <c>Align_interface::get_minimum_translations</c>, so a loose line IS in the
    /// system's skyline, at its minimum, and the inter-system floor that reads it is
    /// X-RESOLVED (:625-632 <c>up_skyline.distance(bottom_skyline_, …)</c>).
    /// LILYPOND-REF: lily/align-interface.cc:235-238 — that minimum does NOT contain the
    /// spec's basic-distance: it is added only behind <c>INT_MAX == end &amp;&amp; 0 == start</c>,
    /// the pure branch, and the call that feeds the skyline passes <c>start = end = 0</c>.
    /// The 5.500000 the line is drawn at arrives afterwards, out of
    /// <c>distribute_loose_lines</c>, INSIDE the room this reservation left.
    /// <para>
    /// It is <see cref="AlignmentWalk"/>, the same walk the placement and the inter-staff
    /// room run — the whole point of the island. ⚠️ IT RETURNED A SCALAR (the profile's
    /// deepest point) until 2026-08-20, and both page paths spread that scalar under every
    /// X — the blindness audit/lp-geometry's <c>lyrics.band-floor.*</c> pair measured:
    /// Lily# read 13.392483 on two books LilyPond forks 12.362129 / 10.090000 apart, the
    /// fork being nothing but WHERE the next system's tall ink stands in X. The profile
    /// reaches the floors through <see cref="PagingAugmentProgram.Builder.AddLyricBand"/>;
    /// the extents keep its deepest point (<c>LayoutSystems</c>), which is the one reading
    /// of it that IS a scalar.
    /// </para>
    /// <para>
    /// ⚠️ ONLY THE BLOCK THAT HANGS BELOW THE SYSTEM, which is the one attached to a staff of
    /// the LAST group — the same split <c>BuildStaffAnchorTables</c> makes for
    /// <c>NoteBoundAnchorY</c>. A block between two groups is INSIDE the system and its room
    /// is the staff pair's (<see cref="BuildLooseLinesBetween"/>); reserving it here as well
    /// would count it twice, which the extent sum did.
    /// </para>
    /// <para>
    /// ★ TWO PROFILES SINCE 2026-08-29 (session 292), because ONE QUANTITY HAD TWO
    /// CONSUMERS THAT WANT DIFFERENT NUMBERS — HANDOFF 5.2.1② read the other way round, and
    /// the split is the fix rather than the defect. The MINIMUM is what LilyPond reserves
    /// and what the inter-system floor must keep reading (the reference above). The
    /// AT-REST profile exists for the CROP alone: Lily# sizes a single page to its content
    /// (<c>LayoutEngine.CreatePages</c>, a declared divergence) and the chain that draws
    /// this block has been solved into the PAPER since session 291, so it rests at the
    /// spring's ideal and the page's bottom white shrank by the difference.
    /// ⚠️ AT-REST IS EXACT ON EVERY PAGE THAT KEEPS THE CROP PATH, and that is an identity
    /// rather than an estimate: the crop is <c>anchor + depth + margin</c> and the chain's
    /// room is <c>paper − margin − anchor</c>, so a page whose AT-REST crop fits the paper
    /// has <c>room ≥ depth</c> — the chain solves at force ≥ 0 and every spring really does
    /// sit at <c>max(min, ideal)</c>. A page where it does not fit leaves for
    /// <c>OptimalPages</c>, where the height IS the paper and this profile is never read.
    /// ⚠️ THE ONE THING IT DOES NOT CARRY is the page-edge chain's own STRETCH TAIL: the
    /// null spring's HUGE_STRETCH absorbs the slack but not quite all of it, so the drawn
    /// distance exceeds the ideal by <c>force × stretchability</c> — MEASURED 1.451282664e-6
    /// on book TBL2, which is the same tail LilyPond publishes (5.500001451282664 against a
    /// 5.5 basic-distance). The crop is under by that, ~2.5 nm of paper.
    /// </para>
    /// </remarks>
    private LooseBlockProfiles LyricReservationBelowSystem(
        MultiStaffScore score, ImmutableArray<MeasureLayout> measureLayouts,
        List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines,
        ImmutableArray<StaffGroupLayout> groups, int startMeasure, int endMeasure)
    {
        if (score.Lyrics.IsDefaultOrEmpty || groups.IsDefaultOrEmpty)
            return default;

        // The staff a staff-affinity-UP line below the system is spaced from, and what stands
        // under it. LILYPOND-REF: lily/page-layout-problem.cc:943-944 last_spaceable_line.
        var alignment = ClassifySystem(groups);
        if (alignment.LastSpaceable is not { } anchorStaff
            || anchorStaff.StaffIndex >= staffSkylines.Count)
            return default;

        // ⚠️ THE ANCHOR STAFF'S OWN LINES, and this is the THIRD spelling of the split — the
        // other two are BuildStaffAnchorTables' anchor table and BuildLooseLinesBetween's
        // range, and all three have to name the same staff. It used to be the last MODEL
        // group's whole staff range (`total - StaffGroups[^1].StaffCount`), which did not
        // agree with either: on a grand staff it reserved for all four staves' lyrics here
        // while the chain drew them elsewhere, and with a trailing lyrics-row group it
        // reserved for the ROW group's range rather than the staff's. Now that a lyric hangs
        // off its own staff, what hangs below the SYSTEM is exactly what is attached to the
        // last spaceable staff — the one this reservation is anchored on.
        var engraver = BuildBlockEngraver(score);

        // THE RUN, in alignment order: the note-bound verses hanging under the anchor staff,
        // and then every independent ROW standing under them. ★ ONE LIST BECAUSE LILYPOND HAS
        // ONE RUN — every non-spaceable line between two spaceable ones goes into the same
        // vector (page-layout-problem.cc:919-925), and a row is non-spaceable wherever it
        // stands. Until 2026-07-28 the row was a separate branch that reserved WHERE IT WAS
        // DRAWN, because it was placed as a staff-like band and never solved; it is an element
        // of the chain now, so what it reserves is its ALIGNMENT MINIMUM like every other
        // line (:593-599).
        var lines = RunBelowAnchor(
            score, engraver, measureLayouts, startMeasure, endMeasure,
            anchorStaff.StaffIndex, alignment.Trailing);
        if (lines.Count == 0)
            return default;

        // EVERY LINE AT ITS OWN TRANSLATION, exactly LilyPond's build_system_skyline
        // (page-layout-problem.cc:1093-1108 merges each element's skyline RAISED BY ITS OWN
        // TRANSLATION), so a line with a deeper descender than the one under it still owns
        // its stretch of the band — and a stretch of X no syllable reaches holds NOTHING,
        // which is the whole difference from the scalar this used to flatten to.
        // ⚠️ THE STEP IS THE PAIR'S OWN SPEC, not "related for the first and nonstaff for the
        // rest". Those two constants ARE what get_spacing_spec returns for an all-Lyrics run,
        // which is why the reading is unchanged on every book that has one; they are the
        // WRONG two the moment a ChordNames line is in the run, and this reservation has to
        // agree with the chain term for term or the page reserves one room and the solve uses
        // another (HANDOFF 5.2.1②, and the walk is shared for exactly that reason).
        var sp = _options.StaffSpacing;

        // ONE LOOP, TWO FLOORS. `restLength` is the only thing the two profiles disagree
        // about: the RESERVATION floors each step at the spec's minimum-distance (LilyPond's
        // minimum translations, the reference above), and the CROP floors it at the spring's
        // force-0 rest length max(minimum, ideal) — which is where the chain that draws this
        // block actually comes to rest. Written as one body rather than two so the run, the
        // specs and the merge cannot drift apart; HANDOFF 5.2.1② is what two spellings of
        // this walk have cost twice already.
        VerticalSkyline? Walk(bool atRest, out double idealOverFloor)
        {
            var walk = new AlignmentWalk();
            walk.Seed(staffSkylines[anchorStaff.StaffIndex].Down);
            var built = new VerticalSkyline(VerticalDirection.Down);
            var previous = LooseLineSpacer.SpaceableStaffLine;
            idealOverFloor = 0;
            for (int k = 0; k < lines.Count; k++)
            {
                var spec = StaffAffinity.GetSpacingSpec(
                    previous.Affinity, previous.Specs,
                    lines[k].Line.Affinity, lines[k].Line.Specs, sp.StaffStaff);
                // LILYPOND-REF: lily/page-layout-problem.cc:1345-1358 alter_spring_from_spacing_spec
                // — basic-distance IS the ideal; and
                // lily/spring.cc:219-237 Spring::length, whose last line is
                // `max (min_distance_, ideal_distance_ + force * inv_k)`, so at force 0 the
                // spring sits at max(min_distance, ideal). Passing the ideal as the walk's
                // minimum IS that max, because Advance already takes max(ink, minimum) —
                // see CreateSpring, whose `ensure_min_distance` argument is this walk's dy.
                double floor = atRest
                    ? Math.Max(spec.MinimumDistance, spec.BasicDistance)
                    : spec.MinimumDistance;
                double dy = walk.Advance(lines[k].Up, lines[k].Down, spec.Padding, floor);
                if (!atRest && spec.BasicDistance - dy > idealOverFloor)
                    idealOverFloor = spec.BasicDistance - dy;
                if (lines[k].Down is { IsEmpty: false } lineDown)
                    built.Merge(lineDown.Buildings, 0, -walk.Where);
                previous = lines[k].Line;
            }
            if (built.IsEmpty)
                return null;

            // ⚠️ THE ANCHOR STAFF'S OWN INK IS DELIBERATELY NOT IN THIS — it stays the system
            // skyline's business (SkylineBuilder.AddEdgeStaffInk seeds the edge staff for the
            // row and verse spellings alike, since a one-staff-plus-row system's first staff IS
            // the staff). A first draft merged staffSkylines[anchor].Down here for the row-outer
            // case, and the PER-STAFF profile is a richer silhouette than the edge model, so the
            // two spellings priced the same gap 0.915000 apart and the
            // SystemGap_ReadsARowsBandOnce pin went red — one quantity, two representations,
            // HANDOFF 5.2.1②. What OuterStaff still leaves unseeded (the LAST spaceable staff
            // of a multi-staff system whose outer element is a row) is that remark's own ▶
            // item, unchanged by this island.

            // The walk ran in the anchor staff's REFPOINT frame; the paging silhouette lives in
            // the SYSTEM-ORIGIN frame. One shift, stated once — the same conversion
            // PageAnchorOffsets' ToLast carries for the springs.
            built.Raise(MultiStaffLayouter.StaffRefpoint(anchorStaff));
            return built;
        }

        var minimum = Walk(atRest: false, out double slack);
        // ⚠️ THE SECOND WALK IS TAKEN ONLY WHEN IT CAN DIFFER, and `slack` is the whole of
        // that question: if no spring's ideal rose above the floor the minimum walk took,
        // then the at-rest floor equals the minimum floor at step 0, so the two walks hold
        // the same accumulation there and — by induction on that — at every step after it.
        // The common sung book takes this branch (MEASURED on the reader's corpus, session
        // 291: all three lyric books' first spring floors above the 5.5 ideal), which is why
        // this memo's cost is not doubled — SystemLayoutCache.GetOrComputeLyricBand's own
        // remark measures what that would have been worth.
        return new LooseBlockProfiles(
            minimum, slack > 0 ? Walk(atRest: true, out _) : minimum);
    }

    /// <summary>
    /// One run's elements below an anchor staff, in alignment order: each line's own ink and
    /// the two things <c>get_spacing_spec</c> asks of it — which way it leans and which
    /// context's <c>nonstaff-*</c> specs it carries.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 — the non-spaceable lines between
    /// two spaceable ones, collected in alignment order. The note-bound verses hanging under
    /// the anchor staff come first because that is where the alignment walks them; then every
    /// independent ROW, a lyrics row verse by verse (its verses are separate Lyrics contexts
    /// to LilyPond) and a chords row as the single line it is.
    /// <para>
    /// ⚠️ ONE READING OF THE INK, THREE READERS. The reservation
    /// (<see cref="LyricReservationBelowSystem"/>) and the solve
    /// (<c>LyricEngraver.DistributeLooseLines</c>) have to walk the SAME list with the SAME
    /// specs, or the page leaves a room the chain does not use — the shape HANDOFF 5.2.1②
    /// names, and the one this island has paid for twice.
    /// </para>
    /// <para>
    /// ★ THE ORDER IS <see cref="LooseLineSpacer.RunSlots"/>'S SINCE 2026-08-26 — the one
    /// spelling all three walks of a run read. This reader keys its ink by POSITION in
    /// each supplier's built list (that remark's seam ⑵); the below-system run has no
    /// attached chord line because it has no closing staff inside the system.
    /// </para>
    /// </remarks>
    private List<(VerticalSkyline Up, VerticalSkyline Down, LooseLineSpacer.RunLine Line)>
        RunBelowAnchor(
            MultiStaffScore score, LyricEngraver engraver,
            ImmutableArray<MeasureLayout> measureLayouts, int startMeasure, int endMeasure,
            int anchorStaffIndex, ImmutableArray<int> rows)
    {
        var sp = _options.StaffSpacing;
        var ink = new Dictionary<(int Line, int Verse),
            (VerticalSkyline Up, VerticalSkyline Down)>();

        var noteBound = engraver.NoteBoundBlockSkylines(
            score.Lyrics, measureLayouts, startMeasure, endMeasure,
            anchorStaffIndex, anchorStaffIndex + 1);
        for (int k = 0; k < noteBound.Count; k++)
            ink[(anchorStaffIndex, k)] = noteBound[k];

        var rowsIn = new List<(int RowStaff, IReadOnlyList<int> Verses,
            LooseLineSpacer.RunLine Line)>();
        if (!rows.IsDefaultOrEmpty)
        {
            var staffByIndex = new Dictionary<int, Staff>();
            foreach (var (_, st, idx) in score.EnumerateStaves())
                staffByIndex[idx] = st;

            foreach (int rowStaff in rows)
            {
                if (!staffByIndex.TryGetValue(rowStaff, out var row)) continue;
                var line = RunLineOf(row, sp);
                if (row.IsLyricsTextRow)
                {
                    var verses = engraver.RowBlockSkylines(
                        score.Lyrics, measureLayouts, startMeasure, endMeasure, rowStaff);
                    for (int k = 0; k < verses.Count; k++)
                        ink[(rowStaff, k)] = verses[k];
                    rowsIn.Add((rowStaff, LooseLineSpacer.ByPosition(verses.Count), line));
                }
                else
                {
                    // ⚠️ THIS SYSTEM'S LAYOUTS, SELECTED BY MeasureIndex. RowSkylines reads a
                    // layout's X and takes every chord it can pair with one; the list handed in
                    // is the WHOLE SCORE's, whose positions restart at 0 on each system, so
                    // giving it the lot builds a row out of several systems' columns at once.
                    // The lyrics arm carries its range as two arguments, which is why only this
                    // one has to say it.
                    var systemLayouts = measureLayouts
                        .Where(ml => ml.MeasureIndex >= startMeasure && ml.MeasureIndex < endMeasure)
                        .ToImmutableArray();
                    var (u, d) = ChordNameEngraver.RowSkylines(
                        score.TextMetrics, score.ChordNames, systemLayouts, rowStaff,
                        row.PrimaryVoice.Measures);
                    if (!u.IsEmpty || !d.IsEmpty)
                    {
                        ink[(rowStaff, 0)] = (u, d);
                        rowsIn.Add((rowStaff, LooseLineSpacer.SingleElementLine, line));
                    }
                    else
                        rowsIn.Add((rowStaff, LooseLineSpacer.NoElements, line));
                }
            }
        }

        var (slots, _) = LooseLineSpacer.RunSlots(
            anchorStaffIndex, LooseLineSpacer.ByPosition(noteBound.Count),
            LooseLineSpacer.NoteBoundLyricLine(sp), rowsIn, attachedChord: null);

        var run = new List<(VerticalSkyline, VerticalSkyline, LooseLineSpacer.RunLine)>(
            slots.Count);
        foreach (var slot in slots)
        {
            var (u, d) = ink[(slot.LineKey, slot.Verse)];
            run.Add((u, d, slot.Line));
        }
        return run;
    }

    /// <summary>
    /// The BAND a system's trailing lyrics rows occupy, in the system-origin frame — the room
    /// they need while the loose chain declines to place them.
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN: departs from lily/page-layout-problem.cc:919-925
    /// <c>Page_layout_problem::append_system</c>, which collects EVERY non-spaceable line
    /// between two spaceable ones and always solves it. GOES when the last un-modelled
    /// arrangement does — see <c>SystemAlignment.UnmodelledRow</c>, whose list this
    /// is the reservation side of. OBSERVED BY <c>TrailingLyricsRowBandTests</c>, which is
    /// what keeps the band and the drawn position from drifting apart: it renders the book
    /// and asserts no syllable lands inside a staff's line span, so the cross-file invariant
    /// this remark leans on below is measured rather than merely asserted.
    /// <para>
    /// ⚠️ AND IT IS THE BAND RATHER THAN THE INK ON PURPOSE. LilyPond has no
    /// band: a Lyrics context is a <c>VerticalAxisGroup</c> whose extent is its syllables, and
    /// it is always solved (page-layout-problem.cc:919-925), so "the room a row keeps while
    /// nothing solves it" is a Lily# state with no counterpart. While that state lasts, the
    /// row is DRAWN in its stacked band — <c>LyricEngraver.CalculateLayouts</c>' <c>isRow</c>
    /// arm, whose own remark says the pre-chain placement is what a row the chain does not
    /// reach keeps — so the band is what the page must leave. Reserving the walked INK
    /// instead would be a second model of a position the chain is not computing: its verse
    /// step is <c>max(2.8, ink + 0.2)</c> (<c>RowSkylinesAboutBaseline</c>) while the drawn
    /// step is the flat <c>VerseSpacing</c>, and the two disagree by 0.400000 per extra verse
    /// on the reported book — the reservation would sit ABOVE the syllables it is for.
    /// </para>
    /// <para>
    /// ⚠️ THE WIDTH IS THE DRAWN WIDTH and the band spans all of it, which is why this is a
    /// box and not a silhouette — the same argument <c>CreatePages</c> makes for the chord-row
    /// band above the next system ("a band spans every X, so the X-disjoint argument for
    /// preferring Distance() does not apply to it"), and the same construction
    /// <c>MultiStaffLayouter.ReserveChordRowBand</c> uses for the mirror image above a staff.
    /// </para>
    /// </remarks>
    private static VerticalSkyline? TrailingRowBandBelowSystem(
        ImmutableArray<int> trailing, ImmutableArray<StaffGroupLayout> groups,
        ImmutableArray<MeasureLayout> measureLayouts, int startMeasure, int endMeasure)
    {
        if (trailing.IsDefaultOrEmpty || measureLayouts.IsDefaultOrEmpty)
            return null;

        // The DEEPEST of them: they are stacked, so the lowest band bottom is the floor for
        // all of them, and one box says it once.
        double bottom = 0;
        foreach (var group in groups)
        {
            if (group.Staves.IsDefaultOrEmpty) continue;
            foreach (var st in group.Staves)
                if (!st.IsHidden && trailing.Contains(st.StaffIndex))
                    bottom = Math.Min(bottom, st.Y - st.Height);
        }
        if (bottom >= 0)
            return null;

        // ⚠️ THIS SYSTEM'S MEASURES, SELECTED BY MeasureIndex. The list handed in is the
        // WHOLE SCORE's (the pairing RowBlockSkylines wants, and the reason this method is
        // given the range at all); taking its full X span would stretch the band across
        // every system's width at once, which is a claim about X this box has no right to
        // make.
        double xLeft = double.PositiveInfinity, xRight = double.NegativeInfinity;
        foreach (var ml in measureLayouts)
        {
            if (ml.MeasureIndex < startMeasure || ml.MeasureIndex >= endMeasure) continue;
            xLeft = Math.Min(xLeft, ml.X);
            xRight = Math.Max(xRight, ml.X + ml.Width);
        }
        if (xRight <= xLeft)
            return null;

        // ⚠️ THE `0` IS NOT THE BAND'S TOP AND IS NOT READ. FromBox stores the edge on the
        // skyline's OWN side — a DOWN skyline keeps yBottom and discards yTop — so the box
        // is the floor alone, which is all a floor should say. Written as the system origin
        // rather than as some larger number so it cannot be mistaken for a claim.
        return VerticalSkyline.FromBox(xLeft, xRight, bottom, 0, VerticalDirection.Down);
    }

    /// <summary>A lyric engraver configured for geometry only — one X model, no layout.</summary>
    private static LyricEngraver BuildBlockEngraver(MultiStaffScore score)
        => LyricEngraver.ForGeometry(score);

    /// <param name="cache">The session's per-system cache, or null (full compile). With a
    /// cache, each upper staff's block is served across keystrokes by
    /// <see cref="SystemLayoutCache.GetOrComputeLooseLines"/> (finding 4-4) — the slice
    /// arguments that follow are its key and must be the same values the system's other
    /// memos were keyed with.</param>
    private MultiStaffLayouter.LooseLinesBetween? BuildLooseLinesBetween(
        MultiStaffScore score, ImmutableArray<MeasureLayout> measureLayouts,
        int startMeasure, int endMeasure,
        SystemLayoutCache? cache, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration)
    {
        // ⚠️ STAVES, NOT GROUPS — and the bail-out used to be `StaffGroups.Length < 2`,
        // which is the same defect stated at the door: a grand staff is ONE group, so a
        // score whose every staff carried a lyric line returned null here and no pair had
        // anything between it. What stands between two staves is what hangs off the UPPER
        // one, whether or not a group boundary happens to fall there.
        if (score.Lyrics.IsDefaultOrEmpty || score.StaffGroups.Sum(g => g.StaffCount) < 2)
            return null;
        bool anyNoteBound = false;
        foreach (var l in score.Lyrics)
            if (!l.IsLyricsRow) { anyNoteBound = true; break; }
        if (!anyNoteBound)
            return null;

        var engraver = BuildBlockEngraver(score);
        // A note-bound verse is a Lyrics line wherever it hangs, so it carries the Lyrics
        // context's affinity and spec set into the walk.
        // LILYPOND-REF: ly/engraver-init.ly:648-658 Lyrics — staff-affinity = UP and the
        // three nonstaff-* declarations, association notwithstanding (the LYRC/LYRR and
        // LYRV/LYRRV dumps measure that identity).
        var lyricSpecs = _options.StaffSpacing.Lyrics;

        var perPair = new Dictionary<(int, int), IReadOnlyList<MultiStaffLayouter.PairLooseLine>?>();
        return (upperStaffIndex, lowerStaffIndex) =>
        {
            var key = (upperStaffIndex, lowerStaffIndex);
            if (perPair.TryGetValue(key, out var hit))
                return hit;

            // The block is the upper staff's OWN note-bound lines — the half-open range
            // [upper, upper+1) — which is the same selection BuildStaffAnchorTables gives
            // that staff an anchor for. The two must agree or the block is drawn at one
            // staff's baseline and the room measured from another's.
            IReadOnlyList<MultiStaffLayouter.PairLooseLine>? Compute()
            {
                var built = engraver.NoteBoundBlockSkylines(
                    score.Lyrics, measureLayouts, startMeasure, endMeasure,
                    upperStaffIndex, upperStaffIndex + 1);
                return built.Count > 0
                    ? built
                        .Select(b => new MultiStaffLayouter.PairLooseLine(
                            b.Up, b.Down, StaffAffinityDirection.Up, lyricSpecs))
                        .ToList()
                    : null;
            }
            var lines = cache == null
                ? Compute()
                : cache.GetOrComputeLooseLines(upperStaffIndex,
                    startMeasure, endMeasure - startMeasure, isFirstSystem, isLastSystem,
                    indent, commonShortestDuration, Compute);
            perPair[key] = lines;
            return lines;
        };
    }

    /// <summary>
    /// ONE SYSTEM's attached chord lines, by the staff that hosts them — the run's TRAILING
    /// element for the pair whose lower staff that is. Null when no staff of the score
    /// qualifies (<c>MultiStaffLayouter.AttachedChordLineInRun</c>), so every other score
    /// pays two O(chordNames) scans and nothing else.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 loose_lines — the ChordNames
    /// context above a lower staff is one of the pair's run, not a band on the staff's skyline.
    /// Built per system because the line's ink is this system's symbols at this system's
    /// columns, cached per staff for the three walk consumers (placement, gap, springs).
    /// </remarks>
    /// <summary>
    /// ONE SYSTEM's pair-run suppliers, as the one value the placement, the springs and
    /// the drawn-baseline walk all take (<c>MultiStaffLayouter.PairRunSources</c>).
    /// </summary>
    private MultiStaffLayouter.PairRunSources BuildPairRunSources(
        MultiStaffScore score, ImmutableArray<MeasureLayout> measureLayouts,
        int startMeasure, int endMeasure,
        SystemLayoutCache? cache = null, bool isFirstSystem = false,
        bool isLastSystem = false, double indent = 0, double commonShortestDuration = 0)
        => new(
            BuildLooseLinesBetween(score, measureLayouts, startMeasure, endMeasure,
                cache, isFirstSystem, isLastSystem, indent, commonShortestDuration),
            BuildAttachedChordLines(score, measureLayouts),
            BuildRowVerseInk(score, measureLayouts, startMeasure, endMeasure));

    /// <summary>
    /// A lyrics ROW's per-verse ink, by row staff index — the pair walk's supplier for
    /// the multi-verse element split (<c>MultiStaffLayouter.PairBlocks</c>, seam ⑴'s
    /// fold). Null when the score has no lyrics row at all, so every other score pays
    /// one predicate and nothing else.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SAME SUPPLIER THE BAND COMPOSED FROM: <c>RowBlockSkylines</c> through the
    /// geometry engraver, per verse about each verse's own baseline — the reservation's
    /// and the solve's reading of the same ink (<c>RunBelowAnchor</c>,
    /// <c>BuildVerseSkylines</c>' agreement net). Handing the walk anything else would
    /// be a second spelling of the quantity (HANDOFF 5.2.1②).
    /// </remarks>
    private Func<int, IReadOnlyList<(VerticalSkyline Up, VerticalSkyline Down)>?>?
        BuildRowVerseInk(
            MultiStaffScore score, ImmutableArray<MeasureLayout> measureLayouts,
            int startMeasure, int endMeasure)
    {
        if (score.Lyrics.IsDefaultOrEmpty || !score.Lyrics.Any(l => l.IsLyricsRow))
            return null;

        var staffByIndex = new Dictionary<int, Staff>();
        foreach (var (_, st, idx) in score.EnumerateStaves())
            staffByIndex[idx] = st;

        var engraver = BuildBlockEngraver(score);
        var cache = new Dictionary<int,
            IReadOnlyList<(VerticalSkyline Up, VerticalSkyline Down)>?>();
        return rowStaff =>
        {
            if (cache.TryGetValue(rowStaff, out var hit))
                return hit;
            IReadOnlyList<(VerticalSkyline Up, VerticalSkyline Down)>? verses =
                staffByIndex.TryGetValue(rowStaff, out var staff) && staff.IsLyricsTextRow
                    ? engraver.RowBlockSkylines(
                        score.Lyrics, measureLayouts, startMeasure, endMeasure, rowStaff)
                    : null;
            cache[rowStaff] = verses;
            return verses;
        };
    }

    private Func<int, MultiStaffLayouter.PairLooseLine?>? BuildAttachedChordLines(
        MultiStaffScore score, ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (score.ChordNames.IsDefaultOrEmpty
            || !score.ChordNames.Any(c => !c.IsChordRow && c.UseTiming))
            return null;

        var staffByIndex = new Dictionary<int, Staff>();
        foreach (var (_, st, idx) in score.EnumerateStaves())
            staffByIndex[idx] = st;

        var sp = _options.StaffSpacing;
        var cache = new Dictionary<int, MultiStaffLayouter.PairLooseLine?>();
        return staffIndex =>
        {
            if (cache.TryGetValue(staffIndex, out var hit))
                return hit;
            var line = staffByIndex.TryGetValue(staffIndex, out var staff)
                ? MultiStaffLayouter.AttachedChordLine(
                    score, measureLayouts, staffIndex, staff, sp)
                : null;
            cache[staffIndex] = line;
            return line;
        };
    }

    /// <summary>
    /// The room a lyric block between two staves of <paramref name="sysIdx"/> is solved into
    /// — the span from the anchor staff's reference point to the next spaceable staff's —
    /// and that staff's up-skyline, which is what the closing minimum measures against.
    /// </summary>
    /// <remarks>
    /// The anchor is the BOTTOM spaceable staff of the group holding
    /// <paramref name="anchorStaffIndex"/>, which is how <see cref="BuildStaffAnchorTables"/>
    /// picks the Y a non-last group's lyrics hang from; the closing staff is the first
    /// spaceable one below it, wherever in the system it lives.
    /// <para>
    /// ⚠️ SPACEABLE, the same set as everywhere else in this island: a hidden staff and a text
    /// ROW are not in the page's spring chain (<c>MultiStaffLayouter.StaffSprings</c>) and
    /// LilyPond never makes one a <c>last_spaceable_line</c>. ⚠️ AN OSSIA IS IN THAT SET since
    /// 2026-07-28 — it has no <c>staff-affinity</c> and is therefore spaceable
    /// (page-layout-problem.cc:1173-1177 <c>is_spaceable</c>).
    /// </para>
    /// <para>
    /// ⚠️ NULL WHEN NO SPACEABLE STAFF IS LEFT BELOW THE ANCHOR, AND THAT IS A DIVERGENCE
    /// RATHER THAN A DEFINITION — corrected here after the port's own commit message stated
    /// it as though LilyPond agreed. LilyPond has no such case: a block whose system runs out
    /// of staves is flushed at the NEXT system's first spaceable staff through the null line
    /// (page-layout-problem.cc:927-933), or at the foot of the page if there is none
    /// (:1004-1013). It always closes on something. Lily# leaves the chain at force 0
    /// instead. Reachable only where hara-kiri has killed every staff of every group below a
    /// non-last group that carries lyrics; no fixture and no ledger point reaches it, which
    /// is why it is named rather than implemented — a branch nothing measures is how a port
    /// acquires an untested one (the same judgement <see cref="BuildLooseChainEnds"/>'s
    /// coarse bail-out is left on).
    /// </para>
    /// <para>
    /// ⚠️ Null ALSO when a text row is STRICTLY INSIDE the span (the guard below),
    /// and that is the room being unknown rather than an exclusion (§5.2): LilyPond puts
    /// those INTO the chain as loose lines of their own, so a span that steps over one is
    /// somebody else's space. ⚠️ THIS IS THE HALF <see cref="BuildLooseChainEnds"/> NO LONGER
    /// SHARES: since 2026-07-27 that one takes a LEADING chords row into its chain instead of
    /// declining, because a row above the next system's first staff is exactly the run
    /// page-layout-problem.cc:948-990 collects. A row inside THIS span is the other case, and
    /// closing it is the same work — put the row in as an element — with its own point to
    /// measure it, which the corpus does not have yet.
    /// </para>
    /// <para>
    /// ★ THE ROOM AND THE ANCHOR ARE BOTH READ PER SYSTEM SINCE 2026-08-25, and the note
    /// this replaces is worth keeping in outline because it was wrong about its own reach.
    /// It said <see cref="BuildStaffAnchorTables"/>'s <c>NoteBoundAnchorY</c>, read off
    /// <c>systemsArray[0]</c>, disagrees with this per-system walk "only where hara-kiri
    /// leaves different staves alive on different systems AND the block hangs from a
    /// non-last group; no fixture and no ledger point reaches that". A CHORDS ROW REACHES IT
    /// WITH NO HARA-KIRI AT ALL: a row printed on one system and absent on another moves the
    /// staff beneath it by the row's whole band, and the anchor is a distance from the
    /// SYSTEM ORIGIN. MEASURED on the reported book (scratch/ベースタブLy/Untitled-6.lys with
    /// <c>lyrics verse sings melody</c>, user report 2026-08-25): the chain solved
    /// 0.000000 / 5.175000 / 7.975000 / 12.033515 and the syllables were drawn 1.895000 below
    /// every one of those. <c>LyricEngraver</c>'s <c>ResolveAnchor</c> reads the system it is
    /// asked about now, which is what LilyPond does (<c>-solution_[spring_idx]</c> is that
    /// system's staff), and falls back to the table only where the staff is not in that
    /// system at all.
    /// ⚠️ THE LESSON IS ABOUT THE EXCLUSION, NOT THE ANCHOR: "no fixture reaches it" is a
    /// statement about the fixtures, and it was carried for weeks as though it were a
    /// statement about the geometry (HANDOFF 5.3).
    /// </para>
    /// </remarks>
    private (double Room, VerticalSkyline NextStaffUp)? ComputeBetweenStavesEnd(
        int sysIdx, int anchorStaffIndex, ImmutableArray<SystemLayout> systems,
        IReadOnlyDictionary<int, Staff> staffByIndex,
        Func<int, int, VerticalSkyline?> staffUpSkyline)
    {
        if (sysIdx < 0 || sysIdx >= systems.Length) return null;
        var groups = systems[sysIdx].StaffGroups;
        if (groups.IsDefaultOrEmpty) return null;

        // Device-DOWN to each SPACEABLE staff's top line, and the anchor's own group; the
        // lines Lily# lays out as bands of their own — a chords or lyrics track — are
        // collected separately, because whether they matter depends on WHERE they are.
        // ⚠️ AN OSSIA IS NO LONGER ONE OF THEM (2026-07-28). It is spaceable
        // (page-layout-problem.cc:1173-1177 is_spaceable), so it CLOSES this span like any staff instead of
        // making the room unknown by standing in it.
        // ⚠️ THE ANCHOR IS THE STAFF ASKED FOR, not the bottom of the group holding it. It
        // used to be the group's deepest spaceable staff, which is the same per-group model
        // BuildStaffAnchorTables carried: on a grand staff every staff's block was measured
        // from the group's LAST staff, so the room returned belonged to a different pair
        // than the block was drawn in. The block hangs off its own staff, so the span starts
        // at its own staff.
        double? anchorDown = null;
        var below = new List<(double Down, int StaffIndex)>();
        foreach (var group in groups)
        {
            if (group.Staves.IsDefaultOrEmpty) continue;
            foreach (var st in group.Staves)
            {
                if (st.IsHidden) continue;
                double down = -st.Y;
                // A non-spaceable line neither bounds this span nor makes it unknown: it is
                // an OCCUPANT, and the chain solved into the span is what places it.
                if (!StaffAffinity.IsSpaceable(st.StaffAffinity)) continue;
                if (st.StaffIndex == anchorStaffIndex)
                    anchorDown = down;
                below.Add((down, st.StaffIndex));
            }
        }
        if (anchorDown is not { } anchor) return null;

        double? nextDown = null;
        int nextIndex = -1;
        foreach (var (down, staffIndex) in below)
        {
            if (down <= anchor) continue;
            if (nextDown is null || down < nextDown) { nextDown = down; nextIndex = staffIndex; }
        }
        if (nextDown is not { } next || !staffByIndex.ContainsKey(nextIndex))
            return null;

        // ⚠️ THE ROOM IS UNKNOWN ONLY IF ONE OF THOSE BANDS IS INSIDE IT. The span this
        // returns runs from the anchor staff's reference point down to the next spaceable
        // staff's, and a band ABOVE the anchor or BELOW that next staff is in somebody
        // else's span — LilyPond's own call site takes two spaceable positions and the loose
        // lines strictly between them (page-layout-problem.cc:936-939, :948-990). A band
        // that IS between them is a genuine disagreement: LilyPond makes it a loose line in
        // this very chain while Lily# gives it a band of its own (HANDOFF 3), so the chain
        // would be solved into space it does not own.
        // ⚠️ THIS USED TO BAIL ON THE WHOLE SYSTEM, and the remark on BuildLooseChainEnds
        // said why it was left coarse: nothing measured it, and narrowing an exclusion
        // nothing measures is how a port acquires an untested branch. The corpus measures it
        // now — lyrics.chord-row.* on book LYRCH, where LilyPond is the identity with LYRB.
        // ★ NO ROW IN THIS SPAN MAKES THE ROOM UNKNOWN ANY MORE. A LYRICS row stopped doing
        // so on 2026-08-25 and a CHORDS row on 2026-08-26: either is an ELEMENT of the very
        // chain this end closes — LayoutEngine.BuildBetweenRowStaves hands it to
        // LyricEngraver.BuildChainPrefix, which is what page-layout-problem.cc:919-925 does
        // with it — so declining here would be declining because the run has an occupant.
        // ⚠️ WHAT THE CHORDS ARM WAITED FOR WAS NOT A MEASUREMENT, AND THAT IS THE FINDING.
        // The remark this replaces said its steps "are get_spacing_spec's other branches
        // (:1280-1332)" and that closing it needed a corpus point measuring the affinity-DOWN
        // walk. Both branches were already ported — StaffAffinity.GetSpacingSpec is
        // :1266-1342 entire — and the only thing missing was that the chain called neither,
        // because it built its springs from two score-wide constants. The point exists now
        // (lyrics.chord-lyric-run.*, book CHL1) and it was written to CHECK the port rather
        // than to unlock it.

        // ⚠️ THE ROOM'S OWN LIST, NOT A SECOND BUILD — the same rule the anchor's DOWN
        // profile follows (see StaffDownSkyline's remark in LayoutLyrics). This up-skyline is
        // what CLOSES the chain, and the room the chain is solved into was measured by
        // MultiStaffLayouter against this staff's skyline with all its side tables: dynamics,
        // SCRIPTS, tuplet brackets, slurs, ties, beams. Rebuilding it here took every one of
        // them at its default, so the closing step saw a bare staff — a fermata on the lower
        // part reaches up into exactly this gap, and the syllable was solved into space the
        // mark was already occupying. The indent went with the rebuild for the clef's sake;
        // reading the list carries the clef and everything else by construction.
        var up = staffUpSkyline(sysIdx, nextIndex);
        return up is null ? null : (next - anchor, up);
    }

}
