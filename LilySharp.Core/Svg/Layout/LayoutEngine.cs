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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Engine for calculating score layout.
/// </summary>
internal sealed class LayoutEngine
{
    private readonly LayoutOptions _options;
    private readonly ElementCoordinator _elementCoordinator;
    private readonly SkylineBuilder _skylineBuilder;
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
    public ScoreLayout Layout(MultiStaffScore score, IReadOnlyList<int>? precomputedLineSizes = null,
        SystemLayoutCache? systemCache = null)
    {
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
        double commonShortestDuration = SpacingRules.CalculateCommonShortestDuration(score);

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
        var systemMeasures = _systemBreaker.BreakIntoSystems(score, commonShortestDuration, precomputedLineSizes);

        // Chord symbols on a TEXT ROW (lead sheets) live in their own band and must not
        // inflate a music staff's up-extent; inline chord symbols (nameless `chords { }`) sit
        // above their staff and must. ...and the LYRICS rows among them, which are the ones
        // the loose chain places (a chords row below a staff carries the ChordNames nonstaff-*
        // set and no corpus point measures that arrangement — see SystemAlignment.UnmodelledRow).
        // ⚠️ NEITHER SET IS "THE NON-SPACEABLE LINES" any more: that question is asked of the
        // staff itself, through the `staff-affinity` a StaffLayout now carries (ClassifySystem).
        // ⚠️ BUILT HERE rather than beside its first reader: PageAnchorOffsets needs the lyrics
        // set, and the first system's Y is decided before the paging pass runs.
        var textRowStaves = new HashSet<int>();
        var lyricsRowStaves = new HashSet<int>();
        foreach (var (_, st, gi) in score.EnumerateStaves())
            if (st.IsTextRow)
            {
                textRowStaves.Add(gi);
                if (st.IsLyricsTextRow) lyricsRowStaves.Add(gi);
            }

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
                score, _skylineBuilder, firstSystemMeasureLayouts));
        var firstLooseLines = systemMeasures.Count > 0
            ? BuildLooseLinesBetween(
                score, firstSystemMeasureLayouts, 0, systemMeasures[0].Count)
            : null;
        var firstStaffGroupLayouts = systemMeasures.Count > 0
            ? multiStaffLayouter.LayoutStaffGroups(
                score, firstStaffSkylines, 0, systemMeasures[0].Count, isFirstSystem: true,
                firstLooseLines)
            : multiStaffLayouter.LayoutStaffGroups(
                score, _skylineBuilder, firstSystemMeasureLayouts);
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
            ImmutableArray<MeasureLayout> mls) =>
            (multiStaffLayouter.StaffBeamLayouts(score, edgeFirstStaff, edgeFirstStaffIndex, mls),
             edgeLastStaff != edgeFirstStaff
                ? multiStaffLayouter.StaffBeamLayouts(score, edgeLastStaff, edgeLastStaffIndex, mls)
                : default);

        // Pre-calculate first system skylines for initial Y positioning
        var firstEdgeBeams = EdgeStaffBeams(firstSystemMeasureLayouts);
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(
            score, firstSystemMeasureLayouts, systemHeight, indent,
            firstEdgeBeams.first, firstEdgeBeams.last, firstStaffGroupLayouts);
        var firstAnchor = PageAnchorOffsets(firstStaffGroupLayouts, lyricsRowStaves);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            _options.MarginTop, headerHeight, LayoutUtilities.CalculateUpExtent(firstUpSkyline),
            firstAnchor.HalfFirst, firstAnchor.ToFirst, _options.VerticalSpacing.TopSystem);

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
            FirstLooseLines = firstLooseLines,
            EdgeStaffBeams = EdgeStaffBeams,
            FirstSystemY = currentY,
        });
        var systems = placed.Systems;
        var perSystemExtents = placed.Extents;
        var perSystemSkylines = placed.Skylines;
        var perSystemHeights = placed.Heights;

        // LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
        var perSystemBands = new List<(double bandUp, double bandDown)>();
        var multiMeasureRanges = new List<(int startMeasure, int measureCount)>();
        int multiMeasStart = 0;
        foreach (var sysMeasures in systemMeasures)
        {
            multiMeasureRanges.Add((multiMeasStart, sysMeasures.Count));
            multiMeasStart += sysMeasures.Count;
        }
        var inlineChordNames = score.ChordNames
            .Where(c => !textRowStaves.Contains(c.StaffIndex)).ToImmutableArray();
        AugmentExtentsWithLooseLines(perSystemExtents,
            score.Dynamics, score.FiguredBasses,
            score.MusicMarks, score.VoltaBrackets, multiMeasureRanges,
            inlineChordNames, perSystemBands, placed.LyricBands);

        // Preliminary annotation pass (see the single-staff path): real
        // protrusions of brackets/marks/voltas/dynamics/ties/slurs join the
        // spacing extents before the page Y is fixed.
        var pagingSkylines = RunPreliminaryAnnotationPass(
            score, systems.ToImmutableArray(), perSystemExtents, perSystemSkylines);

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, systemHeight,
            lyricsRowStaves, pagingSkylines, perSystemHeights, perSystemBands);

        var looseChainEnd = BuildLooseChainEnds(
            score, pages, systemsArray, perSystemExtents, lyricsRowStaves);
        var trailingRowStaves = BuildTrailingRowStaves(systemsArray, lyricsRowStaves);

        // Calculate beams/ties/slurs/glissandos per staff
        var (allBeamLayouts, allTieLayouts, allSlurLayouts, allGlissandoLayouts, restShifts) =
            LayoutAllSpanners(score, systemsArray);

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
            chordNames: score.ChordNames)
        {
            TempoText = score.TempoText,
            TempoBeatUnit = score.TempoBeatUnit,
            TempoDots = score.TempoDots,
        };

        var anchors = BuildStaffAnchorTables(score, systemsArray);

        var annotationContext = new AnnotationLayoutContext
        {
            Score = primaryScore,
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
            BeamGroups = _elementCoordinator.DetectBeamGroups(primaryScore),
            BeamLayouts = allBeamLayouts.ToImmutableArray(),
            SystemSkylines = perSystemSkylines,
            TupletForceStemUp = primaryStaff.IsMultiVoice,
            StaffVoices = primaryStaff.Voices,
            VoicesByStaff = anchors.VoicesByStaff,
            MeasuresByStaff = anchors.MeasuresByStaff,
            StaffYByIndex = anchors.StaffYByIndex,
            NoteBoundAnchorY = anchors.NoteBoundAnchorY,
            StaffByIndex = anchors.StaffByIndex,
            LooseChainEnd = looseChainEnd,
            TrailingRowStaves = trailingRowStaves,
            LastSpaceableStaffY = anchors.LastSpaceableStaffY,
            PrefixTimeSignatureX = BuildPrefixTimeSignatureX(score, systemsArray),
        };
        var annotations = CalculateAnnotationLayouts(annotationContext);

        // ...and the rows the lyric chain solved are moved to where it put them, AFTER the
        // pass rather than inside it — see AnnotationLayoutContext.SolvedRowBaselines.
        (systemsArray, annotations) = ApplySolvedRowPositions(
            score, systemsArray, annotations, annotationContext.SolvedRowBaselines);

        var (voiceOffsets, headWipes, dotForceDown, partCombineLayouts) =
            CalculateVoiceCollisions(score, systemsArray);

        var result = BuildScoreLayout(pages, systemsArray,
            allBeamLayouts.ToImmutableArray(), allTieLayouts.ToImmutableArray(),
            allSlurLayouts.ToImmutableArray(), allGlissandoLayouts.ToImmutableArray(),
            annotations,
            voiceOffsets,
            headWipes,
            dotForceDown,
            restShifts,
            partCombineLayouts);
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
        public required List<(VerticalSkyline Up, VerticalSkyline Down)> FirstStaffSkylines { get; init; }
        /// <summary>System 0's loose-line lookup, built by the caller alongside its placement
        /// so the springs below are floored against the SAME alignment that drew it.</summary>
        public MultiStaffLayouter.LooseLinesBetween? FirstLooseLines { get; init; }
        public required Func<ImmutableArray<MeasureLayout>,
            (ImmutableArray<BeamLayout> first, ImmutableArray<BeamLayout> last)> EdgeStaffBeams { get; init; }
        public required double FirstSystemY { get; init; }
    }

    /// <summary>What the per-system pass produces, index-aligned by system.</summary>
    private readonly record struct SystemPlacements(
        List<SystemLayout> Systems,
        List<(double upExtent, double downExtent)> Extents,
        List<(VerticalSkyline up, VerticalSkyline down)> Skylines,
        List<double> Heights,
        /// <summary>Per system, its loose lyric block's ALIGNMENT MINIMUM below the last
        /// spaceable staff's bottom line — the note-bound verses AND the independent rows
        /// standing under them; see <see cref="LyricReservationBelowSystem"/>. Produced here
        /// because only this pass has the system's own staff skylines.</summary>
        List<double> LyricBands);

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
        var perSystemLyricBands = new List<double>();
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
                        score, _skylineBuilder, measureLayouts));

            // THIS system's placement, from ITS music. LILYPOND-REF:
            // lily/align-interface.cc:217-268 — each System has its own VerticalAlignment and
            // is spaced against its own staves' skylines, so a system whose ink reaches
            // between the staves gets more room and its neighbours do not. Hara-kiri needs no
            // branch here: which staves survive is one more per-system input, passed as the
            // measure range (LayoutStaffGroups takes liveness as a predicate, not as a mode).
            // The alignment's loose lines between THIS system's staves — the block whose ink
            // the room between two staves is walked from. Built once and handed to both the
            // placement and the springs, for the same reason the staff skylines are.
            var sysLooseLines = isFirstSystem
                ? ctx.FirstLooseLines
                : BuildLooseLinesBetween(
                    score, measureLayouts, firstMeasureIndex, firstMeasureIndex + measureCount);

            var sysStaffGroups = isFirstSystem
                ? firstStaffGroupLayouts
                : multiStaffLayouter.LayoutStaffGroups(
                    score, sysStaffSkylines,
                    firstMeasureIndex, firstMeasureIndex + measureCount, isFirstSystem,
                    sysLooseLines);

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
                    var edgeBeams = EdgeStaffBeams(measureLayouts);
                    return _skylineBuilder.BuildSystemSkylines(score, measureLayouts, sysHeight, sysIndent,
                        edgeBeams.first, edgeBeams.last, sysStaffGroups);
                });
            perSystemSkylines.Add((upSky, downSky));
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSky),
                LayoutUtilities.CalculateDownExtent(downSky, sysHeight)));
            perSystemHeights.Add(sysHeight);
            perSystemLyricBands.Add(LyricReservationBelowSystem(
                score, measureLayouts, sysStaffSkylines, sysStaffGroups,
                firstMeasureIndex, firstMeasureIndex + measureCount));

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
                    isFirstSystem && !score.AllStavesTab,
                    score.TimeSignature.Beats, score.TimeSignature.BeatType),
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
                    score, sysStaffGroups, sysStaffSkylines, sysLooseLines)));
            currentY += sysHeight + _options.SystemSpacing;
            firstMeasureIndex += measureCount;
        }

        return new SystemPlacements(
            systems, perSystemExtents, perSystemSkylines, perSystemHeights,
            perSystemLyricBands);
    }


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
    private List<(VerticalSkyline up, VerticalSkyline down)>? RunPreliminaryAnnotationPass(
        MultiStaffScore score, ImmutableArray<SystemLayout> prelimSystems,
        List<(double upExtent, double downExtent)> perSystemExtents,
        List<(VerticalSkyline up, VerticalSkyline down)> perSystemSkylines)
    {
        var prelimStaff = score.PrimaryContentStaff;
        var prelimScore = new Score(
            prelimStaff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            ClefToString(prelimStaff.Clef), score.Tempo, score.Title, score.Composer,
            tupletBrackets: score.TupletBrackets)
        {
            TempoText = score.TempoText,
            TempoBeatUnit = score.TempoBeatUnit,
            TempoDots = score.TempoDots,
        };
        var prelimBeams = new List<BeamLayout>();
        var prelimTies = new List<TieLayout>();
        var prelimSlurs = new List<SlurLayout>();
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            // Beam detection breaks at tuplet boundaries by note index, so a
            // per-staff beam score must see only THIS staff's tuplets — else a
            // tuplet on another staff would split a beam at a colliding index.
            var staffTuplets = StaffTuplets(score.TupletBrackets, staffIndex);
            var staffScore = new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: staffTuplets);
            // Beams detect per voice — expose every voice so voice 2's beam
            // protrusions join the spacing extents (matches the final pass).
            // Ties/slurs keep the primary-voice prelim score (unchanged).
            var staffBeamScore = staff.Voices.Length > 1
                ? new Score(
                    staff.Voices, score.TimeSignature, score.KeySignature,
                    ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                    tupletBrackets: staffTuplets)
                : staffScore;
            var staffPrelimBeams = _elementCoordinator.LayoutBeams(staffBeamScore, prelimSystems, staffIndex);
            prelimBeams.AddRange(staffPrelimBeams);
            prelimTies.AddRange(_elementCoordinator.LayoutTies(staffScore, prelimSystems, staffIndex, staff));
            prelimSlurs.AddRange(_elementCoordinator.LayoutSlurs(staffScore, prelimSystems, staffIndex, staff, score.GraceNotes, staffPrelimBeams));
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

        var prelimAnn = CalculateAnnotationLayouts(new AnnotationLayoutContext
        {
            Score = prelimScore,
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
            BeamGroups = _elementCoordinator.DetectBeamGroups(prelimScore),
            BeamLayouts = prelimBeams.ToImmutableArray(),
            SystemSkylines = perSystemSkylines,
            TupletForceStemUp = prelimStaff.IsMultiVoice,
            StaffVoices = prelimStaff.Voices,
            VoicesByStaff = prelimVoicesByStaff,
            MeasuresByStaff = prelimMeasuresByStaff,
            StaffYByIndex = prelimStaffYByIndex,
            StaffByIndex = prelimStaffByIndex,
            PrefixTimeSignatureX = BuildPrefixTimeSignatureX(score, prelimSystems),
        });
        EnrichExtentsWithAnnotationProtrusions(perSystemExtents, prelimSystems,
            prelimAnn, prelimTies.ToImmutableArray(), prelimSlurs.ToImmutableArray());
        return AugmentSkylinesForPaging(
            perSystemSkylines, prelimAnn.Articulations, prelimAnn.FiguredBasses,
            prelimAnn.VoltaBrackets, prelimSystems,
            prelimAnn.MusicMarks, prelimAnn.CustomTexts, prelimAnn.ChordNames,
            prelimAnn.BarNumbers, prelimAnn.TupletBrackets, prelimSlurs.ToImmutableArray(),
            prelimTies.ToImmutableArray());
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

        // Anchor Y for note-bound lyrics attached to a staff in a NON-LAST staff group:
        // the group's BOTTOM staff Y (Y-up ⇒ min, reflected to device-down like
        // staffYByIndex above), so `staff X with lyrics …` followed
        // by another staff sits in the inter-group gap below its group. Staves in the LAST
        // group are omitted ⇒ their lyrics keep the legacy below-the-whole-system
        // placement — which deliberately includes a grand staff's staves (one group), so
        // an SATB chorale's lyrics still sit below the whole grand staff.
        // ⚠️ "LAST" MEANS THE LAST SPACEABLE GROUP, not the last group in the array, and the
        // difference is a whole run of the alignment. Spaceable is the staff's own
        // `staff-affinity` and nothing else (see lastSpaceableStaffY below), so an ossia —
        // which declares none — counts here as the staff it is.
        // A text ROW is a group of its own in
        // Lily#'s model, so counting it would make `staff X with lyrics a` + `lyrics b` put
        // X's syllables in the INTER-GROUP gap while the row is solved below the system —
        // two chains, one room, and the two blocks drawn on top of each other (MEASURED:
        // they land on the same baseline). LilyPond has no such split: everything
        // non-spaceable under the last spaceable staff is ONE run
        // (page-layout-problem.cc:919-925), and a Lyrics context is non-spaceable, so it
        // cannot end one. Same predicate as lastSpaceableStaffY below.
        var noteBoundAnchorY = new Dictionary<int, double>();
        if (systemsArray.Length > 0 && !systemsArray[0].StaffGroups.IsDefaultOrEmpty)
        {
            var groups = systemsArray[0].StaffGroups;
            int lastSpaceableGroup = -1;
            for (int gi = 0; gi < groups.Length; gi++)
            {
                if (groups[gi].Staves.IsDefaultOrEmpty) continue;
                foreach (var st in groups[gi].Staves)
                    if (!st.IsHidden && StaffAffinity.IsSpaceable(st.StaffAffinity))
                    {
                        lastSpaceableGroup = gi;
                        break;
                    }
            }
            for (int gi = 0; gi < lastSpaceableGroup; gi++)
            {
                if (groups[gi].Staves.IsDefaultOrEmpty) continue;
                double bottomY = -groups[gi].Staves.Min(s => s.Y);
                foreach (var st in groups[gi].Staves)
                    noteBoundAnchorY[st.StaffIndex] = bottomY;
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
        double lastSpaceableStaffY = 0;
        if (systemsArray.Length > 0 && !systemsArray[0].StaffGroups.IsDefaultOrEmpty)
        {
            bool found = false;
            foreach (var group in systemsArray[0].StaffGroups)
            {
                if (group.Staves.IsDefaultOrEmpty) continue;
                foreach (var st in group.Staves)
                {
                    if (st.IsHidden || !StaffAffinity.IsSpaceable(st.StaffAffinity))
                        continue;
                    double down = -st.Y;
                    if (!found || down > lastSpaceableStaffY)
                    {
                        lastSpaceableStaffY = down;
                        found = true;
                    }
                }
            }
        }

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
    private (List<BeamLayout> Beams, List<TieLayout> Ties, List<SlurLayout> Slurs, List<GlissandoLayout> Glissandos,
             ImmutableDictionary<RestShiftKey, double> RestShifts)
        LayoutAllSpanners(MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray)
    {
        var allBeamLayouts = new List<BeamLayout>();
        var allTieLayouts = new List<TieLayout>();
        var allSlurLayouts = new List<SlurLayout>();
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
            // Beam detection breaks at tuplet boundaries by note index, so scope
            // the tuplets to THIS staff — a tuplet on another staff must not split
            // this staff's beams at a colliding index.
            var staffTuplets = StaffTuplets(score.TupletBrackets, staffIndex);
            var staffScore = new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                // Beam detection must see tuplet spans: auto beams break at
                // tuplet boundaries (BeamDetector).
                tupletBrackets: staffTuplets);
            // Beam AND slur/tie/glissando detection run PER VOICE, so a polyphonic
            // staff must expose all its voices (not just the primary) — else voice 2's
            // eighths never beam. Single-voice staves reuse the primary-voice score,
            // so their layout is unchanged.
            var staffSpannerScore = staff.Voices.Length > 1
                ? new Score(
                    staff.Voices, score.TimeSignature, score.KeySignature,
                    ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                    tupletBrackets: staffTuplets)
                : staffScore;
            var staffFinalBeams = _elementCoordinator.LayoutBeams(staffSpannerScore, systemsArray, staffIndex);
            allBeamLayouts.AddRange(staffFinalBeams);
            // Push beamed rests clear of their beam (Beam::rest_collision_callback).
            var staffRestShifts = _elementCoordinator.CalculateRestShifts(
                staffScore, systemsArray, staffFinalBeams.ToImmutableArray());
            foreach (var kv in staffRestShifts)
                if (!restShiftsBuilder.TryGetValue(kv.Key, out var existing)
                    || Math.Abs(kv.Value) > Math.Abs(existing))
                    restShiftsBuilder[kv.Key] = kv.Value;
            allTieLayouts.AddRange(_elementCoordinator.LayoutTies(staffSpannerScore, systemsArray, staffIndex, staff));
            allSlurLayouts.AddRange(_elementCoordinator.LayoutSlurs(staffSpannerScore, systemsArray, staffIndex, staff, score.GraceNotes, staffFinalBeams));
            allGlissandoLayouts.AddRange(_elementCoordinator.LayoutGlissandos(staffSpannerScore, systemsArray, staffIndex));
        }
        return (allBeamLayouts, allTieLayouts, allSlurLayouts, allGlissandoLayouts,
                restShiftsBuilder.ToImmutable());
    }

    // F3/S5-3a: route a system's measure layout through the session cache when one
    // is installed (single-staff incremental path). Null cache => direct compute,
    // byte-identical to the non-incremental path.
    private static ImmutableArray<MeasureLayout> ComputeSystemMeasures(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration,
        Func<ImmutableArray<MeasureLayout>> compute)
        => cache == null
            ? compute()
            : cache.GetOrComputeMeasures(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
                indent, commonShortestDuration, compute);

    // Route a system's PER-STAFF skylines through the session cache. They became a
    // per-system cost when the placement did (see the loop above); before that one list
    // served the whole score, so there was nothing worth memoising. On a fifty-system
    // score a one-note edit rebuilt all fifty without this. Null cache => direct compute,
    // byte-identical to the non-incremental path.
    private static List<(VerticalSkyline Up, VerticalSkyline Down)> ComputeStaffSkylines(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration,
        Func<List<(VerticalSkyline Up, VerticalSkyline Down)>> compute)
        => cache == null
            ? compute()
            : cache.GetOrComputeStaffSkylines(firstMeasureIndex, measureCount, isFirstSystem,
                isLastSystem, indent, commonShortestDuration, compute);

    // F3/S5-3c: route a system's skyline through the session cache (the dominant
    // per-system cost, esp. multi-staff). Keyed additionally on systemHeight.
    private static (VerticalSkyline up, VerticalSkyline down) ComputeSystemSkyline(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration, double systemHeight,
        Func<(VerticalSkyline up, VerticalSkyline down)> compute)
        => cache == null
            ? compute()
            : cache.GetOrComputeSkyline(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
                indent, commonShortestDuration, systemHeight, compute);

    private (ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems) CreatePages(
        ImmutableArray<SystemLayout> systems, double headerHeight,
        List<(double upExtent, double downExtent)> perSystemExtents, double systemHeight,
        IReadOnlySet<int> lyricsRowStaves,
        List<(VerticalSkyline up, VerticalSkyline down)>? perSystemSkylines = null,
        List<double>? perSystemHeights = null,
        List<(double bandUp, double bandDown)>? perSystemBands = null)
    {
        // Whole-line annotation bands (lyric lines below, chord-symbol rows
        // above). They lay out only after the page Y is fixed, so they are
        // absent from the skylines — the skyline distance must be floored by
        // them or adjacent systems overprint them (found by the Greensleeves
        // sample). Local annotations (dynamics, ties, …) are NOT banded: the
        // X-aware skyline distance is the better model for those.
        (double up, double down) AnnBand(int i) =>
            perSystemBands == null || i >= perSystemBands.Count
                ? (0, 0)
                : (perSystemBands[i].bandUp, perSystemBands[i].bandDown);
        // Per-system body height, defaulting to the scalar systemHeight when the
        // caller has none (single-staff path, or no hara-kiri) — in that case every
        // entry equals systemHeight, so the result is byte-identical.
        double SysHeight(int i) =>
            perSystemHeights != null && i >= 0 && i < perSystemHeights.Count
                ? perSystemHeights[i]
                : systemHeight;
        // An empty score (no systems) has nothing to page; return empty rather than
        // indexing perSystemExtents[0] below.
        if (systems.IsDefaultOrEmpty || perSystemExtents.Count == 0)
            return (ImmutableArray<PageLayout>.Empty, ImmutableArray<SystemLayout>.Empty);

        // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
        // Pass per-system skylines for X-dependent inter-system collision detection
        (ImmutableArray<PageLayout>, ImmutableArray<SystemLayout>) OptimalPages()
        {
            var skylines = perSystemSkylines != null
                ? (ImmutableArray<(VerticalSkyline, VerticalSkyline)>?)perSystemSkylines.ToImmutableArray()
                : null;
            // The refpoint frame every page spring is written in, per system — see
            // PageAnchorOffsets. Computed here because the SELECTION it rests on is
            // ClassifySystem's, which needs to know which rows this port solves; the page
            // layouter is handed the answer for the same reason it is handed the body heights.
            var anchors = systems
                .Select(s => PageAnchorOffsets(s.StaffGroups, lyricsRowStaves))
                .ToImmutableArray();
            var pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systems, headerHeight, perSystemExtents.ToImmutableArray(), skylines,
                perSystemBands?.ToImmutableArray(), perSystemHeights, anchors);
            return (pages, pages.SelectMany(p => p.Systems).ToImmutableArray());
        }

        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
            return OptimalPages();

        // Recalculate Y positions using skyline extents to avoid overlaps
        var pageAnchor = PageAnchorOffsets(systems[0].StaffGroups, lyricsRowStaves);
        double skylineY = LayoutUtilities.CalculateFirstSystemY(
            _options.MarginTop, headerHeight, perSystemExtents[0].upExtent,
            pageAnchor.HalfFirst, pageAnchor.ToFirst, _options.VerticalSpacing.TopSystem);
        var updatedSystems = new List<SystemLayout>();
        for (int i = 0; i < systems.Length; i++)
        {
            updatedSystems.Add(systems[i] with { Y = skylineY });
            if (i < systems.Length - 1)
            {
                // LILYPOND-REF: ly/paper-defaults-init.ly:62-65 system-system-spacing —
                // the pair's padding is 1, its minimum-distance 8 and its basic-distance
                // 12, and page-layout-problem.cc:625-632 uses exactly those. This path
                // used to invent `SystemSpacing * 0.5` (= 4) instead, four times
                // LilyPond's padding, which made the skyline term bind on scores where
                // LilyPond's does not — it was invisible while the skylines were thin,
                // and surfaced the moment the clef joined them.
                var pairSpec = _options.VerticalSpacing.SystemSystem;

                // Reference-to-reference distance to the next system. Prefer the
                // X-dependent skyline distance (the same measure the optimal page
                // path uses); the scalar sum below adds this system's deepest
                // downward protrusion to the next system's tallest upward one on
                // ANY X, so it spaces systems too far apart when those protrusions
                // do not actually overlap horizontally. Distance() is the true
                // per-X minimum clearance, so flooring it by systemHeight +
                // SystemSpacing (staff bodies never touch) and adding padding can
                // never introduce an overlap — only close a false gap.
                // Distance() returns -inf for an empty skyline; the scalar sum is
                // then kept, which is byte-identical to the previous behaviour.
                // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline;
                //   lily/skyline.cc Skyline::distance.
                double skylineDistance = SysHeight(i)
                    + perSystemExtents[i].downExtent + perSystemExtents[i + 1].upExtent;
                if (perSystemSkylines != null && i + 1 < perSystemSkylines.Count)
                {
                    // LILYPOND-REF: lily/page-layout-problem.cc:618-629 — measured
                    // with the System grob's skyline-horizontal-padding (1.0).
                    double dist = perSystemSkylines[i + 1].up.Distance(
                        perSystemSkylines[i].down,
                        EngravingDefaults.SystemSkylineHorizontalPadding);
                    if (!double.IsNegativeInfinity(dist))
                    {
                        // A whole-line annotation band clears against the OTHER
                        // side's full extent (the band spans every X, so the
                        // X-disjoint argument for preferring Distance() does not
                        // apply to it).
                        var annPrev = AnnBand(i);
                        var annNext = AnnBand(i + 1);
                        if (annPrev.down > 0)
                            dist = Math.Max(dist, SysHeight(i) + annPrev.down
                                + perSystemExtents[i + 1].upExtent);
                        if (annNext.up > 0)
                            dist = Math.Max(dist, SysHeight(i)
                                + perSystemExtents[i].downExtent + annNext.up);
                        skylineDistance = dist;
                    }
                }

                // LILYPOND-REF: lily/page-layout-problem.cc:625-632 + spring.cc:219-237 —
                // the ink is a FLOOR under the spring, and at force 0 (which is what an
                // unjustified single page runs at) the spring is
                // max(min_distance, ideal_distance). Same shape as PageLayouter's chain.
                double minDistance = Math.Max(
                    pairSpec.MinimumDistance, skylineDistance + pairSpec.Padding);
                skylineY += Math.Max(pairSpec.BasicDistance, minDistance);
            }
        }
        double lastDownExtent = perSystemExtents[systems.Length - 1].downExtent;
        double totalHeight = skylineY + SysHeight(systems.Length - 1) + lastDownExtent + _options.MarginBottom;

        // Auto-pagination: a score that FITS one page keeps this simple layout
        // (byte-identical to the historical single-page output); one that
        // overflows the paper height re-runs through the optimal page breaker
        // and splits across real pages, like LilyPond always does.
        if (_options.PageHeight > 0 && totalHeight > _options.PageHeight)
            return OptimalPages();

        // Stage-4 W2-core: the loop above accumulated each system's top DOWNWARD
        // (device) to size the page; store the final origins as page Y-up (UP from
        // the page bottom) by reflecting through the now-known totalHeight. This is
        // the single-page producer seam — after it, SystemLayout.Y is Y-up and the
        // renderer's YFlip is the only device conversion left.
        var systemsArray = updatedSystems
            .Select(s => s with { Y = totalHeight - s.Y })
            .ToImmutableArray();
        var page = new PageLayout(0, _options.PageWidth, totalHeight, headerHeight, systemsArray);
        return (ImmutableArray.Create(page), systemsArray);
    }

    /// <summary>
    /// Estimates the additional down extent contributed by loose lines (lyrics, dynamics, figured bass)
    /// for a range of measures in a system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
    /// LILYPOND-REF: lily/axis-group-interface.cc:138-173 pure_height estimation
    /// LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside-staff-priority
    ///
    /// In LilyPond, non-spaceable staves (lyrics, dynamics, figured bass) are "loose lines"
    /// that are distributed between spaceable staves after the main vertical spring calculation.
    /// We estimate their height contribution so that page breaking accounts for them.
    ///
    /// Pure height estimation covers both below-staff (downExtent) and above-staff (upExtent)
    /// elements so that page breaking can accurately predict system heights.
    /// </remarks>
    private static (double downExtent, double upExtent, double bandDown, double bandUp) EstimateLooseLineExtents(
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        int startMeasure, int endMeasure,
        ImmutableArray<ChordNameItem> chordNames = default)
    {
        double downExtent = 0;
        double upExtent = 0;
        // Whole-line bands: annotation classes that span the system's full
        // width (lyric lines below, chord-symbol rows above). These floor the
        // inter-system skyline distance — see FloorDistance in CreatePages.
        double bandDown = 0;
        double bandUp = 0;

        // LILYPOND-REF: scm/define-grobs.scm ChordName (a TextScript-class grob
        // above the staff). Inline chord symbols (nameless `chords { }`) sit
        // above the staff: staffPadding(~1.4) + text height(~1.6).
        if (!chordNames.IsDefaultOrEmpty)
        {
            foreach (var cn in chordNames)
            {
                if (cn.MeasureIndex >= startMeasure && cn.MeasureIndex < endMeasure)
                {
                    upExtent = Math.Max(upExtent, 3.0);
                    bandUp = Math.Max(bandUp, 3.0);
                    break;
                }
            }
        }

        // --- Below-staff elements (downExtent) ---

        // ⚠️ NO LYRIC BRANCH HERE ANY MORE. The block's reservation is
        // LyricReservationBelowSystem — the alignment's own walk over the real syllable ink
        // and the real staff skyline — and the caller max-es it into the same two figures
        // this returns. An estimate built from the items alone cannot see either, and while
        // it existed it was a SECOND model of the walk (HANDOFF 5.2.1②).

        // LILYPOND-REF: scm/define-grobs.scm DynamicLineSpanner.outside-staff-priority = 250
        // Dynamics + hairpins: staffPadding(0.2) + padding(0.6) + textAscent(1.2) = 2.0
        bool hasDynamic = false;
        if (!dynamics.IsDefaultOrEmpty)
        {
            foreach (var dyn in dynamics)
            {
                if (dyn.MeasureIndex >= startMeasure && dyn.MeasureIndex < endMeasure)
                {
                    hasDynamic = true;
                    break;
                }
            }
            if (hasDynamic)
                downExtent = Math.Max(downExtent, 2.0);
        }

        // LILYPOND-REF: scm/define-grobs.scm Hairpin — same DynamicLineSpanner (priority 250)
        // Hairpins share Y level with dynamics; estimate ~1.5 ss if no dynamics
        if (!musicMarks.IsDefaultOrEmpty && !hasDynamic)
        {
            bool hasHairpin = false;
            foreach (var mark in musicMarks)
            {
                if (mark.MeasureIndex >= startMeasure && mark.MeasureIndex < endMeasure
                    && (mark.Type == MusicMarkType.Cresc || mark.Type == MusicMarkType.Decresc
                        || mark.Type == MusicMarkType.Dim))
                {
                    hasHairpin = true;
                    break;
                }
            }
            if (hasHairpin)
                downExtent = Math.Max(downExtent, 1.5);
        }

        // LILYPOND-REF: scm/define-grobs.scm BassFigure
        // Figured bass: staffPadding(1.0) + belowStaffOffset(1.0) + figCount * figureSpacing(1.5)
        if (!figuredBasses.IsDefaultOrEmpty)
        {
            int maxFigures = 0;
            foreach (var fb in figuredBasses)
            {
                if (fb.MeasureIndex >= startMeasure && fb.MeasureIndex < endMeasure)
                    maxFigures = Math.Max(maxFigures, fb.Figures.Length);
            }
            if (maxFigures > 0)
                downExtent = Math.Max(downExtent, 2.0 + maxFigures * 1.5);
        }

        // --- Above-staff elements (upExtent) ---

        if (!musicMarks.IsDefaultOrEmpty)
        {
            foreach (var mark in musicMarks)
            {
                if (mark.MeasureIndex < startMeasure || mark.MeasureIndex >= endMeasure)
                    continue;

                // The metronome mark rests at staff ink + its padding 0.8 and its ink
                // tops out at the \smaller note's stem; stacking can only lift it.
                // LILYPOND-REF: scm/define-grobs.scm:2346 MetronomeMark outside-staff-priority
                if (mark.Type == MusicMarkType.Tempo)
                {
                    var tInk = MetronomeMarkGeometry.Ink(mark.Text, mark.TempoText,
                        mark.TempoBeatUnit, mark.TempoDots, mark.SwingSubdivision);
                    upExtent = Math.Max(upExtent,
                        MetronomeMarkGeometry.QuietBaselineAboveMiddle(tInk.Bottom)
                        - 2.0 + tInk.Top);
                }

                // LILYPOND-REF: scm/define-grobs.scm RehearsalMark.outside-staff-priority = 1500
                if (mark.Type == MusicMarkType.Rehearsal)
                    upExtent = Math.Max(upExtent, 3.0); // boxed rehearsal mark

                // LILYPOND-REF: scm/define-grobs.scm SectionLabel
                if (mark.Type == MusicMarkType.SectionLabel)
                    upExtent = Math.Max(upExtent, 3.5); // boxed section label

                // LILYPOND-REF: scm/define-grobs.scm SegnoMark/CodaMark
                if (mark.Type == MusicMarkType.Segno || mark.Type == MusicMarkType.Coda)
                    upExtent = Math.Max(upExtent, 2.5); // glyph above staff
            }
        }

        // LILYPOND-REF: scm/define-grobs.scm VoltaBracketSpanner.outside-staff-priority = 600
        if (!voltaBrackets.IsDefaultOrEmpty)
        {
            foreach (var vb in voltaBrackets)
            {
                if (vb.StartMeasureIndex < endMeasure && vb.EndMeasureIndex >= startMeasure)
                {
                    upExtent = Math.Max(upExtent, 2.0); // volta bracket height
                    break;
                }
            }
        }

        return (downExtent, upExtent, bandDown, bandUp);
    }

    /// <summary>
    /// Augments per-system extents with estimated loose line heights.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:138-173 pure_height
    /// Estimates both above-staff (upExtent) and below-staff (downExtent)
    /// contributions from annotations, so page breaking accounts for full system height.
    /// </remarks>

    /// <summary>
    /// Measures each system's REAL vertical protrusions from a preliminary
    /// annotation pass and max-merges them into the spacing extents. The
    /// provisional systems already carry final X geometry — only the page Y
    /// changes afterwards — so slurs, ties, tuplet brackets, marks and
    /// dynamics can be laid out, measured, and discarded; the final pass
    /// recomputes them against the re-spaced systems.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1070-1127
    /// build_system_skyline — page spacing reads COMPLETE system stencils
    /// (slurs, brackets, scripts included), not just note skylines.
    /// </remarks>
    private static void EnrichExtentsWithAnnotationProtrusions(
        List<(double upExtent, double downExtent)> perSystemExtents,
        ImmutableArray<SystemLayout> systems,
        AnnotationLayouts ann,
        ImmutableArray<TieLayout> ties,
        ImmutableArray<SlurLayout> slurs)
    {
        int n = Math.Min(perSystemExtents.Count, systems.Length);
        var up = new double[n];
        var down = new double[n];

        var measureToSystem = new Dictionary<int, int>();
        var bottoms = new double[n];
        for (int i = 0; i < n; i++)
        {
            foreach (var m in systems[i].Measures)
                measureToSystem[m.MeasureIndex] = i;
            // System bottom relative to its top: last visible staff's bottom
            // (4.0 for a single staff).
            bottoms[i] = 4.0;
            if (!systems[i].StaffGroups.IsDefaultOrEmpty)
            {
                foreach (var g in systems[i].StaffGroups)
                    foreach (var st in g.Staves)
                        if (!st.IsHidden)
                            bottoms[i] = Math.Max(bottoms[i], st.Height - st.Y);
            }
        }

        void Add(int measureIndex, double topRel, double bottomRel)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            up[s] = Math.Max(up[s], -topRel);
            down[s] = Math.Max(down[s], bottomRel - bottoms[s]);
        }

        /// <summary>The up half alone — for a grob whose DOWN reservation is somebody
        /// else's (a note-bound lyric line, whose depth is its alignment minimum).</summary>
        void AddUpOnly(int measureIndex, double topRel)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            up[s] = Math.Max(up[s], -topRel);
        }

        foreach (var t in ann.TupletBrackets)
        {
            // t.*YUp is Y-up from the system top; this pass is system-relative device.
            double startY = -t.StartYUp;
            double endY = -t.EndYUp;
            double hi = Math.Min(startY, endY);
            double lo = Math.Max(startY, endY);
            Add(t.MeasureIndex, hi - (t.IsStemUp ? 1.6 : 0.1), lo + (t.IsStemUp ? 0.7 : 1.7));
        }
        foreach (var v in ann.VoltaBrackets)
        {
            // YUp is Y-up from the system top; this extent pass is system-relative
            // device (down+), which is exactly -YUp.
            double vY = -v.YUp;
            Add(v.StartMeasureIndex, vY - 0.1, vY + 1.6);
        }
        foreach (var m in ann.MusicMarks)
        {
            if (MusicMarkItem.IsSpannerHandled(m.MarkType))
                continue;
            // m.YUp is Y-up above the top-staff middle; system-relative device
            // (down+ from the system top) is 2 − YUp (the middle sits at device 2).
            double mY = 2.0 - m.YUp;
            Add(m.MeasureIndex, mY - 2.1, mY + 0.7);
        }
        foreach (var ct in ann.CustomTexts)
        {
            // ct.YUp is Y-up above the top-staff middle; system-relative device is 2 − YUp.
            double ctY = 2.0 - ct.YUp;
            Add(ct.MeasureIndex, ctY - 1.8, ctY + 0.6);
        }
        // Chord names ride above the staff and rise (ChordNameEngraver skyline) to
        // clear high notes; their REAL text top must join the system up-extent or a
        // lifted chord line pokes into the header/title. Chord font = FontSize*0.65
        // (≈2.6 ss), Middle-anchored, so the glyph top is cap-height/2 ≈ 0.9 above
        // the anchor and the descent ≈ 0.3 below.
        foreach (var cn in ann.ChordNames)
        {
            // cn.YUp is Y-up from the system top; the system-relative device Y (old
            // cn.Y) is its negation.
            double cnY = -cn.YUp;
            Add(cn.MeasureIndex, cnY - 1.9, cnY + 0.3);
        }
        // Lyric text (staff-bound AND row): the ascender rises ~2.11 ss above
        // the baseline at the 3.2 ss lyric font — without it, a first system
        // whose top content is a lyrics/chord ROW grazes the title ink.
        //
        // ⚠️ THE DOWN HALF IS THE ROW'S ONLY. A note-bound line's down reservation is the
        // ALIGNMENT MINIMUM (LyricReservationBelowSystem), not the distance it is DRAWN —
        // and this pass sees the DRAWN one, laid out at force 0, i.e. at
        // nonstaff-relatedstaff-spacing's basic-distance 5.500000. LilyPond reserves the
        // minimum: page-layout-problem.cc:593-599 hands build_system_skyline the minimum
        // translations, and align-interface.cc:235-238 adds basic-distance only behind the
        // pure branch, which that call is not.
        //
        // ⚠️ IT USED TO ADD BOTH, AND THE DRAWN ONE WON. MEASURED 2026-07-27 by
        // perturbation: suppressing this down half for non-row lines moved 13 snapshots
        // (07-lead-sheet, 08-chorale and 11 test/lyrics-*) and no ledger entry, while
        // zeroing the alignment-minimum band moved a DISJOINT set (two system-gap entries
        // and test/lyrics-volta). So the two models bound on different books and the
        // drawn one silently overrode the ported one wherever they met.
        //
        // ⚠️ A LYRICS ROW KEEPS ITS DRAWN EXTENT, AND THAT IS LILYSHARP-OWN, not a second
        // reading of LilyPond. To LilyPond a row is a loose line like any other and its
        // reservation is the same alignment minimum; Lily# places it as an independent
        // staff-like BAND instead (HANDOFF 3, a decided divergence), so it has no alignment
        // minimum to prefer — the drawn extent is the only figure that exists for it. The
        // day that decision is revisited, this branch goes with it.
        // Its UP half is kept for every line — a first system whose top content is a
        // lyrics/chord row would otherwise graze the title ink. ⚠️ For a note-bound line the
        // up half is INERT (the line sits below the staff, so 2.11 - lyY is negative); it is
        // called anyway so the two branches read as one rule with one exception, and so that
        // a future line placed ABOVE its staff is not silently dropped.
        foreach (var lyLay in ann.Lyrics)
        {
            // lyLay.YUp is Y-up from the system top; the system-relative device
            // baseline (old lyLay.Y) is its negation.
            double lyY = -lyLay.YUp;
            if (lyLay.Item.IsLyricsRow)
                Add(lyLay.Item.MeasureIndex, lyY - 2.11, lyY + 0.9);
            else
                AddUpOnly(lyLay.Item.MeasureIndex, lyY - 2.11);
        }
        foreach (var tr in ann.TrillSpanners)
        {
            // tr.YUp is Y-up from the system top; this pass is system-relative device.
            // The "tr" glyph rides stencil-offset (0 . -1) below the LINE tr.YUp
            // anchors (DrawTrillSpanners), so a glyph-bearing piece's drawn ink is
            // (glyphTop − offset) up and offset down — LilyPond's own ext (-1.0 . 1.1)
            // — and a glyphless continuation carries just the wave.
            bool trHasGlyph = tr.GlyphX < tr.LineStartX;
            double trWave = EngravingDefaults.TrillWaveAmplitude
                + EngravingDefaults.StaffLineThickness / 2.0;
            double trY = -tr.YUp;
            Add(tr.StartMeasureIndex,
                trY - (trHasGlyph
                    ? GlyphMetrics.OrnTrillGlyph.Top - EngravingDefaults.TrillSpannerTextOffsetDown
                    : trWave),
                trY + (trHasGlyph ? EngravingDefaults.TrillSpannerTextOffsetDown : trWave));
        }
        // Figured-bass rows hang below the staff; a skyline-dropped row must
        // widen the gap to the NEXT system, or its digits print through that
        // system's volta boxes / high notes (showcase/04).
        foreach (var fb in ann.FiguredBasses)
        {
            // YUp is Y-up; this extent pass is system-relative device, so reconstruct
            // against this figure's own staff offset (0 for a single/top staff).
            double fbOff = measureToSystem.TryGetValue(fb.MeasureIndex, out int fbSys)
                ? LayoutUtilities.StaffOffsetInSystemDown(systems[fbSys], fb.StaffIndex)
                : 0;
            double fbY = fbOff + (2.0 - fb.YUp);
            Add(fb.MeasureIndex,
                fbY - FiguredBassEngraver.FigureTopExtent,
                fbY + (fb.FigureTexts.Length - 1) * FiguredBassEngraver.FigureSpacing + 0.5);
        }
        // Note-bound scripts (a fermata over the top staff, a staccatissimo
        // under the bottom) extend the system silhouette like any other
        // annotation; Ink is the glyph's real box about its anchor (Y-up).
        foreach (var a in ann.Articulations)
        {
            // a.YUp is Y-up above the staff middle; system-relative device is 2 − YUp.
            double aY = 2.0 - a.YUp;
            Add(a.MeasureIndex, aY - a.Ink.Top, aY - a.Ink.Bottom);
        }
        foreach (var d in ann.Dynamics)
        {
            // d.YUp is Y-up above the staff middle; system-relative device is 2 − YUp.
            double dY = 2.0 - d.YUp;
            Add(d.MeasureIndex, dY - 1.2, dY + 0.3);
        }
        foreach (var h in ann.Hairpins)
        {
            // h.YUp is Y-up from the system top; this pass is system-relative device.
            double hY = -h.YUp;
            Add(h.StartMeasureIndex, hY - 0.34, hY + 0.34);
        }
        foreach (var sp in ann.TextSpanners)
        {
            // sp.YUp is Y-up from the system top; this pass is system-relative device.
            // Drawn ink about the line: the dashed rule's half thickness both ways,
            // widened by the text's own ink on the piece that carries it — the same
            // extents OutsideStaffStacker.PlaceTextSpanners registers (the old flat
            // 1.2 / 0.3 box was an invention; the 0.3 descent was ledger
            // textspanner.support.staff-to-line's whole +0.25).
            double lineHalf = EngravingDefaults.StaffLineThickness / 2.0;
            double spTop = lineHalf, spBottom = lineHalf;
            if (!string.IsNullOrEmpty(sp.Text))
            {
                var ink = Rendering.TextFontMetrics.Ink(
                    sp.Text, 4.0 * 0.5, sans: false, Rendering.FontStyle.Italic);
                spTop = Math.Max(spTop, ink.Top);
                spBottom = Math.Max(spBottom, -ink.Bottom);
            }
            double spY = -sp.YUp;
            Add(sp.StartMeasureIndex, spY - spTop, spY + spBottom);
        }
        foreach (var bn in ann.BarNumbers)
        {
            if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int s))
                continue;
            // bn.YUp is Y-up from the system top; the system-relative device value
            // (the old bn.Y - system.Y) is just -YUp.
            double rel = -bn.YUp;
            // The digits' OWN ink over their baseline, from the face — the same face and the
            // same call the WIDTH beside this already uses. It was a bare 1.3 until
            // 2026-07-28, which is a cap height nothing states: LilyPond reserves what the
            // glyphs draw, since a BarNumber's vertical-skylines come from its stencil.
            // LILYPOND-REF: lily/grob.cc:85-89 simple_vertical_skylines_from_extents — a text
            // grob's extent IS its stencil's, so there is no designed box to round up to.
            // MEASURED (audit/lp-geometry/probes/page-vertical.ly, books BNL/BNH): LilyPond
            // puts the baseline 3.076208 over the staff refpoint and the ink top at 4.305433,
            // i.e. 1.229225 — against the 1.3 this used to reserve. Closing it took
            // system.clef-floor.floor-bound-distance to exact and lyrics.*.system-gap from
            // +0.207200 to +0.143468.
            double capTop = Rendering.TextFontMetrics.Ink(
                bn.Text, BarNumberEngraver.FontSize,
                sans: false, Rendering.FontStyle.Bold).Top;
            up[s] = Math.Max(up[s], -(rel - capTop));
        }

        // Ties and slurs now store WITHIN-SYSTEM device Y (step 2d), so the negated
        // bow Y each caller passes is already system-relative — no system.Y subtraction.
        void AddCurve(int measureIndex, double y0, double y1, double c1, double c2)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            // Curve extreme ~ 3/4 of the way from endpoints to controls.
            double topRel = Math.Min(Math.Min(y0, y1), Math.Min(y0, y1) * 0.25 + Math.Min(c1, c2) * 0.75);
            double botRel = Math.Max(Math.Max(y0, y1), Math.Max(y0, y1) * 0.25 + Math.Max(c1, c2) * 0.75);
            up[s] = Math.Max(up[s], -topRel);
            down[s] = Math.Max(down[s], botRel - bottoms[s]);
        }

        foreach (var t in ties)
        {
            // A broken tie's continuation piece (IsBrokenLeft) lives on a LATER
            // system at that system's Y — attribute its extent to the system holding
            // its END, or its low Y leaks onto the start system and forces a huge
            // inter-system gap.
            int mi = t.IsBrokenLeft ? t.Tie.EndMeasureIndex : t.Tie.StartMeasureIndex;
            // Bow Y is now page Y-up (= -device); reflect back for this device extent pass.
            AddCurve(mi, -t.StartYUp, -t.EndYUp, -t.Control1.Y, -t.Control2.Y);
        }
        foreach (var sl in slurs)
        {
            int mi = sl.IsBrokenLeft ? sl.Slur.EndMeasureIndex : sl.Slur.StartMeasureIndex;
            AddCurve(mi, -sl.StartYUp, -sl.EndYUp, -sl.Control1.Y, -sl.Control2.Y);
        }

        for (int i = 0; i < n; i++)
        {
            var ext = perSystemExtents[i];
            perSystemExtents[i] = (
                Math.Max(ext.upExtent, up[i]),
                Math.Max(ext.downExtent, down[i]));
        }
    }

    /// <summary>
    /// Returns per-system skylines with the scripts' ink merged in — above
    /// scripts into the UP skyline, below scripts into the DOWN skyline. The
    /// input skylines are NOT mutated; non-augmented systems reuse the
    /// originals. LilyPond's axis-group skyline contains the note-bound
    /// scripts, so anything spaced against it (the chord-name line above, the
    /// figured-bass row below) clears a staccato or fermata over a protruding
    /// note; these system skylines are built before script layout exists,
    /// hence this second pass. Inner-staff scripts merge harmlessly (the
    /// system silhouette already dominates them). The ink transform mirrors
    /// OutsideStaffStacker's script seeding; script Y is system-local
    /// (staff offset already applied).
    /// LILYPOND-REF: lily/axis-group-interface.cc:359-474 — grobs without
    /// outside-staff-priority stay in the support skyline.
    /// </summary>
    private static IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? AugmentSkylinesWithScripts(
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<SystemLayout> systems)
    {
        if (systemSkylines == null || articulations.IsDefaultOrEmpty)
            return systemSkylines;

        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length && s < systemSkylines.Count; s++)
            foreach (var m in systems[s].Measures)
                measureToSystem[m.MeasureIndex] = s;

        var augmented = systemSkylines.ToArray();
        foreach (var a in articulations)
        {
            if (!measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                continue;
            // ArticulationLayout.YUp is Y-up (staff-spaces above the staff middle);
            // this skyline is Y-up too (system-top origin). Translate against this
            // staff's system-local middle, and take the offset in the SAME frame so the
            // whole line adds — the middle sits half a staff BELOW the staff top, which
            // in Y-up subtracts. Ink Top/Bottom stay up-positive, so they ADD.
            // This is now the same expression as OutsideStaffStacker's articulation
            // branch; the two used to be one Y-up and one Y-down spelling of it.
            var sys = systems[sysIdx];
            double staffMidUp = LayoutUtilities.StaffOffsetInSystemUp(sys, a.StaffIndex) - 2.0;
            double aY = a.YUp + staffMidUp;
            double inkTop = aY + a.Ink.Top;
            double inkBottom = aY + a.Ink.Bottom;
            if (a.IsAbove)
            {
                var up = new VerticalSkyline(VerticalDirection.Up);
                up.Merge(augmented[sysIdx].up);
                up.Merge(VerticalSkyline.FromBox(
                    a.X + a.Ink.Left, a.X + a.Ink.Right,
                    inkBottom, inkTop, VerticalDirection.Up));
                augmented[sysIdx] = (up, augmented[sysIdx].down);
            }
            else
            {
                var down = new VerticalSkyline(VerticalDirection.Down);
                down.Merge(augmented[sysIdx].down);
                down.Merge(VerticalSkyline.FromBox(
                    a.X + a.Ink.Left, a.X + a.Ink.Right,
                    inkBottom, inkTop, VerticalDirection.Down));
                augmented[sysIdx] = (augmented[sysIdx].up, down);
            }
        }
        return augmented;
    }

    /// <summary>
    /// Paging skylines: the per-system skylines plus the annotation ink that
    /// hangs outside the staves — note-bound scripts (both directions) and
    /// figured-bass rows. The optimal page-stacking path spaces systems by
    /// skyline DISTANCE (PageLayouter), so anything missing from these
    /// silhouettes can print into the neighbouring system (showcase/04:
    /// figured-bass digits through the next system's volta boxes).
    /// LILYPOND-REF: lily/page-layout-problem.cc build_system_skyline — LP's
    /// paging skylines contain every grob of the system.
    /// </summary>
    private static List<(VerticalSkyline up, VerticalSkyline down)>? AugmentSkylinesForPaging(
        List<(VerticalSkyline up, VerticalSkyline down)>? skylines,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<FiguredBassLayout> figuredBasses,
        ImmutableArray<VoltaBracketLayout> voltaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MusicMarkLayout> musicMarks = default,
        ImmutableArray<CustomTextLayout> customTexts = default,
        ImmutableArray<ChordNameLayout> chordNames = default,
        ImmutableArray<BarNumberLayout> barNumbers = default,
        ImmutableArray<TupletBracketLayout> tupletBrackets = default,
        ImmutableArray<SlurLayout> slurs = default,
        ImmutableArray<TieLayout> ties = default)
    {
        if (skylines == null)
            return null;
        var result = AugmentSkylinesWithScripts(skylines, articulations, systems)!.ToList();

        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length && s < result.Count; s++)
            foreach (var m in systems[s].Measures)
                measureToSystem[m.MeasureIndex] = s;

        // A tuplet bracket is ordinary ink inside its staff's axis group in LilyPond, so
        // the next system has to clear it exactly as it clears the notes. This skyline is
        // the one the PAGE spaces systems by; MultiStaffLayouter seeds the other one, the
        // per-staff skyline Align_interface reads, and seeding only that left the bracket
        // reserved between staves and not between systems.
        // (EnrichExtentsWithAnnotationProtrusions does add tuplets to the scalar extents,
        // but those are only the fallback for an EMPTY skyline — see CreatePages — so they
        // never decide anything here.)
        // LILYPOND-REF: scm/define-grobs.scm TupletBracket carries vertical-skylines from
        //   its stencil and sets no outside-staff-priority, so axis-group-interface keeps
        //   it inside; lily/page-layout-problem.cc:1070-1127 build_system_skyline spaces
        //   pages by the COMPLETE system stencil.
        if (!tupletBrackets.IsDefaultOrEmpty)
        {
            foreach (var group in tupletBrackets.GroupBy(t => t.MeasureIndex))
            {
                if (!measureToSystem.TryGetValue(group.Key, out int s))
                    continue;
                // *YUp here IS the system frame (the annotation pass baked the staff
                // offset in through staffYAt), which is this skyline's own frame.
                var up = new VerticalSkyline(VerticalDirection.Up);
                up.Merge(result[s].up);
                var down = new VerticalSkyline(VerticalDirection.Down);
                down.Merge(result[s].down);
                // staffTopUp 0: the SYSTEM skyline's origin is the top staff's top line and
                // these layouts are already in it (the annotation pass baked the staff offset
                // in), so there is no half-staff to close here. The PER-STAFF seeding passes a
                // real one, because that skyline is about the staff's reference point.
                // ⚠️ StaffSize.FullSize for the same reason staffTopUp is 0: these layouts
                // arrive in the SYSTEM's frame and units, the annotation pass having baked
                // both in, so sizing them again here would apply the magnification twice.
                // ⚠️ WHAT THAT LEAVES OPEN, named rather than hidden: the annotation pass
                // does not know about magnification either, so an ossia's bracket reaching
                // THIS path is reserved full size. It is the same unit question as
                // SkylineBuilder's, one frame further out, and it goes when the annotation
                // layouts carry the staff they belong to.
                SkylineBuilder.AddTupletBracketsToSkyline(
                    group.ToImmutableArray(), staffTopUp: 0, StaffSize.FullSize, up, down);
                result[s] = (up, down);
            }
        }

        // A slur is the same kind of inside-staff grob as the tuplet bracket -- it carries
        // vertical-skylines from its stencil and sets no outside-staff-priority
        // (scm/define-grobs.scm Slur), so the next system must clear its bow exactly as it
        // clears the notes. MultiStaffLayouter.BuildAllStaffSkylines seeds the OTHER skyline
        // (the per-staff one Align_interface reads); this is the one the PAGE spaces systems
        // by, and until now it reserved the bow nowhere between systems. The bow's *YUp is the
        // WITHIN-SYSTEM Y-up the prelim scorer produced (staffMiddleDown is the within-system
        // staff offset), the same frame AddTupletBracketsToSkyline arrives in, so it seeds
        // without a further offset -- once ElementCoordinator.LayoutSlurs stopped letting a
        // slur on one system collide with one on another (which had drifted each system's bow
        // deeper). Attribution to a system mirrors EnrichExtentsWithAnnotationProtrusions: a
        // broken continuation piece belongs to the system holding its END.
        // audit/lp-geometry system.slur-{under,over}-notes.
        if (!slurs.IsDefaultOrEmpty)
        {
            foreach (var group in slurs.GroupBy(sl => measureToSystem.TryGetValue(
                sl.IsBrokenLeft ? sl.Slur.EndMeasureIndex : sl.Slur.StartMeasureIndex, out int s) ? s : -1))
            {
                int s = group.Key;
                if (s < 0 || s >= result.Count)
                    continue;
                var up = new VerticalSkyline(VerticalDirection.Up);
                up.Merge(result[s].up);
                var down = new VerticalSkyline(VerticalDirection.Down);
                down.Merge(result[s].down);
                // staffTopUp 0 and FullSize — the system frame and its units again, as for
                // the brackets above, and open in the same way for an ossia.
                SkylineBuilder.AddSlursToSkyline(
                    group.ToImmutableArray(), staffTopUp: 0, StaffSize.FullSize, up, down);
                result[s] = (up, down);
            }
        }

        // A tie is the same inside-staff grob as the slur one line up -- vertical-skylines from
        // its stencil, no outside-staff-priority (scm/define-grobs.scm Tie) -- so the next
        // system must clear its bow exactly as it clears the notes. SkylineBuilder.BuildStaffSkylines
        // seeds the tie into the OTHER skyline (the per-staff one Align_interface reads, which
        // staff.staff.tie-{under,over}-notes measure); this is the one the PAGE spaces systems
        // by, and until now it reserved the bow nowhere between systems -- the hole the slur had
        // before it was seeded here. Unlike the slur, the tie carries no cross-system collision
        // term (TieFormattingProblem scores each bow against its own notes, with no existingSlurs
        // analogue), so no LayoutTies fix is needed first. Attribution to a system mirrors the
        // slur: a broken continuation piece belongs to the system holding its END.
        // audit/lp-geometry system.tie-{under,over}-notes.
        if (!ties.IsDefaultOrEmpty)
        {
            foreach (var group in ties.GroupBy(t => measureToSystem.TryGetValue(
                t.IsBrokenLeft ? t.Tie.EndMeasureIndex : t.Tie.StartMeasureIndex, out int s) ? s : -1))
            {
                int s = group.Key;
                if (s < 0 || s >= result.Count)
                    continue;
                var up = new VerticalSkyline(VerticalDirection.Up);
                up.Merge(result[s].up);
                var down = new VerticalSkyline(VerticalDirection.Down);
                down.Merge(result[s].down);
                // staffTopUp 0 and FullSize — the system frame and its units again, as for
                // the brackets above, and open in the same way for an ossia.
                SkylineBuilder.AddTiesToSkyline(
                    group.ToImmutableArray(), staffTopUp: 0, StaffSize.FullSize, up, down);
                result[s] = (up, down);
            }
        }

        foreach (var fb in figuredBasses)
        {
            if (!measureToSystem.TryGetValue(fb.MeasureIndex, out int s))
                continue;
            double half = FiguredBassEngraver.MinFigureBoxWidth;
            // YUp is Y-up; this inter-system skyline is Y-up too (system-top origin), so
            // take the figure's own staff offset in that frame as well and the line adds.
            // The staff middle is half a staff below the staff top, hence the -2.0; the
            // figure column then extends downward (smaller Y-up).
            double fbStaffOffsetUp = LayoutUtilities.StaffOffsetInSystemUp(systems[s], fb.StaffIndex);
            double fbY = fb.YUp - 2.0 + fbStaffOffsetUp;
            double top = fbY + FiguredBassEngraver.FigureTopExtent;
            double bottom = fbY
                - ((fb.FigureTexts.Length - 1) * FiguredBassEngraver.FigureSpacing + 0.5);
            var down = new VerticalSkyline(VerticalDirection.Down);
            down.Merge(result[s].down);
            down.Merge(VerticalSkyline.FromBox(
                fb.X - half, fb.X + half, bottom, top, VerticalDirection.Down));
            result[s] = (result[s].up, down);
        }

        // Volta brackets and their "End1"-style label boxes rise above the
        // staff: without them in the UP silhouette, a previous system's
        // figured-bass digits settle onto the boxes.
        foreach (var v in voltaBrackets)
        {
            if (!measureToSystem.TryGetValue(v.StartMeasureIndex, out int s))
                continue;
            // YUp is Y-up from the system top; this skyline is Y-up too, so use it directly.
            double vY = v.YUp;
            var up = new VerticalSkyline(VerticalDirection.Up);
            up.Merge(result[s].up);
            up.Merge(VerticalSkyline.FromBox(
                v.StartX, v.EndX, vY - 1.6, vY + 0.1, VerticalDirection.Up));
            result[s] = (result[s].up, result[s].down);
            result[s] = (up, result[s].down);
        }

        // Section labels, rehearsal marks and navigation text (Fine, D.C. …)
        // stack above (or below) the staff like any other annotation. Without
        // their boxes in the silhouette, the X-aware inter-system distance let
        // a label above system 2 print through system 1's figured bass. The
        // box is added to BOTH sides — merging on the side the mark does not
        // protrude toward is a no-op, so no direction bookkeeping is needed.
        void AddMarkBox(int measureIndex, double x0, double x1, double top, double bottom)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            var up = new VerticalSkyline(VerticalDirection.Up);
            up.Merge(result[s].up);
            up.Merge(VerticalSkyline.FromBox(x0, x1, bottom, top, VerticalDirection.Up));
            var down = new VerticalSkyline(VerticalDirection.Down);
            down.Merge(result[s].down);
            down.Merge(VerticalSkyline.FromBox(x0, x1, bottom, top, VerticalDirection.Down));
            result[s] = (up, down);
        }
        if (!musicMarks.IsDefaultOrEmpty)
        {
            foreach (var m in musicMarks)
            {
                if (MusicMarkItem.IsSpannerHandled(m.MarkType))
                    continue;
                // Same vertical envelope Enrich uses; width from the real text
                // (boxed labels get the box padding, symbols a 2 ss square).
                double halfW = m.IsSymbol
                    ? 1.0
                    : Rendering.TextFontMetrics.SerifBold(m.Text, 2.4) / 2 + 0.4;
                // YUp is Y-up; the skyline is Y-up too. Translate to the top staff's frame.
                double mY = m.YUp - 2.0;
                AddMarkBox(m.MeasureIndex, m.X - halfW, m.X + halfW, mY + 2.1, mY - 0.7);
            }
        }
        if (!customTexts.IsDefaultOrEmpty)
        {
            foreach (var ct in customTexts)
            {
                double halfW = Rendering.TextFontMetrics.SerifBold(ct.Text, 2.0) / 2 + 0.2;
                // YUp is Y-up; the skyline is Y-up too. Translate to the top staff's frame.
                double ctY = ct.YUp - 2.0;
                AddMarkBox(ct.MeasureIndex, ct.X - halfW, ct.X + halfW, ctY + 1.8, ctY - 0.6);
            }
        }
        // Inline chord symbols: their scalar height joins the up-extents, but
        // the X-aware inter-system Distance() never saw them — on a ragged
        // (natural-gap) page a below-staff jump text ("D.S. al Coda") printed
        // straight onto the next system's chord letters. Same envelope the
        // scalar extents use (cap ascent 1.9, descent 0.3).
        if (!chordNames.IsDefaultOrEmpty)
        {
            foreach (var cn in chordNames)
            {
                double halfW = ChordNameEngraver.SymbolInkWidth(cn.ChordText) / 2 + 0.3;
                double cnY = cn.YUp; // cn.YUp is Y-up from the system top (skyline frame)
                AddMarkBox(cn.MeasureIndex, cn.X - halfW, cn.X + halfW, cnY + 1.9, cnY - 0.3);
            }
        }
        // Line-start bar numbers sit in the band above the staff start where
        // only the staff-symbol roof exists; without their ink in the UP
        // silhouette, Distance() lets the previous system's staff lines crowd
        // the number (their scalar up-extent is overridden by the X-aware
        // distance). Same cap envelope Enrich uses — the digits' own ink from
        // the face, see the note there for what it replaced and what it closed.
        // LILYPOND-REF: lily/page-layout-problem.cc build_system_skyline —
        // the system skyline contains the BarNumber grob.
        if (!barNumbers.IsDefaultOrEmpty)
        {
            foreach (var bn in barNumbers)
            {
                if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int s))
                    continue;
                // bn.YUp is Y-up from the system top; the skyline is Y-up too.
                double rel = bn.YUp;
                double w = Rendering.TextFontMetrics.SerifBold(
                    bn.Text, BarNumberEngraver.FontSize);
                double x0 = bn.RightAligned ? bn.X - w : bn.X;
                double capTop = Rendering.TextFontMetrics.Ink(
                    bn.Text, BarNumberEngraver.FontSize,
                    sans: false, Rendering.FontStyle.Bold).Top;
                var up = new VerticalSkyline(VerticalDirection.Up);
                up.Merge(result[s].up);
                up.Merge(VerticalSkyline.FromBox(
                    x0, x0 + w, rel, rel + capTop, VerticalDirection.Up));
                result[s] = (up, result[s].down);
            }
        }
        return result;
    }

    private static void AugmentExtentsWithLooseLines(
        List<(double upExtent, double downExtent)> perSystemExtents,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        List<(int startMeasure, int measureCount)> systemMeasureRanges,
        ImmutableArray<ChordNameItem> chordNames,
        List<(double bandUp, double bandDown)>? perSystemBands,
        IReadOnlyList<double> lyricBands)
    {
        for (int i = 0; i < perSystemExtents.Count && i < systemMeasureRanges.Count; i++)
        {
            var (start, count) = systemMeasureRanges[i];
            var (looseDown, looseUp, bandDown, bandUp) = EstimateLooseLineExtents(
                dynamics, figuredBasses, musicMarks, voltaBrackets,
                start, start + count, chordNames);

            // The lyric block's reservation is the WALK's, computed per system by
            // LyricReservationBelowSystem where the staff skylines live — not an estimate
            // made from the items alone. It joins the same two maxima the estimate feeds.
            double lyricBand = i < lyricBands.Count ? lyricBands[i] : 0;
            if (lyricBand > 0)
            {
                looseDown = Math.Max(looseDown, lyricBand);
                bandDown = Math.Max(bandDown, lyricBand);
            }
            perSystemBands?.Add((bandUp, bandDown));

            var ext = perSystemExtents[i];
            if (looseDown > 0 || looseUp > 0)
            {
                perSystemExtents[i] = (
                    Math.Max(ext.upExtent, looseUp),
                    Math.Max(ext.downExtent, looseDown));
            }
        }
    }

    /// <summary>
    /// One system's alignment as the loose-line pass needs to see it: the two spaceable
    /// staves that bracket everything, the non-spaceable lines it OPENS with, and the ones
    /// that hang below its last staff.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 and :948-990 —
    /// <c>Page_layout_problem</c> walks the alignment IN ORDER and cuts the non-spaceable
    /// lines into runs between the spaceable ones. This is that walk's classification, and it
    /// is order-based for the same reason: LilyPond never compares two positions to decide
    /// what a line belongs to.
    /// </remarks>
    /// <param name="Trailing">
    /// The independent LYRICS rows standing below <paramref name="LastSpaceable"/>, in
    /// alignment order — elements of this system's own block, which is the run the chain
    /// below it is solved from.
    /// </param>
    /// <param name="UnmodelledRow">
    /// A text row this port does not place in a chain stands below a spaceable staff: a row
    /// BETWEEN two of them (that is <see cref="ComputeBetweenStavesEnd"/>'s span, which still
    /// declines), or a CHORDS row below one (its <c>nonstaff-*</c> specs are the ChordNames
    /// set and no corpus point measures that arrangement).
    /// <para>
    /// ⚠️ LILYSHARP-OWN: DECLINING HAS NO COUNTERPART — LilyPond always solves. Its chain
    /// runs from one spaceable line to the next whatever is between them
    /// (page-layout-problem.cc:919-925), so "the room belongs to something this port does not
    /// model" is a Lily# state and not a LilyPond one. It is an EXTENSION of the bail-out
    /// <see cref="BuildLooseChainEnds"/> has carried since the chain existed, not a new kind
    /// of thing, and it goes when the last un-modelled arrangement does: a row between two
    /// staves and a chords row below one. ⚠️ Until then the flag is what keeps the
    /// reservation and the chain agreeing — <c>LyricReservationBelowSystem</c> reads it too,
    /// because reserving for a line the chain will not place is worse than either.
    /// </para>
    /// <para>
    /// ★ AN OSSIA USED TO BE A THIRD REASON TO DECLINE and is not one since 2026-07-28: it is
    /// a spaceable staff, so it BRACKETS runs instead of being one. The flag that carried it
    /// (<c>HasOssia</c>) is gone with its three readers.
    /// </para>
    /// </param>
    private readonly record struct SystemAlignment(
        StaffLayout? FirstSpaceable,
        StaffLayout? LastSpaceable,
        ImmutableArray<StaffLayout> Leading,
        ImmutableArray<int> Trailing,
        bool UnmodelledRow);

    /// <summary>Cuts one system's placed staves into that classification.</summary>
    private static SystemAlignment ClassifySystem(
        ImmutableArray<StaffGroupLayout> groups, IReadOnlySet<int> lyricsRowStaves)
    {
        StaffLayout? first = null, last = null;
        var leading = ImmutableArray.CreateBuilder<StaffLayout>();
        var trailing = ImmutableArray.CreateBuilder<int>();
        bool unmodelled = false;

        foreach (var group in groups)
        {
            if (group.Staves.IsDefaultOrEmpty) continue;
            foreach (var st in group.Staves)
            {
                // Hara-kiri leaves a hidden staff at the current Y with zero height, so it
                // neither draws nor takes room — LilyPond's filter_dead_elements (:589).
                if (st.IsHidden) continue;
                // LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 Page_layout_problem::is_spaceable
                // — a line is spaceable exactly when it declares no `staff-affinity`, and that
                // ONE property is the whole question. Nothing there reads a magnification (a
                // small staff is a staff) or a kind of context.
                // ⚠️ IT USED TO BE ASKED AS A TYPE ENUMERATION — the score's set of text-row
                // indices, handed in — which is the same answer by a different route and only
                // for as long as the two lists agree. An ossia is what they disagreed about:
                // excluding it put an ossia that LEADS a system outside the page's chain
                // entirely, the anchor fell through to the staff the ossia decorates, and the
                // ossia was drawn ABOVE the page's head, 2.123312 into the top margin
                // (audit/lp-geometry page.ossia-pair.compressed.first-staff-refpoint, book OSSK).
                if (!StaffAffinity.IsSpaceable(st.StaffAffinity))
                {
                    if (first is null) { leading.Add(st); continue; }
                    if (lyricsRowStaves.Contains(st.StaffIndex)) trailing.Add(st.StaffIndex);
                    else unmodelled = true;
                    continue;
                }
                // A spaceable staff below a row means that row stood BETWEEN two of them.
                if (trailing.Count > 0) { unmodelled = true; trailing.Clear(); }
                double down = -st.Y;
                if (first is null || down < -first.Y) first = st;
                if (last is null || down > -last.Y) last = st;
            }
        }

        return new SystemAlignment(
            first, last, leading.ToImmutable(), trailing.ToImmutable(), unmodelled);
    }

    /// <summary>
    /// How far DOWN from a system's ORIGIN its first and its last SPACEABLE staff's
    /// REFERENCE POINTS sit — the two anchors every page spring is written against.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:896-901 — <c>solution_[spring_idx]</c> is the
    /// first spaceable staff's position and the system's origin is that plus
    /// <c>min_offsets[0]</c>; :1116 and :1126 are the same conversion at the other end
    /// (<c>last_spaceable_dy</c>). Every page distance LilyPond writes — top-system-spacing to
    /// the first one, system-system-spacing between them, last-bottom-spacing under the last —
    /// runs between reference points, while Lily# stacks systems by their ORIGIN (the first
    /// element's top line). This is that conversion, and it is one function because it was
    /// three: <c>_options.StaffHeight / 2.0</c> stood in for it in
    /// <see cref="Layout"/>, in <see cref="CreatePages"/> and in <c>PageLayouter</c>, and which
    /// of the three was live depended on the paper regime (HANDOFF 5.2.1 (2)).
    /// <para>
    /// ⚠️ A NOMINAL HALF STAFF IS NOT THIS QUANTITY. A staff's refpoint is the middle of its
    /// OWN line span, so it is 2.000000 below the top line only for a five-line staff: a
    /// six-string tab staff's is 3.750000 below (its lines span (6-1) × 1.5). MEASURED against
    /// LilyPond, which puts the first staff of a tab page exactly where it puts the first staff
    /// of a notation page — audit/lp-geometry <c>page.tab-only.first-staff-refpoint</c> against
    /// its control <c>page.tab-control.first-staff-refpoint</c>, both 11.690551.
    /// </para>
    /// <para>
    /// ⚠️ THE SELECTION IS <see cref="ClassifySystem"/>'s, not "the outer layouts": a hidden
    /// (hara-kiri'd) staff and a text row are both there in the array and neither is what a
    /// page spring attaches to. MEASURED both ways — taking the outer layouts regresses
    /// <c>hara-kiri.wide-ink.lone-staff-to-next-system</c> by 2.000000 (it picks the hidden
    /// staff) and four <c>lyrics.hara-kiri.grouper.*</c> entries with it.
    /// </para>
    /// <para>
    /// ⚠️ LILYSHARP-OWN: THE FALLBACK. A system with no spaceable staff at all — a chords-only
    /// lead sheet — keeps the nominal half staff, because LilyPond's anchor there is a
    /// ChordNames group's own reference point (its baseline) and no corpus point measures a
    /// page anchor over a staffless system. It goes when such a point exists.
    /// </para>
    /// </remarks>
    /// <param name="groups">One system's placed staff groups.</param>
    /// <returns>
    /// <c>ToFirst</c>/<c>ToLast</c>: origin to that staff's refpoint. <c>HalfFirst</c>/
    /// <c>HalfLast</c>: that staff's OWN half span — the distance from its own top (bottom)
    /// line to its refpoint, which is NOT the same number as soon as a loose line stands
    /// between the origin and the staff.
    /// ⚠️ LILYSHARP-OWN: THE SECOND PAIR HAS NO LILYPOND COUNTERPART, and it exists because a
    /// Lily#-only quantity does. LilyPond has one frame — <c>min_offsets</c> off the system's
    /// own reference point, every element in it (page-layout-problem.cc:896-901) — and no
    /// "band": a loose line is IN the skyline it is spaced against. Lily# estimates lyric and
    /// chord-row bands OUTSIDE the skyline and measures them from the STAFF, so the two frames
    /// have to be carried separately until those bands are elements like any other. It goes
    /// with them.
    /// ⚠️ Quantities floored by a whole-line BAND need the
    /// half span, because a band is already measured from the staff it hangs off; quantities
    /// floored by a skyline or a scalar extent need the origin distance, because those are
    /// measured from the origin. Mixing them double-counts the band — MEASURED, it put
    /// <c>lyrics.chord-row.between-systems.system-gap</c> 1.883400 over LilyPond's 12.000000.
    /// </returns>
    private (double ToFirst, double ToLast, double HalfFirst, double HalfLast) PageAnchorOffsets(
        ImmutableArray<StaffGroupLayout> groups, IReadOnlySet<int> lyricsRowStaves)
    {
        double nominal = _options.StaffHeight / 2.0;
        if (groups.IsDefaultOrEmpty)
            return (nominal, nominal, nominal, nominal);
        var alignment = ClassifySystem(groups, lyricsRowStaves);
        return alignment.FirstSpaceable is { } first && alignment.LastSpaceable is { } last
            ? (-MultiStaffLayouter.StaffRefpoint(first), -MultiStaffLayouter.StaffRefpoint(last),
               first.Height / 2.0, last.Height / 2.0)
            : (nominal, nominal, nominal, nominal);
    }

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
    private static Func<int, IReadOnlyList<int>>? BuildTrailingRowStaves(
        ImmutableArray<SystemLayout> systemsArray, IReadOnlySet<int> lyricsRowStaves)
    {
        if (lyricsRowStaves.Count == 0 || systemsArray.IsDefaultOrEmpty)
            return null;

        var perSystem = new List<IReadOnlyList<int>>(systemsArray.Length);
        foreach (var system in systemsArray)
        {
            if (system.StaffGroups.IsDefaultOrEmpty)
            {
                perSystem.Add(Array.Empty<int>());
                continue;
            }
            var alignment = ClassifySystem(system.StaffGroups, lyricsRowStaves);
            perSystem.Add(
                alignment.UnmodelledRow || alignment.LastSpaceable is null
                    ? Array.Empty<int>()
                    : alignment.Trailing);
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
    /// ★ A LYRICS ROW BELOW THE LAST SPACEABLE STAFF NO LONGER BAILS (2026-07-28). It is a
    /// non-spaceable line of THIS system's own run (:919-925), so it is an element of the
    /// chain this end closes rather than a reason to abandon it — see
    /// <see cref="BuildTrailingRowStaves"/> and <see cref="SystemAlignment"/>. What still
    /// bails is a row this port does not place: one BETWEEN two spaceable staves, or a CHORDS
    /// row below one. Both are <see cref="SystemAlignment.UnmodelledRow"/>.
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
        IReadOnlySet<int> lyricsRowStaves)
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
        for (int s = 0; s < systemsArray.Length; s++)
        {
            if (systemsArray[s].StaffGroups.IsDefaultOrEmpty) return null;
            var alignment = ClassifySystem(systemsArray[s].StaffGroups, lyricsRowStaves);
            // A row this port does not model leaves its room to somebody else, so the room is
            // UNKNOWN — the remarks' bail-out. ⚠️ AN OSSIA USED TO BAIL OUT HERE TOO, on the
            // reading that it "is a loose line to LilyPond and goes INTO the chain while Lily#
            // lays it out as a band of its own". BOTH HALVES OF THAT WERE WRONG: an ossia has
            // no `staff-affinity`, so LilyPond makes it SPACEABLE and it brackets runs rather
            // than filling them (page-layout-problem.cc:1173-1177 is_spaceable), and since 2026-07-28 Lily#
            // does the same. It is a chain END here like any other staff.
            if (alignment.UnmodelledRow) return null;
            if (alignment.FirstSpaceable is not { } firstStaff
                || alignment.LastSpaceable is not { } lastStaff) return null;

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
                        leading[index + 1], firstSpaceableIndex[index + 1]);

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
    /// ⚠️ THE INDENT GOES WITH THE CLOSING STAFF'S SKYLINE, the same reason
    /// <see cref="ComputeBetweenStavesEnd"/> passes it: that skyline is what the chain's last
    /// spring is floored by, and one built without the clef would floor it somewhere the
    /// room does not agree with.
    /// </para>
    /// </remarks>
    private (ImmutableArray<LooseLineSpacer.LeadingLine> Lines,
             VerticalSpacingSpec? ClosingSpec, double ClosingMin) LeadingLinesOfSystem(
        MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray,
        IReadOnlyDictionary<int, Staff> staffByIndex, int sysIdx,
        List<StaffLayout> leading, int firstSpaceableIndex)
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
                    previous.StaffAffinity, NonStaffSpecsOf(previous, sp),
                    row.StaffAffinity, NonStaffSpecsOf(row, sp),
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
            previous!.StaffAffinity, NonStaffSpecsOf(previous, sp),
            null, sp.Lyrics, sp.StaffStaff);
        double closingMin = walk.Distance(
            _skylineBuilder.BuildStaffSkylines(
                closingStaff, measures, systemLeft: systemsArray[sysIdx].Indent).Up,
            closingSpec.Padding);

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
                score.ChordNames, measures, staffIndex, row.PrimaryVoice.Measures);

    /// <summary>Which context's <c>nonstaff-*</c> specs a line carries — see
    /// <c>MultiStaffLayouter.NonStaffSpecsOf</c>, whose rule this is.</summary>
    private static StaffSpacingParameters.NonStaffSpacing NonStaffSpecsOf(
        Staff staff, StaffSpacingParameters sp)
        => staff.IsTextRow && !staff.IsLyricsTextRow ? sp.ChordNames : sp.Lyrics;

    /// <summary>
    /// Inputs to <see cref="CalculateAnnotationLayouts"/>. Collapses the former
    /// 21-parameter signature into one context object; the optional members default
    /// to null/empty, matching the old optional parameters exactly.
    /// </summary>
    private sealed class AnnotationLayoutContext
    {
        public required Score? Score { get; init; }
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
        public IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? SystemSkylines { get; init; }
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
        var articulationLayouts = score != null
            ? ArticulationEngraver.Calculate(score, articulations, ml, measuresByStaff, staffYAt, staffByIndex,
                beamLayouts ?? default)
            : ImmutableArray<ArticulationLayout>.Empty;
        var scriptedSkylines = AugmentSkylinesWithScripts(systemSkylines, articulationLayouts, systems);

        var lyricLayouts = LayoutLyrics(ctx, ml, scriptedSkylines);

        // LILYPOND-REF: axis-group-interface.cc skyline_spacing
        // Outside-staff elements are placed in priority order (lower priority = closer to staff).
        // DynamicLineSpanner (250) must be calculated before TextSpanner (350)
        // so text spanners can be placed below dynamics.

        // Dynamics first (outside-staff-priority: 250)
        var dynamicLayouts = score != null ? DynamicEngraver.Calculate(score, dynamics, ml, staffVoices, voicesByStaff, measuresByStaff, beamLayouts ?? default) : ImmutableArray<DynamicLayout>.Empty;

        // Detect and layout hairpins from cresc/decresc marks
        var hairpinItems = HairpinEngraver.DetectHairpins(musicMarks, dynamics);
        var hairpinLayouts = HairpinEngraver.Calculate(hairpinItems, systems, ml, staffYAt);

        // Detect and layout text spanners from rit/accel marks (outside-staff-priority: 350)
        // Pass dynamic layouts so text spanners can stack below them
        var textSpannerItems = TextSpannerEngraver.DetectTextSpanners(musicMarks);
        var textSpannerLayouts = TextSpannerEngraver.Calculate(textSpannerItems, systems, ml, dynamicLayouts, staffYAt);

        // Detect and layout ottava brackets from ottava/loco marks
        var ottavaItems = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);
        var ottavaLayouts = OttavaBracketEngraver.Calculate(ottavaItems, systems, ml, staffYAt);

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
            foreach (var staffIndex in musicMarks
                .Where(m => IsPedalMark(m.Type)).Select(m => m.StaffIndex).Distinct())
            {
                var style = StaffPedalStyle(staffIndex);
                if (style == PedalStyle.Text)
                    continue;
                var staffMarks = musicMarks.Where(m => m.StaffIndex == staffIndex).ToImmutableArray();
                var brackets = PedalEngraver.DetectPedalBrackets(staffMarks);
                pedalBracketBuilder.AddRange(
                    PedalEngraver.Calculate(brackets, systems, ml, isMixed: style == PedalStyle.Mixed));
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
        var figuredBassLayouts = FiguredBassEngraver.Calculate(
            figuredBasses ?? ImmutableArray<FiguredBassItem>.Empty, systems, ml, measures,
            measuresByStaff, scriptedSkylines);

        var chordNameLayouts = LayoutChordNames(
            ctx, ml, scriptedSkylines, staffYAt, minStaffYAt);

        // Layout percent repeats
        var percentRepeatLayouts = PercentRepeatEngraver.Calculate(
            percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty, systems, ml);

        // Layout trill spanners (tr + wavy line)
        // LILYPOND-REF: scm/scheme-engravers.scm — trill spanner positioning
        var trillSpannerLayouts = TrillSpannerEngraver.Calculate(
            trillSpanners ?? ImmutableArray<TrillSpannerItem>.Empty, systems, ml, staffYAt,
            voicesByStaff);

        // Calculate volta brackets first — needed by MusicMarkEngraver for collision avoidance
        // LILYPOND-REF: axis-group-interface.cc — elements sorted by outside-staff-priority
        var voltaBracketLayouts = VoltaBracketEngraver.Calculate(voltaBrackets, systems, ml);

        // LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside_staff_axis_group
        // Post-process below-staff elements using priority-based stacking.
        // This ensures hairpins avoid dynamics (both priority 250) and
        // text spanners avoid both dynamics and hairpins (priority 350).
        // The below pass runs over each staff's REAL down profile — the same
        // ingredients the inter-staff seed accumulated (staff symbol, clef, notes with
        // real thin stems, beams) — so the draw lands where the seed reserved. Fresh
        // skylines per call: the tracker raises them into its own frame.
        // LILYPOND-REF: lily/axis-group-interface.cc:937-950 skyline_spacing.
        Func<int, int, (VerticalSkyline Up, VerticalSkyline Down)?>? belowProfile = null;
        if (staffByIndex != null)
        {
            var allBeams = beamLayouts ?? ImmutableArray<BeamLayout>.Empty;
            belowProfile = (sysIdx, staffIndex) =>
            {
                if (sysIdx < 0 || sysIdx >= systems.Length
                    || !staffByIndex.TryGetValue(staffIndex, out var profStaff))
                    return null;
                var staffBeams = allBeams.IsDefaultOrEmpty
                    ? ImmutableArray<BeamLayout>.Empty
                    : allBeams.Where(b => b.StaffIndex == staffIndex).ToImmutableArray();
                return _skylineBuilder.BuildStaffSkylines(
                    profStaff, systems[sysIdx].Measures, beams: staffBeams,
                    systemLeft: systems[sysIdx].Indent);
            };
        }
        var (stackedDynamics, stackedHairpins) =
            OutsideStaffStacker.StackBelowStaff(systems, dynamicLayouts, hairpinLayouts,
                articulationLayouts, applyStaffOffsets: staffYAt != null,
                staffProfile: belowProfile);

        // ABOVE-staff: one unified priority pass (trill 50, bar number 100,
        // tuplet brackets 200 as immovable seeds, ottava 400, text 450,
        // volta 600, marks 1500), seeded from the per-system up-skylines.
        // Replaces the old pairwise hacks (bar-number-vs-volta in the
        // renderer; music-mark-vs-volta inside MusicMarkEngraver).
        var tupletBracketLayouts = TupletBracketEngraver.Calculate(
            tupletBrackets, ml, measures, beamGroups ?? default, beamLayouts ?? default,
            forceStemUp: tupletForceStemUp,
            measuresByStaff: measuresByStaff, voicesByStaff: voicesByStaff, staffYAt: staffYAt,
            staffByIndex: staffByIndex);
        var musicMarkLayouts = MusicMarkEngraver.Calculate(
            score, musicMarks, systems, ml, measures, default,
            chordNames: chordNameLayouts, lyrics: lyricLayouts, keepMarkText: keepMarkText,
            prefixTimeSignatureX: ctx.PrefixTimeSignatureX);
        var customTextLayouts = CustomTextEngraver.Calculate(customTexts, ml);
        // A leading \partial pickup is bar 0: shift displayed numbers down by one
        // so the first FULL measure is numbered 1, not 2.
        int barNumberOffset = (!measures.IsDefaultOrEmpty && measures[0].IsPickup) ? -1 : 0;
        var barNumberLayouts = BarNumberEngraver.Calculate(systems, numberOffset: barNumberOffset);
        // Forced-above dynamics (@f.up) join the above-staff pass so they clear, and are
        // cleared by, the other above-staff grobs. Below dynamics were already placed by
        // StackBelowStaff and pass through untouched.
        var (stackedTrills, stackedBarNumbers, stackedOttavas, stackedCustomTexts,
             stackedVoltas, stackedMarks, stackedDynamicsAbove, stackedTextSpanners) = OutsideStaffStacker.StackAboveStaff(
            systems, systemSkylines, tupletBracketLayouts,
            trillSpannerLayouts, barNumberLayouts, ottavaLayouts,
            customTextLayouts, voltaBracketLayouts, musicMarkLayouts,
            articulationLayouts, aboveDynamics: stackedDynamics, textSpanners: textSpannerLayouts);
        stackedDynamics = stackedDynamicsAbove;
        // After stacking, sit a boundary "To Coda" on the adjacent section label's
        // line (the two straddle one barline) instead of stacking them apart.
        stackedMarks = MusicMarkEngraver.CoPlaceToCodaWithLabels(stackedMarks);
        // (No tempo/label co-placement any more: the metronome mark and the section
        // label each break-align to their own anchor and the priority pass above
        // already stacked them pointwise, LilyPond's shape. The chart-pair device
        // died with the tempo port — see MusicMarkEngraver's note.)

        var fingeringLayouts = LayoutFingerings(
            score, systems, voicesByStaff, articulationLayouts, staffYAt);

        return new AnnotationLayouts(
            Dynamics: stackedDynamics,
            Articulations: articulationLayouts,
            GraceNotes: score != null ? GraceNoteEngraver.Calculate(score, graceNotes, ml, measuresByStaff, staffYByIndex, staffByIndex, articulations) : ImmutableArray<GraceNoteLayout>.Empty,
            Lyrics: lyricLayouts,
            LyricHyphens: new LyricHyphenEngraver().CalculateLayouts(lyricLayouts, systems),
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
            StanzaNumbers: StanzaNumberEngraver.Calculate(lyricLayouts, systems));
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
                        var up = _skylineBuilder.BuildStaffSkylines(
                            staff, systems[sysIdx].Measures,
                            systemLeft: systems[sysIdx].Indent).Up;
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

        return ChordNameEngraver.Calculate(
            cn, systems, ml, ctx.Measures,
            ctx.MeasuresByStaff, staffYAt, minStaffYAt, scriptedSkylines,
            chordGridSheet: chordGridSheet, lowerStaffUpSkyline: lowerStaffUpSkyline);
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
        // (chord names on a non-top staff); built lazily and only when such a line
        // exists, so single-/bottom-staff lyrics do no extra work and stay byte-identical.
        var downCache = new Dictionary<(int, int), VerticalSkyline?>();
        VerticalSkyline? StaffDownSkyline(int sysIdx, int staffIndex)
        {
            if (sysIdx < 0 || sysIdx >= systems.Length || staffByIndex == null
                || !staffByIndex.TryGetValue(staffIndex, out var staff))
                return null;
            var key = (sysIdx, staffIndex);
            if (!downCache.TryGetValue(key, out var sky))
            {
                // Same silhouette the room was measured from — including the clef,
                // which is why the indent goes with it (SkylineBuilder.SeedClef).
                sky = _skylineBuilder.BuildStaffSkylines(
                    staff, systems[sysIdx].Measures, systemLeft: systems[sysIdx].Indent).Down;
                downCache[key] = sky;
            }
            return sky;
        }

        Func<int, int, VerticalSkyline?>? noteBoundStaffDownSkyline = null;
        var nbAnchor = ctx.NoteBoundAnchorY;
        if (nbAnchor is { Count: > 0 } && staffByIndex != null
            && lyrics.Any(l => !l.IsLyricsRow && nbAnchor.ContainsKey(l.StaffIndex)))
            noteBoundStaffDownSkyline = StaffDownSkyline;

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
        if (nbAnchor is { Count: > 0 } && staffByIndex != null
            && lyrics.Any(l => !l.IsLyricsRow && nbAnchor.ContainsKey(l.StaffIndex)))
        {
            var endCache = new Dictionary<(int, int), (double, VerticalSkyline)?>();
            betweenStavesEnd = (sysIdx, anchorStaffIndex) =>
            {
                var key = (sysIdx, anchorStaffIndex);
                if (endCache.TryGetValue(key, out var cached))
                    return cached;
                var computed = ComputeBetweenStavesEnd(
                    sysIdx, anchorStaffIndex, systems, staffByIndex);
                endCache[key] = computed;
                return computed;
            };
        }

        var engraver = new LyricEngraver(
            parentAlignmentCentre: LyricEngraver.ParentAlignmentCentre(measuresByStaff, measures),
            systemPadding: _options.VerticalSpacing.SystemSystem.Padding);
        var laid = engraver.CalculateLayouts(
            lyrics, ml, _options.StaffHeight, systems, scriptedSkylines, ctx.StaffYByIndex,
            ctx.NoteBoundAnchorY, noteBoundStaffDownSkyline, ctx.LooseChainEnd,
            betweenStavesEnd, ctx.LastSpaceableStaffY, ctx.TrailingRowStaves);

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
    /// How far below the system's LAST SPACEABLE staff's bottom line its LOOSE BLOCK reaches
    /// when every line of it is at its ALIGNMENT MINIMUM — what the page reserves for it, as
    /// opposed to the distance it is eventually drawn at. The block is the note-bound verses
    /// and the independent lyrics ROWS standing under them, in alignment order.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:593-599 — <c>build_system_skyline</c> is
    /// handed <c>Align_interface::get_minimum_translations</c>, so a loose line IS in the
    /// system's skyline, at its minimum.
    /// LILYPOND-REF: lily/align-interface.cc:235-238 — that minimum does NOT contain the
    /// spec's basic-distance: it is added only behind <c>INT_MAX == end &amp;&amp; 0 == start</c>,
    /// the pure branch, and the call that feeds the skyline passes <c>start = end = 0</c>.
    /// The 5.500000 the line is drawn at arrives afterwards, out of
    /// <c>distribute_loose_lines</c>, INSIDE the room this reservation left.
    /// <para>
    /// It is <see cref="AlignmentWalk"/>, the same walk the placement and the inter-staff
    /// room run — the whole point of the island. It replaces TWO estimates of this quantity:
    /// <c>LyricEngraver.AlignmentMinimumBand</c>'s extent sum, and the DRAWN baseline the
    /// enrich pass used to fold into the same <c>downExtent</c>.
    /// </para>
    /// <para>
    /// ⚠️ ONLY THE BLOCK THAT HANGS BELOW THE SYSTEM, which is the one attached to a staff of
    /// the LAST group — the same split <c>BuildStaffAnchorTables</c> makes for
    /// <c>NoteBoundAnchorY</c>. A block between two groups is INSIDE the system and its room
    /// is the staff pair's (<see cref="BuildLooseLinesBetween"/>); reserving it here as well
    /// would count it twice, which the extent sum did.
    /// </para>
    /// </remarks>
    private double LyricReservationBelowSystem(
        MultiStaffScore score, ImmutableArray<MeasureLayout> measureLayouts,
        List<(VerticalSkyline Up, VerticalSkyline Down)> staffSkylines,
        ImmutableArray<StaffGroupLayout> groups, int startMeasure, int endMeasure)
    {
        if (score.Lyrics.IsDefaultOrEmpty || groups.IsDefaultOrEmpty)
            return 0;

        var lyricsRows = new HashSet<int>();
        foreach (var (_, st, idx) in score.EnumerateStaves())
            if (st.IsLyricsTextRow)
                lyricsRows.Add(idx);

        // The staff a staff-affinity-UP line below the system is spaced from, and what stands
        // under it. LILYPOND-REF: lily/page-layout-problem.cc:943-944 last_spaceable_line.
        var alignment = ClassifySystem(groups, lyricsRows);
        if (alignment.LastSpaceable is not { } anchorStaff
            || anchorStaff.StaffIndex >= staffSkylines.Count)
            return 0;

        int total = score.StaffGroups.Sum(g => g.StaffCount);
        int lastGroupFirst = total - score.StaffGroups[^1].StaffCount;
        var engraver = BuildBlockEngraver(score);

        // THE RUN, in alignment order: the note-bound verses hanging under the anchor staff,
        // and then every independent lyrics ROW standing under them, verse by verse. ★ ONE
        // LIST BECAUSE LILYPOND HAS ONE RUN — every non-spaceable line between two spaceable
        // ones goes into the same vector (page-layout-problem.cc:919-925), and a row is
        // non-spaceable wherever it stands. Until 2026-07-28 the row was a separate branch
        // that reserved WHERE IT WAS DRAWN, because it was placed as a staff-like band and
        // never solved; it is an element of the chain now, so what it reserves is its
        // ALIGNMENT MINIMUM like every other line (:593-599).
        var lines = engraver.NoteBoundBlockSkylines(
            score.Lyrics, measureLayouts, startMeasure, endMeasure, lastGroupFirst, total);
        if (!alignment.UnmodelledRow)
            foreach (int rowStaff in alignment.Trailing)
                lines.AddRange(engraver.RowBlockSkylines(
                    score.Lyrics, measureLayouts, startMeasure, endMeasure, rowStaff));
        if (lines.Count == 0)
            return 0;

        var walk = new AlignmentWalk();
        walk.Seed(staffSkylines[anchorStaff.StaffIndex].Down);

        // ⚠️ THE DEEPEST POINT OVER EVERY LINE, not the last line's. LilyPond's
        // build_system_skyline merges each element's skyline RAISED BY ITS OWN TRANSLATION
        // (page-layout-problem.cc:1093-1108) and the profile's maximum is what the page
        // reserves, so a line with a deeper descender than the one under it still owns the
        // band. Taking the last line's descent gives the same number on every book in the
        // corpus — the verse step is at least 2.800000 and a descender is a tenth of that —
        // which is exactly why it would have gone unnoticed.
        double deepest = 0;
        for (int k = 0; k < lines.Count; k++)
        {
            walk.Advance(
                lines[k].Up, lines[k].Down,
                k == 0 ? SkylineDrop.RelatedStaffPadding : SkylineDrop.NonStaffNonStaffPadding,
                k == 0
                    ? LooseLineSpacer.NonStaffRelatedStaff.MinimumDistance
                    : LooseLineSpacer.NonStaffNonStaff.MinimumDistance);
            deepest = Math.Max(deepest, walk.Where + -lines[k].Down.MaxHeight());
        }

        // ...and a down extent is measured below the anchor's BOTTOM LINE, half a staff
        // under the reference point the walk runs from.
        // ⚠️ THE STAFF'S OWN INK IS DELIBERATELY NOT IN THIS. The accumulated profile also
        // carries the anchor staff raised into each line's frame — its clef hangs 3.550000
        // under its reference point — but that ink is already in the system's own extents
        // (SkylineBuilder.BuildSystemSkylines), and this figure is max-ed into the same
        // downExtent. Reading the profile whole would count the staff twice; what the page
        // needs here is the BLOCK's reach.
        return Math.Max(0, deepest - anchorStaff.Height / 2.0);
    }

    /// <summary>A lyric engraver configured for geometry only — one X model, no layout.</summary>
    private static LyricEngraver BuildBlockEngraver(MultiStaffScore score)
        => LyricEngraver.ForGeometry(score);

    private MultiStaffLayouter.LooseLinesBetween? BuildLooseLinesBetween(
        MultiStaffScore score, ImmutableArray<MeasureLayout> measureLayouts,
        int startMeasure, int endMeasure)
    {
        if (score.Lyrics.IsDefaultOrEmpty || score.StaffGroups.Length < 2)
            return null;
        bool anyNoteBound = false;
        foreach (var l in score.Lyrics)
            if (!l.IsLyricsRow) { anyNoteBound = true; break; }
        if (!anyNoteBound)
            return null;

        // staff index -> (its group, that group's staff range)
        var groupOf = new Dictionary<int, (int Group, int First, int End)>();
        int at = 0;
        for (int g = 0; g < score.StaffGroups.Length; g++)
        {
            int count = score.StaffGroups[g].StaffCount;
            for (int k = 0; k < count; k++)
                groupOf[at + k] = (g, at, at + count);
            at += count;
        }

        var engraver = BuildBlockEngraver(score);

        var cache = new Dictionary<(int, int), IReadOnlyList<(VerticalSkyline, VerticalSkyline)>?>();
        return (upperStaffIndex, lowerStaffIndex) =>
        {
            var key = (upperStaffIndex, lowerStaffIndex);
            if (cache.TryGetValue(key, out var hit))
                return hit;

            IReadOnlyList<(VerticalSkyline, VerticalSkyline)>? lines = null;
            if (groupOf.TryGetValue(upperStaffIndex, out var upper)
                && groupOf.TryGetValue(lowerStaffIndex, out var lower)
                && upper.Group != lower.Group)
            {
                var built = engraver.NoteBoundBlockSkylines(
                    score.Lyrics, measureLayouts, startMeasure, endMeasure,
                    upper.First, upper.End);
                if (built.Count > 0)
                    lines = built;
            }
            cache[key] = lines;
            return lines;
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
    /// ⚠️ THE ROOM IS READ PER SYSTEM AND THE ANCHOR IT IS DRAWN FROM IS NOT, and that is
    /// named rather than repaired here. <see cref="BuildStaffAnchorTables"/> takes
    /// <c>NoteBoundAnchorY</c> off <c>systemsArray[0]</c> — the simplification its own remark
    /// declares, shared by all four of its tables — while this walks the system it is asked
    /// about, which is what LilyPond does (<c>-solution_[spring_idx]</c> is that system's
    /// staff). The two disagree only where hara-kiri leaves different staves alive on
    /// different systems AND the block hangs from a non-last group; no fixture and no ledger
    /// point reaches that, so narrowing it would add a branch nothing measures — the same
    /// judgement <see cref="BuildLooseChainEnds"/>'s coarse bail-out is left on.
    /// </para>
    /// </remarks>
    private (double Room, VerticalSkyline NextStaffUp)? ComputeBetweenStavesEnd(
        int sysIdx, int anchorStaffIndex, ImmutableArray<SystemLayout> systems,
        IReadOnlyDictionary<int, Staff> staffByIndex)
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
        double? anchorDown = null;
        var below = new List<(double Down, int StaffIndex)>();
        var looseBands = new List<double>();
        foreach (var group in groups)
        {
            if (group.Staves.IsDefaultOrEmpty) continue;
            bool holdsAnchor = group.Staves.Any(s => s.StaffIndex == anchorStaffIndex);
            foreach (var st in group.Staves)
            {
                if (st.IsHidden) continue;
                double down = -st.Y;
                if (!StaffAffinity.IsSpaceable(st.StaffAffinity))
                {
                    looseBands.Add(down);
                    continue;
                }
                if (holdsAnchor)
                {
                    if (anchorDown is null || down > anchorDown) anchorDown = down;
                }
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
        if (nextDown is not { } next || !staffByIndex.TryGetValue(nextIndex, out var nextStaff))
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
        foreach (double band in looseBands)
            if (band > anchor && band < next)
                return null;

        // ⚠️ THE INDENT GOES WITH IT, and this is the call where it matters most: this
        // up-skyline is what CLOSES the chain, and the room the chain is solved into was
        // measured by MultiStaffLayouter against the same staff's skyline. If one carries
        // the clef (SkylineBuilder.SeedClef) and the other does not, the block is handed a
        // room its own closing step disagrees with.
        var up = _skylineBuilder.BuildStaffSkylines(
            nextStaff, systems[sysIdx].Measures, systemLeft: systems[sysIdx].Indent).Up;
        return (next - anchor, up);
    }

    /// <summary>
    /// Every staff's fingerings, pushed OUTSIDE any articulation that shares the note and
    /// the side so the two do not overprint.
    /// </summary>
    /// <remarks>
    /// Extracted verbatim from <see cref="CalculateAnnotationLayouts"/>.
    /// <para>
    /// Fingerings live on the NoteItem, so they must be read from EACH staff's own voice
    /// (<c>score.Voice</c> is only the first staff) and positioned at that staff's index —
    /// otherwise lower-staff fingerings vanish.
    /// </para>
    /// <para>
    /// LilyPond keeps the articulation (Script) close to the note and places the fingering
    /// on the OUTSIDE — verified against LilyPond 2.24.4: a fingering and a marcato forced
    /// above the same note render marcato inner, fingering outer (the digit sits above the
    /// marcato). So the FINGERING is the one that moves, not the articulation.
    /// LILYPOND-REF: lily/new-fingering-engraver.cc; empirical stacking order.
    /// </para>
    /// </remarks>
    private ImmutableArray<FingeringLayout> LayoutFingerings(
        Score? score, ImmutableArray<SystemLayout> systems,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff,
        ImmutableArray<ArticulationLayout> articulationLayouts,
        Func<int, int, double>? staffYAt)
    {
        ImmutableArray<FingeringLayout> fingeringLayouts;
        if (score == null)
            fingeringLayouts = ImmutableArray<FingeringLayout>.Empty;
        else if (voicesByStaff != null && voicesByStaff.Count > 0)
        {
            var fb = ImmutableArray.CreateBuilder<FingeringLayout>();
            foreach (var kv in voicesByStaff)
            {
                if (kv.Value.IsDefaultOrEmpty)
                    continue;
                var staffScore = new Score(kv.Value[0], score.TimeSignature,
                    score.KeySignature, score.Clef, score.Tempo);
                fb.AddRange(FingeringEngraver.Calculate(staffScore, systems, kv.Key));
            }
            fingeringLayouts = fb.ToImmutable();
        }
        else
            fingeringLayouts = FingeringEngraver.Calculate(score, systems);

        if (fingeringLayouts.IsDefaultOrEmpty || articulationLayouts.IsDefaultOrEmpty)
            return fingeringLayouts;

        // Gap from the outermost articulation's anchor to the fingering baseline.
        // Below needs more: the fingering baseline is the digit's lower edge.
        const double aboveGap = 1.4;
        const double belowGap = 1.9;
        // Outermost articulation anchor per note & side, in Y-up: "outermost" is the MOST
        // above (max YUp) or MOST below (min YUp). StaffIndex is -1 for single-staff
        // fingerings and 0 for their articulations, so normalise a negative staff to 0 on
        // both sides before matching.
        var artOuter = new Dictionary<(int, int, int, bool), double>();
        foreach (var a in articulationLayouts)
        {
            var key = (a.StaffIndex < 0 ? 0 : a.StaffIndex, a.MeasureIndex, a.ItemIndex, a.IsAbove);
            artOuter[key] = artOuter.TryGetValue(key, out var cur)
                ? (a.IsAbove ? System.Math.Max(cur, a.YUp) : System.Math.Min(cur, a.YUp))
                : a.YUp;
        }
        var fb2 = fingeringLayouts.ToBuilder();
        for (int i = 0; i < fb2.Count; i++)
        {
            var fg = fb2[i];
            var key = (fg.StaffIndex < 0 ? 0 : fg.StaffIndex, fg.MeasureIndex, fg.ItemIndex, fg.IsAbove);
            if (artOuter.TryGetValue(key, out var artYUp))
            {
                // Both are Y-up now; do the gap clamp in device against the shared
                // staff middle (fingering & its articulation are the same note/staff),
                // then reflect back to Y-up.
                double staffMid = (staffYAt?.Invoke(fg.MeasureIndex, fg.StaffIndex) ?? 0)
                    + _options.StaffHeight / 2.0;
                double artY = staffMid - artYUp; // ToDevice
                double fgY = staffMid - fg.YUp;  // ToDevice
                double target = fg.IsAbove ? artY - aboveGap : artY + belowGap;
                double newY = fg.IsAbove ? System.Math.Min(fgY, target) : System.Math.Max(fgY, target);
                fb2[i] = fg with { YUp = staffMid - newY }; // ToUp
            }
        }
        return fb2.ToImmutable();
    }

    /// <summary>
    /// Voice collision offsets / head-wipes / dot-force-down for multi-voice staves
    /// (so the renderer can nudge opposing voices apart), plus opt-in part-combine
    /// layouts. Keys are (measureIndex, voiceId, itemIndex) — correct for the common
    /// single-multi-voice-staff case. Extracted verbatim from the multi-staff
    /// <c>Layout</c> body.
    /// </summary>
    private (ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
             ImmutableHashSet<VoiceItemKey> HeadWipes,
             ImmutableHashSet<VoiceItemKey> DotForceDown,
             ImmutableArray<PartCombineLayout> PartCombine)
        CalculateVoiceCollisions(MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray)
    {
        var voiceOffsetsBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, double>();
        var headWipeBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();
        var dotForceDownBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();
        var partCombineLayouts = ImmutableArray<PartCombineLayout>.Empty;
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            if (staff.Voices.Length < 2)
                continue;

            var staffScore = new Score(
                staff.Voices, score.TimeSignature, score.KeySignature, ClefToString(staff.Clef));
            var (vo, hw, df) = _elementCoordinator.CalculateVoiceOffsets(staffScore);
            foreach (var kv in vo) voiceOffsetsBuilder[kv.Key] = kv.Value;
            foreach (var k in hw) headWipeBuilder.Add(k);
            foreach (var k in df) dotForceDownBuilder.Add(k);

            // Part combination is opt-in (\partcombine); plain << \\ >> voices
            // carry no a2/Solo text. Gated off by default to match LilyPond.
            if (_options.EnablePartCombine && partCombineLayouts.IsEmpty)
            {
                var ml = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
                var combineItems = PartCombineAnalyzer.Analyze(
                    staff.Voices[0], staff.Voices[1]);
                partCombineLayouts = PartCombineAnalyzer.Calculate(combineItems, ml, staff.Voices[0].Measures);
            }
        }
        return (voiceOffsetsBuilder.ToImmutable(), headWipeBuilder.ToImmutable(),
                dotForceDownBuilder.ToImmutable(), partCombineLayouts);
    }

    /// <summary>
    /// Common tail of both <c>Layout</c> overloads: stamp the engine options onto
    /// the built layout and, when the score carries user \override/\revert, attach
    /// a grob-property resolver.
    /// LILYPOND-REF: lily/grob-property.cc — user overrides/reverts on the layout.
    /// </summary>
    private ScoreLayout FinalizeLayout(ScoreLayout result,
        ImmutableArray<GrobOverride> grobOverrides, ImmutableArray<GrobRevert> grobReverts)
    {
        result = result with { Options = _options };
        if (!grobOverrides.IsDefaultOrEmpty || !grobReverts.IsDefaultOrEmpty)
        {
            result = result with
            {
                GrobPropertyResolver = new GrobPropertyResolver(grobOverrides, grobReverts)
            };
        }
        return result;
    }

    // Piano pedal sustain / sostenuto / una-corda marks.
    private static bool IsPedalMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
             or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    // A pedal RELEASE mark ("*"): the mixed style has no star (Ped._____| ).
    private static bool IsPedalOffMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOff or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOff;

    // Whether a mark's "Ped." / "*" text is still drawn under the given style.
    // LILYPOND-REF: lily/piano-pedal-engraver.cc — text keeps both, bracket keeps
    // neither, mixed keeps the leading "Ped." only.
    private static bool KeepPedalTextMark(MusicMarkItem m, PedalStyle style)
    {
        if (!IsPedalMark(m.Type)) return true;
        return style switch
        {
            PedalStyle.Text => true,
            PedalStyle.Mixed => !IsPedalOffMark(m.Type),
            _ => false,
        };
    }

    private static ScoreLayout BuildScoreLayout(
        ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beams, ImmutableArray<TieLayout> ties,
        ImmutableArray<SlurLayout> slurs, ImmutableArray<GlissandoLayout> glissandos,
        AnnotationLayouts a,
        ImmutableDictionary<VoiceItemKey, double> voiceOffsets,
        ImmutableHashSet<VoiceItemKey> headWipeEntries,
        ImmutableHashSet<VoiceItemKey> dotForceDownEntries,
        ImmutableDictionary<RestShiftKey, double> restShifts,
        ImmutableArray<PartCombineLayout> partCombineLayouts = default)
    {
        return new ScoreLayout(pages, systems, beams, ties, slurs,
            a.Dynamics, a.Articulations, a.GraceNotes,
            a.Lyrics, a.LyricHyphens, a.MusicMarks,
            a.CustomTexts, a.VoltaBrackets, a.TupletBrackets,
            a.Hairpins, a.TextSpanners, a.OttavaBrackets,
            glissandos, a.Arpeggios, a.PedalBrackets,
            a.FiguredBasses, a.ChordNames, a.PercentRepeats,
            a.CrossStaffs,
            partCombineLayouts.IsDefault ? ImmutableArray<PartCombineLayout>.Empty : partCombineLayouts,
            a.TrillSpanners,
            a.Fingerings,
            a.TieVariants,
            a.MultiMeasureRests,
            a.LedgerLineSpans,
            a.BarNumbers,
            a.StanzaNumbers,
            voiceOffsets, headWipeEntries, dotForceDownEntries, restShifts);
    }

    /// <summary>
    /// Calculates the indent needed to accommodate instrument names.
    /// Returns the indent in staff spaces, or 0 if no names are present.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pango-font.cc — LilyPond uses exact Pango font metrics.
    /// We approximate with average character width ≈ 0.5 × font-size for serif fonts.
    /// LILYPOND-REF: scm/define-grobs.scm:1851-1868 InstrumentName padding = 0.3
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
    /// </remarks>
    private static double CalculateIndentFromInstrumentNames(MultiStaffScore score)
    {
        // LILYPOND-REF: ly/paper-defaults-init.ly — indent = 15\mm
        // 15mm / (20pt/4 × 0.3528mm/pt) = 15 / 1.764 ≈ 8.5 staff spaces
        const double DefaultIndent = 8.5;

        double maxNameWidth = 0;

        // Font size: SvgRenderer.FontSize (4.0) × instrument name scale (0.75) = 3.0
        // Average serif char ≈ 0.5 em; CJK glyphs are full-width (≈ 1.0 em) —
        // the flat Latin estimate ran 「津田さん」 into the clef.
        const double nameFontSize = 3.0;

        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                if (!string.IsNullOrEmpty(staff.InstrumentName))
                {
                    const int cjkBlockStart = 0x2E80;  // CJK Radicals Supplement — first full-width block
                    const double cjkEmWidth = 1.0;     // CJK glyph ≈ full em
                    const double latinEmWidth = 0.5;   // average serif Latin char ≈ half em
                    double nameWidth = 0;
                    foreach (char ch in staff.InstrumentName)
                        nameWidth += (ch >= cjkBlockStart ? cjkEmWidth : latinEmWidth) * nameFontSize;
                    if (nameWidth > maxNameWidth)
                        maxNameWidth = nameWidth;
                }
            }
        }

        if (maxNameWidth <= 0)
            return 0;

        // LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
        // Indent = max(LP default 15mm, name width + delimiter + padding)
        double calculatedIndent = maxNameWidth + 1.5; // 1.0 delimiter + 0.3 name padding + 0.2 extra
        return Math.Max(DefaultIndent, calculatedIndent);
    }

    internal static string ClefToString(ClefType clef) => clef switch
    {
        ClefType.Treble => "treble",
        ClefType.Bass => "bass",
        ClefType.Alto => "alto",
        ClefType.Tenor => "tenor",
        ClefType.Treble8Below => "treble_8",
        _ => "treble"
    };

    /// <summary>The tuplets belonging to one staff — used to scope beam-break
    /// boundaries so a tuplet on another staff can't split this staff's beams.</summary>
    private static ImmutableArray<TupletBracketItem> StaffTuplets(
        ImmutableArray<TupletBracketItem> all, int staffIndex)
        => all.IsDefaultOrEmpty ? all
            : all.Where(t => t.StaffIndex == staffIndex).ToImmutableArray();

    private sealed record AnnotationLayouts(
        ImmutableArray<DynamicLayout> Dynamics,
        ImmutableArray<ArticulationLayout> Articulations,
        ImmutableArray<GraceNoteLayout> GraceNotes,
        ImmutableArray<LyricLayout> Lyrics,
        ImmutableArray<LyricHyphenLayout> LyricHyphens,
        ImmutableArray<MusicMarkLayout> MusicMarks,
        ImmutableArray<CustomTextLayout> CustomTexts,
        ImmutableArray<VoltaBracketLayout> VoltaBrackets,
        ImmutableArray<TupletBracketLayout> TupletBrackets,
        ImmutableArray<HairpinLayout> Hairpins,
        ImmutableArray<TextSpannerLayout> TextSpanners,
        ImmutableArray<OttavaBracketLayout> OttavaBrackets,
        ImmutableArray<ArpeggioLayout> Arpeggios,
        ImmutableArray<PedalBracketLayout> PedalBrackets,
        ImmutableArray<FiguredBassLayout> FiguredBasses,
        ImmutableArray<ChordNameLayout> ChordNames,
        ImmutableArray<PercentRepeatLayout> PercentRepeats,
        ImmutableArray<CrossStaffLayout> CrossStaffs,
        ImmutableArray<TrillSpannerLayout> TrillSpanners,
        ImmutableArray<FingeringLayout> Fingerings,
        ImmutableArray<TieVariantLayout> TieVariants,
        ImmutableArray<MultiMeasureRestLayout> MultiMeasureRests,
        ImmutableArray<LedgerLineSpan> LedgerLineSpans,
        ImmutableArray<BarNumberLayout> BarNumbers,
        ImmutableArray<StanzaNumberLayout> StanzaNumbers);
}
