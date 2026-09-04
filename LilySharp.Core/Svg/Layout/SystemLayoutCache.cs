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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// F3 / S5-3: a session-scoped memo of per-system layout. On an edit, a system
/// whose content is unchanged reuses its cached results instead of recomputing
/// them; only the systems containing edited measures recompute
/// (the F3 incremental design notes §6, §19.5). Two phases are memoized — the
/// per-system spring solve (<see cref="GetOrComputeMeasures"/>) and the per-system
/// skyline build (<see cref="GetOrComputeSkyline"/>) — which the phase breakdown
/// showed are the two dominant per-system costs (the skyline is the larger of the
/// two on multi-staff scores).
/// </summary>
/// <remarks>
/// <para>
/// SOUNDNESS: a cached entry is reused only when the FULL set of inputs matches
/// exactly — the system's ordered per-measure <see cref="MeasureContentKey"/> slice
/// plus every scalar the computation depends on (firstMeasureIndex, count,
/// isFirst/isLast, indent, common shortest duration; and for the skyline also the
/// system height). Lookups VERIFY the key exactly (a hash bucket holds a short list,
/// compared element-wise), so a hash collision degrades to a recompute, never a wrong
/// reuse. Because the stored value is exactly what a fresh computation would produce,
/// GEOMETRY stays byte-identical — proven by the IncrementalCompiler incremental==full
/// harness.
/// </para>
/// <para>
/// ★ firstMeasureIndex IS A STAMP, NOT AN INPUT, since session 330. Nothing a system's
/// layout READS depends on where in the score the system sits — the content slice folds
/// every measure, side-table bucket and entry context, the edge flags and indent are
/// keyed beside it — but the VALUES carry the absolute measure numbers of the edit that
/// computed them (<see cref="MeasureLayout.MeasureIndex"/>, a bow's
/// <c>StartMeasureIndex</c>, a beam group's <c>MeasureIndex</c>), and the renderer and
/// the annotation pass read those to find the live measure. So an entry whose key
/// matches in everything BUT firstMeasureIndex is the same geometry under other numbers:
/// a lookup that misses exactly takes such an entry and RE-STAMPS its value by the
/// difference (<see cref="TypedCache{T}"/>'s shift function, one per store, saying where
/// that store's stamps live). MEASURED (session 329, a 3-page bass book, one bar
/// inserted at bar 4): every system after the insertion missed on firstMeasureIndex
/// alone — 70–95 ms of a 200 ms keystroke against 20–35 for the same bar added at the
/// end. A store whose value carries no stamp at all (a skyline pair) shifts by identity.
/// ⚠️ The stamps a shift does NOT touch are named at each shift function; a stamp that
/// is unreliable on an EXACT hit already (a source offset, a side-table index) is left
/// exactly as unreliable, not made to look repaired.
/// </para>
/// <para>
/// ⚠️ NOT SOURCE OFFSETS. <see cref="MeasureContentKey"/> is blind to where the text sits
/// — it must be, or a trivia insertion would move every key and the memo would never hit —
/// so an entry served here carries the <c>data-pos</c> of the edit that COMPUTED it, not of
/// the edit being rendered. That is sound only because the renderer re-derives every
/// annotation's source offset on every session render
/// (<c>SharedRenderer.ResolveDataPos</c>, unconditional on the IncrementalCompiler path).
/// ★ The claim above is about geometry ALONE; session 190 is what happens when it is read
/// as a claim about the output: three chained keystrokes on a fingered book froze the
/// carried-over system's data-pos while the picture stayed byte-identical, and every net
/// in the suite edited only ONCE, so none of them could see it.
/// </para>
/// <para>
/// The dictionaries persist across edits (that is what enables reuse); the
/// content-key vector is refreshed each edit via <see cref="SetContentKeys"/>.
/// </para>
/// </remarks>
internal sealed class SystemLayoutCache
{
    private ImmutableArray<MeasureContentKey> _keys;
    // One shift function per store: where that store's absolute measure stamps live
    // (see the class remarks). `Unstamped` is the identity — the value carries none.
    // ...and, for the three per-(staff, system) stores, a second one for the SYSTEM
    // stamp: a bar inserted with a `break` before a system moves its number by one
    // while its measures and its music stay. Only a beam carries that number
    // (BeamLayout.SystemIndex); a tie or slur is drawn in the staff's within-system
    // frame and carries none, so its shift is the identity.
    private readonly TypedCache<ImmutableArray<MeasureLayout>> _measures = new(ShiftMeasures);
    private readonly TypedCache<(VerticalSkyline up, VerticalSkyline down)> _skylines = new(Unstamped);
    private readonly TypedCache<MultiStaffLayouter.StaffSkylineSet> _staffSkylines = new(ShiftStaffSkylines);
    private readonly TypedCache<ImmutableArray<BeamLayout>> _staffSystemBeams = new(ShiftBeams, ShiftBeamSystems);
    private readonly TypedCache<ImmutableArray<TieLayout>> _staffSystemTies = new(ShiftTies, Unstamped);
    private readonly TypedCache<ImmutableArray<SlurLayout>> _staffSystemSlurs = new(ShiftSlurs, Unstamped);
    private readonly TypedCache<LayoutEngine.LooseBlockProfiles> _lyricBands = new(Unstamped);
    private readonly TypedCache<IReadOnlyList<MultiStaffLayouter.PairLooseLine>?> _looseLines = new(Unstamped);

