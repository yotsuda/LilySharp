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
public sealed class LayoutEngine
{
    private readonly LayoutOptions _options;
    private readonly ElementCoordinator _elementCoordinator;
    private readonly SkylineBuilder _skylineBuilder;
    private readonly MeasureLayouter _measureLayouter = new();
    private readonly SystemLayouter _systemLayouter;
    private readonly PageLayouter _pageLayouter;
    private readonly SystemBreaker _systemBreaker;
    private readonly MultiStaffLayouter _multiStaffLayouter;

    public LayoutEngine(LayoutOptions? options = null)
    {
        _options = options ?? LayoutOptions.Default;
        _elementCoordinator = new ElementCoordinator(_options);
        _skylineBuilder = new SkylineBuilder(_options.StaffHeight);
        _systemLayouter = new SystemLayouter(_options, _measureLayouter);
        _pageLayouter = new PageLayouter(_options);
        _systemBreaker = new SystemBreaker(_options);
        _multiStaffLayouter = new MultiStaffLayouter(_options, _measureLayouter);
    }

    /// <summary>Calculates the complete layout for a single-staff score.</summary>
    public ScoreLayout Layout(Score score)
    {
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);
        double headerBottom = _options.MarginTop + headerHeight;

        // LILYPOND-REF: lily/spacing-spanner.cc
        // Calculate the common shortest duration across all voices for Gourlay spacing
        double commonShortestDuration = SpacingRules.CalculateCommonShortestDuration(score);

        var systemMeasures = _systemBreaker.BreakIntoSystems(score, commonShortestDuration);

