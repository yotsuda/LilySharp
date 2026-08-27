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

/// <summary>
/// Engine for calculating score layout.
/// </summary>
internal sealed partial class LayoutEngine
{
    private readonly LayoutOptions _options;
    private readonly ElementCoordinator _elementCoordinator;

    /// <summary>
    /// The skyline builder for the score being laid out.
    /// </summary>
    /// <remarks>
    /// ⚠️ NOT READONLY, and the reason is the score's <c>font</c> directive: the builder
    /// reserves text ink (dynamic labels, tuplet numbers) and so has to know the faces,
    /// which the CONSTRUCTOR cannot — it is handed options and no score. It is therefore
    /// re-seated at the top of <see cref="Layout(MultiStaffScore, IReadOnlyList{int},
    /// SystemLayoutCache, MeasureSpringData[], System.Nullable{double})"/>.
    /// <para>
    /// SAFE because an engine lays out ONE score: every production construction is
    /// <c>new LayoutEngine(score.Paper).Layout(score)</c> — SvgGenerator, PngGenerator
    /// (twice), PdfGenerator, IncrementalCompiler, LayoutReport, checked 2026-08-18
    /// (and re-checked 2026-08-23 when the paper directive put the score's own options
    /// into those constructions). An engine reused across two scores would be the case
    /// this breaks, and there is none.
    /// </para>
    /// </remarks>
    private SkylineBuilder _skylineBuilder;
    private readonly MeasureLayouter _measureLayouter = new();
    private readonly PageLayouter _pageLayouter;
    private readonly SystemBreaker _systemBreaker;
    private readonly MultiStaffLayouter _multiStaffLayouter;

    public LayoutEngine(LayoutOptions? options = null)
    {
        _options = options ?? LayoutOptions.Default;
        _elementCoordinator = new ElementCoordinator(_options);
        _skylineBuilder = new SkylineBuilder(_options.StaffHeight);
        _pageLayouter = new PageLayouter(_options);
        _systemBreaker = new SystemBreaker(_options);
        _multiStaffLayouter = new MultiStaffLayouter(_options, _measureLayouter);
    }

    /// <summary>
    /// Calculates the complete layout for a single-staff score by wrapping it in a
    /// <see cref="MultiStaffScore"/> and running the single, real layout path — the same
    /// path the renderer takes for every score (see SvgGenerator.CollectScore). Kept as a
    /// convenience entry for the many single-voice call sites.
    /// </summary>
    public ScoreLayout Layout(Score score)
    {
        return Layout(MultiStaffScore.FromScore(score));
    }

    /// <summary>Calculates the complete layout for a multi-staff score.</summary>
    /// <param name="precomputedShortest">The score-global common shortest duration, when the
    /// caller (the incremental driver) has already computed it for the break gate — the same
    /// value this method would derive from the same score, handed over instead of derived
    /// twice per keystroke (SystemBreaker's "one quantity, two places" remark names this
    /// pair). Null ⇒ compute here (the full-render path).</param>
    public ScoreLayout Layout(MultiStaffScore score, IReadOnlyList<int>? precomputedLineSizes = null,
        SystemLayoutCache? systemCache = null, MeasureSpringData[]? precomputedSprings = null,
        double? precomputedShortest = null)
    {
        // The faces this score reserves against — see the field's remark for why the
        // builder cannot be given them in the constructor.
        _skylineBuilder = new SkylineBuilder(_options.StaffHeight, score.TextMetrics);

        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);

        // LILYPOND-REF: lily/page-layout-problem.cc:656-717 alignment_distances
        // Apply user overrides for StaffGrouper spacing before layout
        var multiStaffLayouter = _multiStaffLayouter;
        if (!score.GrobOverrides.IsDefaultOrEmpty
            && score.GrobOverrides.Any(o => o.GrobType == "StaffGrouper"))
        {
            var overriddenSpacing = _options.StaffSpacing.ApplyOverrides(score.GrobOverrides);
            if (overriddenSpacing != _options.StaffSpacing)
            {
                var overriddenOptions = _options with { StaffSpacing = overriddenSpacing };
                multiStaffLayouter = new MultiStaffLayouter(overriddenOptions, _measureLayouter);
            }
        }

        // LILYPOND-REF: lily/spacing-spanner.cc
        // Calculate the common shortest duration across all voices for Gourlay spacing
        // (or take the driver's, computed for the break gate from this same score).
        double commonShortestDuration =
            precomputedShortest ?? SpacingRules.CalculateCommonShortestDuration(score);

        // LILYPOND-REF: ly/paper-defaults-init.ly — indent / short-indent
        // LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
        // Calculate indent from instrument names (auto-calculate if not explicitly set)
        double indent = _options.Indent > 0
            ? _options.Indent
            : CalculateIndentFromInstrumentNames(score);
        double shortIndent = _options.ShortIndent;