    /// <summary>The memo stores, for <see cref="PassCounters"/>.</summary>
    public enum Store
    {
        Measures, Skylines, StaffSkylines, StaffSystemBeams, StaffSystemTies, StaffSystemSlurs,
        LyricBands, LooseLines,
    }

    /// <summary>How one store paid for the CURRENT pass's lookups (since the last
    /// <see cref="SetContentKeys"/>): exact hits, hits served by re-stamping an entry
    /// found under another firstMeasureIndex, and misses (computed). For diagnostics /
    /// tests — the liveness counter of the shifted-hit nets.</summary>
    public MemoCounters PassCounters(Store store) => store switch
    {
        Store.Measures => _measures.Pass,
        Store.Skylines => _skylines.Pass,
        Store.StaffSkylines => _staffSkylines.Pass,
        Store.StaffSystemBeams => _staffSystemBeams.Pass,
        Store.StaffSystemTies => _staffSystemTies.Pass,
        Store.StaffSystemSlurs => _staffSystemSlurs.Pass,
        Store.LyricBands => _lyricBands.Pass,
        Store.LooseLines => _looseLines.Pass,
        _ => throw new ArgumentOutOfRangeException(nameof(store)),
    };

    /// <summary>The sum of <see cref="PassCounters"/> over every store.</summary>
    public MemoCounters PassCountersTotal =>
        _measures.Pass + _skylines.Pass + _staffSkylines.Pass + _staffSystemBeams.Pass
        + _staffSystemTies.Pass + _staffSystemSlurs.Pass + _lyricBands.Pass + _looseLines.Pass;

    /// <summary>Refreshes the per-measure content keys for the current edit. Must be
    /// called before the layout consults the cache. Also marks the edit boundary for
    /// eviction: entries inserted or hit from here on belong to the new pass and are
    /// exempt from eviction until the next boundary.</summary>
    public void SetContentKeys(ImmutableArray<MeasureContentKey> keys)
    {
        _keys = keys;
        _measures.NextGeneration();
        _skylines.NextGeneration();
        _staffSkylines.NextGeneration();
        _staffSystemBeams.NextGeneration();
        _staffSystemTies.NextGeneration();
        _staffSystemSlurs.NextGeneration();
        _lyricBands.NextGeneration();
        _looseLines.NextGeneration();
    }

    /// <summary>Number of currently cached system measure-layout entries (diagnostics / tests).</summary>
    public int Count => _measures.Count;

    /// <summary>The above-staff stacking memo of the PRELIMINARY annotation pass. One
    /// instance per pass — the two passes stack different systems every keystroke, so a
    /// shared store would overwrite itself twice per keystroke and never hit. Entries
    /// persist across edits by design (a match means the inputs are value-identical, so
    /// staleness cannot serve a wrong answer — see <see cref="AboveStackMemo"/>); the
    /// store is bounded by the session's widest system count, like the paging augments.</summary>
    public AboveStackMemo PreliminaryAboveStack { get; } = new();

    /// <summary>The FINAL annotation pass's above-staff stacking memo
    /// (see <see cref="PreliminaryAboveStack"/>).</summary>
    public AboveStackMemo FinalAboveStack { get; } = new();

    /// <summary>The line-break DP's row-prefix resume (finding 4-5): the previous
    /// keystroke's whole table, so a gate-changing edit refills only the rows at
    /// and after the first changed spring. Lives here so it is shed with the rest
    /// of the session's geometry on a font/paper change, and so the full path
    /// (and override books, which run without a consulted cache) never sees it.
    /// See <see cref="LineBreakDpSession"/> for the recurrence inventory and the
    /// session-191 orthogonality note.</summary>
    public LineBreakDpSession LineBreakDp { get; } = new();

    /// <summary>The PRELIMINARY annotation pass's below-staff stacking memo — one
    /// instance per pass, for the reason <see cref="PreliminaryAboveStack"/> gives
    /// (finding 4-3: the below pass used to run every system live per keystroke).</summary>
    public BelowStackMemo PreliminaryBelowStack { get; } = new();

    /// <summary>The FINAL annotation pass's below-staff stacking memo
    /// (see <see cref="PreliminaryBelowStack"/>).</summary>
    public BelowStackMemo FinalBelowStack { get; } = new();

    /// <summary>The PRELIMINARY annotation pass's per-(staff, system) fingering memo —
    /// one instance per pass for the reason <see cref="PreliminaryAboveStack"/> gives.
    /// See <see cref="FingScriptMemo"/>.</summary>
    public FingScriptMemo PreliminaryFingScripts { get; } = new();

    /// <summary>The FINAL annotation pass's fingering memo
    /// (see <see cref="PreliminaryFingScripts"/>).</summary>
    public FingScriptMemo FinalFingScripts { get; } = new();

    /// <summary>The PRELIMINARY annotation pass's per-system lyric verse-skyline memo.
    /// </summary>
    /// <remarks>
    /// ★ ONE STORE PER PASS SINCE 2026-08-25, and it was ONE SHARED STORE before that on
    /// the reading that "a verse skyline is X-only — it reads nothing the passes disagree
    /// about". It reads one thing they do: the ALIGNMENT LINE a syllable is filed under.
    /// <c>LyricEngraver.DistributeLooseLines</c> buckets the profiles by
    /// <c>LineKeyOf</c>, which answers the staff index for a note-bound block hanging off
    /// a non-last staff and -1 otherwise — and that question is decided by
    /// <c>noteBoundAnchorY</c>, which the PRELIMINARY pass does not have yet. So the
    /// preliminary pass filed a sung score's profiles under -1, the final pass asked for
    /// them under the staff index, found nothing, and walked a chain with NO SYLLABLE INK
    /// in it.
    /// <para>
    /// MEASURED on the reported book (scratch/ベースタブLy/Untitled-6.lys, user report
    /// 2026-08-25): with a <see cref="SystemLayoutCache"/> in play the syllables landed
    /// 4.214000 higher than a full compile put them — through the staff above them on the
    /// one-verse systems. It reached only the EDITOR: the preview renders through
    /// <c>IncrementalCompiler</c>, which is the only caller that passes a cache, so
    /// <c>lysc</c> and every test that renders through <c>SvgGenerator.Generate</c> saw
    /// the correct picture and the preview did not.
    /// </para>
    /// </remarks>
    public VerseSkylineMemo PreliminaryVerseSkylines { get; } = new();