        // Pre-calculate first system skylines for initial Y positioning
        var firstSystemMeasures = systemMeasures.Count > 0 ? systemMeasures[0] : new List<Measure>();
        var firstMeasureLayouts = _systemLayouter.LayoutMeasuresForSystem(firstSystemMeasures, score.KeySignature.Sharps, true, 0,
            baseShortestDuration: commonShortestDuration);
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(firstSystemMeasures, firstMeasureLayouts);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, LayoutUtilities.CalculateUpExtent(firstUpSkyline), _options.TopSystemPadding);

        // Layout systems and build skylines in a single pass
        var systems = new List<SystemLayout>();
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        var perSystemSkylines = new List<(VerticalSkyline up, VerticalSkyline down)>();
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            var system = _systemLayouter.LayoutSystem(
                sysIdx, systemMeasures[sysIdx], currentY,
                score.KeySignature.Sharps, sysIdx == 0, firstMeasureIndex,
                score.Lyrics, isLastSystem: sysIdx == systemMeasures.Count - 1,
                baseShortestDuration: commonShortestDuration);
            systems.Add(system);

            var (upSkyline, downSkyline) = _skylineBuilder.BuildSystemSkylines(systemMeasures[sysIdx], system.Measures);
            perSystemSkylines.Add((upSkyline, downSkyline));
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSkyline),
                LayoutUtilities.CalculateDownExtent(downSkyline, _options.StaffHeight)));

            currentY += _options.StaffHeight + _options.SystemSpacing;
            firstMeasureIndex += systemMeasures[sysIdx].Count;
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
        // Augment system extents with estimated loose line heights (lyrics, dynamics, figured bass)
        var singleMeasureRanges = new List<(int startMeasure, int measureCount)>();
        int measStart = 0;
        foreach (var sysMeasures in systemMeasures)
        {
            singleMeasureRanges.Add((measStart, sysMeasures.Count));
            measStart += sysMeasures.Count;
        }
        AugmentExtentsWithLooseLines(perSystemExtents,
            score.Lyrics, score.Dynamics, score.FiguredBasses,
            score.MusicMarks, score.VoltaBrackets, singleMeasureRanges);

        // Preliminary annotation pass: real protrusions join the spacing
        // extents (see EnrichExtentsWithAnnotationProtrusions). These
        // layouts are discarded; the final pass below recomputes them
        // against the re-spaced systems.
        {
            var prelimSystems = systems.ToImmutableArray();
            var prelimBeams = _elementCoordinator.LayoutBeams(score, prelimSystems);
            var prelimAnn = CalculateAnnotationLayouts(
                score, prelimSystems,
                score.Dynamics, score.Articulations, score.GraceNotes,
                score.Lyrics, score.MusicMarks, score.CustomTexts,
                score.VoltaBrackets, score.TupletBrackets, score.Arpeggios,
                score.Voice.Measures, score.FiguredBasses, score.ChordNames, score.PercentRepeats,
                trillSpanners: score.TrillSpanners,
                beamGroups: _elementCoordinator.DetectBeamGroups(score),
                beamLayouts: prelimBeams,
                systemSkylines: perSystemSkylines,
                tupletForceStemUp: score.IsMultiVoice,
                staffVoices: score.Voices);
            EnrichExtentsWithAnnotationProtrusions(perSystemExtents, prelimSystems,
                prelimAnn,
                _elementCoordinator.LayoutTies(score, prelimSystems),
                _elementCoordinator.LayoutSlurs(score, prelimSystems));
        }

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, _options.StaffHeight,
            perSystemSkylines);

        var beamLayouts = _elementCoordinator.LayoutBeams(score, systemsArray);
        var tieLayouts = _elementCoordinator.LayoutTies(score, systemsArray);
        var slurLayouts = _elementCoordinator.LayoutSlurs(score, systemsArray);
        var glissandoLayouts = _elementCoordinator.LayoutGlissandos(score, systemsArray);
        // LILYPOND-REF: lily/note-collision.cc:486-502
        // Create a resolver for force-hshift manual override during collision calculation
        GrobPropertyResolver? collisionResolver = null;
        if (!score.GrobOverrides.IsDefaultOrEmpty || !score.GrobReverts.IsDefaultOrEmpty)
        {
            collisionResolver = new GrobPropertyResolver(score.GrobOverrides, score.GrobReverts);
        }
        var (voiceOffsets, headWipeEntries, dotForceDownEntries) = _elementCoordinator.CalculateVoiceOffsets(score, collisionResolver);
        var restShifts = _elementCoordinator.CalculateRestShifts(score, systemsArray, beamLayouts);

        // Detect beam groups for tuplet bracket-visibility (if-no-beam)
        // LILYPOND-REF: scm/define-grobs.scm TupletBracket.bracket-visibility
        var beamGroups = _elementCoordinator.DetectBeamGroups(score);

        var annotations = CalculateAnnotationLayouts(
            score, systemsArray,
            score.Dynamics, score.Articulations, score.GraceNotes,
            score.Lyrics, score.MusicMarks, score.CustomTexts,
            score.VoltaBrackets, score.TupletBrackets, score.Arpeggios,
            score.Voice.Measures, score.FiguredBasses, score.ChordNames, score.PercentRepeats,
            trillSpanners: score.TrillSpanners,
            beamGroups: beamGroups,
            beamLayouts: beamLayouts,
            systemSkylines: perSystemSkylines,
            tupletForceStemUp: score.IsMultiVoice,
            staffVoices: score.Voices);

        // Calculate part combination layouts for multi-voice scores.
        // LILYPOND-REF: part combination is opt-in (\partcombine); plain << \\ >>
        // voices are not combined and carry no a2/Solo text. Gated off by default.
        var partCombineLayouts = ImmutableArray<PartCombineLayout>.Empty;
        if (_options.EnablePartCombine && score.IsMultiVoice && score.Voices.Length >= 2)
        {
            var ml = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
            var combineItems = PartCombineAnalyzer.Analyze(
                score.Voices[0], score.Voices[1], score.TimeSignature);
            partCombineLayouts = PartCombineAnalyzer.Calculate(combineItems, ml, score.Voices[0].Measures);
        }

        var result = BuildScoreLayout(pages, systemsArray, beamLayouts, tieLayouts, slurLayouts,
            glissandoLayouts, annotations, voiceOffsets, headWipeEntries, dotForceDownEntries, restShifts, partCombineLayouts);
        return FinalizeLayout(result, score.GrobOverrides, score.GrobReverts);
    }

    /// <summary>Calculates the complete layout for a multi-staff score.</summary>
    public ScoreLayout Layout(MultiStaffScore score, IReadOnlyList<int>? precomputedLineSizes = null,
        SystemLayoutCache? systemCache = null)
    {
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);
        double headerBottom = _options.MarginTop + headerHeight;

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
        double systemHeight = multiStaffLayouter.CalculateSystemHeight(
            score, _skylineBuilder, firstSystemMeasureLayouts);
        var firstStaffGroupLayouts = multiStaffLayouter.LayoutStaffGroups(
            score, 0, _skylineBuilder, firstSystemMeasureLayouts);

        // Pre-compute staff group layouts for subsequent systems (shortIndent)
        multiStaffLayouter.CurrentIndent = shortIndent;
        var defaultStaffGroupLayouts = indent != shortIndent
            ? multiStaffLayouter.LayoutStaffGroups(score, 0, _skylineBuilder, firstSystemMeasureLayouts)
            : firstStaffGroupLayouts;

        // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — check if any staff uses remove-empty
        bool hasHaraKiri = score.StaffGroups.Any(g => g.Staves.Any(s => s.RemoveEmpty));

        // Pre-calculate first system skylines for initial Y positioning
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(score, firstSystemMeasureLayouts, systemHeight);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, LayoutUtilities.CalculateUpExtent(firstUpSkyline), _options.TopSystemPadding);

        // Layout each system with skyline extents
        var systems = new List<SystemLayout>();
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        var perSystemSkylines = new List<(VerticalSkyline up, VerticalSkyline down)>();
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

            // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — per-system staff visibility
            // When hara-kiri is active, compute per-system staff group layouts
            // so empty staves are hidden only in systems where they have no content.
            var sysStaffGroups = hasHaraKiri
                ? multiStaffLayouter.LayoutStaffGroups(
                    score, 0, firstMeasureIndex, firstMeasureIndex + measureCount, isFirstSystem)
                : (isFirstSystem ? firstStaffGroupLayouts : defaultStaffGroupLayouts);

            // Use per-system height when hara-kiri is active (different staves may be visible per system)
            double sysHeight = hasHaraKiri
                ? sysStaffGroups.Where(g => !g.Staves.All(s => s.IsHidden)).Sum(g => g.Height)
                    + Math.Max(0, (sysStaffGroups.Count(g => !g.Staves.All(s => s.IsHidden)) - 1)
                        * (_options.StaffSpacing.StaffGroupStaff.BasicDistance - _options.StaffHeight))
                : systemHeight;
            // Ensure at least one staff space for completely empty systems
            if (hasHaraKiri && sysHeight <= 0)
                sysHeight = _options.StaffHeight;

            var (upSky, downSky) = ComputeSystemSkyline(systemCache, firstMeasureIndex, measureCount,
                isFirstSystem, sysIdx == systemMeasures.Count - 1, sysIndent, commonShortestDuration, sysHeight,
                () => _skylineBuilder.BuildSystemSkylines(score, measureLayouts, sysHeight));
            perSystemSkylines.Add((upSky, downSky));
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSky),
                LayoutUtilities.CalculateDownExtent(downSky, sysHeight)));

            systems.Add(new SystemLayout(
                SystemIndex: sysIdx, Y: currentY,
                Width: _options.ContentWidth - sysIndent,
                PrefixWidth: SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, isFirstSystem,
                    score.TimeSignature.Beats, score.TimeSignature.BeatType),
                Measures: measureLayouts, StaffGroups: sysStaffGroups,
                Indent: sysIndent));
            currentY += sysHeight + _options.SystemSpacing;
            firstMeasureIndex += measureCount;
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
        var multiMeasureRanges = new List<(int startMeasure, int measureCount)>();
        int multiMeasStart = 0;
        foreach (var sysMeasures in systemMeasures)
        {
            multiMeasureRanges.Add((multiMeasStart, sysMeasures.Count));
            multiMeasStart += sysMeasures.Count;
        }
        AugmentExtentsWithLooseLines(perSystemExtents,
            score.Lyrics, score.Dynamics, score.FiguredBasses,
            score.MusicMarks, score.VoltaBrackets, multiMeasureRanges);

        // Preliminary annotation pass (see the single-staff path): real
        // protrusions of brackets/marks/voltas/dynamics/ties/slurs join the
        // spacing extents before the page Y is fixed.
        {
            var prelimSystems = systems.ToImmutableArray();
            var prelimStaff = score.PrimaryContentStaff;
            var prelimScore = new Score(
                prelimStaff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(prelimStaff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: score.TupletBrackets);
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
                prelimBeams.AddRange(_elementCoordinator.LayoutBeams(staffBeamScore, prelimSystems, staffIndex));
                prelimTies.AddRange(_elementCoordinator.LayoutTies(staffScore, prelimSystems, staffIndex, staff));
                prelimSlurs.AddRange(_elementCoordinator.LayoutSlurs(staffScore, prelimSystems, staffIndex));
            }
            var prelimAnn = CalculateAnnotationLayouts(
                prelimScore, prelimSystems,
                score.Dynamics, score.Articulations, score.GraceNotes,
                score.Lyrics, score.MusicMarks, score.CustomTexts,
                score.VoltaBrackets, score.TupletBrackets, score.Arpeggios,
                prelimStaff.PrimaryVoice.Measures, score.FiguredBasses, score.ChordNames,
                score.PercentRepeats,
                trillSpanners: score.TrillSpanners,
                beamGroups: _elementCoordinator.DetectBeamGroups(prelimScore),
                beamLayouts: prelimBeams.ToImmutableArray(),
                systemSkylines: perSystemSkylines,
                tupletForceStemUp: prelimStaff.IsMultiVoice,
                staffVoices: prelimStaff.Voices);
            EnrichExtentsWithAnnotationProtrusions(perSystemExtents, prelimSystems,
                prelimAnn, prelimTies.ToImmutableArray(), prelimSlurs.ToImmutableArray());
        }

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, systemHeight,
            perSystemSkylines);

        // Calculate beams/ties/slurs/glissandos per staff
        var allBeamLayouts = new List<BeamLayout>();
        var allTieLayouts = new List<TieLayout>();
        var allSlurLayouts = new List<SlurLayout>();
        var allGlissandoLayouts = new List<GlissandoLayout>();
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
            allBeamLayouts.AddRange(_elementCoordinator.LayoutBeams(staffSpannerScore, systemsArray, staffIndex));
            allTieLayouts.AddRange(_elementCoordinator.LayoutTies(staffSpannerScore, systemsArray, staffIndex, staff));
            allSlurLayouts.AddRange(_elementCoordinator.LayoutSlurs(staffSpannerScore, systemsArray, staffIndex));
            allGlissandoLayouts.AddRange(_elementCoordinator.LayoutGlissandos(staffSpannerScore, systemsArray, staffIndex));
        }

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
            tupletBrackets: score.TupletBrackets, swingSubdivision: score.SwingSubdivision);

        // Per-staff lookups so a dynamic is positioned under its OWN staff (clears
        // that staff's stems) and offset to it — score-level dynamics otherwise all
        // collapse onto the first staff. Staff vertical offsets are uniform across
        // systems, so read them from the first laid-out system.
        var voicesByStaff = new Dictionary<int, ImmutableArray<Voice>>();
        var measuresByStaff = new Dictionary<int, ImmutableArray<Measure>>();
        var staffByIndex = new Dictionary<int, Staff>();
        foreach (var (g, st, idx) in score.EnumerateStaves())
        {
            voicesByStaff[idx] = st.Voices;
            measuresByStaff[idx] = st.PrimaryVoice.Measures;
            staffByIndex[idx] = st;
        }
        var staffYByIndex = new Dictionary<int, double>();
        if (systemsArray.Length > 0 && !systemsArray[0].StaffGroups.IsDefaultOrEmpty)
            foreach (var sg in systemsArray[0].StaffGroups)
                foreach (var st in sg.Staves)
                    staffYByIndex[st.StaffIndex] = st.Y;

        var annotations = CalculateAnnotationLayouts(
            primaryScore, systemsArray,
            score.Dynamics, score.Articulations, score.GraceNotes,
            score.Lyrics, score.MusicMarks, score.CustomTexts,
            score.VoltaBrackets, score.TupletBrackets, score.Arpeggios,
            primaryStaff.PrimaryVoice.Measures, score.FiguredBasses, score.ChordNames,
            score.PercentRepeats, crossStaffLayouts,
            trillSpanners: score.TrillSpanners,
            // bracket-visibility = if-no-beam needs the beam groups; without
            // them every tuplet bracket draws even when fully beamed. The
            // beam LAYOUTS let the suppressed-bracket number attach to the
            // beam itself.
            beamGroups: _elementCoordinator.DetectBeamGroups(primaryScore),
            beamLayouts: allBeamLayouts.ToImmutableArray(),
            systemSkylines: perSystemSkylines,
            tupletForceStemUp: primaryStaff.IsMultiVoice,
            staffVoices: primaryStaff.Voices,
            voicesByStaff: voicesByStaff,
            measuresByStaff: measuresByStaff,
            staffYByIndex: staffYByIndex,
            staffByIndex: staffByIndex);

        // Voice collision offsets / head-wipes for multi-voice staves, so the
        // renderer can nudge opposing voices apart. Computed per staff; the keys
        // are (measureIndex, voiceId, itemIndex) — fully correct for the common
        // single-multi-voice-staff case. (Multiple multi-voice staves in one
        // system would share keys; rare, handled best-effort.)
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
                    staff.Voices[0], staff.Voices[1], score.TimeSignature);
                partCombineLayouts = PartCombineAnalyzer.Calculate(combineItems, ml, staff.Voices[0].Measures);
            }
        }

        var result = BuildScoreLayout(pages, systemsArray,
            allBeamLayouts.ToImmutableArray(), allTieLayouts.ToImmutableArray(),
            allSlurLayouts.ToImmutableArray(), allGlissandoLayouts.ToImmutableArray(),
            annotations,
            voiceOffsetsBuilder.ToImmutable(),
            headWipeBuilder.ToImmutable(),
            dotForceDownBuilder.ToImmutable(),
            ImmutableDictionary<RestShiftKey, double>.Empty,
            partCombineLayouts);
        return FinalizeLayout(result, score.GrobOverrides, score.GrobReverts);
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
        List<(VerticalSkyline up, VerticalSkyline down)>? perSystemSkylines = null)
    {
        // An empty score (no systems) has nothing to page; return empty rather than
        // indexing perSystemExtents[0] below.
        if (systems.IsDefaultOrEmpty || perSystemExtents.Count == 0)
            return (ImmutableArray<PageLayout>.Empty, ImmutableArray<SystemLayout>.Empty);

        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
        {
            // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
            // Pass per-system skylines for X-dependent inter-system collision detection
            var skylines = perSystemSkylines != null
                ? (ImmutableArray<(VerticalSkyline, VerticalSkyline)>?)perSystemSkylines.ToImmutableArray()
                : null;
            var pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systems, headerHeight, perSystemExtents.ToImmutableArray(), skylines);
            return (pages, pages.SelectMany(p => p.Systems).ToImmutableArray());
        }

        // Recalculate Y positions using skyline extents to avoid overlaps
        double headerBottom = _options.MarginTop + headerHeight;
        double skylineY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, perSystemExtents[0].upExtent, _options.TopSystemPadding);
        var updatedSystems = new List<SystemLayout>();
        for (int i = 0; i < systems.Length; i++)
        {
            updatedSystems.Add(systems[i] with { Y = skylineY });
            if (i < systems.Length - 1)
            {
                double padding = _options.SystemSpacing * 0.5;

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
                double skylineDistance = systemHeight
                    + perSystemExtents[i].downExtent + perSystemExtents[i + 1].upExtent;
                if (perSystemSkylines != null && i + 1 < perSystemSkylines.Count)
                {
                    double dist = perSystemSkylines[i].down.Distance(perSystemSkylines[i + 1].up);
                    if (!double.IsNegativeInfinity(dist))
                        skylineDistance = dist;
                }

                double minDistance = Math.Max(
                    systemHeight + _options.SystemSpacing, skylineDistance + padding);
                skylineY += minDistance;
            }
        }
        double lastDownExtent = perSystemExtents[systems.Length - 1].downExtent;
        double totalHeight = skylineY + systemHeight + lastDownExtent + _options.MarginBottom;
        var systemsArray = updatedSystems.ToImmutableArray();
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
    private static (double downExtent, double upExtent) EstimateLooseLineExtents(
        ImmutableArray<LyricItem> lyrics,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        int startMeasure, int endMeasure)
    {
        double downExtent = 0;
        double upExtent = 0;

        // --- Below-staff elements (downExtent) ---

        // LILYPOND-REF: scm/define-grobs.scm LyricText.outside-staff-priority = #(* 100 1)
        // Lyrics: staffPadding(2.5) + (verseCount-1) * verseSpacing(1.8) + fontSize(1.2)
        if (!lyrics.IsDefaultOrEmpty)
        {
            int maxVerse = 0;
            foreach (var lyric in lyrics)
            {
                // Independent lyrics-row syllables get their own text band; they must
                // not reserve phantom space under a music staff (that inflates the gap).
                if (lyric.IsLyricsRow)
                    continue;
                if (lyric.MeasureIndex >= startMeasure && lyric.MeasureIndex < endMeasure)
                    maxVerse = Math.Max(maxVerse, lyric.VerseNumber);
            }
            if (maxVerse > 0)
                downExtent = Math.Max(downExtent, 2.5 + (maxVerse - 1) * 1.8 + 1.2);
        }

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

                // LILYPOND-REF: scm/define-grobs.scm MetronomeMark.outside-staff-priority = 1000
                if (mark.Type == MusicMarkType.Tempo)
                    upExtent = Math.Max(upExtent, 2.5); // tempo + metronome mark height

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

        return (downExtent, upExtent);
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
                            bottoms[i] = Math.Max(bottoms[i], st.Y + st.Height);
            }
        }

        void Add(int measureIndex, double topRel, double bottomRel)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            up[s] = Math.Max(up[s], -topRel);
            down[s] = Math.Max(down[s], bottomRel - bottoms[s]);
        }

        foreach (var t in ann.TupletBrackets)
        {
            double hi = Math.Min(t.StartY, t.EndY);
            double lo = Math.Max(t.StartY, t.EndY);
            Add(t.MeasureIndex, hi - (t.IsStemUp ? 1.6 : 0.1), lo + (t.IsStemUp ? 0.7 : 1.7));
        }
        foreach (var v in ann.VoltaBrackets)
            Add(v.StartMeasureIndex, v.Y - 0.1, v.Y + 1.6);
        foreach (var m in ann.MusicMarks)
        {
            if (MusicMarkItem.IsSpannerHandled(m.MarkType))
                continue;
            Add(m.MeasureIndex, m.Y - 2.1, m.Y + 0.7);
        }
        foreach (var ct in ann.CustomTexts)
            Add(ct.MeasureIndex, ct.Y - 1.8, ct.Y + 0.6);
        // Chord names ride above the staff and rise (ChordNameEngraver skyline) to
        // clear high notes; their REAL text top must join the system up-extent or a
        // lifted chord line pokes into the header/title. Chord font = FontSize*0.65
        // (≈2.6 ss), Middle-anchored, so the glyph top is cap-height/2 ≈ 0.9 above
        // the anchor and the descent ≈ 0.3 below.
        foreach (var cn in ann.ChordNames)
            Add(cn.MeasureIndex, cn.Y - 0.9, cn.Y + 0.3);
        foreach (var tr in ann.TrillSpanners)
            Add(tr.StartMeasureIndex, tr.Y - GlyphMetrics.OrnTrillGlyph.Top, tr.Y + 0.25);
        foreach (var d in ann.Dynamics)
            Add(d.MeasureIndex, d.Y - 1.2, d.Y + 0.3);
        foreach (var h in ann.Hairpins)
            Add(h.StartMeasureIndex, h.Y - 0.34, h.Y + 0.34);
        foreach (var sp in ann.TextSpanners)
            Add(sp.StartMeasureIndex, sp.Y - 1.2, sp.Y + 0.3);
        foreach (var bn in ann.BarNumbers)
        {
            if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int s))
                continue;
            double rel = bn.Y - systems[s].Y;
            up[s] = Math.Max(up[s], -(rel - 1.3));
        }

        // Ties and slurs carry ABSOLUTE page Y — relativize via their system.
        void AddCurve(int measureIndex, double y0, double y1, double c1, double c2)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            double sy = systems[s].Y;
            // Curve extreme ~ 3/4 of the way from endpoints to controls.
            double topRel = Math.Min(Math.Min(y0, y1), Math.Min(y0, y1) * 0.25 + Math.Min(c1, c2) * 0.75) - sy;
            double botRel = Math.Max(Math.Max(y0, y1), Math.Max(y0, y1) * 0.25 + Math.Max(c1, c2) * 0.75) - sy;
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
            AddCurve(mi, t.StartY, t.EndY, t.Control1.Y, t.Control2.Y);
        }
        foreach (var sl in slurs)
        {
            int mi = sl.IsBrokenLeft ? sl.Slur.EndMeasureIndex : sl.Slur.StartMeasureIndex;
            AddCurve(mi, sl.StartY, sl.EndY, sl.Control1.Y, sl.Control2.Y);
        }

        for (int i = 0; i < n; i++)
        {
            var ext = perSystemExtents[i];
            perSystemExtents[i] = (
                Math.Max(ext.upExtent, up[i]),
                Math.Max(ext.downExtent, down[i]));
        }
    }

    private static void AugmentExtentsWithLooseLines(
        List<(double upExtent, double downExtent)> perSystemExtents,
        ImmutableArray<LyricItem> lyrics,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        List<(int startMeasure, int measureCount)> systemMeasureRanges)
    {
        for (int i = 0; i < perSystemExtents.Count && i < systemMeasureRanges.Count; i++)
        {
            var (start, count) = systemMeasureRanges[i];
            var (looseDown, looseUp) = EstimateLooseLineExtents(
                lyrics, dynamics, figuredBasses, musicMarks, voltaBrackets,
                start, start + count);

            var ext = perSystemExtents[i];
            if (looseDown > 0 || looseUp > 0)
            {
                perSystemExtents[i] = (
                    Math.Max(ext.upExtent, looseUp),
                    Math.Max(ext.downExtent, looseDown));
            }
        }
    }

    private AnnotationLayouts CalculateAnnotationLayouts(
        Score? score, ImmutableArray<SystemLayout> systems,
        ImmutableArray<DynamicItem> dynamics, ImmutableArray<ArticulationItem> articulations,
        ImmutableArray<GraceNoteItem> graceNotes, ImmutableArray<LyricItem> lyrics,
        ImmutableArray<MusicMarkItem> musicMarks, ImmutableArray<CustomTextItem> customTexts,
        ImmutableArray<VoltaBracketItem> voltaBrackets, ImmutableArray<TupletBracketItem> tupletBrackets,
        ImmutableArray<ArpeggioItem> arpeggios, ImmutableArray<Measure> measures,
        ImmutableArray<FiguredBassItem>? figuredBasses = null,
        ImmutableArray<ChordNameItem>? chordNames = null,
        ImmutableArray<PercentRepeatItem>? percentRepeats = null,
        ImmutableArray<CrossStaffLayout>? crossStaffLayouts = null,
        ImmutableArray<TrillSpannerItem>? trillSpanners = null,
        ImmutableArray<BeamGroup>? beamGroups = null,
        ImmutableArray<BeamLayout>? beamLayouts = null,
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines = null,
        bool tupletForceStemUp = false,
        ImmutableArray<Voice> staffVoices = default,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff = null,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Dictionary<int, double>? staffYByIndex = null,
        Dictionary<int, Staff>? staffByIndex = null)
    {
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var lyricLayouts = new LyricEngraver().CalculateLayouts(
            lyrics, ml, _options.StaffHeight, systems, systemSkylines, staffYByIndex);

        // LILYPOND-REF: axis-group-interface.cc skyline_spacing
        // Outside-staff elements are placed in priority order (lower priority = closer to staff).
        // DynamicLineSpanner (250) must be calculated before TextSpanner (350)
        // so text spanners can be placed below dynamics.

        // Dynamics first (outside-staff-priority: 250)
        var dynamicLayouts = score != null ? DynamicEngraver.Calculate(score, dynamics, systems, ml, staffVoices, voicesByStaff, measuresByStaff, staffYByIndex) : ImmutableArray<DynamicLayout>.Empty;

        // Detect and layout hairpins from cresc/decresc marks
        var hairpinItems = HairpinEngraver.DetectHairpins(musicMarks, dynamics);
        var hairpinLayouts = HairpinEngraver.Calculate(hairpinItems, systems, ml, staffYByIndex);

        // Detect and layout text spanners from rit/accel marks (outside-staff-priority: 350)
        // Pass dynamic layouts so text spanners can stack below them
        var textSpannerItems = TextSpannerEngraver.DetectTextSpanners(musicMarks);
        var textSpannerLayouts = TextSpannerEngraver.Calculate(textSpannerItems, systems, ml, dynamicLayouts, staffYByIndex);

        // Detect and layout ottava brackets from ottava/loco marks
        var ottavaItems = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);
        var ottavaLayouts = OttavaBracketEngraver.Calculate(ottavaItems, systems, ml, staffYByIndex);

        // Layout arpeggio markings
        var arpeggioLayouts = ArpeggioEngraver.Calculate(arpeggios, systems, ml, _options.StaffHeight, measures, measuresByStaff, staffYByIndex);

        // Pedal rendering uses the default TEXT style: "Ped." at the engage note
        // and "*" at the release note, with NO connecting line or hook (those
        // belong to the bracket / mixed styles, which Lily# does not emit). The
        // "Ped." / "*" text is drawn from the SustainOn/Off music marks, so here
        // we emit no bracket layout.
        // LILYPOND-REF: scm/define-grobs.scm SustainPedal — default
        //   pedalSustainStyle = 'text ("Ped." … "*"); 'bracket / 'mixed add the
        //   line+hook and are separate styles.
        var pedalBracketLayouts = ImmutableArray<PedalBracketLayout>.Empty;

        // Layout figured bass
        var figuredBassLayouts = FiguredBassEngraver.Calculate(
            figuredBasses ?? ImmutableArray<FiguredBassItem>.Empty, systems, ml, measures,
            measuresByStaff, staffYByIndex, systemSkylines);

        // Layout chord names (skyline-spaced above high notes when skylines available)
        var chordNameLayouts = ChordNameEngraver.Calculate(
            chordNames ?? ImmutableArray<ChordNameItem>.Empty, systems, ml, measures,
            measuresByStaff, staffYByIndex, systemSkylines);

        // Layout percent repeats
        var percentRepeatLayouts = PercentRepeatEngraver.Calculate(
            percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty, systems, ml);

        // Layout trill spanners (tr + wavy line)
        // LILYPOND-REF: scm/scheme-engravers.scm — trill spanner positioning
        var trillSpannerLayouts = TrillSpannerEngraver.Calculate(
            trillSpanners ?? ImmutableArray<TrillSpannerItem>.Empty, systems, ml, staffYByIndex);

        // Calculate volta brackets first — needed by MusicMarkEngraver for collision avoidance
        // LILYPOND-REF: axis-group-interface.cc — elements sorted by outside-staff-priority
        var voltaBracketLayouts = VoltaBracketEngraver.Calculate(voltaBrackets, systems, ml);

        // LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside_staff_axis_group
        // Post-process below-staff elements using priority-based stacking.
        // This ensures hairpins avoid dynamics (both priority 250) and
        // text spanners avoid both dynamics and hairpins (priority 350).
        var articulationLayouts = score != null
            ? ArticulationEngraver.Calculate(score, articulations, systems, ml, measuresByStaff, staffYByIndex, staffByIndex)
            : ImmutableArray<ArticulationLayout>.Empty;
        var (stackedDynamics, stackedHairpins, stackedTextSpanners) =
            OutsideStaffStacker.StackBelowStaff(systems, dynamicLayouts, hairpinLayouts, textSpannerLayouts,
                articulationLayouts, staffYByIndex);

        // ABOVE-staff: one unified priority pass (trill 50, bar number 100,
        // tuplet brackets 200 as immovable seeds, ottava 400, text 450,
        // volta 600, marks 1500), seeded from the per-system up-skylines.
        // Replaces the old pairwise hacks (bar-number-vs-volta in the
        // renderer; music-mark-vs-volta inside MusicMarkEngraver).
        var tupletBracketLayouts = TupletBracketEngraver.Calculate(
            tupletBrackets, systems, ml, measures, beamGroups ?? default, beamLayouts ?? default,
            forceStemUp: tupletForceStemUp,
            measuresByStaff: measuresByStaff, voicesByStaff: voicesByStaff, staffYByIndex: staffYByIndex);
        var musicMarkLayouts = MusicMarkEngraver.Calculate(
            score, musicMarks, systems, ml, measures, default);
        var customTextLayouts = CustomTextEngraver.Calculate(score, customTexts, systems, ml);
        // A leading \partial pickup is bar 0: shift displayed numbers down by one
        // so the first FULL measure is numbered 1, not 2.
        int barNumberOffset = (!measures.IsDefaultOrEmpty && measures[0].IsPickup) ? -1 : 0;
        var barNumberLayouts = BarNumberEngraver.Calculate(systems, numberOffset: barNumberOffset);
        // Forced-above dynamics (@f.up) join the above-staff pass so they clear, and are
        // cleared by, the other above-staff grobs. Below dynamics were already placed by
        // StackBelowStaff and pass through untouched.
        var (stackedTrills, stackedBarNumbers, stackedOttavas, stackedCustomTexts,
             stackedVoltas, stackedMarks, stackedDynamicsAbove) = OutsideStaffStacker.StackAboveStaff(
            systems, systemSkylines, tupletBracketLayouts,
            trillSpannerLayouts, barNumberLayouts, ottavaLayouts,
            customTextLayouts, voltaBracketLayouts, musicMarkLayouts,
            articulationLayouts, aboveDynamics: stackedDynamics);
        stackedDynamics = stackedDynamicsAbove;
        // After stacking, sit a boundary "To Coda" on the adjacent section label's
        // line (the two straddle one barline) instead of stacking them apart.
        stackedMarks = MusicMarkEngraver.CoPlaceToCodaWithLabels(stackedMarks);

        // Fingerings live on the NoteItem, so they must be read from EACH staff's
        // own voice (score.Voice is only the first staff) and positioned at that
        // staff's index — otherwise lower-staff fingerings vanish.
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

        return new AnnotationLayouts(
            Dynamics: stackedDynamics,
            Articulations: articulationLayouts,
            GraceNotes: score != null ? GraceNoteEngraver.Calculate(score, graceNotes, systems, ml, measuresByStaff, staffYByIndex, staffByIndex) : ImmutableArray<GraceNoteLayout>.Empty,
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
    /// LILYPOND-REF: scm/define-grobs.scm:1711-1728 InstrumentName padding = 0.3
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
    /// </remarks>
    private static double CalculateIndentFromInstrumentNames(MultiStaffScore score)
    {
        // LILYPOND-REF: ly/paper-defaults-init.ly — indent = 15\mm
        // 15mm / (20pt/4 × 0.3528mm/pt) = 15 / 1.764 ≈ 8.5 staff spaces
        const double DefaultIndent = 8.5;

        double maxNameWidth = 0;

        // Font size: SvgRenderer.FontSize (4.0) × instrument name scale (0.75) = 3.0
        // Average serif character width ≈ 0.5 × font-size = 1.5 staff spaces
        const double charWidth = 1.5;

        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                if (!string.IsNullOrEmpty(staff.InstrumentName))
                {
                    double nameWidth = staff.InstrumentName.Length * charWidth;
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

    private static string ClefToString(ClefType clef) => clef switch
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