        // F3 incremental cutoff: when the line-break gate is unchanged the driver
        // passes the cached per-line measure counts so SystemBreaker regroups the
        // new measures and skips the DP. Null => normal (byte-identical) breaking.
        // When the DP does run in a session, the cache's row-prefix resume
        // (finding 4-5) refills only the rows after the first changed spring.
        var systemMeasures = _systemBreaker.BreakIntoSystems(
            score, commonShortestDuration, precomputedLineSizes, precomputedSprings,
            systemCache?.LineBreakDp);

        // Chord symbols on a TEXT ROW (lead sheets) live in their own band and must not
        // inflate a music staff's up-extent; inline chord symbols (nameless `chords { }`) sit
        // above their staff and must.
        // ⚠️ THIS SET IS NOT "THE NON-SPACEABLE LINES": that question is asked of the staff
        // itself, through the `staff-affinity` a StaffLayout carries (ClassifySystem). What it
        // is, for the loose-chain tables below, is the CHEAP TEST FOR "is there any run at
        // all" — a score with no text row has no run for the chain to build.
        // ★ IT USED TO BE TWO SETS, and the lyrics-only one was the defect's carrier: the run
        // tables were gated on it, so a score whose only rows were CHORDS rows built no table
        // and a score with both built one that silently omitted the chords row. Every
        // non-spaceable line is an element of its run now (ClassifySystem), so the gate is
        // "any text row" and the membership question has one home.
        var textRowStaves = new HashSet<int>();
        foreach (var (_, st, gi) in score.EnumerateStaves())
            if (st.IsTextRow)
                textRowStaves.Add(gi);

        // LILYPOND-REF: lily/align-interface.cc:217-268
        // Compute first system measure layouts first, then use skyline-based staff spacing
        multiStaffLayouter.CurrentIndent = indent;
        var firstSystemMeasureLayouts = systemMeasures.Count > 0
            ? ComputeSystemMeasures(systemCache, 0, systemMeasures[0].Count, true,
                systemMeasures.Count == 1, indent, commonShortestDuration,
                () => multiStaffLayouter.LayoutMeasures(score, 0, 0, systemMeasures[0].Count,
                    isLastSystem: systemMeasures.Count == 1,
                    baseShortestDuration: commonShortestDuration))
            : ImmutableArray<MeasureLayout>.Empty;
        // System 0's own placement — the same call the loop below makes for every system,
        // with system 0's measure range. LILYPOND-REF: lily/align-interface.cc:217-268 runs
        // once per System, on THAT system's skylines; there is no score-wide placement in
        // LilyPond to share, and Lily# no longer has one either (the shared
        // firstStaffGroupLayouts / defaultStaffGroupLayouts pair, and the hasHaraKiri flag
        // that chose between shared and per-system, went with it).
        // ⚠️ The second arm is a DEGENERATE-INPUT guard, not a spacing branch: a score with
        // no measures has no range to test emptiness over, and asking hara-kiri about the
        // range 0..0 would report every staff empty and hide the lot.
        var firstStaffSkylines = ComputeStaffSkylines(systemCache, 0,
            systemMeasures.Count > 0 ? systemMeasures[0].Count : 0, true,
            systemMeasures.Count <= 1, indent, commonShortestDuration,
            () => multiStaffLayouter.BuildStaffSkylines(
                score, _skylineBuilder, firstSystemMeasureLayouts, systemIndex: 0));
        var firstRunSources = systemMeasures.Count > 0
            ? BuildPairRunSources(
                score, firstSystemMeasureLayouts, 0, systemMeasures[0].Count,
                systemCache, isFirstSystem: true, isLastSystem: systemMeasures.Count == 1,
                indent, commonShortestDuration)
            : default;
        var firstStaffGroupLayouts = systemMeasures.Count > 0
            ? multiStaffLayouter.LayoutStaffGroups(
                score, firstStaffSkylines.Skylines, 0, systemMeasures[0].Count,
                isFirstSystem: true, firstRunSources)
            : multiStaffLayouter.LayoutStaffGroups(
                score, _skylineBuilder, firstSystemMeasureLayouts, systemIndex: 0);
        // The system's height is the extent of the groups AS PLACED — see
        // MultiStaffLayouter.SystemHeightOf.
        double systemHeight = MultiStaffLayouter.SystemHeightOf(firstStaffGroupLayouts);