    /// <summary>The FINAL annotation pass's lyric verse-skyline memo
    /// (see <see cref="PreliminaryVerseSkylines"/>).</summary>
    public VerseSkylineMemo FinalVerseSkylines { get; } = new();

    /// <summary>The PRELIMINARY annotation pass's lyric chain-prefix memo — one store
    /// per pass, like <see cref="PreliminaryAboveStack"/> and for the same kind of
    /// reason: the walk's SEED reads the pass's anchor profile
    /// (the scripted system silhouette on the fallback path, the staff profile
    /// otherwise), and the two passes' profiles are not the same object nor always the
    /// same value — MEASURED, session 224: one shared store served the preliminary
    /// pass's walk to the final pass and two incremental==full nets went red with
    /// syllables 0.6-0.9 ss deep. See <see cref="LyricChainMemo"/>.</summary>
    public LyricChainMemo PreliminaryLyricChains { get; } = new();

    /// <summary>The FINAL annotation pass's lyric chain-prefix memo
    /// (see <see cref="PreliminaryLyricChains"/>).</summary>
    public LyricChainMemo FinalLyricChains { get; } = new();

    /// <summary>Whether the most recent <see cref="GetOrComputeMeasures"/> call was a
    /// hit (reused) rather than a miss (computed). For diagnostics / tests.</summary>
    public bool LastWasHit { get; private set; }

