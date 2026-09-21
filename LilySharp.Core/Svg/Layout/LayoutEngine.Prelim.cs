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
    /// What the preliminary pass produces: the skylines paging spaces against, and the beams
    /// it laid out on the way — which are the SAME beams the final spanner pass needs.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE BEAMS ARE CARRIED, NOT RECOMPUTED, and that is the point of this type. Both
    /// passes call <see cref="ElementCoordinator.LayoutBeams"/> per staff, and they build the
    /// Score they hand it the same way (all voices, this staff's tuplets); they differ only in
    /// which system array they get, and paging does not touch a system's internal geometry.
    /// MEASURED (session 136) before this was carried: 14,350 beams over 468 layouts of every
    /// .lys in the tree — Fixtures, samples and audit/lpreg, 564 staves — compared
    /// element-wise (LeftX/LeftY/RightX/RightY/member X positions/system/staff), 0 mismatches,
    /// with a poisoned control run proving the comparison reports when it should.
    /// The second layout cost 385.5 ms of a 2.1 s keystroke on perf-plain1k.
    /// ⚠️ THE ANNOTATION-CONTEXT BEAM GROUPS RIDE ALONG TOO (session 138): the final
    /// annotation context used to run its own <c>DetectBeamGroups(primaryScore)</c>, but the
    /// preliminary context's detection consumed the SAME voice/time-signature/tuplet
    /// instances, and the detector is a stateless pure function of exactly those three —
    /// see the carry site in <c>Layout</c> for the full argument.
    /// ⚠️ TIES AND SLURS ARE CARRIED TOO (session 141) AND RE-ANCHORED (session 424). Both
    /// passes lay them on the SAME quantity since session 140 (every voice — the
    /// staffSpannerScore spelling), but unlike a beam (whose Y is staff positions), a bow
    /// bakes its own staff's WITHIN-SYSTEM offset into its Y as an additive base
    /// (BuildTieSpecification's staffY, the slur scorer's staffMiddleDown) — and page
    /// justification can MOVE that offset, because the staff springs sit in the page
    /// chain. MEASURED (2026-08-12, every .lys in the tree, 1,243 books): the divergence
    /// is exactly that — 4,201 of 26,140 bows differed, all of them ties on staves below
    /// the first in multi-page books, every one a rigid Y shift equal to the offset
    /// delta, X bit-identical. Sessions 141-423 answered that with a GATE — carry only a
    /// staff no system moved, re-lay the whole staff otherwise; session 424 measured what
    /// the gate cost (0.594% of a keystroke, and 96.3% of the systems it re-laid had
    /// moved, so splitting it per system could have reached 0.005%) and pays the delta
    /// back instead. <see cref="BowLayout.ShiftedUp"/> is the payment and
    /// <c>LayoutEngine.BowShiftsOf</c> holds both measurements of why it is exact.
    /// ⚠️ <c>AllBeams</c> IS <c>BeamsByStaff</c> CONCATENATED in <c>EnumerateStaves</c> order
    /// (session 467) — the one copy the preliminary annotation context takes, carried so the
    /// final pass does not gather the same beams into a list of its own and copy that twice.
    /// ⚠️ <c>Systems</c> IS THE ARRAY THE PASS RAN ON (session 468) — the placement's system list
    /// copied once, which the page builder and the spanner pass read too. They used to copy the
    /// same unchanged list twice more (213 B each, once a keystroke). Nothing between the
    /// placement and the page writes to that list; the systems paging moves are new objects in
    /// the array <c>CreatePages</c> returns.
    /// </remarks>
    private readonly record struct PreliminaryPass(
        List<(VerticalSkyline up, VerticalSkyline down)>? PagingSkylines,
        Dictionary<int, ImmutableArray<BeamLayout>> BeamsByStaff,
        ImmutableArray<BeamLayout> AllBeams,
        ImmutableArray<BeamGroup> AnnotationBeamGroups,
        Dictionary<int, ImmutableArray<TieLayout>> TiesByStaff,
        Dictionary<int, ImmutableArray<SlurLayout>> SlursByStaff,
        ImmutableArray<SystemLayout> Systems);

    /// <summary>
    /// The PRELIMINARY annotation pass: lays the annotations out against provisional system
    /// positions purely so their real protrusions can join the spacing extents and skylines
    /// BEFORE the page Y is fixed, and returns the skylines paging should use.
    /// </summary>
    /// <remarks>
    /// Extracted verbatim from <c>Layout</c>'s body, where it already stood in a block of
    /// its own. ⚠️ It MUTATES <paramref name="perSystemExtents"/> — that is the point of the
    /// pass, and it is why the caller must run it before <c>CreatePages</c>.
    /// <para>
    /// ⚠️ The annotations computed here are THROWN AWAY; the final pass recomputes them
    /// against the paged systems. Only their extents survive, which is why a divergence
    /// between the two passes is invisible in the drawing and shows up as spacing — see the
    /// per-staff lookup comment inside, which records one that did exactly that.
    /// </para>
    /// </remarks>
    private PreliminaryPass RunPreliminaryAnnotationPass(
        MultiStaffScore score, MultiStaffLayouter layouter,
        ImmutableArray<SystemLayout> prelimSystems,
        List<(double upExtent, double downExtent)> perSystemExtents,
        List<(VerticalSkyline up, VerticalSkyline down)> perSystemSkylines,
        Func<Staff, ImmutableDictionary<RestShiftKey, double>> restCollisionsOf,
        SystemLayoutCache? systemCache, double commonShortestDuration,
        List<List<MultiStaffLayouter.StaffInsideSpanners>> staffSpanners,
        List<List<(VerticalSkyline Up, VerticalSkyline Down)>> staffInside,
        IReadOnlyList<double> rowsAboveFirstStaff,
        List<VerticalSkyline?>? lyricBands = null,
        List<List<ImmutableArray<PedalEngraver.SolvedPedalLine>>>? pedalLines = null)
    {
        var (prelimStaff, prelimStaffIndex) = score.PrimaryContentStaffWithIndex();
        var prelimScore = new Score(
            prelimStaff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            ClefToString(prelimStaff.Clef), score.Tempo, score.Title, score.Composer,
            tupletBrackets: score.TupletBrackets, header: score.Header)
        {
            TempoText = score.TempoText,
            TempoBeatUnit = score.TempoBeatUnit,
            TempoDots = score.TempoDots,
        };
        // Lent, and given back once the annotation context has copied it (see RentPrelimBeams).
        var prelimBeams = RentPrelimBeams();
        var prelimBeamsByStaff = new Dictionary<int, ImmutableArray<BeamLayout>>();
        var prelimTiesByStaff = new Dictionary<int, ImmutableArray<TieLayout>>();
        var prelimSlursByStaff = new Dictionary<int, ImmutableArray<SlurLayout>>();
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            var staffTuplets = StaffTuplets(score.TupletBrackets, staffIndex);
            var staffScore = new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: staffTuplets);
            // Beams live on the staff quantity — every voice (so voice 2's beam
            // protrusions join the spacing extents, matching the final pass), this
            // staff's tuplets only (a foreign staff's tuplet would split a beam at a
            // colliding note index) — whose one construction and one detection are
            // MultiStaffLayouter.StaffBeamScoreOf / StaffBeamGroupsOf.
            var staffBeamScore = MultiStaffLayouter.StaffBeamScoreOf(score, staff, staffIndex);
            // Ties/slurs live on the staff quantity too — the SAME score the final pass
            // lays its bows on, from the one construction both passes share
            // (StaffSpannerScoreOf; its remarks carry the measured account of what the
            // primary-voice-only prelim used to cost).
            var staffSpannerScore = StaffSpannerScoreOf(score, staff, staffTuplets, staffScore);
            // ...and the bows' ITEMS come from the one detection too, the way the beams'
            // groups do: the skylines detected this staff's slurs and ties already
            // (MultiStaffLayouter.StaffBowItemsOf, whose remark carries why the two passes'
            // different Score objects detect the same items). Until session 434 this pass
            // ran both detectors itself, once per staff on every keystroke, for an answer
            // the layouter was holding.
            var staffBows = layouter.StaffBowItemsOf(score, staff);
            var staffPrelimBeams = LayoutPreliminaryStaffBeams(
                staffBeamScore, layouter.StaffBeamGroupsOf(score, staff, staffIndex),
                prelimSystems, staffIndex, systemCache, commonShortestDuration);
            prelimBeamsByStaff[staffIndex] = staffPrelimBeams;
            prelimBeams.AddRange(staffPrelimBeams);
            var staffPrelimTies = LayoutPreliminaryStaffTies(
                score.TextMetrics, staffBows.Ties, staffSpannerScore, prelimSystems, staffIndex, staff,
                systemCache, commonShortestDuration);
            prelimTiesByStaff[staffIndex] = staffPrelimTies;
            // The same 'inside script boxes the FINAL pass scores its bows against
            // (LayoutAllSpanners) — a prelim bow that ignored them would shape the
            // spacing extents for a curve the final pass then moves.
            var prelimStaffScripts = ArticulationEngraver.SidePositionedScriptsOf(
                score.Articulations, staffIndex);
            var staffPrelimSlurs = LayoutPreliminaryStaffSlurs(
                score.TextMetrics, staffBows.Slurs, staffSpannerScore, prelimSystems, staffIndex, staff, score.GraceNotes,
                staffPrelimBeams, staffPrelimTies, prelimStaffScripts,
                systemCache, commonShortestDuration);
            prelimSlursByStaff[staffIndex] = staffPrelimSlurs;
        }
        // The SAME per-staff / per-voice lookups the final annotation pass gets. Without
        // them TupletBracketEngraver falls back to the PRIMARY staff's PRIMARY voice for
        // every tuplet, so a voice-two tuplet is positioned from voice one's notes and a
        // lower staff's tuplet from the top staff's — silently, because the FINAL pass
        // does have the lookups and draws the bracket correctly. Only the spacing pass
        // was wrong, which is exactly the kind of divergence a snapshot cannot see.
        // Caught by system.tuplet-bracket-down staying put while its mirror -up moved.
        var prelimVoicesByStaff = new Dictionary<int, ImmutableArray<Voice>>();
        var prelimMeasuresByStaff = new Dictionary<int, ImmutableArray<Measure>>();
        var prelimStaffByIndex = new Dictionary<int, Staff>();
        foreach (var (_, st, idx) in score.EnumerateStaves())
        {
            prelimVoicesByStaff[idx] = st.Voices;
            prelimMeasuresByStaff[idx] = st.PrimaryVoice.Measures;
            prelimStaffByIndex[idx] = st;
        }
        // Device-DOWN staff offsets, built the same way the final pass builds its own.
        // Read from the PRELIMINARY systems on purpose: a staff's offset INSIDE a system
        // is fixed by the staff layout and paging only moves the system's own Y, so this
        // is the same table — and it has to exist before paging, which is what needs it.
        var prelimStaffYByIndex = new Dictionary<int, double>();
        if (prelimSystems.Length > 0 && !prelimSystems[0].StaffGroups.IsDefaultOrEmpty)
            foreach (var sg in prelimSystems[0].StaffGroups)
                foreach (var st in sg.Staves)
                    prelimStaffYByIndex[st.StaffIndex] = -st.Y;

        // Detected ONCE for both annotation contexts — the final context carries this
        // value instead of re-detecting on primaryScore (see the carry site in Layout).
        // ⚠️ AND ONCE WITH THE STAFF QUANTITY WHEN THE TWO ARE THE SAME THREE INPUTS, which
        // is what routing through the layouter's input-keyed memo buys: on a single-voice,
        // single-staff score prelimScore's voice, meter and tuplet list are the primary
        // staff's, and this detection was a second full walk of the same music (MEASURED
        // session 192: 2.61 MB of a 56.7 MB perf-plain1k keystroke). The two remain separate
        // QUANTITIES — MultiStaffLayouter.BeamGroupsOf compares the inputs rather than
        // assuming when they agree, and on a multi-voice or multi-staff score they do not.
        // ⚠️ DETECTED ON ITS OWN STREAM'S TUPLETS, NOT THE SCORE'S. prelimScore carries the
        // WHOLE score's bracket list because the annotation pass DRAWS every bracket from it;
        // the beam detector, handed one voice, reads that list as if every bracket addressed
        // THAT voice's items — BuildTupletSpans' own remark says an in-range foreign bracket
        // still collides by index and that the hole "closes only when the probe filters by
        // staff/voice". MEASURED (session 193, ForeignTupletBracketTests): a triplet opening
        // at index 2 of the LOWER staff turns the upper staff's thirty-second beamlet round
        // (left/right 2/3 against 3/2), because the foreign span's start lands on that stem's
        // moment and flag_directions skips a stem at a span boundary.
        var annotationDetectionScore = DetectionScoreFor(
            prelimScore, prelimStaff, score, prelimStaffIndex);
        var annotationBeamGroups = layouter.BeamGroupsOf(annotationDetectionScore);
        // Every staff's beams in staff order — the context's copy, and the final pass's answer
        // too (see PreliminaryPass.AllBeams at LayoutAllSpanners, which used to gather it again).
        var allBeams = prelimBeams.ToImmutableArray();
        // Every staff's ties and slurs in staff order, ONCE, for the three readers below (the
        // context, the extents, the paging skylines). Each used to take a ToImmutableArray of
        // its own from a list the loop grew — MEASURED (session 468, Release, the reader's
        // corpus, 231 books × 8 forward keystrokes, 1.17 passes a keystroke): 578 B a keystroke
        // of copies nobody needed and 428 B of list growth, for arrays of 19.6 ties and 7.0 slurs.
        var allPrelimTies = ConcatInStaffOrder(score, prelimTiesByStaff);
        var allPrelimSlurs = ConcatInStaffOrder(score, prelimSlursByStaff);
        var prelimAnn = CalculateAnnotationLayouts(new AnnotationLayoutContext
        {
            Score = prelimScore,
            MultiScore = score,
            IsLeadSheet = score.IsLeadSheet,
            GridBarlineRowIndex = score.GridBarlineRowIndex,
            Fonts = score.TextMetrics,
            Systems = prelimSystems,
            Dynamics = score.Dynamics,
            Articulations = score.Articulations,
            GraceNotes = score.GraceNotes,
            Lyrics = score.Lyrics,
            MusicMarks = score.MusicMarks,
            CustomTexts = score.CustomTexts,
            VoltaBrackets = score.VoltaBrackets,
            TupletBrackets = score.TupletBrackets,
            Arpeggios = score.Arpeggios,
            Measures = prelimStaff.PrimaryVoice.Measures,
            FiguredBasses = score.FiguredBasses,
            ChordNames = score.ChordNames,
            PercentRepeats = score.PercentRepeats,
            TrillSpanners = score.TrillSpanners,
            BeamGroups = annotationBeamGroups,
            BeamLayouts = allBeams,
            TieLayouts = allPrelimTies,
            SlurLayouts = allPrelimSlurs,
            SystemSkylines = perSystemSkylines,
            TupletForceStemUp = prelimStaff.IsMultiVoice,
            StaffVoices = prelimStaff.Voices,
            VoicesByStaff = prelimVoicesByStaff,
            MeasuresByStaff = prelimMeasuresByStaff,
            StaffYByIndex = prelimStaffYByIndex,
            StaffByIndex = prelimStaffByIndex,
            // ⚠️ THE SAME TABLE THE FINAL PASS GETS, for the reason the remark above this
            // method gives: the annotations computed here are thrown away and only their
            // EXTENTS survive, so a table the two passes disagree about is invisible in the
            // drawing and comes out as spacing. A profile without the rest shift would leave
            // a dynamic's protrusion short by however far Rest_collision moved the rest.
            RestCollisionsOf = restCollisionsOf,
            // ⚠️ THE ROOM'S TABLES, for the same reason again — and until 2026-08-14 this
            // pass had neither, so its three profile consumers (the stacker's seed, the
            // figured-bass drop, the lower-staff chord row) fell back to a rebuild that
            // held NO spanners (SpannersOf returned empty), NO scripts and NO fingerings.
            // A mover the final pass pushes below a slur was reserved at the slur-free
            // height, so the page under-reserved exactly the ink the drawn pass clears
            // (net: PreliminaryPassSeedTests). The rebuild also walked the system's music
            // once per (system, staff) on every keystroke — the dominant term of the
            // keystroke annotation pass (session 158's stage split: StackAboveStaff
            // prelim floors 14.5/35.9/16.4 ms vs final's 3.8/13.6/5.5 on
            // plain/fingbeam/v2bow — the final pass was already reading these tables).
            StaffSpanners = staffSpanners,
            StaffInside = staffInside,
            PrefixTimeSignatureX = BuildPrefixTimeSignatureX(score, prelimSystems),
            PrefixMarkAnchorX = BuildPrefixMarkAnchorX(score, prelimSystems),
            LineStartBarlineX = BuildLineStartBarlineX(score, prelimSystems),
            // The PRELIMINARY pass's own above-stack memo (see the final pass's site).
            AboveStackMemo = systemCache?.PreliminaryAboveStack,
            // ...and its below-side mirror (finding 4-3).
            BelowStackMemo = systemCache?.PreliminaryBelowStack,
            // ...and its own fingering memo, one store per pass for the same reason: the
            // two passes memoize DIFFERENT systems every keystroke, so a shared store
            // would be overwritten twice per keystroke and never hit.
            FingScriptMemo = systemCache?.PreliminaryFingScripts,
            // The verse-skyline memo is deliberately SHARED with the final pass — its
            // values are X-only, which is what the per-pass stores above are not.
            VerseSkylines = systemCache?.PreliminaryVerseSkylines,
            LyricChains = systemCache?.PreliminaryLyricChains,
        });
        GivePrelimBeams(prelimBeams);
        EnrichExtentsWithAnnotationProtrusions(score.TextMetrics, perSystemExtents, prelimSystems,
            prelimAnn, allPrelimTies, allPrelimSlurs,
            rowsAboveFirstStaff, pedalLines);
        return new PreliminaryPass(
            AugmentSkylinesForPaging(
                score.TextMetrics,
                perSystemSkylines, prelimAnn.Articulations, prelimAnn.FiguredBasses,
                prelimAnn.VoltaBrackets, prelimSystems,
                prelimAnn.MusicMarks, prelimAnn.CustomTexts, prelimAnn.ChordNames,
                prelimAnn.Dynamics,
                prelimAnn.BarNumbers, prelimAnn.TupletBrackets, allPrelimSlurs,
                allPrelimTies, prelimAnn.TextSpanners,
                systemCache, lyricBands, pedalLines),
            prelimBeamsByStaff,
            allBeams,
            annotationBeamGroups,
            prelimTiesByStaff,
            prelimSlursByStaff,
            prelimSystems);
    }

    /// <summary>
    /// One staff-keyed table's arrays laid end to end in <see cref="MultiStaffScore.EnumerateStaves"/>
    /// order, into an array of exactly their total — the order the loops that fill these tables
    /// walk, so it is the order the lists they replaced were grown in.
    /// </summary>
    private static ImmutableArray<T> ConcatInStaffOrder<T>(
        MultiStaffScore score, Dictionary<int, ImmutableArray<T>> byStaff)
    {
        int count = 0;
        foreach (var (_, _, staffIndex) in score.EnumerateStaves())
            if (byStaff.TryGetValue(staffIndex, out var part))
                count += part.Length;
        if (count == 0)
            return ImmutableArray<T>.Empty;
        var all = new T[count];
        int at = 0;
        foreach (var (_, _, staffIndex) in score.EnumerateStaves())
            if (byStaff.TryGetValue(staffIndex, out var part))
            {
                part.CopyTo(all, at);
                at += part.Length;
            }
        return System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(all);
    }

    /// <summary>
    /// The list <see cref="RunPreliminaryAnnotationPass"/> gathers every staff's preliminary
    /// beams into for the annotation context, lent from one list the thread keeps between
    /// passes.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 1.17 passes a keystroke at 186.94 beams each (max 660), and all 2,170 lists
    /// built were unreachable by the time the render that built them returned — its one
    /// reader is the <c>ToImmutableArray()</c> in the context's initializer, which copies.
    /// The lists and their growth ladders were at least 1,850 B a keystroke (the census could
    /// not price 2,034 of its arrays exactly, so that is a lower bound).
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — a list given back dirty would open the next pass's annotation context
    /// with this score's beams, and their protrusions would join the next score's extents.
    /// There is no exit between the rent and the give. <see cref="List{T}.Clear"/> nulls the
    /// slots it drops, so the drawer pins no <see cref="BeamLayout"/>, only its capacity.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static List<BeamLayout>? t_prelimBeams;

    /// <summary>Takes the thread's preliminary beam list, or makes the thread's first.</summary>
    private static List<BeamLayout> RentPrelimBeams()
    {
        var list = t_prelimBeams;
        if (list is null)
            return new List<BeamLayout>();
        t_prelimBeams = null;
        return list;
    }

    /// <summary>Puts a finished pass's beam list back, emptied, with its capacity.</summary>
    private static void GivePrelimBeams(List<BeamLayout> list)
    {
        list.Clear();
        t_prelimBeams = list;
    }

    /// <summary>
    /// One staff's preliminary beams, reused per system through the
    /// <see cref="SystemLayoutCache"/>: on an edit, only the systems whose content changed
    /// re-run the quanter; every other system's beams come back from the memo. Without
    /// this, one keystroke re-scored every beam in the score (measured session 136:
    /// 2,030 quanter runs / 365 ms of a 1.6 s plain1k keystroke, linear in the measure
    /// count and independent of the edit position).
    /// </summary>
    /// <remarks>
    /// SOUNDNESS: a system's beams are a function of the cache's existing key — see
    /// <see cref="SystemLayoutCache.GetOrComputeStaffSystemBeams"/> for the coverage
    /// argument and why (staffIndex, systemIndex) join it. Three structural points here:
    /// <list type="bullet">
    /// <item>ONE DETECTION, EVERY CONSUMER: the groups arrive from the per-staff detection
    /// memo (<see cref="MultiStaffLayouter.StaffBeamGroupsOf"/>, shared with the skyline
    /// and tuplet consumers) and the same instances are both partitioned and handed to
    /// every layout call (the <c>precomputedGroups</c> parameter), so the partition can
    /// never disagree with what the layout lays out.</item>
    /// <item>CROSS-SYSTEM GROUPS DEFEAT THE MEMO for the whole staff: a group whose
    /// members span systems depends on a NEIGHBOUR system's content, which the per-system
    /// key does not cover — so the staff falls back to the unmemoized call (byte-identical
    /// to the old path). Groups touching a measure outside every system take the same
    /// fallback, conservatively.</item>
    /// <item>REASSEMBLY IS IN DETECTION ORDER, cursor-matched by each group's identity
    /// (voice, first member's measure, first member's item) — NOT per-system concatenation,
    /// which would reorder a polyphonic staff's beams (detection is voice-major) and the
    /// renderer draws in list order. A group the layout skipped (its guards) simply fails
    /// the identity match and advances nothing, exactly reproducing the unpartitioned
    /// call's output.</item>
    /// </list>
    /// The per-system sub-call sees a ONE-SYSTEM measure map, which resolves exactly the
    /// measures this partition's groups live in, so its per-group answers are the ones the
    /// full call computes.
    /// </remarks>
    private ImmutableArray<BeamLayout> LayoutPreliminaryStaffBeams(
        Score staffBeamScore, ImmutableArray<BeamGroup> groups,
        ImmutableArray<SystemLayout> prelimSystems, int staffIndex,
        SystemLayoutCache? systemCache, double commonShortestDuration)
    {
        if (systemCache is null || groups.IsEmpty || prelimSystems.Length == 0)
            return _elementCoordinator.LayoutBeams(staffBeamScore, prelimSystems, staffIndex, groups);

        // ⚠️ THE SHARED TABLE, NOT A HAND COPY. This walk was
        // SpannerBreakSubstitution.BuildMeasureToSystemMap's (:88-92) line for line, and it
        // ran ONCE PER STAFF although it is a function of prelimSystems alone — 3,732 builds
        // over 1,848 keystrokes (measured session 422). The shared table is keyed on this
        // very array, so the staves of one placement now share one build with every other
        // house that asks the same question of the same systems.
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(prelimSystems);

        // Which single system each group lives in; -1 = spans systems or reaches an
        // unmapped measure (either way: not memoizable).
        var groupSystem = new int[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            int home = -2;
            foreach (var m in groups[i].Members)
            {
                if (!measureToSystem.TryGetValue(
                        m.ResolveMeasureIndex(groups[i].MeasureIndex), out int k)
                    || (home != -2 && home != k))
                {
                    home = -1;
                    break;
                }
                home = k;
            }
            groupSystem[i] = home == -2 ? -1 : home;
            if (groupSystem[i] < 0)
                return _elementCoordinator.LayoutBeams(
                    staffBeamScore, prelimSystems, staffIndex, groups);
        }

        // Lent, and given back after the memo loop below — its last reader (see RentGroupsBySystem).
        var groupsBySystem = RentGroupsBySystem();
        for (int i = 0; i < groups.Length; i++)
        {
            if (!groupsBySystem.TryGetValue(groupSystem[i], out var list))
                groupsBySystem[groupSystem[i]] = list = new List<BeamGroup>();
            list.Add(groups[i]);
        }

        // One entry per occupied system — the loop below writes exactly one, so the size is
        // the bucketing's own Count (measured before it was handed over: 3,732 calls,
        // asked == Count every time).
        var perSystem = new Dictionary<int, ImmutableArray<BeamLayout>>(groupsBySystem.Count);
        foreach (var (k, sysGroups) in groupsBySystem)
        {
            var sys = prelimSystems[k];
            // The scalar key is exactly the one this system's measure layouts were
            // memoized under (LayoutSystems): its measure range, edge flags, ITS indent
            // (SystemLayout.Indent carries the first/short choice) and the score-wide
            // common shortest duration.
            perSystem[k] = systemCache.GetOrComputeStaffSystemBeams(
                staffIndex, sys.SystemIndex,
                sys.Measures[0].MeasureIndex, sys.Measures.Length,
                isFirstSystem: k == 0, isLastSystem: k == prelimSystems.Length - 1,
                sys.Indent, commonShortestDuration,
                (Coordinator: _elementCoordinator, Score: staffBeamScore, Sys: sys,
                    StaffIndex: staffIndex, Groups: sysGroups),
                static s => s.Coordinator.LayoutBeams(
                    s.Score, ImmutableArray.Create(s.Sys), s.StaffIndex,
                    s.Groups.ToImmutableArray()));
        }
        GiveGroupsBySystem(groupsBySystem);

        // A cursor per system the reassembly actually reads — bounded by the laid systems,
        // and measured exact on the reader's corpus (3,732 calls, asked == Count every one).
        var cursors = new Dictionary<int, int>(perSystem.Count);
        var result = RentBeamLayoutBuilder();
        for (int i = 0; i < groups.Length; i++)
        {
            int k = groupSystem[i];
            var laid = perSystem[k];
            int c = cursors.GetValueOrDefault(k);
            if (c < laid.Length && SameBeamGroupIdentity(laid[c].Group, groups[i]))
            {
                result.Add(laid[c]);
                cursors[k] = c + 1;
            }
        }
        // ToImmutable COPIES (see the drawer's remark), so the builder is finished with here.
        var reassembled = result.ToImmutable();
        GiveBeamLayoutBuilder(result);
        return reassembled;
    }

    /// <summary>
    /// The system → groups map <see cref="LayoutPreliminaryStaffBeams"/> partitions a staff's
    /// beam groups into, lent from one map the thread keeps between staves.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 2.02 partitions a keystroke at 19.29 systems each (max 45), and all 3,732 maps
    /// built were unreachable by the time the render that built them returned. The maps and
    /// their growth ladders were 3,237 B a keystroke, 0.09% of it. The per-system LISTS are
    /// not parked — only the map; clearing it drops them. Each list is read by the memo's
    /// compute lambda, which captures the list and not the map, and which
    /// <see cref="SystemLayoutCache.GetOrComputeStaffSystemBeams"/> runs before it returns.
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — a map given back dirty would hand the next staff this staff's list
    /// under every system number the two share, and the next staff would lay out this staff's
    /// groups beside its own. There is no early return and no throw between the rent and the
    /// give. Reuse keeps the walk order: a cleared <see cref="Dictionary{TKey, TValue}"/>
    /// fills its entries from the front again, so the memo loop visits the systems in
    /// insertion order exactly as a new map would.
    /// </para>
    /// <para>
    /// WHAT IT RETAINS is one map a thread at that thread's longest staff — 45 systems,
    /// emptied, so it pins no beam group.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static Dictionary<int, List<BeamGroup>>? t_groupsBySystem;

    /// <summary>Takes the thread's system → groups map, or makes the thread's first.</summary>
    private static Dictionary<int, List<BeamGroup>> RentGroupsBySystem()
    {
        var map = t_groupsBySystem;
        if (map is null)
            return new Dictionary<int, List<BeamGroup>>();
        t_groupsBySystem = null;
        return map;
    }

    /// <summary>Puts a finished partition's map back, emptied, with its capacity.</summary>
    private static void GiveGroupsBySystem(Dictionary<int, List<BeamGroup>> map)
    {
        map.Clear();
        t_groupsBySystem = map;
    }

    /// <summary>
    /// The builder <see cref="LayoutPreliminaryStaffBeams"/> reassembles a staff's beam
    /// layouts into, lent from one builder the thread keeps between reassemblies.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 2.02 reassemblies a keystroke at 108.7 layouts each (max 330), and all 3,732
    /// builders built were unreachable by the time the render that built them returned — the
    /// reassembly copies into an <see cref="ImmutableArray{T}"/> and the builder dies. The
    /// builders and their growth ladders were 5,388 B a keystroke, 0.14% of it.
    /// <para>
    /// The size is NOT known ahead of the loop, which is why this is a parked builder and not
    /// an exact-sized one: the loop adds only the groups whose identity matches the cursor's
    /// laid group, so <c>groups.Length</c> is an upper bound the reassembly is not obliged to
    /// reach (it is the "a group the layout skipped advances nothing" case in the remark on
    /// <see cref="LayoutPreliminaryStaffBeams"/>).
    /// </para>
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — a builder parked dirty would open the next staff's beams with this
    /// staff's, and the renderer draws in list order, so the suite sees it. There is no early
    /// return and no throw between the rent and the give; the memo calls and the per-system
    /// sub-layouts all happen BEFORE the rent. <c>ToImmutable</c> copies, measured
    /// (session 459) — see <see cref="LedgerLineSpannerEngraver"/>'s drawer for the probe.
    /// </para>
    /// <para>
    /// WHAT IT RETAINS is one builder a thread at that thread's busiest staff — 330 layouts,
    /// emptied, so it pins no beam group.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static ImmutableArray<BeamLayout>.Builder? t_beamLayoutBuilder;

    /// <summary>Takes the thread's beam-layout builder, or makes the thread's first.</summary>
    private static ImmutableArray<BeamLayout>.Builder RentBeamLayoutBuilder()
    {
        var builder = t_beamLayoutBuilder;
        if (builder is null)
            return ImmutableArray.CreateBuilder<BeamLayout>();
        t_beamLayoutBuilder = null;
        return builder;
    }

    /// <summary>Puts a finished reassembly's builder back, emptied, with its capacity.</summary>
    private static void GiveBeamLayoutBuilder(ImmutableArray<BeamLayout>.Builder builder)
    {
        builder.Clear();
        t_beamLayoutBuilder = builder;
    }

    /// <summary>
    /// Whether two detections name the SAME beam group: same voice, same first stem
    /// (its measure and item). Unique per staff — no two groups share a first stem.
    /// Compared by content, not reference, because a memo hit returns layouts whose
    /// <see cref="BeamLayout.Group"/> came from a PREVIOUS edit's detection of the same
    /// (unchanged) music. An intra-system multi-measure piece's group anchors at its
    /// first member's measure (see <c>LayoutCrossMeasureBeamPieces</c>), so the resolved
    /// first-member comparison holds for it too.
    /// </summary>
    private static bool SameBeamGroupIdentity(BeamGroup a, BeamGroup b)
        => a.VoiceIndex == b.VoiceIndex
           && a.Members[0].ResolveMeasureIndex(a.MeasureIndex)
              == b.Members[0].ResolveMeasureIndex(b.MeasureIndex)
           && a.Members[0].ItemIndex == b.Members[0].ItemIndex;

    /// <summary>The measure→system map of the preliminary systems — the home test both
    /// bow memos and the beam memo ask before memoizing per system.</summary>
    /// <remarks>
    /// ⚠️ THE SHARED TABLE, for the reason the beam memo's own call gives. This was a hand
    /// copy of it, built twice per staff (ties, then slurs) over the very array the beam
    /// memo had already keyed — MEASURED (session 466, the owner's 231 books × 8 forward
    /// keystrokes): 4,984 builds, 4,952 of them over an array the shared table already held.
    /// </remarks>
    private static IReadOnlyDictionary<int, int> MeasureToSystemOf(ImmutableArray<SystemLayout> systems)
        => SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

    /// <summary>
    /// The preliminary pass's per-(staff, system) TIE memo — the tie twin of
    /// <see cref="LayoutPreliminaryStaffBeams"/> (2026-08-26 review, finding 4-2: the
    /// prelim bows were the one per-system quantity still recomputed for EVERY system
    /// on every keystroke while the beams beside them hit their memo). Detection runs
    /// once (it is a whole-score walk either way); the SOLVE — the per-column
    /// TieFormattingProblem — is memoized per system through
    /// <see cref="SystemLayoutCache.GetOrComputeStaffSystemTies"/>.
    /// <para>
    /// ⚠️ THE TIES ARRIVE DETECTED, from the layouter's per-staff memo
    /// (<c>MultiStaffLayouter.StaffBowItemsOf</c>) — an explicit parameter and not an
    /// optional one, the posture <see cref="ElementCoordinator"/>'s pre-detected overloads
    /// argue for (§7.7: a defaulted one is how a whole island came to run with its side
    /// tables at default). "Once" above used to mean once per staff per PASS; since
    /// session 434 it means once per staff.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Fallback (whole staff, unmemoized — the beams' posture) when any tie COLUMN is
    /// not wholly inside one system: a straddling column is laid out per SEGMENT with
    /// its neighbour system in the inputs, which this key does not cover. The
    /// reassembly is column-major in detection order — exactly the order the plain
    /// call emits (its own remark: "emitted tie-major") — and cursor-verified against
    /// the column's identity, so a drift reorders nothing silently: an unmatched
    /// column falls back to the plain call for the whole staff.
    /// </remarks>
    private ImmutableArray<TieLayout> LayoutPreliminaryStaffTies(
        Rendering.ScoreTextMetrics fonts, ImmutableArray<TieItem> ties,
        Score staffSpannerScore, ImmutableArray<SystemLayout> prelimSystems, int staffIndex,
        Staff staff, SystemLayoutCache? systemCache, double commonShortestDuration)
    {
        if (systemCache is null || prelimSystems.Length == 0)
            return _elementCoordinator.LayoutTies(
                fonts, ties, staffSpannerScore, prelimSystems, staffIndex, staff);
        if (ties.IsEmpty)
            return ImmutableArray<TieLayout>.Empty;

        ImmutableArray<TieLayout> Fallback() => _elementCoordinator.LayoutTies(
            fonts, ties, staffSpannerScore, prelimSystems, staffIndex, staff);

        var measureToSystem = MeasureToSystemOf(prelimSystems);

        // Columns in detection first-appearance order — the same bucketing the plain
        // call performs ((voice, start measure, start item) names ONE chord's ties).
        // Lent, and given back at every exit below (see RentColumnTies). ⚠️ THE MAP IS ALSO
        // THE KEY LIST: walking it yields the columns in first-appearance order (its remark
        // says why), which is the order a separate list of keys used to carry.
        var columnTies = RentColumnTies();
        foreach (var tie in ties)
        {
            var key = (tie.VoiceIndex, tie.StartMeasureIndex, tie.StartItemIndex);
            if (!columnTies.TryGetValue(key, out var list))
            {
                // Capacity 1, from OBSERVATION and not from an argument: a column CAN hold a
                // whole chord's ties, but the price instrument read 1.00 ties a column (max
                // 2) over the reader's corpus, so the default four slots were three wasted.
                // The poison run tests it (a capacity is not a correctness property).
                columnTies[key] = list = new List<TieItem>(1);
            }
            list.Add(tie);
        }

        // Home system per column; any straddler (or unmapped measure) → fallback.
        // One entry per column key — the loop writes exactly one and there are no repeats
        // (the keys came out of a dictionary), so the size is columnTies.Count and not a
        // bound (measured before it was handed over: 2,620 calls, asked == Count every time).
        var columnSystem = new Dictionary<(int, int, int), int>(columnTies.Count);
        foreach (var (key, columnList) in columnTies)
        {
            int home = -2;
            foreach (var tie in columnList)
            {
                if (!measureToSystem.TryGetValue(tie.StartMeasureIndex, out int ks)
                    || !measureToSystem.TryGetValue(tie.EndMeasureIndex, out int ke)
                    || ks != ke || (home != -2 && home != ks))
                {
                    GiveColumnTies(columnTies);
                    return Fallback();
                }
                home = ks;
            }
            columnSystem[key] = home;
        }

        // One memoized solve per system, over that system's ties in original order.
        var tiesBySystem = new Dictionary<int, List<TieItem>>();
        foreach (var tie in ties)
        {
            int k = columnSystem[(tie.VoiceIndex, tie.StartMeasureIndex, tie.StartItemIndex)];
            if (!tiesBySystem.TryGetValue(k, out var list))
                tiesBySystem[k] = list = new List<TieItem>();
            list.Add(tie);
        }
        // One entry per occupied system, as on the beam side above (2,620 calls, exact).
        var perSystem = new Dictionary<int, ImmutableArray<TieLayout>>(tiesBySystem.Count);
        foreach (var (k, sysTies) in tiesBySystem)
        {
            var sys = prelimSystems[k];
            perSystem[k] = systemCache.GetOrComputeStaffSystemTies(
                staffIndex, sys.SystemIndex,
                sys.Measures[0].MeasureIndex, sys.Measures.Length,
                isFirstSystem: k == 0, isLastSystem: k == prelimSystems.Length - 1,
                sys.Indent, commonShortestDuration,
                (Coordinator: _elementCoordinator, Fonts: fonts, Ties: sysTies,
                    Score: staffSpannerScore, Sys: sys, StaffIndex: staffIndex, Staff: staff),
                static s => s.Coordinator.LayoutTies(
                    s.Fonts, s.Ties.ToImmutableArray(), s.Score,
                    ImmutableArray.Create(s.Sys), s.StaffIndex, s.Staff));
        }

        // Column-major reassembly in detection order, one layout per tie (an
        // intra-system column has exactly one segment).
        var cursors = new Dictionary<int, int>(perSystem.Count);
        var result = ImmutableArray.CreateBuilder<TieLayout>(ties.Length);
        foreach (var (key, columnList) in columnTies)
        {
            int k = columnSystem[key];
            var laid = perSystem[k];
            int c = cursors.GetValueOrDefault(k);
            int count = columnList.Count;
            if (c + count > laid.Length
                || laid[c].Tie.VoiceIndex != key.Voice
                || laid[c].Tie.StartMeasureIndex != key.Measure
                || laid[c].Tie.StartItemIndex != key.Item)
            {
                GiveColumnTies(columnTies);
                return Fallback(); // structural drift — never guess, recompute whole
            }
            for (int i = 0; i < count; i++)
                result.Add(laid[c + i]);
            cursors[k] = c + count;
        }
        GiveColumnTies(columnTies);
        return result.ToImmutable();
    }

    /// <summary>
    /// The column → ties map <see cref="LayoutPreliminaryStaffTies"/> buckets a staff's ties
    /// into, lent from one map the thread keeps between staves.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 1.69 bucketings a keystroke at 22.06 columns each (max 142), and all 3,132
    /// maps built were unreachable by the time the render that built them returned. The maps
    /// and their growth ladders were 3,831 B a keystroke, 0.10% of it. The per-column LISTS
    /// are not parked — only the map; clearing it drops them.
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — a map given back dirty would find a previous staff's list under a
    /// column key this staff shares and append to it. ⚠️ THREE EXITS, three gives: the two
    /// fallbacks return early, and the map is not read after either decision.
    /// </para>
    /// <para>
    /// ⚠️ SINCE SESSION 462 THE MAP IS WALKED, AND ITS ORDER IS THE COLUMNS' ORDER: a list of
    /// its keys in first-appearance order used to stand beside it (<c>columnKeys</c>, 1,716 B a
    /// keystroke in the same census, 1.69 lists at 22.06 keys), and the map already held that
    /// order. A cleared <see cref="Dictionary{TKey, TValue}"/> that is only ever added to
    /// fills its entries from the front and enumerates them in insertion order — the reason
    /// <see cref="LayoutPreliminaryStaffBeams"/>' own drawer gives for its walk. ⚠️ THE ORDER
    /// IS LOAD-BEARING, AND ONLY HALF GUARDED: the reassembly checks each column against its
    /// system's cursor, so two columns of ONE system swapped fall back to the whole-staff call
    /// (a solve, not a picture) — but each system has its own cursor, so columns of DIFFERENT
    /// systems swapped pass the check and come out in the swapped order.
    /// </para>
    /// <para>
    /// WHAT IT RETAINS is one map a thread at that thread's tie-richest staff — 142 columns,
    /// emptied, so it pins no tie.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static Dictionary<(int Voice, int Measure, int Item), List<TieItem>>? t_columnTies;

    /// <summary>Takes the thread's column map, or makes the thread's first.</summary>
    private static Dictionary<(int Voice, int Measure, int Item), List<TieItem>> RentColumnTies()
    {
        var map = t_columnTies;
        if (map is null)
            return new Dictionary<(int Voice, int Measure, int Item), List<TieItem>>();
        t_columnTies = null;
        return map;
    }

    /// <summary>Puts a finished bucketing's column map back, emptied, with its capacity.</summary>
    private static void GiveColumnTies(Dictionary<(int Voice, int Measure, int Item), List<TieItem>> map)
    {
        map.Clear();
        t_columnTies = map;
    }

    /// <summary>The inside-slur script factory a slur solve takes — null when the staff has
    /// no scripts. Static, so the closure is built only when a solve asks for it: as a local
    /// function capturing the method's parameters it made the whole method's environment,
    /// which every call paid at entry, memo hit or not (session 469).</summary>
    private static Func<ImmutableArray<InsideSlurScript>>? InsideScriptFactory(
        Rendering.ScoreTextMetrics fonts, Score staffSpannerScore, int staffIndex, Staff staff,
        ImmutableArray<ArticulationItem> staffScripts, ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beams, ImmutableArray<TieLayout> tieLayouts)
        => staffScripts.IsEmpty ? null : () =>
            ArticulationEngraver.InsideSlurScriptLayouts(
                fonts, staffSpannerScore, staffScripts,
                systems.SelectMany(s => s.Measures).ToImmutableArray(),
                measuresByStaff: new Dictionary<int, ImmutableArray<Measure>>
                    { [staffIndex] = staff.PrimaryVoice.Measures },
                staffYAt: null,
                staffByIndex: new Dictionary<int, Staff> { [staffIndex] = staff },
                beamLayouts: beams,
                tieLayouts: tieLayouts);

    /// <summary>
    /// ONE system's slur solve for <see cref="LayoutPreliminaryStaffSlurs"/>' memo — the
    /// inputs as a value, so the memo is handed a static lambda.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SYSTEM'S BEAMS AND TIES ARE FILTERED HERE, INSIDE THE SOLVE: only the solve
    /// reads them, and they used to be filtered (two <c>Where</c>s and two copies) before the
    /// memo was asked, so every HIT paid for a solve it did not run (session 469).
    /// </remarks>
    private readonly record struct SystemSlurSolve(
        ElementCoordinator Coordinator, Rendering.ScoreTextMetrics Fonts, List<SlurItem> Slurs,
        Score Score, SystemLayout Sys, int SystemKey, int StaffIndex, Staff Staff,
        ImmutableArray<GraceNoteItem> GraceNotes, ImmutableArray<BeamLayout> StaffBeams,
        ImmutableArray<TieLayout> StaffTies, ImmutableArray<ArticulationItem> StaffScripts,
        IReadOnlyDictionary<int, int> MeasureToSystem)
    {
        public ImmutableArray<SlurLayout> Solve()
        {
            var single = ImmutableArray.Create(Sys);
            int systemIndex = Sys.SystemIndex;
            var sysBeams = StaffBeams.IsDefaultOrEmpty
                ? StaffBeams
                : StaffBeams.Where(b => b.SystemIndex == systemIndex).ToImmutableArray();
            var measureToSystem = MeasureToSystem;
            int k = SystemKey;
            var sysTies = StaffTies.IsDefaultOrEmpty
                ? StaffTies
                : StaffTies.Where(t =>
                        measureToSystem.TryGetValue(t.Tie.StartMeasureIndex, out int tk)
                        && tk == k)
                    .ToImmutableArray();
            return Coordinator.LayoutSlurs(
                Fonts, Slurs.ToImmutableArray(), Score, single, StaffIndex,
                Staff, GraceNotes, sysBeams,
                InsideScriptFactory(Fonts, Score, StaffIndex, Staff, StaffScripts,
                    single, sysBeams, sysTies));
        }
    }

    /// <summary>
    /// The preliminary pass's per-(staff, system) SLUR memo — see
    /// <see cref="LayoutPreliminaryStaffTies"/>; same posture, slur-shaped inputs. The
    /// per-system compute hands the solve only ITS system's beams and ties (both stamp
    /// their system) and an inside-script factory over ITS measures, so the memo key's
    /// coverage claim covers what the value read.
    /// </summary>
    private ImmutableArray<SlurLayout> LayoutPreliminaryStaffSlurs(
        Rendering.ScoreTextMetrics fonts, ImmutableArray<SlurItem> slurs,
        Score staffSpannerScore, ImmutableArray<SystemLayout> prelimSystems, int staffIndex,
        Staff staff, ImmutableArray<GraceNoteItem> graceNotes,
        ImmutableArray<BeamLayout> staffBeams, ImmutableArray<TieLayout> staffTies,
        ImmutableArray<ArticulationItem> staffScripts,
        SystemLayoutCache? systemCache, double commonShortestDuration)
    {
        if (systemCache is null || prelimSystems.Length == 0)
            return _elementCoordinator.LayoutSlurs(
                fonts, slurs, staffSpannerScore, prelimSystems, staffIndex, staff, graceNotes,
                staffBeams, InsideScriptFactory(fonts, staffSpannerScore, staffIndex, staff,
                    staffScripts, prelimSystems, staffBeams, staffTies));
        if (slurs.IsEmpty)
            return ImmutableArray<SlurLayout>.Empty;

        ImmutableArray<SlurLayout> Fallback() => _elementCoordinator.LayoutSlurs(
            fonts, slurs, staffSpannerScore, prelimSystems, staffIndex, staff, graceNotes,
            staffBeams, InsideScriptFactory(fonts, staffSpannerScore, staffIndex, staff,
                staffScripts, prelimSystems, staffBeams, staffTies));

        var measureToSystem = MeasureToSystemOf(prelimSystems);
        var slurSystem = new int[slurs.Length];
        for (int i = 0; i < slurs.Length; i++)
        {
            if (!measureToSystem.TryGetValue(slurs[i].StartMeasureIndex, out int ks)
                || !measureToSystem.TryGetValue(slurs[i].EndMeasureIndex, out int ke)
                || ks != ke)
                return Fallback();
            slurSystem[i] = ks;
        }

        var slursBySystem = new Dictionary<int, List<SlurItem>>();
        for (int i = 0; i < slurs.Length; i++)
        {
            if (!slursBySystem.TryGetValue(slurSystem[i], out var list))
                slursBySystem[slurSystem[i]] = list = new List<SlurItem>();
            list.Add(slurs[i]);
        }
        var perSystem = new Dictionary<int, ImmutableArray<SlurLayout>>();
        foreach (var (k, sysSlurs) in slursBySystem)
        {
            var sys = prelimSystems[k];
            perSystem[k] = systemCache.GetOrComputeStaffSystemSlurs(
                staffIndex, sys.SystemIndex,
                sys.Measures[0].MeasureIndex, sys.Measures.Length,
                isFirstSystem: k == 0, isLastSystem: k == prelimSystems.Length - 1,
                sys.Indent, commonShortestDuration,
                new SystemSlurSolve(_elementCoordinator, fonts, sysSlurs, staffSpannerScore,
                    sys, k, staffIndex, staff, graceNotes, staffBeams, staffTies, staffScripts,
                    measureToSystem),
                static s => s.Solve());
        }

        // Reassembly in detection order. A slur emits AT MOST one layout for its one
        // segment (a tab slur can emit none), so the cursor advances by identity match
        // and a mismatch means this slur emitted nothing.
        var cursors = new Dictionary<int, int>();
        var result = ImmutableArray.CreateBuilder<SlurLayout>(slurs.Length);
        for (int i = 0; i < slurs.Length; i++)
        {
            int k = slurSystem[i];
            var laid = perSystem[k];
            int c = cursors.GetValueOrDefault(k);
            if (c < laid.Length && ReferenceOrIdentityMatch(laid[c].Slur, slurs[i]))
            {
                result.Add(laid[c]);
                cursors[k] = c + 1;
            }
        }
        // Every produced layout must have been claimed — an unclaimed one means the
        // identity match drifted; never guess.
        foreach (var (k, laid) in perSystem)
            if (cursors.GetValueOrDefault(k) != laid.Length)
                return Fallback();
        return result.ToImmutable();

        static bool ReferenceOrIdentityMatch(SlurItem a, SlurItem b)
            => ReferenceEquals(a, b)
               || (a.VoiceIndex == b.VoiceIndex
                   && a.StartMeasureIndex == b.StartMeasureIndex
                   && a.StartItemIndex == b.StartItemIndex
                   && a.EndMeasureIndex == b.EndMeasureIndex
                   && a.EndItemIndex == b.EndItemIndex);
    }

    /// <summary>
    /// The per-staff lookups and the anchor Ys the annotation engravers position against.
    /// </summary>
    /// <remarks>
    /// Extracted verbatim from <c>Layout</c>'s body. Everything here is read from the FIRST
    /// laid-out system on purpose: a staff's offset INSIDE a system is fixed by the staff
    /// layout, and paging only moves the system's own Y. ⚠️ Hara-kiri can hide different
    /// staves per system, which this does not model — a simplification all four tables carry.
    /// </remarks>
    private static StaffAnchorTables BuildStaffAnchorTables(
        MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray)
    {
        // Per-staff lookups so a dynamic is positioned under its OWN staff (clears
        // that staff's stems) and offset to it — score-level dynamics otherwise all
        // collapse onto the first staff.
        var voicesByStaff = new Dictionary<int, ImmutableArray<Voice>>();
        var measuresByStaff = new Dictionary<int, ImmutableArray<Measure>>();
        var staffByIndex = new Dictionary<int, Staff>();
        foreach (var (g, st, idx) in score.EnumerateStaves())
        {
            voicesByStaff[idx] = st.Voices;
            measuresByStaff[idx] = st.PrimaryVoice.Measures;
            staffByIndex[idx] = st;
        }
        // Device-DOWN offsets: the annotation engravers downstream (grace notes,
        // lyrics, ottava, chord names) all measure downward from the system top, so
        // this table reflects staff.Y's Y-up storage once, here at the island edge.
        var staffYByIndex = new Dictionary<int, double>();
        if (systemsArray.Length > 0 && !systemsArray[0].StaffGroups.IsDefaultOrEmpty)
            foreach (var sg in systemsArray[0].StaffGroups)
                foreach (var st in sg.Staves)
                    staffYByIndex[st.StaffIndex] = -st.Y;

        // Anchor Y for note-bound lyrics: THE STAFF THEY ARE ATTACHED TO, device-down from
        // the system origin to that staff's top line (Y-up reflected, like staffYByIndex
        // above). Every spaceable staff is here except the LAST one, whose lyrics have
        // nothing below them to sit above and so keep the below-the-whole-system placement
        // that `lastSpaceableStaffY` anchors (they are the `-1` family in LyricEngraver).
        // LILYPOND-REF: lily/page-layout-problem.cc:919-925 loose_lines — "lay out any
        // non-spaceable lines between this line and the last one": the run a Lyrics context
        // belongs to runs between the spaceable line above it and the next spaceable line,
        // and nothing in that walk knows about groups.
        // ⚠️ IT USED TO BE PER-GROUP, and that was this port's own invention rather than
        // LilyPond's: a lyric attached to any staff of a group anchored on the group's
        // BOTTOM staff, and a group that was the last one was left out entirely. On a
        // one-group score — which is what a grand staff is — that put EVERY staff's lyrics
        // in the same place, so an SATB chorale with a line per voice drew all four on one
        // baseline, on top of each other (MEASURED on scratch/…/がくふ.lys: four rows, one
        // Y). The old shape is kept nowhere; a lyric hangs off its own staff, which is the
        // only rule LilyPond has.
        // ⚠️ SPACEABLE, AND BY DEPTH, NOT BY ORDER, because that is how the staff the `-1`
        // family hangs from is chosen (lastSpaceableStaffY below) and the two have to name
        // the same staff or one block is anchored twice. Spaceable is the staff's own
        // `staff-affinity` and nothing else, so an ossia — declaring none — counts as the
        // staff it is, and a text ROW (a chord or lyrics track) does not count at all: a row
        // carries no staff spring and LilyPond never makes one a `last_spaceable_line`.
        var noteBoundAnchorY = new Dictionary<int, double>();
        if (systemsArray.Length > 0 && !systemsArray[0].StaffGroups.IsDefaultOrEmpty)
        {
            var spaceable = new List<StaffLayout>();
            foreach (var group in systemsArray[0].StaffGroups)
            {
                if (group.Staves.IsDefaultOrEmpty) continue;
                foreach (var st in group.Staves)
                    if (!st.IsHidden && StaffAffinity.IsSpaceable(st.StaffAffinity))
                        spaceable.Add(st);
            }
            if (spaceable.Count > 1)
            {
                double deepest = spaceable.Max(s => -s.Y);
                foreach (var st in spaceable)
                    if (-st.Y < deepest)
                        noteBoundAnchorY[st.StaffIndex] = -st.Y;
            }
        }

        // ...and the anchor for everything else note-bound: the system's LAST SPACEABLE
        // staff, device-down from the system origin to its top line.
        //
        // LILYPOND-REF: lily/page-layout-problem.cc:943-944 — find_system_offsets records
        // each spaceable staff as `last_spaceable_line`, and a loose line below it is
        // spaced from THAT (get_spacing_spec, :1284-1294, takes the affinity-UP branch
        // against the staff above the line). Lily# used to measure the block from the
        // SYSTEM ORIGIN — the TOP staff's top line — which is the same place only when the
        // system has one staff. With two it is a staff away, and the basic-distance 5.5
        // stopped binding entirely: MEASURED at 4.009200 against LilyPond's 5.500000
        // (audit/lp-geometry, lyrics.two-staff.staff-to-lyric), and that 4.009200 is purely
        // the ink floor, 2.050000 + the syllable's 1.459200 + padding 0.500000.
        //
        // ⚠️ SPACEABLE, which is what excludes a text ROW (a chord or lyrics track): a row
        // carries no staff spring (MultiStaffLayouter.StaffSprings) and LilyPond never makes
        // one a `last_spaceable_line`. A lead sheet's chord row sits ABOVE the staff, so
        // taking the bottom-most staff blindly would anchor on the wrong thing on exactly the
        // scores that have lyrics.
        // LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 Page_layout_problem::is_spaceable
        // — the staff's own `staff-affinity`, which is why an ossia (declaring none) is NOT
        // excluded and an ossia BELOW a staff is that system's `last_spaceable_line`, its
        // lyrics hanging from IT. ⚠️ MEASURED: moving this predicate alone changes nothing in
        // the corpus, because every ossia book here puts the ossia ABOVE. That is a regime and
        // not a proof (HANDOFF 5.3) — the reason it moves with the rest is that one predicate
        // spelled several ways is one defect (HANDOFF 5.2.1②).
        // One spelling with the per-system selection the lyric chain makes
        // (LyricEngraver.LastSpaceableStaffOf) — hara-kiri can hide a different staff on
        // every system, so the block's anchor is per-system there, and this table keeps
        // only what its remaining readers want: system 0's anchor Y.
        double lastSpaceableStaffY = systemsArray.Length > 0
            ? LyricEngraver.LastSpaceableStaffOf(systemsArray[0])?.DeviceDown ?? 0
            : 0;

        return new StaffAnchorTables(
            voicesByStaff, measuresByStaff, staffByIndex, staffYByIndex,
            noteBoundAnchorY, lastSpaceableStaffY);
    }

    /// <summary>
    /// The per-staff lookups and anchor Ys <see cref="BuildStaffAnchorTables"/> produces.
    /// </summary>
    private readonly record struct StaffAnchorTables(
        Dictionary<int, ImmutableArray<Voice>> VoicesByStaff,
        Dictionary<int, ImmutableArray<Measure>> MeasuresByStaff,
        Dictionary<int, Staff> StaffByIndex,
        Dictionary<int, double> StaffYByIndex,
        Dictionary<int, double> NoteBoundAnchorY,
        double LastSpaceableStaffY);

    /// <summary>
    /// Lays out beams, ties, slurs and glissandos for every staff (each detected
    /// per voice, so a polyphonic staff exposes all its voices). Extracted verbatim
    /// from the multi-staff <c>Layout</c> body.
    /// </summary>
    /// <param name="allBeams">Every staff's beams in <c>EnumerateStaves</c> order, which the
    /// preliminary pass gathered for its own annotation context
    /// (<see cref="PreliminaryPass.AllBeams"/>) and which IS this pass's answer: the loop below
    /// walks the same staves in the same order and takes <paramref name="beamsByStaff"/>'s entry
    /// for each — the prelim pass's beams, carried (see <see cref="PreliminaryPass"/>). So it is
    /// returned rather than gathered a second time. MEASURED (session 457's census, Release, the
    /// reader's corpus, eight forward keystrokes a book): the second gathering was a list of
    /// 184.67 beams (max 660) once a keystroke, at least 1,547 B with its growth ladder, and
    /// the caller then copied it twice.</param>
    private (ImmutableArray<BeamLayout> Beams, ImmutableArray<TieLayout> Ties, ImmutableArray<SlurLayout> Slurs,
             List<GlissandoLayout> Glissandos, ImmutableDictionary<RestShiftKey, double> RestShifts)
        LayoutAllSpanners(MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray,
            Func<Staff, ImmutableDictionary<RestShiftKey, double>> restCollisionsOf,
            Dictionary<int, ImmutableArray<BeamLayout>> beamsByStaff,
            ImmutableArray<BeamLayout> allBeams,
            Dictionary<int, ImmutableArray<TieLayout>> prelimTiesByStaff,
            Dictionary<int, ImmutableArray<SlurLayout>> prelimSlursByStaff,
            ImmutableArray<SystemLayout> prelimSystems)
    {
        // Lent, and given back cleared once copied below (see t_finalTies).
        var allTieLayouts = t_finalTies ?? new List<TieLayout>();
        t_finalTies = null;
        var allSlurLayouts = t_finalSlurs ?? new List<SlurLayout>();
        t_finalSlurs = null;
        var allGlissandoLayouts = new List<GlissandoLayout>();
        // Rest shifts are keyed by (measure, item) only, so they are computed for
        // each staff's PRIMARY voice — enough for the single-voice scores where
        // beamed rests occur in practice. A polyphonic beamed rest in a
        // non-primary voice, or the same (measure, item) slot across staves, would
        // share one entry; this matches the granularity the codebase already
        // accepts for HeadWipe/DotForceDown (VoiceItemKey, no staff axis).
        var restShiftsBuilder = ImmutableDictionary.CreateBuilder<RestShiftKey, double>();
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            // Beam detection reads tuplet brackets by (measure, note index), so scope
            // the tuplets to THIS staff — a tuplet on another staff must not attach
            // itself to this staff's beams at a colliding index.
            var staffTuplets = StaffTuplets(score.TupletBrackets, staffIndex);
            var staffScore = new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                // Beam detection must see tuplet spans: they clamp beamlets at span
                // boundaries and rank stems in written proportions (BeamDetector).
                tupletBrackets: staffTuplets);
            // Beam AND slur/tie/glissando detection run PER VOICE, so a polyphonic
            // staff must expose all its voices (not just the primary) — else voice 2's
            // eighths never beam. One construction shared with the preliminary pass
            // (StaffSpannerScoreOf), so the two passes cannot drift apart again.
            var staffSpannerScore = StaffSpannerScoreOf(score, staff, staffTuplets, staffScore);
            // ...the beams the PRELIMINARY pass already laid out for this staff, not a second
            // layout of them. See PreliminaryPass' remarks for why they are the same beams and
            // what was measured to say so. Indexed, not TryGetValue'd with a recompute
            // fallback: both loops walk score.EnumerateStaves(), so a missing key is a broken
            // invariant and should say so rather than quietly pay 385 ms.
            var staffFinalBeams = beamsByStaff[staffIndex];
            // The rest movers CHAIN in LilyPond's callback order: the voiced position and
            // Rest_collision translate first, and Beam::rest_collision_callback evaluates
            // the rest's ink WHERE THEY PUT IT, adding its push on top (beam.cc:1388-1390
            // reads prev_offset; :1414 returns offset + shift). A beamed rest's entry is
            // therefore the chained TOTAL and REPLACES the collision entry (SetItems) —
            // merging larger-wins instead kept a voiced +4 over the chained +4−2 and the
            // beam push never landed (dot-rest-beam-trigger.ly is the pin).
            // ⚠️ THE COLLISION TABLE COMES THROUGH THE ROOM'S MEMO, NOT A SECOND CALL.
            // This used to call ElementCoordinator.CalculateRestNoteCollisions directly,
            // while MultiStaffLayouter.BuildAllStaffSkylines asked the same question of the
            // same Staff through RestCollisionsOf — so every layout ran that WHOLE-SCORE
            // scan twice for every polyphonic staff, and an edit pays it on each keystroke.
            // The answer is a function of the Staff alone (see CalculateRestNoteCollisions'
            // remark), which is what makes the memo sound and made the duplicate invisible.
            var staffCollisionShifts = restCollisionsOf(staff);
            var staffRestShifts = _elementCoordinator.CalculateRestShifts(
                staffScore, systemsArray, staffFinalBeams,
                staffCollisionShifts);
            foreach (var kv in staffCollisionShifts.SetItems(staffRestShifts))
                if (!restShiftsBuilder.TryGetValue(kv.Key, out var existing)
                    || Math.Abs(kv.Value) > Math.Abs(existing))
                    restShiftsBuilder[kv.Key] = kv.Value;
            // ...and the ties/slurs too (since session 141), CARRIED AND RE-ANCHORED. A bow
            // bakes its OWN staff's within-system offset into its Y as an additive base (the
            // scorers feed StaffOffsetInSystemDown), and page justification can move that
            // offset, because the staff springs sit in the page chain — so a carried bow has
            // to be moved to where paging put its staff. BowShiftsOf holds the measurement
            // that says the move is exactly that and nothing else; a staff whose systems
            // paging left alone gets zero shifts and its own instances back, unchanged.
            var bowShifts = BowShiftsOf(prelimSystems, systemsArray, staffIndex);
            var bowSystemOf = SpannerBreakSubstitution.BuildMeasureToSystemMap(systemsArray);
            var staffTies = bowShifts is null
                ? default
                : ReanchorBows(prelimTiesByStaff[staffIndex], bowShifts, bowSystemOf);
            if (staffTies.IsDefault)
                staffTies = _elementCoordinator.LayoutTies(score.TextMetrics, staffSpannerScore, systemsArray, staffIndex, staff);
            allTieLayouts.AddRange(staffTies);
            var staffSlurs = bowShifts is null
                ? default
                : ReanchorBows(prelimSlursByStaff[staffIndex], bowShifts, bowSystemOf);
            if (staffSlurs.IsDefault)
            {
                // The bow is scored around this staff's avoid-slur #'inside marks, so they
                // have to be PLACED before it — the ordering half of that rule; the mark's
                // own placement is slur-free, which is what makes the order legal (see
                // ArticulationEngraver.InsideSlurScriptLayouts). Passed as a factory so a
                // slur-free staff never runs the walk.
                var staffScripts = ArticulationEngraver.SidePositionedScriptsOf(
                    score.Articulations, staffIndex);
                staffSlurs = _elementCoordinator.LayoutSlurs(
                    score.TextMetrics, staffSpannerScore, systemsArray, staffIndex, staff, score.GraceNotes, staffFinalBeams,
                    insideScripts: staffScripts.IsEmpty ? null : () =>
                        ArticulationEngraver.InsideSlurScriptLayouts(
                            score.TextMetrics, staffSpannerScore, staffScripts,
                            systemsArray.SelectMany(s => s.Measures).ToImmutableArray(),
                            measuresByStaff: new Dictionary<int, ImmutableArray<Measure>>
                                { [staffIndex] = staff.PrimaryVoice.Measures },
                            staffYAt: null,
                            staffByIndex: new Dictionary<int, Staff> { [staffIndex] = staff },
                            beamLayouts: staffFinalBeams,
                            tieLayouts: staffTies));
            }
            allSlurLayouts.AddRange(staffSlurs);
            allGlissandoLayouts.AddRange(_elementCoordinator.LayoutGlissandos(staffSpannerScore, systemsArray, staffIndex));
        }
        var ties = allTieLayouts.ToImmutableArray();
        var slurs = allSlurLayouts.ToImmutableArray();
        allTieLayouts.Clear();
        t_finalTies = allTieLayouts;
        allSlurLayouts.Clear();
        t_finalSlurs = allSlurLayouts;
        return (allBeams, ties, slurs, allGlissandoLayouts, restShiftsBuilder.ToImmutable());
    }

    /// <summary>
    /// The list <see cref="LayoutAllSpanners"/> gathers every staff's final ties into, lent from
    /// one list the thread keeps between layouts; <see cref="t_finalSlurs"/> is its slur twin.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 468, Release, the reader's corpus, 231 books × 8 forward keystrokes, once
    /// a keystroke): the two lists grew 363 B a keystroke for 19.6 ties and 7.1 slurs, and the
    /// caller then copied each twice — once for the final annotation context, once for the
    /// <see cref="ScoreLayout"/> — 245 B for a copy nobody needed. The pass now copies each ONCE
    /// and both readers take that array.
    /// <para>
    /// Same idiom as <see cref="t_prelimBeams"/>: renting takes the list out of the drawer and the
    /// clearing is on give, so a list given back dirty cannot open the next layout with this
    /// score's bows. An exception between the two loses the list, and the next layout builds one.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static List<TieLayout>? t_finalTies;

    /// <summary>The slur twin of <see cref="t_finalTies"/>.</summary>
    [ThreadStatic]
    private static List<SlurLayout>? t_finalSlurs;

    /// <summary>
    /// How far, in the page Y-UP frame, each system's copy of ONE staff moved between the
    /// preliminary systems and the paged ones — what a bow carried from the preliminary pass
    /// has to be moved by to stand where the final pass would have scored it. Empty when
    /// nothing moved (carry the bows unchanged); null when the two passes do not have the
    /// same systems at all, which no shift can repair.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE MOVE IS RIGID, AND THAT IS A MEASUREMENT, NOT AN ARGUMENT. Page justification
    /// stretches the staff springs (they sit in the page chain), so a staff below the first
    /// can move WITHIN its system when the page is solved; a bow bakes that offset into its Y
    /// as an additive base (<see cref="LayoutUtilities.StaffOffsetInSystemDown"/>, fed to
    /// <c>BuildTieSpecification</c>'s staffY and the slur scorer's staffMiddleDown), and every
    /// other input the scorers read is either untouched by paging or derived from that same
    /// base — so translating the base translates the answer.
    /// <para>
    /// MEASURED 2026-08-12 (every .lys in the tree, 1,243 books, 26,140 bows): re-anchoring
    /// closed all but 87 to the bit and left those 87 ONE ULP (~9e-16) away. MEASURED AGAIN
    /// 2026-09-19 at this HEAD, on the staves that actually move (a reader's corpus, 231 books
    /// × 8 keystrokes, 5,552 bows): 5,463 bit-identical, 89 one ulp, NONE worse, and every X
    /// and every direction bit-identical.
    /// </para>
    /// <para>
    /// ⚠️ THIS REPLACED A GATE, and the gate is why the number above matters. Sessions 141 to
    /// 423 carried a staff's bows only when EVERY system's offset was unmoved and re-laid the
    /// whole staff otherwise. Measured at this HEAD: that gate rejected 1,336 of its 1,376
    /// calls on staves below the first and the re-layout cost 0.594% of a keystroke — and of
    /// the 32,707 systems inside a rejection only 1,223, 3.7%, had stood still, so splitting
    /// the gate per system (the repair this repository had ticketed) could have reached
    /// 0.005%. Re-anchoring retires the whole 0.594% instead.
    /// </para>
    /// <para>
    /// LILYSHARP-OWN: the shift itself. LilyPond has no counterpart, because it has no second
    /// pass to carry from — it breaks a spanner AFTER solving it, while Lily# scores bows once
    /// for the spacing extents and again for the page. It goes away the day a bow is scored in
    /// its STAFF'S OWN frame with the offset added at the draw site — the shape DrawSlurs
    /// already has for <c>SystemLayout.Y</c> — because then the two passes agree bit-for-bit
    /// and there is nothing to pay back. Observed by: the page hashes above, which is what
    /// says the one-ulp tail never reaches a page.
    /// </para>
    /// <para>
    /// ⚠️ Staff 0's offset is 0 on every system — measured, never once rejected by the old
    /// gate — so its shifts are all zero and its bows come back as the SAME instances.
    /// </para>
    /// </remarks>
    private static double[]? BowShiftsOf(
        ImmutableArray<SystemLayout> prelimSystems, ImmutableArray<SystemLayout> finalSystems,
        int staffIndex)
    {
        if (prelimSystems.Length != finalSystems.Length)
            return null;
        // Two passes so the common case — a staff paging left alone — allocates nothing.
        bool moved = false;
        for (int s = 0; s < finalSystems.Length && !moved; s++)
            moved = LayoutUtilities.StaffOffsetInSystemDown(finalSystems[s], staffIndex)
                != LayoutUtilities.StaffOffsetInSystemDown(prelimSystems[s], staffIndex);
        if (!moved)
            return [];
        var shifts = new double[finalSystems.Length];
        for (int s = 0; s < finalSystems.Length; s++)
            // Stored bow Y is page Y-UP while the offset is device-DOWN, hence the order.
            shifts[s] = LayoutUtilities.StaffOffsetInSystemDown(prelimSystems[s], staffIndex)
                - LayoutUtilities.StaffOffsetInSystemDown(finalSystems[s], staffIndex);
        return shifts;
    }

    /// <summary>
    /// The carried bows, each moved by ITS OWN system's shift (a bow's geometry reads the
    /// system it was broken onto and no other).
    /// </summary>
    /// <remarks>
    /// Returns <c>default</c> — the caller's signal to re-lay the staff, the posture the old
    /// gate had for every moved staff — when a bow names a measure no system claims, so a
    /// drift is never papered over with a guess.
    /// </remarks>
    private static ImmutableArray<T> ReanchorBows<T>(
        ImmutableArray<T> carried, double[] shifts,
        IReadOnlyDictionary<int, int> systemOfMeasure)
        where T : BowLayout
    {
        if (shifts.Length == 0 || carried.IsDefaultOrEmpty)
            return carried;
        var moved = ImmutableArray.CreateBuilder<T>(carried.Length);
        foreach (var bow in carried)
        {
            if (bow.RenderMeasureIndex < 0
                || !systemOfMeasure.TryGetValue(bow.RenderMeasureIndex, out int sys)
                || (uint)sys >= (uint)shifts.Length)
                return default;
            double dy = shifts[sys];
            moved.Add(dy == 0 ? bow : (T)bow.ShiftedUp(dy));
        }
        return moved.MoveToImmutable();
    }

    // F3/S5-3a: route a system's measure layout through the session cache when one
    // is installed (single-staff incremental path). Null cache => direct compute,
    // byte-identical to the non-incremental path.
    private static ImmutableArray<MeasureLayout> ComputeSystemMeasures<TState>(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration,
        TState state, Func<TState, ImmutableArray<MeasureLayout>> compute)
        => cache == null
            ? compute(state)
            : cache.GetOrComputeMeasures(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
                indent, commonShortestDuration, state, compute);

}