        // The system silhouette's edge staves — the two staves BuildSystemSkylines
        // processes — and their drawn beams. A beamed stem is drawn to the quanter's
        // length, but the note boxes reserve a fixed 3.5 stem; supplying the beams lets
        // the builder suppress those and seed the real outer edge instead, so the page
        // (and the first system's Y) spaces against the drawn ink, exactly as the
        // staff-to-staff path does. Beams are a pure function of the same measure
        // content the skyline memo is keyed on, so computing them inside the compute
        // lambda keeps the cache sound. audit/lp-geometry system.beam-{under,over}-notes.
        // First/last in EnumerateStaves order, which is by construction the same
        // pair BuildSystemSkylines picks (StaffGroups[0].Staves[0] is the first
        // yield, StaffGroups[^1].Staves[^1] the last) — the index comes from the
        // same enumeration, so no reference matching or fallback is involved.
        var edgeFirstStaff = score.StaffGroups[0].PrimaryStaff;
        var edgeLastStaff = score.StaffGroups[^1].Staves[^1];
        int edgeFirstStaffIndex = 0, edgeLastStaffIndex = 0;
        foreach (var (_, _, gi) in score.EnumerateStaves())
            edgeLastStaffIndex = gi;
        (ImmutableArray<BeamLayout> first, ImmutableArray<BeamLayout> last) EdgeStaffBeams(
            ImmutableArray<MeasureLayout> mls, int sysIdx) =>
            (multiStaffLayouter.StaffBeamLayouts(
                score, edgeFirstStaff, edgeFirstStaffIndex, mls, sysIdx),
             edgeLastStaff != edgeFirstStaff
                ? multiStaffLayouter.StaffBeamLayouts(
                    score, edgeLastStaff, edgeLastStaffIndex, mls, sysIdx)
                : default);