    /// <summary>Reuses or computes the system's spring-solved measure layouts.</summary>
    public ImmutableArray<MeasureLayout> GetOrComputeMeasures(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<ImmutableArray<MeasureLayout>> compute)
    {
        var result = _measures.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: 0, compute, out bool hit);
        LastWasHit = hit;
        return result;
    }

    /// <summary>Reuses or computes the system's PER-STAFF skylines — the list its staves
    /// are both placed and sprung against.</summary>
    /// <remarks>
    /// Keyed exactly like <see cref="GetOrComputeMeasures"/> and for the same reason: the
    /// staff skylines are a function of that system's measure layouts plus the score's
    /// side-tables, and every one of those inputs is already in this key.
    /// <list type="bullet">
    /// <item>the measure layouts themselves are the value under this same key;</item>
    /// <item><c>Dynamics</c>, <c>Articulations</c>, <c>ChordNames</c>, <c>TupletBrackets</c>
    /// and <c>GraceNotes</c> — the side tables <c>BuildAllStaffSkylines</c> reads — are
    /// folded per measure by <c>MeasureContentKey.BucketSideTables</c>;</item>
    /// <item>slurs, ties and beams are derived from the voices' own measures, which the
    /// intrinsic hash covers (secondary voices included);</item>
    /// <item>which staves exist and what they are is folded by <c>AddStaffIdentity</c>.</item>
    /// </list>
    /// ⚠️ NO <c>systemHeight</c> HERE, unlike <see cref="GetOrComputeSkyline"/>: a staff's
    /// skyline is built in that staff's own frame and does not know where the system's
    /// other staves ended up. Adding it would only make the key stricter, but stating why
    /// it is absent keeps the next reader from "fixing" the asymmetry.
    /// ⚠️ THE VALUE ALSO CARRIES THE INSIDE-STAFF SPANNERS the skylines were built from
    /// (<c>MultiStaffLayouter.StaffInsideSpanners</c>), and the key needs nothing added for
    /// them: they are the slurs, ties and tuplet brackets already named in the list above,
    /// which is why they can ride here instead of being laid out a second time.
    /// ⚠️ THE CACHED LIST IS SHARED, so nothing downstream may mutate it or the skylines in
    /// it. Verified 2026-07-27: every consumer goes through
    /// <c>CalculateStaffGapWithSkylines</c> / <c>AlignmentMinimumWithSkylines</c>, which
    /// only read (<c>Distance</c>, <c>IsEmpty</c>, <c>Count</c>). The one mutation,
    /// <c>ReserveChordRowBand</c>, happens during construction, before the value is stored.
    /// </remarks>
    public MultiStaffLayouter.StaffSkylineSet GetOrComputeStaffSkylines(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<MultiStaffLayouter.StaffSkylineSet> compute)
        => _staffSkylines.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: 0, compute, out _);

    /// <summary>Hits and misses of <see cref="GetOrComputeLyricBand"/> over this cache's
    /// lifetime (diagnostics / tests) — what lets a net assert the memo actually served
    /// rather than silently recomputing forever.</summary>
    public (int Hits, int Misses) LyricBandStats { get; private set; }

    /// <summary>Reuses or computes the system's below-system lyric reservation band
    /// (<c>LayoutEngine.LyricReservationBelowSystem</c>) — the loose lyric block's minimum
    /// profile, session 224's largest single per-keystroke recompute on sung books
    /// (135 MB of a 347 MB keystroke on perf-lyrplain1k, measured by region).</summary>
    /// <remarks>
    /// Keyed exactly like <see cref="GetOrComputeStaffSkylines"/>, and the coverage claim
    /// has the same shape — every input is either a value under this same key or folded
    /// into it:
    /// <list type="bullet">
    /// <item>the system's measure layouts and per-staff skylines ARE the values cached
    /// under this key (<see cref="GetOrComputeMeasures"/> /
    /// <see cref="GetOrComputeStaffSkylines"/>), and the staff-group placement the band
    /// walks from is recomputed each pass from those same values;</item>
    /// <item>the syllables themselves — <c>score.Lyrics</c> bucketed by measure — are in
    /// the content key (<c>MeasureContentKey.BucketSideTables</c>), which is what moves
    /// the key when a syllable is edited;</item>
    /// <item>which staves exist, which are lyric rows, and the anchor-staff choice are
    /// functions of staff identity (<c>AddStaffIdentity</c>) and the group shape
    /// (<c>AddGroupIdentity</c>);</item>
    /// <item>the text metrics are NOT in the key, exactly as for every other memo here —
    /// the session sheds this cache whole on a font-plan change
    /// (<c>IncrementalCompiler.Compile</c>'s guard, pinned by FontEditIncrementalTests).</item>
    /// </list>
    /// ⚠️ THE CACHED VALUE IS SHARED AND MUTABLE (<see cref="VerticalSkyline"/>), so
    /// nothing downstream may mutate it. Verified at the three consumers: the two extent
    /// reads (<c>LayoutUtilities.CalculateDownExtent</c>, on the minimum profile for the
    /// spacing extent and on the at-rest one for the crop) only walk buildings, and
    /// <c>PagingAugmentProgram.Builder.AddLyricBand</c> copies the profile into the
    /// program's numeric argument stream and keeps the reference for read-only replay.
    /// A stable instance is strictly BETTER for the paging-augment memo above: its
    /// baseline comparison is by reference, so a served band keeps the program equal.
    /// ⚠️ NULL IS A VALUE HERE, not an absence: an unsung system's band is null (a
    /// <c>default</c> pair) and the memo serves that null on a hit (TypedCache stores it
    /// like any other value), so the unsung case costs one lookup, never a recompute.
    /// ⚠️ IT IS A PAIR SINCE 2026-08-29 and the memo did not grow: the second profile is
    /// the SAME INSTANCE wherever the block's springs are already at rest, and where it is
    /// not, the second walk is the arithmetic this memo exists to avoid repeating — see
    /// <see cref="LayoutEngine.LooseBlockProfiles"/> for which consumer wants which.
    /// </remarks>
    public LayoutEngine.LooseBlockProfiles GetOrComputeLyricBand(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<LayoutEngine.LooseBlockProfiles> compute)
    {
        var result = _lyricBands.GetOrCompute(_keys, firstMeasureIndex, measureCount,
            isFirstSystem, isLastSystem, indent, commonShortestDuration, extra: 0,
            compute, out bool hit);
        LyricBandStats = hit
            ? (LyricBandStats.Hits + 1, LyricBandStats.Misses)
            : (LyricBandStats.Hits, LyricBandStats.Misses + 1);
        return result;
    }

    /// <summary>Hits and misses of <see cref="GetOrComputeLooseLines"/> over this cache's
    /// lifetime (diagnostics / tests) — the liveness counter, same purpose as
    /// <see cref="LyricBandStats"/>.</summary>
    public (int Hits, int Misses) LooseLinesStats { get; private set; }

    /// <summary>Reuses or computes ONE upper staff's note-bound loose-line block for ONE
    /// system — the alignment elements between that staff and the next
    /// (<c>LayoutEngine.BuildLooseLinesBetween</c>'s per-pair unit, 2026-08-26 review,
    /// finding 4-4: the third reader of the lyric-band inputs, and the only one that
    /// recomputed per keystroke).</summary>
    /// <remarks>
    /// Keyed like <see cref="GetOrComputeLyricBand"/> — the same coverage claim covers the
    /// same inputs (the syllables are in the content key per measure, the measure layouts
    /// are the value under this key, staff identity is folded, text metrics are shed by the
    /// session's font guard, the lyric spacing specs by its paper guard) — plus
    /// <paramref name="upperStaffIndex"/>, because the value is ONE staff's block
    /// (<c>LyricEngraver.NoteBoundBlockSkylines</c> filters by it). Unlike the
    /// verse-skyline memo, this value reads NO pass-dependent state: every input arrives
    /// as an argument, so the placement pass and the final annotation pass share entries
    /// soundly (they hand the same arguments).
    /// ⚠️ THE CACHED LIST AND ITS SKYLINES ARE SHARED — consumers may only read. Verified
    /// at the walk (<c>AlignmentWalk.Advance</c>/<c>Seed</c> read Distance/extents) and at
    /// the springs (same walk); nothing mutates a <c>PairLooseLine</c>.
    /// ⚠️ NULL IS A VALUE, exactly as for the lyric band: a pair with no note-bound block
    /// caches its null and a hit serves it.
    /// </remarks>
    public IReadOnlyList<MultiStaffLayouter.PairLooseLine>? GetOrComputeLooseLines(
        int upperStaffIndex,
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<IReadOnlyList<MultiStaffLayouter.PairLooseLine>?> compute)
    {
        var result = _looseLines.GetOrCompute(_keys, firstMeasureIndex, measureCount,
            isFirstSystem, isLastSystem, indent, commonShortestDuration, extra: 0,
            compute, out bool hit, extra2: upperStaffIndex);
        LooseLinesStats = hit
            ? (LooseLinesStats.Hits + 1, LooseLinesStats.Misses)
            : (LooseLinesStats.Hits, LooseLinesStats.Misses + 1);
        return result;
    }

    /// <summary>Reuses or computes the system's up/down skyline. Keyed additionally
    /// on <paramref name="systemHeight"/>, which the skyline depends on.</summary>
    public (VerticalSkyline up, VerticalSkyline down) GetOrComputeSkyline(
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration, double systemHeight,
        Func<(VerticalSkyline up, VerticalSkyline down)> compute)
        => _skylines.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: systemHeight, compute, out _);

    /// <summary>Reuses or computes ONE staff's laid-out beams for ONE system — the
    /// preliminary annotation pass's per-(staff, system) unit of work.</summary>
    /// <remarks>
    /// Keyed like <see cref="GetOrComputeMeasures"/> — the beams are a function of that
    /// system's measure layouts (member Xs come from <c>MeasureLayout.X</c> /
    /// <c>GetXForTiming</c>) plus the voices' own measures, tuplet spans and the entry
    /// time signature, all of which the content-key slice already folds. That is the same
    /// coverage claim <see cref="GetOrComputeStaffSkylines"/> makes for the beams IT
    /// computes (via <c>MultiStaffLayouter.StaffBeamLayouts</c>), and the same one the
    /// edge-beam lambda in <c>LayoutEngine</c> relies on.
    /// <list type="bullet">
    /// <item><paramref name="systemIndex"/> is a STAMP like firstMeasureIndex, not an input:
    /// <see cref="BeamLayout"/> carries <c>SystemIndex</c>, and a hit found under another
    /// system number is served re-stamped (<c>ShiftBeamSystems</c>) — a `break` inserted
    /// before the system moves every later system's number while their music and
    /// measures stay (session 330: the beams, ties and slurs of a 3-page book all missed on
    /// such a keystroke while the measures and skylines shifted fine).</item>
    /// <item><paramref name="staffIndex"/> is in the key because the value is one staff's
    /// beams and the stamp rides in <c>BeamLayout.StaffIndex</c>.</item>
    /// <item>⚠️ A group whose members CROSS a system boundary must never be memoized here:
    /// its piece in this system exists only because the group reaches into a NEIGHBOUR
    /// system's measures, which this key does not cover. The caller
    /// (<c>LayoutEngine.LayoutPreliminaryStaffBeams</c>) falls back to the unmemoized path
    /// for the whole staff when any such group exists.</item>
    /// </list>
    /// </remarks>
    public ImmutableArray<BeamLayout> GetOrComputeStaffSystemBeams(
        int staffIndex, int systemIndex,
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<ImmutableArray<BeamLayout>> compute)
        => _staffSystemBeams.GetOrCompute(_keys, firstMeasureIndex, measureCount, isFirstSystem,
            isLastSystem, indent, commonShortestDuration, extra: 0,
            compute, out _, extra2: staffIndex, systemIndex: systemIndex);

    /// <summary>Reuses or computes ONE staff's laid-out TIES for ONE system — keyed like
    /// <see cref="GetOrComputeStaffSystemBeams"/> and standing on the same claim, extended
    /// one step: a tie's Ys carry the staff's WITHIN-system offset, and that offset is a
    /// function of the same key (the staff skylines it is placed from are the value under
    /// <see cref="GetOrComputeStaffSkylines"/>, the lyric band under
    /// <see cref="GetOrComputeLyricBand"/>, and the rows/side-tables are folded per
    /// measure). A tie COLUMN whose chords straddle a system boundary must never be
    /// memoized here (the caller falls back for the whole staff — same posture as the
    /// beams' cross-system group). Held by the bowed chained-edit net in
    /// IncrementalCompilerTests.</summary>
    /// <summary>Hits and misses of the two bow memos below over this cache's lifetime
    /// (diagnostics / tests) — the liveness counter that lets the bowed chained-edit net
    /// assert the memo actually served, rather than silently falling back forever
    /// (a fixture whose bows all straddle systems would pass byte-equality without
    /// exercising the memo at all).</summary>
    public (int Hits, int Misses) BowMemoStats { get; private set; }

    public ImmutableArray<TieLayout> GetOrComputeStaffSystemTies(
        int staffIndex, int systemIndex,
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<ImmutableArray<TieLayout>> compute)
    {
        var result = _staffSystemTies.GetOrCompute(_keys, firstMeasureIndex, measureCount,
            isFirstSystem, isLastSystem, indent, commonShortestDuration, extra: 0,
            compute, out bool hit, extra2: staffIndex, systemIndex: systemIndex);
        BowMemoStats = hit
            ? (BowMemoStats.Hits + 1, BowMemoStats.Misses)
            : (BowMemoStats.Hits, BowMemoStats.Misses + 1);
        return result;
    }

    /// <summary>Reuses or computes ONE staff's laid-out SLURS for ONE system — see
    /// <see cref="GetOrComputeStaffSystemTies"/>; the slur's extra inputs (its beams, the
    /// 'inside scripts, the grace notes) are the per-system beam value under this same key
    /// family plus side-tables folded per measure.</summary>
    public ImmutableArray<SlurLayout> GetOrComputeStaffSystemSlurs(
        int staffIndex, int systemIndex,
        int firstMeasureIndex, int measureCount, bool isFirstSystem, bool isLastSystem,
        double indent, double commonShortestDuration,
        Func<ImmutableArray<SlurLayout>> compute)
    {
        var result = _staffSystemSlurs.GetOrCompute(_keys, firstMeasureIndex, measureCount,
            isFirstSystem, isLastSystem, indent, commonShortestDuration, extra: 0,
            compute, out bool hit, extra2: staffIndex, systemIndex: systemIndex);
        BowMemoStats = hit
            ? (BowMemoStats.Hits + 1, BowMemoStats.Misses)
            : (BowMemoStats.Hits, BowMemoStats.Misses + 1);
        return result;
    }

    /// <summary>Reuses or computes ONE system's augmented PAGING skyline — its base
    /// skyline pair with the annotation ink merged in (scripts, tuplet brackets, bows,
    /// figured bass, voltas, marks, texts, chord names, bar numbers).</summary>
    /// <remarks>
    /// ⚠️ KEYED DIFFERENTLY from every other memo here, and soundly SIMPLER: the key is the
    /// function's own inputs, not the content-key slice they were derived from. One
    /// system's augment is <c>program.Execute(baseline)</c> where the
    /// <see cref="PagingAugmentProgram"/> carries every merge argument RESOLVED (see its
    /// remarks); Execute is deterministic. So "same baseline INSTANCES + equal program ⇒
    /// bit-identical output" holds with no coverage claim about what the annotation
    /// layouts depend on — staff offsets, neighbours, fonts are all inside the resolved
    /// arguments. The baseline is compared by REFERENCE: an unchanged system's base pair
    /// comes back from <see cref="GetOrComputeSkyline"/> as the same instances, and a
    /// recomputed (even if byte-equal) pair just misses into a recompute — conservative,
    /// never wrong.
    /// <para>
    /// One entry per system index, overwritten on miss — the store is bounded by the
    /// widest system count the session ever saw, so it needs no generation eviction. The
    /// cached pair is SHARED across keystrokes; the paging consumer only reads
    /// (<c>PageLayouter</c>'s <c>Distance</c>), verified 2026-08-12.
    /// </para>
    /// </remarks>
    public (VerticalSkyline up, VerticalSkyline down) GetOrComputePagingAugment(
        int systemIndex, (VerticalSkyline up, VerticalSkyline down) baseline,
        PagingAugmentProgram program)
    {
        if (_pagingAugments.TryGetValue(systemIndex, out var e)
            && ReferenceEquals(e.BaseUp, baseline.up)
            && ReferenceEquals(e.BaseDown, baseline.down)
            && program.Matches(e.Program))
            return e.Value;
        var value = program.Execute(baseline);
        _pagingAugments[systemIndex] = new PagingAugmentEntry(
            baseline.up, baseline.down, program, value);
        return value;
    }

    private sealed record PagingAugmentEntry(
        VerticalSkyline BaseUp, VerticalSkyline BaseDown,
        PagingAugmentProgram Program, (VerticalSkyline up, VerticalSkyline down) Value);

    /// <summary>Lookups of one memo store by outcome — see <see cref="PassCounters"/>.</summary>
    internal readonly record struct MemoCounters(int Hits, int ShiftedHits, int Misses)
    {
        public MemoCounters WithHit() => this with { Hits = Hits + 1 };
        public MemoCounters WithShiftedHit() => this with { ShiftedHits = ShiftedHits + 1 };
        public MemoCounters WithMiss() => this with { Misses = Misses + 1 };
        public static MemoCounters operator +(MemoCounters a, MemoCounters b)
            => new(a.Hits + b.Hits, a.ShiftedHits + b.ShiftedHits, a.Misses + b.Misses);
    }

    private readonly Dictionary<int, PagingAugmentEntry> _pagingAugments = new();

    // ---- the shift functions: where each store's absolute measure stamps live ----

    private static T Unstamped<T>(T value, int delta) => value;

    private static ImmutableArray<MeasureLayout> ShiftMeasures(ImmutableArray<MeasureLayout> v, int delta)
    {
        var b = ImmutableArray.CreateBuilder<MeasureLayout>(v.Length);
        foreach (var m in v)
            b.Add(m.WithMeasureIndex(m.MeasureIndex + delta));
        return b.MoveToImmutable();
    }

    private static ImmutableArray<TieLayout> ShiftTies(ImmutableArray<TieLayout> v, int delta)
    {
        if (v.IsDefaultOrEmpty) return v;
        var b = ImmutableArray.CreateBuilder<TieLayout>(v.Length);
        foreach (var t in v)
            b.Add(t with
            {
                Tie = t.Tie.WithMeasureIndicesShifted(delta),
                RenderMeasureIndex = t.RenderMeasureIndex < 0 ? -1 : t.RenderMeasureIndex + delta,
            });
        return b.MoveToImmutable();
    }

    private static ImmutableArray<SlurLayout> ShiftSlurs(ImmutableArray<SlurLayout> v, int delta)
    {
        if (v.IsDefaultOrEmpty) return v;
        var b = ImmutableArray.CreateBuilder<SlurLayout>(v.Length);
        foreach (var s in v)
            b.Add(s with
            {
                Slur = s.Slur.WithMeasureIndicesShifted(delta),
                RenderMeasureIndex = s.RenderMeasureIndex < 0 ? -1 : s.RenderMeasureIndex + delta,
            });
        return b.MoveToImmutable();
    }

    private static ImmutableArray<BeamLayout> ShiftBeams(ImmutableArray<BeamLayout> v, int delta)
    {
        if (v.IsDefaultOrEmpty) return v;
        var b = ImmutableArray.CreateBuilder<BeamLayout>(v.Length);
        foreach (var beam in v)
            b.Add(beam.WithMeasureIndicesShifted(delta));
        return b.MoveToImmutable();
    }

    private static ImmutableArray<BeamLayout> ShiftBeamSystems(ImmutableArray<BeamLayout> v, int delta)
    {
        if (v.IsDefaultOrEmpty) return v;
        var b = ImmutableArray.CreateBuilder<BeamLayout>(v.Length);
        foreach (var beam in v)
            b.Add(beam.WithSystemIndexShifted(delta));
        return b.MoveToImmutable();
    }

    // The room's per-staff skylines are pure geometry in the staff's own frame and are
    // SHARED with the entry they were found under (read-only by contract — see
    // GetOrComputeStaffSkylines). The spanners and the pedal lines carry measures.
    // ⚠️ NOT SHIFTED, deliberately: a tuplet bracket's SourceIndex (into
    // score.TupletBrackets) and every SourcePosition in the spanners and pedal rows. Both
    // are already unreliable on an EXACT hit — a tuplet added in an EARLIER measure moves
    // the table under an unchanged system, and the content key excludes source offsets
    // by design — and no consumer of the room's spanners reads them: they are collision
    // geometry (SkylineBuilder.BuildInsideStaffSkylines and the two profile seeds beside
    // it), while the brackets and bows that are DRAWN are the annotation pass's own,
    // whose data-pos SharedRenderer.ResolveDataPos re-derives. Re-stamping the index
    // here by the measure delta would make a number that is wrong on both kinds of hit
    // look repaired on one of them.
    private static MultiStaffLayouter.StaffSkylineSet ShiftStaffSkylines(
        MultiStaffLayouter.StaffSkylineSet set, int delta)
    {
        var spanners = new List<MultiStaffLayouter.StaffInsideSpanners>(set.Spanners.Count);
        foreach (var s in set.Spanners)
        {
            var tuplets = s.TupletBrackets;
            if (!tuplets.IsDefaultOrEmpty)
            {
                var tb = ImmutableArray.CreateBuilder<TupletBracketLayout>(tuplets.Length);
                foreach (var t in tuplets)
                    tb.Add(t with { MeasureIndex = t.MeasureIndex + delta });
                tuplets = tb.MoveToImmutable();
            }
            spanners.Add(new MultiStaffLayouter.StaffInsideSpanners(
                ShiftSlurs(s.Slurs, delta), ShiftTies(s.Ties, delta), tuplets));
        }
        var pedalLines = new List<ImmutableArray<PedalEngraver.SolvedPedalLine>>(set.PedalLines.Count);
        foreach (var lines in set.PedalLines)
        {
            if (lines.IsDefaultOrEmpty)
            {
                pedalLines.Add(lines);
                continue;
            }
            var lb = ImmutableArray.CreateBuilder<PedalEngraver.SolvedPedalLine>(lines.Length);
            foreach (var l in lines)
                lb.Add(l with { StartMeasureIndex = l.StartMeasureIndex + delta });
            pedalLines.Add(lb.MoveToImmutable());
        }
        return set with { Spanners = spanners, PedalLines = pedalLines };
    }

    // A keyed memo: bucket by a hash of (system shape + extra scalars + content slice),
    // verify the full key exactly on hit so collisions only cost a recompute. The bucket
    // hash EXCLUDES firstMeasureIndex so that an entry for the same content under other
    // measure numbers lands in the same bucket, where a shifted hit can find it.
    private sealed class TypedCache<T>
    {
        private sealed class Entry
        {
            public readonly int First, Count, System;
            public readonly bool IsFirst, IsLast;
            public readonly double Indent, Shortest, Extra, Extra2;
            public readonly ImmutableArray<MeasureContentKey> Content;
            public readonly T Value;

            /// <summary>The pass (see <see cref="NextGeneration"/>) that last inserted
            /// or hit this entry — current-pass entries are exempt from eviction.</summary>
            public int Generation;

            public Entry(int first, int count, int system, bool isFirst, bool isLast, double indent,
                double shortest, double extra, double extra2,
                ImmutableArray<MeasureContentKey> content, T value, int generation)
            {
                First = first; Count = count; System = system; IsFirst = isFirst; IsLast = isLast;
                Indent = indent; Shortest = shortest; Extra = extra; Extra2 = extra2;
                Content = content; Value = value;
                Generation = generation;
            }

            /// <summary>Everything the computation READ is equal — the key without the
            /// firstMeasureIndex stamp. The caller decides whether First is equal too
            /// (an exact hit) or only shifted.</summary>
            public bool MatchesContent(int count, bool isFirst, bool isLast, double indent,
                double shortest, double extra, double extra2,
                ReadOnlySpan<MeasureContentKey> content)
            {
                if (Count != count || IsFirst != isFirst || IsLast != isLast
                    || Indent != indent || Shortest != shortest || Extra != extra
                    || Extra2 != extra2 || Content.Length != content.Length)
                    return false;
                for (int i = 0; i < content.Length; i++)
                    if (Content[i] != content[i])
                        return false;
                return true;
            }
        }

        /// <summary>Re-stamps a value found under another firstMeasureIndex by the
        /// difference (new − stored). Identity for a store whose values carry no stamp.</summary>
        private readonly Func<T, int, T> _shift;

        /// <summary>The same for the SYSTEM stamp (the three per-(staff, system) stores).</summary>
        private readonly Func<T, int, T> _shiftSystem;

        public TypedCache(Func<T, int, T> shift, Func<T, int, T>? shiftSystem = null)
        {
            _shift = shift;
            _shiftSystem = shiftSystem ?? Unstamped<T>;
        }

        /// <summary>This pass's lookups by outcome — reset at <see cref="NextGeneration"/>.</summary>
        public MemoCounters Pass { get; private set; }

        // Cap on the STALE backlog: each edit that changes a system leaves its
        // now-stale entry behind, so entries would otherwise accumulate monotonically
        // over a long session. Eviction is always SOUND — a dropped entry just
        // degrades to a recompute (a miss), never a wrong reuse. But entries the
        // CURRENT pass inserted or hit are exempt (second-chance rotation in
        // EvictOldestIfOverCap): evicting those would let a score with more than
        // MaxEntries systems flush its own working set mid-pass and degrade to a
        // permanent 0% hit rate. So the live working set may exceed the cap when the
        // score genuinely needs more; only prior-pass leftovers are bounded by it.
        private const int MaxEntries = 1024;

        private readonly Dictionary<int, List<Entry>> _buckets = new();
        private readonly Queue<(int BucketKey, Entry Entry)> _insertionOrder = new(); // one token per live entry, oldest first
        private int _count;
        private int _generation;

        public int Count => _count;

        /// <summary>Marks an edit boundary (a new layout pass) for the eviction
        /// exemption. Called once per edit via <see cref="SetContentKeys"/>.</summary>
        public void NextGeneration()
        {
            _generation++;
            Pass = default;
        }

        public T GetOrCompute(ImmutableArray<MeasureContentKey> keys,
            int first, int count, bool isFirst, bool isLast, double indent, double shortest,
            double extra, Func<T> compute, out bool hit, double extra2 = 0, int systemIndex = 0)
        {
            if (keys.IsDefault || first < 0 || first + count > keys.Length)
            {
                hit = false;
                return compute();
            }

            // Hash and match straight off the caller's keys — a HIT allocates nothing.
            // This used to materialize a fresh slice array per lookup per store per
            // keystroke, hit or miss (2026-08-26 review, finding 1-6); only a MISS
            // needs an owned copy now (the stored Entry.Content, one copy below).
            var slice = keys.AsSpan().Slice(first, count);

            var hc = new HashCode();
            hc.Add(count);
            hc.Add(isFirst);
            hc.Add(isLast);
            hc.Add(indent);
            hc.Add(shortest);
            hc.Add(extra);
            hc.Add(extra2);
            foreach (var k in slice)
                hc.Add(k);
            int bucketKey = hc.ToHashCode();

            Entry? shiftable = null;
            if (_buckets.TryGetValue(bucketKey, out var list))
            {
                foreach (var e in list)
                {
                    if (!e.MatchesContent(count, isFirst, isLast, indent, shortest, extra, extra2, slice))
                        continue;
                    if (e.First == first && e.System == systemIndex)
                    {
                        e.Generation = _generation; // live this pass -> eviction-exempt
                        Pass = Pass.WithHit();
                        hit = true;
                        return e.Value;
                    }
                    // The same computation under other measure (or system) numbers. Keep
                    // looking for an exact entry (no re-stamp at all); fall back to this one.
                    shiftable ??= e;
                }
            }
            else
            {
                list = new List<Entry>(1);
                _buckets[bucketKey] = list;
            }

            T value;
            if (shiftable != null)
            {
                value = shiftable.Value;
                if (first != shiftable.First)
                    value = _shift(value, first - shiftable.First);
                if (systemIndex != shiftable.System)
                    value = _shiftSystem(value, systemIndex - shiftable.System);
                Pass = Pass.WithShiftedHit();
                hit = true;
            }
            else
            {
                value = compute();
                Pass = Pass.WithMiss();
                hit = false;
            }
            // A shifted hit is stored under its own firstMeasureIndex like a computed value:
            // the next keystroke then hits it exactly and hands out the SAME instances, which
            // the reference-keyed memos downstream (LyricChainMemo, VerseSkylineMemo, the
            // paging augment) need to hit at all. The content slice is shared with the entry
            // it came from — an ImmutableArray, so sharing is free.
            var entry = new Entry(first, count, systemIndex, isFirst, isLast, indent, shortest, extra, extra2,
                shiftable?.Content ?? ImmutableArray.Create(keys, first, count), value, _generation);
            list.Add(entry);
            _insertionOrder.Enqueue((bucketKey, entry));
            _count++;
            EvictOldestIfOverCap();
            return value;
        }

        // Second-chance FIFO, oldest first: an entry the current pass inserted or hit
        // rotates to the back instead of being dropped (evicting the live working set
        // would make a >MaxEntries-system score thrash itself to 0% hits). One full
        // rotation without an eviction means everything live is current-pass — then
        // the cap yields (the cache grows) rather than thrashes. Each queue token
        // holds its exact entry, so removal never touches the wrong entry and _count
        // stays exact.
        private void EvictOldestIfOverCap()
        {
            int scan = _insertionOrder.Count;
            while (_count > MaxEntries && scan-- > 0)
            {
                var (oldKey, entry) = _insertionOrder.Dequeue();
                if (entry.Generation == _generation)
                {
                    _insertionOrder.Enqueue((oldKey, entry));
                    continue;
                }
                if (_buckets.TryGetValue(oldKey, out var oldList) && oldList.Remove(entry))
                {
                    _count--;
                    if (oldList.Count == 0)
                        _buckets.Remove(oldKey);
                }
            }
        }
    }
}