        // Pre-calculate first system skylines for initial Y positioning
        var firstEdgeBeams = EdgeStaffBeams(firstSystemMeasureLayouts, 0);
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(
            score, firstSystemMeasureLayouts, systemHeight, indent,
            firstEdgeBeams.first, firstEdgeBeams.last, firstStaffGroupLayouts,
            firstStaffSkylines.Skylines);
        var firstAnchor = PageAnchorOffsets(firstStaffGroupLayouts);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            _options.MarginTop, headerHeight, LayoutUtilities.CalculateUpExtent(firstUpSkyline),
            firstAnchor.ToFirst, _options.VerticalSpacing.TopSystem);

        var placed = LayoutSystems(new SystemPassContext
        {
            Score = score,
            Layouter = multiStaffLayouter,
            SystemMeasures = systemMeasures,
            SystemCache = systemCache,
            Indent = indent,
            ShortIndent = shortIndent,
            CommonShortestDuration = commonShortestDuration,
            FirstSystemMeasureLayouts = firstSystemMeasureLayouts,
            FirstStaffGroupLayouts = firstStaffGroupLayouts,
            FirstStaffSkylines = firstStaffSkylines,
            FirstRunSources = firstRunSources,
            EdgeStaffBeams = EdgeStaffBeams,
            FirstSystemY = currentY,
        });
        var systems = placed.Systems;
        var perSystemExtents = placed.Extents;
        var perSystemSkylines = placed.Skylines;
        var perSystemHeights = placed.Heights;

        // LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
        var perSystemBandUps = new List<double>();
        var multiMeasureRanges = new List<(int startMeasure, int measureCount)>();
        int multiMeasStart = 0;
        foreach (var sysMeasures in systemMeasures)
        {
            multiMeasureRanges.Add((multiMeasStart, sysMeasures.Count));
            multiMeasStart += sysMeasures.Count;
        }
        var inlineChordNames = score.ChordNames
            .Where(c => !textRowStaves.Contains(c.StaffIndex)).ToImmutableArray();
        // The raise that brings this pass's STAFF-framed estimates into the ORIGIN frame the
        // extents are kept in — LilyPond's -first_spaceable_dy, per system.
        var rowsAboveFirstStaff = systems
            .Select(s => RowsAboveFirstStaff(s.StaffGroups))
            .ToList();
        AugmentExtentsWithLooseLines(score.TextMetrics, perSystemExtents,
            score.MusicMarks, score.VoltaBrackets, multiMeasureRanges,
            inlineChordNames, perSystemBandUps, rowsAboveFirstStaff);

        // Preliminary annotation pass (see the single-staff path): real
        // protrusions of brackets/marks/voltas/dynamics/ties/slurs join the
        // spacing extents before the page Y is fixed.
        // ⚠️ THE ROOM'S TABLES GO WITH IT — placed.StaffSpanners / placed.StaffInside are
        // built by LayoutSystems from the very system list this pass iterates
        // (prelimSystems IS placed.Systems, same objects, same indices), so the check the
        // profile-cache remark demanded — "do the two runs hold the same measure layouts?"
        // — is reference identity by construction here. See the context fields' remarks
        // for what diverged while the preliminary pass rebuilt its own profiles.
        var prelim = RunPreliminaryAnnotationPass(
            score, multiStaffLayouter, systems.ToImmutableArray(), perSystemExtents,
            perSystemSkylines, multiStaffLayouter.RestCollisionsOf, systemCache,
            commonShortestDuration, placed.StaffSpanners, placed.StaffInside,
            rowsAboveFirstStaff, placed.LyricBands);

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, systemHeight,
            prelim.PagingSkylines, perSystemHeights, perSystemBandUps);

        var looseChainEnd = BuildLooseChainEnds(
            score, pages, systemsArray, perSystemExtents,
            multiStaffLayouter.RestCollisionsOf, placed.StaffSpanners, placed.StaffInside);
        var trailingRowStaves = BuildTrailingRowStaves(systemsArray, textRowStaves);
        var betweenRowStaves = BuildBetweenRowStaves(systemsArray, textRowStaves);

        // Calculate beams/ties/slurs/glissandos per staff
        var (allBeamLayouts, allTieLayouts, allSlurLayouts, allGlissandoLayouts, restShifts) =
            LayoutAllSpanners(score, systemsArray, multiStaffLayouter.RestCollisionsOf,
                prelim.BeamsByStaff, prelim.TiesByStaff, prelim.SlursByStaff,
                systems.ToImmutableArray());

        // Resolve cross-staff layouts per voice
        var crossStaffLayouts = ImmutableArray<CrossStaffLayout>.Empty;
        if (!score.CrossStaffItems.IsDefaultOrEmpty)
        {
            // Use primary staff index (0) as default source; in full multi-voice,
            // each voice would have its own staff index.
            int primaryStaffIdx = 0;
            int staffCount = score.StaffGroups.Sum(g => g.StaffCount);
            crossStaffLayouts = CrossStaffEngraver.Calculate(
                score.CrossStaffItems, primaryStaffIdx, staffCount);
        }

        // Create primary staff Score for annotation engravers
        var primaryStaff = score.PrimaryContentStaff;
        var primaryScore = new Score(
            primaryStaff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            ClefToString(primaryStaff.Clef), score.Tempo, score.Title, score.Composer,
            tupletBrackets: score.TupletBrackets, swingSubdivision: score.SwingSubdivision,
            // The MMR engraver reads score.ChordNames to keep a chord-bearing
            // rest bar out of a compressed run (see MultiMeasureRestEngraver).
            chordNames: score.ChordNames,
            // ...and score.PercentRepeats to keep the % measures' unfolded R out
            // of the symbol pass (the % is the symbol there) — without this the
            // synthetic score reported no repeats and every covered bar drew its
            // whole rest under the sign.
            percentRepeats: score.PercentRepeats,
            // The mark engraver reads Header.Tempo off THIS score to give the opening
            // metronome mark its data-pos, so the header offsets have to come along.
            header: score.Header)
        {
            TempoText = score.TempoText,
            TempoBeatUnit = score.TempoBeatUnit,
            TempoDots = score.TempoDots,
        };

        var anchors = BuildStaffAnchorTables(score, systemsArray);

        var annotationContext = new AnnotationLayoutContext
        {
            Score = primaryScore,
            MultiScore = score,
            IsLeadSheet = score.IsLeadSheet,
            GridBarlineRowIndex = score.GridBarlineRowIndex,
            Fonts = score.TextMetrics,
            Systems = systemsArray,
            Dynamics = score.Dynamics,
            Articulations = score.Articulations,
            GraceNotes = score.GraceNotes,
            Lyrics = score.Lyrics,
            MusicMarks = score.MusicMarks,
            CustomTexts = score.CustomTexts,
            VoltaBrackets = score.VoltaBrackets,
            TupletBrackets = score.TupletBrackets,
            Arpeggios = score.Arpeggios,
            Measures = primaryStaff.PrimaryVoice.Measures,
            FiguredBasses = score.FiguredBasses,
            ChordNames = score.ChordNames,
            PercentRepeats = score.PercentRepeats,
            CrossStaffLayouts = crossStaffLayouts,
            TrillSpanners = score.TrillSpanners,
            // bracket-visibility = if-no-beam needs the beam groups; without
            // them every tuplet bracket draws even when fully beamed. The
            // beam LAYOUTS let the suppressed-bracket number attach to the
            // beam itself.
            // ⚠️ CARRIED from the preliminary pass, not detected a second time: the two
            // detections consumed IDENTICAL inputs — prelimScore and primaryScore hand
            // BeamDetector the same PrimaryVoice, TimeSignature and TupletBrackets
            // instances (detection reads nothing else of the Score, and the detector is
            // stateless) — so this is the same value, not an approximation of it. The
            // fields the two Scores DO differ in (swing, chord names, header) never
            // reach the detector; if detection ever grows such an input, this carry
            // must be re-examined.
            BeamGroups = prelim.AnnotationBeamGroups,
            BeamLayouts = allBeamLayouts.ToImmutableArray(),
            TieLayouts = allTieLayouts.ToImmutableArray(),
            SlurLayouts = allSlurLayouts.ToImmutableArray(),
            SystemSkylines = perSystemSkylines,
            StaffSkylines = placed.StaffSkylines,
            RunSources = placed.RunSources,
            StaffSpanners = placed.StaffSpanners,
            StaffInside = placed.StaffInside,
            PedalLines = placed.PedalLines,
            PedalRows = placed.PedalRows,
            // The room's own memo, not a second call: see AnnotationLayoutContext.RestCollisionsOf.
            RestCollisionsOf = multiStaffLayouter.RestCollisionsOf,
            TupletForceStemUp = primaryStaff.IsMultiVoice,
            StaffVoices = primaryStaff.Voices,
            VoicesByStaff = anchors.VoicesByStaff,
            MeasuresByStaff = anchors.MeasuresByStaff,
            StaffYByIndex = anchors.StaffYByIndex,
            NoteBoundAnchorY = anchors.NoteBoundAnchorY,
            StaffByIndex = anchors.StaffByIndex,
            LooseChainEnd = looseChainEnd,
            TrailingRowStaves = trailingRowStaves,
            BetweenRowStaves = betweenRowStaves,
            LastSpaceableStaffY = anchors.LastSpaceableStaffY,
            PrefixTimeSignatureX = BuildPrefixTimeSignatureX(score, systemsArray),
            LineStartBarlineX = BuildLineStartBarlineX(score, systemsArray),
            // The FINAL pass's above-stack memo — its own instance, because the
            // preliminary pass stacks different systems every keystroke and one shared
            // store would overwrite itself twice per keystroke and never hit.
            AboveStackMemo = systemCache?.FinalAboveStack,
            // Its below-side mirror (finding 4-3), likewise this pass's own instance.
            BelowStackMemo = systemCache?.FinalBelowStack,
            // Likewise its own fingering memo (see the preliminary pass's site).
            FingScriptMemo = systemCache?.FinalFingScripts,
            // The verse-skyline memo is the SAME instance the preliminary pass used —
            // X-only values, so the final pass hits what the preliminary computed.
            VerseSkylines = systemCache?.FinalVerseSkylines,
            LyricChains = systemCache?.FinalLyricChains,
        };
        var annotations = CalculateAnnotationLayouts(annotationContext);

        // ...and the rows the lyric chain solved are moved to where it put them, AFTER the
        // pass rather than inside it — see AnnotationLayoutContext.SolvedRowBaselines.
        (systemsArray, annotations) = ApplySolvedRowPositions(
            score, systemsArray, annotations, annotationContext.SolvedRowBaselines);

        var (voiceOffsets, headWipes, dotAdjustments, partCombineLayouts) =
            CalculateVoiceCollisions(score, systemsArray);

        // The dot-column answer for every dotted rest, through the static memo so the
        // renderer and the skyline seed read what one solve produced (same slot-sharing
        // granularity across staves as RestShifts — the key has no staff axis).
        var restDotOffsetsBuilder = ImmutableDictionary.CreateBuilder<RestShiftKey, int>();
        foreach (var (_, staffForDots, _) in score.EnumerateStaves())
            foreach (var kv in ElementCoordinator.RestDotOffsetsOf(staffForDots))
                restDotOffsetsBuilder[kv.Key] = kv.Value;

        var result = BuildScoreLayout(pages, systemsArray,
            allBeamLayouts.ToImmutableArray(), allTieLayouts.ToImmutableArray(),
            allSlurLayouts.ToImmutableArray(), allGlissandoLayouts.ToImmutableArray(),
            annotations,
            voiceOffsets,
            headWipes,
            dotAdjustments,
            restShifts,
            partCombineLayouts) with
        {
            RestDotOffsets = restDotOffsetsBuilder.ToImmutable(),
        };
        return FinalizeLayout(result, score.GrobOverrides, score.GrobReverts);
    }

    /// <summary>Everything the per-system pass needs that does not vary between systems.</summary>
    private sealed class SystemPassContext
    {
        public required MultiStaffScore Score { get; init; }
        public required MultiStaffLayouter Layouter { get; init; }
        public required List<List<Measure>> SystemMeasures { get; init; }
        public required SystemLayoutCache? SystemCache { get; init; }
        public required double Indent { get; init; }
        public required double ShortIndent { get; init; }
        public required double CommonShortestDuration { get; init; }
        public required ImmutableArray<MeasureLayout> FirstSystemMeasureLayouts { get; init; }
        public required ImmutableArray<StaffGroupLayout> FirstStaffGroupLayouts { get; init; }
        public required MultiStaffLayouter.StaffSkylineSet FirstStaffSkylines { get; init; }
        /// <summary>System 0's loose-line lookup, built by the caller alongside its placement
        /// so the springs below are floored against the SAME alignment that drew it.</summary>
        /// <summary>System 0's pair-run suppliers (note-bound block + attached chord
        /// line), built once and shared by the placement and the springs.</summary>
        public MultiStaffLayouter.PairRunSources FirstRunSources { get; init; }
        /// <summary>This system's edge-staff beams — the measure layouts AND the system they
        /// belong to, because the beams are stamped with it (BeamLayout.SystemIndex).</summary>
        public required Func<ImmutableArray<MeasureLayout>, int,
            (ImmutableArray<BeamLayout> first, ImmutableArray<BeamLayout> last)> EdgeStaffBeams { get; init; }
        public required double FirstSystemY { get; init; }
    }

    /// <summary>What the per-system pass produces, index-aligned by system.</summary>
    /// <remarks>
    /// ⚠️ The per-member docs are <c>&lt;param&gt;</c> tags HERE and not <c>///</c> blocks on the
    /// positional parameters below: C# does not emit documentation for a record's positional
    /// parameters from their own comments (CS1587), so four of these accounts were being
    /// dropped from the XML entirely until 2026-08-18.
    /// </remarks>
    /// <param name="Systems">The placed <see cref="SystemLayout"/>s, in system order.</param>
    /// <param name="Extents">Per system, its protrusion above and below its own frame —
    /// <see cref="LayoutUtilities.CalculateUpExtent"/> of the up skyline and
    /// <see cref="LayoutUtilities.CalculateDownExtent"/> of the down one.</param>
    /// <param name="Skylines">Per system, the WHOLE-system up/down pair
    /// <see cref="SkylineBuilder.BuildSystemSkylines"/> returned — the silhouette paging
    /// springs against, as opposed to the per-staff tables below.</param>
    /// <param name="Heights">Per system, the height it was laid out at.</param>
    /// <param name="LyricBands">Per system, its loose lyric block at its ALIGNMENT MINIMUM as
    /// a DOWN profile in the system-origin frame — the note-bound verses AND the independent
    /// rows standing under them; see <see cref="LyricReservationBelowSystem"/>. Produced here
    /// because only this pass has the system's own staff skylines; its deepest point is
    /// already folded into <see cref="Extents"/>, and the profile itself joins the paging
    /// silhouette through <see cref="PagingAugmentProgram.Builder.AddLyricBand"/>.</param>
    /// <param name="StaffSkylines">Per system, the per-staff UP/DOWN skylines that system was
    /// placed and sprung against, indexed by global staff index. Carried out of the pass rather
    /// than rebuilt because a note-bound lyric line is DRAWN against the same
    /// silhouette — see <see cref="AnnotationLayoutContext.StaffSkylines"/>.</param>
    /// <param name="StaffSpanners">Per system, the inside-staff spanners each staff's skyline was
    /// built from, indexed by global staff index. Carried for the consumers that must rebuild
    /// a profile of their own — see
    /// <see cref="MultiStaffLayouter.StaffInsideSpanners"/>.</param>
    /// <param name="StaffInside">Per system, each staff's INSIDE-staff skyline — the one LilyPond
    /// builds once per VerticalAxisGroup and every consumer of a staff's silhouette reads. Every
    /// site that used to rebuild its own subset takes this instead; see
    /// <see cref="SkylineBuilder.BuildInsideStaffSkylines"/>.</param>
    /// <param name="RunSources">Per system, the pair-run suppliers its placement and springs
    /// consumed — carried so the final annotation pass's drawn-baseline walk reads the SAME
    /// suppliers instead of rebuilding them (finding 4-4); see
    /// <see cref="AnnotationLayoutContext.RunSources"/>.</param>
    private readonly record struct SystemPlacements(
        List<SystemLayout> Systems,
        List<(double upExtent, double downExtent)> Extents,
        List<(VerticalSkyline up, VerticalSkyline down)> Skylines,
        List<double> Heights,
        List<VerticalSkyline?> LyricBands,
        List<List<(VerticalSkyline Up, VerticalSkyline Down)>> StaffSkylines,
        List<List<MultiStaffLayouter.StaffInsideSpanners>> StaffSpanners,
        List<List<(VerticalSkyline Up, VerticalSkyline Down)>> StaffInside,
        List<List<ImmutableArray<PedalEngraver.SolvedPedalLine>>> PedalLines,
        List<List<ImmutableArray<PedalEngraver.SolvedPedalRow>>> PedalRows,
        List<MultiStaffLayouter.PairRunSources> RunSources);

    /// <summary>
    /// Lays out every system: its measures, its staves, its height and its skyline.
    /// </summary>
    /// <remarks>
    /// Extracted verbatim from <c>Layout</c>'s body. ⚠️ System 0's measure layouts, staff
    /// skylines and placement are computed by the CALLER (it needs them to fix the first
    /// system's Y) and handed in, so this pass reuses them rather than recomputing — see
    /// <see cref="MultiStaffLayouter.BuildStaffSkylines"/> for why building them twice is
    /// worth avoiding.
    /// </remarks>
    private SystemPlacements LayoutSystems(SystemPassContext ctx)
    {
        var score = ctx.Score;
        var multiStaffLayouter = ctx.Layouter;
        var systemMeasures = ctx.SystemMeasures;
        var systemCache = ctx.SystemCache;
        double indent = ctx.Indent;
        double shortIndent = ctx.ShortIndent;
        double commonShortestDuration = ctx.CommonShortestDuration;
        var firstSystemMeasureLayouts = ctx.FirstSystemMeasureLayouts;
        var firstStaffGroupLayouts = ctx.FirstStaffGroupLayouts;
        var firstStaffSkylines = ctx.FirstStaffSkylines;
        var EdgeStaffBeams = ctx.EdgeStaffBeams;
        double currentY = ctx.FirstSystemY;

        // Layout each system with skyline extents
        var systems = new List<SystemLayout>();
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        var perSystemSkylines = new List<(VerticalSkyline up, VerticalSkyline down)>();
        // Per-system body height. Equals the scalar systemHeight for every system
        // unless hara-kiri hides different staves per system (then each system is as
        // tall as its OWN surviving staves). CreatePages spaces systems by this so a
        // hara-kiri'd system's gap is not over-reserved at the full height.
        var perSystemHeights = new List<double>();
        var perSystemLyricBands = new List<VerticalSkyline?>();
        var perSystemStaffSkylines = new List<List<(VerticalSkyline Up, VerticalSkyline Down)>>();
        var perSystemStaffSpanners = new List<List<MultiStaffLayouter.StaffInsideSpanners>>();
        var perSystemStaffInside = new List<List<(VerticalSkyline Up, VerticalSkyline Down)>>();
        var perSystemPedalLines = new List<List<ImmutableArray<PedalEngraver.SolvedPedalLine>>>();
        var perSystemPedalRows = new List<List<ImmutableArray<PedalEngraver.SolvedPedalRow>>>();
        // Per-system pair-run suppliers, carried out for the FINAL annotation pass's
        // drawn-baseline walk (finding 4-4): it used to rebuild them per system per
        // keystroke; the carried instance is the same value — built from the same
        // measure layouts and range — with its within-pass caches along for the ride.
        var perSystemRunSources = new List<MultiStaffLayouter.PairRunSources>();
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            bool isFirstSystem = sysIdx == 0;
            double sysIndent = isFirstSystem ? indent : shortIndent;
            multiStaffLayouter.CurrentIndent = sysIndent;
            int measureCount = systemMeasures[sysIdx].Count;
            var measureLayouts = isFirstSystem
                ? firstSystemMeasureLayouts
                : ComputeSystemMeasures(systemCache, firstMeasureIndex, measureCount, false,
                    sysIdx == systemMeasures.Count - 1, sysIndent, commonShortestDuration,
                    () => multiStaffLayouter.LayoutMeasures(score, sysIdx, firstMeasureIndex, measureCount,
                        sysIdx == systemMeasures.Count - 1, commonShortestDuration));

            // THIS system's staff skylines, built ONCE and used by both its placement and
            // its page springs below. Building them is the expensive part of laying a system
            // out, and the two halves need the identical list anyway — see
            // MultiStaffLayouter.BuildStaffSkylines.
            var sysStaffSkylines = isFirstSystem
                ? firstStaffSkylines
                : ComputeStaffSkylines(systemCache, firstMeasureIndex, measureCount, false,
                    sysIdx == systemMeasures.Count - 1, sysIndent, commonShortestDuration,
                    () => multiStaffLayouter.BuildStaffSkylines(
                        score, _skylineBuilder, measureLayouts, sysIdx));

            // THIS system's placement, from ITS music. LILYPOND-REF:
            // lily/align-interface.cc:217-268 — each System has its own VerticalAlignment and
            // is spaced against its own staves' skylines, so a system whose ink reaches
            // between the staves gets more room and its neighbours do not. Hara-kiri needs no
            // branch here: which staves survive is one more per-system input, passed as the
            // measure range (LayoutStaffGroups takes liveness as a predicate, not as a mode).
            // The alignment's loose lines between THIS system's staves — the block whose ink
            // the room between two staves is walked from. Built once and handed to both the
            // placement and the springs, for the same reason the staff skylines are.
            var sysRunSources = isFirstSystem
                ? ctx.FirstRunSources
                : BuildPairRunSources(
                    score, measureLayouts, firstMeasureIndex, firstMeasureIndex + measureCount,
                    systemCache, isFirstSystem: false,
                    isLastSystem: sysIdx == systemMeasures.Count - 1,
                    sysIndent, commonShortestDuration);

            var sysStaffGroups = isFirstSystem
                ? firstStaffGroupLayouts
                : multiStaffLayouter.LayoutStaffGroups(
                    score, sysStaffSkylines.Skylines,
                    firstMeasureIndex, firstMeasureIndex + measureCount, isFirstSystem,
                    sysRunSources);

            // The height of THIS system: the extent of the groups it actually placed. A
            // hidden staff was placed at zero height, so it leaves the union by itself,
            // exactly as LilyPond's dead elements leave its alignment
            // (page-layout-problem.cc:1366-1370).
            double sysHeight = MultiStaffLayouter.SystemHeightOf(sysStaffGroups);
            // Ensure at least one staff space for completely empty systems
            // (LILYSHARP-OWN: LilyPond never emits a system with no live staff at all).
            if (sysHeight <= 0)
                sysHeight = _options.StaffHeight;

            var (upSky, downSky) = ComputeSystemSkyline(systemCache, firstMeasureIndex, measureCount,
                isFirstSystem, sysIdx == systemMeasures.Count - 1, sysIndent, commonShortestDuration, sysHeight,
                () =>
                {
                    var edgeBeams = EdgeStaffBeams(measureLayouts, sysIdx);
                    return _skylineBuilder.BuildSystemSkylines(score, measureLayouts, sysHeight, sysIndent,
                        edgeBeams.first, edgeBeams.last, sysStaffGroups,
                        sysStaffSkylines.Skylines);
                });
            perSystemSkylines.Add((upSky, downSky));
            // The loose block's minimum profile, in the system-origin frame. Its deepest
            // point joins the down EXTENT here (the page-fill and fallback arithmetic are
            // scalars); the profile itself joins the paging silhouette as a program step
            // (AugmentSkylinesForPaging), so the inter-system floor reads it WITH X —
            // audit/lp-geometry lyrics.band-floor.*.
            var lyricBand = ComputeLyricBand(systemCache, firstMeasureIndex, measureCount,
                isFirstSystem, sysIdx == systemMeasures.Count - 1, sysIndent,
                commonShortestDuration,
                () => LyricReservationBelowSystem(
                    score, measureLayouts, sysStaffSkylines.Skylines, sysStaffGroups,
                    firstMeasureIndex, firstMeasureIndex + measureCount));
            perSystemLyricBands.Add(lyricBand);
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSky),
                Math.Max(
                    LayoutUtilities.CalculateDownExtent(downSky, sysHeight),
                    lyricBand is null
                        ? 0
                        : LayoutUtilities.CalculateDownExtent(lyricBand, sysHeight))));
            perSystemHeights.Add(sysHeight);

            systems.Add(new SystemLayout(
                SystemIndex: sysIdx, Y: currentY,
                Width: _options.ContentWidth - sysIndent,
                // The union of the signatures this system's staves ENGRAVE, which is the model
                // the spacing reserves from and the renderer draws to — not the score key,
                // which knows nothing about a transposed part's own signature.
                // LILYPOND-REF: lily/break-alignment-interface.cc:141-142,242.
                // ⚠️ THIS FIELD IS READ, and a 2026-07-25 commit message wrongly called it
                // dead: TrillSpannerEngraver starts a trill's CONTINUATION segment at
                // `system.PrefixWidth + BoundPadding`, so this is where a trill line begins
                // after a line break — it must be the prefix the renderer actually draws, not
                // the score key's. TabOnlyKeyPrefixTests asserts the same value directly.
                PrefixWidth: SpacingRules.CalculatePrefixWidth(SpacingRules.MaxClefWidth(score),
                    SpacingRules.WidestActiveKeyInk(score, firstMeasureIndex),
                    isFirstSystem && SpacingRules.AnyStaffEngravesTime(score),
                    score.TimeSignature.NumeratorText, score.TimeSignature.DenominatorText),
                Measures: measureLayouts, StaffGroups: sysStaffGroups,
                Indent: sysIndent,
                // The springs the PAGE solves between THIS system's staves.
                // LILYPOND-REF: lily/page-layout-problem.cc:651-720 append_system(), which
                // LilyPond calls once per system and floors out of that system's own
                // skylines. They are built from the same groups and the same measure layouts
                // this system was placed with, which is what makes the floor and the drawn
                // distance the same system's answer — a spring's minimum is reconstructed
                // from the drawn distance (see StaffSprings), so the two cannot come from
                // different systems.
                // ⚠️ THE SKYLINES ARE THE POINT, and passing them is no longer optional:
                // StaffSprings used to have an overload that took none, whose floor fell
                // back to the drawn distance so the pair could stretch but never squeeze.
                // A spring's minimum only binds on a page that COMPRESSES, which is why
                // that overload held a hara-kiri'd score's staves at their ideal 9.000000
                // where the same music without the declaration squeezed to 8.651797
                // (LilyPond's own value: audit/lp-geometry page.compressed.staff-staff-inside).
                // It is gone; the argument is not nullable.
                StaffSprings: multiStaffLayouter.StaffSprings(
                    score, sysStaffGroups, sysStaffSkylines.Skylines, sysRunSources)));
            perSystemRunSources.Add(sysRunSources);
            perSystemStaffSkylines.Add(sysStaffSkylines.Skylines);
            perSystemStaffSpanners.Add(sysStaffSkylines.Spanners);
            perSystemStaffInside.Add(sysStaffSkylines.Inside);
            perSystemPedalLines.Add(sysStaffSkylines.PedalLines);
            perSystemPedalRows.Add(sysStaffSkylines.PedalRows);
            currentY += sysHeight + _options.SystemSpacing;
            firstMeasureIndex += measureCount;
        }

        return new SystemPlacements(
            systems, perSystemExtents, perSystemSkylines, perSystemHeights,
            perSystemLyricBands, perSystemStaffSkylines, perSystemStaffSpanners,
            perSystemStaffInside, perSystemPedalLines, perSystemPedalRows,
            perSystemRunSources);
    }


}
